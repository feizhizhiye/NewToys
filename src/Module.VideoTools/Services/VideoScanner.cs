using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NewToys.VideoTools.Models;

namespace NewToys.VideoTools.Services;

/// <summary>
/// 视频库的目录扫描与媒体信息探测。
/// <para>
/// 扫描**只读文件系统元信息**（名称 / 大小 / 修改时间），保证几百个文件的目录也是瞬间出列表；
/// 时长 / 分辨率 / 编码走 ffprobe，且**只在用户显式点「读取媒体信息」时对单个文件跑一次**。
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
    /// 跑一次 ffprobe 拿时长 / 分辨率 / 视频编码。失败返回 null（界面显示「—」，不报错）。
    /// </summary>
    public static async Task<(bool Ok, string Message)> ProbeAsync(VideoItem item, string ffprobe, CancellationToken ct = default)
    {
        if (item == null) return (false, "未选择文件。");
        if (!File.Exists(item.FullPath)) return (false, "文件已不存在。");

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
                     "-select_streams", "v:0",
                     "-show_entries", "stream=width,height,codec_name",
                     "-show_entries", "format=duration",
                     "-of", "default=noprint_wrappers=1",
                     item.FullPath
                 }) psi.ArgumentList.Add(a);

        try
        {
            using var proc = new Process { StartInfo = psi };
            if (!proc.Start()) return (false, "无法启动 ffprobe。");
            var outTask = proc.StandardOutput.ReadToEndAsync();
            var errTask = proc.StandardError.ReadToEndAsync();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);

            var stdout = await outTask.ConfigureAwait(false);
            var stderr = await errTask.ConfigureAwait(false);

            foreach (var line in stdout.Split('\n'))
            {
                var kv = line.Trim().Split('=', 2);
                if (kv.Length != 2) continue;
                switch (kv[0])
                {
                    case "width": if (int.TryParse(kv[1], out var w)) item.Width = w; break;
                    case "height": if (int.TryParse(kv[1], out var h)) item.Height = h; break;
                    case "codec_name": item.Codec = kv[1].Trim(); break;
                    case "duration":
                        if (double.TryParse(kv[1], System.Globalization.NumberStyles.Float,
                                            System.Globalization.CultureInfo.InvariantCulture, out var d))
                            item.Duration = d;
                        break;
                }
            }

            if (item.Duration is null && item.Width is null)
                return (false, stderr.Trim().Length > 0 ? stderr.Trim() : "ffprobe 未返回可识别的信息。");
            return (true, $"已读取：{item.DurationDisplay} · {item.ResolutionDisplay} · {item.Codec}");
        }
        catch (OperationCanceledException)
        {
            return (false, "ffprobe 超时或已取消。");
        }
        catch (Exception ex)
        {
            return (false, $"ffprobe 调用失败：{ex.Message}（可在「设置 → 路径配置」里指定 ffmpeg 路径）");
        }
    }
}
