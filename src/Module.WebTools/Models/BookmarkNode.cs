using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NewToys.Shared.Models;

namespace NewToys.WebTools.Models;

/// <summary>书签树的节点类型。</summary>
public enum BookmarkNodeKind
{
    /// <summary>文件夹行（可展开 / 折叠，带子孙条数）。</summary>
    Folder,

    /// <summary>书签行（叶子，指向一条 <see cref="BookmarkItem"/>）。</summary>
    Bookmark
}

/// <summary>
/// 书签树的**视图节点**：由扁平列表按 <see cref="BookmarkItem.Folder"/>（<c>/</c> 分隔全路径）**派生**，
/// ⛔ 不持久化、⛔ 不进 <c>web_docs</c>。
/// <para>
/// 🔴 页面按「缩进列 + 扁平集合」渲染（不是 <c>TreeView</c>）：展开态由 <see cref="IsExpanded"/> 记在节点上，
/// 页面 VM 只把**当前可见**的节点按先序塞进 <c>VisibleNodes</c>。这样整个行宽仍由列规格串决定 ⇒
/// 网址列 / 状态列与表头严格对齐（<c>TreeView</c> 的层级自动缩进会把整行右移，列必然错位）。
/// </para>
/// </summary>
public partial class BookmarkNode : ObservableObject
{
    /// <summary>节点类型。</summary>
    public BookmarkNodeKind Kind { get; init; }

    /// <summary>本级名称（文件夹名 / 书签标题的构建时快照）。</summary>
    public string Name { get; init; } = "";

    /// <summary>文件夹 = 自身全路径；书签 = 所属文件夹全路径（空 = 未分组）。</summary>
    public string Path { get; init; } = "";

    /// <summary>层级深度（根级 = 0），缩进 = <c>Depth × 16</c>。</summary>
    public int Depth { get; init; }

    /// <summary>文件夹含**子孙**的书签数（书签行恒 0）。</summary>
    public int Count { get; set; }

    /// <summary>书签行指向的源条目；文件夹行为 null。</summary>
    public BookmarkItem? Item { get; init; }

    /// <summary>子节点（书签行恒空）。</summary>
    public ObservableCollection<BookmarkNode> Children { get; } = new();

    /// <summary>展开态（一期不持久化；页面 VM 用内存集合记住用户折叠过的路径）。</summary>
    [ObservableProperty] private bool _isExpanded = true;

    public bool IsFolder => Kind == BookmarkNodeKind.Folder;

    public bool IsBookmark => Kind == BookmarkNodeKind.Bookmark;

    /// <summary>缩进占位宽（像素）：每级 16。</summary>
    public double IndentWidth => Depth * 16;

    /// <summary>行显示名（书签行跟随标题编辑，文件夹行用建树时的名称）。</summary>
    public string DisplayName =>
        IsFolder ? Name : (Item?.Title is { Length: > 0 } t ? t : Name);

    /// <summary>文件夹行的「(N)」条数；书签行为空串（不占位）。</summary>
    public string CountText => IsFolder ? $"({Count})" : "";

    /// <summary>行内网址列（文件夹行空）。</summary>
    public string Url => Item?.Url ?? "";

    /// <summary>行内状态列（文件夹行空）。</summary>
    public string? CheckStatus => Item?.CheckStatus;

    /// <summary>
    /// 折叠指示字符（**纯静态显示、不接收点击**，二次修订）：文件夹行 <c>▾</c> / <c>▸</c>，
    /// 书签行空串（保留固定宽度占位 ⇒ 同级名称不错位）。真正的折叠动作用**双击整行**。
    /// </summary>
    public string ChevronText => IsFolder ? (IsExpanded ? "▾" : "▸") : "";

    /// <summary>
    /// 源条目被就地改动（联通检测 / 标题编辑 / 批量标记）后刷新行 ——
    /// <see cref="BookmarkItem"/> 是普通类，不会自己发通知。
    /// </summary>
    public void NotifyChanged()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Url));
        OnPropertyChanged(nameof(CheckStatus));
    }

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ChevronText));
}

/// <summary>
/// 书签页「打开方式」下拉的一项：复用「设置 → 启动与行为」里已有的浏览器1 / 浏览器2，
/// ⛔ 不新建配置概念。<see cref="Config"/> 为 null = 系统默认浏览器。
/// </summary>
public sealed class OpenWithOption
{
    /// <summary>稳定序号（0 = 系统默认 / 1 = 浏览器1 / 2 = 浏览器2），重建选项时用它保住当前选择。</summary>
    public int Index { get; init; }

    /// <summary>下拉里显示的文字（ComboBox 默认走 <see cref="ToString"/>）。</summary>
    public string Label { get; init; } = "";

    /// <summary>对应的浏览器按钮配置；null = 系统默认浏览器。</summary>
    public LaunchButtonConfig? Config { get; init; }

    /// <summary>是否已配置程序路径（未配置 ⇒ 只能当「系统默认」用，动作置灰）。</summary>
    public bool IsConfigured => Config is null || !string.IsNullOrWhiteSpace(Config.ProgramPath);

    public override string ToString() => Label;
}
