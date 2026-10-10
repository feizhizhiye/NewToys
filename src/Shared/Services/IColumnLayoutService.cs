using System.Collections.Generic;

namespace NewToys.Shared.Services;

/// <summary>
/// 列表页「列显隐」偏好的持久化契约（规范 3.8 的右键表头增删列）。
/// <para>
/// 落在 <c>config\columnlayout.json</c>（与应用其它设置同一目录，因此会被「设置 → 数据管理」一起备份）。
/// 存的是「页面 → 列键 → 是否可见」，与任何具体列表页解耦，新增列表页零改造即可复用。
/// </para>
/// </summary>
public interface IColumnLayoutService
{
    /// <summary>读取某页已保存的列可见性（只含被显式保存过的列；未保存的列由页内默认决定）。</summary>
    Dictionary<string, bool> LoadPage(string pageKey);

    /// <summary>保存某页的列可见性。</summary>
    void SavePage(string pageKey, Dictionary<string, bool> visibility);
}
