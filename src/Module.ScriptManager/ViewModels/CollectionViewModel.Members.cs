using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.ViewModels;

public partial class CollectionViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    private void RefreshCollections()
    {
        if (_store == null) return;
        _loading = true;

        var keepId = SelectedCollection?.Id;
        var keyword = (CollectionSearch ?? "").Trim();
        var kindFilter = SelectedKindFilter?.Value ?? "";
        var group = SelectedGroup?.Value ?? "";

        // 各页面（信息面板 / 脚本库 / 命令库）正在使用的集合 ⇒ 标「在用」；
        // 沉底只对 info 集合（库页高频切库，沉底反而难找）；默认库恒「在用」，不参与沉底（否则会挤掉置顶）。
        int InUseRank(ScriptCollection c)
            => !c.Builtin && ScriptCollectionKind.Normalize(c.Kind) == ScriptCollectionKind.Info && IsActive(c) ? 1 : 0;

        bool IsActive(ScriptCollection c)
        {
            // 默认库（内置）= 兜底库：没有归属的条目会自动收进它 ⇒ 永远算「在用」，也因此不可删除。
            if (c.Builtin) return true;

            var id = _store!.GetLastCollectionId(ScriptCollectionKind.Normalize(c.Kind));
            return id.Length > 0 && string.Equals(c.Id, id, StringComparison.Ordinal);
        }

        var rows = _store.Data.Collections
            .Where(c => kindFilter.Length == 0 || ScriptCollectionKind.Normalize(c.Kind) == kindFilter)
            .Where(c => keyword.Length == 0 || Matches(c, keyword))
            .Where(c => group != "pinned" || c.Pinned)
            .OrderBy(InUseRank)                                // 使用中的集合排最后（置顶 / 归档都优先不了）
            .ThenBy(c => c.Archived)                           // 归档的排最后（仍可见，方便取消归档）
            .ThenByDescending(c => c.Pinned)
            .ThenByDescending(c => SafeDate(c.LastOpenedAt))
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (group == "recent")
            rows = rows.OrderByDescending(c => SafeDate(c.LastOpenedAt)).ThenBy(InUseRank).ToList();

        Collections.Clear();
        foreach (var c in rows)
        {
            var row = new CollectionRow(c)
            {
                MemberCount = _store.ItemsOf(c.Id).Count,
                IsInUse = IsActive(c),
            };
            Collections.Add(row);
        }

        SelectedCollection = Collections.FirstOrDefault(r => r.Id == keepId) ?? Collections.FirstOrDefault();
        _loading = false;

        OnPropertyChanged(nameof(HasCollections));
        OnPropertyChanged(nameof(HasSelectedCollection));
        OnPropertyChanged(nameof(CollectionHint));
    }

    /// <summary>
    /// 页面重新显示时重算「在用」标记与沉底顺序。
    /// <para>信息面板切集合只会改 <c>lastCollectionId</c>，本页 VM 收不到通知 ⇒ 由 View 在挂上视觉树时调一次。</para>
    /// </summary>
    public void RefreshUsageState() => RefreshCollections();

    private static bool Matches(ScriptCollection c, string keyword)
        => (c.Name ?? "").Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
           || (c.Description ?? "").Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
           || (c.Tags ?? new List<string>()).Any(t => t.Contains(keyword, StringComparison.CurrentCultureIgnoreCase));

    private static DateTime SafeDate(string? iso)
        => DateTime.TryParse(iso, out var dt) ? dt : DateTime.MinValue;

    /// <summary>重建右栏成员表：候选池（按集合 kind 过滤）+ 是否属于当前集合（D13）。</summary>
    private void LoadMemberTable()
    {
        if (_store == null) return;

        _suppressMember = true;
        Members.Clear();

        var colId = SelectedCollection?.Id ?? "";
        var kind = SelectedCollection == null ? "" : ScriptCollectionKind.Normalize(SelectedCollection.Kind);
        var keyword = (MemberSearch ?? "").Trim();
        var isPackageCol = kind == ScriptCollectionKind.Package;

        foreach (var item in _store.Data.Items)
        {
            // 🔴 v3.81：软件包条目 ↔ 软件包集合是**一对一**的容器关系 ——
            // 包条目只在「软件包」类集合的候选池里出现，其他集合完全看不到（反之亦然）。
            var isPackageItem = ScriptItemRole.Normalize(item.Role) == ScriptItemRole.Package;
            if (isPackageItem != isPackageCol) continue;

            if (!ShowAllKinds && kind.Length > 0 && !RoleMatchesKind(item.Role, kind)) continue;
            if (!ShowNonCollectable && !item.Collectable) continue;
            if (keyword.Length > 0
                && !(item.Title ?? "").Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
                && !(item.Target ?? "").Contains(keyword, StringComparison.CurrentCultureIgnoreCase)) continue;

            Members.Add(new MemberRow(item, item.Collections.Contains(colId)));
        }

        _suppressMember = false;
        OnPropertyChanged(nameof(HasMembers));
    }

    /// <summary>条目角色是否属于这个集合 kind 的候选域（信息面板集合不限）。</summary>
    private static bool RoleMatchesKind(string? role, string kind)
    {
        var r = ScriptItemRole.Normalize(role);
        return ScriptCollectionKind.Normalize(kind) switch
        {
            ScriptCollectionKind.Script => r is ScriptItemRole.Script or ScriptItemRole.Collector,
            ScriptCollectionKind.Command => r == ScriptItemRole.Command,
            ScriptCollectionKind.Package => r == ScriptItemRole.Package,
            ScriptCollectionKind.Crawl => r is ScriptItemRole.Script or ScriptItemRole.Collector,
            _ => true,   // 信息面板集合：脚本 + 命令都能当采集器
        };
    }

    /// <summary>给每一行挂上勾选联动（重建时统一挂，避免漏挂）。</summary>
    private void OnMembersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (MemberRow r in e.OldItems) r.PropertyChanged -= OnMemberRowPropertyChanged;
        if (e.NewItems != null)
            foreach (MemberRow r in e.NewItems) r.PropertyChanged += OnMemberRowPropertyChanged;
    }

    private void OnMemberRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MemberRow.IsMember)) return;
        if (_suppressMember || _loading || sender is not MemberRow row) return;
        if (_store == null || SelectedCollection == null) return;

        if (!row.CanToggle)
        {
            // 不可采集的行 ⛔ 勾不上：把勾选状态翻回去。
            _suppressMember = true;
            row.IsMember = !row.IsMember;
            _suppressMember = false;
            StatusText = string.IsNullOrEmpty(row.Reason) ? "这条不可采集，不能加入集合。" : $"这条不可采集：{row.Reason}";
            return;
        }

        if (_store.SetMembership(row.ItemId, SelectedCollection.Id, row.IsMember))
        {
            _store.Save();
            var col = SelectedCollection;
            col.MemberCount = _store.ItemsOf(col.Id).Count;
            StatusText = row.IsMember
                ? $"已把「{row.Title}」加入「{col.Name}」"
                : $"已把「{row.Title}」移出「{col.Name}」";
        }
    }

    // ==================== 详情卡回写 ====================

    private void LoadDetailEdits()
    {
        _loading = true;
        var col = SelectedCollection == null || _store == null ? null : _store.FindCollection(SelectedCollection.Id);

        EditName = col?.Name ?? "";
        EditIcon = col?.Icon ?? "";
        EditKind = ScriptCollectionKind.Normalize(col?.Kind);
        EditDescription = col?.Description ?? "";
        EditTags = col == null ? "" : string.Join(", ", col.Tags);
        EditRefreshMode = ScriptRefreshMode.Normalize(col?.RefreshMode);
        EditRefreshSeconds = Math.Max(0, col?.RefreshSeconds ?? 0).ToString();
        EditRootPath = col?.RootPath ?? "";
        EditPinned = col?.Pinned ?? false;

        EditDefaultViewId = col?.DefaultViewId ?? "";

        ViewOptions.Clear();
        ViewOptions.Add(new CollectionFilterOption { Label = "默认视图", Value = "" });
        if (col != null)
            foreach (var v in _store?.Data.Views.Where(v => v.CollectionId == col.Id) ?? Enumerable.Empty<ScriptView>())
                ViewOptions.Add(new CollectionFilterOption { Label = v.Name, Value = v.Id });

        _loading = false;
        OnPropertyChanged(nameof(HasSelectedCollection));
        OnPropertyChanged(nameof(CollectionHint));
        OnPropertyChanged(nameof(IsInfoCollection));
        OnPropertyChanged(nameof(IsScriptCollection));
        OnPropertyChanged(nameof(ViewCountText));
    }

    private void ApplyCollectionEdits()
    {
        if (_loading || _store == null || SelectedCollection == null) return;
        var col = _store.FindCollection(SelectedCollection.Id);
        if (col == null) return;

        // 🔴 集合不允许同名（用户 2026-10-19）：撞名 ⇒ 不落盘，输入框退回原名并提示。
        var name = string.IsNullOrWhiteSpace(EditName) ? col.Name : EditName.Trim();
        if (NameTaken(name, col.Id))
        {
            StatusText = $"已存在同名集合「{name}」，名称未修改。";
            _loading = true;
            EditName = col.Name;
            _loading = false;
            return;
        }

        col.Name = name;
        col.Icon = EditIcon ?? "";
        // 类别可改（用户 2026-10-19）：内置库改走后，下次启动由 Normalize 补一个新默认库。
        col.Kind = ScriptCollectionKind.Normalize(EditKind);
        col.Description = EditDescription ?? "";
        col.Tags = (EditTags ?? "").Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(t => t.Trim()).Where(t => t.Length > 0).Distinct().ToList();
        col.RefreshMode = ScriptRefreshMode.Normalize(EditRefreshMode);
        col.RefreshSeconds = int.TryParse((EditRefreshSeconds ?? "").Trim(), out var sec) && sec > 0 ? sec : 0;
        col.RootPath = (EditRootPath ?? "").Trim().TrimEnd('\\', '/');
        col.DefaultViewId = EditDefaultViewId ?? "";
        col.Pinned = EditPinned;

        _store.Save();

        // 左栏行同步（⛔ 不整表重建，免得丢选中）
        SelectedCollection.Name = col.Name;
        SelectedCollection.Kind = col.Kind;
        SelectedCollection.Icon = col.Icon;
        SelectedCollection.Description = col.Description;
        SelectedCollection.Pinned = col.Pinned;
        OnPropertyChanged(nameof(CollectionHint));
        OnPropertyChanged(nameof(IsInfoCollection));
        OnPropertyChanged(nameof(IsScriptCollection));
        OnPropertyChanged(nameof(ViewCountText));
    }

    /// <summary>集合名是否被别的集合占用（大小写 / 首尾空格不同也算同名）。</summary>
    private bool NameTaken(string name, string? exceptId = null)
        => _store != null && _store.Data.Collections.Any(c =>
            !string.Equals(c.Id, exceptId ?? "", StringComparison.Ordinal)
            && string.Equals((c.Name ?? "").Trim(), name, StringComparison.CurrentCultureIgnoreCase));

    /// <summary>取一个不重名的名字：`新集合` → `新集合 2` → `新集合 3`……</summary>
    private string UniqueName(string baseName)
    {
        if (!NameTaken(baseName)) return baseName;
        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{baseName} {i}";
            if (!NameTaken(candidate)) return candidate;
        }
        return $"{baseName} {Guid.NewGuid().ToString("N")[..4]}";
    }

    // ==================== 命令 ====================

}
