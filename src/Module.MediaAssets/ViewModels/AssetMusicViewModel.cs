using NewToys.MediaAssets.Models;
using NewToys.MediaAssets.Services;
using NewToys.Shared.Services;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>
/// 「音乐」页（NT-017 / v3.34）—— **薄壳页**：实现全部复用 <see cref="AssetAllViewModel"/>，
/// 只声明本页专属的 <c>PageKey</c>（<c>"assets-music"</c>）、预设类型（音乐）与状态栏前缀（<c>"音乐："</c>）。
/// 详见 <see cref="AssetVideoViewModel"/> 的说明（含「必须常量表达式」的原因）。
/// </summary>
public class AssetMusicViewModel : AssetAllViewModel
{
    /// <summary>设计期 / DI 未注册时兜底。</summary>
    public AssetMusicViewModel() { }

    public AssetMusicViewModel(AssetService assets, ScanService scan, IColumnLayoutService cols,
        IAppConfigService? app = null, TagService? tagSvc = null, CategoryService? catSvc = null,
        CollectionService? colSvc = null, RelationService? relationSvc = null,
        OperationLogService? opLog = null, AssetNavigationStore? navStore = null)
        : base(assets, scan, cols, app, tagSvc, catSvc, colSvc, relationSvc, opLog, navStore)
        => SetPresetType(AssetTypes.Music);

    /// <inheritdoc/>
    protected override string PageKey => "assets-music";

    /// <inheritdoc/>
    protected override string StatusPrefix => "音乐：";
}
