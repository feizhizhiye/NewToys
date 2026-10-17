using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.Settings.Models;

/// <summary>路径项的类型 —— 决定「浏览…」打开的是文件选择器还是文件夹选择器。</summary>
public enum PathItemKind
{
    /// <summary>可执行文件 / 文件路径（ffmpeg、python、播放器…）。</summary>
    File,

    /// <summary>目录路径（脚本目录…）。</summary>
    Folder,

    /// <summary>只读展示（数据目录 / 配置目录：由应用自己决定，不给改）。</summary>
    ReadOnly
}

/// <summary>
/// 行分类（v3.53 / NT-056，需求 §4.9.1）：「设置 → 路径与配置」是一张**设置总表**，
/// 用分类列区分行类型、并据此决定"哪些工具可用"。
/// <para>
/// 🔴 分类是**开放集合**：以后若要「指定某个功能默认使用哪个 json / 配置」，只加一个枚举值
/// （如 `功能绑定`）+ 该分类的行与工具即可，⛔ 不再新开页面 —— 这是合并两页的根本理由。
/// </para>
/// </summary>
public enum SettingCategory
{
    /// <summary>路径项：`app.json` 里的运行环境路径（ffmpeg / 播放器 / 解释器 / 脚本目录…）。</summary>
    Path,

    /// <summary>配置档：`profiles\` 下的多套独立 data（config / data / logs），切换 = 重启本窗口。</summary>
    Profile
}

/// <summary>
/// 「设置 → 路径与配置」列表里的一行（v3.53 / NT-056 起承载**两类**行）。
/// <para>
/// 本页原先是一张"若干 TextBox + 一个💾 保存"的表单，v1.34 起改成与其他列表页一致的
/// **列表 + 详情**布局（左可排序表头、右详情卡、表头右侧小按钮展开详情）：
/// 路径项会越来越多（解释器、脚本目录、缓存库…），表单式排版只会越来越长。
/// </para>
/// <para>
/// v3.53 起「配置」页并入本表：配置行也用它，靠 <see cref="Category"/> 区分，⛔ 不搞两套行类型。
/// </para>
/// </summary>
public partial class PathSettingItem : ObservableObject
{
    /// <summary>稳定键（写回 <c>app.json</c> 时按它分派），如 <c>FfmpegPath</c>。</summary>
    public string Key { get; set; } = "";

    /// <summary>列表第一列显示的名字。</summary>
    public string Name { get; set; } = "";

    /// <summary>一句话说明（第三列 / 详情卡）。</summary>
    public string Description { get; set; } = "";

    public PathItemKind Kind { get; set; } = PathItemKind.File;

    // ==================== v3.53 / NT-056：分类（设置总表） ====================

    /// <summary>行分类（默认路径行）；「操作」列与工具可用性都按它分派。</summary>
    public SettingCategory Category { get; set; } = SettingCategory.Path;

    /// <summary>分类列的显示文字（**排序 / 筛选的取值来源**，故必须是纯文本属性）。</summary>
    public string CategoryText => Category == SettingCategory.Profile ? "配置" : "路径";

    /// <summary>是不是配置行（行内「操作」列按它分派）。</summary>
    public bool IsProfileRow => Category == SettingCategory.Profile;

    /// <summary>是不是路径行。</summary>
    public bool IsPathRow => Category == SettingCategory.Path;

    /// <summary>配置行：是否**本窗口正在使用**的配置档（行在 <c>RefreshProfiles</c> 里整体重建，故无需通知）。</summary>
    public bool IsActiveProfile { get; set; }

    /// <summary>配置行的「当前使用」标记（列表名称列用强调色显示；路径行恒为空）。</summary>
    public string Mark => IsActiveProfile ? "● 当前使用" : "";

    /// <summary>配置行的状态文字（详情卡用）。</summary>
    public string ProfileStateText => IsActiveProfile ? "当前窗口正在使用" : "未使用（可切换 / 新开 / 删除）";

    /// <summary>默认值（「↩ 还原默认」预填用；空串表示"留空即用系统默认"）。</summary>
    public string DefaultValue { get; set; } = "";

    /// <summary>当前值。</summary>
    [ObservableProperty] private string _value = "";

    /// <summary>不能编辑的展示项（数据目录 / 配置目录；**配置行的「当前值」是数据目录，同样只读展示**）。</summary>
    public bool IsReadOnly => Kind == PathItemKind.ReadOnly;

    public bool IsEditable => !IsReadOnly;

    /// <summary>第三列显示的类型文字（配置行显示「配置档」——它们的 Kind 借只读展示位，不代表真的只读项）。</summary>
    public string KindText => Category == SettingCategory.Profile
        ? "配置档"
        : Kind switch
        {
            PathItemKind.Folder => "目录",
            PathItemKind.ReadOnly => "只读",
            _ => "文件"
        };

    /// <summary>留空时列表里显示的提示（别显示成空白，看起来像坏了）。</summary>
    public string ValueDisplay => Value.Length > 0
        ? Value
        : (IsReadOnly ? "—" : (DefaultValue.Length > 0 ? $"（默认：{DefaultValue}）" : "（留空 = 用系统默认）"));

    partial void OnValueChanged(string value) => OnPropertyChanged(nameof(ValueDisplay));
}
