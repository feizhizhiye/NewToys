using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NewToys.Core.Services;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

/// <summary>
/// <see cref="IScriptStoreService"/> 实现：手工数据落 <c>config\script\script.json</c>，<b>原子写</b>（NT-068 §三.2 / §三.4）。
/// </summary>
/// <remarks>
/// <para>
/// 🔴 为什么是 <c>config\script\</c> 而不是 <c>config\script\settings.json</c>：模块<b>设置</b>走
/// <c>IOptionsStore</c>，其 <c>Validate</c> 禁路径分隔符 ⇒ 只能落 <c>config\script-manager.json</c>。
/// 本服务管的是<b>手工数据</b>（集合 / 条目 / 视图），不进 <c>IOptionsStore</c>，所以能开子目录。
/// </para>
/// <para>
/// 只读 <c>ConfigStoreService</c> 的 <see cref="ConfigStoreService.ConfigDir"/> /
/// <see cref="ConfigStoreService.DataDir"/>（框架层冻结，⛔ 不新增 <c>GetModuleDataDir</c>）。
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

    public ScriptStoreService(ConfigStoreService cfg)
    {
        ConfigDir = Path.Combine(cfg.ConfigDir, "script");
        DataDir = Path.Combine(cfg.DataDir, "script");
        FilePath = Path.Combine(ConfigDir, "script.json");
        DatabasePath = Path.Combine(DataDir, "script.db");
        Reload();
    }

    public string ConfigDir { get; }
    public string DataDir { get; }
    public string FilePath { get; }
    public string DatabasePath { get; }

    public ScriptStoreRoot Data { get; private set; } = new();
    public bool IsLoaded { get; private set; }
    public string LastError { get; private set; } = "";

    public event Action? Changed;

    public void Reload()
    {
        LastError = "";
        try
        {
            Directory.CreateDirectory(ConfigDir);

            if (!File.Exists(FilePath))
            {
                // 首次运行：落一份带两个内置集合的空文件（需求 §6.2.5 / NT-068 §三.7）。
                Data = CreateSeed();
                IsLoaded = true;
                Save();
                return;
            }

            var json = File.ReadAllText(FilePath);
            var root = string.IsNullOrWhiteSpace(json)
                ? new ScriptStoreRoot()
                : JsonSerializer.Deserialize<ScriptStoreRoot>(json, JsonOpts) ?? new ScriptStoreRoot();

            var dirty = Normalize(root);
            Data = root;
            IsLoaded = true;

            // 归一化补了东西（schema 版本 / 内置集合）⇒ 立刻回写，让磁盘与内存一致。
            if (dirty) Save();
        }
        catch (Exception ex)
        {
            // ⛔ 不抛：坏文件只降级成空数据 + 状态栏提示（验收 §五.15）。
            Data = new ScriptStoreRoot();
            IsLoaded = false;
            LastError = $"读 {FilePath} 失败：{ex.Message}";
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            Data.SchemaVersion = 1;

            var json = JsonSerializer.Serialize(Data, JsonOpts);
            WriteAtomic(FilePath, json);
            LastError = "";
        }
        catch (Exception ex)
        {
            LastError = $"写 {FilePath} 失败：{ex.Message}";
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

    /// <summary>
    /// <b>内置集合成员同步</b>（NT-068 §三.7）：<c>脚本库</c> 集合收全部 <c>role∈{script,collector}</c>，
    /// <c>命令库</c> 集合收全部 <c>role=command</c>。只读关系 —— 用户要自定义就新建集合。
    /// </summary>
    /// <returns>是否有成员变化（需要回写磁盘）。</returns>
    public bool SyncBuiltinCollections()
    {
        var dirty = false;
        foreach (var col in Data.Collections.Where(c => c.Builtin && !c.Archived))
        {
            var wantsScript = col.Kind == ScriptCollectionKind.Script;
            var wantsCommand = col.Kind == ScriptCollectionKind.Command;
            if (!wantsScript && !wantsCommand) continue;

            foreach (var item in Data.Items)
            {
                var belongs = wantsScript
                    ? item.Role is ScriptItemRole.Script or ScriptItemRole.Collector
                    : item.Role == ScriptItemRole.Command;

                var has = item.Collections.Contains(col.Id);
                if (belongs == has) continue;

                if (belongs) item.Collections.Add(col.Id);
                else item.Collections.RemoveAll(c => c == col.Id);
                dirty = true;
            }
        }
        return dirty;
    }

    // ==================== 内部 ====================

    /// <summary>原子写：同目录临时文件 + 一次 rename 覆盖（同卷 rename 是原子操作）。</summary>
    private static void WriteAtomic(string path, string content)
    {
        var dir = Path.GetDirectoryName(path)!;
        var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tmp, content);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* 残留临时文件不影响主流程 */ }
        }
    }

    /// <summary>首次运行的种子：只有两个内置集合，⛔ 不预置任何条目（NT-060 干净启动）。</summary>
    private static ScriptStoreRoot CreateSeed()
    {
        var root = new ScriptStoreRoot();
        EnsureBuiltin(root, "脚本库", ScriptCollectionKind.Script, "▤", 0);
        EnsureBuiltin(root, "命令库", ScriptCollectionKind.Command, "⚙", 1);
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

        foreach (var c in root.Collections)
        {
            c.Id = string.IsNullOrWhiteSpace(c.Id) ? NewId() : c.Id;
            c.Name = string.IsNullOrWhiteSpace(c.Name) ? "未命名集合" : c.Name;
            c.Kind = ScriptCollectionKind.Normalize(c.Kind);
            c.RefreshMode = ScriptRefreshMode.Normalize(c.RefreshMode);
            c.Tags ??= new List<string>();
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
        }

        // 内置集合缺失（老文件 / 手删）⇒ 补回来；⛔ 不因缺就清空用户的其它集合。
        if (EnsureBuiltin(root, "脚本库", ScriptCollectionKind.Script, "▤", 0)) dirty = true;
        if (EnsureBuiltin(root, "命令库", ScriptCollectionKind.Command, "⚙", 1)) dirty = true;

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
            Description = $"{name}里的全部条目（内置集合，可改名、不可删）",
            Order = order,
            Builtin = true,
            RefreshMode = ScriptRefreshMode.Manual,
        });
        return true;
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..12];
}
