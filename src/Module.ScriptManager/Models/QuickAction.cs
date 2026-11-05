using System.IO;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using NewToys.Core.Services;   // ScriptInterpreter / QuickActionShell 常量已上提 Core

namespace NewToys.ScriptManager.Models;

/// <summary>
/// 「脚本库 / 命令库」两页的<b>归属标识</b>（<see cref="QuickAction.Page"/> 的合法取值）。
/// </summary>
public static class QuickActionPage
{
    /// <summary>「脚本库」页：较复杂的脚本库，靠命令 / 脚本执行。</summary>
    public const string Shortcuts = "shortcuts";

    /// <summary>「命令库」页：系统自带设置入口（Windows 设置面板 / 管理工具），点一下即打开。</summary>
    public const string Quick = "quick";
}

/// <summary>条目执行类型（QuickAction.Kind 的合法取值）。</summary>
public static class QuickActionKind
{
    /// <summary>命令行命令：直接执行 <see cref="QuickAction.Command"/>。</summary>
    public const string Command = "command";

    /// <summary>脚本文件：执行 <see cref="QuickAction.ScriptFile"/> 指向的文件（解释器见 <see cref="ScriptInterpreter"/>）。</summary>
    public const string Script = "script";
}

/// <summary>「脚本库」/「命令库」里的一条项目：一条系统设置项（注册表开关 / 电源计划 / 控制面板入口 …） 或一段常用脚本。（WHY: SM-011）</summary>
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
    /// 「隐藏终端窗口」（v3.19，仅对<b>命令行命令</b>有意义）：
    /// 勾上 = GUI 启动类语义，运行时不弹终端窗口（<c>cmd /c</c> + 隐藏窗口静默跑，目标 GUI 正常打开）。
    /// 默认 false（弹窗看输出）。<b>需要管理员时本开关失效</b>（仍 runas + 可见 UAC）。
    /// </summary>
    [ObservableProperty] private bool _runHidden;

    /// <summary>
    /// 命令行宿主选择（取值见 <see cref="QuickActionShell"/>，v3.19）：
    /// Auto / Cmd / Pwsh；旧数据缺省 = <see cref="QuickActionShell.Auto"/>（行为不变）。
    /// </summary>
    [ObservableProperty] private string _shell = QuickActionShell.Auto;

    /// <summary>列表「」列的显示文本（收藏即 ★）。派生自 <see cref="IsFavorite"/>，不写盘。</summary>
    [JsonIgnore]
    public string FavoriteMark => IsFavorite ? "★" : "";

    /// <summary>
    /// <b>可采集</b>（§三.5）：由 <c>CollectableEvaluator</c> 判定后填进来，供库页「可采集」列显示。
    /// <para>⚠️ <b>派生值</b>，⛔ 不从本类回写数据层（真相源是 <c>ScriptItem.Collectable</c>）。</para>
    /// </summary>
    [ObservableProperty] private bool _collectable = true;

    /// <summary>不可采集的原因（列悬浮提示用）；可采集时为空。</summary>
    [ObservableProperty] private string _ineligibleReason = "";

    /// <summary>
    /// 所属库的集合 id（派生自 <see cref="ScriptItem.Collections"/>，⛔ 不写盘）。
    /// <para>库页按它过滤 / 显示「所属库」。</para>
    /// </summary>
    [JsonIgnore]
    public List<string> CollectionIds { get; set; } = new();

    /// <summary>所属库名称（派生，⛔ 不写盘；「全部库」视图下拼在标题后面）。</summary>
    [JsonIgnore]
    public string LibraryText { get; set; } = "";

    /// <summary>所属库的脚本根目录（派生；空 = 用全局脚本目录）。</summary>
    [JsonIgnore]
    public string LibraryRoot { get; set; } = "";

    /// <summary>列表「名称」列实际显示的文本：有库名后缀时 = <c>名称 · 库名</c>。</summary>
    [JsonIgnore]
    public string DisplayTitle => string.IsNullOrEmpty(LibraryText) ? Title : $"{Title}  ·  {LibraryText}";

    /// <summary>列表「可采集」列的显示文本：✔ / ✖。派生，不写盘。</summary>
    [JsonIgnore]
    public string CollectableMark => Collectable ? "✔" : "✖";

    /// <summary>这条到底跑什么（命令内容 / 脚本文件），界面上提示用。派生，不写盘。</summary>
    [JsonIgnore]
    public string TargetText
        => Kind == QuickActionKind.Script ? ScriptFile : Command;

    /// <summary>类型的中文名（界面提示用）。派生，不写盘。</summary>
    [JsonIgnore]
    public string KindText
        => Kind == QuickActionKind.Script ? "脚本文件" : "命令行命令";

    /// <summary>
    /// 「分类」列（v3.63）：**脚本 = 脚本后缀**（.py / .ps1 / .bat / .cmd）；**命令行命令 = Shell 宿主**（cmd / pwsh / 自动）。
    /// <para>派生、**不写盘**：两页各自的分类维度不同（脚本按文件类型、命令按执行宿主），一个属性同时覆盖。</para>
    /// </summary>
    [JsonIgnore]
    public string Classify
    {
        get
        {
            if (Kind == QuickActionKind.Script)
            {
                var ext = Path.GetExtension(ScriptFile ?? "").ToLowerInvariant();
                return ext.Length > 0 ? ext : "—";
            }

            return QuickActionShell.Normalize(Shell) switch
            {
                QuickActionShell.Cmd => "cmd",
                QuickActionShell.Pwsh => "pwsh",
                _ => "自动"
            };
        }
    }

    partial void OnIsFavoriteChanged(bool value) => OnPropertyChanged(nameof(FavoriteMark));

    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(DisplayTitle));

    partial void OnCollectableChanged(bool value) => OnPropertyChanged(nameof(CollectableMark));

    partial void OnKindChanged(string value)
    {
        OnPropertyChanged(nameof(KindText));
        OnPropertyChanged(nameof(Classify));
        OnPropertyChanged(nameof(TargetText));
    }

    partial void OnScriptFileChanged(string value)
    {
        OnPropertyChanged(nameof(Classify));
        OnPropertyChanged(nameof(TargetText));
    }

    partial void OnShellChanged(string value) => OnPropertyChanged(nameof(Classify));

    partial void OnCommandChanged(string value) => OnPropertyChanged(nameof(TargetText));
}
