using NewToys.MediaAssets.Models;
using NewToys.MediaAssets.Services;
using NewToys.Shared.Services;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>
/// 「图片」页（NT-017 / v3.34）—— **薄壳页**：实现全部复用 <see cref="AssetAllViewModel"/>，
/// 只声明本页专属的 <c>PageKey</c>（<c>"assets-image"</c>）、预设类型（图片）与状态栏前缀（<c>"图片："</c>）。
/// <para>⛔ 卡片 / 网格视图不做（v3.34 用户定调）—— 本页与其余四个类型页**完全同构**。</para>
/// 详见 <see cref="AssetVideoViewModel"/> 的说明（含「必须常量表达式」的原因）。
/// </summary>
public class AssetImageViewModel : AssetAllViewModel
{
    /// <summary>设计期 / DI 未注册时兜底。</summary>
    public AssetImageViewModel() { }

    public AssetImageViewModel(AssetService assets, ScanService scan, IColumnLayoutService cols,
        IAppConfigService? app = null, TagService? tagSvc = null, CategoryService? catSvc = null,
        CollectionService? colSvc = null, RelationService? relationSvc = null,
        OperationLogService? opLog = null, AssetNavigationStore? navStore = null, FileOperationService? fileOps = null)
        : base(assets, scan, cols, app, tagSvc, catSvc, colSvc, relationSvc, opLog, navStore, fileOps)
        => SetPresetType(AssetTypes.Image);

    /// <inheritdoc/>
    protected override string PageKey => "assets-image";

    /// <inheritdoc/>
    protected override string StatusPrefix => "图片：";
}
