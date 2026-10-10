using System.Collections.Generic;

namespace NewToys.Shared.Services;

/// <summary>
/// 列表页「布局偏好」的持久化契约（规范 3.8 的右键表头增删列 + 详情栏开合）。
/// <para>
/// 落在 <c>config\columnlayout.json</c>（与应用其它设置同一目录，因此会被「设置 → 数据管理」一起备份）。
/// 存的是「页面 → 键 → 布尔值」，与任何具体列表页解耦，新增列表页零改造即可复用。
/// </para>
/// <para>
/// ⚠️ 同一个文件里存两类东西：① **列显隐**（<see cref="LoadPage"/> / <see cref="SavePage"/>，键 = 列键）；
/// ② **面板开合**等页面级开关（<see cref="LoadFlag"/> / <see cref="SaveFlag"/>，内部用保留前缀避免撞键）。
/// </para>
/// </summary>
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
