using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.ViewModels;

public partial class ScriptInfoViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    private void ReloadCollections()
    {
        if (_store == null) return;

        var keepId = SelectedCollection?.Id ?? "";
        _loadingSelection = true;
        try
        {
            Collections.Clear();

            var options = _store.Data.Collections
                .Where(c => !c.Archived && ScriptCollectionKind.Normalize(c.Kind) == ScriptCollectionKind.Info)
                .Select(c => new InfoCollectionOption
                {
                    Id = c.Id,
                    Name = string.IsNullOrWhiteSpace(c.Name) ? "未命名集合" : c.Name,
                    Icon = c.Icon,
                    Kind = c.Kind,
                    Pinned = c.Pinned,
                    LastOpenedAt = c.LastOpenedAt,
                    MemberCount = _store.Data.Items.Count(i => i.Collections.Contains(c.Id)),
                })
                // 排序 = ★置顶 → 最近使用 → 名称（§三.8）
                .OrderByDescending(o => o.Pinned)
                .ThenByDescending(o => o.LastOpenedUtc)
                .ThenBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            foreach (var o in options) Collections.Add(o);

            if (keepId.Length > 0)
                SelectedCollection = Collections.FirstOrDefault(o => o.Id == keepId);
        }
        finally { _loadingSelection = false; }
    }

    /// <summary>首次进页面：恢复上次选中的集合（<c>lastCollectionId</c>）；没有就选第一个。</summary>
    private void RestoreLastCollection()
    {
        if (Collections.Count == 0) { UpdateEmpty(); return; }

        var last = _store?.GetLastCollectionId(ScriptCollectionKind.Info) ?? "";
        var target = Collections.FirstOrDefault(o => o.Id == last) ?? Collections[0];

        _loadingSelection = true;
        try { SelectedCollection = target; }
        finally { _loadingSelection = false; }

        LoadCollection(target);
    }

    partial void OnSelectedCollectionChanged(InfoCollectionOption? value)
    {
        if (_loadingSelection) return;
        LoadCollection(value);
    }

    partial void OnSelectedViewChanged(InfoViewOption? value)
    {
        if (_loadingSelection || _store == null) return;
        if (value == null) { _view = null; return; }

        _view = _store.Data.Views.FirstOrDefault(v => v.Id == value.Id);

        // 视图决定「每节显示哪些列 / 节怎么排」，切换后整页按新视图重投影。
        ApplyViewProjection();
    }

    /// <summary>载入一个集合：视图 + 节（先出缓存，再按刷新策略跑一次）。</summary>
    private void LoadCollection(InfoCollectionOption? option)
    {
        Sections.Clear();
        Views.Clear();
        _view = null;
        SelectedRow = null;
        // 切集合 ⇒ 之前的节已不在列表里，节设置面板必须清空（否则会对着已移除的节改参数）。
        SettingsSection = null;

        if (_store == null || option == null) { UpdateEmpty(); return; }

        var col = _store.FindCollection(option.Id);
        if (col == null) { UpdateEmpty(); return; }

        // ---- 视图（没有就建一个默认视图，保证折叠状态可落盘） ----
        var views = _store.Data.Views.Where(v => v.CollectionId == col.Id).ToList();
        if (views.Count == 0)
        {
            var created = new ScriptView { Id = "view-" + col.Id, CollectionId = col.Id, Name = "默认" };
            ViewColumnProjection.InitializeForCollection(created,
                _store.ItemsOf(col.Id).Where(i => i.Role != ScriptItemRole.Package));
            _store.Data.Views.Add(created);
            views.Add(created);
            _store.Save();
        }
        if (string.IsNullOrWhiteSpace(col.DefaultViewId)) col.DefaultViewId = views[0].Id;

        _loadingSelection = true;
        try
        {
            foreach (var v in views) Views.Add(new InfoViewOption(v));
            SelectedView = Views.FirstOrDefault(v => v.Id == col.DefaultViewId) ?? Views[0];
            _view = _store.Data.Views.FirstOrDefault(v => v.Id == SelectedView.Id);
        }
        finally { _loadingSelection = false; }

        // ---- 节：集合成员里能采集的条目（先截断上限，再按视图的 sectionOrder 排序） ----
        var members = _store.ItemsOf(col.Id)
            .Where(i => i.Role != ScriptItemRole.Package && i.Capture && i.Collectable)
            .Take(MaxSections)
            .ToList();
        members = ViewColumnProjection.Order(members, _view);

        foreach (var item in members) Sections.Add(CreateSection(item));

        ApplyCollapsedFromView();
        LoadCachedRows();
        SelectedRow = Sections.FirstOrDefault()?.Selected;

        // 详情卡针对「节」⇒ 进页面先落在第一节，卡里立刻有内容（右键别的节可切）。
        SettingsSection = Sections.FirstOrDefault();

        // ---- 记住上次选中（重启恢复，验收 §五.7） ----
        col.LastOpenedAt = Now();
        _store.SetLastCollectionId(ScriptCollectionKind.Info, col.Id);
        _store.Save();

        OnPropertyChanged(nameof(HasViews));
        UpdateEmpty();
        UpdateFooter();

        // 刷新策略：manual 不动；on-enter / timer 进页面刷一次（只跑自动白名单）
        if (EffectiveMode() != ScriptRefreshMode.Manual) _ = RefreshAllCoreAsync(autoOnly: true);
    }

}
