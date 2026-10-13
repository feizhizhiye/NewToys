using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NewToys.Core.Services;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.Services;

/// <summary>用户数据 JSON 的包裹（自描述：格式名 + 版本 + 导出时间）。</summary>
public sealed class UserDataJsonFile
{
    public string Format { get; set; } = UserDataExport.FormatName;
    public int Version { get; set; } = 1;
    public string ExportedAt { get; set; } = "";

    public List<TagJson> Tags { get; set; } = new();
    public List<CategoryJson> Categories { get; set; } = new();
    public List<CollectionJson> Collections { get; set; } = new();
    public List<RelationJson> Relations { get; set; } = new();
}

public sealed class TagJson
{
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string Color { get; set; } = "";
}

public sealed class CategoryJson
{
    public string Name { get; set; } = "";
    /// <summary>父分类**名字**（⛔ 不存内部 id；恢复时按名字找，找不到挂顶级）。</summary>
    public string Parent { get; set; } = "";
}

public sealed class CollectionJson
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = CollectionTypes.Manual;
    public string Description { get; set; } = "";
    public string RuleJson { get; set; } = "";
    /// <summary>成员按**文件绝对路径**存（⛔ 不存内部 id；恢复时按路径找资产，找不到跳过）。</summary>
    public List<string> Members { get; set; } = new();
}

public sealed class RelationJson
{
    public string SourcePath { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public string Type { get; set; } = RelationTypes.RelatedTo;
}

/// <summary>
/// **用户手动数据**的导出 / 恢复（NT-022，R1 缓解）：标签 / 分类 / 集合 / 关联 4 类。
/// <para>
/// 🔴 为什么需要它：这 4 类是用户手工维护的，却落在"不进备份"的 <c>data\newtoys.db</c>——
/// 删库重建就会全丢。导出成 <c>config\media-assets-user-data.json</c> 就能进「数据管理」的备份。
/// </para>
/// <para>
/// 🔴 结构**自描述、⛔ 不依赖内部 id**：分类的父子用名字、集合成员用文件路径、关联用两端路径；
/// 恢复时按 name/path 重建映射。恢复 = **合并**（⛔ 不静默覆盖已有的同名项；缺的才建）。
/// </para>
/// </summary>
public class UserDataExport
{
    public const string FormatName = "newtoys-media-assets-user-data";

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly AssetService _assets;
    private readonly TagService _tags;
    private readonly CategoryService _categories;
    private readonly CollectionService _collections;
    private readonly RelationService _relations;
    private readonly ConfigStoreService _cfg;

    public UserDataExport(AssetService assets, TagService tags, CategoryService categories,
                          CollectionService collections, RelationService relations, ConfigStoreService cfg)
    {
        _assets = assets;
        _tags = tags;
        _categories = categories;
        _collections = collections;
        _relations = relations;
        _cfg = cfg;
    }

    public bool IsAvailable => _assets.IsAvailable;

    /// <summary>导出文件全路径（<c>config\media-assets-user-data.json</c>，进「数据管理」备份）。</summary>
    public string FilePath => Path.Combine(_cfg.ConfigDir, "media-assets-user-data.json");

    /// <summary>导出 4 类用户数据为 JSON 文本。</summary>
    public string ExportJson()
    {
        var file = new UserDataJsonFile { ExportedAt = AssetService.Now() };

        foreach (var t in _tags.List())
            file.Tags.Add(new TagJson
            {
                Name = t.Name,
                Namespace = t.Namespace ?? "",
                Color = t.Color ?? ""
            });

        foreach (var c in _categories.List())
            file.Categories.Add(new CategoryJson { Name = c.Name, Parent = ParentName(c.ParentId) });

        foreach (var c in _collections.List())
        {
            var members = new List<string>();
            foreach (var assetId in _collections.ListMemberIds(c.Id))
            {
                var loc = _assets.FindPrimaryLocation(assetId);
                if (loc != null && !string.IsNullOrEmpty(loc.Path)) members.Add(loc.Path);
            }
            file.Collections.Add(new CollectionJson
            {
                Name = c.Name,
                Type = c.Type,
                Description = c.Description ?? "",
                RuleJson = c.RuleJson ?? "",
                Members = members
            });
        }

        // 关联：要遍历全部资产才能找全 relations（relations 表没有"全部"查询入口）——
        // 用列表页的行查询拿一批资产，逐个 ListFor（量级 = 资产数，⛔ 一次性任务可接受）。
        foreach (var row in _assets.QueryRows(null, true, 5000))
        {
            foreach (var rel in _relations.ListFor(row.Id, 200))
            {
                // 两个方向都算 → 每条会被看到两次；只在"源 = 当前行"时记一次（⛔ 不重复导出）
                if (rel.SourceAssetId != row.Id) continue;
                var src = _assets.FindPrimaryLocation(rel.SourceAssetId);
                var dst = _assets.FindPrimaryLocation(rel.TargetAssetId);
                if (src == null || dst == null) continue;
                file.Relations.Add(new RelationJson
                {
                    SourcePath = src.Path,
                    TargetPath = dst.Path,
                    Type = rel.RelationType
                });
            }
        }

        return JsonSerializer.Serialize(file, JsonOpts);
    }

