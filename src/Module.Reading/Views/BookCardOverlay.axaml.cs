using Avalonia.Controls;
using Avalonia.Input;
using NewToys.Reading.ViewModels;

namespace NewToys.Reading.Views;

/// <summary>
/// 书籍卡浮层：点遮罩 = 关闭（卡片本体不响应，避免点简介被误关）。
/// <para>控件本身只负责布局，数据与命令全在页面 VM 上（同名约定见 XAML 顶部说明）。</para>
/// </summary>
public partial class BookCardOverlay : UserControl
{
    public BookCardOverlay()
    {
        InitializeComponent();
    }

    private void OnBackdropTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        switch (DataContext)
        {
            case ExploreViewModel explore: explore.CloseCardCommand.Execute(null); break;
            case SearchViewModel search: search.CloseCardCommand.Execute(null); break;
        }
    }
}
