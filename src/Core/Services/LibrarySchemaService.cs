using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NewToys.Core.Services;

/// <summary>
/// 总库建表的**唯一入口**（NT-076）：容器里所有 <see cref="NewToys.Shared.Services.ILibrarySchemaContributor"/>
/// 按 <c>ModuleKey</c> 排序依次建表，并把每个模块的 schema 版本写进 <c>meta</c>（键 <c>schema.&lt;模块键&gt;</c>）。
/// <para>
/// 调用时机：`ModuleLoader.InitializeAllDeferred` 的**第一步**（首帧之后、各模块重活之前）——
/// 这样任何页面的首次查询都能看到完整的表。
/// </para>
/// <para>
/// 语义：单模块失败只记日志、⛔ 不阻断其它模块；库里版本高于代码版本（= 用旧版软件打开新版库）
/// **不回写版本号**，只记 <see cref="AheadModules"/>，由调用方决定是否提示。
/// </para>
/// <para>
/// NT-076 第二批补充：**任何模块的 schema 要升级前先 <c>VACUUM INTO</c> 快照**（<c>data\backups\</c>），
/// 失败写 <c>logs\migrate-*.log</c>（不可逆操作必须留证据；WAL 下不能靠拷 .db 当备份）。
/// </para>
/// </summary>
public sealed class LibrarySchemaService
{
    private readonly SqliteDatabase _db;
    private readonly ConfigStoreService _cfg;
    private readonly IReadOnlyList<NewToys.Shared.Services.ILibrarySchemaContributor> _contributors;

    public LibrarySchemaService(
        SqliteDatabase db,
        ConfigStoreService cfg,
        IEnumerable<NewToys.Shared.Services.ILibrarySchemaContributor> contributors)
    {
        _db = db;
        _cfg = cfg;
        _contributors = contributors
            .OrderBy(c => c.ModuleKey, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>已建表成功的模块键（诊断用）。</summary>
    public List<string> Applied { get; } = new();

    /// <summary>建表失败的模块键 + 原因（诊断用）。</summary>
    public List<string> Failed { get; } = new();

    /// <summary>库里 schema 版本高于本软件的模块键（旧软件打开新库）。</summary>
    public List<string> AheadModules { get; } = new();

    /// <summary>升级前留的库快照全路径（本次没有升级 / 快照失败 ⇒ 空串）。</summary>
    public string PreMigrateSnapshotPath { get; private set; } = "";

    /// <summary>失败日志全路径（本次没有失败 ⇒ 空串）。</summary>
    public string FailureLogPath { get; private set; } = "";

    /// <summary>给界面用的一句话摘要（没有需要提示的事 ⇒ 空串）。</summary>
    public string Summary { get; private set; } = "";

    /// <summary>
    /// 跑全部贡献者。返回 (成功数, 失败数, 版本超前数)。可重复调用（各实现幂等）。
    /// </summary>
    public (int Applied, int Failed, int Ahead) EnsureAll()
    {
        Applied.Clear();
        Failed.Clear();
        AheadModules.Clear();
        PreMigrateSnapshotPath = "";
        FailureLogPath = "";
        Summary = "";

        var maxVersion = 0;
        var details = new List<string>();

        // ① 先看有没有模块要升级：有 ⇒ 不可逆，先快照（⛔ 不留备份就改库）
        var pending = _contributors
            .Where(c =>
            {
                var raw = _db.GetMeta("schema." + c.ModuleKey);
                return !int.TryParse(raw, out var v) || v < c.SchemaVersion;
            })
            .Select(c => c.ModuleKey)
            .ToList();

        if (pending.Count > 0)
        {
            var snap = _db.VacuumInto(Path.Combine(_cfg.DataDir, "backups"));
            PreMigrateSnapshotPath = snap ?? "";
            details.Add(snap != null
                ? $"升级前快照：{snap}"
                : "⚠ 升级前快照失败（VACUUM INTO 返回空），继续执行 —— 失败时请勿手工清理库文件。");
        }

        foreach (var c in _contributors)
        {
            var key = "schema." + c.ModuleKey;
            try
            {
                var storedRaw = _db.GetMeta(key);
                var hasStored = int.TryParse(storedRaw, out var storedVersion);

                if (hasStored && storedVersion > c.SchemaVersion)
                {
                    // 旧软件打开新版库：⛔ 不降级、⛔ 不改版本号，只报告
                    AheadModules.Add(c.ModuleKey);
                    maxVersion = Math.Max(maxVersion, storedVersion);
                    var ahead = $"{c.ModuleKey}：库版本 {storedVersion} 高于软件版本 {c.SchemaVersion}（旧版软件打开新版库？）";
                    details.Add(ahead);
                    System.Diagnostics.Debug.WriteLine("[NewToys][schema] " + ahead);
                    continue;
                }

                c.EnsureSchema();

                if (!hasStored || storedVersion < c.SchemaVersion)
                    _db.SetMeta(key, c.SchemaVersion.ToString());

                maxVersion = Math.Max(maxVersion, c.SchemaVersion);
                Applied.Add(c.ModuleKey);
            }
            catch (Exception ex)
            {
                Failed.Add(c.ModuleKey);
                details.Add($"{c.ModuleKey} 建表/迁移失败：{ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[NewToys][schema] {c.ModuleKey} 建表失败：{ex.Message}");
            }
        }

        // user_version 只是给外部工具看的总版本（真正的分模块版本在 meta 里）
        if (maxVersion > 0) _db.SetUserVersion(maxVersion);

        if (Failed.Count > 0 || AheadModules.Count > 0)
        {
            FailureLogPath = WriteLog(details);
            Summary = Failed.Count > 0
                ? $"⚠ 总库建表/迁移失败：{string.Join(", ", Failed)}（详见 {FailureLogPath}）"
                : $"⚠ 总库版本高于本软件：{string.Join(", ", AheadModules)}";
            _db.SetMeta("migrate.last-warning", Summary);
        }

        return (Applied.Count, Failed.Count, AheadModules.Count);
    }

    /// <summary>写 <c>logs\migrate-&lt;时间戳&gt;.log</c>。写失败只吞掉（不因为日志写不了而影响启动）。</summary>
    private string WriteLog(List<string> details)
    {
        try
        {
            Directory.CreateDirectory(_cfg.LogsDirectory);
            var path = Path.Combine(_cfg.LogsDirectory, $"migrate-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            var sb = new StringBuilder();
            sb.AppendLine($"# NewToys 总库 schema 日志 · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"库文件：{_db.DatabasePath}");
            sb.AppendLine($"成功：{string.Join(", ", Applied)}");
            sb.AppendLine($"失败：{string.Join(", ", Failed)}");
            sb.AppendLine($"超前：{string.Join(", ", AheadModules)}");
            sb.AppendLine();
            foreach (var d in details) sb.AppendLine("- " + d);
            File.WriteAllText(path, sb.ToString());
            return path;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][schema] 迁移日志写入失败：" + ex.Message);
            return "";
        }
    }
}
