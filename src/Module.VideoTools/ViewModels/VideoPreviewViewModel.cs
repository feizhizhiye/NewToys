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

/// <summary>
/// 「视频工具 → 视频预览」页（v3.12 由「命令生成」页更名；View / VM 类名与文件名同步改为
/// <c>VideoPreviewView</c> / <c>VideoPreviewViewModel</c>，**菜单 key 仍是 <c>video-cut</c>** —— 它落
/// config\columnlayout.json 的页键，改名会让已有用户的详情栏开合 / 列显隐偏好失效）。
/// <para>
/// 一句话职责：**边看边制定一个任务的 ffmpeg 命令**。选文件 → 预览 / 拖三条时间条定区间 → 在详情卡里配参数
/// → 看（或改）命令 → 「➡ 发送到批处理」入队。真正「跑」由「批量处理」页负责。
/// </para>
/// <para>
/// ⚠️ **详情卡结构与「批量处理」页一致**（需求 §6.4.1 / 规范 §3.9）：分区按 ffmpeg 命令书写顺序
/// （剪辑 → 转码 → 输出 → 命令）；两个分区的**勾选框就在标题行上**；⛔ 详情卡里不放任何操作按钮，全在工具条。
/// </para>
/// <para>
/// ⚠️ **不是列表页**：不继承 <c>ListPageViewModelBase</c>，改 <c>ObservableObject, IStatusProvider</c>
/// （状态文本交给 Shell 底部状态栏）。
/// </para>
/// <para>
/// 预览为什么用 <c>-ss</c> 前置快速定位：ffmpeg 配合 <c>-ss</c> 走关键帧搜索几乎瞬间完成；
/// 精确解码到随机时间点在长视频上会明显卡顿（<c>FfmpegService.ExtractFrameAsync</c>）。
/// </para>
/// <para>
/// 🔴 **命令可编辑**：自动生成值 <see cref="CommandPreview"/> 与手改值 <see cref="EditArgs"/> 分离 ——
/// 手改后参数变化**不覆盖**手改，⛔ 手改后的命令必须**原样执行**。
/// </para>
/// </summary>
public partial class VideoPreviewViewModel : ObservableObject, IStatusProvider, IDetailPanelProvider, IDetailCardSource, IToolbarSource
{
    // ==================== 声明式工具条 + 选择器命令（/ 需求 §4.8.5） ====================
    // ⚠️ 本页 VM 不是 ListPageViewModelBase ⇒ 契约成员自己实现（其余 12 页由基类统一实现）。

    /// <summary>工具条构成变化时通知 Shell（本页工具条静态 ⇒ 不主动触发；留契约给"项随状态增删"的将来）。</summary>
    public event EventHandler? ToolbarChanged;

    /// <summary>工具条构成变了 → 通知 Shell（⛔ 只有动态构造 <see cref="Toolbar"/> 时才需要调）。</summary>
    private void NotifyToolbarChanged() => ToolbarChanged?.Invoke(this, EventArgs.Empty);

    private const string VideoFilePatterns =
        "*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.m4v;*.ts;*.mpg;*.mpeg;*.rmvb;*.3gp";

