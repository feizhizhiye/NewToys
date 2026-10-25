using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;   // 🔴 SetTextAsync / GetTextAsync 是 IClipboard 的**扩展方法**，漏了这行会报 CS1061
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.UI.Services;   // 跨页传物（书签 / Ping → 抓取）已上提 NewToys.UI
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.Services;

namespace NewToys.ScriptManager.ViewModels;

public partial class RuleEditorViewModel : ObservableObject, IStatusProvider, IToolbarSource
{

    private void RefreshFlows()
    {
        var keep = SelectedFlowName;
        FlowNames.Clear();
        foreach (var n in _flows.List()) FlowNames.Add(n);
        SelectedFlowName = keep is not null && FlowNames.Contains(keep) ? keep : null;
        NotifyToolbarChanged();
    }

    partial void OnSelectedFlowNameChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) LoadFlow(value);
    }

    /// <summary>载入一条流水线到槽位（工具条下拉选中即载入）。</summary>
    private void LoadFlow(string name)
    {
        var cfg = _flows.Load(name);
        FlowName = cfg.Name;
        if (cfg.UrlTemplate.Length > 0) StartUrl = cfg.UrlTemplate;

        foreach (var slot in Slots)
        {
            if (slot.BuiltIn) continue;
            var want = cfg.Slot(slot.Key);
            var opt = slot.Options.FirstOrDefault(o => o.Value == want);
            if (opt is null && want.Length > 0)
            {
                opt = new SlotOption { Value = want, Label = want + "（缺脚本）" };
                slot.Options.Add(opt);
            }
            slot.Selected = opt ?? slot.Options[0];
        }

        StatusText = "已载入流水线「" + cfg.Name + "」· " + cfg.Summary;
    }

    [RelayCommand]
    private void SaveFlow()
    {
        var name = (FlowName ?? "").Trim();
        if (name.Length == 0) { StatusText = "先在工具条输入框里填流水线名。"; return; }

        try
        {
            var cfg = new FlowConfig { Name = name, UrlTemplate = StartUrl };
            foreach (var slot in Slots)
                if (!slot.BuiltIn) cfg.SetSlot(slot.Key, slot.ScriptName);

            _flows.Save(cfg);
            RefreshFlows();
            SelectedFlowName = cfg.Name;
            StatusText = "已保存流水线「" + cfg.Name + "」· " + cfg.Summary;
        }
        catch (Exception ex) { StatusText = "⚠ 保存流水线失败：" + FirstLine(ex.Message); }
    }

    [RelayCommand]
    private void DeleteFlow()
    {
        var name = SelectedFlowName;
        if (string.IsNullOrWhiteSpace(name)) { StatusText = "先在工具条下拉里选一条流水线。"; return; }

        if (_flows.Delete(name))
        {
            RefreshFlows();
            FlowName = "";
            StatusText = "已删除流水线「" + name + "」。" ;
        }
        else StatusText = "⚠ 删除失败：" + name;
    }

    [RelayCommand]
    private void BackToCatalog() => _nav?.NavigateTo("web-scrape");

    // ==================== 右栏：命令编辑（写回 RULES 块） ====================

}
