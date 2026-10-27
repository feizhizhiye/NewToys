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
    /// 工具条：视图模式 / 图标尺寸 / 排序（都是按钮组）+ 类型 + 搜索 + 生成本目录缩略图 + 重读 + 上一级 + 目录树开合。
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
            Kind = ToolbarItemKind.ChipGroup, ItemsPath = nameof(IconSizes), DisplayPath = nameof(ChoiceItem.Label),
            SelectedPath = nameof(ChoiceItem.IsSelected), CommandPath = nameof(SetIconSizeCommand),
            IsVisiblePath = nameof(IsTileView), ToolTip = "图标卡尺寸（96 / 144 / 200）"
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
            ValuePath = nameof(SearchText), ToolTip = "按名称 / 路径 / 标签搜索当前目录（空格分词，多个词要同时命中）"
        },
        new()
        {
            Text = "🖼 生成本目录缩略图", CommandPath = nameof(GenerateThumbsCommand),
            IsEnabledPath = nameof(CanGenerateThumbs), ShowToolTipOnDisabled = true,
            ToolTip = "给本目录里还没有缩略图的视频 / 图片逐条生成（走任务队列，可取消，单条失败不停批）"
        },
        new() { Text = "🔄 重读", CommandPath = nameof(RefreshCommand), IsEnabledPath = "!" + nameof(IsBusy), ToolTip = "重新读取索引（⛔ 不扫盘）" },
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
    /// 本页详情卡：**只读展示 + 两个打开动作**（重命名 / 删除这类写操作留在右键菜单）。
    /// </summary>
    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        EmptyText = "在右侧选一个文件，这里显示它的详情。",
        Sections =
        {
            new DetailSection
            {
                Fields =
                {
                    new DetailField
                    {
                        Label = "文件名", ValuePath = "DetailRow.FileName", Kind = FieldKind.Text,
                        Selectable = true, Mono = true
                    },
                    new DetailField { Label = "类型", ValuePath = "DetailRow.TypeText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "大小", ValuePath = "DetailRow.SizeText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "修改时间", ValuePath = "DetailRow.ModifiedText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "存储源", ValuePath = "DetailRow.StorageName", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "所在目录", ValuePath = nameof(DetailFolder), Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "文件路径", ValuePath = "DetailRow.FilePath", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "时长", ValuePath = "DetailRow.DurationText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "分辨率", ValuePath = "DetailRow.ResolutionText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "编码", ValuePath = "DetailRow.CodecText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "标签", ValuePath = "DetailRow.TagsText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "分类", ValuePath = "DetailRow.CategoriesText", Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "集合", ValuePath = "DetailRow.CollectionsText", Kind = FieldKind.Text, Selectable = true },
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
                IsVisiblePath = nameof(ShowDetailBody), ToolTip = "用系统默认程序打开（视频 = 播放 / 图片 = 看图）"
            },
            new DetailAction
            {
                Text = "📁 打开所在文件夹", Classes = "ghost", Command = OpenFolderSelectedCommand,
                IsVisiblePath = nameof(ShowDetailBody), ToolTip = "在资源管理器里选中这个文件"
            }
        }
    };

    /// <summary>详情卡「所在目录」显示串（存储源内的相对目录；根目录给个明确说法）。</summary>
    public string DetailFolder
    {
        get
        {
            if (Selected == null) return "";
            var dir = Selected.Dir.Length == 0 ? AssetBrowseService.RootDirName : Selected.Dir;
            var storage = Selected.Row.StorageName;
            return string.IsNullOrEmpty(storage) ? dir : $"{storage} › {dir}";
        }
    }

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

    /// <summary>📂 打开（双击 / Enter / 右键）：用系统默认程序打开选中的文件。</summary>
    [RelayCommand]
    private void OpenSelected()
    {
        var tile = Selected;
        if (tile == null) return;

        var path = tile.Row.FilePath;
        RunOffUiThread(() =>
        {
            if (!File.Exists(path)) return $"文件不存在（可能已被移动或删除）：{path}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            return $"已用系统默认程序打开「{tile.Row.FileName}」。";
        }, "打开失败：");
    }

    /// <summary>📁 打开所在文件夹：在资源管理器里选中该文件。</summary>
    [RelayCommand]
    private void OpenFolderSelected()
    {
        var tile = Selected;
        if (tile == null) return;

        var path = tile.Row.FilePath;
        RunOffUiThread(() =>
        {
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
    /// 只对**单个**文件开放：批量重命名需要命名模板，不在本页职责内。
    /// </summary>
    [RelayCommand]
    private async Task RenameSelectedAsync()
    {
        if (Selected == null) { StatusText = "先选中一个文件。"; return; }
        if (SelectedRows.Count > 1) { StatusText = "重命名一次只能改一个文件。"; return; }

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
        var tiles = BatchTiles();
        if (tiles.Count == 0) { StatusText = "先选中要移除的文件。"; return; }

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
        var tiles = BatchTiles();
        if (tiles.Count == 0) { StatusText = "先选中要移除的文件。"; return; }

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
    /// 只放右键菜单 + 二次确认 + 走任务队列，⛔ 不在 UI 线程直接删。
    /// </summary>
    [RelayCommand]
    private async Task DeleteFromDiskAsync()
    {
        var tiles = BatchTiles();
        if (tiles.Count == 0) { StatusText = "先选中要删除的文件。"; return; }

        if (!await DialogHooks.ConfirmAsync(
                $"⛔ 从磁盘**永久删除**选中的 {tiles.Count} 个文件？\n\n" +
                "· 不进系统回收站、无法还原；\n" +
                "· 库里对应的文件索引也会一起删掉（同一资产在别处的位置不受影响）；\n" +
                "· 只想从库里移除、文件留着 ⇒ 用「⤫ 从库移除」。").ConfigureAwait(true))
        { StatusText = "已取消。"; return; }

        var jobs = tiles.Select(LocationOf).Where(l => l != null).Select(l => l!).ToList();
        _queue.Enqueue($"从磁盘删除 {jobs.Count} 个文件", async (ct, p) =>
        {
            await Task.Run(() =>
            {
                foreach (var loc in jobs)
                {
                    ct.ThrowIfCancellationRequested();
                    p.Report($"正在永久删除「{Path.GetFileName(loc.Path)}」…");
                    var r = _fileOps.PermanentDelete(loc);
                    p.Report(r.Message);
                    if (!r.Ok) continue;
                    _assets.DeleteLocation(loc.Id);
                    if (_assets.ListLocations(loc.AssetId).Count == 0) _assets.DeleteAsset(loc.AssetId);
                }
            }, ct).ConfigureAwait(false);
            Dispatcher.UIThread.Post(() => Reload(restoreLastFolder: false));
        });
        StatusText = $"已把 {jobs.Count} 个文件的永久删除排进任务队列（进度看「任务中心」）。";
    }

    /// <summary>位置行 → <see cref="FileLocation"/>（只带物理操作需要的字段）。</summary>
    private static FileLocation LocationOf(AssetTile tile) => new()
    {
        Id = tile.LocationId,
        AssetId = tile.Row.Id,
        StorageId = tile.Row.StorageId,
        Path = tile.Row.FilePath,
        FileName = tile.Row.FileName,
        Ext = Path.GetExtension(tile.Row.FilePath),
        Size = tile.Row.Size,
        Mtime = tile.Row.Mtime
    };
}
