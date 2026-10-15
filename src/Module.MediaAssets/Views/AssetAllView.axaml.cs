using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using NewToys.MediaAssets.ViewModels;

namespace NewToys.MediaAssets.Views;

/// <summary>
/// 「全部资产」页的 code-behind：只做**四件 View 才做得了的事**
/// （弹选择器 / 转发表头拖拽事件 / 给 ContextMenu 补 DataContext / 取跨页投递的件），业务逻辑一律在 VM。
/// </summary>
public partial class AssetAllView : UserControl
{
    public AssetAllView()
    {
        InitializeComponent();

        // ① 「📁 扫描」SplitButton：主按钮直接点 = 选文件夹；下拉 = 选文件夹 / 选文件。
        //    v3.30（用户定调）：原先「📁 扫描文件夹」+「📄 扫描文件」两个独立按钮**合并**成这一条，
        //    样式与接线都对齐 VideoTools 的「📂 导入」。
        //    两条路都是**预览**（只进内存、⛔ 不入库）：看完决定「💾 加入库」还是「🧹 清除列表」换目录重来。
        //    用 `_ =` 显式丢弃返回的 Task：事件处理器是 void 语义，这里就是"点火即忘"（CS4014 的规范写法）。
        ScanSplit.Click += (_, _) => _ = ScanFolderAsync();
        ScanFolderItem.Click += (_, _) => _ = ScanFolderAsync();
        ScanFilesItem.Click += (_, _) => _ = ScanFilesAsync();

        // ② NT-026：「🗄 并入…」主按钮 = 选一个外部 .db 并入当前库（预览 + 确认在 VM 里做）。
        MergeSplit.Click += (_, _) => _ = PickMergeDbAsync();

        // ③ 「➕ 登记」：加入待扫列表（⛔ 不扫描，等「🔄 扫描全部」）
        BtnRegisterFolder.Click += async (_, _) =>
        {
            if (DataContext is not AssetAllViewModel vm) return;
            var dir = await PickFolderAsync(vm).ConfigureAwait(false);
            if (dir == null) return;
            vm.RegisterFolder(dir);
        };

        // ④ 路径输入框回车 = 预览扫描（省得再去点按钮）
        ImportPathBox.KeyDown += (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter) return;
            if (DataContext is AssetAllViewModel vm) vm.PreviewPathCommand.Execute(null);
            e.Handled = true;
        };

        // ⑤ 拖表头列边界 → 转给 VM（Grid.ColumnDefinitions 不能绑，只能这样转）
        HeaderGrid.ColumnResize += (_, e) =>
        {
            if (DataContext is AssetAllViewModel vm)
                vm.ResizeColumns(e.LeftIndex, e.LeftWidth, e.RightIndex, e.RightWidth);
        };

        // ⑥ 表头首次测量完 → 让 VM 按「示例宽 ≤ 表头宽」锁列宽（v3.22 §4.8.1）
        HeaderGrid.HeaderMeasured += (_, e) =>
        {
            if (DataContext is AssetAllViewModel vm) vm.LockColumnsBySample(e.Widths, e.SampleWidths);
        };

        // ⑦ v3.34 / NT-018：表头 ☑ 上的右键菜单（全选 / 取消选择 / 反选）
        //    🔴 v3.42.1：**菜单只挂外层 Border（HeaderCheckHost），右键由 Avalonia 原生弹出** ——
        //    ⛔ 不要再在 PointerPressed 里手动 menu.Open()：消息冒泡到 CheckBox 时它自己的 ContextMenu
        //    属性已经自动弹过一次，二次 Open 会抛异常 → 应用直接崩（用户实测）。
        //    整格热区由 Border 本身提供（它的 ContextMenu 覆盖整格，含包在里面的 CheckBox）。

