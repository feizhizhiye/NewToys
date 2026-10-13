using NewToys.MediaAssets.Models;
using NewToys.MediaAssets.Services;
using NewToys.Shared.Services;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>
/// 「文档」页（NT-017 / v3.34）—— **薄壳页**：实现全部复用 <see cref="AssetAllViewModel"/>，
/// 只声明本页专属的 <c>PageKey</c>（<c>"assets-document"</c>）、预设类型（文档）与状态栏前缀（<c>"文档："</c>）。
/// 详见 <see cref="AssetVideoViewModel"/> 的说明（含「必须常量表达式」的原因）。
/// </summary>
public class AssetDocumentViewModel : AssetAllViewModel
{
    /// <summary>设计期 / DI 未注册时兜底。</summary>
    public AssetDocumentViewModel() { }

    public AssetDocumentViewModel(AssetService assets, ScanService scan, IColumnLayoutService cols,
        IAppConfigService? app = null, TagService? tagSvc = null, CategoryService? catSvc = null,
        CollectionService? colSvc = null, RelationService? relationSvc = null,
        OperationLogService? opLog = null, AssetNavigationStore? navStore = null, FileOperationService? fileOps = null,
        AssetImportExport? impExp = null, AssetMergeService? merge = null, TaskQueue? queue = null)
        : base(assets, scan, cols, app, tagSvc, catSvc, colSvc, relationSvc, opLog, navStore, fileOps, impExp, merge, queue)
        => SetPresetType(AssetTypes.Document);

    /// <inheritdoc/>
    protected override string PageKey => "assets-document";

    /// <inheritdoc/>
    protected override string StatusPrefix => "文档：";
}
