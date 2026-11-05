using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.BackupCenter.Models;
using NewToys.BackupCenter.Services;
using NewToys.BackupCenter.Services.Mail;
using NewToys.Core.Services;

namespace NewToys.BackupCenter.ViewModels;

/// <summary>「备份中心设置」页（settings-backup）：主密码密码库、保留份数与目录、邮件投递、文件位置。（WHY: BC-019）</summary>
public partial class SettingsBackupViewModel : ObservableObject
{
    private readonly BackupSettingsService _settings;
    private readonly VaultService _vault;
    private readonly BackupDb _db;
    private readonly BackupMailService _mailer;
    private readonly ImapMailService _imap;
    private readonly OutboxService _outbox;

    [ObservableProperty] private string _vaultStateText = "";
    [ObservableProperty] private string _masterInput = "";
    [ObservableProperty] private string _masterConfirmInput = "";
    [ObservableProperty] private string _oldMasterInput = "";
    [ObservableProperty] private string _newMasterInput = "";
    [ObservableProperty] private string _newMasterConfirmInput = "";
    [ObservableProperty] private string _keepText = "5";
    [ObservableProperty] private string _archiveDirText = "";
    [ObservableProperty] private string _smtpHostText = "";
    [ObservableProperty] private string _smtpPortText = "465";
    [ObservableProperty] private string _smtpUserText = "";
    [ObservableProperty] private string _smtpToText = "";
    [ObservableProperty] private string _imapHostText = "";
    [ObservableProperty] private string _imapPortText = "993";
    [ObservableProperty] private string _imapUserText = "";
    [ObservableProperty] private string _authCodeInput = "";
    [ObservableProperty] private string _authStateText = "";
    [ObservableProperty] private int _volumeMb = 30;
    [ObservableProperty] private string _outboxDirText = "";
    [ObservableProperty] private string _mailStatusText = "";
    [ObservableProperty] private string _statusText = "就绪";

    /// <summary>分卷大小候选（MB）。🔴 上限 30：再大 base64 编码后会超 QQ 的 50 M 单封限制。</summary>
    public IReadOnlyList<int> VolumeOptions { get; } = new[] { 10, 20, 30 };

    public SettingsBackupViewModel(
        BackupSettingsService settings,
        VaultService vault,
        BackupDb db,
        BackupMailService mailer,
        ImapMailService imap,
        OutboxService outbox)
    {
        _settings = settings;
        _vault = vault;
        _db = db;
        _mailer = mailer;
        _imap = imap;
        _outbox = outbox;

        KeepText = settings.DefaultKeepCount.ToString();
        ArchiveDirText = settings.ArchiveDir;
        LoadMailSettings();
        RefreshVaultState();
    }

    // ==================== 显示路径 ====================

    public string SettingsFile => _settings.SettingsFile;

    /// <summary>任务 / 包清单 / 运行记录 / 待办 / 密码库都在这个 SQLite 库里。</summary>
    public string DatabaseFile => _db.DatabasePath;

    public string DataDir => _settings.DataDir;

    public string DefaultBackupDir => _settings.DefaultBackupDir;

    public string StagingRoot => _settings.StagingRoot;

    public string VaultFile => _settings.VaultFile;

    // ==================== 主密码状态 ====================

    public bool VaultInitialized => _vault.IsInitialized;

    public bool VaultUnlocked => _vault.IsUnlocked;

    public bool VaultLockedOut => _vault.IsLockedOut;

    /// <summary>首次设置主密码（还没建库）。</summary>
    public bool ShowSetupVault => !VaultInitialized;

    /// <summary>已建库但本会话没解锁。</summary>
    public bool ShowUnlockVault => VaultInitialized && !VaultUnlocked;

    /// <summary>已解锁 ⇒ 可以改主密码 / 锁库。</summary>
    public bool ShowUnlockedActions => VaultUnlocked;

    public string RemainingAttemptsText => VaultLockedOut
        ? "⚠ 失败次数已用尽，本会话不再接受解锁（重启程序再试）"
        : $"还剩 {_vault.RemainingAttempts} 次尝试机会";

    // ==================== 邮件投递 ====================

    /// <summary>授权码是否已存进密码库（只显示状态，⛔ 不回显密文）。</summary>
    public bool HasAuthCode => _vault.IsUnlocked && _vault.HasEntry(BackupSettingsService.MailAuthVaultId);

    public string AuthCodePlaceholder => HasAuthCode ? "已保存（重输会覆盖）" : "授权码（不是登录密码）";

