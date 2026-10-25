using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>备份包的 manifest（<c>manifest.json</c>，）。</summary>
public sealed class BackupManifest
{
    /// <summary>写备份时的程序版本（<c>AssemblyInformationalVersion</c>，形如 <c>1.20261019.14.20</c>）。</summary>
    public string App { get; set; } = "";

    /// <summary>容器格式版本：1 = 旧的单 JSON（<c>Files[]</c>），2 = zip + 分节。</summary>
    public int Format { get; set; } = BackupService.FormatZip;

    public DateTime CreatedAt { get; set; }

    /// <summary>导出时所在的配置档名（恢复时不同 ⇒ 只提示，不拒绝）。</summary>
    public string Profile { get; set; } = "";

    public List<BackupManifestSection> Sections { get; set; } = new();
}

/// <summary>manifest 里的一条节记录。</summary>
public sealed class BackupManifestSection
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Entry { get; set; } = "";
    public int Schema { get; set; }
    public long EstimatedBytes { get; set; }
    public bool Machine { get; set; }
}

/// <summary>导出结果。</summary>
public sealed record BackupExportResult(string Path, long Bytes, IReadOnlyList<string> SectionIds);

/// <summary>恢复结果。</summary>
public sealed record BackupRestoreResult(int Ok, int Skipped, IReadOnlyList<string> Notes);

/// <summary>
/// 备份 / 恢复的**唯一入口**（需求 §3.5）。
/// <para>
/// 容器 = 一个 zip：<c>manifest.json</c> + 每个节一个 <c>&lt;id&gt;.zip</c>（内层 zip，见
/// <see cref="IBackupSection"/>）。节由 DI 收集（框架节在 Shell，模块节在各自 <c>Module.*</c> 里注册）。
/// </para>
/// <para>
/// 🔴 三条铁律：
/// <list type="number">
///   <item>**只 zip 不 JSON** —— db / 缩略图无法进 JSON，且 zip 才支持「只恢复其中一节」；</item>
///   <item>**恢复前校验** —— <c>manifest.app</c> 主版本不一致 ⇒ 拒绝；节的 <c>schema</c> 高于当前 ⇒ 跳过该节并提示升级；</item>
///   <item>**恢复不覆盖数据** —— 设置节覆盖原文件（先归档 <c>.bak-&lt;时间戳&gt;</c>），数据节一律恢复到新文件（批次 B）。</item>
/// </list>
/// </para>
/// <para>⚠️ 旧格式（v1 单 JSON 的 <c>Files[]</c>）只保留**只读导入**，⛔ 本服务不再产出该格式。</para>
/// </summary>
public sealed class BackupService
{
    /// <summary>zip 容器格式版本。</summary>
    public const int FormatZip = 2;

    private const string ManifestEntryName = "manifest.json";

    private readonly ConfigStoreService _store;
    private readonly IReadOnlyList<IBackupSection> _sections;

