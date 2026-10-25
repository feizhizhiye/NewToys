using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using NewToys.VideoTools.Services;

namespace NewToys.VideoTools.Models;


/// <summary>
/// 一条<b>转码任务</b>：源 → 目标（按预设）。
/// <para>
/// <b>前后对比</b>就落在这条记录上：源信息（<c>Source*</c>）在加任务时用 ffprobe 填一次，
/// 跑完再探测输出文件填 <c>Output*</c>，详情卡里并排显示"前 → 后"。
/// </para>
/// </summary>
public partial class TranscodeTask : VideoTaskBase
{
    /// <summary>预设 id（<see cref="TranscodePreset.Id"/>）—— 决定**编码族**与默认容器。v3.19 默认改 H.265。</summary>
    [ObservableProperty] private string _presetId = "h265";

    /// <summary>单独改过的 CRF（0 = 用预设的值）。</summary>
    [ObservableProperty] private int _crf;

    /// <summary>缩放到的最大边（0 = 不改分辨率）。例：720 = 缩到 720p。</summary>
    [ObservableProperty] private int _maxHeight;

    // ==================== v3.10 补足：格式 / 编码器(CPU·GPU) / 分辨率 / 码率 ====================

    /// <summary>
    /// 是否用 **GPU 编码器**（`h264_nvenc` / `hevc_nvenc` / `av1_nvenc`）。
    /// <para>⚠️ 机器没有对应显卡时 ffmpeg 会直接报错 —— 如实显示错误即可，⛔ 不做"悄悄回退"。</para>
    /// </summary>
    [ObservableProperty] private bool _useGpuEncoder;

    /// <summary>输出容器扩展名（带点）。ffmpeg 靠输出文件扩展名判断容器，所以它同时决定默认输出名后缀。</summary>
    [ObservableProperty] private string _container = ".mp4";

    /// <summary>缩放到的**宽**（像素）；0 = 不约束宽（配合 <see cref="TargetHeight"/> 按比例算）。</summary>
    [ObservableProperty] private int _targetWidth;

    /// <summary>缩放到的**高**（像素）；0 = 不约束高。</summary>
    [ObservableProperty] private int _targetHeight;

    /// <summary>
    /// **GIF 帧率**（只对 GIF 预设生效）：12 是「体积 / 流畅度」的常用折中，24 = 接近原速。
    /// </summary>
    [ObservableProperty] private int _frameRate = 12;

    /// <summary>**GIF 宽度**（只对 GIF 预设生效）：0 = 保持原宽（体积会很大），默认 480。</summary>
    [ObservableProperty] private int _gifWidth = 480;

    /// <summary>
    /// **默认宽 / 高上限**（v3.19；0 = 不启用）：源宽高**大于**它才缩到它，**小于等于**则保持原始大小
    /// （ffmpeg <c>scale=W:H:force_original_aspect_ratio=decrease</c>，只缩不放、保比例）。
    /// 与 <see cref="TargetWidth"/>/<see cref="TargetHeight"/>（显式宽高）二选一，优先用显式宽高。
    /// </summary>
    [ObservableProperty] private int _defaultWidth = 1920;

    /// <summary>默认高度上限（配合 <see cref="DefaultWidth"/>；0 = 不启用）。</summary>
    [ObservableProperty] private int _defaultHeight = 1080;

    /// <summary>
    /// 码率模式：<c>crf</c> = 恒定质量（<see cref="Crf"/>）；<c>bitrate</c> = 目标码率（<see cref="TargetBitrate"/>）。
    /// </summary>
    [ObservableProperty] private string _bitrateMode = "crf";

    /// <summary>目标码率文本（如 <c>5000k</c> / <c>5M</c>）；仅 <c>bitrate</c> 模式生效。</summary>
    [ObservableProperty] private string _targetBitrate = "";

    /// <summary>
    /// v3.16：**是否带"剪辑区间"**（详情卡转码分区上方那个「✂ 剪辑」勾选框绑它）。
    /// <para>以前只看"终点 &gt; 起点"，导致"勾了剪辑但结束留空（= 到末尾）"被判成不剪辑、勾选框显示不出来。
    /// 现在勾了就是勾了：终点留空 = 从起点到文件末尾。</para>
    /// </summary>
    [ObservableProperty] private bool _useTrim;

    /// <summary>**既剪辑又转码**时的起点（秒）；<see cref="HasTrim"/> 为真才生效。</summary>
    [ObservableProperty] private double _trimStart;

    /// <summary>**既剪辑又转码**时的终点（秒）；0 = 到末尾。</summary>
    [ObservableProperty] private double _trimEnd;

