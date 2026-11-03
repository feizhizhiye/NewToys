using System;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.MediaAssets.Models;

/// <summary>
/// 分类管理页的一行：`categories` + 成员数（与标签 / 集合页同构的**列表页**）。
/// <para>分类是树（`parent_id`）：列表用 <see cref="Depth"/> 缩进 + <see cref="PathText"/> 路径列表达层级，
/// 改挂父级走详情卡的「父分类」chips（<c>CategoryService.Move</c> 自带防环）。</para>
/// </summary>
public class CategoryNode
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public long? ParentId { get; set; }
    public int SortOrder { get; set; }
    public int MemberCount { get; set; }
    public string CreatedAt { get; set; } = "";

    /// <summary>层级深度（0 = 根）；列表首列的缩进按它算。</summary>
    public int Depth { get; set; }

    /// <summary>完整路径（VM 按 <see cref="ParentId"/> 链拼，如 <c>影视 / 电影 / 动作</c>）。</summary>
    public string FullPath { get; set; } = "";

    // ===== 界面派生（⛔ 不落库） =====

    public Thickness Indent => new(Depth * 12, 0, 0, 0);

    public string MemberText => MemberCount == 0 ? "（无成员）" : $"{MemberCount} 个";

    public string PathText => string.IsNullOrWhiteSpace(FullPath) ? Name : FullPath;

    public string SortText => SortOrder.ToString();

    public string CreatedText
        => DateTime.TryParse(CreatedAt, out var dt) ? dt.ToString("yyyy-MM-dd HH:mm") : CreatedAt;
}

/// <summary>
/// 组织维度（分类 / 集合）详情卡里的成员列表行 = 资产行。
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
