using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.Shell.ViewModels;

/// <summary>
/// 「设置 → 数据」节：备份节清单 + 勾选状态 + 配置文件清单。
/// <para>
/// 🔴 真正的打包 / 恢复逻辑在 <c>Core.Services.BackupService</c>，本 VM **不碰磁盘** —— 
/// 勾选状态与大小只用于界面展示；导出 / 恢复由 code-behind 走文件选择器后调用服务。
/// </para>
/// <para>
/// 「设置备份 / 数据备份」两个入口 = 同一份节清单的不同默认勾选（<c>IBackupSection.DefaultSelected</c>）：
/// 设置节（<c>shell</c> / <c>module.*</c>）默认勾、数据节与 <c>env</c> 默认不勾。
/// </para>
/// </summary>
public partial class DataViewModel : ObservableObject, NewToys.Shared.Services.IStatusProvider
{
    /// <summary>可备份的节（顺序由服务排好）。</summary>
    public ObservableCollection<BackupSectionRow> Sections { get; } = new();

    /// <summary>当前配置档的配置文件清单（只读展示）。</summary>
    public ObservableCollection<ConfigFileDisplay> ConfigFiles { get; } = new();

    [ObservableProperty] private string _statusText = "就绪";

    // v3.9：接入 Shell 底部状态栏契约（状态文本由 Shell 统一显示）
    public event EventHandler? StatusChanged;
    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    [ObservableProperty] private string _lastBackupPath = "";

    [ObservableProperty] private bool _isBusy;

    /// <summary>勾选摘要（「已选 2 节 · 约 12.3 KB」+ 不勾数据时的说明）。</summary>
    [ObservableProperty] private string _selectionSummary = "";

    public DataViewModel()
    {
        RefreshFileList();
    }

    [RelayCommand]
    private void RefreshFileList()
    {
        // 真正的文件枚举由 DataView.axaml.cs 在加载时调用 Refresh 命令完成；
        // 这里先清空展示，等待 code-behind 填充。
        ConfigFiles.Clear();
        StatusText = $"配置列表已重列 · {DateTime.Now:HH:mm:ss}";
    }

    /// <summary>勾选变化 / 节清单重建后重算摘要。</summary>
    public void RebuildSelectionSummary()
    {
        var picked = Sections.Where(s => s.IsSelected).ToList();
        if (picked.Count == 0)
        {
            SelectionSummary = "没有勾选任何节 —— 导出会提示先勾选。";
            return;
        }

        var bytes = picked.Sum(s => s.SizeBytes);
        var hasData = picked.Any(s => s.IsData);
        SelectionSummary = hasData
            ? $"已选 {picked.Count} 节 · 约 {FormatSize(bytes)}（含数据节，体积可能很大）"
            : $"已选 {picked.Count} 节 · 约 {FormatSize(bytes)}（= 设置备份）";
    }

    internal static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:0.1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:0.00} GB";
    }
}

/// <summary>一节在界面上的行：标题 / 说明 / 大小 / 层级 + 勾选。</summary>
public partial class BackupSectionRow : ObservableObject
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>层级短标签（本机 / 设置 / 模块 / 数据）。</summary>
    public string ScopeText { get; init; } = "";
    public bool IsMachineBound { get; init; }
    /// <summary>是否是数据节（决定摘要里的提示语）。</summary>
    public bool IsData { get; init; }

    /// <summary>估算字节数（先在后台算好再回填，⛔ 别在 UI 线程递归缩略图目录）。</summary>
    [ObservableProperty] private long _sizeBytes;

    /// <summary>大小文案（随 <see cref="SizeBytes"/> 自动刷新；未算完时显示「…」）。</summary>
    [ObservableProperty] private string _sizeText = "…";

    partial void OnSizeBytesChanged(long value) => SizeText = DataViewModel.FormatSize(value);

    [ObservableProperty] private bool _isSelected;

    /// <summary>勾选变化（VM 订阅后重算摘要；⛔ 别在这里写磁盘或跑估算）。</summary>
    public event EventHandler? SelectionChanged;

    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke(this, EventArgs.Empty);
}

public partial class ConfigFileDisplay : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private long _sizeBytes;
    [ObservableProperty] private DateTime _lastWrite;
    public string SizeDisplay
    {
        get
        {
            if (SizeBytes < 1024) return $"{SizeBytes} B";
            if (SizeBytes < 1024 * 1024) return $"{SizeBytes / 1024.0:0.0} KB";
            return $"{SizeBytes / 1024.0 / 1024.0:0.1} MB";
        }
    }
}
