using NewToys.Shared.Services;
using NewToys.Viewer.Services;

namespace NewToys.Viewer.ViewModels;

/// <summary>「文档查看」页 VM：只收源码 / 短文本 / HTML（长文本阅读已并入「网络阅读」）。</summary>
public sealed class ViewerTextViewModel : ViewerViewModel
{
    public ViewerTextViewModel(ViewerSettingsService settings, ArchiveImageService archive,
        AudioLibraryService audio, IAudioPlayback playback, IModuleBus? bus = null, INavigationService? nav = null)
        : base(ViewerScope.Text, settings, archive, audio, playback, bus, nav) { }
}

/// <summary>「图片查看」页 VM：只收图片与压缩包内图片。</summary>
public sealed class ViewerImageViewModel : ViewerViewModel
{
    public ViewerImageViewModel(ViewerSettingsService settings, ArchiveImageService archive,
        AudioLibraryService audio, IAudioPlayback playback, IModuleBus? bus = null, INavigationService? nav = null)
        : base(ViewerScope.Image, settings, archive, audio, playback, bus, nav) { }
}

/// <summary>「音乐播放」页 VM：只收本地音频（可一次多首）。</summary>
public sealed class ViewerAudioViewModel : ViewerViewModel
{
    public ViewerAudioViewModel(ViewerSettingsService settings, ArchiveImageService archive,
        AudioLibraryService audio, IAudioPlayback playback, IModuleBus? bus = null, INavigationService? nav = null)
        : base(ViewerScope.Audio, settings, archive, audio, playback, bus, nav) { }
}
