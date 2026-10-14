using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.Settings.ViewModels;

/// <summary>
/// 「设置 → 配置」页（v3.5 新增）：多配置管理。
/// <para>一个配置 = 一套**独立的 config + data + logs**（落 <c>&lt;应用根&gt;\profiles\&lt;名称&gt;</c>）。</para>
/// <para>⚠️ **切换配置 = 重启应用** —— 配置在启动时就被大量单例服务读进内存了，热切换极易留下
/// "一半新配置一半旧配置"的状态（见规范 §4）。v3.26 起：切换只重启**本窗口**（显式传 --profile），
/// ⛔ 不改全局默认；「⭐ 设为默认」才写 active-profile.txt；「🚀 新开一个」以别的配置再起一个窗口
/// （多实例，各跑各的数据目录；同一配置单例）。</para>
/// </summary>
public partial class ProfileViewModel : ObservableObject, IStatusProvider
{
    private readonly IProfileService? _profiles;

    public ObservableCollection<ProfileItem> Items { get; } = new();

    [ObservableProperty] private ProfileItem? _selected;
    [ObservableProperty] private string _statusText = "";

    // v3.9：接入 Shell 底部状态栏契约（状态文本由 Shell 统一显示）
    public event EventHandler? StatusChanged;
    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);
    [ObservableProperty] private string _activeName = "";
    [ObservableProperty] private string _activeDirectory = "";

    /// <summary>数据是应用根（`profiles\` 的父级），方便用户去资源管理器里看。</summary>
    [ObservableProperty] private string _profilesHint = "";

    public ProfileViewModel() { }

    public ProfileViewModel(IProfileService profiles)
    {
        _profiles = profiles;
        Refresh();
    }

    public bool HasSelection => Selected != null;

    partial void OnSelectedChanged(ProfileItem? value) => OnPropertyChanged(nameof(HasSelection));

    private void Refresh()
    {
        Items.Clear();
        if (_profiles == null) { StatusText = "配置服务未就绪。"; return; }

        ActiveName = _profiles.ActiveProfile;
        ActiveDirectory = _profiles.GetDirectory(ActiveName);
        ProfilesHint = $"数据目录：{ActiveDirectory}";

        foreach (var name in _profiles.Profiles)
            Items.Add(new ProfileItem(
                name,
                string.Equals(name, ActiveName, StringComparison.OrdinalIgnoreCase),
                _profiles.GetDirectory(name)));

        // 默认选中当前配置，便于一眼看到"我在哪"
        Selected = Items.Count > 0 ? Items[0] : null;
        foreach (var it in Items)
            if (it.IsActive) { Selected = it; break; }

        StatusText = $"共 {Items.Count} 个配置 · 当前：{ActiveName}";
    }

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
            Refresh();
        }
        catch (Exception ex) { StatusText = "新建失败：" + ex.Message; }
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (_profiles == null) return;
        if (Selected == null) { StatusText = "先在列表里选中一个配置。"; return; }

        var oldName = Selected.Name;
        var name = await DialogHooks.InputAsync($"重命名配置：{oldName}", oldName);
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (string.Equals(name, oldName, StringComparison.Ordinal)) return;

        try
        {
            _profiles.Rename(oldName, name);
            StatusText = $"已重命名：{oldName} → {name}";
            Refresh();
        }
        catch (Exception ex) { StatusText = "重命名失败：" + ex.Message; }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (_profiles == null) return;
        if (Selected == null) { StatusText = "先在列表里选中一个配置。"; return; }

        var name = Selected.Name;
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
            Refresh();
        }
        catch (Exception ex) { StatusText = "删除失败：" + ex.Message; }
    }

    /// <summary>把选中配置**重置为初始态**（NT-038）：debug = 重新复制 default 的 config；release = 清空。⛔ 不能重置 default 或当前配置。</summary>
    [RelayCommand]
    private async Task ResetAsync()
    {
        if (_profiles == null) return;
        if (Selected == null) { StatusText = "先在列表里选中一个配置。"; return; }

        var name = Selected.Name;
        var ok = await DialogHooks.ConfirmAsync(
            $"把配置「{name}」重置为初始态？\n\n" +
            "会清空它的 config / data / logs（debug 会重新复制当前 default 的配置）。\n" +
            "此操作不可撤销；不能重置 default 或当前正在使用的配置。");
        if (!ok) return;

        try
        {
            _profiles.ResetToInitial(name);
            StatusText = $"已重置配置：{name}";
            Refresh();
        }
        catch (Exception ex) { StatusText = "重置失败：" + ex.Message; }
    }

    [RelayCommand]
    private async Task SwitchAsync()
    {
        if (_profiles == null) return;
        if (Selected == null) { StatusText = "先在列表里选中一个配置。"; return; }

        var name = Selected.Name;
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
    private void SetAsDefault()
    {
        if (_profiles == null) return;
        if (Selected == null) { StatusText = "先在列表里选中一个配置。"; return; }

        var name = Selected.Name;
        try
        {
            _profiles.SetAsDefault(name);
            StatusText = $"已将「{name}」设为默认配置 —— 以后直接启动 NewToys（不带参数）就是它。";
        }
        catch (Exception ex) { StatusText = "设置默认失败：" + ex.Message; }
    }

    /// <summary>以选中配置**新开一个窗口**（v3.26 多实例）：不退出当前窗口。</summary>
    [RelayCommand]
    private void LaunchNew()
    {
        if (_profiles == null) return;
        if (Selected == null) { StatusText = "先在列表里选中一个配置。"; return; }

        var name = Selected.Name;
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

/// <summary>列表里的一行配置。</summary>
public class ProfileItem
{
    public string Name { get; }
    public string Directory { get; }

    /// <summary>是否当前生效的配置。</summary>
    public bool IsActive { get; }

    /// <summary>当前配置的标记（列表里用强调色显示）。</summary>
    public string Mark => IsActive ? "● 当前使用" : "";

    public ProfileItem(string name, bool isActive, string directory)
    {
        Name = name;
        IsActive = isActive;
        Directory = directory;
    }
}
