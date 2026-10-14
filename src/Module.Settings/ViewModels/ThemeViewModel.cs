using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Services;

namespace NewToys.Settings.ViewModels;

/// <summary>
/// 主题外观页。
/// <para>
/// **本页没有任何「应用 / 保存」按钮，这是刻意的**：明暗与强调色属于"点一下立刻看得见"的设置，
/// 直接写 <see cref="IThemeService"/>（内部立即落盘 + 广播 <c>ThemeChanged</c>），Shell 收到后重建资源字典
/// —— 全程无需确认、无需重启。
/// </para>
/// 规律（已写进《工程规范》）：**能立即看到反馈的开关 / 单选 → 立即生效；只有多字段文本表单才留"保存"。**
/// </summary>
public partial class ThemeViewModel : ObservableObject, IStatusProvider
{
    private readonly IThemeService? _theme;

    /// <summary>抑制标志：区分"程序回写属性"与"用户点击控件"，避免互相触发。</summary>
    private bool _syncing;

    [ObservableProperty] private bool _isDarkMode;
    [ObservableProperty] private bool _isLightMode;
    [ObservableProperty] private bool _isSystemMode;
    [ObservableProperty] private AccentOption? _selectedAccent;
    [ObservableProperty] private string _statusText = "";

    // v3.9：接入 Shell 底部状态栏契约（状态文本由 Shell 统一显示）
    public event EventHandler? StatusChanged;
    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>强调色色卡数据源（直接取自 <see cref="AccentPalette"/>，与 Shell 注入用的是同一份表）。</summary>
    public ObservableCollection<AccentOption> Accents { get; } = new();

    public ThemeViewModel() { BuildAccents(); }

    public ThemeViewModel(IThemeService theme)
    {
        _theme = theme;
        BuildAccents();
        Load();
    }

    private void BuildAccents()
    {
        foreach (var p in AccentPalette.All)
        {
            Accents.Add(new AccentOption
            {
                Id = p.Id,
                Name = p.Name,
                Swatch = new SolidColorBrush(Color.Parse(p.Color)),
            });
        }
    }

    private void Load()
    {
        ApplyMode(_theme?.Mode ?? ThemeMode.Dark);
        var current = AccentPalette.Find(_theme?.AccentId).Id;
        _syncing = true;
        SelectedAccent = Accents.FirstOrDefault(a => a.Id == current) ?? Accents.FirstOrDefault();
        _syncing = false;
        RefreshStatus();
    }

    // ==================== 模式：勾选即生效 ====================

    partial void OnIsDarkModeChanged(bool value) { if (value) SetMode(ThemeMode.Dark); }
    partial void OnIsLightModeChanged(bool value) { if (value) SetMode(ThemeMode.Light); }
    partial void OnIsSystemModeChanged(bool value) { if (value) SetMode(ThemeMode.System); }

    private void SetMode(ThemeMode mode)
    {
        if (_syncing) return;
        _theme?.SetMode(mode);      // 立即落盘 + 广播，Shell 收到后重刷资源字典
        ApplyMode(mode);            // 三个布尔互斥由这里保证，不依赖 GroupName 的容器作用域
        RefreshStatus();
    }

    private void ApplyMode(ThemeMode mode)
    {
        _syncing = true;
        IsDarkMode = mode == ThemeMode.Dark;
        IsLightMode = mode == ThemeMode.Light;
        IsSystemMode = mode == ThemeMode.System;
        _syncing = false;
    }

    // ==================== 强调色：选中即生效 ====================

    partial void OnSelectedAccentChanged(AccentOption? value)
    {
        if (_syncing || value == null) return;
        _theme?.SetAccent(value.Id);
        RefreshStatus();
    }

    // ==================== 恢复默认 ====================

    [RelayCommand]
    private void ResetTheme()
    {
        _theme?.SetMode(ThemeMode.Dark);                  // 工程约定：默认深色
        _theme?.SetAccent(AccentPalette.Default.Id);       // 默认微信绿

        ApplyMode(ThemeMode.Dark);
        _syncing = true;
        SelectedAccent = Accents.FirstOrDefault(a => a.Id == AccentPalette.Default.Id);
        _syncing = false;
        RefreshStatus("已恢复默认");
    }

    private void RefreshStatus(string? prefix = null)
    {
        var mode = IsDarkMode ? "深色" : IsLightMode ? "浅色" : "跟随系统";
        var accentName = SelectedAccent?.Name ?? AccentPalette.Find(_theme?.AccentId).Name;
        StatusText = $"{prefix ?? "当前"}：{mode} · 强调色 {accentName} —— 已立即生效并写入 theme.json";
    }
}

/// <summary>一张强调色色卡。<see cref="Swatch"/> 直接绑到 XAML 的小圆点背景。</summary>
public class AccentOption
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public IBrush Swatch { get; init; } = Brushes.Transparent;
}
