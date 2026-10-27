using System;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.MediaAssets.Models;

/// <summary>
/// 「资产浏览」图标卡视图的一项 = 一个**文件位置行**（同一资产在 A / B 两个目录各出现一次是正确语义）。
/// <para>缩略图异步解码后由 UI 线程赋给 <see cref="Thumb"/>（后台线程解码、前台赋值）；
/// 解码失败 / 缓存缺失 → 保持 null，界面按「类型占位」显示（⛔ 不抛、⛔ 不弹窗）。</para>
/// <para>尺寸在构造时给定（<see cref="Width"/> / <see cref="ThumbHeight"/>）：图标尺寸切档时整页重建，
/// 免得在模板里写祖先绑定去取 VM 的当前档位。</para>
/// </summary>
public partial class AssetTile : ObservableObject
{
    public AssetTile(AssetRow row, long locationId, string dir, double width)
    {
        Row = row;
        LocationId = locationId;
        Dir = dir;
        Width = width;
        ThumbHeight = Math.Round(width * 0.62);
    }

    public AssetRow Row { get; }

    public long LocationId { get; }

    /// <summary>该位置所在目录（相对存储源根，<c>\</c> 分隔；空串 = 根目录）。</summary>
    public string Dir { get; }

    /// <summary>卡片宽度（= 当前图标尺寸档位）。</summary>
    public double Width { get; }

    /// <summary>缩略图区高度（卡片宽 × 0.62，与图标尺寸档位联动）。</summary>
    public double ThumbHeight { get; }

    /// <summary>缩略图（null = 还没解码 / 没有缓存 / 解码失败 ⇒ 显示类型占位）。</summary>
    [ObservableProperty] private Bitmap? _thumb;

    public bool HasThumb => Thumb != null;

    public bool IsMissing => Row.IsMissing;

    /// <summary>行整体不透明度（文件在磁盘上已找不到 ⇒ 标灰）。</summary>
    public double RowOpacity => IsMissing ? 0.45 : 1.0;

    public string Name => Row.Name;
    public string TypeText => Row.TypeText;
    public string SizeText => Row.SizeText;
    public string ModifiedText => Row.ModifiedText;

    /// <summary>副行：大小 · 修改时间。</summary>
    public string SubText => string.IsNullOrEmpty(ModifiedText) ? SizeText : $"{SizeText} · {ModifiedText}";

    /// <summary>只有视频 / 图片有缩略图（其余类型直接显示占位）。</summary>
    public bool WantsThumb => Row.Type is AssetTypes.Video or AssetTypes.Image;

    partial void OnThumbChanged(Bitmap? value) => OnPropertyChanged(nameof(HasThumb));
}
