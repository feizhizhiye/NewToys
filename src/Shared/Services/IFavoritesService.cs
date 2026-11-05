using System;
using System.Collections.Generic;

namespace NewToys.Shared.Services;

/// <summary>「首页 → 常用功能（收藏夹）」的存取。（WHY: SH-038）</summary>
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
