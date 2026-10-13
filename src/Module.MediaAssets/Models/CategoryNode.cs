using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.MediaAssets.Models;

/// <summary>
/// 分类管理页的一个节点（NT-018）：`categories` + 成员数。
/// <para>🔴 分类页是**树形页**（本模块唯一非列表页）：左边自绘树、右边成员列表。
/// 树按 <see cref="Depth"/> **扁平化**后交给 `ItemsControl`（⛔ 不用递归模板、⛔ 不用 `TreeView`，规范 §2.1）。</para>
/// </summary>
public partial class CategoryNode : ObservableObject
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public long? ParentId { get; set; }
    public int SortOrder { get; set; }
    public int MemberCount { get; set; }

    /// <summary>树深度（0 = 根）；界面按它换算左缩进（<see cref="Indent"/>）。</summary>
    public int Depth { get; set; }

    /// <summary>子节点数（>0 才画展开 / 折叠箭头）。</summary>
    public int ChildCount { get; set; }

    /// <summary>当前选中（左树高亮；`Classes.selected` 绑它）。</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>当前展开（有子节点时才有效）。</summary>
    [ObservableProperty] private bool _isExpanded = true;

    public bool HasChildren => ChildCount > 0;

    /// <summary>展开箭头（自绘，⛔ 不用 TreeView 的三角）。</summary>
    public string Arrow => !HasChildren ? "　" : (IsExpanded ? "▾" : "▸");

    /// <summary>缩进（每层 14px）。</summary>
    public Thickness Indent => new(Depth * 14, 0, 0, 0);

    /// <summary>成员数文案。</summary>
    public string MemberText => MemberCount == 0 ? "（无成员）" : $"{MemberCount} 个";

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(Arrow));
}

/// <summary>
/// 「左树 + 右成员列表」这类页面里**成员列表**的一行 = 资产行（分类页 / 集合页共用）。
/// </summary>
public partial class AssetMember : ObservableObject
{
    public AssetMember(AssetRow row) => Row = row;

    public AssetRow Row { get; }

    [ObservableProperty] private bool _isChecked;

    public long Id => Row.Id;
    public string Name => Row.Name;
    public string TypeText => Row.TypeText;
    public string SizeText => Row.SizeText;
    public string ModifiedText => Row.ModifiedText;
    public string StorageName => Row.StorageName;
}
