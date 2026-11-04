using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Details;
using NewToys.VideoTools.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.VideoTools.Models;
using NewToys.VideoTools.Services;

namespace NewToys.VideoTools.ViewModels;

public partial class VideoPreviewViewModel : ObservableObject, IStatusProvider, IDetailPanelProvider, IDetailCardSource, IToolbarSource
{

    /// <summary>
    /// 工具条末位的详情开关 → 走本命令。
    /// <para>⚠️ Shell 右列由 <c>DetailCardHost</c> 承载，靠 <c>IsDetailVisible</c> 联动；
    /// ⛔ 另起一个只翻标志位的开关会与 Shell 脱节（右列不跟着出现 / 消失）。</para>
    /// </summary>
    [RelayCommand]
    private void ToggleDetailPanel()
    {
        IsDetailVisible = !IsDetailVisible;
        _cols?.SaveFlag(PageKey, IColumnLayoutService.FlagDetailPanel, IsDetailVisible);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shell 恢复到「展开」时调（Shell 侧契约绑定按 config 回放），同样要通知 Shell。</summary>
    [RelayCommand]
    private void OpenDetailPanel()
    {
        if (!IsDetailVisible) IsDetailVisible = true;
        _cols?.SaveFlag(PageKey, IColumnLayoutService.FlagDetailPanel, IsDetailVisible);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    // —— 显式实现接口（⛔ 不写成 public 方法：与 [RelayCommand] 生成的 XxxCommand 并存会混淆） ——
    void IDetailPanelProvider.ToggleDetail() => ToggleDetailPanelCommand.Execute(null);
    void IDetailPanelProvider.OpenDetail() => OpenDetailPanelCommand.Execute(null);

    private DetailCardDescriptor? _card;

    /// <summary>
    /// 本页详情卡：结构 / 字段 / 文案全部来自 <see cref="VideoTaskCard"/>（与「批量处理」页同一份），
    /// 这里只声明本页的差异 —— 失焦即生效（预览页没有「保存修改」按钮）、没有「📄 任务状态」节。
    /// </summary>
    public DetailCardDescriptor? Card => _card ??= VideoTaskCard.Build(
        new VideoCardCommands
        {
            PickOutput = PickOutputCommand,
            PickSubtitle = PickSubtitleCommand,
            PickAudio = PickAudioCommand,
            DetectCrop = DetectCropCommand
        },
        explicitCommit: false,
        emptyText: "先点「选择视频」挑一个文件：这里可以调剪辑区间、转码预设、画面 / 音轨 / 码率，边调边看上面的命令。",
        includeTaskState: false);

    /// <summary>台头「类型 / 文件」那一行（两页共用同一张卡，属性名也必须一致）。</summary>
    public string DetailTitle => string.IsNullOrWhiteSpace(SourcePath)
        ? "（未载入视频）"
        : System.IO.Path.GetFileName(SourcePath)
          + (EditIsTranscode ? (EditIsCut ? " · 剪辑 + 转码" : " · 转码") : EditIsCut ? " · 剪辑" : "");

    /// <summary>右侧正文是否显示（预览页恒显示；批量页靠「有没有选中」）。</summary>
    public bool ShowDetailBody => true;

    /// <summary>卡片顶部勾选框可用性：没载入视频时不许切任务类型。</summary>
    public bool HasSelection => HasSource;

    /// <summary>源分辨率是否已知（未知就不显示「原视频宽高」提示行）。</summary>
    public bool HasSourceResolution => SourceWidth > 0 && SourceHeight > 0;

    /// <summary>
    /// 预览页不出收益预估行：这里是「边看画面边调参」，判定结论在生成任务后到批量页看
    /// （与批量页同名的占位，恒 false ⇒ 整行不渲染）。
    /// </summary>
    public bool HasDetailAdvice => false;

    /// <summary>与批量页同名的占位。</summary>
    public string DetailAdvice => "—";

    /// <summary>「🎨 画面」节折叠状态。</summary>
    [ObservableProperty] private bool _isCropSectionExpanded = true;

    /// <summary>「🔊 音轨」节折叠状态。</summary>
    [ObservableProperty] private bool _isAudioSectionExpanded;

    /// <summary>「🎚 码率与体积」节折叠状态。</summary>
    [ObservableProperty] private bool _isBitrateSectionExpanded;

    /// <summary>「⌨ ffmpeg 参数」节折叠状态。</summary>
    [ObservableProperty] private bool _isCommandSectionExpanded;

    // ==================== 任务类型（剪辑 / 转码，**可多选**） ====================

    /// <summary>是否**剪辑**（截取区间）。与 <see cref="EditIsTranscode"/> 独立 —— 都勾 = 一条命令既切区间又转码。</summary>
    [ObservableProperty] private bool _editIsCut = true;

    /// <summary>是否**转码**（换编码 / 格式 / 分辨率 / 码率）。</summary>
    [ObservableProperty] private bool _editIsTranscode;

    /// <summary>「剪辑设置」折叠区是否展开（取消勾选时自动折叠，⛔ 折叠不影响是否执行）。</summary>
    [ObservableProperty] private bool _isCutSectionExpanded = true;

    /// <summary>「转码设置」折叠区是否展开（同上）。</summary>
    [ObservableProperty] private bool _isTranscodeSectionExpanded;

    /// <summary>区间是否参与命令（勾选剪辑时才生效）。</summary>
    public bool RangeEnabled => EditIsCut;

    partial void OnEditIsCutChanged(bool value)
    {
        IsCutSectionExpanded = value;
        OnPropertyChanged(nameof(RangeEnabled));
        RefreshCommand();
        OnPropertyChanged(nameof(CanTrial));
        NotifyToolbarChanged();
    }

    partial void OnEditIsTranscodeChanged(bool value)
    {
        IsTranscodeSectionExpanded = value;
        RefreshCommand();
        OnPropertyChanged(nameof(CanTrial));
        NotifyToolbarChanged();
    }

    // ==================== 源文件 ====================

    [ObservableProperty] private string _sourcePath = "";
    [ObservableProperty] private double _duration;
    [ObservableProperty] private bool _isBusy;

    public bool IsNotBusy => !IsBusy;
    public bool HasSource => !string.IsNullOrWhiteSpace(SourcePath);
    public string DurationDisplay => Duration > 0 ? CutTask.FormatTime(Duration) : "—";

    /// <summary>时长是否已知（未知就不显示时间条 —— 没时长没得拖）。</summary>
    public bool HasDuration => Duration > 0;

    /// <summary>
    /// 时间条的 <c>Maximum</c>。
    /// <para>⚠️ **必须恒大于** <c>Minimum=0</c>：时长未知时若直接把 <c>Maximum</c> 绑到 <c>Duration</c>，
    /// 就得到 <c>Maximum == Minimum == 0</c>，Slider 内部按 <c>(Value-Min)/(Max-Min)</c> 取值 → <c>0/0</c> = <b>NaN</b>，
    /// Avalonia 布局拿到 NaN 会整趟算不过去（表现成「整页样式失效」）。所以给个占位 1。</para>
    /// </summary>
    public double TimelineMax => Duration > 0 ? Duration : 1;

    partial void OnSourcePathChanged(string value)
    {
        // 换文件 → 上一张预览图 / 时长 / 输出 / 手改过的命令全部作废；顺手停掉播放预览
        StopPlayTimer();
        IsPlaying = false;
        PreviewImage = null;
        Duration = 0;
        EditOutput = "";
        EditEnd = "";              // v3.16.1：换文件 → 结束归零，探测到新时长后自动填成"整段末尾"（停最右）
        PreviewRotation = 0;       // v3.19：换文件 → 预览旋转角度与「旋转」字段归零
        EditRotate = "";
        // ~ 裁剪坐标 / 替换音轨 / 目标体积都跟具体文件绑定，换文件必须作废
        EditCropWidth = 0;
        EditCropHeight = 0;
        EditCropX = 0;
        EditCropY = 0;
        EditAudioPath = "";
        EditTargetSizeMb = 0;
        IsArgsEdited = false;
        OnPropertyChanged(nameof(HasSource));
        RefreshCommand();
        RefreshStatusPrefix();     // v3.16：换文件 → 状态栏前缀立刻跟上
        NotifyToolbarChanged();    // 工具条按钮的可用性跟着「有没有源」走
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    partial void OnDurationChanged(double value)
    {
        OnPropertyChanged(nameof(TimelineMax));
        OnPropertyChanged(nameof(HasDuration));
        OnPropertyChanged(nameof(DurationDisplay));
        OnPropertyChanged(nameof(TargetSizeHint));      // 体积反算依赖时长
        OnPropertyChanged(nameof(HasTargetSizeHint));
        RefreshStatusPrefix();     // v3.16：探测到时长 → 状态栏前缀里的时长补上

        // 1：没手填过结束 → 结束滑块**停在最右**（= 整段末尾）。
        // 以前它和上面两条一样停在 0，三条滑块全挤在最左边，看着像没对齐、也读不出"结束=片尾"这层意思。
        if (value > 0 && string.IsNullOrWhiteSpace(EditEnd)) EndSeconds = value;
    }

    // ==================== 预览（一张大画面 + 三条时间条） ====================

    /// <summary>当前预览的时间点（秒）—— 第一条时间条绑它，拖动即时抽帧。</summary>
    [ObservableProperty] private double _previewTime;

    /// <summary>当前预览的大画面。</summary>
    [ObservableProperty] private Bitmap? _previewImage;

    /// <summary>预览画面旋转角度（0 / 90 / 180 / 270；纯视觉 <c>RenderTransform</c>，⛔ 不改文件）。v3.19。</summary>
    [ObservableProperty] private double _previewRotation;

    public bool HasPreviewImage => PreviewImage != null;
    public string PreviewTimeDisplay => CutTask.FormatTime(PreviewTime);

    partial void OnPreviewImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasPreviewImage));

    partial void OnPreviewTimeChanged(double value)
    {
        OnPropertyChanged(nameof(PreviewTimeDisplay));
        _ = UpdatePreviewAsync(value);
    }

}
