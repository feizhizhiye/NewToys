using System;

namespace NewToys.ScriptManager.Services;

/// <summary>「脚本管理器」模块设置的读写契约（，需求 §3.5 + §4.9）：本模块配置的唯一写入点。（WHY: SM-033）</summary>
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
