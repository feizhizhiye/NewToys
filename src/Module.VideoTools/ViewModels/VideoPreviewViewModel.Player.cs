using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.VideoTools.Models;
using NewToys.VideoTools.Services;

namespace NewToys.VideoTools.ViewModels;

public partial class VideoPreviewViewModel : ObservableObject, IStatusProvider, IDetailPanelProvider, IDetailCardSource, IToolbarSource
{

    /// <summary>📋 复制将要执行的 ffmpeg 命令行（手改过就选手改的那条）。</summary>
    [RelayCommand]
    private async Task CopyCommandLineAsync()
    {
        var text = CommandPreview;
        if (text.StartsWith("（", StringComparison.Ordinal)) { StatusText = text; return; }
        try
        {
            var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (win?.Clipboard == null) { StatusText = "剪贴板不可用。"; return; }
            await win.Clipboard.SetTextAsync(text);
            StatusText = "已复制 ffmpeg 命令行。";
        }
        catch (Exception ex) { StatusText = "复制失败：" + ex.Message; }
    }

    /// <summary>↺ 把命令恢复成「由参数自动生成」（丢弃手改）。</summary>
    [RelayCommand]
    private void ResetCommand()
    {
        IsArgsEdited = false;
        RefreshCommand();
        StatusText = "命令已恢复为由参数自动生成。";
    }

    // ==================== 类播放器：拖拽快览 / 自动播放预览 ====================

    /// <summary>▶/⏸ 切换「自动播放预览」：开 = 按时间轴逐帧推进（抽帧模拟播放），关 = 仅手动拖时间条。</summary>
    [RelayCommand]
    private void TogglePlay()
    {
        if (!HasSource || !HasDuration) { StatusText = "先载入一个有时长的视频才能播放预览。"; return; }
        IsPlaying = !IsPlaying;
        if (IsPlaying)
        {
            if (PreviewTime >= TimelineMax - 0.05) PreviewTime = 0;   // 已到末尾 → 从头播
            StartPlayTimer();
            StatusText = "正在播放预览（抽帧模拟）… 再点一次停止。";
        }
        else
        {
            StopPlayTimer();
            StatusText = "已停止播放预览。";
        }
    }

    private void StartPlayTimer()
    {
        StopPlayTimer();
        _playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _playTimer.Tick += OnPlayTick;
        _playTimer.Start();
    }

    private void StopPlayTimer()
    {
        if (_playTimer != null)
        {
            _playTimer.Tick -= OnPlayTick;
            _playTimer.Stop();
            _playTimer = null;
        }
    }

    private void OnPlayTick(object? sender, EventArgs e)
    {
        if (!IsPlaying) { StopPlayTimer(); return; }
        var next = PreviewTime + 0.1;
        if (next >= TimelineMax)
        {
            PreviewTime = TimelineMax;
            IsPlaying = false;
            StopPlayTimer();
            StatusText = "播放预览结束。";
            return;
        }
        PreviewTime = next;
    }

    // ==================== 生命周期 ====================

    public VideoPreviewViewModel() { }

    public VideoPreviewViewModel(VideoTaskStore store, IAppConfigService cfg, IColumnLayoutService? cols = null)
    {
        _store = store;
        _cfg = cfg;
        _cols = cols;
        if (cols != null)
            IsDetailVisible = cols.LoadFlag(PageKey, IColumnLayoutService.FlagDetailPanel, true);
    }

    /// <summary>把外部（视频列表页 / code-behind 选文件）给的路径载入编辑器 —— **载入后自动预览**。</summary>
    public void LoadSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        SourcePath = path;   // 触发 OnSourcePathChanged：清预览 / 时长 / 输出 / 手改命令
        _ = PreparePreviewAsync();
    }

    /// <summary>
    /// 进入本页时取走「视频列表」页送来的文件（<c>EnqueueForCut</c>）。
    /// <para>⚠️ 本页一次只编辑**一个**任务，所以只载入第一个；其余在状态栏里如实说明。</para>
    /// </summary>
    public void ConsumePending()
    {
        if (_store == null) return;
        var pending = _store.TakePendingCut();
        if (pending.Count == 0) return;

        LoadSource(pending[0]);
        if (pending.Count > 1)
            StatusText = $"已从「视频列表」收到 {pending.Count} 个文件，先载入第 1 个（本页一次只编一个）；其余可逐条处理。";
    }

}
