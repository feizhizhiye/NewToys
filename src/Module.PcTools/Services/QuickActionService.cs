using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NewToys.Core.Services;
using NewToys.PcTools.Models;
using NewToys.Shared.Services;

namespace NewToys.PcTools.Services;

/// <summary>
/// <see cref="IQuickActionService"/> 实现：两个页面 = 两个配置文件。
/// <list type="bullet">
///   <item>「设置与脚本」→ <c>&lt;应用根&gt;\config\pc-scripts.json</c>（有脚本文件的脚本）；</item>
///   <item>「快捷设置」→ <c>&lt;应用根&gt;\config\pc-quick.json</c>（命令行命令）。</item>
/// </list>
/// <para>
/// <b>命令内容不写死在代码里</b>（v1.33 起的铁律）：默认清单随程序发布在
/// <c>&lt;程序目录&gt;\defaults\pc-scripts.default.json</c> 与 <c>...\pc-quick.default.json</c>，
/// 首次运行播种成上面两个用户文件；之后一切以用户那份为准（删条目不会被"复活"）。
/// </para>
/// <para>
/// 旧版（v1.33 及以前）只有一个 <c>quickactions.json</c>：首次运行本服务会把它**按 <see cref="QuickAction.Page"/>
/// 拆成两个新文件**，然后把旧文件改名成 <c>quickactions.json.migrated</c>（改名而不是删除 —— 不丢数据，
/// 且 <c>*.json</c> 的备份扫描不会再带上它）。
/// </para>
/// <para>
/// 删除等破坏性操作的二次确认通过静态钩子 <see cref="ConfirmHandler"/> 外抛 —— <c>Core</c> 不引用 Avalonia，
/// 由宿主（<c>App.axaml.cs</c>）接到模态框上，与 <see cref="PackageManagerService"/> 同款做法。
/// </para>
/// </summary>
public class QuickActionService : IQuickActionService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>普通命令 / 脚本的运行上限：超时即连子进程一起结束（防长命令把「运行中」挂死）。</summary>
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(5);

    private readonly ConfigStoreService _cfg;
    private readonly IAppConfigService? _app;

    private readonly List<QuickAction> _shortcuts = new();
    private readonly List<QuickAction> _quick = new();

    /// <summary>删除前的二次确认钩子（宿主注入；为 null 时直接删除）。</summary>
    public static Func<string, Task<bool>>? ConfirmHandler { get; set; }

    public QuickActionService(ConfigStoreService cfg, IAppConfigService app)
    {
        _cfg = cfg;
        _app = app;
        var needPersist = Load();
        EnsureScriptsDirectory();
        // 首次运行播种新文件 / 从旧单文件迁移 / 归一化归属：都落盘，让磁盘上的文件与界面完全一致
        if (needPersist) Persist();
    }

    // ==================== 读取 ====================

    public IReadOnlyList<QuickAction> Items => _shortcuts.Concat(_quick).ToList();
    public IReadOnlyList<QuickAction> Shortcuts => _shortcuts;
    public IReadOnlyList<QuickAction> QuickSettings => _quick;
    public IReadOnlyList<QuickAction> Favorites => _shortcuts.Concat(_quick).Where(i => i.IsFavorite).ToList();

    public string ScriptsFilePath => Path.Combine(_cfg.ConfigDir, "pc-scripts.json");
    public string QuickFilePath => Path.Combine(_cfg.ConfigDir, "pc-quick.json");

    /// <summary>旧版单文件（v1.33 及以前）；只用于一次性迁移。</summary>
    private string LegacyFilePath => Path.Combine(_cfg.ConfigDir, "quickactions.json");

    /// <summary>随程序发布的默认清单：设置与脚本。</summary>
    public static string ScriptsDefaultsFile
        => Path.Combine(AppContext.BaseDirectory, "defaults", "pc-scripts.default.json");

    /// <summary>随程序发布的默认清单：快捷设置。</summary>
    public static string QuickDefaultsFile
        => Path.Combine(AppContext.BaseDirectory, "defaults", "pc-quick.default.json");

    /// <summary>脚本目录（生效值）：配置为空时用 <c>&lt;应用根&gt;\scripts</c>。</summary>
    public string ScriptsDirectory
    {
        get
        {
            var custom = (_app?.ScriptFolder ?? "").Trim();
            if (custom.Length > 0) return custom;
            var def = _app?.DefaultScriptFolder ?? "";
            return def.Length > 0 ? def : Path.Combine(AppContext.BaseDirectory, "scripts");
        }
    }

    public event Action? Changed;

    // ==================== 增删改 ====================

    public QuickAction Add(string page)
    {
        var quick = page == QuickActionPage.Quick;
        var item = new QuickAction
        {
            Id = Guid.NewGuid().ToString("N"),
            Page = quick ? QuickActionPage.Quick : QuickActionPage.Shortcuts,
            // 页面默认类型：快捷设置页加的是命令行命令，设置与脚本页加的是脚本文件（可在详情里改回）。
            Kind = quick ? QuickActionKind.Command : QuickActionKind.Script,
            Title = quick ? "新的命令" : "新的脚本",
            Category = "未分类",
            Description = "",
            Command = "",
            ScriptFile = "",
            Interpreter = ScriptInterpreter.Auto,
            NeedAdmin = false,
            IsFavorite = false
        };
        (quick ? _quick : _shortcuts).Add(item);
        Persist();
        return item;
    }

    public void Save() => Persist();

    public async Task<bool> RemoveAsync(QuickAction item)
    {
        if (item == null) return false;
        if (!_shortcuts.Contains(item) && !_quick.Contains(item)) return false;

        if (ConfirmHandler != null)
        {
            var ok = await ConfirmHandler($"确定删除「{item.Title}」吗？此操作不可撤销。").ConfigureAwait(false);
            if (!ok) return false;
        }
        _shortcuts.Remove(item);
        _quick.Remove(item);
        Persist();
        return true;
    }

    public void ToggleFavorite(QuickAction item)
    {
        if (item == null) return;
        item.IsFavorite = !item.IsFavorite;
        Persist();
    }

    /// <summary>
    /// 在两页之间移动一条（改 <see cref="QuickAction.Page"/> + 挪到另一个列表 + 落盘）。
    /// 目标页是「设置与脚本」时顺带把类型标成脚本（否则它下次又会被移回快捷设置）。
    /// </summary>
    public void Move(QuickAction item, string page)
    {
        if (item == null) return;

        var toQuick = page != QuickActionPage.Shortcuts;
        _shortcuts.Remove(item);
        _quick.Remove(item);

        item.Page = toQuick ? QuickActionPage.Quick : QuickActionPage.Shortcuts;
        if (!toQuick)
        {
            item.Kind = QuickActionKind.Script;
        }
        else if (item.Kind == QuickActionKind.Script && string.IsNullOrWhiteSpace(item.ScriptFile))
        {
            item.Kind = QuickActionKind.Command;   // 没有脚本文件又放到快捷设置 → 就是一行命令
        }

        (toQuick ? _quick : _shortcuts).Add(item);
        Persist();
    }

    // ==================== 运行 ====================

    public async Task<(bool Ok, string Message)> RunAsync(QuickAction item, CancellationToken ct = default)
    {
        if (item == null) return (false, "未选择项目。");
        if (!OperatingSystem.IsWindows())
            return (false, "「运行」目前只在 Windows 上可用。");

        var (ok, cmdLine, workDir, err) = BuildRunPlan(item);
        if (!ok) return (false, err);

        // 需要管理员：只能用 ShellExecute + runas（弹 UAC），此时**无法重定向输出** ——
        // 于是让 cmd 在新控制台窗口里跑，命令的原始输出 / 报错用户直接看得到（比在状态行里转述更可靠）。
        if (item.NeedAdmin)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c " + cmdLine,
                    Verb = "runas",
                    UseShellExecute = true
                });
                return (true, $"已请求以管理员身份运行「{item.Title}」：请在 UAC 弹窗确认，结果见新打开的命令行窗口。");
            }
            catch (Exception ex)
            {
                // 1223 = 用户在 UAC 弹窗里点了「否」
                var msg = ex is System.ComponentModel.Win32Exception w && w.NativeErrorCode == 1223
                    ? "已取消（未获得管理员权限）。"
                    : "无法提权运行：" + ex.Message;
                return (false, msg);
            }
        }

        // 普通命令 / 脚本：隐藏窗口 + 捕获输出，把结果 / 报错压成一行给页面状态行。
        // ⚠️ 前置 chcp 65001：重定向成管道时 cmd 及其子程序按控制台编码写字节，不切到 UTF-8 中文会读成乱码。
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/d /c chcp 65001>nul & " + cmdLine,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workDir
        };

        using var proc = new Process { StartInfo = psi };
        try
        {
            if (!proc.Start()) return (false, "无法启动 cmd.exe。");
        }
        catch (Exception ex)
        {
            return (false, "运行失败：" + ex.Message);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RunTimeout);

        var outTask = proc.StandardOutput.ReadToEndAsync();
        var errTask = proc.StandardError.ReadToEndAsync();
        try
        {
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* 已退出 */ }
            return (false, ct.IsCancellationRequested ? "已取消。" : $"执行超过 {RunTimeout.TotalMinutes:0} 分钟，已中止。");
        }

        var stdout = await outTask.ConfigureAwait(false);
        var stderr = await errTask.ConfigureAwait(false);
        var detail = Summarize(stderr, stdout);

        if (proc.ExitCode == 0)
            return (true, detail.Length == 0 ? $"已运行：{item.Title}" : $"已运行：{item.Title} —— {detail}");
        return (false, detail.Length == 0
            ? $"运行失败（退出码 {proc.ExitCode}）：{item.Title}"
            : $"运行失败（退出码 {proc.ExitCode}）：{detail}");
    }

    /// <summary>
    /// 把一条项目翻译成"要给 cmd 执行的命令行 + 工作目录"。
    /// <list type="bullet">
    /// <item><see cref="QuickActionKind.Command"/>：命令行本身就是参数。</item>
    /// <item><see cref="QuickActionKind.Script"/>：拼出 <c>解释器 [开关] "脚本路径"</c>；工作目录取脚本所在目录（脚本里的相对路径才好使）。</item>
    /// </list>
    /// </summary>
    private (bool Ok, string CmdLine, string WorkDir, string Error) BuildRunPlan(QuickAction item)
    {
        var kind = string.IsNullOrWhiteSpace(item.Kind) ? QuickActionKind.Command : item.Kind;

        if (kind != QuickActionKind.Script)
        {
            var cmd = (item.Command ?? "").Trim();
            if (cmd.Length == 0) return (false, "", "", "这条还没有命令内容。");
            return (true, cmd, SafeWorkingDir(), "");
        }

        // ---------- 脚本文件 ----------
        var file = (item.ScriptFile ?? "").Trim();
        if (file.Length == 0) return (false, "", "", "这条还没有指定脚本文件。");

        var path = Path.IsPathRooted(file) ? file : Path.Combine(ScriptsDirectory, file);
        if (!File.Exists(path))
            return (false, "", "", $"找不到脚本文件：{path}（脚本目录可在「设置 → 路径配置」里改）");

        var ext = Path.GetExtension(path).ToLowerInvariant();
        var interp = string.IsNullOrWhiteSpace(item.Interpreter) ? AutoInterpreter(ext) : item.Interpreter.Trim();

        var exe = ResolveInterpreter(interp, out var err);
        if (exe == null) return (false, "", "", err);

        var scriptDir = Path.GetDirectoryName(path);
        var workDir = string.IsNullOrEmpty(scriptDir) ? SafeWorkingDir() : scriptDir;

        var cmdLine = interp switch
        {
            // -X utf8：python 在管道里默认按本地代码页写字节，强制 UTF-8 才不会把中文输出读成乱码
            ScriptInterpreter.Python => $"\"{exe}\" -X utf8 \"{path}\"",
            // -ExecutionPolicy Bypass：绕过执行策略，避免"脚本被禁跑"（本工具就是给用户跑自己脚本用的）
            ScriptInterpreter.Pwsh or ScriptInterpreter.PowerShell51
                => $"\"{exe}\" -NoProfile -ExecutionPolicy Bypass -File \"{path}\"",
            // call：批处理要 call 起来，否则会顶替掉外层 cmd（后面的收尾命令就不执行了）
            _ => $"call \"{path}\"",
        };
        return (true, cmdLine, workDir, "");
    }

    /// <summary>按扩展名挑默认解释器（<c>.ps1</c> 配了 pwsh 就用 pwsh，否则退回系统自带的 5.1）。</summary>
    private string AutoInterpreter(string ext) => ext switch
    {
        ".py" => ScriptInterpreter.Python,
        ".ps1" => string.IsNullOrWhiteSpace(_app?.PwshPath) ? ScriptInterpreter.PowerShell51 : ScriptInterpreter.Pwsh,
        ".bat" or ".cmd" => ScriptInterpreter.Cmd,
        _ => ""      // 交给 ResolveInterpreter 报"不认识的类型"，消息里带上扩展名更好懂
    };

    /// <summary>解释器 → 可执行文件（配置里填了就用配置，否则用命令名走 PATH）。</summary>
    private string? ResolveInterpreter(string interp, out string error)
    {
        error = "";
        string configured;
        string fallback;
        switch (interp)
        {
            case ScriptInterpreter.Python:
                configured = (_app?.PythonPath ?? "").Trim(); fallback = "python"; break;
            case ScriptInterpreter.Pwsh:
                configured = (_app?.PwshPath ?? "").Trim(); fallback = "pwsh"; break;
            case ScriptInterpreter.PowerShell51:
                configured = (_app?.PowerShell51Path ?? "").Trim(); fallback = "powershell"; break;
            case ScriptInterpreter.Cmd:
                return "cmd.exe";
            default:
                error = string.IsNullOrWhiteSpace(interp)
                    ? "认不出这个脚本类型（支持 .py / .ps1 / .bat / .cmd），可在详情里手动选解释器。"
                    : $"未知的解释器：{interp}";
                return null;
        }

        var exe = configured.Length > 0 ? configured : fallback;
        // 只有填了"看起来像路径"的值才检查存在性；纯命令名（python / pwsh）交给 PATH 去解析
        if (exe.IndexOf('\\') >= 0 || exe.IndexOf('/') >= 0)
        {
            if (!File.Exists(exe))
            {
                error = $"配置的解释器路径不存在：{exe}（可在「设置 → 路径配置」里改，留空则用系统默认）";
                return null;
            }
        }
        return exe;
    }

    /// <summary>从命令输出里挑一行「最有信息量的」给状态行（优先 stderr 的最后一行，再退到 stdout）。</summary>
    private static string Summarize(string? stderr, string? stdout)
    {
        foreach (var src in new[] { stderr, stdout })
        {
            var lines = (src ?? "")
                .Replace("\r", "")
                .Split('\n')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToList();
            if (lines.Count > 0) return Truncate(lines[^1]);
        }
        return "";
    }

    private static string Truncate(string s, int max = 160) => s.Length <= max ? s : s[..max] + "…";

    /// <summary>命令的工作目录取用户主目录（比程序目录更"像"用户自己开 cmd），取不到就用默认。</summary>
    private static string SafeWorkingDir()
    {
        try
        {
            var p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Directory.Exists(p) ? p : "";
        }
        catch { return ""; }
    }

    // ==================== 落盘 / 加载 ====================

    /// <summary>两个文件各写各的（顺序稳定 = 界面顺序稳定）。</summary>
    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(_cfg.ConfigDir);
            File.WriteAllText(ScriptsFilePath, JsonSerializer.Serialize(_shortcuts, JsonOpts));
            File.WriteAllText(QuickFilePath, JsonSerializer.Serialize(_quick, JsonOpts));
        }
        catch { /* 保存失败不打断 UI；下一次改动会再试 */ }
        Changed?.Invoke();
    }

    /// <summary>
    /// 加载顺序：
    /// ① 两个用户文件（<c>pc-scripts.json</c> / <c>pc-quick.json</c>）各自读；
    /// ② 缺哪个 → 用随程序发布的同名默认清单播种；
    /// ③ 两个都不存在、但旧的单文件 <c>quickactions.json</c> 在 → 按 <c>Page</c> 拆开迁移，旧文件改名 <c>.migrated</c>。
    /// 最后做一次**归属归一化**（没脚本文件的一律进「快捷设置」）。
    /// </summary>
    /// <returns>是否需要立即回写磁盘（有播种 / 迁移 / 归一化发生）。</returns>
    private bool Load()
    {
        var dirty = false;

        var scriptItems = TryRead(ScriptsFilePath);
        var quickItems = TryRead(QuickFilePath);

        // ---- ③ 从旧单文件迁移（仅当两个新文件都还没有时） ----
        if (scriptItems == null && quickItems == null && File.Exists(LegacyFilePath))
        {
            var legacy = TryRead(LegacyFilePath) ?? new List<QuickAction>();
            // 先按旧文件里的 Page 分栏（缺失的按分类推断），随后统一走下面的归属归一化 ——
            // 这样"设置与脚本里没有脚本文件的老条目"也会被挪到「快捷设置」，不会既跑不起来又看不见。
            scriptItems = legacy.Where(i => InferPage(i.Page, i.Category) != QuickActionPage.Quick).ToList();
            quickItems = legacy.Where(i => InferPage(i.Page, i.Category) == QuickActionPage.Quick).ToList();
            TryRetireLegacyFile();
            dirty = true;
        }

        // ---- ② 播种默认清单 ----
        if (scriptItems == null)
        {
            scriptItems = TryRead(ScriptsDefaultsFile) ?? new List<QuickAction>();
            dirty = true;
        }
        if (quickItems == null)
        {
            quickItems = TryRead(QuickDefaultsFile) ?? new List<QuickAction>();
            dirty = true;
        }

        // ---- 字段回填（老文件缺 Id / Page / Kind） ----
        if (Migrate(scriptItems, QuickActionPage.Shortcuts)) dirty = true;
        if (Migrate(quickItems, QuickActionPage.Quick)) dirty = true;

        _shortcuts.Clear();
        _quick.Clear();
        _shortcuts.AddRange(scriptItems);
        _quick.AddRange(quickItems);

        // ---- ④ 归属归一化：没有脚本文件的，不属于「设置与脚本」 ----
        var stray = _shortcuts.Where(i => !QualifiesForShortcuts(i)).ToList();
        foreach (var item in stray)
        {
            _shortcuts.Remove(item);
            item.Page = QuickActionPage.Quick;
            if (string.IsNullOrWhiteSpace(item.Kind) || item.Kind == QuickActionKind.Script)
                item.Kind = item.Command.Trim().Length > 0 ? QuickActionKind.Command : item.Kind;
            _quick.Add(item);
            dirty = true;
        }

        return dirty;
    }

    /// <summary>一条是否够格待在「设置与脚本」：必须是脚本类型**且**填了脚本文件。</summary>
    private static bool QualifiesForShortcuts(QuickAction item)
        => string.Equals(item.Kind, QuickActionKind.Script, StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(item.ScriptFile);

    /// <summary>
    /// 推断一条旧数据该属于哪一页：<c>Page</c> 有值就用它；没有（v1.32 及更早的单文件）按分类判断 ——
    /// 「打开」类是系统自带入口 → 快捷设置，其余（reg / powercfg / 网络 / 维护脚本）→ 设置与脚本。
    /// </summary>
    private static string InferPage(string? page, string? category)
    {
        if (string.Equals(page, QuickActionPage.Quick, StringComparison.OrdinalIgnoreCase)) return QuickActionPage.Quick;
        if (string.Equals(page, QuickActionPage.Shortcuts, StringComparison.OrdinalIgnoreCase)) return QuickActionPage.Shortcuts;
        return string.Equals((category ?? "").Trim(), "打开", StringComparison.Ordinal)
            ? QuickActionPage.Quick
            : QuickActionPage.Shortcuts;
    }

    private static List<QuickAction>? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var list = JsonSerializer.Deserialize<List<QuickAction>>(File.ReadAllText(path));
            return list ?? new List<QuickAction>();
        }
        catch { return null; }
    }

    /// <summary>旧单文件改名成 <c>quickactions.json.migrated</c>（不删除；改名后不会再进备份 / 加载）。</summary>
    private void TryRetireLegacyFile()
    {
        try
        {
            var target = LegacyFilePath + ".migrated";
            if (File.Exists(target)) File.Delete(target);
            File.Move(LegacyFilePath, target);
        }
        catch { /* 改不动就留着：它已经不参与加载，最多是多占几 KB */ }
    }

    /// <summary>
    /// 老文件 / 默认清单的字段回填：
    /// <c>Id</c> 缺失 → 生成；<c>Kind</c> 缺失 → 按页推断（快捷设置默认命令行命令、设置与脚本默认脚本）；
    /// <c>Page</c> 统一按它所在的文件写死（文件才是归属的真相）。
    /// </summary>
    private static bool Migrate(List<QuickAction> list, string page)
    {
        var migrated = false;
        foreach (var i in list)
        {
            if (string.IsNullOrEmpty(i.Id)) { i.Id = Guid.NewGuid().ToString("N"); migrated = true; }
            if (string.IsNullOrEmpty(i.Kind))
            {
                i.Kind = page == QuickActionPage.Quick ? QuickActionKind.Command : QuickActionKind.Script;
                migrated = true;
            }
            if (!string.Equals(i.Page, page, StringComparison.Ordinal)) { i.Page = page; migrated = true; }
        }
        return migrated;
    }

    /// <summary>
    /// 首次运行把随程序发布的示例脚本（<c>defaults\scripts\*</c>）复制到脚本目录。
    /// <para>
    /// ⚠️ 仅当脚本目录**还不存在**时才做：目录已存在说明用户已经在用了，
    /// 此时把"被删掉的示例脚本"再塞回去就成了"删不掉的幽灵文件"（v1.33 的有意取舍）。
    /// </para>
    /// </summary>
    private void EnsureScriptsDirectory()
    {
        try
        {
            var dir = ScriptsDirectory;
            if (Directory.Exists(dir)) return;

            Directory.CreateDirectory(dir);
            var src = Path.Combine(AppContext.BaseDirectory, "defaults", "scripts");
            if (!Directory.Exists(src)) return;

            foreach (var f in Directory.GetFiles(src))
            {
                try { File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), overwrite: false); }
                catch { /* 单个文件失败不影响其它 */ }
            }
        }
        catch { /* 建目录失败不阻断启动：运行时会给出"找不到脚本文件"的明确提示 */ }
    }
}
