using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

/// <summary>「脚本管理器 → 脚本库 / 命令库」的存取 + 执行。（WHY: SM-031）</summary>
public interface IQuickActionService
{
    /// <summary>全部项目（两页合计），顺序 = 脚本库在前、命令库在后。</summary>
    IReadOnlyList<QuickAction> Items { get; }

    /// <summary>「脚本库」页的项目（来自 <c>config\script\script.json</c>）。</summary>
    IReadOnlyList<QuickAction> Shortcuts { get; }

    /// <summary>「命令库」页的项目（来自 <c>config\script\script.json</c>）。</summary>
    IReadOnlyList<QuickAction> QuickSettings { get; }

    /// <summary>已收藏的项目（两页合计）。</summary>
    IReadOnlyList<QuickAction> Favorites { get; }

    /// <summary>「脚本库」的配置文件全路径（页面上显示给用户看）。</summary>
    string ScriptsFilePath { get; }

    /// <summary>「命令库」的配置文件全路径（页面上显示给用户看）。</summary>
    string QuickFilePath { get; }

    /// <summary>脚本目录（生效值）：「脚本库」页里的脚本文件都从这里找；配置为空时是 <c>&lt;应用根&gt;\scripts</c>。</summary>
    string ScriptsDirectory { get; }

    /// <summary>新建一条（带默认标题与默认类型），归入指定页面（<see cref="QuickActionPage"/>），加入列表并落盘，返回它。</summary>
    QuickAction Add(string page);

    /// <summary>把改动落盘（编辑属性后调用；属性本身就是同一个对象，不需要拷贝）。</summary>
    void Save();

    /// <summary>删除一条（内部走二次确认钩子；用户取消返回 <c>false</c>）。</summary>
    Task<bool> RemoveAsync(QuickAction item);

    /// <summary>
    /// 删除一条**但不弹确认**（从磁盘删除那一路 —— 确认框由调用方自己弹，
    /// ⛔ 否则"确认删文件 + 确认删条目"会出现两次，且第二次取消时文件已经没了）。
    /// </summary>
    bool RemoveSilent(QuickAction item);

    /// <summary>切换收藏（没有则收藏、已有则取消）。</summary>
    void ToggleFavorite(QuickAction item);

    /// <summary>把一条在两个页面之间移动（改归属 + 落盘）。</summary>
    void Move(QuickAction item, string page);

    /// <summary>导出某页**全条目**为 json 文本（<see cref="QuickActionPage.Quick"/> / <see cref="QuickActionPage.Shortcuts"/>；v3.19 ）。</summary>
    string ExportPage(string page);

    /// <summary>
    /// 导入某页条目（v3.19）：<paramref name="replace"/>=true 整页替换、false 合并（按 <see cref="QuickAction.Id"/> 去重，同 Id 覆盖）。
    /// </summary>
    /// <returns>(新增, 更新, 跳过/无效)；<c>Skipped=-1</c> 表示 json 解析失败。</returns>
    (int Added, int Updated, int Skipped) ImportPage(string page, string json, bool replace);

    /// <summary>清空某页**全部条目**并落盘（v3.19「清空本页」= 真删除，）。</summary>
    void ClearPage(string page);

    /// <summary>运行一条项目：命令行命令直接执行；脚本文件按扩展名 / QuickAction.Interpreter 挑解释器 （python / pwsh / powershell 5.1 / cmd，路径见「设置 → 路径配置」），统一经 cmd 跑；</summary>
    Task<(bool Ok, string Message)> RunAsync(QuickAction item, CancellationToken ct = default);

    /// <summary>数据变化通知（列表 / 收藏夹刷新用）。</summary>
    event Action? Changed;

    /// <summary>
    /// 从 <c>script.json</c> <b>重读</b>并重建两个库页的内存镜像，随后触发 <see cref="Changed"/>。
    /// <para>用于"别的页写了数据层"之后让库页跟上（集合页扫描入库、信息面板改列 schema 等）。</para>
    /// </summary>
    void Reload();

    /// <summary>
    /// <b>手动扫描</b>（§三.6）：把目录里的脚本列成候选（三态：新文件 / 已入库 / 不可采集）。
    /// <para>⛔ 只列不建 —— 入库动作由界面的「加入库」触发（用户拍板「用户自己扫描」）。</para>
    /// </summary>
    IReadOnlyList<ScriptCandidate> Scan(string directory, bool includeSubFolders);
}
