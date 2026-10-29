using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NewToys.BackupCenter.Models;

/// <summary>任务的产出通道：本地归档（现状）或邮件投递（分卷 + 索引号）。</summary>
public enum BackupDeliver
{
    /// <summary>落在「默认备份目录」，进本地包清单，受保留份数轮转。</summary>
    Local,

    /// <summary>分卷落在待发目录，发到邮箱；⛔ 不进本地包清单、⛔ 不受保留份数管。</summary>
    Mail
}

/// <summary>邮件通道的配置（<c>config\backup-center.json</c>；授权码⛔ 不在这里，进密码库）。</summary>
public sealed class MailSettingsDto
{
    public string SmtpHost { get; set; } = "";

    /// <summary>默认 465（QQ 邮箱的 SSL 端口）；587 = STARTTLS。</summary>
    public int SmtpPort { get; set; } = 465;

    public string SmtpUser { get; set; } = "";

    /// <summary>收件人（留空 = 发给自己，即 <see cref="SmtpUser"/>）。</summary>
    public string SmtpTo { get; set; } = "";

    public string ImapHost { get; set; } = "";

    public int ImapPort { get; set; } = 993;

    /// <summary>留空 = 与 SMTP 同一个账号（同一个邮箱收发）。</summary>
    public string ImapUser { get; set; } = "";

    /// <summary>分卷大小（MB）：10 / 20 / 30。🔴 上限 30 —— 再大 base64 膨胀后会超 QQ 的 50 M。</summary>
    public int VolumeMb { get; set; } = 30;

    /// <summary>待发目录（留空 = <c>%USERPROFILE%\NewToys\Backup\outbox</c>）。</summary>
    public string OutboxDir { get; set; } = "";

    [JsonIgnore]
    public bool IsConfigured => SmtpHost.Trim().Length > 0 && SmtpUser.Trim().Length > 0;

    [JsonIgnore]
    public string Recipient => string.IsNullOrWhiteSpace(SmtpTo) ? SmtpUser.Trim() : SmtpTo.Trim();

    [JsonIgnore]
    public string ImapAccount => string.IsNullOrWhiteSpace(ImapUser) ? SmtpUser.Trim() : ImapUser.Trim();

    [JsonIgnore]
    public bool IsImapConfigured => ImapHost.Trim().Length > 0 && ImapAccount.Length > 0;
}

/// <summary>一次邮件投递（<c>mail_job</c>）：一个索引号 = 一个加密包的全部 n 个分卷。</summary>
public sealed class MailJobRecord
{
    public string IndexNo { get; set; } = "";

    public string PlanId { get; set; } = "";

    public string PlanName { get; set; } = "";

    /// <summary>加密包基名（<c>&lt;索引号&gt;.7z</c>，分卷再加 <c>.001</c>）。</summary>
    public string PackageName { get; set; } = "";

    /// <summary>分卷合计字节。</summary>
    public long TotalBytes { get; set; }

    public int PartCount { get; set; }

    /// <summary>整包 SHA256（按分卷顺序拼接内容的哈希，UI 只看前 12 位）。</summary>
    public string Sha256 { get; set; } = "";

    /// <summary>pending / sent / failed / purged。</summary>
    public string Status { get; set; } = "pending";

    public DateTime CreatedAt { get; set; }

    public DateTime? SentAt { get; set; }

    /// <summary>本地分卷到期可删的时间（成功 + 缓冲期）。</summary>
    public DateTime? PurgeAfter { get; set; }

    public string Error { get; set; } = "";

    [JsonIgnore] public string SizeText => ArchiveRecord.FormatSize(TotalBytes);

    [JsonIgnore] public string PartsText => PartCount <= 1 ? "1 卷" : PartCount + " 卷";

    [JsonIgnore] public string CreatedText => CreatedAt == default ? "" : CreatedAt.ToString("yyyy-MM-dd HH:mm");

    [JsonIgnore] public string StatusText => Status switch
    {
        "sent" => "已发送",
        "failed" => "失败",
        "purged" => "已清理本地",
        _ => "待发送"
    };

    [JsonIgnore] public string ShaText => Sha256.Length >= 12 ? Sha256[..12] : Sha256;
}

/// <summary>一个分卷（<c>mail_part</c>）：本地文件名 = 邮件附件名。</summary>
public sealed class MailPartRecord
{
    public string IndexNo { get; set; } = "";

    /// <summary>从 1 开始（7z 用 <c>.001</c> 起）。</summary>
    public int PartNo { get; set; }

    public string FileName { get; set; } = "";

    public long SizeBytes { get; set; }

    public string Sha256 { get; set; } = "";

    public string MessageId { get; set; } = "";

    public string LocalPath { get; set; } = "";

    public DateTime? SentAt { get; set; }

    /// <summary>本地文件是否已按缓冲策略删掉。</summary>
    public bool Purged { get; set; }

    [JsonIgnore] public string SizeText => ArchiveRecord.FormatSize(SizeBytes);

    [JsonIgnore] public string PartText => $"第 {PartNo} 卷";
}

/// <summary>邮箱里的一封邮件（<c>mail_index</c> 缓存行，列表用）。</summary>
public sealed class MailListItem
{
    public string Mailbox { get; set; } = "INBOX";

    public uint Uid { get; set; }

    public string Subject { get; set; } = "";

    public string From { get; set; } = "";

    public DateTime Date { get; set; }

    public long SizeBytes { get; set; }

    public int AttachCount { get; set; }

    /// <summary>NewToys 备份邮件才有（从主题里提取的索引号）。</summary>
    public string IndexNo { get; set; } = "";

    public bool IsSeen { get; set; }

    [JsonIgnore] public bool IsBackup => IndexNo.Length > 0;

    [JsonIgnore] public string DateText => Date == default ? "" : Date.ToString("yyyy-MM-dd HH:mm");

    [JsonIgnore] public string SizeText => ArchiveRecord.FormatSize(SizeBytes);

    [JsonIgnore] public string FromText => From.Length > 40 ? From[..40] + "…" : From;

    [JsonIgnore] public string SeenMark => IsSeen ? "已读" : "未读";

    [JsonIgnore] public string BackupMark => IsBackup ? "备份" : "";

    [JsonIgnore] public string AttachText => AttachCount > 0 ? AttachCount + " 个附件" : "";
}

/// <summary>邮件详情里的一个附件（IMAP 现场解析，不落库）。</summary>
public sealed class MailAttachment
{
    public string FileName { get; set; } = "";

    public long SizeBytes { get; set; }

    public bool IsBackupPart => FileName.EndsWith(".7z.001", StringComparison.OrdinalIgnoreCase)
        || FileName.Contains(".7z.", StringComparison.OrdinalIgnoreCase);

    public string SizeText => ArchiveRecord.FormatSize(SizeBytes);
}

/// <summary>一次投递的结果（给任务页状态栏用）。</summary>
public sealed record MailDeliveryOutcome(bool Ok, bool Canceled, string Message, string IndexNo, int Parts);
