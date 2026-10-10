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
/// 「设置 → 路径配置」列表里的一行 = 一个路径项。
/// <para>
/// 本页原先是一张"若干 TextBox + 一个💾 保存"的表单，v1.34 起改成与其他列表页一致的
/// **列表 + 详情**布局（左可排序表头、右详情卡、表头右侧小按钮展开详情）：
/// 路径项会越来越多（解释器、脚本目录、缓存库…），表单式排版只会越来越长。
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

    /// <summary>默认值（「↩ 还原默认」预填用；空串表示"留空即用系统默认"）。</summary>
    public string DefaultValue { get; set; } = "";

    /// <summary>当前值。</summary>
    [ObservableProperty] private string _value = "";

    /// <summary>不能编辑的展示项（数据目录 / 配置目录）。</summary>
    public bool IsReadOnly => Kind == PathItemKind.ReadOnly;

    public bool IsEditable => !IsReadOnly;

    /// <summary>第三列显示的类型文字。</summary>
    public string KindText => Kind switch
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
