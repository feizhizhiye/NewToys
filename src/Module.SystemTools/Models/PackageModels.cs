using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.SystemTools.Models;

/// <summary>
/// 一条软件包记录 —— 「搜索结果 / 已安装 / 可升级」三种列表**共用同一个模型**。
/// <para>
/// 为什么共用：列表页的表头排序、右键菜单、右侧详情三处都只认它，各写一份必然漂移
/// （见规范 3.8「列表页布局规范」）。
/// </para>
/// <para>
/// 继承 <see cref="ObservableObject"/> —— 详情卡绑 <c>Selected.Publisher</c> 等，
/// 「🔎 读取详情」回填字段后**必须通知 UI 才会补充显示**（改了不刷新 = 详情不补充）。
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
    /// **勾选列**状态（「⬇ 安装选中」「⬆ 升级选中」按勾选批量执行）。
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
    /// <summary>源名，也是 <c>--source</c> 的取值（winget / winget-font …）。</summary>
    public string Name { get; set; } = "";

    /// <summary>源的 URL / UNC 路径。</summary>
    public string Argument { get; set; } = "";

    /// <summary>源类型（<c>winget source export</c> 的 <c>Type</c>，如 <c>Microsoft.PreIndexed.Package</c>）；列表输出里没有 ⇒ 空。</summary>
    public string Type { get; set; } = "";

    /// <summary>
    /// 显式源（<c>source list</c> 的「显式」列）：<c>true</c> = 不参与默认搜索，只有显式 <c>--source</c> 才用。
    /// <para>🔴 重新添加时必须带上 <c>--explicit</c>，否则字体源之类的显式源会被卷进每次默认搜索。</para>
    /// </summary>
    public bool Explicit { get; set; }

    /// <summary>「发现」列文案：显式源只在你勾选它时才被查询。</summary>
    public string ScopeText => Explicit ? "显式（仅勾选时查询）" : "默认参与搜索";

    public string TypeText => Type.Length == 0 ? "—" : Type;
}

/// <summary>
/// 「软件包管理」模块的**应用级设置**，落 <c>&lt;应用根&gt;\config\package.json</c>。
/// <para>
/// ⚠️ 这是"设置里的数据"：它由设置页维护，但只有 <c>config\</c> 下的文件
/// 才能被「数据管理」页备份 / 导入 —— 所以**不要**把源设置散落到模块自己的目录里
/// （见规范 3.9「设置数据可备份，模块只读设置」）。
/// </para>
/// <para>
/// ⚠️ 🔴 **⛔ 不存源相关偏好**（defaultSource / 源分类 / 源定义快照这类字段别再回来）。
/// 源是系统 winget 的东西，增删 / 切换一律走「命令库」里的命令条目；本文件只剩安装偏好与排序。
/// </para>
/// </summary>
public class PackageSettings
{
    /// <summary>安装 / 升级时追加 <c>--silent</c>（不弹安装器交互）。</summary>
    public bool SilentInstall { get; set; } = true;

    /// <summary>上次的排序列（列表页打开时恢复）。</summary>
    public string SortColumn { get; set; } = "Name";

    /// <summary>上次的排序方向。</summary>
    public bool SortDescending { get; set; }
}

/// <summary>
/// winget **内置源**常量（v3.80 精简）。
/// <para>
/// 🔴 只留「商店源」判定：商店包（<c>msstore</c>，Id 形如 <c>9NBLGGH4NNS1</c>）走 Microsoft Store 安装通道，
/// ⛔ 不吃 <c>--silent</c>。
/// </para>
/// <para>
/// 🔴 源的增删 / 切换已**移出本应用**（系统 winget 配置，全要 UAC）⇒ 分类 / 互斥 / 定义快照 / 镜像地址
/// 这些常量一并删除；要改源请用「命令库 → winget 源」集合里的命令。
/// </para>
/// </summary>
public static class PackageSources
{
    /// <summary>商店源名（<c>msstore</c>）。</summary>
    public const string StoreName = "msstore";

    /// <summary>是不是商店源（忽略大小写 / 首尾空格）。</summary>
    public static bool IsStore(string? name)
        => string.Equals((name ?? "").Trim(), StoreName, StringComparison.OrdinalIgnoreCase);
}