        // ⑧ v3.42：工具条按容器宽度**自动折叠** —— 布局一变就重算（两行放得下就多显示，放不下才折尾部的）。
        //    🔴 用 LayoutUpdated 而不是 SizeChanged：首次要等子元素量出宽度才能算（SizeChanged 可能早于子元素布局）。
        ToolbarPanel.LayoutUpdated += (_, _) => ApplyToolbarLayout();
    }

    /// <summary>
    /// 🔴 跨页取件点（NT-018，规范 §4.8 硬规则 8）：别的页面（标签管理 / 分类管理）请求"打开某个资产"时，
    /// 在这里取件并选中。⛔ 不能放 `OnDataContextChanged` —— 标签已存在、切回来的那次只有可视树重挂，
    /// DataContext 不变、那个回调**不会触发**。
    /// </summary>
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is AssetAllViewModel vm) vm.ConsumePendingAsset();
        ApplyToolbarLayout();   // v3.42：从别的标签页切回来时按当前宽度重排一次工具条
    }

    /// <summary>⚠️ ContextMenu 挂在独立弹出树上，DataContext 不随宿主继承，必须手工同步（规范 §3.8）。</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
        if (AssetList?.ContextMenu is { } amenu) amenu.DataContext = DataContext;
        if (HeaderCheckHost?.ContextMenu is { } cmenu) cmenu.DataContext = DataContext;

        // v3.42：换 VM（换页 / 薄壳页切换）后要重新挂钩「⋯」展开态监听并重排工具条
        HookToolbarVm();
        ApplyToolbarLayout();
    }

    /// <summary>「📁 扫描 → 文件夹…」：选一个目录 → **预览**（只进内存，⛔ 不入库）。</summary>
    private async Task ScanFolderAsync()
    {
        if (DataContext is not AssetAllViewModel vm) return;
        var dir = await PickFolderAsync(vm).ConfigureAwait(false);
        if (dir == null) return;
        await vm.PreviewFolderAsync(dir).ConfigureAwait(false);
    }

    /// <summary>「📁 扫描 → 文件…」：只预览选中的几个文件（⛔ 同样不入库）。</summary>
    private async Task ScanFilesAsync()
    {
        if (DataContext is not AssetAllViewModel vm) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            SuggestedStartLocation = await SuggestedStartAsync(topLevel.StorageProvider, vm.ImportExportDefaultDir),
            AllowMultiple = true
        }).ConfigureAwait(false);

        if (files == null || files.Count == 0) return;
        var paths = new List<string>();
        foreach (var f in files)
        {
            var p = f.TryGetLocalPath();
            if (!string.IsNullOrEmpty(p)) paths.Add(p);
        }
        if (paths.Count == 0) return;
        await vm.PreviewFilesAsync(paths).ConfigureAwait(false);
    }

    /// <summary>NT-026：「🗄 并入…」：选一个外部 .db → VM 预览 + 确认 + 入队合并。</summary>
    private async Task PickMergeDbAsync()
    {
        if (DataContext is not AssetAllViewModel vm) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择要并入当前库的 newtoys.db",
            SuggestedStartLocation = await SuggestedStartAsync(sp, vm.ImportExportDefaultDir),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("SQLite 数据库") { Patterns = new[] { "*.db", "*.db3", "*.sqlite" } },
                new FilePickerFileType("所有文件") { Patterns = new[] { "*" } }
            }
        }).ConfigureAwait(false);

        var path = files?.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (string.IsNullOrWhiteSpace(path)) return;
        vm.MergeLibraryCommand.Execute(path);
    }

    /// <summary>NT-026：「💿 另存为…」：选一个目标 .db → VM 另存（已存在会问覆盖）。</summary>
    private async void OnSaveLibraryAsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not AssetAllViewModel vm) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存资产库为",
            SuggestedStartLocation = await SuggestedStartAsync(sp, vm.ImportExportDefaultDir),
            SuggestedFileName = "newtoys.db",
            FileTypeChoices = new[] { new FilePickerFileType("SQLite 数据库") { Patterns = new[] { "*.db" } } }
        }).ConfigureAwait(false);

        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        vm.SaveLibraryAsCommand.Execute(path);
    }

    /// <summary>弹目录选择框，返回选中的本地路径（取消 / 拿不到 → null）。</summary>
    private async Task<string?> PickFolderAsync(AssetAllViewModel vm)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return null;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            SuggestedStartLocation = await SuggestedStartAsync(topLevel.StorageProvider, vm.ImportExportDefaultDir),
            AllowMultiple = false
        }).ConfigureAwait(false);

        var path = folders?.Count > 0 ? folders[0].TryGetLocalPath() : null;
        return string.IsNullOrEmpty(path) ? null : path;
    }

    /// <summary>把文件 / 目录选择器定位到「导入导出默认目录」（v3.19 / NT-006 全页统一）。</summary>
    private static async Task<IStorageFolder?> SuggestedStartAsync(IStorageProvider sp, string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return null;
        try { return await sp.TryGetFolderFromPathAsync(dir); }
        catch { return null; }
    }

    // ==================== NT-022：资产列表 JSON 导入 / 导出 ====================

    private async void OnExportAssetsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not AssetAllViewModel vm) return;
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出资产列表",
                SuggestedStartLocation = await SuggestedStartAsync(sp, vm.ImportExportDefaultDir),
                SuggestedFileName = "media-assets.json",
                FileTypeChoices = new[] { new FilePickerFileType("JSON 清单") { Patterns = new[] { "*.json" } } }
            }).ConfigureAwait(false);

            var path = file?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) return;

            File.WriteAllText(path, vm.ExportAssetsJson());
            vm.StatusText = $"已导出当前列表到：{path}";
        }
        catch (Exception ex) { vm.StatusText = "导出失败：" + ex.Message; }
    }

    private async void OnImportAssetsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not AssetAllViewModel vm) return;
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择要导入的资产 json",
                SuggestedStartLocation = await SuggestedStartAsync(sp, vm.ImportExportDefaultDir),
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("JSON 清单") { Patterns = new[] { "*.json" } },
                    new FilePickerFileType("所有文件") { Patterns = new[] { "*" } }
                }
            }).ConfigureAwait(false);

            var src = files?.Count > 0 ? files[0].TryGetLocalPath() : null;
            if (string.IsNullOrWhiteSpace(src)) return;

            var (ok, msg) = vm.ImportAssetsJson(File.ReadAllText(src));
            vm.StatusText = msg;
        }
        catch (Exception ex) { vm.StatusText = "导入失败：" + ex.Message; }
    }

    // ==================== v3.42：工具条按容器宽度自动折叠（用户定调） ====================

    /// <summary>各项的布局宽（不含间距），首次布局后**缓存一次** —— 用它贪心算"两行能放几项"。</summary>
    private double[]? _itemWidths;

    /// <summary>末尾「⋯」按钮自己的宽 —— 算两行预算时先给它留位，保证它永远落得下。</summary>
    private double _moreWidth;

    private AssetAllViewModel? _toolbarVm;
    private int _appliedCapacity = -1;

    /// <summary>
    /// 按容器宽度重排工具条：**两行内放得下就全显示，放不下才把序列尾部几项收进「⋯」**。
    /// <para>约定：<c>ToolbarPanel</c> 的**最后一个**子元素就是那个「⋯」按钮；其余按 XAML 顺序 = **优先级顺序**。</para>
    /// <para>🔴 为什么不会布局循环 / 抖动：项宽**只在首次布局后测一次并缓存**，之后每次只按"容器宽 + 缓存宽"做纯计算
    /// ⇒ 折叠结果**不反过来影响计算输入**（容器宽由窗口决定，与内容无关）。</para>
    /// </summary>
    private void ApplyToolbarLayout()
    {
        if (ToolbarPanel is null) return;                        // 极早期（DataContext 先于 XAML 树就绪）的兜底
        var children = ToolbarPanel.Children;
        if (children.Count < 2) return;

        var width = ToolbarPanel.Bounds.Width;
        if (width <= 0) return;                                  // 还没量到尺寸 → 等下一次布局回调

        var itemCount = children.Count - 1;                      // 末尾那个是「⋯」本身

        // ① 首次（或子元素数变了）：此刻子元素都还可见 → 记下各自宽。
        //    ⚠️ 任一项量到 0 就整体放弃缓存（说明布局还没完成），⛔ 不能把脏数据存下来。
        if (_itemWidths is null || _itemWidths.Length != itemCount)
        {
            var widths = new double[itemCount];
            for (var i = 0; i < itemCount; i++)
            {
                var w = children[i].Bounds.Width;
                if (w <= 0) return;
                widths[i] = w;
            }
            var moreWidth = children[itemCount].Bounds.Width;
            if (moreWidth <= 0) return;
            _itemWidths = widths;
            _moreWidth = moreWidth;
        }

        var expanded = _toolbarVm?.IsToolbarExpanded == true;
        var capacity = expanded ? itemCount : FitTwoRows(width);

        if (capacity == _appliedCapacity) return;                // 幂等短路：容量没变就不动（避免无谓重排）
        _appliedCapacity = capacity;

        for (var i = 0; i < itemCount; i++)
            children[i].IsVisible = i < capacity;

        // 「⋯」：收起态且确实有东西被折时才显示；展开态保留（否则没法收起）；全放得下则自己消失。
        children[itemCount].IsVisible = expanded || capacity < itemCount;
    }

    /// <summary>
    /// 贪心算"容器宽 <paramref name="width"/> 下、**两行**内能放下几项"（先给末尾「⋯」留出位置）。
    /// <para>逐行累计、超出容器宽就换行、两行预算用完即停 —— 与 <c>WrapPanel</c> 的实际排布规则一致。</para>
    /// </summary>
    private int FitTwoRows(double width)
    {
        var widths = _itemWidths;
        if (widths is null) return 0;

        var spacing = ToolbarPanel.ItemSpacing;
        var budget = width * 2 - _moreWidth - spacing;           // 两行总宽，先扣掉「⋯」的占位
        if (budget <= 0) return 1;                              // 极端窄也留一项：⛔ 不许工具条整条空掉

        var rowUsed = 0.0;                                       // 当前行已占宽（含间距）
        var total = 0.0;                                         // 两行合计（含间距）
        var count = 0;

        foreach (var w in widths)
        {
            var add = (rowUsed > 0 ? spacing : 0) + w;
            if (rowUsed > 0 && rowUsed + add > width)
            {
                rowUsed = 0;                                     // 该换行了
                add = w;
            }
            if (total + add > budget) break;                     // 两行预算用尽 → 剩下的都进「⋯」
            rowUsed += add;
            total += add;
            count++;
        }
        return count;
    }

    /// <summary>订阅 / 退订当前 VM 的 <see cref="AssetAllViewModel.IsToolbarExpanded"/>（点「⋯」后要立即重排）。</summary>
    private void HookToolbarVm()
    {
        var vm = DataContext as AssetAllViewModel;
        if (ReferenceEquals(_toolbarVm, vm)) return;

        if (_toolbarVm != null) _toolbarVm.PropertyChanged -= OnToolbarVmPropertyChanged;
        _toolbarVm = vm;
        if (_toolbarVm != null) _toolbarVm.PropertyChanged += OnToolbarVmPropertyChanged;

        _appliedCapacity = -1;                               // 换了 VM → 强制重算一次
    }

    private void OnToolbarVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AssetAllViewModel.IsToolbarExpanded)) ApplyToolbarLayout();
    }
}
