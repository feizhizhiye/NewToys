using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Services;
using NewToys.WebTools.Models;
using NewToys.WebTools.Services;

namespace NewToys.WebTools.ViewModels;

/// <summary>
/// 「系统 Hosts」页（<c>web-hosts-system</c>）：展示系统 hosts 现状 + 受管块区间 + 手动编辑 +
/// 备份 / 还原 / 删除 + 清除受管块。
/// <para>
/// 🔴 本页所有写动作（保存 / 还原 / 清除）都**先备份再写**（备份逻辑在 <see cref="HostsService"/>），
/// 且保存 / 还原前必须二次确认 —— 这是需求 §3「改系统级配置必须二次确认」的落点。
/// </para>
/// </summary>
public partial class SystemHostsViewModel : ObservableObject, IStatusProvider
{
    private readonly HostsService _svc = null!;
    private string _rawText = "";

    public SystemHostsViewModel() { }

    public SystemHostsViewModel(HostsService svc)
    {
        _svc = svc;
        Reload();
    }

    // ==================== 只读展示 ====================

    [ObservableProperty] private string _hostsPath = "";
    [ObservableProperty] private string _fileInfoText = "";
    [ObservableProperty] private string _managedInfoText = "";
    [ObservableProperty] private string _previewText = "";
    [ObservableProperty] private string _editText = "";

    /// <summary>本次编辑态（切到可编辑文本框）。</summary>
    [ObservableProperty] private bool _isEditing;

    /// <summary>当前用户是否有权写系统 hosts（无权限时保存入口置灰）。</summary>
    [ObservableProperty] private bool _canWrite;

    /// <summary>只看受管块（只读预览的过滤视图）。</summary>
    [ObservableProperty] private bool _showManagedOnly;

    [ObservableProperty] private string _permissionText = "";

    public ObservableCollection<HostsBackupInfo> Backups { get; } = new();

    [ObservableProperty] private HostsBackupInfo? _selectedBackup;

    // ==================== 加载 ====================

    [RelayCommand]
    private void Reload()
    {
        HostsPath = _svc.SystemHostsPath;

        var (ok, text, msg) = _svc.ReadSystemHosts();
        var (canWrite, why) = _svc.CanWrite();
        CanWrite = canWrite;
        PermissionText = canWrite ? "🔓 可写（有权限）" : "🔒 只读：" + why;

        if (!ok)
        {
            _rawText = "";
            PreviewText = "";
            FileInfoText = msg;
            ManagedInfoText = "—";
            StatusText = "⚠ " + msg;
            RefreshBackups();
            return;
        }

        _rawText = text;
        var lineCount = text.Replace("\r\n", "\n").Split('\n').Length;
        var (found, startLine, endLine, count) = HostsService.ManagedRange(text);
        ManagedInfoText = found
            ? $"受管块：第 {startLine}~{endLine} 行 · {count} 条"
            : "受管块：没有（应用方案后会追加一段）";

        FileInfoText = BuildFileInfo(text);
        ApplyPreview();
        RefreshBackups();
        StatusText = $"已读入系统 hosts（{lineCount} 行）";
    }

    private string BuildFileInfo(string text)
    {
        try
        {
            var info = new FileInfo(HostsPath);
            var size = info.Exists ? info.Length : 0;
            var modified = info.Exists ? info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") : "—";
            var lines = text.Replace("\r\n", "\n").Split('\n').Length;
            return $"{size} 字节 · {lines} 行 · 修改于 {modified}";
        }
        catch (Exception ex)
        {
            return "文件信息读取失败：" + ex.Message;
        }
    }

    partial void OnShowManagedOnlyChanged(bool value) => ApplyPreview();

    /// <summary>只读预览：全文，或只留受管块那几行。</summary>
    private void ApplyPreview()
    {
        if (!ShowManagedOnly || _rawText.Length == 0)
        {
            PreviewText = _rawText;
            return;
        }

        var (found, startLine, endLine, _) = HostsService.ManagedRange(_rawText);
        if (!found)
        {
            PreviewText = "（系统 hosts 里没有本程序的受管块）";
            return;
        }

        var lines = _rawText.Replace("\r\n", "\n").Split('\n');
        PreviewText = string.Join("\n", lines.Skip(startLine - 1).Take(endLine - startLine + 1));
    }

