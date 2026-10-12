using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.PcTools.Models;

/// <summary>
/// 「设置与脚本 / 快捷设置」两页的<b>归属标识</b>（<see cref="QuickAction.Page"/> 的合法取值）。
/// </summary>
public static class QuickActionPage
{
    /// <summary>「设置与脚本」页：较复杂的设置与脚本，靠命令 / 脚本执行。</summary>
    public const string Shortcuts = "shortcuts";

    /// <summary>「快捷设置」页：系统自带设置入口（Windows 设置面板 / 管理工具），点一下即打开。</summary>
    public const string Quick = "quick";
}

/// <summary>
/// 条目**执行类型**（<see cref="QuickAction.Kind"/> 的合法取值）。
/// <para>
/// 两页的分工就落在这里：「快捷设置」放<b>命令行命令</b>（<see cref="Command"/>，一行命令直接跑）；
/// 「设置与脚本」放<b>脚本文件</b>（<see cref="Script"/>，引用脚本目录下的 .py / .ps1 / .bat）。
/// </para>
/// </summary>
public static class QuickActionKind
{
    /// <summary>命令行命令：直接执行 <see cref="QuickAction.Command"/>。</summary>
    public const string Command = "command";

    /// <summary>脚本文件：执行 <see cref="QuickAction.ScriptFile"/> 指向的文件（解释器见 <see cref="ScriptInterpreter"/>）。</summary>
    public const string Script = "script";
}

/// <summary>
/// 脚本文件用哪个解释器（<see cref="QuickAction.Interpreter"/> 的取值）。
/// <para>
/// <see cref="Auto"/>（空串）= 按扩展名自动：<c>.py</c>→python、<c>.ps1</c>→pwsh（没配就退回 5.1）、
/// <c>.bat/.cmd</c>→cmd。各解释器的**路径**在「设置 → 路径配置」里配，留空 = 用系统 PATH 里的。
/// </para>
/// </summary>
public static class ScriptInterpreter
{
    /// <summary>按扩展名自动挑（默认）。</summary>
    public const string Auto = "";

    public const string Python = "python";

    /// <summary>PowerShell 7+（pwsh）。</summary>
    public const string Pwsh = "pwsh";

    /// <summary>Windows PowerShell 5.1（powershell.exe，系统自带）。</summary>
    public const string PowerShell51 = "powershell";

    public const string Cmd = "cmd";
}

/// <summary>
/// 「快捷设置」命令行的**宿主 / 窗口行为**（<see cref="QuickAction.Shell"/> 的取值，v3.19 / NT-006）。
/// <para>
/// <see cref="Auto"/>（默认）= 当前智能行为：有输出的命令用 pwsh 弹可见终端并回显命令、GUI 启动类命令静默；
/// 显式 <see cref="Cmd"/> / <see cref="Pwsh"/> 覆盖 Auto，强制用哪个宿主跑。
/// </para>
/// </summary>
public static class QuickActionShell
{
    /// <summary>自动：有输出→pwsh 可见终端（回显命令）；GUI 启动类→cmd 静默。默认。</summary>
    public const string Auto = "Auto";

    /// <summary>强制用 cmd（<c>cmd /k</c> 可见窗口）。</summary>
    public const string Cmd = "Cmd";

    /// <summary>强制用 pwsh（可见终端 + 回显命令）。</summary>
    public const string Pwsh = "Pwsh";

    /// <summary>详情卡「Shell」下拉的选项（顺序即显示顺序）。</summary>
    public static IReadOnlyList<(string Value, string Label)> Options { get; } = new[]
    {
        (Auto, "自动（有输出用 pwsh / GUI 用 cmd）"),
        (Cmd, "cmd（/k 可见窗口）"),
        (Pwsh, "pwsh（可见终端 + 回显命令）"),
    };

    /// <summary>归一化：认得的返回值，其余（含 null / 空 / 旧数据缺省）一律回退 <see cref="Auto"/>。</summary>
    public static string Normalize(string? shell)
        => string.Equals(shell, Cmd, StringComparison.OrdinalIgnoreCase) ? Cmd
         : string.Equals(shell, Pwsh, StringComparison.OrdinalIgnoreCase) ? Pwsh
         : Auto;
}

