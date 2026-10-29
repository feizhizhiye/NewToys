using System;
using System.Collections.Generic;
using System.Globalization;
using NewToys.BackupCenter.Models;

namespace NewToys.BackupCenter.Services.Mail;

/// <summary>
/// 邮件投递与邮箱缓存的落库层（<c>backup.db</c> 的 <c>mail_seq</c> / <c>mail_job</c> / <c>mail_part</c> / <c>mail_index</c>）。
/// <para>
/// 🔴 **先落库后发信**：发信过程中崩溃 / 断电时，凭索引号与分卷清单还能找回哪几卷已发、哪几卷没发。
/// </para>
/// </summary>
public sealed class MailJobStore
{
    private readonly BackupDb _db;

    public MailJobStore(BackupDb db) => _db = db;

    // ==================== 索引号 ====================

    /// <summary>
    /// 领一个索引号（<c>NTB-yyMMdd-NNNN</c>，当日 4 位序号）。
    /// <para>单机单进程 + 事务内 <c>last_no = last_no + 1</c>，不存在并发抢号问题。</para>
    /// </summary>
    public string NewIndexNo()
    {
        var day = DateTime.Now.ToString("yyMMdd");
        var no = 1;

        if (_db.IsReady)
        {
            try
            {
                using var conn = _db.Open();
                using var tx = conn.BeginTransaction();
                using (var upsert = conn.CreateCommand())
                {
                    upsert.Transaction = tx;
                    upsert.CommandText =
                        "INSERT INTO mail_seq (day, last_no) VALUES ($d, 1) " +
                        "ON CONFLICT(day) DO UPDATE SET last_no = last_no + 1 RETURNING last_no;";
                    upsert.Parameters.AddWithValue("$d", day);
                    no = Convert.ToInt32(upsert.ExecuteScalar() ?? 1);
                }
                tx.Commit();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[NewToys][backup] 索引号生成失败（退化为时间戳）：" + ex.Message);
                no = (int)(DateTime.Now.TimeOfDay.TotalSeconds % 9999) + 1;
            }
        }

        return $"NTB-{day}-{no:D4}";
    }

    // ==================== 投递记录 ====================

    /// <summary>写一条投递（状态 <c>pending</c>）；已存在则整条覆盖。</summary>
    public bool SaveJob(MailJobRecord job)
    {
        if (!_db.IsReady || string.IsNullOrWhiteSpace(job.IndexNo)) return false;
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO mail_job (index_no, plan_id, plan_name, package_name, total_bytes, part_count, " +
                "  sha256, status, created_at, sent_at, purge_after, error) " +
                "VALUES ($no, $pid, $pname, $pkg, $bytes, $parts, $sha, $status, $at, $sent, $purge, $err) " +
                "ON CONFLICT(index_no) DO UPDATE SET " +
                "  plan_id = $pid, plan_name = $pname, package_name = $pkg, total_bytes = $bytes," +
                "  part_count = $parts, sha256 = $sha, status = $status, sent_at = $sent," +
                "  purge_after = $purge, error = $err;";
            cmd.Parameters.AddWithValue("$no", job.IndexNo);
            cmd.Parameters.AddWithValue("$pid", job.PlanId ?? "");
            cmd.Parameters.AddWithValue("$pname", job.PlanName ?? "");
            cmd.Parameters.AddWithValue("$pkg", job.PackageName ?? "");
            cmd.Parameters.AddWithValue("$bytes", job.TotalBytes);
            cmd.Parameters.AddWithValue("$parts", job.PartCount);
            cmd.Parameters.AddWithValue("$sha", job.Sha256 ?? "");
            cmd.Parameters.AddWithValue("$status", job.Status ?? "pending");
            cmd.Parameters.AddWithValue("$at", job.CreatedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$sent", job.SentAt?.ToString("o") ?? "");
            cmd.Parameters.AddWithValue("$purge", job.PurgeAfter?.ToString("o") ?? "");
            cmd.Parameters.AddWithValue("$err", job.Error ?? "");
            return cmd.ExecuteNonQuery() > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][backup] 投递记录写入失败：" + ex.Message);
            return false;
        }
    }

