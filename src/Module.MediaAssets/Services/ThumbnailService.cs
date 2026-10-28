using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NewToys.MediaAssets.Models;
using NewToys.Shared.Services;

namespace NewToys.MediaAssets.Services;

/// <summary>
/// 缩略图生成：视频抽帧 / 图片缩放，落 <c>cache\assets\thumbs\{sha1(path)}.jpg</c>（复用视频工具约定）。
/// <para>
/// 🔴 两条硬经验（视频工具先例）：
/// ① 先写 <c>.tmp.jpg</c> 再 <see cref="File.Move(string,string, bool)"/> 替换 —— 收藏页 / 列表的 Bitmap
///    可能正占着旧文件，直接写会失败；② ⛔ 不放大小图（<c>scale='min(SIZE,iw)':-2</c> 只缩不放）。
/// </para>
/// </summary>
public class ThumbnailService
{
    private readonly MediaAssetsSettingsIO _io;
    private readonly IAppConfigService? _app;

    public ThumbnailService(MediaAssetsSettingsIO io, IAppConfigService? app = null)
    {
        _io = io;
        _app = app;
    }

    /// <summary>缩略图目录（生效值，配置为空 → <c>&lt;配置根&gt;\cache\assets\thumbs</c>）。</summary>
    public string ThumbsDirectory => _io.ThumbsDirectory;

    /// <summary>一个路径对应的缩略图文件全路径（<c>{sha1(path)}.jpg</c>）。</summary>
    public string ThumbPathFor(string filePath)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(filePath ?? ""))).ToLowerInvariant();
        return Path.Combine(ThumbsDirectory, hash + ".jpg");
    }

    /// <summary>
    /// 给一个文件位置生成缩略图（视频 → ffmpeg 抽帧；图片 → **原生解码缩放**）。
    /// 返回（成功 / 消息 / 缩略图路径）。
    /// <para>
    /// 🔴 图片不再走 ffmpeg：一次子进程启动 + JPEG 编码要 80~150ms，而 Skia 解码缩放只要几毫秒
    /// （本机 CPU，无需外部进程）。视频仍用 ffmpeg —— 原生解码要靠平台 API（Windows = Media Foundation），
    /// 那会把缩略图变成平台相关代码，与「新代码必须平台中立」冲突，且收益远小于图片这一档。
    /// </para>
    /// </summary>
    public async Task<(bool Ok, string Message, string? ThumbPath)> GenerateAsync(FileLocation loc, CancellationToken ct)
    {
        if (loc == null || string.IsNullOrEmpty(loc.Path)) return (false, "参数不对。", null);
        if (!File.Exists(loc.Path)) return (false, "文件已不存在。", null);

        var ext = (loc.Ext ?? Path.GetExtension(loc.Path)).ToLowerInvariant();
        var isVideo = ScanService.GuessAssetType(ext) == AssetTypes.Video;
        var isImage = ScanService.GuessAssetType(ext) == AssetTypes.Image;
        if (!isVideo && !isImage) return (false, "这个类型不生成缩略图（本轮只做视频 / 图片）。", null);

        if (isImage) return await GenerateImageAsync(loc.Path, ct).ConfigureAwait(false);

        try
        {
            Directory.CreateDirectory(ThumbsDirectory);
            var target = ThumbPathFor(loc.Path);
            var tmp = target + ".tmp.jpg";

            var args = new System.Collections.Generic.List<string>
            {
                "-hide_banner", "-loglevel", "error", "-y",
                "-threads", "1",        // 单帧抽图：多线程只会抢 CPU，反而不快
                "-an", "-sn"            // 不要音轨 / 字幕流（省解析时间）
            };

            var at = await ResolveFramePositionAsync(loc.Path, ct).ConfigureAwait(false);
            if (at < 0) at = 0;
            args.Add("-ss");
            args.Add(at.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));

            args.Add("-i");
            args.Add(loc.Path);
            args.Add("-frames:v");
            args.Add("1");
            args.Add("-vf");
            args.Add($"scale='min({Size},iw)':-2");
            args.Add("-q:v");
            args.Add("4");              // 缩略图不需要高质量 JPEG（体积 / 编码时间都省一半）
            args.Add(tmp);

            var (code, _, stderr) = await MediaTools.RunAsync(
                MediaTools.FfmpegPath(_app), args, TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);

            if (code != 0 || !File.Exists(tmp))
                return (false, stderr.Trim().Length > 0 ? stderr.Trim() : "ffmpeg 未生成缩略图。", null);

            // 🔴 先落 .tmp 再替换：旧文件可能被界面 Bitmap 占着（视频工具先例）
            File.Move(tmp, target, overwrite: true);
            return (true, "已生成缩略图。", target);
        }
        catch (Exception ex)
        {
            return (false, "生成缩略图失败：" + ex.Message, null);
        }
    }

    /// <summary>
    /// 图片缩略图：本机解码 → 按长边缩放 → 编码成 JPEG 落到缓存路径。
    /// <para>编码要显式给 <see cref="Avalonia.Media.Imaging.BitmapEncoderOptions"/>（无参重载已过时）：
    /// 用 JPEG quality 85 —— 缩略图不需要原图质量，体积与编码时间都省一半，且扩展名与实际格式一致。</para>
    /// </summary>
    private async Task<(bool Ok, string Message, string? ThumbPath)> GenerateImageAsync(string path, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(ThumbsDirectory);
            var target = ThumbPathFor(path);
            var tmp = target + ".tmp.jpg";

            var ok = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var fs = File.OpenRead(path);
                using var bmp = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(fs, Size);
                if (bmp == null) return false;
                bmp.Save(tmp, new Avalonia.Media.Imaging.JpegBitmapEncoderOptions { Quality = 85 });
                return File.Exists(tmp);
            }, ct).ConfigureAwait(false);

            if (!ok) return (false, "图片解码失败（格式不支持 / 文件损坏）。", null);

            File.Move(tmp, target, overwrite: true);
            return (true, "已生成缩略图。", target);
        }
        catch (OperationCanceledException) { return (false, "已取消。", null); }
        catch (Exception ex) { return (false, "生成缩略图失败：" + ex.Message, null); }
    }

    /// <summary>缩略图长边像素（生效值）。</summary>
    private int Size => Math.Clamp(_io.Settings.ImageThumbSize, 16, 4096);

    /// <summary>
    /// 视频抽帧位置（秒）：percent 模式先 probe 时长再换算；probe 失败 → 回退固定秒。
    /// </summary>
    private async Task<double> ResolveFramePositionAsync(string path, CancellationToken ct)
    {
        var s = _io.Settings;
        if (string.Equals(s.ThumbFrameMode, "seconds", StringComparison.OrdinalIgnoreCase))
            return Math.Max(0, s.ThumbFrameSeconds);

        var (code, stdout, _) = await MediaTools.RunAsync(
            MediaTools.FfprobePath(_app),
            new[] { "-v", "error", "-hide_banner", "-show_format", "-of", "json", path },
            TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);

        if (code == 0 && !string.IsNullOrWhiteSpace(stdout))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(stdout);
                if (doc.RootElement.TryGetProperty("format", out var fmt)
                    && fmt.TryGetProperty("duration", out var d)
                    && d.ValueKind == System.Text.Json.JsonValueKind.String
                    && double.TryParse(d.GetString(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var duration)
                    && duration > 0)
                {
                    var percent = Math.Clamp(s.ThumbFramePercent, 0, 100);
                    return duration * percent / 100.0;
                }
            }
            catch { /* 解析失败 → 回退 */ }
        }
        return Math.Max(0, s.ThumbFrameSeconds);
    }
}
