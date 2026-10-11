using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NewToys.Core.Services;
using NewToys.PcTools.Models;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.PcTools.ViewModels;

/// <summary>
/// 「电脑工具 → 系统信息」页 —— 一张**列表**：把硬件 / 系统 / 运行时的关键事实
/// 与「设置 → 路径配置」里的**默认路径**并排陈列（用户点这里就能核对程序到底在用什么目录）。
/// <para>
/// 布局沿用规范 3.6 列表页（左表头 + 列表、右详情卡、右键「增删改查」里的"查"），
/// 列显隐 / 详情开合 / 排序 / 多项筛选全部继承自 <see cref="ListPageViewModelBase"/>。
/// </para>
/// <para>
/// 信息怎么来：能用 BCL 就拿 BCL（<c>Environment</c> / <c>DriveInfo</c> / <c>Dns</c> / <c>GC</c>），
/// 只有 CPU 型号要读一次注册表（读不到就写"未知"，**绝不抛异常炸页面**）。
/// </para>
/// </summary>
public partial class PcInfoViewModel : ListPageViewModelBase
{
    private readonly IAppConfigService? _cfg;
    private readonly ConfigStoreService? _store;
    private readonly StartupService? _startup;
    private readonly INavigationService? _nav;

    private readonly List<PcInfoEntry> _all = new();

    public ObservableCollection<PcInfoEntry> Items { get; } = new();

    // v3.9：StatusText 已提到 ListPageViewModelBase（由 Shell 底部状态栏统一显示），这里不再自己声明。
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private PcInfoEntry? _selected;

    public bool IsNotBusy => !IsBusy;
    public bool HasSelection => Selected != null;
    public bool HasItems => Items.Count > 0;

    /// <summary>汇总（共几项、按分类怎么分布）。</summary>
    [ObservableProperty] private string _summaryText = "";

    // ==================== 列（与 XAML 表头 / 行一一对应） ====================

    [ObservableProperty] private string _categoryHeader = "分类 ▲";
    [ObservableProperty] private string _nameHeader = "项目";
    [ObservableProperty] private string _valueHeader = "值";

    public bool IsCategoryVisible => Vis("Category");
    public bool IsNameVisible => Vis("Name");
    public bool IsValueVisible => Vis("Value");
    public bool IsNoteVisible => Vis("Note");

