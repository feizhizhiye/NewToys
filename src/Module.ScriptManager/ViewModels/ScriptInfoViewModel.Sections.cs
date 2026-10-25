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

    private InfoSectionViewModel CreateSection(ScriptItem item)
    {
        var section = new InfoSectionViewModel(item, ViewColumnProjection.Resolve(item, _view))
        {
            RefreshRequested = s => _ = RefreshSectionsAsync(new[] { s }),
            DetailRequested = OpenSettingsFor,
            CollapseChanged = PersistCollapsed,
            ColumnToggled = (s, col) =>
            {
                // 显隐记在**当前视图**，不再回写脚本 schema（否则一个集合切列会连累全部集合）。
                ViewColumnProjection.Toggle(s.Item, _view, col.Key);
                _store?.Save();
            },
        };

        section.PropertyChanged += OnSectionPropertyChanged;
        return section;
    }

    /// <summary>节内选中变化 → 详情卡跟着（跨节只留一个选中）。</summary>
    private void OnSectionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(InfoSectionViewModel.Selected)) return;
        if (sender is not InfoSectionViewModel section || section.Selected == null) return;

        foreach (var other in Sections)
            if (!ReferenceEquals(other, section) && other.Selected != null)
            {
                other.Selected = null;
                break;
            }

        SelectedRow = section.Selected;
    }

    private InfoSectionViewModel? OwnerSectionOf(SysInfoRow? row)
        => row == null ? null : Sections.FirstOrDefault(s => s.ItemId == row.ItemId);

    /// <summary>点某一行（节内）⇒ 选中这一行，详情卡跟着换（视图 code-behind 调用）。</summary>
    public void SelectRow(SysInfoRow row)
    {
        var section = OwnerSectionOf(row);
        if (section != null) section.Selected = row;
        SelectedRow = row;
    }

    /// <summary>把视图里记录的折叠状态套到节上。</summary>
    private void ApplyCollapsedFromView()
    {
        var collapsed = _view?.Collapsed;
        foreach (var s in Sections)
            s.IsCollapsed = collapsed != null && collapsed.Contains(s.ItemId);
    }

    /// <summary>按当前视图重投影全部节：列（显隐 / 标题 / 宽度）+ 折叠 + 顺序。</summary>
    private void ApplyViewProjection()
    {
        foreach (var s in Sections)
            s.ApplyColumns(ViewColumnProjection.Resolve(s.Item, _view));

        ApplyCollapsedFromView();
        ReorderSections();
        RebuildSectionColumnChips();
    }

    /// <summary>按 <see cref="ScriptView.SectionOrder"/> 就地重排 <see cref="Sections"/>（不重建节 ⇒ 数据 / 选中不丢）。</summary>
    private void ReorderSections()
    {
        if (Sections.Count < 2) return;

        var ordered = ViewColumnProjection.Order(Sections.Select(s => s.Item), _view);
        var desired = new List<InfoSectionViewModel>(ordered.Count);
        foreach (var item in ordered)
        {
            var section = Sections.FirstOrDefault(s => s.ItemId == item.Id);
            if (section != null) desired.Add(section);
        }

        for (var i = 0; i < desired.Count; i++)
        {
            var at = Sections.IndexOf(desired[i]);
            if (at >= 0 && at != i) Sections.Move(at, i);
        }
    }

    /// <summary>⬆ 把当前「节设置」对应的节往前挪一位。</summary>
    [RelayCommand]
    private void MoveSectionUp() => MoveSection(-1);

    /// <summary>⬇ 往后挪一位。</summary>
    [RelayCommand]
    private void MoveSectionDown() => MoveSection(1);

    private void MoveSection(int delta)
    {
        if (_view == null || SettingsSection == null) return;

        if (!ViewColumnProjection.Move(_view, Sections.Select(s => s.Item), SettingsSection.ItemId, delta))
        {
            StatusText = "已经到头了。";
            return;
        }

        _store?.Save();
        ReorderSections();
        StatusText = "已调整节的顺序（只影响当前视图）。";
    }

    private void PersistCollapsed(InfoSectionViewModel section)
    {
        if (_view == null) return;

        if (section.IsCollapsed)
        {
            if (!_view.Collapsed.Contains(section.ItemId)) _view.Collapsed.Add(section.ItemId);
        }
        else _view.Collapsed.RemoveAll(id => id == section.ItemId);

        _store?.Save();
    }

    /// <summary>先出上次的观测（进页面不空白），再异步刷新。</summary>
    private void LoadCachedRows()
    {
        if (_collector == null) return;

        foreach (var s in Sections)
        {
            var (order, map) = _collector.LoadLatest(s.Item);
            if (order.Count == 0) continue;

            s.RebuildHeaders();
            s.SetRows(order.Select(k => (k, SysInfoStatus.Ok, (IReadOnlyDictionary<string, string>)map[k])));
            s.MarkOk(SysInfoStatus.Ok, DateTime.TryParse(s.Item.LastRunAt, out var t) ? t : DateTime.Now);
        }
    }

    // ==================== 刷新 ====================

}
