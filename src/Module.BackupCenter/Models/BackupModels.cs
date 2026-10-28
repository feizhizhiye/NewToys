using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NewToys.BackupCenter.Models;

/// <summary>一个备份任务：若干源目录 → 一个加密归档。</summary>
public sealed class BackupPlan
{
    /// <summary>稳定标识（密码库里按它取密码，⛔ 不随改名变化）。</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>源目录（1~N；可以是单个文件）。</summary>
    public List<string> Sources { get; set; } = new();

    /// <summary>排除规则，一行一条（按 7z 的 <c>-xr!</c> 通配，如 <c>*.tmp</c> / <c>cache\*</c>）。</summary>
    public string Excludes { get; set; } = "";

    /// <summary>本地保留份数（归档成功后按份数轮转删最旧的）。</summary>
    public int KeepCount { get; set; } = 5;

    public string Note { get; set; } = "";
}

/// <summary>本地包清单里的一条（<c>catalog.json</c>）。</summary>
public sealed class ArchiveRecord
{
    public string FileName { get; set; } = "";

    public string PlanId { get; set; } = "";

    public string PlanName { get; set; } = "";

    public long SizeBytes { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>整包 SHA256（大写十六进制；UI 只显示前 12 位）。</summary>
    public string Sha256 { get; set; } = "";

    [JsonIgnore] public string SizeText => FormatSize(SizeBytes);

    [JsonIgnore] public string ShaText => Sha256.Length >= 12 ? Sha256[..12] : Sha256;

    [JsonIgnore] public string CreatedText => CreatedAt == default ? "" : CreatedAt.ToString("yyyy-MM-dd HH:mm");

    /// <summary>归档一律 AES-256 + 头部加密 ⇒ 恒为「是」（字段保留给将来的明文包）。</summary>
    [JsonIgnore] public string EncryptedText => "是";

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
        if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
        return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.00") + " GB";
    }
}

/// <summary>一次备份运行的记录（<c>runs.json</c>；每个任务只保留最近若干条）。</summary>
public sealed class BackupRunRecord
{
    public string PlanId { get; set; } = "";

    public DateTime StartedAt { get; set; }

    /// <summary>成功 / 警告 / 失败 / 已取消。</summary>
    public string Status { get; set; } = "";

    public string ArchiveFile { get; set; } = "";

    public string Message { get; set; } = "";

    public int SkippedFiles { get; set; }
}

/// <summary>详情卡只读列表的一行（主文本 + 可选第二行说明）。</summary>
public sealed class TextLine
{
    public string Text { get; set; } = "";

    public string Detail { get; set; } = "";
}

/// <summary>还原时目标已存在的处理方式。</summary>
public enum RestoreConflictPolicy
{
    /// <summary>跳过（不动磁盘）——默认。</summary>
    Skip,

    /// <summary>覆盖已有文件。</summary>
    Overwrite,

    /// <summary>改名成「名字 (1).ext」再放。</summary>
    Rename
}