    // ==================== 编辑 ====================

    [RelayCommand]
    private void StartEdit()
    {
        if (!CanWrite) { StatusText = "⚠ 没有写权限：" + PermissionText + " —— 请以管理员身份运行本程序。"; return; }
        EditText = _rawText;
        IsEditing = true;
        StatusText = "编辑中：改完点「💾 保存」（保存前会二次确认并自动备份）。";
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        EditText = "";
        StatusText = "已取消编辑（未改动系统文件）。";
    }

    [RelayCommand]
    private async Task SaveEditAsync()
    {
        if (!IsEditing) return;

        var ok = await DialogHooks.ConfirmAsync(
            "要直接改写系统 hosts 全文吗？\n" +
            "这会覆盖整份文件（包括本程序的受管块）。保存前会自动备份，可在下方备份列表里还原。");
        if (!ok) { StatusText = "已取消保存。"; return; }

        var (written, msg) = _svc.WriteRaw(EditText);
        StatusText = written ? msg : "⚠ " + msg;
        if (!written) return;

        IsEditing = false;
        Reload();
    }

    // ==================== 备份 ====================

    private void RefreshBackups()
    {
        Backups.Clear();
        foreach (var b in _svc.ListBackups()) Backups.Add(b);
        OnPropertyChanged(nameof(HasBackups));
        SelectedBackup = Backups.FirstOrDefault();
    }

    public bool HasBackups => Backups.Count > 0;

    [RelayCommand]
    private void NewBackup()
    {
        var (ok, msg, _) = _svc.BackupNow("手动备份");
        StatusText = ok ? msg : "⚠ " + msg;
        RefreshBackups();
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (SelectedBackup is not { } backup) { StatusText = "先在下方备份列表里选一份。"; return; }

        var ok = await DialogHooks.ConfirmAsync(
            $"要把系统 hosts 还原成 {backup.TimeText} 那份备份吗？\n" +
            "还原前的当前文件会另外备份一份，仍然可以再换回来。");
        if (!ok) { StatusText = "已取消还原。"; return; }

        var (done, msg) = _svc.Restore(backup.Path);
        StatusText = done ? msg : "⚠ " + msg;
        Reload();
    }

    [RelayCommand]
    private async Task DeleteBackupAsync()
    {
        if (SelectedBackup is not { } backup) { StatusText = "先在下方备份列表里选一份。"; return; }

        var ok = await DialogHooks.ConfirmAsync($"删除备份 {backup.Name}？\n（只删备份文件，不动系统 hosts）");
        if (!ok) return;

        var (done, msg) = _svc.DeleteBackup(backup.Path);
        StatusText = done ? msg : "⚠ " + msg;
        RefreshBackups();
    }

    [RelayCommand]
    private void OpenBackupFolder()
    {
        try
        {
            Directory.CreateDirectory(_svc.BackupDir);
            Process.Start(new ProcessStartInfo(_svc.BackupDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = "⚠ 打不开备份目录：" + ex.Message;
        }
    }

    // ==================== 清除受管块 ====================

    [RelayCommand]
    private async Task ClearManagedAsync()
    {
        var ok = await DialogHooks.ConfirmAsync(
            "清除系统 hosts 里本程序的受管块？\n" +
            "只删 `# === NewToys hosts start/end ===` 之间的内容，你自己写的其它行原样保留；清除前会自动备份。");
        if (!ok) { StatusText = "已取消。"; return; }

        var (done, msg) = _svc.ClearManagedBlock();
        StatusText = done ? msg : "⚠ " + msg;
        Reload();
    }

    // ==================== 状态栏 ====================

    [ObservableProperty] private string _statusText = "";

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    public event EventHandler? StatusChanged;
}
