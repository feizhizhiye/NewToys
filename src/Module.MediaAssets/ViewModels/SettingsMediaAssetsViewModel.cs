using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.MediaAssets.Models;
using NewToys.MediaAssets.Services;
using NewToys.Shared.Services;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>设置页下拉用的一项（标签 + 值）。</summary>
public sealed class ChoiceOption
{
    public string Label { get; init; } = "";
    public string Value { get; init; } = "";
    public override string ToString() => Label;
}

/// <summary>
/// 「媒体资产」模块设置页（NT-021）：View/VM 归 <c>Module.MediaAssets</c>，由
/// <c>MediaAssetsModule.Initialize</c> 自行注册进 <c>settings</c> 分组（Order 100，跨组注册走延迟补登）。
/// <para>
/// 两类生效方式（规范 §4.9）：开关 / 数值 / 路径 = **即时生效**（改即写 <c>config\media-assets.json</c>，
/// ⛔ 无保存按钮）；忽略规则 / 命名模板等多行文本 = **显式 💾 保存**（一次写全，避免每敲一个字都落盘）。
/// </para>
/// </summary>
public partial class SettingsMediaAssetsViewModel : ObservableObject, IStatusProvider
{
    private readonly MediaAssetsSettingsIO _io;
    private readonly ScanService _scan;
    private readonly FileOperationService _fileOps;
    private readonly IAppConfigService? _app;

    /// <summary>批量灌值时抑制"改属性 → 回写设置"（防递归 + 无谓写盘）。</summary>
    private bool _syncing;

    public SettingsMediaAssetsViewModel() { }

    public SettingsMediaAssetsViewModel(MediaAssetsSettingsIO io, ScanService scan, FileOperationService fileOps,
        IAppConfigService? app = null)
    {
        _io = io;
        _scan = scan;
        _fileOps = fileOps;
        _app = app;

        _syncing = true;
        var s = io.Settings;

        IgnoreHiddenFiles = s.IgnoreHiddenFiles;
        IgnoreTempFiles = s.IgnoreTempFiles;
        QuickHashSampleMb = s.QuickHashSampleMb;
        AutoExtractMetadata = s.AutoExtractMetadata;
        AutoGenerateThumbnails = s.AutoGenerateThumbnails;
        ThumbFrameMode = string.Equals(s.ThumbFrameMode, "seconds", StringComparison.OrdinalIgnoreCase) ? "seconds" : "percent";
        ThumbFramePercent = s.ThumbFramePercent;
        ThumbFrameSeconds = s.ThumbFrameSeconds;
        ImageThumbSize = s.ImageThumbSize;
        ThumbsDirectory = s.ThumbsDirectory;
        TrashRetentionDays = s.TrashRetentionDays;
        AutoCleanupTrash = s.AutoCleanupTrash;
        DeleteToRecycleBin = s.DeleteToRecycleBin;
        ConflictPolicy = s.ConflictPolicy;

        IgnoreExtensions = s.IgnoreExtensions;
        IgnoreDirectories = s.IgnoreDirectories;
        NameTemplateVideo = s.NameTemplateVideo;
        NameTemplateMusic = s.NameTemplateMusic;
        NameTemplateBook = s.NameTemplateBook;
        NameTemplateImage = s.NameTemplateImage;
        _syncing = false;

        StatusText = Summary();
    }

    // ==================== 即时生效：开关 / 数值 ====================

    [ObservableProperty] private bool _ignoreHiddenFiles;
    [ObservableProperty] private bool _ignoreTempFiles;
    [ObservableProperty] private int _quickHashSampleMb = 1;
    [ObservableProperty] private bool _autoExtractMetadata = true;
    [ObservableProperty] private bool _autoGenerateThumbnails = true;
    [ObservableProperty] private int _imageThumbSize = 320;
    [ObservableProperty] private int _trashRetentionDays = 30;
    [ObservableProperty] private bool _autoCleanupTrash;
    [ObservableProperty] private bool _deleteToRecycleBin = true;
    [ObservableProperty] private string _thumbFrameMode = "percent";
    [ObservableProperty] private double _thumbFramePercent = 25;
    [ObservableProperty] private double _thumbFrameSeconds = 5;
    [ObservableProperty] private string _thumbsDirectory = "";

