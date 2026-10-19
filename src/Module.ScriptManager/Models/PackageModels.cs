using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.ScriptManager.Models;

/// <summary>
/// 一条软件包记录 —— 「搜索结果 / 已安装 / 可升级」三种列表**共用同一个模型**。
/// <para>
/// 为什么共用：列表页的表头排序、右键菜单、右侧详情三处都只认它，各写一份必然漂移
/// （见规范 3.8「列表页布局规范」）。
/// </para>
/// <para>
/// v3.19 / NT-008：改成 <see cref="ObservableObject"/> —— 详情卡绑 <c>Selected.Publisher</c> 等，
/// 「🔎 读取详情」回填字段后**必须通知 UI 才会补充显示**（以前是普通类，改了不刷新 = 详情不补充）。
/// </para>
/// </summary>
public partial class PackageInfo : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _id = "";

    /// <summary>已安装版本；在「搜索结果」里是**最新可用版本**（winget search 的语义）。</summary>
    [ObservableProperty] private string _version = "";

    /// <summary>可升级到的版本（`winget list` / `winget upgrade` 的 Available 列）。</summary>
    [ObservableProperty] private string _available = "";

    /// <summary>来源（winget / msstore …）。</summary>
    [ObservableProperty] private string _source = "";

    [ObservableProperty] private bool _isInstalled;

    /// <summary>
    /// v3.37 / NT-002：**勾选列**状态（「⬇ 安装选中」「⬆ 升级选中」按勾选批量执行）。
    /// ⚠️ 只是界面态，⛔ 不落库、不参与筛选 / 排序 / 导入导出。
    /// </summary>
    [ObservableProperty] private bool _isChecked;

    /// <summary>是否被 pin（pin = 暂停更新，winget upgrade 会跳过它）。</summary>
    [ObservableProperty] private bool _isPinned;

    /// <summary>被 pin 住的版本（来自 <c>winget pin list</c>，可能为空）。</summary>
    [ObservableProperty] private string _pinnedVersion = "";

    // ---------- 以下字段按需补：只有点「读取详情」跑一次 winget show 才填 ----------
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _publisher = "";
    [ObservableProperty] private string _homepage = "";
    [ObservableProperty] private string _license = "";
    [ObservableProperty] private string _installLocation = "";

    /// <summary>有可用更新（Available 存在且与当前版本不同）。</summary>
    public bool HasUpgrade => !string.IsNullOrEmpty(Available)
                              && !string.Equals(Available, Version, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 可以真的点「⬆ 升级」= **已安装**且有可用更新（未安装的包没有升级语义）。
    /// <para>详情卡的升级按钮绑这个：已安装但已是最新 ⇒ 置灰，并给「已是最新版」的理由。</para>
    /// </summary>
    public bool CanUpgrade => IsInstalled && HasUpgrade;

    /// <summary>列表「状态」列的文字。</summary>
    public string StatusText =>
        IsPinned ? "已 Pin（暂停更新）"
        : !IsInstalled ? "未安装"
        : HasUpgrade ? "可升级"
        : "已安装";

    /// <summary>列表「Pin」列的显示文本（被 pin 住 = 📌）。派生，不写盘。</summary>
    public string PinnedMark => IsPinned ? "📌" : "";

    // ---- 派生属性随依赖变化刷新（详情卡 / 状态列 / Pin 列都绑这些）----

    partial void OnIsPinnedChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(PinnedMark));
    }

    partial void OnIsInstalledChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanUpgrade));
    }

    partial void OnVersionChanged(string value)
    {
        OnPropertyChanged(nameof(HasUpgrade));
        OnPropertyChanged(nameof(CanUpgrade));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnAvailableChanged(string value)
    {
        OnPropertyChanged(nameof(HasUpgrade));
        OnPropertyChanged(nameof(CanUpgrade));
        OnPropertyChanged(nameof(StatusText));
    }
}

/// <summary>包管理器的一个源（`winget source list` 的一行）。</summary>
public class PackageSourceInfo
{
    /// <summary>源名，也是 <c>--source</c> 的取值（winget / msstore …）。</summary>
    public string Name { get; set; } = "";

    /// <summary>源的 URL / UNC 路径。</summary>
    public string Argument { get; set; } = "";

    /// <summary>类型或 Explicit 标记（winget 不同版本列头不同，两者都收）。</summary>
    public string Type { get; set; } = "";

    public string TypeText => Type switch
    {
        "true" or "True" => "自定义（explicit）",
        "false" or "False" => "内置",
        "" => "—",
        _ => Type
    };
}

/// <summary>
/// 「源」下拉的一项。
/// <para>
/// 放在 <c>Shared</c> 是因为**两个模块都要用**：PC 工具箱（软件包管理页的源下拉）与
/// 设置模块（软件源的默认源下拉）。模块之间禁止互相引用，所以共用类型一律进 Shared。
/// </para>
/// <para><see cref="Value"/> 为 <c>null</c> 表示「全部已启用的源」。</para>
/// </summary>
public partial class PackageSourceOption : ObservableObject
{
    public string Label { get; set; } = "";
    public string? Value { get; set; }

    /// <summary>v3.19 / NT-008：是否当前选中的源（源选择框改按钮后，按钮靠它高亮）。</summary>
    [ObservableProperty] private bool _isSelected;

    public override string ToString() => Label;
}

