namespace NewToys.Viewer.Views;

/// <summary>「图片查看」页：图片与压缩包（zip / rar / 7z）内图片，缩放 / 适应窗口 / 逐张翻页。</summary>
public partial class ViewerImageView : ViewerPageBase
{
    public ViewerImageView()
    {
        InitializeComponent();
        InitPage();
    }
}