    partial void OnIgnoreHiddenFilesChanged(bool value) => SaveScanSetting(s => s.IgnoreHiddenFiles = value, "忽略隐藏文件");
    partial void OnIgnoreTempFilesChanged(bool value) => SaveScanSetting(s => s.IgnoreTempFiles = value, "忽略临时文件");

    partial void OnQuickHashSampleMbChanged(int value)
        => SaveScanSetting(s => s.QuickHashSampleMb = Math.Clamp(value, 0, 64), "quick_hash 采样大小");

    partial void OnAutoExtractMetadataChanged(bool value)
        => SaveSetting(s => s.AutoExtractMetadata = value, value ? "扫描到新文件将自动提取元数据。" : "已关闭自动提取元数据。");

    partial void OnAutoGenerateThumbnailsChanged(bool value)
        => SaveSetting(s => s.AutoGenerateThumbnails = value, value ? "扫描到新文件将自动生成缩略图。" : "已关闭自动生成缩略图。");

    partial void OnImageThumbSizeChanged(int value)
        => SaveSetting(s => s.ImageThumbSize = Math.Clamp(value, 16, 4096), "缩略图尺寸");

    partial void OnThumbFrameModeChanged(string value)
    {
        SaveSetting(s => s.ThumbFrameMode = value ?? "percent", "抽帧位置模式");
        OnPropertyChanged(nameof(IsFramePercentMode));
        OnPropertyChanged(nameof(IsFrameSecondsMode));
    }

    /// <summary>抽帧位置模式 = 百分比（右侧百分比输入框可见）。</summary>
    public bool IsFramePercentMode => !string.Equals(ThumbFrameMode, "seconds", StringComparison.OrdinalIgnoreCase);

    /// <summary>抽帧位置模式 = 固定秒数（右侧秒输入框可见）。</summary>
    public bool IsFrameSecondsMode => !IsFramePercentMode;

    partial void OnThumbFramePercentChanged(double value)
        => SaveSetting(s => s.ThumbFramePercent = Math.Clamp(value, 0, 100), "抽帧位置（百分比）");

    partial void OnThumbFrameSecondsChanged(double value)
        => SaveSetting(s => s.ThumbFrameSeconds = Math.Max(0, value), "抽帧位置（秒）");

    partial void OnThumbsDirectoryChanged(string value)
        => SaveSetting(s => s.ThumbsDirectory = value ?? "", "缩略图目录");

    partial void OnTrashRetentionDaysChanged(int value)
        => SaveSetting(s => s.TrashRetentionDays = Math.Clamp(value, 1, 3650), "回收站保留天数");

    partial void OnAutoCleanupTrashChanged(bool value)
        => SaveSetting(s => s.AutoCleanupTrash = value,
            value ? "已开启到期自动清理（下次启动 + 手动清理都会生效）。" : "已关闭到期自动清理。");

    partial void OnDeleteToRecycleBinChanged(bool value)
        => SaveSetting(s => s.DeleteToRecycleBin = value,
            value ? "删除默认送系统回收站。" : "⚠️ 删除将直接永久删除（不进回收站）——请谨慎。");

    partial void OnConflictPolicyChanged(string value)
        => SaveSetting(s => s.ConflictPolicy = value ?? "append", "重命名冲突策略");

    // ==================== 显式保存：多行文本 ====================

    [ObservableProperty] private string _ignoreExtensions = "";
    [ObservableProperty] private string _ignoreDirectories = "";
    [ObservableProperty] private string _nameTemplateVideo = "";
    [ObservableProperty] private string _nameTemplateMusic = "";
    [ObservableProperty] private string _nameTemplateBook = "";
    [ObservableProperty] private string _nameTemplateImage = "";

    // ==================== 只读展示 ====================

    public string ConfigFilePath => _io.FilePath;

    /// <summary>缩略图目录（生效值：配置为空 → <c>&lt;应用根&gt;\data\thumbs</c>）。</summary>
    public string ThumbsDirectoryEffective => _io.ThumbsDirectory;

