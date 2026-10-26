using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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
/// hosts 管理页（<c>web-hosts</c>）= **双列表工作台**：左「目标」（当前系统 hosts 或某份备份）
/// 与右「来源」（拉取的方案）并排对照，详情卡做条目级增删改，工具条管备份 / 还原 / 写入。
/// <para>
/// 🔴 左侧可编辑内容是**内存草稿** <c>_draftText</c>（全文）：条目行编辑与文本视图改的都是它，
/// 点「✅ 写入系统 hosts」才落盘（一次二次确认 + 一次备份）；备份快照只读。
/// </para>
/// <para>🔴 备份只针对系统 hosts 原文（整份），⛔ 不备份拉取来的方案。</para>
/// </summary>
public partial class HostsViewModel : ObservableObject, IStatusProvider, IToolbarSource, IDetailPanelProvider, IDetailCardSource
{
    private readonly HostsService _svc = null!;
    private readonly HostsSourceService? _sources;
    private readonly HostsCheckService? _check;
    private readonly IWebToolsSettingsService? _settings;

    /// <summary>左栏草稿全文（唯一真相；行模型是它的投影）。</summary>
    private string _draftText = "";

    private CancellationTokenSource? _checkCts;
    private DetailCardDescriptor? _card;

    public HostsViewModel() { }

    public HostsViewModel(
        HostsService svc,
        HostsSourceService sources,
        HostsCheckService check,
        IWebToolsSettingsService settings)
    {
        _svc = svc;
        _sources = sources;
        _check = check;
        _settings = settings;

        LoadSources();
        Refresh();
    }

    // ==================== 工具条 ====================

