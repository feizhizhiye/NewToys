using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NewToys.Core.Services;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

/// <summary>
/// 流水线配置的读写：一个 flow = 一个 <c>flows\&lt;名&gt;.json</c>。
/// <para>
/// 🔴 <b>不开 DB 表</b>：flow 是「段名 → 脚本名」的轻量映射，随 rules 一起被人肉看懂 / 手改 / 拷走，
/// 落 JSON 与「脚本即资产」的范式一致（DB 用来存 runs 之类的大批量数据）。
/// </para>
/// </summary>
public sealed class FlowService
{
    private readonly ConfigStoreService _cfg;
    private bool _legacyMigrated;

    public FlowService(ConfigStoreService cfg) => _cfg = cfg;

    /// <summary>flows 目录：<c>&lt;StoreDir&gt;\script\flows</c>（用户数据，进备份）。</summary>
    public string FlowsDir
    {
        get
        {
            var dir = Path.Combine(_cfg.StoreDir, "script", "flows");
            MigrateLegacyFlows(dir);
            return dir;
        }
    }

    /// <summary>
    /// 与 <see cref="RuleFileService"/> 同款的一次性搬迁 —— 旧 <c>web-tools\flows\*.json</c> 搬进新目录
    /// （只搬一次、⛔ 不覆盖同名新文件、整段 try/catch）。
    /// </summary>
    private void MigrateLegacyFlows(string dir)
    {
        if (_legacyMigrated) return;
        _legacyMigrated = true;
        try
        {
            var old = Path.Combine(_cfg.StoreDir, "web-tools", "flows");
            if (!Directory.Exists(old)) return;
            foreach (var f in Directory.GetFiles(old, "*.json"))
            {
                var dst = Path.Combine(dir, Path.GetFileName(f));
                if (!File.Exists(dst))
                {
                    Directory.CreateDirectory(dir);
                    File.Move(f, dst);
                }
            }
        }
        catch
        {
            // 搬不动 ⇒ 按空流水线表处理。
        }
    }

    /// <summary>已有流水线名（按名字排序；目录不存在 = 空表）。</summary>
    public IReadOnlyList<string> List()
    {
        try
        {
            if (!Directory.Exists(FlowsDir)) return Array.Empty<string>();
            return Directory.GetFiles(FlowsDir, "*.json")
                            .Select(f => Path.GetFileNameWithoutExtension(f))
                            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                            .ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>读一条流水线（不存在 = 新建一条空配置）。</summary>
    public FlowConfig Load(string name)
    {
        var safe = SafeName(name);
        try
        {
            var p = Path.Combine(FlowsDir, safe + ".json");
            if (File.Exists(p))
            {
                var json = File.ReadAllText(p);
                var cfg = JsonSerializer.Deserialize<FlowConfig>(json);
                if (cfg is not null)
                {
                    cfg.Name = safe;
                    return cfg;
                }
            }
        }
        catch { /* 坏文件当新配置处理，⛔ 不让整页挂掉 */ }

        return new FlowConfig { Name = safe };
    }

    /// <summary>写一条流水线（覆盖同名）。</summary>
    public void Save(FlowConfig cfg)
    {
        var safe = SafeName(cfg.Name);
        cfg.Name = safe;
        cfg.UpdatedAt = DateTime.Now;

        Directory.CreateDirectory(FlowsDir);
        var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(FlowsDir, safe + ".json"), json);
    }

    /// <summary>删一条流水线。</summary>
    public bool Delete(string name)
    {
        try
        {
            var p = Path.Combine(FlowsDir, SafeName(name) + ".json");
            if (!File.Exists(p)) return false;
            File.Delete(p);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// 引用某脚本的流水线名（删除引用检查）：脚本被 flow 任一槽位挂着 ⇒ 删不得静默。
    /// </summary>
    public IReadOnlyList<string> ReferencesOf(string scriptName)
    {
        var hits = new List<string>();
        if (string.IsNullOrWhiteSpace(scriptName)) return hits;
        try
        {
            if (!Directory.Exists(FlowsDir)) return hits;
            foreach (var f in Directory.GetFiles(FlowsDir, "*.json"))
            {
                try
                {
                    var cfg = JsonSerializer.Deserialize<FlowConfig>(File.ReadAllText(f));
                    if (cfg is null) continue;
                    foreach (var key in FlowConfig.StageKeys)
                        if (string.Equals(cfg.Slot(key), scriptName, StringComparison.OrdinalIgnoreCase))
                        {
                            hits.Add(cfg.Name.Length > 0 ? cfg.Name : Path.GetFileNameWithoutExtension(f));
                            break;
                        }
                }
                catch { /* 坏文件跳过 */ }
            }
        }
        catch { return hits; }
        return hits;
    }

    /// <summary>文件名清洗（与规则文件同一套口径：<c>Path.GetInvalidFileNameChars</c> 换 <c>_</c>）。</summary>
    public static string SafeName(string name)
    {
        var s = (name ?? "").Trim();
        foreach (var ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
        return s.Length == 0 ? "flow" : s;
    }
}