    /// <summary>把库里的配置摊到界面字段上（每次进页面 / 保存后都走一遍，保证显示与文件一致）。</summary>
    private void LoadMailSettings()
    {
        var mail = _settings.MailSettings;
        SmtpHostText = mail.SmtpHost;
        SmtpPortText = mail.SmtpPort.ToString();
        SmtpUserText = mail.SmtpUser;
        SmtpToText = mail.SmtpTo;
        ImapHostText = mail.ImapHost;
        ImapPortText = mail.ImapPort.ToString();
        ImapUserText = mail.ImapUser;
        VolumeMb = mail.VolumeMb is 10 or 20 or 30 ? mail.VolumeMb : 30;
        OutboxDirText = _settings.OutboxDir;
        AuthCodeInput = "";
        RefreshAuthState();
    }

    private void RefreshAuthState()
    {
        OnPropertyChanged(nameof(HasAuthCode));
        OnPropertyChanged(nameof(AuthCodePlaceholder));
        AuthStateText = !_vault.IsUnlocked
            ? "🔒 密码库没解锁 —— 解锁后才能存取授权码"
            : HasAuthCode ? "✅ 授权码已保存在密码库（密文）" : "⚠ 还没存授权码";
    }

    /// <summary>保存整份邮件配置（主机 / 端口 / 账号 / 分卷 / 待发目录）；授权码单独走密码库。</summary>
    [RelayCommand]
    private void SaveMail()
    {
        if (!int.TryParse(SmtpPortText, out var smtpPort) || smtpPort < 1 || smtpPort > 65535)
        {
            MailStatusText = "⚠ 发信端口要填 1~65535。";
            return;
        }
        if (!int.TryParse(ImapPortText, out var imapPort) || imapPort < 1 || imapPort > 65535)
        {
            MailStatusText = "⚠ 收信端口要填 1~65535。";
            return;
        }

        var mail = new MailSettingsDto
        {
            SmtpHost = (SmtpHostText ?? "").Trim(),
            SmtpPort = smtpPort,
            SmtpUser = (SmtpUserText ?? "").Trim(),
            SmtpTo = (SmtpToText ?? "").Trim(),
            ImapHost = (ImapHostText ?? "").Trim(),
            ImapPort = imapPort,
            ImapUser = (ImapUserText ?? "").Trim(),
            VolumeMb = VolumeMb is 10 or 20 or 30 ? VolumeMb : 30,
            OutboxDir = (OutboxDirText ?? "").Trim()
        };

        _settings.SaveMailSettings(mail);
        if (_settings.LastError != null) { MailStatusText = "⚠ 保存失败：" + _settings.LastError; return; }

        // 授权码：只有用户这次输了新值才写（空 = 保留已存值）
        var auth = (AuthCodeInput ?? "").Trim();
        if (auth.Length > 0)
        {
            if (!_vault.IsUnlocked) { MailStatusText = "⚠ 配置已保存，但密码库没解锁 ⇒ 授权码没存进去。"; return; }
            if (!_vault.Set(BackupSettingsService.MailAuthVaultId, auth))
            {
                MailStatusText = "⚠ 授权码存进密码库失败。";
                return;
            }
            AuthCodeInput = "";
        }

        LoadMailSettings();
        MailStatusText = "已保存邮件配置。" + (mail.IsConfigured ? "点「📡 测试连接」验证一下。" : "");
    }

    [RelayCommand]
    private void ClearAuth()
    {
        if (!_vault.IsUnlocked) { MailStatusText = "⚠ 密码库没解锁。"; return; }
        _vault.Remove(BackupSettingsService.MailAuthVaultId);
        AuthCodeInput = "";
        RefreshAuthState();
        MailStatusText = "已清除密码库里的邮箱授权码。";
    }