    /// <summary>导入导出默认目录（全局设置 <c>app.json</c> 的 VideoDbFolder；⛔ 本页不新建字段）。</summary>
    public string ImportExportDefaultDir => _app?.VideoDbFolder ?? "";

    public bool HasImportExportDir => !string.IsNullOrWhiteSpace(ImportExportDefaultDir);

    /// <summary>「操作前二次确认」强制开（破坏性操作的底线），UI 置灰。</summary>
    public bool ConfirmBeforeOperation => _io.Settings.ConfirmBeforeOperation;

    public ObservableCollection<ChoiceOption> FrameModeOptions { get; } = new()
    {
        new ChoiceOption { Label = "按时长百分比", Value = "percent" },
        new ChoiceOption { Label = "固定秒数", Value = "seconds" }
    };

    public ObservableCollection<ChoiceOption> ConflictOptions { get; } = new()
    {
        new ChoiceOption { Label = "追加序号 (1)", Value = "append" },
        new ChoiceOption { Label = "短哈希后缀", Value = "hash" },
        new ChoiceOption { Label = "跳过（不重命名）", Value = "skip" },
        new ChoiceOption { Label = "覆盖（需确认）", Value = "overwrite" }
    };

    [ObservableProperty] private string _statusText = "就绪";

    public event EventHandler? StatusChanged;
    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    private string Summary()
        => $"设置文件：{ConfigFilePath}（即时生效的项已自动写盘）";

    // ==================== 命令 ====================

    /// <summary>💾 保存文本设置（忽略规则 / 命名模板 / 冲突策略）：一次写全，并应用到扫描器。</summary>
    [RelayCommand]
    private void SaveTextSettings()
    {
        var s = _io.Settings;
        s.IgnoreExtensions = IgnoreExtensions ?? "";
        s.IgnoreDirectories = IgnoreDirectories ?? "";
        s.NameTemplateVideo = NameTemplateVideo ?? "";
        s.NameTemplateMusic = NameTemplateMusic ?? "";
        s.NameTemplateBook = NameTemplateBook ?? "";
        s.NameTemplateImage = NameTemplateImage ?? "";
        s.ConflictPolicy = ConflictPolicy ?? "append";

        _io.Save();
        _scan.ApplySettings(s);
        StatusText = "已保存忽略规则 / 命名模板 / 冲突策略（下次扫描即按新规则）。";
    }

    /// <summary>🧹 立即清理过期回收站（超过保留天数的资产 → 永久删除；走任务队列，任务中心可见）。</summary>
    [RelayCommand]
    private async Task CleanupTrashNowAsync()
    {
        var days = TrashRetentionDays;
        if (!await DialogHooks.ConfirmAsync(
                $"立即清理回收站里超过 {days} 天的资产？\n" +
                "会**真删磁盘文件**（⛔ 不进回收站、无法还原）并删掉库里的索引行。\n" +
                "删除过程排进任务队列，可在「任务中心」看进度 / 取消。").ConfigureAwait(false))
        {
            StatusText = "已取消。";
            return;
        }

        var n = _fileOps.EnqueueExpiredTrashPurge(days);
        StatusText = n == 0
            ? $"回收站里没有超过 {days} 天的资产。"
            : $"已把 {n} 个过期资产的永久删除排进任务队列（进度看「任务中心」）。";
    }

    /// <summary>📂 选缩略图目录（留空 = 恢复默认 <c>data\thumbs</c>）。</summary>
    public void SetThumbsDirectory(string dir) => ThumbsDirectory = dir ?? "";

