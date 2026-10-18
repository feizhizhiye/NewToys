using System;
using System.IO;
using System.Linq;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

/// <summary>
/// 随包<b>示例</b>的播种器：示例脚本文件（.ps1 / .py / .bat / 采集器模板）+ 示例命令（Windows 设置项 / 管理工具）。
/// </summary>
/// <remarks>
/// <para>
/// 定位：v3.66 配置整改第 ④ 条「示例走『随包 defaults + Seeder』」—— 示例只是<b>第一次运行的一次性礼物</b>，
/// 与 <see cref="BuiltinCollectorSeeder"/>（内置采集器，永远保活）刻意区分：
/// </para>
/// <list type="bullet">
///   <item>文件：<c>defaults\scripts\*</c> → <c>&lt;脚本目录&gt;\examples\</c>（只加不覆盖）；</item>
///   <item>条目：<c>role=script</c>（3 个示例脚本）/ <c>role=collector</c>（采集器模板）/ <c>role=command</c>（示例命令）；</item>
///   <item>🔴 <b>只播种一次</b>：<c>examples\.seeded</c> 标记存在 ⇒ 整体跳过。用户删掉的示例<b>不会复活</b> ——
///   这正是示例与内置采集器的分野（内置采集器缺了就补回，示例删了就是删了）。</item>
/// </list>
/// <para>
/// ⛔ 不建集合：脚本 / 命令由 <see cref="ScriptStoreService.SyncBuiltinCollections"/> 自动进「脚本库」「命令库」；
/// 采集器模板要进信息面板就由用户在集合页勾进任意 <c>kind=info</c> 的集合（⛔ 不自作主张塞进「系统信息」）。
/// </para>
/// </remarks>
public static class ExampleSeeder
{
    /// <summary>示例脚本子目录名（<c>&lt;脚本目录&gt;\examples</c>）。</summary>
    public const string SubFolderName = "examples";

    /// <summary>播种完成标记（内容 = 程序版本）。存在 ⇒ 本次及以后都不再播种。</summary>
    public const string MarkerFileName = ".seeded";

    /// <summary>普通示例脚本（role = <c>script</c>）。</summary>
    private static readonly (string File, string Id, string Title, string Description)[] Scripts =
    {
        ("hello-pwsh.ps1", "example-hello-pwsh", "示例：系统概览（PowerShell）",
            "最小 PowerShell 示例：打印本机信息。改这个文件就是你的第一个脚本。"),
        ("hello-python.py", "example-hello-python", "示例：环境速览（Python）",
            "最小 Python 示例：打印解释器与工作目录。需要本机装有 Python。"),
        ("hello-batch.bat", "example-hello-batch", "示例：环境速览（批处理）",
            "最小 .bat 示例：不依赖任何运行时，双击即可跑。"),
    };

    /// <summary>示例采集器（role = <c>collector</c>，输出 <c>attrs</c> JSON，可直接抄的模板）。</summary>
    private const string CollectorFile = "demo-collector.ps1";
    private const string CollectorId = "example-collector-demo";
    private const string CollectorTitle = "示例采集器：本机速览";

    /// <summary>示例命令（role = <c>command</c>，target = 命令行）。</summary>
    private static readonly (string Id, string Title, string Target, string Category, string Description)[] Commands =
    {
        ("example-cmd-display",  "显示设置",   "Start-Process ms-settings:display",    "Windows 设置", "打开系统设置的「显示」页"),
        ("example-cmd-network",  "网络连接",   "ncpa.cpl",                             "Windows 设置", "打开「网络连接」（适配器列表）"),
        ("example-cmd-power",    "电源选项",   "powercfg.cpl",                         "Windows 设置", "打开电源计划与睡眠设置"),
        ("example-cmd-env",      "环境变量",   "rundll32.exe sysdm.cpl,EditEnvironmentVariables", "Windows 设置", "打开用户环境变量编辑框"),
        ("example-cmd-device",   "设备管理器", "devmgmt.msc",                          "管理工具",     "查看 / 更新驱动程序"),
        ("example-cmd-diskmgr",  "磁盘管理",   "diskmgmt.msc",                         "管理工具",     "分区 / 盘符 / 初始化磁盘"),
        ("example-cmd-services", "服务",       "services.msc",                         "管理工具",     "启动类型与服务状态"),
        ("example-cmd-msinfo",   "系统信息",   "msinfo32",                             "管理工具",     "硬件 / 组件 / 环境的详细清单"),
    };

    /// <summary>
    /// 需要时播种一次（幂等）。
    /// </summary>
    /// <returns>状态文案（<c>""</c> = 什么都没做）。</returns>
    public static string Seed(IScriptStoreService store, IScriptManagerSettingsService settings)
    {
        if (store == null || settings == null) return "";

        var dir = Path.Combine(settings.EffectiveScriptsFolder, SubFolderName);
        if (File.Exists(Path.Combine(dir, MarkerFileName))) return "";   // 🔴 只播种一次：删掉的示例不复活

        var note = CopyDefaults(dir);
        if (note.StartsWith("⚠", StringComparison.Ordinal)) return note;  // 复制失败就别登记条目（免得一堆"文件缺失"）

        var dirty = EnsureScripts(store, dir);
        dirty |= EnsureCommands(store);
        if (dirty) store.Save();

        return note;
    }

