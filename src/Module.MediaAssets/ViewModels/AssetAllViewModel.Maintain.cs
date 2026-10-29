using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.MediaAssets.Models;
using NewToys.MediaAssets.Services;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>
/// 「全部资产」的**库维护**动作：清重复、从库移除、读媒体信息。
/// <para>
/// 三者都只在**索引层**动手（<c>assets</c> / <c>file_locations</c>），磁盘文件一个都不碰 ——
/// 物理删除仍然只有行右键的「⛔ 从磁盘删除…」一条路。
/// </para>
/// </summary>
public partial class AssetAllViewModel
{
    // ==================== ① 重复数据（同一物理路径 → 多条资产） ====================

    /// <summary>
    /// 「🔍 查重合并」：先报组数，确认后把「同一物理路径挂在多条资产下」的记录并成一条。
    /// <para>只认**完全相同的物理路径**（忽略大小写 / 分隔符）；不同路径的真副本要手工处理。</para>
    /// </summary>
    [RelayCommand]
    private async Task MergeDuplicateAssetsAsync()
    {
        if (!IsDbAvailable) { StatusText = "资产库不可用。"; return; }

        var groups = _assets.CountDuplicatePaths();
        if (groups == 0)
        {
            StatusText = "没有「同一个文件被重复收录」的组。若列表里仍看到重复行，" +
                         "说明它们指向**不同路径**（真副本）—— 选中多余的那几条（Ctrl / Shift），用「⤫ 从库移除」。";
            return;
        }

        if (!await DialogHooks.ConfirmAsync(
                $"发现 {groups} 个文件被重复收录（同一个物理路径挂在多条资产下）。\n\n" +
                "· 每组保留一条资产（正常状态优先、id 最小）；\n" +
                "· 其余资产的标签 / 分类 / 集合 / 关联 / 文件位置全部并过去后删除；\n" +
                "· ⛔ 磁盘文件一个都不动。\n\n继续合并？").ConfigureAwait(true))
        {
            StatusText = "已取消。";
            return;
        }

        var result = await Task.Run(() => _assets.MergeDuplicatePaths()).ConfigureAwait(true);
        Reload();

        if (_assets.LastDedupError.Length > 0)
        {
            StatusText = "⚠ 合并失败：" + _assets.LastDedupError;
            return;
        }

        if (result.IsEmpty)
        {
            StatusText = "合并没有产生变化 —— 这些重复可能都不在同一条物理路径上（真副本），" +
                         "选中多余的几条用「⤫ 从库移除」。";
            return;
        }

        // 合并后再核一次：仍有残留就明说，别让用户以为点了个寂寞
        var left = _assets.CountDuplicatePaths();
        StatusText = $"已合并 {result.Groups} 组：删除 {result.RemovedAssets} 条多余资产、" +
                     $"重挂 {result.MovedLocations} 个位置、清理 {result.PrunedLocations} 条冗余位置记录（磁盘文件没动）。" +
                     (left > 0 ? $" ⚠ 还剩 {left} 组（可能是合并期间被扫描改动过，再点一次即可）。" : "");
    }

    // ==================== ② 从库移除（只删索引） ====================

    /// <summary>
    /// 「⤫ 从库移除」：把勾选的资产**连同它们的文件位置记录**从库里删掉，⛔ 不碰磁盘。
    /// <para>
    /// 这是清理「不同路径的重复副本」的正确手段 —— 走「移入回收站」要先把文件搬进系统回收站，
    /// 走「从磁盘删除」会真删文件，两者都不该用来整理索引。
    /// </para>
    /// <para>⚠️ 文件仍在已登记存储源的目录里 ⇒ 下次扫描会再次入库；要彻底不再回来，去「存储源」页删掉多余的嵌套目录源。</para>
    /// </summary>
    [RelayCommand]
    private async Task RemoveCheckedFromLibraryAsync()
    {
        var rows = BatchRows();
        if (rows.Count == 0)
        {
            StatusText = "先选中要移除的资产（Ctrl 增选 / Shift 连选 / Ctrl+A 全选当前页）。";
            return;
        }

        var dupHint = _assets.CountDuplicatePaths() > 0
            ? "\n\n⚠ 库里还有同路径重复 —— 那种情况先用「🔍 查重合并」，它能保住标签 / 评分 / 描述。"
            : "";

        if (!await DialogHooks.ConfirmAsync(
                $"把这 {rows.Count} 条资产从库里移除？\n\n" +
                "· 只删库存记录（含它们登记的所有文件位置），⛔ 磁盘文件一个都不动；\n" +
                "· 标签 / 分类 / 集合的挂接随记录一起消失，不可撤销；\n" +
                "· 文件还在已登记的存储源目录里 ⇒ 下次扫描会重新入库。" + dupHint).ConfigureAwait(true))
        {
            StatusText = "已取消。";
            return;
        }

        var removed = 0;
        foreach (var r in rows)
        {
            if (!_assets.DeleteAsset(r.Id)) continue;
            _opLog.Log("remove", "asset", r.Id, reversible: false);
            removed++;
        }

        Reload();
        StatusText = removed == 0
            ? "一条都没删掉（库不可用？）。"
            : $"已从库里移除 {removed} 条资产记录（磁盘文件没动）。";
    }

