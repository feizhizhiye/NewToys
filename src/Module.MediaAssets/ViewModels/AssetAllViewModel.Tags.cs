using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.MediaAssets.Models;
using NewToys.MediaAssets.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

// Core 里也有一个 FileOperationService（纯磁盘动作，跨模块共用）。
// 本页用的是**带库表日志**的模块版，显式取别名，免得两个 using 撞名（CS0104）。
using FileOperationService = NewToys.MediaAssets.Services.FileOperationService;

namespace NewToys.MediaAssets.ViewModels;

public partial class AssetAllViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    /// <summary>工具条「🏷 加标签」：给**勾选的行**打一个标签（输入标签名，不存在就建）。</summary>
    [RelayCommand]
    private async Task AddTagToCheckedAsync()
    {
        var ids = _all.Where(r => r.IsChecked).Select(r => r.Id).ToList();
        if (ids.Count == 0) { StatusText = "先在左边勾选要打标签的资产。"; return; }

        var name = await DialogHooks.InputAsync($"给勾选的 {ids.Count} 个资产打什么标签？", "").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name)) { StatusText = "已取消。"; return; }

        var tagId = _tagSvc.Ensure(name.Trim());
        if (tagId <= 0) { StatusText = "打标签失败（库不可用？）。"; return; }

        var n = _tagSvc.AddTagToAssets(ids, tagId);
        _opLog.Log("tag", "tag", tagId, afterJson: $"{{\"assets\":{ids.Count},\"added\":{n},\"name\":\"{name.Trim()}\"}}");
        Reload();
        StatusText = $"已给 {n} 个资产打上标签「{name.Trim()}」（原本已有该标签的不重复）。";
    }

    /// <summary>工具条「📚 加集合」：把**勾选的行**加进一个集合（输入集合名，不存在就建一个手动集合）。</summary>
    [RelayCommand]
    private async Task AddToCollectionCheckedAsync()
    {
        var ids = _all.Where(r => r.IsChecked).Select(r => r.Id).ToList();
        if (ids.Count == 0) { StatusText = "先在左边勾选要加入集合的资产。"; return; }

        var name = await DialogHooks.InputAsync($"把勾选的 {ids.Count} 个资产加进哪个集合？（没有就新建）", "").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name)) { StatusText = "已取消。"; return; }

        var hit = _colSvc.List().FirstOrDefault(c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        var colId = hit?.Id ?? _colSvc.Create(name.Trim(), CollectionTypes.Manual);
        if (colId <= 0) { StatusText = "加入集合失败（库不可用？）。"; return; }
        if (hit != null && hit.IsSmart) { StatusText = $"「{hit.Name}」是智能集合，成员由规则决定，⛔ 不能手工加。"; return; }

        var n = _colSvc.AddAssets(colId, ids);
        _opLog.Log("collection", "collection", colId, afterJson: $"{{\"assets\":{ids.Count},\"added\":{n},\"name\":\"{name.Trim()}\"}}");
        Reload();
        StatusText = hit == null
            ? $"已新建手动集合「{name.Trim()}」并加入 {n} 个资产。"
            : $"已把 {n} 个资产加进集合「{name.Trim()}」。";
    }

    // ==================== v3.34：详情卡编辑（显示名 / 评分 / 描述 / 标签 / 分类 / 集合） ====================

    /// <summary>详情卡的标签色片（点一下即选；「💾 保存」统一落库）。</summary>
    public ObservableCollection<PickItem> TagPicks { get; } = new();

    /// <summary>详情卡的分类色片。</summary>
    public ObservableCollection<PickItem> CategoryPicks { get; } = new();

    /// <summary>详情卡的集合色片。</summary>
    public ObservableCollection<PickItem> CollectionPicks { get; } = new();

    // ===== 评分改成「一排 1~5 小按钮」（点一下即生效并落盘，与图标组同一口径） =====

    /// <summary>评分可点按钮（1~5；<see cref="PickItem.Id"/> 就是分值）。</summary>
    public ObservableCollection<PickItem> RatingPicks { get; } = new();

    /// <summary>评分按钮的分值（与 <see cref="RatingPicks"/> 一一对应）。</summary>
    private static readonly int[] RatingValues = { 1, 2, 3, 4, 5 };

    /// <summary>把 <see cref="RatingPicks"/> 的选中态同步成当前评分（点选 / 点赞 / 点踩 / 换选中项后都要调）。</summary>
    private void SyncRatingPicks(int rating)
    {
        for (var i = 0; i < RatingPicks.Count; i++) RatingPicks[i].IsSelected = RatingValues[i] == rating;
    }

    [ObservableProperty] private int _editRating;

    // ===== 状态改**单选下拉**（预设三档；索引 ↔ 状态 key 的映射只在这里） =====

    /// <summary>状态下拉的显示文案（顺序 = <see cref="StatusKeys"/>）。</summary>
    public string[] StatusChoices { get; } = { "正常", "已归档", "文件缺失" };

    /// <summary>状态下拉的实际值（存库；<see cref="AssetStatuses"/>）。</summary>
    private static readonly string[] StatusKeys =
        { AssetStatuses.Active, AssetStatuses.Archived, AssetStatuses.Missing };

    [ObservableProperty] private int _editStatusIndex;
    [ObservableProperty] private string _editDescription = "";

    /// <summary>该资产的关联（只读列出；增删归后续——本轮只在详情卡展示）。</summary>
    public ObservableCollection<Relation> Relations { get; } = new();

    public bool HasRelations => Relations.Count > 0;

    /// <summary>三种色片有没有内容（详情卡给"还没有…"提示用）。</summary>
    public bool HasTagPicks => TagPicks.Count > 0;
    public bool HasCategoryPicks => CategoryPicks.Count > 0;
    public bool HasCollectionPicks => CollectionPicks.Count > 0;

    /// <summary>点一下色片 = 打上 / 取消（⛔ 不立即落库，等「💾 保存」）。</summary>
    [RelayCommand]
    private void ToggleTag(PickItem? item) { if (item != null) item.IsSelected = !item.IsSelected; }

    [RelayCommand]
    private void ToggleCategory(PickItem? item) { if (item != null) item.IsSelected = !item.IsSelected; }

    [RelayCommand]
    private void ToggleCollection(PickItem? item) { if (item != null) item.IsSelected = !item.IsSelected; }

    // ===== 详情卡**图标动作组**（收藏 / 评分 ±1 / 保存 / 刷新 / 删除） =====
    // 🔴 与「💾 保存」的分工：收藏 / 评分是**单项即时生效即落盘**（点一下就写库，⛔ 不用再点保存）；
    //    显示名 / 描述 / 标签 / 分类 / 集合仍是**编辑态 + 显式保存**（多字段表单）。

    /// <summary>「★ 收藏」：切换收藏（即时落盘）。</summary>
    [RelayCommand]
    private void ToggleFavorite()
    {
        if (Selected == null) { StatusText = "先在左侧选中一个资产。"; return; }
        var next = !Selected.IsFavorite;
        if (!_assets.SetFavorite(Selected.Id, next)) { StatusText = "写库失败（资产库不可用）。"; return; }
        Selected.IsFavorite = next;
        OnPropertyChanged(nameof(DetailRow));   // 图标 .active 态跟着变（渲染器订阅的是页面 VM）
        StatusText = next ? $"已收藏「{Selected.Name}」。★" : $"已取消收藏「{Selected.Name}」。";
    }

    /// <summary>「👍 点赞」：评分 +1（0~5 封顶，即时落盘）。</summary>
    [RelayCommand]
    private void RateUp() => ChangeRating(1);

    /// <summary>「👎 点踩」：评分 -1（0~5 封底，即时落盘）。</summary>
    [RelayCommand]
    private void RateDown() => ChangeRating(-1);

    /// <summary>「1~5 评分按钮」：直接设成这个值（即时落盘）。</summary>
    [RelayCommand]
    private void SetRating(PickItem? item)
    {
        if (item == null) return;
        ChangeRating(0, (int)item.Id);
    }

    private void ChangeRating(int delta, int? absolute = null)
    {
        if (Selected == null) { StatusText = "先在左侧选中一个资产。"; return; }
        var next = Math.Clamp(absolute ?? Selected.Rating + delta, 0, 5);
        if (next == Selected.Rating)
        {
            StatusText = absolute.HasValue
                ? $"当前就是 {next} 分。"
                : delta > 0 ? "已经是最高分 5 了。" : "已经是最低分 0 了。";
            return;
        }
        // ⚠️ 描述一起写是这个方法的要求 ⇒ 传**库里的现值**（不是编辑态 EditDescription），
        //    否则会把用户还没保存的草稿覆盖掉 / 或把已保存的描述清掉。
        if (!_assets.SetRatingAndDescription(Selected.Id, next, Selected.Description))
        { StatusText = "写库失败（资产库不可用）。"; return; }
        Selected.Rating = next;
        EditRating = next;                      // 详情卡编辑态同步
        SyncRatingPicks(next);                  // 1~5 按钮的选中态同步
        OnPropertyChanged(nameof(DetailRow));
        StatusText = $"评分 → {next}（已写入资产库）。";
    }

    private void BuildPicks(AssetRow target)
    {
        TagPicks.Clear();
        CategoryPicks.Clear();
        CollectionPicks.Clear();
        Relations.Clear();

        // 评分按钮（1~5）：**固定五项**，选中态 = 当前评分（⛔ 不用色块 → Brush 给透明）
        RatingPicks.Clear();
        for (var i = 0; i < RatingValues.Length; i++)
            RatingPicks.Add(new PickItem(RatingValues[i], $"{RatingValues[i]}", Brushes.Transparent, target.Rating == RatingValues[i]));

        if (target.Id <= 0) return;   // 未入库（Id<=0）：不给色片

        var tagIds = _tagSvc.ListTagIdsOfAsset(target.Id);
        // 标签**按命名空间分组**显示 ⇒ 先按 ns 排序（分组靠"键连续"，乱序会拆出很多组）
        foreach (var t in _tagSvc.List().OrderBy(t => t.Namespace ?? "").ThenBy(t => t.Name, StringComparer.CurrentCulture))
            TagPicks.Add(new PickItem(t.Id, t.Name, t.ChipBrush, tagIds.Contains(t.Id), t.Namespace));

        var catIds = _catSvc.ListCategoryIdsOfAsset(target.Id);
        foreach (var c in _catSvc.List())
            CategoryPicks.Add(new PickItem(c.Id, c.Name, new SolidColorBrush(Color.Parse(AssetPalette.For(c.Name))), catIds.Contains(c.Id)));

        var colIds = _colSvc.ListCollectionIdsOfAsset(target.Id);
        foreach (var c in _colSvc.List())
            CollectionPicks.Add(new PickItem(c.Id, c.Name, new SolidColorBrush(Color.Parse(AssetPalette.For(c.Name))), colIds.Contains(c.Id)));

        foreach (var rel in _relationSvc.ListFor(target.Id)) Relations.Add(rel);
        OnPropertyChanged(nameof(HasRelations));
        OnPropertyChanged(nameof(HasTagPicks));
        OnPropertyChanged(nameof(HasCategoryPicks));
        OnPropertyChanged(nameof(HasCollectionPicks));
    }

}
