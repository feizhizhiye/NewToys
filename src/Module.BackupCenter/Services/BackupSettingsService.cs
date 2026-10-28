using System;
using System.IO;
using System.Linq;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.BackupCenter.Services;

/// <summary>
/// 备份中心的设置与路径（本模块配置的**唯一写入点**）。
/// <para>
/// 选项经 <see cref="IOptionsStore"/> 落 <c>config\backup-center.json</c>；
/// 任务 / 包清单 / 运行记录 / 待办 / 密码库落 SQLite 库 <c>store\backup\backup.db</c>；
/// 归档包落「默认备份目录」（未指定 = <c>%USERPROFILE%\NewToys\Backup</c>）；解包暂存落 <c>cache\backup\staging</c>（可重建）。
/// </para>
/// <para>
/// 🔴 7z 路径**不在这里存**：它是「本机怎么跑外部程序」，属应用级运行环境（<c>env.json</c>，
/// 唯一写入点 =「设置 → 运行环境」）。本类只做转发，避免同一份值存两处。
/// </para>
/// </summary>
public sealed class BackupSettingsService
{
    /// <summary><see cref="IOptionsStore"/> 的分区名。</summary>
    public const string Section = "backup-center";

    /// <summary>已废弃的键（7z 路径已上提到应用级运行环境），仅用于一次性迁移。</summary>
    private const string LegacyKeySevenZip = "sevenZipPath";

    private const string KeyKeepCount = "defaultKeepCount";
    private const string KeyArchiveDir = "archiveDir";

    private readonly IOptionsStore _opts;
    private readonly ConfigStoreService _cfg;
    private readonly IAppConfigService _app;

    public BackupSettingsService(IOptionsStore opts, ConfigStoreService cfg, IAppConfigService app)
    {
        _opts = opts;
        _cfg = cfg;
        _app = app;
        MigrateLegacySevenZip();
        MigrateLegacyArchiveDir();
    }

    /// <summary>最近一次写选项失败的原因（无失败 = null）。</summary>
    public string? LastError { get; private set; }

    /// <summary>用户显式指定的 7z 路径（空 = 自动探测）。读写都落到应用级 <c>env.json</c>。</summary>
    public string ConfiguredSevenZip
    {
        get
        {
            try { return _app.SevenZipPath ?? ""; }
            catch { return ""; }
        }
        set
        {
            try { _app.SevenZipPath = (value ?? "").Trim(); LastError = null; }
            catch (Exception ex) { LastError = ex.Message; }
        }
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

    /// <summary>默认备份目录（用户没指定 ⇒ <see cref="DefaultBackupDir"/>）。</summary>
    public string ArchiveDir
    {
        get
        {
            string configured;
            try { configured = _opts.Get(Section, KeyArchiveDir) ?? ""; }
            catch { configured = ""; }
            return string.IsNullOrWhiteSpace(configured) ? DefaultBackupDir : configured;
        }
        set => Write(KeyArchiveDir, string.IsNullOrWhiteSpace(value) ? null : value!.Trim());
    }

    /// <summary>
    /// 「默认备份目录」的出厂值：<c>%USERPROFILE%\NewToys\Backup</c>。
    /// <para>
    /// 刻意**不进配置档目录**：<c>…\profiles\&lt;配置&gt;\store\backup\archives</c> 太深，
    /// 用户想手动瞄一眼 / 拖个文件进去得点好几层；这里放在用户目录下一层。
    /// </para>
    /// </summary>
    public string DefaultBackupDir
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home)) home = _cfg.StoreDir;   // 极端情况：拿不到用户目录就退回数据目录
            return Path.Combine(home, "NewToys", "Backup");
        }
    }

    /// <summary>解包暂存根（每次还原用一个新的子目录，结束即删）。</summary>
    public string StagingRoot => Path.Combine(_cfg.CacheDir, "backup", "staging");

    /// <summary>本模块的数据目录（旧 JSON 的归档位，也是迁移来源）。</summary>
    public string DataDir => Path.Combine(_cfg.ConfigDir, "backup");

    /// <summary>旧任务清单（已迁 SQLite，仅迁移时读）。</summary>
    public string PlansFile => Path.Combine(DataDir, "plans.json");

    /// <summary>旧密码库（已迁 SQLite，仅迁移时读）。</summary>
    public string VaultFile => Path.Combine(DataDir, "vault.json");

    /// <summary>旧包清单（已迁 SQLite，仅迁移时读）。</summary>
    public string CatalogFile => Path.Combine(DataDir, "catalog.json");

    /// <summary>旧运行记录（已迁 SQLite，仅迁移时读）。</summary>
    public string RunsFile => Path.Combine(DataDir, "runs.json");

    /// <summary>设置文件（选项真正的落点，页面显示用）。</summary>
    public string SettingsFile => Path.Combine(_cfg.ModulesConfigDir, "backup-center.json");

    /// <summary>启动时的一次性迁移：把旧的模块级 7z 路径搬到应用级运行环境。</summary>
    private void MigrateLegacySevenZip()
    {
        string legacy;
        try { legacy = (_opts.Get(Section, LegacyKeySevenZip) ?? "").Trim(); }
        catch { return; }
        if (legacy.Length == 0) return;

        try
        {
            if (string.IsNullOrWhiteSpace(_app.SevenZipPath)) _app.SevenZipPath = legacy;
            _opts.Set(Section, LegacyKeySevenZip, null);   // 旧键清掉，避免两处值打架
        }
        catch (Exception ex) { LastError = ex.Message; }
    }

    /// <summary>
    /// 默认备份目录改成了用户目录下一层（<c>%USERPROFILE%\NewToys\Backup</c>）。
    /// 旧默认位（配置档内 <c>store\backup\archives</c>）里**已经有包**且用户没显式配过目录 ⇒ 沿用它，
    /// ⛔ 不让已有的包凭空"消失"（改了默认值不等于要把用户的包搬走）。
    /// </summary>
    private void MigrateLegacyArchiveDir()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_opts.Get(Section, KeyArchiveDir))) return;

            var legacy = Path.Combine(_cfg.StoreDir, "backup", "archives");
            if (!Directory.Exists(legacy)) return;
            if (!Directory.EnumerateFiles(legacy, "*.7z").Any()) return;

            _opts.Set(Section, KeyArchiveDir, legacy);
        }
        catch (Exception ex) { LastError = ex.Message; }
    }

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
