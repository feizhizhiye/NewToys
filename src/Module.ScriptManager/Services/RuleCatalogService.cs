using System;
using System.Collections.Generic;
using System.Linq;
using NewToys.Core.Services;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

/// <summary>
/// 规则目录的元数据与迁移（NT-084）：
/// <list type="bullet">
///   <item>把磁盘上的 <c>.py</c>（<see cref="RuleFileService.Scan"/>）与 <c>web_docs</c> 里的
///         启用状态 / 最近运行合并成列表行。</item>
///   <item>旧 <c>scrape-tasks</c> 一次性转成规则文件（只转一次，<c>LegacyMigrated</c> 标记兜住重复）。</item>
/// </list>
/// <para>
/// 🔴 元数据落 <c>web_docs</c> 文档 <c>rule-meta</c>（**不是** <c>config\</c> 下的文件）；
/// 规则内容本身不在这里 —— 那是 <see cref="RuleFileService"/> 的活。
/// </para>
/// </summary>
public sealed class RuleCatalogService
{
    /// <summary>元数据文档名。</summary>
    public const string DocRuleMeta = "rule-meta";

    /// <summary>
    /// 旧「网页抓取」任务文档名（NT-089：原取自 <c>WebToolsSchemaContributor.DocScrapeTasks</c>；
    /// 该类型留在网络工具模块，模块禁互引 ⇒ 这里留一份同名常量，值必须与之一致）。
    /// </summary>
    private const string DocScrapeTasks = "scrape-tasks";

    private readonly JsonDocStore _docs;
    private readonly RuleFileService _files;

    public RuleCatalogService(SqliteDatabase db, RuleFileService files)
    {
        _docs = new JsonDocStore(db, "web");
        _files = files;
    }

    // ==================== 读写 ====================

