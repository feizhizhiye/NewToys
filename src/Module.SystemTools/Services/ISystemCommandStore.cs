using System.Collections.Generic;
using NewToys.SystemTools.Models;

namespace NewToys.SystemTools.Services;

/// <summary>
/// 系统工具**命令条目**的存储：<c>config\system\commands.json</c>，本模块唯一手工数据落点。
/// <para>⛔ 不开 DB：system 没有观测数据（需求 §6.7：不做采集 / 间隔刷新）。</para>
/// </summary>
public interface ISystemCommandStore
{
    /// <summary>全部条目（按内存顺序）。</summary>
    IReadOnlyList<SystemCommand> Items { get; }

    /// <summary>数据文件全路径（页面显示给用户看）。</summary>
    string FilePath { get; }

    /// <summary>新增一条（带默认标题），返回它。</summary>
    SystemCommand Add(string title);

    /// <summary>按原样新增一条（播种器用：保留调用方给的 <see cref="SystemCommand.Id"/>；⛔ 不做判重，由调用方保证）。</summary>
    SystemCommand Insert(SystemCommand item);

    /// <summary>删除一条。</summary>
    bool Remove(SystemCommand item);

    /// <summary>落盘（原子写）。</summary>
    void Save();

    /// <summary>重读磁盘（外部改了文件 / 导入之后）。</summary>
    void Reload();

    /// <summary>
    /// 合并导入（json 清单）：按 <see cref="SystemCommand.Id"/> 判重 —— 已有的更新、没有的加入。
    /// </summary>
    /// <returns>（新增数, 更新数）。</returns>
    (int Added, int Updated) Merge(IEnumerable<SystemCommand> incoming);
}
