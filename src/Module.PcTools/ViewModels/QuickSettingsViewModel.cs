using NewToys.PcTools.Models;
using NewToys.PcTools.Services;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.PcTools.ViewModels;

/// <summary>
/// 「电脑工具 → 快捷设置」页：一张<b>命令行命令</b>清单（系统自带设置入口 / 注册表开关 / 工具命令），
/// 布局与「设置与脚本」页完全一致（规范 3.6），点「▶ 运行」即用 <c>cmd</c> 执行那行命令。
/// <para>
/// 页面逻辑全部在基类 <see cref="QuickActionListViewModel"/> 里；本类只声明
/// 「显示哪一批条目」（<see cref="QuickActionPage.Quick"/>）与「列显隐落盘键」。
/// </para>
/// <para>
/// 与「设置与脚本」页的分工（v1.33）：这里放<b>命令行命令</b>（<see cref="QuickActionKind.Command"/>，
/// 如 <c>start ms-settings:display</c>、<c>ipconfig /flushdns</c>）；
/// <b>有脚本文件的脚本</b>（.py / .ps1 / .bat）放「设置与脚本」页。
/// 两页**各有自己的清单文件**（v1.34）：本页 = <c>config\pc-quick.json</c>，
/// 「设置与脚本」= <c>config\pc-scripts.json</c>；收藏是同一批 <c>IsFavorite</c> 标记。
/// </para>
/// </summary>
public partial class QuickSettingsViewModel : QuickActionListViewModel
{
    protected override string PageKind => QuickActionPage.Quick;
    protected override string ColumnLayoutKey => "pc-quicksettings";
    protected override string PageDisplayName => "快捷设置";

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public QuickSettingsViewModel() { }

    public QuickSettingsViewModel(IQuickActionService svc, IColumnLayoutService cols, IAppConfigService app)
        : base(svc, cols, app) { }
}
