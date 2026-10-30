using System;
using Avalonia.Controls;
using NewToys.UI.Settings;
using NewToys.Shell.ViewModels;

namespace NewToys.Shell.Views;

/// <summary>
/// 应用级设置页（v3.65，需求 §4.9.2）。
/// <para>
/// 🔴 （2026-10-18）：**折叠 / 磁吸 / 滚动定位整体搬进 UI 库** <see cref="NewToys.UI.Controls.SettingsSectionHost"/>
/// （与 4 个模块设置页共用同一套骨架）⇒ 本文件只剩两件**本页特有**的事：
/// </para>
/// <list type="number">
///   <item>VM 的 <c>ScrollRequested</c>（外部寻址 / 搜索）→ 转给宿主去滚；</item>
///   <item>宿主的 <c>SectionToggled</c>（用户点抬头 / 全部收起）→ 通知 VM 刷新
///         状态栏聚合与当前节（状态栏按"哪几节展开"算，⛔ 不能只靠节自己）。</item>
/// </list>
/// <para>⚠️ 视口相关的数学全在宿主里，⛔ 本页不再有任何 <c>Offset</c> 补偿。</para>
/// </summary>
public partial class SettingsView : UserControl
{
    /// <summary>当前 VM（用于挂 / 摘 <c>ScrollRequested</c>）。</summary>
    private SettingsViewModel? _vm;

    public SettingsView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        // 宿主不认 VM（UI 库不引 Shell）⇒ 折叠意图由本页转交
        Host.SectionToggled += OnSectionToggled;
    }

    /// <summary>VM 换人（标签页切来切去 / 重建）⇒ 重挂定位事件。</summary>
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null) _vm.ScrollRequested -= OnScrollRequested;
        _vm = DataContext as SettingsViewModel;
        if (_vm != null) _vm.ScrollRequested += OnScrollRequested;
    }

    /// <summary>VM 请求定位（外部寻址 / 搜索 / 点开某节）⇒ 让宿主滚过去（收敛在宿主里）。</summary>
    private void OnScrollRequested(SettingsSection section) => Host.ScrollTo(section);

    /// <summary>用户折叠 / 展开某节（或整批）⇒ VM 刷新「当前节 + 状态栏聚合」。</summary>
    private void OnSectionToggled(object? sender, SettingsSection? section) => _vm?.NotifySectionsToggled(section);
}
