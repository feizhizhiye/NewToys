using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NewToys.VideoTools.Models;

namespace NewToys.VideoTools.Services;

/// <summary>
/// 视频库的目录扫描与媒体信息探测。
/// <para>
/// 扫描**只读文件系统元信息**（名称 / 大小 / 修改时间），保证几百个文件的目录也是瞬间出列表；
/// 时长 / 分辨率 / 编码 / 码率走 ffprobe，由调用方决定单个读还是并发批量读
/// （<see cref="ProbeAsync"/> 本身**不碰 UI、不改模型**，只把结果返回 —— 这样多任务并发调用是线程安全的，
/// 界面更新统一由 ViewModel 在 UI 线程上做）。
/// </para>
/// </summary>
public static class VideoScanner
{
    /// <summary>认得的视频扩展名（小写，带点）。</summary>
    public static readonly string[] Extensions =
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm",
        ".m4v", ".ts", ".mpg", ".mpeg", ".rmvb", ".3gp", ".vob", ".m2ts"
    };

    /// <summary>扫描目录。目录不存在或无权限时返回空列表（不抛异常）。</summary>
    public static List<VideoItem> Scan(string folder, bool recursive, CancellationToken ct = default)
    {
        var list = new List<VideoItem>();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return list;

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        try
        {
            foreach (var f in new DirectoryInfo(folder).EnumerateFiles("*", option))
            {
                ct.ThrowIfCancellationRequested();
                if (!Extensions.Contains(f.Extension.ToLowerInvariant())) continue;
                list.Add(new VideoItem
                {
                    Name = f.Name,
                    FullPath = f.FullName,
                    Extension = f.Extension.ToLowerInvariant(),
                    SizeBytes = f.Length,
                    Modified = f.LastWriteTime
                });
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* 遍历中途无权限：返回已扫到的部分 */ }
        return list;
    }

    /// <summary>
    /// 由 ffmpeg 路径推出 ffprobe 路径。
    /// 例：<c>C:\ffmpeg\bin\ffmpeg.exe</c> → <c>C:\ffmpeg\bin\ffprobe.exe</c>；
    /// 配的是裸名 <c>ffmpeg</c> 时沿用裸名 <c>ffprobe</c>（走 PATH）。
    /// </summary>
    public static string DeriveFfprobe(string? ffmpegPath)
    {
        if (string.IsNullOrWhiteSpace(ffmpegPath)) return "ffprobe";
        var p = ffmpegPath.Trim();
        if (p.IndexOf(Path.DirectorySeparatorChar) < 0 && p.IndexOf('/') < 0 && p.IndexOf(':') < 0)
        {
            var n = Path.GetFileNameWithoutExtension(p);
            return n.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase) ? "ffprobe" : n;
        }
        var dir = Path.GetDirectoryName(p) ?? "";
        var name = Path.GetFileNameWithoutExtension(p);
        var probe = name.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase) ? "ffprobe" : name + "probe";
        var ext = Path.GetExtension(p);
        return Path.Combine(dir, probe + (string.IsNullOrEmpty(ext) ? ".exe" : ext));
    }

    /// <summary>
    /// 跑一次 ffprobe 拿时长 / 分辨率 / 编码 / 码率（JSON 输出，比 <c>key=value</c> 稳：字段可能重复、可能有嵌套）。
    /// <para><b>线程安全、不碰 UI</b>：可以放心地并发调用（见 <c>VideoListViewModel</c> 的批量读取）。</para>
    /// </summary>
    public static async Task<(bool Ok, string Message, VideoMediaInfo? Info)> ProbeAsync(
        string fullPath, string ffprobe, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return (false, "未指定文件。", null);
        if (!File.Exists(fullPath)) return (false, "文件已不存在。", null);

        var psi = new ProcessStartInfo
        {
            FileName = ffprobe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in new[]
                 {
                     "-v", "error",
                     "-hide_banner",
                     "-select_streams", "v:0",
                     "-show_format",
                     "-show_streams",
                     "-of", "json",
                     fullPath
                 }) psi.ArgumentList.Add(a);

        try
        {
            using var proc = new Process { StartInfo = psi };
            if (!proc.Start()) return (false, "无法启动 ffprobe。", null);
            var outTask = proc.StandardOutput.ReadToEndAsync();
            var errTask = proc.StandardError.ReadToEndAsync();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);

            var stdout = await outTask.ConfigureAwait(false);
            var stderr = await errTask.ConfigureAwait(false);

            var info = ParseProbeJson(stdout);
            if (info == null)
                return (false, stderr.Trim().Length > 0 ? stderr.Trim() : "ffprobe 未返回可识别的信息。", null);

            return (true, "已读取媒体信息。", info);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* 进程已退出 */ }
            return (false, "ffprobe 超时或已取消。", null);
        }
        catch (Exception ex)
        {
            return (false, $"ffprobe 调用失败：{ex.Message}（可在「设置 → 路径配置」里指定 ffmpeg 路径）", null);
        }
    }

    /// <summary>
    /// 解析 ffprobe 的 JSON（<c>-of json</c>）。字段可能是数字也可能被写成字符串，两种都收。
    /// </summary>
    private static VideoMediaInfo? ParseProbeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string codec = "";
            int? width = null, height = null;

            if (root.TryGetProperty("streams", out var streams)
                && streams.ValueKind == JsonValueKind.Array && streams.GetArrayLength() > 0)
            {
                var s = streams[0];
                codec = GetString(s, "codec_name");
                width = GetInt(s, "width");
                height = GetInt(s, "height");
            }

            double? duration = null;
            long? bitRate = null;

            if (root.TryGetProperty("format", out var fmt) && fmt.ValueKind == JsonValueKind.Object)
            {
                duration = GetDouble(fmt, "duration");
                bitRate = GetLong(fmt, "bit_rate");
            }

            if (width is null && height is null && duration is null && codec.Length == 0) return null;
            return new VideoMediaInfo(duration, width, height, codec, bitRate);
        }
        catch
        {
            return null;
        }
    }

    private static string GetString(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";

    private static int? GetInt(JsonElement obj, string name)
    {
        var l = GetLong(obj, name);
        return l is null ? null : (int)Math.Clamp(l.Value, int.MinValue, int.MaxValue);
    }

    private static long? GetLong(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String
            && long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)) return s;
        return null;
    }

    private static double? GetDouble(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String
            && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) return s;
        return null;
    }
}
