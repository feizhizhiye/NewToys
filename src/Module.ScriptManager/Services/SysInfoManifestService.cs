using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

/// <summary>
/// <see cref="ISysInfoManifestService"/> 的默认实现（纯文件读写 + 校验，⛔ 不碰 UI / 不碰数据库）。
/// </summary>
public sealed class SysInfoManifestService : ISysInfoManifestService
{
    /// <summary>子目录名（脚本集合整体就在这一个文件夹里，导入 / 导出 = 打包它）。</summary>
    public const string SubFolderName = "sysinfo";

    /// <summary>manifest 文件名。</summary>
    public const string ManifestFileName = "sysinfo.manifest.json";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        // 中文列标题 / 分组名不转成 \uXXXX（manifest 是给人手改的）
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string GetSysInfoDir(string scriptsFolder)
        => Path.Combine(string.IsNullOrWhiteSpace(scriptsFolder) ? "." : scriptsFolder, SubFolderName);

    public string GetManifestPath(string scriptsFolder)
        => Path.Combine(GetSysInfoDir(scriptsFolder), ManifestFileName);

    public SysInfoManifestLoadResult Load(string scriptsFolder)
    {
        var path = GetManifestPath(scriptsFolder);

        if (!File.Exists(path))
            return new SysInfoManifestLoadResult
            {
                Error = "找不到 " + ManifestFileName + "（「🔄 重新检测」会先尝试从随包默认值播种）",
                ManifestPath = path,
            };

        SysInfoManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<SysInfoManifest>(File.ReadAllText(path), ReadOptions);
        }
        catch (Exception ex)
        {
            return new SysInfoManifestLoadResult { Error = "manifest 读取失败：" + ex.Message, ManifestPath = path };
        }

        if (manifest is null)
            return new SysInfoManifestLoadResult { Error = "manifest 是空的", ManifestPath = path };

        // schemaVersion：缺 / 超版本 ⇒ 坏 manifest（⛔ 不猜字段）
        if (manifest.SchemaVersion <= 0)
            return new SysInfoManifestLoadResult { Error = "manifest 缺少 schemaVersion", ManifestPath = path };

        if (manifest.SchemaVersion > SysInfoManifest.SupportedSchemaVersion)
            return new SysInfoManifestLoadResult
            {
                Error = $"manifest 的 schemaVersion={manifest.SchemaVersion} 高于本程序支持的 " +
                        $"{SysInfoManifest.SupportedSchemaVersion}（请升级程序）",
                ManifestPath = path,
            };

        var result = new SysInfoManifestLoadResult { ManifestPath = path };
        var warnings = result.Warnings;

        // ---------- columns：去重 key、修宽度、丢空标题 ----------
        var columns = new List<SysInfoColumn>();
        var seenKeys = new HashSet<int>();
        foreach (var c in manifest.Columns)
        {
            if (c is null) continue;
            if (c.Key <= 0) { warnings.Add("有一列的 key 不是正整数，已忽略"); continue; }
            if (!seenKeys.Add(c.Key)) { warnings.Add($"列 key={c.Key} 重复，已忽略后一个"); continue; }

            var title = (c.Title ?? "").Trim();
            if (title.Length == 0) { warnings.Add($"列 key={c.Key} 没有标题，已忽略"); continue; }

            var width = NormalizeWidth(c.Width);
            if (width != (c.Width ?? "").Trim()) warnings.Add($"列「{title}」的宽度「{c.Width}」不合法，已改用 {width}");

            columns.Add(new SysInfoColumn { Key = c.Key, Title = title, Width = width, Visible = c.Visible });
        }

        if (columns.Count == 0)
            return new SysInfoManifestLoadResult { Error = "manifest 里没有可用的 columns", ManifestPath = path };

        // ---------- scripts：去重 file、收敛 interval / timeout ----------
        var scripts = new List<SysInfoScript>();
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in manifest.Scripts)
        {
            if (s is null) continue;
            var file = (s.File ?? "").Trim();
            if (file.Length == 0) { warnings.Add("有一条脚本没有 file，已忽略"); continue; }
            if (!seenFiles.Add(file)) { warnings.Add($"脚本 {file} 重复出现，已忽略后一个"); continue; }

            var interval = s.Interval;
            if (interval < 0) interval = 0;
            if (interval > 0 && interval < SysInfoScript.MinIntervalSeconds)
            {
                interval = SysInfoScript.MinIntervalSeconds;
                warnings.Add($"脚本 {file} 的 interval 小于 {SysInfoScript.MinIntervalSeconds}s，已按 {interval}s 处理");
            }

            var timeout = s.Timeout <= 0 ? ScriptRunner.DefaultTimeoutSeconds : Math.Min(s.Timeout, SysInfoScript.MaxTimeoutSeconds);

            scripts.Add(new SysInfoScript
            {
                File = file,
                Group = (s.Group ?? "").Trim(),
                Interval = interval,
                Auto = s.Auto,
                Timeout = timeout,
            });
        }

        return new SysInfoManifestLoadResult
        {
            Ok = true,
            ManifestPath = path,
            Manifest = new SysInfoManifest
            {
                SchemaVersion = manifest.SchemaVersion,
                Columns = columns,
                Scripts = scripts,
            },
        };
    }

    public string SaveColumnWidths(string scriptsFolder, IReadOnlyDictionary<int, string> widths)
    {
        var path = GetManifestPath(scriptsFolder);
        if (!File.Exists(path)) return "⚠ 找不到 manifest，宽度没保存。";

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) as JsonObject;
            if (node is null) return "⚠ manifest 结构不是对象，宽度没保存。";

            if (node["columns"] is not JsonArray columns) return "⚠ manifest 里没有 columns，宽度没保存。";

            var changed = 0;
            foreach (var item in columns)
            {
                if (item is not JsonObject col) continue;
                if (col["key"] is not JsonValue kv || !kv.TryGetValue<int>(out var key)) continue;
                if (!widths.TryGetValue(key, out var w)) continue;

                var width = NormalizeWidth(w);
                col["width"] = width;
                changed++;
            }

            if (changed == 0) return "⚠ 没有可写回的列宽。";

            File.WriteAllText(path, node.ToJsonString(WriteOptions));
            return $"已把 {changed} 个列宽写回 manifest。";
        }
        catch (Exception ex)
        {
            return "⚠ 列宽写回失败：" + ex.Message;
        }
    }

    /// <summary>
    /// 宽度合法化：<c>Auto</c> / <c>数字</c> / <c>数字*</c> / <c>*</c>，其余一律退回 <c>1*</c>
    /// （⛔ 不把非法串塞进 <c>GridLength.Parse</c>，那会在布局期抛异常）。
    /// </summary>
    public static string NormalizeWidth(string? raw)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return "1*";
        if (string.Equals(s, "Auto", StringComparison.OrdinalIgnoreCase)) return "Auto";

        var body = s.EndsWith("*", StringComparison.Ordinal) ? s[..^1] : s;
        if (body.Length == 0) return "1*";   // "*" = 1*
        if (!double.TryParse(body, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0)
            return "1*";

        return v.ToString("0.###", CultureInfo.InvariantCulture) + (s.EndsWith("*", StringComparison.Ordinal) ? "*" : "");
    }
}
