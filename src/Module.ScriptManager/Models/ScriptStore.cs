using System.Collections.Generic;

namespace NewToys.ScriptManager.Models;

/// <summary>
/// <b>集合类别</b>（<see cref="ScriptCollection.Kind"/> 的合法取值，NT-068 §三.0）。
/// <para>决定集合归属哪一页：<c>script</c> / <c>command</c> / <c>info</c> / <c>package</c>。</para>
/// <para>⚠️ 与条目角色 <see cref="ScriptItemRole"/> 是<b>两套枚举</b>，⛔ 不要混用。</para>
/// </summary>
public static class ScriptCollectionKind
{
    public const string Script = "script";
    public const string Command = "command";
    public const string Info = "info";
    public const string Package = "package";

    /// <summary>认得的返回值，其余（含 null / 空）回退 <see cref="Info"/>。</summary>
    public static string Normalize(string? kind)
        => kind == Script || kind == Command || kind == Package ? kind : Info;

    /// <summary>集合列表 / 下拉里显示的中文名。</summary>
    public static string Label(string? kind) => Normalize(kind) switch
    {
        Script => "脚本库",
        Command => "命令库",
        Package => "软件包",
        _ => "信息面板",
    };
}

/// <summary>
/// <b>条目角色</b>（<see cref="ScriptItem.Role"/> 的合法取值，NT-068 §三.0）。
/// <para><c>collector</c> 仅用于内置播种脚本的角色标记；采集能力本身看 <see cref="ScriptItem.Capture"/>。</para>
/// </summary>
public static class ScriptItemRole
{
    public const string Script = "script";
    public const string Command = "command";
    public const string Collector = "collector";
    public const string Package = "package";

    public static string Normalize(string? role)
        => role == Command || role == Collector || role == Package ? role : Script;
}

/// <summary><b>保留策略</b>（<see cref="ScriptItem.RetentionMode"/> 的合法取值，NT-068 §三.10）。</summary>
public static class ScriptRetentionMode
{
    /// <summary>只留最新一批观测（只读条目<b>强制</b>本模式）。</summary>
    public const string Latest = "latest";

    /// <summary>保留最近 <see cref="ScriptItem.RetentionN"/> 次 run。</summary>
    public const string Count = "count";

    /// <summary>按 <c>captured_at</c> 删超过 <see cref="ScriptItem.RetentionN"/> 天的观测。</summary>
    public const string Days = "days";

    public static string Normalize(string? mode)
        => mode == Count || mode == Days ? mode : Latest;
}

/// <summary><b>刷新模式</b>（集合 / 视图共用，NT-068 §三.9）。</summary>
public static class ScriptRefreshMode
{
    public const string Manual = "manual";
    public const string OnEnter = "on-enter";
    public const string Timer = "timer";

    public static string Normalize(string? mode)
        => mode == Manual || mode == Timer ? mode : OnEnter;

    /// <summary>空串 = 视图不覆盖集合（沿用集合的设置）。</summary>
    public static string NormalizeOverride(string? mode)
        => mode == Manual || mode == OnEnter || mode == Timer ? mode : "";
}

/// <summary><b>视图布局</b>（<see cref="ScriptView.Layout"/> 的合法取值，NT-068 §三.13）。</summary>
public static class ScriptViewLayout
{
    /// <summary>按采集器分节（默认）：每节用自己的 <see cref="ScriptItem.Columns"/>。</summary>
    public const string Sectioned = "sectioned";

    /// <summary>合并表 + 分页：共用 <see cref="ScriptView.Columns"/>（要求各采集器有共同 <c>row_key</c>）。</summary>
    public const string Merged = "merged";

    public static string Normalize(string? layout) => layout == Merged ? Merged : Sectioned;
}

/// <summary><b>输出解析器</b>（<see cref="ScriptItem.ParseMode"/> 的合法取值，NT-068 §三.11）。</summary>
public static class ScriptParseMode
{
    public const string Json = "json";
    public const string Line = "line";
    public const string Csv = "csv";
    public const string Regex = "regex";

    public static string Normalize(string? mode)
        => mode == Line || mode == Csv || mode == Regex ? mode : Json;
}

/// <summary><b>可采集判定的来源</b>（<see cref="ScriptItem.CollectableSource"/>）。</summary>
public static class ScriptCollectableSource
{
    /// <summary>程序按规则自动判定。</summary>
    public const string Auto = "auto";

    /// <summary>用户手动覆盖（改过 <see cref="ScriptItem.Collectable"/>）。</summary>
    public const string User = "user";

    public static string Normalize(string? source) => source == User ? User : Auto;
}

