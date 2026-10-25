using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NewToys.Core.Services;
using NewToys.Shared.Services;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

public sealed partial class RuleRunService
{

    /// <summary>
    /// 爬虫面板：跑脚本 → 按索引合成**行表**（columns + rows + total）。
    /// 🔴 与 <see cref="RunScriptAsync"/> 的分工：那个按字段回命中数（编辑器检验用），这个回行表（面板列表用）。
    /// </summary>
    public async Task<CrawlResult> RunRowsAsync(string ruleName, string htmlPath, string baseUrl, CancellationToken ct = default)
    {
        if (!File.Exists(htmlPath)) throw new InvalidOperationException("还没抓到 HTML —— 先点「🔗 抓链接」。");

        var req = new PyRequest
        {
            Mode = "code",
            TimeoutSeconds = 120,
            ArgsJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["mode"] = "rows",
                ["path"] = _files.PathOf(ruleName),
                ["html_path"] = htmlPath,
                ["base"] = baseUrl ?? ""
            }),
            Code = Program
        };

        var res = await _py.RunAsync(req, ct).ConfigureAwait(false);
        if (!res.Ok || !string.IsNullOrEmpty(res.Error))
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(res.Traceback) ? res.Error : res.Traceback);

        var json = LastLine(res.Out);
        var result = new CrawlResult();
        if (json.Length == 0) return result;

        using var doc = JsonDocument.Parse(json);
        var rootEl = doc.RootElement;

        if (rootEl.TryGetProperty("columns", out var colsEl) && colsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in colsEl.EnumerateArray())
            {
                var col = new CrawlColumn
                {
                    Key = el.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "",
                    Expr = el.TryGetProperty("expr", out var e) ? e.GetString() ?? "" : "",
                    Count = el.TryGetProperty("count", out var c) && c.TryGetInt32(out var ci) ? ci : 0,
                    Error = el.TryGetProperty("error", out var pe) ? pe.GetString() ?? "" : ""
                };
                if (el.TryGetProperty("values", out var vs) && vs.ValueKind == JsonValueKind.Array)
                    foreach (var v in vs.EnumerateArray())
                        col.Values.Add(v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString());
                result.Columns.Add(col);
            }
        }

        if (rootEl.TryGetProperty("rows", out var rowsEl) && rowsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in rowsEl.EnumerateArray())
            {
                var row = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var prop in el.EnumerateObject())
                    row[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString() ?? ""
                        : prop.Value.ToString();
                result.Rows.Add(row);
            }
        }

        result.Total = rootEl.TryGetProperty("total", out var t) && t.TryGetInt32(out var ti)
            ? ti
            : result.Rows.Count;
        return result;
    }

    /// <summary>
    /// 期②下载：对一份 HTML 只求值 <paramref name="keys"/>（逗号分隔，如 <c>content,next</c>），回**全量值**。
    /// 🔴 与 <see cref="RunRowsAsync"/> 的分工：那个按索引合成行表（面板列表用），这个回全量值（下载落盘用）。
    /// </summary>
    public async Task<StepResult> RunStepAsync(string ruleName, string htmlPath, string baseUrl, string keys, CancellationToken ct = default)
    {
        if (!File.Exists(htmlPath)) throw new InvalidOperationException("HTML 文件不存在：" + htmlPath);

        var req = new PyRequest
        {
            Mode = "code",
            TimeoutSeconds = 120,
            ArgsJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["mode"] = "step",
                ["path"] = _files.PathOf(ruleName),
                ["html_path"] = htmlPath,
                ["base"] = baseUrl ?? "",
                ["keys"] = keys ?? "content"
            }),
            Code = Program
        };

        var res = await _py.RunAsync(req, ct).ConfigureAwait(false);
        if (!res.Ok || !string.IsNullOrEmpty(res.Error))
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(res.Traceback) ? res.Error : res.Traceback);

        var json = LastLine(res.Out);
        var result = new StepResult();
        if (json.Length == 0) return result;

        using var doc = JsonDocument.Parse(json);
        var rootEl = doc.RootElement;

        if (rootEl.TryGetProperty("fields", out var fEl) && fEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in fEl.EnumerateObject())
            {
                var list = new List<string>();
                if (prop.Value.ValueKind == JsonValueKind.Array)
                    foreach (var v in prop.Value.EnumerateArray())
                        list.Add(v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString());
                result.Fields[prop.Name] = list;
            }
        }

        if (rootEl.TryGetProperty("errors", out var eEl) && eEl.ValueKind == JsonValueKind.Object)
            foreach (var prop in eEl.EnumerateObject())
                result.Errors[prop.Name] = prop.Value.ToString();

        return result;
    }

}
