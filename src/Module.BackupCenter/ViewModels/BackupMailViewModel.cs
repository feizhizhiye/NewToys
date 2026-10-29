using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.BackupCenter.Models;
using NewToys.BackupCenter.Services;
using NewToys.BackupCenter.Services.Mail;
using NewToys.Core.Services;

namespace NewToys.BackupCenter.ViewModels;

/// <summary>
/// 「备份中心 → 备份邮件」页（<c>backup-mail</c>）：IMAP 收信，当**邮箱管理器**用（用户定调：收全部邮件），
/// 同时承担备份邮件的搜索与一键还原。
/// <para>
/// 列表先读本地缓存（<c>mail_index</c>）⇒ 没网也能翻历史；点「⟳ 同步」才连服务器拉新邮件。
/// </para>
/// <para>🔴 打开邮件 / 下载附件全程 <c>BODY.PEEK</c>，⛔ 不改动邮件的已读状态。</para>
/// </summary>
public partial class BackupMailViewModel : ObservableObject
{
    private readonly ImapMailService _imap;
    private readonly MailIndexStore _index;
    private readonly MailJobStore _jobs;
    private readonly VaultService _vault;
    private readonly ArchiveService _archives;
    private readonly SevenZipLocator _locator;
    private readonly BackupSettingsService _settings;

    private CancellationTokenSource? _cts;

    [ObservableProperty] private MailListItem? _selected;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyPhase = "";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _hintText = "";
    [ObservableProperty] private string _quotaText = "";
    [ObservableProperty] private string _cachedText = "";

    [ObservableProperty] private string _detailSubject = "";
    [ObservableProperty] private string _detailFrom = "";
    [ObservableProperty] private string _detailDate = "";
    [ObservableProperty] private string _detailBody = "";
    [ObservableProperty] private string _detailIndexNo = "";
    [ObservableProperty] private string _detailBackupInfo = "";
    [ObservableProperty] private MailAttachment? _selectedAttachment;

    public BackupMailViewModel(
        ImapMailService imap,
        MailIndexStore index,
        MailJobStore jobs,
        VaultService vault,
        ArchiveService archives,
        SevenZipLocator locator,
        BackupSettingsService settings)
    {
        _imap = imap;
        _index = index;
        _jobs = jobs;
        _vault = vault;
        _archives = archives;
        _locator = locator;
        _settings = settings;

        LoadCached();
        RefreshHint();
    }

    public ObservableCollection<MailListItem> Items { get; } = new();

    public ObservableCollection<MailAttachment> Attachments { get; } = new();

    /// <summary>还原时的冲突处理（默认跳过 = 最安全，⛔ 不默认覆盖用户的文件）。</summary>
    public IReadOnlyList<RestoreConflictPolicy> PolicyOptions { get; } = new[]
    {
        RestoreConflictPolicy.Skip, RestoreConflictPolicy.Overwrite, RestoreConflictPolicy.Rename
    };

    [ObservableProperty] private RestoreConflictPolicy _restorePolicy = RestoreConflictPolicy.Skip;

    public bool HasItems => Items.Count > 0;

    public bool HasSelection => Selected is not null;

    public bool IsBackupMail => DetailIndexNo.Length > 0;

    public bool HasAttachments => Attachments.Count > 0;

    // ==================== 载入 / 搜索 ====================

    /// <summary>先展示本地缓存（同步前也能看到上次拉到的邮件）。</summary>
    private void LoadCached()
    {
        var rows = _index.Query(SearchText ?? "");
        Items.Clear();
        foreach (var row in rows) Items.Add(row);
        NotifyList();
        CachedText = $"本地已缓存 {_index.Count()} 封";
    }

