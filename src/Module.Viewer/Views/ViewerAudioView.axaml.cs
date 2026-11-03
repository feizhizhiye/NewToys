namespace NewToys.Viewer.Views;

/// <summary>「音乐播放」页：本地音乐播放列表，顺序 / 单曲 / 随机、进度与音量、同名 lrc 歌词。</summary>
public partial class ViewerAudioView : ViewerPageBase
{
    public ViewerAudioView()
    {
        InitializeComponent();
        InitPage();
    }
}
