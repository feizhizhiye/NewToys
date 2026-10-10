using System;
using NewToys.Shared.Models;

namespace NewToys.Shared.Services;

/// <summary>
/// 「软件包管理」模块的设置存取。
/// <para>
/// **唯一落点**：<c>&lt;应用根&gt;\config\package.json</c>（默认 <c>%LocalAppData%\NewToys\config\</c>）。
/// 之所以必须是 <c>config\</c> 下：① 只有这里的 <c>*.json</c> 会被「设置 → 数据管理」的
/// 备份 / 导入覆盖；② 模块只**读**这里的值，不自己另存一份，避免双真相源。
/// </para>
/// </summary>
public interface IPackageSettingsService
{
    PackageSettings Settings { get; }

    /// <summary>配置文件全路径（页面上要显示给用户看）。</summary>
    string FilePath { get; }

    /// <summary>写回磁盘（即时生效的开关点一下就调它，不需要"保存"按钮），并广播 <see cref="Changed"/>。</summary>
    void Save();

    /// <summary>设置变更通知（模块页据此重算可用源 / 重排列表）。</summary>
    event Action? Changed;
}
