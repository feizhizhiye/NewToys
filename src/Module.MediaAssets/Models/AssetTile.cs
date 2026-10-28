using System;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.MediaAssets.Models;

/// <summary>
/// 「资产浏览」列表的一项 = **磁盘上的一个条目**（文件或子文件夹）。
/// <para>
/// 🔴 与索引的关系是**叠加**而不是来源：目录内容一律来自实时枚举，<see cref="InLibrary"/> 表示
/// 这一项在资产库里有对应索引（<see cref="Row"/> 带 id / 标签 / 元数据）。未入库项用
/// **Id = 0 的合成行**填 <see cref="Row"/>，这样卡片模板与详情卡 ⛔ 不需要到处判空。
/// </para>
/// <para>未入库项整卡标灰（<see cref="RowOpacity"/>）但**功能不减**：打开 / 定位照常，库操作会提示先入库。</para>
/// <para>缩略图异步解码后由 UI 线程赋给 <see cref="Thumb"/>（后台线程解码、前台赋值）；
/// 解码失败 / 缓存缺失 → 保持 null，界面按「类型占位」显示（⛔ 不抛、⛔ 不弹窗）。</para>
/// </summary>
public partial class AssetTile : ObservableObject
{
    public AssetTile(AssetRow row, long locationId, string dir, string absPath,
                     bool inLibrary, bool isFolder, double width)
    {
        Row = row;
        LocationId = locationId;
        Dir = dir;
        AbsPath = absPath;
        InLibrary = inLibrary;
        IsFolder = isFolder;
        Width = width;
        ThumbHeight = Math.Round(width * 0.62);
    }

    /// <summary>叠加的索引行：入库项 = 库里的资产行；未入库项 = Id=0 的合成行（文件名 / 大小 / 修改时间照填）。</summary>
    public AssetRow Row { get; }

    /// <summary>文件位置 id（未入库 = 0）。批量库操作只认它。</summary>
    public long LocationId { get; }

    /// <summary>该位置所在目录（相对存储源根，<c>\</c> 分隔；空串 = 根目录 / 未入库）。</summary>
    public string Dir { get; }

    /// <summary>绝对路径（文件或目录）—— 打开 / 定位 / 缩略图键都用它。</summary>
    public string AbsPath { get; }

    /// <summary>是否已加入资产库（决定标灰与缩略图）。</summary>
    public bool InLibrary { get; }

    /// <summary>是不是目录（双击 = 进入该目录）。</summary>
    public bool IsFolder { get; }

    /// <summary>卡片宽度（= 当前图标尺寸档位）。</summary>
    public double Width { get; }

    /// <summary>缩略图区高度（卡片宽 × 0.62，与图标尺寸档位联动）。</summary>
    public double ThumbHeight { get; }

    /// <summary>缩略图（null = 还没解码 / 没有缓存 / 解码失败 / 未入库 ⇒ 显示类型占位）。</summary>
    [ObservableProperty] private Bitmap? _thumb;

    /// <summary>本项是否被选中（图标视图的选中画在卡片自己身上，⛔ 不靠列表容器）。</summary>
    [ObservableProperty] private bool _isSelected;

    public bool HasThumb => Thumb != null;

    public bool IsMissing => InLibrary && Row.IsMissing;

    /// <summary>整卡不透明度：未入库 0.55、磁盘上找不到 0.4、正常 1.0。</summary>
    public double RowOpacity => IsMissing ? 0.4 : InLibrary ? 1.0 : 0.55;

    public string Name => Row.Name;
    public string TypeText => IsFolder ? "文件夹" : Row.TypeText;
    public string SizeText => IsFolder ? "" : Row.SizeText;
    public string ModifiedText => Row.ModifiedText;

    /// <summary>副行：大小 · 修改时间（目录只显示修改时间）。</summary>
    public string SubText
    {
        get
        {
            if (IsFolder) return ModifiedText;
            return string.IsNullOrEmpty(ModifiedText) ? SizeText : $"{SizeText} · {ModifiedText}";
        }
    }

    /// <summary>只有**已入库**的视频 / 图片才解码缩略图（未入库 = 不碰磁盘、不烧 IO）。</summary>
    public bool WantsThumb => InLibrary && !IsFolder && Row.Type is AssetTypes.Video or AssetTypes.Image;

    /// <summary>缩略图请求键（绝对路径唯一；⛔ 用 LocationId —— 未入库项是 0）。</summary>
    public string ThumbKey => AbsPath;

    partial void OnThumbChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(HasThumb));
    }
}
