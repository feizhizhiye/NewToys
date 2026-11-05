using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.MediaAssets.Models;
using NewToys.MediaAssets.Services;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>「资产浏览」的入库动作：把磁盘上的文件 / 文件夹加进资产库。（WHY: MA-068）</summary>
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
    /// 树节点右键「加入资产库」：只认右键瞄准的那个目录。
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

    /// <summary>
    /// 树节点右键「重新扫描此目录」：把右键目录里**还没入库**的文件补进来。
    /// <para>走的还是入库那条链路（<see cref="AssetLibraryAddService.AddFolderAsync"/> 按路径去重）——
    /// 对已在库的目录就是一次增量重扫，⛔ 不会重复索引已入库的文件。</para>
    /// </summary>
    [RelayCommand]
    private void RescanContextNode()
    {
        var node = ContextNode;
        if (node == null) return;
        if (!IsDbAvailable) { StatusText = "⚠ 资产库不可用（数据库打不开）—— 没法扫描。"; return; }
        if (!node.HasPending) { StatusText = $"「{node.Name}」里没有待加入的文件（折起来再展开一次可重新检查）。"; return; }
        if (IsBusy) { StatusText = "正在忙，等这一步完成再扫。"; return; }

        node.PendingCount = 0;      // 乐观清零：任务在跑时先别继续飘红
        node.RefreshDerived();
        OnPropertyChanged(nameof(CanRescanContextNode));

        EnqueueFolderAdd(new[] { node.AbsPath });
    }

    /// <summary>
    /// 左树右键「重命名目录源」：只改库里这个目录对应的源名（「全部资产」的「存储源」列用这个名字）。
    /// <para>⛔ 不改磁盘目录名、⛔ 不动 `root_path` —— 想改磁盘上的文件夹名请用系统的资源管理器。</para>
    /// </summary>
    [RelayCommand]
    private async Task RenameContextNodeStorageAsync()
    {
        var node = ContextNode ?? SelectedNode;
        if (node == null) return;
        if (!IsDbAvailable) { StatusText = "⚠ 资产库不可用（数据库打不开）。"; return; }

        var storage = _assets.FindStorageByRoot(node.AbsPath);
        if (storage == null)
        {
            StatusText = $"「{node.Name}」本身没有登记成存储源 —— 先「加入资产库」，再给它改名。";
            return;
        }

        var name = await DialogHooks.InputAsync("重命名目录源（只改库里的显示名）", storage.Name).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == storage.Name) { StatusText = "已取消（名字没变）。"; return; }

        _assets.RenameStorage(storage.Id, name);
        StatusText = $"已把目录源「{storage.Name}」改名为「{name.Trim()}」（⛔ 未动磁盘目录名）。";
        Reload(restoreLastFolder: false);
    }

    /// <summary>
    /// 树节点右键「移出资产库」：把该目录对应的存储源（含其文件位置索引）移除，⛔ 不动磁盘文件。
    /// <para>取代原「存储源」页的「移除存储源」——目录级的库操作现在都在这里。</para>
    /// </summary>
    [RelayCommand]
    private async Task RemoveContextNodeFromLibraryAsync()
    {
        var node = ContextNode ?? SelectedNode;
        if (node == null) return;
        if (!IsDbAvailable) { StatusText = "⚠ 资产库不可用（数据库打不开）。"; return; }

        var storage = _assets.FindStorageByRoot(node.AbsPath);
        if (storage == null)
        {
            StatusText = $"「{node.Name}」本身没有登记成存储源（里面可能只有子文件夹的源）——请在那一层上操作。";
            return;
        }

        var count = _assets.GetStorageStats(storage.Id).FileCount;
        var warn = $"把「{node.Name}」（{storage.RootPath}）移出资产库？\n" +
                   $"只删索引：该目录下的 {count} 个文件位置会被移除，资产记录保留（重新加入即可找回）。\n" +
                   "⛔ 磁盘上的文件一个都不会动。";
        if (!await DialogHooks.ConfirmAsync(warn).ConfigureAwait(true)) { StatusText = "已取消。"; return; }

        _assets.DeleteStorage(storage.Id);
        StatusText = $"已把「{node.Name}」移出资产库（删了 {count} 个位置索引；磁盘文件未动）。";
        Reload(restoreLastFolder: false);
    }

    /// <summary>
    /// 「扫描全部已入库」：把所有已登记目录扫一遍（增量）。
    /// <para>取代原「存储源」页的「扫描全部」；进度进任务中心，可取消。</para>
    /// </summary>
    [RelayCommand]
    private void ScanAllLibrary()
    {
        if (!IsDbAvailable) { StatusText = "⚠ 资产库不可用（数据库打不开）。"; return; }
        if (IsBusy) { StatusText = "正在忙，等这一步完成再扫。"; return; }

        _queue.Enqueue("扫描全部已入库目录", async (ct, p) =>
        {
            try
            {
                var job = await _scan.ScanAsync(null, p, ct).ConfigureAwait(false);
                p.Report($"扫描完成：新增 {job.AddedFiles} / 更新 {job.UpdatedFiles} / 丢失 {job.MissingFiles}。");
            }
            catch (OperationCanceledException) { p.Report("已取消。"); }
            catch (Exception ex) { p.Report("扫描失败：" + ex.Message); }

            Dispatcher.UIThread.Post(() => Reload(restoreLastFolder: false));
        });
        StatusText = "已把「扫描全部已入库目录」排进任务队列（进度看「任务中心」，可取消）。";
    }

    // ==================== 目录源运维（原「存储源」页的类型 / 上线动作，页撤下后迁到这里） ====================

    /// <summary>右键瞄准目录对应的存储源（上下文菜单打开时取一次，⛔ 不每次读库）。</summary>
    private StorageSource? _contextStorage;

    /// <summary>右键的目录本身登记成存储源 ⇒ 类型 / 上线菜单可用（子层里的源得在那一层操作）。</summary>
    public bool CanOperateContextStorage => IsDbAvailable && _contextStorage != null;

    public bool IsContextStorageLocal => _contextStorage?.Type == StorageTypes.Local;

    public bool IsContextStorageRemovable => _contextStorage?.Type == StorageTypes.Removable;

    public bool IsContextStorageNas => _contextStorage?.Type == StorageTypes.Nas;

    /// <summary>「上线 / ⬇ 下线」菜单文案：在线时给下线。</summary>
    public string ContextOnlineMenuText => _contextStorage is { Online: true } ? "⬇ 下线（扫描跳过它）" : "⬆ 上线";

    /// <summary>右键目录源的类型 / 在线状态摘要（菜单标题右侧提示用）。</summary>
    public string ContextStorageSummary => _contextStorage == null
        ? ""
        : $"当前：{TypeTextOf(_contextStorage.Type)} · {(_contextStorage.Online ? "在线" : "离线")}";

    /// <summary>重取右键目录对应的存储源并刷新派生属性（右键换目标 / 改完类型或上线后调）。</summary>
    private void RefreshContextStorage()
    {
        _contextStorage = ContextNode is { } n && IsDbAvailable ? _assets.FindStorageByRoot(n.AbsPath) : null;
        OnPropertyChanged(nameof(CanOperateContextStorage));
        OnPropertyChanged(nameof(IsContextStorageLocal));
        OnPropertyChanged(nameof(IsContextStorageRemovable));
        OnPropertyChanged(nameof(IsContextStorageNas));
        OnPropertyChanged(nameof(ContextOnlineMenuText));
        OnPropertyChanged(nameof(ContextStorageSummary));
    }

    private static string TypeTextOf(string type) => new StorageRow { Type = type }.TypeText;

    /// <summary>
    /// 右键「🗄 类型」：改存储源分类（本地 / 移动 / NAS）。
    /// <para>只改 <c>storages.type</c>，⛔ 不动位置索引、⛔ 不动磁盘（类型只影响显示与筛选口径）。</para>
    /// </summary>
    [RelayCommand]
    private void SetContextStorageType(string? type)
    {
        var storage = _contextStorage;
        if (storage == null || string.IsNullOrWhiteSpace(type)) return;

        if (storage.Type == type) { StatusText = $"「{storage.Name}」的类型已经是 {TypeTextOf(type)}。"; return; }

        _assets.SetStorageType(storage.Id, type);
        StatusText = $"「{storage.Name}」的类型已改为 {TypeTextOf(type)}（只改分类，索引与磁盘未动）。";
        RefreshContextStorage();
        Reload(restoreLastFolder: false);
    }

    /// <summary>
    /// 右键「上线 / ⬇ 下线」：只写 <c>storages.online</c>。
    /// <para>下线 = 扫描跳过它（其下位置 ⛔ 不会被标成丢失）；上线前先探根目录，读不到要求确认（强制上线后扫描仍会失败）。</para>
    /// </summary>
    [RelayCommand]
    private async Task ToggleContextStorageOnlineAsync()
    {
        var storage = _contextStorage;
        if (storage == null) return;

        var next = !storage.Online;

        if (next && !StorageService.ProbeOnline(storage.RootPath))
        {
            var warn = $"「{storage.Name}」的根目录现在读不到：\n{storage.RootPath}\n" +
                       "强制标记为在线后，扫描仍会因目录不可访问而把它下面的文件标成丢失。\n" +
                       "仍然标记为在线？";
            if (!await DialogHooks.ConfirmAsync(warn).ConfigureAwait(true)) { StatusText = "已取消（仍保持离线）。"; return; }
        }

        _assets.SetStorageOnline(storage.Id, next);
        StatusText = next
            ? $"「{storage.Name}」已上线 —— 点「重新扫描此目录」校准其下文件状态。"
            : $"「{storage.Name}」已下线 —— 扫描将跳过它（⛔ 不会把它下面的文件标成丢失）。";
        RefreshContextStorage();
        Reload(restoreLastFolder: false);
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
