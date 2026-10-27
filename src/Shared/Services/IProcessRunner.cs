using System;
using System.Collections.Generic;

namespace NewToys.Shared.Services;

/// <summary>外部工具执行请求。</summary>
public sealed class ProcessRunRequest
{
    /// <summary>可执行文件 / 命令名（按 PATH 解析）。</summary>
    public string FileName { get; init; } = "";

    /// <summary>原始命令行参数串（与 <see cref="ArgumentList"/> 二选一；都给了以 <see cref="ArgumentList"/> 为准）。</summary>
    public string? Arguments { get; init; }

    /// <summary>参数列表（逐项转义，⛔ 不自己拼引号）。</summary>
    public IReadOnlyList<string>? ArgumentList { get; init; }

    public string? WorkingDirectory { get; init; }

    /// <summary>额外环境变量（覆盖父进程同名变量）。</summary>
    public IReadOnlyDictionary<string, string>? Environment { get; init; }

    /// <summary>
    /// stdout 用**严格** UTF-8 解码（非法字节 ⇒ <see cref="ProcessRunResult.OutputDecodeFailed"/>）。
    /// <para>默认 true：约定外部工具输出必须是 UTF-8，乱码不如当失败报出来。winget 等宽容场景传 false。</para>
    /// </summary>
    public bool StrictOutput { get; init; } = true;
}

/// <summary>外部工具执行结果。</summary>
public sealed class ProcessRunResult
{
    /// <summary>进程是否成功启动（起不来时 <see cref="Error"/> 有人话说明）。</summary>
    public bool Started { get; init; }

    /// <summary>退出码；未正常退出（起不来 / 超时 / 取消 / 读不到）为 -1。</summary>
    public int ExitCode { get; init; } = -1;

    /// <summary>超时被强杀（进程树）。</summary>
    public bool TimedOut { get; init; }

    /// <summary>被调用方的 <see cref="CancellationToken"/> 取消（= 主动放弃，不算失败）。</summary>
    public bool Canceled { get; init; }

    /// <summary>stdout（UTF-8 解码后；解码失败时可能是残缺文本）。</summary>
    public string StdOut { get; init; } = "";

    /// <summary>stderr（宽容解码；诊断信息，只判断"非空"当 warn）。</summary>
    public string StdErr { get; init; } = "";

    /// <summary>stdout 含非 UTF-8 字节（严格模式）——脚本侧约定输出必须是 UTF-8。</summary>
    public bool OutputDecodeFailed { get; init; }

    /// <summary>启动失败 / 超时 / 退出码非零等人话说明；正常时为 null。</summary>
    public string? Error { get; init; }
}

/// <summary>
/// 「跑外部工具并拿回文本」的统一底座（平台中立）。
/// <para>
/// 语义：隐藏窗口 + 重定向 stdout/stderr + **显式 UTF-8** + 超时 / 取消**杀进程树**。
/// 先起读取任务再等待（避免输出超过管道缓冲把子进程卡死）；
/// **进程退出后输出读取只再等一个很短宽限期**（孙进程占着管道也不许无限等，见 `ProcessRunner.OutputDrainGrace`）。
/// </para>
/// <para>
/// 🔴 不在本契约内的两类（刻意保留现状，⛔ 不一刀切合并）：① **平台能力**（打开文件夹 / 文件 / URL，
/// <c>UseShellExecute=true</c>）——VM 层直接用 BCL，换平台时改的是"explorer → xdg-open"这一处；
/// ② **可见控制台 / 提权**（<c>Verb=runas</c>，窗口留着给用户看）——见 <c>QuickActionService.StartVisible</c>
/// 与 定调（winget 源命令走命令库 + UAC）。
/// </para>
/// </summary>
public interface IProcessRunner
{
    /// <summary>跑一个外部工具并等待结束；<paramref name="timeout"/> 到时杀进程树。</summary>
    Task<ProcessRunResult> RunAsync(ProcessRunRequest request, TimeSpan timeout, CancellationToken ct = default);
}
