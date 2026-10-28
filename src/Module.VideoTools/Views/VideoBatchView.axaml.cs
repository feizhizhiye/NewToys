using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Models;
using NewToys.VideoTools.Models;
using NewToys.VideoTools.ViewModels;

namespace NewToys.VideoTools.Views;

public partial class VideoBatchView : UserControl
{
    public VideoBatchView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// ⚠️ <c>ContextMenu</c> 挂在**独立的弹出树**上，DataContext 不保证随宿主继承 ——
    /// 不显式同步的话，菜单项里的 <c>{Binding XxxCommand}</c> 解析不到，右键点了没反应（见规范 3.6）。
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (TaskList?.ContextMenu is { } menu) menu.DataContext = DataContext;
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
        // §6.4.10：表头勾选框的「全选 / 取消选择 / 反选」菜单同理（不同步的话右键点了没反应）。
        // 1：菜单挪到 HeaderCheckHost（透明 Border = 整格热区）上。
        if (HeaderCheckHost?.ContextMenu is { } cmenu) cmenu.DataContext = DataContext;

        // 平台文件选择器只能在 View 层用（VM 不引 Avalonia）：本页把「📂 选择输出文件」等真实实现
        // 注入给声明式卡的动作。每次 DataContext 变化重挂一次，幂等 —— 命令内部只认最新 VM。
        if (DataContext is VideoBatchViewModel vmb)
        {
            vmb.PickOutputCommand = new RelayCommand(() => _ = PickOutputAsync());
            vmb.PickSubtitleCommand = new RelayCommand(() => _ = PickSubtitleAsync());
            vmb.PickAudioCommand = new RelayCommand(() => _ = PickAudioAsync());
        }
    }

    /// <summary>
    /// 🔴 **取件点必须在这里，不能只在 <see cref="OnDataContextChanged"/>**（需求 §4.8 硬规则 8）。
    /// <para>标签页**已存在**时从「视频列表」跳过来，Shell 只是把缓存的 View 重新挂回内容区 ——
    /// <c>DataContext</c> 没变，<c>OnDataContextChanged</c> **不再触发**，送过来的文件就没人建任务。
    /// <c>TakePendingCut/TakePendingTranscode</c> 取一次即清空，重复调安全。</para>
    /// <para>⚠️ 基类 <c>Visual.OnAttachedToVisualTree</c> 是 **protected**，这里必须写 <c>protected override</c>，
    /// 写成 <c>public override</c> → <b>CS0507「无法更改访问修饰符」</b>。</para>
    /// </summary>
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
            vm.StatusText = "已选择字幕：" + System.IO.Path.GetFileName(path) + "（记得点「💾 保存修改」）";
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
            vm.StatusText = "已选择音频：" + System.IO.Path.GetFileName(path) + "（记得点「💾 保存修改」）";
        }
        catch (Exception ex)
        {
            vm.StatusText = "选择音频失败：" + ex.Message;
        }
    }

}
