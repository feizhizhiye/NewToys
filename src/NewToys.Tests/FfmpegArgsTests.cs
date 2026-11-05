using System;
using System.Collections.Generic;
using System.Linq;
using NewToys.VideoTools.Models;
using NewToys.VideoTools.Services;
using Xunit;

namespace NewToys.Tests;

/// <summary>ffmpeg 命令行拼装的回归测试（纯静态函数，不执行 ffmpeg、不碰磁盘）。（WHY: TS-002）</summary>
public class FfmpegArgsTests
{
    private static TranscodeTask NewTask(string presetId = "h265") => new() { PresetId = presetId };

    private static List<string> Transcode(string presetId, Action<TranscodeTask>? setup = null)
    {
        var task = NewTask(presetId);
        setup?.Invoke(task);
        return FfmpegService.BuildTranscodeArgs(task, null);
    }

    /// <summary>断言「相邻两个参数」成对出现（避免把 <c>-c:v</c> 和别的 <c>libx264</c> 撞上）。</summary>
    private static void AssertPair(IReadOnlyList<string> args, string name, string value)
    {
        for (var i = 0; i < args.Count - 1; i++)
            if (args[i] == name && args[i + 1] == value) return;

        Assert.Fail($"期望参数对 [{name} {value}] 不存在：{string.Join(' ', args)}");
    }

    private static void AssertNoPair(IReadOnlyList<string> args, string name, string value)
    {
        for (var i = 0; i < args.Count - 1; i++)
            Assert.False(args[i] == name && args[i + 1] == value,
                         $"不该出现的参数对 [{name} {value}]：{string.Join(' ', args)}");
    }

    private static string ValueAfter(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
            if (args[i] == name) return args[i + 1];

        Assert.Fail($"参数 {name} 不存在：{string.Join(' ', args)}");
        return "";   // Assert.Fail 不会返回，仅为满足编译
    }

    // ==================== 纯字符串助手 ====================

    [Fact]
    public void CropFilter_DisabledWhenNoSize() => Assert.Equal("", FfmpegService.CropFilter(NewTask()));

    [Fact]
    public void CropFilter_RoundsOddSidesDownToEven()
    {
        var task = NewTask();
        task.CropWidth = 1921;
        task.CropHeight = 1081;

        // 奇数边长会被 yuv420p 编码器直接拒绝，必须取偶
        Assert.Equal("crop=1920:1080:0:0", FfmpegService.CropFilter(task));
    }

    [Fact]
    public void CropFilter_ClampsNegativeOriginToZero()
    {
        var task = NewTask();
        task.CropWidth = 640;
        task.CropHeight = 480;
        task.CropX = -10;
        task.CropY = -20;

        Assert.Equal("crop=640:480:0:0", FfmpegService.CropFilter(task));
    }

    [Theory]
    [InlineData(1280, 720, "scale=1280:720")]
    [InlineData(0, 720, "scale=-2:720")]
    [InlineData(1280, 0, "scale=1280:-2")]
    public void ScaleFilter_ZeroSideKeepsAspectAndEven(int w, int h, string expected)
        => Assert.Equal(expected, FfmpegService.ScaleFilter(w, h));

    [Fact]
    public void FitFilter_OnlyShrinks()
        => Assert.Equal("scale=1920:1080:force_original_aspect_ratio=decrease",
                        FfmpegService.FitFilter(1920, 1080));

    [Theory]
    [InlineData(0d, "")]
    [InlineData(0.0004, "")]
    [InlineData(6d, "volume=6dB")]
    [InlineData(-3.5, "volume=-3.5dB")]
    public void VolumeFilter_ZeroMeansNoFilter(double db, string expected)
        => Assert.Equal(expected, FfmpegService.VolumeFilter(db));

    // ==================== 编码器映射 ====================

    [Theory]
    [InlineData("libx264", "h264_nvenc")]
    [InlineData("libx265", "hevc_nvenc")]
    [InlineData("libsvtav1", "av1_nvenc")]
    [InlineData("copy", "copy")]        // 映射不上就保持原样，⛔ 不悄悄换一个
    [InlineData("", "")]
    public void ResolveEncoder_GpuMappingKeepsUnknown(string cpu, string expected)
        => Assert.Equal(expected, FfmpegService.ResolveEncoder(cpu, useGpu: true));

