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

    [RelayCommand]
    private void NewCollection()
    {
        if (_store == null) return;
        var col = new ScriptCollection
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Name = UniqueName("新集合"),
            Kind = ScriptCollectionKind.Info,
            Icon = "🗂",
            Order = _store.Data.Collections.Count,
            RefreshMode = ScriptRefreshMode.OnEnter,
            LastOpenedAt = Now(),
        };
        _store.Data.Collections.Add(col);
        _store.Save();
        RefreshCollections();
        SelectedCollection = Collections.FirstOrDefault(r => r.Id == col.Id);
        StatusText = "已新建集合，在右栏勾选成员（即时生效）。";
    }

    [RelayCommand]
    private void DuplicateCollection()
    {
        if (_store == null || SelectedCollection == null) return;
        var src = _store.FindCollection(SelectedCollection.Id);
        if (src == null) return;

        var col = new ScriptCollection
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Name = UniqueName(src.Name + " 副本"),
            Kind = src.Kind,
            Icon = src.Icon,
            Description = src.Description,
            Order = _store.Data.Collections.Count,
            Pinned = false,
            Builtin = false,
            Tags = new List<string>(src.Tags),
            RefreshMode = src.RefreshMode,
            RefreshSeconds = src.RefreshSeconds,
            RootPath = src.RootPath,
            LastOpenedAt = Now(),
        };
        _store.Data.Collections.Add(col);

        // 成员一并复制（条目侧加一条集合 id）
        foreach (var item in _store.Data.Items.Where(i => i.Collections.Contains(src.Id)))
            item.Collections.Add(col.Id);

        // 视图也要一起复制 —— 视图承载「每节显示哪些列 / 节顺序」，不复制副本就变成全默认。
        foreach (var srcView in _store.Data.Views.Where(v => v.CollectionId == src.Id).ToList())
        {
            var newView = new ScriptView
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                CollectionId = col.Id,
                Name = srcView.Name,
                Layout = srcView.Layout,
                Filter = srcView.Filter,
                Sort = srcView.Sort,
                Group = srcView.Group,
                RefreshMode = srcView.RefreshMode,
                RefreshSeconds = srcView.RefreshSeconds,
                Collapsed = new List<string>(srcView.Collapsed ?? new List<string>()),
                SectionOrder = new List<string>(srcView.SectionOrder ?? new List<string>()),
                SectionColumns = (srcView.SectionColumns ?? new List<SectionViewColumns>()).Select(s => new SectionViewColumns
                {
                    ItemId = s.ItemId,
                    Hidden = new List<string>(s.Hidden ?? new List<string>()),
                    Titles = s.Titles == null ? new Dictionary<string, string>() : new Dictionary<string, string>(s.Titles),
                    Widths = s.Widths == null ? new Dictionary<string, string>() : new Dictionary<string, string>(s.Widths),
                }).ToList(),
                Columns = (srcView.Columns ?? new List<ScriptColumn>()).Select(c => new ScriptColumn
                {
                    Key = c.Key, Title = c.Title, Visible = c.Visible, Order = c.Order, Width = c.Width,
                }).ToList(),
            };
            _store.Data.Views.Add(newView);

            if (string.Equals(src.DefaultViewId, srcView.Id, StringComparison.Ordinal))
                col.DefaultViewId = newView.Id;
        }

        _store.Save();
        RefreshCollections();
        SelectedCollection = Collections.FirstOrDefault(r => r.Id == col.Id);
        StatusText = $"已复制为「{col.Name}」。";
    }

    [RelayCommand]
    private async Task DeleteCollectionAsync()
    {
        if (_store == null || SelectedCollection == null) return;
        var row = SelectedCollection;
        var col = _store.FindCollection(row.Id);
        if (col == null) return;

        // 🔴 「在用」是唯一不可删的条件（用户 2026-10-19）：默认库恒在用 ⇒ 自然受保护，⛔ 不再按 Builtin 特殊拦。
        var kind = ScriptCollectionKind.Normalize(col.Kind);
        var inUse = col.Builtin
                    || string.Equals(_store.GetLastCollectionId(kind), col.Id, StringComparison.Ordinal);
        if (inUse)
        {
            StatusText = $"「{col.Name}」正在被{ScriptCollectionKind.Label(kind)}使用，不能删除；可以改名或归档。";
            return;
        }

        var message = $"确定删除集合「{col.Name}」吗？\n只删集合定义，⛔ 不删里面的条目。";
        var ok = await DialogHooks.ConfirmAsync(message);
        if (!ok) return;

        // 先清成员关系（条目侧），再删集合。
        foreach (var item in _store.Data.Items) item.Collections.RemoveAll(c => c == col.Id);
        _store.Data.Collections.RemoveAll(c => c.Id == col.Id);

        // D8：只属于这个库的条目 ⇒ 收回同 kind 的默认库（⛔ 不删条目 / 文件）。
        var def = _store.Data.Collections.FirstOrDefault(c => c.Builtin && !c.Archived
                                                              && ScriptCollectionKind.Normalize(c.Kind) == kind);
        if (def != null)
            foreach (var orphan in _store.Data.Items.Where(i => i.Collections.Count == 0))
                orphan.Collections.Add(def.Id);

        if (string.Equals(_store.GetLastCollectionId(kind), col.Id, StringComparison.Ordinal))
            _store.SetLastCollectionId(kind, "");

        _store.Save();

        RefreshCollections();
        StatusText = $"已删除集合「{col.Name}」。";
    }

    [RelayCommand]
    private void ArchiveCollection()
    {
        if (_store == null || SelectedCollection == null) return;
        var col = _store.FindCollection(SelectedCollection.Id);
        if (col == null) return;

        col.Archived = !col.Archived;
        _store.Save();
        RefreshCollections();
        StatusText = col.Archived ? $"已归档「{col.Name}」。" : $"已取消归档「{col.Name}」。";
    }

    /// <summary>
    /// 「📤 发送到对应页面」：按集合类别决定目标页（信息面板 / 脚本库 / 命令库 / 软件包），
    /// 先记下「在用」集合再跳过去 —— 目标页构造时按 <c>LastCollectionIdByKind</c> 自动选中同一个集合。
    /// <para>v3.81取代原来只认信息面板的 <c>SendToInfoPanelCommand</c>。</para>
    /// </summary>
    [RelayCommand]
    private void SendToPage()
    {
        if (_store == null || SelectedCollection == null)
        {
            StatusText = "先在左栏选一个集合。";
            return;
        }

        var col = _store.FindCollection(SelectedCollection.Id);
        if (col == null) return;

        var kind = ScriptCollectionKind.Normalize(col.Kind);
        var pageKey = ScriptCollectionKind.PageKey(kind);
        var pageName = ScriptCollectionKind.Label(kind);

        _store.SetLastCollectionId(kind, col.Id);
        col.LastOpenedAt = Now();
        _store.Save();

        if (_nav == null) { StatusText = "无法跳转（导航服务不可用）。"; return; }
        if (_nav.CurrentPageKey == pageKey)
        {
            StatusText = $"「{col.Name}」已在{pageName}中。";
            return;
        }

        _nav.NavigateTo(pageKey);
        StatusText = $"已发送「{col.Name}」到{pageName}。";
    }

}
