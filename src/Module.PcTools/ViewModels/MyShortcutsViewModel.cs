using CommunityToolkit.Mvvm.Input;
using NewToys.PcTools.Models;
using NewToys.PcTools.Services;
using NewToys.Shared.Details;
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

    /// <summary>本页详情卡（NT-054 / 需求 §4.5.1）：类型下拉 + 「命令」/「脚本文件」两套字段（按条目类型显隐）。</summary>
    protected override DetailCardDescriptor BuildCard()
    {
        var card = new DetailCardDescriptor
        {
            BodyVisiblePath = nameof(ShowDetailBody),
            SamplePath = nameof(IsDetailSample),
            EmptyText = "在左侧列表里选中一条，可在这里编辑名称 / 分类 / 简介与「命令」或「脚本文件」，并点「▶ 运行」执行；右键列表还有「运行 / 收藏 / 复制命令 / 删除」。点「➕ 新增」可添加一条。",
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
                            Placeholder = "系统设置 / 网络 / 维护 / 打开…"
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
                            Label = "类型",
                            Kind = FieldKind.Select,
                            OptionsPath = nameof(KindOptions),
                            OptionDisplayPath = "Label",
                            ValuePath = nameof(SelectedKind)
                        },
                        // 「命令」只在命令行条目上显示（脚本条目走下面的脚本文件 / 解释器）
                        new DetailField
                        {
                            Label = "命令",
                            ValuePath = nameof(EditCommand),
                            Kind = FieldKind.Text,
                            Commit = EditCommitMode.OnBlur,
                            Mono = true,
                            MultiLine = true,
                            Rows = 84,
                            VisibleWhen = nameof(IsCommandKind)
                        },
                        new DetailField
                        {
                            Kind = FieldKind.Hint,
                            Hint = "▶ 运行 = 用 cmd 执行这条命令；勾选「需要管理员权限」时会弹 UAC（输出见新开的命令行窗口）。成功 / 失败原因显示在顶部状态行。",
                            VisibleWhen = nameof(IsCommandKind)
                        },
                        new DetailField
                        {
                            Label = "脚本文件",
                            ValuePath = nameof(EditScriptFile),
                            Kind = FieldKind.Text,
                            Commit = EditCommitMode.OnBlur,
                            Mono = true,
                            Placeholder = "如 sysinfo.ps1（相对脚本目录）或 D:\\my\\script.py",
                            VisibleWhen = nameof(IsScriptKind)
                        },
                        new DetailField
                        {
                            Label = "解释器",
                            Kind = FieldKind.Select,
                            OptionsPath = nameof(InterpreterOptions),
                            OptionDisplayPath = "Label",
                            ValuePath = nameof(SelectedInterpreter),
                            VisibleWhen = nameof(IsScriptKind)
                        },
                        new DetailField
                        {
                            Kind = FieldKind.Hint,
                            ValuePath = nameof(ScriptHint),
                            VisibleWhen = nameof(IsScriptKind)
                        },
                        new DetailField
                        {
                            Label = "需要管理员权限",
                            ValuePath = nameof(EditNeedAdmin),
                            Kind = FieldKind.Check
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
        // 原来「脚本文件」框右侧有个内联 📂（走 code-behind）—— 声明式卡里收进 Actions 区。
        card.Actions.Add(new DetailAction
        {
            Text = "📂 选脚本文件…",
            Classes = "ghost",
            ToolTip = "挑一个脚本文件（相对脚本目录，或绝对路径）",
            IsVisiblePath = nameof(IsScriptKind),
            Command = new RelayCommand(() => PickScriptFileCommand?.Execute(null))
        });

        return card;
    }

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public MyShortcutsViewModel() { }

    public MyShortcutsViewModel(IQuickActionService svc, IColumnLayoutService cols, IAppConfigService app)
        : base(svc, cols, app) { }
}
