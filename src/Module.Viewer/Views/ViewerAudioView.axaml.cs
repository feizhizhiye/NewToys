namespace NewToys.Viewer.Views;

/// <summary>「音频播放」页：本地音频播放列表，顺序 / 单曲 / 随机、进度与音量、同名 lrc 歌词。</summary>
public partial class ViewerAudioView : ViewerPageBase
{
    public ViewerAudioView()
    {
        InitializeComponent();
        InitPage();
    }
}
