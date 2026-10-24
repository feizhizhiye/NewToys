using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace NewToys.VideoTools.Services;

/// <summary>一条预检结果（<see cref="IsError"/> = true 表示「这条命令一定跑不通」）。</summary>
public sealed record LintIssue(bool IsError, string Text);

/// <summary>
/// **预检的补充上下文**（NT-102 / NT-103 / NT-104）：这些值不是从命令行文本能反推出来的
/// （裁剪坐标、音频方式、目标体积都在任务对象上），所以由调用方显式传入。
/// </summary>
public sealed record LintExtra(
    int CropWidth = 0,
    int CropHeight = 0,
    string? AudioMode = null,
    string? ReplaceAudioPath = null,
    double TargetSizeMb = 0,
    double Duration = 0);

/// <summary>
/// **ffmpeg 参数预检**（NT-098）：纯静态分析，不跑 ffmpeg、不碰 UI。
/// <para>
/// 只做「已知冲突」白名单 —— ⛔ 不做完整 ffmpeg 语法解析（那是不可能维护的）。
/// 目标是「用户手改命令后的常见踩坑，在点运行之前就看得见」。
/// </para>
/// <para>两页（视频预览 / 批量处理）共用，输入永远是「同一套 Build 函数产出的参数列表」。</para>
/// </summary>
public static class CommandLint
{
    private static readonly string[] CodecFlags = { "-c", "-codec", "-c:v", "-vcodec", "-codec:v" };
    private static readonly string[] QualityFlags = { "-crf", "-cq", "-b:v", "-preset", "-b:a" };

    /// <summary>
    /// 检查一条命令。<paramref name="args"/> = 不含 <c>ffmpeg</c>、**含**输出路径的完整参数列表。
    /// </summary>
    public static IReadOnlyList<LintIssue> Check(IReadOnlyList<string>? args, string? output, string? sourcePath,
        string? subtitlePath = null, string? subtitleMode = null, LintExtra? extra = null)
    {
        var list = new List<LintIssue>();
        if (args == null || args.Count == 0) return list;

        // ① 输入 / 输出
        if (!string.IsNullOrWhiteSpace(sourcePath) && !File.Exists(sourcePath))
            list.Add(new(true, "源文件不存在 —— 重新挑一个文件。"));

        var outPath = (output ?? "").Trim();
        if (outPath.Length == 0)
            list.Add(new(true, "没有输出路径 —— 命令末尾缺文件名，ffmpeg 会直接报错。"));

        var ext = outPath.Length > 0 ? Path.GetExtension(outPath).ToLowerInvariant() : "";
        var isGifOut = ext == ".gif";

        // ①-b 字幕（NT-100）：选了方式没挑文件 / 文件不存在 / 软字幕装进了非 mkv 容器
        var subPath = (subtitlePath ?? "").Trim();
        var subMode = (subtitleMode ?? "").Trim().ToLowerInvariant();
        if (subMode.Length > 0 && subPath.Length == 0)
            list.Add(new(true, "选了字幕方式但没挑字幕文件 —— 挑一个 .srt / .ass，或把方式改回「不加字幕」。"));
        if (subPath.Length > 0 && !File.Exists(subPath))
            list.Add(new(true, "字幕文件不存在 —— 文件被移走了？重新挑一次。"));
        if (subMode == "soft" && ext.Length > 0 && ext != ".mkv")
            list.Add(new(true, "软字幕的输出必须是 .mkv —— mp4 / mov 装不了 srt 字幕轨。"));

        // ①-c 裁剪 / 音轨 / 目标体积（NT-102 ~ NT-104）
        var codec = LastValue(args, CodecFlags);
        var isCopy = string.Equals(codec, "copy", StringComparison.OrdinalIgnoreCase);

        if (extra != null)
        {
            var cropW = extra.CropWidth;
            var cropH = extra.CropHeight;
            if ((cropW > 0) != (cropH > 0))
                list.Add(new(true, "裁剪的宽 / 高要**同时**填 —— 只填一个等于没填，命令里不会出现 crop。"));
            else if (cropW > 0 && (cropW % 2 != 0 || cropH % 2 != 0))
                list.Add(new(false, $"裁剪尺寸 {cropW}×{cropH} 有奇数边 —— H.264/H.265 的 yuv420p 要求偶数，会自动取偶。"));

            var audioMode = (extra.AudioMode ?? "").Trim().ToLowerInvariant();
            if (audioMode == "replace")
            {
                var audioPath = (extra.ReplaceAudioPath ?? "").Trim();
                if (audioPath.Length == 0)
                    list.Add(new(true, "选了「替换音轨」但没挑音频文件 —— 挑一个 mp3 / m4a / wav。"));
                else if (!File.Exists(audioPath))
                    list.Add(new(true, "替换用的音频文件不存在 —— 重新挑一次。"));
                if (subMode == "soft")
                    list.Add(new(true, "替换音轨与软字幕不能同时用 —— 两条路径都要 `-map`，互相打架；先做一样。"));
            }
            else if (audioMode == "volume" && isGifOut)
            {
                list.Add(new(false, "GIF 没有音轨 —— 音量增益对 GIF 无效。"));
            }

            // 软字幕是「只封装」模式：这些都要重编码，本模式下不生效
            if (subMode == "soft"
                && (cropW > 0 || audioMode == "volume" || extra.TargetSizeMb > 0))
            {
                list.Add(new(false, "软字幕只封装（`-c copy`）—— 裁剪 / 音量 / 目标体积在这个模式下不生效；要这些效果请改用重新编码 + 硬字幕。"));
            }

            if (extra.TargetSizeMb > 0)
            {
                if (extra.Duration <= 0)
                    list.Add(new(true, "填了「目标体积」但读不到源时长 —— 算不出码率；先让预览读出时长，或改用 CRF。"));
                else if (isCopy)
                    list.Add(new(true, "`-c copy` 不能按体积压 —— copy 不改码率。"));
            }
        }

        // ② copy 与「需要重新编码的参数」冲突（最常见的手改翻车点）
        if (subMode == "hard" && isCopy)
            list.Add(new(true, "硬字幕要烧进画面 ⇒ 必须重新编码，`-c copy` 用不了。"));

        var vfCount = CountFlag(args, "-vf") + CountFlag(args, "-filter:v");
        var hasComplex = CountFlag(args, "-filter_complex") > 0;

        if (isCopy && (vfCount > 0 || hasComplex))
            list.Add(new(true, "`-c copy` 与滤镜不能同时用 —— copy 不重新编码，滤镜没处作用；去掉滤镜或改成编码器。"));

        if (isCopy && HasAnyFlag(args, QualityFlags))
            list.Add(new(true, "`-c copy` 与 CRF / 码率 / preset 不能同时用 —— copy 不重新编码，这些参数会被拒绝。"));

        if (vfCount > 1)
            list.Add(new(true, "命令里有多个 `-vf` —— ffmpeg 只认最后一个，前面的会被悄悄丢掉。合成一条再用。"));

        if (vfCount > 0 && hasComplex)
            list.Add(new(true, "同一路视频不能同时用 `-vf` 和 `-filter_complex` —— 二选一。"));

        // ③ 编码器 / 质量参数
        var crfText = LastValue(args, "-crf");
        if (crfText != null && int.TryParse(crfText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var crf)
            && (crf < 0 || crf > 51))
        {
            list.Add(new(true, $"`-crf {crf}` 超出 0~51 —— 常用区间 18~28（越小越清晰、体积越大）。"));
        }

        if (crfText != null && codec != null && codec.EndsWith("_nvenc", StringComparison.OrdinalIgnoreCase))
            list.Add(new(true, $"NVENC 编码器用 `-cq` 而不是 `-crf` —— `-crf` 会被忽略（命令里两条都出现时以后者为准）。"));

        // ④ 容器 / 编码器匹配
        if (isGifOut && codec != null && !string.Equals(codec, "gif", StringComparison.OrdinalIgnoreCase))
            list.Add(new(true, $"输出是 .gif 但编码器是 `{codec}` —— 容器与编码器不匹配。"));

        if (isGifOut && vfCount == 0 && !hasComplex)
            list.Add(new(false, "GIF 没带调色板滤镜（fps + palettegen + paletteuse）—— 颜色会发灰、体积偏大。"));

        if (isGifOut)
        {
            var len = ParseLength(LastValue(args, "-to") ?? LastValue(args, "-t"));
            if (len > 30)
                list.Add(new(false, $"GIF {len:0} 秒偏长 —— 建议先勾「剪辑」截 5~15 秒（体积和内存都会爆）。"));
        }

        if (ext.Length > 0 && ext != ".gif" && ext != ".mp3" && !FfmpegService.Containers.Contains(ext))
            list.Add(new(false, $"输出后缀 `{ext}` 不在常用容器列表里 —— ffmpeg 靠扩展名选容器，写错会报 Unknown format。"));

        // ⑤ copy 剪切的精度提示
        if (isCopy && (CountFlag(args, "-ss") > 0 || CountFlag(args, "-to") > 0 || CountFlag(args, "-t") > 0))
            list.Add(new(false, "`-c copy` 剪切只能切在关键帧上（起止会就近对齐）；要精确到帧请选「重新编码」。"));

        return list;
    }