    /// <summary>
    /// 旋转 / 翻转（v3.19，取值见 <see cref="RotateOptions"/>；空 = 不旋转）。
    /// 映射到 ffmpeg <c>-vf</c>（<c>transpose</c> / <c>hflip</c>），保证转码后视频方向正确。
    /// </summary>
    [ObservableProperty] private string _rotate = "";

    /// <summary>旋转的中文名（详情卡 / 命令摘要显示）。</summary>
    public string RotateLabel => RotateOptions.Find(Rotate).Label;

    // ==================== 字幕（软字幕 / 硬字幕烧入） ====================

    /// <summary>字幕文件全路径（<c>.srt / .ass / .ssa / .vtt</c>；空 = 不加字幕）。</summary>
    [ObservableProperty] private string _subtitlePath = "";

    /// <summary>字幕方式（取值见 <see cref="SubtitleModes"/>；<c>""</c> = 不加）。</summary>
    [ObservableProperty] private string _subtitleMode = SubtitleModes.None;

    /// <summary>硬字幕字号（像素；0 = 用字幕文件自带的样式）。仅硬字幕生效。</summary>
    [ObservableProperty] private int _subtitleFontSize = 22;

    /// <summary>硬字幕距底边距（像素；0 = 不设）。仅硬字幕生效。</summary>
    [ObservableProperty] private int _subtitleMarginV = 24;