    public BackupService(ConfigStoreService store, IEnumerable<IBackupSection> sections)
    {
        _store = store;
        // 排序稳定：Scope 决定大块顺序，Order 决定块内顺序，Id 兜底（⛔ 依赖 DI 的注册顺序）
        _sections = (sections ?? Array.Empty<IBackupSection>())
            .Where(s => s != null)
            .OrderBy(s => (int)s.Scope)
            .ThenBy(s => s.Order)
            .ThenBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>容器里全部可备份的节（已排序）。</summary>
    public IReadOnlyList<IBackupSection> Sections => _sections;

    /// <summary>按 ID 找节（找不到 ⇒ null）。</summary>
    public IBackupSection? Find(string id)
        => _sections.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 按**入口预设**挑节：<c>settings</c> = 默认勾选的节（Shell + Module），<c>data</c> = <see cref="BackupScope.Data"/>。
    /// <para>界面上「设置备份 / 数据备份」两个入口就是这两个预设（见 <c>DataView</c>）。</para>
    /// </summary>
    public IReadOnlyList<IBackupSection> Preset(string kind)
        => string.Equals(kind, "data", StringComparison.OrdinalIgnoreCase)
            ? _sections.Where(s => s.Scope == BackupScope.Data).ToList()
            : _sections.Where(s => s.Scope != BackupScope.Data && s.DefaultSelected).ToList();

    // ==================== 导出 ====================

    /// <summary>
    /// 导出选中的节到 <paramref name="zipPath"/>。
    /// <para>先写同目录临时文件再改名 —— 中途失败不会留下半个备份，也不会毁掉同名旧备份。</para>
    /// </summary>
    public BackupExportResult Export(string zipPath, IEnumerable<string>? sectionIds = null)
    {
        var picked = (sectionIds == null
                ? _sections.Where(s => s.DefaultSelected).ToList()
                : sectionIds.Select(Find).Where(s => s != null).Select(s => s!).ToList());

        if (picked.Count == 0)
            throw new InvalidOperationException("没有选中任何备份节。");

        var manifest = new BackupManifest
        {
            App = UpdateService.CurrentVersion,
            Format = FormatZip,
            CreatedAt = DateTime.Now,
            Profile = _store.ActiveProfileName,
        };

        var tmp = zipPath + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);

        try
        {
            using (var fs = File.Create(tmp))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var section in picked)
                {
                    // 外层不压缩：压缩交给节自己（数据节的 db 压缩收益低，JSON 有收益）
                    var entry = zip.CreateEntry($"{section.Id}.zip", CompressionLevel.NoCompression);
                    using (var es = entry.Open()) section.ExportTo(es);

                    manifest.Sections.Add(new BackupManifestSection
                    {
                        Id = section.Id,
                        Title = section.Title,
                        Scope = section.Scope.ToString(),
                        Entry = $"{section.Id}.zip",
                        Schema = section.SchemaVersion,
                        EstimatedBytes = section.EstimateBytes(),
                        Machine = section.IsMachineBound,
                    });
                }

                // manifest 最后写（先有节内容，再落索引）
                var mEntry = zip.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                using var ms = mEntry.Open();
                using var writer = new StreamWriter(ms, new System.Text.UTF8Encoding(false));
                writer.Write(JsonSerializer.Serialize(manifest, JsonOpts));
            }

