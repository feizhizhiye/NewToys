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

    /// <summary>
    /// 工具条末位的详情开关 → 走本命令。
    /// <para>⛔ 不能直接用基类的 <c>ToggleDetailCommand</c>：它只翻 <c>IsDetailVisible</c>，**不通知 Shell**，
    /// Shell 的右列（由 <c>DetailCardHost</c> 承载）就不会跟着出现 / 消失。</para>
    /// </summary>
    [RelayCommand]
    private void ToggleDetailPanel()
    {
        ToggleDetailCommand.Execute(null);   // 基类逻辑：翻转 + 落盘 + 展开时没选中就选第一项
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>右键「▤ 显示详情栏」→ 走本命令（同上：基类逻辑 + 通知 Shell）。</summary>
    [RelayCommand]
    private void OpenDetailPanel()
    {
        ShowDetailCommand.Execute(null);     // 基类逻辑：确保展开并定位到当前选中项
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    // —— 显式实现接口（⛔ 不写成 public 方法：基类已有同名的 private [RelayCommand] ToggleDetail，隐式实现会撞名） ——
    void IDetailPanelProvider.ToggleDetail() => ToggleDetailPanelCommand.Execute(null);
    void IDetailPanelProvider.OpenDetail() => OpenDetailPanelCommand.Execute(null);

    private DetailCardDescriptor? _card;

    /// <summary>
    /// 本页详情卡描述符（**C# 对象字面量**，⛔ 不做运行时 JSON / XAML 加载）。
    /// <para>结构与迁移前的页内自绘卡片 1:1 对应：剪辑 / 转码两个可折叠分区（标题带勾选框）+ 输出 + 命令 + 处理前后对比。</para>
    /// </summary>
    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        EmptyText = "在左侧选中一个任务：这里可以改它的参数（剪辑 = 区间 / 预设；转码 = 预设 / 编码器 / CRF / 分辨率），改完点工具条上的「💾 保存修改」。",
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
                        ValuePath = "DetailTask.Name",
                        Kind = FieldKind.Text,
                        Selectable = true
                    }
                }
            },

            // ===== ① 剪辑 =====
            new DetailSection
            {
                Header = "✂ 剪辑（截区间）",
                TogglePath = nameof(EditIsCut),
                ToggleEnabledPath = nameof(HasSelection),
                IsExpandedPath = nameof(IsCutSectionExpanded),
                Fields =
                {
                    new DetailField
                    {
                        Label = "开始（HH:MM:SS.mmm 或秒）",
                        ValuePath = nameof(EditStart),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.Explicit,
                        Placeholder = "00:00:00.000"
                    },
                    new DetailField
                    {
                        Label = "结束（留空 = 到末尾）",
                        ValuePath = nameof(EditEnd),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.Explicit,
                        Placeholder = "00:00:10.000"
                    },
                    // 多段剪切 + 拼接（≥2 段时忽略上面的开始 / 结束）
                    new DetailField
                    {
                        Label = "多段剪切（每行一段：开始-结束；≥2 段 = 切完再拼成一条）",
                        ValuePath = nameof(EditSegmentsText),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.Explicit,
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
                        ValuePath = nameof(EditCutPresetId)
                    },
                    new DetailField
                    {
                        Label = "重新编码时的 CRF（18~28 常用，越小越清晰）",
                        ValuePath = nameof(EditCutCrf),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.Explicit,
                        Mono = true,
                        VisibleWhen = nameof(IsCutReEncode)
                    },
                    new DetailField
                    {
                        Label = "自定义 ffmpeg 参数（追加在预设之后 → 可覆盖预设）",
                        ValuePath = nameof(EditExtraArgs),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.Explicit,
                        Mono = true,
                        MultiLine = true,
                        Placeholder = "例：-vf scale=1280:-2"
                    }
                }
            },

            // ===== ② 转码 =====
            new DetailSection
            {
                Header = "🔁 转码（换编码 / 格式）",
                TogglePath = nameof(EditIsTranscode),
                ToggleEnabledPath = nameof(HasSelection),
                IsExpandedPath = nameof(IsTranscodeSectionExpanded),
                Fields =
                {
                    new DetailField
                    {
                        Label = "编码预设",
                        Kind = FieldKind.Select,
                        OptionsPath = nameof(Presets),
                        OptionValuePath = "Id",
                        OptionDisplayPath = "Name",
                        ValuePath = nameof(EditPresetId)
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
                        ValuePath = nameof(SelectedEncoder)
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
                        Commit = EditCommitMode.Explicit,
                        Mono = true,
                        Placeholder = "宽",
                        SecondValuePath = nameof(EditTargetHeight),
                        SecondPlaceholder = "高"
                    },
                    new DetailField
                    {
                        Label = "默认宽高上限（源更大才缩，只缩不放）",
                        ValuePath = nameof(EditDefaultWidth),
                        Kind = FieldKind.Pair,
                        Commit = EditCommitMode.Explicit,
                        Mono = true,
                        Placeholder = "宽",
                        SecondValuePath = nameof(EditDefaultHeight),
                        SecondPlaceholder = "高"
                    },
                    new DetailField
                    {
                        Label = "旋转 / 翻转（视频方向不对时用）",
                        Kind = FieldKind.Select,
                        OptionsPath = nameof(RotateChoices),
                        OptionDisplayPath = "Label",
                        ValuePath = nameof(SelectedRotate)
                    },
                    // ===== 裁剪=====
                    new DetailField { Kind = FieldKind.Divider },
                    new DetailField
                    {
                        Label = "裁剪（宽 × 高；0 = 不裁剪。可用下面「🖼 探测黑边」自动填）",
                        ValuePath = nameof(EditCropWidth),
                        Kind = FieldKind.Pair,
                        Commit = EditCommitMode.Explicit,
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
                        Commit = EditCommitMode.Explicit,
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
                        Commit = EditCommitMode.Explicit,
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
                        Commit = EditCommitMode.Explicit,
                        Mono = true,
                        VisibleWhen = nameof(IsHardSubtitleEdit)
                    },
                    new DetailField
                    {
                        Label = "硬字幕底部边距（像素；0 = 不设）",
                        ValuePath = nameof(EditSubtitleMarginV),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.Explicit,
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
                        Commit = EditCommitMode.Explicit,
                        Mono = true,
                        Placeholder = "6",
                        VisibleWhen = nameof(IsVolumeModeEdit)
                    },
                    new DetailField
                    {
                        Label = "替换成的音频文件（用下面「🎵 选择音频文件…」挑）",
                        ValuePath = nameof(EditAudioPath),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.Explicit,
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
                        Label = "CRF（0 = 用预设的值；18~28 常用）",
                        ValuePath = nameof(EditCrf),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.Explicit,
                        Mono = true,
                        VisibleWhen = nameof(UseCrf)
                    },
                    new DetailField
                    {
                        Label = "目标码率（如 5000k / 5M）",
                        ValuePath = nameof(EditTargetBitrate),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.Explicit,
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
                        Commit = EditCommitMode.Explicit,
                        Mono = true,
                        Placeholder = "50"
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        ValuePath = nameof(TargetSizeHint),
                        VisibleWhen = nameof(HasTargetSizeHint)
                    },
                    // 仅 GIF 预设可见
                    new DetailField
                    {
                        Label = "GIF 帧率（12 常用；越大越流畅、体积越大）",
                        ValuePath = nameof(EditFrameRate),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.Explicit,
                        Mono = true,
                        VisibleWhen = nameof(IsGifPreset)
                    },
                    new DetailField
                    {
                        Label = "GIF 宽度（像素；0 = 保持原宽，体积会很大）",
                        ValuePath = nameof(EditGifWidth),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.Explicit,
                        Mono = true,
                        VisibleWhen = nameof(IsGifPreset)
                    }
                }
            },

            // ===== ③ 输出 + 命令（无分区标题） =====
            new DetailSection
            {
                Fields =
                {
                    new DetailField
                    {
                        Label = "输出文件（留空 = 自动命名，绝不覆盖已有文件）",
                        ValuePath = nameof(EditOutput),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.Explicit,
                        Mono = true
                    },
                    new DetailField { Kind = FieldKind.Divider },
                    new DetailField
                    {
                        Label = "将要执行的命令（改参数即时刷新，实际跑的就是这条）",
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
                        Commit = EditCommitMode.Explicit,
                        Mono = true,
                        MultiLine = true
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "✎ 已手改：运行时以这里为准（工具条上「↺ 重置为自动」可撤销）。",
                        VisibleWhen = nameof(IsArgsEdited)
                    }
                }
            },

            // ===== ④ 处理前 → 处理后 + 任务状态 =====
            new DetailSection
            {
                Header = "处理前 → 处理后",
                Fields =
                {
                    new DetailField
                    {
                        Kind = FieldKind.List,
                        OptionsPath = nameof(CompareRows),
                        ItemLabelPath = "Label",
                        ItemTextPath = "Before",
                        ItemSeparator = "→",
                        ItemSubPath = "After",
                        Hint = "「后」这一列在跑完之后才有值（转码会自动用 ffprobe 探测输出文件）。"
                    },
                    new DetailField
                    {
                        Label = "任务状态",
                        ValuePath = "DetailTask.StatusDisplay",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        ValuePath = "DetailTask.Message"
                    }
                }
            },

            // ===== ⑤ 收尾说明 =====
            new DetailSection
            {
                Fields =
                {
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "任务只在这里排队：改完参数点工具条上的「💾 保存修改」，要跑的时候点「▶▶ 批量运行」。任务清单存 config\\video-tasks.json，关掉软件再回来还在。"
                    }
                }
            }
        },
        Actions =
        {
            // 📂 收进 Actions 区：声明式卡的输入框在 Shell 侧，页面 code-behind 够不着
            //（需求 §4.5.1：Actions = 与该卡内容强绑定的卡内动作）。
            new DetailAction
            {
                Text = "📂 选择输出文件…",
                Classes = "ghost",
                ToolTip = "挑一个输出文件；留空 = 按预设自动命名（⛔ 绝不覆盖已有文件）",
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
            // 先用小样验证这条任务的参数（不改状态、不落正式输出）
            new DetailAction
            {
                Text = "🧪 试跑选中",
                Classes = "ghost",
                ToolTip = "用当前参数真转一小段（有区间就只跑区间，否则前 5 秒）到临时目录，成功自动播放 —— 验完再批量跑",
                Command = TrialSelectedCommand
            },
            // 探测黑边（填进编辑缓冲，保存后才写回任务）
            new DetailAction
            {
                Text = "🖼 探测黑边",
                Classes = "ghost",
                ToolTip = "从源文件（有区间就从区间起点）取 2 秒跑 cropdetect，把结果填进上面的裁剪框",
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

}
