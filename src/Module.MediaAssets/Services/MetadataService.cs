using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NewToys.MediaAssets.Models;
using NewToys.Shared.Services;

namespace NewToys.MediaAssets.Services;

/// <summary>
/// 元数据提取（NT-021）：视频走 ffprobe（时长 / 分辨率 / 编码），图片读文件头（尺寸）。
/// <para>
/// ⚠️ 只做**最小实现**：音乐 / 书籍 / 文档本轮只落基础文件信息（ mutagen / exif / PDF 解析等
/// 第三方库 ⛔ 不擅引——先问用户）。结果写进 <c>assets.metadata_json</c>（按类型分键，⛔ 不覆盖别的类型的键）。
/// </para>
/// </summary>
public class MetadataService
{
    private readonly AssetService _assets;
    private readonly IAppConfigService? _app;

    public MetadataService(AssetService assets, IAppConfigService? app = null)
    {
        _assets = assets;
        _app = app;
    }

    public bool IsAvailable => _assets.IsAvailable;

    /// <summary>
    /// 给一个文件位置提取元数据并写进它所属资产的 <c>metadata_json</c>（位置里带 <see cref="FileLocation.AssetId"/>）。
    /// </summary>
    public async Task<(bool Ok, string Message)> ApplyAsync(FileLocation loc, CancellationToken ct)
    {
        if (loc == null || loc.AssetId <= 0 || !IsAvailable) return (false, "参数不对或库不可用。");
        if (!File.Exists(loc.Path)) return (false, "文件已不存在。");

        try
        {
            var ext = (loc.Ext ?? Path.GetExtension(loc.Path)).ToLowerInvariant();
            string json = ext switch
            {
                ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".flv" or ".webm" or ".m4v" or ".ts" or ".mpg" or ".mpeg" or ".rmvb"
                    => await ProbeVideoJsonAsync(loc.Path, ct).ConfigureAwait(false),
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tif" or ".tiff" or ".svg" or ".heic"
                    => ProbeImageJson(loc.Path),
                _ => "{}"
            };

            if (json == "{}") return (true, "这个类型没有可提取的元数据（本轮只做视频 + 图片）。");

            var asset = _assets.FindAsset(loc.AssetId);
            if (asset == null) return (false, "资产行不见了。");

            var merged = MergeJson(asset.MetadataJson, json);
            _assets.UpdateAsset(new Asset
            {
                Id = asset.Id,
                Type = asset.Type,
                Title = asset.Title,
                DisplayName = asset.DisplayName,
                Description = asset.Description,
                Status = asset.Status,
                Rating = asset.Rating,
                MetadataJson = merged,
                CreatedAt = asset.CreatedAt
            });
            return (true, "已提取元数据。");
        }
        catch (Exception ex)
        {
            return (false, "提取元数据失败：" + ex.Message);
        }
    }

    /// <summary>ffprobe 拿视频元数据 → metadata_json 的 video 段。</summary>
    private async Task<string> ProbeVideoJsonAsync(string path, CancellationToken ct)
    {
        var (code, stdout, stderr) = await MediaTools.RunAsync(
            MediaTools.FfprobePath(_app),
            new[]
            {
                "-v", "error", "-hide_banner",
                "-select_streams", "v:0",
                "-show_format", "-show_streams",
                "-of", "json", path
            },
            TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);

        if (code != 0 || string.IsNullOrWhiteSpace(stdout)) return "{}";

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;

            double? duration = null;
            if (root.TryGetProperty("format", out var fmt))
                duration = GetDouble(fmt, "duration");

            var width = 0; var height = 0; var codec = ""; var fpsText = "";
            if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array && streams.GetArrayLength() > 0)
            {
                var s = streams[0];
                width = GetInt(s, "width");
                height = GetInt(s, "height");
                codec = GetString(s, "codec_name");
                fpsText = GetString(s, "avg_frame_rate");
            }

