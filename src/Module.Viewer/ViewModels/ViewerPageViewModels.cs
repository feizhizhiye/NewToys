using NewToys.Shared.Services;
using NewToys.Viewer.Services;

namespace NewToys.Viewer.ViewModels;

/// <summary>「文本阅读」页 VM：只收 txt / md / 源码（长文本自动进阅读器）。</summary>
public sealed class ViewerTextViewModel : ViewerViewModel
{
    public ViewerTextViewModel(ViewerSettingsService settings, TxtBookService txt, ArchiveImageService archive,
        AudioLibraryService audio, IAudioPlayback playback, IModuleBus? bus = null, INavigationService? nav = null)
        : base(ViewerScope.Text, settings, txt, archive, audio, playback, bus, nav) { }
}

/// <summary>「图片查看」页 VM：只收图片与压缩包内图片。</summary>
public sealed class ViewerImageViewModel : ViewerViewModel
{
    public ViewerImageViewModel(ViewerSettingsService settings, TxtBookService txt, ArchiveImageService archive,
        AudioLibraryService audio, IAudioPlayback playback, IModuleBus? bus = null, INavigationService? nav = null)
        : base(ViewerScope.Image, settings, txt, archive, audio, playback, bus, nav) { }
}

/// <summary>「音乐播放」页 VM：只收本地音频（可一次多首）。</summary>
public sealed class ViewerAudioViewModel : ViewerViewModel
{
    public ViewerAudioViewModel(ViewerSettingsService settings, TxtBookService txt, ArchiveImageService archive,
        AudioLibraryService audio, IAudioPlayback playback, IModuleBus? bus = null, INavigationService? nav = null)
        : base(ViewerScope.Audio, settings, txt, archive, audio, playback, bus, nav) { }
}