    [RelayCommand]
    private async Task TestMailAsync()
    {
        MailStatusText = "正在测试连接…";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            var smtp = await _mailer.TestAsync(cts.Token).ConfigureAwait(true);
            var imap = await _imap.TestAsync(cts.Token).ConfigureAwait(true);
            MailStatusText = smtp.Message + " ｜ " + imap.Message;
        }
        catch (Exception ex) { MailStatusText = "⚠ 测试失败：" + ex.Message; }
    }

    [RelayCommand]
    private async Task BrowseOutboxDirAsync()
    {
        var picked = await DialogHooks.PickFolderAsync("选择邮件待发目录", OutboxDirText).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(picked)) { OutboxDirText = picked; SaveMail(); }
    }

    [RelayCommand]
    private void OpenOutboxDir()
    {
        try
        {
            var dir = string.IsNullOrWhiteSpace(OutboxDirText) ? _settings.OutboxDir : OutboxDirText.Trim();
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
        }
        catch (Exception ex) { MailStatusText = "⚠ 打开目录失败：" + ex.Message; }
    }

    [RelayCommand]
    private void PurgeOutbox()
    {
        var result = _outbox.PurgeAllSent();
        MailStatusText = result.Message;
    }

    // ==================== 主密码 ====================

    [RelayCommand]
    private void SetupVault()
    {
        var master = MasterInput ?? "";
        if (master.Length < 8) { StatusText = "⚠ 主密码至少 8 位。"; return; }
        if (master != (MasterConfirmInput ?? "")) { StatusText = "⚠ 两次输入不一致。"; return; }

        if (!_vault.Initialize(master)) { StatusText = "⚠ 设置失败（密码库可能已存在）。"; return; }

        MasterInput = "";
        MasterConfirmInput = "";
        RefreshVaultState();
        StatusText = "主密码已设置，密码库已解锁。🔴 主密码丢了，包密码就再也取不回来。";
    }

    [RelayCommand]
    private void UnlockVault()
    {
        if (!_vault.Unlock(MasterInput ?? ""))
        {
            MasterInput = "";
            RefreshVaultState();
            StatusText = VaultLockedOut ? "⚠ 失败次数用尽，本会话已锁库。" : "⚠ 主密码不对。";
            return;
        }
        MasterInput = "";
        RefreshVaultState();
        StatusText = "密码库已解锁（本次会话有效）。";
    }

    [RelayCommand]
    private void LockVault()
    {
        _vault.Lock();
        RefreshVaultState();
        StatusText = "已锁库：取密码 / 备份 / 还原都要重新解锁。";
    }

    [RelayCommand]
    private void ChangeMaster()
    {
        var newMaster = NewMasterInput ?? "";
        if (newMaster.Length < 8) { StatusText = "⚠ 新主密码至少 8 位。"; return; }
        if (newMaster != (NewMasterConfirmInput ?? "")) { StatusText = "⚠ 两次输入不一致。"; return; }

        if (!_vault.ChangeMaster(OldMasterInput ?? "", newMaster))
        {
            RefreshVaultState();
            StatusText = "⚠ 改密码失败：旧主密码不对（或密码库未解锁）。";
            return;
        }

        OldMasterInput = "";
        NewMasterInput = "";
        NewMasterConfirmInput = "";
        RefreshVaultState();
        StatusText = "主密码已更换，库里的条目已用新密码重新加密。";
    }

    // ==================== 保留份数 / 目录 ====================

    [RelayCommand]
    private void SaveKeep()
    {
        if (!int.TryParse(KeepText, out var keep) || keep < 1 || keep > 999)
        {
            StatusText = "⚠ 保留份数要填 1~999。";
            return;
        }
        _settings.DefaultKeepCount = keep;
        StatusText = "已保存默认保留份数：" + keep + "（只影响新建任务）。";
    }

    [RelayCommand]
    private async Task BrowseArchiveDirAsync()
    {
        var picked = await DialogHooks.PickFolderAsync("选择默认备份目录", ArchiveDirText).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(picked)) ArchiveDirText = picked;
    }

    [RelayCommand]
    private void SaveArchiveDir()
    {
        _settings.ArchiveDir = ArchiveDirText;
        if (_settings.LastError != null) { StatusText = "⚠ 保存失败：" + _settings.LastError; return; }
        ArchiveDirText = _settings.ArchiveDir;
        StatusText = "已保存默认备份目录：" + ArchiveDirText + "（已有的包不会自己搬过去）";
    }

    [RelayCommand]
    private void ResetArchiveDir()
    {
        ArchiveDirText = DefaultBackupDir;
        SaveArchiveDir();
    }

    [RelayCommand]
    private void OpenBackupDir()
    {
        try
        {
            var dir = string.IsNullOrWhiteSpace(ArchiveDirText) ? _settings.ArchiveDir : ArchiveDirText.Trim();
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
            StatusText = "已打开：" + dir;
        }
        catch (Exception ex) { StatusText = "⚠ 打开目录失败：" + ex.Message; }
    }

    [RelayCommand]
    private void OpenDataDir()
    {
        try
        {
            var dir = Path.GetDirectoryName(DatabaseFile) ?? DataDir;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
        }
        catch (Exception ex) { StatusText = "⚠ 打开目录失败：" + ex.Message; }
    }

    private void RefreshVaultState()
    {
        VaultStateText = !VaultInitialized ? "未设置主密码"
            : VaultUnlocked ? "已解锁"
            : VaultLockedOut ? "已锁定（本会话）"
            : "已锁定";

        OnPropertyChanged(nameof(VaultInitialized));
        OnPropertyChanged(nameof(VaultUnlocked));
        OnPropertyChanged(nameof(VaultLockedOut));
        OnPropertyChanged(nameof(ShowSetupVault));
        OnPropertyChanged(nameof(ShowUnlockVault));
        OnPropertyChanged(nameof(ShowUnlockedActions));
        OnPropertyChanged(nameof(RemainingAttemptsText));
        RefreshAuthState();
    }
}
