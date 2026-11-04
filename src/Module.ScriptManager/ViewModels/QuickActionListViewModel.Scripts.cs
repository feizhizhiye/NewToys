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

    /// <summary>
    /// 提交工具条那个框：交**模块设置服务**校验 + 落盘（<c>config\script-manager.json</c>）。
    /// 路径无效 ⇒ 状态文案以 `⚠` 开头（底栏红字）+ ⛔ 不写盘，保持上一个有效值。
    /// </summary>
    [RelayCommand]
    private void CommitScriptsFolder()
    {
        if (_pc == null) return;
        StatusText = _pc.SetScriptsFolder(ScriptsFolderDraft);
        RefreshScriptFolderViews();
    }

    /// <summary>
    /// 顶部「脚本目录」用文件选择器选完之后由 View 的 code-behind 调用：这是**明确的动作** → 立即生效并落盘
    /// （不用再点保存；避免"选了却没生效"的困惑，与模块设置页同一条规则）。
    /// </summary>
    public void ApplyPickedScriptFolder(string path)
    {
        if (_pc == null || string.IsNullOrWhiteSpace(path)) return;
        ScriptsFolderDraft = path.Trim();
        CommitScriptsFolder();
    }

    /// <summary>恢复成默认脚本目录（<c>&lt;应用根&gt;\scripts</c>）。</summary>
    [RelayCommand]
    private void ResetScriptFolder()
    {
        if (_pc == null) return;
        ScriptsFolderDraft = "";
        StatusText = _pc.ResetScriptsFolder();
        RefreshScriptFolderViews();
    }

    /// <summary>在资源管理器里打开脚本目录（没有就先建出来）。</summary>
    [RelayCommand]
    private void OpenScriptsFolder()
    {
        var dir = ScriptsDirectory;
        try
        {
            if (string.IsNullOrWhiteSpace(dir)) { StatusText = "脚本目录未配置。"; return; }
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
            StatusText = "已打开脚本目录：" + dir;
        }
        catch (Exception ex) { StatusText = "打开失败：" + ex.Message; }
    }

    /// <summary>脚本目录相关的显示项一起刷新（生效值 / 默认值 / 是否默认 / 提示文本）。</summary>
    private void RefreshScriptFolderViews()
    {
        OnPropertyChanged(nameof(ScriptsDirectory));
        OnPropertyChanged(nameof(DefaultScriptFolder));
        OnPropertyChanged(nameof(IsDefaultScriptFolder));
        OnPropertyChanged(nameof(ScriptHint));
    }

    /// <summary>脚本字段下方的提示（解释器怎么挑 + 脚本目录在哪）。</summary>
    public string ScriptHint =>
        $"脚本文件 = 脚本目录里的 .py / .ps1 / .bat（填相对文件名即可，目录在「设置 → 脚本管理器设置」里改，工具条那个框也能直接改）；工具条「导入脚本」可批量导入。\n脚本目录：{ScriptsDirectory}" +
        (IsDefaultScriptFolder ? "（默认值）\n" : "\n") +
        "解释器留「自动」即可：.py→python、.ps1→pwsh（没配则退回 5.1）、.bat/.cmd→cmd；三个解释器路径在「设置 → 脚本管理器设置」里配，留空 = 用系统默认。";

    // ==================== 派生显示 ====================

    /// <summary>右键菜单里「收藏 / 取消收藏」的动态文案。</summary>
    [ObservableProperty] private string _favoriteMenuLabel = "★ 添加到收藏夹";

    public bool HasSelection => Selected != null;
    public bool HasItems => Items.Count > 0;

    /// <summary>详情卡的数据源：有选中项时显示它（v3.60 §4.8.1：⛔ 没选中不填示例值）。</summary>
    public QuickAction? DetailItem => Selected;

    /// <summary>详情卡正文是否显示（有选中项）。</summary>
    public bool ShowDetailBody => HasSelection;
    // ==================== 详情面板 + 声明式详情卡（的 L2 迁移页，需求 §4.5.1） ====================
    //    本基类被两页共用（「脚本库」/「命令库」）⇒ 契约在这里实现一次，
    //    **卡片描述符由两个子类各自 override <see cref="BuildCard"/>**（两页字段本来就不同：
    //    脚本页有「类型 / 脚本文件 / 解释器」，命令库页有「Shell / 隐藏终端窗口」）。

    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;

    /// <summary>声明式卡（<see cref="Card"/> 非 null）⇒ ⛔ 不再返回自绘视图；老路只留给尚未迁移的页。</summary>
    public object? DetailContent => null;

    public event EventHandler? DetailChanged;

}
