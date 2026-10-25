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

// v3.66：Core 里也有一个 FileOperationService（纯磁盘动作，跨模块共用）。
// 本页用的是**带库表日志**的模块版，显式取别名，免得两个 using 撞名（CS0104）。
using FileOperationService = NewToys.MediaAssets.Services.FileOperationService;

namespace NewToys.MediaAssets.ViewModels;

public partial class AssetAllViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    // ==================== 过滤 / 排序 ====================

    private void ApplyView()
    {
        // 草稿态（扫描预览）走内存路径；库态 _all 即当前页（SQL 已筛/排过分）⇒ 直接渲染
        if (IsDraft) { ApplyViewMemory(); return; }

        Items.Clear();
        foreach (var r in _all) Items.Add(r);

        // 翻页 / 重取后选中项落到页内第一条（验收标准②；⛔ 不留在已不可见的旧行上）。
        // 同一资产换了对象实例（重取）⇒ 按 Id 认领，保详情卡不跳走；真不在本页才落到第一条。
        if (Selected != null && !Items.Contains(Selected))
            Selected = Items.FirstOrDefault(r => r.Id == Selected.Id)
                       ?? (Items.Count > 0 ? Items[0] : null);

        UpdateHeaders();
        RefreshChecked();                          // 列表重建 → 勾选计数 / 表头三态跟上
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
    }

    /// <summary>草稿态专用：筛选 + 排序全在内存做（数据不在库里，⛔ 无分页）。</summary>
    private void ApplyViewMemory()
    {
        IEnumerable<AssetRow> q = _all;

        var text = (SearchText ?? "").Trim();
        if (text.Length > 0)
            q = q.Where(r => r.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                             || r.FilePath.Contains(text, StringComparison.OrdinalIgnoreCase)
                             || r.TagsText.Contains(text, StringComparison.OrdinalIgnoreCase));

        var type = FilterType?.Value ?? "";
        if (type.Length > 0) q = q.Where(r => string.Equals(r.Type, type, StringComparison.Ordinal));

        var status = FilterStatus?.Value ?? "";
        if (status.Length > 0) q = q.Where(r => string.Equals(r.Status, status, StringComparison.Ordinal));

        var storage = FilterStorage?.Value ?? "";
        if (storage.Length > 0)
            q = q.Where(r => r.StorageId.ToString(CultureInfo.InvariantCulture) == storage);

        var tagId = FilterTag?.Value ?? "";
        if (tagId.Length > 0)
        {
            // 标签是"逗号分隔串里含某一个" → 用下拉里显示的**名字**去 Contains（⛔ 别拿 id 比文本）
            var tagName = TagOptions.FirstOrDefault(o => o.Value == tagId)?.Label ?? "";
            if (tagName.Length > 0)
                q = q.Where(r => r.TagsText.Split(',', StringSplitOptions.TrimEntries)
                                           .Contains(tagName, StringComparer.OrdinalIgnoreCase));
        }

        // 表头右键的多项筛选（基类）：多条之间取「且」
        q = ApplyFilters(q);

        q = SortColumn switch
        {
            "Type" => Order(q, r => r.Type),
            "Tags" => Order(q, r => r.TagsText),
            "Categories" => Order(q, r => r.CategoriesText),
            "Collections" => Order(q, r => r.CollectionsText),
            "Size" => Order(q, r => r.Size),
            "Modified" => Order(q, r => r.Mtime ?? ""),
            "Storage" => Order(q, r => r.StorageName),
            "Duration" => Order(q, r => r.DurationSeconds),
            "Resolution" => Order(q, r => r.ResolutionText),
            "Codec" => Order(q, r => r.CodecText),
            _ => Order(q, r => r.Name)
        };

        var ordered = q.ToList();

        // 🔴 （2026-10-16 用户定：草稿态跟随分页）：**内存侧** Skip/Take 切片
        //    —— 上面的筛选 / 排序已经做完，这里切出来的就是「当前页」。
        //    ⛔ TotalCount 必须是**筛选后**的总数（不能用 Items.Count = 当页行数，否则「共 N 页」永远是 1）。
        //    PageSize = AllPageSize（下拉选「全部」）⇒ 不分片，等于不分页。
        var total = ordered.Count;
        TotalCount = total;

        // 页码越界自动收敛（删行 / 收紧筛选后 TotalCount 变小才会碰上）⇒ 直接 return，setter 会带着新页码再跑一遍
        var pages = total <= 0 ? 0
            : PageSize >= ListPageViewModelBase.AllPageSize ? 1
            : (total - 1) / PageSize + 1;
        if (pages > 0 && PageIndex > pages) { PageIndex = pages; return; }

        Items.Clear();
        if (PageSize >= ListPageViewModelBase.AllPageSize || PageSize >= total)
        {
            foreach (var r in ordered) Items.Add(r);
        }
        else
        {
            // PageSize 走到这里必然是有限值 ⇒ skip 不会溢出（≤ total）
            foreach (var r in ordered.Skip((PageIndex - 1) * PageSize).Take(PageSize)) Items.Add(r);
        }

        if (Selected != null && !Items.Contains(Selected)) Selected = null;
        UpdateHeaders();
        RefreshChecked();                          // 列表重建 → 勾选计数 / 表头三态跟上
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasPaging));       // 切片后页数变了 ⇒ 底栏跳转链跟着出现/消失
    }

    private IOrderedEnumerable<AssetRow> Order<TKey>(IEnumerable<AssetRow> q, Func<AssetRow, TKey> key)
        => SortDescending ? q.OrderByDescending(key) : q.OrderBy(key);

    private void UpdateHeaders()
    {
        NameHeader = "名称" + SortArrow("Name");
        TypeHeader = "类型" + SortArrow("Type");
        TagsHeader = "标签" + SortArrow("Tags");
        CategoriesHeader = "分类" + SortArrow("Categories");
        CollectionsHeader = "集合" + SortArrow("Collections");
        SizeHeader = "大小" + SortArrow("Size");
        ModifiedHeader = "修改时间" + SortArrow("Modified");
        StorageHeader = "存储源" + SortArrow("Storage");
        DurationHeader = "时长" + SortArrow("Duration");
        ResolutionHeader = "分辨率" + SortArrow("Resolution");
        CodecHeader = "编码" + SortArrow("Codec");
    }

}
