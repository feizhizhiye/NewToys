using System;

namespace NewToys.ScriptManager.Services;

/// <summary>
/// 「脚本管理器」模块设置的读写契约（v3.64 / NT-061，需求 §3.5 + §4.9）：**本模块配置的唯一写入点**。
/// <list type="bullet">
///   <item>数据落 <c>&lt;配置根&gt;\config\script-manager.json</c>（走 Core 的 <c>IOptionsStore</c>，按模块分区、随 profile 隔离）。</item>
///   <item>「脚本库」页的脚本目录框、本模块设置页（<c>settings-script</c>）都只调本服务 ⇒ 一处写、多处读。</item>
///   <item>🔴 v3.65 / NT-062：三个解释器路径（pwsh / python / powershell 5.1）**已回迁应用级**
///   （<c>app.json</c>，判据 =「运行环境」是应用级路由）⇒ 本契约只剩**脚本目录**一项。</item>
/// </list>
/// <para>
/// 🔴 所有 <c>Set*</c> 都返回**状态文案**（成功 / 失败都直接给状态栏用）：路径不存在 ⇒ 返回 <c>⚠ …</c> 且
/// **不写盘、保持上一个有效值**（需求 §3.5）；失败文案必须以 <c>⚠</c> 开头，Shell 底栏据此显示红字。
/// </para>
/// </summary>
public interface IScriptManagerSettingsService
{
    /// <summary>选项存储里的模块分区名（= 配置文件名基名）。</summary>
    string ModuleKey { get; }

    /// <summary>本模块设置文件全路径（<c>config\script-manager.json</c>；设置页展示 / 排障用）。</summary>
    string FilePath { get; }

    /// <summary>脚本目录（用户值）；空 = 用 <see cref="DefaultScriptsFolder"/>。</summary>
    string ScriptsFolder { get; }

    /// <summary>脚本目录的默认值：<c>&lt;应用根&gt;\scripts</c>（多配置**共享同一份**脚本目录，⛔ 不随 profile 隔离）。</summary>
    string DefaultScriptsFolder { get; }

    /// <summary>脚本目录**生效值**（用户值为空时回落默认值）。</summary>
    string EffectiveScriptsFolder { get; }

    /// <summary>任一项写盘成功后触发（读方据此刷新显示）。</summary>
    event Action? Changed;

    /// <summary>写「脚本目录」；返回状态文案。</summary>
    string SetScriptsFolder(string value);

    /// <summary>脚本目录恢复成默认值（清空用户值）；返回状态文案。</summary>
    string ResetScriptsFolder();
}
