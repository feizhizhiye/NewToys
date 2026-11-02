using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NewToys.Reading.ViewModels;

namespace NewToys.Reading.Views;

/// <summary>
/// 发现页。控件级逻辑只有一件：树里点分类 = 读该分类。
/// <para>🔴 分类的 ListBox 在 DataTemplate 里 ⇒ 事件用**路由冒泡**挂到树上（⛔ 不在 XAML 模板里写事件名）。</para>
/// <para>点一本书弹书籍卡由 VM 处理（<c>SelectedResult</c> 变化即弹），本文件不参与。</para>
/// </summary>
public partial class ExploreView : UserControl
{
    public ExploreView()
    {
        InitializeComponent();
        SourceTree.AddHandler(
            ListBox.SelectionChangedEvent,
            new EventHandler<SelectionChangedEventArgs>(OnCategorySelectionChanged),
            RoutingStrategies.Bubble);
    }

    private void OnCategorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not ExploreViewModel vm) return;
        if (e.Source is not ListBox list || list.SelectedItem is not ExploreNode node) return;
        vm.SelectCategory(node);
    }
}
