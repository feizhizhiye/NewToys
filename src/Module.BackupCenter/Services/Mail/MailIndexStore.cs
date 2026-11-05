using System;
using System.Collections.Generic;
using System.Globalization;
using NewToys.BackupCenter.Models;

namespace NewToys.BackupCenter.Services.Mail;

/// <summary>服务器邮件头的本地缓存（mail_index）。（WHY: BC-010）</summary>
public sealed class MailIndexStore
{
    private readonly BackupDb _db;

    public MailIndexStore(BackupDb db) => _db = db;

    /// <summary>某个邮箱夹已缓存的最大 UID（0 = 还没同步过）。</summary>
    public uint MaxUid(string mailbox)
    {
        if (!_db.IsReady) return 0;
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(MAX(uid), 0) FROM mail_index WHERE mailbox = $m;";
            cmd.Parameters.AddWithValue("$m", mailbox);
            return Convert.ToUInt32(cmd.ExecuteScalar() ?? 0u);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][backup] 邮箱缓存读 UID 失败：" + ex.Message);
            return 0;
        }
    }

    /// <summary>批量写入 / 覆盖邮件头（按 <c>mailbox + uid</c> 唯一）。</summary>
    public int Upsert(IEnumerable<MailListItem> items)
    {
        if (!_db.IsReady) return 0;
        var count = 0;
        try
        {
            using var conn = _db.Open();
            using var tx = conn.BeginTransaction();
            foreach (var m in items)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    "INSERT INTO mail_index (mailbox, uid, subject, from_addr, date_utc, size_bytes," +
                    "  attach_count, index_no, is_seen, synced_at) " +
                    "VALUES ($box, $uid, $subj, $from, $date, $size, $att, $no, $seen, $at) " +
                    "ON CONFLICT(mailbox, uid) DO UPDATE SET " +
                    "  subject = $subj, from_addr = $from, date_utc = $date, size_bytes = $size," +
                    "  attach_count = $att, index_no = $no, is_seen = $seen, synced_at = $at;";
                cmd.Parameters.AddWithValue("$box", m.Mailbox);
                cmd.Parameters.AddWithValue("$uid", (long)m.Uid);
                cmd.Parameters.AddWithValue("$subj", m.Subject ?? "");
                cmd.Parameters.AddWithValue("$from", m.From ?? "");
                cmd.Parameters.AddWithValue("$date", m.Date.ToString("o"));
                cmd.Parameters.AddWithValue("$size", m.SizeBytes);
                cmd.Parameters.AddWithValue("$att", m.AttachCount);
                cmd.Parameters.AddWithValue("$no", m.IndexNo ?? "");
                cmd.Parameters.AddWithValue("$seen", m.IsSeen ? 1 : 0);
                cmd.Parameters.AddWithValue("$at", DateTime.Now.ToString("o"));
                count += cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][backup] 邮箱缓存写入失败：" + ex.Message);
            return 0;
        }
        return count;
    }

    /// <summary>
    /// 本地搜索：主题 / 发件人 / 索引号 / 附件名（附件名只在备份邮件里有，靠索引号命中）。
    /// <paramref name="from"/> / <paramref name="to"/> 为本地日期闭区间（null = 不限）。
    /// </summary>
    public List<MailListItem> Query(string keyword = "", DateTime? from = null, DateTime? to = null, int limit = 500)
    {
        var list = new List<MailListItem>();
        if (!_db.IsReady) return list;
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();

            var where = new List<string>();
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                where.Add("(subject LIKE $kw OR from_addr LIKE $kw OR index_no LIKE $kw)");
                cmd.Parameters.AddWithValue("$kw", "%" + keyword.Trim() + "%");
            }
            if (from != null) { where.Add("date_utc >= $from"); cmd.Parameters.AddWithValue("$from", from.Value.Date.ToString("o")); }
            if (to != null) { where.Add("date_utc < $to"); cmd.Parameters.AddWithValue("$to", to.Value.Date.AddDays(1).ToString("o")); }

            cmd.CommandText =
                "SELECT mailbox, uid, subject, from_addr, date_utc, size_bytes, attach_count, index_no, is_seen " +
                "FROM mail_index " +
                (where.Count > 0 ? "WHERE " + string.Join(" AND ", where) + " " : "") +
                "ORDER BY date_utc DESC LIMIT $limit;";
            cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 2000));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new MailListItem
                {
                    Mailbox = reader.GetString(0),
                    Uid = (uint)reader.GetInt64(1),
                    Subject = reader.GetString(2),
                    From = reader.GetString(3),
                    Date = DateTime.TryParse(reader.GetString(4), CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var d) ? d.ToLocalTime() : default,
                    SizeBytes = reader.GetInt64(5),
                    AttachCount = reader.GetInt32(6),
                    IndexNo = reader.GetString(7),
                    IsSeen = reader.GetInt32(8) != 0
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][backup] 邮箱缓存查询失败：" + ex.Message);
        }
        return list;
    }

    /// <summary>服务器上已删除的邮件，从缓存里移除。</summary>
    public void Remove(string mailbox, uint uid)
    {
        if (!_db.IsReady) return;
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM mail_index WHERE mailbox = $m AND uid = $u;";
            cmd.Parameters.AddWithValue("$m", mailbox);
            cmd.Parameters.AddWithValue("$u", (long)uid);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[NewToys][backup] 邮箱缓存删除失败：" + ex.Message); }
    }

    /// <summary>缓存条数（页面显示「本地已缓存 N 封」）。</summary>
    public int Count()
    {
        if (!_db.IsReady) return 0;
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM mail_index;";
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }
        catch { return 0; }
    }
}
