using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
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

public partial class AssetAllViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    /// <summary>
    /// 详情卡「保存」：一次把**评分 / 描述 / 状态 / 标签 / 分类 / 集合**全部落库，并写一条 `operation_logs`。
    /// ⛔ 不动磁盘文件（物理重命名是卡底的「应用到文件」）。
    /// </summary>
    [RelayCommand]
    private void SaveAssetDetail()
    {
        if (_loadingDetail || Selected == null) { StatusText = "先在左侧选中一个资产。"; return; }

        var row = Selected;
        var before = $"{{\"rating\":{row.Rating},\"status\":\"{row.Status}\"}}";

        _assets.SetRatingAndDescription(row.Id, EditRating, string.IsNullOrWhiteSpace(EditDescription) ? null : EditDescription);
        row.Rating = EditRating;
        row.Description = string.IsNullOrWhiteSpace(EditDescription) ? null : EditDescription;

        // 状态（单选下拉 → 存库的 key）
        var status = StatusKeys[Math.Clamp(EditStatusIndex, 0, StatusKeys.Length - 1)];
        if (!string.Equals(row.Status, status, StringComparison.Ordinal))
        {
            _assets.SetAssetStatus(row.Id, status);
            row.Status = status;
        }

        var tagIds = TagPicks.Where(p => p.IsSelected).Select(p => p.Id).ToList();
        var catIds = CategoryPicks.Where(p => p.IsSelected).Select(p => p.Id).ToList();
        var colIds = CollectionPicks.Where(p => p.IsSelected).Select(p => p.Id).ToList();
        _tagSvc.SetAssetTags(row.Id, tagIds);
        _catSvc.SetAssetCategories(row.Id, catIds);
        _colSvc.SetAssetCollections(row.Id, colIds);

        _opLog.Log("metadata", "asset", row.Id, beforeJson: before,
            afterJson: $"{{\"rating\":{EditRating},\"tags\":{tagIds.Count},\"categories\":{catIds.Count},\"collections\":{colIds.Count}}}");

        RefreshView();   // 标签/分类/集合变了 ⇒ 按当前筛重排重取（选中项按 Id 认领，⛔ 不跳走）

        // 写库是短连接 + 静默 catch ⇒ 只有**从库里读回来**才算数（否则界面骗自己「已保存」）
        var fresh = Items.FirstOrDefault(r => r.Id == row.Id);
        if (fresh != null)
        {
            Selected = fresh;     // 触发 LoadDetail：详情卡显示的是库里的值，不是内存草稿
        }
        else
        {
            RaiseDetailChanged();
        }

        var stuck = fresh != null && fresh.Rating != EditRating;
        // 音乐资产的 5 个标签框（标题/艺术家/专辑/年份/流派）归「写入标签」管，保存不覆盖
        var musicHint = string.Equals(row.Type, AssetTypes.Music, StringComparison.OrdinalIgnoreCase)
            ? " 音乐标签（标题 / 艺术家 / 专辑 / 年份 / 流派）请点「写入标签」落盘。"
            : "";
        StatusText = stuck
            ? "⚠ 评分没写进资产库（库不可用 / 被占用？）—— 点「重读」后再试一次。"
            : $"已保存「{row.Name}」的评分 / 描述 / 状态 / 标签 / 分类 / 集合（⛔ 未动磁盘文件）。{musicHint}";
    }

    /// <summary>
    /// 详情卡「应用到文件」：⚠️ 与「保存」完全不同 ——
    /// 「保存」只写**库里的元数据**（⛔ 不动磁盘）；这个才是**把「文件名」框里的新名字真写到磁盘文件上**。
    /// <para>物理操作 → 走任务队列 + 日志先行；同名冲突按「追加序号」处理（⛔ 不覆盖，覆盖会丢文件）。</para>
    /// </summary>
    [RelayCommand]
    private async Task ApplyToFileAsync()
    {
        if (Selected == null) { StatusText = "先在左侧选中一个资产。"; return; }

        var row = Selected;
        var loc = _assets.FindPrimaryLocation(row.Id) ?? _assets.ListLocations(row.Id).FirstOrDefault();
        if (loc == null) { StatusText = "这个资产没有登记文件位置，没法应用到文件。"; return; }
        if (!IsDbAvailable) { StatusText = "资产库不可用。"; return; }

        var current = Path.GetFileName(loc.Path);
        var name = (EditFileName ?? "").Trim();
        if (name.Length == 0) { StatusText = "先在详情卡「文件名」里填新名字（含扩展名）。"; return; }
        if (string.Equals(name, current, StringComparison.Ordinal))
        { StatusText = "文件名没变（在详情卡「文件名」里改一个新名字再应用）。"; return; }

        _fileOps.Queue.Enqueue($"重命名 {current}", async (ct, p) =>
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                p.Report($"正在把「{current}」改成「{name}」…");
                var r = _fileOps.Rename(loc, name, ConflictPolicy.AppendNumber);
                p.Report(r.Message);
            }, ct).ConfigureAwait(false);

            try { Dispatcher.UIThread.Post(Reload); } catch { /* 已关闭 */ }
        });

        StatusText = $"已把「{current}」→「{name}」的重命名排进任务队列（进度看「任务中心」）。";
    }

    /// <summary>详情卡「移入回收站」= **软删除**（只改 `status`，⛔ 不删行、⛔ 不动磁盘文件）。</summary>
    [RelayCommand]
    private async Task MoveToTrashAsync()
    {
        if (Selected == null) { StatusText = "先选中一个资产。"; return; }

        var row = Selected;
        if (!await DialogHooks.ConfirmAsync($"把「{row.Name}」移入回收站？（⛔ 只改库里的状态，**不动磁盘文件**；回收站里能恢复）").ConfigureAwait(true))
        { StatusText = "已取消。"; return; }

        _assets.SetAssetStatus(row.Id, AssetStatuses.Trashed);
        _opLog.Log("trash", "asset", row.Id, beforeJson: "{\"status\":\"active\"}", afterJson: "{\"status\":\"trashed\"}");
        Reload();
        StatusText = $"已把「{row.Name}」移入回收站（磁盘文件没动）。";
    }

    /// <summary>
    /// 列表右键「⛔ 从磁盘删除」：**本程序唯一**直接删磁盘文件的入口（批量口径 = 当前选中行）。
    /// <para>
    /// 🔴 三重防误触（这是索引库里**破坏性最大**的操作，故意把点击成本做高）：
    /// ① 不进详情卡 / 不上工具条，**只在行右键菜单里**；② 二次确认（文案写明删几个、哪几个、"不进回收站、不可还原"）；
    /// ③ 真删走**任务队列**（任务中心可见、可取消；一条失败不影响其余）。
    /// </para>
    /// <para>只想从库里移除（文件留着）→ 用「⤫ 从库移除」；只想标记 → 详情卡「移入回收站」。</para>
    /// </summary>
    [RelayCommand]
    private async Task DeleteFromDiskAsync()
    {
        var rows = BatchRows();
        if (rows.Count == 0)
        {
            StatusText = "先选中要从磁盘删除的资产（Ctrl 增选 / Shift 连选 / Ctrl+A 全选当前页）。";
            return;
        }

        var targets = new List<(AssetRow Row, FileLocation Loc)>();
        var skipped = 0;
        foreach (var row in rows)
        {
            var loc = PrimaryLocationOf(row);
            if (loc is null) { skipped++; continue; }
            targets.Add((row, loc));
        }

        if (targets.Count == 0)
        {
            StatusText = "选中的资产都没有登记文件位置，没法从磁盘删除。";
            return;
        }

        var names = targets.Select(t => Path.GetFileName(t.Loc.Path)).ToList();
        var preview = string.Join("\n", names.Take(6).Select(n => "· " + n));
        if (names.Count > 6) preview += $"\n· …（共 {names.Count} 个文件）";

        var skipHint = skipped > 0 ? $"\n\n另有 {skipped} 条没有登记文件位置，将被跳过。" : "";

        if (!await DialogHooks.ConfirmAsync(
                $"⛔ 从磁盘**永久删除**这 {targets.Count} 个文件？\n\n{preview}{skipHint}\n\n" +
                "· 不进系统回收站、无法还原；\n" +
                "· 库里对应的文件位置记录一起删掉（资产还有其它位置的，资产本体保留）；\n" +
                "· 只想从库里移除、文件留着 ⇒ 用「⤫ 从库移除」；\n" +
                "· 只想打标记 ⇒ 详情卡「移入回收站」。").ConfigureAwait(true))
        { StatusText = "已取消。"; return; }

        var title = targets.Count == 1
            ? $"从磁盘删除 {names[0]}"
            : $"从磁盘删除 {targets.Count} 个文件";

        _fileOps.Queue.Enqueue(title, async (ct, p) =>
        {
            var deleted = 0;
            var failed = 0;
            await Task.Run(() =>
            {
                for (var i = 0; i < targets.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var (row, loc) = targets[i];
                    var name = Path.GetFileName(loc.Path);
                    p.Report($"正在永久删除（{i + 1}/{targets.Count}）「{name}」…");

                    var r = _fileOps.PermanentDelete(loc);
                    if (!r.Ok) { failed++; p.Report($"「{name}」删除失败：{r.Message}"); continue; }

                    deleted++;
                    _assets.DeleteLocation(loc.Id);
                    // 这条资产的**所有**位置都删光了 ⇒ 资产本体也没意义了（连带挂接一起清）
                    if (_assets.ListLocations(row.Id).Count == 0) _assets.DeleteAsset(row.Id);
                }
            }, ct).ConfigureAwait(false);

            p.Report(failed == 0
                ? $"已永久删除 {deleted} 个文件。"
                : $"已永久删除 {deleted} 个文件，{failed} 个失败（看任务中心明细）。");

            try { Dispatcher.UIThread.Post(Reload); } catch { /* 已关闭 */ }
        });

        StatusText = targets.Count == 1
            ? $"已把「{names[0]}」的永久删除排进任务队列（进度看「任务中心」）。"
            : $"已把 {targets.Count} 个文件的永久删除排进任务队列（进度看「任务中心」）。";
    }

    // ==================== 移动 / 复制（目标 = 已登记存储源根；防穿越由 FileOperationService 保证） ====================

    private FileLocation? PrimaryLocationOf(AssetRow row)
        => _assets.FindPrimaryLocation(row.Id) ?? _assets.ListLocations(row.Id).FirstOrDefault();

    [RelayCommand]
    private async Task MoveToTargetAsync()
    {
        if (Selected == null) { StatusText = "先在左侧选中一个资产。"; return; }
        var loc = PrimaryLocationOf(Selected);
        if (loc == null) { StatusText = "这个资产没有登记文件位置，没法移动 / 复制。"; return; }
        var target = TargetStorages.FirstOrDefault(s => s.Id == SelectedTargetStorageId);
        if (target == null) { StatusText = "先选一个目标存储源。"; return; }
        if (!target.Online) { StatusText = $"目标存储源「{target.Name}」当前离线。"; return; }
        if (!await DialogHooks.ConfirmAsync(
                $"把「{Path.GetFileName(loc.Path)}」移动到「{target.Name}」？\n⛔ 只移到已登记存储源根内（不越界写盘）；同名按「追加序号」处理。").ConfigureAwait(true))
        { StatusText = "已取消。"; return; }

        _fileOps.Queue.Enqueue($"移动到 {target.Name}", async (ct, p) =>
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                p.Report($"正在移动到「{target.Name}」…");
                p.Report(_fileOps.Move(loc, target.RootPath, ConflictPolicy.AppendNumber).Message);
            }, ct).ConfigureAwait(false);
            try { Dispatcher.UIThread.Post(Reload); } catch { /* 已关闭 */ }
        });
        StatusText = $"已把移动排进任务队列（进度看「任务中心」）。";
    }

    [RelayCommand]
    private async Task CopyToTargetAsync()
    {
        if (Selected == null) { StatusText = "先在左侧选中一个资产。"; return; }
        var loc = PrimaryLocationOf(Selected);
        if (loc == null) { StatusText = "这个资产没有登记文件位置，没法复制。"; return; }
        var target = TargetStorages.FirstOrDefault(s => s.Id == SelectedTargetStorageId);
        if (target == null) { StatusText = "先选一个目标存储源。"; return; }
        if (!target.Online) { StatusText = $"目标存储源「{target.Name}」当前离线。"; return; }
        if (!await DialogHooks.ConfirmAsync(
                $"把「{Path.GetFileName(loc.Path)}」复制到「{target.Name}」？\n⛔ 只复制到已登记存储源根内；同名按「追加序号」处理。").ConfigureAwait(true))
        { StatusText = "已取消。"; return; }

        _fileOps.Queue.Enqueue($"复制到 {target.Name}", async (ct, p) =>
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                p.Report($"正在复制到「{target.Name}」…");
                p.Report(_fileOps.Copy(loc, target.RootPath, ConflictPolicy.AppendNumber).Message);
            }, ct).ConfigureAwait(false);
            try { Dispatcher.UIThread.Post(Reload); } catch { /* 已关闭 */ }
        });
        StatusText = $"已把复制排进任务队列（进度看「任务中心」）。";
    }

    // ==================== 跨页取件（标签 / 分类页点资产 → 跳到这里并选中） ====================

    /// <summary>
    /// 取件点（由 View 在 <c>OnAttachedToVisualTree</c> 调 —— 🔴 **不是** `OnDataContextChanged`，规范 §4.8 硬规则 8）：
    /// 别的页面请求"打开某个资产"时，在这里选中它。
    /// </summary>
    public void ConsumePendingAsset()
    {
        var req = _navStore?.Take();
        if (req == null) return;

        var hit = Items.FirstOrDefault(r => r.Id == req.Value.AssetId);
        if (hit == null)
        {
            StatusText = "目标资产不在当前列表（可能已移入回收站）。";
            return;
        }

        Selected = hit;
        if (!IsDetailVisible)
        {
            IsDetailVisible = true;
            SaveDetailFlag();
            RaiseDetailChanged();
        }
        StatusText = $"已定位到资产「{hit.Name}」。";
    }

    // ==================== 详情面板（由 Shell 的 ContentControl 承载） ====================

    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;

    /// <summary>声明式卡（<see cref="Card"/> 非 null）⇒ ⛔ 不返回自绘视图（有卡就不该再出自绘视图）。</summary>
    public object? DetailContent => null;

    private DetailCardDescriptor? _card;

}
