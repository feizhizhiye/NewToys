using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using NewToys.MediaAssets.Models;
using NewToys.MediaAssets.Services;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>
/// 「资产浏览」的入库动作：把磁盘上的文件 / 文件夹加进资产库。
/// <para>
/// 🔴 语义（资源管理器口径 + 库的边界）：
/// · **文件** ⇒ 只索引它自己（所在目录没登记就登记该目录，⛔ 不登记盘符根）；
/// · **文件夹** ⇒ 递归扫描该目录（已在某存储源之内 ⇒ 归那个源，⛔ 不另建重叠源）；
/// · 没有选中项时 = 把**当前目录**加入资产库。
/// </para>
/// </summary>
public partial class AssetBrowseViewModel
{
    /// <summary>➕ 加入资产库（选中项优先，其次当前目录）。</summary>
    [RelayCommand]
    private async Task AddToLibraryAsync()
    {
        if (IsBusy) { StatusText = "正在忙，等这一步完成再加。"; return; }
        if (!IsDbAvailable) { StatusText = "⚠ 资产库不可用（数据库打不开）——没法入库。"; return; }

        var targets = _selectedTiles.Count > 0
            ? _selectedTiles.ToList()
            : Selected != null ? new List<AssetTile> { Selected } : new List<AssetTile>();

        var folders = new List<string>();
        var files = new List<string>();

        foreach (var t in targets)
        {
            if (t.InLibrary) continue;                       // 已入库 ⇒ 跳过（⛔ 不重复索引）
            if (t.IsFolder) folders.Add(t.AbsPath);
            else files.Add(t.AbsPath);
        }

        // 没选中未入库项 ⇒ 把当前目录本身加进去（右键空白处 / 工具条点一下的常见意图）
        if (folders.Count == 0 && files.Count == 0)
        {
            if (SelectedNode is { InLibrary: false } node) folders.Add(node.AbsPath);
            else
            {
                StatusText = targets.Count == 0
                    ? "先选中一个文件 / 文件夹（或在左树选一个没入库的目录）。"
                    : "选中的项都已经在资产库里了。";
                return;
            }
        }

        // ① 文件：直接入库（快，不用进任务队列）
        if (files.Count > 0)
        {
            IsBusy = true;
            try
            {
                var r = await _lib.AddFilesAsync(files, CancellationToken.None).ConfigureAwait(true);
                StatusText = r.Message;
            }
            catch (Exception ex) { StatusText = "加入资产库失败：" + ex.Message; }
            finally { IsBusy = false; }

            Reload(restoreLastFolder: false);
        }

        // ② 文件夹：可能几百上千个文件 ⇒ 进任务队列，可取消、有进度
        EnqueueFolderAdd(folders);
    }

    /// <summary>
    /// 树节点右键「➕ 加入资产库」：只认右键瞄准的那个目录。
    /// <para>🔴 ⛔ 不改选中、⛔ 不导航 —— 右键只负责瞄准（资源管理器里右键同样不跳转目录）。</para>
    /// </summary>
    [RelayCommand]
    private void AddContextNodeToLibrary()
    {
        var node = ContextNode;
        if (node == null) return;

        if (node.InLibrary) { StatusText = $"「{node.Name}」已经在资产库里了。"; return; }
        if (!IsDbAvailable) { StatusText = "⚠ 资产库不可用（数据库打不开）——没法入库。"; return; }
        if (IsBusy) { StatusText = "正在忙，等这一步完成再加。"; return; }

        EnqueueFolderAdd(new[] { node.AbsPath });
    }

    /// <summary>文件夹入库统一走任务队列（可能几百上千个文件 ⇒ 可取消、有进度）。</summary>
    private void EnqueueFolderAdd(IReadOnlyList<string> folders)
    {
        foreach (var dir in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var name = NameOf(dir);
            _queue.Enqueue($"加入资产库（{name}）", async (ct, p) =>
            {
                AddToLibraryResult r;
                try { r = await _lib.AddFolderAsync(dir, p, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { p.Report("已取消。"); return; }
                catch (Exception ex) { p.Report("失败：" + ex.Message); return; }

                p.Report(r.Message);
                Dispatcher.UIThread.Post(() =>
                {
                    StatusText = r.Message;
                    Reload(restoreLastFolder: false);
                });
            });
        }

        if (folders.Count > 0)
            StatusText = $"已把 {folders.Count} 个文件夹的入库排进任务队列（进度看「任务中心」，可取消）。";
    }

    /// <summary>跳到某个绝对目录（双击文件夹 / 入库后定位）。</summary>
    public async Task NavigateToAsync(string absPath)
    {
        if (string.IsNullOrWhiteSpace(absPath)) return;
        if (!Directory.Exists(absPath)) { StatusText = $"目录不存在（可能已被移动或删除）：{absPath}"; return; }

        try
        {
            var node = await EnsureNodeAsync(absPath).ConfigureAwait(true);
            if (node == null)
            {
                StatusText = $"进不去：{absPath}（盘符可能已断开，或不在任何磁盘 / 共享根之下）。";
                return;
            }
            SelectNode(node);
        }
        catch (Exception ex) { StatusText = "打开目录失败：" + ex.Message; }
    }
}