    [Fact]
    public void ResolveEncoder_CpuPassesThrough()
        => Assert.Equal("libx265", FfmpegService.ResolveEncoder("libx265", useGpu: false));

    // ==================== 目标体积反算码率 ====================

    [Fact]
    public void AutoBitrate_Formula_TakesAudioAndOverheadOut()
    {
        var task = NewTask();
        task.TargetSizeMb = 100;
        task.SourceDuration = 100;

        // 100 MB × 8192 ÷ 100 s = 8192 kbps，减音频 192k 与容器开销 8k
        Assert.Equal(8192 - 192 - 8, FfmpegService.AutoBitrateKbps(task));
    }

    [Fact]
    public void AutoBitrate_Muted_AddsAudioBack()
    {
        var task = NewTask();
        task.TargetSizeMb = 100;
        task.SourceDuration = 100;
        task.AudioMode = AudioModes.Mute;

        Assert.Equal(8192 - 8, FfmpegService.AutoBitrateKbps(task));
    }

    [Fact]
    public void AutoBitrate_TooSmall_ClampsTo50()
    {
        var task = NewTask();
        task.TargetSizeMb = 1;
        task.SourceDuration = 1000;

        Assert.Equal(50, FfmpegService.AutoBitrateKbps(task));
    }

    [Theory]
    [InlineData(0d, 60d)]     // 没填体积
    [InlineData(50d, 0d)]     // 时长未知
    public void AutoBitrate_ReturnsZeroWhenUndeterminable(double mb, double duration)
    {
        var task = NewTask();
        task.TargetSizeMb = mb;
        task.SourceDuration = duration;

        Assert.Equal(0, FfmpegService.AutoBitrateKbps(task));
    }

    // ==================== 输出扩展名 ====================

    [Fact]
    public void OutputExtension_GifIsAlwaysGif()
        => Assert.Equal(".gif", FfmpegService.OutputExtension(NewTask("gif")));

    [Fact]
    public void OutputExtension_SoftSubtitleForcesMkv()
    {
        var task = NewTask("h265");
        task.SubtitlePath = @"F:\sub.srt";
        task.SubtitleMode = SubtitleModes.Soft;

        // mp4 装不了 srt 轨，软字幕必须换容器
        Assert.Equal(".mkv", FfmpegService.OutputExtension(task));
    }

    [Fact]
    public void OutputExtension_DefaultsToPresetContainer()
        => Assert.Equal(".mp4", FfmpegService.OutputExtension(NewTask("h265")));

    // ==================== 多段串解析 ====================

    [Theory]
    [InlineData("5-12.5", 5d, 12.5)]
    [InlineData("00:00:05-00:00:12", 5d, 12d)]
    [InlineData("01:00:00~01:00:30", 3600d, 3630d)]
    public void CutSegments_ParsesSupportedSeparators(string text, double start, double end)
    {
        var segs = CutSegments.Parse(text);
        Assert.Single(segs);
        Assert.Equal(start, segs[0].Start, 3);
        Assert.Equal(end, segs[0].End, 3);
    }

    [Fact]
    public void CutSegments_MultipleLines_KeepsOrder()
    {
        var segs = CutSegments.Parse("1-2\n3→4\n5,6");

        Assert.Equal(3, segs.Count);
        Assert.Equal(1d, segs[0].Start);
        Assert.Equal(3d, segs[1].Start);
        Assert.Equal(5d, segs[2].Start);
    }

    [Theory]
    [InlineData("")]
    [InlineData("# 注释\n")]
    [InlineData("abc")]
    [InlineData("-5")]       // 分隔符在开头
    [InlineData("5-")]       // 分隔符在结尾
    [InlineData("10-5")]     // 终点早于起点
    [InlineData("5-5")]      // 零长度
    public void CutSegments_DropsInvalidLines(string text)
        => Assert.Empty(CutSegments.Parse(text));

    [Fact]
    public void CutSegments_IgnoresCommentLine()
        => Assert.Single(CutSegments.Parse("# 第一段\n1-2"));

    // ==================== 剪切命令 ====================

