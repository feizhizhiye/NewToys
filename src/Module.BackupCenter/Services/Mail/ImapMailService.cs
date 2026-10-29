using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using NewToys.BackupCenter.Models;

namespace NewToys.BackupCenter.Services.Mail;

/// <summary>一封邮件的正文与附件清单（详情面板用）。</summary>
public sealed record MailDetail(string Subject, string From, DateTime Date, string BodyText, List<MailAttachment> Attachments, string IndexNo);

/// <summary>
/// IMAP 收信：邮箱管理器（全量邮件列表）+ 备份邮件下载。
/// <para>
/// 🔴 取信一律走 <c>GetMessageAsync(uid, ct)</c>（MailKit 内部用 <c>BODY.PEEK[]</c>，⛔ 不会把邮件标已读），
/// ⛔ 不用会把邮件标已读的重载 —— 用户不希望打开页面就改掉邮箱状态。
/// </para>
/// <para>连接按「一次操作一条」用完即断：IMAP 空闲连接会被服务器掐，长连接反而要处理重连与 NOOP 心跳。</para>
/// </summary>
public sealed class ImapMailService
{
    /// <summary>一次同步最多拉多少封（首次同步别把几万封邮件全拉下来）。</summary>
    public const int SyncBatch = 300;

    private const string BackupSubjectPrefix = "[NewToys备份]";

    private static readonly System.Text.RegularExpressions.Regex IndexPattern =
        new(@"NTB-\d{6}-\d{4}", System.Text.RegularExpressions.RegexOptions.Compiled);

    private readonly BackupSettingsService _settings;
    private readonly VaultService _vault;

    public ImapMailService(BackupSettingsService settings, VaultService vault)
    {
        _settings = settings;
        _vault = vault;
    }

    /// <summary>收信是否可用（配置齐 + 密码库解锁 + 有授权码）。</summary>
    public (bool Ok, string Reason) CanReceive()
    {
        var mail = _settings.MailSettings;
        if (!mail.IsImapConfigured) return (false, "还没配置收信服务器（备份中心设置 → 邮件投递）");
        if (!_vault.IsUnlocked) return (false, "密码库没解锁，取不到邮箱授权码");
        if (string.IsNullOrEmpty(_vault.Get(BackupSettingsService.MailAuthVaultId)))
            return (false, "密码库里没有邮箱授权码");
        return (true, "");
    }

    /// <summary>只连不收：验证 IMAP 地址 / 端口 / 授权码（配置页「测试连接」用）。</summary>
    public async Task<(bool Ok, string Message)> TestAsync(CancellationToken ct)
    {
        var mail = _settings.MailSettings;
        if (!mail.IsImapConfigured) return (false, "还没接收信服务器或账号。");
        if (!_vault.IsUnlocked) return (false, "密码库没解锁，取不到授权码。");

        var auth = _vault.Get(BackupSettingsService.MailAuthVaultId);
        if (string.IsNullOrEmpty(auth)) return (false, "密码库里没有授权码。");

        try
        {
            using var client = await ConnectAsync(mail, auth, ct).ConfigureAwait(true);
            var count = client.Inbox.Count;
            await client.DisconnectAsync(true, ct).ConfigureAwait(true);
            return (true, $"✅ 收信服务器登录成功（收件箱 {count} 封）");
        }
        catch (OperationCanceledException) { return (false, "已取消。"); }
        catch (Exception ex) { return (false, "⚠ 收信测试失败：" + ex.Message); }
    }

    /// <summary>
    /// 增量同步收件箱邮件头（只拉比本地缓存更新的 UID）。返回新拿到的邮件头。
    /// <paramref name="firstUid"/> = 本地已缓存的最大 UID + 1。
    /// </summary>
    public async Task<List<MailListItem>> SyncAsync(
        uint firstUid, IProgress<string>? progress, CancellationToken ct)
    {
        var gate = CanReceive();
        if (!gate.Ok) throw new InvalidOperationException(gate.Reason);

        var mail = _settings.MailSettings;
        var auth = _vault.Get(BackupSettingsService.MailAuthVaultId) ?? "";
        var result = new List<MailListItem>();

        using var client = await ConnectAsync(mail, auth, ct).ConfigureAwait(false);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, ct).ConfigureAwait(false);

        var total = inbox.Count;
        if (total == 0) return result;

        // 首次同步（firstUid = 0）只取最近一批；之后按 UID 区间增量
        IList<IMessageSummary> summaries;
        if (firstUid == 0)
        {
            var start = Math.Max(0, total - SyncBatch);
            progress?.Report($"同步最近 {total - start} 封…");
            summaries = await inbox.FetchAsync(start, total - 1,
                MessageSummaryItems.Full, ct).ConfigureAwait(false);
        }
        else
        {
            progress?.Report("同步新邮件…");
            summaries = await inbox.FetchAsync(new UniqueIdRange(new UniqueId(firstUid), UniqueId.MaxValue),
                MessageSummaryItems.Full, ct).ConfigureAwait(false);
        }