    public IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new() { Text = "📦 备份当前", CommandPath = nameof(BackupNowCommand),
                ToolTip = "把当前系统 hosts 整份复制到备份目录（只读也能备份）" },
        new() { Text = "♻ 还原", CommandPath = nameof(RestoreCommand), IsEnabledPath = nameof(CanRestore),
                ToolTip = "把系统 hosts 整份换回左栏选中的那份备份（还原前会再备份当前文件）" },
        new() { Text = "🗑 删备份", Classes = "ghost", CommandPath = nameof(DeleteBackupCommand), IsEnabledPath = nameof(CanRestore),
                ToolTip = "只删备份文件，⛔ 不动系统 hosts" },
        new() { Text = "📄 文本", AltText = "📋 条目", AltTextPath = nameof(IsTextView),
                CommandPath = nameof(ToggleTextViewCommand),
                ToolTip = "左栏在「条目列表」和「原文全文」之间切换（文本视图可直接改整份文件）" },
        new() { Text = "🧹 清除受管块", Classes = "ghost", CommandPath = nameof(ClearManagedCommand),
                IsEnabledPath = nameof(CanEditTarget),
                ToolTip = "从草稿里删掉本程序写的那一段（用户自己写的行保留），仍需点「✅ 写入」才落盘" },
        new() { Text = "📂 备份目录", Classes = "ghost", CommandPath = nameof(OpenBackupFolderCommand),
                ToolTip = "打开 hosts 备份所在文件夹" },
        new() { Text = "🔍 检测", AltText = "⏹ 停止", AltTextPath = nameof(IsChecking),
                CommandPath = nameof(CheckCommand), IsEnabledPath = nameof(HasProfileSelection),
                ToolTip = "逐条检测右栏方案：本机 DNS 与映射 IP 是否一致 + 目标端口是否可达（可取消），并跑静态安全规则" },
        new() { Text = "➕ 新建方案", Classes = "ghost", CommandPath = nameof(NewProfileCommand) },
        new() { Text = "🔄 重读", CommandPath = nameof(RefreshCommand),
                ToolTip = "重读系统 hosts 与方案清单（丢弃左栏未写入的改动）" }
    };

    /// <summary>工具条构成变化（增删项 / 换页）时通知 Shell 重建。</summary>
    public event EventHandler? ToolbarChanged;

    private void NotifyToolbarChanged() => ToolbarChanged?.Invoke(this, EventArgs.Empty);

    // ==================== 集合 ====================

    /// <summary>左栏行（草稿投影）。</summary>
    public ObservableCollection<HostsLine> SystemRows { get; } = new();

    /// <summary>右栏行（选中方案条目的投影）。</summary>
    public ObservableCollection<HostsLine> ProfileRows { get; } = new();

    public ObservableCollection<HostsProfile> Profiles { get; } = new();

    public ObservableCollection<HostsSource> Sources { get; } = new();

    public ObservableCollection<HostsTargetSource> TargetSources { get; } = new();

    // ==================== 选中 ====================

    [ObservableProperty] private HostsTargetSource? _selectedTarget;
    [ObservableProperty] private HostsProfile? _selectedProfile;
    [ObservableProperty] private HostsSource? _selectedSource;

    /// <summary>左栏选中行（与右栏互斥）。</summary>
    [ObservableProperty] private HostsLine? _selectedSystemRow;

    /// <summary>右栏选中行（与左栏互斥）。</summary>
    [ObservableProperty] private HostsLine? _selectedProfileRow;

    // ==================== 状态 ====================

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private bool _isTextView;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _showEntriesOnly = true;
    [ObservableProperty] private bool _leftDiffOnly;
    [ObservableProperty] private bool _rightDiffOnly;
    [ObservableProperty] private bool _isDetailVisible = true;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _fileInfoText = "";
    [ObservableProperty] private string _managedInfoText = "";
    [ObservableProperty] private string _permissionText = "";
    [ObservableProperty] private string _diffSummary = "";
    [ObservableProperty] private string _leftCountText = "";
    [ObservableProperty] private string _rightCountText = "";
    [ObservableProperty] private string _checkReportText = "";
    [ObservableProperty] private string _securityText = "";

    /// <summary>系统 hosts 是否可写（无管理员时为 false）。</summary>
    [ObservableProperty] private bool _canWriteSystem;

    /// <summary>左栏是否在看备份快照（只读）。</summary>
    public bool TargetIsBackup => SelectedTarget is { IsCurrent: false };

    public bool CanEditTarget => !TargetIsBackup && CanWriteSystem;

    public bool CanRestore => TargetIsBackup;

    public bool HasProfileSelection => SelectedProfile != null;

    public bool HasProfiles => Profiles.Count > 0;

    public bool HasSources => Sources.Count > 0;

    public bool HasCheckReport => !string.IsNullOrWhiteSpace(CheckReportText);

    public bool HasSecurity => !string.IsNullOrWhiteSpace(SecurityText);

    public bool IsListMode => !IsTextView;

    public string SourceHint => SelectedSource?.Hint ?? "设置页里还没有可用源";

    public string SystemHostsPath => _svc?.SystemHostsPath ?? "";

    public string EmptyLeftHint => TargetIsBackup
        ? "这份备份是空的（切「📄 文本」看原文）"
        : "没有可显示的条目（切「📄 文本」看原文）";

    public string EmptyRightHint => Profiles.Count == 0
        ? "还没有方案：在右栏顶部选一个源，点「⬇ 拉取」。"
        : "这个方案没有条目";

    // ==================== 属性联动 ====================

    partial void OnSelectedTargetChanged(HostsTargetSource? value)
    {
        LoadTarget(value);
        OnPropertyChanged(nameof(TargetIsBackup));
        OnPropertyChanged(nameof(CanEditTarget));
        OnPropertyChanged(nameof(CanRestore));
        OnPropertyChanged(nameof(EmptyLeftHint));
        NotifyToolbarChanged();
    }

    partial void OnSelectedProfileChanged(HostsProfile? value)
    {
        OnPropertyChanged(nameof(HasProfileSelection));
        OnPropertyChanged(nameof(EmptyRightHint));
        NotifyToolbarChanged();
        CheckReportText = "";
        SecurityText = value == null ? "" : BuildSecurityText(value.Entries);
        RefreshRows();
    }

    partial void OnSelectedSourceChanged(HostsSource? value) => OnPropertyChanged(nameof(SourceHint));

    partial void OnIsTextViewChanged(bool value)
    {
        OnPropertyChanged(nameof(IsListMode));
        RebuildRows();
    }

    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(WriteButtonText));

    public string WriteButtonText => IsDirty ? "✅ 写入系统 hosts（有改动）" : "✅ 写入系统 hosts";

    partial void OnCanWriteSystemChanged(bool value) => OnPropertyChanged(nameof(CanEditTarget));

    partial void OnCheckReportTextChanged(string value) => OnPropertyChanged(nameof(HasCheckReport));

    partial void OnSecurityTextChanged(string value) => OnPropertyChanged(nameof(HasSecurity));

    partial void OnShowEntriesOnlyChanged(bool value) => RebuildRows();

    partial void OnLeftDiffOnlyChanged(bool value) => RebuildRows();

    partial void OnRightDiffOnlyChanged(bool value) => RebuildRows();

    // ==================== 加载 ====================

    private void LoadSources()
    {
        Sources.Clear();
        if (_sources is null) return;
        foreach (var s in _sources.Load().Where(s => s.Enabled)) Sources.Add(s);
        SelectedSource = Sources.FirstOrDefault();
        OnPropertyChanged(nameof(HasSources));
    }

    [RelayCommand]
    private void Refresh()
    {
        var keepName = SelectedProfile?.Name;

        Profiles.Clear();
        foreach (var p in _svc.Load().OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            HostsCheckService.ResetResults(p.Entries);
            Profiles.Add(p);
        }
        OnPropertyChanged(nameof(HasProfiles));

        RefreshTargetSources();
        SelectedTarget = TargetSources[0];
        SelectedProfile = Profiles.FirstOrDefault(p => p.Name == keepName) ?? Profiles.FirstOrDefault();
        NotifyToolbarChanged();
    }

    /// <summary>重建左栏数据源下拉（当前系统 hosts + 备份快照，新的在前）。</summary>
    private void RefreshTargetSources()
    {
        var keep = SelectedTarget?.Backup?.Path;
        TargetSources.Clear();
        TargetSources.Add(HostsTargetSource.Current());
        foreach (var b in _svc.ListBackups()) TargetSources.Add(HostsTargetSource.Of(b));
        SelectedTarget = TargetSources.FirstOrDefault(t => t.Backup?.Path == keep) ?? TargetSources[0];
    }

    /// <summary>按当前数据源装载左栏草稿。</summary>
    private void LoadTarget(HostsTargetSource? target)
    {
        _draftText = "";
        IsDirty = false;

        if (target is null) { RebuildRows(); return; }

        var (ok, text, msg) = target.IsCurrent
            ? _svc.ReadSystemHosts()
            : HostsService.ReadTextFile(target.Backup!.Path);

        var (canWrite, why) = _svc.CanWrite();
        CanWriteSystem = canWrite && target.IsCurrent;
        PermissionText = target.IsCurrent
            ? canWrite ? "🔓 可写" : "🔒 只读（" + why + "）"
            : "🔒 备份快照 · 只读";

        if (!ok)
        {
            FileInfoText = msg;
            ManagedInfoText = "—";
            RebuildRows();
            StatusText = "⚠ " + msg;
            return;
        }

        _draftText = text;
        FileInfoText = BuildFileInfo(text, target);
        var (found, startLine, endLine, count) = HostsService.ManagedRange(text);
        ManagedInfoText = found ? $"受管块 第 {startLine}~{endLine} 行 · {count} 条" : "无受管块";

        RebuildRows();
        StatusText = target.IsCurrent
            ? $"已读入系统 hosts（{text.Replace("\r\n", "\n").Split('\n').Length} 行）"
            : $"正在查看备份 {target.Backup!.TimeText}（只读，可点「♻ 还原」）";
    }

    private string BuildFileInfo(string text, HostsTargetSource target)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Length;
        if (!target.IsCurrent) return $"{target.Backup!.SizeText} · {lines} 行 · {target.Backup.TimeText}";

        try
        {
            var info = new FileInfo(_svc.SystemHostsPath);
            return info.Exists
                ? $"{info.Length} 字节 · {lines} 行 · 修改于 {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}"
                : $"{lines} 行";
        }
        catch { return $"{lines} 行"; }
    }

    // ==================== 行投影 + 差异 ====================

    /// <summary>重算差异并重建两侧行集合（唯一入口：任何改动后都调它）。</summary>
    private void RefreshRows()
    {
        var prevLeftNo = SelectedSystemRow?.No;
        var prevRightNo = SelectedProfileRow?.No;

        var leftRows = HostsService.ParseLines(_draftText);
        var leftEntries = leftRows.Where(r => r.IsEntry).ToList();

        var rightRows = new List<HostsLine>();
        if (SelectedProfile is { } profile)
        {
            for (var i = 0; i < profile.Entries.Count; i++)
            {
                var e = profile.Entries[i];
                rightRows.Add(new HostsLine
                {
                    No = i + 1,
                    Raw = e.ToLine(),
                    IsEntry = true,
                    Enabled = e.Enabled,
                    Ip = e.Ip,
                    Host = e.Host,
                    Comment = e.Comment,
                    InManaged = true
                });
            }
        }

        var leftMap = ToMap(leftEntries);
        var rightMap = ToMap(rightRows);

        foreach (var r in leftEntries)
            r.Mark = !rightMap.TryGetValue(r.Host, out var ip) ? "－"
                : !string.Equals(ip, r.Ip, StringComparison.OrdinalIgnoreCase) ? "≠" : "";
        foreach (var r in rightRows)
            r.Mark = !leftMap.TryGetValue(r.Host, out var ip) ? "＋"
                : !string.Equals(ip, r.Ip, StringComparison.OrdinalIgnoreCase) ? "≠" : "";

        var added = rightMap.Keys.Count(k => !leftMap.ContainsKey(k));
        var removed = leftMap.Keys.Count(k => !rightMap.ContainsKey(k));
        var changed = rightMap.Count(kv => leftMap.TryGetValue(kv.Key, out var ip) && !string.Equals(ip, kv.Value, StringComparison.OrdinalIgnoreCase));
        DiffSummary = added + removed + changed == 0
            ? "两侧条目一致"
            : $"右栏相对左栏：＋{added} 新增 · －{removed} 缺失 · ≠{changed} 改 IP";

        SystemRows.Clear();
        if (!IsTextView)
        {
            foreach (var r in leftRows)
            {
                if (ShowEntriesOnly && !r.IsEntry) continue;
                if (LeftDiffOnly && r.Mark.Length == 0) continue;
                SystemRows.Add(r);
            }
        }
        LeftCountText = IsTextView
            ? $"原文 {_draftText.Replace("\r\n", "\n").Split('\n').Length} 行"
            : $"共 {SystemRows.Count} 行 · 条目 {leftEntries.Count}";

        ProfileRows.Clear();
        foreach (var r in rightRows)
        {
            if (RightDiffOnly && r.Mark.Length == 0) continue;
            ProfileRows.Add(r);
        }
        RightCountText = $"共 {ProfileRows.Count} 条";

        _suppressSelectionSync = true;
        SelectedSystemRow = prevLeftNo == null ? null : SystemRows.FirstOrDefault(r => r.No == prevLeftNo);
        SelectedProfileRow = prevRightNo == null ? null : ProfileRows.FirstOrDefault(r => r.No == prevRightNo);
        _suppressSelectionSync = false;

        if (SelectedProfileRow is { } rightRow) LoadEditBuffer(rightRow, false);
        else if (SelectedSystemRow is { } leftRow) LoadEditBuffer(leftRow, true);
        else ClearEditBuffer();

        OnPropertyChanged(nameof(EmptyLeftHint));
        OnPropertyChanged(nameof(EmptyRightHint));
        OnPropertyChanged(nameof(HasNoSystemRows));
        OnPropertyChanged(nameof(HasNoProfileRows));
        OnPropertyChanged(nameof(CanApplyToTarget));
    }

    private void RebuildRows() => RefreshRows();

    private static Dictionary<string, string> ToMap(IEnumerable<HostsLine> rows)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows) map[r.Host] = r.Ip;
        return map;
    }

    // ==================== 左侧草稿 ====================

    /// <summary>文本视图的双向绑定（条目视图下不参与编辑）。</summary>
    public string DraftText
    {
        get => _draftText;
        set
        {
            if (_draftText == value) return;
            _draftText = value ?? "";
            OnPropertyChanged();
            if (IsTextView)
            {
                IsDirty = true;
                RefreshRows();
            }
        }
    }

    [RelayCommand]
    private void ToggleTextView() => IsTextView = !IsTextView;

    [RelayCommand]
    private void ClearManaged()
    {
        if (!CanEditTarget) { StatusText = "⚠ 备份快照 / 只读文件不能改。"; return; }

        _draftText = HostsService.StripManagedBlock(_draftText);
        IsDirty = true;
        RefreshRows();
        StatusText = "已从草稿移除受管块（点「✅ 写入系统 hosts」才落盘）。";
    }

    /// <summary>把右栏方案（启用条目）写进左栏草稿的受管块。</summary>
    [RelayCommand]
    private void ApplyToTarget()
    {
        if (TargetIsBackup) { StatusText = "⚠ 备份快照只读：切回「当前系统 hosts」再应用。"; return; }
        if (SelectedProfile is not { } profile || profile.Entries.Count == 0)
        {
            StatusText = "⚠ 右栏没有可应用的条目。";
            return;
        }

        var enabled = profile.Entries.Where(e => e.Enabled).ToList();
        _draftText = HostsService.StripManagedBlock(_draftText).TrimEnd('\n') + "\n\n" + HostsService.BuildManagedBlock(enabled);

        IsDirty = true;
        RefreshRows();
        StatusText = $"已把「{profile.Name}」写入左侧草稿（受管块 {enabled.Count} 条），点「✅ 写入系统 hosts」落盘。";
    }

    /// <summary>草稿写盘（二次确认 + 自动备份）。</summary>
    [RelayCommand]
    private async Task WriteAsync()
    {
        if (TargetIsBackup) { StatusText = "⚠ 备份快照只读：切回「当前系统 hosts」再写入。"; return; }
        if (!CanWriteSystem) { StatusText = "⚠ 没有写权限：" + PermissionText + " —— 请以管理员身份运行本程序。"; return; }

        var ok = await DialogHooks.ConfirmAsync(
            "要把左侧草稿写入系统 hosts 吗？\n" +
            "写入前会自动备份当前文件（可在左栏下拉里选中备份还原）。");
        if (!ok) { StatusText = "已取消写入。"; return; }

        var (written, msg) = _svc.WriteRaw(_draftText);
        if (!written) { StatusText = "⚠ " + msg; return; }

        IsDirty = false;
        RefreshTargetSources();
        SelectedTarget = TargetSources[0];
        StatusText = msg + (_settings?.HostsFlushDnsAfterApply == true ? "（DNS 刷新请在「网络 Ping」页操作）" : "");
    }

    // ==================== 备份 ====================

    [RelayCommand]
    private void BackupNow()
    {
        var (ok, msg, _) = _svc.BackupNow("手动备份");
        StatusText = ok ? msg : "⚠ " + msg;
        RefreshTargetSources();
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (SelectedTarget?.Backup is not { } backup) return;

        var ok = await DialogHooks.ConfirmAsync(
            $"要把系统 hosts 整份换回 {backup.TimeText} 那份备份吗？\n" +
            "还原前的当前文件会再备份一份，可以再换回来。");
        if (!ok) { StatusText = "已取消还原。"; return; }

        var (done, msg) = _svc.Restore(backup.Path);
        StatusText = done ? msg : "⚠ " + msg;
        Refresh();
    }

    [RelayCommand]
    private async Task DeleteBackupAsync()
    {
        if (SelectedTarget?.Backup is not { } backup) return;

        var ok = await DialogHooks.ConfirmAsync($"删除备份 {backup.Name}？\n（只删备份文件，不动系统 hosts）");
        if (!ok) return;

        var (done, msg) = _svc.DeleteBackup(backup.Path);
        StatusText = done ? msg : "⚠ " + msg;
        RefreshTargetSources();
        SelectedTarget = TargetSources[0];
    }

    [RelayCommand]
    private void OpenBackupFolder()
    {
        try
        {
            Directory.CreateDirectory(_svc.BackupDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_svc.BackupDir) { UseShellExecute = true });
        }
        catch (Exception ex) { StatusText = "⚠ 打不开备份目录：" + ex.Message; }
    }

    // ==================== 方案 ====================

    [RelayCommand]
    private async Task NewProfileAsync()
    {
        var name = await DialogHooks.InputAsync("新建方案", "新方案");
        if (string.IsNullOrWhiteSpace(name)) return;

        var p = new HostsProfile { Name = name.Trim() };
        Profiles.Add(p);
        OnPropertyChanged(nameof(HasProfiles));
        SelectedProfile = p;
        await PersistAsync();
        StatusText = "已新建空方案：在右栏详情卡点「➕ 新增条目」。";
    }

    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile is not { } profile) return;

        var ok = await DialogHooks.ConfirmAsync($"删除方案「{profile.Name}」？\n（⛔ 不动系统 hosts）");
        if (!ok) return;

        Profiles.Remove(profile);
        await PersistAsync();
        SelectedProfile = Profiles.FirstOrDefault();
        StatusText = "已删除方案。";
    }

    /// <summary>把右栏条目落库（条目编辑本身即时改内存，这里只持久化）。</summary>
    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        if (SelectedProfile is not { } profile) return;

        HostsCheckService.ResetResults(profile.Entries);
        await PersistAsync();
        RefreshRows();
        StatusText = $"已保存方案「{profile.Name}」（{profile.Entries.Count} 条）。";
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (SelectedProfile is not { } profile) return;

        var path = await DialogHooks.SaveFileAsync("导出 hosts 文本", null, SafeFileName(profile.Name) + ".hosts", "*.hosts|*.*");
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            File.WriteAllText(path, HostsService.BuildManagedBlock(profile));
            StatusText = $"已导出受管块 → {path}";
        }
        catch (Exception ex) { StatusText = "⚠ 导出失败：" + ex.Message; }
    }

    [RelayCommand]
    private async Task ImportFileAsync()
    {
        if (SelectedProfile is not { } profile) { StatusText = "先选一个方案（或新建一个）再导入。"; return; }

        var files = await DialogHooks.PickFilesAsync("导入 hosts 文件", null, false, "*.hosts|*.txt|*.*");
        if (files.Count == 0) return;

        var (ok, msg, entries) = HostsService.ImportFile(files[0]);
        if (!ok) { StatusText = "⚠ " + msg; return; }

        profile.Entries = entries;
        profile.Source = "file:" + Path.GetFileName(files[0]);
        HostsCheckService.ResetResults(entries);
        await PersistAsync();
        RefreshRows();
        StatusText = msg;
    }

    private async Task PersistAsync() => await Task.Run(() => _svc.Save(Profiles.ToList()));

    // ==================== 源订阅 ====================

    [RelayCommand]
    private async Task FetchSourceAsync()
    {
        if (_sources is null) return;
        if (SelectedSource is not { } source) { StatusText = "先在「网络工具设置」里添加一个 hosts 源。"; return; }

        try
        {
            IsBusy = true;
            StatusText = $"正在拉取「{source.Name}」…";
            var result = await _sources.FetchAsync(source);
            if (!result.Ok) { StatusText = "⚠ 拉取失败：" + result.Message; return; }

            var target = Profiles.FirstOrDefault(p =>
                string.Equals(p.Source, "source:" + source.Id, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                target = new HostsProfile { Name = source.Name, Source = "source:" + source.Id };
                Profiles.Add(target);
            }

            target.Entries = result.Entries;
            target.LastFetchedAt = source.LastFetchedAt;
            target.LastFetchedCount = source.LastCount;

            _sources.Save(Sources);
            await PersistAsync();

            OnPropertyChanged(nameof(HasProfiles));
            SelectedProfile = target;
            StatusText = result.Message + "（已写入右栏，点「⬅ 应用到左侧」+「✅ 写入系统 hosts」才动系统文件）";
        }
        finally { IsBusy = false; }
    }

    // ==================== 检测 ====================

    [RelayCommand]
    private async Task CheckAsync()
    {
        if (_check is null || SelectedProfile is not { } profile) return;

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
            RefreshRows();

            var ok = profile.Entries.Count(e => e.Status == HostsEntryStatus.Ok);
            var mismatch = profile.Entries.Count(e => e.Status == HostsEntryStatus.Mismatch);
            var fail = profile.Entries.Count(e => e.Status is HostsEntryStatus.Unreachable or HostsEntryStatus.Timeout);
            StatusText = (fail + mismatch) == 0
                ? $"检测完成：{ok} 条可达 · 未发现问题"
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

    private static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Length == 0 ? "hosts" : name;
    }

    // ==================== 详情面板 ====================

    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;
    public object? DetailContent => null;

    public event EventHandler? DetailChanged;

    [RelayCommand]
    private void ToggleDetailPanel()
    {
        IsDetailVisible = !IsDetailVisible;
        OnPropertyChanged(nameof(IsDetailOpen));
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenDetailPanel()
    {
        IsDetailVisible = true;
        OnPropertyChanged(nameof(IsDetailOpen));
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    void IDetailPanelProvider.ToggleDetail() => ToggleDetailPanelCommand.Execute(null);
    void IDetailPanelProvider.OpenDetail() => OpenDetailPanelCommand.Execute(null);

    public DetailCardDescriptor? Card => _card ??= BuildCard();

    // ==================== 状态栏 ====================

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    public event EventHandler? StatusChanged;
}
