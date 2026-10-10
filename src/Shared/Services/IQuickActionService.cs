using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NewToys.Shared.Models;

namespace NewToys.Shared.Services;

/// <summary>
/// 「电脑工具 → 设置与脚本」的存取：一堆 <see cref="QuickAction"/>（系统设置项 + 脚本）。
/// <para>
/// **唯一落点**：<c>&lt;应用根&gt;\config\quickactions.json</c>（默认 <c>%LocalAppData%\NewToys\config\</c>）。
/// 放 <c>config\</c> 的原因同 <see cref="IFavoritesService"/>：只有这里的 <c>*.json</c> 会被
/// 「设置 → 数据管理」的备份 / 导入覆盖。
/// </para>
/// <para>
/// 「收藏」不是单独一份清单，而是每条 <see cref="QuickAction.IsFavorite"/> 上的标记 —— 单一真相源：
/// 「设置与脚本」列表与「收藏夹」页读的是同一批对象，天然一致（见规范 3.9）。
/// </para>
/// </summary>
public interface IQuickActionService
{
    /// <summary>全部项目（含未收藏的），按文件里的顺序。</summary>
    IReadOnlyList<QuickAction> Items { get; }

    /// <summary>已收藏的项目。</summary>
    IReadOnlyList<QuickAction> Favorites { get; }

    /// <summary>配置文件全路径（页面上显示给用户看）。</summary>
    string FilePath { get; }

    /// <summary>新建一条（带默认标题），加入列表并落盘，返回它。</summary>
    QuickAction Add();

    /// <summary>把改动落盘（编辑属性后调用；属性本身就是同一个对象，不需要拷贝）。</summary>
    void Save();

    /// <summary>删除一条（内部走二次确认钩子；用户取消返回 <c>false</c>）。</summary>
    Task<bool> RemoveAsync(QuickAction item);

    /// <summary>切换收藏（没有则收藏、已有则取消）。</summary>
    void ToggleFavorite(QuickAction item);

    /// <summary>数据变化通知（列表 / 收藏夹刷新用）。</summary>
    event Action? Changed;
}
