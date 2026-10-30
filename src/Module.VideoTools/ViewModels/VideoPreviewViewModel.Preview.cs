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
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.VideoTools.Models;
using NewToys.VideoTools.Services;

namespace NewToys.VideoTools.ViewModels;

public partial class VideoPreviewViewModel : ObservableObject, IStatusProvider, IDetailPanelProvider, IDetailCardSource, IToolbarSource
{

    /// <summary>
    /// 抽 <paramref name="time"/> 这一帧当预览大图。
    /// <para>拖动时间条会连发很多次，ffmpeg 每次要几十~几百毫秒 —— 这里做**最新值优先**：
    /// 正在抽就把新位置记为 pending，抽完再补最后一次，中间的丢弃。</para>
    /// </summary>
    private async Task UpdatePreviewAsync(double time)
    {
        var src = SourcePath;
        if (string.IsNullOrWhiteSpace(src) || !File.Exists(src)) return;

        if (_extracting) { _pendingPreview = time; return; }

        _extracting = true;
        try
        {
            do
            {
                _pendingPreview = -1;
                var dir = Path.Combine(Path.GetTempPath(), "NewToys", "preview");
                Directory.CreateDirectory(dir);
                // 轮换 4 个文件名：上一张 Bitmap 还占着文件，直接覆盖会被 Bitmap / ffmpeg 抢写失败
                var file = Path.Combine(dir, $"p{_previewIndex:00}.jpg");
                _previewIndex = (_previewIndex + 1) % 4;

                var ffmpeg = FfmpegService.ResolveFfmpeg(_cfg?.FfmpegPath);
                if (await FfmpegService.ExtractFrameAsync(ffmpeg, src, time, file) && File.Exists(file))
                {
                    try { PreviewImage = new Bitmap(file); }
                    catch { /* 图坏了就保留上一帧 */ }
                }
                time = _pendingPreview;
            } while (time >= 0);
        }
        finally { _extracting = false; }
    }

    /// <summary>
    /// 载入源文件后**自动进入预览**（v3.10 要求：不用再手点抽帧）—— 探时长 + 抽首帧。
    /// </summary>
    private async Task PreparePreviewAsync()
    {
        var path = SourcePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        if (IsBusy) return;

        IsBusy = true;
        try
        {
            if (Duration <= 0)
            {
                var ffprobe = VideoScanner.DeriveFfprobe(_cfg?.FfmpegPath);
                var (ok, _, info) = await VideoScanner.ProbeAsync(path, ffprobe);
                if (ok && info != null)
                {
                    if (info.Duration != null) Duration = info.Duration.Value;
                    if (info.Width is > 0) SourceWidth = info.Width.Value;    // v3.19：顺手记下源宽高供参考
                    if (info.Height is > 0) SourceHeight = info.Height.Value;
                    if (info.Codec is { Length: > 0 }) SourceCodec = info.Codec;
                    if (info.BitRate is > 0) SourceBitRate = info.BitRate;
                    if (!string.IsNullOrWhiteSpace(info.ContainerFormat)) SourceContainerFormat = info.ContainerFormat!;
                    try { var fi = new System.IO.FileInfo(path); if (fi.Exists) SourceSize = fi.Length; } catch { /* 忽略 */ }
                }
                NotifyVideoInfo();
            }

            if (Duration <= 0)
            {
                StatusText = "读不出时长 —— 检查 ffmpeg / ffprobe 是否可用（「设置 → 路径配置」）；三条时间条已隐藏。";
                return;
            }

            // ⚠️ 不能只赋值 PreviewTime：值没变就不触发 OnChanged → 抽帧不跑。所以显式再调一次。
            var start = StartSeconds > 0 ? StartSeconds : 0;
            PreviewTime = start;
            await UpdatePreviewAsync(start);
            StatusText = "已载入 —— 拖「预览位置」条选画面，「开始 / 结束」两条定区间。";
        }
        catch (Exception ex)
        {
            StatusText = "预览失败：" + ex.Message;
        }
        finally { IsBusy = false; }
    }

    // ==================== 剪辑参数（保持简单） ====================

    [ObservableProperty] private string _editStart = "00:00:00.000";
    [ObservableProperty] private string _editEnd = "";
    [ObservableProperty] private string _editCutPresetId = CutPresets.Copy;
    [ObservableProperty] private int _editCutCrf = 20;
    [ObservableProperty] private string _editExtraArgs = "";

    /// <summary>剪切预设下拉框的数据源（不写死在 XAML 里）。</summary>
    public IReadOnlyList<CutPresetOption> CutPresetOptions => CutPresets.All;

