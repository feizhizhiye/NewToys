using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.MediaAssets.Models;
using NewToys.MediaAssets.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

// Core 里也有一个 FileOperationService（纯磁盘动作，跨模块共用）。
// 本页用的是**带库表日志**的模块版，显式取别名，免得两个 using 撞名（CS0104）。
using FileOperationService = NewToys.MediaAssets.Services.FileOperationService;

namespace NewToys.MediaAssets.ViewModels;

public partial class AssetAllViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    /// <summary>
    /// 本页详情卡描述符（/ 需求 §4.5.1，**C# 对象字面量**，⛔ 不做运行时 JSON / XAML 加载）。
    /// <para>结构与迁移前的 <c>AssetDetailView</c> 1:1 对应；三组 chips 带**色块**（`ChipBrushPath`）、
    /// 目标是「编辑 + 保存」这套卡内动作。</para>
    /// </summary>
    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        EmptyText = "在左侧列表里选中一个资产，这里显示它的详情。",
        Sections =
        {
            new DetailSection
            {
                Fields =
                {
                    // ===== 顶部：文件名（磁盘上的真名，只读；要改库里的显示名用卡底部的输入框） =====
                    new DetailField
                    {
                        Label = "文件名",
                        ValuePath = "DetailRow.FileName",
                        Kind = FieldKind.Text,
                        Selectable = true,
                        Mono = true
                    },
                    new DetailField { Label = "类型", ValuePath = "DetailRow.TypeText", Kind = FieldKind.Text, Selectable = true },
                    // 状态：**单选下拉**（预设三档；改完点「💾 保存」落库）
                    new DetailField
                    {
                        Label = "状态",
                        Kind = FieldKind.Select,
                        OptionsPath = nameof(StatusChoices),
                        IndexPath = nameof(EditStatusIndex)
                    },
                    new DetailField { Label = "大小", ValuePath = "DetailRow.SizeText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "修改时间", ValuePath = "DetailRow.ModifiedText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "存储源", ValuePath = "DetailRow.StorageName", Kind = FieldKind.Text, Selectable = true },
                    new DetailField
                    {
                        Label = "文件路径",
                        ValuePath = "DetailRow.FilePath",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    // ===== v3.69：视频专属字段（仅筛选类型 = 视频时显示；列同理）=====
                    new DetailField
                    {
                        Label = "时长",
                        ValuePath = "DetailRow.DurationText",
                        Kind = FieldKind.Text,
                        Selectable = true,
                        VisibleWhen = nameof(IsVideoType)
                    },
                    new DetailField
                    {
                        Label = "分辨率",
                        ValuePath = "DetailRow.ResolutionText",
                        Kind = FieldKind.Text,
                        Selectable = true,
                        VisibleWhen = nameof(IsVideoType)
                    },
                    new DetailField
                    {
                        Label = "编码",
                        ValuePath = "DetailRow.CodecText",
                        Kind = FieldKind.Text,
                        Selectable = true,
                        VisibleWhen = nameof(IsVideoType)
                    },
                    // ⛔ 已删：原「重命名 / 移动 / 复制 / 删除磁盘文件在 落地」这句提示 —— 那些操作早已落地，
                    //    提示留着只是噪声（样板：固定项区只留**事实**，不放实现进度）。
                    // ===== 三组 chips（标签 / 分类 / 集合）：点一下打上 / 取消，每项带一个颜色块 =====
                    new DetailField
                    {
                        Label = "标签（点一下打上 / 取消；按命名空间分组）",
                        Kind = FieldKind.Chips,
                        OptionsPath = nameof(TagPicks),
                        ChipTextPath = "Name",
                        ChipBrushPath = "Brush",
                        ChipSelectedPath = "IsSelected",
                        ChipGroupPath = "Group",
                        ChipCommandPath = nameof(ToggleTagCommand)
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "（还没有标签：到「标签管理」页新建，或用工具条的「🏷 加标签」）",
                        VisibleWhen = "!" + nameof(HasTagPicks)
                    },
                    new DetailField
                    {
                        Label = "分类（点一下加 / 去掉）",
                        Kind = FieldKind.Chips,
                        OptionsPath = nameof(CategoryPicks),
                        ChipTextPath = "Name",
                        ChipBrushPath = "Brush",
                        ChipSelectedPath = "IsSelected",
                        ChipCommandPath = nameof(ToggleCategoryCommand)
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "（还没有分类：到「分类管理」页新建）",
                        VisibleWhen = "!" + nameof(HasCategoryPicks)
                    },
                    new DetailField
                    {
                        Label = "集合（点一下加 / 去掉；⚡ 智能集合不参与手工设置）",
                        Kind = FieldKind.Chips,
                        OptionsPath = nameof(CollectionPicks),
                        ChipTextPath = "Name",
                        ChipBrushPath = "Brush",
                        ChipSelectedPath = "IsSelected",
                        ChipCommandPath = nameof(ToggleCollectionCommand)
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "（还没有集合：到「集合管理」页新建）",
                        VisibleWhen = "!" + nameof(HasCollectionPicks)
                    },
                    // ===== 样板顺序：**固定只读项 → 标签 / 分类 / 集合 → 动作按钮组 → 评分 → 描述** =====
                    // ===== 图标动作组（高频动作收成一行，⛔ 不要一排撑满宽度的文字按钮） =====
                    new DetailField
                    {
                        Kind = FieldKind.IconBar,
                        IconActions =
                        {
                            new DetailIconAction
                            {
                                IconKey = "IconStar",
                                ToolTip = "收藏 / 取消收藏（即时写库）",
                                CommandPath = nameof(ToggleFavoriteCommand),
                                IsActivePath = "DetailRow.IsFavorite",
                                IsEnabledPath = nameof(HasSelection)
                            },
                            new DetailIconAction
                            {
                                IconKey = "IconThumbUp",
                                ToolTip = "点赞：评分 +1（0~5，即时写库）",
                                CommandPath = nameof(RateUpCommand),
                                IsEnabledPath = nameof(HasSelection)
                            },
                            new DetailIconAction
                            {
                                IconKey = "IconThumbDown",
                                ToolTip = "点踩：评分 -1（0~5，即时写库）",
                                CommandPath = nameof(RateDownCommand),
                                IsEnabledPath = nameof(HasSelection)
                            },
                            new DetailIconAction { IsSeparator = true },
                            new DetailIconAction
                            {
                                IconKey = "IconSave",
                                ToolTip = "保存：把显示名 / 描述 / 状态 / 标签 / 分类 / 集合写进资产库（⛔ 不动磁盘文件）",
                                CommandPath = nameof(SaveAssetDetailCommand),
                                IsEnabledPath = nameof(HasSelection)
                            },
                            new DetailIconAction
                            {
                                IconKey = "IconZap",
                                ToolTip = "读媒体信息：对这个文件跑一次 ffprobe（视频=时长/分辨率/编码；图片=尺寸），即时写库",
                                CommandPath = nameof(ReadMediaInfoCommand),
                                IsEnabledPath = nameof(HasSelection)
                            },
                            new DetailIconAction
                            {
                                IconKey = "IconRefresh",
                                ToolTip = "刷新：重读资产库（列表 + 详情）",
                                CommandPath = nameof(RefreshCommand),
                                IsEnabledPath = "!" + nameof(IsBusy)
                            },
                            new DetailIconAction
                            {
                                IconKey = "IconTrash",
                                ToolTip = "移入回收站：软删除（只改库里状态，⛔ 不动磁盘文件；回收站页可恢复）",
                                CommandPath = nameof(MoveToTrashCommand),
                                IsEnabledPath = nameof(HasSelection),
                                ConfirmText = "把这条资产移入回收站？\n（只改库里的状态，磁盘文件不动；可在「回收站」页恢复）",
                                IsDanger = true
                            }
                        }
                    },
                    // ===== 评分：一排 1~5 小按钮（点一下**即时落盘**；要 0 分用按钮组的「👎 点踩」） =====
                    new DetailField
                    {
                        Label = "评分",
                        Kind = FieldKind.Chips,
                        OptionsPath = nameof(RatingPicks),
                        ChipTextPath = "Name",
                        ChipSelectedPath = "IsSelected",
                        ChipCommandPath = nameof(SetRatingCommand)
                    },
                    new DetailField
                    {
                        Label = "描述",
                        ValuePath = nameof(EditDescription),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        MultiLine = true,
                        Rows = 52,
                        Placeholder = "随手记点什么（可留空）"
                    },
                    // ⛔ 原「移动到 / 复制到（目标存储源）」整块已删：
                    //    这两个是**低频的重操作**，详情卡样板只留「查看 + 改元数据」，不再摆两个物理量操作入口。
                    new DetailField { Kind = FieldKind.Divider },
                    new DetailField
                    {
                        Label = "关联（字幕 / 封面 / 版本…）",
                        Kind = FieldKind.List,
                        OptionsPath = nameof(Relations),
                        ItemLabelPath = "TypeText",
                        ItemTextPath = "TargetName"
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "（暂无关联；关联的增删在本轮之后落地）",
                        VisibleWhen = "!" + nameof(HasRelations)
                    },
                    // ⛔ 原「文件位置」List 已删：它与上面的「文件路径」是同一份信息（磁盘路径），
                    //    只读展示一次就够（详情卡样板：去重 + 直接读取的信息一律只读）。
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "⚠ 这个文件在磁盘上找不到了（最近一次扫描没见到它）。",
                        VisibleWhen = "DetailRow.IsMissing"
                    },
                    new DetailField { Kind = FieldKind.Divider },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "操作日志（谁在什么时候改了什么）的入口会在这里给出。"
                    },
                    // ===== 卡底部：改名输入框（⛔ 只改库里的显示名；真改磁盘文件名走下面的「应用到文件」） =====
                    new DetailField { Kind = FieldKind.Divider },
                    new DetailField
                    {
                        Label = "显示名（只改库里的名字，⛔ 不动磁盘文件；改完点「💾 保存」）",
                        ValuePath = nameof(DetailDisplayName),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Placeholder = "留空 = 用扫描时得到的标题"
                    }
                }
            }
        },
        Actions =
        {
            // ⛔ 「💾 保存」「🗑 移入回收站」已上提到**顶部图标动作组**，这里不再重复。
            new DetailAction
            {
                Text = "↗ 应用到文件",
                Classes = "ghost",
                ToolTip = "把名字**真写到磁盘文件上**（物理重命名：走任务队列 + 日志先行；同名冲突按「追加序号」，⛔ 不覆盖）",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = ApplyToFileCommand
            },
            // ⛔ 「📁 移动到此源」「📋 复制到此源」已删：物理搬运归任务中心 / 后续入口，
            //    详情卡样板只保留「↗ 应用到文件」这一个**写磁盘**的显式动作。
        }
    };

    public event EventHandler? DetailChanged;

    /// <summary>
    /// 工具条末位的详情开关调这个（⛔ 别绑基类的 <c>ToggleDetailCommand</c> —— 它只翻
    /// <c>IsDetailVisible</c>，**不会**通知 Shell，右列就不会出现）。
    /// </summary>
    [RelayCommand]
    private void ToggleDetailPanel()
    {
        IsDetailVisible = !IsDetailVisible;
        if (IsDetailVisible) EnsureDetailSelection();
        SaveDetailFlag();
        RaiseDetailChanged();
    }

    /// <summary>接口实现：展开详情（Shell 切回本标签、恢复上次开合状态时调）。</summary>
    public void OpenDetail()
    {
        EnsureDetailSelection();
        IsDetailVisible = true;
        SaveDetailFlag();
        RaiseDetailChanged();
    }

    /// <summary>接口实现：开合切换（与 <see cref="ToggleDetailPanel"/> 同一份逻辑）。</summary>
    public void ToggleDetail() => ToggleDetailPanel();

    private void RaiseDetailChanged() => DetailChanged?.Invoke(this, EventArgs.Empty);

    // ==================== 选中项 → 详情 ====================

    partial void OnSelectedChanged(AssetRow? value)
    {
        OnPropertyChanged(nameof(DetailRow));
        LoadDetail(value);
        OnPropertyChanged(nameof(HasSelection));
        RaiseDetailChanged();
    }

    private void LoadDetail(AssetRow? row)
    {
        _loadingDetail = true;
        try
        {
            Locations.Clear();
            var target = row;
            DetailDisplayName = target == null
                ? ""
                : target.DisplayName ?? target.Title ?? target.Name;
            EditRating = target?.Rating ?? 0;
            EditDescription = target?.Description ?? "";
            // 状态下拉：索引 ↔ 状态 key（取不到就落到第 0 项「正常」）
            var si = target == null ? -1 : Array.IndexOf(StatusKeys, target.Status);
            EditStatusIndex = si < 0 ? 0 : si;

            if (target != null && target.Id > 0) BuildPicks(target);

            if (target != null)
                foreach (var l in _assets.ListLocations(target.Id)) Locations.Add(l);
        }
        finally { _loadingDetail = false; }
    }

    /// <summary>
    /// 保存显示名（详情卡改名框**失焦**时由 View 调）：只写 <c>display_name</c>，
    /// ⛔ **绝不动磁盘文件名**（物理改名是显式操作，由文件操作服务负责）。
    /// </summary>
    [RelayCommand]
    private void SaveDisplayName()
    {
        if (_loadingDetail || Selected == null) return;

        var name = (DetailDisplayName ?? "").Trim();
        _assets.SetDisplayName(Selected.Id, name.Length == 0 ? null : name);

        Selected.DisplayName = name.Length == 0 ? null : name;
        Selected.Name = name.Length == 0 ? (Selected.Title ?? $"资产 #{Selected.Id}") : name;

        RefreshView();   // 显示名变了 ⇒ 按当前排序重取（选中项按 Id 认领，⛔ 不跳走）
        StatusText = name.Length == 0
            ? "已清除显示名（回退到标题）。"
            : $"已把显示名改为「{name}」（⛔ 未动磁盘文件）。";
    }

    // ==================== 筛选条下拉的构建 ====================

    private void BuildStaticOptions()
    {
        TypeOptions.Clear();
        foreach (var (label, value) in AssetTypeTexts.Choices) TypeOptions.Add(new FilterOption(label, value));
        FilterType = TypeOptions[0];

        StatusOptions.Clear();
        StatusOptions.Add(new FilterOption("全部状态", ""));
        StatusOptions.Add(new FilterOption("正常", AssetStatuses.Active));
        StatusOptions.Add(new FilterOption("已丢失", AssetStatuses.Missing));
        StatusOptions.Add(new FilterOption("回收站", AssetStatuses.Trashed));
        StatusOptions.Add(new FilterOption("已归档", AssetStatuses.Archived));
        FilterStatus = StatusOptions[0];
    }

    /// <summary>
    /// 重建下拉期间置位：<c>Clear()</c> 会让绑定中的 ComboBox 把 <c>SelectedItem</c> 回写成 null，
    /// 那会顺带触发一次「无筛选」的重取（大库上等于白跑一次 COUNT + 一页查询）。
    /// 只在 <see cref="Reload"/> 里调用，重建完由 Reload 统一取页，所以抑制是安全的。
    /// </summary>
    private bool _rebuildingOptions;

    private void RebuildStorageOptions()
    {
        var keep = FilterStorage?.Value ?? "";
        _rebuildingOptions = true;
        try
        {
            StorageOptions.Clear();
            StorageOptions.Add(new FilterOption("全部存储源", ""));
            foreach (var s in _assets.ListStorages())
                StorageOptions.Add(new FilterOption(s.Name, s.Id.ToString(CultureInfo.InvariantCulture)));
            FilterStorage = StorageOptions.FirstOrDefault(o => o.Value == keep) ?? StorageOptions[0];
        }
        finally { _rebuildingOptions = false; }
    }

    private void RebuildTagOptions()
    {
        var keep = FilterTag?.Value ?? "";
        _rebuildingOptions = true;
        try
        {
            TagOptions.Clear();
            TagOptions.Add(new FilterOption("全部标签", ""));
            // 之前标签表是空的 → 下拉只有「全部标签」，属预期（⛔ 不是 bug）
            foreach (var (id, name) in _assets.ListTagOptions())
                TagOptions.Add(new FilterOption(name, id.ToString(CultureInfo.InvariantCulture)));
            FilterTag = TagOptions.FirstOrDefault(o => o.Value == keep) ?? TagOptions[0];
        }
        finally { _rebuildingOptions = false; }
    }

    private void RebuildTargetStorages()
    {
        TargetStorages.Clear();
        if (!IsDbAvailable) { SelectedTargetStorageId = 0; return; }
        foreach (var s in _assets.ListStorages()) TargetStorages.Add(s);
        SelectedTargetStorageId = TargetStorages.FirstOrDefault(s => s.Online)?.Id
                                  ?? TargetStorages.FirstOrDefault()?.Id ?? 0;
    }

    partial void OnSearchTextChanged(string value)
    {
        if (_booted) ResetToFirstPage();
    }
    partial void OnFilterTypeChanged(FilterOption? value)
    {
        ApplyTypeColumnDefaults();               // v3.69：列集合随类型切换（⛔ 不落盘、不覆盖用户偏好）
        OnPropertyChanged(nameof(IsVideoType));  // v3.69：详情卡字段集随类型切换（VisibleWhen 重解析）
        if (_booted) ResetToFirstPage();         // 筛选变了回第一页（库态 ⇒ 重取一页）
        StatusText = StatusSummary();            // v3.69：状态栏前缀随类型切换
    }
    partial void OnFilterStorageChanged(FilterOption? value)
    {
        if (_booted && !_rebuildingOptions) ResetToFirstPage();
    }
    partial void OnFilterStatusChanged(FilterOption? value)
    {
        if (_booted && !_rebuildingOptions) ResetToFirstPage();
    }
    partial void OnFilterTagChanged(FilterOption? value)
    {
        if (_booted && !_rebuildingOptions) ResetToFirstPage();
    }

    // ==================== 工具条「⋯ 更多」开合（v3.41：用户定调） ====================

    // ==================== 声明式工具条 + 选择器命令（/ 需求 §4.8.5） ====================
    // 🔴 本页工具条由 **Shell 的 ToolbarHost 统一渲染**（折叠 / 「⋯」/ 详情开关浮层都在那边实现），
    //    ⛔ 不建页内 `Border.toolbar`、不做 `ApplyToolbarLayout()` / `IsToolbarExpanded`。
    //    顺序 = 优先级顺序（越靠前越不会被折进「⋯」）。
    // ⚠️ v3.69 起 5 个类型薄壳页已合并⇒ 本工具条只服务「全部资产」一页。

    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new() { Kind = ToolbarItemKind.SplitButton, Text = "📁 扫描", CommandPath = nameof(ScanFolderCommand),
                IsEnabledPath = "!" + nameof(IsBusy), ToolTip = "扫目录 / 扫文件到列表（⛔ 只进内存、不入库）",
                Items = new ToolbarMenuItemDescriptor[]
                {
                    new() { Text = "📁 文件夹…", CommandPath = nameof(ScanFolderCommand), ToolTip = "扫一个目录到列表（⛔ 只进内存、不入库）" },
                    new() { Text = "📄 文件…", CommandPath = nameof(ScanFilesCommand), ToolTip = "只扫选中的几个文件（⛔ 只进内存、不入库）" },
                } },
        new() { Kind = ToolbarItemKind.TextField, Width = 88, Placeholder = "路径（回车预览）", ValuePath = nameof(ImportPath),
                EnterCommandPath = nameof(PreviewPathCommand), ToolTip = "填文件夹路径，回车即预览扫描（也可用左侧「📁 扫描」选目录）" },
        new() { Kind = ToolbarItemKind.TextField, Width = 88, Placeholder = "搜索…", ValuePath = nameof(SearchText), ToolTip = "按名称 / 路径 / 标签搜索（空格分词，多个词要同时命中）" },
        new() { Text = "➕ 登记", CommandPath = nameof(RegisterFolderCommand), IsEnabledPath = "!" + nameof(IsBusy),
                ToolTip = "加入待扫文件夹列表（⛔ 不扫描），攒几个后点「🔄 扫描全部」" },
        new() { Text = "🔄 扫描全部", CommandPath = nameof(ScanAllCommand), IsEnabledPath = "!" + nameof(IsBusy),
                ToolTip = "把所有已登记的存储源扫一遍，直接入库" },
        new() { Text = "🏷 加标签", CommandPath = nameof(AddTagToCheckedCommand), IsEnabledPath = nameof(HasSelectedRows), ShowToolTipOnDisabled = true,
                ToolTip = "先选中行（Ctrl 增选 / Shift 连选 / Ctrl+A 全选），再点这里打标签（输入标签名，不存在就新建）" },
        new() { Text = "📚 加集合", CommandPath = nameof(AddToCollectionCheckedCommand), IsEnabledPath = nameof(HasSelectedRows), ShowToolTipOnDisabled = true,
                ToolTip = "先选中行（Ctrl 增选 / Shift 连选 / Ctrl+A 全选），再点这里加进集合（输入集合名，不存在就新建一个手动集合）" },
        new() { Text = "🗑 移入回收站", CommandPath = nameof(TrashCheckedCommand), IsEnabledPath = nameof(HasSelectedRows), ShowToolTipOnDisabled = true,
                ToolTip = "先选中行，再点这里移入回收站（只改库状态，磁盘文件不动；可恢复）" },
        new() { Text = "⤫ 从库移除", CommandPath = nameof(RemoveCheckedFromLibraryCommand), IsEnabledPath = nameof(HasSelectedRows), ShowToolTipOnDisabled = true,
                ToolTip = "选中后点这里：只删库存记录（含文件位置），⛔ 磁盘文件一个都不动 —— 清理重复行的正确手段" },
        new() { Text = "📊 读媒体信息", CommandPath = nameof(ReadMediaInfoCheckedCommand), IsEnabledPath = nameof(HasSelectedRows), ShowToolTipOnDisabled = true,
                ToolTip = "选中后点这里：给还没读过的视频 / 图片跑 ffprobe 补时长 / 分辨率 / 编码（走任务队列，可取消）" },
        new() { Text = "🔍 查重合并", CommandPath = nameof(MergeDuplicateAssetsCommand), IsEnabledPath = nameof(IsDbAvailable),
                ToolTip = "把「同一个物理文件被多条资产重复收录」的组并成一条（标签 / 评分 / 关联都保住；⛔ 磁盘文件不动）" },
        new() { Text = "💾 加入库", CommandPath = nameof(CommitDraftCommand), IsEnabledPath = nameof(IsDraft),
                ToolTip = "把当前预览结果写入资产库（⛔ 不动磁盘文件）" },
        new() { Text = "🧹 清除列表", CommandPath = nameof(ClearListCommand),
                ToolTip = "草稿态：丢弃预览结果；库视图：清空当前显示（数据仍在库里，刷新即找回）" },
        new() { Kind = ToolbarItemKind.Toggle, Text = "含子目录", ValuePath = nameof(Recursive), ToolTip = "关掉就只扫当前这一层" },
        new() { Text = "🔄 重读", CommandPath = nameof(RefreshCommand), IsEnabledPath = "!" + nameof(IsBusy),
                ToolTip = "重新读取资产库（⛔ 不扫描磁盘）" },
        new() { Kind = ToolbarItemKind.SplitButton, Text = "🗄 并入…", CommandPath = nameof(MergeDbCommand), IsEnabledPath = nameof(IsDbAvailable),
                ToolTip = "把另一个资产库（*.db）合并进当前库（按路径/内容去重；源库只读；进度看任务中心）",
                Items = new ToolbarMenuItemDescriptor[]
                {
                    new() { Text = "📂 打开库所在文件夹", CommandPath = nameof(OpenDbFileCommand), ToolTipPath = nameof(DatabasePath),
                            ToolTip = "在资源管理器打开库所在目录（路径形式；⛔ 不直接开 .db 文件，避免没装查看工具时打不开）" },
                } },
        new() { Text = "💿 另存为…", CommandPath = nameof(PickSaveLibraryAsCommand), IsEnabledPath = nameof(IsDbAvailable),
                ToolTip = "把当前资产库另存成一个 .db（备份 / 交付用）" },
        new() { Text = "📤 导出", CommandPath = nameof(ExportAssetsCommand),
                ToolTip = "导出当前列表：.json = 列表（含标签 / 分类 / 集合名字）；.db = 整个资产库另存（备份 / 交付）" },
        new() { Text = "📥 导入", CommandPath = nameof(ImportAssetsCommand),
                ToolTip = "把 json 列表 / 资产库（*.db）加载到列表**预览**（⛔ 不写库）；看完点「💾 加入库」才写入" },
        new() { Text = "🧠 存为智能集合", IsEnabled = false, ToolTip = "智能集合需要规则求值（未实装）" },
    };

}
