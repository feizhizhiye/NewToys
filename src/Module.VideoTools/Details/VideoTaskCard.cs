using System.Windows.Input;
using NewToys.Shared.Details;

namespace NewToys.VideoTools.Details;

/// <summary>
/// 「视频预览」与「批量处理」两页详情卡的命令入口。
/// <para>两页各自把可用的命令塞进来，为 <c>null</c> 的按钮整条不渲染 —— 卡定义不必知道谁提供了什么。</para>
/// </summary>
public sealed class VideoCardCommands
{
    public ICommand? PickOutput { get; init; }
    public ICommand? PickSubtitle { get; init; }
    public ICommand? PickAudio { get; init; }
    public ICommand? DetectCrop { get; init; }

    /// <summary>🧪 试跑选中（仅批量处理页有：按当前参数先转一小段）。</summary>
    public ICommand? Trial { get; init; }

    /// <summary>💾 保存命令（仅批量处理页有：只把手改的参数串写回任务）。</summary>
    public ICommand? SaveCommandLine { get; init; }
}

/// <summary>
/// 视频剪辑 / 转码<b>详情卡的唯一来源</b>。
/// <para>
/// Why：预览页与批量页本该是同一张卡（同一批参数、同一套文案），此前两页各写一份
/// <c>BuildCard()</c>，改一处忘另一处就出现「同一功能两副面孔」。这里把节 / 字段 / 文案 / 显隐
/// 收成一份，两页只提供差异项：
/// </para>
/// <list type="bullet">
/// <item>提交时机（批量走「💾 保存修改」，预览失焦即生效）</item>
/// <item>批量页多一个「📄 任务状态」节</item>
/// <item>空态文案</item>
/// </list>
/// <para>
/// 🔴 约束：两页 VM 必须提供这里出现的<b>同名属性</b>（<c>ValuePath</c> / <c>VisibleWhen</c> /
/// <c>OptionsPath</c> / <c>IndexPath</c>）。由 <c>skill/scripts/check-video-card.ps1</c> 静态校验，
/// ⛔ 不靠人记。
/// </para>
/// </summary>
public static class VideoTaskCard
{
    /// <summary>详情卡里会引用到、但只有批量页有的路径前缀（其余路径两页都必须有）。</summary>
    public static readonly string[] BatchOnlyPrefixes = { "DetailTask" };

