using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using NewToys.Reading.ViewModels;

namespace NewToys.Reading.Views;

/// <summary>
/// 阅读页 code-behind：管「选书 / 目录 / 抓取」的界面侧 —— 预览文本由绑定直出（<c>PreviewText</c>），
/// 定时保存抓取进度；正文渲染在查看器的「文本阅读」页（<c>📖 在查看器中打开</c>）。
/// </summary>
public partial class ReaderView : UserControl
{
    private ReaderViewModel? _vm;
    private DispatcherTimer? _timer;

    public ReaderView()
    {
        InitializeComponent();

        AttachedToVisualTree += (_, _) =>
        {
            _vm?.CheckPending();
            StartTimer();
            ApplySurfaceTheme();
        };

        DetachedFromVisualTree += (_, _) =>
        {
            _timer?.Stop();
            SaveProgress();
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm is not null) _vm.TypographyChanged -= OnTypographyChanged;

        _vm = DataContext as ReaderViewModel;
        if (_vm is null) return;

        // 配色档变了（设置页 / CycleTheme）⇒ 预览区背景跟着换
        _vm.TypographyChanged += OnTypographyChanged;

        ApplySurfaceTheme();
        _vm.CheckPending();
    }

    private void OnTypographyChanged(object? sender, EventArgs e) => ApplySurfaceTheme();

    private void ApplySurfaceTheme()
    {
        if (_vm is null) return;

        try
        {
            ReaderSurface.Background = new SolidColorBrush(Color.Parse(_vm.ReaderBackground));
        }
        catch { /* 色值坏 ⇒ 保持主题默认 */ }
    }

    /// <summary>抓取进度每 5 秒落一次库（切章 / 离开页面时各补一次）。</summary>
    private void StartTimer()
    {
        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick -= OnTimerTick;
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e) => SaveProgress();

    private void SaveProgress() => _vm?.SaveProgress(0);

    /// <summary>点目录浮层的遮罩 = 收起目录。</summary>
    private void OnTocBackdropTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        e.Handled = true;
        if (_vm is not null) _vm.ShowToc = false;
    }

    /// <summary>点换源浮层的遮罩 = 收起换源。</summary>
    private void OnSwitchBackdropTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        e.Handled = true;
        if (_vm is not null) _vm.ShowSwitchPanel = false;
    }

    /// <summary>点整本下载浮层的遮罩 = 收起（下载中不收起，避免用户看不到进度）。</summary>
    private void OnDownloadBackdropTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        e.Handled = true;
        if (_vm is not null && !_vm.IsDownloading) _vm.DownloadIsOpen = false;
    }
}
