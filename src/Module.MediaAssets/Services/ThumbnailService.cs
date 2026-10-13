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
/// 缩略图生成（NT-021）：视频抽帧 / 图片缩放，落 <c>data\thumbs\{sha1(path)}.jpg</c>（复用视频工具约定）。
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

    /// <summary>缩略图目录（生效值，配置为空 → <c>&lt;应用根&gt;\data\thumbs</c>）。</summary>
    public string ThumbsDirectory => _io.ThumbsDirectory;

    /// <summary>一个路径对应的缩略图文件全路径（<c>{sha1(path)}.jpg</c>）。</summary>
    public string ThumbPathFor(string filePath)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(filePath ?? ""))).ToLowerInvariant();
        return Path.Combine(ThumbsDirectory, hash + ".jpg");
    }

    /// <summary>
    /// 给一个文件位置生成缩略图（视频 → 抽帧；图片 → 缩放）。返回（成功 / 消息 / 缩略图路径）。
    /// </summary>
    public async Task<(bool Ok, string Message, string? ThumbPath)> GenerateAsync(FileLocation loc, CancellationToken ct)
    {
        if (loc == null || string.IsNullOrEmpty(loc.Path)) return (false, "参数不对。", null);
        if (!File.Exists(loc.Path)) return (false, "文件已不存在。", null);

        var ext = (loc.Ext ?? Path.GetExtension(loc.Path)).ToLowerInvariant();
        var isVideo = ScanService.GuessAssetType(ext) == AssetTypes.Video;
        var isImage = ScanService.GuessAssetType(ext) == AssetTypes.Image;
        if (!isVideo && !isImage) return (false, "这个类型不生成缩略图（本轮只做视频 / 图片）。", null);

        try
        {
            Directory.CreateDirectory(ThumbsDirectory);
            var target = ThumbPathFor(loc.Path);
            var tmp = target + ".tmp.jpg";

            var args = new System.Collections.Generic.List<string>
            {
                "-hide_banner", "-loglevel", "error", "-y"
            };

            if (isVideo)
            {
                var at = await ResolveFramePositionAsync(loc.Path, ct).ConfigureAwait(false);
                if (at < 0) at = 0;
                args.Add("-ss");
                args.Add(at.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }

            args.Add("-i");
            args.Add(loc.Path);
            args.Add("-frames:v");
            args.Add("1");
            args.Add("-vf");
            args.Add($"scale='min({Size},iw)':-2");
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
