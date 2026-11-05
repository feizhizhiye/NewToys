using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Models;
using NewToys.VideoTools.Models;
using NewToys.VideoTools.ViewModels;

namespace NewToys.VideoTools.Views;

public partial class VideoBatchView : UserControl
{
    private VideoBatchViewModel? _vm;

    /// <summary>Shift 连选的基准行。</summary>
    private VideoTaskBase? _anchor;

    /// <summary>程序化改 <c>SelectedItems</c> 时抑制自己的 <c>SelectionChanged</c> 回抛。</summary>
    private bool _syncingSelection;

    public VideoBatchView()
    {
        InitializeComponent();

        // 「已完成」区的右键菜单作用于命中的那一行：Avalonia 右键不自带选行，
        // 在**按压阶段**补选中（ContextRequested 常被菜单自己先吃掉）。
        FinishedList.AddHandler(PointerPressedEvent, OnFinishedPointerPressed, RoutingStrategies.Tunnel);

        // 上区多选：修饰键在**隧道**阶段接管（ListBox 在 Multiple 下单击默认是「追加」，
        // 与资源管理器「单击 = 只选这一行」不一致 ⇒ 无修饰键也一并接管）
        TaskList.AddHandler(PointerPressedEvent, OnTaskListPointerPressed, RoutingStrategies.Tunnel);
        TaskList.AddHandler(KeyDownEvent, OnTaskListKeyDown, RoutingStrategies.Tunnel);
        TaskList.SelectionChanged += OnTaskListSelectionChanged;
    }

