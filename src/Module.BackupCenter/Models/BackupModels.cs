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

    /// <summary>「大小 · 时间」一行摘要（日历右栏用）。</summary>
    [JsonIgnore] public string MetaText => SizeText + " · " + CreatedText;

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

/// <summary>备份日历上的一条待办（<c>todos</c> 表）：哪天要备份、做了没。⛔ 不参与自动执行。</summary>
public sealed class BackupTodo
{
    public long Id { get; set; }

    /// <summary>关联任务（可为空 = 只是提醒，没绑具体任务）。</summary>
    public string PlanId { get; set; } = "";

    /// <summary>计划日期（只用到日）。</summary>
    public DateTime DueDate { get; set; }

    public string Title { get; set; } = "";

    public string Note { get; set; } = "";

    /// <summary>完成时间（null = 未完成）。</summary>
    public DateTime? DoneAt { get; set; }

    [JsonIgnore] public bool IsDone => DoneAt != null;

    [JsonIgnore] public string CheckMark => IsDone ? "☑" : "☐";

    [JsonIgnore] public string DueText => DueDate == default ? "" : DueDate.ToString("yyyy-MM-dd");

    [JsonIgnore] public string PlanName { get; set; } = "";
}

/// <summary>
/// 日历格子（月视图的一格）：当天备份次数 + 待办数。
/// <para>纯展示模型，由 <c>BackupCalendarViewModel</c> 每次重建 —— 一个月的格子最多 42 个，重建比增量更新简单。</para>
/// </summary>
public sealed class CalendarDay
{
    public DateTime Date { get; set; }

    /// <summary>是否属于当前显示月份（前后补齐的空格显示淡字）。</summary>
    public bool IsCurrentMonth { get; set; }

    public bool IsToday { get; set; }

    public int BackupCount { get; set; }

    public int FailedCount { get; set; }

    public int TodoCount { get; set; }

    [JsonIgnore] public string DayText => Date.Day.ToString();

    /// <summary>格子右下角的一句话摘要（空 = 不显示）。</summary>
    [JsonIgnore]
    public string Summary
    {
        get
        {
            if (BackupCount == 0 && TodoCount == 0) return "";
            var parts = new List<string>();
            if (BackupCount > 0) parts.Add("备份 " + BackupCount);
            if (TodoCount > 0) parts.Add("待办 " + TodoCount);
            return string.Join(" · ", parts);
        }
    }

    [JsonIgnore] public bool HasBackup => BackupCount > 0;

    [JsonIgnore] public bool HasTodo => TodoCount > 0;

    [JsonIgnore] public bool HasFailure => FailedCount > 0;

    [JsonIgnore] public string TipText
    {
        get
        {
            var parts = new List<string> { Date.ToString("yyyy-MM-dd") };
            if (BackupCount > 0) parts.Add($"备份 {BackupCount} 次");
            if (FailedCount > 0) parts.Add($"其中失败 {FailedCount} 次");
            if (TodoCount > 0) parts.Add($"待办 {TodoCount} 条");
            return string.Join(" · ", parts);
        }
    }
}
