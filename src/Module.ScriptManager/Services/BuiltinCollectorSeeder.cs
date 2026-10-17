using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

/// <summary>
/// 内置采集器 + 内置集合的<b>播种器</b>（NT-068 §三.12，取代 NT-067 的 <c>SysInfoSeeder</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 三件事，全部**幂等 + 只加不改**（用户删掉的脚本不会被复活、改名过的条目不会被覆盖）：
/// <list type="number">
///   <item>随包脚本 <c>defaults\sysinfo\*.ps1</c> → <c>&lt;脚本目录&gt;\sysinfo\</c>（只复制缺失的）；</item>
///   <item>这 6 个脚本登记成 <c>role=collector</c> 的条目（<c>readonly=true</c> / <c>capture=true</c> / <c>latest</c>）；</item>
///   <item>建一个 <c>kind=info</c> 的内置集合「系统信息」装它们 —— 否则首次运行信息面板是空的（卡面 §三.7 只列了
///   <c>脚本库</c> / <c>命令库</c> 两个内置集合，这里补一个 info 类的，属加法）。</item>
/// </list>
/// </para>
/// <para>🔴 <c>sysinfo.manifest.json</c> 已废弃：本类<b>不读不写不删</b>那个文件（S8）。</para>
/// </remarks>
public static class BuiltinCollectorSeeder
{
    /// <summary>脚本子目录名（<c>&lt;脚本目录&gt;\sysinfo</c>）。</summary>
    public const string SubFolderName = "sysinfo";

    /// <summary>播种完成标记（内容 = 程序版本）。</summary>
    public const string MarkerFileName = ".seeded";

    /// <summary>内置信息集合名。</summary>
    public const string InfoCollectionName = "系统信息";

    /// <summary>内置采集器定义（文件名 → 标题 / 归类）。</summary>
    private static readonly (string File, string Id, string Title, string Category)[] Collectors =
    {
        ("os.ps1",      "collector-os",      "操作系统", "系统"),
        ("cpu.ps1",     "collector-cpu",     "处理器",   "系统"),
        ("mem.ps1",     "collector-mem",     "内存",     "系统"),
        ("disk.ps1",    "collector-disk",    "磁盘",     "存储"),
        ("net.ps1",     "collector-net",     "网络",     "网络"),
        ("runtime.ps1", "collector-runtime", "运行时",   "开发"),
    };

    /// <summary>
    /// 需要时播种一次。
    /// </summary>
    /// <returns>状态文案（<c>""</c> = 什么都没做）。</returns>
    public static string Seed(IScriptStoreService store, IScriptManagerSettingsService settings)
    {
        if (store == null || settings == null) return "";

        var dir = Path.Combine(settings.EffectiveScriptsFolder, SubFolderName);
        var note = CopyDefaults(dir);

        var dirty = EnsureItems(store, dir);
        dirty |= EnsureInfoCollection(store);

        if (dirty) store.Save();

        return note;
    }

    /// <summary>随包脚本 → 脚本目录（只加不覆盖）。</summary>
    private static string CopyDefaults(string sysInfoDir)
    {
        var source = GetSourceDir();
        if (source is null) return "";

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

            var marker = Path.Combine(sysInfoDir, MarkerFileName);
            if (!File.Exists(marker)) File.WriteAllText(marker, AppVersionText());

            return copied == 0 ? "" : $"已播种 {copied} 个默认采集脚本";
        }
        catch (Exception ex)
        {
            return "⚠ 默认采集脚本播种失败：" + ex.Message;
        }
    }

    /// <summary>把 6 个采集脚本登记成条目（已存在 = 按 <c>Target</c> 命中，⛔ 不动用户改过的字段）。</summary>
    private static bool EnsureItems(IScriptStoreService store, string sysInfoDir)
    {
        var dirty = false;
        foreach (var (file, id, title, category) in Collectors)
        {
            var target = SubFolderName + "/" + file;
            var path = Path.Combine(sysInfoDir, file);
            if (!File.Exists(path)) continue;

            var exists = store.Data.Items.FirstOrDefault(i =>
                string.Equals(i.Target.Replace('\\', '/'), target, StringComparison.OrdinalIgnoreCase));
            if (exists != null) continue;

            // Id 已被占（用户改过目标 / 手工造过同 id）⇒ 不抢，跳过这一条（⛔ 不制造重复 id）。
            if (store.Data.Items.Any(i => i.Id == id)) continue;

            store.Data.Items.Add(new ScriptItem
            {
                Id = id,
                Role = ScriptItemRole.Collector,
                Title = title,
                Category = category,
                Target = target,
                Interpreter = "",
                Capture = true,
                Readonly = true,
                Collectable = true,
                CollectableSource = ScriptCollectableSource.Auto,
                IneligibleReason = "",
                RetentionMode = ScriptRetentionMode.Latest,
                ParseMode = ScriptParseMode.Json,
                Timeout = 30,
                // 内置采集器全是只读 ⇒ 允许自动刷新（§三.9 硬约束 1），进页面即出数据、之后按 60s 节奏刷。
                Auto = true,
                IntervalSeconds = 60,
            });
            dirty = true;
        }
        return dirty;
    }

    /// <summary>内置信息集合「系统信息」：把 6 个内置采集器装进去（只在缺成员时补，⛔ 不动用户改过的排版）。</summary>
    private static bool EnsureInfoCollection(IScriptStoreService store)
    {
        var col = store.Data.Collections.FirstOrDefault(c => c.Builtin && c.Kind == ScriptCollectionKind.Info);
        var dirty = false;
        var created = false;

        if (col == null)
        {
            // 固定 id 被占（用户手工造过）⇒ 换个 id，别抢别人的集合。
            var id = store.Data.Collections.Any(c => c.Id == "builtin-info")
                ? Guid.NewGuid().ToString("N")[..12]
                : "builtin-info";

            col = new ScriptCollection
            {
                Id = id,
                Name = InfoCollectionName,
                Kind = ScriptCollectionKind.Info,
                Icon = "💻",
                Description = "内置采集器的默认集合（可改名、可增减成员；⛔ 不可删）",
                Order = 0,
                Builtin = true,
                Pinned = true,
                RefreshMode = ScriptRefreshMode.OnEnter,
            };
            store.Data.Collections.Add(col);
            dirty = true;
            created = true;
        }

        // 🔴 只在**刚建**时装成员：用户把某个采集器移出这个集合是他的意图，下次启动 ⛔ 不抢回来。
        if (!created) return dirty;

        foreach (var (_, id, _, _) in Collectors)
        {
            var item = store.Data.Items.FirstOrDefault(i => i.Id == id);
            if (item == null) continue;
            if (item.Collections.Contains(col.Id)) continue;
            item.Collections.Add(col.Id);
            dirty = true;
        }

        return dirty;
    }

    /// <summary>随包默认脚本所在目录（不存在返回 null）。</summary>
    public static string? GetSourceDir()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "defaults", SubFolderName);
        return Directory.Exists(dir) ? dir : null;
    }

    /// <summary>程序版本（写进 <c>.seeded</c> 标记；取不到就写 <c>0</c>）。</summary>
    private static string AppVersionText()
        => typeof(BuiltinCollectorSeeder).Assembly.GetName().Version?.ToString() ?? "0";
}
