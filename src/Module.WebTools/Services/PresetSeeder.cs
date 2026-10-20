using System;
using System.IO;

namespace NewToys.WebTools.Services;

/// <summary>
/// 随包<b>预设规则集</b>的播种器（NT-086 批 E）：
/// <c>&lt;程序目录&gt;\defaults\presets\*.py</c> → <c>&lt;DataDir&gt;\web-tools\rules\</c>（只加不覆盖）。
/// <para>
/// 🔴 **只播种一次**（<c>rules\.seeded</c> 标记）：用户删掉的预设<b>不会复活</b> ——
/// 与 <c>Module.ScriptManager</c> 的 <c>ExampleSeeder</c> 同一套语义。
/// </para>
/// <para>
/// ⚠️ 播种进来的预设就是**普通规则文件**：目录页直接列得出、改得动、删得掉；
/// 「从预设创建」是另复制一份（⛔ 不动原文件）。
/// </para>
/// </summary>
public static class PresetSeeder
{
    /// <summary>随包源目录名（<c>&lt;程序目录&gt;\defaults\presets</c>）。</summary>
    public const string SourceDirName = "presets";

    /// <summary>播种完成标记（内容 = 程序版本）。存在 ⇒ 本次及以后都不再播种。</summary>
    public const string MarkerFileName = ".seeded";

    /// <summary>需要时播种一次（幂等）。返回状态文案（<c>""</c> = 什么都没做）。</summary>
    public static string Seed(RuleFileService files)
    {
        if (files is null) return "";

        var dir = files.RulesDir;
        if (File.Exists(Path.Combine(dir, MarkerFileName))) return "";   // 🔴 只播种一次：删掉的预设不复活

        return CopyDefaults(dir);
    }

    /// <summary>随包预设 → <c>rules\</c>（只加不覆盖），成功后写标记。</summary>
    private static string CopyDefaults(string targetDir)
    {
        var source = GetSourceDir();
        if (source is null) return "";   // 随包没带 presets（开发机直接跑 bin）⇒ 静默跳过

        try
        {
            Directory.CreateDirectory(targetDir);

            var copied = 0;
            foreach (var src in Directory.GetFiles(source, "*" + RuleFileService.Extension))
            {
                var dst = Path.Combine(targetDir, Path.GetFileName(src));
                if (File.Exists(dst)) continue;   // 🔴 用户改过的预设 ⛔ 不覆盖
                File.Copy(src, dst);
                copied++;
            }

            // 标记放在最后：复制中途失败 ⇒ 没有标记 ⇒ 下次启动重试。
            File.WriteAllText(Path.Combine(targetDir, MarkerFileName), AppVersionText());

            return copied == 0 ? "" : $"已播种 {copied} 个预设规则集";
        }
        catch (Exception ex)
        {
            return "⚠ 预设规则集播种失败：" + ex.Message;
        }
    }

    /// <summary>随包预设所在目录（不存在返回 null）。</summary>
    public static string? GetSourceDir()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "defaults", SourceDirName);
        return Directory.Exists(dir) ? dir : null;
    }

    /// <summary>程序版本（写进 <c>.seeded</c> 标记；取不到就写 <c>0</c>）。</summary>
    private static string AppVersionText()
        => typeof(PresetSeeder).Assembly.GetName().Version?.ToString() ?? "0";
}
