using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NewToys.Core.Services;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

/// <summary>
/// 规则集文件读写：**一条规则集 = 一个 <c>.py</c> 文件**，落
/// <c>&lt;StoreDir&gt;\script\rules\</c>（模块独有目录，用户数据）。
/// <para>
/// 文件头用注释块写元数据 —— C# 侧<b>只按行读这些键</b>，⛔ 不解析 Python 语法（真正的执行在 Python 侧）：
/// <list type="bullet">
///   <item>标量键：<c>name / base / encoding / desc / category / role / works / artifact /
///         concurrency / delay-ms / timeout / max-pages / max-items</c>（重复取第一个）。</item>
///   <item>多行键：<c>ua</c>（UA 池）/ <c>header</c>（请求头），每行一个值。</item>
///   <item>数字键脏值（非数字 / 越界）⇒ **回落缺省 + 记一条 <see cref="RuleSetMeta.HeaderWarnings"/>**。</item>
/// </list>
/// </para>
/// <para>
/// 🔴 类名刻意不叫 <c>ScrapeService</c>（旧实现已删）—— 见 命名表。
/// </para>
/// </summary>
public sealed class RuleFileService
{
    /// <summary>规则文件扩展名。</summary>
    public const string Extension = ".py";

    /// <summary>文件头元数据前缀。</summary>
    private const string MetaPrefix = "#";

    /// <summary>文件头最多扫多少行（头注释到第一条非注释为止）。</summary>
    private const int HeaderScanLines = 40;

    /// <summary>**多行**键（每行一个值，按出现顺序收集）。</summary>
    private static readonly HashSet<string> MultiKeys = new(StringComparer.OrdinalIgnoreCase) { "ua", "header" };

    private readonly ConfigStoreService _cfg;

    public RuleFileService(ConfigStoreService cfg) => _cfg = cfg;

    /// <summary>旧规则目录（<c>web-tools\rules</c>）一次性搬迁标记。</summary>
    private static bool _legacyMigrated;

    /// <summary>规则目录：<c>&lt;StoreDir&gt;\script\rules</c>（懒创建）。</summary>
    public string RulesDir
    {
        get
        {
            var dir = Path.Combine(_cfg.StoreDir, "script", "rules");
            MigrateLegacyRules(dir);
            try { Directory.CreateDirectory(dir); } catch { /* 只读环境退化为空列表 */ }
            return dir;
        }
    }