    /// <summary>目录列表 = 磁盘文件 + 元数据合并（按名称排序）。</summary>
    public IReadOnlyList<RuleSetMeta> Load()
    {
        EnsureMigrated();

        var doc = LoadDoc();
        var items = _files.Scan().ToList();

        foreach (var item in items)
        {
            var rec = doc.Items.FirstOrDefault(r => string.Equals(r.Name, item.Name, StringComparison.OrdinalIgnoreCase));
            if (rec == null) continue;
            item.Enabled = rec.Enabled;
            item.LastRunAt = rec.LastRunAt;
            item.LastCount = rec.LastCount;
        }

        return items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>整表回写（只写元数据；不存在于磁盘的条目自动丢弃）。</summary>
    public void Save(IEnumerable<RuleSetMeta> items)
    {
        try
        {
            var doc = LoadDoc();
            doc.Items = items
                .Where(i => _files.Exists(i.Name))
                .Select(i => new MetaRecord
                {
                    Name = i.Name,
                    Enabled = i.Enabled,
                    LastRunAt = i.LastRunAt,
                    LastCount = i.LastCount
                })
                .ToList();
            _docs.Save(DocRuleMeta, doc);
        }
        catch { /* 保存失败不打断 UI */ }
    }

    /// <summary>
    /// 记一次运行（NT-086 批 G：目录页「最近运行」列 / 详情卡的数据源）—— 只写元数据，⛔ 不碰文件。
    /// </summary>
    public void MarkRun(string name, DateTime at, int count)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            var doc = LoadDoc();
            var rec = doc.Items.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
            if (rec is null)
            {
                rec = new MetaRecord { Name = name };
                doc.Items.Add(rec);
            }
            rec.LastRunAt = at;
            rec.LastCount = count;
            _docs.Save(DocRuleMeta, doc);
        }
        catch { /* 记不上不打断运行 */ }
    }

    /// <summary>改名以外的编辑都靠文件；这里只删文件 + 元数据。</summary>
    public void Delete(string name)
    {
        _files.Delete(name);
        var doc = LoadDoc();
        doc.Items.RemoveAll(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        try { _docs.Save(DocRuleMeta, doc); } catch { }
    }

    // ==================== 旧数据迁移 ====================

    /// <summary>
    /// 旧 <c>scrape-tasks</c>（单条 <c>Rule</c> + <c>JsScript</c>）→ 规则文件。**只做一次**。
    /// </summary>
    public void EnsureMigrated()
    {
        var doc = LoadDoc();
        if (doc.LegacyMigrated) return;

        try
        {
            var legacy = _docs.Load<List<LegacyTask>>(DocScrapeTasks);
            if (legacy is { Count: > 0 })
            {
                foreach (var task in legacy)
                {
                    var name = RuleFileService.SafeName(string.IsNullOrWhiteSpace(task.Name) ? "旧任务" : task.Name);
                    if (_files.Exists(name)) continue;   // ⛔ 不覆盖同名新文件
                    _files.Write(name, BuildFromLegacy(name, task));
                    doc.Items.Add(new MetaRecord
                    {
                        Name = name,
                        Enabled = true,
                        LastRunAt = task.LastRunAt,
                        LastCount = task.LastCount
                    });
                }
            }

            doc.LegacyMigrated = true;
            _docs.Save(DocRuleMeta, doc);
        }
        catch { /* 迁移失败就下次再试 */ }
    }

    /// <summary>旧任务 → 规则文件文本（<c>Rule</c> 变成一条命令；<c>RuleType</c> 决定前缀）。</summary>
    private static string BuildFromLegacy(string name, LegacyTask task)
    {
        var rule = string.Equals(task.RuleType, "Regex", StringComparison.OrdinalIgnoreCase)
            ? "regex:" + (task.Rule ?? "")
            : "css:" + (task.Rule ?? "");

        var header = string.IsNullOrWhiteSpace(task.JsScript)
            ? ""
            : "\n# TODO（旧数据的「整合脚本」无法自动翻译，请按需改写）：\n"
              + string.Join("\n", task.JsScript!.Split('\n').Take(40).Select(l => "# " + l))
              + "\n";

        return "# NewToys 规则集（由旧「网页抓取」任务自动转换）\n"
             + "# name: " + name + "\n"
             + "# base: " + (task.StartUrl ?? "") + "\n"
             + "# encoding: utf8\n"
             + header
             + "\nSOURCE = {\n"
             + "    \"name\": \"" + (task.Name ?? "").Replace("\"", "\\\"") + "\",\n"
             + "    \"base\": \"" + (task.StartUrl ?? "").Replace("\"", "\\\"") + "\",\n"
             + "    \"encoding\": \"utf8\",\n"
             + "    \"header\": {},\n"
             + "}\n\n"
             + "RULES = [\n"
             + "    (\"result\", \"" + rule.Replace("\"", "\\\"") + "\"),\n"
             + "]\n";
    }

    // ==================== 元数据文档 ====================

    private CatalogDoc LoadDoc()
    {
        try { return _docs.Load<CatalogDoc>(DocRuleMeta) ?? new CatalogDoc(); }
        catch { return new CatalogDoc(); }
    }

    /// <summary>元数据文档根（一条 <c>LegacyMigrated</c> 标记 + 若干条目）。</summary>
    internal sealed class CatalogDoc
    {
        public bool LegacyMigrated { get; set; }
        public List<MetaRecord> Items { get; set; } = new();
    }

    /// <summary>单条元数据（只存"文件里没有"的状态）。</summary>
    internal sealed class MetaRecord
    {
        public string Name { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public DateTime? LastRunAt { get; set; }
        public int LastCount { get; set; }
    }

    /// <summary>旧 <c>scrape-tasks</c> 的形状（只用于迁移，⛔ 别在新代码里用）。</summary>
    internal sealed class LegacyTask
    {
        public string Name { get; set; } = "";
        public string StartUrl { get; set; } = "";
        public string RuleType { get; set; } = "Css";
        public string Rule { get; set; } = "";
        public string? JsScript { get; set; }
        public DateTime? LastRunAt { get; set; }
        public int LastCount { get; set; }
    }
}