            File.Move(tmp, zipPath, overwrite: true);
            var bytes = new FileInfo(zipPath).Length;
            return new BackupExportResult(zipPath, bytes, picked.Select(s => s.Id).ToList());
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* 临时文件删不掉不影响结果 */ }
        }
    }

    // ==================== 读取 ====================

    /// <summary>读 zip 的 manifest（不是 zip / 没有 manifest ⇒ null）。</summary>
    public BackupManifest? ReadManifest(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.GetEntry(ManifestEntryName);
            if (entry == null) return null;
            using var stream = entry.Open();
            return JsonSerializer.Deserialize<BackupManifest>(stream, JsonOpts);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][备份] manifest 读取失败：" + ex.Message);
            return null;
        }
    }

    /// <summary>是不是旧的 v1 单 JSON 备份（按扩展名 + 首个非空白字符判断，⛔ 不整份解析）。</summary>
    public static bool LooksLikeLegacyJson(string path)
    {
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return false;
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            using var reader = new StreamReader(path);
            var buffer = new char[64];
            var n = reader.Read(buffer, 0, buffer.Length);
            var head = new string(buffer, 0, n).TrimStart();
            return head.StartsWith("{", StringComparison.Ordinal);
        }
        catch { return false; }
    }

    /// <summary>
    /// 恢复前预检：版本兼容 + 与当前配置的差异说明。
    /// <returns>(能否继续, 提示语列表)。能继续但含风险时，提示语非空（由界面弹确认）。</returns>
    /// </summary>
    public (bool CanRestore, IReadOnlyList<string> Notes) Validate(BackupManifest manifest)
    {
        var notes = new List<string>();

        if (manifest.Format > FormatZip)
            return (false, new[] { $"备份格式版本 {manifest.Format} 高于本程序支持的 {FormatZip} —— 请先升级软件。" });

        if (!SameMajor(manifest.App, UpdateService.CurrentVersion))
            return (false, new[] { $"备份来自版本 {manifest.App}，与当前 {UpdateService.CurrentVersion} 的主版本不一致，不做恢复。" });

        if (!string.Equals(manifest.Profile, _store.ActiveProfileName, StringComparison.OrdinalIgnoreCase))
            notes.Add($"备份来自配置档「{manifest.Profile}」，当前是「{_store.ActiveProfileName}」—— 恢复后当前配置档的设置会被覆盖。");

        if (manifest.Sections.Any(s => s.Machine))
            notes.Add("备份里含本机相关的绝对路径（ffmpeg / 解释器等），换机器后可能失效，恢复完请到「设置 → 运行环境」复核。");

        if (manifest.Sections.Any(s => s.Scope == nameof(BackupScope.Data)))
            notes.Add("备份里含数据节（库 / 缩略图 / 脚本文件），恢复会**写成新副本来**，不会覆盖当前库。");

        var unknown = manifest.Sections.Where(s => Find(s.Id) == null).Select(s => s.Title.Length > 0 ? s.Title : s.Id).ToList();
        if (unknown.Count > 0)
            notes.Add($"以下节在本版本里不存在，会被跳过：{string.Join("、", unknown)}");

        return (true, notes);
    }

    private static bool SameMajor(string a, string b)
    {
        static string Major(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return "";
            var i = v.IndexOf('.');
            return i < 0 ? v.Trim() : v[..i].Trim();
        }
        var ma = Major(a);
        var mb = Major(b);
        return ma.Length == 0 || mb.Length == 0 || string.Equals(ma, mb, StringComparison.Ordinal);
    }

    // ==================== 恢复 ====================

    /// <summary>
    /// 恢复选中（默认 = 备份里全部）的节。
    /// <para>节自己负责落盘细节（设置节覆盖 + 先归档、数据节写新副本）；本方法只管版本闸与逐节隔离。</para>
    /// </summary>
    public BackupRestoreResult Restore(string zipPath, BackupManifest manifest, IEnumerable<string>? only = null)
    {
        var notes = new List<string>();
        var wanted = only == null ? null : new HashSet<string>(only, StringComparer.OrdinalIgnoreCase);
        var ok = 0;
        var skipped = 0;
        var restoredData = false;

        using var zip = ZipFile.OpenRead(zipPath);
        // 数据节统一落到同一个 data\restore-<时间戳>\（会话由节自己经 BackupPaths.CurrentRestoreDir 取）
        var before = SnapshotRestoreDirs();
        using var session = BackupPaths.BeginRestore(_store.DataDir);
        foreach (var record in manifest.Sections)
        {
            if (wanted != null && !wanted.Contains(record.Id)) continue;

            var live = Find(record.Id);
            if (live == null)
            {
                skipped++;
                notes.Add($"跳过「{record.Title}」：本版本没有这个节。");
                continue;
            }

            if (record.Schema > live.SchemaVersion)
            {
                skipped++;
                notes.Add($"跳过「{live.Title}」：备份格式版本 {record.Schema} 高于当前 {live.SchemaVersion}，请升级软件。");
                continue;
            }

            var entry = zip.GetEntry(record.Entry) ?? zip.GetEntry($"{record.Id}.zip");
            if (entry == null)
            {
                skipped++;
                notes.Add($"跳过「{live.Title}」：备份包里找不到节数据。");
                continue;
            }

            try
            {
                var (wrote, skip) = live.RestoreFrom(entry);
                ok++;
                if (string.Equals(record.Scope, nameof(BackupScope.Data), StringComparison.OrdinalIgnoreCase)) restoredData = true;
                if (skip > 0) notes.Add($"「{live.Title}」：{wrote} 项已恢复，{skip} 项被跳过（路径不合法或已归档的产物）。");
            }
            catch (Exception ex)
            {
                skipped++;
                notes.Add($"「{live.Title}」恢复失败：{ex.Message}");
            }
        }

        // 数据节是"恢复成新副本" ⇒ 必须告诉用户副本在哪，否则等于没恢复。
        // 口径 = 恢复前后 data\ 下 restore-* 目录的差集（比"猜最近一个"可靠）。
        if (restoredData)
        {
            var created = SnapshotRestoreDirs().Except(before).OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (created != null)
                notes.Add($"数据节已恢复成新副本：{created}（未覆盖当前库；要使用它请自己替换 / 换配置档）。");
        }

        return new BackupRestoreResult(ok, skipped, notes);
    }

    /// <summary><c>data\</c> 下现有的 <c>restore-*</c> 目录（恢复前后对比用）。</summary>
    private IReadOnlyCollection<string> SnapshotRestoreDirs()
    {
        try
        {
            var dir = _store.DataDir;
            return Directory.Exists(dir)
                ? Directory.GetDirectories(dir, "restore-*")
                : Array.Empty<string>();
        }
        catch { return Array.Empty<string>(); }
    }

    // ==================== 旧格式（只读兼容） ====================

    /// <summary>
    /// 导入 v1 单 JSON 备份（<c>Files[]</c> 或早期界面的 <c>files[{path,content}]</c>）。
    /// <para>⛔ 不产出新格式；⛔ 不做版本 / 节校验（旧格式没有这些信息）。</para>
    /// </summary>
    public (int Ok, int Skipped, string? BackupDir) ImportLegacy(string jsonPath)
        => _store.ImportBackup(File.ReadAllText(jsonPath));

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
