using System.Threading;
using System.Threading.Tasks;

namespace NewToys.Shared.Services;

/// <summary>
/// 一次 Python 执行请求。<b>C# 只做调度，不解释规则</b> —— 解析/净化全部在 Python 侧。
/// </summary>
public sealed class PyRequest
{
    /// <summary><c>code</c>（内联代码）/ <c>file</c>（执行 .py 文件）。</summary>
    public string Mode { get; set; } = "code";

    /// <summary>内联代码（<see cref="Mode"/>=<c>code</c>）。</summary>
    public string Code { get; set; } = "";

    /// <summary>脚本文件绝对路径（<see cref="Mode"/>=<c>file</c>）。</summary>
    public string Path { get; set; } = "";

    /// <summary>传给脚本的变量表（JSON 对象文本，脚本内为 <c>vars</c>）。</summary>
    public string ArgsJson { get; set; } = "{}";

    /// <summary>超时秒数；到点杀整个进程树。</summary>
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>一次 Python 执行结果。</summary>
public sealed class PyResult
{
    /// <summary>脚本是否正常结束（Python 侧异常也算 false，此时看 <see cref="Traceback"/>）。</summary>
    public bool Ok { get; set; }

    /// <summary>脚本 stdout（已重定向捕获）。</summary>
    public string Out { get; set; } = "";

    /// <summary>脚本 stderr / 异常摘要。</summary>
    public string Err { get; set; } = "";

    /// <summary>完整 traceback（含行号），脚本抛异常时非空。</summary>
    public string Traceback { get; set; } = "";

    /// <summary>运行时级错误（找不到解释器 / 超时 / 进程崩溃），不是脚本错误。</summary>
    public string Error { get; set; } = "";

    /// <summary>脚本是否成功（便捷判据）。</summary>
    public bool Succeeded => Ok && string.IsNullOrEmpty(Error);
}

/// <summary>Python 环境状态（用于「运行环境」节的自检显示）。</summary>
public sealed class PyEnvStatus
{
    /// <summary>是否可用（找到解释器即可用；venv 缺失只降级不影响跑标准库）。</summary>
    public bool Ready { get; set; }

    /// <summary>实际使用的解释器路径。</summary>
    public string PythonExe { get; set; } = "";

    /// <summary>版本串（如 <c>3.15.0</c>）。</summary>
    public string Version { get; set; } = "";

    /// <summary>是否在用共享 venv（<c>%LOCALAPPDATA%\NewToys\tools\py\venv</c>）。</summary>
    public bool UsingVenv { get; set; }

    /// <summary>给用户看的一句话（不可用时说明怎么修）。</summary>
    public string Message { get; set; } = "";
}

/// <summary>
/// Python 运行时底座：探测解释器 / 长驻 worker / 超时杀进程树 / traceback 回传。
/// <para>
/// 🔴 <b>跨模块共用</b>（网络工具的规则集、脚本管理器的 .py 命令）⇒ 接口在 Shared、实现在 Core。
/// 🔴 <b>⛔ 不引 pythonnet</b>：子进程 + 行分隔 JSON（stdin/stdout），保持平台中立（framework.md §2.1）。
/// </para>
/// </summary>
public interface IPythonRuntime
{
    /// <summary>共享工具根（<c>%LOCALAPPDATA%\NewToys\tools\py</c>）：venv、worker 脚本都落在这里。</summary>
    string ToolsRoot { get; }

    /// <summary>探测环境（同步、快；用于设置页与首次执行前的自检）。</summary>
    PyEnvStatus Probe();

    /// <summary>执行一次；内部串行（长驻 worker 一次只跑一条）。</summary>
    Task<PyResult> RunAsync(PyRequest request, CancellationToken ct = default);

    /// <summary>关闭长驻 worker（应用退出时调）。</summary>
    void Shutdown();
}
