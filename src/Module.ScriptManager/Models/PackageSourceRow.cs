
namespace NewToys.ScriptManager.Models;

/// <summary>
/// 「软件源」设置里的一行 —— 在 <see cref="PackageSourceInfo"/> 外面包一层**界面要用的派生值**
/// （v3.79：分类 / 能不能拨开关 / 关掉后怎么加回来）。
/// <para>
/// 为什么包一层：源种类（社区源 / 字体源 / 商店源 / 自定义源）是**界面口径**，判定规则要跟着
/// <see cref="PackageSources"/> 走；塞进模型里会让服务层也背上这份 UI 语义。
/// </para>
/// </summary>
public sealed class PackageSourceRow
{
    /// <param name="info">原始源信息。</param>
    /// <param name="enabled">
    /// 系统 winget 里**现在有没有**这个源 —— 开关的初始位置。
    /// 传 <c>false</c> 用于「系统里已移除、但定义还在」的兜底行（开关在关位，拨开 = source add 加回来）。
    /// </param>
    /// <param name="category">
    /// 用户给自定义源指定的分类；<c>null</c> ⇒ 内置源按名字定、其它按地址猜。
    /// </param>
    public PackageSourceRow(PackageSourceInfo info, bool enabled = true, string? category = null)
    {
        Info = info;
        Name = info.Name;
        Argument = info.Argument;
        TypeText = info.TypeText;
        ScopeText = info.ScopeText;
        _isEnabled = enabled;      // 直接灌字段：灌初值⛔ 不能触发 Toggled（否则一进页面就跑去改系统）
        WasEnabled = enabled;

        IsStore = PackageSources.IsStore(Name);
        IsFont = PackageSources.IsFont(Name);
        IsBuiltin = PackageSources.IsBuiltin(Name);
        CanToggle = PackageSources.CanToggle(Name);
        IsMirror = !IsBuiltin && Argument.Contains("mirrors.ustc.edu.cn", System.StringComparison.OrdinalIgnoreCase);

        KindLabel = IsStore ? "商店源"
                  : IsFont ? "字体源（官方）"
                  : IsMirror ? "镜像源"
                  : IsBuiltin ? "社区源（官方）"
                  : "自定义源";

        // 分类 —— 内置源由名字定，其它取用户添加时选的（没有就按地址猜）。
        Category = PackageSources.NormalizeCategory(
            PackageSources.BuiltinCategory(Name) ?? category ?? PackageSources.GuessCategory(Argument));

        ActionTip = !CanToggle
            ? "商店源 msstore 由 winget 统一管理，不支持在这里启用 / 停用。丢了就点「↺ 恢复默认源」。"
            : IsEnabled
                ? $"拨到关 = winget source remove --name {Name}（定义会记在本应用配置里，随时可以拨回来）"
                : $"拨到开 = winget source add --name {Name} --arg {Argument}（会弹 UAC）";
    }

    /// <summary>原始信息（命令要拿它去调服务）。</summary>
    public PackageSourceInfo Info { get; }

    public string Name { get; }
    public string Argument { get; }

    /// <summary>源类型（<c>source export</c> 的 Type；列表里读不到时为「—」）。</summary>
    public string TypeText { get; }

    /// <summary>「发现」列文案：显式源只有勾选它时才被查询。</summary>
    public string ScopeText { get; }

    /// <summary>种类标签（商店源 / 字体源 / 镜像源 / 社区源 / 自定义源）—— 只作展示。</summary>
    public string KindLabel { get; }

    /// <summary>分类（社区源 / 字体源 / 商店源 / 自定义源）。**同分类的源互斥**，同时最多启用一个。</summary>
    public string Category { get; }

    /// <summary>本行所在分类是互斥分类（社区源 / 字体源）—— 启用它要把同分类里其它已启用的关掉。</summary>
    public bool IsExclusive => PackageSources.IsExclusiveCategory(Category);

    public bool IsStore { get; }
    public bool IsFont { get; }
    public bool IsBuiltin { get; }
    public bool IsMirror { get; }

    /// <summary>能否拨开关（🔴 商店源不可切换，v3.79 用户定调）。</summary>
    public bool CanToggle { get; }

    /// <summary>操作提示（开关的悬浮说明，一律给 winget 命令）。</summary>
    public string ActionTip { get; }

    private bool _isEnabled;

    /// <summary>
    /// 开关**当前位置**（拨一下立刻生效 —— ⛔ 没有「生效」按钮）。
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

    /// <summary>开关被拨动（VM 挂上去做「立即生效」）。</summary>
    public event System.Action<PackageSourceRow>? Toggled;

    /// <summary>开关的**初始位置**（= 系统里的真实状态）—— 操作失败要拨回原位时用它。</summary>
    public bool WasEnabled { get; }

    /// <summary>「开关位置 ≠ 初始状态」这一行被改过（仅供排查）。</summary>
    public bool IsDirty => IsEnabled != WasEnabled;

    /// <summary>这一行**现在**这个开关位置对应哪条 winget 命令（提示用）。</summary>
    public string PendingCommand => IsEnabled
        ? $"winget source remove --name {Name}"
        : $"winget source add --name {Name} --arg {Argument}";
}
