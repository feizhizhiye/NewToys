using System;
using System.IO;

namespace NewToys.ScriptManager.Services;

/// <summary>
/// 「信息面板」页的一键播种器（NT-067 §三.7 / 决策 D2）。
/// <para>
/// 随包资源在 <c>&lt;程序目录&gt;\defaults\sysinfo\</c>（只放数据文件、零代码改动 ⇒ 不破框架层冻结）；
/// 首次打开本页（或 <c>sysinfo.manifest.json</c> 缺失）时逐文件复制到 <c>&lt;脚本目录&gt;\sysinfo\</c>。
/// </para>
/// <para>
/// 🔴 <b>只加不覆盖</b>：已存在的文件一律跳过 ⇒ 用户删掉的脚本不会被复活。
/// 播种完成写标记 <c>.seeded</c>（内容 = 程序版本），标记在且 manifest 在 ⇒ 下次不再播种。
/// </para>
/// <para>
/// ⚠️ ⛔ 不回退 NT-060 对「脚本收集 / 命令收集」的空清单结论 —— 本播种器**只服务 sysinfo**，
/// 且落在模块内（⛔ 不进 Core，框架层冻结）。
/// </para>
/// </summary>
public static class SysInfoSeeder
{
    /// <summary>播种完成标记文件名（内容 = 播种时的程序版本）。</summary>
    public const string MarkerFileName = ".seeded";

    /// <summary>
    /// 需要时播种一次。<paramref name="sysInfoDir"/> = <c>&lt;脚本目录&gt;\sysinfo</c>。
    /// </summary>
    /// <returns>状态文案（<c>""</c> = 什么都没做，调用方不用报）。</returns>
    public static string Seed(string sysInfoDir)
    {
        if (string.IsNullOrWhiteSpace(sysInfoDir)) return "";

        var source = GetSourceDir();
        var manifest = Path.Combine(sysInfoDir, SysInfoManifestService.ManifestFileName);
        var marker = Path.Combine(sysInfoDir, MarkerFileName);

        // 已经播过（标记在）且 manifest 还在 ⇒ 不再动（用户删脚本不会被复活）
        if (File.Exists(marker) && File.Exists(manifest)) return "";

        if (source is null)
        {
            // 没随包资源（例如开发期直接跑模块）：只有 manifest 缺失才提示，避免误报
            return File.Exists(manifest) ? "" : "⚠ 没有随包默认脚本（defaults\\sysinfo 不存在）";
        }

        try
        {
            Directory.CreateDirectory(sysInfoDir);
            var copied = 0;
            foreach (var src in Directory.GetFiles(source))
            {
                var dst = Path.Combine(sysInfoDir, Path.GetFileName(src));
                if (File.Exists(dst)) continue;   // 🔴 只加不覆盖
                File.Copy(src, dst);
                copied++;
            }

            File.WriteAllText(marker, AppVersionText());
            return copied == 0 ? "" : $"已播种 {copied} 个默认脚本到 {sysInfoDir}";
        }
        catch (Exception ex)
        {
            return "⚠ 默认脚本播种失败：" + ex.Message;
        }
    }

    /// <summary>随包默认脚本所在目录（不存在返回 null）。</summary>
    public static string? GetSourceDir()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "defaults", SysInfoManifestService.SubFolderName);
        return Directory.Exists(dir) ? dir : null;
    }

    /// <summary>程序版本（写进 <c>.seeded</c> 标记；取不到就写 <c>0</c>）。</summary>
    private static string AppVersionText()
        => typeof(SysInfoSeeder).Assembly.GetName().Version?.ToString() ?? "0";
}
