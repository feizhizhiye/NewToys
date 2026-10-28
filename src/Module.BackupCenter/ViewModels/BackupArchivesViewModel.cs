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

/// <summary>
/// 「备份中心 → 备份包」页（<c>backup-archives</c>）：本地包清单 + 还原 + 删除。
/// <para>
/// 清单与磁盘对账（<c>catalog.json</c> ↔ 包目录里的 <c>.7z</c>）；还原必须先解锁密码库，
/// 解包走暂存目录 + 条目名校验，目标同名按用户选定的策略处理（默认跳过）。
/// </para>
/// </summary>
public partial class BackupArchivesViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    private readonly ArchiveCatalog _catalog = null!;
    private readonly VaultService _vault = null!;
    private readonly BackupSettingsService _settings = null!;
    private readonly ArchiveService _archives = null!;
    private readonly SevenZipLocator _locator = null!;

    private readonly List<ArchiveRecord> _all = new();
    private CancellationTokenSource? _restoreCts;

    [ObservableProperty] private ArchiveRecord? _selected;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _busyPhase = "";

    [ObservableProperty] private bool _isRestoreOpen;
    [ObservableProperty] private string _restoreTargetDir = "";
    [ObservableProperty] private int _restorePolicyIndex;
    [ObservableProperty] private string _restorePlanName = "";

    [ObservableProperty] private string _fileNameHeader = "包文件";
    [ObservableProperty] private string _planNameHeader = "任务";
    [ObservableProperty] private string _sizeHeader = "大小";
    [ObservableProperty] private string _createdHeader = "生成时间";
    [ObservableProperty] private string _shaHeader = "SHA256";
    [ObservableProperty] private string _encryptedHeader = "加密";

    public BackupArchivesViewModel() : base(null, null) { }

    public BackupArchivesViewModel(
        ArchiveCatalog catalog,
        VaultService vault,
        BackupSettingsService settings,
        ArchiveService archives,
        SevenZipLocator locator,
        IColumnLayoutService? cols = null,
        IOptionsStore? opts = null)
        : base(cols, opts)
    {
        _catalog = catalog;
        _vault = vault;
        _settings = settings;
        _archives = archives;
        _locator = locator;
        LoadLayout();
        RefreshList();
    }

    public ObservableCollection<ArchiveRecord> Items { get; } = new();

    /// <summary>同名冲突策略候选（下标 ↔ <see cref="RestoreConflictPolicy"/>）。</summary>
    public IReadOnlyList<string> RestorePolicies { get; } = new[] { "跳过（默认）", "覆盖", "重命名" };

    public bool HasItems => Items.Count > 0;

    public bool IsNotBusy => !IsBusy;

    public bool HasSelection => Selected is not null;

    public bool ShowDetailBody => Selected is not null;

    public string ArchiveDir => _settings?.ArchiveDir ?? "";

    public bool IsNameVisible => Vis("FileName");
    public bool IsPlanVisible => Vis("PlanName");
    public bool IsSizeVisible => Vis("Size");
    public bool IsCreatedVisible => Vis("Created");
    public bool IsShaVisible => Vis("Sha");
    public bool IsEncryptedVisible => Vis("Encrypted");

    protected override string PageKey => "backup-archives";

    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("FileName", "包文件", "*", sample: "工作文档每日备份-20261028-200000.7z"),
        new("PlanName", "任务", "180", sample: "工作文档每日备份"),
        new("Size", "大小", "90", sample: "128.4 MB"),
        new("Created", "生成时间", "140", sample: "2026-10-28 20:00"),
        new("Sha", "SHA256", "120", sample: "3f9a21bc7e55"),
        new("Encrypted", "加密", "60", sample: "是"),
    };

    protected override void RefreshView() => ApplyFilter();

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Items.Count > 0) Selected = Items[0];
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    partial void OnSelectedChanged(ArchiveRecord? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowDetailBody));
        OnPropertyChanged(nameof(DetailFileName));
        OnPropertyChanged(nameof(DetailPlanName));
        OnPropertyChanged(nameof(DetailSizeText));
        OnPropertyChanged(nameof(DetailCreatedText));
        OnPropertyChanged(nameof(DetailSha));
        OnPropertyChanged(nameof(DetailEncrypted));
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnIsRestoreOpenChanged(bool value) => OnPropertyChanged(nameof(RestoreTitle));

    // ==================== 命令 ====================

    /// <summary>重新扫描包目录：补新包（算 SHA256）、剔掉已不存在的记录。</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        int added = 0, removed = 0;
        IsBusy = true;
        BusyPhase = "按包目录对账（新包要算 SHA256）…";
        try
        {
            // 大包的 SHA256 很慢：整段放后台线程，⛔ 不卡 UI
            (added, removed) = await Task.Run(() => _catalog.SyncWithDisk()).ConfigureAwait(true);
        }
        catch (Exception ex) { StatusText = "⚠ 扫描包目录失败：" + ex.Message; }
        finally
        {
            IsBusy = false;
            BusyPhase = "";
        }

        RefreshList();
        StatusText = $"共 {_all.Count} 个包 · 新增 {added} / 移除 {removed}（目录：{ArchiveDir}）";
    }

    [RelayCommand]
    private void DeleteArchive()
    {
        var rec = Selected;
        if (rec is null) { StatusText = "先选中一个包。"; return; }

        var path = Path.Combine(_catalog.ArchiveDir, rec.FileName);
        try
        {
            if (File.Exists(path)) File.Delete(path);
            _catalog.Remove(rec);
            Selected = null;
            RefreshList();
            StatusText = "已删除包：" + rec.FileName;
        }
        catch (Exception ex) { StatusText = "⚠ 删除失败（包可能被占用）：" + ex.Message; }
    }

    [RelayCommand]
    private void OpenArchiveDir()
    {
        try
        {
            Directory.CreateDirectory(ArchiveDir);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + ArchiveDir + "\"") { UseShellExecute = true });
        }
        catch { /* 打开资源管理器失败不影响数据 */ }
    }

    [RelayCommand]
    private async Task CopyShaAsync()
    {
        var rec = Selected;
        if (rec is null || string.IsNullOrEmpty(rec.Sha256)) { StatusText = "这个包还没有 SHA256（点「🔄 重新扫描」补算）。"; return; }
        var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (win?.Clipboard is null) { StatusText = "剪贴板不可用。"; return; }
        try
        {
            await win.Clipboard.SetTextAsync(rec.Sha256).ConfigureAwait(true);
            StatusText = "已复制整包 SHA256。";
        }
        catch (Exception ex) { StatusText = "⚠ 复制失败：" + ex.Message; }
    }

    /// <summary>打开还原弹层（先做前置检查：包在不在、密码库解没解锁、这个包有没有密码）。</summary>
    [RelayCommand]
    private void BeginRestore()
    {
        var rec = Selected;
        if (rec is null) { StatusText = "先选中一个包。"; return; }
        if (!File.Exists(Path.Combine(_catalog.ArchiveDir, rec.FileName))) { StatusText = "⚠ 包文件已经不在目录里了。"; return; }
        if (_vault is null || !_vault.IsUnlocked)
        {
            StatusText = "⚠ 密码库未解锁：到「备份中心设置 → 主密码」解锁后再还原。";
            return;
        }
        if (string.IsNullOrEmpty(rec.PlanId))
        {
            StatusText = "⚠ 这个包没登记所属任务（手工拷进来的），取不到包密码，无法还原。";
            return;
        }

        RestorePlanName = rec.FileName;
        RestoreTargetDir = "";
        RestorePolicyIndex = 0;
        IsRestoreOpen = true;
    }

    [RelayCommand]
    private void CancelRestore()
    {
        _restoreCts?.Cancel();
        IsRestoreOpen = false;
    }

    [RelayCommand]
    private async Task PickRestoreFolderAsync()
    {
        var picked = await DialogHooks.PickFolderAsync("还原到哪个目录", RestoreTargetDir).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(picked)) RestoreTargetDir = picked;
    }

    [RelayCommand]
    private async Task ConfirmRestoreAsync()
    {
        var rec = Selected;
        if (rec is null || IsBusy) return;
        if (string.IsNullOrWhiteSpace(RestoreTargetDir)) { StatusText = "⚠ 先选还原目标目录。"; return; }

        var password = _vault!.Get(rec.PlanId);
        if (string.IsNullOrEmpty(password)) { StatusText = "⚠ 这个包的密码不在密码库里（或被删除），无法还原。"; return; }

        var archiveFile = Path.Combine(_catalog.ArchiveDir, rec.FileName);
        var policy = RestorePolicyIndex switch
        {
            1 => RestoreConflictPolicy.Overwrite,
            2 => RestoreConflictPolicy.Rename,
            _ => RestoreConflictPolicy.Skip
        };

        IsBusy = true;
        IsRestoreOpen = false;
        BusyPhase = "探测 7z…";
        _restoreCts = new CancellationTokenSource();
        try
        {
            var zip = await _locator.LocateAsync(force: true, _restoreCts.Token).ConfigureAwait(true);
            if (!zip.Found) { StatusText = "⚠ " + zip.Message; return; }

            var progress = new Progress<string>(s => BusyPhase = s);
            var outcome = await _archives!.RestoreAsync(zip.Path, archiveFile, password, RestoreTargetDir,
                policy, progress, _restoreCts.Token).ConfigureAwait(true);

            StatusText = outcome.Ok ? "✅ " + outcome.Message : (outcome.Canceled ? "已取消还原。" : "⚠ " + outcome.Message);
        }
        catch (OperationCanceledException) { StatusText = "已取消还原。"; }
        catch (Exception ex) { StatusText = "⚠ 还原失败：" + ex.Message; }
        finally
        {
            IsBusy = false;
            BusyPhase = "";
            _restoreCts?.Dispose();
            _restoreCts = null;
        }
    }

    // ==================== 列表 ====================

    private void RefreshList()
    {
        var keep = Selected?.FileName;
        _all.Clear();
        if (_catalog is not null) _all.AddRange(_catalog.Items);
        ApplyFilter();
        if (keep != null) Selected = Items.FirstOrDefault(r => r.FileName == keep);
        OnPropertyChanged(nameof(ArchiveDir));
    }

    private void ApplyFilter()
    {
        IEnumerable<ArchiveRecord> src = _all;

        var kw = (SearchText ?? "").Trim();
        if (kw.Length > 0)
            src = src.Where(r => r.FileName.Contains(kw, StringComparison.CurrentCultureIgnoreCase)
                                 || r.PlanName.Contains(kw, StringComparison.CurrentCultureIgnoreCase));

        src = SortColumn switch
        {
            "Size" => src.OrderBy(r => r.SizeBytes),
            "Created" => src.OrderBy(r => r.CreatedAt),
            "PlanName" => src.OrderBy(r => r.PlanName, StringComparer.CurrentCultureIgnoreCase),
            "Sha" => src.OrderBy(r => r.ShaText, StringComparer.Ordinal),
            _ => src.OrderByDescending(r => r.CreatedAt)
        };
        if (SortDescending) src = src.Reverse();

        Items.Clear();
        foreach (var r in src) Items.Add(r);
        OnPropertyChanged(nameof(HasItems));

        FileNameHeader = "包文件" + SortArrow("FileName");
        PlanNameHeader = "任务" + SortArrow("PlanName");
        SizeHeader = "大小" + SortArrow("Size");
        CreatedHeader = "生成时间" + SortArrow("Created");
        ShaHeader = "SHA256" + SortArrow("Sha");
        EncryptedHeader = "加密" + SortArrow("Encrypted");
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

    public string DetailFileName => Selected?.FileName ?? "";
    public string DetailPlanName => string.IsNullOrEmpty(Selected?.PlanName) ? "（未登记任务）" : Selected!.PlanName;
    public string DetailSizeText => Selected?.SizeText ?? "";
    public string DetailCreatedText => Selected?.CreatedText ?? "";
    public string DetailSha => Selected?.Sha256 ?? "";

    public string DetailEncrypted => Selected?.EncryptedText ?? "";
    public string RestoreTitle => string.IsNullOrEmpty(RestorePlanName) ? "还原归档包" : "还原：" + RestorePlanName;

    private DetailCardDescriptor? _card;

    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        EmptyText = "在左侧选中一个包查看明细；「↩ 还原」会解到临时目录校验条目名后再搬到目标目录。",
        Sections =
        {
            new DetailSection
            {
                Header = "包",
                Fields =
                {
                    new DetailField { Label = "文件", ValuePath = nameof(DetailFileName), Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "任务", ValuePath = nameof(DetailPlanName), Kind = FieldKind.Text },
                    new DetailField { Label = "大小", ValuePath = nameof(DetailSizeText), Kind = FieldKind.Text },
                    new DetailField { Label = "生成时间", ValuePath = nameof(DetailCreatedText), Kind = FieldKind.Text },
                    new DetailField { Label = "加密", ValuePath = nameof(DetailEncrypted), Kind = FieldKind.Text },
                }
            },
            new DetailSection
            {
                Header = "校验",
                Fields =
                {
                    new DetailField { Label = "SHA256", ValuePath = nameof(DetailSha), Kind = FieldKind.Text, Mono = true, Selectable = true },
                    new DetailField { Kind = FieldKind.Hint, Hint = "SHA256 是整包校验值：拷到别的机器后先比它，值一样说明包没坏。" },
                }
            }
        }
    };

    // ==================== 工具条 ====================

    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new()
        {
            Text = "🔄 重新扫描", Classes = "ghost",
            CommandPath = nameof(RefreshCommand),
            IsEnabledPath = "!" + nameof(IsBusy),
            ToolTip = "按包目录对账清单：补新包（算 SHA256）、剔掉已删除的记录"
        },
        new()
        {
            Text = "↩ 还原", Classes = "primary",
            CommandPath = nameof(BeginRestoreCommand),
            IsEnabledPath = nameof(HasSelection),
            ToolTip = "把选中的包还原到指定目录（同名冲突按你选的策略处理）"
        },
        new()
        {
            Text = "🗑 删除包", Classes = "danger-btn",
            CommandPath = nameof(DeleteArchiveCommand),
            IsEnabledPath = nameof(HasSelection),
            ToolTip = "直接从磁盘删掉这个包（不可恢复）"
        },
        new()
        {
            Text = "📋 复制 SHA256", Classes = "ghost",
            CommandPath = nameof(CopyShaCommand),
            IsEnabledPath = nameof(HasSelection),
            ToolTip = "复制整包校验值"
        },
        new()
        {
            Kind = ToolbarItemKind.TextField,
            Width = 110,
            Placeholder = "搜索包名 / 任务…",
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
