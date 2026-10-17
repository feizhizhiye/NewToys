using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

/// <summary>一次采集的结果（NT-068 S5 / §三.11）。</summary>
public sealed class CollectOutcome
{
    /// <summary>被取消（主动放弃，不算失败）。</summary>
    public bool Canceled { get; init; }

    /// <summary>失败原因（人话；成功时为 null）。</summary>
    public string? Error { get; init; }

    /// <summary>解析出来的行（失败时为 null ⇒ 保留上次数据）。</summary>
    public IReadOnlyList<ParsedInfoRow>? Rows { get; init; }

    /// <summary>非致命警告（stderr 非空 / 退出码非 0 但输出可解析）。</summary>
    public bool Warn { get; init; }

    /// <summary>本次采集状态（<see cref="SysInfoStatus"/>）。</summary>
    public string Status { get; init; } = SysInfoStatus.Ok;

    /// <summary>耗时（毫秒）。</summary>
    public long ElapsedMs { get; init; }

    /// <summary>写入的行数。</summary>
    public int RowCount { get; init; }

    /// <summary>列 schema 有新属性被追加（需要回写 <c>script.json</c>）。</summary>
    public bool ColumnsChanged { get; init; }

    /// <summary>观测库写入失败（页面不崩，只提示）。</summary>
    public string? StorageError { get; init; }

    public bool Ok => !Canceled && Error == null;
}

/// <summary>
/// <b>采集器执行</b>（NT-068 S5）：跑一个条目（脚本或命令）→ 解析输出 → 写观测库 → 生成列 schema。
/// </summary>
/// <remarks>
/// <para>
/// 采集器不是独立类型 —— 只要 <c>capture=true</c> + <c>collectable=true</c> 的条目都能当采集器，
/// 脚本与命令的唯一差别是执行方式（<see cref="ScriptRunner.RunAsync"/> vs <see cref="ScriptRunner.RunCommandAsync"/>）。
/// </para>
/// <para>⚠️ 观测库失败（打不开 / 锁住）**不影响界面出数据**：本次结果照常返回，只把错误放进 <see cref="CollectOutcome.StorageError"/>。</para>
/// </remarks>
public sealed class CollectorService
{
    /// <summary>命令型采集器的默认超时（秒）。</summary>
    public const int CommandDefaultTimeout = 15;

    /// <summary>命令型采集器的超时上限（秒）。</summary>
    public const int CommandMaxTimeout = 120;

    /// <summary>自动刷新最小间隔（秒）。</summary>
    public const int MinIntervalSeconds = 2;

    /// <summary>自动刷新最大间隔（秒）。</summary>
    public const int MaxIntervalSeconds = 3600;

    private readonly ScriptRunner _runner;
    private readonly ScriptDatabase _db;
    private readonly IScriptManagerSettingsService _settings;

    public CollectorService(ScriptRunner runner, ScriptDatabase db, IScriptManagerSettingsService settings)
    {
        _runner = runner;
        _db = db;
        _settings = settings;
    }

