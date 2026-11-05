using System.Collections.Generic;

namespace NewToys.Shared.Services;

/// <summary>列表页「布局偏好」的持久化契约（规范 3.8 的右键表头增删列 + 详情栏开合）。（WHY: SH-034）</summary>
public interface IColumnLayoutService
{
    /// <summary>页面级开关名：「右侧详情栏是否展开」（各列表页共用此名字，取值互相独立）。</summary>
    const string FlagDetailPanel = "detail";

    /// <summary>读取某页已保存的列可见性（只含被显式保存过的列；未保存的列由页内默认决定）。</summary>
    Dictionary<string, bool> LoadPage(string pageKey);

    /// <summary>保存某页的列可见性。</summary>
    void SavePage(string pageKey, Dictionary<string, bool> visibility);

    /// <summary>读取某页的一个页面级开关（如「详情栏是否展开」）；从未保存过则返回 <paramref name="defaultValue"/>。</summary>
    bool LoadFlag(string pageKey, string name, bool defaultValue = false);

    /// <summary>保存某页的一个页面级开关（不动该页的列显隐）。</summary>
    void SaveFlag(string pageKey, string name, bool value);
}