    /// <summary>是否软字幕（只封装、不重编码）。</summary>
    public bool IsSoftSubtitle
        => string.Equals(SubtitleMode, SubtitleModes.Soft, StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(SubtitlePath);

    /// <summary>是否硬字幕（烧进画面）。</summary>
    public bool IsHardSubtitle
        => string.Equals(SubtitleMode, SubtitleModes.Hard, StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(SubtitlePath);

    /// <summary>字幕摘要（命令摘要 / 详情卡提示）。</summary>
    public string SubtitleDisplay
        => !IsSoftSubtitle && !IsHardSubtitle
            ? "无字幕"
            : $"{(IsSoftSubtitle ? "软字幕" : "硬字幕")} · {System.IO.Path.GetFileName(SubtitlePath)}";

    partial void OnSubtitleModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsSoftSubtitle));
        OnPropertyChanged(nameof(IsHardSubtitle));
        OnPropertyChanged(nameof(SubtitleDisplay));
        OnPropertyChanged(nameof(ParamDisplay));
    }

    partial void OnSubtitlePathChanged(string value)
    {
        OnPropertyChanged(nameof(IsSoftSubtitle));
        OnPropertyChanged(nameof(IsHardSubtitle));
        OnPropertyChanged(nameof(SubtitleDisplay));
        OnPropertyChanged(nameof(ParamDisplay));
    }

    // ==================== 画面裁剪（去黑边 / 只留画面一块） ====================

    /// <summary>裁剪宽（像素；0 = 不裁剪）。必须与 <see cref="CropHeight"/> 同时 &gt; 0 才生效。</summary>
    [ObservableProperty] private int _cropWidth;

    /// <summary>裁剪高（像素；0 = 不裁剪）。</summary>
    [ObservableProperty] private int _cropHeight;

    /// <summary>裁剪左上角 X（像素；0 = 贴左边）。</summary>
    [ObservableProperty] private int _cropX;

    /// <summary>裁剪左上角 Y（像素；0 = 贴顶边）。</summary>
    [ObservableProperty] private int _cropY;

    /// <summary>是否真的有裁剪（宽高都 &gt; 0）。</summary>
    public bool IsCropping => CropWidth > 0 && CropHeight > 0;

    /// <summary>裁剪摘要（详情卡提示 / 列表参数列）。</summary>
    public string CropDisplay
        => IsCropping ? $"{CropWidth}×{CropHeight} @ ({CropX}, {CropY})" : "不裁剪";

    partial void OnCropWidthChanged(int value) => NotifyCropChanged();
    partial void OnCropHeightChanged(int value) => NotifyCropChanged();
    partial void OnCropXChanged(int value) => NotifyCropChanged();
    partial void OnCropYChanged(int value) => NotifyCropChanged();

    private void NotifyCropChanged()
    {
        OnPropertyChanged(nameof(IsCropping));
        OnPropertyChanged(nameof(CropDisplay));
        OnPropertyChanged(nameof(ParamDisplay));
    }

    // ==================== 音轨（静音 / 音量 / 替换） ====================

    /// <summary>音频方式（取值见 <see cref="AudioModes"/>；<c>""</c> = 保持原音频）。</summary>
    [ObservableProperty] private string _audioMode = AudioModes.Keep;

    /// <summary>音量增益（dB，正 = 变响、负 = 变轻；0 = 不改）。仅 <see cref="AudioModes.Volume"/> 生效。</summary>
    [ObservableProperty] private double _volumeDb;

    /// <summary>替换用的音频文件全路径。仅 <see cref="AudioModes.Replace"/> 生效。</summary>
    [ObservableProperty] private string _replaceAudioPath = "";

    public bool IsAudioMuted => string.Equals(AudioMode, AudioModes.Mute, StringComparison.OrdinalIgnoreCase);
    public bool IsVolumeMode => string.Equals(AudioMode, AudioModes.Volume, StringComparison.OrdinalIgnoreCase);
    public bool IsReplaceAudio => string.Equals(AudioMode, AudioModes.Replace, StringComparison.OrdinalIgnoreCase);

    /// <summary>音频摘要（详情卡 / 参数列）。</summary>
    public string AudioDisplay => AudioMode switch
    {
        AudioModes.Mute => "静音",
        AudioModes.Volume => VolumeDb == 0 ? "音量（未设 dB）" : $"音量 {VolumeDb:+0.#;-0.#;0} dB",
        AudioModes.Replace => string.IsNullOrWhiteSpace(ReplaceAudioPath)
            ? "替换音轨（未选文件）"
            : "替换音轨 · " + System.IO.Path.GetFileName(ReplaceAudioPath),
        _ => "原音频"
    };

    partial void OnAudioModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsAudioMuted));
        OnPropertyChanged(nameof(IsVolumeMode));
        OnPropertyChanged(nameof(IsReplaceAudio));
        OnPropertyChanged(nameof(AudioDisplay));
        OnPropertyChanged(nameof(ParamDisplay));
    }

    partial void OnVolumeDbChanged(double value)
    {
        OnPropertyChanged(nameof(AudioDisplay));
        OnPropertyChanged(nameof(ParamDisplay));
    }

    partial void OnReplaceAudioPathChanged(string value)
    {
        OnPropertyChanged(nameof(AudioDisplay));
        OnPropertyChanged(nameof(ParamDisplay));
    }

    // ==================== 目标体积（按 MB 反算码率） ====================

    /// <summary>
    /// 目标体积（MB；0 = 不用）。启用后按 <see cref="SourceDuration"/> 反算视频码率
    /// （<see cref="FfmpegService.AutoBitrateKbps"/>），**优先于** CRF / 目标码率。
    /// </summary>
    [ObservableProperty] private double _targetSizeMb;

    public bool HasTargetSize => TargetSizeMb > 0;

    partial void OnTargetSizeMbChanged(double value)
    {
        OnPropertyChanged(nameof(HasTargetSize));
        OnPropertyChanged(nameof(ParamDisplay));
    }

    // ==================== 转码前（源） ====================

    [ObservableProperty] private long _sourceSize;
    [ObservableProperty] private string _sourceCodec = "";
    [ObservableProperty] private string _sourceResolution = "";
    [ObservableProperty] private string _sourceBitRate = "";
    [ObservableProperty] private double _sourceDuration;

    // ==================== 转码后（输出） ====================

    [ObservableProperty] private string _outputCodec = "";
    [ObservableProperty] private string _outputResolution = "";
    [ObservableProperty] private string _outputBitRate = "";
    [ObservableProperty] private double _outputDuration;

    public TranscodePreset Preset => TranscodePreset.Find(PresetId);

    public string PresetName => Preset.Name;

    public string SourceSizeDisplay => FormatSize(SourceSize);

    // ==================== v3.10：参数摘要（详情卡 / 批量列表显示） ====================

    /// <summary>是否带"剪辑区间"（既剪辑又转码的任务）。终点 0 / 留空 = 到末尾。</summary>
    public bool HasTrim => UseTrim || TrimEnd > TrimStart;

    /// <summary>区间文本（<c>00:01:02.000 → 00:01:30.000</c>）；没设返回空串。</summary>
    public string TrimDisplay => HasTrim
        ? $"{CutTask.FormatTime(TrimStart)} → {(TrimEnd > 0 ? CutTask.FormatTime(TrimEnd) : "末尾")}"
        : "";

    /// <summary>实际会用的视频编码器名（CPU / GPU 由 <see cref="UseGpuEncoder"/> 决定）。</summary>
    public string EncoderDisplay => FfmpegService.ResolveEncoder(Preset.VideoCodec, UseGpuEncoder);

    /// <summary>容器（格式）显示文本。GIF 预设强制 <c>.gif</c>；**软字幕强制 <c>.mkv</c>**（mp4 装不了 srt 字幕轨）。</summary>
    public string ContainerDisplay =>
        FfmpegService.IsGif(Preset) ? ".gif"
        : IsSoftSubtitle ? ".mkv"
        : string.IsNullOrWhiteSpace(Container) ? Preset.Extension
        : Container;

    /// <summary>分辨率设置显示；两个都为 0 = 保持原分辨率。</summary>
    public string ResolutionDisplay =>
        TargetWidth > 0 && TargetHeight > 0 ? $"{TargetWidth}×{TargetHeight}"
      : TargetHeight > 0 ? $"自适应 × 高 {TargetHeight}"
      : TargetWidth > 0 ? $"宽 {TargetWidth} × 自适应"
      : (MaxHeight > 0 ? $"限高 {MaxHeight}p"
      : (DefaultWidth > 0 && DefaultHeight > 0 ? $"不超 {DefaultWidth}×{DefaultHeight}" : "原分辨率"));

    /// <summary>码率设置显示。</summary>
    public string RateDisplay
    {
        get
        {
            if (string.Equals(BitrateMode, "bitrate", StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(TargetBitrate) ? "—" : "目标 " + TargetBitrate;
            var crf = Crf > 0 ? Crf : Preset.Crf;
            return crf > 0 ? $"CRF {crf}" : "—";
        }
    }

    public string SourceDurationDisplay => SourceDuration > 0 ? CutTask.FormatTime(SourceDuration) : "—";

    public string OutputDurationDisplay => OutputDuration > 0 ? CutTask.FormatTime(OutputDuration) : "—";

    /// <summary>
    /// 省了多少（负数 = 变大了）。只在两头都有大小时才有意义。
    /// 例：源 100MB、输出 60MB → "−40%（省 40.0 MB）"。
    /// </summary>
    public string SavingDisplay
    {
        get
        {
            if (SourceSize <= 0 || OutputSize <= 0) return "—";
            var delta = (double)(OutputSize - SourceSize) / SourceSize;
            var saved = SourceSize - OutputSize;
            var sign = delta <= 0 ? "−" : "+";
            return $"{sign}{Math.Abs(delta) * 100:0.0}%（{sign}{FormatSize(Math.Abs(saved))}）";
        }
    }

    public bool HasResult => OutputSize > 0;

    public override string KindDisplay => HasTrim ? "剪辑+转码" : "转码";

    public override string ParamDisplay
    {
        get
        {
            var head = HasTrim ? TrimDisplay + " · " + PresetName : PresetName;
            if (IsSoftSubtitle || IsHardSubtitle) head += " · " + SubtitleDisplay;
            // 非默认值才追加，避免列表里堆一串"不裁剪 · 原音频"
            if (IsCropping) head += " · 裁剪 " + CropDisplay;
            if (!string.IsNullOrEmpty(AudioMode)) head += " · " + AudioDisplay;
            if (HasTargetSize) head += $" · 目标 {TargetSizeMb:0.#} MB";
            return head;
        }
    }
    public override string BeforeDisplay => SourceSizeDisplay;

    partial void OnPresetIdChanged(string value)
    {
        OnPropertyChanged(nameof(Preset));
        OnPropertyChanged(nameof(PresetName));
        OnPropertyChanged(nameof(ParamDisplay));
        // 预设变了 → 容器（GIF 强制 .gif）、编码器、码率、分辨率显示都要跟着变
        OnPropertyChanged(nameof(ContainerDisplay));
        OnPropertyChanged(nameof(EncoderDisplay));
        OnPropertyChanged(nameof(RateDisplay));
        OnPropertyChanged(nameof(IsGifPreset));
    }

    /// <summary>是不是 GIF 预设（详情卡据此显隐「GIF 帧率 / 宽度」）。</summary>
    public bool IsGifPreset => FfmpegService.IsGif(Preset);

    partial void OnRotateChanged(string value) => OnPropertyChanged(nameof(RotateLabel));

    partial void OnSourceSizeChanged(long value) => OnPropertyChanged(nameof(SavingDisplay));

    /// <summary>
    /// 「输出大小变了」的回调。
    /// <para>⚠️ <see cref="VideoTaskBase.OutputSize"/> 是<b>基类</b>里的 <c>[ObservableProperty]</c>，
    /// 所以它的分部方法 <c>OnOutputSizeChanged</c> 只能在基类里声明（写在这里会 CS0759：找不到定义声明）。
    /// 基类那边会转调这个虚方法，让派生类照样能补发自己的派生属性通知。</para>
    /// </summary>
    protected override void NotifyOutputSizeChanged() => OnPropertyChanged(nameof(SavingDisplay));
}

