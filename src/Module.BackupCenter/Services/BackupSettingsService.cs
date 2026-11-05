using System;
using System.IO;
using System.Linq;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.BackupCenter.Services;

/// <summary>备份中心的设置与路径（本模块配置的唯一写入点）。（WHY: BC-005）</summary>
public sealed class BackupSettingsService
{
    /// <summary><see cref="IOptionsStore"/> 的分区名。</summary>
    public const string Section = "backup-center";

    /// <summary>已废弃的键（7z 路径已上提到应用级运行环境），仅用于一次性迁移。</summary>
    private const string LegacyKeySevenZip = "sevenZipPath";

    private const string KeyKeepCount = "defaultKeepCount";
    private const string KeyArchiveDir = "archiveDir";
    private const string KeyMail = "mail";

    /// <summary>密码库里存邮箱授权码的条目 Id（SMTP / IMAP 共用一个授权码）。</summary>
    public const string MailAuthVaultId = "mail.auth";

    /// <summary>投递成功后本地分卷的缓冲天数（到期才删，留出重发 / 复查的窗口）。</summary>
    public const int MailBufferDays = 3;

    private static readonly System.Text.Json.JsonSerializerOptions MailJson = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

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

    /// <summary>「默认备份目录」的出厂值：%USERPROFILE%\NewToys\Backup。</summary>
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

    /// <summary>
    /// 邮件通道的待发目录（分卷落这里，发送成功后保留 <see cref="MailBufferDays"/> 天）。
    /// <para>刻意**与本地备份目录分开**：本地包受「保留份数」轮转，待发箱受「发完即清」管，两套规则混在一起会互相删错。</para>
    /// </summary>
    public string OutboxDir
    {
        get
        {
            var configured = MailSettings.OutboxDir;
            return string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(DefaultBackupDir, "outbox")
                : configured.Trim();
        }
    }

    /// <summary>
    /// 邮件通道配置（<c>config\backup-center.json</c> 的 <c>mail</c> 键，值是一段 JSON）。
    /// <para>🔴 授权码⛔ 不在这里 —— 它在密码库（<see cref="MailAuthVaultId"/>），密文。</para>
    /// </summary>
    public Models.MailSettingsDto MailSettings
    {
        get
        {
            string raw;
            try { raw = _opts.Get(Section, KeyMail) ?? ""; }
            catch { return new Models.MailSettingsDto(); }
            if (string.IsNullOrWhiteSpace(raw)) return new Models.MailSettingsDto();

            try { return System.Text.Json.JsonSerializer.Deserialize<Models.MailSettingsDto>(raw, MailJson) ?? new(); }
            catch { return new Models.MailSettingsDto(); }
        }
    }

    /// <summary>整体替换邮件配置（页面「保存」= 一次写全，⛔ 不逐字段写）。</summary>
    public void SaveMailSettings(Models.MailSettingsDto value)
    {
        try
        {
            Write(KeyMail, System.Text.Json.JsonSerializer.Serialize(value ?? new Models.MailSettingsDto(), MailJson));
        }
        catch (Exception ex) { LastError = ex.Message; }
    }

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
