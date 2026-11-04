using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.BackupCenter.Models;
using NewToys.BackupCenter.Services;

namespace NewToys.BackupCenter.ViewModels;

/// <summary>
/// 「备份日历」页（<c>backup-calendar</c>）：月视图把「哪天备份过」和「哪天打算备份」画在同一张格子里。
/// <para>
/// 左 = 6×7 月历（格子显示备份数 / 待办数，失败的那天标红）；右 = 选中那天的明细 + 待办勾选。
/// 数据全部来自 SQLite（<c>archives</c> 看历史产物、<c>runs</c> 看失败、<c>todos</c> 看待办），⛔ 不再读 JSON。
/// </para>
/// <para>
/// ⚠️ 待办只是备忘，⛔ 不会自动触发备份（定时任务属 v2）；做完请到「备份任务」页手动点「立即备份」。
/// </para>
/// </summary>
public partial class BackupCalendarViewModel : ObservableObject
{
    private static readonly string[] WeekHeads = { "一", "二", "三", "四", "五", "六", "日" };

    private readonly BackupRunLog _runLog;
    private readonly ArchiveCatalog _catalog;
    private readonly BackupPlanStore _plans;
    private readonly BackupTodoStore _todos;
    private readonly BackupSettingsService _settings;

    private DateTime _month;

    [ObservableProperty] private string _monthTitle = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private CalendarDay? _selectedDay;
    [ObservableProperty] private string _selectedDateText = "";
    [ObservableProperty] private string _newTodoTitle = "";

    public BackupCalendarViewModel(
        BackupRunLog runLog,
        ArchiveCatalog catalog,
        BackupPlanStore plans,
        BackupTodoStore todos,
        BackupSettingsService settings)
    {
        _runLog = runLog;
        _catalog = catalog;
        _plans = plans;
        _todos = todos;
        _settings = settings;

        var today = DateTime.Today;
        _month = new DateTime(today.Year, today.Month, 1);
        BuildMonth();
        SelectedDay = Days.FirstOrDefault(d => d.Date == today) ?? Days.FirstOrDefault();
        StatusText = "共" + _catalog.Items.Count + " 个归档包 · "
                     + _runLog.Items.Count + " 条运行记录 · "
                     + _todos.ReadAll().Count + " 条待办";
    }

    /// <summary>表头（周一开始）。</summary>
    public IReadOnlyList<string> WeekHeadList => WeekHeads;

    /// <summary>月历格子（固定 42 格 = 6 行 × 7 列）。</summary>
    public ObservableCollection<CalendarDay> Days { get; } = new();

    /// <summary>选中那天的归档包（按时间倒序）。</summary>
    public ObservableCollection<ArchiveRecord> DayArchives { get; } = new();

    /// <summary>选中那天的运行记录（含失败原因）。</summary>
    public ObservableCollection<BackupRunRecord> DayRuns { get; } = new();

    /// <summary>选中那天的待办。</summary>
    public ObservableCollection<BackupTodo> DayTodos { get; } = new();

    public bool HasDayArchives => DayArchives.Count > 0;

    public bool HasDayRuns => DayRuns.Count > 0;

    public bool HasDayTodos => DayTodos.Count > 0;

    public string BackupDir => _settings.ArchiveDir;

    // ==================== 月份导航 ====================

    [RelayCommand]
    private void PrevMonth() => Shift(-1);

    [RelayCommand]
    private void NextMonth() => Shift(1);

    [RelayCommand]
    private void GoToday()
    {
        var today = DateTime.Today;
        _month = new DateTime(today.Year, today.Month, 1);
        BuildMonth();
        SelectedDay = Days.FirstOrDefault(d => d.Date == today);
        StatusText = "已回到本月（" + today.ToString("yyyy-MM-dd") + "）";
    }

    /// <summary>重新读库并重画（在别的页备份完回来点一下）。</summary>
    [RelayCommand]
    private void Refresh()
    {
        _catalog.Load();
        _runLog.Load();
        _plans.Load();
        BuildMonth();
        ReloadDay();
        StatusText = "已重新读取：" + _catalog.Items.Count + " 个包 · " + _runLog.Items.Count + " 条记录";
    }

    private void Shift(int months)
    {
        SelectedDay = null;                  // 翻月要换选中日，⛔ 别让 BuildMonth 把旧日期选回来
        _month = _month.AddMonths(months);
        BuildMonth();
        SelectedDay = Days.FirstOrDefault(d => d.IsCurrentMonth && d.Date.Day == 1);
    }

    // ==================== 待办 ====================

    [RelayCommand]
    private void AddTodo()
    {
        var title = (NewTodoTitle ?? "").Trim();
        if (title.Length == 0) { StatusText = "⚠ 先写一句待办内容（如「备份照片」）。"; return; }
        if (SelectedDay == null) { StatusText = "⚠ 先在左边点一天。"; return; }

        var id = _todos.Add(SelectedDay.Date, title, "", "");
        if (id <= 0) { StatusText = "⚠ 写入失败（数据库不可用，去设置页看「文件位置」）。"; return; }

        NewTodoTitle = "";
        BuildMonth();        // 格子上的「待办 N」要跟着变
        ReloadDay();
        StatusText = "已加待办：" + title + " · " + SelectedDay.Date.ToString("yyyy-MM-dd");
    }

