using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using NewToys.Reading.ViewModels;

namespace NewToys.Reading.Views;

/// <summary>
/// 阅读页 code-behind：只做三件事 —— 把正文推给 <c>TextViewer</c>、把排版参数写进控件、定时保存进度。
/// <para>VM ⛔ 不碰控件；正文控件 ⛔ 自己不发请求。</para>
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

            // 🔴 正文控件的 Foreground 在库内 XAML 上绑的是 DynamicResource，挂树之后才解析，
            // 会把进页前下发的阅读配色盖回主题色 —— 白色底 + 浅色字，几乎看不清。
            // ⇒ 挂树后再重放一次下发（Loaded 优先级 = 排在动态资源解析之后）。
            Dispatcher.UIThread.Post(ApplyTypography, DispatcherPriority.Loaded);
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

        Unsubscribe();
        _vm = DataContext as ReaderViewModel;
        if (_vm is null) return;

        _vm.ContentChanged += OnContentChanged;
        _vm.TypographyChanged += OnTypographyChanged;

        ApplyTypography();
        ApplyContent(keepOffset: false);
        _vm.CheckPending();
    }

    private void Unsubscribe()
    {
        if (_vm is null) return;
        _vm.ContentChanged -= OnContentChanged;
        _vm.TypographyChanged -= OnTypographyChanged;
    }

    private void OnContentChanged(object? sender, EventArgs e) => ApplyContent(keepOffset: false);

    /// <summary>
    /// 重排（字号 / 行距 / 缩进 / 段距 / 配色变化）后要重排文本 —— 此时必须**保住当前滚动位置**，
    /// 否则每次调排版都会把人拉回本章开头（<see cref="ReaderViewModel.PendingProgress"/> 只在换章时有效）。
    /// </summary>
    private void OnTypographyChanged(object? sender, EventArgs e)
    {
        ApplyTypography();
        ApplyContent(keepOffset: true);
    }

    private void ApplyContent(bool keepOffset)
    {
        var offset = keepOffset ? Body.ReadProgress : (_vm?.PendingProgress ?? 0);
        Body.SetText(_vm?.FormattedContent ?? "", offset);
    }

    private void ApplyTypography()
    {
        if (_vm is null) return;

        Body.ApplyTypography(_vm.FontSize, _vm.LineSpacing, _vm.ReaderPadding);
        Body.ApplyAppearance(_vm.FontFamily, _vm.ReaderForeground);
        Body.MaxWidth = _vm.ContentWidth > 0 ? _vm.ContentWidth : double.PositiveInfinity;

        try
        {
            ReaderSurface.Background = new SolidColorBrush(Color.Parse(_vm.ReaderBackground));
        }
        catch { /* 色值坏 ⇒ 保持主题默认 */ }
    }

    /// <summary>滚动进度每 5 秒落一次库（切章 / 离开页面时各补一次）。</summary>
    private void StartTimer()
    {
        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick -= OnTimerTick;
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e) => SaveProgress();

    private void SaveProgress() => _vm?.SaveProgress(Body.ReadProgress);

    /// <summary>点目录浮层的遮罩 = 收起目录。</summary>
    private void OnTocBackdropTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        if (_vm is not null) _vm.ShowToc = false;
    }

    /// <summary>点样式卡的遮罩 = 收起样式卡。</summary>
    private void OnStyleBackdropTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        if (_vm is not null) _vm.ShowStylePanel = false;
    }

    /// <summary>点换源浮层的遮罩 = 收起换源。</summary>
    private void OnSwitchBackdropTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        if (_vm is not null) _vm.ShowSwitchPanel = false;
    }

    /// <summary>点整本下载浮层的遮罩 = 收起（下载中不收起，避免用户看不到进度）。</summary>
    private void OnDownloadBackdropTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        if (_vm is not null && !_vm.IsDownloading) _vm.DownloadIsOpen = false;
    }
}
