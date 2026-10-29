using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Security;
using MimeKit;
using NewToys.BackupCenter.Models;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace NewToys.BackupCenter.Services.Mail;

/// <summary>
/// 邮件投递：把加密包切成若干分卷、登记索引号、逐封发到邮箱。
/// <para>
/// 🔴 **顺序是硬要求**：领索引号 → 归档（带 <c>-v</c> 分卷）→ 算每卷 SHA256 → **先落库** → <c>7z t</c> 自检 → 逐封发送。
/// 任何一步失败都不进入下一步，本地分卷不删（等用户重投或人工处理）。
/// </para>
/// </summary>
public sealed class BackupMailService
{
    /// <summary>同一批分卷之间的发送间隔（服务器有频率限制，连发容易被判成垃圾邮件）。</summary>
    private static readonly TimeSpan SendGap = TimeSpan.FromSeconds(3);

    private const int MaxAttempts = 3;

    private readonly BackupSettingsService _settings;
    private readonly ArchiveService _archives;
    private readonly VaultService _vault;
    private readonly MailJobStore _jobs;

    public BackupMailService(
        BackupSettingsService settings,
        ArchiveService archives,
        VaultService vault,
        MailJobStore jobs)
    {
        _settings = settings;
        _archives = archives;
        _vault = vault;
        _jobs = jobs;
    }

    /// <summary>邮件通道是否可用于投递（配置齐 + 密码库已解锁且存了授权码）。</summary>
    public (bool Ok, string Reason) CanDeliver()
    {
        var mail = _settings.MailSettings;
        if (!mail.IsConfigured) return (false, "还没配置邮箱（备份中心设置 → 邮件投递）");
        if (!_vault.IsUnlocked) return (false, "密码库没解锁，取不到邮箱授权码");
        if (string.IsNullOrEmpty(_vault.Get(BackupSettingsService.MailAuthVaultId)))
            return (false, "密码库里没有邮箱授权码（备份中心设置 → 邮件投递）");
        return (true, "");
    }

    /// <summary>只连不发：验证服务器地址 / 端口 / 授权码（配置页「测试连接」用）。</summary>
    public async Task<(bool Ok, string Message)> TestAsync(CancellationToken ct)
    {
        var mail = _settings.MailSettings;
        if (!mail.IsConfigured) return (false, "还没填发信服务器或账号。");
        if (!_vault.IsUnlocked) return (false, "密码库没解锁，取不到授权码。");

        var auth = _vault.Get(BackupSettingsService.MailAuthVaultId);
        if (string.IsNullOrEmpty(auth)) return (false, "密码库里没有授权码。");

        try
        {
            using var client = new SmtpClient { Timeout = 30_000 };
            var secure = mail.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            await client.ConnectAsync(mail.SmtpHost.Trim(), mail.SmtpPort, secure, ct).ConfigureAwait(true);
            await client.AuthenticateAsync(mail.SmtpUser.Trim(), auth, ct).ConfigureAwait(true);
            await client.DisconnectAsync(true, ct).ConfigureAwait(true);
            return (true, $"✅ 发信服务器登录成功（{mail.SmtpHost.Trim()}:{mail.SmtpPort}）");
        }
        catch (OperationCanceledException) { return (false, "已取消。"); }
        catch (Exception ex) { return (false, "⚠ 发信测试失败：" + Describe(ex)); }
    }