    [Fact]
    public void BuildCutArgs_CopyPreset_AddsMakeZeroAndRelativeDuration()
    {
        var task = new CutTask { SourcePath = @"F:\in.mp4", Start = 5, End = 12 };
        var args = FfmpegService.BuildCutArgs(task, null);

        AssertPair(args, "-ss", "5");
        AssertPair(args, "-to", "7");                                   // -to 是**相对时长**
        AssertPair(args, "-c", "copy");
        AssertPair(args, "-avoid_negative_ts", "make_zero");            // copy + 前置 -ss ⇒ 首段 DTS 可能为负
        Assert.DoesNotContain(@"F:\out.mp4", args);                      // output=null ⇒ 不追加输出路径
    }

    [Fact]
    public void BuildCutArgs_ReEncode_UsesX264AndSkipsMakeZero()
    {
        var task = new CutTask { SourcePath = @"F:\in.mp4", Start = 1, End = 2, ReEncode = true, Crf = 18 };
        var args = FfmpegService.BuildCutArgs(task, @"F:\out.mp4");

        AssertPair(args, "-c:v", "libx264");
        AssertPair(args, "-crf", "18");
        AssertPair(args, "-c:a", "aac");
        Assert.DoesNotContain("-avoid_negative_ts", args);
        Assert.Equal(@"F:\out.mp4", args[^1]);
    }

    [Fact]
    public void BuildCutArgs_MultiSegment_SwitchesToFilterComplex()
    {
        var task = new CutTask { SourcePath = @"F:\in.mp4", SegmentsText = "1-2\n3-4" };
        var args = FfmpegService.BuildCutArgs(task, null);

        var filter = ValueAfter(args, "-filter_complex");
        Assert.Contains("trim=start=1:end=2", filter);
        Assert.Contains("atrim=start=3:end=4", filter);
        Assert.Contains("concat=n=2:v=1:a=1", filter);
        AssertPair(args, "-c:v", "libx264");        // copy 拼不了时间轴 ⇒ 必须重编码
        AssertPair(args, "-map", "[v]");
        AssertPair(args, "-map", "[a]");
        AssertNoPair(args, "-c", "copy");
    }

    [Fact]
    public void BuildCutArgs_ExtraArgsComeAfterPreset()
    {
        var task = new CutTask { SourcePath = @"F:\in.mp4", Start = 0, End = 1, ExtraArgs = "-c:v libx265" };
        var args = FfmpegService.BuildCutArgs(task, null);

        Assert.True(args.IndexOf("-c") < args.IndexOf("-c:v"));   // 后者覆盖前者
        AssertPair(args, "-c:v", "libx265");
    }

    // ==================== 转码命令 ====================

    [Fact]
    public void BuildTranscodeArgs_CopyPresetWithCrop_UpgradesToX264()
    {
        var args = Transcode("copy", t =>
        {
            t.CropWidth = 1920;
            t.CropHeight = 1080;
        });

        AssertNoPair(args, "-c", "copy");            // 滤镜没有落脚处 ⇒ 必须升级
        AssertPair(args, "-c:v", "libx264");
        Assert.Contains("crop=1920:1080:0:0", ValueAfter(args, "-vf"));
    }

    [Fact]
    public void BuildTranscodeArgs_SoftSubtitle_OnlyRemuxes()
    {
        var args = Transcode("h265", t =>
        {
            t.SubtitlePath = @"F:\sub.srt";
            t.SubtitleMode = SubtitleModes.Soft;
        });

        AssertPair(args, "-c", "copy");
        AssertPair(args, "-c:s", "srt");
        AssertPair(args, "-avoid_negative_ts", "make_zero");
        Assert.Contains(@"F:\sub.srt", args);
        Assert.DoesNotContain("-c:v", args);         // 编码设置在本模式下不生效
    }

    [Fact]
    public void BuildTranscodeArgs_TargetSize_BeatsCrf()
    {
        var args = Transcode("h265", t =>
        {
            t.TargetSizeMb = 50;
            t.SourceDuration = 60;
        });

        // 50 × 8192 ÷ 60 = 6826 kbps − 音频 192 − 8 开销
        AssertPair(args, "-b:v", "6626k");
        AssertPair(args, "-maxrate", "9939k");       // 1.5×
        AssertPair(args, "-bufsize", "13252k");      // 2×
        Assert.DoesNotContain("-crf", args);
    }

