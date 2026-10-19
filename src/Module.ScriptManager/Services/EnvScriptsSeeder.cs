using System;
using System.IO;
using System.Linq;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

/// <summary>
/// 「环境维护」内置集合的播种器（NT-083 批 3 · 2026-10-19 用户定调）。
/// <para>
/// <b>为什么放集合里</b>：环境准备脚本（建 Python venv / 装依赖）本来就是"软件自己的维护脚本"，
/// 塞进脚本管理器的**集合列表**后：① 不用记路径，点一下就跑；② 复用现有的运行 / 输出捕获 / 超时；
/// ③ 以后加 ffmpeg 检查、winget 源导出之类，全是这个集合的成员，不新增概念。
/// </para>
/// <para>
/// <b>与 <see cref="ExampleSeeder"/> 的分工</b>：示例脚本是"教学材料"，环境脚本是"真在用的工具"，
/// 所以分开两个随包目录（<c>defaults\env-scripts\</c> vs <c>defaults\scripts\</c>）、两个目标目录。
/// </para>
/// <para>
/// 🔴 **只播种一次**（<c>.seeded</c> 标记）：用户删掉集合 / 改坏脚本是他的意图，下次启动 ⛔ 不复活、⛔ 不覆盖。
/// </para>
/// </summary>
public static class EnvScriptsSeeder
{
    /// <summary>随包源目录名（<c>&lt;程序目录&gt;\defaults\env-scripts</c>）。</summary>
    public const string SourceDirName = "env-scripts";

    /// <summary>落盘子目录名（<c>&lt;脚本目录&gt;\env</c>）。</summary>
    public const string TargetSubFolder = "env";

    /// <summary>播种完成标记（内容 = 程序版本）。</summary>
    public const string MarkerFileName = ".seeded";

    /// <summary>集合名（⛔ 不可删，可改名）。</summary>
    public const string CollectionName = "环境维护";

    /// <summary>集合固定 id。</summary>
    public const string CollectionId = "builtin-env";

    /// <summary>环境脚本条目（id / 文件名 / 标题 / 说明 / 超时秒）。</summary>
    private static readonly (string Id, string FileName, string Title, string Description, int Timeout)[] Scripts =
    {
        ("env-python-setup", "setup-python-env.ps1", "环境：准备 Python",
            "建共享 venv（%LOCALAPPDATA%\\NewToys\\tools\\py\\venv）并安装 requests / lxml / parsel。" +
            "幂等：已存在只补装；装崩了改用 -Force 重建。跑完把结尾打印的解释器路径填进「设置 → 运行环境」。",
            900),
    };

    /// <summary>需要时播种一次（幂等）。返回状态文案（<c>""</c> = 什么都没做）。</summary>
    public static string Seed(IScriptStoreService store, IScriptManagerSettingsService settings)
    {
        if (store == null || settings == null) return "";

        var dir = Path.Combine(settings.EffectiveScriptsFolder, TargetSubFolder);
        var marker = Path.Combine(dir, MarkerFileName);
        if (File.Exists(marker)) return "";   // 🔴 只播种一次：删掉的集合 / 脚本不复活

        var note = CopyDefaults(dir);
        if (note.StartsWith("⚠", StringComparison.Ordinal)) return note;   // 复制失败就别登记条目

        var dirty = EnsureScripts(store, dir);
        dirty |= EnsureCollection(store);
        if (dirty) store.Save();

        return note;
    }

    /// <summary>随包环境脚本 → <c>&lt;脚本目录&gt;\env</c>（只加不覆盖），成功后写标记。</summary>
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
                if (File.Exists(dst)) continue;   // 🔴 用户改过的脚本 ⛔ 不覆盖
                File.Copy(src, dst);
                copied++;
            }

            // 标记最后写：中途失败 ⇒ 没标记 ⇒ 下次启动重试
            File.WriteAllText(Path.Combine(targetDir, MarkerFileName), AppVersionText());

            return copied == 0 ? "" : $"已播种 {copied} 个环境脚本";
        }
        catch (Exception ex)
        {
            return "⚠ 环境脚本播种失败：" + ex.Message;
        }
    }

    /// <summary>登记环境脚本条目（文件不存在则跳过；已存在的 <c>Target</c> / <c>Id</c> 不重复登记）。</summary>
    private static bool EnsureScripts(IScriptStoreService store, string dir)
    {
        var dirty = false;

        foreach (var (id, fileName, title, description, timeout) in Scripts)
        {
            if (!File.Exists(Path.Combine(dir, fileName))) continue;

            var target = Path.Combine(TargetSubFolder, fileName).Replace('\\', '/');

            if (store.Data.Items.Any(i => i.Id == id)) continue;
            if (store.Data.Items.Any(i => string.Equals(i.Target.Replace('\\', '/'), target, StringComparison.OrdinalIgnoreCase)))
                continue;

            store.Data.Items.Add(new ScriptItem
            {
                Id = id,
                Role = "script",
                Title = title,
                Category = CollectionName,
                Description = description,
                Target = target,
                Interpreter = "",
                Capture = true,          // 输出要能看（装包过程有进度）
                Readonly = false,
                // 🔴 有副作用（写磁盘 / 装包）⇒ 不进采集器候选池
                Collectable = false,
                CollectableSource = ScriptCollectableSource.Auto,
                IneligibleReason = "环境维护脚本有副作用（写磁盘 / 安装依赖），不适合当采集器",
                RetentionMode = ScriptRetentionMode.Latest,
                ParseMode = ScriptParseMode.Line,
                Timeout = timeout,
                Auto = false,
                IntervalSeconds = 0,
            });
            dirty = true;
        }

        return dirty;
    }

    /// <summary>建「环境维护」集合（脚本库类），只在**刚建**时装成员 —— 用户移出的成员 ⛔ 不抢回来。</summary>
    private static bool EnsureCollection(IScriptStoreService store)
    {
        var col = store.Data.Collections.FirstOrDefault(c => c.Id == CollectionId);
        var created = false;

        if (col == null)
        {
            col = new ScriptCollection
            {
                Id = CollectionId,
                Name = CollectionName,
                Kind = ScriptCollectionKind.Script,
                Icon = "🧰",
                Description = "软件自身的环境维护脚本（Python 环境等）。可改名、可增减成员；⛔ 不可删。",
                Order = 1,
                Builtin = true,
                Pinned = false,
            };
            store.Data.Collections.Add(col);
            created = true;
        }

        if (!created) return false;

        foreach (var (id, _, _, _, _) in Scripts)
        {
            var item = store.Data.Items.FirstOrDefault(i => i.Id == id);
            if (item == null) continue;
            if (!item.Collections.Contains(col.Id)) item.Collections.Add(col.Id);
        }

        return true;
    }

    /// <summary>随包环境脚本所在目录（不存在返回 null）。</summary>
    public static string? GetSourceDir()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "defaults", SourceDirName);
        return Directory.Exists(dir) ? dir : null;
    }

    private static string AppVersionText()
        => typeof(EnvScriptsSeeder).Assembly.GetName().Version?.ToString() ?? "0";
}
