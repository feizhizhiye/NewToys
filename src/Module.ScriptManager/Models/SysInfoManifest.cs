using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace NewToys.ScriptManager.Models;

/// <summary>
/// <c>sysinfo\sysinfo.manifest.json</c> 的强类型投影（NT-067 §三.1）。
/// <para>
/// 🔴 `columns[].key` = **稳定的 1-based 列位**，与数组顺序解耦：脚本输出 <c>values[key-1]</c>。
/// 用户调整 <c>columns[]</c> 的排列顺序（= 列在界面上左右换位）**不影响脚本输出**。
/// </para>
/// <para>
/// 缺 <c>schemaVersion</c> / 值 &gt; 当前支持版本 / JSON 语法错 ⇒ 视为**坏 manifest**：空态 + 状态栏 ⚠，
/// ⛔ 不抛异常、⛔ 不猜字段（见 <c>ISysInfoManifestService</c>）。
/// </para>
/// </summary>
public sealed class SysInfoManifest
{
    /// <summary>当前支持的唯一 schema 版本。</summary>
    public const int SupportedSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }

    [JsonPropertyName("columns")] public List<SysInfoColumn> Columns { get; set; } = new();

    [JsonPropertyName("scripts")] public List<SysInfoScript> Scripts { get; set; } = new();
}

/// <summary>manifest 的一列。</summary>
public sealed class SysInfoColumn
{
    /// <summary>稳定列位（1-based）→ 脚本输出的 <c>values[key-1]</c>。</summary>
    [JsonPropertyName("key")] public int Key { get; set; }

    /// <summary>表头标题。</summary>
    [JsonPropertyName("title")] public string Title { get; set; } = "";

    /// <summary>
    /// 列宽：Avalonia 的 <c>GridLength</c> 字符串（<c>140</c> / <c>2*</c> / <c>Auto</c>）。
    /// 🔴 **宽度唯一来源 = manifest**（本期不接列宽拖拽，改宽走「⚙ 列设置」写回这里）。
    /// <para>
    /// 手改 manifest 写数字（<c>"width": 110</c>）也接受 —— <see cref="LenientStringConverter"/> 读成 <c>"110"</c>。
    /// </para>
    /// </summary>
    [JsonPropertyName("width")]
    [JsonConverter(typeof(LenientStringConverter))]
    public string Width { get; set; } = "1*";

    /// <summary>默认是否显示（运行时显隐以 <c>ColumnLayoutService</c> 里的 `script-info` 页键为准）。</summary>
    [JsonPropertyName("visible")] public bool Visible { get; set; } = true;
}

/// <summary>manifest 的一个脚本（= 一个 <c>ScriptId</c> 的产出组）。</summary>
public sealed class SysInfoScript
{
    /// <summary>相对 <c>sysinfo\</c> 的脚本路径（也允许绝对路径）。</summary>
    [JsonPropertyName("file")] public string File { get; set; } = "";

    /// <summary>归类（映射到「类型：&lt;分组&gt;」快捷筛选）。</summary>
    [JsonPropertyName("group")] public string Group { get; set; } = "";

    /// <summary>定时刷新间隔（秒，0 = 仅手动；最小 2s，见 <c>MinIntervalSeconds</c>）。</summary>
    [JsonPropertyName("interval")] public int Interval { get; set; }

    /// <summary>是否允许**无人值守**跑（定时器只跑 <c>auto:true</c> 且 <c>interval&gt;0</c> 的脚本）。缺省 false。</summary>
    [JsonPropertyName("auto")] public bool Auto { get; set; }

    /// <summary>超时（秒，缺省 30、上限 300）。</summary>
    [JsonPropertyName("timeout")] public int Timeout { get; set; } = 30;

    /// <summary>定时刷新允许的最小间隔（秒）。</summary>
    public const int MinIntervalSeconds = 2;

    /// <summary>超时上限（秒）。</summary>
    public const int MaxTimeoutSeconds = 300;

    /// <summary>ScriptId = 文件名去扩展名（同一 <c>file</c> 只许出现一次）。</summary>
    [JsonIgnore]
    public string ScriptId => Path.GetFileNameWithoutExtension(File ?? "");

    /// <summary>脚本在磁盘上的绝对路径（相对路径一律相对 <c>sysinfo\</c>）。</summary>
    public string ResolvePath(string sysInfoDir)
        => string.IsNullOrWhiteSpace(File) ? "" : Path.IsPathRooted(File) ? File : Path.Combine(sysInfoDir, File);
}