    /// <summary>
    /// 建卡。
    /// </summary>
    /// <param name="commands">卡内动作（<c>null</c> 的按钮不渲染）。</param>
    /// <param name="explicitCommit">
    /// <c>true</c> = 输入框改动只留在缓冲里、由工具条「💾 保存修改」统一落库（批量页）；
    /// <c>false</c> = 失焦即生效（预览页）。
    /// </param>
    /// <param name="emptyText">右侧没选中任何内容时的提示。</param>
    /// <param name="includeTaskState">是否追加「📄 任务状态」节（仅批量页）。</param>
    public static DetailCardDescriptor Build(
        VideoCardCommands commands,
        bool explicitCommit,
        string emptyText,
        bool includeTaskState)
    {
        var commit = explicitCommit ? EditCommitMode.Explicit : EditCommitMode.OnBlur;

        var card = new DetailCardDescriptor
        {
            BodyVisiblePath = BodyVisible,
            EmptyText = emptyText,
            Sections =
            {
                // ===== 台头：类型 / 文件（不可编辑） =====
                new DetailSection
                {
                    Fields =
                    {
                        new DetailField
                        {
                            Label = "类型 / 文件",
                            ValuePath = DetailTitle,
                            Kind = FieldKind.Text,
                            Selectable = true
                        }
                    }
                },

                // ===== ① 剪辑 =====
                new DetailSection
                {
                    Header = "✂ 剪辑（截区间）",
                    TogglePath = EditIsCut,
                    ToggleEnabledPath = HasSelection,
                    IsExpandedPath = IsCutSectionExpanded,
                    Fields =
                    {
                        new DetailField
                        {
                            Label = "开始（HH:MM:SS.mmm 或秒；预览页也可拖时间条）",
                            ValuePath = EditStart,
                            Kind = FieldKind.Text,
                            Commit = commit,
                            Mono = true,
                            Placeholder = "00:00:00.000"
                        },
                        new DetailField
                        {
                            Label = "结束（留空 = 到末尾）",
                            ValuePath = EditEnd,
                            Kind = FieldKind.Text,
                            Commit = commit,
                            Mono = true,
                            Placeholder = "00:00:10.000"
                        },
                        new DetailField
                        {
                            Label = "多段剪切（每行一段：开始-结束；≥2 段 = 切完再拼成一条）",
                            ValuePath = EditSegmentsText,
                            Kind = FieldKind.Text,
                            Commit = commit,
                            Mono = true,
                            MultiLine = true,
                            Placeholder = "00:00:05-00:00:12\n00:00:20-00:00:28"
                        },
                        new DetailField
                        {
                            Kind = FieldKind.Hint,
                            ValuePath = SegmentsHint,
                            VisibleWhen = HasSegmentsHint
                        },
                        new DetailField
                        {
                            Label = "剪切预设",
                            Kind = FieldKind.Select,
                            OptionsPath = CutPresetOptions,
                            OptionValuePath = "Id",
                            OptionDisplayPath = "Name",
                            ValuePath = EditCutPresetId
                        },
                        new DetailField
                        {
                            Label = "重新编码时的 CRF（18~28 常用，越小越清晰）",
                            ValuePath = EditCutCrf,
                            Kind = FieldKind.Text,
                            Commit = commit,
                            Mono = true,
                            VisibleWhen = IsCutReEncode
                        },
                        new DetailField
                        {
                            Label = "自定义 ffmpeg 参数（追加在预设之后 → 可覆盖预设）",
                            ValuePath = EditExtraArgs,
                            Kind = FieldKind.Text,
                            Commit = commit,
                            Mono = true,
                            MultiLine = true,
                            Placeholder = "例：-vf scale=1280:-2"
                        }
                    }
                },

                // ===== ② 转码基础 =====
                new DetailSection
                {
                    Header = "🔁 转码（换编码 / 格式）",
                    TogglePath = EditIsTranscode,
                    ToggleEnabledPath = HasSelection,
                    IsExpandedPath = IsTranscodeSectionExpanded,
                    Fields =
                    {
                        new DetailField
                        {
                            Label = "编码预设",
                            Kind = FieldKind.Select,
                            OptionsPath = TranscodePresets,
                            OptionValuePath = "Id",
                            OptionDisplayPath = "Name",
                            ValuePath = EditTranscodePresetId
                        },
                        new DetailField
                        {
                            Kind = FieldKind.Hint,
                            Prefix = "收益预估：",
                            ValuePath = DetailAdvice,
                            VisibleWhen = HasDetailAdvice
                        },
                        new DetailField
                        {
                            Label = "编码器",
                            Kind = FieldKind.Select,
                            IndexPath = EncoderIndex,
                            LiteralOptions = new[] { "CPU（软件编码，兼容性最好）", "GPU（NVENC，N 卡）" }
                        },
                        new DetailField
                        {
                            Kind = FieldKind.Hint,
                            Prefix = "实际会用：",
                            ValuePath = SelectedEncoder
                        },
                        new DetailField
                        {
                            Label = "容器 / 格式（决定输出文件后缀）",
                            Kind = FieldKind.Select,
                            OptionsPath = ContainerOptions,
                            ValuePath = EditContainer
                        },
                        new DetailField
                        {
                            Label = "分辨率（宽 × 高；0 = 按比例 / 不改）",
                            ValuePath = EditTargetWidth,
                            Kind = FieldKind.Pair,
                            Commit = commit,
                            Mono = true,
                            Placeholder = "宽",
                            SecondValuePath = EditTargetHeight,
                            SecondPlaceholder = "高"
                        },
                        new DetailField
                        {
                            Kind = FieldKind.Hint,
                            Prefix = "原视频宽高：",
                            ValuePath = SourceResolutionDisplay,
                            VisibleWhen = HasSourceResolution
                        },
                        new DetailField
                        {
                            Label = "默认宽高上限（源更大才缩，只缩不放）",
                            ValuePath = EditDefaultWidth,
                            Kind = FieldKind.Pair,
                            Commit = commit,
                            Mono = true,
                            Placeholder = "宽",
                            SecondValuePath = EditDefaultHeight,
                            SecondPlaceholder = "高"
                        },
                        new DetailField
                        {
                            Label = "旋转 / 翻转（视频方向不对时用）",
                            Kind = FieldKind.Select,
                            OptionsPath = RotateChoices,
                            OptionDisplayPath = "Label",
                            ValuePath = SelectedRotate
                        }
                    }
                },

                // ===== ③ 画面（裁剪 / 字幕；只对转码任务有意义） =====
                new DetailSection
                {
                    Header = "🎨 画面（裁剪 / 字幕）",
                    VisiblePath = EditIsTranscode,
                    IsExpandedPath = IsCropSectionExpanded,
                    Fields =
                    {
                        new DetailField
                        {
                            Label = "裁剪（宽 × 高；0 = 不裁剪。可用下面「🖼 探测黑边」自动填）",
                            ValuePath = EditCropWidth,
                            Kind = FieldKind.Pair,
                            Commit = commit,
                            Mono = true,
                            Placeholder = "宽",
                            SecondValuePath = EditCropHeight,
                            SecondPlaceholder = "高"
                        },
                        new DetailField
                        {
                            Label = "裁剪起点（X × Y，左上角；0,0 = 贴左上）",
                            ValuePath = EditCropX,
                            Kind = FieldKind.Pair,
                            Commit = commit,
                            Mono = true,
                            Placeholder = "X",
                            SecondValuePath = EditCropY,
                            SecondPlaceholder = "Y"
                        },
                        new DetailField { Kind = FieldKind.Divider },
                        new DetailField
                        {
                            Label = "字幕文件（.srt / .ass；留空 = 不加字幕）",
                            ValuePath = EditSubtitlePath,
                            Kind = FieldKind.Text,
                            Commit = commit,
                            Mono = true
                        },
                        new DetailField
                        {
                            Label = "字幕方式",
                            Kind = FieldKind.Select,
                            OptionsPath = SubtitleModeChoices,
                            OptionValuePath = "Value",
                            OptionDisplayPath = "Label",
                            ValuePath = EditSubtitleMode
                        },
                        new DetailField
                        {
                            Kind = FieldKind.Hint,
                            ValuePath = SoftSubtitleHint,
                            VisibleWhen = IsSoftSubtitleEdit
                        },
                        new DetailField
                        {
                            Label = "硬字幕字号（像素；0 = 用字幕自带样式）",
                            ValuePath = EditSubtitleFontSize,
                            Kind = FieldKind.Text,
                            Commit = commit,
                            Mono = true,
                            VisibleWhen = IsHardSubtitleEdit
                        },
                        new DetailField
                        {
                            Label = "硬字幕底部边距（像素；0 = 不设）",
                            ValuePath = EditSubtitleMarginV,
                            Kind = FieldKind.Text,
                            Commit = commit,
                            Mono = true,
                            VisibleWhen = IsHardSubtitleEdit
                        }
                    }
                },

                // ===== ④ 音轨（只对转码任务有意义） =====
                new DetailSection
                {
                    Header = "🔊 音轨",
                    VisiblePath = EditIsTranscode,
                    IsExpandedPath = IsAudioSectionExpanded,
                    Fields =
                    {
                        new DetailField
                        {
                            Label = "音频",
                            Kind = FieldKind.Select,
                            OptionsPath = AudioModeChoices,
                            OptionValuePath = "Value",
                            OptionDisplayPath = "Label",
                            ValuePath = EditAudioMode
                        },
                        new DetailField
                        {
                            Label = "音量增益（dB；正 = 变响、负 = 变轻，±6 已很明显）",
                            ValuePath = EditVolumeDb,
                            Kind = FieldKind.Text,
                            Commit = commit,
                            Mono = true,
                            Placeholder = "6",
                            VisibleWhen = IsVolumeModeEdit
                        },
                        new DetailField
                        {
                            Label = "替换成的音频文件（用下面「🎵 选择音频文件…」挑）",
                            ValuePath = EditAudioPath,
                            Kind = FieldKind.Text,
                            Commit = commit,
                            Mono = true,
                            VisibleWhen = IsReplaceAudioEdit
                        }
                    }
                },

                // ===== ⑤ 码率与体积（只对转码任务有意义） =====
                new DetailSection
                {
                    Header = "🎚 码率与体积",
                    VisiblePath = EditIsTranscode,
                    IsExpandedPath = IsBitrateSectionExpanded,
                    Fields =
                    {
                        new DetailField
                        {
                            Label = "码率模式",
                            Kind = FieldKind.Select,
                            IndexPath = BitrateModeIndex,
                            LiteralOptions = new[] { "CRF（恒定质量，推荐）", "目标码率（如 5000k / 5M）" }
                        },
                        new DetailField
                        {
                            Label = "CRF（0 = 用预设的值；18~28 常用）",
                            ValuePath = EditTranscodeCrf,
                            Kind = FieldKind.Text,
                            Commit = commit,
                            Mono = true,
                            VisibleWhen = UseCrf
                        },
                        new DetailField
                        {
                            Label = "目标码率（如 5000k / 5M）",
                            ValuePath = EditTargetBitrate,
                            Kind = FieldKind.Text,
                            Commit = commit,
                            Mono = true,
                            Placeholder = "5000k",
                            VisibleWhen = "!" + UseCrf
                        },
                        new DetailField
                        {
                            Label = "目标体积（MB；0 = 不用。填了优先于 CRF / 目标码率）",
                            ValuePath = EditTargetSizeMb,
                            Kind = FieldKind.Text,
                            Commit = commit,
                            Mono = true,
                            Placeholder = "50"
                        },
                        new DetailField
                        {
                            Kind = FieldKind.Hint,
                            ValuePath = TargetSizeHint,
                            VisibleWhen = HasTargetSizeHint
                        },
                        new DetailField
                        {
                            Label = "GIF 帧率（12 常用；越大越流畅、体积越大）",
                            ValuePath = EditFrameRate,
                            Kind = FieldKind.Text,
                            Commit = commit,
                            Mono = true,
                            VisibleWhen = IsGifPreset
                        },
                        new DetailField
                        {
                            Label = "GIF 宽度（像素；0 = 保持原宽，体积会很大）",
                            ValuePath = EditGifWidth,
                            Kind = FieldKind.Text,
                            Commit = commit,
                            Mono = true,
                            VisibleWhen = IsGifPreset
                        }
                    }
                },

                // ===== ⑥ 输出文件 =====
                new DetailSection
                {
                    Header = "📂 输出文件",
                    Fields =
                    {
                        new DetailField
                        {
                            Label = "路径（留空 = 自动命名，绝不覆盖已有文件）",
                            ValuePath = EditOutput,
                            Kind = FieldKind.Text,
                            Commit = commit,
                            Mono = true
                        }
                    }
                },

                // ===== ⑦ 命令（手改 + 预检） =====
                new DetailSection
                {
                    Header = "⌨ ffmpeg 参数（可直接改）",
                    IsExpandedPath = IsCommandSectionExpanded,
                    Fields =
                    {
                        new DetailField
                        {
                            Kind = FieldKind.Hint,
                            ValuePath = LintText,
                            VisibleWhen = HasLint
                        },
                        new DetailField
                        {
                            ValuePath = EditArgs,
                            Kind = FieldKind.Text,
                            Commit = commit,
                            Mono = true,
                            MultiLine = true
                        },
                        new DetailField
                        {
                            Kind = FieldKind.Hint,
                            Hint = "✏ 已手改：运行时以这里为准（工具条上「↺ 重置为自动」可撤销）。",
                            VisibleWhen = IsArgsEdited
                        }
                    }
                }
            }
        };

        if (includeTaskState)
        {
            card.Sections.Add(new DetailSection
            {
                Header = "📄 任务状态",
                Fields =
                {
                    new DetailField
                    {
                        Label = "状态",
                        ValuePath = DetailTask + ".StatusDisplay",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        ValuePath = DetailTask + ".Message"
                    },
                    new DetailField
                    {
                        Label = "完成时间 / 耗时",
                        ValuePath = DetailTask + ".FinishedDisplay",
                        Kind = FieldKind.Text,
                        Selectable = true
                    }
                }
            });
        }

        // ===== 卡内动作（与该卡内容强绑定的才放这里；页面级功能走工具条） =====
        if (commands.PickOutput != null)
            card.Actions.Add(new DetailAction
            {
                Text = "📂 选输出",
                Classes = "ghost",
                ToolTip = "挑一个输出文件；留空 = 按预设自动命名（⛔ 绝不覆盖已有文件）",
                Command = commands.PickOutput
            });

        if (commands.PickSubtitle != null)
            card.Actions.Add(new DetailAction
            {
                Text = "📝 选字幕",
                Classes = "ghost",
                IsVisiblePath = EditIsTranscode,
                ToolTip = "挑一个 .srt / .ass 字幕，再把「字幕方式」选成软字幕或硬字幕",
                Command = commands.PickSubtitle
            });

        if (commands.Trial != null)
            card.Actions.Add(new DetailAction
            {
                Text = "🧪 试跑选中",
                Classes = "ghost",
                ToolTip = "用当前参数真转一小段（有区间就只跑区间，否则前 5 秒）到临时目录，成功自动播放 —— 验完再批量跑",
                Command = commands.Trial
            });

        if (commands.DetectCrop != null)
            card.Actions.Add(new DetailAction
            {
                Text = "🖼 探测黑边",
                Classes = "ghost",
                IsVisiblePath = EditIsTranscode,
                ToolTip = "从源文件取 2 秒跑 cropdetect，把结果填进「🎨 画面」的裁剪框",
                Command = commands.DetectCrop
            });

        if (commands.PickAudio != null)
            card.Actions.Add(new DetailAction
            {
                Text = "🎵 选音频",
                Classes = "ghost",
                IsVisiblePath = EditIsTranscode,
                ToolTip = "先把「音频」选成「替换音轨」，再挑一个 mp3 / m4a / wav",
                Command = commands.PickAudio
            });

        if (commands.SaveCommandLine != null)
            card.Actions.Add(new DetailAction
            {
                Text = "💾 保存命令",
                Classes = "ghost",
                ToolTip = "把「⌨ ffmpeg 参数」框里的内容直接写回选中的任务（跑的时候以它为准）；取消手改用工具条「↺ 重置为自动」",
                Command = commands.SaveCommandLine
            });

        return card;
    }