    private void OnFinishedPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
        if (e.Source is Visual src && src.FindAncestorOfType<ListBoxItem>() is { } item)
            item.IsSelected = true;
    }

    // ==================== 上区多选（选中真相在 VM.SelectedRows） ====================

    /// <summary>
    /// 🔴 右键**必须在按压阶段**定行：<c>ContextRequested</c> 常被菜单自己先吃掉，
    /// 结果菜单项读的 <c>Selected</c> 还是上一条 / 为空 —— 表现为「右键删除点了没反应」。
    /// <para>命中行已在选中集里 ⇒ 保留多选（菜单里的批量项照样作用于整批）；否则替换为该行。</para>
    /// </summary>
    private void OnTaskListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null) return;
        if ((e.Source as Control)?.DataContext is not VideoTaskBase row) return;

        var props = e.GetCurrentPoint(this).Properties;

        if (props.IsRightButtonPressed)
        {
            if (TaskList.SelectedItems is not { } selected || !selected.Contains(row))
            {
                ReplaceWith(row);
                _anchor = row;
            }
            _vm.Selected = row;
            PushSelectionToVm(row);
            return;   // ⛔ 不 Handled：否则右键菜单弹不出来
        }

        if (!props.IsLeftButtonPressed) return;

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        // 行要拿到键盘焦点，否则 Ctrl+A 之类的键事件到不了列表
        (e.Source as Control)?.FindAncestorOfType<ListBoxItem>()?.Focus();
        e.Handled = true;

        if (shift) SelectRange(row, additive: ctrl);
        else if (ctrl) Toggle(row);
        else ReplaceWith(row);

        if (!shift) _anchor = row;
        _vm.Selected = row;          // 详情卡跟最后一次点击走
        PushSelectionToVm(row);
    }

    /// <summary>Shift 连选：从基准行到目标行整段选中；<paramref name="additive"/> = 保留原有选中（Ctrl+Shift）。</summary>
    private void SelectRange(VideoTaskBase target, bool additive)
    {
        if (_vm == null) return;

        var rows = _vm.Items.ToList();
        var to = rows.IndexOf(target);
        if (to < 0) return;

        var from = _anchor != null ? rows.IndexOf(_anchor) : to;
        if (from < 0) from = to;

        BulkUpdateSelection(items =>
        {
            if (!additive) items.Clear();
            for (var i = Math.Min(from, to); i <= Math.Max(from, to); i++)
                if (!items.Contains(rows[i])) items.Add(rows[i]);
        });
    }

    /// <summary>Ctrl 点击：已选中就去掉，没选中就加上。</summary>
    private void Toggle(VideoTaskBase row)
    {
        BulkUpdateSelection(items =>
        {
            if (items.Contains(row)) items.Remove(row);
            else items.Add(row);
        });
    }

    /// <summary>无修饰键：清空其余、只留这一行（资源管理器语义）。</summary>
    private void ReplaceWith(VideoTaskBase row)
    {
        BulkUpdateSelection(items =>
        {
            items.Clear();
            items.Add(row);
        });
    }

    /// <summary>
    /// 批量改 <c>SelectedItems</c>：**期间关掉自动滚动**。
    /// <para>🔴 每 <c>Add</c> 一项 ListBox 都会把该项滚进视野（虚拟化下还要建容器 + 触发布局）——
    /// 全选 / 大范围连选上百行就是上百次滚动，界面看起来像卡死。</para>
    /// </summary>
    private void BulkUpdateSelection(Action<System.Collections.IList> mutate)
    {
        if (TaskList?.SelectedItems is not { } items) return;

        var autoScroll = TaskList.AutoScrollToSelectedItem;
        TaskList.AutoScrollToSelectedItem = false;
        _syncingSelection = true;
        try { mutate(items); }
        finally
        {
            _syncingSelection = false;
            TaskList.AutoScrollToSelectedItem = autoScroll;
        }
    }

    /// <summary>Ctrl+A：交给 VM 的全选命令（与右键「全选」同一入口）。</summary>
    private void OnTaskListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.A || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        e.Handled = true;
        _vm?.SelectAllCommand.Execute(null);
    }

    /// <summary>列表自身的选中变化（键盘方向键 / 程序化）→ 回填 VM。</summary>
    private void OnTaskListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection) return;
        if (_vm?.IsSyncingView == true) return;   // 列表重建中，别拿「被清空」覆盖用户的选中
        PushSelectionToVm(null);
    }

    private void PushSelectionToVm(VideoTaskBase? clicked)
    {
        if (_vm == null || TaskList?.SelectedItems is not { } items) return;
        _vm.SetSelectionFromView(items.OfType<VideoTaskBase>().ToList(), clicked);
    }

    /// <summary>VM 侧改了选中（全选 / 反选 / 取消 / 列表重算认领）⇒ 回填 <c>SelectedItems</c>。</summary>
    private void OnSelectionSyncRequested(object? sender, EventArgs e)
    {
        if (_vm == null) return;

        BulkUpdateSelection(items =>
        {
            items.Clear();
            foreach (var t in _vm.SelectedRows) items.Add(t);
        });
    }

    /// <summary>
    /// ⚠️ <c>ContextMenu</c> 挂在**独立的弹出树**上，DataContext 不保证随宿主继承 ——
    /// 不显式同步的话菜单项里的 <c>{Binding XxxCommand}</c> 解析不到，右键点了没反应（见规范 3.6）。
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm != null) _vm.SelectionSyncRequested -= OnSelectionSyncRequested;
        _vm = DataContext as VideoBatchViewModel;
        if (_vm != null) _vm.SelectionSyncRequested += OnSelectionSyncRequested;

        if (TaskList?.ContextMenu is { } menu) menu.DataContext = DataContext;
        if (FinishedList?.ContextMenu is { } fmenu) fmenu.DataContext = DataContext;
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;

        // 平台文件选择器只能在 View 层用（VM 不引 Avalonia）：本页把「选择输出文件」等真实实现
        // 注入给声明式卡的动作。每次 DataContext 变化重挂一次，幂等 —— 命令内部只认最新 VM。
        if (DataContext is VideoBatchViewModel vmb)
        {
            vmb.PickOutputCommand = new RelayCommand(() => _ = PickOutputAsync());
            vmb.PickSubtitleCommand = new RelayCommand(() => _ = PickSubtitleAsync());
            vmb.PickAudioCommand = new RelayCommand(() => _ = PickAudioAsync());
        }
    }

    /// <summary>🔴 取件点必须在这里，不能只在 OnDataContextChanged（需求 §4.8 硬规则 8）。（WHY: VT-040）</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (DataContext is VideoBatchViewModel vm)
            _ = vm.ConsumePendingAsync();
    }

    /// <summary>📂 为当前选中任务挑一个输出文件。</summary>
    private async System.Threading.Tasks.Task PickOutputAsync()
    {
        if (DataContext is not VideoBatchViewModel vm) return;
        if (vm.Selected == null) { vm.StatusText = "先在列表里选中一个任务。"; return; }

        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "选择输出文件",
                SuggestedFileName = System.IO.Path.GetFileName(vm.EditOutput)
            });

            var path = file?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path)) vm.EditOutput = path;
        }
        catch (Exception ex)
        {
            vm.StatusText = "选择输出失败：" + ex.Message;
        }
    }

    /// <summary>📝 为当前选中任务挑一个字幕文件（.srt / .ass）。</summary>
    private async System.Threading.Tasks.Task PickSubtitleAsync()
    {
        if (DataContext is not VideoBatchViewModel vm) return;
        if (vm.Selected == null) { vm.StatusText = "先在列表里选中一个任务。"; return; }

        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择字幕文件",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("字幕文件") { Patterns = new[] { "*.srt", "*.ass", "*.ssa", "*.vtt" } }
                }
            });

            var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
            if (string.IsNullOrWhiteSpace(path)) return;

            vm.EditSubtitlePath = path;
            // 挑完默认按软字幕（最省事），用户再改成硬字幕也行
            if (string.Equals(vm.EditSubtitleMode, SubtitleModes.None, StringComparison.OrdinalIgnoreCase))
                vm.EditSubtitleMode = SubtitleModes.Soft;
            vm.StatusText = "已选择字幕：" + System.IO.Path.GetFileName(path) + "（记得点「保存修改」）";
        }
        catch (Exception ex)
        {
            vm.StatusText = "选择字幕失败：" + ex.Message;
        }
    }

    /// <summary>🎵 为当前选中任务挑替换用的音频文件。</summary>
    private async System.Threading.Tasks.Task PickAudioAsync()
    {
        if (DataContext is not VideoBatchViewModel vm) return;
        if (vm.Selected == null) { vm.StatusText = "先在列表里选中一个任务。"; return; }

        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择音频文件",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("音频文件")
                    {
                        Patterns = new[] { "*.mp3", "*.m4a", "*.aac", "*.wav", "*.flac", "*.ogg", "*.opus" }
                    }
                }
            });

            var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
            if (string.IsNullOrWhiteSpace(path)) return;

            vm.EditAudioPath = path;
            if (string.Equals(vm.EditAudioMode, AudioModes.Keep, StringComparison.OrdinalIgnoreCase))
                vm.EditAudioMode = AudioModes.Replace;
            vm.StatusText = "已选择音频：" + System.IO.Path.GetFileName(path) + "（记得点「保存修改」）";
        }
        catch (Exception ex)
        {
            vm.StatusText = "选择音频失败：" + ex.Message;
        }
    }

}
