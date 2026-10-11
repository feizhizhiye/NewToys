using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Services;
using NewToys.VideoTools.Models;
using NewToys.VideoTools.Services;

namespace NewToys.VideoTools.ViewModels;

/// <summary>收藏页卡片网格里的一张卡（缩略图 + 文件名 + 标签，需求 §6.4.9）。</summary>
public sealed class VideoFavoriteCard
{
    /// <summary>视频完整路径（<c>video_favorites</c> 的主键）。</summary>
    public string Path { get; set; } = "";

    /// <summary>现读的文件信息 + 媒体信息（⛔ 不存快照：文件可能已移动 / 改名）。</summary>
    public VideoItem Item { get; set; } = new();

    /// <summary>缩略图（磁盘 <c>data\thumbs</c> 里的 jpg；没抽过帧时为 null）。</summary>
    public Bitmap? Thumb { get; set; }

    public bool HasThumb => Thumb != null;

    public string AddedAtDisplay { get; set; } = "";
}

/// <summary>
/// 「视频工具 → 视频收藏」页（v3.15，需求 §6.4.9）。
/// <para>
/// 与另两页不同，本页**不是列表页**：主显示区是 <c>ItemsControl</c> + <c>WrapPanel</c> 的**卡片网格**
/// （每张卡 = 缩略图 + 文件名 + 标签），所以⛔ **不继承** <c>ListPageViewModelBase</c>（那是给列表页用的列显隐 / 排序 / 筛选）。
/// </para>
/// <para>
/// 数据来源只有一处：**视频列表**页「★ 收藏」送过来（必须先在 <c>video_catalog</c> 里）；
/// 缩略图由「视频预览」页「📸 取当前帧」抽出来，经 <see cref="VideoTaskStore"/> 的任务通道送过来建卡。
/// ⛔ 不支持直接导入文件。
/// </para>
/// </summary>
public partial class VideoFavoritesViewModel : ObservableObject, IStatusProvider
{
    private readonly VideoFavoritesService? _fav;
    private readonly VideoCatalogService? _catalog;
    private readonly VideoMediaIndex? _index;
    private readonly IAppConfigService? _cfg;
    private readonly VideoTaskStore? _store;
    private readonly INavigationService? _nav;

    public ObservableCollection<VideoFavoriteCard> Cards { get; } = new();

    [ObservableProperty] private VideoFavoriteCard? _selected;
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isDetailVisible = true;
    [ObservableProperty] private string _summaryText = "还没有收藏";

    /// <summary>详情卡里的「标签」编辑框（§6.4.6 同一套：提交才落 <c>video_catalog.tags</c>）。</summary>
    [ObservableProperty] private string _editTags = "";

    /// <summary>详情卡里的「文件名」编辑框（§6.4.7 同一套：提交才动磁盘）。</summary>
    [ObservableProperty] private string _editName = "";

    public bool HasSelection => Selected != null;
    public bool HasCards => Cards.Count > 0;

    public event EventHandler? StatusChanged;

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    partial void OnSelectedChanged(VideoFavoriteCard? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        EditTags = value?.Item.Tags ?? "";
        EditName = value?.Item.Name ?? "";
    }

    public VideoFavoritesViewModel() { }

    public VideoFavoritesViewModel(VideoFavoritesService fav, VideoCatalogService catalog, VideoMediaIndex index,
                                   IAppConfigService cfg, VideoTaskStore store, INavigationService nav)
    {
        _fav = fav;
        _catalog = catalog;
        _index = index;
        _cfg = cfg;
        _store = store;
        _nav = nav;
        Load();
    }

    // ==================== 载入 ====================

    /// <summary>读 <c>video_favorites</c> 全表 → 建卡片（缩略图 / 标签 / 文件信息都是**现读**）。</summary>
    public void Load()
    {
        Cards.Clear();
        Selected = null;

        if (_fav == null || !_fav.IsAvailable)
        {
            StatusText = "收藏不可用（数据库未就绪）。";
            SummaryText = "收藏不可用";
            return;
        }

        foreach (var row in _fav.All())
        {
            var item = BuildItem(row.Path);
            item.Tags = _catalog?.GetTags(row.Path) ?? "";      // §6.4.9：标签读 video_catalog.tags
            Cards.Add(new VideoFavoriteCard
            {
                Path = row.Path,
                Item = item,
                Thumb = LoadThumb(row.Thumb),
                AddedAtDisplay = row.AddedAtDisplay
            });
        }

        var missing = Cards.Count(c => !File.Exists(c.Path));
        SummaryText = $"共 {Cards.Count} 个收藏" + (missing > 0 ? $" · {missing} 个文件已不在原处" : "");
        StatusText = Cards.Count == 0
            ? "还没有收藏：在「视频列表」页勾几个文件点「★ 收藏」（要先「💾 加入视频库」）；缩略图在「视频预览」页点「📸 取当前帧」生成。"
            : $"已载入 {Cards.Count} 个收藏" + (missing > 0 ? $"（{missing} 个源文件已不在原处）" : "");
    }

