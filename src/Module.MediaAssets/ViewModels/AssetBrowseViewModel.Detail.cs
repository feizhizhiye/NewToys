using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.MediaAssets.Models;
using NewToys.MediaAssets.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

// Core 里也有一个 FileOperationService（纯磁盘动作，跨模块共用）。
// 本页用的是**带库表日志**的模块版，显式取别名，免得两个 using 撞名（CS0104）。
using FileOperationService = NewToys.MediaAssets.Services.FileOperationService;

namespace NewToys.MediaAssets.ViewModels;

public partial class AssetBrowseViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    // ==================== 工具条（由 Shell 统一渲染） ====================

    /// <summary>
    /// 工具条：视图模式 / 排序（按钮组）+ 类型 + 搜索 + 加入资产库 + 重读 + 上一级 + 目录树开合。
    /// <para>缩略图**进目录即自动补**（见 <c>AutoGenerateThumbs</c>），⛔ 不占工具条位置。</para>
    /// <para>单项操作（打开 / 重命名 / 删除）按 §4.8 硬规则 9 归**右键菜单**，⛔ 不上工具条。</para>
    /// </summary>
    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new()
        {
            Kind = ToolbarItemKind.ChipGroup, ItemsPath = nameof(ViewModes), DisplayPath = nameof(ChoiceItem.Label),
            SelectedPath = nameof(ChoiceItem.IsSelected), CommandPath = nameof(SetViewModeCommand),
            ToolTip = "图标卡 / 详细信息两种视图（即时切换，选择会被记住）"
        },
        new()
        {
            Kind = ToolbarItemKind.ChipGroup, ItemsPath = nameof(SortChips), DisplayPath = nameof(ChoiceItem.Label),
            SelectedPath = nameof(ChoiceItem.IsSelected), CommandPath = nameof(SetSortCommand),
            ToolTip = "排序：再点一次同一项 = 反向"
        },
        new()
        {
            Kind = ToolbarItemKind.Combo, ItemsPath = nameof(TypeOptions), DisplayPath = nameof(FilterOption.Label),
            SelectedItemPath = nameof(FilterType), Width = 104, ToolTip = "目录内按类型过滤"
        },
        new()
        {
            Kind = ToolbarItemKind.TextField, Width = 110, Placeholder = "在当前目录搜索…",
            ValuePath = nameof(SearchText), ToolTip = "按名称 / 路径搜索当前目录（空格分词，多个词要同时命中）"
        },
        new()
        {
            Text = "➕ 加入资产库", CommandPath = nameof(AddToLibraryCommand), IsEnabledPath = "!" + nameof(IsBusy),
            ShowToolTipOnDisabled = true,
            ToolTip = "把选中的文件 / 文件夹（或当前目录）加入资产库：文件只索引自身，文件夹递归扫描"
        },
        new() { Text = "🔄 重读", CommandPath = nameof(RefreshCommand), IsEnabledPath = "!" + nameof(IsBusy), ToolTip = "重新读取索引（⛔ 不扫盘，目录内容本来就是实时的）" },
        new() { Kind = ToolbarItemKind.Separator },
        new()
        {
            Text = "↥ 上一级", CommandPath = nameof(GoUpCommand), IsEnabledPath = nameof(CanGoUp),
            ShowToolTipOnDisabled = true, ToolTip = "回到上一层目录"
        },
        new() { Text = "◧ 目录树", CommandPath = nameof(ToggleTreeCommand), ToolTip = "显示 / 收起左侧目录树（腾出列表宽度）" },
    };

    // ==================== 详情卡（只读；单条动作在右键菜单） ====================

    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor? _card;

    /// <summary>
    /// 本页详情卡：**只读展示 + 打开动作**（重命名 / 删除这类写操作留在右键菜单）。
    /// </summary>
    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        EmptyText = "在右侧选一项，这里显示它的详情。",
        Sections =
        {
            new DetailSection
            {
                Fields =
                {
                    new DetailField
                    {
                        Label = "名称", ValuePath = "DetailRow.Name", Kind = FieldKind.Text,
                        Selectable = true, Mono = true
                    },
                    new DetailField { Label = "类型", ValuePath = "DetailRow.TypeText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "大小", ValuePath = "DetailRow.SizeText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "修改时间", ValuePath = "DetailRow.ModifiedText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "存储源", ValuePath = "DetailRow.StorageName", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "所在目录", ValuePath = nameof(DetailFolder), Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "完整路径", ValuePath = nameof(DetailPath), Kind = FieldKind.Text, Selectable = true, Mono = true },
                    new DetailField { Label = "时长", ValuePath = "DetailRow.DurationText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "分辨率", ValuePath = "DetailRow.ResolutionText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "编码", ValuePath = "DetailRow.CodecText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "标签", ValuePath = "DetailRow.TagsText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "分类", ValuePath = "DetailRow.CategoriesText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "集合", ValuePath = "DetailRow.CollectionsText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "📁 这是一个文件夹：双击进入，右键可把整棵目录加入资产库。",
                        VisibleWhen = nameof(DetailIsFolder)
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "⚠ 这一项还没加入资产库（列表里标灰）：能打开 / 定位，库操作请先「➕ 加入资产库」。",
                        VisibleWhen = "!DetailIsInLibrary"
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "⚠ 这个文件在磁盘上找不到了（最近一次扫描没见到它）。",
                        VisibleWhen = "DetailRow.IsMissing"
                    }
                }
            }
        },
        Actions =
        {
            new DetailAction
            {
                Text = "📂 打开", Classes = "primary", Command = OpenSelectedCommand,
                IsVisiblePath = nameof(ShowDetailBody), ToolTip = "用系统默认程序打开（文件夹 = 进入）；也可以双击"
            },
            new DetailAction
            {
                Text = "📁 打开所在文件夹", Classes = "ghost", Command = OpenFolderSelectedCommand,
                IsVisiblePath = nameof(ShowDetailBody), ToolTip = "在资源管理器里定位它"
            },
            new DetailAction
            {
                Text = "➕ 加入资产库", Classes = "ghost", Command = AddToLibraryCommand,
                IsVisiblePath = "!DetailIsInLibrary", ToolTip = "把这一项加入资产库（文件夹会递归扫描）"
            }
        }
    };

    /// <summary>详情卡「所在目录」显示串（相对存储源内的目录；未入库给绝对上级目录）。</summary>
    public string DetailFolder
    {
        get
        {
            if (Selected == null) return "";
            if (!Selected.InLibrary) return AssetBrowseService.ParentDir(Selected.AbsPath) ?? "";

            var dir = Selected.Dir.Length == 0 ? AssetBrowseService.RootDirName : Selected.Dir;
            var storage = Selected.Row.StorageName;
            return string.IsNullOrEmpty(storage) ? dir : $"{storage} › {dir}";
        }
    }

    /// <summary>详情卡「完整路径」。</summary>
    public string DetailPath => Selected?.AbsPath ?? "";

    /// <summary>当前选中项是否已入库（详情卡提示 / 加入按钮的可见性）。</summary>
    public bool DetailIsInLibrary => Selected?.InLibrary ?? true;

    /// <summary>当前选中项是不是文件夹。</summary>
    public bool DetailIsFolder => Selected?.IsFolder ?? false;

    // ==================== IDetailPanelProvider ====================

    public event EventHandler? DetailChanged;

    public bool IsDetailOpen => IsDetailVisible;

    /// <summary>本页走**声明式**详情卡（<see cref="Card"/> 非 null）⇒ 自绘内容恒为 null。</summary>
    public object? DetailContent => null;

    public bool HasDetailPanel => true;

    /// <summary>工具条末位的详情开关调这个（⛔ 别绑基类的 <c>ToggleDetailCommand</c>：它不通知 Shell）。</summary>
    [RelayCommand]
    private void ToggleDetailPanel()
    {
        IsDetailVisible = !IsDetailVisible;
        SaveDetailFlag();
        RaiseDetailChanged();
    }

    public void OpenDetail()
    {
        IsDetailVisible = true;
        SaveDetailFlag();
        RaiseDetailChanged();
    }

    public void ToggleDetail() => ToggleDetailPanel();

    private void RaiseDetailChanged() => DetailChanged?.Invoke(this, EventArgs.Empty);

    // ==================== 单条动作（右键菜单 / 键盘） ====================

    /// <summary>📂 打开（双击 / Enter / 右键）：文件夹 = 进入；文件 = 系统默认程序打开。</summary>
    [RelayCommand]
    private void OpenSelected()
    {
        var tile = Selected;
        if (tile == null) return;

        if (tile.IsFolder)
        {
            _ = NavigateToAsync(tile.AbsPath);
            return;
        }

        var path = tile.AbsPath;
        RunOffUiThread(() =>
        {
            if (!File.Exists(path)) return $"文件不存在（可能已被移动或删除）：{path}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            return $"已用系统默认程序打开「{tile.Row.FileName}」。";
        }, "打开失败：");
    }

    /// <summary>📁 打开所在文件夹：文件 = 在资源管理器里选中它；文件夹 = 打开该文件夹本身。</summary>
    [RelayCommand]
    private void OpenFolderSelected()
    {
        var tile = Selected;
        if (tile == null) return;

        var path = tile.AbsPath;
        var isFolder = tile.IsFolder;
        RunOffUiThread(() =>
        {
            if (isFolder)
            {
                if (!Directory.Exists(path)) return $"目录不存在（可能已被移动或删除）：{path}";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                return "已在资源管理器中打开该文件夹。";
            }

            if (!File.Exists(path)) return $"文件不存在（可能已被移动或删除）：{path}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = true,
                Arguments = $"/select,\"{path}\""
            });
            return "已在资源管理器中定位该文件。";
        }, "打开文件夹失败：");
    }

    /// <summary>只读一条 → 在**线程池**里启动 shell，结果文案回投 UI 线程（⛔ 不占 UI 线程）。</summary>
    private void RunOffUiThread(Func<string> work, string failPrefix)
    {
        _ = Task.Run(() =>
        {
            string text;
            try { text = work(); }
            catch (Exception ex) { text = failPrefix + ex.Message; }
            Dispatcher.UIThread.Post(() => StatusText = text);
        });
    }

    /// <summary>
    /// ✏ 重命名（真改磁盘文件名，走任务队列 + 日志先行；同名冲突按「追加序号」，⛔ 不覆盖）。
    /// <para>只对**单个已入库文件**开放：文件夹与未入库项请回「📁 打开所在文件夹」用资源管理器改。</para>
    /// </summary>
    [RelayCommand]
    private async Task RenameSelectedAsync()
    {
        if (Selected == null) { StatusText = "先选中一个文件。"; return; }

        if (Selected.IsFolder)
        {
            StatusText = "⚠ 文件夹不支持在这里重命名 —— 用「📁 打开所在文件夹」在资源管理器里改名。";
            return;
        }

        if (!Selected.InLibrary)
        {
            StatusText = $"⚠「{Selected.Name}」还没加入资产库：先右键「➕ 加入资产库」再重命名。";
            return;
        }

        var tiles = BatchTiles();
        if (tiles.Count > 1) { StatusText = "重命名一次只能改一个文件。"; return; }

        var tile = Selected;
        var old = tile.Row.FileName;
        var input = await DialogHooks.InputAsync("重命名文件", Path.GetFileNameWithoutExtension(old)).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(input)) { StatusText = "已取消。"; return; }

        var newName = input.Trim();
        if (newName == Path.GetFileNameWithoutExtension(old)) { StatusText = "名称没变，已取消。"; return; }

        var loc = LocationOf(tile);
        _queue.Enqueue($"重命名 {old}", async (ct, p) =>
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                p.Report($"正在把「{old}」改成「{newName}」…");
                var r = _fileOps.Rename(loc, newName, ConflictPolicy.AppendNumber);
                p.Report(r.Message);
            }, ct).ConfigureAwait(false);
            Dispatcher.UIThread.Post(() => { Reload(restoreLastFolder: false); StatusText = "重命名完成（列表已刷新）。"; });
        });
        StatusText = $"已把「{old}」的重命名排进任务队列（进度看「任务中心」）。";
    }

    /// <summary>🗑 移入回收站：批量软删除（只改库里状态，⛔ 不动磁盘文件）。</summary>
    [RelayCommand]
    private async Task TrashSelectedAsync()
    {
        var tiles = LibraryFileTiles();
        if (tiles.Count == 0) { WarnNoLibrarySelection("移入回收站"); return; }

        if (!await DialogHooks.ConfirmAsync(
                $"把选中的 {tiles.Count} 项移入回收站？\n" +
                "⛔ 只改库里的状态，**不动磁盘文件**；回收站里能恢复，也能永久删除。").ConfigureAwait(true))
        { StatusText = "已取消。"; return; }

        var assetIds = tiles.Select(t => t.Row.Id).Distinct().ToList();
        foreach (var id in assetIds)
        {
            _assets.SetAssetStatus(id, AssetStatuses.Trashed);
            _opLog.Log("trash", "asset", id, beforeJson: "{\"status\":\"active\"}", afterJson: "{\"status\":\"trashed\"}");
        }

        Reload(restoreLastFolder: false);
        StatusText = $"已把 {assetIds.Count} 个资产移入回收站（磁盘文件没动）。";
    }

    /// <summary>⤫ 从库移除：只删这几条**位置行**（磁盘文件一个都不动）；资产没有其它位置了就一起清掉。</summary>
    [RelayCommand]
    private async Task RemoveFromLibraryAsync()
    {
        var tiles = LibraryFileTiles();
        if (tiles.Count == 0) { WarnNoLibrarySelection("从库移除"); return; }

        if (!await DialogHooks.ConfirmAsync(
                $"从库里移除选中的 {tiles.Count} 项索引？\n" +
                "⛔ 磁盘文件一个都不动（只是不再索引它们）；想恢复就重新扫描该目录。").ConfigureAwait(true))
        { StatusText = "已取消。"; return; }

        var removed = 0;
        foreach (var tile in tiles)
        {
            if (_assets.DeleteLocation(tile.LocationId)) removed++;
            if (_assets.ListLocations(tile.Row.Id).Count == 0) _assets.DeleteAsset(tile.Row.Id);
        }

        Reload(restoreLastFolder: false);
        StatusText = $"已从库里移除 {removed} 条索引（磁盘文件没动）。";
    }

    /// <summary>
    /// ⛔ 从磁盘删除：**破坏性最大**的操作（不进回收站、不可还原）——
    /// 只放右键菜单 + 二次确认（列出文件清单）+ 走任务队列，⛔ 不在 UI 线程直接删。
    /// <para>对象 = 当前选中的已入库文件；一条失败不影响其余，末尾报失败数。</para>
    /// </summary>
    [RelayCommand]
    private async Task DeleteFromDiskAsync()
    {
        var tiles = LibraryFileTiles();
        if (tiles.Count == 0) { WarnNoLibrarySelection("从磁盘删除"); return; }

        var names = tiles.Select(t => Path.GetFileName(t.AbsPath)).ToList();
        var preview = string.Join("\n", names.Take(6).Select(n => "· " + n));
        if (names.Count > 6) preview += $"\n· …（共 {names.Count} 个文件）";

        if (!await DialogHooks.ConfirmAsync(
                $"⛔ 从磁盘**永久删除**这 {tiles.Count} 个文件？\n\n{preview}\n\n" +
                "· 不进系统回收站、无法还原；\n" +
                "· 库里对应的文件位置记录一起删掉（资产还有其它位置的，资产本体保留）；\n" +
                "· 只想从库里移除、文件留着 ⇒ 用「⤫ 从库移除」。").ConfigureAwait(true))
        { StatusText = "已取消。"; return; }

        var jobs = tiles.Select(LocationOf).Where(l => l != null).Select(l => l!).ToList();
        _queue.Enqueue($"从磁盘删除 {jobs.Count} 个文件", async (ct, p) =>
        {
            var deleted = 0;
            var failed = 0;
            await Task.Run(() =>
            {
                for (var i = 0; i < jobs.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var loc = jobs[i];
                    var name = Path.GetFileName(loc.Path);
                    p.Report($"正在永久删除（{i + 1}/{jobs.Count}）「{name}」…");

                    var r = _fileOps.PermanentDelete(loc);
                    if (!r.Ok) { failed++; p.Report($"「{name}」删除失败：{r.Message}"); continue; }

                    deleted++;
                    _assets.DeleteLocation(loc.Id);
                    if (_assets.ListLocations(loc.AssetId).Count == 0) _assets.DeleteAsset(loc.AssetId);
                }
            }, ct).ConfigureAwait(false);

            p.Report(failed == 0
                ? $"已永久删除 {deleted} 个文件。"
                : $"已永久删除 {deleted} 个文件，{failed} 个失败（看任务中心明细）。");

            Dispatcher.UIThread.Post(() => Reload(restoreLastFolder: false));
        });
        StatusText = $"已把 {jobs.Count} 个文件的永久删除排进任务队列（进度看「任务中心」）。";
    }

    /// <summary>
    /// 批量库操作的候选：当前选中里**已入库的文件**。
    /// 文件夹与未入库项不在此列（它们没有 location id，做不了库操作）。
    /// </summary>
    private List<AssetTile> LibraryFileTiles()
        => BatchTiles().Where(t => t.InLibrary && !t.IsFolder).ToList();

    /// <summary>
    /// 「未入库 / 全是文件夹」时的统一提醒（用户要求：功能置灰但可点，点了要说清为什么）。
    /// </summary>
    private void WarnNoLibrarySelection(string action)
    {
        if (Selected is { IsFolder: true })
        {
            StatusText = $"⚠ 文件夹不支持「{action}」—— 先进去选中文件，或用「📁 打开所在文件夹」在资源管理器里处理。";
            return;
        }

        StatusText = BatchTiles().Count == 0
            ? "先选中要操作的项。"
            : $"⚠ 选中的项都还没加入资产库，不能「{action}」—— 先右键「➕ 加入资产库」。";
    }

    /// <summary>位置行 → <see cref="FileLocation"/>（只带物理操作需要的字段）。</summary>
    private static FileLocation LocationOf(AssetTile tile) => new()
    {
        Id = tile.LocationId,
        AssetId = tile.Row.Id,
        StorageId = tile.Row.StorageId,
        Path = tile.AbsPath,
        FileName = tile.Row.FileName,
        Ext = Path.GetExtension(tile.AbsPath),
        Size = tile.Row.Size,
        Mtime = tile.Row.Mtime
    };
}
