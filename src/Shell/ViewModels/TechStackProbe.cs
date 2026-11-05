using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NewToys.Core.Services;

namespace NewToys.Shell.ViewModels;

/// <summary>「技术栈」清单的一行：一个随程序引入的 NuGet 库及其版本。</summary>
public sealed class TechLibraryItem
{
    public required string Name { get; init; }
    public required string Version { get; init; }

    /// <summary>附加说明（如家族折叠时的包数量）；无则空。</summary>
    public string Note { get; init; } = "";
}

/// <summary>「技术栈」取数器：解析程序目录（含 Modules\）下的 *.deps.json。（WHY: SHL-050）</summary>
public static class TechStackProbe
{
    private const string AvaloniaFamily = "Avalonia";

    /// <summary>返回直接引用的库（Avalonia 家族折叠为一行）；按名称升序，同名多版本取最高。</summary>
    public static IReadOnlyList<TechLibraryItem> Collect()
    {
        var direct = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in EnumerateDepsFiles())
        {
            var (packages, referenced) = ReadDeps(file);
            foreach (var (name, version) in packages)
                if (!referenced.Contains(name)) Merge(direct, name, version);
        }

        return FoldFamily(direct);
    }

    private static IReadOnlyList<TechLibraryItem> FoldFamily(IReadOnlyDictionary<string, string> direct)
    {
        var items = new List<TechLibraryItem>();
        var family = new List<(string Name, string Version)>();

        foreach (var (name, version) in direct)
        {
            if (name == AvaloniaFamily || name.StartsWith(AvaloniaFamily + ".", StringComparison.Ordinal))
                family.Add((name, version));
            else
                items.Add(new TechLibraryItem { Name = name, Version = version });
        }

        if (family.Count > 0)
        {
            // 有主包就报主包版本（Avalonia/12.1.4），否则取族内最高
            var mainVersion = family.Where(f => f.Name == AvaloniaFamily).Select(f => f.Version).FirstOrDefault();
            var version = mainVersion ?? family.OrderByDescending(f => Parse(f.Version)).First().Version;
            items.Add(new TechLibraryItem
            {
                Name = AvaloniaFamily,
                Version = version,
                Note = family.Count > 1 ? $"合并 {family.Count} 个包" : "",
            });
        }

        return items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> EnumerateDepsFiles()
    {
        var baseDir = AppContext.BaseDirectory;
        var files = new List<string>();
        try
        {
            files.AddRange(Directory.EnumerateFiles(baseDir, "*.deps.json", SearchOption.TopDirectoryOnly));

            var modulesDir = Path.Combine(baseDir, ModuleLoader.ModulesSubDirectory);
            if (Directory.Exists(modulesDir))
                files.AddRange(Directory.EnumerateFiles(modulesDir, "*.deps.json", SearchOption.TopDirectoryOnly));
        }
        catch
        {
            // 目录不可读（权限 / 被占用）⇒ 退化成空清单，⛔ 不让设置页打不开
        }
        return files;
    }

    /// <summary>
    /// 读一个 deps.json：返回该文件的 package 清单（名 → 版本）与「被其他包依赖」的名字集合。
    /// 直接引用 = package 清单 − 被依赖集合（csproj 写 <c>PackageReference</c> 的才会落在差集里）。
    /// </summary>
    private static (Dictionary<string, string> Packages, HashSet<string> Referenced) ReadDeps(string file)
    {
        var packages = new Dictionary<string, string>(StringComparer.Ordinal);
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));

            if (doc.RootElement.TryGetProperty("libraries", out var libraries) &&
                libraries.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in libraries.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.Object) continue;
                    if (!entry.Value.TryGetProperty("type", out var type) ||
                        type.ValueKind != JsonValueKind.String ||
                        !string.Equals(type.GetString(), "package", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var slash = entry.Name.LastIndexOf('/');
                    if (slash <= 0 || slash == entry.Name.Length - 1) continue;
                    packages[entry.Name[..slash]] = entry.Name[(slash + 1)..];
                }
            }

            if (doc.RootElement.TryGetProperty("targets", out var targets) &&
                targets.ValueKind == JsonValueKind.Object)
            {
                foreach (var target in targets.EnumerateObject())
                {
                    if (target.Value.ValueKind != JsonValueKind.Object) continue;
                    foreach (var lib in target.Value.EnumerateObject())
                    {
                        // 🔴 只统计 **package** 条目的 dependencies。项目自身条目（如 Shell/1.0.0）
                        //    的 dependencies 恰好就是它的全部直接引用 —— 算进来会让差集恒为空（列表 0 行）。
                        var slash = lib.Name.LastIndexOf('/');
                        if (slash <= 0 || !packages.ContainsKey(lib.Name[..slash])) continue;

                        if (!lib.Value.TryGetProperty("dependencies", out var deps) ||
                            deps.ValueKind != JsonValueKind.Object)
                            continue;
                        foreach (var dep in deps.EnumerateObject()) referenced.Add(dep.Name);
                    }
                }
            }
        }
        catch
        {
            // 单个 deps.json 坏掉不影响其他文件
        }

        return (packages, referenced);
    }

    private static void Merge(Dictionary<string, string> map, string name, string version)
    {
        if (!map.TryGetValue(name, out var existing) || Parse(version) > Parse(existing))
            map[name] = version;
    }

    private static Version Parse(string text) => Version.TryParse(text, out var v) ? v : new Version(0, 0);
}
