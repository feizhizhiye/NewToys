using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

/// <summary>库包里的一个条目（NT-073 S7）。脚本条目的 <see cref="Target"/> 是**相对库根**的路径。</summary>
public class LibraryPackageItem
{
    public string Title { get; set; } = "";
    public string Role { get; set; } = ScriptItemRole.Script;
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string Target { get; set; } = "";
    public string Interpreter { get; set; } = "";
    public string Shell { get; set; } = "";
    public bool NeedAdmin { get; set; }
    public bool RunHidden { get; set; }
    public bool Capture { get; set; }
    public bool Readonly { get; set; }
    public bool Favorite { get; set; }
}

/// <summary>库包（索引包 = 单个 <c>library.json</c>；全量包 = zip 里的 <c>library.json</c> + <c>files/…</c>）。</summary>
public class LibraryPackage
{
    public int Version { get; set; } = 1;
    public string Kind { get; set; } = ScriptCollectionKind.Script;
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string RootPath { get; set; } = "";
    public List<LibraryPackageItem> Items { get; set; } = new();
}

/// <summary>
/// 库级导入导出（NT-073 S7）：只服务库页，⛔ 不进 Shared/Core。
/// <para>索引包 = 定义 + 成员（脚本用相对路径）；全量包 = 索引包 + 脚本文件本体。</para>
/// </summary>
public static class ScriptLibraryPackageService
{
    private const string ManifestName = "library.json";
    private const string FilesFolder = "files";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>导出集合为索引包或全量包。返回一句话结果（给状态栏）。</summary>
    public static string Export(IScriptStoreService store, ScriptCollection col, string path, bool bundle)
    {
        var root = store.RootOf(col.Id);
        var package = new LibraryPackage
        {
            Kind = ScriptCollectionKind.Normalize(col.Kind),
            Name = col.Name,
            Description = col.Description ?? "",
            RootPath = root,
        };

        foreach (var item in store.ItemsOf(col.Id).Where(i => i.Role != ScriptItemRole.Package))
        {
            package.Items.Add(new LibraryPackageItem
            {
                Title = item.Title,
                Role = ScriptItemRole.Normalize(item.Role),
                Category = item.Category ?? "",
                Description = item.Description ?? "",
                Target = ToRelative(item.Target, root),
                Interpreter = item.Interpreter ?? "",
                Shell = QuickActionShell.Normalize(item.Shell),
                NeedAdmin = item.NeedAdmin,
                RunHidden = item.RunHidden,
                Capture = item.Capture,
                Readonly = item.Readonly,
                Favorite = item.Favorite,
            });
        }

        if (!bundle)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(package, JsonOpts));
            return $"已导出索引包：{package.Items.Count} 个条目 → {path}";
        }

        // 全量包：先写临时目录（library.json + files/…），再整体打 zip。
        var temp = Path.Combine(Path.GetTempPath(), "newtoys-lib-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            File.WriteAllText(Path.Combine(temp, ManifestName), JsonSerializer.Serialize(package, JsonOpts));

            var copied = 0;
            foreach (var entry in package.Items)
            {
                if (!string.Equals(entry.Role, ScriptItemRole.Command, StringComparison.Ordinal)
                    && entry.Target.Length > 0 && root.Length > 0)
                {
                    var src = Path.IsPathRooted(entry.Target) ? entry.Target : Path.Combine(root, entry.Target);
                    if (!File.Exists(src)) continue;

                    var dst = Path.Combine(temp, FilesFolder, entry.Target.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    File.Copy(src, dst, overwrite: true);
                    copied++;
                }
            }

            if (File.Exists(path)) File.Delete(path);
            ZipFile.CreateFromDirectory(temp, path);
            return $"已导出全量包：{package.Items.Count} 个条目 / {copied} 个文件 → {path}";
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { /* 临时目录清不掉不影响结果 */ }
        }
    }

    /// <summary>
    /// 导入库包：<paramref name="kind"/> = 当前页的 kind（脚本包只能导入脚本页）。
    /// <para>全量包先解到 <paramref name="targetDir"/>（路径穿越会被拒），库根 = 该目录。</para>
    /// </summary>
    public static (bool Ok, string Message) Import(
        IScriptStoreService store, string kind, string path, bool bundle, string targetDir)
    {
        LibraryPackage package;
        var root = "";

        try
        {
            if (bundle)
            {
                if (string.IsNullOrWhiteSpace(targetDir)) return (false, "全量包需要一个解压目录。");
                Directory.CreateDirectory(targetDir);

                using var zip = ZipFile.OpenRead(path);
                foreach (var entry in zip.Entries)
                {
                    var dest = SafeJoin(targetDir, entry.FullName);
                    if (dest == null) continue;                 // 路径穿越 / 绝对路径 ⇒ 跳过
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) { Directory.CreateDirectory(dest); continue; }

                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    entry.ExtractToFile(dest, overwrite: true);
                }

                var manifest = Path.Combine(targetDir, ManifestName);
                if (!File.Exists(manifest)) return (false, "这个 zip 里没有 library.json，不像是库包。");

                package = JsonSerializer.Deserialize<LibraryPackage>(File.ReadAllText(manifest), JsonOpts)
                          ?? throw new InvalidDataException("library.json 解析为空。");
                root = targetDir.TrimEnd('\\', '/');
            }
            else
            {
                package = JsonSerializer.Deserialize<LibraryPackage>(File.ReadAllText(path), JsonOpts)
                          ?? throw new InvalidDataException("索引包解析为空。");
                root = (package.RootPath ?? "").TrimEnd('\\', '/');
                if (root.Length > 0 && !Directory.Exists(root))
                {
                    // 换机后老根目录不存在 ⇒ 退回全局脚本目录（由调用方按 collectable 重新判定）。
                    root = "";
                }
            }
        }
        catch (Exception ex)
        {
            return (false, "读取库包失败：" + ex.Message);
        }

        if (package.Items.Count == 0 && string.IsNullOrWhiteSpace(package.Name))
            return (false, "库包里没有内容（name / items 都为空）。");

        // 建库：名称冲突加后缀（⛔ 不覆盖已有库）。
        var name = string.IsNullOrWhiteSpace(package.Name) ? "导入的库" : package.Name.Trim();
        var unique = name;
        for (var i = 2; store.Data.Collections.Any(c => string.Equals(c.Name, unique, StringComparison.CurrentCulture)); i++)
            unique = $"{name} ({i})";

        var col = new ScriptCollection
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Name = unique,
            Kind = ScriptCollectionKind.Normalize(kind),
            Icon = ScriptCollectionKind.Script == ScriptCollectionKind.Normalize(kind) ? "▤" : "⚙",
            Description = package.Description ?? "",
            Order = store.Data.Collections.Count,
            RootPath = root,
            RefreshMode = ScriptRefreshMode.Manual,
        };
        store.Data.Collections.Add(col);

        var added = 0;
        var reused = 0;
        foreach (var entry in package.Items)
        {
            var item = FindSameItem(store, entry, root);
            if (item == null)
            {
                item = new ScriptItem
                {
                    Id = Guid.NewGuid().ToString("N")[..12],
                    Title = entry.Title ?? "",
                    Role = ScriptItemRole.Normalize(entry.Role),
                    Category = entry.Category ?? "",
                    Description = entry.Description ?? "",
                    Target = entry.Target ?? "",
                    Interpreter = entry.Interpreter ?? "",
                    Shell = QuickActionShell.Normalize(entry.Shell),
                    NeedAdmin = entry.NeedAdmin,
                    RunHidden = entry.RunHidden,
                    Capture = entry.Capture,
                    Readonly = entry.Readonly,
                    Favorite = entry.Favorite,
                };
                store.Data.Items.Add(item);
                added++;
            }
            else
            {
                reused++;
            }

            if (!item.Collections.Contains(col.Id)) item.Collections.Add(col.Id);
        }

        return (true, $"已导入库「{unique}」：新建 {added} 条，复用已有 {reused} 条。");
    }

    // ==================== 内部 ====================

    /// <summary>同一个物理文件 / 同一条命令只对应一条 item（D3 绝对路径去重）。</summary>
    private static ScriptItem? FindSameItem(IScriptStoreService store, LibraryPackageItem entry, string root)
    {
        var target = (entry.Target ?? "").Trim();
        if (target.Length == 0) return null;

        if (string.Equals(ScriptItemRole.Normalize(entry.Role), ScriptItemRole.Command, StringComparison.Ordinal))
            return store.Data.Items.FirstOrDefault(i =>
                string.Equals(ScriptItemRole.Normalize(i.Role), ScriptItemRole.Command, StringComparison.Ordinal)
                && string.Equals((i.Target ?? "").Trim(), target, StringComparison.OrdinalIgnoreCase));

        foreach (var item in store.Data.Items)
        {
            if (string.Equals(ScriptItemRole.Normalize(item.Role), ScriptItemRole.Command, StringComparison.Ordinal)) continue;
            var a = Absolute(item.Target, store.RootOfItem(item));
            var b = Absolute(target, root);
            if (a.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return item;
        }
        return null;
    }

    private static string Absolute(string? target, string root)
    {
        var t = (target ?? "").Trim();
        if (t.Length == 0) return "";
        if (Path.IsPathRooted(t)) return Path.GetFullPath(t);
        return root.Length == 0 ? "" : Path.GetFullPath(Path.Combine(root, t));
    }

    private static string ToRelative(string? target, string root)
    {
        var t = (target ?? "").Trim();
        if (t.Length == 0 || root.Length == 0 || !Path.IsPathRooted(t)) return t;

        var full = Path.GetFullPath(t);
        var baseDir = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return full.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase)
            ? full[baseDir.Length..].Replace('\\', '/')
            : t;
    }

    /// <summary>解压路径安全：越界（<c>..</c> / 绝对路径）返回 null。</summary>
    private static string? SafeJoin(string baseDir, string entryName)
    {
        var full = Path.GetFullPath(Path.Combine(baseDir, entryName));
        var baseFull = Path.GetFullPath(baseDir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return full.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}