    /// <summary>导出并写盘到 <see cref="FilePath"/>。返回（成功 / 消息）。</summary>
    public (bool Ok, string Message) ExportToFile()
    {
        try
        {
            var json = ExportJson();
            Directory.CreateDirectory(_cfg.ConfigDir);
            File.WriteAllText(FilePath, json);
            return (true, $"已导出用户数据（标签 / 分类 / 集合 / 关联）到：{FilePath}");
        }
        catch (Exception ex)
        {
            return (false, "导出失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 从 JSON 恢复用户数据（**合并**：同名已存在 → 保留现状；缺的才建；成员 / 关联按路径挂接）。
    /// ⛔ 不清空、不覆盖任何已有数据。
    /// </summary>
    public (bool Ok, string Message) RestoreFromJson(string json)
    {
        if (!IsAvailable) return (false, "资产库不可用（数据库打不开）。");
        if (string.IsNullOrWhiteSpace(json)) return (false, "文件是空的。");

        UserDataJsonFile? file = null;
        try
        {
            file = JsonSerializer.Deserialize<UserDataJsonFile>(json);
        }
        catch (Exception ex)
        {
            return (false, "不是合法的 JSON：" + ex.Message);
        }
        if (file == null || (!string.Equals(file.Format, FormatName, StringComparison.OrdinalIgnoreCase)))
            return (false, $"格式不对（期望 {FormatName}）。");

        var newTags = 0; var newCats = 0; var newCols = 0; var linkedMembers = 0; var newRels = 0;
        var skipped = 0;

        foreach (var t in file.Tags.Where(t => !string.IsNullOrWhiteSpace(t.Name)))
        {
            var existing = _tags.List().FirstOrDefault(x => string.Equals(x.Name, t.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (existing != null) { skipped++; continue; }
            var id = _tags.Create(t.Name.Trim(), string.IsNullOrWhiteSpace(t.Namespace) ? null : t.Namespace.Trim(),
                string.IsNullOrWhiteSpace(t.Color) ? null : t.Color.Trim());
            if (id > 0) newTags++;
        }

        // 分类：先建顶级的、再建有父的（⛔ 不管插入顺序——父可能排在后面）
        foreach (var c in file.Categories.Where(c => !string.IsNullOrWhiteSpace(c.Name) && string.IsNullOrWhiteSpace(c.Parent)))
        {
            if (FindCategory(c.Name) != null) { skipped++; continue; }
            if (_categories.Create(c.Name.Trim()) > 0) newCats++;
        }
        foreach (var c in file.Categories.Where(c => !string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.Parent)))
        {
            if (FindCategory(c.Name) != null) { skipped++; continue; }
            var parent = FindCategory(c.Parent);
            var id = _categories.Create(c.Name.Trim(), parent?.Id);
            if (id > 0) newCats++;
        }

        foreach (var c in file.Collections.Where(c => !string.IsNullOrWhiteSpace(c.Name)))
        {
            long id;
            var existing = _collections.List().FirstOrDefault(x => string.Equals(x.Name, c.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (existing != null) { id = existing.Id; skipped++; }
            else
            {
                id = _collections.Create(c.Name.Trim(),
                    string.Equals(c.Type, CollectionTypes.Smart, StringComparison.OrdinalIgnoreCase) ? CollectionTypes.Smart : CollectionTypes.Manual,
                    string.IsNullOrWhiteSpace(c.RuleJson) ? null : c.RuleJson,
                    string.IsNullOrWhiteSpace(c.Description) ? null : c.Description);
                if (id > 0) newCols++;
            }
            if (id <= 0) continue;

            foreach (var path in c.Members.Where(m => !string.IsNullOrWhiteSpace(m)))
            {
                var assetId = _assets.FindAssetIdByPath(path.Trim());
                if (assetId == null) { skipped++; continue; }
                if (_collections.AddAssets(id, new[] { assetId.Value }) > 0) linkedMembers++;
            }
        }

        foreach (var r in file.Relations)
        {
            var s = _assets.FindAssetIdByPath((r.SourcePath ?? "").Trim());
            var t = _assets.FindAssetIdByPath((r.TargetPath ?? "").Trim());
            if (s == null || t == null) { skipped++; continue; }
            if (_relations.Add(s.Value, t.Value, string.IsNullOrWhiteSpace(r.Type) ? RelationTypes.RelatedTo : r.Type.Trim()) > 0)
                newRels++;
        }

        return (true, $"恢复完成：新标签 {newTags} / 新分类 {newCats} / 新集合 {newCols} / 成员挂接 {linkedMembers} / 关联 {newRels}；跳过（已存在或找不到路径）{skipped}。");
    }

    // ==================== 内部 ====================

    private CategoryNode? FindCategory(string name)
    {
        var want = (name ?? "").Trim();
        if (want.Length == 0) return null;
        return _categories.List().FirstOrDefault(c => string.Equals(c.Name, want, StringComparison.OrdinalIgnoreCase));
    }

    private string ParentName(long? parentId)
    {
        if (parentId == null) return "";
        var parent = _categories.List().FirstOrDefault(c => c.Id == parentId.Value);
        return parent?.Name ?? "";
    }
}
