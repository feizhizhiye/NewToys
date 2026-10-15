using NewToys.PcTools.Models;
using NewToys.PcTools.Services;
using NewToys.Shared.Details;
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

    /// <summary>本页详情卡（NT-054 / 需求 §4.5.1）：本页条目**全是命令行命令** ⇒ 没有类型下拉，多出 Shell / 隐藏终端窗口两项。</summary>
    protected override DetailCardDescriptor BuildCard()
    {
        var card = new DetailCardDescriptor
        {
            BodyVisiblePath = nameof(ShowDetailBody),
            SamplePath = nameof(IsDetailSample),
            EmptyText = "在左侧列表里选中一条。这里放的是系统自带的设置入口（Windows 设置面板 / 控制面板 / 管理工具），选中后点「▶ 运行」即打开；右键列表还有「运行 / 收藏 / 复制命令 / 删除」。点「➕ 新增」可添加一条。",
            SampleText = "（示例数据）在左侧选中一条后，这里显示它的真实内容；示例态下卡片不可编辑。",
            Sections =
            {
                new DetailSection
                {
                    Header = "选中项",
                    Fields =
                    {
                        new DetailField
                        {
                            Label = "名称",
                            ValuePath = nameof(EditTitle),
                            Kind = FieldKind.Text,
                            Commit = EditCommitMode.OnBlur
                        },
                        new DetailField
                        {
                            Label = "分类",
                            ValuePath = nameof(EditCategory),
                            Kind = FieldKind.Text,
                            Commit = EditCommitMode.OnBlur,
                            Placeholder = "系统设置 / 打开…"
                        },
                        new DetailField
                        {
                            Label = "简介",
                            ValuePath = nameof(EditDescription),
                            Kind = FieldKind.Text,
                            Commit = EditCommitMode.OnBlur,
                            MultiLine = true,
                            Rows = 52
                        },
                        new DetailField
                        {
                            Label = "命令",
                            ValuePath = nameof(EditCommand),
                            Kind = FieldKind.Text,
                            Commit = EditCommitMode.OnBlur,
                            Mono = true,
                            MultiLine = true,
                            Rows = 84
                        },
                        new DetailField
                        {
                            Kind = FieldKind.Hint,
                            Hint = "本页条目都是命令行命令：例：start ms-settings:display 打开「显示」设置；control 打开控制面板。▶ 运行 = 用 cmd 执行它。要跑脚本文件（.py / .ps1 / .bat）请去「设置与脚本」页。"
                        },
                        new DetailField
                        {
                            Label = "需要管理员权限",
                            ValuePath = nameof(EditNeedAdmin),
                            Kind = FieldKind.Check
                        },
                        // 命令行宿主（v3.19 / NT-006）：仅命令行条目显示
                        new DetailField
                        {
                            Label = "Shell（命令行宿主）",
                            Kind = FieldKind.Select,
                            OptionsPath = nameof(ShellOptions),
                            OptionDisplayPath = "Label",
                            ValuePath = nameof(SelectedShell),
                            VisibleWhen = nameof(ShowCommandRunOptions)
                        },
                        new DetailField
                        {
                            Label = "隐藏终端窗口（GUI 启动类：不弹终端，静默打开目标程序）",
                            ValuePath = nameof(EditRunHidden),
                            Kind = FieldKind.Check,
                            VisibleWhen = nameof(ShowCommandRunOptions)
                        },
                        new DetailField { Kind = FieldKind.Divider },
                        new DetailField
                        {
                            Kind = FieldKind.Hint,
                            ValuePath = nameof(DataFileHint)
                        }
                    }
                }
            }
        };

        card.Actions.AddRange(BuildCommonActions());
        return card;
    }

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public QuickSettingsViewModel() { }

    public QuickSettingsViewModel(IQuickActionService svc, IColumnLayoutService cols, IAppConfigService app)
        : base(svc, cols, app) { }
}
