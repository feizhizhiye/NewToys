using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.BackupCenter.Models;
using NewToys.BackupCenter.Services;
using NewToys.Core.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.BackupCenter.ViewModels;

/// <summary>任务页的一行（把任务配置 + 运行摘要摊平成列表 / 详情卡要的字段）。</summary>
public sealed class PlanRow
{
    public PlanRow(BackupPlan plan, int archiveCount, BackupRunRecord? last, bool hasEntry, string fingerprint)
    {
        Plan = plan;
        ArchiveCount = archiveCount;
        LastRunAt = last?.StartedAt;
        LastRunText = last == null ? "还没跑过" : $"{last.StartedAt:yyyy-MM-dd HH:mm} · {last.Status}";
        HasPassword = hasEntry;
        PasswordText = !hasEntry ? "未生成"
            : string.IsNullOrEmpty(fingerprint) ? "已生成（密码库未解锁）"
            : "已生成 · 指纹 " + fingerprint;
    }

    public BackupPlan Plan { get; }

    public int ArchiveCount { get; }

    public bool HasPassword { get; }

    public DateTime? LastRunAt { get; }

    public string Name => Plan.Name;

    public string SourcesText => Plan.Sources.Count == 0 ? "（未指定）" : string.Join("；", Plan.Sources);

    public string SourceCountText => Plan.Sources.Count + " 个";

    public string KeepText => Plan.KeepCount + " 份";

    public string ArchiveCountText => ArchiveCount + " 个";

    public string LastRunText { get; }

    public string PasswordText { get; }
}

