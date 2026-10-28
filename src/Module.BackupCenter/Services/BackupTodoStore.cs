using System;
using System.Collections.Generic;
using NewToys.BackupCenter.Models;

namespace NewToys.BackupCenter.Services;

/// <summary>
/// 备份日历的待办（SQLite <c>backup.db</c> 的 <c>todos</c> 表）。
/// <para>
/// 只是「哪天打算备份」的备忘，⛔ 不触发自动备份（定时执行属 v2）；
/// 勾完成 / 取消完成就地改 <c>done_at</c>。
/// </para>
/// </summary>
public sealed class BackupTodoStore
{
    private readonly BackupDb _db;

    public BackupTodoStore(BackupDb db) => _db = db;

    /// <summary>全部待办（按日期升序）。</summary>
    public List<BackupTodo> ReadAll()
    {
        var list = new List<BackupTodo>();
        if (!_db.IsReady) return list;
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT id, plan_id, due_date, title, note, done_at FROM todos ORDER BY due_date, id;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new BackupTodo
                {
                    Id = reader.GetInt64(0),
                    PlanId = reader.GetString(1),
                    DueDate = DateTime.TryParse(reader.GetString(2), out var d) ? d.Date : default,
                    Title = reader.GetString(3),
                    Note = reader.GetString(4),
                    DoneAt = DateTime.TryParse(reader.GetString(5), out var done) ? done : null
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][backup] 待办读取失败：" + ex.Message);
        }
        return list;
    }

    /// <summary>某一天的待办（日期精确到天）。</summary>
    public List<BackupTodo> OnDate(DateTime day)
        => ReadAll().FindAll(t => t.DueDate.Date == day.Date);

    /// <summary>新增一条待办，返回自增 Id（写不进去 ⇒ 0）。</summary>
    public long Add(DateTime dueDate, string title, string planId, string note = "")
    {
        if (!_db.IsReady) return 0;
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO todos (plan_id, due_date, title, note, done_at) " +
                "VALUES ($pid, $due, $title, $note, '') RETURNING id;";
            cmd.Parameters.AddWithValue("$pid", planId ?? "");
            cmd.Parameters.AddWithValue("$due", dueDate.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$title", title ?? "");
            cmd.Parameters.AddWithValue("$note", note ?? "");
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][backup] 待办写入失败：" + ex.Message);
            return 0;
        }
    }

    /// <summary>勾完成 / 取消完成（<paramref name="done"/> = false 时清空完成时间）。</summary>
    public bool SetDone(long id, bool done)
    {
        if (!_db.IsReady || id <= 0) return false;
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE todos SET done_at = $at WHERE id = $id;";
            cmd.Parameters.AddWithValue("$at", done ? DateTime.Now.ToString("o") : "");
            cmd.Parameters.AddWithValue("$id", id);
            return cmd.ExecuteNonQuery() > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][backup] 待办状态写入失败：" + ex.Message);
            return false;
        }
    }

    public bool Remove(long id)
    {
        if (!_db.IsReady || id <= 0) return false;
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM todos WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            return cmd.ExecuteNonQuery() > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][backup] 待办删除失败：" + ex.Message);
            return false;
        }
    }
}