/// <summary>
/// <c>config\script\script.json</c> 的根结构（NT-068 §三.4）。
/// <para>
/// 这里放的是<b>手工数据</b>：集合 / 条目 / 标签 / 视图 / 上次选中的集合 —— 可手改、可备份、可 diff。
/// 观测快照与运行日志是"会长大要查询"的数据，进 <c>data\script\script.db</c>（见 <c>ScriptDatabase</c>）。
/// </para>
/// </summary>
public class ScriptStoreRoot
{
    /// <summary>结构版本（当前 = 1）。加载时低于当前值的做就地补字段，不迁移文件。</summary>
    public int SchemaVersion { get; set; } = 1;

    public List<ScriptCollection> Collections { get; set; } = new();

    public List<ScriptItem> Items { get; set; } = new();

    public List<ScriptTag> Tags { get; set; } = new();

    public List<ScriptView> Views { get; set; } = new();

    /// <summary>上次在信息面板里选中的集合（重启恢复）。</summary>
    public string LastCollectionId { get; set; } = "";
}

/// <summary>
/// 一个<b>集合</b> = 一组条目的容器（需求 §6.2.5）。
/// <para>
/// 集合不持有成员 id 列表 —— 成员关系写成 <see cref="ScriptItem.Collections"/>（条目侧多对多），
/// 这样删集合不会留下悬空引用。
/// </para>
/// </summary>
public class ScriptCollection
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>取值见 <see cref="ScriptCollectionKind"/>。</summary>
    public string Kind { get; set; } = ScriptCollectionKind.Info;

    /// <summary>图标（emoji 或单个字符；模块只有 emoji 与 <c>Path</c> 两种画法）。</summary>
    public string Icon { get; set; } = "";

    public string Description { get; set; } = "";

    /// <summary>集合列表里的排序权重（小的在前）。</summary>
    public int Order { get; set; }

    /// <summary>★ 置顶（下拉里排最前）。</summary>
    public bool Pinned { get; set; }

    /// <summary>已归档（不删但默认不显示）。</summary>
    public bool Archived { get; set; }

    /// <summary>内置集合（首次启动播种；<b>可改名、⛔ 不可删</b>）。</summary>
    public bool Builtin { get; set; }

    public List<string> Tags { get; set; } = new();

    /// <summary>最近打开时间（ISO 8601；排序用，空 = 从未打开）。</summary>
    public string LastOpenedAt { get; set; } = "";

    /// <summary>取值见 <see cref="ScriptRefreshMode"/>。</summary>
    public string RefreshMode { get; set; } = ScriptRefreshMode.OnEnter;

    /// <summary>定时刷新间隔（秒）；<c>timer</c> 模式才有效。</summary>
    public int RefreshSeconds { get; set; }

    /// <summary>默认视图 id（空 = 用第一条视图）。</summary>
    public string DefaultViewId { get; set; } = "";
}

/// <summary>
/// 一条<b>条目</b>：脚本文件 / 命令行命令 / 内置采集器 / 软件包。
/// <para>
/// 采集器不是独立类型 —— 它只是 <see cref="Capture"/> 为真的条目（脚本与命令都能当采集器）。
/// </para>
/// </summary>
public class ScriptItem
{
    public string Id { get; set; } = "";

    /// <summary>取值见 <see cref="ScriptItemRole"/>。</summary>
    public string Role { get; set; } = ScriptItemRole.Script;

    public string Title { get; set; } = "";

    public string Category { get; set; } = "";

    public string Description { get; set; } = "";

    /// <summary>要跑的东西：脚本文件（相对脚本目录 / 绝对路径）或命令行命令。</summary>
    public string Target { get; set; } = "";

    /// <summary>解释器（取值见 <see cref="ScriptInterpreter"/>；空 = 按扩展名自动）。</summary>
    public string Interpreter { get; set; } = "";

    /// <summary>命令行宿主（取值见 <see cref="QuickActionShell"/>）。</summary>
    public string Shell { get; set; } = QuickActionShell.Auto;

    public bool NeedAdmin { get; set; }

    public bool RunHidden { get; set; }

    /// <summary>是否<b>捕获输出</b>（采集的前提；false = 只跑不看）。</summary>
    public bool Capture { get; set; }

    /// <summary>只读条目（采集器一律 true）⇒ 允许自动刷新、保留策略强制 <c>latest</c>。</summary>
    public bool Readonly { get; set; }

    /// <summary>可采集判定结果（派生但<b>落盘</b>，用户可覆盖）。</summary>
    public bool Collectable { get; set; }

    /// <summary>取值见 <see cref="ScriptCollectableSource"/>。</summary>
    public string CollectableSource { get; set; } = ScriptCollectableSource.Auto;