    [Fact]
    public void BuildTranscodeArgs_GifBranch_HasNoAudioAndUsesPalette()
    {
        var args = Transcode("gif");

        AssertPair(args, "-c:v", "gif");
        AssertPair(args, "-loop", "0");
        Assert.Contains("-an", args);                // GIF 无声
        Assert.DoesNotContain("-crf", args);         // gif 编码器不吃 CRF
        Assert.Contains("palettegen", ValueAfter(args, "-vf"));
        Assert.Contains("paletteuse", ValueAfter(args, "-vf"));
    }

    [Fact]
    public void BuildTranscodeArgs_VolumeOnCopyPreset_SwitchesAudioToAac()
    {
        var args = Transcode("copy", t =>
        {
            t.AudioMode = AudioModes.Volume;
            t.VolumeDb = 6;
        });

        // -af 对 copy 音轨无效 ⇒ 音频必须改 aac，否则 ffmpeg 直接报错
        AssertPair(args, "-c:a", "aac");
        AssertPair(args, "-af", "volume=6dB");
    }

    [Fact]
    public void BuildTranscodeArgs_Mute_EmitsAn()
    {
        var args = Transcode("h265", t => t.AudioMode = AudioModes.Mute);

        Assert.Contains("-an", args);
        Assert.DoesNotContain("-c:a", args);
    }

    [Fact]
    public void BuildTranscodeArgs_ReplaceAudio_TakesSecondInputAndMaps()
    {
        var args = Transcode("h265", t =>
        {
            t.AudioMode = AudioModes.Replace;
            t.ReplaceAudioPath = @"F:\new.m4a";
        });

        AssertPair(args, "-i", @"F:\new.m4a");
        AssertPair(args, "-map", "0:v:0");
        AssertPair(args, "-map", "1:a:0");
        AssertPair(args, "-c:a", "aac");
    }

    [Fact]
    public void BuildTranscodeArgs_AudioOnlyPreset_IsVideoDisabled()
    {
        var args = Transcode("audio");

        Assert.Contains("-vn", args);
        AssertPair(args, "-c:a", "libmp3lame");
        Assert.DoesNotContain("-c:v", args);
    }

    [Fact]
    public void BuildTranscodeArgs_Trim_PutsSsBeforeInput()
    {
        var args = Transcode("h265", t =>
        {
            t.UseTrim = true;
            t.TrimStart = 10;
            t.TrimEnd = 25;
        });

        var ss = args.ToList().IndexOf("-ss");
        var i = args.ToList().IndexOf("-i");
        Assert.True(ss >= 0 && ss < i);              // 前置 -ss = 关键帧快速定位
        AssertPair(args, "-to", "15");                // 相对时长
    }

    [Fact]
    public void BuildTranscodeArgs_GpuEncoder_UsesNvencAndSkipsPresetSpeed()
    {
        var args = Transcode("h264", t => t.UseGpuEncoder = true);

        AssertPair(args, "-c:v", "h264_nvenc");
        Assert.DoesNotContain("-preset", args);      // nvenc 的 -preset 语义不同，不猜
    }

    [Fact]
    public void BuildTranscodeArgs_OutputAppendedOnlyWhenGiven()
    {
        var task = NewTask();
        Assert.DoesNotContain(@"F:\out.mp4", FfmpegService.BuildTranscodeArgs(task, null));
        Assert.Equal(@"F:\out.mp4", FfmpegService.BuildTranscodeArgs(task, @"F:\out.mp4")[^1]);
    }

    // ==================== 预设表 ====================

    [Fact]
    public void PresetTable_HasSingleCopyOfEachId()
    {
        var ids = TranscodePreset.All.Select(p => p.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void PresetFind_UnknownFallsBackToFirst()
        => Assert.Equal(TranscodePreset.All[0].Id, TranscodePreset.Find("nope").Id);

    // ==================== 单行自定义参数 ====================

    [Fact]
    public void SplitArgs_KeepsQuotedSpaces()
    {
        var list = FfmpegService.SplitArgs("-vf \"scale=1280:720, crop=640:480\" -crf 20");

        Assert.Equal(new[] { "-vf", "scale=1280:720, crop=640:480", "-crf", "20" }, list);
    }
}