    [RelayCommand]
    private void ToggleTodo(BackupTodo? todo)
    {
        if (todo == null) return;
        var wasDone = todo.IsDone;                       // 对象不重新加载，翻状态后的值不会自己更新
        if (!_todos.SetDone(todo.Id, !wasDone)) { StatusText = "⚠ 更新失败。"; return; }
        ReloadDay();
        StatusText = (wasDone ? "已取消完成：" : "已完成：") + todo.Title;
    }

    [RelayCommand]
    private void DeleteTodo(BackupTodo? todo)
    {
        if (todo == null) return;
        if (!_todos.Remove(todo.Id)) { StatusText = "⚠ 删除失败。"; return; }
        BuildMonth();
        ReloadDay();
        StatusText = "已删除待办：" + todo.Title;
    }

    // ==================== 打开目录 ====================

    [RelayCommand]
    private void OpenBackupDir()
    {
        try
        {
            Directory.CreateDirectory(BackupDir);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + BackupDir + "\"") { UseShellExecute = true });
            StatusText = "已打开默认备份目录：" + BackupDir;
        }
        catch (Exception ex) { StatusText = "⚠ 打不开目录：" + ex.Message; }
    }

    // ==================== 构建 ====================

    /// <summary>重画 42 格（格子数量小，整体重建最省心）。</summary>
    private void BuildMonth()
    {
        // 清空 Days 时 ListBox 会把 SelectedItem 置 null ⇒ 先记住日期，填完再选回去，
        // 否则加完待办 / 刷新之后右栏会莫名其妙变空。
        var keep = SelectedDay?.Date;

        MonthTitle = _month.ToString("yyyy 年 M 月");

        var counts = CountByDay(_catalog.Items.Select(a => a.CreatedAt));
        var fails = CountByDay(_runLog.Items
            .Where(r => (r.Status ?? "").Contains("失败", StringComparison.Ordinal))
            .Select(r => r.StartedAt));
        var todoCounts = _todos.ReadAll()
            .GroupBy(t => t.DueDate.Date)
            .ToDictionary(g => g.Key, g => g.Count());

        var offset = ((int)_month.DayOfWeek + 6) % 7;      // 周一 = 0
        var start = _month.AddDays(-offset);

        Days.Clear();
        for (var i = 0; i < 42; i++)
        {
            var date = start.AddDays(i);
            Days.Add(new CalendarDay
            {
                Date = date,
                IsCurrentMonth = date.Month == _month.Month && date.Year == _month.Year,
                IsToday = date == DateTime.Today,
                BackupCount = counts.GetValueOrDefault(date),
                FailedCount = fails.GetValueOrDefault(date),
                TodoCount = todoCounts.GetValueOrDefault(date)
            });
        }

        if (keep != null) SelectedDay = Days.FirstOrDefault(d => d.Date == keep.Value);
    }

    private static Dictionary<DateTime, int> CountByDay(IEnumerable<DateTime> items)
    {
        var map = new Dictionary<DateTime, int>();
        foreach (var at in items)
        {
            if (at == default) continue;
            var day = at.Date;
            map[day] = map.GetValueOrDefault(day) + 1;
        }
        return map;
    }

    partial void OnSelectedDayChanged(CalendarDay? value)
    {
        SelectedDateText = value == null ? "未选日期" : value.Date.ToString("yyyy年M月d日 dddd");
        ReloadDay();
    }

    /// <summary>重建右栏三块（当天包 / 当天记录 / 当天待办）。</summary>
    private void ReloadDay()
    {
        DayArchives.Clear();
        DayRuns.Clear();
        DayTodos.Clear();

        if (SelectedDay != null)
        {
            var day = SelectedDay.Date.Date;

            foreach (var a in _catalog.Items.Where(a => a.CreatedAt.Date == day).OrderByDescending(a => a.CreatedAt))
                DayArchives.Add(a);

            foreach (var r in _runLog.Items.Where(r => r.StartedAt.Date == day).OrderByDescending(r => r.StartedAt))
                DayRuns.Add(r);

            var planNames = _plans.Items.ToDictionary(p => p.Id, p => p.Name);
            foreach (var t in _todos.OnDate(day))
            {
                t.PlanName = t.PlanId.Length > 0 && planNames.TryGetValue(t.PlanId, out var n) ? n : "";
                DayTodos.Add(t);
            }
        }

        OnPropertyChanged(nameof(HasDayArchives));
        OnPropertyChanged(nameof(HasDayRuns));
        OnPropertyChanged(nameof(HasDayTodos));
    }
}
