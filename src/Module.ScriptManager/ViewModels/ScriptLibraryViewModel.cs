using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
namespace NewToys.ScriptManager.ViewModels;
/// <summary>
/// 「脚本管理器 → 脚本库」页：一张<b>脚本文件</b>清单（规范的列表页布局，见 3.6）。
/// <para>
/// 页面逻辑（列表 / 排序 / 列显隐 / 运行 / 增删改查 / 收藏）全部在基类 <see cref="QuickActionListViewModel"/> 里，
/// 本类只声明「显示哪一批条目」（<see cref="QuickActionPage.Shortcuts"/>）与「列显隐落盘键」。
/// </para>
/// <para>
/// 与「命令库」页的分工（v1.33）：这里放<b>有脚本文件的脚本</b>（<see cref="QuickActionKind.Script"/>，
/// 指向脚本目录下的 .py / .ps1 / .bat，解释器路径在「设置 → 路径配置」配）；
/// 一行的<b>命令行命令</b>放「命令库」页。
/// </para>
/// </summary>

public partial class ScriptLibraryViewModel : QuickActionListViewModel
{

    protected override string PageKind => QuickActionPage.Shortcuts;

    protected override string ColumnLayoutKey => "script-library";

    protected override string PageDisplayName => "脚本库";

    // ==================== 工具条（NT-055 / 需求 §4.8.5） ====================

    protected override string ExportSuggestedName => "pc-scripts-export.json";

    /// <summary>
    /// v3.63：本页最前已经有了「📂 导入」（脚本文件）⇒ 共用的「📥 导入」（json 清单）退到「📤 导出」前面，
    /// ⛔ 不让两个导入挨在一起。
    /// </summary>
    protected override bool JsonImportFirst => false;

    /// <summary>
    /// 本页专属的工具条项（插在搜索框之前）：「📂 导入」（一个按钮 + 下拉：**导入文件 / 导入文件夹**两路）
    /// + 脚本目录框（**v3.64 / NT-061 起可编辑**：回车 / 失焦都提交，交模块设置服务落盘；
    /// 唯一写入点仍是「设置 → 脚本管理器设置」那个模块设置页）。
    /// </summary>

    protected override IEnumerable<ToolbarItemDescriptor> ToolbarExtras => new ToolbarItemDescriptor[]
    {
        new()
        {
            // 与视频列表「📂 导入」**同一口径**：SplitButton（主钮 = 选文件夹，右侧箭头 = 两路菜单）。
            Kind = ToolbarItemKind.SplitButton,
            Text = "📂 导入",   // v3.63：与「📥 导入」（json 清单，排在导出前面）靠图标 + 悬停提示区分
            Classes = "ghost",
            CommandPath = nameof(ImportScriptFolderCommand),
            ToolTip = "把脚本文件建成条目：直接点 = 选文件夹（含子目录），右侧箭头 = 选文件夹 / 选文件；已存在的会跳过",
            Items = new[]
            {
                new ToolbarMenuItemDescriptor
                {
                    Text = "📁 文件夹…",
                    CommandPath = nameof(ImportScriptFolderCommand),
                    ToolTip = "选一个目录，把它里的脚本全部建成条目"
                },
                new ToolbarMenuItemDescriptor
                {
                    Text = "📄 文件…",
                    CommandPath = nameof(ImportScriptFilesCommand),
                    ToolTip = "一次选一个或多个 .py / .ps1 / .bat / .cmd 文件"
                }
            }
        },
        new()
        {
            Kind = ToolbarItemKind.TextField,
            Width = 88,
            // v3.64 / NT-061：**可编辑** —— 写的是草稿，回车 / 失焦由 CommitScriptsFolderCommand 校验 + 落盘。
            Placeholder = "脚本目录",
            ValuePath = nameof(ScriptsFolderDraft),
            EnterCommandPath = nameof(CommitScriptsFolderCommand),
            ToolTipPath = nameof(ScriptHint)
        }
    };

    // 「含子目录」只跟「导入文件夹」这一路有关 ⇒ 排在共用搜索框之后（基类 ToolbarTailExtras）。
    protected override IEnumerable<ToolbarItemDescriptor> ToolbarTailExtras => new ToolbarItemDescriptor[]
    {
        new()
        {
            Kind = ToolbarItemKind.Toggle,
            Text = "含子目录",
            ValuePath = nameof(ImportSubFolders),
            ToolTip = "勾上 = 导入文件夹时连子目录里的脚本一起收（对「导入文件」无效）"
        }
    };

    /// <summary>
    /// 📄 导入文件：一次选一个或多个脚本文件。
    /// <para>🔴 起始目录 = **配置的脚本目录**（<see cref="ScriptsDirectory"/>）—— 路径一律按配置走。</para>
    /// </summary>

    [RelayCommand]

    private async Task ImportScriptFilesAsync()
    {
        var files = await DialogHooks.PickFilesAsync("选择要导入的脚本文件", ScriptsDirectory, true, ScriptFilePattern);
        if (files == null || files.Count == 0) return;
        var (added, skipped) = ImportScriptFiles(files);
        if (added == 0 && skipped == 0)
            StatusText = $"选中的文件里没有 {string.Join(" / ", ScriptExtensions)} 这类脚本。";
        // 有结果时结论已经写进 StatusText（ImportScriptFiles 内部负责）
    }