    /// <summary>把预检结果拼成详情卡里那段提示文本（最多 4 条，空 = 没有意见）。</summary>
    public static string Describe(IReadOnlyList<LintIssue>? issues)
    {
        if (issues == null || issues.Count == 0) return "";
        var sb = new StringBuilder();
        foreach (var i in issues.Take(4))
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(i.IsError ? "⛔ " : "ℹ ").Append(i.Text);
        }
        return sb.ToString();
    }

    // ==================== 小工具 ====================

    private static int CountFlag(IReadOnlyList<string> args, string flag)
        => args.Count(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    private static bool HasAnyFlag(IReadOnlyList<string> args, IEnumerable<string> flags)
        => flags.Any(f => CountFlag(args, f) > 0);

    /// <summary>取某个开关的**最后一个**值（ffmpeg 里后出现的同名参数覆盖前面的）。</summary>
    private static string? LastValue(IReadOnlyList<string> args, IEnumerable<string> flags)
    {
        var set = flags as IReadOnlyList<string> ?? flags.ToList();
        string? value = null;
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (set.Any(f => string.Equals(args[i], f, StringComparison.OrdinalIgnoreCase)))
                value = args[i + 1];
        }
        return value;
    }

    private static string? LastValue(IReadOnlyList<string> args, string flag) => LastValue(args, new[] { flag });

    /// <summary>把 <c>-to</c> / <c>-t</c> 的值解析成秒（支持 <c>12.5</c> 与 <c>00:00:12.5</c> 两种）。</summary>
    private static double ParseLength(string? text)
    {
        var s = (text ?? "").Trim();
        if (s.Length == 0) return 0;

        if (s.Contains(':'))
        {
            var parts = s.Split(':');
            if (parts.Length != 3) return 0;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)) return 0;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m)) return 0;
            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var sec)) return 0;
            return h * 3600 + m * 60 + sec;
        }

        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}