    /// <summary>
    /// 建包 + 投递。返回的 <see cref="MailDeliveryOutcome.IndexNo"/> 在失败时也可能有值（已经领过号），便于用户排查。
    /// </summary>
    public async Task<MailDeliveryOutcome> DeliverAsync(
        string sevenZip, BackupPlan plan, string password, IProgress<string>? phase, CancellationToken ct)
    {
        var gate = CanDeliver();
        if (!gate.Ok) return new MailDeliveryOutcome(false, false, gate.Reason, "", 0);

        var mail = _settings.MailSettings;
        var auth = _vault.Get(BackupSettingsService.MailAuthVaultId) ?? "";
        var indexNo = _jobs.NewIndexNo();

        var outbox = _settings.OutboxDir;
        Directory.CreateDirectory(outbox);

        var archiveFile = Path.Combine(outbox, indexNo + ".7z");
        var volumeBytes = Math.Clamp(mail.VolumeMb, 1, 30) * 1024L * 1024L;

        phase?.Report("压缩 + 加密 + 分卷中…");
        var created = await _archives.CreateAsync(sevenZip, plan, archiveFile, password, phase, ct, volumeBytes)
            .ConfigureAwait(true);

        if (created.Canceled)
        {
            Cleanup(archiveFile);
            return new MailDeliveryOutcome(false, true, "已取消（半成品分卷已删除）。", indexNo, 0);
        }
        if (!created.Ok)
        {
            Cleanup(archiveFile);
            return new MailDeliveryOutcome(false, false, created.Message, indexNo, 0);
        }

        var parts = created.Parts ?? ArchiveService.ListVolumes(archiveFile);
        if (parts.Count == 0)
            return new MailDeliveryOutcome(false, false, "分卷没有生成（7z 可能没装或不支持 -v）。", indexNo, 0);

        // 卷数上限：一封邮件一个分卷，卷太多会让「一封信」的索引号散成几十封，回头没法管理
        if (parts.Count > 99)
        {
            Cleanup(archiveFile);
            return new MailDeliveryOutcome(false, false,
                $"这个包要切成 {parts.Count} 卷（超过 99 卷上限）。建议缩小备份范围，或把分卷大小改成 30 MB。", indexNo, 0);
        }

        phase?.Report("计算校验值…");
        var totalBytes = 0L;
        var partRecords = new List<MailPartRecord>(parts.Count);
        for (var i = 0; i < parts.Count; i++)
        {
            var info = new FileInfo(parts[i]);
            totalBytes += info.Length;
            partRecords.Add(new MailPartRecord
            {
                IndexNo = indexNo,
                PartNo = i + 1,
                FileName = info.Name,
                SizeBytes = info.Length,
                LocalPath = info.FullName,
                Sha256 = await Task.Run(() => ArchiveCatalog.ComputeSha256(info.FullName), ct).ConfigureAwait(true)
            });
        }

        var job = new MailJobRecord
        {
            IndexNo = indexNo,
            PlanId = plan.Id,
            PlanName = plan.Name,
            PackageName = Path.GetFileName(archiveFile),
            TotalBytes = totalBytes,
            PartCount = parts.Count,
            Sha256 = await Task.Run(() => CombinedSha256(partRecords.Select(p => (p.LocalPath, p.Sha256))), ct)
                .ConfigureAwait(true),
            Status = "pending",
            CreatedAt = DateTime.Now
        };

        // 🔴 先落库再发信：发到一半崩溃也能凭 db 找回索引号与分卷清单
        if (!_jobs.SaveJob(job))
            return new MailDeliveryOutcome(false, false, "投递记录写不进数据库，已中止（分卷留在待发目录）。", indexNo, parts.Count);
        foreach (var part in partRecords) _jobs.SavePart(part);

        phase?.Report("本地校验分卷…");
        var test = await _archives.TestAsync(sevenZip, parts[0], password, ct).ConfigureAwait(true);
        if (!test.Ok)
        {
            _jobs.MarkJobFailed(indexNo, "自检失败：" + test.Message);
            return new MailDeliveryOutcome(false, false, "分卷自检失败（未发送）：" + test.Message, indexNo, parts.Count);
        }

        for (var i = 0; i < partRecords.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var part = partRecords[i];
            phase?.Report($"发送第 {part.PartNo}/{partRecords.Count} 卷…");

            var sent = await SendWithRetryAsync(mail, auth, part, job, ct).ConfigureAwait(true);
            if (!sent.Ok)
            {
                _jobs.MarkJobFailed(indexNo, sent.Error);
                return new MailDeliveryOutcome(false, false,
                    $"第 {part.PartNo}/{partRecords.Count} 卷发送失败：{sent.Error}（本地分卷保留，可重投）",
                    indexNo, partRecords.Count);
            }

            _jobs.MarkPartSent(indexNo, part.PartNo, sent.MessageId ?? "");
            if (i < partRecords.Count - 1) await Task.Delay(SendGap, ct).ConfigureAwait(true);
        }

        _jobs.MarkJobSent(indexNo, DateTime.Now.AddDays(BackupSettingsService.MailBufferDays));
        return new MailDeliveryOutcome(true, false,
            $"已发送 {indexNo}（{partRecords.Count} 封，共 {ArchiveRecord.FormatSize(totalBytes)}）；本地分卷保留 {BackupSettingsService.MailBufferDays} 天后自动清理。",
            indexNo, partRecords.Count);
    }

