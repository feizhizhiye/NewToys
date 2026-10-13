using NewToys.MediaAssets.Models;
using NewToys.MediaAssets.Services;
using NewToys.Shared.Services;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>
/// 「视频」页（NT-017 / v3.34）—— **薄壳页**：实现全部复用 <see cref="AssetAllViewModel"/>，
/// 这里只声明本页专属的三件事：
/// <list type="number">
///   <item><see cref="PageKey"/> = <c>"assets-video"</c> → 列显隐 / 排序 / 详情开合**独立**落 <c>config\columnlayout.json</c>；</item>
///   <item>预设类型 = 视频（构造末尾调基类 <c>SetPresetType</c>，只重算视图、⛔ 不重查库）；</item>
///   <item><see cref="StatusPrefix"/> = <c>"视频："</c>（状态栏前缀）。</item>
/// </list>
/// <para>
/// 🔴 <see cref="PageKey"/> 与 <see cref="StatusPrefix"/> 都被**基类构造函数**读（读列偏好 / 写状态栏），
/// 所以必须写成**常量表达式**，⛔ 别依赖本类字段（那一刻字段还没初始化）。
/// </para>
/// </summary>
public class AssetVideoViewModel : AssetAllViewModel
{
    /// <summary>设计期 / DI 未注册时兜底（与基类同：不读库、不建下拉）。</summary>
    public AssetVideoViewModel() { }

    public AssetVideoViewModel(AssetService assets, ScanService scan, IColumnLayoutService cols,
        IAppConfigService? app = null, TagService? tagSvc = null, CategoryService? catSvc = null,
        CollectionService? colSvc = null, RelationService? relationSvc = null,
        OperationLogService? opLog = null, AssetNavigationStore? navStore = null, FileOperationService? fileOps = null)
        : base(assets, scan, cols, app, tagSvc, catSvc, colSvc, relationSvc, opLog, navStore, fileOps)
        => SetPresetType(AssetTypes.Video);

    /// <inheritdoc/>
    protected override string PageKey => "assets-video";

    /// <inheritdoc/>
    protected override string StatusPrefix => "视频：";
}
