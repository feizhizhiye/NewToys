using System;

namespace NewToys.VideoTools.Models;

/// <summary>
/// 视频库列表的一行（= 磁盘上的一个视频文件）。
/// <para>
/// 只有 <see cref="Name"/> / <see cref="SizeBytes"/> / <see cref="Modified"/> 是扫描目录就能拿到的；
/// <see cref="Duration"/> / <see cref="Resolution"/> / <see cref="Codec"/> 需要跑一次 ffprobe
/// —— 所以**扫描时不逐个探测**（几百个文件会卡死），点「读取媒体信息」才补。
/// </para>
/// </summary>
public class VideoItem
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    /// <summary>扩展名（小写、带点，如 <c>.mp4</c>）。</summary>
    public string Extension { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime Modified { get; set; }

    /// <summary>时长（秒）；未探测时为 <c>null</c>。</summary>
    public double? Duration { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string Codec { get; set; } = "";

    public string Folder => System.IO.Path.GetDirectoryName(FullPath) ?? "";

    public string SizeDisplay => SizeBytes switch
    {
        < 1024 => $"{SizeBytes} B",
        < 1024 * 1024 => $"{SizeBytes / 1024.0:0.0} KB",
        < 1024L * 1024 * 1024 => $"{SizeBytes / 1024.0 / 1024.0:0.0} MB",
        _ => $"{SizeBytes / 1024.0 / 1024.0 / 1024.0:0.00} GB"
    };

    public string ModifiedDisplay => Modified.ToString("yyyy-MM-dd HH:mm");

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

    public string StatusText => Duration is > 0 ? $"{ResolutionDisplay} · {Codec}" : "未读取媒体信息";
}