    /// <summary>写一个分卷（含本地路径与 SHA256）。</summary>
    public bool SavePart(MailPartRecord part)
    {
        if (!_db.IsReady || string.IsNullOrWhiteSpace(part.IndexNo)) return false;
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO mail_part (index_no, part_no, file_name, size_bytes, sha256, message_id, local_path, sent_at, purged) " +
                "VALUES ($no, $part, $file, $size, $sha, $mid, $path, $sent, $purged) " +
                "ON CONFLICT(index_no, part_no) DO UPDATE SET " +
                "  file_name = $file, size_bytes = $size, sha256 = $sha, message_id = $mid," +
                "  local_path = $path, sent_at = $sent, purged = $purged;";
            cmd.Parameters.AddWithValue("$no", part.IndexNo);
            cmd.Parameters.AddWithValue("$part", part.PartNo);
            cmd.Parameters.AddWithValue("$file", part.FileName ?? "");
            cmd.Parameters.AddWithValue("$size", part.SizeBytes);
            cmd.Parameters.AddWithValue("$sha", part.Sha256 ?? "");
            cmd.Parameters.AddWithValue("$mid", part.MessageId ?? "");
            cmd.Parameters.AddWithValue("$path", part.LocalPath ?? "");
            cmd.Parameters.AddWithValue("$sent", part.SentAt?.ToString("o") ?? "");
            cmd.Parameters.AddWithValue("$purged", part.Purged ? 1 : 0);
            return cmd.ExecuteNonQuery() > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][backup] 分卷记录写入失败：" + ex.Message);
            return false;
        }
    }

    /// <summary>发送成功后回填 Message-ID 与发送时间。</summary>
    public void MarkPartSent(string indexNo, int partNo, string messageId)
    {
        if (!_db.IsReady) return;
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "UPDATE mail_part SET message_id = $mid, sent_at = $at WHERE index_no = $no AND part_no = $part;";
            cmd.Parameters.AddWithValue("$mid", messageId ?? "");
            cmd.Parameters.AddWithValue("$at", DateTime.Now.ToString("o"));
            cmd.Parameters.AddWithValue("$no", indexNo);
            cmd.Parameters.AddWithValue("$part", partNo);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[NewToys][backup] 分卷回填失败：" + ex.Message); }
    }

    /// <summary>整批投递成功：状态 <c>sent</c> + 记下本地分卷的到期清理时间。</summary>
    public void MarkJobSent(string indexNo, DateTime purgeAfter)
    {
        UpdateJob($"UPDATE mail_job SET status = 'sent', sent_at = $at, purge_after = $purge, error = '' WHERE index_no = $no;",
            indexNo, DateTime.Now.ToString("o"), purgeAfter.ToString("o"), "");
    }

    public void MarkJobFailed(string indexNo, string error)
    {
        UpdateJob("UPDATE mail_job SET status = 'failed', error = $err WHERE index_no = $no;", indexNo, "", "", error ?? "");
    }

    /// <summary>本地分卷已按缓冲策略清掉。</summary>
    public void MarkJobPurged(string indexNo)
    {
        if (!_db.IsReady) return;
        try
        {
            using var conn = _db.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE mail_job SET status = 'purged' WHERE index_no = $no;";
                cmd.Parameters.AddWithValue("$no", indexNo);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE mail_part SET purged = 1 WHERE index_no = $no;";
                cmd.Parameters.AddWithValue("$no", indexNo);
                cmd.ExecuteNonQuery();
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[NewToys][backup] 清理标记失败：" + ex.Message); }
    }

    private void UpdateJob(string sql, string indexNo, string at, string purge, string err)
    {
        if (!_db.IsReady) return;
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$no", indexNo);
            cmd.Parameters.AddWithValue("$at", at);
            cmd.Parameters.AddWithValue("$purge", purge);
            cmd.Parameters.AddWithValue("$err", err);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[NewToys][backup] 投递状态更新失败：" + ex.Message); }
    }

    // ==================== 查询 ====================

    /// <summary>最近的投递记录（新 → 旧）。</summary>
    public List<MailJobRecord> RecentJobs(int limit = 50)
    {
        var list = new List<MailJobRecord>();
        if (!_db.IsReady) return list;
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT index_no, plan_id, plan_name, package_name, total_bytes, part_count, sha256," +
                "  status, created_at, sent_at, purge_after, error FROM mail_job " +
                "ORDER BY created_at DESC LIMIT $limit;";
            cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new MailJobRecord
                {
                    IndexNo = reader.GetString(0),
                    PlanId = reader.GetString(1),
                    PlanName = reader.GetString(2),
                    PackageName = reader.GetString(3),
                    TotalBytes = reader.GetInt64(4),
                    PartCount = reader.GetInt32(5),
                    Sha256 = reader.GetString(6),
                    Status = reader.GetString(7),
                    CreatedAt = ParseDate(reader.GetString(8)) ?? default,
                    SentAt = ParseDate(reader.GetString(9)),
                    PurgeAfter = ParseDate(reader.GetString(10)),
                    Error = reader.GetString(11)
                });
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[NewToys][backup] 投递记录读取失败：" + ex.Message); }
        return list;
    }

    public MailJobRecord? FindJob(string indexNo)
    {
        if (string.IsNullOrWhiteSpace(indexNo)) return null;
        return RecentJobs(500).Find(j => string.Equals(j.IndexNo, indexNo, StringComparison.OrdinalIgnoreCase));
    }

    public List<MailPartRecord> ListParts(string indexNo)
    {
        var list = new List<MailPartRecord>();
        if (!_db.IsReady || string.IsNullOrWhiteSpace(indexNo)) return list;
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT index_no, part_no, file_name, size_bytes, sha256, message_id, local_path, sent_at, purged " +
                "FROM mail_part WHERE index_no = $no ORDER BY part_no;";
            cmd.Parameters.AddWithValue("$no", indexNo);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new MailPartRecord
                {
                    IndexNo = reader.GetString(0),
                    PartNo = reader.GetInt32(1),
                    FileName = reader.GetString(2),
                    SizeBytes = reader.GetInt64(3),
                    Sha256 = reader.GetString(4),
                    MessageId = reader.GetString(5),
                    LocalPath = reader.GetString(6),
                    SentAt = ParseDate(reader.GetString(7)),
                    Purged = reader.GetInt32(8) != 0
                });
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[NewToys][backup] 分卷读取失败：" + ex.Message); }
        return list;
    }

    /// <summary>缓冲期已到、可以清本地分卷的投递（只含 <c>sent</c>）。</summary>
    public List<MailJobRecord> DueForPurge(DateTime now)
        => RecentJobs(500).FindAll(j => j.Status == "sent" && j.PurgeAfter != null && j.PurgeAfter.Value <= now);

    private static DateTime? ParseDate(string text)
        => DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : null;
}
