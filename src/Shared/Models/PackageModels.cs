using System;

namespace NewToys.Shared.Models;

/// <summary>
/// 一条软件包记录 —— 「搜索结果 / 已安装 / 可升级」三种列表**共用同一个模型**。
/// <para>
/// 为什么共用：列表页的表头排序、右键菜单、右侧详情三处都只认它，各写一份必然漂移
/// （见规范 3.8「列表页布局规范」）。
/// </para>
/// </summary>
public class PackageInfo
{
    public string Name { get; set; } = "";
    public string Id { get; set; } = "";

    /// <summary>已安装版本；在「搜索结果」里是**最新可用版本**（winget search 的语义）。</summary>
    public string Version { get; set; } = "";

    /// <summary>可升级到的版本（`winget list` / `winget upgrade` 的 Available 列）。</summary>
    public string Available { get; set; } = "";

    /// <summary>来源（winget / msstore …）。</summary>
    public string Source { get; set; } = "";

    public bool IsInstalled { get; set; }

    /// <summary>是否被 pin（pin = 暂停更新，winget upgrade 会跳过它）。</summary>
    public bool IsPinned { get; set; }

    /// <summary>被 pin 住的版本（来自 <c>winget pin list</c>，可能为空）。</summary>
    public string PinnedVersion { get; set; } = "";

    // ---------- 以下字段按需补：只有点「读取详情」跑一次 winget show 才填 ----------
    public string Description { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Homepage { get; set; } = "";
    public string License { get; set; } = "";
    public string InstallLocation { get; set; } = "";

    /// <summary>有可用更新（Available 存在且与当前版本不同）。</summary>
    public bool HasUpgrade => !string.IsNullOrEmpty(Available)
                              && !string.Equals(Available, Version, StringComparison.OrdinalIgnoreCase);

    /// <summary>列表「状态」列的文字。</summary>
    public string StatusText =>
        IsPinned ? "已 Pin（暂停更新）"
        : !IsInstalled ? "未安装"
        : HasUpgrade ? "可升级"
        : "已安装";
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

    /// <summary>是否在应用内启用（由 <see cref="PackageSettings.StoreSourceEnabled"/> 决定 msstore 的取值）。</summary>
    public bool IsEnabled { get; set; } = true;

    public string TypeText => Type switch
    {
        "true" or "True" => "自定义（explicit）",
        "false" or "False" => "内置",
        "" => "—",
        _ => Type
    };

    /// <summary>列表「状态」列：本应用查询时是否用它。</summary>
    public string EnabledText => IsEnabled ? "启用" : "已停用";
}

/// <summary>
/// 「源」下拉的一项。
/// <para>
/// 放在 <c>Shared</c> 是因为**两个模块都要用**：PC 工具箱（软件包管理页的源下拉）与
/// 设置模块（软件源的默认源下拉）。模块之间禁止互相引用，所以共用类型一律进 Shared。
/// </para>
/// <para><see cref="Value"/> 为 <c>null</c> 表示「全部已启用的源」。</para>
/// </summary>
public class PackageSourceOption
{
    public string Label { get; set; } = "";
    public string? Value { get; set; }
    public override string ToString() => Label;
}

/// <summary>
/// 「软件包管理」模块的**应用级设置**，落 <c>&lt;应用根&gt;\config\package.json</c>。
/// <para>
/// ⚠️ 这是"设置里的数据"：它由设置模块的「软件源」页维护，但只有 <c>config\</c> 下的文件
/// 才能被「数据管理」页备份 / 导入 —— 所以**不要**把源设置散落到模块自己的目录里
/// （见规范 3.9「设置数据可备份，模块只读设置」）。
/// </para>
/// </summary>
public class PackageSettings
{
    /// <summary>默认查询源（对应 <c>--source</c> 的取值）；空串表示「全部已启用的源」。</summary>
    public string DefaultSource { get; set; } = "";

    /// <summary>是否在应用内启用 Microsoft Store 源。关掉后「全部源」只查 winget，msstore 不出现在下拉里。</summary>
    public bool StoreSourceEnabled { get; set; } = true;

    /// <summary>安装 / 升级时追加 <c>--silent</c>（不弹安装器交互）。</summary>
    public bool SilentInstall { get; set; } = true;

    /// <summary>上次的排序列（列表页打开时恢复）。</summary>
    public string SortColumn { get; set; } = "Name";

    /// <summary>上次的排序方向。</summary>
    public bool SortDescending { get; set; }
}
