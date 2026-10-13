using NewToys.MediaAssets.Models;
using NewToys.MediaAssets.Services;
using NewToys.Shared.Services;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>
/// 「书籍」页（NT-017 / v3.34）—— **薄壳页**：实现全部复用 <see cref="AssetAllViewModel"/>，
/// 只声明本页专属的 <c>PageKey</c>（<c>"assets-book"</c>）、预设类型（书籍）与状态栏前缀（<c>"书籍："</c>）。
/// 详见 <see cref="AssetVideoViewModel"/> 的说明（含「必须常量表达式」的原因）。
/// </summary>
public class AssetBookViewModel : AssetAllViewModel
{
    /// <summary>设计期 / DI 未注册时兜底。</summary>
    public AssetBookViewModel() { }

    public AssetBookViewModel(AssetService assets, ScanService scan, IColumnLayoutService cols, IAppConfigService? app = null)
        : base(assets, scan, cols, app)
        => SetPresetType(AssetTypes.Book);

    /// <inheritdoc/>
    protected override string PageKey => "assets-book";

    /// <inheritdoc/>
    protected override string StatusPrefix => "书籍：";
}