    // ==================== ③ 读媒体信息（ffprobe / 图片尺寸） ====================

    /// <summary>
    /// 工具条「📊 读媒体信息」：给勾选的资产逐个补媒体信息（视频走 ffprobe、图片读文件头）。
    /// <para>只跑**还没有媒体信息**的（时长 / 分辨率 / 编码都为空），已经有的不重复跑 —— 迭代点击是幂等的。</para>
    /// <para>走任务队列（任务中心可见、可取消）；单个文件失败不影响其余。</para>
    /// </summary>
    [RelayCommand]
    private void ReadMediaInfoChecked()
    {
        var rows = BatchRows();
        if (rows.Count == 0)
        {
            StatusText = "先选中要读取的资产（Ctrl 增选 / Shift 连选 / Ctrl+A 全选当前页）。";
            return;
        }

        var targets = rows.Where(CanReadMediaInfo).ToList();
        if (targets.Count == 0)
        {
            StatusText = $"选中的 {rows.Count} 条里没有可读媒体信息的（只支持视频 / 图片；" +
                         "且已经读过的不会重复跑）。";
            return;
        }

        if (_meta == null) { StatusText = "媒体信息服务不可用。"; return; }

        var name = $"读取媒体信息 {targets.Count} 个";
        StatusText = $"已把「{name}」排进任务队列（进度看「任务中心」）。";

        // 并发度取模块设置（每个视频 = 一个 ffprobe 进程）；库写入仍是短连接 + busy_timeout 兜底。
        var concurrency = Math.Clamp(_io?.Settings.ProbeConcurrency ?? 4, 1, 16);

        _queue.Enqueue(name, async (ct, p) =>
        {
            var ok = 0;
            var fail = 0;
            var done = 0;

            using var gate = new SemaphoreSlim(concurrency, concurrency);

            var tasks = new List<Task>(targets.Count);
            foreach (var row in targets)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await gate.WaitAsync(ct).ConfigureAwait(false);   // ⛔ 放 try 外：没拿到锁就不会 Release
                    try
                    {
                        ct.ThrowIfCancellationRequested();

                        var loc = _assets.FindPrimaryLocation(row.Id) ?? _assets.ListLocations(row.Id).FirstOrDefault();
                        if (loc == null) { Interlocked.Increment(ref fail); return; }

                        var r = await _meta.ApplyAsync(loc, ct).ConfigureAwait(false);
                        if (r.Ok) Interlocked.Increment(ref ok); else Interlocked.Increment(ref fail);

                        var n = Interlocked.Increment(ref done);
                        p.Report($"已读取 {n}/{targets.Count}：{row.Name}");
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { Interlocked.Increment(ref fail); }
                    finally { gate.Release(); }
                }, ct));
            }

            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                p.Report("已取消。");
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                try { Reload(); }
                catch (Exception ex) { StatusText = "读完刷新列表失败：" + ex.Message; }
            });
            p.Report($"读完媒体信息：成功 {ok}，失败 {fail}。");
        });
    }

    /// <summary>
    /// 详情卡「⚡ 读媒体信息」：只读当前选中资产，读完立刻刷新列表（详情卡跟着更新）。
    /// </summary>
    [RelayCommand]
    private async Task ReadMediaInfoAsync()
    {
        if (Selected == null) { StatusText = "先选中一个资产。"; return; }
        if (_meta == null) { StatusText = "媒体信息服务不可用。"; return; }

        var row = Selected;
        if (!CanReadMediaInfo(row)) { StatusText = "这个类型没有可读的媒体信息（只支持视频 / 图片）。"; return; }

        var loc = _assets.FindPrimaryLocation(row.Id) ?? _assets.ListLocations(row.Id).FirstOrDefault();
        if (loc == null) { StatusText = "这个资产没有登记文件位置。"; return; }

        StatusText = $"正在读取「{row.Name}」的媒体信息…";
        var r = await _meta.ApplyAsync(loc, CancellationToken.None).ConfigureAwait(true);
        if (!r.Ok) { StatusText = r.Message; return; }

        RefreshView();   // 按 Id 认领选中项 ⇒ 详情卡重取到新值，⛔ 不跳走
        StatusText = $"已读取「{row.Name}」的媒体信息（时长 / 分辨率 / 编码）。";
    }

    /// <summary>
    /// 可以读媒体信息：类型是视频 / 图片，且**还没有**任何一项媒体值（已读过的跳过，保证重复点击不白跑进程）。
    /// </summary>
    private static bool CanReadMediaInfo(AssetRow row)
    {
        var typed = row.Type is AssetTypes.Video or AssetTypes.Image;
        if (!typed) return false;
        return row.DurationSeconds <= 0
               && row.ResolutionText.Length == 0
               && row.CodecText.Length == 0;
    }
}