    /// <summary>
    /// 抓取能力从「网络工具」迁到「脚本中心」，规则目录随之从 <c>web-tools\rules</c> 挪到 <c>script\rules</c>。
    /// 老用户磁盘上已播种 / 手写过规则 ⇒ 首次访问目录时把旧目录里的 <c>.py</c> **搬过来**（只搬一次，⛔ 不覆盖同名新文件）。
    /// <para>⚠️ 内部自己 try/catch —— 搬不动就按空目录处理（⛔ 不拦启动、不弹错）。</para>
    /// </summary>
    private void MigrateLegacyRules(string dir)
    {
        if (_legacyMigrated) return;
        _legacyMigrated = true;
        try
        {
            var old = Path.Combine(_cfg.StoreDir, "web-tools", "rules");
            if (!Directory.Exists(old)) return;
            foreach (var f in Directory.GetFiles(old, "*" + Extension))
            {
                var dst = Path.Combine(dir, Path.GetFileName(f));
                if (!File.Exists(dst))
                {
                    Directory.CreateDirectory(dir);
                    File.Move(f, dst);
                }
            }
        }
        catch
        {
            // 搬不动 ⇒ 页面按空数据处理。
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
                var header = ReadScriptHeader(file);
                var warnings = new List<string>();
                var meta = new RuleSetMeta
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    FilePath = file,
                    BaseUrl = header.Scalar("base"),
                    Encoding = NonEmpty(header.Scalar("encoding"), "utf8"),
                    SizeBytes = info.Length,
                    ModifiedAt = info.LastWriteTime,
                    Enabled = true,

                    // 脚本头配置
                    Desc = header.Scalar("desc"),
                    Category = header.Scalar("category"),
                    Role = header.Scalar("role"),
                    Works = header.Scalar("works"),
                    Artifact = header.Scalar("artifact"),
                    UaPool = header.List("ua"),
                    Headers = header.List("header"),
                    Concurrency = ReadInt(header, "concurrency", 1, 1, "", warnings),
                    DelayMs = ReadInt(header, "delay-ms", 0, 0, " 毫秒", warnings),
                    TimeoutSeconds = ReadInt(header, "timeout", 0, 0, " 秒", warnings),
                    MaxPages = ReadInt(header, "max-pages", 0, 0, "", warnings),
                    MaxItems = ReadInt(header, "max-items", 0, 0, "", warnings)
                };
                meta.HeaderWarnings.AddRange(warnings);
                list.Add(meta);
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

    // ==================== 预设 ====================

    /// <summary>预设文件路径（预设由 <see cref="PresetSeeder"/> 播种进 rules 目录，⛔ 不去随包目录找）。</summary>
    public string PresetPath(string presetId) => Path.Combine(RulesDir, SafeName(presetId) + Extension);

    /// <summary>预设文件是否还在（用户删过就不再复活）。</summary>
    public bool PresetExists(string presetId) => File.Exists(PresetPath(presetId));

    /// <summary>
    /// 从预设创建规则集：复制预设文件 + 写 <c># base:</c>。
    /// 已存在 / 预设不在 ⇒ false（⛔ 不覆盖）。
    /// </summary>
    public bool CreateFromPreset(PresetInfo preset, string name, string baseUrl)
    {
        if (preset is null || string.IsNullOrWhiteSpace(name) || Exists(name)) return false;

        var src = PresetPath(preset.Id);
        if (!File.Exists(src)) return false;

        string text;
        try { text = File.ReadAllText(src, Encoding.UTF8); }
        catch { return false; }

        Write(name, SetBaseUrl(text, baseUrl));
        return true;
    }

    /// <summary>
    /// 在文件头写 / 改 <c># base:</c>：已有行就地改；没有就插在 <c># name:</c> 行之后（再没有就插在最前面）。
    /// </summary>
    private static string SetBaseUrl(string text, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return text;

        var crlf = text.Contains("\r\n", StringComparison.Ordinal);
        var eol = crlf ? "\r\n" : "\n";
        var lines = text.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("# base:", StringComparison.OrdinalIgnoreCase))
            {
                var lineEnd = lines[i].EndsWith("\r", StringComparison.Ordinal) ? "\r" : "";
                lines[i] = "# base: " + baseUrl + lineEnd;
                return string.Join('\n', lines);
            }
        }