    // ⚠️ 下面这些是卡里引用的**属性名**：除 BatchOnlyPrefixes 里的前缀外，其余必须同时存在于
    // VideoPreviewViewModel 与 VideoBatchViewModel（由 check-video-card.ps1 静态校验，⛔ 不靠人记）。
    public const string BodyVisible = "ShowDetailBody";
    public const string DetailTitle = "DetailTitle";
    public const string DetailTask = "DetailTask";
    public const string HasSelection = "HasSelection";
    public const string EditIsCut = "EditIsCut";
    public const string EditIsTranscode = "EditIsTranscode";
    public const string IsCutSectionExpanded = "IsCutSectionExpanded";
    public const string IsTranscodeSectionExpanded = "IsTranscodeSectionExpanded";
    public const string EditStart = "EditStart";
    public const string EditEnd = "EditEnd";
    public const string EditSegmentsText = "EditSegmentsText";
    public const string SegmentsHint = "SegmentsHint";
    public const string HasSegmentsHint = "HasSegmentsHint";
    public const string CutPresetOptions = "CutPresetOptions";
    public const string EditCutPresetId = "EditCutPresetId";
    public const string EditCutCrf = "EditCutCrf";
    public const string IsCutReEncode = "IsCutReEncode";
    public const string EditExtraArgs = "EditExtraArgs";
    public const string TranscodePresets = "TranscodePresets";
    public const string EditTranscodePresetId = "EditTranscodePresetId";
    public const string EncoderIndex = "EncoderIndex";
    public const string SelectedEncoder = "SelectedEncoder";
    public const string ContainerOptions = "ContainerOptions";
    public const string EditContainer = "EditContainer";
    public const string EditTargetWidth = "EditTargetWidth";
    public const string EditTargetHeight = "EditTargetHeight";
    public const string SourceResolutionDisplay = "SourceResolutionDisplay";
    public const string HasSourceResolution = "HasSourceResolution";