    protected override string PageKey => "pc-info";

    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Category", "分类", "1.2*"),
        new("Name", "项目", "2.2*"),
        new("Value", "值", "5*"),
        new("Note", "说明", "2.4*"),
        new("Detail", "", "36", sortable: false),
    };

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public PcInfoViewModel() { }

    public PcInfoViewModel(IAppConfigService cfg, ConfigStoreService store,
                           IColumnLayoutService cols, StartupService startup, INavigationService nav)
        : base(cols)
    {
        _cfg = cfg;
        _store = store;
        _startup = startup;
        _nav = nav;
        LoadLayout();
        Collect();
        RefreshView();
    }

    protected override void RefreshView()
    {
        IEnumerable<PcInfoEntry> q = _all;

        var text = (SearchText ?? "").Trim();
        if (text.Length > 0)
            q = q.Where(i => i.Category.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                          || i.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                          || i.Value.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                          || i.Note.Contains(text, StringComparison.CurrentCultureIgnoreCase));

        q = ApplyFilters(q);

        var byText = StringComparer.CurrentCultureIgnoreCase;
        q = SortColumn switch
        {
            "Name" => SortDescending ? q.OrderByDescending(i => i.Name, byText) : q.OrderBy(i => i.Name, byText),
            "Value" => SortDescending ? q.OrderByDescending(i => i.Value, byText) : q.OrderBy(i => i.Value, byText),
            _ => SortDescending
                ? q.OrderByDescending(i => i.Category, byText).ThenBy(i => i.Name, byText)
                : q.OrderBy(i => i.Category, byText).ThenBy(i => i.Name, byText)
        };

        Items.Clear();
        foreach (var i in q) Items.Add(i);

        if (Selected != null && !Items.Contains(Selected)) Selected = null;

        UpdateHeaders();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
    }

    /// <summary>筛选取值：按列键拿这一行的文本（多项筛选的"列"下拉框就列这些）。</summary>
    protected override string? FieldText(object row, string columnKey) => columnKey switch
    {
        "Category" => ((PcInfoEntry)row).Category,
        "Name" => ((PcInfoEntry)row).Name,
        "Value" => ((PcInfoEntry)row).Value,
        "Note" => ((PcInfoEntry)row).Note,
        _ => null
    };

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Items.Count > 0) Selected = Items[0];
    }

    private void UpdateHeaders()
    {
        CategoryHeader = "分类" + SortArrow("Category");
        NameHeader = "项目" + SortArrow("Name");
        ValueHeader = "值" + SortArrow("Value");
    }

    partial void OnSearchTextChanged(string value) => RefreshView();

    partial void OnSelectedChanged(PcInfoEntry? value) => OnPropertyChanged(nameof(HasSelection));

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    // ==================== 采集 ====================

    /// <summary>重新采集一次全部信息（点「🔄 重新检测」走这里）。</summary>
    [RelayCommand]
    private async Task DetectAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "正在采集系统信息……";
        try
        {
            await Task.Run(Collect).ConfigureAwait(true);
            RefreshView();
            StatusText = $"已重新检测 · {DateTime.Now:HH:mm:ss} · 共 {_all.Count} 项";
        }
        catch (Exception ex)
        {
            StatusText = "采集失败：" + ex.Message;
        }
        finally { IsBusy = false; }
    }

    private void Collect()
    {
        _all.Clear();

        // ---------- 操作系统 ----------
        _all.Add(new PcInfoEntry("操作系统", "系统版本", RuntimeInformation.OSDescription, "Environment.OSVersion / RuntimeInformation"));
        _all.Add(new PcInfoEntry("操作系统", "平台架构", RuntimeInformation.OSArchitecture.ToString(),
                                 RuntimeInformation.ProcessArchitecture.ToString() + " 进程"));
        _all.Add(new PcInfoEntry("操作系统", "计算机名", Safe(() => Environment.MachineName)));
        _all.Add(new PcInfoEntry("操作系统", "当前用户", Safe(() => Environment.UserName)));
        _all.Add(new PcInfoEntry("操作系统", "系统已运行", FormatUptime(Environment.TickCount64), "开机到现在（TickCount64）"));
        _all.Add(new PcInfoEntry("操作系统", "开机启动项", DescribeStartup(), "「设置 → 通用 → 开机时启动」的真实状态"));

        // ---------- CPU ----------
        var cpu = ReadCpuName();
        _all.Add(new PcInfoEntry("CPU", "处理器", cpu, "读 HKLM\\HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0"));
        _all.Add(new PcInfoEntry("CPU", "逻辑处理器数", Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture),
                                 "Environment.ProcessorCount（含超线程）"));

        // ---------- 内存 ----------
        try
        {
            var info = GC.GetGCMemoryInfo();
            _all.Add(new PcInfoEntry("内存", "可用内存（估计）", FormatSize((long)info.TotalAvailableMemoryBytes),
                                     "GC.GetGCMemoryInfo().TotalAvailableMemoryBytes"));
        }
        catch { /* 读不到就不列这一项 */ }
        _all.Add(new PcInfoEntry("内存", "本进程占用", FormatSize(Environment.WorkingSet), "Environment.WorkingSet（工作集）"));

        // ---------- 磁盘 ----------
        foreach (var d in SafeDrives())
        {
            var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.Name : $"{d.Name}（{d.VolumeLabel}）";
            var used = d.TotalSize - d.AvailableFreeSpace;
            var percent = d.TotalSize > 0 ? (int)(used * 100.0 / d.TotalSize) : 0;
            _all.Add(new PcInfoEntry("磁盘", label,
                                     $"{FormatSize(d.AvailableFreeSpace)} 可用 / {FormatSize(d.TotalSize)}",
                                     $"已用 {percent}% · 文件系统 {d.DriveFormat} · 类型 {d.DriveType}"));
        }

        // ---------- 网络 ----------
        try
        {
            var host = Dns.GetHostName();
            var ips = Dns.GetHostAddresses(host)
                         .Where(a => !a.IsIPv6LinkLocal && !a.IsIPv6Multicast)
                         .Select(a => a.ToString())
                         .Distinct()
                         .ToList();
            _all.Add(new PcInfoEntry("网络", "主机名", host));
            _all.Add(new PcInfoEntry("网络", "本机 IP", ips.Count == 0 ? "（未取到）" : string.Join("、", ips),
                                     "Dns.GetHostAddresses（含 IPv4 / IPv6）"));
        }
        catch { /* 没网也能看别的信息 */ }

        // ---------- 运行时 ----------
        _all.Add(new PcInfoEntry("运行时", ".NET 版本", RuntimeInformation.FrameworkDescription));
        _all.Add(new PcInfoEntry("运行时", "进程启动时间", FormatTime(Process.GetCurrentProcess().StartTime)));
        _all.Add(new PcInfoEntry("运行时", "本进程已运行", FormatSpan(DateTime.Now - Process.GetCurrentProcess().StartTime)));
        _all.Add(new PcInfoEntry("运行时", "程序目录", AppContext.BaseDirectory, "AppContext.BaseDirectory"));
        _all.Add(new PcInfoEntry("运行时", "命令行", Safe(() => Environment.CommandLine)));

        // ---------- 应用路径（=「设置 → 路径配置」里的那几项） ----------
        AppendAppPaths();

        SummaryText = string.Join(" · ", _all.GroupBy(i => i.Category).Select(g => $"{g.Key} {g.Count()}"));
    }

    /// <summary>把「设置 → 路径配置」里的默认路径一并列出（用户核对实际用的是哪个目录）。</summary>
    private void AppendAppPaths()
    {
        if (_cfg == null) return;

        void Add(string name, string value, string note)
            => _all.Add(new PcInfoEntry("应用路径", name, string.IsNullOrWhiteSpace(value) ? "（留空 = 用系统默认）" : value, note));

        Add("ffmpeg 路径", _cfg.FfmpegPath, "留空或写 ffmpeg 则走 PATH；ffprobe 由它推导");
        Add("外部播放器", _cfg.PlayerPath, "视频列表「▶ 播放」用它；留空 = 系统默认关联程序");
        Add("Python 路径", _cfg.PythonPath, "「设置与脚本」里 .py 的解释器");
        Add("PowerShell 7+（pwsh）", _cfg.PwshPath, ".ps1 优先用它");
        Add("Windows PowerShell 5.1", _cfg.PowerShell51Path, "没配 pwsh 时 .ps1 用它");
        Add("脚本目录", Effective(_cfg.ScriptFolder, _cfg.DefaultScriptFolder), "「设置与脚本」页引用的脚本都在这里");
        Add("数据目录", _cfg.DataDirectory, "newtoys.db 等；环境变量 NEWTOYS_HOME 可整根切换");
        Add("配置目录", _store?.ConfigDir ?? "", "app.json / launch.json / pc-scripts.json … 可整体备份");
        Add("更新检查地址", _cfg.UpdateUrl, "「设置 → 通用」用它检查更新；留空 = 不检查");
        Add("配置文件", _cfg.ConfigFilePath, "app.json 全路径");
    }

    /// <summary>「设置 → 通用 → 开机时启动」的真实状态（读注册表，不是读配置）。</summary>
    private string DescribeStartup()
    {
        if (_startup == null) return "（服务未就绪）";
        if (!_startup.IsSupported) return "当前平台不支持";
        return _startup.IsEnabled ? "已启用" : "未启用";
    }

    // ==================== 小工具 ====================

    private static string Effective(string value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string Safe(Func<string> f)
    {
        try { return f(); }
        catch (Exception ex) { return "（取不到：" + ex.Message + "）"; }
    }

    private static IEnumerable<DriveInfo> SafeDrives()
    {
        try
        {
            return DriveInfo.GetDrives()
                            .Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable or DriveType.Network)
                            .ToList();
        }
        catch { return Array.Empty<DriveInfo>(); }
    }

    /// <summary>读 CPU 型号（注册表）；读不到就如实写"未知"。</summary>
    private static string ReadCpuName()
    {
        if (!OperatingSystem.IsWindows()) return "（非 Windows 平台不读注册表）";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", false);
            var v = key?.GetValue("ProcessorNameString") as string;
            return string.IsNullOrWhiteSpace(v) ? "未知（注册表里没有 ProcessorNameString）" : v.Trim();
        }
        catch (Exception ex) { return "未知（" + ex.Message + "）"; }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 0 => "—",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:0.0} MB",
        _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:0.00} GB"
    };

    private static string FormatUptime(long ms) => FormatSpan(TimeSpan.FromMilliseconds(ms));

    private static string FormatSpan(TimeSpan t)
        => t.TotalDays >= 1 ? $"{(int)t.TotalDays} 天 {t.Hours} 小时 {t.Minutes} 分"
                            : $"{t.Hours} 小时 {t.Minutes} 分 {t.Seconds} 秒";

    private static string FormatTime(DateTime dt) => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    // ==================== 操作 ====================

    /// <summary>📋 复制选中行的「项目 = 值」。</summary>
    [RelayCommand]
    private async Task CopySelectedAsync()
    {
        if (Selected == null) { StatusText = "先在左侧选中一行。"; return; }
        await CopyTextAsync($"{Selected.Name} = {Selected.Value}");
    }

    /// <summary>📋 复制全部（分类 / 项目 / 值，制表符分隔，可直接粘进表格）。</summary>
    [RelayCommand]
    private async Task CopyAllAsync()
    {
        if (_all.Count == 0) { StatusText = "还没有采集到内容。"; return; }

        var sb = new StringBuilder();
        sb.AppendLine("分类\t项目\t值\t说明");
        foreach (var i in _all) sb.AppendLine($"{i.Category}\t{i.Name}\t{i.Value}\t{i.Note}");
        await CopyTextAsync(sb.ToString());
    }

    private async Task CopyTextAsync(string text)
    {
        try
        {
            var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (win?.Clipboard == null) { StatusText = "剪贴板不可用。"; return; }
            await win.Clipboard.SetTextAsync(text);
            StatusText = "已复制到剪贴板。";
        }
        catch (Exception ex) { StatusText = "复制失败：" + ex.Message; }
    }

    /// <summary>📂 打开配置目录（顺手核对「设置 → 路径配置」）。</summary>
    [RelayCommand]
    private void OpenConfigDir()
    {
        try
        {
            var dir = _store?.ConfigDir ?? "";
            if (dir.Length == 0 || !Directory.Exists(dir)) { StatusText = "配置目录不存在。"; return; }
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
            StatusText = "已打开配置目录：" + dir;
        }
        catch (Exception ex) { StatusText = "打开失败：" + ex.Message; }
    }

    /// <summary>⚙ 跳到「设置 → 路径配置」（模块内导航走 INavigationService，不依赖 Shell 程序集）。</summary>
    [RelayCommand]
    private void OpenPathSettings() => _nav?.NavigateTo("settings-path");
}
