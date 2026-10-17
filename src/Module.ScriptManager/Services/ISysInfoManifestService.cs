using System.Collections.Generic;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

/// <summary>
/// <c>sysinfo\sysinfo.manifest.json</c> 的加载 / 校验 / 列宽回写契约（NT-067 §三.1）。
/// <para>
/// 🔴 加载**永不抛异常**：缺文件 / JSON 语法错 / schemaVersion 不认识 ⇒ <see cref="SysInfoManifestLoadResult.Ok"/> = false
/// 且 <see cref="SysInfoManifestLoadResult.Error"/> 给人话（页面据此走空态 + 状态栏 ⚠，⛔ 不崩、⛔ 不猜字段）。
/// </para>
/// </summary>
public interface ISysInfoManifestService
{
    /// <summary>脚本集合目录（= <c>&lt;脚本目录&gt;\sysinfo</c>）。</summary>
    string GetSysInfoDir(string scriptsFolder);

    /// <summary>manifest 全路径（= <c>sysinfo\sysinfo.manifest.json</c>）。</summary>
    string GetManifestPath(string scriptsFolder);

    /// <summary>读 + 校验 manifest。</summary>
    SysInfoManifestLoadResult Load(string scriptsFolder);

    /// <summary>
    /// 把「⚙ 列设置」里改过的列宽写回 manifest（键 = <c>columns[].key</c>，值 = <c>GridLength</c> 字符串）。
    /// 返回状态文案（成功 / 失败都给状态栏用）。
    /// </summary>
    string SaveColumnWidths(string scriptsFolder, IReadOnlyDictionary<int, string> widths);
}

/// <summary>manifest 加载结果（成功 / 失败都返回它，⛔ 不抛）。</summary>
public sealed class SysInfoManifestLoadResult
{
    /// <summary>manifest 可用（已通过校验）。</summary>
    public bool Ok { get; init; }

    /// <summary>失败原因（人话；<see cref="Ok"/> = true 时为 null）。</summary>
    public string? Error { get; init; }

    /// <summary>能修就修的小问题（重复脚本 / 非法宽度 / 间隔太短…）—— 只进状态栏，不阻断。</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>校验后的 manifest（<see cref="Ok"/> = false 时为 null）。</summary>
    public SysInfoManifest? Manifest { get; init; }

    /// <summary>manifest 全路径（空态 / 排障文案要用）。</summary>
    public string ManifestPath { get; init; } = "";
}
