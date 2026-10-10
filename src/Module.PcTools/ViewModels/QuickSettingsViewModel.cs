using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.PcTools.ViewModels;

/// <summary>
/// 「电脑工具 → 快捷设置」页：一张<b>系统自带设置入口</b>清单（Windows 设置面板 / 控制面板 / 管理工具），
/// 布局与「设置与脚本」页完全一致（规范 3.6），点「▶ 运行」即打开对应面板。
/// <para>
/// 页面逻辑全部在基类 <see cref="QuickActionListViewModel"/> 里；本类只声明
/// 「显示哪一批条目」（<see cref="QuickActionPage.Quick"/>）与「列显隐落盘键」。
/// </para>
/// <para>
/// 与「设置与脚本」页的分工：这里放<b>系统自带的设置 / 工具入口</b>（打开即用）；
/// 需要脚本 / 命令执行的较复杂设置放「设置与脚本」页。两页共享同一份
/// <c>config\quickactions.json</c>，收藏也是同一批标记。
/// </para>
/// </summary>
public partial class QuickSettingsViewModel : QuickActionListViewModel
{
    protected override string PageKind => QuickActionPage.Quick;
    protected override string ColumnLayoutKey => "pc-quicksettings";
    protected override string PageDisplayName => "快捷设置";

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public QuickSettingsViewModel() { }

    public QuickSettingsViewModel(IQuickActionService svc, IColumnLayoutService cols) : base(svc, cols) { }
}
