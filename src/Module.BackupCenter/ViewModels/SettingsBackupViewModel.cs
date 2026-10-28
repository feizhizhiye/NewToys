using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.BackupCenter.Services;
using NewToys.Core.Services;

namespace NewToys.BackupCenter.ViewModels;

/// <summary>
/// 「备份中心设置」页（<c>settings-backup</c>）：归档引擎（7z）、主密码密码库、保留份数与目录。
/// <para>
/// 主密码⛔ 不落盘：这里只管设置 / 解锁 / 改密码 / 锁库，密码库本体由 <see cref="VaultService"/> 封装。
/// </para>
/// </summary>
public partial class SettingsBackupViewModel : ObservableObject
{
    private readonly BackupSettingsService _settings;
    private readonly VaultService _vault;
    private readonly SevenZipLocator _locator;

    [ObservableProperty] private string _sevenZipPathText = "";
    [ObservableProperty] private string _sevenZipStatusText = "还没探测";
    [ObservableProperty] private string _vaultStateText = "";
    [ObservableProperty] private string _masterInput = "";
    [ObservableProperty] private string _masterConfirmInput = "";
    [ObservableProperty] private string _oldMasterInput = "";
    [ObservableProperty] private string _newMasterInput = "";
    [ObservableProperty] private string _newMasterConfirmInput = "";
    [ObservableProperty] private string _keepText = "5";
    [ObservableProperty] private string _archiveDirText = "";
    [ObservableProperty] private string _statusText = "就绪";

    public SettingsBackupViewModel(
        BackupSettingsService settings,
        VaultService vault,
        SevenZipLocator locator)
    {
        _settings = settings;
        _vault = vault;
        _locator = locator;

        SevenZipPathText = settings.ConfiguredSevenZip;
        KeepText = settings.DefaultKeepCount.ToString();
        ArchiveDirText = settings.ArchiveDir;
        RefreshVaultState();
    }

    // ==================== 显示路径 ====================

    public string SettingsFile => _settings.SettingsFile;

    public string DataDir => _settings.DataDir;

    public string DefaultArchiveDir => _settings.DefaultArchiveDir;

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

    // ==================== 归档引擎 ====================

    [RelayCommand]
    private async Task DetectSevenZipAsync()
    {
        StatusText = "正在探测 7z…";
        try
        {
            var status = await _locator.LocateAsync(force: true).ConfigureAwait(true);
            SevenZipStatusText = status.Found
                ? $"✅ {status.Version} · {status.Path}"
                : "⚠ " + status.Message;
            if (status.Found) SevenZipPathText = status.Path;
            StatusText = status.Found ? "探测到 7z：" + status.Version : "⚠ " + status.Message;
        }
        catch (Exception ex) { StatusText = "⚠ 探测失败：" + ex.Message; }
    }

    [RelayCommand]
    private async Task BrowseSevenZipAsync()
    {
        var files = await DialogHooks.PickFilesAsync("选择 7z.exe", null, false, "7z.exe").ConfigureAwait(true);
        if (files.Count > 0) SevenZipPathText = files[0];
    }

    [RelayCommand]
    private void SaveSevenZip()
    {
        _settings.ConfiguredSevenZip = SevenZipPathText;
        if (_settings.LastError != null) { StatusText = "⚠ 保存失败：" + _settings.LastError; return; }
        _locator.Reset();
        StatusText = string.IsNullOrWhiteSpace(SevenZipPathText)
            ? "已清空：以后走自动探测（常见安装位 → PATH）。"
            : "已保存 7z 路径（点「🔍 探测」验证一下版本）。";
    }

    [RelayCommand]
    private void ClearSevenZip()
    {
        SevenZipPathText = "";
        SaveSevenZip();
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
        StatusText = "✅ 主密码已设置，密码库已解锁。🔴 主密码丢了，包密码就再也取不回来。";
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
        StatusText = "✅ 密码库已解锁（本次会话有效）。";
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
        StatusText = "✅ 主密码已更换，库里的条目已用新密码重新加密。";
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
        var picked = await DialogHooks.PickFolderAsync("选择归档包目录", ArchiveDirText).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(picked)) ArchiveDirText = picked;
    }

    [RelayCommand]
    private void SaveArchiveDir()
    {
        _settings.ArchiveDir = ArchiveDirText;
        if (_settings.LastError != null) { StatusText = "⚠ 保存失败：" + _settings.LastError; return; }
        ArchiveDirText = _settings.ArchiveDir;
        StatusText = "已保存包目录：" + ArchiveDirText;
    }

    [RelayCommand]
    private void ResetArchiveDir()
    {
        ArchiveDirText = DefaultArchiveDir;
        SaveArchiveDir();
    }

    [RelayCommand]
    private void OpenDataDir()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + DataDir + "\"") { UseShellExecute = true });
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
    }
}
