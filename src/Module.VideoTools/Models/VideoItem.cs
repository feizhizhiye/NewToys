using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.VideoTools.Models;

/// <summary>
/// ffprobe 探测出来的一条媒体信息（纯数据，不含 UI / 不含路径）。
/// <para>由 <c>VideoScanner.ProbeAsync</c> 产出，既是给界面填列的来源，也是写进 SQLite 索引的内容。</para>
/// </summary>
/// <param name="Duration">时长（秒）。</param>
/// <param name="Width">视频宽（像素）。</param>
/// <param name="Height">视频高（像素）。</param>
/// <param name="Codec">视频编码名（ffprobe 的 <c>codec_name</c>，如 <c>h264</c> / <c>hevc</c>）。</param>
/// <param name="BitRate">总码率（bit/s，取容器 format 的 bit_rate；容器没写时为 null）。</param>
public sealed record VideoMediaInfo(double? Duration, int? Width, int? Height, string Codec, long? BitRate);

/// <summary>
/// 视频库列表的一行（= 磁盘上的一个视频文件）。
/// <para>
/// <see cref="Name"/> / <see cref="SizeBytes"/> / <see cref="Modified"/> 是扫描目录就能拿到的；
/// 时长 / 分辨率 / 编码 / 码率要跑一次 ffprobe（<c>VideoScanner.ProbeAsync</c>），
/// 探测结果会缓存进 <c>data\newtoys.db</c>（<c>VideoMediaIndex</c>），下次扫同一个目录直接命中、不再跑 ffprobe。
/// </para>
/// <para>
/// 本类型是 <see cref="ObservableObject"/>：批量探测时结果**逐个异步到达**，让行自己刷新，
/// 而不是每来一条就重建整个列表（几百个文件重建一次会很卡）。
/// </para>
/// </summary>
public partial class VideoItem : ObservableObject
{
    // ==================== 扫描目录即可得（不可变，一次性赋值） ====================

    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";

    /// <summary>扩展名（小写、带点，如 <c>.mp4</c>）。</summary>
    public string Extension { get; set; } = "";

    public long SizeBytes { get; set; }
    public DateTime Modified { get; set; }

    // ==================== ffprobe 探测得到（可观察） ====================

    /// <summary>时长（秒）；未探测时为 <c>null</c>。</summary>
    [ObservableProperty] private double? _duration;
    [ObservableProperty] private int? _width;
    [ObservableProperty] private int? _height;

    /// <summary>视频编码名（ffprobe 的 codec_name）。</summary>
    [ObservableProperty] private string _codec = "";

    /// <summary>总码率（bit/s）。</summary>
    [ObservableProperty] private long? _bitRate;

    /// <summary>媒体信息的读取状态（给详情栏显示：成功摘要 / 失败原因 / 未读取）。</summary>
    [ObservableProperty] private string _mediaStatus = "未读取媒体信息";

    public string Folder => System.IO.Path.GetDirectoryName(FullPath) ?? "";

    public string SizeDisplay => SizeBytes switch
    {
        < 1024 => $"{SizeBytes} B",
        < 1024 * 1024 => $"{SizeBytes / 1024.0:0.0} KB",
        < 1024L * 1024 * 1024 => $"{SizeBytes / 1024.0 / 1024.0:0.0} MB",
        _ => $"{SizeBytes / 1024.0 / 1024.0 / 1024.0:0.00} GB"
    };

    public string ModifiedDisplay => Modified.ToString("yyyy-MM-dd HH:mm");

    /// <summary>是否已经有媒体信息（缓存命中或探测成功都算）—— 批量探测只挑没有的那些。</summary>
    public bool HasMedia => Duration is > 0 || Width is > 0;

    public string DurationDisplay
    {
        get
        {
            if (Duration is not > 0) return "—";
            var t = TimeSpan.FromSeconds(Duration.Value);
            return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
        }
    }

    public string ResolutionDisplay => Width is > 0 && Height is > 0 ? $"{Width}×{Height}" : "—";

    /// <summary>编码的中文/常用叫法（h264 → H.264/AVC），认不出来就原样显示。</summary>
    public string CodecDisplay => string.IsNullOrWhiteSpace(Codec) ? "—" : FriendlyCodec(Codec);

    public string BitRateDisplay
    {
        get
        {
            var b = BitRate;
            if (b is not > 0) return "—";
            return b.Value >= 1_000_000
                ? $"{b.Value / 1_000_000.0:0.0} Mbps"
                : $"{b.Value / 1000.0:0} kbps";
        }
    }

    // ==================== 缓存用 ====================

    /// <summary>用于判断缓存是否过期的修改时间（UTC Ticks）。</summary>
    public long ModifiedUtcTicks => Modified.ToUniversalTime().Ticks;

    /// <summary>套用一条媒体信息（缓存命中 / 探测成功共用同一条路径）。</summary>
    public void ApplyMedia(VideoMediaInfo info)
    {
        if (info is null) return;
        Duration = info.Duration;
        Width = info.Width;
        Height = info.Height;
        Codec = info.Codec ?? "";
        BitRate = info.BitRate;
        MediaStatus = $"{ResolutionDisplay} · {CodecDisplay} · {BitRateDisplay} · {DurationDisplay}";
    }

    // 任一媒体字段变了 → 依赖它们的显示串也要刷新（表格列直接绑这些派生属性）。
    partial void OnDurationChanged(double? value) => NotifyDerived();
    partial void OnWidthChanged(int? value) => NotifyDerived();
    partial void OnHeightChanged(int? value) => NotifyDerived();
    partial void OnCodecChanged(string value) => NotifyDerived();
    partial void OnBitRateChanged(long? value) => NotifyDerived();

    private void NotifyDerived()
    {
        OnPropertyChanged(nameof(HasMedia));
        OnPropertyChanged(nameof(DurationDisplay));
        OnPropertyChanged(nameof(ResolutionDisplay));
        OnPropertyChanged(nameof(CodecDisplay));
        OnPropertyChanged(nameof(BitRateDisplay));
    }

    /// <summary>ffprobe 的 <c>codec_name</c> → 常见写法（不认识的编码原样返回）。</summary>
    private static string FriendlyCodec(string codec) => codec.ToLowerInvariant() switch
    {
        "h264" => "H.264/AVC",
        "hevc" or "h265" => "H.265/HEVC",
        "av1" => "AV1",
        "vp8" => "VP8",
        "vp9" => "VP9",
        "mpeg4" => "MPEG-4",
        "mpeg2video" => "MPEG-2",
        "mpeg1video" => "MPEG-1",
        "vc1" => "VC-1",
        "wmv3" => "WMV3",
        "msmpeg4v3" => "MS MPEG-4 v3",
        "theora" => "Theora",
        "prores" => "ProRes",
        "dnxhd" => "DNxHD",
        "flv1" => "Sorenson H.263",
        _ => codec
    };
}
