using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.PcTools.ViewModels;

/// <summary>
/// 「电脑工具 → 设置与脚本」页：一张<b>较复杂的系统设置项 + 脚本</b>清单（规范的列表页布局，见 3.6）。
/// <para>
/// 页面逻辑（列表 / 排序 / 列显隐 / 运行 / 增删改查 / 收藏）全部在基类 <see cref="QuickActionListViewModel"/> 里，
/// 本类只声明「显示哪一批条目」（<see cref="QuickActionPage.Shortcuts"/>）与「列显隐落盘键」。
/// </para>
/// <para>
/// 与「快捷设置」页的分工：这里放<b>需要脚本 / 命令执行</b>的较复杂设置；
/// 系统自带设置的入口（Windows 设置面板 / 控制面板 / 管理工具）放「快捷设置」页。
/// </para>
/// </summary>
public partial class MyShortcutsViewModel : QuickActionListViewModel
{
    protected override string PageKind => QuickActionPage.Shortcuts;
    protected override string ColumnLayoutKey => "pc-shortcuts";
    protected override string PageDisplayName => "设置与脚本";

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public MyShortcutsViewModel() { }

    public MyShortcutsViewModel(IQuickActionService svc, IColumnLayoutService cols) : base(svc, cols) { }
}