    /// <summary>📂 选择视频：源文件选择器（宿主经 <c>Core.DialogHooks</c> 注入 ⇒ VM ⛔ 不引 Avalonia UI）。</summary>
    [RelayCommand]
    private async Task PickSourceAsync()
    {
        var files = await DialogHooks.PickFilesAsync("选择视频", null, false, VideoFilePatterns);
        var path = files.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(path)) return;
        LoadSource(path);
    }

    public IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new() { Text = "📂 选择视频", Classes = "ghost", CommandPath = nameof(PickSourceCommand), ToolTip = "挑一个视频文件载入编辑器（这一页一次只编一个任务；载入后自动预览）" },
        new() { Text = "▶ 播放预览", AltText = "⏸ 停止", AltTextPath = nameof(IsPlaying), CommandPath = nameof(TogglePlayCommand), ToolTip = "类播放器：点一下自动逐帧播放预览（抽帧模拟），再点停止；也可直接拖动下面「预览位置」条手动快览" },
        new() { Text = "🧪 试跑 5 秒", CommandPath = nameof(RunTrialCommand), IsEnabledPath = nameof(CanTrial), ToolTip = "用当前全部参数（含手改命令）从「预览位置」起真转 5 秒到临时目录 —— 先验证参数和画面，再发给批处理" },
        new() { Text = "▶ 播放小样", CommandPath = nameof(PlayTrialCommand), IsVisiblePath = nameof(HasTrial), ToolTip = "用外部播放器 / ffplay 打开刚试跑出来的小样（带声音、真流畅）" },
        new() { Text = "↻ 90°", CommandPath = nameof(RotatePreviewCwCommand), IsEnabledPath = nameof(HasSource), ToolTip = "预览画面顺时针旋转 90°（纯视觉）；命令卡「旋转」字段会同步" },
        new() { Text = "↺ 90°", CommandPath = nameof(RotatePreviewCcwCommand), IsEnabledPath = nameof(HasSource), ToolTip = "预览画面逆时针旋转 90°（纯视觉）；命令卡「旋转」字段会同步" },
        new() { Text = "➡ 发送到批处理", CommandPath = nameof(SendToBatchCommand), ToolTip = "把当前这一条任务加入「批量处理」的队列（不立刻运行）" },
        new() { Text = "✔ 保存回任务", CommandPath = nameof(SaveBackToTaskCommand), IsVisiblePath = nameof(IsEditingBatchTask), ToolTip = "把当前参数写回「批量处理」里那条任务（保队列位置、不新增一条）" },
        new() { Text = "↺ 区间重置", CommandPath = nameof(ResetRangeCommand), ToolTip = "开始归零、结束留空 = 整段" },
        new() { Text = "📋 复制命令", CommandPath = nameof(CopyCommandLineCommand) },
        new() { Text = "↺ 重置为自动", CommandPath = nameof(ResetCommandCommand), IsVisiblePath = nameof(IsArgsEdited), ToolTip = "丢弃手改的命令，恢复成由参数自动生成" },
    };

    private const string PageKey = "video-cut";

    private readonly VideoTaskStore? _store;
    private readonly IAppConfigService? _cfg;
    private readonly IColumnLayoutService? _cols;

    private bool _extracting;
    private double _pendingPreview = -1;
    private int _previewIndex;

    // ==================== 类播放器「拖拽快览 / 自动播放预览」 ====================

    private bool _isPlaying;
    private DispatcherTimer? _playTimer;

    /// <summary>是否正在自动播放预览（抽帧模拟播放）。</summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (_isPlaying == value) return;
            _isPlaying = value;
            OnPropertyChanged();
        }
    }

    // ==================== 状态栏（v3.9：Shell 底部状态栏契约） ====================

    private string _statusText = "点「📂 选择视频」挑一个文件 → 拖三条时间条定区间 → 在右侧详情卡里配参数 → 「➡ 发送到批处理」。";
    public event EventHandler? StatusChanged;

    /// <summary>
    /// 状态栏文本（v3.16 §6.4.2）：**已载入视频时，每一行都带「当前文件」前缀** ——
    /// 动作反馈（复制 / 发送 / 播放…）⛔ 不许把"现在预览的是哪个文件"冲掉。
    /// <para>
    /// 写进来的是**动作消息**（各处照旧 `StatusText = "…"`），读出去的是拼好前缀的那一行；
    /// 未载入文件时前缀为空串，原样输出消息（不出现"未载入 · …"这种废话）。
    /// </para>
    /// </summary>
    /// ⚠️ setter 必须是 **public**：View（文件选择器失败提示等）会直接给它赋值，
    /// 写成 `private set` → 调用方报 CS0272「set 访问器不可访问」。
    /// </summary>
    public string StatusText
    {
        get => _statusText;
        set
        {
            var prefix = SourceBrief;
            var text = string.IsNullOrWhiteSpace(value) ? prefix
                     : prefix.Length == 0 ? value
                     : prefix + " · " + value;
            if (_statusText == text) return;
            _statusText = text;
            OnPropertyChanged();
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>状态栏前缀：🎬 文件名 · 时长 · 完整路径（未载入 = 空串）。</summary>
    private string SourceBrief
    {
        get
        {
            if (!HasSource) return "";
            var name = Path.GetFileName(SourcePath);
            var dur = Duration > 0 ? DurationDisplay : "时长未知";
            return $"🎬 {name} · {dur} · {SourcePath}";
        }
    }

    /// <summary>刷新状态栏前缀（换文件 / 探测到时长后，让"当前文件"立刻出现在状态栏上）。</summary>
    private void RefreshStatusPrefix()
    {
        if (!HasSource) return;
        StatusText = "";
    }

    // ==================== 右侧详情卡（可开合） ====================

    [ObservableProperty] private bool _isDetailVisible = true;

    // ==================== 详情面板 + 声明式详情卡（的 L2 迁移页，需求 §4.5.1） ====================

    /// <summary>
    /// 📂 选输出文件。平台文件选择器**只能在 View 层用**（VM 不引 Avalonia）⇒ 由 <c>VideoPreviewView</c>
    /// 在 <c>OnDataContextChanged</c> 里注入实现（声明式卡的动作按钮没有本页 code-behind 可挂）。
    /// </summary>
    public ICommand? PickOutputCommand { get; set; }

    /// <summary>📝 选择字幕文件（code-behind 挂真正实现）。</summary>
    public ICommand? PickSubtitleCommand { get; set; }

    /// <summary>🎵 选择替换用的音频文件（code-behind 挂真正实现）。</summary>
    public ICommand? PickAudioCommand { get; set; }

    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;

    /// <summary>声明式卡（<see cref="Card"/> 非 null）⇒ ⛔ 不再返回自绘视图；老路只留给尚未迁移的页。</summary>
    public object? DetailContent => null;

    public event EventHandler? DetailChanged;

}
