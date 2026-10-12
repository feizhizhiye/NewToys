using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
public partial class VideoPreviewViewModel : ObservableObject, IStatusProvider
{
    private const string PageKey = "video-cut";

    private readonly VideoTaskStore? _store;
    private readonly IAppConfigService? _cfg;
    private readonly IColumnLayoutService? _cols;

    /// <summary>v3.15 §6.4.2：「📸 取当前帧」抽出来的图要落 <c>data\thumbs</c>（缩略图目录由它管）。</summary>
    private readonly VideoFavoritesService? _fav;

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

    /// <summary>▤ / ❮：开合右侧详情卡（沿用 `IColumnLayoutService` 的页面级 Flag，按本页 Key 记）。</summary>
    [RelayCommand]
    private void ToggleDetail()
    {
        IsDetailVisible = !IsDetailVisible;
        _cols?.SaveFlag(PageKey, IColumnLayoutService.FlagDetailPanel, IsDetailVisible);
    }

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
    }

    partial void OnIsTranscodeChanged(bool value)
    {
        IsTranscodeSectionExpanded = value;
        RefreshCommand();
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
        PreviewRotation = 0;       // v3.19 / NT-012：换文件 → 预览旋转角度与「旋转」字段归零
        EditRotate = "";
        IsArgsEdited = false;
        OnPropertyChanged(nameof(HasSource));
        RefreshCommand();
        RefreshStatusPrefix();     // v3.16：换文件 → 状态栏前缀立刻跟上
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    partial void OnDurationChanged(double value)
    {
        OnPropertyChanged(nameof(TimelineMax));
        OnPropertyChanged(nameof(HasDuration));
        OnPropertyChanged(nameof(DurationDisplay));
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

    /// <summary>预览画面旋转角度（0 / 90 / 180 / 270；纯视觉 <c>RenderTransform</c>，⛔ 不改文件）。v3.19 / NT-012。</summary>
    [ObservableProperty] private double _previewRotation;

    public bool HasPreviewImage => PreviewImage != null;
    public string PreviewTimeDisplay => CutTask.FormatTime(PreviewTime);

    partial void OnPreviewImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasPreviewImage));

    partial void OnPreviewTimeChanged(double value)
    {
        OnPropertyChanged(nameof(PreviewTimeDisplay));
        _ = UpdatePreviewAsync(value);
    }

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
    /// 📸 取当前帧（需求 §6.4.2）：把当前拖到的这一帧用 ffmpeg 抽成 jpg，落
    /// <c>data\thumbs\{sha1(源路径)}.jpg</c>，再经 <see cref="VideoTaskStore"/> 的任务通道送「视频收藏」页建卡。
    /// <para>
    /// ⛔ **不自动跳转**：用户正在拖着看画面，别把他拽走 —— 卡片由收藏页在下次挂载时建好
    /// （取件点在 <c>VideoFavoritesView.OnAttachedToVisualTree</c>，见规范 §4.8 硬规则 8）。
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task CaptureFrameAsync()
    {
        var src = SourcePath;
        if (string.IsNullOrWhiteSpace(src) || !File.Exists(src)) { StatusText = "先「📂 选择视频」挑一个文件。"; return; }
        if (_fav == null || !_fav.IsAvailable) { StatusText = "收藏不可用（数据库未就绪）。"; return; }

        try
        {
            Directory.CreateDirectory(_fav.ThumbsDir);
            var name = VideoFavoritesService.ThumbFileName(src);
            var target = Path.Combine(_fav.ThumbsDir, name);

            // ⚠️ 先写临时文件再替换：目标 jpg 可能正被收藏页的 Bitmap 占着，直接覆盖会写失败
            var tmp = target + ".tmp.jpg";
            var ffmpeg = FfmpegService.ResolveFfmpeg(_cfg?.FfmpegPath);
            if (!await FfmpegService.ExtractFrameAsync(ffmpeg, src, PreviewTime, tmp) || !File.Exists(tmp))
            {
                StatusText = "抽帧失败：ffmpeg 没跑成功（检查「设置 → 路径」里的 ffmpeg 路径）。";
                return;
            }

            try
            {
                if (File.Exists(target)) File.Delete(target);
                File.Move(tmp, target);
            }
            catch
            {
                try { File.Delete(tmp); } catch { /* 清不掉就算了 */ }
                StatusText = "抽帧成功，但缩略图写不进去（文件被占用？）。";
                return;
            }

            _fav.Add(src, name);                   // 没收藏过就顺手建一条（同一路径永远同一张图）
            _store?.EnqueueFrame(src, name);       // 交给收藏页建卡
            StatusText = $"已取第 {CutTask.FormatTime(PreviewTime)} 这一帧 → 收藏页（{name}）；"
                         + "标签要存得住，还得在收藏页里点「💾 加入视频库」。";
        }
        catch (Exception ex) { StatusText = "取当前帧失败：" + ex.Message; }
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
                    if (info.Width is > 0) SourceWidth = info.Width.Value;    // v3.19 / NT-013：顺手记下源宽高供参考
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
    [ObservableProperty] private string _editPresetId = CutPresets.Copy;
    [ObservableProperty] private int _editCrf = 20;
    [ObservableProperty] private string _editExtraArgs = "";

    /// <summary>剪切预设下拉框的数据源（不写死在 XAML 里）。</summary>
    public IReadOnlyList<CutPresetOption> CutPresetOptions => CutPresets.All;

    /// <summary>当前剪切预设是不是「重新编码」（决定 CRF 输入框显不显示）。</summary>
    public bool IsReEncode => string.Equals(EditPresetId, CutPresets.ReEncode, StringComparison.OrdinalIgnoreCase);

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
        OnPropertyChanged(nameof(IsReEncode));
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

    /// <summary>默认宽上限（0 = 不用；源更宽才缩到它，否则保原始，v3.19 / NT-013）。</summary>
    [ObservableProperty] private int _editDefaultWidth = 1920;

    /// <summary>默认高上限（0 = 不用；配合 <see cref="EditDefaultWidth"/>）。</summary>
    [ObservableProperty] private int _editDefaultHeight = 1080;

    /// <summary>源视频宽 / 高（v3.19 / NT-013：探测后显示，供设「默认宽高上限」时参考）。</summary>
    [ObservableProperty] private int _sourceWidth;
    [ObservableProperty] private int _sourceHeight;
    partial void OnSourceWidthChanged(int value) => OnPropertyChanged(nameof(SourceResolutionDisplay));
    partial void OnSourceHeightChanged(int value) => OnPropertyChanged(nameof(SourceResolutionDisplay));

    /// <summary>「原视频宽高」显示文本（未探测到则提示）。</summary>
    public string SourceResolutionDisplay =>
        SourceWidth > 0 && SourceHeight > 0 ? $"{SourceWidth}×{SourceHeight}" : "未读取（载入后自动探测）";

    /// <summary>v3.19 / NT-013：源视频编码 / 码率 / 容器（探测时填充，供右键「视频信息」）。</summary>
    [ObservableProperty] private string _sourceCodec = "";
    [ObservableProperty] private long? _sourceBitRate;
    [ObservableProperty] private string _sourceContainerFormat = "";

    /// <summary>源文件大小（探测时顺手取）。</summary>
    [ObservableProperty] private long _sourceSize;

    /// <summary>
    /// 预览画面右键「视频信息」的多行文本（v3.19 / NT-013：编辑命令时快速查看名称/大小/编码/时长/码率/容器/分辨率/旋转）。
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
    /// v3.16：两页详情卡字段集合要一致 —— 「批量处理」页早就有这一项，这边补上。
    /// </summary>
    [ObservableProperty] private int _editMaxHeight;

    /// <summary>码率模式：<c>crf</c>（恒定质量）/ <c>bitrate</c>（目标码率）。</summary>
    [ObservableProperty] private string _editBitrateMode = "crf";

    [ObservableProperty] private int _editTranscodeCrf;
    [ObservableProperty] private string _editTargetBitrate = "";

    // ---- 旋转 / 翻转（v3.19 / NT-012）----

    /// <summary>旋转 / 翻转编辑缓冲（取值见 <see cref="RotateOptions"/>；空 = 不旋转）。</summary>
    [ObservableProperty] private string _editRotate = "";

    /// <summary>「旋转」下拉的选项（不写死在 XAML 里）。</summary>
    public IReadOnlyList<RotateOption> RotateChoices => RotateOptions.All;

    /// <summary>「旋转」下拉选中项（变化经 <see cref="OnSelectedRotateChanged"/> 回写 <see cref="EditRotate"/> 并同步预览角度）。</summary>
    [ObservableProperty] private RotateOption? _selectedRotate;

    partial void OnSelectedRotateChanged(RotateOption? value)
    {
        if (value != null) EditRotate = value.Value;
        NotifyVideoInfo();   // v3.19 / NT-013：旋转方向进了右键「视频信息」
    }

    partial void OnEditRotateChanged(string value)
    {
        RefreshCommand();
        // 旋转值 → 回带预览角度（命令卡改值 / ↻↺ 都走这里；翻转无角度 → 0）
        PreviewRotation = RotateOptions.ToAngle(value);
    }

    // ---- 转码参数变化 → 刷新命令预览（v3.19 / NT-013 补齐预览页缺口，与「批量处理」页一致）----
    // ⚠️ 预览页此前缺这些 On*Changed：改预设 / 编码器 / 分辨率 / CRF / 码率 / 容器时命令预览不刷新（批量页一直有）。

    partial void OnEditTranscodePresetIdChanged(string value)
    {
        OnPropertyChanged(nameof(EncoderPreview));
        RefreshCommand();
    }

    partial void OnEditUseGpuChanged(bool value)
    {
        OnPropertyChanged(nameof(EncoderIndex));
        OnPropertyChanged(nameof(EncoderPreview));
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

    public IReadOnlyList<TranscodePreset> TranscodePresets => TranscodePreset.All;
    public IReadOnlyList<string> ContainerOptions => FfmpegService.Containers;

    /// <summary>
    /// CPU / GPU 下拉框绑它（0 = CPU、1 = GPU）。
    /// <para>⚠️ 为什么不直接绑 bool：ComboBox 的 <c>SelectedValue</c> 走布尔转换容易踩坑
    /// （XAML 里的字面量其实是字符串），绑 <c>int</c> + <c>SelectedIndex</c> 最可靠。</para>
    /// </summary>
    public int EncoderIndex
    {
        get => EditUseGpu ? 1 : 0;
        set
        {
            EditUseGpu = value == 1;
            OnPropertyChanged();
        }
    }

    /// <summary>当前会用的编码器名（详情卡里显示出来，避免以为用了 GPU 其实没用）。</summary>
    public string EncoderPreview =>
        FfmpegService.ResolveEncoder(TranscodePreset.Find(EditTranscodePresetId).VideoCodec, EditUseGpu);

    /// <summary>码率模式下拉框绑它（0 = CRF、1 = 目标码率），理由同 <see cref="EncoderIndex"/>。</summary>
    public int BitrateModeIndex
    {
        get => string.Equals(EditBitrateMode, "bitrate", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        set
        {
            EditBitrateMode = value == 1 ? "bitrate" : "crf";
            OnPropertyChanged();
        }
    }

    /// <summary>CRF 是否用得上（crf 模式下才显示 CRF 输入框）。</summary>
    public bool UseCrf => !string.Equals(EditBitrateMode, "bitrate", StringComparison.OrdinalIgnoreCase);

    // ==================== 输出 ====================

    [ObservableProperty] private string _editOutput = "";

    partial void OnEditOutputChanged(string value) => RefreshCommand();

    // ==================== 命令：自动生成 + 可手改 ====================

    /// <summary>
    /// **将要执行的完整 ffmpeg 命令行**（自动生成；改参数即时刷新）。
    /// 与「发送到批处理」的那条同源 —— 界面上看到的就是会执行的东西，不含隐藏参数。
    /// </summary>
    public string CommandPreview
    {
        get
        {
            if (!HasSource) return "（先点「📂 选择视频」挑一个文件）";
            if (!IsCut && !IsTranscode) return "（至少勾选一种任务类型：剪辑 / 转码）";

            var ffmpeg = FfmpegService.ResolveFfmpeg(_cfg?.FfmpegPath);
            var args = BuildAutoArgs();
            args.Add(ResolveOutput());
            return FfmpegService.DescribeCommand(ffmpeg, args);
        }
    }

    /// <summary>
    /// 可编辑的**参数部分**（⛔ 不含 ffmpeg.exe、也**不含**输出路径 —— 输出由「输出文件」统一管理）。
    /// 用户一旦手改 → <see cref="IsArgsEdited"/> 置 true → 参数变化**不再覆盖**它，`↺ 重置为自动` 才恢复。
    /// </summary>
    [ObservableProperty] private string _editArgs = "";

    /// <summary>命令是否被手改过（true = 以手改文本为准执行）。</summary>
    [ObservableProperty] private bool _isArgsEdited;

    partial void OnEditArgsChanged(string value)
    {
        var edited = !string.Equals((value ?? "").Trim(), AutoArgsText, StringComparison.Ordinal);
        if (IsArgsEdited != edited) IsArgsEdited = edited;
    }

    /// <summary>当前自动生成的参数文本（与手改文本比较的基准）。</summary>
    private string AutoArgsText => string.Join(" ", BuildAutoArgs());

    /// <summary>
    /// 按当前「任务类型 + 参数」拼出 ffmpeg 参数列表（⛔ **不含**输出路径 —— 由 <see cref="ResolveOutput"/> 统一补在末尾）。
    /// <para>三种情况：
    /// <list type="bullet">
    /// <item>只剪辑 → <see cref="FfmpegService.BuildCutArgs(CutTask, string?)"/></item>
    /// <item>只转码 → <see cref="FfmpegService.BuildTranscodeArgs(TranscodeTask, string?)"/>（不带区间）</item>
    /// <item>剪辑+转码 → 转码参数 + 区间（<see cref="TranscodeTask.TrimStart"/> / <see cref="TranscodeTask.TrimEnd"/>）</item>
    /// </list></para>
    /// </summary>
    private List<string> BuildAutoArgs()
    {
        if (IsTranscode)
        {
            var t = BuildTranscodeTask();
            if (IsCut) { t.TrimStart = StartSeconds; t.TrimEnd = EndSeconds; }
            return FfmpegService.BuildTranscodeArgs(t, null);
        }
        return IsCut ? FfmpegService.BuildCutArgs(BuildCutTask(), null) : new List<string>();
    }

    /// <summary>刷新命令（未手改过 → 参数文本始终跟随参数走）。</summary>
    private void RefreshCommand()
    {
        if (!IsArgsEdited)
        {
            // ⛔ 别直接写该属性背后的私有字段（MVVMTK0034）。走属性 → 由 OnEditArgsChanged 统一判定"已手改"：
            // 这里赋的就是 AutoArgsText，比较结果必然是 false，不会把自动刷新误标成手改。
            EditArgs = AutoArgsText;
        }
        OnPropertyChanged(nameof(CommandPreview));
    }

    private static double ParseOr(string? text, double fallback) => CutTask.ParseTime(text) ?? fallback;

    /// <summary>容器扩展名：确保带点，空则回退到预设值。</summary>
    private static string NormalizeExt(string? raw, string fallback)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return fallback;
        return s.StartsWith('.') ? s : "." + s;
    }

    /// <summary>输出路径：用户填了就用它，否则按类型自动命名（绝不覆盖已有文件）。</summary>
    private string ResolveOutput()
    {
        var o = (EditOutput ?? "").Trim();
        if (o.Length > 0) return o;

        if (IsTranscode)
        {
            var preset = TranscodePreset.Find(EditTranscodePresetId);
            return FfmpegService.DefaultOutputPath(SourcePath, preset.OutputSuffix, NormalizeExt(EditContainer, preset.Extension));
        }
        return FfmpegService.DefaultOutputPath(SourcePath, "_cut", ".mp4");
    }

    /// <summary>按当前编辑缓冲拼一条剪辑任务。</summary>
    private CutTask BuildCutTask() => new()
    {
        SourcePath = SourcePath,
        Duration = Duration,
        Start = ParseOr(EditStart, 0),
        End = string.IsNullOrWhiteSpace(EditEnd) ? 0 : ParseOr(EditEnd, 0),
        PresetId = string.IsNullOrWhiteSpace(EditPresetId) ? CutPresets.Copy : EditPresetId,
        Crf = EditCrf,
        ReEncode = string.Equals(EditPresetId, CutPresets.ReEncode, StringComparison.OrdinalIgnoreCase),
        ExtraArgs = (EditExtraArgs ?? "").Trim(),
        OutputPath = ResolveOutput()
    };

    /// <summary>按当前编辑缓冲拼一条转码任务。</summary>
    private TranscodeTask BuildTranscodeTask()
    {
        var preset = TranscodePreset.Find(EditTranscodePresetId);
        return new TranscodeTask
        {
            SourcePath = SourcePath,
            PresetId = preset.Id,
            UseGpuEncoder = EditUseGpu,
            Container = NormalizeExt(EditContainer, preset.Extension),
            TargetWidth = EditTargetWidth,
            TargetHeight = EditTargetHeight,
            MaxHeight = EditMaxHeight,
            DefaultWidth = EditDefaultWidth,
            DefaultHeight = EditDefaultHeight,
            Rotate = EditRotate,
            BitrateMode = string.IsNullOrWhiteSpace(EditBitrateMode) ? "crf" : EditBitrateMode,
            Crf = EditTranscodeCrf,
            TargetBitrate = (EditTargetBitrate ?? "").Trim(),
            SourceDuration = Duration,
            OutputPath = ResolveOutput()
        };
    }

    // ==================== 操作 ====================

    /// <summary>↺ 区间重置（开始归零、结束 = 整段末尾 → 滑块停最右）。</summary>
    [RelayCommand]
    private void ResetRange()
    {
        EditStart = CutTask.FormatTime(0);
        EndSeconds = Duration > 0 ? Duration : 0;   // v3.16.1：整段 = 结束在片尾，滑块停最右（不再留空停最左）
        StatusText = "区间已重置（整段）。";
    }

    /// <summary>↻ 预览顺时针旋转 90°（纯视觉 <c>RenderTransform</c>；经 <see cref="SelectedRotate"/> 同步命令卡「旋转」字段）。v3.19 / NT-012。</summary>
    [RelayCommand]
    private void RotatePreviewCw()
    {
        var next = (PreviewRotation + 90) % 360;
        SelectedRotate = RotateOptions.Find(RotateOptions.FromAngle(next));
        StatusText = $"预览已旋转到 {next}°，命令卡「旋转」= {RotateOptions.Find(EditRotate).Label}。";
    }

    /// <summary>↺ 预览逆时针旋转 90°（纯视觉；同步命令卡「旋转」字段）。</summary>
    [RelayCommand]
    private void RotatePreviewCcw()
    {
        var next = (PreviewRotation + 270) % 360;
        SelectedRotate = RotateOptions.Find(RotateOptions.FromAngle(next));
        StatusText = $"预览已旋转到 {next}°，命令卡「旋转」= {RotateOptions.Find(EditRotate).Label}。";
    }

    /// <summary>➡ 发送到批处理：把当前这一条任务入队（剪辑 / 转码 / 剪辑+转码）。</summary>
    [RelayCommand]
    private void SendToBatch()
    {
        if (!HasSource || !File.Exists(SourcePath)) { StatusText = "先点「📂 选择视频」挑一个文件。"; return; }
        if (!IsCut && !IsTranscode) { StatusText = "至少勾选一种任务类型（剪辑 / 转码）。"; return; }
        if (_store == null) { StatusText = "任务服务未就绪。"; return; }

        var output = ResolveOutput();
        var raw = IsArgsEdited ? (EditArgs ?? "").Trim() : "";

        try
        {
            if (IsTranscode)
            {
                var t = BuildTranscodeTask();
                if (IsCut) { t.TrimStart = StartSeconds; t.TrimEnd = EndSeconds; }
                if (t.HasTrim && t.TrimEnd <= t.TrimStart) { StatusText = "结束时间必须大于开始时间。"; return; }
                t.OutputPath = output;
                t.RawArgs = raw;
                _store.AddTranscode(t);
                StatusText = $"已发送到「批量处理」：{t.Name} · {(t.HasTrim ? "剪辑+转码" : "转码")} · {t.PresetName}";
            }
            else
            {
                var c = BuildCutTask();
                if (c.End > 0 && c.End <= c.Start) { StatusText = "结束时间必须大于开始时间。"; return; }
                c.OutputPath = output;
                c.RawArgs = raw;
                _store.AddCut(c);
                StatusText = $"已发送到「批量处理」：{c.Name} · 剪辑 · {c.RangeDisplay}";
            }
        }
        catch (Exception ex) { StatusText = "发送失败：" + ex.Message; }
    }

    /// <summary>📋 复制将要执行的 ffmpeg 命令行（手改过就选手改的那条）。</summary>
    [RelayCommand]
    private async Task CopyCommandLineAsync()
    {
        var text = CommandPreview;
        if (text.StartsWith("（", StringComparison.Ordinal)) { StatusText = text; return; }
        try
        {
            var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (win?.Clipboard == null) { StatusText = "剪贴板不可用。"; return; }
            await win.Clipboard.SetTextAsync(text);
            StatusText = "已复制 ffmpeg 命令行。";
        }
        catch (Exception ex) { StatusText = "复制失败：" + ex.Message; }
    }

    /// <summary>↺ 把命令恢复成「由参数自动生成」（丢弃手改）。</summary>
    [RelayCommand]
    private void ResetCommand()
    {
        IsArgsEdited = false;
        RefreshCommand();
        StatusText = "命令已恢复为由参数自动生成。";
    }

    // ==================== 类播放器：拖拽快览 / 自动播放预览 ====================

    /// <summary>▶/⏸ 切换「自动播放预览」：开 = 按时间轴逐帧推进（抽帧模拟播放），关 = 仅手动拖时间条。</summary>
    [RelayCommand]
    private void TogglePlay()
    {
        if (!HasSource || !HasDuration) { StatusText = "先载入一个有时长的视频才能播放预览。"; return; }
        IsPlaying = !IsPlaying;
        if (IsPlaying)
        {
            if (PreviewTime >= TimelineMax - 0.05) PreviewTime = 0;   // 已到末尾 → 从头播
            StartPlayTimer();
            StatusText = "正在播放预览（抽帧模拟）… 再点一次停止。";
        }
        else
        {
            StopPlayTimer();
            StatusText = "已停止播放预览。";
        }
    }

    private void StartPlayTimer()
    {
        StopPlayTimer();
        _playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _playTimer.Tick += OnPlayTick;
        _playTimer.Start();
    }

    private void StopPlayTimer()
    {
        if (_playTimer != null)
        {
            _playTimer.Tick -= OnPlayTick;
            _playTimer.Stop();
            _playTimer = null;
        }
    }

    private void OnPlayTick(object? sender, EventArgs e)
    {
        if (!IsPlaying) { StopPlayTimer(); return; }
        var next = PreviewTime + 0.1;
        if (next >= TimelineMax)
        {
            PreviewTime = TimelineMax;
            IsPlaying = false;
            StopPlayTimer();
            StatusText = "播放预览结束。";
            return;
        }
        PreviewTime = next;
    }

    // ==================== 生命周期 ====================

    public VideoPreviewViewModel() { }

    public VideoPreviewViewModel(VideoTaskStore store, IAppConfigService cfg, IColumnLayoutService? cols = null,
                                 VideoFavoritesService? fav = null)
    {
        _store = store;
        _cfg = cfg;
        _cols = cols;
        _fav = fav;
        if (cols != null)
            IsDetailVisible = cols.LoadFlag(PageKey, IColumnLayoutService.FlagDetailPanel, true);
    }

    /// <summary>把外部（视频列表页 / code-behind 选文件）给的路径载入编辑器 —— **载入后自动预览**。</summary>
    public void LoadSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        SourcePath = path;   // 触发 OnSourcePathChanged：清预览 / 时长 / 输出 / 手改命令
        _ = PreparePreviewAsync();
    }

    /// <summary>
    /// 进入本页时取走「视频列表」页送来的文件（<c>EnqueueForCut</c>）。
    /// <para>⚠️ 本页一次只编辑**一个**任务，所以只载入第一个；其余在状态栏里如实说明。</para>
    /// </summary>
    public void ConsumePending()
    {
        if (_store == null) return;
        var pending = _store.TakePendingCut();
        if (pending.Count == 0) return;

        LoadSource(pending[0]);
        if (pending.Count > 1)
            StatusText = $"已从「视频列表」收到 {pending.Count} 个文件，先载入第 1 个（本页一次只编一个）；其余可逐条处理。";
    }
}
