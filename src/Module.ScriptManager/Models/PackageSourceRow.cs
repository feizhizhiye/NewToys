
namespace NewToys.ScriptManager.Models;

/// <summary>
/// 「软件源」设置页里的一行 —— 在 <see cref="PackageSourceInfo"/> 外面包一层**界面要用的派生值**
/// （v3.78：种类标签 / 能不能用 enable-disable 开关）。
/// <para>
/// 为什么包一层：源种类（官方 / 商店 / 镜像 / 自定义）是**界面口径**，判定规则要跟着
/// <see cref="PackageSources"/> 走；塞进 Shared 的模型里会让 Core 也背上这份 UI 语义。
/// </para>
/// </summary>
public sealed class PackageSourceRow
{
    /// <param name="info">原始源信息。</param>
    /// <param name="enabled">
    /// 系统 winget 里**现在有没有**这个源 —— 开关的初始位置（NT-074）。
    /// 传 <c>false</c> 用于「内置源兜底行」（winget / msstore 在系统里没启用时补的一行）。
    /// </param>
    /// <param name="category">
    /// 分类（<see cref="PackageSources.CategoryCommunity"/> / <see cref="PackageSources.CategoryStore"/> /
    /// <see cref="PackageSources.CategoryCustom"/>）；<c>null</c> ⇒ 内置源按名字定、自定义源按地址猜。
    /// </param>
    public PackageSourceRow(PackageSourceInfo info, bool enabled = true, string? category = null)
    {
        Info = info;
        Name = info.Name;
        Argument = info.Argument;
        TypeText = info.TypeText;
        _isEnabled = enabled;      // 直接灌字段：灌初值⛔ 不能触发 Toggled（否则一进页面就跑去改系统）
        WasEnabled = enabled;

        IsStore = PackageSources.IsStore(Name);
        IsBuiltin = PackageSources.IsBuiltin(Name);
        IsMirror = !IsBuiltin && Argument.Contains("mirrors.ustc.edu.cn", System.StringComparison.OrdinalIgnoreCase);

        KindLabel = IsStore ? "商店源" : IsMirror ? "镜像源" : IsBuiltin ? "社区源（官方）" : "自定义源";

        // NT-076：分类 —— 内置源由名字定，自定义源取设置里存的（没有就按地址猜）。
        Category = PackageSources.NormalizeCategory(
            PackageSources.BuiltinCategory(Name) ?? category ?? PackageSources.GuessCategory(Argument));

        // 内置源（winget / msstore）用 source enable|disable，⛔ 不删定义；自定义源只能 remove（删除）。
        ActionTip = IsBuiltin
            ? "winget source enable|disable --name " + Name + "（源定义保留，随时可再启用）"
            : "winget source remove --name " + Name + "（自定义源没有 enable/disable，只能真的删掉）";
    }

    /// <summary>原始信息（命令要拿它去调服务）。</summary>
    public PackageSourceInfo Info { get; }

    public string Name { get; }
    public string Argument { get; }

    /// <summary><c>winget source list</c> 里的类型列（不同版本列头不同，可能为空）。</summary>
    public string TypeText { get; }

    /// <summary>种类标签（商店源 / 镜像源 / 社区源（官方）/ 自定义源）—— 分类下的**细分**，只作展示。</summary>
    public string KindLabel { get; }

    /// <summary>分类（社区源 / 商店源 / 自定义源；NT-076）。同分类的源**互斥**，同时最多启用一个。</summary>
    public string Category { get; }

    /// <summary>本行所在分类是互斥分类（社区源 / 商店源）—— 启用它要把同分类里其它已启用的关掉。</summary>
    public bool IsExclusive => PackageSources.IsExclusiveCategory(Category);

    public bool IsStore { get; }
    public bool IsBuiltin { get; }
    public bool IsMirror { get; }

    /// <summary>操作提示（开关的悬浮说明，NT-074：一律给 winget 命令）。</summary>
    public string ActionTip { get; }

    private bool _isEnabled;

    /// <summary>
    /// 开关**当前位置**（NT-076：源列表右侧的开关就绑它，**拨一下立刻生效** —— ⛔ 没有「生效」按钮）。
    /// <para>setter 只在位置**真的变了**时才发 <see cref="Toggled"/>（构造 / 重建灌初值⛔ 不触发）。</para>
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            Toggled?.Invoke(this);
        }
    }

    /// <summary>开关被拨动（VM 挂上去做「立即生效」；NT-076）。</summary>
    public event System.Action<PackageSourceRow>? Toggled;

    /// <summary>开关的**初始位置**（= 系统里的真实状态）—— 操作失败要拨回原位时用它。</summary>
    public bool WasEnabled { get; }

    /// <summary>「开关位置 ≠ 初始状态」这一行被改过（NT-076 之后仅供排查，界面不再靠它标脏）。</summary>
    public bool IsDirty => IsEnabled != WasEnabled;

    /// <summary>这一行**现在**这个开关位置对应哪条 winget 命令（提示用）。</summary>
    public string PendingCommand => IsEnabled
        ? $"winget source enable --name {Name}"
        : IsBuiltin ? $"winget source disable --name {Name}" : $"winget source remove --name {Name}";
}
