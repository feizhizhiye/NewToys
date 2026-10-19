using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NewToys.Core.Services;
using NewToys.WebTools.Models;

namespace NewToys.WebTools.Services;

/// <summary>
/// 规则集文件读写（NT-084）：**一条规则集 = 一个 <c>.py</c> 文件**，落
/// <c>&lt;DataDir&gt;\web-tools\rules\</c>（模块独有目录）。
/// <para>
/// 文件头用注释块写元数据（<c># name:</c> / <c># base:</c> / <c># encoding:</c>）——
/// C# 侧<b>只按行读这几个键</b>，⛔ 不解析 Python 语法（真正的执行在 Python 侧）。
/// </para>
/// <para>
/// 🔴 类名刻意不叫 <c>ScrapeService</c>（旧实现已删）—— 见 NT-084 命名表。
/// </para>
/// </summary>
public sealed class RuleFileService
{
    /// <summary>规则文件扩展名。</summary>
    public const string Extension = ".py";

    /// <summary>文件头元数据前缀。</summary>
    private const string MetaPrefix = "#";

    private readonly ConfigStoreService _cfg;

    public RuleFileService(ConfigStoreService cfg) => _cfg = cfg;

    /// <summary>规则目录：<c>&lt;DataDir&gt;\web-tools\rules</c>（懒创建）。</summary>
    public string RulesDir
    {
        get
        {
            var dir = Path.Combine(_cfg.DataDir, "web-tools", "rules");
            try { Directory.CreateDirectory(dir); } catch { /* 只读环境退化为空列表 */ }
            return dir;
        }
    }

    /// <summary>规则名 → 文件绝对路径（名字先做文件名清洗）。</summary>
    public string PathOf(string name) => Path.Combine(RulesDir, SafeName(name) + Extension);

    /// <summary>规则集是否存在。</summary>
    public bool Exists(string name) => !string.IsNullOrWhiteSpace(name) && File.Exists(PathOf(name));

    /// <summary>扫目录 + 读文件头（不做任何 Python 解析）。</summary>
    public IReadOnlyList<RuleSetMeta> Scan()
    {
        var list = new List<RuleSetMeta>();
        string[] files;
        try { files = Directory.GetFiles(RulesDir, "*" + Extension); }
        catch { return list; }

        foreach (var file in files)
        {
            try
            {
                var info = new FileInfo(file);
                var header = ReadHeader(file);
                list.Add(new RuleSetMeta
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    FilePath = file,
                    BaseUrl = header.TryGetValue("base", out var b) ? b : "",
                    Encoding = header.TryGetValue("encoding", out var e) && e.Length > 0 ? e : "utf8",
                    SizeBytes = info.Length,
                    ModifiedAt = info.LastWriteTime,
                    Enabled = true
                });
            }
            catch { /* 单个坏文件不影响整表 */ }
        }

        return list;
    }

    /// <summary>读全文（⛔ 不缓存：用户在外部改了要能立刻看到）。</summary>
    public string Read(string name)
    {
        try { return File.ReadAllText(PathOf(name), Encoding.UTF8); }
        catch { return ""; }
    }

    /// <summary>整文件写回（覆盖）。</summary>
    public void Write(string name, string content)
    {
        var path = PathOf(name);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, content ?? "", new UTF8Encoding(false));
    }

    /// <summary>新建规则集（已存在则返回 false，⛔ 不覆盖）。</summary>
    public bool Create(string name, string baseUrl)
    {
        if (Exists(name)) return false;
        Write(name, BuildTemplate(name, baseUrl));
        return true;
    }

    /// <summary>删除规则集文件（⛔ 不删目录）。</summary>
    public bool Delete(string name)
    {
        try
        {
            var path = PathOf(name);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch { return false; }
    }

    /// <summary>新规则集模板（用户看得懂、能直接改）。</summary>
    public static string BuildTemplate(string name, string baseUrl)
    {
        var safeName = (name ?? "").Replace("\"", "\\\"");
        var safeBase = (baseUrl ?? "").Replace("\"", "\\\"");
        return
$@"# NewToys 规则集（一条规则集 = 一个 .py 文件；可手改，改完回软件点「🔄 重读」）
# name: {name}
# base: {baseUrl}
# encoding: utf8

SOURCE = {{
    ""name"": ""{safeName}"",
    ""base"": ""{safeBase}"",
    ""encoding"": ""utf8"",
    ""header"": {{}},
}}

# 一条命令一行：(字段名, 表达式)
#   表达式前缀：css: / xpath: / json: / regex: / =常量
#   多个候选用 || 连接（取第一个有结果的）；净化写成 表达式##正则##替换
RULES = [
    (""title"", ""css:h1@text""),
]

# 规则表达不了时才写下面四个函数（可选，签名固定）：
# def search(html, base, key, page): return 列表
# def detail(html, base, item): return 字典
# def toc(html, base, item): return 列表
# def content(html, base, item): return 正文文本
";
    }

    /// <summary>只读文件头前 40 行里的 <c># key: value</c>（大小写不敏感）。</summary>
    private static Dictionary<string, string> ReadHeader(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var reader = new StreamReader(path, Encoding.UTF8);
            for (var i = 0; i < 40; i++)
            {
                var line = reader.ReadLine();
                if (line is null) break;
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                if (!trimmed.StartsWith(MetaPrefix, StringComparison.Ordinal)) break;   // 头注释到第一条非注释为止
                var body = trimmed[MetaPrefix.Length..].Trim();
                var idx = body.IndexOf(':');
                if (idx <= 0) continue;
                var key = body[..idx].Trim();
                var value = body[(idx + 1)..].Trim();
                if (key.Length > 0 && !map.ContainsKey(key)) map[key] = value;
            }
        }
        catch { /* 读不到就当没有头 */ }
        return map;
    }

    /// <summary>文件名清洗：去掉 Windows 非法字符，空名退化为「未命名」。</summary>
    public static string SafeName(string name)
    {
        var raw = (name ?? "").Trim();
        if (raw.Length == 0) return "未命名";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = raw.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var result = new string(chars).Trim().TrimEnd('.');
        return result.Length == 0 ? "未命名" : result;
    }
}
