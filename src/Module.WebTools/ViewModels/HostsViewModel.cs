using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.WebTools.Models;
using NewToys.WebTools.Services;

namespace NewToys.WebTools.ViewModels;

/// <summary>
/// hosts 管理页：多套方案（列表）+ 右侧详情（编辑条目 / 预检差异 / 三路检测 / 应用到系统 hosts）。
/// <para>
/// 系统 hosts 本身的展示、备份、手动编辑与还原在**另一个页面** <c>web-hosts-system</c>——
/// 本页只管「方案」这个对象，⛔ 不显示系统文件全文。
/// </para>
/// </summary>
public partial class HostsViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    // ==================== 声明式工具条（需求 §4.8.5） ====================
    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new() { Text = "📂 导入文件", CommandPath = nameof(ImportFileCommand), IsEnabledPath = "!" + nameof(IsBusy),
                ToolTip = "选一份本地 hosts 文本，解析成条目写进当前方案" },
        new() { Kind = ToolbarItemKind.Combo, Width = 170, ItemsPath = nameof(Sources), DisplayPath = nameof(HostsSource.Display),
                SelectedItemPath = nameof(SelectedSource), ToolTipPath = nameof(SourceHint),
                ToolTip = "hosts 源（预设 + 自定义）；选定后点「⬇ 拉取源」" },
        new() { Text = "⬇ 拉取源", CommandPath = nameof(FetchSourceCommand), IsEnabledPath = "!" + nameof(IsBusy),
                ToolTip = "按选中源的地址列表逐个尝试，拉到一份映射集写进当前方案（⛔ 不会自动写系统 hosts）" },
        new() { Text = "➕ 源", Classes = "ghost", CommandPath = nameof(AddSourceCommand),
                ToolTip = "添加一个自定义源（名称 + 地址；多个地址用空格分隔，第一个是主地址）" },
        new() { Text = "➕ 新建方案", Classes = "ghost", CommandPath = nameof(NewProfileCommand) },
        new() { Text = "🔍 检测", AltText = "⏹ 停止", AltTextPath = nameof(IsChecking),
                CommandPath = nameof(CheckCommand), IsEnabledPath = nameof(HasSelection),
                ToolTip = "逐条检测：本机 DNS 与映射 IP 是否一致 + 目标端口是否可达（可取消），并跑一遍静态安全规则" },
        new() { Text = "🔄 重读方案", CommandPath = nameof(RefreshCommand),
                ToolTip = "重新读一遍本地的 hosts 方案清单（⛔ 不动系统 hosts、不导入）" }
    };

    private readonly HostsService _svc = null!;
    private readonly HostsSourceService? _sources;
    private readonly HostsCheckService? _check;
    private readonly IWebToolsSettingsService? _settings;

    private CancellationTokenSource? _checkCts;

    public ObservableCollection<HostsProfile> Profiles { get; } = new();

    /// <summary>源清单（工具条下拉的数据源）。</summary>
    public ObservableCollection<HostsSource> Sources { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private HostsProfile? _selected;
    [ObservableProperty] private HostsSource? _selectedSource;
    [ObservableProperty] private string _entriesText = "";
    [ObservableProperty] private string _blockPreview = "";
    [ObservableProperty] private string _diffText = "";
    [ObservableProperty] private string _checkReportText = "";
    [ObservableProperty] private string _securityText = "";

    // ============ 表头 ============
    [ObservableProperty] private string _nameHeader = "名称 ▲";
    [ObservableProperty] private string _entriesHeader = "条目数";
    [ObservableProperty] private string _healthHeader = "健康";
    [ObservableProperty] private string _sourceHeader = "来源";

    // ============ 列显隐 / 详情开合 / 排序 / 多项筛选：继承自 ListPageViewModelBase ============

    public bool IsNameVisible => Vis("Name");
    public bool IsEntriesVisible => Vis("Entries");
    public bool IsHealthVisible => Vis("Health");
    public bool IsSourceVisible => Vis("Source");

    protected override string PageKey => "web-hosts";

    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Name", "名称", "3*", sample: "开发环境 hosts"),
        new("Entries", "条目数", "1*", sample: "12"),
        new("Health", "健康", "1.4*", sortable: false, sample: "✅20 ⚠2"),
        new("Source", "来源", "2*", sample: "GitHub520"),
    };

    protected override void RefreshView() => ApplyView();

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Profiles.Count > 0) Selected = Profiles[0];
    }

    public bool HasSelection => Selected != null;
    public bool HasItems => Profiles.Count > 0;

    /// <summary>详情卡的数据源。</summary>
    public HostsProfile? DetailItem => Selected;

    /// <summary>详情卡正文是否显示（有选中项）。</summary>
    public bool ShowDetailBody => HasSelection;

    /// <summary>选中源的悬停说明（地址 / 上次拉取）。</summary>
    public string SourceHint => SelectedSource?.Hint ?? "还没有可用的源";

    // ==================== 详情面板 + 声明式详情卡 ====================

    public bool HasBlockPreview => !string.IsNullOrWhiteSpace(BlockPreview);
    public bool HasDiff => !string.IsNullOrWhiteSpace(DiffText);
    public bool HasCheckReport => !string.IsNullOrWhiteSpace(CheckReportText);
    public bool HasSecurity => !string.IsNullOrWhiteSpace(SecurityText);

    partial void OnBlockPreviewChanged(string value) => OnPropertyChanged(nameof(HasBlockPreview));
    partial void OnDiffTextChanged(string value) => OnPropertyChanged(nameof(HasDiff));
    partial void OnCheckReportTextChanged(string value) => OnPropertyChanged(nameof(HasCheckReport));
    partial void OnSecurityTextChanged(string value) => OnPropertyChanged(nameof(HasSecurity));

    partial void OnSelectedSourceChanged(HostsSource? value) => OnPropertyChanged(nameof(SourceHint));

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

    private DetailCardDescriptor? _card;

    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        EmptyText = "在左侧列表里选中一套方案",
        Sections =
        {
            new DetailSection
            {
                Fields =
                {
                    new DetailField
                    {
                        Label = "方案名称",
                        ValuePath = "DetailItem.Name",
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur
                    },
                    new DetailField
                    {
                        Label = "条目（每行一条：IP 主机名 # 注释；行首 # 表示禁用）",
                        ValuePath = nameof(EntriesText),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true,
                        MultiLine = true,
                        Rows = 150
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "与当前系统 hosts 受管块的差异（点「🧪 预检」刷新）：",
                        VisibleWhen = nameof(HasDiff)
                    },
                    new DetailField
                    {
                        ValuePath = nameof(DiffText),
                        Kind = FieldKind.Text,
                        Selectable = true,
                        Mono = true,
                        MultiLine = true,
                        Rows = 70,
                        VisibleWhen = nameof(HasDiff)
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "检测结果（只列有问题的条目 + 汇总）：",
                        VisibleWhen = nameof(HasCheckReport)
                    },
                    new DetailField
                    {
                        ValuePath = nameof(CheckReportText),
                        Kind = FieldKind.Text,
                        Selectable = true,
                        Mono = true,
                        MultiLine = true,
                        Rows = 100,
                        VisibleWhen = nameof(HasCheckReport)
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "静态安全检查（不联网，只查一眼能看出的风险；不等于无毒）：",
                        VisibleWhen = nameof(HasSecurity)
                    },
                    new DetailField
                    {
                        ValuePath = nameof(SecurityText),
                        Kind = FieldKind.Text,
                        Selectable = true,
                        MultiLine = true,
                        Rows = 80,
                        VisibleWhen = nameof(HasSecurity)
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "无权限写入系统 hosts 时，下方会显示可复制的受管块：",
                        VisibleWhen = nameof(HasBlockPreview)
                    },
                    new DetailField
                    {
                        ValuePath = nameof(BlockPreview),
                        Kind = FieldKind.Text,
                        Selectable = true,
                        Mono = true,
                        VisibleWhen = nameof(HasBlockPreview)
                    }
                }
            }
        },
        Actions =
        {
            new DetailAction
            {
                Text = "💾 保存",
                Classes = "primary",
                ToolTip = "把当前条目写回方案（落模块数据库）",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = SaveCommand
            },
            new DetailAction
            {
                Text = "🧪 预检",
                Classes = "ghost",
                ToolTip = "与当前系统 hosts 的受管块比对，看这次应用会改哪些行",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = PreviewDiffCommand
            },
            new DetailAction
            {
                Text = "✅ 应用",
                Classes = "ghost",
                ToolTip = "把这套方案写进系统 hosts 的受管块（需管理员；写前自动备份）",
                ConfirmText = "将替换系统 hosts 里本程序的受管块（用户自己写的其它行不动）。\n写入前会自动备份原文件，可在「系统 Hosts」页还原。继续？",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = ApplyCommand
            },
            new DetailAction
            {
                Text = "📤 导出",
                Classes = "ghost",
                ToolTip = "把当前方案导出一份 hosts 文本文件",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = ExportCommand
            },
            new DetailAction
            {
                Text = "🗑 删除",
                Classes = "ghost",
                ToolTip = "删除这套方案（⛔ 不动系统 hosts）",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = DeleteCommand
            }
        }
    };

    public HostsViewModel() { }

    public HostsViewModel(
        HostsService svc,
        IColumnLayoutService cols,
        HostsSourceService sources,
        HostsCheckService check,
        IWebToolsSettingsService settings)
        : base(cols)
    {
        _svc = svc;
        _sources = sources;
        _check = check;
        _settings = settings;

        LoadLayout();
        LoadSources();
        Refresh();
    }

    // ============ 加载 ============

    private void LoadSources()
    {
        Sources.Clear();
        if (_sources is null) return;
        foreach (var s in _sources.Load().Where(s => s.Enabled)) Sources.Add(s);
        SelectedSource = Sources.FirstOrDefault();
    }

    [RelayCommand]
    private void Refresh()
    {
        Profiles.Clear();
        foreach (var p in _svc.Load().OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            HostsCheckService.ResetResults(p.Entries);
            Profiles.Add(p);
        }

        if (Selected != null && !Profiles.Contains(Selected)) Selected = null;
        ApplyView();
        StatusText = $"共 {Profiles.Count} 套方案 · 系统 hosts：{_svc.SystemHostsPath}";
    }

    // ============ 排序 / 过滤 ============

    private void ApplyView()
    {
        IEnumerable<HostsProfile> q = ApplyFilters(Profiles);

        q = SortDescending
            ? q.OrderByDescending(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            : q.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase);

        var ordered = q.ToList();
        Profiles.Clear();
        foreach (var p in ordered) Profiles.Add(p);
        UpdateHeaders();
        OnPropertyChanged(nameof(HasItems));
    }

    private void UpdateHeaders() => NameHeader = "名称" + SortArrow("Name");

    // ============ 选中 → 同步详情编辑区 ============

    partial void OnSelectedChanged(HostsProfile? value)
    {
        OnPropertyChanged(nameof(DetailItem));
        EntriesText = value == null ? "" : string.Join("\n", value.Entries.Select(e => e.ToLine()));
        BlockPreview = "";
        DiffText = "";
        CheckReportText = "";
        SecurityText = value == null ? "" : BuildSecurityText(value.Entries);
        OnPropertyChanged(nameof(HasSelection));
    }

    // ============ 增删改 ============

    [RelayCommand]
    private void NewProfile()
    {
        var p = new HostsProfile { Name = "新方案 " + (Profiles.Count + 1) };
        Profiles.Add(p);
        Selected = p;
        IsDetailVisible = true;
        StatusText = "已新建方案，编辑条目后点「保存」。";
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected == null) return;
        Profiles.Remove(Selected);
        await PersistAsync();
        Selected = null;
        StatusText = "已删除方案。";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Selected == null) return;
        Selected.Entries = HostsService.ParseText(EntriesText);
        HostsCheckService.ResetResults(Selected.Entries);
        await PersistAsync();
        OnPropertyChanged(nameof(DetailItem));
        StatusText = $"已保存「{Selected.Name}」（{Selected.Entries.Count} 条）。";
    }

    private async Task PersistAsync()
    {
        await Task.Run(() => _svc.Save(Profiles.ToList()));
    }

    // ============ 源订阅 ============

    [RelayCommand]
    private async Task FetchSourceAsync()
    {
        if (_sources is null) return;
        if (SelectedSource is not { } source) { StatusText = "请先在工具条下拉里选一个源。"; return; }

        try
        {
            IsBusy = true;
            StatusText = $"正在拉取「{source.Name}」…";
            var result = await _sources.FetchAsync(source);
            if (!result.Ok)
            {
                StatusText = "⚠ 拉取失败：" + result.Message;
                return;
            }

            // 有同名源方案就覆盖，否则新建（⛔ 不自动写系统 hosts）
            var target = Profiles.FirstOrDefault(p =>
                string.Equals(p.Source, "source:" + source.Id, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                target = new HostsProfile { Name = source.Name, Source = "source:" + source.Id };
                Profiles.Add(target);
            }

            target.Entries = result.Entries;
            target.Source = "source:" + source.Id;
            target.LastFetchedAt = source.LastFetchedAt;
            target.LastFetchedCount = source.LastCount;

            _sources.Save(Sources);
            await PersistAsync();

            Selected = target;
            OnPropertyChanged(nameof(DetailItem));
            ApplyView();
            StatusText = result.Message + "（已写入方案，点「✅ 应用」才动系统 hosts）";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task AddSourceAsync()
    {
        if (_sources is null) return;

        var name = await DialogHooks.InputAsync("添加 hosts 源", "自定义源");
        if (string.IsNullOrWhiteSpace(name)) return;

        var urls = await DialogHooks.InputAsync("源地址（多个用空格分隔，第一个为主地址）", "https://");
        if (string.IsNullOrWhiteSpace(urls)) return;

        var source = HostsSourceService.NewCustom(name, urls.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (source.Urls.Count == 0) { StatusText = "⚠ 没填有效地址，未添加。"; return; }

        Sources.Add(source);
        _sources.Save(Sources);
        SelectedSource = source;
        StatusText = $"已添加自定义源「{source.Name}」，点「⬇ 拉取源」开始下载。";
    }

    // ============ 检测（可用性 + 静态安全） ============

    [RelayCommand]
    private async Task CheckAsync()
    {
        if (_check is null || Selected is not { } profile) return;

        if (IsChecking)
        {
            _checkCts?.Cancel();
            return;
        }

        _checkCts = new CancellationTokenSource();
        IsChecking = true;
        try
        {
            var options = new HostsCheckOptions
            {
                TimeoutSeconds = _settings?.HostsCheckTimeoutSeconds ?? 3,
                Concurrency = _settings?.HostsCheckConcurrency ?? 8
            };

            var total = profile.Entries.Count(e => e.Enabled);
            StatusText = $"正在检测「{profile.Name}」的 {total} 条启用条目…（点「⏹ 停止」可取消）";

            var progress = new Progress<int>(n => StatusText = $"检测中 {n}/{total}…");
            await _check.CheckAsync(profile.Entries, options, progress, _checkCts.Token);

            SecurityText = BuildSecurityText(profile.Entries);
            CheckReportText = BuildCheckReport(profile);
            OnPropertyChanged(nameof(DetailItem));
            ApplyView();

            var ok = profile.Entries.Count(e => e.Status == HostsEntryStatus.Ok);
            var mismatch = profile.Entries.Count(e => e.Status == HostsEntryStatus.Mismatch);
            var fail = profile.Entries.Count(e => e.Status is HostsEntryStatus.Unreachable or HostsEntryStatus.Timeout);
            StatusText = (fail + mismatch) == 0
                ? $"检测完成：{ok} 条可达 · 未发现问题（含未检测的禁用行）"
                : $"检测完成：✅{ok} · ⚠{mismatch} · ❌{fail}（明细见右侧「检测结果」）";
        }
        catch (OperationCanceledException)
        {
            StatusText = "已停止检测（保留已完成的结论）。";
            CheckReportText = BuildCheckReport(profile);
        }
        finally
        {
            IsChecking = false;
            _checkCts?.Dispose();
            _checkCts = null;
        }
    }

    private static string BuildCheckReport(HostsProfile profile)
    {
        var checkedCount = profile.Entries.Count(e => e.Status != HostsEntryStatus.Unknown);
        if (checkedCount == 0) return "";

        var problems = profile.Entries.Where(e => e.IsProblem).ToList();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"共检测 {checkedCount} 条（启用），其中 {problems.Count} 条有问题：");
        sb.AppendLine();

        foreach (var e in problems.Take(60))
            sb.AppendLine($"{e.StatusText,-28} {e.Host,-32} → {e.Ip}");
        if (problems.Count > 60) sb.AppendLine($"…还有 {problems.Count - 60} 条，略。");

        if (problems.Count == 0)
        {
            sb.AppendLine("✅ 没有发现不可达 / 解析不一致的条目。");
            sb.AppendLine("提示：⚠ 解析不一致 = 本机 DNS 给出的 IP 与映射不同（可能是 hosts 已过期）；");
            sb.AppendLine("     ❌ 不可达 = 映射的 IP 在 443 / 80 上都连不上。");
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildSecurityText(IReadOnlyList<HostsEntry> entries)
    {
        if (entries.Count == 0) return "";
        var report = HostsCheckService.Inspect(entries);
        if (report.IsSafe) return "🛡 未发现静态风险（私网 / 黑洞地址、重复域名、非法主机名、单 IP 域名过多）。";

        var sb = new System.Text.StringBuilder();
        foreach (var item in report.Items) sb.AppendLine("• " + item);
        return sb.ToString().TrimEnd();
    }

    // ============ 应用 / 预检 / 导出 / 导入 ============

    [RelayCommand]
    private void PreviewDiff()
    {
        if (Selected is not { } profile) return;
        var diff = _svc.DiffAgainstManaged(profile);
        DiffText = diff.Summary;
        StatusText = "预检：" + diff.Summary;
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (Selected is not { } profile) return;

        // 编辑框里的改动先落进模型（用户可能改完直接点应用）
        profile.Entries = HostsService.ParseText(EntriesText);

        var (ok, msg) = await _svc.ApplyAsync(profile);
        StatusText = msg;
        if (!ok) BlockPreview = _svc.ExportBlock(profile);   // 无权限时给可复制的块

        DiffText = _svc.DiffAgainstManaged(profile).Summary;
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (Selected is not { } profile) return;

        var path = await DialogHooks.SaveFileAsync("导出 hosts 文本", null, SafeFileName(profile.Name) + ".hosts", "*.hosts|*.*");
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            System.IO.File.WriteAllText(path, HostsService.BuildManagedBlock(profile));
            BlockPreview = _svc.ExportBlock(profile);
            StatusText = $"已导出受管块 → {path}";
        }
        catch (Exception ex)
        {
            StatusText = "⚠ 导出失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task ImportFileAsync()
    {
        if (Selected == null) { StatusText = "先选一套方案（或新建一个）再导入。"; return; }

        var files = await DialogHooks.PickFilesAsync("导入 hosts 文件", null, false, "*.hosts|*.txt|*.*");
        if (files.Count == 0) return;

        var (ok, msg, entries) = HostsService.ImportFile(files[0]);
        if (!ok) { StatusText = "⚠ " + msg; return; }

        Selected.Entries = entries;
        Selected.Source = "file:" + System.IO.Path.GetFileName(files[0]);
        HostsCheckService.ResetResults(entries);
        EntriesText = string.Join("\n", entries.Select(e => e.ToLine()));
        CheckReportText = "";
        SecurityText = BuildSecurityText(entries);
        await PersistAsync();
        OnPropertyChanged(nameof(DetailItem));
        ApplyView();
        StatusText = msg;
    }

    private static string SafeFileName(string name)
    {
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Length == 0 ? "hosts" : name;
    }
}
