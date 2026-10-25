using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.ViewModels;

public abstract partial class QuickActionListViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    // ==================== 增删改查 ====================

    /// <summary>
    /// 把新建 / 导入的条目归到**当前库**（收录必须归属一个库）。
    /// <para>「全部库」时不动（保持 <c>Add</c> 给它的默认库归属）。</para>
    /// </summary>
    private void AttachToCurrentLibrary(QuickAction? item)
    {
        var colId = CurrentLibraryId;
        if (_store == null || item == null || LibraryKind.Length == 0 || colId.Length == 0) return;

        var col = _store.FindCollection(colId);
        if (col == null) return;

        // 从默认库移出：默认库只兜底无归属条目，⛔ 不吞掉用户归到别的库的东西。
        foreach (var def in _store.Data.Collections.Where(c => c.Builtin
                     && ScriptCollectionKind.Normalize(c.Kind) == LibraryKind && c.Id != colId).ToList())
            _store.SetMembership(item.Id, def.Id, false);

        _store.SetMembership(item.Id, colId, true);

        item.CollectionIds.Clear();
        item.CollectionIds.Add(colId);
        item.LibraryText = col.Name;
        item.LibraryRoot = _store.RootOf(colId);
    }

    /// <summary>新增：先清掉过滤（保证新项一定可见），追加一条并选中它，改好后点「💾 保存」。</summary>
    [RelayCommand]
    private void Add()
    {
        if (_svc == null) return;

        _suppress = true;
        SearchText = "";
        if (CategoryOptions.Count > 0) SelectedCategory = CategoryOptions[0];
        _suppress = false;
        ResetFilters();   // 清掉筛选，保证新项一定看得见

        var item = _svc.Add(_pageKind);
        AttachToCurrentLibrary(item);
        RebuildCategories();
        ApplyView();
        Selected = Items.FirstOrDefault(i => ReferenceEquals(i, item)) ?? item;
        StatusText = $"已在「{PageDisplayName}」新增一条「{item.Title}」，请在右侧改名 / 选类型 / 填命令或脚本文件后点「💾 保存」。";
    }

    /// <summary>保存：把编辑缓冲写回选中对象并落盘。</summary>
    [RelayCommand]
    private void Save()
    {
        if (Selected == null || _svc == null) return;

        var title = (EditTitle ?? "").Trim();
        if (title.Length == 0) { StatusText = "名称不能为空。"; return; }

        Selected.Title = title;
        Selected.Category = (EditCategory ?? "").Trim();
        Selected.Description = (EditDescription ?? "").Trim();
        Selected.Command = (EditCommand ?? "").Trim();
        Selected.NeedAdmin = EditNeedAdmin;
        Selected.RunHidden = EditRunHidden;
        Selected.Shell = QuickActionShell.Normalize(EditShell);
        Selected.Kind = string.IsNullOrWhiteSpace(EditKind) ? QuickActionKind.Command : EditKind;
        Selected.ScriptFile = (EditScriptFile ?? "").Trim();
        Selected.Interpreter = EditInterpreter ?? ScriptInterpreter.Auto;

        _svc.Save();

        // 归属铁律（v1.34）：「脚本库」只放**有脚本文件的脚本**。
        // 本页保存后若它不满足条件（类型不是脚本 / 没填脚本文件），自动挪去「命令库」并如实告知。
        var moved = false;
        if (_pageKind != QuickActionPage.Quick
            && (Selected.Kind != QuickActionKind.Script || Selected.ScriptFile.Length == 0))
        {
            var movedTitle = Selected.Title;
            _svc.Move(Selected, QuickActionPage.Quick);
            Selected = null;
            moved = true;
            StatusText = $"「{movedTitle}」没有脚本文件 → 已归入「命令库」（本页只放有脚本文件的脚本）。";
        }

        RebuildCategories();
        ApplyView();

        if (!moved) StatusText = $"已保存到 {FilePath}";
    }

    /// <summary>把选中项移到另一个页面（「脚本库」↔「命令库」，各写各的文件）。</summary>
    [RelayCommand]
    private void MoveToOtherPage()
    {
        var item = Selected;
        if (item == null || _svc == null) return;

        _svc.Move(item, _pageKind == QuickActionPage.Quick ? QuickActionPage.Shortcuts : QuickActionPage.Quick);
        Selected = null;
        RebuildCategories();
        ApplyView();
        StatusText = $"已把「{item.Title}」移到「{OtherPageName}」。";
    }

    /// <summary>
    /// 🗑 删除前是否弹二次确认（v3.63）：
    /// 「脚本库」= **索引库**，删的只是清单里的一行（脚本文件还在磁盘上）⇒ false，不打扰；
    /// 「命令库」= **json 里的真数据**，删掉就没了 ⇒ true（派生类 override）。
    /// </summary>
    protected virtual bool ConfirmBeforeDelete => false;

    /// <summary>
    /// 🗑 **从列表删除**：把这条从清单里去掉并落盘（⛔ 从不碰磁盘文件；删脚本文件走右键「🔥 从磁盘删除…」）。
    /// <para>是否弹确认框由 <see cref="ConfirmBeforeDelete"/> 决定。</para>
    /// </summary>
    [RelayCommand]
    private async Task DeleteAsync()
    {
        var item = Selected;
        if (item == null || _svc == null) return;

        // D15：选了具体库时，删除 = **从当前库移出**（⛔ 不删条目本体 / 文件）。
        if (LibraryKind.Length > 0 && CurrentLibraryId.Length > 0 && _store != null)
        {
            var okMove = await DialogHooks.ConfirmAsync(
                $"把「{item.Title}」从「{CurrentLibraryName}」移出吗？\n条目和磁盘文件都会保留，其他库仍能看到它。");
            if (!okMove) { StatusText = "已取消。"; return; }

            _store.SetMembership(item.Id, CurrentLibraryId, false);
            item.CollectionIds.Remove(CurrentLibraryId);

            // 移出后没有归属 ⇒ 收回默认库（⛔ 不留孤儿）。
            var si = _store.FindItem(item.Id);
            if (si != null && si.Collections.Count == 0)
            {
                var def = _store.Data.Collections.FirstOrDefault(c => c.Builtin
                             && ScriptCollectionKind.Normalize(c.Kind) == LibraryKind);
                if (def != null) _store.SetMembership(item.Id, def.Id, true);
            }

            _store.Save();
            _svc.Reload();
            RebuildCategories();
            ApplyView();
            StatusText = $"已从「{CurrentLibraryName}」移出：{item.Title}";
            return;
        }

        if (ConfirmBeforeDelete)
        {
            var ok = await DialogHooks.ConfirmAsync(
                $"确定删除「{item.Title}」吗？\n\n这条是 {FilePath} 里的记录，删掉后不可撤销（磁盘上的文件不会动）。");
            if (!ok) { StatusText = "已取消删除。"; return; }
        }

        if (!RemoveFromList(item)) { StatusText = "这条不在清单里，没有可移除的。"; return; }
        StatusText = ConfirmBeforeDelete
            ? $"已删除：{item.Title}"
            : $"已从列表移除：{item.Title}（不删磁盘文件）";
    }

    /// <summary>数据服务是否可用（派生类要做"没有服务就别动磁盘"这类判断时用）。</summary>
    protected bool HasService => _svc != null;

    /// <summary>
    /// 把一条从清单里移除并刷新（⛔ 只动清单、**不碰磁盘文件**、不弹确认）。
    /// <para>「从磁盘删除」那一路由派生类先删文件、再调本方法收尾。</para>
    /// </summary>
    /// <returns>真的移掉了才 true（不在清单里 ⇒ false）。</returns>
    protected bool RemoveFromList(QuickAction item)
    {
        if (_svc == null || item == null) return false;
        if (!_svc.RemoveSilent(item)) return false;

        Selected = null;
        RebuildCategories();
        ApplyView();
        return true;
    }

    /// <summary>切换收藏（收藏夹页读的是同一批对象，天然同步）。</summary>
    [RelayCommand]
    private void ToggleFavorite()
    {
        var item = Selected;
        if (item == null || _svc == null) return;

        _svc.ToggleFavorite(item);
        UpdateFavoriteUi(item);
        // 图标组里收藏是**一个图标 + IsActivePath** ⇒ 切换后要让高亮重算
        OnPropertyChanged(nameof(IsSelectedFavorite));
        OnPropertyChanged(nameof(IsSelectedNotFavorite));

        ApplyViewKeepSelection();

        StatusText = item.IsFavorite ? $"已收藏：{item.Title}" : $"已取消收藏：{item.Title}";
    }

    /// <summary>复制命令内容到剪贴板。</summary>
    [RelayCommand]
    private async Task Copy()
    {
        var cmd = Selected?.Command;
        if (string.IsNullOrWhiteSpace(cmd)) { StatusText = "这条还没有命令内容。"; return; }
        try
        {
            var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (win?.Clipboard == null) { StatusText = "剪贴板不可用。"; return; }
            await win.Clipboard.SetTextAsync(cmd);
            StatusText = "已复制命令到剪贴板。";
        }
        catch (Exception ex) { StatusText = "复制失败：" + ex.Message; }
    }

    // ==================== 右键「编辑」脚本（v3.19，需求 §6.2.2） ====================

    /// <summary>选中项是否可「编辑」：是脚本类型且脚本文件已在磁盘上。</summary>
    public bool CanEditScript => Selected != null
        && string.Equals(Selected.Kind, QuickActionKind.Script, StringComparison.OrdinalIgnoreCase)
        && File.Exists(ResolveScriptPath(Selected.ScriptFile));

    /// <summary>把脚本文件字段（相对脚本目录或绝对路径）解析成磁盘全路径；空则返回空串。</summary>
    protected string ResolveScriptPath(string? scriptFile)
    {
        var file = (scriptFile ?? "").Trim();
        if (file.Length == 0) return "";
        return Path.IsPathRooted(file) ? file : Path.Combine(ScriptsDirectory, file);
    }

    /// <summary>📝 用记事本编辑选中脚本文件。</summary>
    [RelayCommand]
    private void EditWithNotepad()
    {
        var item = Selected;
        if (item == null) { StatusText = "先选中一条。"; return; }
        var path = ResolveScriptPath(item.ScriptFile);
        if (!File.Exists(path)) { StatusText = $"找不到脚本文件：{(path.Length > 0 ? path : "（未填）")}"; return; }
        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = true });
            StatusText = $"已用记事本打开：{path}";
        }
        catch (Exception ex) { StatusText = "打开失败：" + ex.Message; }
    }

    /// <summary>🧰 用系统「打开方式」打开选中脚本文件（选外部程序）。</summary>
    [RelayCommand]
    private void OpenWith()
    {
        var item = Selected;
        if (item == null) { StatusText = "先选中一条。"; return; }
        var path = ResolveScriptPath(item.ScriptFile);
        if (!File.Exists(path)) { StatusText = $"找不到脚本文件：{(path.Length > 0 ? path : "（未填）")}"; return; }
        try
        {
            // verb=openas：弹出系统「打开方式」对话框，让用户选用哪个程序打开。
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "openas" });
            StatusText = $"已弹出「打开方式」：{path}";
        }
        catch (Exception ex) { StatusText = "打开失败：" + ex.Message; }
    }

    private void UpdateFavoriteUi(QuickAction? item)
    {
        var fav = item?.IsFavorite == true;
        FavoriteMenuLabel = fav ? "✕ 取消收藏" : "★ 添加到收藏夹";
        OnPropertyChanged(nameof(FavoriteButtonText));
    }

}
