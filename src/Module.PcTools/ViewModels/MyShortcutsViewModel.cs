using NewToys.PcTools.Models;
using NewToys.PcTools.Services;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.PcTools.ViewModels;

/// <summary>
/// 「电脑工具 → 设置与脚本」页：一张<b>脚本文件</b>清单（规范的列表页布局，见 3.6）。
/// <para>
/// 页面逻辑（列表 / 排序 / 列显隐 / 运行 / 增删改查 / 收藏）全部在基类 <see cref="QuickActionListViewModel"/> 里，
/// 本类只声明「显示哪一批条目」（<see cref="QuickActionPage.Shortcuts"/>）与「列显隐落盘键」。
/// </para>
/// <para>
/// 与「快捷设置」页的分工（v1.33）：这里放<b>有脚本文件的脚本</b>（<see cref="QuickActionKind.Script"/>，
/// 指向脚本目录下的 .py / .ps1 / .bat，解释器路径在「设置 → 路径配置」配）；
/// 一行的<b>命令行命令</b>放「快捷设置」页。
/// </para>
/// </summary>
public partial class MyShortcutsViewModel : QuickActionListViewModel
{
    protected override string PageKind => QuickActionPage.Shortcuts;
    protected override string ColumnLayoutKey => "pc-shortcuts";
    protected override string PageDisplayName => "设置与脚本";

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public MyShortcutsViewModel() { }

    public MyShortcutsViewModel(IQuickActionService svc, IColumnLayoutService cols, IAppConfigService app)
        : base(svc, cols, app) { }
}