    /// <summary>
    /// 🔴 **取件点**：把「视频预览」页「📸 取当前帧」送过来的帧建成收藏卡。
    /// <para>⛔ 必须由 View 在 <c>OnAttachedToVisualTree</c> 里调（规范 §4.8 硬规则 8）：
    /// 标签页已存在、切回来时只重挂可视树、DataContext 不变 → <c>OnDataContextChanged</c> **不会**触发。</para>
    /// </summary>
    public void ConsumePendingFrames()
    {
        if (_store == null || _fav == null || !_fav.IsAvailable) return;

        var frames = _store.TakePendingFrames();
        if (frames.Count == 0) return;

        foreach (var (path, thumb) in frames)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            _fav.Add(path, thumb);
        }
        Load();
        StatusText = $"已按取到的 {frames.Count} 张帧建成 / 更新收藏卡。";
    }

    private VideoItem BuildItem(string path)
    {
        var item = new VideoItem
        {
            FullPath = path,
            Name = Path.GetFileName(path),
            Extension = Path.GetExtension(path).ToLowerInvariant()
        };

        if (File.Exists(path))
        {
            var fi = new FileInfo(path);
            item.SizeBytes = fi.Length;
            item.Modified = fi.LastWriteTime;
            item.Created = fi.CreationTime;
        }

        _index?.TryApplyCached(item);       // 缓存命中就填上，⛔ 这里不跑 ffprobe
        return item;
    }

    private Bitmap? LoadThumb(string thumbFileName)
    {
        if (_fav == null || string.IsNullOrWhiteSpace(thumbFileName)) return null;
        var full = _fav.ThumbFullPath(thumbFileName);
        if (string.IsNullOrEmpty(full) || !File.Exists(full)) return null;
        try { return new Bitmap(full); }
        catch { return null; }              // 图坏了 / 不是图片：当没有缩略图处理
    }

    // ==================== 命令 ====================

    [RelayCommand]
    private void Refresh() => Load();

    /// <summary>
    /// 点一张卡 = 选中它（<c>ItemsControl</c> 没有 <c>SelectedItem</c>，只能自己传卡对象过来）。
    /// <para>⚠️ 参数收 <c>object?</c> 自己转：XAML 里的 <c>CommandParameter</c> 是绑定对象时类型才可靠，
    /// 写成字面量会被当成 <c>string</c> 喂进来（踩过的坑，见 AI 协作提示词）。</para>
    /// </summary>
    [RelayCommand]
    private void SelectCard(object? card)
    {
        if (card is not VideoFavoriteCard c) return;
        Selected = c;
        IsDetailVisible = true;
    }

    /// <summary>❮/❯ 显示 / 隐藏右侧详情。</summary>
    [RelayCommand]
    private void ToggleDetail()
    {
        IsDetailVisible = !IsDetailVisible;
        if (IsDetailVisible && Selected == null && Cards.Count > 0) Selected = Cards[0];
    }

    /// <summary>💾 加入视频库（收藏必须来自视频库；标签也要入库才存得住）。</summary>
    [RelayCommand]
    private void AddToCatalog()
    {
        var c = Selected;
        if (c == null || _catalog == null) return;
        if (!_catalog.IsAvailable) { StatusText = "视频库不可用（数据库未就绪）。"; return; }

        _catalog.Upsert(c.Item);
        StatusText = $"已写入视频库：{c.Item.Name}";
    }

    /// <summary>★ 取消收藏（顺带删掉它的缩略图文件）。</summary>
    [RelayCommand]
    private void RemoveFavorite()
    {
        var c = Selected;
        if (c == null || _fav == null) return;

        var name = c.Item.Name;
        if (!_fav.Remove(c.Path)) { StatusText = "取消收藏失败。"; return; }
        Cards.Remove(c);
        Selected = null;
        StatusText = $"已取消收藏：{name}";
    }

    /// <summary>🏷 提交标签（落 <c>video_catalog.tags</c>）；没入库的提示先「💾 加入视频库」。</summary>
    [RelayCommand]
    private void SaveTags()
    {
        var c = Selected;
        if (c == null) return;

        var tags = (EditTags ?? "").Trim();
        if (string.Equals(tags, c.Item.Tags ?? "", StringComparison.Ordinal)) { StatusText = "标签没变。"; return; }
        if (_catalog == null || !_catalog.HasPath(c.Path))
        {
            StatusText = $"「{c.Item.Name}」还没进视频库 —— 先点「💾 加入视频库」，标签才能存下来。";
            return;
        }

        c.Item.Tags = tags;
        _catalog.UpdateTags(c.Path, tags);
        OnPropertyChanged(nameof(Selected));
        StatusText = "已更新标签：" + (tags.Length > 0 ? tags : "（已清空）");
    }

    /// <summary>✏ 提交改名（复用 <see cref="VideoFileService.RenameToAsync"/> 的防穿越 / 同名检查）。</summary>
    [RelayCommand]
    private async Task SaveNameAsync()
    {
        var c = Selected;
        if (c == null) return;

        var newName = (EditName ?? "").Trim();
        if (newName.Length == 0) { StatusText = "文件名不能为空。"; return; }
        if (string.Equals(newName, c.Item.Name, StringComparison.Ordinal)) { StatusText = "名字没变。"; return; }

        var oldPath = c.Path;
        var (ok, msg, newPath) = await VideoFileService.RenameToAsync(oldPath, newName);
        StatusText = msg;
        if (!ok || newPath == null) return;

        c.Item.Name = Path.GetFileName(newPath);
        c.Item.FullPath = newPath;
        c.Path = newPath;
        if (_catalog != null && _catalog.HasPath(oldPath)) _catalog.MovePath(oldPath, newPath, c.Item.Name);
        _fav?.MovePath(oldPath, newPath);
        OnPropertyChanged(nameof(Selected));
    }

    /// <summary>▶ 播放（配了外部播放器就用它，否则走系统关联程序）。</summary>
    [RelayCommand]
    private void Play()
    {
        var c = Selected;
        if (c == null) return;
        if (!File.Exists(c.Path)) { StatusText = "文件已不存在：" + c.Path; return; }

        var player = (_cfg?.PlayerPath ?? "").Trim();
        try
        {
            if (player.Length > 0 && File.Exists(player))
                Process.Start(new ProcessStartInfo(player, $"\"{c.Path}\"") { UseShellExecute = false });
            else
                Process.Start(new ProcessStartInfo(c.Path) { UseShellExecute = true });
            StatusText = "已播放：" + c.Item.Name;
        }
        catch (Exception ex) { StatusText = "播放失败：" + ex.Message; }
    }

    /// <summary>📂 在资源管理器中定位。</summary>
    [RelayCommand]
    private void RevealInExplorer()
    {
        var c = Selected;
        if (c == null) return;
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{c.Path}\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo(Path.GetDirectoryName(c.Path) ?? "") { UseShellExecute = true });
            StatusText = "已在文件管理器中定位。";
        }
        catch (Exception ex) { StatusText = "打开失败：" + ex.Message; }
    }

    [RelayCommand]
    private async Task CopyPathAsync()
    {
        var c = Selected;
        if (c == null) return;
        try
        {
            var win = (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (win?.Clipboard == null) { StatusText = "剪贴板不可用。"; return; }
            await win.Clipboard.SetTextAsync(c.Path);
            StatusText = "已复制路径：" + c.Path;
        }
        catch (Exception ex) { StatusText = "复制失败：" + ex.Message; }
    }

    /// <summary>✂ 送到「视频预览」页（用取件通道，预览页一次只编一个）。</summary>
    [RelayCommand]
    private void SendToPreview()
    {
        var c = Selected;
        if (c == null || _store == null) return;
        _store.EnqueueForCut(new[] { c.Path });
        _nav?.NavigateTo("video-cut");
        StatusText = $"已把「{c.Item.Name}」送去视频预览页。";
    }

    /// <summary>➡ 送到「批量处理」页建转码任务。</summary>
    [RelayCommand]
    private void SendToTranscode()
    {
        var c = Selected;
        if (c == null || _store == null) return;
        _store.EnqueueForTranscode(new[] { c.Path });
        _nav?.NavigateTo("video-batch");
        StatusText = $"已把「{c.Item.Name}」送去批量处理页。";
    }
}
