using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NewToys.MediaAssets.Models;
using NewToys.MediaAssets.Services;

namespace NewToys.MediaAssets.Services;

/// <summary>资产列表 JSON 的一条（NT-022）。路径是主键；组织信息按**名字**存（⛔ 不存内部 id）。</summary>
public sealed class AssetJsonItem
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Type { get; set; } = AssetTypes.Other;
    public long Size { get; set; }
    public string Mtime { get; set; } = "";
    public string Title { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public int Rating { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> Categories { get; set; } = new();
    public List<string> Collections { get; set; } = new();
}

/// <summary>资产列表 JSON 的包裹（自描述：格式名 + 版本 + 导出时间 + 范围）。</summary>
public sealed class AssetJsonFile
{
    public string Format { get; set; } = AssetImportExport.FormatName;
    public int Version { get; set; } = 1;
    public string ExportedAt { get; set; } = "";
    public string Scope { get; set; } = "";
    public List<AssetJsonItem> Items { get; set; } = new();
}

/// <summary>
/// 资产列表的 JSON 导入 / 导出（NT-022，R1 缓解的一部分）。
/// <para>
/// 🔴 **导入 = 追加合并**（⛔ 不静默覆盖）：按绝对路径去重（已有 → 跳过），新的按所在目录
/// 登记 / 复用存储源后入库（复用 <see cref="AssetService.CommitRows"/> 的事务），再补
/// 评分 / 描述 / 标签 / 分类 / 集合（组织信息按名字 get-or-create）。
/// </para>
/// </summary>
public class AssetImportExport
{
    public const string FormatName = "newtoys-media-assets-list";

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly AssetService _assets;
    private readonly TagService _tags;
    private readonly CategoryService _categories;
    private readonly CollectionService _collections;

    public AssetImportExport(AssetService assets, TagService tags, CategoryService categories, CollectionService collections)
    {
        _assets = assets;
        _tags = tags;
        _categories = categories;
        _collections = collections;
    }

    public bool IsAvailable => _assets.IsAvailable;

    /// <summary>
    /// 把一批行导成 JSON 文本（当前列表 / 全量由调用方给）。
    /// 组织信息走**批量查询**（⛔ 不 per-row 查：500 行 = 3 次查询）。
    /// </summary>
    public string Export(IReadOnlyList<AssetRow> rows, string scope)
    {
        var tagMap = _assets.LoadOrgLists(rows.Select(r => r.Id).ToList(), "asset_tags", "tags");
        var catMap = _assets.LoadOrgLists(rows.Select(r => r.Id).ToList(), "asset_categories", "categories");
        var colMap = _assets.LoadOrgLists(rows.Select(r => r.Id).ToList(), "collection_assets", "collections");

        var file = new AssetJsonFile
        {
            ExportedAt = AssetService.Now(),
            Scope = scope,
            Items = rows.Select(r => new AssetJsonItem
            {
                FilePath = r.FilePath,
                FileName = r.FileName,
                Type = r.Type,
                Size = r.Size,
                Mtime = r.Mtime ?? "",
                Title = r.Title ?? "",
                DisplayName = r.DisplayName ?? "",
                Description = r.Description ?? "",
                Rating = r.Rating,
                Tags = tagMap.TryGetValue(r.Id, out var t) ? t : new List<string>(),
                Categories = catMap.TryGetValue(r.Id, out var c) ? c : new List<string>(),
                Collections = colMap.TryGetValue(r.Id, out var k) ? k : new List<string>()
            }).ToList()
        };

        return JsonSerializer.Serialize(file, JsonOpts);
    }

    /// <summary>
    /// 导入一段 JSON：**追加合并**。返回（成功 / 消息 / 新增 / 跳过 / 组织挂接数）。
    /// ⛔ 解析失败 / 格式不对 → Ok=false 且消息说清原因（⛔ 不吞异常返回空）。
    /// </summary>
    public (bool Ok, string Message, int Added, int Skipped, int Linked) Import(string json)
    {
        if (!IsAvailable) return (false, "资产库不可用（数据库打不开）。", 0, 0, 0);
        if (string.IsNullOrWhiteSpace(json)) return (false, "文件是空的。", 0, 0, 0);

        AssetJsonFile? file = null;
        try
        {
            file = JsonSerializer.Deserialize<AssetJsonFile>(json);
        }
        catch (Exception ex)
        {
            return (false, "不是合法的 JSON：" + ex.Message, 0, 0, 0);
        }
        if (file?.Items == null || file.Items.Count == 0)
            return (false, "JSON 里没有条目（或不是本程序导出的清单）。", 0, 0, 0);
        if (!string.Equals(file.Format, FormatName, StringComparison.OrdinalIgnoreCase))
            return (false, $"格式不对（format = {file.Format}，期望 {FormatName}）。", 0, 0, 0);

        // ① 文件内按路径去重 + 与库内已有位置去重（路径比较忽略大小写）
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fresh = new List<AssetJsonItem>();
        var skipped = 0;
        foreach (var it in file.Items)
        {
            var path = (it.FilePath ?? "").Trim();
            if (path.Length == 0) { skipped++; continue; }
            if (!seen.Add(path)) { skipped++; continue; }              // 文件内重复
            if (_assets.FindAssetIdByPath(path) != null) { skipped++; continue; }   // 库里已有
            fresh.Add(it);
        }
        if (fresh.Count == 0)
            return (true, $"全部 {file.Items.Count} 条都已在库里（跳过 {skipped} 条），没有新增。", 0, skipped, 0);

        // ② 批量入库（CommitRows：按目录分组、登记 / 复用存储源、一个事务；回填 Id）
        var rows = fresh.Select(it => new AssetRow
        {
            Name = !string.IsNullOrWhiteSpace(it.DisplayName) ? it.DisplayName
                 : !string.IsNullOrWhiteSpace(it.Title) ? it.Title
                 : Path.GetFileNameWithoutExtension(it.FileName.Length > 0 ? it.FileName : it.FilePath),
            Title = it.Title,
            DisplayName = it.DisplayName,
            Type = string.IsNullOrWhiteSpace(it.Type) ? ScanService.GuessAssetType(Path.GetExtension(it.FilePath)) : it.Type,
            Status = AssetStatuses.Active,
            LocationStatus = LocationStatuses.Active,
            FileName = it.FileName.Length > 0 ? it.FileName : Path.GetFileName(it.FilePath),
            FilePath = it.FilePath,
            Size = it.Size,
            Mtime = it.Mtime
        }).ToList();

        var added = _assets.CommitRows(rows);
        if (added == 0)
            return (false, "写入资产库失败（事务被回滚）——可能数据库不可用。", 0, skipped, 0);

        // ③ 补评分 / 描述 + 组织信息（按名字 get-or-create；⛔ 不静默丢）
        var linked = 0;
        foreach (var (row, it) in rows.Zip(fresh))
        {
            if (row.Id <= 0) continue;
            if (it.Rating > 0 || !string.IsNullOrWhiteSpace(it.Description))
                _assets.SetRatingAndDescription(row.Id, it.Rating, it.Description ?? "");

            foreach (var name in it.Tags.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                var id = _tags.Ensure(name.Trim());
                if (id > 0 && _tags.AddTagToAssets(new[] { row.Id }, id) > 0) linked++;
            }
            foreach (var name in it.Categories.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                var id = FindOrCreateCategory(name.Trim());
                if (id > 0 && _categories.AddAssets(id, new[] { row.Id }) > 0) linked++;
            }
            foreach (var name in it.Collections.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                var id = FindOrCreateCollection(name.Trim());
                if (id > 0 && _collections.AddAssets(id, new[] { row.Id }) > 0) linked++;
            }
        }

        return (true, $"导入完成：新增 {added} / 跳过 {skipped} / 组织挂接 {linked}。", added, skipped, linked);
    }

    /// <summary>
    /// **只解析**一段 JSON 成预览行（⛔ 不写库、⛔ 不去重）——2026-10-16 用户定稿：
    /// 「📥 导入」= 把 db/json 的内容**加载到列表显示**（草稿态预览），写入由「💾 加入库」决定。
    /// 返回（成功 / 消息 / 行）；解析失败 / 格式不对 ⇒ Ok=false 且消息说清原因（⛔ 不吞异常返回空）。
    /// </summary>
    public (bool Ok, string Message, List<AssetRow> Rows) Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (false, "文件是空的。", new List<AssetRow>());

        AssetJsonFile? file = null;
        try
        {
            file = JsonSerializer.Deserialize<AssetJsonFile>(json);
        }
        catch (Exception ex)
        {
            return (false, "不是合法的 JSON：" + ex.Message, new List<AssetRow>());
        }
        if (file?.Items == null || file.Items.Count == 0)
            return (false, "JSON 里没有条目（或不是本程序导出的清单）。", new List<AssetRow>());
        if (!string.Equals(file.Format, FormatName, StringComparison.OrdinalIgnoreCase))
            return (false, $"格式不对（format = {file.Format}，期望 {FormatName}）。", new List<AssetRow>());

        var rows = new List<AssetRow>();
        foreach (var it in file.Items)
        {
            var path = (it.FilePath ?? "").Trim();
            if (path.Length == 0) continue;
            var fileName = it.FileName ?? "";
            rows.Add(new AssetRow
            {
                // Id=0（草稿态）：入库时 CommitRows 统一分配
                // 🔴 `?? ""` 三连：① 消 CS8601（Path.GetFileName* 返回 string?）；② 防 JSON 注入 null
                //    （System.Text.Json 无视默认值，`{"FilePath":null}` 真能赋进 null ⇒ UI 绑定 / 后续读取 NRE）
                Name = (!string.IsNullOrWhiteSpace(it.DisplayName) ? it.DisplayName
                     : !string.IsNullOrWhiteSpace(it.Title) ? it.Title
                     : Path.GetFileNameWithoutExtension(fileName.Length > 0 ? fileName : path)) ?? "",
                Title = it.Title,
                DisplayName = it.DisplayName,
                Description = it.Description,
                Rating = it.Rating,
                Type = string.IsNullOrWhiteSpace(it.Type) ? ScanService.GuessAssetType(Path.GetExtension(path)) : it.Type,
                Status = AssetStatuses.Active,
                LocationStatus = LocationStatuses.Active,
                FileName = !string.IsNullOrEmpty(fileName) ? fileName : (Path.GetFileName(path) ?? ""),
                FilePath = path,
                Size = it.Size,
                Mtime = it.Mtime,
                // 组织信息原文带进预览（逗号拼接，与库态 TagsText 同格式）
                TagsText = string.Join(", ", it.Tags.Where(t => !string.IsNullOrWhiteSpace(t))),
                CategoriesText = string.Join(", ", it.Categories.Where(c => !string.IsNullOrWhiteSpace(c))),
                CollectionsText = string.Join(", ", it.Collections.Where(c => !string.IsNullOrWhiteSpace(c))),
            });
        }
        if (rows.Count == 0)
            return (false, "JSON 条目里没有有效路径。", new List<AssetRow>());

        return (true, $"已解析 {rows.Count} 条。", rows);
    }

    /// <summary>按名字找分类，找不到就建（顶级）。</summary>
    private long FindOrCreateCategory(string name)
    {
        foreach (var n in _categories.List())
            if (string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase))
                return n.Id;
        return _categories.Create(name);
    }

    /// <summary>按名字找集合，找不到就建（手动集合）。</summary>
    private long FindOrCreateCollection(string name)
    {
        foreach (var c in _collections.List())
            if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                return c.Id;
        return _collections.Create(name, CollectionTypes.Manual);
    }
}