    /// <summary>
    /// 📁 导入文件夹：选一个目录 → 把里面的脚本文件全建成条目。
    /// <para>🔴 起始目录 = **配置的脚本目录**（<see cref="ScriptsDirectory"/>）—— 路径一律按配置走。</para>
    /// </summary>

    [RelayCommand]

    private async Task ImportScriptFolderAsync()
    {
        var dir = await DialogHooks.PickFolderAsync("选择要导入的脚本目录", ScriptsDirectory);
        if (string.IsNullOrWhiteSpace(dir)) return;
        var (added, skipped) = ImportScriptFolder(dir, ImportSubFolders);
        if (added == 0 && skipped == 0)
            StatusText = $"「{dir}」里没有 {string.Join(" / ", ScriptExtensions)} 这类脚本文件。";
        // 有结果时结论已经写进 StatusText（ImportScriptFiles 内部负责）
    }

    /// <summary>
    /// 🔥 **从磁盘删除**（列表右键）：先删脚本文件本身，再把这条从清单里移除。
    /// <para>
    /// 与详情卡图标组的「🗑」分工：**图标组只动清单（⛔ 不碰磁盘）**；右键这一路是**真删文件**，不可撤销。
    /// 本页本质是**脚本索引库**，操作逻辑与资产库一致 ⇒ 破坏性动作放右键、放二次确认。
    /// </para>
    /// </summary>

    [RelayCommand]

    private async Task DeleteFromDiskAsync()
    {
        var item = Selected;
        if (item == null || !HasService) return;

        var path = ResolveScriptPath(item.ScriptFile);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            StatusText = $"「{item.Title}」的脚本文件不在磁盘上（{item.ScriptFile}）⇒ 只把这条从列表移除。";
            RemoveFromList(item);
            return;
        }

        // 二次确认**只在这里弹一次**（service 走 RemoveSilent，⛔ 否则会问两遍、且第二遍取消时文件已删）
        var ok = await DialogHooks.ConfirmAsync(
            $"确定从磁盘删除脚本文件「{Path.GetFileName(path)}」吗？\n{path}\n\n此操作不可撤销，删除后这条也会从清单里移除。");
        if (!ok) { StatusText = "已取消，文件没动。"; return; }

        try { File.Delete(path); }
        catch (Exception ex) { StatusText = "删除文件失败：" + ex.Message; return; }

        RemoveFromList(item);
        StatusText = $"已从磁盘删除：{Path.GetFileName(path)}（清单里的这条也一并移除）";
    }

    /// <summary>本页详情卡（NT-054 / 需求 §4.5.1）：类型 = 脚本后缀 + 「脚本文件 / 解释器」两套字段。</summary>

    protected override DetailCardDescriptor BuildCard()
    {
        var card = new DetailCardDescriptor
        {
            BodyVisiblePath = nameof(ShowDetailBody),
            EmptyText = "在左侧列表里选中一条，可在这里看它的脚本文件 / 解释器并改名称 / 简介 / 分类；下面的图标按钮依次是「运行 / 收藏 / 保存 / 从列表移除」。本页是**脚本索引库**：新增条目靠工具条「📂 导入」，⛔ 不在这里新建脚本。",
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
                        // v3.63 改名：这一列（设置 / 脚本 / 定时任务）叫**功能**；「分类」给了下面的后缀/宿主列
                        new DetailField
                        {
                            Label = "功能（点一下打上 / 再点取消）",
                            Kind = FieldKind.Chips,
                            OptionsPath = nameof(CategoryPicks),
                            ChipTextPath = nameof(CategoryPick.Name),
                            ChipSelectedPath = nameof(CategoryPick.IsSelected),
                            ChipCommandPath = nameof(ToggleCategoryCommand)
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
                        // v3.63：「分类」= 脚本后缀（.py / .ps1 / .bat / .cmd），只读（解释器按后缀自动挑）；
                        //        值与表头「分类」列同源（ClassifyText ⇒ QuickAction.Classify）
                        new DetailField
                        {
                            Label = "分类",
                            ValuePath = nameof(ClassifyText),
                            Kind = FieldKind.Text,
                            Selectable = true,
                            Mono = true
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
                        BuildActionBar(),
                        new DetailField
                        {
                            Kind = FieldKind.Hint,
                            ValuePath = nameof(DataFileHint)
                        }
                    }
                }
            }
        };
        // 🔴 NT-059：卡内动作不再用一排文字按钮 —— 运行 / 收藏 / 保存 / 删除合成字段区末尾的**图标动作组**；
        //    「📂 选脚本文件」按钮删除（脚本文件字段手写相对文件名即可，批量添加走工具条「📥 导入脚本」）。
        return card;
    }

    /// <summary>设计期 / 无 DI 兜底构造。</summary>

    public ScriptLibraryViewModel() { }

    public ScriptLibraryViewModel(IQuickActionService svc, IColumnLayoutService cols, IAppConfigService app,
        IScriptManagerSettingsService pc)
        : base(svc, cols, app, pc) { }
}