    /// <summary>不可采集的原因（界面悬浮提示用）。</summary>
    public string IneligibleReason { get; set; } = "";

    /// <summary>取值见 <see cref="ScriptRetentionMode"/>。</summary>
    public string RetentionMode { get; set; } = ScriptRetentionMode.Latest;

    /// <summary>保留次数 / 天数（<c>count</c> / <c>days</c> 模式用）。</summary>
    public int RetentionN { get; set; }

    /// <summary>取值见 <see cref="ScriptParseMode"/>。</summary>
    public string ParseMode { get; set; } = ScriptParseMode.Json;

    /// <summary>单次执行超时（秒）。</summary>
    public int Timeout { get; set; } = 30;

    /// <summary>自动刷新间隔（秒）；0 = 只手动。</summary>
    public int IntervalSeconds { get; set; }

    /// <summary>参与定时刷新（只有 <see cref="Readonly"/> 才允许为 true）。</summary>
    public bool Auto { get; set; }

    public bool Favorite { get; set; }

    /// <summary>目标文件缺失（加载时探测，不落盘为真值来源）。</summary>
    public bool Missing { get; set; }

    public int RunCount { get; set; }

    /// <summary>上次运行时间（ISO 8601；空 = 从未）。</summary>
    public string LastRunAt { get; set; } = "";

    public int LastExitCode { get; set; }

    public double AvgMs { get; set; }

    /// <summary>
    /// 列 schema（NT-068 §三.13）：<b>分节模式</b>下每采集器一份，列宽 / 列序独立。
    /// <para>列 <see cref="ScriptColumn.Key"/> = 输出 <c>attrs</c> 的属性名字符串（与 <c>pc_observations.attr</c> 同一键空间）。</para>
    /// </summary>
    public List<ScriptColumn> Columns { get; set; } = new();

    /// <summary>
    /// 成员关系（条目侧多对多）：本条目所属的集合 id。
    /// <para>集合侧不存成员列表 ⇒ 删集合时只需从各条目的本字段里剔除该 id。</para>
    /// </summary>
    public List<string> Collections { get; set; } = new();
}

/// <summary>标签（集合的横向分类，<c>script.json</c> 里统一登记）。</summary>
public class ScriptTag
{
    public string Name { get; set; } = "";

    /// <summary>颜色（空 = 用主题默认）。</summary>
    public string Color { get; set; } = "";
}

/// <summary>
/// 一个<b>视图</b>（同一集合的不同呈现：列选择 / 过滤 / 排序 / 折叠状态）。
/// <para>布局 <c>merged</c> 时列 schema 落在本类的 <see cref="Columns"/>（分节模式落在 <see cref="ScriptItem.Columns"/>）。</para>
/// </summary>
public class ScriptView
{
    public string Id { get; set; } = "";

    public string CollectionId { get; set; } = "";

    public string Name { get; set; } = "默认";

    /// <summary>取值见 <see cref="ScriptViewLayout"/>。</summary>
    public string Layout { get; set; } = ScriptViewLayout.Sectioned;

    public List<ScriptColumn> Columns { get; set; } = new();

    /// <summary>过滤表达式（空 = 不过滤；本期只存不解析）。</summary>
    public string Filter { get; set; } = "";

    /// <summary>排序表达式（空 = 默认顺序）。</summary>
    public string Sort { get; set; } = "";

    /// <summary>分组字段（空 = 不分组）。</summary>
    public string Group { get; set; } = "";

    /// <summary>已折叠的节（采集器 item id）。</summary>
    public List<string> Collapsed { get; set; } = new();

    /// <summary>刷新模式覆盖（空 = 沿用集合）。</summary>
    public string RefreshMode { get; set; } = "";

    /// <summary>刷新间隔覆盖（0 = 沿用集合）。</summary>
    public int RefreshSeconds { get; set; }
}

/// <summary>
/// 一列的定义（列 schema，NT-068 §三.13）。
/// <para>⚠️ <see cref="Key"/> 是<b>属性名字符串</b>，⛔ 不是 NT-067 的 1-based 列位。</para>
/// </summary>
public class ScriptColumn
{
    /// <summary>列 key = 输出 <c>attrs</c> 的属性名（与 <c>pc_observations.attr</c> 同一键空间）。</summary>
    public string Key { get; set; } = "";

    /// <summary>表头文案（默认 = <see cref="Key"/>；用户可改）。</summary>
    public string Title { get; set; } = "";

    public bool Visible { get; set; } = true;

    /// <summary>显示顺序（出现序；用户调过的顺序不被新 attr 打乱）。</summary>
    public int Order { get; set; }

    /// <summary>列宽（"" = 自适应；数字 = 固定 px）。</summary>
    public string Width { get; set; } = "";
}