        var insert = "# base: " + baseUrl + (crlf ? "\r" : "");
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("# name:", StringComparison.OrdinalIgnoreCase))
            {
                var list = lines.ToList();
                list.Insert(i + 1, insert);
                return string.Join('\n', list);
            }
        }

        return insert + eol + text;
    }

    /// <summary>新规则集模板（用户看得懂、能直接改；带全部脚本头键说明）。</summary>
    /// <remarks>
    /// 🔴 说明块里的键名**刻意不用英文冒号**（<c># desc —— …</c>）—— 解析器只认
    /// <c># 键名: 值</c>（英文冒号），否则文档行会被当成真键读进去（如 UA 池混进说明文字）。
    /// </remarks>
    public static string BuildTemplate(string name, string baseUrl)
    {
        var safeName = (name ?? "").Replace("\"", "\\\"");
        var safeBase = (baseUrl ?? "").Replace("\"", "\\\"");
        return
$@"# NewToys 规则集（一条规则集 = 一个 .py 文件；可手改，改完回软件点「🔄 重读」）
# name: {name}
# base: {baseUrl}
# encoding: utf8

# ==================== 脚本头键说明（本块只是文档，不构成配置） ====================
#   desc        —— 这个规则集干什么用的（目录页「说明」列）
#   category    —— book / image / video / doc / generic（目录页「分类」列；未知值原样显示）
#   works       —— 适用站点（详情卡显示）
#   role        —— pipeline（缺省）/ entry / pick / toc / content（调试集合按角色过滤候选）
#   artifact    —— files / text / none（产物形态；缺省按分类猜）
#   ua          —— 多行 = UA 池；不写 = 用设置页缺省 UA
#   header      —— 多行 = 额外请求头（原样透传）
#   concurrency —— 并发数（缺省 1）
#   delay-ms    —— 每次请求间隔毫秒（缺省 0）
#   timeout     —— 单次请求超时秒数（缺省 0 = 用设置页缺省）
#   max-pages   —— 分页上限（缺省 0 = 不限制）
#   max-items   —— 条目上限（缺省 0 = 不限制）
# 要启用某个键，照上面的名字另起一行写成 「# 键名」加值即可。

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

    /// <summary>读文件头的 <c># base:</c> / <c># encoding:</c>（编辑器页用；缺省返回空串 / <c>utf8</c>）。</summary>
    public (string BaseUrl, string Encoding) ReadMeta(string name)
    {
        var header = ReadScriptHeader(PathOf(name));
        return (header.Scalar("base"), NonEmpty(header.Scalar("encoding"), "utf8"));
    }

    // ==================== 脚本头解析 ====================

    /// <summary>
    /// 脚本头解析结果：标量键（首个值）+ 多行键（<c>ua</c> / <c>header</c>）+ 脏值提示。
    /// </summary>
    public sealed class RuleHeader
    {
        /// <summary>标量键 → 首个值（键大小写不敏感；⛔ 不含多行键）。</summary>
        public Dictionary<string, string> Scalars { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>多行键 → 全部值（按出现顺序；空行跳过）。</summary>
        public Dictionary<string, List<string>> Multi { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>标量值（缺 key = 空串）。</summary>
        public string Scalar(string key) => Scalars.TryGetValue(key, out var v) ? v : "";

        /// <summary>多行值（缺 key = 空数组）。</summary>
        public IReadOnlyList<string> List(string key)
            => Multi.TryGetValue(key, out var v) ? v : Array.Empty<string>();
    }

    /// <summary>
    /// 只读文件头前 <see cref="HeaderScanLines"/> 行里的 <c># key: value</c>（大小写不敏感）；
    /// 头注释到第一条非注释行为止。⛔ 不做任何 Python 解析。
    /// </summary>
    public static RuleHeader ReadScriptHeader(string path)
    {
        var header = new RuleHeader();
        try
        {
            using var reader = new StreamReader(path, Encoding.UTF8);
            for (var i = 0; i < HeaderScanLines; i++)
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
                if (key.Length == 0) continue;

                if (MultiKeys.Contains(key))
                {
                    if (value.Length == 0) continue;                       // 空行不进池
                    if (!header.Multi.TryGetValue(key, out var list))
                    {
                        list = new List<string>();
                        header.Multi[key] = list;
                    }
                    list.Add(value);
                }
                else if (!header.Scalars.ContainsKey(key))
                {
                    header.Scalars[key] = value;                           // 重复标量键取第一个
                }
            }
        }
        catch { /* 读不到就当没有头 */ }
        return header;
    }

    /// <summary>
    /// 读数字键：非数字 / 小于 <paramref name="min"/> ⇒ 回落 <paramref name="fallback"/> 并记一条警告。
    /// 缺键（空串）= 没配，直接回落，⛔ 不警告。
    /// </summary>
    private static int ReadInt(RuleHeader header, string key, int fallback, int min, string unit, List<string> warnings)
    {
        var raw = header.Scalar(key);
        if (raw.Length == 0) return fallback;
        if (int.TryParse(raw, out var value) && value >= min) return value;
        warnings.Add($"#{key} 的值「{raw}」不是有效数字（需 ≥{min}{unit}），已按缺省 {fallback}{unit}执行");
        return fallback;
    }

    /// <summary>空串回落（编码缺省 <c>utf8</c>）。</summary>
    private static string NonEmpty(string value, string fallback) => value.Length > 0 ? value : fallback;

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