    /// <summary>当前剪切预设是不是「重新编码」（决定 CRF 输入框显不显示）。</summary>
    public bool IsCutReEncode => string.Equals(EditCutPresetId, CutPresets.ReEncode, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 「开始」时间条绑它（秒）。
    /// <para>⚠️ 单独一层的原因：直接绑时间码文本的话，拖动只会改文本、和时间条值互相打架；
    /// 这里改一处 → 文本 + 命令预览一起更新。</para>
    /// </summary>
    public double StartSeconds
    {
        get => ParseOr(EditStart, 0);
        set
        {
            EditStart = CutTask.FormatTime(value < 0 ? 0 : value);
            OnPropertyChanged();
        }
    }

    /// <summary>「结束」时间条绑它（秒；0 = 到末尾）。</summary>
    public double EndSeconds
    {
        get => string.IsNullOrWhiteSpace(EditEnd) ? 0 : ParseOr(EditEnd, 0);
        set
        {
            EditEnd = value > 0 ? CutTask.FormatTime(value) : "";
            OnPropertyChanged();
        }
    }

    partial void OnEditStartChanged(string value)
    {
        OnPropertyChanged(nameof(StartSeconds));
        RefreshCommand();
    }

    partial void OnEditEndChanged(string value)
    {
        OnPropertyChanged(nameof(EndSeconds));
        RefreshCommand();
    }

    partial void OnEditPresetIdChanged(string value)
    {
        OnPropertyChanged(nameof(IsCutReEncode));
        RefreshCommand();
    }

    partial void OnEditExtraArgsChanged(string value) => RefreshCommand();

    // ==================== 转码参数（v3.10 补足：格式 / 编码器(CPU·GPU) / 分辨率 / 码率） ====================

    /// <summary>编码族 + 默认容器（取值见 <see cref="TranscodePreset"/>）。v3.19 默认改 H.265。</summary>
    [ObservableProperty] private string _editTranscodePresetId = "h265";

    /// <summary>是否走 GPU 编码器（<c>h264_nvenc</c> / <c>hevc_nvenc</c> / <c>av1_nvenc</c>）。</summary>
    [ObservableProperty] private bool _editUseGpu;

    /// <summary>输出容器扩展名（带点，如 <c>.mkv</c>）。</summary>
    [ObservableProperty] private string _editContainer = ".mp4";

    /// <summary>目标宽（0 = 不约束宽，按高度等比）。</summary>
    [ObservableProperty] private int _editTargetWidth;

    /// <summary>目标高（0 = 不约束高，按宽度等比）。</summary>
    [ObservableProperty] private int _editTargetHeight;

    /// <summary>默认宽上限（0 = 不用；源更宽才缩到它，否则保原始，v3.19）。</summary>
    [ObservableProperty] private int _editDefaultWidth = 1920;

    /// <summary>默认高上限（0 = 不用；配合 <see cref="EditDefaultWidth"/>）。</summary>
    [ObservableProperty] private int _editDefaultHeight = 1080;

    /// <summary>源视频宽 / 高（v3.19：探测后显示，供设「默认宽高上限」时参考）。</summary>
    [ObservableProperty] private int _sourceWidth;
    [ObservableProperty] private int _sourceHeight;
    partial void OnSourceWidthChanged(int value) => OnPropertyChanged(nameof(SourceResolutionDisplay));
    partial void OnSourceHeightChanged(int value) => OnPropertyChanged(nameof(SourceResolutionDisplay));

    /// <summary>「原视频宽高」显示文本（未探测到则提示）。</summary>
    public string SourceResolutionDisplay =>
        SourceWidth > 0 && SourceHeight > 0 ? $"{SourceWidth}×{SourceHeight}" : "未读取（载入后自动探测）";

    /// <summary>v3.19：源视频编码 / 码率 / 容器（探测时填充，供右键「视频信息」）。</summary>
    [ObservableProperty] private string _sourceCodec = "";
    [ObservableProperty] private long? _sourceBitRate;
    [ObservableProperty] private string _sourceContainerFormat = "";

    /// <summary>源文件大小（探测时顺手取）。</summary>
    [ObservableProperty] private long _sourceSize;

    /// <summary>
    /// 预览画面右键「视频信息」的多行文本（v3.19：编辑命令时快速查看名称/大小/编码/时长/码率/容器/分辨率/旋转）。
    /// </summary>
    public string VideoInfoText
    {
        get
        {
            var name = string.IsNullOrEmpty(SourcePath) ? "（未载入）" : System.IO.Path.GetFileName(SourcePath);
            var size = SourceSize > 0 ? VideoTaskBase.FormatSize(SourceSize) : "—";
            var codec = string.IsNullOrWhiteSpace(SourceCodec) ? "—" : SourceCodec;
            var dur = Duration > 0 ? CutTask.FormatTime(Duration) : "—";
            var rate = SourceBitRate is > 0 ? $"{SourceBitRate.Value / 1000.0:0} kbps" : "—";
            var cont = !string.IsNullOrWhiteSpace(SourceContainerFormat)
                ? SourceContainerFormat
                : (string.IsNullOrEmpty(SourcePath) ? "—" : System.IO.Path.GetExtension(SourcePath).TrimStart('.').ToLowerInvariant());
            var rot = SelectedRotate?.Label ?? "不旋转";
            return string.Join("\n", new[]
            {
                $"名称：{name}",
                $"大小：{size}",
                $"编码：{codec}",
                $"时长：{dur}",
                $"码率：{rate}",
                $"容器：{cont}",
                $"分辨率：{SourceResolutionDisplay}",
                $"旋转：{rot}",
                $"路径：{SourcePath}",
            });
        }
    }

    private void NotifyVideoInfo() => OnPropertyChanged(nameof(VideoInfoText));

    /// <summary>
    /// 缩到的**最大高度**（0 = 不改；例 720 = 缩到 720p）。
    /// 两页详情卡字段集合要一致 —— 「批量处理」页早就有这一项，这边补上。
    /// </summary>
    [ObservableProperty] private int _editMaxHeight;

    /// <summary>码率模式：<c>crf</c>（恒定质量）/ <c>bitrate</c>（目标码率）。</summary>
    [ObservableProperty] private string _editBitrateMode = "crf";

    [ObservableProperty] private int _editTranscodeCrf;
    [ObservableProperty] private string _editTargetBitrate = "";

    /// <summary>GIF 帧率（只对 GIF 预设生效）。</summary>
    [ObservableProperty] private int _editFrameRate = 12;

    /// <summary>GIF 宽度（0 = 保持原宽）。</summary>
    [ObservableProperty] private int _editGifWidth = 480;

    /// <summary>当前转码预设是不是 GIF（详情卡据此显隐两个 GIF 字段）。</summary>
    public bool IsGifPreset => FfmpegService.IsGif(TranscodePreset.Find(EditTranscodePresetId));

    partial void OnEditFrameRateChanged(int value) => RefreshCommand();
    partial void OnEditGifWidthChanged(int value) => RefreshCommand();

    // ---- 旋转 / 翻转（v3.19）----

    /// <summary>旋转 / 翻转编辑缓冲（取值见 <see cref="RotateOptions"/>；空 = 不旋转）。</summary>
    [ObservableProperty] private string _editRotate = "";

    /// <summary>「旋转」下拉的选项（不写死在 XAML 里）。</summary>
    public IReadOnlyList<RotateOption> RotateChoices => RotateOptions.All;

    /// <summary>「旋转」下拉选中项（变化经 <see cref="OnSelectedRotateChanged"/> 回写 <see cref="EditRotate"/> 并同步预览角度）。</summary>
    [ObservableProperty] private RotateOption? _selectedRotate;

    partial void OnSelectedRotateChanged(RotateOption? value)
    {
        if (value != null) EditRotate = value.Value;
        NotifyVideoInfo();   // v3.19：旋转方向进了右键「视频信息」
    }

    partial void OnEditRotateChanged(string value)
    {
        RefreshCommand();
        // 旋转值 → 回带预览角度（命令卡改值 / ↻↺ 都走这里；翻转无角度 → 0）
        PreviewRotation = RotateOptions.ToAngle(value);
    }

    // ---- 转码参数变化 → 刷新命令预览（v3.19：补齐预览页缺口，与「批量处理」页一致）----
    // ⚠️ 预览页此前缺这些 On*Changed：改预设 / 编码器 / 分辨率 / CRF / 码率 / 容器时命令预览不刷新（批量页一直有）。

    partial void OnEditTranscodePresetIdChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedEncoder));
        OnPropertyChanged(nameof(IsGifPreset));
        RefreshCommand();
    }

    partial void OnEditUseGpuChanged(bool value)
    {
        OnPropertyChanged(nameof(EncoderIndex));
        OnPropertyChanged(nameof(SelectedEncoder));
        RefreshCommand();
    }

    partial void OnEditContainerChanged(string value) => RefreshCommand();

    partial void OnEditBitrateModeChanged(string value)
    {
        OnPropertyChanged(nameof(BitrateModeIndex));
        OnPropertyChanged(nameof(UseCrf));
        RefreshCommand();
    }

    partial void OnEditTargetWidthChanged(int value) => RefreshCommand();
    partial void OnEditTargetHeightChanged(int value) => RefreshCommand();
    partial void OnEditMaxHeightChanged(int value) => RefreshCommand();
    partial void OnEditDefaultWidthChanged(int value) => RefreshCommand();
    partial void OnEditDefaultHeightChanged(int value) => RefreshCommand();
    partial void OnEditTranscodeCrfChanged(int value) => RefreshCommand();
    partial void OnEditTargetBitrateChanged(string value) => RefreshCommand();

    // ==================== 字幕/ 多段剪切 ====================

    /// <summary>字幕文件（<c>.srt / .ass</c>；空 = 不加字幕）。</summary>
    [ObservableProperty] private string _editSubtitlePath = "";

    /// <summary>字幕方式（取值见 <see cref="SubtitleModes"/>）。</summary>
    [ObservableProperty] private string _editSubtitleMode = SubtitleModes.None;

    /// <summary>硬字幕字号（0 = 用字幕自带样式）。</summary>
    [ObservableProperty] private int _editSubtitleFontSize = 22;

    /// <summary>硬字幕底部边距（0 = 不设）。</summary>
    [ObservableProperty] private int _editSubtitleMarginV = 24;

    /// <summary>「字幕方式」下拉选项。</summary>
    public IReadOnlyList<SubtitleModeOption> SubtitleModeChoices => SubtitleModes.All;

    public bool IsHardSubtitleEdit => string.Equals(EditSubtitleMode, SubtitleModes.Hard, StringComparison.OrdinalIgnoreCase);
    public bool IsSoftSubtitleEdit => string.Equals(EditSubtitleMode, SubtitleModes.Soft, StringComparison.OrdinalIgnoreCase);

    public string SoftSubtitleHint
        => "软字幕 = 视频 / 音频原样 copy，字幕单独成轨（秒完成、播放器里可开关）；输出固定 .mkv。"
           + "此时编码 / 分辨率 / 码率设置不生效。";

    partial void OnEditSubtitleModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsHardSubtitleEdit));
        OnPropertyChanged(nameof(IsSoftSubtitleEdit));
        RefreshCommand();
    }

    partial void OnEditSubtitlePathChanged(string value) => RefreshCommand();
    partial void OnEditSubtitleFontSizeChanged(int value) => RefreshCommand();
    partial void OnEditSubtitleMarginVChanged(int value) => RefreshCommand();

    // ==================== 裁剪 / 音轨 / 目标体积（~ ） ====================

    /// <summary>裁剪宽（0 = 不裁剪；必须与高同时 &gt; 0）。</summary>
    [ObservableProperty] private int _editCropWidth;

    /// <summary>裁剪高。</summary>
    [ObservableProperty] private int _editCropHeight;

    /// <summary>裁剪左上角 X（像素）。</summary>
    [ObservableProperty] private int _editCropX;

    /// <summary>裁剪左上角 Y（像素）。</summary>
    [ObservableProperty] private int _editCropY;

    /// <summary>音频方式（取值见 <see cref="AudioModes"/>）。</summary>
    [ObservableProperty] private string _editAudioMode = AudioModes.Keep;

    /// <summary>音量增益（dB；正 = 变响）。</summary>
    [ObservableProperty] private double _editVolumeDb;

    /// <summary>替换用的音频文件。</summary>
    [ObservableProperty] private string _editAudioPath = "";

    /// <summary>目标体积（MB；0 = 不用）。</summary>
    [ObservableProperty] private double _editTargetSizeMb;

    /// <summary>「音频方式」下拉选项。</summary>
    public IReadOnlyList<AudioModeOption> AudioModeChoices => AudioModes.All;

    public bool IsVolumeModeEdit => string.Equals(EditAudioMode, AudioModes.Volume, StringComparison.OrdinalIgnoreCase);
    public bool IsReplaceAudioEdit => string.Equals(EditAudioMode, AudioModes.Replace, StringComparison.OrdinalIgnoreCase);

    /// <summary>目标体积反算出的码率提示（没填 / 时长未知 → 空）。</summary>
    public string TargetSizeHint
    {
        get
        {
            if (EditTargetSizeMb <= 0) return "";
            if (Duration <= 0) return "⚠ 读不到源时长 —— 算不出码率（先让预览读出时长）。";
            var kbps = FfmpegService.AutoBitrateKbps(BuildTranscodeTask());
            return kbps > 0
                ? $"按 {EditTargetSizeMb:0.#} MB ÷ {CutTask.FormatTime(Duration)} ≈ 视频 {kbps} kbps（已扣音频与容器开销；单遍 ABR，实际会略有偏差）。"
                : "";
        }
    }

    public bool HasTargetSizeHint => TargetSizeHint.Length > 0;

    /// <summary>📝 探测黑边（失败 / 取不到 → 状态栏照实说，不改已有裁剪值）。</summary>
    [ObservableProperty] private bool _isDetectingCrop;

}
