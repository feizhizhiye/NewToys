using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.MediaAssets.Models;

/// <summary>
/// 标签管理页的一行（NT-018）：`tags` + 使用数（`asset_tags` 计数）。
/// <para>列：名称 / 命名空间 / 颜色 / 使用数 / 创建时间（需求 §6.6 页面清单）。</para>
/// </summary>
public class TagRow
{
    public long Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>命名空间（可空；空 = 「默认」）。</summary>
    public string? Namespace { get; set; }

    /// <summary>库里存的颜色（用户改过的）；为空则按名字取 <see cref="AssetPalette"/>。</summary>
    public string? Color { get; set; }

    /// <summary>有多少个资产在用这个标签。</summary>
    public int UsageCount { get; set; }

    public string CreatedAt { get; set; } = "";

    // ===== 界面派生（⛔ 不落库） =====

    public string NamespaceText => string.IsNullOrWhiteSpace(Namespace) ? "（默认）" : Namespace!;

    /// <summary>该标签的最终色（库值优先，否则稳定散列）。</summary>
    public string ColorHex => AssetPalette.Resolve(Color, Name);

    /// <summary>列表里的色片（数据色 → 画刷；⛔ XAML 不写死色值）。</summary>
    public IBrush ChipBrush => new SolidColorBrush(Color.Parse(ColorHex));

    public string ColorHexText => ColorHex.ToUpperInvariant();

    public string UsageText => UsageCount == 0 ? "未使用" : $"{UsageCount} 个";

    public string CreatedText
        => DateTime.TryParse(CreatedAt, out var dt) ? dt.ToString("yyyy-MM-dd HH:mm") : CreatedAt;

    /// <summary>右键「删除」时提示用（未使用的标签删掉最干净）。</summary>
    public bool IsUnused => UsageCount == 0;
}

/// <summary>
/// 「用了这个标签的一个资产」的轻量引用（标签详情卡里列出；点一下能跳到它所在的页）。
/// <para>⛔ 不装整行 <c>AssetRow</c>：详情卡只需要 id + 名字，不必把文件位置等全查出来（省一次大查询）。</para>
/// </summary>
public class TagAssetRef
{
    public TagAssetRef(long id, string name)
    {
        Id = id;
        Name = name;
    }

    public long Id { get; }
    public string Name { get; }
}

/// <summary>
/// 「详情卡里可点的色片」（NT-018）：标签 / 分类 / 集合三种多选项共用。
/// 点一下 = 给当前资产打上 / 取消；<see cref="IsSelected"/> 决定实心还是淡显。
/// </summary>
public partial class PickItem : ObservableObject
{
    public PickItem(long id, string name, IBrush brush, bool selected)
    {
        Id = id;
        Name = name;
        Brush = brush;
        _isSelected = selected;
    }

    public long Id { get; }
    public string Name { get; }

    /// <summary>色片画刷（数据色；标签用库里的色，分类 / 集合按名字稳定取）。</summary>
    public IBrush Brush { get; }

    [ObservableProperty] private bool _isSelected;
}