/// <summary>
/// 「软件包管理」模块的**应用级设置**，落 <c>&lt;应用根&gt;\config\package.json</c>。
/// <para>
/// ⚠️ 这是"设置里的数据"：它由设置模块的「软件源」页维护，但只有 <c>config\</c> 下的文件
/// 才能被「数据管理」页备份 / 导入 —— 所以**不要**把源设置散落到模块自己的目录里
/// （见规范 3.9「设置数据可备份，模块只读设置」）。
/// </para>
/// <para>
/// ⚠️ v1.33 起<b>源本身不在这里</b>：源的增删直接作用于**系统 winget**（一套数据，见「设置 → 软件源」页），
/// 本文件只剩"界面偏好"（默认源 / 静默安装 / 排序列）。
/// </para>
/// </summary>
public class PackageSettings
{
    /// <summary>默认查询源（对应 <c>--source</c> 的取值）；空串表示「全部已启用的源」。</summary>
    public string DefaultSource { get; set; } = "";

    /// <summary>安装 / 升级时追加 <c>--silent</c>（不弹安装器交互）。</summary>
    public bool SilentInstall { get; set; } = true;

    /// <summary>上次的排序列（列表页打开时恢复）。</summary>
    public string SortColumn { get; set; } = "Name";

    /// <summary>上次的排序方向。</summary>
    public bool SortDescending { get; set; }

    /// <summary>
    /// **自定义源的分类**（源名 → 分类；NT-076）。
    /// <para>内置源（<c>winget</c> / <c>msstore</c>）的分类由名字直接定，⛔ 不存这里（存了会与真相源打架）。</para>
    /// <para>分类决定「和谁互斥」：社区源 / 商店源里**同时最多启用一个**；自定义源分类不互斥。</para>
    /// </summary>
    public System.Collections.Generic.Dictionary<string, string> SourceCategories { get; set; } = new();
}

/// <summary>
/// winget **内置源**的常量（v3.78）—— Core（拼命令）与模块（界面文案）共用一份，⛔ 别各写一遍。
/// <para>
/// 「商店源」= <c>msstore</c>（Microsoft Store）：包 Id 是 Store 的 12 位字符（如 <c>9NBLGGH4NNS1</c>），
/// 与 winget 社区源的 <c>发布者.应用名</c> 长得不一样 —— 判定是不是商店包一律走 <see cref="IsStore"/>。
/// </para>
/// </summary>
public static class PackageSources
{
    /// <summary>社区源名（<c>winget</c>）。</summary>
    public const string MainName = "winget";

    /// <summary>社区源官方地址（切回官方 / 添加时用）。</summary>
    public const string MainUrl = "https://cdn.winget.microsoft.com/cache";

    /// <summary>商店源名（<c>msstore</c>）。</summary>
    public const string StoreName = "msstore";

    /// <summary>商店源地址（老 winget 没有 <c>source enable</c>，要靠 add 加回来时用这个）。</summary>
    public const string StoreUrl = "https://storeedgefd.dsx.mp.microsoft.com/v9.0";

    /// <summary>商店源类型（<c>--type</c>）。</summary>
    public const string StoreType = "Microsoft.Rest";

    /// <summary>中科大镜像源地址（国内通常更快；社区源的镜像，⛔ 商店源没有镜像）。</summary>
    public const string UstcUrl = "https://mirrors.ustc.edu.cn/winget-source";

    /// <summary>是不是商店源（忽略大小写 / 首尾空格）。</summary>
    public static bool IsStore(string? name)
        => string.Equals((name ?? "").Trim(), StoreName, StringComparison.OrdinalIgnoreCase);

    /// <summary>是不是 winget 内置源（<c>winget</c> / <c>msstore</c>）—— 内置的用 enable/disable，自定义的只能 remove。</summary>
    public static bool IsBuiltin(string? name)
        => IsStore(name) || string.Equals((name ?? "").Trim(), MainName, StringComparison.OrdinalIgnoreCase);

    // ==================== 分类（NT-076：源列表多一列「分类」，同分类互斥） ====================

    /// <summary>社区源分类（默认项 = <c>winget</c>；镜像源也归这一类，因此与官方社区源<b>互斥</b>）。</summary>
    public const string CategoryCommunity = "社区源";

    /// <summary>商店源分类（默认项 = <c>msstore</c>）。</summary>
    public const string CategoryStore = "商店源";

    /// <summary>自定义源分类（⛔ 不互斥 —— 这类源可以同时开好几个）。</summary>
    public const string CategoryCustom = "自定义源";

    /// <summary>分类下拉的可选项（顺序即界面顺序）。</summary>
    public static readonly string[] AllCategories = { CategoryCommunity, CategoryStore, CategoryCustom };

    /// <summary>内置源的分类；不是内置源 ⇒ <c>null</c>（分类由用户在「添加源」时指定）。</summary>
    public static string? BuiltinCategory(string? name)
        => IsStore(name) ? CategoryStore
         : string.Equals((name ?? "").Trim(), MainName, StringComparison.OrdinalIgnoreCase) ? CategoryCommunity
         : null;

    /// <summary>按地址**猜**分类（「添加源」时的默认值，用户可改）。</summary>
    public static string GuessCategory(string? argument)
    {
        var a = argument ?? "";
        if (a.Contains("storeedgefd", StringComparison.OrdinalIgnoreCase)) return CategoryStore;
        if (a.Contains("winget", StringComparison.OrdinalIgnoreCase)) return CategoryCommunity;
        return CategoryCustom;
    }

    /// <summary>这个分类里有内置源 ⇒ 同分类的源**互斥**（同时最多启用一个）。</summary>
    public static bool IsExclusiveCategory(string? category)
        => string.Equals(category, CategoryCommunity, StringComparison.OrdinalIgnoreCase)
        || string.Equals(category, CategoryStore, StringComparison.OrdinalIgnoreCase);

    /// <summary>归一化分类串（未知值 ⇒ <see cref="CategoryCustom"/>）。</summary>
    public static string NormalizeCategory(string? category)
    {
        foreach (var c in AllCategories)
            if (string.Equals(c, (category ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) return c;
        return CategoryCustom;
    }
}
