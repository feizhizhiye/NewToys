using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shell.Models;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Shell.ViewModels;

public partial class PathViewModel : ListPageViewModelBase, IStatusActionProvider
{

    [RelayCommand]
    private async Task NewProfileAsync()
    {
        if (_profiles == null) return;

        var name = await DialogHooks.InputAsync("新建配置（名称）", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();

        try
        {
            _profiles.Create(name);
            StatusText = $"已新建配置「{name}」—— 切换过去后从空白开始。";
            RefreshProfiles();
        }
        catch (Exception ex) { StatusText = "新建失败：" + ex.Message; }
    }

    [RelayCommand]
    private async Task RenameProfileAsync(PathSettingItem? row)
    {
        if (_profiles == null) return;
        var target = Resolve(row);
        if (target?.IsProfileRow != true) { StatusText = "先在列表里选中一个配置行。"; return; }

        var oldName = target.Name;
        var name = await DialogHooks.InputAsync($"重命名配置：{oldName}", oldName);
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (string.Equals(name, oldName, StringComparison.Ordinal)) return;

        try
        {
            _profiles.Rename(oldName, name);
            StatusText = $"已重命名：{oldName} → {name}";
            RefreshProfiles();
        }
        catch (Exception ex) { StatusText = "重命名失败：" + ex.Message; }
    }

    [RelayCommand]
    private async Task DeleteProfileAsync(PathSettingItem? row)
    {
        if (_profiles == null) return;
        var target = Resolve(row);
        if (target?.IsProfileRow != true) { StatusText = "先在列表里选中一个配置行。"; return; }

        var name = target.Name;
        if (string.Equals(name, _profiles.ActiveProfile, StringComparison.OrdinalIgnoreCase))
        {
            StatusText = "不能删除当前正在使用的配置 —— 先切到别的配置再删。";
            return;
        }

        var dir = _profiles.GetDirectory(name);
        var ok = await DialogHooks.ConfirmAsync(
            $"确定要删除配置「{name}」吗？\n\n" +
            "这会永久删除它的全部设置与功能数据（config / data / logs）：\n" +
            $"{dir}\n\n" +
            "此操作不可撤销。");
        if (!ok) return;

        try
        {
            _profiles.Delete(name);
            StatusText = $"已删除配置：{name}";
            RefreshProfiles();
        }
        catch (Exception ex) { StatusText = "删除失败：" + ex.Message; }
    }

    [RelayCommand]
    private async Task SwitchProfileAsync(PathSettingItem? row)
    {
        if (_profiles == null) return;
        var target = Resolve(row);
        if (target?.IsProfileRow != true) { StatusText = "先在列表里选中一个配置行。"; return; }

        var name = target.Name;
        if (string.Equals(name, _profiles.ActiveProfile, StringComparison.OrdinalIgnoreCase))
        {
            StatusText = $"当前已经在「{name}」上了。";
            return;
        }

        var dir = _profiles.GetDirectory(name);
        var ok = await DialogHooks.ConfirmAsync(
            $"切换到配置「{name}」？\n\n" +
            "本窗口会自动重启，之后所有设置与功能数据都来自：\n" +
            $"{dir}\n\n" +
            "（只影响本窗口——其它已打开的窗口和全局默认配置都不变）");
        if (!ok) return;

        try
        {
            _profiles.SwitchTo(name);
            _profiles.RestartApp(name);   // 起新实例（带 --profile）+ 退出自己；新实例起不来时这里会返回（不退出）
            StatusText = $"已切到「{name}」，但自动重启失败 —— 请手动重启 NewToys 使配置生效。";
        }
        catch (Exception ex) { StatusText = "切换失败：" + ex.Message; }
    }

    /// <summary>把选中配置设为**全局默认**（v3.26）：写 active-profile.txt，影响以后无参启动的实例。</summary>
    [RelayCommand]
    private void SetProfileDefault(PathSettingItem? row)
    {
        if (_profiles == null) return;
        var target = Resolve(row);
        if (target?.IsProfileRow != true) { StatusText = "先在列表里选中一个配置行。"; return; }

        var name = target.Name;
        try
        {
            _profiles.SetAsDefault(name);
            StatusText = $"已将「{name}」设为默认配置 —— 以后直接启动 NewToys（不带参数）就是它。";
        }
        catch (Exception ex) { StatusText = "设置默认失败：" + ex.Message; }
    }

    /// <summary>以选中配置**新开一个窗口**（v3.26 多实例）：不退出当前窗口。</summary>
    [RelayCommand]
    private void LaunchProfileNew(PathSettingItem? row)
    {
        if (_profiles == null) return;
        var target = Resolve(row);
        if (target?.IsProfileRow != true) { StatusText = "先在列表里选中一个配置行。"; return; }

        var name = target.Name;
        if (string.Equals(name, _profiles.ActiveProfile, StringComparison.OrdinalIgnoreCase))
        {
            StatusText = $"「{name}」就是当前窗口正在用的配置 —— 同一配置同时只允许一个窗口（数据会互相覆盖）。";
            return;
        }

        try
        {
            _profiles.LaunchNewInstance(name);
            StatusText = $"已新开一个「{name}」窗口（任务栏里能看到，标题带配置名）。";
        }
        catch (Exception ex) { StatusText = "新开失败：" + ex.Message; }
    }

}