    /// <summary>转码前收益预估那一行（批量页有值；预览页恒隐藏）。</summary>
    public const string DetailAdvice = "DetailAdvice";

    /// <summary>是否显示收益预估行。</summary>
    public const string HasDetailAdvice = "HasDetailAdvice";
    public const string EditDefaultWidth = "EditDefaultWidth";
    public const string EditDefaultHeight = "EditDefaultHeight";
    public const string RotateChoices = "RotateChoices";
    public const string SelectedRotate = "SelectedRotate";
    public const string IsCropSectionExpanded = "IsCropSectionExpanded";
    public const string EditCropWidth = "EditCropWidth";
    public const string EditCropHeight = "EditCropHeight";
    public const string EditCropX = "EditCropX";
    public const string EditCropY = "EditCropY";
    public const string EditSubtitlePath = "EditSubtitlePath";
    public const string SubtitleModeChoices = "SubtitleModeChoices";
    public const string EditSubtitleMode = "EditSubtitleMode";
    public const string SoftSubtitleHint = "SoftSubtitleHint";
    public const string IsSoftSubtitleEdit = "IsSoftSubtitleEdit";
    public const string EditSubtitleFontSize = "EditSubtitleFontSize";
    public const string EditSubtitleMarginV = "EditSubtitleMarginV";
    public const string IsHardSubtitleEdit = "IsHardSubtitleEdit";
    public const string IsAudioSectionExpanded = "IsAudioSectionExpanded";
    public const string AudioModeChoices = "AudioModeChoices";
    public const string EditAudioMode = "EditAudioMode";
    public const string EditVolumeDb = "EditVolumeDb";
    public const string IsVolumeModeEdit = "IsVolumeModeEdit";
    public const string EditAudioPath = "EditAudioPath";
    public const string IsReplaceAudioEdit = "IsReplaceAudioEdit";
    public const string IsBitrateSectionExpanded = "IsBitrateSectionExpanded";
    public const string BitrateModeIndex = "BitrateModeIndex";
    public const string EditTranscodeCrf = "EditTranscodeCrf";
    public const string UseCrf = "UseCrf";
    public const string EditTargetBitrate = "EditTargetBitrate";
    public const string EditTargetSizeMb = "EditTargetSizeMb";
    public const string TargetSizeHint = "TargetSizeHint";
    public const string HasTargetSizeHint = "HasTargetSizeHint";
    public const string IsGifPreset = "IsGifPreset";
    public const string EditFrameRate = "EditFrameRate";
    public const string EditGifWidth = "EditGifWidth";
    public const string EditOutput = "EditOutput";
    public const string IsCommandSectionExpanded = "IsCommandSectionExpanded";
    public const string LintText = "LintText";
    public const string HasLint = "HasLint";
    public const string EditArgs = "EditArgs";
    public const string IsArgsEdited = "IsArgsEdited";
}
