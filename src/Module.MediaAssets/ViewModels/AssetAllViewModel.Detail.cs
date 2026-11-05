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
            // ===== 节 1：基本信息 —— 短且固定的事实用行内「标签：值」；文件名可编辑（应用到文件才落盘） =====
            new DetailSection
            {
                Fields =
                {
                    // 文件名：框内只放主名，扩展名以只读后缀贴在框右；点卡底「应用到文件」才真改磁盘。
                    new DetailField
                    {
                        Label = "文件名",
                        ValuePath = nameof(EditFileName),
                        SuffixPath = nameof(EditFileExtension),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true
                    },
                    new DetailField { Label = "类型", ValuePath = "DetailRow.TypeText", Kind = FieldKind.Text, Selectable = true, Inline = true },
                    new DetailField { Label = "大小", ValuePath = "DetailRow.SizeText", Kind = FieldKind.Text, Selectable = true, Inline = true },
                    new DetailField { Label = "修改时间", ValuePath = "DetailRow.ModifiedText", Kind = FieldKind.Text, Selectable = true, Inline = true },
                    new DetailField { Label = "存储源", ValuePath = "DetailRow.StorageName", Kind = FieldKind.Text, Selectable = true, Inline = true },
                    // 状态：**单选下拉**（预设三档；改完点「保存」落库）—— 行内「状态：[下拉]」
                    new DetailField
                    {
                        Label = "状态",
                        Kind = FieldKind.Select,
                        OptionsPath = nameof(StatusChoices),
                        IndexPath = nameof(EditStatusIndex),
                        Inline = true
                    },
                    new DetailField { Label = "文件路径", ValuePath = "DetailRow.FilePath", Kind = FieldKind.Text, Selectable = true },
                    // ===== 视频专属字段（仅筛选类型 = 视频时显示）=====
                    new DetailField
                    {
                        Label = "时长",
                        ValuePath = "DetailRow.DurationText",
                        Kind = FieldKind.Text,
                        Selectable = true,
                        Inline = true,
                        VisibleWhen = nameof(IsVideoType)
                    },
                    new DetailField
                    {
                        Label = "分辨率",
                        ValuePath = "DetailRow.ResolutionText",
                        Kind = FieldKind.Text,
                        Selectable = true,
                        Inline = true,
                        VisibleWhen = nameof(IsVideoType)
                    },
                    new DetailField
                    {
                        Label = "编码",
                        ValuePath = "DetailRow.CodecText",
                        Kind = FieldKind.Text,
                        Selectable = true,
                        Inline = true,
                        VisibleWhen = nameof(IsVideoType)
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "⚠ 这个文件在磁盘上找不到了（最近一次扫描没见到它）。",
                        VisibleWhen = "DetailRow.IsMissing"
                    },
                    // ===== 三组 chips（标签 / 分类 / 集合）：点一下打上 / 取消（编辑态，「保存」落库） =====
                    new DetailField
                    {
                        Label = "标签",
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
                        Hint = "（还没有标签：到「标签管理」页新建，或右键「加标签…」）",
                        VisibleWhen = "!" + nameof(HasTagPicks)
                    },
                    new DetailField
                    {
                        Label = "分类",
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
                        Label = "集合",
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
                    // ===== 图标动作组（高频动作收成一行；收藏 / 评分即时落库，保存 = 卡内编辑统一落库） =====
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
                                ToolTip = "保存：把描述 / 状态 / 标签 / 分类 / 集合写进资产库（⛔ 不动磁盘文件）",
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
                    // ===== 评分：一排 1~5 小按钮（点一下即时落库；要 0 分用图标组的「点踩」） =====
                    new DetailField
                    {
                        Label = "评分",
                        Kind = FieldKind.Chips,
                        OptionsPath = nameof(RatingPicks),
                        ChipTextPath = "Name",
                        ChipSelectedPath = "IsSelected",
                        ChipCommandPath = nameof(SetRatingCommand)
                    }
                }
            },
            // ===== 节 2「更多」：可折叠（开合落盘记住）—— 音乐标签字段 / 描述 / 关联 =====
            new DetailSection
            {
                Header = "更多",
                IsExpandedPath = nameof(IsMetaSectionExpanded),
                Fields =
                {
                    // ===== 音乐专属字段（行内编辑）：改完点卡底「🎵 写入标签」才落盘 =====
                    new DetailField { Label = "标题", ValuePath = nameof(EditMusicTitle), Kind = FieldKind.Text, Inline = true, Commit = EditCommitMode.OnBlur, VisibleWhen = nameof(IsMusicType) },
                    new DetailField { Label = "艺术家", ValuePath = nameof(EditMusicArtist), Kind = FieldKind.Text, Inline = true, Commit = EditCommitMode.OnBlur, VisibleWhen = nameof(IsMusicType) },
                    new DetailField { Label = "专辑", ValuePath = nameof(EditMusicAlbum), Kind = FieldKind.Text, Inline = true, Commit = EditCommitMode.OnBlur, VisibleWhen = nameof(IsMusicType) },
                    new DetailField { Label = "年份", ValuePath = nameof(EditMusicYear), Kind = FieldKind.Text, Inline = true, Commit = EditCommitMode.OnBlur, VisibleWhen = nameof(IsMusicType) },
                    new DetailField { Label = "流派", ValuePath = nameof(EditMusicGenre), Kind = FieldKind.Text, Inline = true, Commit = EditCommitMode.OnBlur, VisibleWhen = nameof(IsMusicType) },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        ValuePath = nameof(MusicStatusText),
                        VisibleWhen = nameof(IsMusicType)
                    },
                    // ===== 封面 / 歌词文件感知（只读）：同目录找 cover/folder/front 图片、同名 .lrc =====
                    new DetailField { Label = "封面文件", ValuePath = nameof(MusicCoverInfo), Kind = FieldKind.Text, Selectable = true, Inline = true, VisibleWhen = nameof(IsMusicType) },
                    new DetailField { Label = "歌词文件", ValuePath = nameof(MusicLyricsInfo), Kind = FieldKind.Text, Selectable = true, Inline = true, VisibleWhen = nameof(IsMusicType) },
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
                        Hint = "（暂无关联）",
                        VisibleWhen = "!" + nameof(HasRelations)
                    }
                }
            }
        },
        Actions =
        {
            // 卡内动作统一 ghost + 图标（⛔ 文案不带 emoji）；批量口径：多选时出现「加标签 / 加集合」
            new DetailAction
            {
                Text = "加标签", IconKey = "IconPlus", Classes = "ghost",
                ToolTip = "给选中的多个资产一起加标签（多选弹层：可搜索 / 多选 / 新建；只加不减）",
                IsVisiblePath = nameof(HasManySelected),
                Command = AddTagToCheckedCommand
            },
            new DetailAction
            {
                Text = "加集合", IconKey = "IconCopy", Classes = "ghost",
                ToolTip = "把选中的多个资产一起加进集合（智能集合不能手工加）",
                IsVisiblePath = nameof(HasManySelected),
                Command = AddToCollectionCheckedCommand
            },
            new DetailAction
            {
                Text = "应用到文件", IconKey = "IconEdit", Classes = "ghost",
                ToolTip = "把详情卡「文件名」里的新名字**真写到磁盘文件上**（物理重命名：任务队列 + 日志先行；同名冲突按「追加序号」，⛔ 不覆盖）",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = ApplyToFileCommand
            },
            new DetailAction
            {
                Text = "联网补全", IconKey = "IconWeb", Classes = "ghost",
                ToolTip = "按标题 + 艺术家查 MusicBrainz，取回专辑 / 年份 / 流派 / 封面并填进编辑框（此时还没写文件）",
                IsVisiblePath = nameof(IsMusicType),
                Command = FetchMusicOnlineCommand
            },
            new DetailAction
            {
                Text = "写入标签", IconKey = "IconSave", Classes = "ghost",
                ToolTip = "把音乐标签（含封面）写进文件的 ID3 / FLAC / MP4 标签（⛔ 不动音频数据）",
                IsVisiblePath = nameof(IsMusicType),
                Command = SaveMusicTagsCommand
            },
        }
    };

    public event EventHandler? DetailChanged;

    /// <summary>「更多」节的展开态（开合落盘，重启后恢复上次的开合）。</summary>
    [ObservableProperty] private bool _isMetaSectionExpanded;

    partial void OnIsMetaSectionExpandedChanged(bool value)
        => _layoutCols?.SaveFlag(PageKey, "metaSection", value);

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
        StatusText = IsDetailVisible
            ? (Selected == null ? "详情卡已展开（还没选中资产）。" : $"详情卡已展开：{Selected.Name}")
            : "详情卡已收起。";
    }

    /// <summary>接口实现：展开详情（Shell 切回本标签、恢复上次开合状态时调）。</summary>
    public void OpenDetail()
    {
        EnsureDetailSelection();
        IsDetailVisible = true;
        SaveDetailFlag();
        RaiseDetailChanged();
        StatusText = Selected == null ? "详情卡已展开（还没选中资产）。" : $"详情卡已展开：{Selected.Name}";
    }

    /// <summary>接口实现：开合切换（与 <see cref="ToggleDetailPanel"/> 同一份逻辑）。</summary>
    public void ToggleDetail() => ToggleDetailPanel();

    private void RaiseDetailChanged() => DetailChanged?.Invoke(this, EventArgs.Empty);

    // ==================== 选中项 → 详情 ====================

    partial void OnSelectedChanged(AssetRow? value)
    {
        OnPropertyChanged(nameof(DetailRow));
        LoadDetail(value);
        LoadMusicTags();
        OnPropertyChanged(nameof(HasMusicSelection));
        OnPropertyChanged(nameof(IsMusicType));
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
            var fileName = target?.FileName ?? "";
            EditFileName = Path.GetFileNameWithoutExtension(fileName);
            EditFileExtension = Path.GetExtension(fileName);
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
        var typeKey = string.IsNullOrEmpty(FilterType?.Value) ? null : FilterType!.Value;
        _rebuildingOptions = true;
        try
        {
            StorageOptions.Clear();
            StorageOptions.Add(new FilterOption("全部存储源", ""));
            // 只列**当前类型下真的有资产**的存储源（选了「音乐」却列出只有视频的盘 ⇒ 选了必然空）
            var storages = typeKey == null
                ? _assets.ListStorages().Select(s => (s.Id, s.Name)).ToList()
                : _assets.ListStorageOptionsForType(typeKey);
            foreach (var (id, name) in storages)
                StorageOptions.Add(new FilterOption(name, id.ToString(CultureInfo.InvariantCulture)));
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
            // 有类型筛选时只列**该类型下被用到**的标签（否则选了必然空）
            var typeKey = string.IsNullOrEmpty(FilterType?.Value) ? null : FilterType!.Value;
            var tags = typeKey == null ? _assets.ListTagOptions() : _assets.ListTagOptionsForType(typeKey);
            foreach (var (id, name) in tags)
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
        OnPropertyChanged(nameof(IsMusicType));
        // 存储源 / 标签两个下拉跟着类型收窄（只留有该类型资产的），否则选了必然空
        if (_booted)
        {
            RebuildStorageOptions();
            RebuildTagOptions();
        }
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
        new() { Kind = ToolbarItemKind.SplitButton, Text = "扫描", IconKey = "IconOpen", CommandPath = nameof(ScanFolderCommand),
                IsEnabledPath = "!" + nameof(IsBusy), ToolTip = "扫目录 / 扫文件到列表（⛔ 只进内存、不入库）",
                Items = new ToolbarMenuItemDescriptor[]
                {
                    new() { Text = "文件夹…", IconKey = "IconOpen", CommandPath = nameof(ScanFolderCommand), ToolTip = "扫一个目录到列表（⛔ 只进内存、不入库）" },
                    new() { Text = "📄 文件…", CommandPath = nameof(ScanFilesCommand), ToolTip = "只扫选中的几个文件（⛔ 只进内存、不入库）" },
                } },
        new() { Kind = ToolbarItemKind.TextField, Width = 88, Placeholder = "路径（回车预览）", ValuePath = nameof(ImportPath),
                EnterCommandPath = nameof(PreviewPathCommand), ToolTip = "填文件夹路径，回车即预览扫描（也可用左侧「📁 扫描」选目录）" },
        new() { Kind = ToolbarItemKind.TextField, Width = 88, Placeholder = "搜索…", ValuePath = nameof(SearchText), ToolTip = "按名称 / 路径 / 标签搜索（空格分词，多个词要同时命中）" },
        new() { Text = "＋ 登记", CommandPath = nameof(RegisterFolderCommand), IsEnabledPath = "!" + nameof(IsBusy),
                ToolTip = "加入待扫文件夹列表（⛔ 不扫描），攒几个后点「扫描全部」" },
        new() { Text = "扫描", IconKey = "IconRefresh", CommandPath = nameof(ScanAllCommand), IsEnabledPath = "!" + nameof(IsBusy),
                ToolTip = "把所有已登记的存储源扫一遍，直接入库" },
        new() { Text = "🧪 校验", CommandPath = nameof(VerifyFilesCommand), IsEnabledPath = "!" + nameof(IsBusy),
                ToolTip = "逐条查磁盘：文件已不在的标为「已丢失」，回来的恢复「正常」（⛔ 不改磁盘、不删记录）" },
        new() { Kind = ToolbarItemKind.SplitButton, Text = "播放", IconKey = "IconPlay", CommandPath = nameof(PlaySelectedInPlayerCommand),
                IsEnabledPath = nameof(HasSelectedRows), ShowToolTipOnDisabled = true,
                ToolTip = "把选中的音乐交给「查看器 → 音乐播放」并立即开播（顺序 = 当前列表顺序）",
                Items = new ToolbarMenuItemDescriptor[]
                {
                    new() { Text = "按序播放", IconKey = "IconPlay", CommandPath = nameof(PlaySelectedInPlayerCommand), ToolTip = "在「音乐播放」里按当前列表顺序播" },
                    new() { Text = "🎵 导出列表", CommandPath = nameof(ExportPlaylistCommand), ToolTip = "写一份 m3u8（默认相对路径），第三方播放器也能吃" },
                } },
        new() { Text = "移回收站", IconKey = "IconTrash", CommandPath = nameof(TrashCheckedCommand), IsEnabledPath = nameof(HasSelectedRows), ShowToolTipOnDisabled = true,
                ToolTip = "先选中行，再点这里移入回收站（只改库状态，磁盘文件不动；可恢复）" },
        new() { Text = "移除", IconKey = "IconTrash", CommandPath = nameof(RemoveCheckedFromLibraryCommand), IsEnabledPath = nameof(HasSelectedRows), ShowToolTipOnDisabled = true,
                ToolTip = "选中后点这里：只删库存记录（含文件位置），⛔ 磁盘文件一个都不动 —— 清理重复行的正确手段" },
        new() { Text = "📄 读媒体信息", CommandPath = nameof(ReadMediaInfoCheckedCommand), IsEnabledPath = nameof(HasSelectedRows), ShowToolTipOnDisabled = true,
                ToolTip = "选中后点这里：给还没读过的视频 / 图片跑 ffprobe 补时长 / 分辨率 / 编码（走任务队列，可取消）" },
        new() { Text = "重命名", IconKey = "IconEdit", CommandPath = nameof(BatchRenameSelectedCommand), IsEnabledPath = nameof(CanBatchRename), ShowToolTipOnDisabled = true,
                ToolTip = "选中 ≥2 个后点这里：按规则（查找替换 / 前缀后缀 / 编号 / 大小写）批量改磁盘文件名，先看预览再确认" },
        new() { Text = "查重", IconKey = "IconSearch", CommandPath = nameof(MergeDuplicateAssetsCommand), IsEnabledPath = nameof(IsDbAvailable),
                ToolTip = "把「同一个物理文件被多条资产重复收录」的组并成一条（标签 / 评分 / 关联都保住；⛔ 磁盘文件不动）" },
        new() { Text = "加入库", IconKey = "IconSave", CommandPath = nameof(CommitDraftCommand), IsEnabledPath = nameof(IsDraft),
                ToolTip = "把当前预览结果写入资产库（⛔ 不动磁盘文件）" },
        new() { Text = "清除列表", IconKey = "IconEraser", CommandPath = nameof(ClearListCommand),
                ToolTip = "草稿态：丢弃预览结果；库视图：清空当前显示（数据仍在库里，刷新即找回）" },
        new() { Kind = ToolbarItemKind.Toggle, Text = "含子目录", ValuePath = nameof(Recursive), ToolTip = "关掉就只扫当前这一层" },
        new() { Text = "重读", IconKey = "IconRefresh", CommandPath = nameof(RefreshCommand), IsEnabledPath = "!" + nameof(IsBusy),
                ToolTip = "重新读取资产库（⛔ 不扫描磁盘）" },
        new() { Kind = ToolbarItemKind.SplitButton, Text = "并入", IconKey = "IconOpen", CommandPath = nameof(MergeDbCommand), IsEnabledPath = nameof(IsDbAvailable),
                ToolTip = "把另一个资产库（*.db）合并进当前库（按路径/内容去重；源库只读；进度看任务中心）",
                Items = new ToolbarMenuItemDescriptor[]
                {
                    new() { Text = "库目录", IconKey = "IconOpen", CommandPath = nameof(OpenDbFileCommand), ToolTipPath = nameof(DatabasePath),
                            ToolTip = "在资源管理器打开库所在目录（路径形式；⛔ 不直接开 .db 文件，避免没装查看工具时打不开）" },
                } },
        new() { Text = "另存为…", IconKey = "IconSave", CommandPath = nameof(PickSaveLibraryAsCommand), IsEnabledPath = nameof(IsDbAvailable),
                ToolTip = "把当前资产库另存成一个 .db（备份 / 交付用）" },
        new() { Text = "导出", IconKey = "IconExport", CommandPath = nameof(ExportAssetsCommand),
                ToolTip = "导出当前列表：.json = 列表（含标签 / 分类 / 集合名字）；.db = 整个资产库另存（备份 / 交付）" },
        new() { Text = "导入", IconKey = "IconImport", CommandPath = nameof(ImportAssetsCommand),
                ToolTip = "把 json 列表 / 资产库（*.db）加载到列表**预览**（⛔ 不写库）；看完点「加入库」才写入" },
        new() { Text = "📌 存为集合", IsEnabled = false, ToolTip = "智能集合需要规则求值（未实装）" },
    };

}