/// <summary>
/// 「备份中心 → 备份任务」页（<c>backup-plans</c>）：任务清单 + 工具条 + 详情卡 + 编辑弹层。
/// <para>
/// 立即备份的完整链路 = 探测 7z → 从密码库取包密码（没有就现场生成并入库）→ 7z 建包 → 登记包清单 → 按份数轮转。
/// 密码库未设置 / 未解锁时一律拒跑并给出方向（⛔ 不偷偷用空密码建包）。
/// </para>
/// </summary>
public partial class BackupPlansViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    private readonly BackupPlanStore _store = null!;
    private readonly ArchiveCatalog _catalog = null!;
    private readonly BackupRunLog _runs = null!;
    private readonly VaultService _vault = null!;
    private readonly BackupSettingsService _settings = null!;
    private readonly ArchiveService _archives = null!;
    private readonly SevenZipLocator _locator = null!;

    private readonly List<PlanRow> _all = new();
    private BackupPlan? _editing;
    private CancellationTokenSource? _runCts;

    [ObservableProperty] private PlanRow? _selected;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _busyPhase = "";

    [ObservableProperty] private bool _isEditorOpen;
    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private string _editNote = "";
    [ObservableProperty] private string _editExcludes = "";
    [ObservableProperty] private string _editKeepText = "5";
    [ObservableProperty] private string _editorSourceSelected = "";

    [ObservableProperty] private string _nameHeader = "任务";
    [ObservableProperty] private string _sourcesHeader = "源目录";
    [ObservableProperty] private string _keepHeader = "保留";
    [ObservableProperty] private string _lastRunHeader = "最近运行";
    [ObservableProperty] private string _archivesHeader = "包数";
    [ObservableProperty] private string _passwordHeader = "包密码";

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public BackupPlansViewModel() : base(null, null) { }

    public BackupPlansViewModel(
        BackupPlanStore store,
        ArchiveCatalog catalog,
        BackupRunLog runs,
        VaultService vault,
        BackupSettingsService settings,
        ArchiveService archives,
        SevenZipLocator locator,
        IColumnLayoutService? cols = null,
        IOptionsStore? opts = null)
        : base(cols, opts)
    {
        _store = store;
        _catalog = catalog;
        _runs = runs;
        _vault = vault;
        _settings = settings;
        _archives = archives;
        _locator = locator;
        LoadLayout();
        Reload();
    }

    public ObservableCollection<PlanRow> Items { get; } = new();

    /// <summary>编辑弹层里的源目录（可增删；保存时才写回任务）。</summary>
    public ObservableCollection<string> EditorSources { get; } = new();

    /// <summary>详情卡「源目录」列表。</summary>
    public ObservableCollection<TextLine> SourceLines { get; } = new();

    /// <summary>详情卡「最近运行」列表。</summary>
    public ObservableCollection<TextLine> RunLines { get; } = new();

    public bool HasItems => Items.Count > 0;

    public bool IsNotBusy => !IsBusy;

    public bool HasSelection => Selected is not null;

    public bool ShowDetailBody => Selected is not null;

    public string ArchiveDir => _settings?.ArchiveDir ?? "";

    public string VaultStateText => _vault is null
        ? ""
        : !_vault.IsInitialized ? "还没有主密码（到「备份中心设置」设置一次）"
        : _vault.IsUnlocked ? "密码库已解锁"
        : _vault.IsLockedOut ? "⚠ 解锁失败次数过多，本会话已锁库"
        : "密码库未解锁";

    // ==================== 列显隐直通 ====================

    public bool IsNameVisible => Vis("Name");
    public bool IsSourcesVisible => Vis("Sources");
    public bool IsKeepVisible => Vis("Keep");
    public bool IsLastRunVisible => Vis("LastRun");
    public bool IsArchivesVisible => Vis("Archives");
    public bool IsPasswordVisible => Vis("Password");

    protected override string PageKey => "backup-plans";

    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Name", "任务", "220", sample: "工作文档每日备份"),
        new("Sources", "源目录", "*", sample: @"D:\文档；E:\图片"),
        new("Keep", "保留", "80", sample: "5 份"),
        new("LastRun", "最近运行", "170", sample: "2026-10-28 20:00 · 成功"),
        new("Archives", "包数", "80", sample: "12 个"),
        new("Password", "包密码", "150", sample: "已生成 · 指纹 3f9a21bc"),
    };

    protected override void RefreshView() => ApplyFilter();

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Items.Count > 0) Selected = Items[0];
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    partial void OnSelectedChanged(PlanRow? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowDetailBody));
        OnPropertyChanged(nameof(DetailName));
        OnPropertyChanged(nameof(DetailSources));
        OnPropertyChanged(nameof(DetailKeepText));
        OnPropertyChanged(nameof(DetailNote));
        OnPropertyChanged(nameof(DetailPasswordText));
        OnPropertyChanged(nameof(DetailLastRunText));
        RefreshDetailLists();
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnIsEditorOpenChanged(bool value) => OnPropertyChanged(nameof(EditorTitle));

    // ==================== 命令 ====================

    [RelayCommand]
    private void Reload()
    {
        var keepId = Selected?.Plan.Id;

        _all.Clear();
        if (_store is not null)
        {
            foreach (var plan in _store.Items)
            {
                var count = _catalog.Items.Count(r => r.PlanId == plan.Id || r.PlanName == plan.Name);
                var last = _runs.Recent(plan.Id, 1).FirstOrDefault();
                var fingerprint = _vault is { IsUnlocked: true } ? PasswordGenerator.Fingerprint(_vault.Get(plan.Id)) : "";
                _all.Add(new PlanRow(plan, count, last, _vault?.HasEntry(plan.Id) ?? false, fingerprint));
            }
        }

        ApplyFilter();
        if (keepId != null) Selected = Items.FirstOrDefault(r => r.Plan.Id == keepId);
        OnPropertyChanged(nameof(VaultStateText));
        OnPropertyChanged(nameof(ArchiveDir));
        StatusText = $"共 {_all.Count} 个任务 · 包目录 {ArchiveDir}";
    }

    [RelayCommand]
    private void NewPlan()
    {
        _editing = null;
        EditName = "";
        EditNote = "";
        EditExcludes = "";
        EditKeepText = (_settings?.DefaultKeepCount ?? 5).ToString();
        EditorSources.Clear();
        IsEditorOpen = true;
        StatusText = "新建任务：填名称、加源目录，然后「💾 保存」。";
    }

    [RelayCommand]
    private void EditPlan()
    {
        var row = Selected;
        if (row is null) { StatusText = "先选中一个任务。"; return; }
        _editing = row.Plan;
        EditName = row.Plan.Name;
        EditNote = row.Plan.Note;
        EditExcludes = row.Plan.Excludes;
        EditKeepText = row.Plan.KeepCount.ToString();
        EditorSources.Clear();
        foreach (var s in row.Plan.Sources) EditorSources.Add(s);
        IsEditorOpen = true;
    }

    [RelayCommand]
    private void CloseEditor()
    {
        IsEditorOpen = false;
        _editing = null;
    }

    [RelayCommand]
    private async Task AddSourceFolderAsync()
    {
        var picked = await DialogHooks.PickFolderAsync("选择要备份的目录").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(picked)) return;
        if (EditorSources.Any(s => string.Equals(s, picked, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText = "这个目录已经在清单里了。";
            return;
        }
        EditorSources.Add(picked);
    }

    [RelayCommand]
    private void RemoveSourceFolder(string? path)
    {
        var target = string.IsNullOrWhiteSpace(path) ? EditorSourceSelected : path;
        if (string.IsNullOrWhiteSpace(target)) { StatusText = "先选中要移除的源目录。"; return; }
        EditorSources.Remove(target);
    }

    [RelayCommand]
    private async Task SaveEditorAsync()
    {
        var name = (EditName ?? "").Trim();
        if (name.Length == 0) { StatusText = "⚠ 任务名不能为空。"; return; }
        if (EditorSources.Count == 0) { StatusText = "⚠ 至少要加一个源目录。"; return; }

        var keep = int.TryParse(EditKeepText, out var k) ? Math.Clamp(k, 1, 999) : 5;

        if (_editing is null)
        {
            var plan = _store!.Add(name);
            plan.Sources = EditorSources.ToList();
            plan.Excludes = EditExcludes ?? "";
            plan.Note = EditNote ?? "";
            plan.KeepCount = keep;
            _store.Save();
        }
        else
        {
            _editing.Name = name;
            _editing.Sources = EditorSources.ToList();
            _editing.Excludes = EditExcludes ?? "";
            _editing.Note = EditNote ?? "";
            _editing.KeepCount = keep;
            _store!.Save();
        }

        IsEditorOpen = false;
        _editing = null;
        Reload();
        StatusText = "已保存任务：" + name;
    }

    [RelayCommand]
    private async Task DeletePlanAsync()
    {
        var row = Selected;
        if (row is null) { StatusText = "先选中一个任务。"; return; }

        var ok = await DialogHooks.ConfirmAsync(
            $"删除任务「{row.Name}」？\n\n只删任务配置与它的包密码，已生成的归档包**不会**被删除。").ConfigureAwait(true);
        if (!ok) return;

        _store!.Remove(row.Plan);
        _vault?.Remove(row.Plan.Id);
        Selected = null;
        Reload();
        StatusText = "已删除任务：" + row.Name;
    }

    /// <summary>立即备份（全程可取消；失败原因分级提示）。</summary>
    [RelayCommand]
    private async Task RunBackupAsync()
    {
        var row = Selected;
        if (row is null || IsBusy) { if (row is null) StatusText = "先选中一个任务。"; return; }

        if (_vault is null || !_vault.IsInitialized)
        {
            StatusText = "⚠ 还没有主密码：到「备份中心设置 → 主密码」设置一次，再来备份。";
            return;
        }
        if (!_vault.IsUnlocked)
        {
            StatusText = _vault.IsLockedOut
                ? "⚠ 解锁失败次数过多，本会话已锁库（重启程序再试）。"
                : "⚠ 密码库未解锁：到「备份中心设置 → 主密码」解锁后再备份。";
            return;
        }

        var plan = row.Plan;
        if (plan.Sources.Count == 0) { StatusText = "⚠ 这个任务没有源目录。"; return; }

        IsBusy = true;
        BusyPhase = "探测 7z…";
        _runCts = new CancellationTokenSource();
        var started = DateTime.Now;
        try
        {
            var zip = await _locator.LocateAsync(force: true, _runCts.Token).ConfigureAwait(true);
            if (!zip.Found)
            {
                _runs!.Append(new BackupRunRecord
                {
                    PlanId = plan.Id, StartedAt = started, Status = "失败", Message = zip.Message
                });
                StatusText = "⚠ " + zip.Message;
                return;
            }

            var password = _vault.Get(plan.Id);
            if (string.IsNullOrEmpty(password))
            {
                password = PasswordGenerator.Generate();
                if (!_vault.Set(plan.Id, password))
                {
                    StatusText = "⚠ 包密码写不进密码库，已中止。";
                    return;
                }
            }

            BusyPhase = "收集源目录…";
            var file = Path.Combine(_settings!.ArchiveDir, SafeFileName(plan.Name) + "-" + started.ToString("yyyyMMdd-HHmmss") + ".7z");

            var progress = new Progress<string>(s => BusyPhase = s);
            var outcome = await _archives!.CreateAsync(zip.Path, plan, file, password, progress, _runCts.Token)
                .ConfigureAwait(true);

            if (outcome.Canceled)
            {
                _runs.Append(new BackupRunRecord { PlanId = plan.Id, StartedAt = started, Status = "已取消", Message = outcome.Message });
                StatusText = "已取消本次备份。";
                return;
            }

            if (!outcome.Ok)
            {
                _runs.Append(new BackupRunRecord { PlanId = plan.Id, StartedAt = started, Status = "失败", Message = outcome.Message });
                StatusText = "⚠ 备份失败：" + outcome.Message;
                return;
            }

            BusyPhase = "校验并登记…";
            var info = new FileInfo(file);
            // 整包 SHA256 可能几 GB：放后台线程算，⛔ 不卡 UI
            var sha = await Task.Run(() => ArchiveCatalog.ComputeSha256(file)).ConfigureAwait(true);
            var record = new ArchiveRecord
            {
                FileName = info.Name,
                PlanId = plan.Id,
                PlanName = plan.Name,
                SizeBytes = info.Length,
                CreatedAt = started,
                Sha256 = sha
            };
            _catalog!.Add(record);

            var pruned = Prune(plan);
            _runs.Append(new BackupRunRecord
            {
                PlanId = plan.Id, StartedAt = started, Status = "成功",
                ArchiveFile = info.Name, Message = outcome.Message, SkippedFiles = outcome.SkippedFiles
            });

            Reload();
            StatusText = $"✅ 已生成 {info.Name}（{ArchiveRecord.FormatSize(info.Length)}）"
                         + (pruned > 0 ? $"，按保留份数删了 {pruned} 个旧包" : "");
        }
        catch (OperationCanceledException)
        {
            _runs!.Append(new BackupRunRecord { PlanId = plan.Id, StartedAt = started, Status = "已取消", Message = "用户取消" });
            StatusText = "已取消本次备份。";
        }
        catch (Exception ex)
        {
            _runs!.Append(new BackupRunRecord { PlanId = plan.Id, StartedAt = started, Status = "失败", Message = ex.Message });
            StatusText = "⚠ 备份失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            BusyPhase = "";
            _runCts?.Dispose();
            _runCts = null;
        }
    }

    [RelayCommand]
    private void CancelRun() => _runCts?.Cancel();

    /// <summary>📋 复制选中任务的包密码（需解锁）。</summary>
    [RelayCommand]
    private async Task CopyPasswordAsync()
    {
        var row = Selected;
        if (row is null) { StatusText = "先选中一个任务。"; return; }
        var password = _vault?.Get(row.Plan.Id);
        if (string.IsNullOrEmpty(password))
        {
            StatusText = _vault is { IsUnlocked: false }
                ? "⚠ 密码库未解锁（到「备份中心设置 → 主密码」解锁）。"
                : "这个任务还没有包密码（第一次备份时自动生成）。";
            return;
        }
        await SetClipboardAsync(password).ConfigureAwait(true);
        StatusText = "已复制包密码（丢了这个密码，旧包就打不开了）。";
    }

    /// <summary>🔄 重生成包密码：旧包将无法用新密码打开，必须先确认。</summary>
    [RelayCommand]
    private async Task RegeneratePasswordAsync()
    {
        var row = Selected;
        if (row is null) { StatusText = "先选中一个任务。"; return; }
        if (_vault is null || !_vault.IsUnlocked) { StatusText = "⚠ 密码库未解锁。"; return; }

        var ok = await DialogHooks.ConfirmAsync(
            $"给「{row.Name}」重新生成包密码？\n\n🔴 之前用旧密码建的包**只能用旧密码**打开（新密码只对以后的新包生效）。").ConfigureAwait(true);
        if (!ok) return;

        var password = PasswordGenerator.Generate();
        if (!_vault.Set(row.Plan.Id, password)) { StatusText = "⚠ 写入密码库失败。"; return; }
        Reload();
        StatusText = "已重新生成包密码（记到你的密码管理器里，界面只显示指纹）。";
    }

    [RelayCommand]
    private void OpenArchiveDir() => OpenFolder(_settings!.ArchiveDir);

    [RelayCommand]
    private void OpenStagingDir() => OpenFolder(_settings!.StagingRoot);

    // ==================== 列表 ====================

    private void ApplyFilter()
    {
        IEnumerable<PlanRow> src = _all;

        var kw = (SearchText ?? "").Trim();
        if (kw.Length > 0)
            src = src.Where(r => r.Name.Contains(kw, StringComparison.CurrentCultureIgnoreCase)
                                 || r.SourcesText.Contains(kw, StringComparison.CurrentCultureIgnoreCase)
                                 || r.Plan.Note.Contains(kw, StringComparison.CurrentCultureIgnoreCase));

        src = SortColumn switch
        {
            "Sources" => src.OrderBy(r => r.SourceCountText, StringComparer.CurrentCultureIgnoreCase),
            "LastRun" => src.OrderByDescending(r => r.LastRunAt ?? DateTime.MinValue),
            "Archives" => src.OrderByDescending(r => r.ArchiveCount),
            _ => src.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
        };
        if (SortDescending) src = src.Reverse();

        Items.Clear();
        foreach (var r in src) Items.Add(r);
        OnPropertyChanged(nameof(HasItems));

        NameHeader = "任务" + SortArrow("Name");
        SourcesHeader = "源目录" + SortArrow("Sources");
        KeepHeader = "保留" + SortArrow("Keep");
        LastRunHeader = "最近运行" + SortArrow("LastRun");
        ArchivesHeader = "包数" + SortArrow("Archives");
        PasswordHeader = "包密码" + SortArrow("Password");
    }

    /// <summary>按任务保留份数删最旧的本地包（返回删掉的个数）。</summary>
    private int Prune(BackupPlan plan)
    {
        var keep = Math.Max(1, plan.KeepCount);
        var owned = _catalog!.Items
            .Where(r => r.PlanId == plan.Id)
            .OrderByDescending(r => r.CreatedAt)
            .ToList();

        var removed = 0;
        foreach (var old in owned.Skip(keep))
        {
            var path = Path.Combine(_catalog.ArchiveDir, old.FileName);
            try
            {
                if (File.Exists(path)) File.Delete(path);
                _catalog.Remove(old);
                removed++;
            }
            catch { /* 删不掉（被占用）就留着，下轮再删 */ }
        }
        return removed;
    }

    private void RefreshDetailLists()
    {
        SourceLines.Clear();
        RunLines.Clear();
        var row = Selected;
        if (row is null) return;

        foreach (var s in row.Plan.Sources) SourceLines.Add(new TextLine { Text = s });

        foreach (var run in _runs.Recent(row.Plan.Id, 5))
            RunLines.Add(new TextLine
            {
                Text = $"{run.StartedAt:MM-dd HH:mm} · {run.Status}",
                Detail = string.IsNullOrWhiteSpace(run.ArchiveFile) ? run.Message : run.ArchiveFile + " · " + run.Message
            });
        if (RunLines.Count == 0) RunLines.Add(new TextLine { Text = "还没跑过", Detail = "点工具条「▶ 立即备份」" });
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var s = new string(chars).Trim();
        return s.Length == 0 ? "backup" : s;
    }

    private static void OpenFolder(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
        }
        catch { /* 打开资源管理器失败不影响数据 */ }
    }

    private async Task SetClipboardAsync(string text)
    {
        var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (win?.Clipboard is null) { StatusText = "剪贴板不可用。"; return; }
        try { await win.Clipboard.SetTextAsync(text); }
        catch (Exception ex) { StatusText = "⚠ 复制失败：" + ex.Message; }
    }

    // ==================== 详情面板 + 详情卡 ====================

    public bool HasDetailPanel => true;

    public bool IsDetailOpen => IsDetailVisible;

    public object? DetailContent => null;

    public event EventHandler? DetailChanged;

    [RelayCommand]
    private void ToggleDetailPanel()
    {
        ToggleDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenDetailPanel()
    {
        ShowDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    void IDetailPanelProvider.ToggleDetail() => ToggleDetailPanelCommand.Execute(null);

    void IDetailPanelProvider.OpenDetail() => OpenDetailPanelCommand.Execute(null);

    // ==================== 详情卡绑定代理 ====================

    public string DetailName => Selected?.Name ?? "";
    public string DetailSources => Selected?.SourcesText ?? "";
    public string DetailKeepText => Selected?.KeepText ?? "";
    public string DetailNote => Selected?.Plan.Note ?? "";
    public string DetailPasswordText => Selected?.PasswordText ?? "";
    public string DetailLastRunText => Selected?.LastRunText ?? "";

    public string EditorTitle => _editing is null ? "新建备份任务" : "编辑备份任务：" + _editing.Name;

    private DetailCardDescriptor? _card;

    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        EmptyText = "在左侧选中一个任务，或点工具条「➕ 新建任务」。一个任务 = 若干源目录 → 一个加密归档包。",
        Sections =
        {
            new DetailSection
            {
                Header = "任务",
                Fields =
                {
                    new DetailField { Label = "名称", ValuePath = nameof(DetailName), Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "保留份数", ValuePath = nameof(DetailKeepText), Kind = FieldKind.Text },
                    new DetailField { Label = "包密码", ValuePath = nameof(DetailPasswordText), Kind = FieldKind.Text },
                    new DetailField { Label = "最近运行", ValuePath = nameof(DetailLastRunText), Kind = FieldKind.Text },
                    new DetailField { Label = "备注", ValuePath = nameof(DetailNote), Kind = FieldKind.Text, MultiLine = true, Rows = 44, Selectable = true },
                }
            },
            new DetailSection
            {
                Header = "源目录",
                Fields =
                {
                    new DetailField
                    {
                        Kind = FieldKind.List,
                        OptionsPath = nameof(SourceLines),
                        ItemTextPath = nameof(TextLine.Text),
                        Placeholder = "这个任务还没有源目录"
                    }
                }
            },
            new DetailSection
            {
                Header = "最近 5 次运行",
                Fields =
                {
                    new DetailField
                    {
                        Kind = FieldKind.List,
                        OptionsPath = nameof(RunLines),
                        ItemTextPath = nameof(TextLine.Text),
                        ItemDetailPath = nameof(TextLine.Detail)
                    }
                }
            },
            new DetailSection
            {
                Header = "归档引擎",
                Fields =
                {
                    new DetailField { Label = "默认备份目录", ValuePath = nameof(ArchiveDir), Kind = FieldKind.Text, Selectable = true, Mono = true },
                    new DetailField { Label = "密码库", ValuePath = nameof(VaultStateText), Kind = FieldKind.Text },
                    new DetailField { Kind = FieldKind.Hint, Hint = "包用 7z AES-256 加密，且开了头部加密（-mhe=on）—— 主要防的是包传到云端 / 别人拿到文件后偷看：不解密连包里的文件名都看不到。" },
                }
            }
        }
    };

    // ==================== 工具条 ====================

    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new()
        {
            Text = "➕ 新建任务", Classes = "ghost",
            CommandPath = nameof(NewPlanCommand),
            IsEnabledPath = "!" + nameof(IsBusy),
            ToolTip = "新建一个备份任务：源目录 → 加密归档"
        },
        new()
        {
            Text = "✏ 编辑", Classes = "ghost",
            CommandPath = nameof(EditPlanCommand),
            IsEnabledPath = nameof(HasSelection),
            ToolTip = "编辑选中任务（名称 / 源目录 / 排除规则 / 保留份数 / 备注）"
        },
        new()
        {
            Text = "▶ 立即备份", Classes = "primary",
            CommandPath = nameof(RunBackupCommand),
            IsEnabledPath = nameof(IsNotBusy),
            ToolTip = "马上跑一次：7z 加密归档 → 登记包清单 → 按份数轮转"
        },
        new()
        {
            Text = "⏹ 取消", Classes = "ghost",
            CommandPath = nameof(CancelRunCommand),
            IsVisiblePath = nameof(IsBusy),
            ToolTip = "中止本次备份（会杀 7z 进程并清理半成品包）"
        },
        new()
        {
            Text = "📋 复制密码", Classes = "ghost",
            CommandPath = nameof(CopyPasswordCommand),
            IsEnabledPath = nameof(HasSelection),
            ToolTip = "复制选中任务的包密码（只显示指纹，密码本体在密码库里）"
        },
        new()
        {
            Text = "🗑 删除任务", Classes = "danger-btn",
            CommandPath = nameof(DeletePlanCommand),
            IsEnabledPath = nameof(HasSelection),
            ToolTip = "删除任务配置与它的包密码（⛔ 不删已生成的归档包）"
        },
        new()
        {
            Kind = ToolbarItemKind.TextField,
            Width = 110,
            Placeholder = "搜索任务 / 目录…",
            ValuePath = nameof(SearchText),
            ToolTip = "即时过滤（本地）"
        },
        new()
        {
            Text = "📂 包目录", Classes = "ghost",
            CommandPath = nameof(OpenArchiveDirCommand),
            ToolTip = "打开归档包所在目录"
        },
    };
}