/// <summary>
/// 「设置与脚本」/「快捷设置」里的一条项目：一条<b>系统设置项</b>（注册表开关 / 电源计划 / 控制面板入口 …）
/// 或一段常用<b>脚本</b>。
/// <para>
/// ⚠️ 这里的「设置」指 <b>操作系统设置</b>，<b>不是本软件自己的设置项</b>（那种在「设置」模块里）。
/// 两者在本模型里统一成「要跑的东西 + 元信息」，贴合本工程「个人工具 ≈ 带 GUI 的脚本管理器」的定位。
/// </para>
/// <para>
/// <b>归属</b>由 <see cref="Page"/> 决定：<see cref="QuickActionPage.Shortcuts"/> 出现在「设置与脚本」页、
/// <see cref="QuickActionPage.Quick"/> 出现在「快捷设置」页。两页**各有一份清单文件**（v1.34）：
/// 「设置与脚本」=<c>config\pc-scripts.json</c>、「快捷设置」=<c>config\pc-quick.json</c>；
/// 收藏（<see cref="IsFavorite"/>）是同一条记录上的标记 —— 单一真相源，收藏夹页据此汇总两页的收藏项。
/// </para>
/// <para>
/// <b>怎么跑</b>由 <see cref="Kind"/> 决定：命令行命令直接执行 <see cref="Command"/>；
/// 脚本文件执行脚本目录下的 <see cref="ScriptFile"/>（解释器按 <see cref="Interpreter"/> 或扩展名挑）。
/// <see cref="NeedAdmin"/> 为真时统一走 UAC 提权。
/// </para>
/// <para>
/// ⛔ 默认清单<b>不写在代码里</b>：随程序发布 <c>&lt;程序目录&gt;\defaults\pc-scripts.default.json</c> 与
/// <c>pc-quick.default.json</c>，首次运行分别复制成 <c>config\pc-scripts.json</c> / <c>config\pc-quick.json</c>，
/// 之后改本地文件即可（见 <c>QuickActionService</c>）。
/// </para>
/// </summary>
public partial class QuickAction : ObservableObject
{
    /// <summary>稳定标识（新建时生成；收藏夹按它引用，改名不受影响）。</summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// 归属页面（取值见 <see cref="QuickActionPage"/>）。
    /// <para>空串 = 旧版本文件没有这一列，加载时按分类回填（见 <c>QuickActionService.Load</c>）。</para>
    /// </summary>
    public string Page { get; set; } = "";

    /// <summary>
    /// 执行类型（取值见 <see cref="QuickActionKind"/>）。
    /// <para>空串 = 旧版本文件没有这一列 → 按「命令行命令」处理（旧条目全是命令）。</para>
    /// </summary>
    [ObservableProperty] private string _kind = QuickActionKind.Command;

    /// <summary>名称（列表第一列）。</summary>
    [ObservableProperty] private string _title = "";

    /// <summary>分类（如「系统设置」「网络」「维护」「打开」）；列表可按它排序 / 过滤。</summary>
    [ObservableProperty] private string _category = "";

    /// <summary>简介（一句话说明这条做什么）。</summary>
    [ObservableProperty] private string _description = "";

    /// <summary>命令行命令（<see cref="QuickActionKind.Command"/> 时执行它）。</summary>
    [ObservableProperty] private string _command = "";

    /// <summary>
    /// 脚本文件（<see cref="QuickActionKind.Script"/> 时执行它）：相对「脚本目录」的文件名，或绝对路径。
    /// </summary>
    [ObservableProperty] private string _scriptFile = "";

    /// <summary>脚本解释器（取值见 <see cref="ScriptInterpreter"/>）；空 = 按扩展名自动。</summary>
    [ObservableProperty] private string _interpreter = ScriptInterpreter.Auto;

    /// <summary>执行时是否需要管理员权限（为真则用 UAC 提权运行）。</summary>
    [ObservableProperty] private bool _needAdmin;

    /// <summary>是否已收藏到「本模块 → 收藏夹」。</summary>
    [ObservableProperty] private bool _isFavorite;

    /// <summary>
    /// 「隐藏终端窗口」（v3.19 / NT-006，仅对<b>命令行命令</b>有意义）：
    /// 勾上 = GUI 启动类语义，运行时不弹终端窗口（<c>cmd /c</c> + 隐藏窗口静默跑，目标 GUI 正常打开）。
    /// 默认 false（弹窗看输出）。<b>需要管理员时本开关失效</b>（仍 runas + 可见 UAC）。
    /// </summary>
    [ObservableProperty] private bool _runHidden;

    /// <summary>
    /// 命令行宿主选择（取值见 <see cref="QuickActionShell"/>，v3.19 / NT-006）：
    /// Auto / Cmd / Pwsh；旧数据缺省 = <see cref="QuickActionShell.Auto"/>（行为不变）。
    /// </summary>
    [ObservableProperty] private string _shell = QuickActionShell.Auto;

    /// <summary>列表「★」列的显示文本（收藏即 ★）。派生自 <see cref="IsFavorite"/>，不写盘。</summary>
    [JsonIgnore]
    public string FavoriteMark => IsFavorite ? "★" : "";

    /// <summary>这条到底跑什么（命令内容 / 脚本文件），界面上提示用。派生，不写盘。</summary>
    [JsonIgnore]
    public string TargetText
        => Kind == QuickActionKind.Script ? ScriptFile : Command;

    /// <summary>类型的中文名（界面提示用）。派生，不写盘。</summary>
    [JsonIgnore]
    public string KindText
        => Kind == QuickActionKind.Script ? "脚本文件" : "命令行命令";

    partial void OnIsFavoriteChanged(bool value) => OnPropertyChanged(nameof(FavoriteMark));

    partial void OnKindChanged(string value)
    {
        OnPropertyChanged(nameof(KindText));
        OnPropertyChanged(nameof(TargetText));
    }

    partial void OnScriptFileChanged(string value) => OnPropertyChanged(nameof(TargetText));

    partial void OnCommandChanged(string value) => OnPropertyChanged(nameof(TargetText));
}
