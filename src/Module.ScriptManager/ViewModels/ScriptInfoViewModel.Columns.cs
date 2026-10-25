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

    /// <summary>按当前节重建「显示列」chips（列标题取视图覆盖后的值）。</summary>
    private void RebuildSectionColumnChips()
    {
        SectionColumnChips.Clear();

        if (SettingsSection != null)
            foreach (var col in SettingsSection.Columns)
                SectionColumnChips.Add(new SectionColumnChip
                {
                    Key = col.Key,
                    Title = InfoRowFactory.Title(col),
                    Visible = col.Visible,
                });

        OnPropertyChanged(nameof(SectionColumnsHint));
    }

    /// <summary>
    /// 卡里「表头名称」失焦 ⇒ 把这一节的列标题写进当前视图并重投影本节（只影响当前视图）。
    /// </summary>
    [RelayCommand]
    private void CommitColumnTitle(SectionColumnChip? chip)
    {
        // 没有视图就没有「视图级覆盖」的落点（直接改会退回默认列显隐）⇒ 不处理
        if (chip == null || SettingsSection == null || _view == null) return;

        var titles = SectionColumnChips
            .Where(c => !string.IsNullOrWhiteSpace(c.Key))
            .Select(c => new KeyValuePair<string, string>(
                c.Key,
                string.Equals(c.Key, chip.Key, StringComparison.OrdinalIgnoreCase) ? chip.Title : c.Title));

        ViewColumnProjection.SetTitles(_view, SettingsSection.Item, titles);
        _store?.Save();

        SettingsSection.ApplyColumns(ViewColumnProjection.Resolve(SettingsSection.Item, _view));
        var applied = SettingsSection.Columns.FirstOrDefault(c => string.Equals(c.Key, chip.Key, StringComparison.OrdinalIgnoreCase));
        StatusText = applied == null
            ? "表头已更新（只影响当前视图）。"
            : $"表头「{chip.Key}」已改为「{InfoRowFactory.Title(applied)}」（只影响当前视图）。";
    }

    /// <summary>卡里点 chip ⇒ 切这一列在当前视图的显隐（写视图 + 即时重投影本节）。</summary>
    [RelayCommand]
    private void ToggleSectionColumn(SectionColumnChip? chip)
    {
        if (chip == null || SettingsSection == null) return;

        ViewColumnProjection.Toggle(SettingsSection.Item, _view, chip.Key);
        _store?.Save();

        SettingsSection.ApplyColumns(ViewColumnProjection.Resolve(SettingsSection.Item, _view));
        RebuildSectionColumnChips();
        // chip.Visible 是点击**前**的值 ⇒ 文案取反。
        StatusText = $"已{(chip.Visible ? "隐藏" : "显示")}列「{chip.Title}」（只影响当前视图）。";
    }

    /// <summary>
    /// 点节（节头 / 节点任意处）⇒ 把详情卡切到这一节；**不主动打开面板**（和列表页点行一个心智）。
    /// 需要打开面板时走右键菜单「⚙ 显示这一节的详情 / 设置」。
    /// </summary>
    public void SelectSection(InfoSectionViewModel? section)
    {
        if (section == null || ReferenceEquals(SettingsSection, section)) return;
        SettingsSection = section;
    }

    [RelayCommand]
    private void ToggleDetailPanel()
    {
        ToggleDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenDetailPanel()
    {
        ShowDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    void IDetailPanelProvider.ToggleDetail() => ToggleDetailPanelCommand.Execute(null);
    void IDetailPanelProvider.OpenDetail() => OpenDetailPanelCommand.Execute(null);

    // ==================== 集合 / 视图 ====================

}
