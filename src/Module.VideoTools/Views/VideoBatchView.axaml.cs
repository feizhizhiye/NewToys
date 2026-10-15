using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Models;
using NewToys.VideoTools.ViewModels;

namespace NewToys.VideoTools.Views;

public partial class VideoBatchView : UserControl
{
    public VideoBatchView()
    {
        InitializeComponent();

        // 平台文件选择器只能在 View 层用（VM 不引 Avalonia），选完把结果交给 VM
        PickFilesBtn.Click += (_, _) => _ = PickFilesAsync();
        // ⚠️ NT-054：原来卡片里那个内联「📂」按钮（PickOutputBtn）随页内卡一起删了 ——
        //    现在它是声明式卡的 Actions 项，命令由本页在 OnDataContextChanged 里注入（见下）。
        ExportBtn.Click += (_, _) => _ = ExportTasksAsync();
        ImportBtn.Click += (_, _) => _ = ImportTasksAsync();

        // v3.20 §6.4.15：拖动表头列边界 → VM 改「手动列宽」→ 表头与所有行同规格重建
        HeaderGrid.ColumnResize += (_, e) =>
        {
            if (DataContext is ListPageViewModelBase vm)
                vm.ResizeColumns(e.LeftIndex, e.LeftWidth, e.RightIndex, e.RightWidth);
        };
        // v3.22 §4.8.1：表头首次布局后把「表头宽 + 示例值宽」交给 VM —— 示例 ≤ 表头的列锁成表头宽
        HeaderGrid.HeaderMeasured += (_, e) =>
        {
            if (DataContext is ListPageViewModelBase vm) vm.LockColumnsBySample(e.Widths, e.SampleWidths);
        };
    }

    /// <summary>
    /// 把"导入/导出默认目录"（v3.15 全局）解析成 <see cref="IStorageFolder"/>，作为文件选择器的 <see cref="FilePickerOpenOptions.SuggestedStartLocation"/>。
    /// 目录为空 / 解析失败 → 返回 <c>null</c>（选择器回退到系统默认）。
    /// </summary>
    private static async Task<IStorageFolder?> SuggestedStartAsync(IStorageProvider sp, string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return null;
        try { return await sp.TryGetFolderFromPathAsync(dir); }
        catch { return null; }
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
        // v3.16 §6.4.10：表头勾选框的「全选 / 取消选择 / 反选」菜单同理（不同步的话右键点了没反应）。
        // v3.16.1：菜单挪到 HeaderCheckHost（透明 Border = 整格热区）上。
        if (HeaderCheckHost?.ContextMenu is { } cmenu) cmenu.DataContext = DataContext;

        // NT-054：把「📂 选择输出文件」的真实实现（要 TopLevel / StorageProvider）注入给声明式卡的动作。
        // 每次 DataContext 变化重挂一次，幂等 —— 命令内部只认最新 VM。
        if (DataContext is VideoBatchViewModel vmb)
            vmb.PickOutputCommand = new RelayCommand(() => _ = PickOutputAsync());
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

    /// <summary>📤 导出任务：把当前队列（剪辑 + 转码）序列化成 JSON 文件，可用外部脚本批量加工后再导入。</summary>
    private async System.Threading.Tasks.Task ExportTasksAsync()
    {
        if (DataContext is not VideoBatchViewModel vm) return;
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出任务为 JSON 文件",
                SuggestedFileName = "video-tasks.json",
                SuggestedStartLocation = await SuggestedStartAsync(sp, vm.ImportExportDefaultDir),
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("JSON 文件") { Patterns = new[] { "*.json" } },
                    new FilePickerFileType("所有文件") { Patterns = new[] { "*" } }
                }
            });

            var path = file?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path)) vm.ExportTasks(path);
        }
        catch (Exception ex)
        {
            vm.StatusText = "导出任务失败：" + ex.Message;
        }
    }

    /// <summary>📥 导入任务：从导出的 JSON 里追加任务进当前队列（不覆盖已有）。</summary>
    private async System.Threading.Tasks.Task ImportTasksAsync()
    {
        if (DataContext is not VideoBatchViewModel vm) return;
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "导入任务 JSON 文件",
                AllowMultiple = false,
                SuggestedStartLocation = await SuggestedStartAsync(sp, vm.ImportExportDefaultDir),
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("JSON 文件") { Patterns = new[] { "*.json" } },
                    new FilePickerFileType("所有文件") { Patterns = new[] { "*" } }
                }
            });

            var path = files?.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path)) vm.ImportTasks(path);
        }
        catch (Exception ex)
        {
            vm.StatusText = "导入任务失败：" + ex.Message;
        }
    }
}
