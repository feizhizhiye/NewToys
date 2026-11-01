using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.Viewer.Services;

namespace NewToys.Viewer.ViewModels;

/// <summary>
/// 查看器页 VM：只管「打开哪个文件 / 最近打开 / 排版参数 / 状态文案」。
/// <para>
/// 🔴 真正的装载与渲染在 <c>NewToys.UI.Viewers.ViewerHost</c>（View 持有它），VM 通过
/// <see cref="OpenRequested"/> 请求 View 打开 —— VM ⛔ 不 new 控件、⛔ 不碰 StorageProvider，
/// 与项目既有约定一致（选择器 / 控件操作归 View）。
/// </para>
/// </summary>
public partial class ViewerViewModel : ObservableObject, IStatusProvider, IToolbarSource
{
    private readonly ViewerSettingsService _settings;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _hint = "";
    [ObservableProperty] private string _currentTitle = "未打开文件";
    [ObservableProperty] private string _currentInfo = "";
    [ObservableProperty] private bool _isText;
    [ObservableProperty] private bool _isImage;
    [ObservableProperty] private bool _hasContent;

    /// <summary>设计期用。</summary>
    public ViewerViewModel()
    {
        _settings = null!;
    }

    public ViewerViewModel(ViewerSettingsService settings)
    {
        _settings = settings;
        RefreshRecent();
        StatusText = "拖入文件或点「📂 打开」开始。";
    }

    /// <summary>请求 View 打开文件；参数 <c>null</c> = 由 View 弹选择器。</summary>
    public event EventHandler<string?>? OpenRequested;

    /// <summary>请求 View 关闭当前内容。</summary>
    public event EventHandler? CloseRequested;

    public event EventHandler? ZoomInRequested;
    public event EventHandler? ZoomOutRequested;
    public event EventHandler? FitRequested;

    public event EventHandler? StatusChanged;
    public event EventHandler? ToolbarChanged;

    // ---------- 最近打开 ----------

    public ObservableCollection<string> RecentItems { get; } = new();

    public bool HasRecent => RecentItems.Count > 0;

    private void RefreshRecent()
    {
        RecentItems.Clear();
        foreach (var p in _settings.Recent) RecentItems.Add(p);
        OnPropertyChanged(nameof(HasRecent));
    }

    // ---------- 排版参数 ----------

    public double FontSize => _settings.FontSize;

    public double LineSpacing => _settings.LineSpacing;

    public double PagePadding => _settings.PagePadding;

    /// <summary>字号加减（文本模式下有效）。</summary>
    public void StepFontSize(double delta)
    {
        try
        {
            _settings.FontSize = FontSize + delta;
            OnPropertyChanged(nameof(FontSize));
            StatusText = "字号 " + FontSize.ToString("0");
            TypographyChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { StatusText = "⚠ 保存字号失败：" + ex.Message; }
    }

    /// <summary>排版参数变化（View 据此重新下发到渲染器）。</summary>
    public event EventHandler? TypographyChanged;

    /// <summary>取某文件的阅读进度（0~1）；没有记录返回 0。</summary>
    public double GetProgress(string path) => _settings.GetProgress(path);

    /// <summary>存某文件的阅读进度（关闭 / 切文件 / 离开页面时调）。</summary>
    public void SaveProgress(string path, double progress) => _settings.SetProgress(path, progress);

    // ---------- View 回调 ----------

    /// <summary>打开成功。</summary>
    public void OnOpened(string path, double progress, bool isText, bool isImage)
    {
        try { _settings.PushRecent(path); }
        catch (Exception ex) { StatusText = "⚠ 记录最近打开失败：" + ex.Message; }

        CurrentTitle = Path.GetFileName(path);
        CurrentInfo = path;
        IsText = isText;
        IsImage = isImage;
        HasContent = true;
        Hint = "";

        StatusText = isText
            ? "已打开（阅读进度 " + (progress * 100).ToString("0") + "%）"
            : "已打开";

        RefreshRecent();
        NotifyToolbarChanged();
    }

    /// <summary>打开失败：保持空态并显示原因。</summary>
    public void OnOpenFailed(string error)
    {
        HasContent = false;
        IsText = false;
        IsImage = false;
        CurrentTitle = "未打开文件";
        CurrentInfo = "";
        Hint = error;
        StatusText = "⚠ " + error;
        NotifyToolbarChanged();
    }

    /// <summary>关闭后回到空态。</summary>
    public void OnClosed()
    {
        HasContent = false;
        IsText = false;
        IsImage = false;
        CurrentTitle = "未打开文件";
        CurrentInfo = "";
        StatusText = "已关闭。拖入文件或点「📂 打开」。";
        NotifyToolbarChanged();
    }

    /// <summary>状态栏右侧附带信息（当前缩放 / 进度）。</summary>
    public void SetInfo(string info)
    {
        CurrentInfo = info ?? "";
    }

    // ---------- 命令 ----------

    [RelayCommand]
    private void Open() => OpenRequested?.Invoke(this, null);

    [RelayCommand]
    private void OpenRecent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        OpenRequested?.Invoke(this, path);
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ClearRecent()
    {
        try
        {
            _settings.ClearRecent();
            RefreshRecent();
            StatusText = "已清空最近打开。";
        }
        catch (Exception ex) { StatusText = "⚠ 清空失败：" + ex.Message; }
    }

    [RelayCommand]
    private void ZoomIn() => ZoomInRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ZoomOut() => ZoomOutRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Fit() => FitRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void FontLarger() => StepFontSize(1);

    [RelayCommand]
    private void FontSmaller() => StepFontSize(-1);

    // ---------- 工具条 ----------

    public IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new()
        {
            Text = "📂 打开",
            Classes = "primary",
            CommandPath = nameof(OpenCommand),
            ToolTip = "选择要查看的文件（也可直接把文件拖进窗口）",
        },
        new()
        {
            Text = "A-",
            CommandPath = nameof(FontSmallerCommand),
            IsEnabledPath = nameof(IsText),
            ToolTip = "文本字号减小",
        },
        new()
        {
            Text = "A+",
            CommandPath = nameof(FontLargerCommand),
            IsEnabledPath = nameof(IsText),
            ToolTip = "文本字号增大",
        },
        new()
        {
            Text = "⊞ 适应窗口",
            CommandPath = nameof(FitCommand),
            IsEnabledPath = nameof(IsImage),
            ToolTip = "图片缩放到适应窗口（也可双击图片切换 1:1 / 适应）",
        },
        new()
        {
            Text = "－",
            CommandPath = nameof(ZoomOutCommand),
            IsEnabledPath = nameof(IsImage),
            ToolTip = "缩小（Ctrl + 滚轮同样有效）",
        },
        new()
        {
            Text = "＋",
            CommandPath = nameof(ZoomInCommand),
            IsEnabledPath = nameof(IsImage),
            ToolTip = "放大（Ctrl + 滚轮同样有效）",
        },
        new()
        {
            Text = "✕ 关闭",
            CommandPath = nameof(CloseCommand),
            IsEnabledPath = nameof(HasContent),
            ToolTip = "关闭当前文件（回到空态）",
        },
    };

    private void NotifyToolbarChanged() => ToolbarChanged?.Invoke(this, EventArgs.Empty);

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);
}