    private void NotifyList()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
    }

    partial void OnSelectedChanged(MailListItem? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        if (value is null) { ClearDetail(); return; }

        Attachments.Clear();
        DetailSubject = value.Subject;
        DetailFrom = value.From;
        DetailDate = value.DateText;
        DetailIndexNo = value.IndexNo;
        DetailBody = "（正在读取正文…）";
        DetailBackupInfo = value.IsBackup ? BuildBackupInfo(value.IndexNo) : "";
        OnPropertyChanged(nameof(IsBackupMail));
        OnPropertyChanged(nameof(HasAttachments));

        _ = LoadDetailAsync(value);
    }

    private async Task LoadDetailAsync(MailListItem item)
    {
        var gate = _imap.CanReceive();
        if (!gate.Ok)
        {
            DetailBody = "🔒 " + gate.Reason;
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var detail = await _imap.GetDetailAsync(item.Mailbox, item.Uid, cts.Token).ConfigureAwait(true);
            if (Selected?.Uid != item.Uid) return;   // 用户已经点开别的邮件了

            DetailSubject = detail.Subject;
            DetailFrom = detail.From;
            DetailDate = detail.Date.ToString("yyyy-MM-dd HH:mm");
            DetailBody = string.IsNullOrWhiteSpace(detail.BodyText) ? "（这封邮件没有纯文本正文）" : detail.BodyText;
            DetailIndexNo = detail.IndexNo;
            DetailBackupInfo = item.IsBackup ? BuildBackupInfo(item.IndexNo) : "";

            Attachments.Clear();
            foreach (var a in detail.Attachments) Attachments.Add(a);
            OnPropertyChanged(nameof(IsBackupMail));
            OnPropertyChanged(nameof(HasAttachments));
        }
        catch (Exception ex)
        {
            DetailBody = "⚠ 读取邮件失败：" + ex.Message;
        }
    }

    /// <summary>备份邮件在详情里多显示一行：索引号 → 任务 / 卷数 / 时间（来自本地 db）。</summary>
    private string BuildBackupInfo(string indexNo)
    {
        var job = _jobs.FindJob(indexNo);
        if (job is null) return $"{indexNo} · 本地没有这条投递记录（可能是别的机器发的）";
        return $"{indexNo} · {job.PlanName} · {job.PartsText} · {job.SizeText} · {job.CreatedText}";
    }

    private void ClearDetail()
    {
        DetailSubject = "";
        DetailFrom = "";
        DetailDate = "";
        DetailBody = "";
        DetailIndexNo = "";
        DetailBackupInfo = "";
        Attachments.Clear();
        OnPropertyChanged(nameof(IsBackupMail));
        OnPropertyChanged(nameof(HasAttachments));
    }

    [RelayCommand]
    private void Search() => LoadCached();

    /// <summary>同步收件箱新邮件（增量），同步完顺手刷新容量与本地列表。</summary>
    [RelayCommand]
    private async Task SyncAsync()
    {
        if (IsBusy) return;
        var gate = _imap.CanReceive();
        if (!gate.Ok) { StatusText = "⚠ " + gate.Reason; RefreshHint(); return; }

        IsBusy = true;
        _cts = new CancellationTokenSource();
        try
        {
            var firstUid = _index.MaxUid("INBOX") + 1;
            BusyPhase = firstUid <= 1 ? "首次同步（只拉最近一批）…" : "同步新邮件…";
            var progress = new Progress<string>(s => BusyPhase = s);

            var fetched = await _imap.SyncAsync(firstUid, progress, _cts.Token).ConfigureAwait(true);
            if (fetched.Count > 0) _index.Upsert(fetched);

            BusyPhase = "读取服务器概况…";
            QuotaText = await _imap.QuotaTextAsync(_cts.Token).ConfigureAwait(true);

            LoadCached();
            StatusText = fetched.Count > 0 ? $"✅ 同步到 {fetched.Count} 封新邮件。" : "没有新邮件。";
        }
        catch (OperationCanceledException) { StatusText = "已取消同步。"; }
        catch (Exception ex) { StatusText = "⚠ 同步失败：" + ex.Message; }
        finally
        {
            IsBusy = false;
            BusyPhase = "";
            RefreshHint();
        }
    }

    /// <summary>搜索服务器：先把新邮件拉回来，再按关键词过滤（本地 SQL 不认服务器端的全文索引）。</summary>
    [RelayCommand]
    private async Task SearchServerAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText)) { StatusText = "先输入要搜的关键词（索引号 / 主题 / 发件人）。"; return; }
        await SyncAsync().ConfigureAwait(true);
        LoadCached();
        StatusText = Items.Count == 0
            ? $"服务器上也找不到「{SearchText}」。"
            : $"找到 {Items.Count} 封匹配「{SearchText}」。";
    }

    // ==================== 动作 ====================

    [RelayCommand]
    private async Task SaveAttachmentAsync()
    {
        var mail = Selected;
        var attachment = SelectedAttachment;
        if (mail is null) { StatusText = "先选中一封邮件。"; return; }
        if (attachment is null) { StatusText = "先选中一个附件。"; return; }

        var target = await DialogHooks.PickFolderAsync("保存附件到…").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(target)) return;

        IsBusy = true;
        BusyPhase = "下载附件…";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var (files, error) = await _imap.DownloadAttachmentAsync(mail.Mailbox, mail.Uid, attachment.FileName, target, cts.Token)
                .ConfigureAwait(true);
            if (files.Count == 0) { StatusText = "⚠ " + (string.IsNullOrEmpty(error) ? "没下到附件。" : error); return; }
            StatusText = "✅ 已保存：" + files[0];
        }
        catch (Exception ex) { StatusText = "⚠ 下载失败：" + ex.Message; }
        finally { IsBusy = false; BusyPhase = ""; }
    }

    /// <summary>备份邮件的核心动作：按索引号把全部分卷拉下来 → 校验 → 解压还原。</summary>
    [RelayCommand]
    private async Task RestoreAsync()
    {
        var indexNo = DetailIndexNo;
        if (indexNo.Length == 0) { StatusText = "选中的不是 NewToys 备份邮件（主题里没有索引号）。"; return; }

        if (_vault is null || !_vault.IsUnlocked)
        {
            StatusText = "⚠ 密码库没解锁：到「备份中心设置 → 主密码」解锁后再还原（要用包密码解压）。";
            return;
        }

        var job = _jobs.FindJob(indexNo);
        var password = job != null ? _vault.Get(job.PlanId) : null;
        if (string.IsNullOrEmpty(password))
        {
            StatusText = "⚠ 密码库里找不到这封邮件对应的包密码（可能是别的机器 / 别的配置档发的）。";
            return;
        }

        var targetDir = await DialogHooks.PickFolderAsync("还原到哪个目录？").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(targetDir)) return;

        IsBusy = true;
        _cts = new CancellationTokenSource();
        var staging = Path.Combine(_settings.StagingRoot, "mail-" + indexNo);
        try
        {
            Directory.CreateDirectory(staging);
            foreach (var stale in Directory.EnumerateFiles(staging)) File.Delete(stale);

            BusyPhase = "下载分卷…";
            var progress = new Progress<string>(s => BusyPhase = s);
            var (files, error) = await _imap.DownloadIndexAsync(indexNo, staging, progress, _cts.Token).ConfigureAwait(true);

            if (files.Count == 0) { StatusText = "⚠ " + (string.IsNullOrEmpty(error) ? "没下到分卷。" : error); return; }

            BusyPhase = "校验分卷…";
            var expected = _jobs.ListParts(indexNo);
            if (expected.Count > 0)
            {
                var missing = expected.FindAll(p => !files.Any(f =>
                    string.Equals(Path.GetFileName(f), p.FileName, StringComparison.OrdinalIgnoreCase)));
                if (missing.Count > 0)
                {
                    StatusText = $"⚠ 缺 {missing.Count} 卷（{string.Join("、", missing.Select(m => m.PartText))}），无法还原。";
                    return;
                }

                foreach (var part in expected)
                {
                    var local = files.First(f => string.Equals(Path.GetFileName(f), part.FileName, StringComparison.OrdinalIgnoreCase));
                    var sha = await Task.Run(() => ArchiveCatalog.ComputeSha256(local), _cts.Token).ConfigureAwait(true);
                    if (!string.Equals(sha, part.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        StatusText = $"⚠ {part.FileName} 校验值不对（下载损坏 / 被改过），已中止。";
                        return;
                    }
                }
            }

            var zip = await _locator.LocateAsync(force: true, _cts.Token).ConfigureAwait(true);
            if (!zip.Found) { StatusText = "⚠ " + zip.Message; return; }

            BusyPhase = "解压还原…";
            var first = files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).First();
            var restored = await _archives.RestoreAsync(zip.Path, first, password, targetDir, RestorePolicy,
                progress, _cts.Token).ConfigureAwait(true);

            StatusText = restored.Ok
                ? $"✅ 已还原 {indexNo}：{restored.Restored} 个文件（跳过 {restored.Skipped}）→ {targetDir}"
                : (restored.Canceled ? "已取消还原。" : "⚠ 还原失败：" + restored.Message);
        }
        catch (OperationCanceledException) { StatusText = "已取消还原。"; }
        catch (Exception ex) { StatusText = "⚠ 还原失败：" + ex.Message; }
        finally
        {
            IsBusy = false;
            BusyPhase = "";
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { /* 暂存删不掉不影响还原结果 */ }
        }
    }

    [RelayCommand]
    private async Task DeleteMailAsync()
    {
        var mail = Selected;
        if (mail is null) { StatusText = "先选中一封邮件。"; return; }

        var ok = await DialogHooks.ConfirmAsync(
            $"删除服务器上的这封邮件？\n\n主题：{mail.Subject}\n\n🔴 删了就不能再从邮箱还原它；本地已下载的分卷不受影响。").ConfigureAwait(true);
        if (!ok) return;

        IsBusy = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var (success, error) = await _imap.DeleteAsync(mail.Mailbox, mail.Uid, cts.Token).ConfigureAwait(true);
            if (!success) { StatusText = "⚠ 删除失败：" + error; return; }

            _index.Remove(mail.Mailbox, mail.Uid);
            Selected = null;
            LoadCached();
            StatusText = "已删除服务器上的那封邮件。";
        }
        catch (Exception ex) { StatusText = "⚠ 删除失败：" + ex.Message; }
        finally { IsBusy = false; }
    }

    private void RefreshHint()
    {
        var gate = _imap.CanReceive();
        HintText = gate.Ok
            ? "选一封邮件看详情；带「备份」标记的可以直接「📥 下载并还原」。（读信/下载都不会把邮件标成已读）"
            : "🔒 " + gate.Reason;
    }
}