    /// <summary>单卷发送（失败重试 <see cref="MaxAttempts"/> 次，3/6/12 秒退避）。</summary>
    private async Task<(bool Ok, string? MessageId, string Error)> SendWithRetryAsync(
        MailSettingsDto mail, string auth, MailPartRecord part, MailJobRecord job, CancellationToken ct)
    {
        var error = "";
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var messageId = await SendOnceAsync(mail, auth, part, job, ct).ConfigureAwait(true);
                return (true, messageId, "");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                error = Describe(ex);
                if (attempt == MaxAttempts) break;
                await Task.Delay(TimeSpan.FromSeconds(3 * (1 << (attempt - 1))), ct).ConfigureAwait(true);
            }
        }
        return (false, null, error);
    }

    private static async Task<string> SendOnceAsync(
        MailSettingsDto mail, string auth, MailPartRecord part, MailJobRecord job, CancellationToken ct)
    {
        using var client = new SmtpClient { Timeout = 300_000 };
        var secure = mail.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

        await client.ConnectAsync(mail.SmtpHost.Trim(), mail.SmtpPort, secure, ct).ConfigureAwait(false);
        await client.AuthenticateAsync(mail.SmtpUser.Trim(), auth, ct).ConfigureAwait(false);

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(mail.SmtpUser.Trim()));
        message.To.Add(MailboxAddress.Parse(mail.Recipient));

        // 🔴 主题⛔ 不含任务名 / 源目录：邮件在服务器上是明文，索引号是唯一对外的标识
        message.Subject = $"[NewToys备份] {job.IndexNo} · {part.PartNo}/{job.PartCount}";

        var body = new Multipart("mixed");
        body.Add(new TextPart("plain")
        {
            Text =
                $"NewToys 加密备份 · 索引号 {job.IndexNo}\r\n" +
                $"分卷 {part.PartNo}/{job.PartCount} · {ArchiveRecord.FormatSize(part.SizeBytes)}\r\n" +
                $"分卷合计 {ArchiveRecord.FormatSize(job.TotalBytes)}\r\n" +
                $"生成时间 {job.CreatedAt:yyyy-MM-dd HH:mm}\r\n\r\n" +
                "附件是 AES-256 加密（含头部加密）的 7z 分卷，必须集齐同一索引号的全部分卷才能解压。\r\n" +
                "还原：NewToys → 备份中心 → 备份邮件，搜索引号 → 下载并还原。\r\n"
        });

        // 🔴 附件流必须自己释放：MimeContent 只包住 stream，SendAsync 完成后不会替我们关文件
        var fileStream = File.OpenRead(part.LocalPath);
        try
        {
            var attachment = new MimePart("application", "x-7z-compressed")
            {
                Content = new MimeContent(fileStream, ContentEncoding.Default),
                ContentDisposition = new ContentDisposition(ContentDisposition.Attachment)
                {
                    FileName = part.FileName
                },
                ContentTransferEncoding = ContentEncoding.Base64
            };

            body.Add(attachment);
            message.Body = body;

            await client.SendAsync(message, ct).ConfigureAwait(false);
        }
        finally
        {
            await fileStream.DisposeAsync().ConfigureAwait(false);
        }

        try { await client.DisconnectAsync(true, CancellationToken.None).ConfigureAwait(false); } catch { /* 发完就走，断连失败不影响结果 */ }

        return message.MessageId ?? "";
    }

    /// <summary>把异常翻成用户能看懂的一句话（Q6 邮箱最常见的是「授权码错 / 未开 SMTP」）。</summary>
    private static string Describe(Exception ex)
    {
        var text = ex.Message ?? "";
        if (ex is AuthenticationException) return "账号或授权码不对（QQ 邮箱要用授权码，不是登录密码）";
        if (text.Contains("5.7.", StringComparison.Ordinal)) return "服务器拒收：" + text;
        if (ex is System.Net.Sockets.SocketException) return "连不上服务器（网络 / 端口 / 防火墙）";
        if (text.Contains("timed out", StringComparison.OrdinalIgnoreCase)) return "连接超时";
        return ex.GetType().Name + "：" + text;
    }

    /// <summary>按分卷顺序拼接内容的 SHA256（不整包读内存，逐卷流式增量算）。</summary>
    private static string CombinedSha256(IEnumerable<(string Path, string Sha)> parts)
    {
        using var sha = SHA256.Create();
        foreach (var (path, _) in parts)
        {
            if (!File.Exists(path)) continue;
            using var stream = File.OpenRead(path);
            var buffer = new byte[81920];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) sha.TransformBlock(buffer, 0, read, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash ?? Array.Empty<byte>());
    }

    /// <summary>建包失败 / 取消时清掉半成品分卷（成功不动：要留 3 天）。</summary>
    private static void Cleanup(string archiveFile)
    {
        foreach (var path in ArchiveService.ListVolumes(archiveFile))
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* 删不掉就留着，不阻断面向上层的结果 */ }
        }
        try { if (File.Exists(archiveFile)) File.Delete(archiveFile); } catch { }
    }
}
