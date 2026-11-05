using System;
using System.IO;
using System.Linq;
using NewToys.BackupCenter.Models;

namespace NewToys.BackupCenter.Services.Mail;

/// <summary>待发目录的清理：投递成功后本地分卷保留 BackupSettingsService.MailBufferDays 天，到期由这里删掉。（WHY: BC-012）</summary>
public sealed class OutboxService
{
    private readonly MailJobStore _jobs;
    private readonly BackupSettingsService _settings;

    public OutboxService(MailJobStore jobs, BackupSettingsService settings)
    {
        _jobs = jobs;
        _settings = settings;
    }

    /// <summary>待发目录里当前的分卷文件（页面显示「待清理 N 个」）。</summary>
    public (int Files, long Bytes) OutboxUsage()
    {
        var dir = _settings.OutboxDir;
        if (!Directory.Exists(dir)) return (0, 0);
        try
        {
            var files = Directory.EnumerateFiles(dir, "*.7z.*").ToList();
            return (files.Count, files.Sum(f => new FileInfo(f).Length));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][backup] 待发目录统计失败：" + ex.Message);
            return (0, 0);
        }
    }

    /// <summary>
    /// 清理到期的本地分卷。返回（已删记录数, 已删文件数, 说明）。
    /// <para>启动时与「备份任务」页刷新时各调一次即可，⛔ 不需要定时器 —— 缓冲期以天计。</para>
    /// </summary>
    public (int Jobs, int Files, string Message) PurgeDue()
    {
        var due = _jobs.DueForPurge(DateTime.Now);
        if (due.Count == 0) return (0, 0, "");

        var deletedFiles = 0;
        var doneJobs = 0;

        foreach (var job in due)
        {
            var parts = _jobs.ListParts(job.IndexNo);
            if (parts.Count == 0)
            {
                // 没有分卷记录（早期记录 / 手工删过）⇒ 直接把状态推进，不留僵尸行
                _jobs.MarkJobPurged(job.IndexNo);
                doneJobs++;
                continue;
            }

            var removed = 0;
            foreach (var part in parts.Where(p => !p.Purged))
            {
                try
                {
                    if (File.Exists(part.LocalPath))
                    {
                        File.Delete(part.LocalPath);
                        removed++;
                    }
                }
                catch (Exception ex)
                {
                    // 占用 / 权限问题 ⇒ 这条留着，下次再试（⛔ 不能标成已清理，否则文件永远留在磁盘上）
                    System.Diagnostics.Debug.WriteLine("[NewToys][backup] 分卷删除失败：" + part.LocalPath + "：" + ex.Message);
                }
            }

            if (removed == parts.Count(p => !p.Purged))
            {
                _jobs.MarkJobPurged(job.IndexNo);
                doneJobs++;
                deletedFiles += removed;
            }
        }

        return doneJobs == 0
            ? (0, 0, "⚠ 有到期分卷删不掉（文件被占用？）")
            : (doneJobs, deletedFiles, $"已清理 {doneJobs} 个索引号的本地分卷（{deletedFiles} 个文件）。");
    }

    /// <summary>手工清空待发目录里**所有已发送**的分卷（不看缓冲期；用户主动点才会走这里）。</summary>
    public (int Jobs, int Files, string Message) PurgeAllSent()
    {
        var all = _jobs.RecentJobs(500).Where(j => j.Status == "sent").ToList();
        if (all.Count == 0) return (0, 0, "没有可清理的已发送分卷。");

        var files = 0;
        foreach (var job in all)
        {
            foreach (var part in _jobs.ListParts(job.IndexNo).Where(p => !p.Purged))
            {
                try { if (File.Exists(part.LocalPath)) { File.Delete(part.LocalPath); files++; } }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[NewToys][backup] 手工清理失败：" + ex.Message); }
            }
            _jobs.MarkJobPurged(job.IndexNo);
        }

        return (all.Count, files, $"已清理 {all.Count} 个索引号的本地分卷（{files} 个文件）。");
    }

    /// <summary>投递记录（页面右栏 / 详情卡用）。</summary>
    public System.Collections.Generic.List<MailJobRecord> RecentJobs(int limit = 30) => _jobs.RecentJobs(limit);
}
