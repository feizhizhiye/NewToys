using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.VideoTools.Models;
using NewToys.VideoTools.Services;

namespace NewToys.VideoTools.ViewModels;

public partial class VideoBatchViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    public VideoBatchViewModel() { }

    public VideoBatchViewModel(VideoTaskStore store, IAppConfigService cfg, IColumnLayoutService cols, INavigationService nav)
        : base(cols)
    {
        _store = store;
        _cfg = cfg;
        _nav = nav;
        LoadLayout();
        // §4.8.2：类型（剪辑 / 转码 / 剪辑+转码，与 VideoTask.KindDisplay 同字面量）进表头右键「筛选」
        SetQuickFilters(new[]
        {
            (Label: "类型：剪辑", ColumnKey: "Kind", Value: "剪辑"),
            (Label: "类型：转码", ColumnKey: "Kind", Value: "转码"),
            (Label: "类型：剪辑+转码", ColumnKey: "Kind", Value: "剪辑+转码")
        });
        RefreshView();
        FillEditors(DetailTask);
    }

    /// <summary>
    /// 进入本页时把「视频列表」页送过来的待处理文件建成任务（两类都收）。
    /// ⚠️ 必须在 VM 构造后由 View 调一次（构造函数里做会碰到"UI 还没 ready"）。
    /// </summary>
    public async Task ConsumePendingAsync()
    {
        if (_store == null) return;

        var transcode = _store.TakePendingTranscode();
        var cut = _store.TakePendingCut();
        if (transcode.Count == 0 && cut.Count == 0) return;

        if (cut.Count > 0) await AddCutFilesAsync(cut);
        if (transcode.Count > 0) await AddTranscodeFilesAsync(transcode);

        RefreshView();
    }

    protected override void RefreshView()
    {
        // 🔴 先记下选中项：Items.Clear() 会让 ListBox 把 SelectedItem 回写 null，
        //    不补回来的话详情卡会掉回「未选中」空态（用户看到：一点保存，卡片就空白，得再点一次列表）。
        var keep = Selected;

        IEnumerable<VideoTaskBase> q = ItemsRaw;

        var text = (SearchText ?? "").Trim();
        if (text.Length > 0)
            q = q.Where(t => t.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                          || (t.OutputPath ?? "").Contains(text, StringComparison.CurrentCultureIgnoreCase));

        q = ApplyFilters(q);

        var byText = StringComparer.CurrentCultureIgnoreCase;
        q = SortColumn switch
        {
            "Kind" => SortDescending ? q.OrderByDescending(KindOf, byText) : q.OrderBy(KindOf, byText),
            "Param" => SortDescending ? q.OrderByDescending(ParamOf, byText) : q.OrderBy(ParamOf, byText),
            "Before" => SortDescending ? q.OrderByDescending(BeforeSizeOf) : q.OrderBy(BeforeSizeOf),
            "After" => SortDescending ? q.OrderByDescending(t => t.OutputSize) : q.OrderBy(t => t.OutputSize),
            "Status" => SortDescending ? q.OrderByDescending(t => t.Status, byText) : q.OrderBy(t => t.Status, byText),
            // 扩充列
            "Source" => SortDescending ? q.OrderByDescending(t => t.SourcePath, byText) : q.OrderBy(t => t.SourcePath, byText),
            "Command" => SortDescending ? q.OrderByDescending(CommandSummaryOf, byText) : q.OrderBy(CommandSummaryOf, byText),
            "Created" => SortDescending ? q.OrderByDescending(t => t.Created) : q.OrderBy(t => t.Created),
            "Finished" => SortDescending
                ? q.OrderByDescending(t => t.FinishedAt ?? DateTime.MinValue)
                : q.OrderBy(t => t.FinishedAt ?? DateTime.MinValue),
            _ => SortDescending ? q.OrderByDescending(t => t.Name, byText) : q.OrderBy(t => t.Name, byText)
        };

        // 🔴 `_syncing` 必须**罩住 Clear**：清空那一刻 ListBox 会把 SelectedItems 抛空并回传，
        //    View 若照收就会把用户选好的一批冲掉（与「视频列表」页同一处坑）。
        _syncing = true;
        try
        {
            Items.Clear();
            FinishedItems.Clear();
            foreach (var t in q)
            {
                // 「命令摘要」列：模型自己拼不出 ffmpeg 参数（拿不到 ffmpeg 路径），由 VM 填一次
                t.CommandSummaryDisplay = CommandSummaryOf(t);
                // 已完成 ⇒ 沉到下方独立分区（上面那块只剩还有事要做的）
                if (t.Status == VideoTaskStatus.Done) FinishedItems.Add(t);
                else Items.Add(t);
            }
        }
        finally { _syncing = false; }

        UpdateHeaders();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasFinished));
        OnPropertyChanged(nameof(FinishedHeaderText));
        UpdateSummaryText();

        // 补回选中项（还在列表里才补；被筛掉 / 删掉的保持未选中）
        if (keep != null && !ReferenceEquals(Selected, keep)
            && (Items.Contains(keep) || FinishedItems.Contains(keep)))
        {
            Selected = keep;
        }

        ReconcileSelection();
    }

    /// <summary>任务类型（列表「类型」列 / 排序用）：剪辑 / 转码 / 剪辑+转码。</summary>
    public static string KindOf(VideoTaskBase t) => t.KindDisplay;

    /// <summary>参数摘要（列表「参数」列）：剪辑看区间，转码看预设。</summary>
    public static string ParamOf(VideoTaskBase t) => t switch
    {
        CutTask c => c.RangeDisplay,
        TranscodeTask x => x.PresetName,
        _ => ""
    };

    /// <summary>
    /// 「命令摘要」列：手改过就显示手改的那条，否则显示自动生成的参数串。
    /// <para>⛔ 只放**摘要**（超长截断）—— 完整命令行在详情卡的命令区看，列表格子里塞不下也不该塞。</para>
    /// </summary>
    private string CommandSummaryOf(VideoTaskBase t)
    {
        var raw = (t.RawArgs ?? "").Trim();
        var text = raw.Length > 0 ? raw : string.Join(" ", BuildArgs(t, includeRaw: false));
        if (text.Length == 0) return "—";
        return text.Length <= 100 ? text : string.Concat(text.AsSpan(0, 100), "…");
    }

    private static long BeforeSizeOf(VideoTaskBase t) => t switch
    {
        TranscodeTask x => x.SourceSize,
        CutTask c => c.Duration > 0 ? (long)c.Duration : 0,
        _ => 0
    };

    protected override string? FieldText(object row, string columnKey) => columnKey switch
    {
        "Kind" => KindOf((VideoTaskBase)row),
        "Name" => ((VideoTaskBase)row).Name,
        "Param" => ParamOf((VideoTaskBase)row),
        "Status" => ((VideoTaskBase)row).Status,
        "Before" => ((VideoTaskBase)row) is TranscodeTask x ? x.SourceSizeDisplay : ((VideoTaskBase)row).OutputSize <= 0 ? "" : "",
        "After" => ((VideoTaskBase)row).OutputSizeDisplay,
        // 扩充列（列键与模型属性名不一致的 Command 必须显式映射，否则基类反射取不到 → 该条筛选静默失效）
        "Source" => ((VideoTaskBase)row).SourcePath,
        "Command" => CommandSummaryOf((VideoTaskBase)row),
        "Created" => ((VideoTaskBase)row).CreatedDisplay,
        "Finished" => ((VideoTaskBase)row).FinishedDisplay,
        "Progress" => ((VideoTaskBase)row).Progress.ToString(),
        _ => null
    };

    protected override double? FieldNumber(object row, string columnKey) => columnKey switch
    {
        "Before" => BeforeSizeOf((VideoTaskBase)row),
        "After" => ((VideoTaskBase)row).OutputSize,
        "Progress" => ((VideoTaskBase)row).Progress,
        "Created" => ((VideoTaskBase)row).Created.ToOADate(),
        "Finished" => ((VideoTaskBase)row).FinishedAt?.ToOADate() ?? 0,
        _ => null
    };

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Items.Count > 0) Selected = Items[0];
    }

    private void UpdateHeaders()
    {
        KindHeader = "类型" + SortArrow("Kind");
        NameHeader = "文件" + SortArrow("Name");
        ParamHeader = "参数" + SortArrow("Param");
        BeforeHeader = "处理前" + SortArrow("Before");
        AfterHeader = "处理后" + SortArrow("After");
        StatusHeader = "状态" + SortArrow("Status");
        SourceHeader = "源文件" + SortArrow("Source");
        CommandHeader = "命令摘要" + SortArrow("Command");
        CreatedHeader = "创建时间" + SortArrow("Created");
        FinishedHeader = "完成时间" + SortArrow("Finished");
    }

    partial void OnSearchTextChanged(string value) => RefreshView();
    partial void OnNewAsCutChanged(bool value) => OnPropertyChanged(nameof(NewAsTranscode));

    partial void OnSelectedChanged(VideoTaskBase? value)
    {
        OnPropertyChanged(nameof(DetailTask));
        FillEditors(DetailTask);             // 填充详情卡编辑缓冲

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanActOnSelection));
        OnPropertyChanged(nameof(SelectedIsCut));
        OnPropertyChanged(nameof(SelectedIsTranscode));
        OnPropertyChanged(nameof(SelectedHasTrim));
        OnPropertyChanged(nameof(SelectedKind));
        OnPropertyChanged(nameof(SelectedEncoder));
        OnPropertyChanged(nameof(IsCutReEncode));
    }

    /// <summary>
    /// 把一条任务（**真正的选中项**）填进详情卡的编辑缓冲。
    /// <para>抽成方法：编辑缓冲的填充只此一条路径，⛔ 别在多处各填一遍。</para>
    /// </summary>
    private void FillEditors(VideoTaskBase? value)
    {
        if (value is TranscodeTask x)
        {
            EditTranscodePresetId = x.PresetId;
            EditTranscodeCrf = x.Crf;
            EditMaxHeight = x.MaxHeight;
            EditContainer = x.Container;
            EditUseGpu = x.UseGpuEncoder;
            EditTargetWidth = x.TargetWidth;
            EditTargetHeight = x.TargetHeight;
            EditDefaultWidth = x.DefaultWidth;
            EditDefaultHeight = x.DefaultHeight;
            EditBitrateMode = x.BitrateMode;
            EditTargetBitrate = x.TargetBitrate;
            EditFrameRate = x.FrameRate;
            EditGifWidth = x.GifWidth;
            EditRotate = x.Rotate;
            SelectedRotate = RotateOptions.Find(x.Rotate);
            EditSubtitlePath = x.SubtitlePath;
            EditSubtitleMode = x.SubtitleMode;
            EditSubtitleFontSize = x.SubtitleFontSize;
            EditSubtitleMarginV = x.SubtitleMarginV;
            EditCropWidth = x.CropWidth;
            EditCropHeight = x.CropHeight;
            EditCropX = x.CropX;
            EditCropY = x.CropY;
            EditAudioMode = x.AudioMode;
            EditVolumeDb = x.VolumeDb;
            EditAudioPath = x.ReplaceAudioPath;
            EditTargetSizeMb = x.TargetSizeMb;
            EditStart = x.HasTrim ? CutTask.FormatTime(x.TrimStart) : "";
            EditEnd = x.TrimEnd > 0 ? CutTask.FormatTime(x.TrimEnd) : "";
        }
        else if (value is CutTask c)
        {
            EditCutPresetId = c.PresetId;
            EditStart = CutTask.FormatTime(c.Start);
            EditEnd = c.End > 0 ? CutTask.FormatTime(c.End) : "";
            EditSegmentsText = c.SegmentsText;
            EditExtraArgs = c.ExtraArgs;
            EditCutCrf = c.Crf;
        }
        EditOutput = value?.OutputPath ?? "";

        // 手改过的命令按原样载入编辑框（否则用户会看到"参数生成值"覆盖了自己的手改）
        IsArgsEdited = !string.IsNullOrWhiteSpace(value?.RawArgs);
        EditArgs = IsArgsEdited ? value!.RawArgs : AutoArgsText;
        OnPropertyChanged(nameof(CommandPreview));

        // §6.4.1：两个分区的勾选框跟着任务类型走（剪辑任务 = 只勾剪辑；转码任务 = 勾转码，带区间时两段都勾）
        _converting = true;
        EditIsCut = value is CutTask || value is TranscodeTask { HasTrim: true };
        EditIsTranscode = value is TranscodeTask;
        _converting = false;

        // 分区展开态：**没启用的功能一律收起**（勾选框显式赋值 —— 值没变时不会触发 OnXxxChanged，
        // 之前就会出现"上一张卡展开着、换到不相关任务还开着"的残留）。
        IsCutSectionExpanded = EditIsCut;
        IsTranscodeSectionExpanded = EditIsTranscode;
        if (value is TranscodeTask)
        {
            IsCropSectionExpanded = EditCropWidth > 0 || EditCropHeight > 0
                                    || !string.IsNullOrWhiteSpace(EditSubtitlePath);
            IsAudioSectionExpanded = !string.Equals(EditAudioMode, AudioModes.Keep, StringComparison.OrdinalIgnoreCase);
            IsBitrateSectionExpanded = EditTargetSizeMb > 0 || !UseCrf || IsGifPreset;
        }
        else
        {
            IsCropSectionExpanded = false;
            IsAudioSectionExpanded = false;
            IsBitrateSectionExpanded = false;
        }
        IsCommandSectionExpanded = IsArgsEdited;
    }

    /// <summary>重算底部摘要（队列构成 + 已选数；选中变化与刷新列表共用）。</summary>
    private void UpdateSummaryText()
    {
        var all = ItemsRaw.ToList();
        SummaryText = all.Count == 0
            ? "还没有任务"
            : $"共 {all.Count} 个任务（剪辑 {all.Count(t => t is CutTask)} · 转码 {all.Count(t => t is TranscodeTask)}）"
              + $" · 已完成 {all.Count(t => t.Status == VideoTaskStatus.Done)}"
              + (all.Count(t => t.Status == VideoTaskStatus.Failed) > 0
                  ? $" · 失败 {all.Count(t => t.Status == VideoTaskStatus.Failed)}" : "")
              + (SelectedCount > 0 ? $" · 已选 {SelectedCount}" : "");
    }

    partial void OnIsBusyChanged(bool value) => NotifyBusyChanged();
    partial void OnIsRunningChanged(bool value) => NotifyBusyChanged();

    private void NotifyBusyChanged()
    {
        OnPropertyChanged(nameof(IsNotBusy));
        OnPropertyChanged(nameof(CanActOnSelection));
    }

}
