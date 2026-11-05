using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using NewToys.Reading.Models;

namespace NewToys.Reading.Services;

/// <summary>一次导入的结果（界面状态栏 / 提示用）。</summary>
public sealed class ImportResult
{
    public int Added { get; set; }

    public int Updated { get; set; }

    public int Skipped { get; set; }

    /// <summary>跳过的非文本源条数（图片 / 音频 / 视频 —— 本软件只做文本）。</summary>
    public int Unsupported { get; set; }

    public List<string> Errors { get; } = new();

    public bool HasErrors => Errors.Count > 0;

    public string Summary
    {
        get
        {
            var text = $"导入完成：新增 {Added}，更新 {Updated}";
            if (Skipped > 0) text += $"，跳过 {Skipped}";
            if (Unsupported > 0) text += $"，跳过非文本源 {Unsupported}（图片 / 音频 / 视频不导入）";
            if (Errors.Count > 0) text += " · " + Errors[0];
            return text;
        }
    }
}

/// <summary>legado 书源 JSON 导入 / 导出。（WHY: RD-027）</summary>
public sealed class SourceImportService
{
    private readonly SourceStore _store;

    public SourceImportService(SourceStore store) => _store = store;

    /// <summary>从文件导入（编码按 BOM → 严格 UTF-8 → GB18030 嗅探）。</summary>
    public ImportResult ImportFile(string path)
    {
        string text;
        try { text = ReadText(path); }
        catch (Exception ex)
        {
            var failed = new ImportResult();
            failed.Errors.Add("读文件失败：" + ex.Message);
            return failed;
        }

        return ImportJson(text);
    }

    /// <summary>从 JSON 文本导入。</summary>
    public ImportResult ImportJson(string text)
    {
        var result = new ImportResult();

        var parsed = Parse(text, result);
        if (parsed.Count == 0) return result;

        var existing = _store.LoadAll()
            .Select(s => s.Url)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var source in parsed)
        {
            if (existing.Contains(source.Url)) result.Updated++;
            else
            {
                result.Added++;
                existing.Add(source.Url);
            }
        }

        var written = _store.Upsert(parsed);
        if (written == 0 && parsed.Count > 0) result.Errors.Add("写库失败：书源库不可用");
        return result;
    }

    /// <summary>把选中的书源导成 legado 兼容的数组文本（原样回转，⛔ 不重新拼字段）。</summary>
    public string Export(IEnumerable<BookSource> sources)
    {
        var array = new JsonArray();
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source.RawJson)) continue;
            try
            {
                var node = JsonNode.Parse(source.RawJson);
                if (node is not null) array.Add(node);
            }
            catch { /* 原文损坏的那条跳过，不影响其余 */ }
        }

        return array.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static List<BookSource> Parse(string text, ImportResult result)
    {
        var list = new List<BookSource>();

        JsonDocument document;
        try { document = JsonDocument.Parse(text); }
        catch (Exception ex)
        {
            result.Errors.Add("不是合法 JSON：" + ex.Message);
            return list;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("bookSources", out var nested))
                root = nested;

            if (root.ValueKind != JsonValueKind.Array)
            {
                result.Errors.Add("格式不符：既不是顶层数组，也没有 bookSources 字段");
                return list;
            }

            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    result.Skipped++;
                    continue;
                }

                try
                {
                    var raw = item.GetRawText();
                    var source = JsonSerializer.Deserialize<BookSource>(raw);
                    if (source is null || string.IsNullOrWhiteSpace(source.Url))
                    {
                        result.Skipped++;
                        continue;
                    }

                    // 非文本源（图片 / 音频 / 视频）直接不入库：本软件只做文本正文，留着只会污染搜索与发现列表
                    if (source.Type != 0)
                    {
                        result.Unsupported++;
                        continue;
                    }

                    source.RawJson = raw;
                    var (capability, reason) = SourceAnalyzer.Analyze(source);
                    source.Capability = capability;
                    source.CapabilityReason = reason;
                    list.Add(source);
                }
                catch { result.Skipped++; }
            }
        }

        return list;
    }

    private static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        try
        {
            return new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (System.Text.DecoderFallbackException)
        {
            try
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                return System.Text.Encoding.GetEncoding("gb18030").GetString(bytes);
            }
            catch { return System.Text.Encoding.UTF8.GetString(bytes); }
        }
    }
}