            var sb = new System.Text.StringBuilder();
            sb.Append('{');
            if (duration is > 0) sb.Append("\"duration_seconds\":").Append(duration.Value.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            if (width > 0 && height > 0) sb.Append("\"width\":").Append(width).Append(",\"height\":").Append(height).Append(',');
            if (codec.Length > 0) sb.Append("\"video_codec\":\"").Append(codec).Append("\",");
            if (double.TryParse(FpsToNumber(fpsText), NumberStyles.Float, CultureInfo.InvariantCulture, out var fps) && fps > 0)
                sb.Append("\"fps\":").Append(fps.ToString("0.##", CultureInfo.InvariantCulture)).Append(',');
            var json = sb.ToString().TrimEnd(',');
            return json.Length > 1 ? json + "}" : "{}";
        }
        catch { return "{}"; }
    }

    /// <summary>读图片文件头拿尺寸 → metadata_json 的 image 段（⛔ 不起进程、不引第三方库）。</summary>
    public static string ProbeImageJson(string path)
    {
        var (w, h) = ReadImageSize(path);
        if (w <= 0 || h <= 0) return "{}";
        return $"{{\"width\":{w.ToString(CultureInfo.InvariantCulture)},\"height\":{h.ToString(CultureInfo.InvariantCulture)}}}";
    }

    /// <summary>
    /// 从文件头读图片尺寸（PNG / GIF / BMP / JPEG / WebP；拿不准返回 0）。
    /// ⚠️ 只读文件头几十字节，⛔ 不整文件解码。
    /// </summary>
    public static (int Width, int Height) ReadImageSize(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> head = stackalloc byte[64];
            var read = fs.Read(head);
            if (read < 8) return (0, 0);

            // PNG：签名 + IHDR(宽高在偏移 16，大端)
            if (head[0] == 0x89 && head[1] == 'P' && head[2] == 'N' && head[3] == 'G' && read >= 24)
                return (Be32(head, 16), Be32(head, 20));

            // GIF：'GIF8' + 逻辑屏幕描述符（偏移 6，小端）
            if (head[0] == 'G' && head[1] == 'I' && head[2] == 'F' && head[3] == '8' && read >= 10)
                return (Le16(head, 6), Le16(head, 8));

            // BMP：'BM' + DIB 头（偏移 18，小端，有符号）
            if (head[0] == 'B' && head[1] == 'M' && read >= 26)
                return (Le32(head, 18), Le32(head, 22));

            // WebP：'RIFF' .... 'WEBP' + 块
            if (head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F'
                && head[8] == 'W' && head[9] == 'E' && head[10] == 'B' && head[11] == 'P')
                return ReadWebpSize(fs, head, read);

            // JPEG：扫 SOF 标记（SOF0~SOF15，跳过 SOS / DHT 等）
            if (head[0] == 0xFF && head[1] == 0xD8)
                return ReadJpegSize(fs);
        }
        catch { /* 读不了 → 0 */ }
        return (0, 0);
    }

    private static (int, int) ReadWebpSize(FileStream fs, Span<byte> head, int headRead)
    {
        // 简单格式（VP8 / VP8L）：块头四字节在偏移 12
        var chunk = System.Text.Encoding.ASCII.GetString(head.Slice(12, 4)).TrimEnd('\0');
        if (headRead >= 30 && chunk == "VP8 ")
        {
            // 有损 VP8：尺寸在偏移 26（小端，14 位）
            Span<byte> b = stackalloc byte[4];
            fs.Seek(26, SeekOrigin.Begin);
            if (fs.Read(b) == 4)
                return (Le16(b, 0) & 0x3FFF, Le16(b, 2) & 0x3FFF);
        }
        if (headRead >= 25 && chunk == "VP8L")
        {
            Span<byte> b = stackalloc byte[5];
            fs.Seek(21, SeekOrigin.Begin);
            if (fs.Read(b) == 5)
            {
                var bits = b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24);
                return ((bits & 0x3FFF) + 1, ((bits >> 14) & 0x3FFF) + 1);
            }
        }
        if (headRead >= 30 && chunk == "VP8X")
        {
            Span<byte> b = stackalloc byte[6];
            fs.Seek(24, SeekOrigin.Begin);
            if (fs.Read(b) == 6)
                return ((b[0] | (b[1] << 8) | (b[2] << 16)) + 1, ((b[3] | (b[4] << 8) | (b[5] << 16)) + 1));
        }
        return (0, 0);
    }

    private static (int, int) ReadJpegSize(FileStream fs)
    {
        fs.Seek(2, SeekOrigin.Begin);
        Span<byte> seg = stackalloc byte[16];
        while (true)
        {
            if (fs.Read(seg.Slice(0, 2)) < 2) return (0, 0);
            if (seg[0] != 0xFF) return (0, 0);           // 不同步 → 放弃
            var marker = seg[1];
            if (marker == 0xD9 || marker == 0xDA) return (0, 0);   // EOI / SOS → 没找到 SOF
            if (fs.Read(seg.Slice(0, 2)) < 2) return (0, 0);
            var len = (seg[0] << 8) | seg[1];
            if (len < 2) return (0, 0);

            // SOF0~SOF15（除 DHT(c4)/JPG(c8)/DAC(cc)）：段内 精度1 + 高2 + 宽2
            if ((marker >= 0xC0 && marker <= 0xCF) && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                if (fs.Read(seg) < 5) return (0, 0);
                return ((seg[1] << 8) | seg[2], (seg[3] << 8) | seg[4]);
            }
            fs.Seek(len - 2, SeekOrigin.Current);        // 跳过这个段
        }
    }

    private static int Be32(Span<byte> b, int offset)
        => (b[offset] << 24) | (b[offset + 1] << 16) | (b[offset + 2] << 8) | b[offset + 3];

    private static int Le16(Span<byte> b, int offset) => b[offset] | (b[offset + 1] << 8);

    private static int Le32(Span<byte> b, int offset)
        => b[offset] | (b[offset + 1] << 8) | (b[offset + 2] << 16) | (b[offset + 3] << 24);

    /// <summary>"30000/1001" → "29.97"；非分数 / 除零 → 空串。</summary>
    private static string FpsToNumber(string? text)
    {
        var t = (text ?? "").Trim();
        var slash = t.IndexOf('/');
        if (slash <= 0 || slash == t.Length - 1) return "";
        if (!double.TryParse(t[..slash], NumberStyles.Float, CultureInfo.InvariantCulture, out var a)) return "";
        if (!double.TryParse(t[(slash + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var b) || b == 0) return "";
        return (a / b).ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static double? GetDouble(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => null
        };
    }

    private static int GetInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetInt32(),
            JsonValueKind.String when int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
            _ => 0
        };
    }

    private static string GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>把新提出的字段**并进**已有 metadata_json（同键覆盖、异键保留；⛔ 不整对象替换）。</summary>
    private static string MergeJson(string? existing, string addition)
    {
        if (string.IsNullOrWhiteSpace(addition) || addition == "{}") return existing ?? "{}";
        if (string.IsNullOrWhiteSpace(existing) || existing == "{}") return addition;
        try
        {
            var map = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (src, _) in new[] { (existing, 0), (addition, 1) })
            {
                using var doc = JsonDocument.Parse(src);
                foreach (var prop in doc.RootElement.EnumerateObject())
                    map[prop.Name] = prop.Value.GetRawText();   // 后写的（新提取的）覆盖旧值
            }
            var sb = new System.Text.StringBuilder("{");
            var first = true;
            foreach (var (k, v) in map)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(k).Append("\":").Append(v);
            }
            sb.Append('}');
            return sb.ToString();
        }
        catch { return addition; }
    }
}
