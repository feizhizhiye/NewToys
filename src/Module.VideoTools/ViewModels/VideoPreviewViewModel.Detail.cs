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
    /// 工具条末位的详情开关 → 走本命令。
    /// <para>⛔ 原来那个 <c>ToggleDetailCommand</c> 只翻 <c>IsDetailVisible</c>、**不通知 Shell**，
    /// Shell 的右列（由 <c>DetailCardHost</c> 承载）就不会跟着出现 / 消失。</para>
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
    /// 本页详情卡描述符（**C# 对象字面量**，⛔ 不做运行时 JSON / XAML 加载）。
    /// <para>结构与迁移前的页内自绘卡片 1:1 对应：剪辑 / 转码两个可折叠分区（标题带勾选框）+ 输出 + 命令 + 收尾说明。</para>
    /// </summary>
    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor BuildCard() => new()
    {
        Sections =
        {
            // ===== ① 剪辑（勾选框就在分区标题上：勾 = 这段进命令） =====
            new DetailSection
            {
                Header = "✂ 剪切",
                TogglePath = nameof(IsCut),
                IsExpandedPath = nameof(IsCutSectionExpanded),
                Fields =
                {
                    new DetailField
                    {
                        Label = "开始（也可拖左侧时间条）",
                        ValuePath = nameof(EditStart),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true
                    },
                    new DetailField
                    {
                        Label = "结束（留空 = 到末尾）",
                        ValuePath = nameof(EditEnd),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true
                    },
                    // 多段剪切 + 拼接（≥2 段时忽略上面的开始 / 结束）
                    new DetailField
                    {
                        Label = "多段剪切（每行一段：开始-结束；≥2 段 = 切完再拼成一条）",
                        ValuePath = nameof(EditSegmentsText),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true,
                        MultiLine = true,
                        Placeholder = "00:00:05-00:00:12\n00:00:20-00:00:28"
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        ValuePath = nameof(SegmentsHint),
                        VisibleWhen = nameof(HasSegmentsHint)
                    },
                    new DetailField
                    {
                        Label = "剪切预设",
                        Kind = FieldKind.Select,
                        OptionsPath = nameof(CutPresetOptions),
                        OptionValuePath = "Id",
                        OptionDisplayPath = "Name",
                        ValuePath = nameof(EditPresetId)
                    },
                    new DetailField
                    {
                        Label = "重新编码时的 CRF（18~28 常用，越小越清晰）",
                        ValuePath = nameof(EditCrf),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true,
                        VisibleWhen = nameof(IsReEncode)
                    },
                    new DetailField
                    {
                        Label = "自定义 ffmpeg 参数（追加在预设之后 → 可覆盖预设）",
                        ValuePath = nameof(EditExtraArgs),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true,
                        MultiLine = true,
                        Placeholder = "例：-vf scale=1280:-2"
                    }
                }
            },

            // ===== ② 转码 =====
            new DetailSection
            {
                Header = "🔁 转码",
                TogglePath = nameof(IsTranscode),
                IsExpandedPath = nameof(IsTranscodeSectionExpanded),
                Fields =
                {
                    new DetailField
                    {
                        Label = "编码预设",
                        Kind = FieldKind.Select,
                        OptionsPath = nameof(TranscodePresets),
                        OptionValuePath = "Id",
                        OptionDisplayPath = "Name",
                        ValuePath = nameof(EditTranscodePresetId)
                    },
                    new DetailField
                    {
                        Label = "编码器",
                        Kind = FieldKind.Select,
                        IndexPath = nameof(EncoderIndex),
                        LiteralOptions = new[] { "CPU（软件编码，兼容性最好）", "GPU（NVENC，N 卡）" }
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Prefix = "实际会用：",
                        ValuePath = nameof(EncoderPreview)
                    },
                    new DetailField
                    {
                        Label = "容器 / 格式（决定输出文件后缀）",
                        Kind = FieldKind.Select,
                        OptionsPath = nameof(ContainerOptions),
                        ValuePath = nameof(EditContainer)
                    },
                    // 分辨率：宽 × 高 一行两个框（FieldKind.Pair）
                    new DetailField
                    {
                        Label = "分辨率（宽 × 高；0 = 按比例 / 不改）",
                        ValuePath = nameof(EditTargetWidth),
                        Kind = FieldKind.Pair,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true,
                        Placeholder = "宽",
                        SecondValuePath = nameof(EditTargetHeight),
                        SecondPlaceholder = "高"
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Prefix = "原视频宽高：",
                        ValuePath = nameof(SourceResolutionDisplay)
                    },
                    new DetailField
                    {
                        Label = "默认宽高上限（源更大才缩，只缩不放）",
                        ValuePath = nameof(EditDefaultWidth),
                        Kind = FieldKind.Pair,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true,
                        Placeholder = "宽",
                        SecondValuePath = nameof(EditDefaultHeight),
                        SecondPlaceholder = "高"
                    },
                    new DetailField
                    {
                        Label = "旋转 / 翻转（视频方向不对时用；也可点工具条 ↻ / ↺ 转预览自动同步）",
                        Kind = FieldKind.Select,
                        OptionsPath = nameof(RotateChoices),
                        OptionDisplayPath = "Label",
                        ValuePath = nameof(SelectedRotate)
                    },
                    // ===== 裁剪：宽 × 高 + 左上角坐标 =====
                    new DetailField { Kind = FieldKind.Divider },
                    new DetailField
                    {
                        Label = "裁剪（宽 × 高；0 = 不裁剪。可用下面「🖼 探测黑边」自动填）",
                        ValuePath = nameof(EditCropWidth),
                        Kind = FieldKind.Pair,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true,
                        Placeholder = "宽",
                        SecondValuePath = nameof(EditCropHeight),
                        SecondPlaceholder = "高"
                    },
                    new DetailField
                    {
                        Label = "裁剪起点（X × Y，左上角；0,0 = 贴左上）",
                        ValuePath = nameof(EditCropX),
                        Kind = FieldKind.Pair,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true,
                        Placeholder = "X",
                        SecondValuePath = nameof(EditCropY),
                        SecondPlaceholder = "Y"
                    },
                    // ===== 字幕=====
                    new DetailField { Kind = FieldKind.Divider },
                    new DetailField
                    {
                        Label = "字幕文件（.srt / .ass；留空 = 不加字幕）",
                        ValuePath = nameof(EditSubtitlePath),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true
                    },
                    new DetailField
                    {
                        Label = "字幕方式",
                        Kind = FieldKind.Select,
                        OptionsPath = nameof(SubtitleModeChoices),
                        OptionValuePath = "Value",
                        OptionDisplayPath = "Label",
                        ValuePath = nameof(EditSubtitleMode)
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        ValuePath = nameof(SoftSubtitleHint),
                        VisibleWhen = nameof(IsSoftSubtitleEdit)
                    },
                    new DetailField
                    {
                        Label = "硬字幕字号（像素；0 = 用字幕自带样式）",
                        ValuePath = nameof(EditSubtitleFontSize),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true,
                        VisibleWhen = nameof(IsHardSubtitleEdit)
                    },
                    new DetailField
                    {
                        Label = "硬字幕底部边距（像素；0 = 不设）",
                        ValuePath = nameof(EditSubtitleMarginV),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true,
                        VisibleWhen = nameof(IsHardSubtitleEdit)
                    },
                    // ===== 音轨=====
                    new DetailField { Kind = FieldKind.Divider },
                    new DetailField
                    {
                        Label = "音频",
                        Kind = FieldKind.Select,
                        OptionsPath = nameof(AudioModeChoices),
                        OptionValuePath = "Value",
                        OptionDisplayPath = "Label",
                        ValuePath = nameof(EditAudioMode)
                    },
                    new DetailField
                    {
                        Label = "音量增益（dB；正 = 变响、负 = 变轻，±6 已很明显）",
                        ValuePath = nameof(EditVolumeDb),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true,
                        Placeholder = "6",
                        VisibleWhen = nameof(IsVolumeModeEdit)
                    },
                    new DetailField
                    {
                        Label = "替换成的音频文件（用下面「🎵 选择音频文件…」挑）",
                        ValuePath = nameof(EditAudioPath),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true,
                        VisibleWhen = nameof(IsReplaceAudioEdit)
                    },
                    new DetailField
                    {
                        Label = "码率模式",
                        Kind = FieldKind.Select,
                        IndexPath = nameof(BitrateModeIndex),
                        LiteralOptions = new[] { "CRF（恒定质量，推荐）", "目标码率（如 5000k / 5M）" }
                    },
                    new DetailField
                    {
                        Label = "CRF（0 = 用预设值；18~28 常用）",
                        ValuePath = nameof(EditTranscodeCrf),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true,
                        VisibleWhen = nameof(UseCrf)
                    },
                    new DetailField
                    {
                        Label = "目标码率（如 5000k / 5M）",
                        ValuePath = nameof(EditTargetBitrate),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true,
                        Placeholder = "5000k",
                        VisibleWhen = "!" + nameof(UseCrf)
                    },
                    // ===== 目标体积=====
                    new DetailField { Kind = FieldKind.Divider },
                    new DetailField
                    {
                        Label = "目标体积（MB；0 = 不用。填了优先于 CRF / 目标码率）",
                        ValuePath = nameof(EditTargetSizeMb),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true,
                        Placeholder = "50"
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        ValuePath = nameof(TargetSizeHint),
                        VisibleWhen = nameof(HasTargetSizeHint)
                    },
                    // 仅 GIF 预设可见（其它预设这些参数没有意义）
                    new DetailField
                    {
                        Label = "GIF 帧率（12 常用；越大越流畅、体积越大）",
                        ValuePath = nameof(EditFrameRate),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true,
                        VisibleWhen = nameof(IsGifPreset)
                    },
                    new DetailField
                    {
                        Label = "GIF 宽度（像素；0 = 保持原宽，体积会很大）",
                        ValuePath = nameof(EditGifWidth),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true,
                        VisibleWhen = nameof(IsGifPreset)
                    }
                }
            },

            // ===== ③ 输出 + 命令（原来没有分区标题，这里也不给） =====
            new DetailSection
            {
                Fields =
                {
                    new DetailField
                    {
                        Label = "输出文件（留空 = 自动命名，绝不覆盖已有文件）",
                        ValuePath = nameof(EditOutput),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true
                    },
                    new DetailField { Kind = FieldKind.Divider },
                    new DetailField
                    {
                        Label = "将要执行的命令（改参数即时刷新，与发到批处理的那条同源）",
                        ValuePath = nameof(CommandPreview),
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    // 参数预检（有意见才显示）
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        ValuePath = nameof(LintText),
                        VisibleWhen = nameof(HasLint)
                    },
                    new DetailField
                    {
                        Label = "ffmpeg 参数（可手动编辑；不含 ffmpeg.exe 与输出路径）",
                        ValuePath = nameof(EditArgs),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true,
                        MultiLine = true
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "✎ 已手改：执行时以这里为准（工具条上「↺ 重置为自动」可撤销）。",
                        VisibleWhen = nameof(IsArgsEdited)
                    }
                }
            },

            // ===== ④ 收尾说明 =====
            new DetailSection
            {
                Fields =
                {
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "本页只负责「制定一条任务」：编好点「➡ 发送到批处理」入队，真正运行在「批量处理」页。剪辑默认 -c copy 不重编码（秒级、无损，但起止会就近对齐关键帧）；要精确到帧就选「重新编码」。"
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "⚠️ GPU 编码需要 N 卡 + 支持 NVENC 的 ffmpeg；机器不支持时 ffmpeg 会直接报错，页面会如实显示原因。"
                    }
                }
            }
        },
        Actions =
        {
            // 原来这个 📂 是「输出文件」输入框右侧的内联按钮（code-behind 挂 Click）——
            // 声明式卡里统一收进 Actions 区（需求 §4.5.1：Actions = 与该卡内容强绑定的卡内动作）。
            new DetailAction
            {
                Text = "📂 选择输出文件…",
                Classes = "ghost",
                ToolTip = "挑一个输出文件；留空 = 自动命名（⛔ 绝不覆盖已有文件）",
                Command = new RelayCommand(() => PickOutputCommand?.Execute(null))
            },
            // 字幕文件（.srt / .ass）
            new DetailAction
            {
                Text = "📝 选择字幕文件…",
                Classes = "ghost",
                ToolTip = "挑一个 .srt / .ass 字幕，再把上面的「字幕方式」选成软字幕或硬字幕",
                Command = new RelayCommand(() => PickSubtitleCommand?.Execute(null))
            },
            // 探测黑边（从预览位置取 2 秒跑 cropdetect）
            new DetailAction
            {
                Text = "🖼 探测黑边",
                Classes = "ghost",
                ToolTip = "自动算出去掉黑边后的画面尺寸与起点，填进上面的裁剪框（从当前预览位置取 2 秒）",
                Command = DetectCropCommand
            },
            // 替换音轨用的音频文件
            new DetailAction
            {
                Text = "🎵 选择音频文件…",
                Classes = "ghost",
                ToolTip = "先把「音频」选成「替换音轨」，再挑一个 mp3 / m4a / wav",
                Command = new RelayCommand(() => PickAudioCommand?.Execute(null))
            }
        }
    };

    // ==================== 任务类型（剪辑 / 转码，**可多选**） ====================

    /// <summary>是否**剪辑**（截取区间）。与 <see cref="IsTranscode"/> 独立 —— 都勾 = 一条命令既切区间又转码。</summary>
    [ObservableProperty] private bool _isCut = true;

    /// <summary>是否**转码**（换编码 / 格式 / 分辨率 / 码率）。</summary>
    [ObservableProperty] private bool _isTranscode;

    /// <summary>「剪辑设置」折叠区是否展开（取消勾选时自动折叠，⛔ 折叠不影响是否执行）。</summary>
    [ObservableProperty] private bool _isCutSectionExpanded = true;

    /// <summary>「转码设置」折叠区是否展开（同上）。</summary>
    [ObservableProperty] private bool _isTranscodeSectionExpanded;

    /// <summary>区间是否参与命令（勾选剪辑时才生效）。</summary>
    public bool RangeEnabled => IsCut;

    partial void OnIsCutChanged(bool value)
    {
        IsCutSectionExpanded = value;
        OnPropertyChanged(nameof(RangeEnabled));
        RefreshCommand();
        OnPropertyChanged(nameof(CanTrial));
        NotifyToolbarChanged();
    }

    partial void OnIsTranscodeChanged(bool value)
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

        // v3.16.1：没手填过结束 → 结束滑块**停在最右**（= 整段末尾）。
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
