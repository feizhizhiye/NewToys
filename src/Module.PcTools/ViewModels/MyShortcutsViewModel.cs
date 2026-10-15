using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
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

    // ==================== 工具条（NT-055 / 需求 §4.8.5） ====================

    protected override string ExportSuggestedName => "pc-scripts-export.json";

    /// <summary>
    /// 本页专属的工具条项（插在搜索框之后、导入 / 导出之前）：脚本目录（只读框 + 选择 / 打开 / 默认）
    /// + 「📥 批量导入脚本」+「含子目录」。与迁移前的页内工具条**逐项一致**。
    /// </summary>

    protected override IEnumerable<ToolbarItemDescriptor> ToolbarExtras => new ToolbarItemDescriptor[]
    {
        // v3.60：原 Label「脚本目录」删除（工具条⛔ 不放提示文字）—— 含义由下面的只读框 ToolTip（ScriptHint）表达。
        new()
        {
            Text = "📥 批量导入脚本",
            Classes = "ghost",
            CommandPath = nameof(ImportScriptFolderCommand),
            ToolTip = "选一个目录，把它里的脚本文件一次性全部建成条目（已存在的会跳过）"
        },
        new()
        {
            Kind = ToolbarItemKind.TextField,
            Width = 88,
            IsReadOnly = true,
            ValuePath = nameof(ScriptsDirectory),
            ToolTipPath = nameof(ScriptHint)
        }
    };

    // 这几个是「脚本目录」框的配套动作 / 选项 ⇒ 排在共用搜索框**之后**（基类 ToolbarTailExtras），
    // 让「📥 批量导入脚本 → 目录框 → 搜索框」三件套在最前面挨着。
    protected override IEnumerable<ToolbarItemDescriptor> ToolbarTailExtras => new ToolbarItemDescriptor[]
    {
        new()
        {
            Text = "📂 选择",
            CommandPath = nameof(PickScriptFolderCommand),
            ToolTip = "选一个目录作为脚本目录（立即生效，写入 app.json）"
        },
        new() { Text = "↗ 打开", CommandPath = nameof(OpenScriptsFolderCommand) },
        new() { Text = "↩ 默认", CommandPath = nameof(ResetScriptFolderCommand), ToolTipPath = nameof(DefaultScriptFolder) },
        new() { Kind = ToolbarItemKind.Toggle, Text = "含子目录", ValuePath = nameof(ImportSubFolders) }
    };

    /// <summary>📂 选择脚本目录（NT-055：原 code-behind 的 Click 搬进 VM，选择器走 DialogHooks）。</summary>

    [RelayCommand]

    private async Task PickScriptFolderAsync()
    {
        var dir = await DialogHooks.PickFolderAsync("选择脚本目录");
        if (!string.IsNullOrWhiteSpace(dir)) ApplyPickedScriptFolder(dir);
    }

    /// <summary>📥 批量导入：选一个目录 → 把里面的脚本文件一次性全建成条目（NT-055：同上搬进 VM）。</summary>

    [RelayCommand]

    private async Task ImportScriptFolderAsync()
    {
        var dir = await DialogHooks.PickFolderAsync("选择要导入的脚本目录");
        if (string.IsNullOrWhiteSpace(dir)) return;
        var (added, skipped) = ImportScriptFolder(dir, ImportSubFolders);
        if (added == 0 && skipped == 0)
            StatusText = $"「{dir}」里没有 {string.Join(" / ", ScriptExtensions)} 这类脚本文件。";
        // 有结果时结论已经写进 StatusText（ImportScriptFolder 内部负责）
    }

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
