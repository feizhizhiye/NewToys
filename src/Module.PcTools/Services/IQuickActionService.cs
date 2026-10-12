using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NewToys.PcTools.Models;

namespace NewToys.PcTools.Services;

/// <summary>
/// 「电脑工具 → 设置与脚本 / 快捷设置」的存取 + 执行。
/// <para>
/// <b>两个页面 = 两个配置文件</b>（v1.34 拆分，单一职责、便于手改、不易因格式出错）：
/// <list type="bullet">
///   <item>「设置与脚本」→ <c>&lt;应用根&gt;\config\pc-scripts.json</c>：**有脚本文件的脚本**（.py / .ps1 / .bat）。</item>
///   <item>「快捷设置」→ <c>&lt;应用根&gt;\config\pc-quick.json</c>：**命令行命令**（<c>ms-settings:</c> / <c>reg</c> / <c>ipconfig</c>…）。</item>
/// </list>
/// 两个文件同属 <c>config\</c>，会被「设置 → 数据管理」一起备份 / 导入。
/// </para>
/// <para>
/// <b>归属铁律</b>：「设置与脚本」只放**有脚本文件的脚本**；只要没指定脚本文件（也就不可能跑起来），
/// 一律归「快捷设置」。服务在加载时做一次归一化，各页面保存时也会自动把不合规的条目移过去。
/// </para>
/// <para>
/// 「收藏」不是第三份清单：它就是每条 <see cref="QuickAction.IsFavorite"/> 上的标记（单一真相源），
/// 因此「收藏夹」页天然汇总两页的收藏项。
/// </para>
/// </summary>
public interface IQuickActionService
{
    /// <summary>全部项目（两页合计），顺序 = 设置与脚本在前、快捷设置在后。</summary>
    IReadOnlyList<QuickAction> Items { get; }

    /// <summary>「设置与脚本」页的项目（来自 <c>config\pc-scripts.json</c>）。</summary>
    IReadOnlyList<QuickAction> Shortcuts { get; }

    /// <summary>「快捷设置」页的项目（来自 <c>config\pc-quick.json</c>）。</summary>
    IReadOnlyList<QuickAction> QuickSettings { get; }

    /// <summary>已收藏的项目（两页合计）。</summary>
    IReadOnlyList<QuickAction> Favorites { get; }

    /// <summary>「设置与脚本」的配置文件全路径（页面上显示给用户看）。</summary>
    string ScriptsFilePath { get; }

    /// <summary>「快捷设置」的配置文件全路径（页面上显示给用户看）。</summary>
    string QuickFilePath { get; }

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

    /// <summary>把一条在两个页面之间移动（改归属 + 落盘）。</summary>
    void Move(QuickAction item, string page);

    /// <summary>导出某页**全条目**为 json 文本（<see cref="QuickActionPage.Quick"/> / <see cref="QuickActionPage.Shortcuts"/>；v3.19 NT-006/007）。</summary>
    string ExportPage(string page);

    /// <summary>
    /// 导入某页条目（v3.19）：<paramref name="replace"/>=true 整页替换、false 合并（按 <see cref="QuickAction.Id"/> 去重，同 Id 覆盖）。
    /// </summary>
    /// <returns>(新增, 更新, 跳过/无效)；<c>Skipped=-1</c> 表示 json 解析失败。</returns>
    (int Added, int Updated, int Skipped) ImportPage(string page, string json, bool replace);

    /// <summary>清空某页**全部条目**并落盘（v3.19「🧹 清空本页」= 真删除，NT-006）。</summary>
    void ClearPage(string page);

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
