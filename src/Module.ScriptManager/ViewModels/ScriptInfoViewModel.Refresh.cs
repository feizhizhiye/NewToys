using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.ViewModels;

public partial class ScriptInfoViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    /// <summary>🔄 全部刷新（当前集合里全部未折叠的节）。</summary>
    [RelayCommand]
    private async Task RefreshAllAsync() => await RefreshAllCoreAsync(autoOnly: false);

    private async Task RefreshAllCoreAsync(bool autoOnly)
    {
        if (IsBusy || _collector == null) return;

        // 手动「全部刷新」= 用户明确要数据 ⇒ 连折叠的节也刷；自动刷新只跑未折叠的（§三.9 硬约束 5）。
        var targets = autoOnly ? AutoTargets() : Sections.ToList();
        if (targets.Count == 0) { UpdateFooter(); return; }

        await RefreshSectionsAsync(targets).ConfigureAwait(true);
    }

    /// <summary>定时 / 进页面刷新时能跑的节：未折叠 + 只读 + 开关打开 + 间隔到期（§三.9）。</summary>
    private List<InfoSectionViewModel> AutoTargets()
    {
        var now = DateTime.UtcNow;
        var list = new List<InfoSectionViewModel>();

        foreach (var s in Sections)
        {
            if (s.IsCollapsed) continue;
            if (!CollectorService.CanAutoRefresh(s.Item) || !s.Item.Auto) continue;

            var interval = CollectorService.ClampInterval(s.Item.IntervalSeconds);
            if (interval < CollectorService.MinIntervalSeconds) continue;
            if (_lastRun.TryGetValue(s.ItemId, out var last) && (now - last).TotalSeconds < interval) continue;

            list.Add(s);
        }
        return list;
    }

    /// <summary>跑一批节（并发上限 4；同一节正在跑就跳过、不排队）。</summary>
    private async Task RefreshSectionsAsync(IReadOnlyList<InfoSectionViewModel> targets)
    {
        if (_collector == null || targets.Count == 0) return;

        IsBusy = true;
        var sw = Stopwatch.StartNew();
        _columnsDirty = false;
        _storageWarning = "";

        try
        {
            var tasks = new List<Task>();
            foreach (var s in targets)
            {
                if (!s.IsRefreshing) tasks.Add(RunSectionAsync(s));
            }
            if (tasks.Count > 0) await Task.WhenAll(tasks).ConfigureAwait(true);
        }
        finally
        {
            sw.Stop();
            IsBusy = false;

            if (_columnsDirty) _store?.Save();
            foreach (var s in Sections) s.RebuildHeaders();
            SelectedRow ??= Sections.FirstOrDefault(x => x.Selected != null)?.Selected;

            UpdateFooter(sw.ElapsedMilliseconds);
            OnPropertyChanged(nameof(HasSections));

            if (_storageWarning.Length > 0) StatusText = "⚠ " + _storageWarning;
        }
    }

    private async Task RunSectionAsync(InfoSectionViewModel section)
    {
        section.IsRefreshing = true;
        try
        {
            await _gate.WaitAsync().ConfigureAwait(true);
            try
            {
                var outcome = await _collector!.CollectAsync(section.Item, CancellationToken.None).ConfigureAwait(true);
                if (outcome.Canceled) return;

                if (outcome.StorageError is { Length: > 0 }) _storageWarning = outcome.StorageError;
                if (outcome.ColumnsChanged) _columnsDirty = true;

                if (outcome.Rows != null)
                {
                    // 列 schema 变了（脚本吐出新的 key）⇒ 重新投影本节，让新列按视图规则出现。
                    if (outcome.ColumnsChanged)
                        section.ApplyColumns(ViewColumnProjection.Resolve(section.Item, _view));
                    else
                        section.RebuildHeaders();

                    section.SetRows(outcome.Rows.Select(r => (r.RowKey, r.Status, ToDict(r.Attrs))));
                    section.MarkOk(outcome.Status, DateTime.Now);
                }
                else
                {
                    section.MarkError(outcome.Error ?? "采集失败");
                }

                _lastRun[section.ItemId] = DateTime.UtcNow;
            }
            finally { _gate.Release(); }
        }
        catch (Exception ex)
        {
            section.MarkError(ex.Message);
        }
        finally
        {
            section.IsRefreshing = false;
        }
    }

    private static IReadOnlyDictionary<string, string> ToDict(IReadOnlyList<KeyValuePair<string, string>> attrs)
    {
        var map = new Dictionary<string, string>(attrs.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in attrs) map[kv.Key] = kv.Value;
        return map;
    }

    /// <summary>局部刷新一个节（节头 ⟳ / 右键）。</summary>
    [RelayCommand]
    private async Task RefreshSectionAsync(InfoSectionViewModel? section)
    {
        if (section == null) return;
        await RefreshSectionsAsync(new[] { section });
    }

    // ==================== 执行频率（§三.9 三层） ====================

    /// <summary>生效的刷新方式：视图覆盖优先，其次集合。</summary>
    private string EffectiveMode()
    {
        var viewMode = ScriptRefreshMode.NormalizeOverride(_view?.RefreshMode);
        if (viewMode.Length > 0) return viewMode;

        var col = CurrentCollection();
        return ScriptRefreshMode.Normalize(col?.RefreshMode);
    }

    /// <summary>生效的刷新间隔（秒）：视图覆盖优先，其次集合，最小 2 秒。</summary>
    private int EffectiveSeconds()
    {
        var viewSeconds = _view?.RefreshSeconds ?? 0;
        var col = CurrentCollection();
        var seconds = viewSeconds > 0 ? viewSeconds : col?.RefreshSeconds ?? 0;
        return Math.Max(seconds, CollectorService.MinIntervalSeconds);
    }

    private ScriptCollection? CurrentCollection()
        => _store?.FindCollection(SelectedCollection?.Id ?? "");

    /// <summary>页面进入可视树时调（切走即停表，§三.9 硬约束 4）。</summary>
    public void StartAutoRefresh()
    {
        if (_timer != null) return;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        if (EffectiveMode() != ScriptRefreshMode.Manual) _ = RefreshAllCoreAsync(autoOnly: true);
    }

    /// <summary>页面离开可视树时调（暂停定时器；在跑的采集不强杀）。</summary>
    public void StopAutoRefresh()
    {
        if (_timer is null) return;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _timer = null;
    }

    private void OnTimerTick(object? sender, EventArgs e) => _ = TickAsync();

    /// <summary>1s tick：只有 <c>timer</c> 模式才动，到期后跑「未折叠 + 自动白名单」的节。</summary>
    private async Task TickAsync()
    {
        if (IsBusy || Sections.Count == 0) return;
        if (EffectiveMode() != ScriptRefreshMode.Timer) return;

        var seconds = EffectiveSeconds();
        if (_lastBatchAt != DateTime.MinValue && (DateTime.UtcNow - _lastBatchAt).TotalSeconds < seconds) return;

        await RefreshAllCoreAsync(autoOnly: true).ConfigureAwait(true);
    }

    private DateTime _lastBatchAt = DateTime.MinValue;

    private void UpdateFooter(long elapsedMs = -1)
    {
        if (elapsedMs >= 0) _lastBatchAt = DateTime.UtcNow;

        var rows = Sections.Sum(s => s.AllRows.Count);
        if (Sections.Count == 0) { FooterSummary = "分节模式 · 无采集器"; return; }

        var prefix = elapsedMs >= 0 ? "本次 " + InfoRowFactory.DurationText(elapsedMs) : "上次结果";
        FooterSummary = $"{prefix} · {Sections.Count} 节 · {rows} 行（分节模式不分页）";
    }

    // ==================== 空态 ====================

    private void UpdateEmpty()
    {
        if (Collections.Count == 0)
        {
            EmptyMessage = "还没有集合。集合 = 一组采集器，信息面板一次显示一个集合。";
            EmptyActionText = "🗂 去创建集合";
        }
        else if (Sections.Count == 0)
        {
            EmptyMessage = "这个集合还没有采集器。到「集合」页勾选条目，或在脚本库「🔍 扫描目录」入库。";
            EmptyActionText = "🗂 去添加";
        }
        else
        {
            EmptyMessage = "";
            EmptyActionText = "";
        }

        OnPropertyChanged(nameof(HasSections));
        OnPropertyChanged(nameof(HasEmptyAction));
    }

    /// <summary>空态按钮：都跳到「集合」页（唯一的成员编排入口）。</summary>
    [RelayCommand]
    private void OpenEmptyAction()
    {
        if (_nav == null) { StatusText = "⚠ 导航服务不可用"; return; }
        _nav.NavigateTo("script-collections");
    }

    // ==================== 节设置（解析器 / 超时 / 保留 / 自动刷新） ====================

    public ObservableCollection<InfoOption> ParserOptions { get; } = new(new[]
    {
        new InfoOption(ScriptParseMode.Json, "json（推荐）"),
        new InfoOption(ScriptParseMode.Line, "line（每行一个值）"),
        new InfoOption(ScriptParseMode.Csv, "csv（首行是属性名）"),
        new InfoOption(ScriptParseMode.Regex, "regex（本期等同 line）"),
    });

    public ObservableCollection<InfoOption> RetentionOptions { get; } = new(new[]
    {
        new InfoOption(ScriptRetentionMode.Latest, "latest（只留最新一次）"),
        new InfoOption(ScriptRetentionMode.Count, "count（保留最近 N 次）"),
        new InfoOption(ScriptRetentionMode.Days, "days（保留最近 N 天）"),
    });

    public ObservableCollection<InfoOption> CollectionRefreshOptions { get; } = new(new[]
    {
        new InfoOption(ScriptRefreshMode.Manual, "手动"),
        new InfoOption(ScriptRefreshMode.OnEnter, "进页面刷新"),
        new InfoOption(ScriptRefreshMode.Timer, "定时刷新"),
    });

    [ObservableProperty] private InfoOption? _settingsParser;
    [ObservableProperty] private string _settingsTimeout = "30";
    [ObservableProperty] private InfoOption? _settingsRetention;
    [ObservableProperty] private string _settingsRetentionN = "0";
    [ObservableProperty] private bool _settingsAuto;
    [ObservableProperty] private string _settingsInterval = "0";

    /// <summary>集合级刷新方式（下拉，改动即落盘）。</summary>
    [ObservableProperty] private InfoOption? _collectionRefresh;
    [ObservableProperty] private string _collectionRefreshSeconds = "0";

    /// <summary>只读条目才允许自动刷新（硬约束 1），否则下拉灰掉并给原因。</summary>
    public bool CanAuto => SettingsSection?.Item.Readonly == true;

    public string AutoHint => CanAuto
        ? "只有只读采集器能自动刷新（有副作用的条目不排期）。间隔 2~3600 秒。"
        : "本采集器不是只读条目 ⇒ 自动刷新不可用。";

    /// <summary>保留策略提示：latest 不支持趋势（§三.9 硬约束 6）。</summary>
    public string RetentionHint => SettingsSection?.Item.Readonly == true
        ? "只读采集器强制 latest（不保留历史 ⇒ 不提供趋势）。"
        : "latest = 只留最新一批；count / days 才留历史（本期不做趋势图，供后续使用）。";

}
