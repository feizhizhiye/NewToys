using System;
using System.Collections.Generic;
using System.Linq;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

/// <summary>视图级列投影：把「脚本的列能力」投影成「某个视图里这一节实际显示哪些列」。（WHY: SM-058）</summary>
public static class ViewColumnProjection
{
    private static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;

    private const StringComparison KeyComparison = StringComparison.OrdinalIgnoreCase;

    /// <summary>取（必要时新建）某节在当前视图下的覆盖记录。新建时按脚本默认可见性填 <c>Hidden</c>。</summary>
    public static SectionViewColumns Ensure(ScriptView view, ScriptItem item)
    {
        var set = Find(view, item.Id);
        if (set != null)
        {
            // 手改过 json 的防御（Normalize 会补，但这里再兜一次，避免切列时 NRE）。
            set.Hidden ??= new List<string>();
            set.Titles ??= new Dictionary<string, string>();
            set.Widths ??= new Dictionary<string, string>();
            return set;
        }

        set = new SectionViewColumns { ItemId = item.Id };
        foreach (var col in item.Columns ?? new List<ScriptColumn>())
            if (!col.Visible && !string.IsNullOrWhiteSpace(col.Key))
                set.Hidden.Add(col.Key);

        view.SectionColumns ??= new List<SectionViewColumns>();
        view.SectionColumns.Add(set);
        return set;
    }

    public static SectionViewColumns? Find(ScriptView? view, string itemId)
        => view?.SectionColumns?.FirstOrDefault(s => string.Equals(s.ItemId, itemId, StringComparison.Ordinal));

    /// <summary>
    /// 这一节在给定视图下的**有效列**（顺序跟随脚本 schema 的 <see cref="ScriptColumn.Order"/>）。
    /// </summary>
    public static List<ScriptColumn> Resolve(ScriptItem item, ScriptView? view)
    {
        var defaults = (item.Columns ?? new List<ScriptColumn>())
            .OrderBy(c => c.Order)
            .ToList();

        if (view == null || ScriptViewLayout.Normalize(view.Layout) != ScriptViewLayout.Sectioned)
            return defaults.Select(Clone).ToList();

        var set = Find(view, item.Id);
        var hiddenKeys = set?.Hidden;
        var titles = set?.Titles;
        var widths = set?.Widths;

        var result = new List<ScriptColumn>(defaults.Count);
        foreach (var col in defaults)
        {
            var visible = hiddenKeys != null
                ? !hiddenKeys.Contains(col.Key, KeyComparer)
                : col.Visible;

            var title = col.Title;
            if (titles != null && titles.TryGetValue(col.Key, out var t) && !string.IsNullOrWhiteSpace(t))
                title = t;

            var width = col.Width;
            if (widths != null && widths.TryGetValue(col.Key, out var w))
                width = w;

            result.Add(new ScriptColumn
            {
                Key = col.Key,
                Title = title,
                Visible = visible,
                Order = col.Order,
                Width = width,
            });
        }
        return result;
    }

    /// <summary>切换某列在当前视图下的显隐（写视图，⛔ 不动脚本 schema）。</summary>
    /// <returns>切换后的可见性；视图为空 / 布局不适用时返回 null（调用方不落盘）。</returns>
    public static bool? Toggle(ScriptItem item, ScriptView? view, string key)
    {
        if (view == null || string.IsNullOrWhiteSpace(key)) return null;
        if (ScriptViewLayout.Normalize(view.Layout) != ScriptViewLayout.Sectioned) return null;

        var set = Ensure(view, item);
        var hidden = set.Hidden.RemoveAll(k => string.Equals(k, key, KeyComparison)) > 0;
        if (!hidden) set.Hidden.Add(key);
        return hidden;
    }

    /// <summary>把节内各列宽度写进视图覆盖（保存设置时调）。</summary>
    public static void SetWidths(ScriptView? view, ScriptItem item, IEnumerable<KeyValuePair<string, string>> widths)
    {
        if (view == null || item == null) return;
        if (ScriptViewLayout.Normalize(view.Layout) != ScriptViewLayout.Sectioned) return;

        var set = Ensure(view, item);
        foreach (var kv in widths)
        {
            if (string.IsNullOrWhiteSpace(kv.Key)) continue;
            set.Widths[kv.Key] = kv.Value ?? "";
        }
    }

    /// <summary>
    /// 把节内各列标题写进视图覆盖（详情卡「表头名称」失焦时调）。
    /// <para>与脚本默认标题相同的项**不存**（保持 <c>Titles</c> 只放差异）。</para>
    /// </summary>
    public static void SetTitles(ScriptView? view, ScriptItem item, IEnumerable<KeyValuePair<string, string>> titles)
    {
        if (view == null || item == null) return;
        if (ScriptViewLayout.Normalize(view.Layout) != ScriptViewLayout.Sectioned) return;

        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in item.Columns ?? new List<ScriptColumn>())
            if (!string.IsNullOrWhiteSpace(col.Key))
                defaults[col.Key] = col.Title ?? "";

        var set = Ensure(view, item);
        set.Titles.Clear();

        foreach (var kv in titles)
        {
            if (string.IsNullOrWhiteSpace(kv.Key)) continue;

            var title = (kv.Value ?? "").Trim();
            if (title.Length == 0) continue;
            if (defaults.TryGetValue(kv.Key, out var d) && string.Equals(d, title, StringComparison.Ordinal)) continue;

            set.Titles[kv.Key] = title;
        }
    }

    /// <summary>新建视图 / 迁移时：按脚本默认可见性把整个集合的成员登记一遍。</summary>
    public static void InitializeForCollection(ScriptView view, IEnumerable<ScriptItem> members)
    {
        view.SectionColumns ??= new List<SectionViewColumns>();
        if (view.SectionColumns.Count > 0) return;

        foreach (var item in members)
            if (!string.IsNullOrWhiteSpace(item.Id))
                Ensure(view, item);
    }

    /// <summary>按视图的 <see cref="ScriptView.SectionOrder"/> 给成员排序（没登记过的排后面，保持原序）。</summary>
    public static List<ScriptItem> Order(IEnumerable<ScriptItem> members, ScriptView? view)
    {
        var list = members.ToList();
        var order = view?.SectionOrder;
        if (order == null || order.Count == 0) return list;

        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < order.Count; i++)
            if (!index.ContainsKey(order[i])) index[order[i]] = i;

        return list
            .Select((item, i) => (item, key: index.TryGetValue(item.Id, out var k) ? k : int.MaxValue, i))
            .OrderBy(t => t.key).ThenBy(t => t.i)
            .Select(t => t.item)
            .ToList();
    }

    /// <summary>上移 / 下移一节（<paramref name="delta"/> = -1 / +1）；越界不动。</summary>
    public static bool Move(ScriptView? view, IEnumerable<ScriptItem> members, string itemId, int delta)
    {
        if (view == null || string.IsNullOrWhiteSpace(itemId) || delta == 0) return false;

        var ids = Order(members, view).Select(i => i.Id).ToList();
        var at = ids.IndexOf(itemId);
        if (at < 0) return false;

        var to = at + delta;
        if (to < 0 || to >= ids.Count) return false;

        (ids[at], ids[to]) = (ids[to], ids[at]);
        view.SectionOrder = ids;
        return true;
    }

    private static ScriptColumn Clone(ScriptColumn col) => new()
    {
        Key = col.Key,
        Title = col.Title,
        Visible = col.Visible,
        Order = col.Order,
        Width = col.Width,
    };
}
