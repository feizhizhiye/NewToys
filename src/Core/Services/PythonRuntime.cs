using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>
/// Python 运行时实现。
/// <para>
/// <b>形态</b>：长驻 worker 子进程 + stdin/stdout **行分隔 JSON**；冷启动一次，之后每任务毫秒级。
/// <br/>**环境**：优先共享 venv <c>%LOCALAPPDATA%\NewToys\tools\py\venv</c>，否则用「运行环境 → Python 解释器」或 PATH 里的 <c>python</c>/<c>python3</c>。
/// <br/>**并发**：单 worker 串行（<see cref="SemaphoreSlim"/>），跨任务不并行 —— 规则集执行本来就是串行的。
/// </para>
/// <para>
/// 🔴 三个必踩的坑（已处理，改代码前先读）：① <c>-u</c> 不加则 Python 块缓冲，实时输出全废；
/// ② Windows 管道默认 GBK ⇒ 两侧都显式 UTF-8；③ worker 里响应必须写 <c>sys.__stdout__</c>（脚本的 stdout 被重定向了）。
/// </para>
/// </summary>
public sealed class PythonRuntime : IPythonRuntime
{
    private readonly EnvConfigService _env;
    private readonly ConfigStoreService _cfg;

    /// <summary>串行闸门：一个 worker 同时只跑一条（协议是「一问一答」）。</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly object _sync = new();
    private Worker? _worker;
    private int _seq;

    /// <summary>空闲多久自动收掉 worker（避免一个 python.exe 常驻到关机）。</summary>
    private const int IdleMinutes = 10;

    /// <summary>最后一次执行结束的时刻（<see cref="Environment.TickCount64"/> 毫秒）。</summary>
    private long _lastUseTicks;

    /// <summary>空闲巡检定时器（只在 worker 存活时存在）。</summary>
    private Timer? _idle;

    public PythonRuntime(EnvConfigService env, ConfigStoreService cfg)
    {
        _env = env;
        _cfg = cfg;
        // 应用退出时收掉子进程，⛔ 不留孤儿 python.exe
        try { AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown(); } catch { }
    }

    // ==================== 路径 ====================

    /// <summary>
    /// 共享工具根：<c>%LOCALAPPDATA%\NewToys\tools\py</c>（与 <c>env.json</c> 同级）。
    /// <para>
    /// 🔴 刻意<b>不</b>用 <c>AppRoot</c>：程序可能装在只读目录（Program Files），运行期写
    /// <c>py_worker.py</c> 会直接失败；且 venv 体积大、属用户级数据，跟着 env.json 走边界更清楚。
    /// </para>
    /// </summary>
    public string ToolsRoot
    {
        get
        {
            var envDir = Path.GetDirectoryName(_env.FilePath);
            var baseDir = string.IsNullOrWhiteSpace(envDir) ? _cfg.AppRootDirectory : envDir;
            return Path.Combine(baseDir, "tools", "py");
        }
    }

    /// <summary>共享 venv 的解释器路径（Windows / 非 Windows 各一套命名）。</summary>
    public string VenvPythonPath => Path.Combine(ToolsRoot, "venv",
        OperatingSystem.IsWindows() ? Path.Combine("Scripts", "python.exe") : Path.Combine("bin", "python"));

    /// <summary>worker 脚本落盘位置（每次启动重写，保证与程序版本一致）。</summary>
    private string WorkerFile => Path.Combine(ToolsRoot, "py_worker.py");

    // ==================== 探测 ====================

    public PyEnvStatus Probe()
    {
        var venv = VenvPythonPath;
        var useVenv = File.Exists(venv);
        var exe = useVenv ? venv : ResolveBasePython();

        if (string.IsNullOrWhiteSpace(exe))
        {
            return new PyEnvStatus
            {
                Ready = false,
                Message = "未找到 Python：请在「运行环境 → Python 解释器」填路径，或安装 Python 3.10+ 后点「重新探测」。"
            };
        }

        var version = QueryVersion(exe);
        return new PyEnvStatus
        {
            Ready = true,
            PythonExe = exe,
            Version = version,
            UsingVenv = useVenv,
            Message = (useVenv ? "共享环境：" : "系统解释器：") + exe + (version.Length > 0 ? "（" + version + "）" : "")
        };
    }

    /// <summary>解释器解析顺序：env.json 显式路径 → PATH 里的 <c>python</c> → <c>python3</c>（平台中立）。</summary>
    private string? ResolveBasePython()
    {
        var configured = _env.PythonPath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        return WhichOnPath("python") ?? WhichOnPath("python3");
    }

