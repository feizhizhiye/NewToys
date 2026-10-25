using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NewToys.Core.Services;
using NewToys.Core.Services.DataMigrations;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

/// <summary>
/// <see cref="IScriptStoreService"/> 实现：手工数据落**总库的 <c>script_docs</c> 表**
///（⛔ 不再写 <c>config\script\script.json</c>），写入即一条 upsert。
/// </summary>
/// <remarks>
/// <para>
/// 🔴 落点在总库里而不是 <c>config\</c>：脚本库的集合 / 条目 / 视图是**功能数据**，与资产表、观测表同库
/// （一个配置档 = 一个库）。旧 <c>script.json</c> 由 <see cref="ScriptManagerSchemaContributor"/> 在启动时
/// 一次性搬进表并改名 <c>.legacy</c>（⛔ 不删）。
/// </para>
/// <para>
/// ⚠️ <see cref="ConfigDir"/> / <see cref="FilePath"/> 保留只为**界面展示与排障**（状态栏会打印落盘位置），
/// ⛔ 不再用于读写。
/// </para>
/// </remarks>
public class ScriptStoreService : IScriptStoreService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // 手改文件里允许写注释 / 尾逗号：宽容读，标准写。
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // 中文 / emoji 原样落盘（不转 \uXXXX），手工可读。
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary><c>script_docs</c> 里的文档名（整份 <see cref="ScriptStoreRoot"/> 一条）。</summary>
    public const string DocName = "store";

    /// <summary>旧 JSON 的落点（迁移源；迁移完成后是 <c>script.json.legacy</c>）。</summary>
    public static string LegacyFile(ConfigStoreService cfg)
        => Path.Combine(cfg.ConfigDir, "script", "script.json");

    private readonly JsonDocStore _docs;
    private readonly string _legacyFile;

    public ScriptStoreService(ConfigStoreService cfg, SqliteDatabase db)
    {
        _docs = new JsonDocStore(db, "script");
        ConfigDir = Path.Combine(cfg.ConfigDir, "script");
        FilePath = db.DatabasePath;
        _legacyFile = LegacyFile(cfg);
        Reload();
    }

    public string ConfigDir { get; }

    public string FilePath { get; }

    public ScriptStoreRoot Data { get; private set; } = new();
    public bool IsLoaded { get; private set; }
    public string LastError { get; private set; } = "";

    public event Action? Changed;

    public void Reload()
    {
        LastError = "";
        try
        {
            var raw = _docs.LoadRaw(DocName);

            if (raw == null)
            {
                // 首次运行（或迁移尚未跑）：先试搬旧 script.json（表已建时才会成功），再按种子起步。
                JsonDocMigration.ImportOnce(_docs, DocName, _legacyFile);
                raw = _docs.LoadRaw(DocName);
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                // 首次运行：落一份带两个内置集合的空文档（需求 §6.2.5 / §三.7）。
                Data = CreateSeed();
                IsLoaded = true;
                Save();
                return;
            }

            var root = JsonSerializer.Deserialize<ScriptStoreRoot>(raw, JsonOpts) ?? new ScriptStoreRoot();

            var dirty = Normalize(root);
            Data = root;
            IsLoaded = true;

            // 归一化补了东西（schema 版本 / 内置集合）⇒ 立刻回写，让库与内存一致。
            if (dirty) Save();
        }
        catch (Exception ex)
        {
            // ⛔ 不抛：坏数据只降级成空数据 + 状态栏提示（验收 §五.15）。
            Data = new ScriptStoreRoot();
            IsLoaded = false;
            LastError = $"读 script_docs 失败：{ex.Message}";
        }
    }

    public void Save()
    {
        try
        {
            Data.SchemaVersion = 1;
            _docs.SaveRaw(DocName, JsonSerializer.Serialize(Data, JsonOpts));
            LastError = "";
        }
        catch (Exception ex)
        {
            LastError = $"写 script_docs 失败：{ex.Message}";
        }
        Changed?.Invoke();
    }

    public void NotifyChanged() => Changed?.Invoke();

    // ==================== 查询 ====================

    public ScriptCollection? FindCollection(string id)
        => string.IsNullOrEmpty(id) ? null : Data.Collections.FirstOrDefault(c => c.Id == id);

    public ScriptItem? FindItem(string id)
        => string.IsNullOrEmpty(id) ? null : Data.Items.FirstOrDefault(i => i.Id == id);

    public IReadOnlyList<ScriptItem> ItemsOf(string collectionId)
        => string.IsNullOrEmpty(collectionId)
            ? Array.Empty<ScriptItem>()
            : Data.Items.Where(i => i.Collections.Contains(collectionId)).ToList();

    public bool SetMembership(string itemId, string collectionId, bool member)
    {
        var item = FindItem(itemId);
        if (item == null || string.IsNullOrEmpty(collectionId)) return false;

        var had = item.Collections.Contains(collectionId);
        if (member == had) return false;

        if (member) item.Collections.Add(collectionId);
        else item.Collections.RemoveAll(c => c == collectionId);
        return true;
    }

    /// <inheritdoc />
    public IReadOnlyList<ScriptItem> MembersOfKind(string kind)
    {
        var k = ScriptCollectionKind.Normalize(kind);
        var ids = Data.Collections.Where(c => !c.Archived && ScriptCollectionKind.Normalize(c.Kind) == k)
                                  .Select(c => c.Id)
                                  .ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0) return Array.Empty<ScriptItem>();
        return Data.Items.Where(i => i.Collections.Any(ids.Contains)).ToList();
    }

    /// <inheritdoc />
    public string RootOf(string collectionId)
    {
        var col = FindCollection(collectionId);
        return (col?.RootPath ?? "").TrimEnd('\\', '/');
    }

    /// <inheritdoc />
    public string RootOfItem(ScriptItem item)
    {
        foreach (var id in item.Collections)
        {
            var root = RootOf(id);
            if (root.Length > 0) return root;
        }
        return "";
    }

    /// <inheritdoc />
    public string GetLastCollectionId(string kind)
    {
        var k = ScriptCollectionKind.Normalize(kind);
        if (Data.LastCollectionIdByKind.TryGetValue(k, out var id) && !string.IsNullOrEmpty(id))
        {
            // 集合被删 / 换了 kind ⇒ 视为没有记录。
            var col = FindCollection(id);
            if (col != null && ScriptCollectionKind.Normalize(col.Kind) == k) return id;
            return "";
        }
        return k == ScriptCollectionKind.Info ? (Data.LastCollectionId ?? "") : "";
    }

    /// <inheritdoc />
    public void SetLastCollectionId(string kind, string id)
    {
        var k = ScriptCollectionKind.Normalize(kind);
        Data.LastCollectionIdByKind[k] = id ?? "";
        if (k == ScriptCollectionKind.Info) Data.LastCollectionId = id ?? "";
    }

    /// <summary>
    /// <b>内置集合成员同步</b>（§三.7，D10 修订）：只把<b>还没有归属</b>的条目收进默认库。
    /// <para>🔴 不再全量同步 —— 否则多库时默认库会吞掉用户归到其他库的条目。</para>
    /// </summary>
    /// <returns>是否有成员变化（需要回写磁盘）。</returns>
    public bool SyncBuiltinCollections()
    {
        var dirty = false;
        foreach (var item in Data.Items)
        {
            if (item.Collections.Count > 0) continue;

            var kind = item.Role is ScriptItemRole.Command ? ScriptCollectionKind.Command : ScriptCollectionKind.Script;
            var col = Data.Collections.FirstOrDefault(c => c.Builtin && !c.Archived
                                                           && ScriptCollectionKind.Normalize(c.Kind) == kind);
            if (col == null) continue;

            item.Collections.Add(col.Id);
            dirty = true;
        }
        return dirty;
    }

    // ==================== 内部 ====================

    /// <summary>首次运行的种子：只有两个内置集合，⛔ 不预置任何条目（干净启动）。</summary>
    private static ScriptStoreRoot CreateSeed()
    {
        var root = new ScriptStoreRoot();
        EnsureBuiltin(root, "默认脚本库", ScriptCollectionKind.Script, "▤", 0);
        EnsureBuiltin(root, "默认命令库", ScriptCollectionKind.Command, "⚙", 1);
        return root;
    }

    /// <summary>补字段 / 补内置集合（只增不改，⛔ 不动用户已有的顺序与名字）。</summary>
    /// <returns>是否发生了<b>需要回写磁盘</b>的补齐（schema 版本 / 内置集合）。</returns>
    private static bool Normalize(ScriptStoreRoot root)
    {
        var dirty = root.SchemaVersion != 1;
        root.SchemaVersion = 1;
        root.Collections ??= new List<ScriptCollection>();
        root.Items ??= new List<ScriptItem>();
        root.Tags ??= new List<ScriptTag>();
        root.Views ??= new List<ScriptView>();
        root.LastCollectionIdByKind ??= new Dictionary<string, string>();

        // 迁移：老 lastCollectionId → byKind["info"]（不动老字段，保存后仍是兼容别名）。
        if (!root.LastCollectionIdByKind.ContainsKey(ScriptCollectionKind.Info)
            && !string.IsNullOrWhiteSpace(root.LastCollectionId))
        {
            root.LastCollectionIdByKind[ScriptCollectionKind.Info] = root.LastCollectionId;
            dirty = true;
        }

        foreach (var c in root.Collections)
        {
            c.Id = string.IsNullOrWhiteSpace(c.Id) ? NewId() : c.Id;
            c.Name = string.IsNullOrWhiteSpace(c.Name) ? "未命名集合" : c.Name;
            c.Kind = ScriptCollectionKind.Normalize(c.Kind);
            c.RefreshMode = ScriptRefreshMode.Normalize(c.RefreshMode);
            c.Tags ??= new List<string>();
            c.RootPath = (c.RootPath ?? "").Trim().TrimEnd('\\', '/');

            // 内置两库改名（只改仍叫旧默认名的，⛔ 不动用户自定义名）。
            if (c.Builtin && ScriptCollectionKind.Normalize(c.Kind) == ScriptCollectionKind.Script && c.Name == "脚本库")
            { c.Name = "默认脚本库"; dirty = true; }
            if (c.Builtin && ScriptCollectionKind.Normalize(c.Kind) == ScriptCollectionKind.Command && c.Name == "命令库")
            { c.Name = "默认命令库"; dirty = true; }
        }

        foreach (var i in root.Items)
        {
            i.Id = string.IsNullOrWhiteSpace(i.Id) ? NewId() : i.Id;
            i.Role = ScriptItemRole.Normalize(i.Role);
            i.RetentionMode = ScriptRetentionMode.Normalize(i.RetentionMode);
            i.ParseMode = ScriptParseMode.Normalize(i.ParseMode);
            i.CollectableSource = ScriptCollectableSource.Normalize(i.CollectableSource);
            i.Columns ??= new List<ScriptColumn>();
            i.Collections ??= new List<string>();
            // 只读条目强制 latest（§三.10）。
            if (i.Readonly) { i.RetentionMode = ScriptRetentionMode.Latest; i.RetentionN = 0; }
        }

        foreach (var v in root.Views)
        {
            v.Id = string.IsNullOrWhiteSpace(v.Id) ? NewId() : v.Id;
            v.Layout = ScriptViewLayout.Normalize(v.Layout);
            v.RefreshMode = ScriptRefreshMode.NormalizeOverride(v.RefreshMode);
            v.Columns ??= new List<ScriptColumn>();
            v.Collapsed ??= new List<string>();
            v.SectionColumns ??= new List<SectionViewColumns>();
            v.SectionOrder ??= new List<string>();

            foreach (var sc in v.SectionColumns)
            {
                sc.Hidden ??= new List<string>();
                sc.Titles ??= new Dictionary<string, string>();
                sc.Widths ??= new Dictionary<string, string>();
            }

            // 一次性迁移：老文件里「列显隐」写在脚本 schema 上（全局），
            // 首次加载按成员默认值把它们收进该集合视图的覆盖记录，之后切列只影响本视图。
            if (v.SectionColumns.Count == 0)
            {
                ViewColumnProjection.InitializeForCollection(v, root.Items.Where(i => i.Collections.Contains(v.CollectionId)));
                if (v.SectionColumns.Count > 0) dirty = true;
            }
        }

        // 内置集合缺失（老文件 / 手删）⇒ 补回来；⛔ 不因缺就清空用户的其它集合。
        if (EnsureBuiltin(root, "默认脚本库", ScriptCollectionKind.Script, "▤", 0)) dirty = true;
        if (EnsureBuiltin(root, "默认命令库", ScriptCollectionKind.Command, "⚙", 1)) dirty = true;

        return dirty;
    }

    /// <returns>是否新补了内置集合。</returns>
    private static bool EnsureBuiltin(ScriptStoreRoot root, string name, string kind, string icon, int order)
    {
        if (root.Collections.Any(c => c.Builtin && c.Kind == kind)) return false;
        root.Collections.Add(new ScriptCollection
        {
            Id = NewId(),
            Name = name,
            Kind = kind,
            Icon = icon,
            Description = $"{name}里的全部条目（默认库：没有归属的条目会自动收进来，可改名、⛔ 不可删）",
            Order = order,
            Builtin = true,
            RefreshMode = ScriptRefreshMode.Manual,
        });
        return true;
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..12];
}