    /// <summary>↺ 恢复默认：把全部设置回默认值并保存（即时生效项同步应用）。</summary>
    [RelayCommand]
    private void ResetDefaults()
    {
        var fresh = new MediaAssetsSettings();
        _syncing = true;
        try
        {
            IgnoreHiddenFiles = fresh.IgnoreHiddenFiles;
            IgnoreTempFiles = fresh.IgnoreTempFiles;
            QuickHashSampleMb = fresh.QuickHashSampleMb;
            AutoExtractMetadata = fresh.AutoExtractMetadata;
            AutoGenerateThumbnails = fresh.AutoGenerateThumbnails;
            ThumbFrameMode = fresh.ThumbFrameMode;
            ThumbFramePercent = fresh.ThumbFramePercent;
            ThumbFrameSeconds = fresh.ThumbFrameSeconds;
            ImageThumbSize = fresh.ImageThumbSize;
            ThumbsDirectory = fresh.ThumbsDirectory;
            TrashRetentionDays = fresh.TrashRetentionDays;
            AutoCleanupTrash = fresh.AutoCleanupTrash;
            DeleteToRecycleBin = fresh.DeleteToRecycleBin;
            ConflictPolicy = fresh.ConflictPolicy;
            IgnoreExtensions = fresh.IgnoreExtensions;
            IgnoreDirectories = fresh.IgnoreDirectories;
            NameTemplateVideo = fresh.NameTemplateVideo;
            NameTemplateMusic = fresh.NameTemplateMusic;
            NameTemplateBook = fresh.NameTemplateBook;
            NameTemplateImage = fresh.NameTemplateImage;
        }
        finally { _syncing = false; }

        // 一次性把默认值灌进活设置并落盘（上面逐项赋值时被 _syncing 挡回了）
        var s = _io.Settings;
        s.IgnoreHiddenFiles = fresh.IgnoreHiddenFiles;
        s.IgnoreTempFiles = fresh.IgnoreTempFiles;
        s.QuickHashSampleMb = fresh.QuickHashSampleMb;
        s.AutoExtractMetadata = fresh.AutoExtractMetadata;
        s.AutoGenerateThumbnails = fresh.AutoGenerateThumbnails;
        s.ThumbFrameMode = fresh.ThumbFrameMode;
        s.ThumbFramePercent = fresh.ThumbFramePercent;
        s.ThumbFrameSeconds = fresh.ThumbFrameSeconds;
        s.ImageThumbSize = fresh.ImageThumbSize;
        s.ThumbsDirectory = fresh.ThumbsDirectory;
        s.TrashRetentionDays = fresh.TrashRetentionDays;
        s.AutoCleanupTrash = fresh.AutoCleanupTrash;
        s.DeleteToRecycleBin = fresh.DeleteToRecycleBin;
        s.ConflictPolicy = fresh.ConflictPolicy;
        s.IgnoreExtensions = fresh.IgnoreExtensions;
        s.IgnoreDirectories = fresh.IgnoreDirectories;
        s.NameTemplateVideo = fresh.NameTemplateVideo;
        s.NameTemplateMusic = fresh.NameTemplateMusic;
        s.NameTemplateBook = fresh.NameTemplateBook;
        s.NameTemplateImage = fresh.NameTemplateImage;

        _io.Save();
        _scan.ApplySettings(s);
        OnPropertyChanged(nameof(ThumbsDirectoryEffective));
        OnPropertyChanged(nameof(ConfirmBeforeOperation));
        StatusText = "已恢复默认设置并保存。";
    }

    // ==================== 内部 ====================

    /// <summary>即时生效项的统一落点：改活设置 → 落盘 → 状态栏反馈。</summary>
    private void SaveSetting(Action<MediaAssetsSettings> apply, string label)
    {
        if (_syncing) return;
        apply(_io.Settings);
        _io.Save();
        OnPropertyChanged(nameof(ThumbsDirectoryEffective));
        StatusText = $"已应用「{label}」并写入 {System.IO.Path.GetFileName(ConfigFilePath)}。";
    }

    /// <summary>扫描相关项：落盘后还要把新规则应用到 <see cref="ScanService"/>（下次扫描即生效）。</summary>
    private void SaveScanSetting(Action<MediaAssetsSettings> apply, string label)
    {
        if (_syncing) return;
        apply(_io.Settings);
        _io.Save();
        _scan.ApplySettings(_io.Settings);
        StatusText = $"已应用「{label}」并写入 {System.IO.Path.GetFileName(ConfigFilePath)}（下次扫描即按新规则）。";
    }
}