    /// <summary>在 PATH 里找可执行文件（Windows 补 <c>.exe</c>；⛔ 不做平台判断以外的假设）。</summary>
    private static string? WhichOnPath(string name)
    {
        var exts = OperatingSystem.IsWindows() ? new[] { ".exe", ".bat", ".cmd", "" } : new[] { "" };
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in exts)
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), name + ext);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { /* 非法 PATH 段直接跳过 */ }
            }
        }
        return null;
    }

    /// <summary>跑一次 <c>-V</c> 取版本（短超时、同步；只用于探测）。</summary>
    private static string QueryVersion(string exe)
    {
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false)
            };
            psi.ArgumentList.Add("-V");
            using var p = Process.Start(psi);
            if (p is null) return "";
            var text = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            if (!p.WaitForExit(3000)) { try { p.Kill(entireProcessTree: true); } catch { } return ""; }
            // "Python 3.15.0" → "3.15.0"
            var t = text.Trim();
            var idx = t.IndexOf(' ');
            return idx > 0 ? t[(idx + 1)..].Trim() : t;
        }
        catch { return ""; }
    }

    // ==================== 执行 ====================

    public async Task<PyResult> RunAsync(PyRequest request, CancellationToken ct = default)
    {
        if (request is null) return Fail("请求为空。");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var status = Probe();
            if (!status.Ready) return Fail(status.Message);

            var id = "r" + Interlocked.Increment(ref _seq);
            var payload = BuildRequestJson(id, request);

            Worker worker;
            try { worker = EnsureWorker(status.PythonExe); }
            catch (Exception ex) { return Fail("启动 Python 失败：" + ex.Message); }

            try
            {
                await worker.In.WriteAsync(payload + "\n").ConfigureAwait(false);
                await worker.In.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DiscardWorker(worker);
                return Fail("写入 worker 失败：" + ex.Message);
            }

            var timeout = Math.Clamp(request.TimeoutSeconds <= 0 ? 30 : request.TimeoutSeconds, 1, 600);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeout));

            string? line;
            try
            {
                line = await worker.Out.ReadLineAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 卡在 requests.get 之类里 ⇒ 只能杀进程树（worker 自己没机会处理超时）
                DiscardWorker(worker);
                return Fail($"执行超时（{timeout}s），已终止脚本。");
            }

            if (line is null)
            {
                DiscardWorker(worker);
                return Fail("Python 进程已退出（脚本可能调用了 sys.exit，或解释器崩溃）。");
            }

            return ParseResponse(line);
        }
        finally
        {
            Interlocked.Exchange(ref _lastUseTicks, Environment.TickCount64);
            _gate.Release();
        }
    }

    /// <summary>拼一行请求 JSON（<c>vars</c> 直接透传为 JSON 对象）。</summary>
    private static string BuildRequestJson(string id, PyRequest req)
    {
        var dict = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["mode"] = string.IsNullOrWhiteSpace(req.Mode) ? "code" : req.Mode,
            ["code"] = req.Code ?? "",
            ["path"] = req.Path ?? "",
            ["timeout"] = req.TimeoutSeconds
        };
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(req.ArgsJson) ? "{}" : req.ArgsJson);
            dict["vars"] = doc.RootElement.Clone();
        }
        catch { dict["vars"] = new Dictionary<string, object?>(); }
        return JsonSerializer.Serialize(dict);
    }

    /// <summary>解析 worker 的一行响应；坏 JSON 也不抛（退化成运行时错误）。</summary>
    private static PyResult ParseResponse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            return new PyResult
            {
                Ok = root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True,
                Out = Str(root, "out"),
                Err = Str(root, "err"),
                Traceback = Str(root, "traceback")
            };
        }
        catch (Exception ex)
        {
            return Fail("worker 响应无法解析：" + ex.Message + "\n" + line);
        }
    }

    private static string Str(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static PyResult Fail(string message) => new() { Ok = false, Error = message };

    // ==================== worker 生命周期 ====================

    private Worker EnsureWorker(string exe)
    {
        lock (_sync)
        {
            if (_worker is { } alive && !alive.Proc.HasExited) return alive;
            _worker?.Dispose();
            _worker = null;

            Directory.CreateDirectory(ToolsRoot);
            File.WriteAllText(WorkerFile, WorkerSource, new UTF8Encoding(false));

            var psi = new ProcessStartInfo(exe)
            {
                WorkingDirectory = ToolsRoot,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false)
            };
            // -u：无缓冲（否则实时输出 / 采集器全废）；-X utf8：管道强制 UTF-8（Windows 默认 GBK）
            psi.ArgumentList.Add("-u");
            psi.ArgumentList.Add("-X");
            psi.ArgumentList.Add("utf8");
            psi.ArgumentList.Add(WorkerFile);
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            psi.Environment["PYTHONUTF8"] = "1";
            psi.Environment["PYTHONDONTWRITEBYTECODE"] = "1";   // ⛔ 别往 rules 目录塞 __pycache__

            var proc = new Process { StartInfo = psi };
            if (!proc.Start()) throw new InvalidOperationException("Process.Start 返回 false");

            // stderr 必须有人读：否则管道写满会让子进程阻塞
            proc.ErrorDataReceived += (_, _) => { };
            proc.BeginErrorReadLine();

            var worker = new Worker(proc);
            worker.In.AutoFlush = true;
            _worker = worker;
            Interlocked.Exchange(ref _lastUseTicks, Environment.TickCount64);
            // 空闲巡检：每 60s 看一眼，超过 IdleMinutes 没干活就收掉（下次用来自动重启）
            _idle ??= new Timer(_ => IdleSweep(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            return worker;
        }
    }

    /// <summary>空闲巡检（定时器线程）：⛔ 不许抛，⛔ 不许打断正在跑的请求。</summary>
    private void IdleSweep()
    {
        try
        {
            if (Environment.TickCount64 - Interlocked.Read(ref _lastUseTicks) < IdleMinutes * 60_000L) return;
            if (!_gate.Wait(0)) return;   // 正在执行：下次再说
            try { Shutdown(); } finally { _gate.Release(); }
        }
        catch { /* 巡检失败不影响下次启动 */ }
    }

    private void DiscardWorker(Worker worker)
    {
        lock (_sync) { if (ReferenceEquals(_worker, worker)) _worker = null; }
        try { worker.Dispose(); } catch { }
    }

    public void Shutdown()
    {
        Worker? worker;
        Timer? timer;
        lock (_sync) { worker = _worker; _worker = null; timer = _idle; _idle = null; }
        try { timer?.Dispose(); } catch { }
        if (worker is null) return;
        try { worker.Dispose(); } catch { }
    }

    /// <summary>长驻 worker 句柄（关 stdin ⇒ Python 侧 for 循环自然结束）。</summary>
    private sealed class Worker : IDisposable
    {
        private readonly Process _proc;

        public Worker(Process proc)
        {
            _proc = proc;
            In = proc.StandardInput;
            Out = proc.StandardOutput;
        }

        public StreamWriter In { get; }
        public StreamReader Out { get; }
        public Process Proc => _proc;

        public void Dispose()
        {
            try { In.Close(); } catch { }
            try
            {
                if (!_proc.HasExited && !_proc.WaitForExit(500)) _proc.Kill(entireProcessTree: true);
            }
            catch { }
            try { _proc.Dispose(); } catch { }
        }
    }

    // ==================== 随包 worker 脚本 ====================
    //
    // 🔴 响应必须写 sys.__stdout__：脚本的 stdout 被 redirect_stdout 接走了。
    // 🔴 入参出参都是「一行 JSON」，⛔ 不要往 stdout 打日志。
    private const string WorkerSource = """
import sys, json, io, contextlib, traceback

try:
    sys.stdin.reconfigure(encoding="utf-8")
    sys.__stdout__.reconfigure(encoding="utf-8")
    sys.__stderr__.reconfigure(encoding="utf-8")
except Exception:
    pass

def _run(req):
    if req.get("mode") == "file":
        fname = req.get("path") or "<rule>"
        with open(fname, "r", encoding="utf-8") as f:
            code = f.read()
    else:
        fname = "<rule>"
        code = req.get("code") or ""
    env = {"__name__": "__rule__", "__file__": fname, "vars": req.get("vars") or {}}
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf):
        exec(compile(code, fname, "exec"), env)
    return buf.getvalue()

def main():
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        rid = ""
        try:
            req = json.loads(line)
            rid = req.get("id") or ""
            resp = {"id": rid, "ok": True, "out": _run(req), "err": "", "traceback": ""}
        except Exception as e:
            resp = {"id": rid, "ok": False, "out": "",
                    "err": str(e) or type(e).__name__,
                    "traceback": traceback.format_exc()}
        try:
            sys.__stdout__.write(json.dumps(resp, ensure_ascii=False) + "\n")
            sys.__stdout__.flush()
        except Exception:
            break

main()
""";
}
