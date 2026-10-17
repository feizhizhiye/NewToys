using System;
using System.Collections.Generic;

namespace NewToys.Shared.Services;

/// <summary>
/// 「首页 → 常用功能（收藏夹）」的存取。
/// <para>
/// **唯一落点**：<c>&lt;应用根&gt;\config\favorites.json</c>（默认 <c>%LocalAppData%\NewToys\config\</c>）。
/// 之所以必须是 <c>config\</c> 下：① 只有这里的 <c>*.json</c> 会被「设置 → 数据管理」的备份 / 导入覆盖；
/// ② 首页与「设置 → 功能清单」都只通过本服务读写，不各自保存一份，避免双真相源。
/// </para>
/// <para>
/// 收藏项只存**功能 key**（如 <c>script-info</c>），标题 / 图标 / 简介一律到 <see cref="IMenuService"/> 现取，
/// 这样改名后首页卡片自动跟着变。
/// </para>
/// </summary>
public interface IFavoritesService
{
    /// <summary>已固定的功能 key，按用户顺序返回（首页按这个顺序铺卡片）。</summary>
    IReadOnlyList<string> Keys { get; }

    /// <summary>配置文件全路径（页面上要显示给用户看）。</summary>
    string FilePath { get; }

    bool Contains(string key);

    /// <summary>固定一个功能（已存在则忽略）。</summary>
    void Pin(string key);

    /// <summary>取消固定。</summary>
    void Unpin(string key);

    /// <summary>有则移除、无则添加（右键菜单一键切换用）。</summary>
    void Toggle(string key);

    /// <summary>收藏夹变化通知（首页据此重建卡片、功能清单页据此刷新 ★ 标记）。</summary>
    event Action? Changed;
}