    /// <summary>随包示例脚本 → <c>&lt;脚本目录&gt;\examples</c>（只加不覆盖），成功后写标记。</summary>
    private static string CopyDefaults(string targetDir)
    {
        var source = GetSourceDir();
        if (source is null) return "";

        try
        {
            Directory.CreateDirectory(targetDir);

            var copied = 0;
            foreach (var src in Directory.GetFiles(source))
            {
                var dst = Path.Combine(targetDir, Path.GetFileName(src));
                if (File.Exists(dst)) continue;
                File.Copy(src, dst);
                copied++;
            }

            // 标记放在最后：复制中途失败 ⇒ 没有标记 ⇒ 下次启动重试。
            File.WriteAllText(Path.Combine(targetDir, MarkerFileName), AppVersionText());

            return copied == 0 ? "" : $"已播种 {copied} 个示例脚本";
        }
        catch (Exception ex)
        {
            return "⚠ 示例脚本播种失败：" + ex.Message;
        }
    }

    /// <summary>登记 3 个示例脚本 + 1 个采集器模板（按 <c>Target</c> 去重，⛔ 不抢已占用的 id）。</summary>
    private static bool EnsureScripts(IScriptStoreService store, string dir)
    {
        var dirty = false;

        foreach (var (file, id, title, description) in Scripts)
        {
            var target = SubFolderName + "/" + file;
            if (Register(store, dir, id, title, "示例", description, target, ScriptItemRole.Script,
                    capture: false, readOnly: false))
                dirty = true;
        }

        if (Register(store, dir, CollectorId, CollectorTitle, "示例",
                "示例采集器：按 attrs 输出 {rows:[{key,status,attrs}]}，可直接当模板改。勾进任意「信息」类集合即可在信息面板看到。",
                SubFolderName + "/" + CollectorFile, ScriptItemRole.Collector, capture: true, readOnly: true))
            dirty = true;

        return dirty;
    }

    /// <summary>登记示例命令（按 id + 命令文本去重）。</summary>
    private static bool EnsureCommands(IScriptStoreService store)
    {
        var dirty = false;

        foreach (var (id, title, target, category, description) in Commands)
        {
            if (store.Data.Items.Any(i => i.Id == id)) continue;
            if (store.Data.Items.Any(i => i.Role == ScriptItemRole.Command
                                          && string.Equals(i.Target, target, StringComparison.OrdinalIgnoreCase)))
                continue;

            store.Data.Items.Add(new ScriptItem
            {
                Id = id,
                Role = ScriptItemRole.Command,
                Title = title,
                Category = category,
                Description = description,
                Target = target,
                Capture = false,        // 命令库的定位是"点一下开窗口"，不采集
                Collectable = false,
                IneligibleReason = "有副作用或需交互",
            });
            dirty = true;
        }

        return dirty;
    }

    /// <summary>加一条脚本型条目（文件不存在则跳过；<c>Target</c> 已存在 ⇒ 跳过）。</summary>
    private static bool Register(IScriptStoreService store, string dir, string id, string title, string category,
        string description, string target, string role, bool capture, bool readOnly)
    {
        if (!File.Exists(Path.Combine(dir, Path.GetFileName(target)))) return false;

        if (store.Data.Items.Any(i => string.Equals(i.Target.Replace('\\', '/'), target, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (store.Data.Items.Any(i => i.Id == id)) return false;

        store.Data.Items.Add(new ScriptItem
        {
            Id = id,
            Role = role,
            Title = title,
            Category = category,
            Description = description,
            Target = target,
            Interpreter = "",
            Capture = capture,
            Readonly = readOnly,
            Collectable = true,
            CollectableSource = ScriptCollectableSource.Auto,
            IneligibleReason = "",
            RetentionMode = ScriptRetentionMode.Latest,
            ParseMode = ScriptParseMode.Json,
            Timeout = 30,
            // 采集器模板是只读的 ⇒ 允许自动刷新（沿内置采集器的节奏）；示例脚本不动。
            Auto = capture,
            IntervalSeconds = capture ? 60 : 0,
        });
        return true;
    }

    /// <summary>随包示例脚本所在目录（不存在返回 null）。</summary>
    public static string? GetSourceDir()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "defaults", "scripts");
        return Directory.Exists(dir) ? dir : null;
    }

    /// <summary>程序版本（写进 <c>.seeded</c> 标记；取不到就写 <c>0</c>）。</summary>
    private static string AppVersionText()
        => typeof(ExampleSeeder).Assembly.GetName().Version?.ToString() ?? "0";
}
