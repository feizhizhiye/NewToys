using System;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

/// <summary>
/// <c>config\script\script.json</c> 的读写契约（NT-068 §三.4）：<b>手工数据</b>（集合 / 条目 / 标签 / 视图）
/// 的<b>唯一写入点</b>。
/// </summary>
/// <remarks>
/// <para>
/// 🔴 落位由本服务决定，⛔ 不改 <c>Core</c> 的 <c>ConfigStoreService</c> / <c>IOptionsStore</c>：
/// 模块设置走 <c>config\script-manager.json</c>（<c>IOptionsStore.Validate</c> 禁路径分隔符，
/// 塞不进子目录），手工数据走本服务在 <c>config</c> 下自建的子目录 <c>script\</c>。
/// </para>
/// <para>
/// 🔴 <b>写入一律原子</b>（临时文件 + 替换），⛔ 不再出现"全量重写无保护"（旧 <c>QuickActionService.Persist</c> 的做法）。
/// </para>
/// </remarks>
public interface IScriptStoreService
{
    /// <summary>手工数据目录 = <c>&lt;配置根&gt;\config\script</c>（含 <see cref="FilePath"/>）。</summary>
    string ConfigDir { get; }

    /// <summary>观测缓存目录 = <c>&lt;配置根&gt;\data\script</c>（含 <see cref="DatabasePath"/>）。</summary>
    string DataDir { get; }

    /// <summary>手工数据文件全路径 = <c>config\script\script.json</c>。</summary>
    string FilePath { get; }

    /// <summary>观测数据库全路径 = <c>data\script\script.db</c>。</summary>
    string DatabasePath { get; }

    /// <summary>已加载的根结构（进程内唯一实例；改动后由调用方调 <see cref="Save"/>）。</summary>
    ScriptStoreRoot Data { get; }

    /// <summary>是否成功加载（false = 文件坏了 / 读失败，界面按空数据处理并给状态栏提示）。</summary>
    bool IsLoaded { get; }

    /// <summary>上次加载 / 保存的错误描述（空 = 无错误）。</summary>
    string LastError { get; }

    /// <summary>数据已变更（集合 / 条目增删改后触发，供界面刷新）。</summary>
    event Action? Changed;

    /// <summary>重新从磁盘加载（坏文件不抛，只置 <see cref="LastError"/>）。</summary>
    void Reload();

    /// <summary>原子写回磁盘并触发 <see cref="Changed"/>。</summary>
    void Save();

    /// <summary>让后续 <see cref="Save"/> 触发 <see cref="Changed"/>（调用方只改字段、不改结构时用）。</summary>
    void NotifyChanged();

    // ---------- 便捷查询（避免各页各拼一份） ----------

    /// <summary>按 id 找集合。</summary>
    ScriptCollection? FindCollection(string id);

    /// <summary>按 id 找条目。</summary>
    ScriptItem? FindItem(string id);

    /// <summary>某集合的成员条目（按 <see cref="ScriptItem.Collections"/> 反查，保持 <see cref="ScriptStoreRoot.Items"/> 顺序）。</summary>
    System.Collections.Generic.IReadOnlyList<ScriptItem> ItemsOf(string collectionId);

    /// <summary>把条目加入 / 移出集合（即时生效，调用方自行 <see cref="Save"/>）。</summary>
    bool SetMembership(string itemId, string collectionId, bool member);
}
