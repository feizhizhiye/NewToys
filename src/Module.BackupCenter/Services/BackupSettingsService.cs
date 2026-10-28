using System;
using System.IO;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.BackupCenter.Services;

/// <summary>
/// 备份中心的设置与路径（本模块配置的**唯一写入点**）。
/// <para>
/// 选项经 <see cref="IOptionsStore"/> 落 <c>config\backup-center.json</c>；
/// 密码库 / 任务清单 / 包清单 / 运行记录落 <c>config\backup\</c>（与选项分开，损坏时能独立处理）；
/// 归档包落 <c>store\backup\archives</c>（数据，随配置档备份）；解包暂存落 <c>cache\backup\staging</c>（可重建）。
/// </para>
/// </summary>
public sealed class BackupSettingsService
{
    /// <summary><see cref="IOptionsStore"/> 的分区名。</summary>
    public const string Section = "backup-center";

    private const string KeySevenZip = "sevenZipPath";
    private const string KeyKeepCount = "defaultKeepCount";
    private const string KeyArchiveDir = "archiveDir";

    private readonly IOptionsStore _opts;
    private readonly ConfigStoreService _cfg;

    public BackupSettingsService(IOptionsStore opts, ConfigStoreService cfg)
    {
        _opts = opts;
        _cfg = cfg;
    }

    /// <summary>最近一次写选项失败的原因（无失败 = null）。</summary>
    public string? LastError { get; private set; }

    /// <summary>用户显式指定的 7z 路径（空 = 自动探测）。</summary>
    public string ConfiguredSevenZip
    {
        get
        {
            try { return _opts.Get(Section, KeySevenZip) ?? ""; }
            catch { return ""; }
        }
        set => Write(KeySevenZip, string.IsNullOrWhiteSpace(value) ? null : value!.Trim());
    }

    /// <summary>新建任务时的默认保留份数（1~999）。</summary>
    public int DefaultKeepCount
    {
        get
        {
            try
            {
                return int.TryParse(_opts.Get(Section, KeyKeepCount), out var v) ? Math.Clamp(v, 1, 999) : 5;
            }
            catch { return 5; }
        }
        set => Write(KeyKeepCount, Math.Clamp(value, 1, 999).ToString());
    }

    /// <summary>归档包目录（用户没指定 ⇒ <see cref="DefaultArchiveDir"/>）。</summary>
    public string ArchiveDir
    {
        get
        {
            string configured;
            try { configured = _opts.Get(Section, KeyArchiveDir) ?? ""; }
            catch { configured = ""; }
            return string.IsNullOrWhiteSpace(configured) ? DefaultArchiveDir : configured;
        }
        set => Write(KeyArchiveDir, string.IsNullOrWhiteSpace(value) ? null : value!.Trim());
    }

    public string DefaultArchiveDir => Path.Combine(_cfg.StoreDir, "backup", "archives");

    /// <summary>解包暂存根（每次还原用一个新的子目录，结束即删）。</summary>
    public string StagingRoot => Path.Combine(_cfg.CacheDir, "backup", "staging");

    /// <summary>本模块的数据目录（密码库 / 任务 / 包清单 / 运行记录）。</summary>
    public string DataDir => Path.Combine(_cfg.ConfigDir, "backup");

    public string PlansFile => Path.Combine(DataDir, "plans.json");

    public string VaultFile => Path.Combine(DataDir, "vault.json");

    public string CatalogFile => Path.Combine(DataDir, "catalog.json");

    public string RunsFile => Path.Combine(DataDir, "runs.json");

    /// <summary>设置文件（选项真正的落点，页面显示用）。</summary>
    public string SettingsFile => Path.Combine(_cfg.ModulesConfigDir, "backup-center.json");

    private void Write(string key, string? value)
    {
        try
        {
            _opts.Set(Section, key, value);
            LastError = null;
        }
        catch (Exception ex) { LastError = ex.Message; }
    }
}
