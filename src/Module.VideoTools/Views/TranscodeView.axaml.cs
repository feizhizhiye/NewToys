using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using NewToys.VideoTools.ViewModels;

namespace NewToys.VideoTools.Views;

public partial class TranscodeView : UserControl
{
    public TranscodeView()
    {
        InitializeComponent();
        // 选源文件 / 输出文件走平台选择器（VM 不引用 Avalonia 的 UI 类型）
        PickFilesBtn.Click += (_, _) => _ = PickSourceFilesAsync();
        PickOutputBtn.Click += (_, _) => _ = PickOutputFileAsync();
    }

    /// <summary>
    /// ⚠️ <c>ContextMenu</c> 挂在**独立的弹出树**上，DataContext 不保证随宿主继承 ——
    /// 不显式同步的话，菜单项里的 <c>{Binding XxxCommand}</c> 解析不到，右键点了没反应（规范 3.6）。
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (TaskList?.ContextMenu is { } menu) menu.DataContext = DataContext;
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
    }

    /// <summary>📂 添加文件：可多选，一次生成一批转码任务。</summary>
    private async System.Threading.Tasks.Task PickSourceFilesAsync()
    {
        if (DataContext is not TranscodeViewModel vm) return;

        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择要转码的视频（可多选）",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("视频文件")
                    {
                        Patterns = new[] { "*.mp4", "*.mkv", "*.avi", "*.mov", "*.wmv", "*.flv", "*.webm",
                                           "*.m4v", "*.ts", "*.mpg", "*.mpeg", "*.rmvb", "*.3gp" }
                    },
                    new FilePickerFileType("所有文件") { Patterns = new[] { "*" } }
                }
            });

            var paths = files?.Select(f => f.TryGetLocalPath())
                              .Where(p => !string.IsNullOrWhiteSpace(p))
                              .ToList();
            if (paths == null || paths.Count == 0) return;

            await vm.AddFilesAsync(paths);
        }
        catch (Exception ex)
        {
            vm.StatusText = "选择文件失败：" + ex.Message;
        }
    }

    /// <summary>📂 给当前任务挑输出文件（另存为）。</summary>
    private async System.Threading.Tasks.Task PickOutputFileAsync()
    {
        if (DataContext is not TranscodeViewModel vm) return;
        if (vm.Selected == null) { vm.StatusText = "先在左侧选中一个任务。"; return; }

        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

            var ext = vm.Selected.Preset.Extension;
            var suggested = string.IsNullOrWhiteSpace(vm.Selected.OutputPath)
                ? System.IO.Path.GetFileNameWithoutExtension(vm.Selected.SourcePath) + "_" + vm.Selected.PresetId + ext
                : System.IO.Path.GetFileName(vm.Selected.OutputPath);

            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "转码结果保存到",
                SuggestedFileName = suggested,
                DefaultExtension = ext,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("输出文件") { Patterns = new[] { "*" + ext } },
                    new FilePickerFileType("所有文件") { Patterns = new[] { "*" } }
                }
            });

            var path = file?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path))
            {
                vm.EditOutput = path;
                vm.StatusText = "已选择输出文件：" + path + "（点「💾 保存」写回任务）";
            }
        }
        catch (Exception ex)
        {
            vm.StatusText = "选择输出文件失败：" + ex.Message;
        }
    }
}