    /// <summary>该条目按命令行跑（<c>role=command</c> 且不是脚本文件）。</summary>
    public static bool IsCommandItem(ScriptItem item)
    {
        if (item == null || item.Role != ScriptItemRole.Command) return false;

        var target = item.Target ?? "";
        // ⚠️ ScriptExtensions 是 string[]，⛔ 没有 List.Exists（写成 .Exists(...) 会落到 Array.Exists 的两参重载）
        return !Array.Exists(CollectableEvaluator.ScriptExtensions,
            e => target.EndsWith(e, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>脚本目录（<see cref="ScriptItem.Target"/> 为相对路径时的基准）。</summary>
    public string ScriptsFolder => _settings.EffectiveScriptsFolder;

    /// <summary>跑一次采集（永不抛异常）。</summary>
    public async Task<CollectOutcome> CollectAsync(ScriptItem item, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var startedAt = DateTime.Now;

        string? runId = null;
        string? storageError = null;
        try { runId = _db.BeginRun(item.Id); }
        catch (Exception ex) { storageError = "观测库不可用：" + ex.Message; }

        ScriptRunResult res;
        if (IsCommandItem(item))
        {
            res = await _runner.RunCommandAsync(new CommandRunRequest
            {
                Command = item.Target,
                Shell = item.Shell,
                WorkingDirectory = ScriptsFolder,
                TimeoutSeconds = item.Timeout,
            }, ct).ConfigureAwait(true);
        }
        else
        {
            var path = CollectableEvaluator.ResolveScriptPath(item.Target, ScriptsFolder);
            if (path.Length == 0 || !File.Exists(path))
            {
                sw.Stop();
                FinishRun(runId, SysInfoStatus.Error, -1, sw.ElapsedMilliseconds, 0, ref storageError);
                return new CollectOutcome
                {
                    Error = "找不到脚本文件：" + item.Target,
                    Status = SysInfoStatus.Error,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    StorageError = storageError,
                };
            }

            res = await _runner.RunAsync(new ScriptRunRequest
            {
                ScriptPath = path,
                Interpreter = item.Interpreter,
                // 工作目录 = 脚本自己所在目录（脚本里的相对路径按同目录资源解析）
                WorkingDirectory = Path.GetDirectoryName(path) ?? ScriptsFolder,
                TimeoutSeconds = item.Timeout,
            }, ct).ConfigureAwait(true);
        }

        if (res.Canceled)
        {
            sw.Stop();
            FinishRun(runId, "canceled", res.ExitCode, sw.ElapsedMilliseconds, 0, ref storageError);
            return new CollectOutcome { Canceled = true, ElapsedMs = sw.ElapsedMilliseconds, StorageError = storageError };
        }

        // 整批失败：没有输出 / 输出编码非法 / 超时
        if (res.StdOut.Trim().Length == 0)
        {
            sw.Stop();
            FinishRun(runId, SysInfoStatus.Error, res.ExitCode, sw.ElapsedMilliseconds, 0, ref storageError);
            ApplyStats(item, startedAt, res.ExitCode, sw.ElapsedMilliseconds, ok: false);
            return new CollectOutcome
            {
                Error = res.Error ?? "没有任何输出",
                Status = SysInfoStatus.Error,
                ElapsedMs = sw.ElapsedMilliseconds,
                StorageError = storageError,
            };
        }

        if (!SysInfoOutputParser.TryParse(res.StdOut, item.ParseMode, out var rows, out var parseError))
        {
            sw.Stop();
            FinishRun(runId, SysInfoStatus.Error, res.ExitCode, sw.ElapsedMilliseconds, 0, ref storageError);
            ApplyStats(item, startedAt, res.ExitCode, sw.ElapsedMilliseconds, ok: false);
            return new CollectOutcome
            {
                Error = res.Error != null ? res.Error + " · " + parseError : parseError,
                Status = SysInfoStatus.Error,
                ElapsedMs = sw.ElapsedMilliseconds,
                StorageError = storageError,
            };
        }

        sw.Stop();

        // ---- 写观测库（失败不影响出数据） ----
        var rowCount = rows.Count;
        try
        {
            _db.WriteObservations(item.Id, runId ?? "", InfoRowFactory.ToObservations(rows), item);
            if (runId != null)
                _db.CompleteRun(runId, res.Ok ? SysInfoStatus.Ok : SysInfoStatus.Warn,
                    res.ExitCode, sw.ElapsedMilliseconds, rowCount);
        }
        catch (Exception ex)
        {
            storageError = "观测库写入失败：" + ex.Message;
        }

        // ---- 列 schema 增量生成（§三.13：新属性追加到末尾） ----
        var columnsChanged = InfoRowFactory.AppendNewColumns(item, rows);
        ApplyStats(item, startedAt, res.ExitCode, sw.ElapsedMilliseconds, ok: true);

        var warn = !res.Ok || res.StdErr.Trim().Length > 0;
        return new CollectOutcome
        {
            Rows = rows,
            Warn = warn,
            Status = warn ? SysInfoStatus.Warn : SysInfoStatus.Ok,
            ElapsedMs = sw.ElapsedMilliseconds,
            RowCount = rowCount,
            ColumnsChanged = columnsChanged,
            StorageError = storageError,
        };
    }

    /// <summary>
    /// 读某采集器最近一次观测（用于进页面时先出上次的缓存，再刷新）。
    /// </summary>
    /// <returns>行键顺序 = 落库顺序。</returns>
    public (List<string> Order, Dictionary<string, Dictionary<string, string>> Attrs) LoadLatest(ScriptItem item)
    {
        var order = new List<string>();
        var map = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var attrOrder = new List<string>();

        IReadOnlyList<ScriptObservation> obs;
        try { obs = _db.LatestObservations(item.Id); }
        catch { return (order, map); }

        foreach (var o in obs)
        {
            if (!map.TryGetValue(o.RowKey, out var attrs))
            {
                attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                map[o.RowKey] = attrs;
                order.Add(o.RowKey);
            }
            attrs[o.Attr] = o.Value;
            if (!attrOrder.Contains(o.Attr, StringComparer.OrdinalIgnoreCase)) attrOrder.Add(o.Attr);
        }

        // 列 schema 丢了（手改文件 / 老数据）⇒ 按观测里的属性顺序补一份内存列（⛔ 不落盘，下一次采集会补）
        if (item.Columns.Count == 0 && attrOrder.Count > 0)
            item.Columns = InfoRowFactory.ColumnsFromAttrs(attrOrder);

        return (order, map);
    }

    /// <summary>统计字段回写（RunCount / LastRunAt / LastExitCode / AvgMs）。</summary>
    private static void ApplyStats(ScriptItem item, DateTime startedAt, int exitCode, long ms, bool ok)
    {
        item.RunCount++;
        item.LastRunAt = startedAt.ToString("yyyy-MM-ddTHH:mm:ss");
        item.LastExitCode = exitCode;
        item.AvgMs = item.RunCount <= 1 ? ms : (item.AvgMs * (item.RunCount - 1) + ms) / item.RunCount;
        if (ok) item.Missing = false;
    }

    /// <summary>结束一次 run（库不可用时静默）。</summary>
    private void FinishRun(string? runId, string status, int exitCode, long ms, int rowCount, ref string? storageError)
    {
        if (runId == null) return;
        try { _db.CompleteRun(runId, status, exitCode, ms, rowCount); }
        catch (Exception ex) { storageError ??= "观测库写入失败：" + ex.Message; }
    }

    /// <summary>该条目能不能进自动刷新（§三.9 硬约束 1：只有只读条目允许）。</summary>
    public static bool CanAutoRefresh(ScriptItem item) => item.Readonly && item.Collectable;

    /// <summary>把间隔夹进 [2, 3600] 秒（0 = 只手动，原样返回）。</summary>
    public static int ClampInterval(int seconds)
        => seconds <= 0 ? 0 : Math.Clamp(seconds, MinIntervalSeconds, MaxIntervalSeconds);
}
