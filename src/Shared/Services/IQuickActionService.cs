using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NewToys.Shared.Models;

namespace NewToys.Shared.Services;

/// <summary>
/// 「电脑工具 → 设置与脚本 / 快捷设置」的存取 + 执行：一堆 <see cref="QuickAction"/>（系统设置项 + 脚本）。
/// <para>
/// **唯一落点**：<c>&lt;应用根&gt;\config\quickactions.json</c>（默认 <c>%LocalAppData%\NewToys\config\</c>）。
/// 放 <c>config\</c> 的原因同 <see cref="IFavoritesService"/>：只有这里的 <c>*.json</c> 会被
/// 「设置 → 数据管理」的备份 / 导入覆盖。
/// </para>
/// <para>
/// 两页（设置与脚本 / 快捷设置）读的是<b>同一份清单</b>，只按 <see cref="QuickAction.Page"/> 分栏显示 ——
/// 「收藏」同样不是单独一份清单，而是每条 <see cref="QuickAction.IsFavorite"/> 上的标记（单一真相源），
/// 因此「收藏夹」页天然汇总两页的收藏项。
/// </para>
/// </summary>
public interface IQuickActionService
{
    /// <summary>全部项目（两页合计，含未收藏的），按文件里的顺序。</summary>
    IReadOnlyList<QuickAction> Items { get; }

    /// <summary>「设置与脚本」页的项目（<see cref="QuickActionPage.Shortcuts"/>）。</summary>
    IReadOnlyList<QuickAction> Shortcuts { get; }

    /// <summary>「快捷设置」页的项目（<see cref="QuickActionPage.Quick"/>）。</summary>
    IReadOnlyList<QuickAction> QuickSettings { get; }

    /// <summary>已收藏的项目（两页合计）。</summary>
    IReadOnlyList<QuickAction> Favorites { get; }

    /// <summary>配置文件全路径（页面上显示给用户看）。</summary>
    string FilePath { get; }

    /// <summary>脚本目录（生效值）：「设置与脚本」页里的脚本文件都从这里找；配置为空时是 <c>&lt;应用根&gt;\scripts</c>。</summary>
    string ScriptsDirectory { get; }

    /// <summary>新建一条（带默认标题与默认类型），归入指定页面（<see cref="QuickActionPage"/>），加入列表并落盘，返回它。</summary>
    QuickAction Add(string page);

    /// <summary>把改动落盘（编辑属性后调用；属性本身就是同一个对象，不需要拷贝）。</summary>
    void Save();

    /// <summary>删除一条（内部走二次确认钩子；用户取消返回 <c>false</c>）。</summary>
    Task<bool> RemoveAsync(QuickAction item);

    /// <summary>切换收藏（没有则收藏、已有则取消）。</summary>
    void ToggleFavorite(QuickAction item);

    /// <summary>
    /// 运行一条项目：命令行命令直接执行；脚本文件按扩展名 / <see cref="QuickAction.Interpreter"/> 挑解释器
    /// （python / pwsh / powershell 5.1 / cmd，路径见「设置 → 路径配置」），统一经 <c>cmd</c> 跑；
    /// <see cref="QuickAction.NeedAdmin"/> 为真时用 UAC 提权（提权后新开一个控制台窗口，输出直接可见）。
    /// <para>普通情况下隐藏窗口并捕获输出，返回「是否成功 + 可读的结果 / 失败原因」，供页面状态行显示。</para>
    /// </summary>
    Task<(bool Ok, string Message)> RunAsync(QuickAction item, CancellationToken ct = default);

    /// <summary>数据变化通知（列表 / 收藏夹刷新用）。</summary>
    event Action? Changed;
}
