using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using NewToys.VideoTools.ViewModels;

namespace NewToys.VideoTools.Views;

public partial class VideoBatchView : UserControl
{
    public VideoBatchView()
    {
        InitializeComponent();

        // 平台文件选择器只能在 View 层用（VM 不引 Avalonia），选完把结果交给 VM
        PickFilesBtn.Click += (_, _) => _ = PickFilesAsync();
        PickOutputBtn.Click += (_, _) => _ = PickOutputAsync();
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

    /// <summary>📂 添加文件（可多选）→ 按工具条上选的类型建任务。</summary>
    private async System.Threading.Tasks.Task PickFilesAsync()
    {
        if (DataContext is not VideoBatchViewModel vm) return;

        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择视频文件",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("视频文件")
                    {
                        Patterns = new[] { "*.mp4", "*.mkv", "*.avi", "*.mov", "*.wmv", "*.flv", "*.webm", "*.ts", "*.m4v", "*.mpg", "*.mpeg" }
                    },
                    new FilePickerFileType("所有文件") { Patterns = new[] { "*" } }
                }
            });

            var paths = files
                ?.Select(f => f.TryGetLocalPath())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!)
                .ToList();

            if (paths == null || paths.Count == 0) return;
            await vm.AddFilesAsync(paths);
        }
        catch (Exception ex)
        {
            vm.StatusText = "选择文件失败：" + ex.Message;
        }
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
}