        foreach (var s in summaries)
        {
            if (s.UniqueId.Id == 0) continue;
            var subject = s.Envelope?.Subject ?? "";
            result.Add(new MailListItem
            {
                Mailbox = inbox.FullName,
                Uid = s.UniqueId.Id,
                Subject = subject,
                From = s.Envelope?.From?.ToString() ?? "",
                Date = (s.Envelope?.Date ?? DateTimeOffset.Now).LocalDateTime,
                SizeBytes = (long)(s.Size ?? 0),
                AttachCount = s.Attachments?.Count() ?? 0,
                IndexNo = ExtractIndexNo(subject),
                IsSeen = s.Flags?.HasFlag(MessageFlags.Seen) == true
            });
        }

        return result;
    }

    /// <summary>取一封邮件的正文与附件清单（只读，不改已读状态）。</summary>
    public async Task<MailDetail> GetDetailAsync(string mailbox, uint uid, CancellationToken ct)
    {
        var gate = CanReceive();
        if (!gate.Ok) throw new InvalidOperationException(gate.Reason);

        var mail = _settings.MailSettings;
        var auth = _vault.Get(BackupSettingsService.MailAuthVaultId) ?? "";

        using var client = await ConnectAsync(mail, auth, ct).ConfigureAwait(false);
        var folder = await OpenAsync(client, mailbox, ct).ConfigureAwait(false);
        var message = await folder.GetMessageAsync(new UniqueId(uid), ct)
            .ConfigureAwait(false);

        var attachments = message.Attachments.OfType<MimePart>()
            .Select(p => new MailAttachment
            {
                FileName = p.FileName ?? "(未命名)",
                SizeBytes = p.Content?.Stream?.Length ?? 0
            })
            .ToList();

        var body = message.TextBody ?? StripHtml(message.HtmlBody) ?? "";

        return new MailDetail(
            message.Subject ?? "",
            message.From?.ToString() ?? "",
            message.Date.LocalDateTime,
            body.Length > 20000 ? body[..20000] + "\n\n…（正文过长，已截断）" : body,
            attachments,
            ExtractIndexNo(message.Subject ?? ""));
    }

    /// <summary>
    /// 按索引号把某个备份邮件的全部分卷下载到 <paramref name="targetDir"/>。
    /// <para>🔴 用 IMAP 的 <c>SUBJECT</c> 搜索（索引号是纯 ASCII，各服务器都支持）；一个连接内下完，避免每卷重连。</para>
    /// </summary>
    public async Task<(List<string> Files, string Error)> DownloadIndexAsync(
        string indexNo, string targetDir, IProgress<string>? progress, CancellationToken ct)
    {
        var gate = CanReceive();
        if (!gate.Ok) return (new(), gate.Reason);

        var mail = _settings.MailSettings;
        var auth = _vault.Get(BackupSettingsService.MailAuthVaultId) ?? "";
        Directory.CreateDirectory(targetDir);

        var files = new List<string>();
        using var client = await ConnectAsync(mail, auth, ct).ConfigureAwait(false);
        var folder = client.Inbox;
        await folder.OpenAsync(FolderAccess.ReadOnly, ct).ConfigureAwait(false);

        var uids = await folder.SearchAsync(SearchQuery.SubjectContains(indexNo), ct).ConfigureAwait(false);
        if (uids.Count == 0) return (files, $"服务器上找不到索引号 {indexNo} 的邮件。");

        var done = 0;
        foreach (var uid in uids)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"下载第 {++done}/{uids.Count} 封…");

            var message = await folder.GetMessageAsync(uid, ct).ConfigureAwait(false);
            foreach (var part in message.Attachments.OfType<MimePart>())
            {
                var name = part.FileName ?? "";
                if (name.Length == 0 || !name.StartsWith(indexNo, StringComparison.OrdinalIgnoreCase)) continue;

                // 🔴 只接受索引号开头的分卷名：⛔ 不信任邮件里的文件名（可能含 .. 或路径分隔符）
                var safe = Path.GetFileName(name);
                if (safe.Length == 0 || safe != name) continue;

                var target = Path.Combine(targetDir, safe);
                if (part.Content != null)
                {
                    await using var fs = File.Create(target);
                    await part.Content.DecodeToAsync(fs, ct).ConfigureAwait(false);
                    files.Add(target);
                }
            }
        }

        var ordered = files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        return (ordered, ordered.Count == 0 ? "邮件里没有匹配的分卷附件。" : "");
    }

    /// <summary>下载一封邮件里某个附件到 <paramref name="targetDir"/>（返回落盘路径）。</summary>
    public async Task<(List<string> Files, string Error)> DownloadAttachmentAsync(
        string mailbox, uint uid, string fileName, string targetDir, CancellationToken ct)
    {
        var gate = CanReceive();
        if (!gate.Ok) return (new(), gate.Reason);
        if (string.IsNullOrWhiteSpace(fileName)) return (new(), "附件名是空的。");

        var mail = _settings.MailSettings;
        var auth = _vault.Get(BackupSettingsService.MailAuthVaultId) ?? "";
        Directory.CreateDirectory(targetDir);

        using var client = await ConnectAsync(mail, auth, ct).ConfigureAwait(false);
        var folder = await OpenAsync(client, mailbox, ct).ConfigureAwait(false);
        var message = await folder.GetMessageAsync(new UniqueId(uid), ct)
            .ConfigureAwait(false);

        var files = new List<string>();
        foreach (var part in message.Attachments.OfType<MimePart>())
        {
            if (!string.Equals(part.FileName, fileName, StringComparison.OrdinalIgnoreCase)) continue;
            if (part.Content == null) continue;

            // 🔴 只取文件名部分：邮件里的名字是外部输入，⛔ 不能直接拼路径
            var safe = Path.GetFileName(part.FileName ?? "");
            if (safe.Length == 0) continue;

            var target = Path.Combine(targetDir, safe);
            await using (var fs = File.Create(target))
                await part.Content.DecodeToAsync(fs, ct).ConfigureAwait(false);
            files.Add(target);
        }

        return (files, files.Count == 0 ? "邮件里找不到这个附件（可能已被服务器改写）。" : "");
    }

    /// <summary>把邮件移到「已删除」并清理（只影响服务器上那一封）。</summary>
    public async Task<(bool Ok, string Error)> DeleteAsync(string mailbox, uint uid, CancellationToken ct)
    {
        var gate = CanReceive();
        if (!gate.Ok) return (false, gate.Reason);

        var mail = _settings.MailSettings;
        var auth = _vault.Get(BackupSettingsService.MailAuthVaultId) ?? "";

        using var client = await ConnectAsync(mail, auth, ct).ConfigureAwait(false);
        var folder = await OpenAsync(client, mailbox, ct).ConfigureAwait(false);
        await folder.AddFlagsAsync(new UniqueId(uid), MessageFlags.Deleted, true, ct).ConfigureAwait(false);
        await folder.ExpungeAsync(ct).ConfigureAwait(false);
        return (true, "");
    }

    /// <summary>
    /// 服务器侧概况：收件箱封数。容量（QUOTA）多数免费邮箱不开放 IMAP 查询，⛔ 不假装能算，
    /// 页面显示「未提供」并指路网页版。
    /// </summary>
    public async Task<string> QuotaTextAsync(CancellationToken ct)
    {
        var gate = CanReceive();
        if (!gate.Ok) return "";

        var mail = _settings.MailSettings;
        var auth = _vault.Get(BackupSettingsService.MailAuthVaultId) ?? "";
        try
        {
            using var client = await ConnectAsync(mail, auth, ct).ConfigureAwait(false);
            var count = client.Inbox.Count;
            return $"服务器收件箱 {count} 封 · 容量请到网页版查看";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][backup] 读取服务器概况失败：" + ex.Message);
            return "";
        }
    }

    /// <summary>从主题里取索引号（无则空串 ⇒ 当普通邮件处理）。</summary>
    public static string ExtractIndexNo(string subject)
    {
        if (string.IsNullOrEmpty(subject)) return "";
        var m = IndexPattern.Match(subject);
        return m.Success ? m.Value : "";
    }

    /// <summary>是否是本程序发出的备份邮件（主题前缀 + 索引号）。</summary>
    public static bool IsBackupSubject(string subject)
        => !string.IsNullOrEmpty(subject)
           && subject.Contains(BackupSubjectPrefix, StringComparison.Ordinal)
           && ExtractIndexNo(subject).Length > 0;

    private static async Task<ImapClient> ConnectAsync(MailSettingsDto mail, string auth, CancellationToken ct)
    {
        var client = new ImapClient { Timeout = 120_000 };
        var secure = mail.ImapPort == 993 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await client.ConnectAsync(mail.ImapHost.Trim(), mail.ImapPort, secure, ct).ConfigureAwait(false);
        await client.AuthenticateAsync(mail.ImapAccount, auth, ct).ConfigureAwait(false);
        return client;
    }

    private static async Task<IMailFolder> OpenAsync(ImapClient client, string mailbox, CancellationToken ct)
    {
        IMailFolder folder;
        if (string.IsNullOrWhiteSpace(mailbox) || mailbox.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
        {
            folder = client.Inbox;
        }
        else
        {
            folder = await client.GetFolderAsync(mailbox, ct).ConfigureAwait(false);
        }
        await folder.OpenAsync(FolderAccess.ReadOnly, ct).ConfigureAwait(false);
        return folder;
    }

    /// <summary>HTML 兜底成纯文本（⛔ 不渲染 HTML：邮件里的脚本 / 外链图片不该进桌面程序）。</summary>
    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var sb = new StringBuilder(html.Length);
        var inTag = false;
        foreach (var c in html)
        {
            if (c == '<') { inTag = true; continue; }
            if (c == '>') { inTag = false; sb.Append(' '); continue; }
            if (!inTag) sb.Append(c);
        }
        return System.Net.WebUtility.HtmlDecode(sb.ToString()).Trim();
    }
}
