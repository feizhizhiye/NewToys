"""NewToys · Legado（阅读 App）书源 → NewToys 规则集转换器（NT-084 批 D）。

用法（由 C# 调 IPythonRuntime 执行，也可命令行跑）：
    python convert_legado.py <书源.json> <规则目录>

入参约定（C# 侧走 worker 的 vars）：
    {"in": "<书源 json 路径>", "outDir": "<规则目录>"}

输出（stdout 最后一行 JSON）：
    {"ok": true, "written": [{"name": "...", "path": "...", "rules": 5}],
     "skipped": [{"name": "...", "reason": "..."}]}

🔴 C# 里不写任何书源解析逻辑 —— 全部在这里（模块 csproj 零改动，脚本随程序发送）。
🔴 一个书源 = 一个 .py 规则集；文件名重名自动加序号，⛔ 不覆盖已有文件。
🔴 表达不了的规则（详情 / 目录 / 正文 / 发现）不打丢：原样写进文件尾注释。
"""

import json
import os
import re
import sys

# 书源里这些字段能直接映射成一条命令（字段名 -> legado 里的 key）
SEARCH_FIELDS = [
    ("title", "name"),
    ("author", "author"),
    ("kind", "kind"),
    ("last_chapter", "lastChapter"),
    ("url", "bookUrl"),
    ("cover", "coverUrl"),
    ("intro", "intro"),
    ("update_time", "updateTime"),
    ("word_count", "wordCount"),
]

# 这里没有对应能力的规则块，原样留注释
OTHER_BLOCKS = [
    ("ruleExplore", "发现（未转换：规则编辑器没有「发现」页）"),
    ("ruleBookInfo", "详情（未转换：规则编辑器只跑列表页字段）"),
    ("ruleToc", "目录（未转换）"),
    ("ruleContent", "正文（未转换）"),
]


def _q(text):
    """Python 字符串字面量转义（反斜杠与双引号；换行折成字面 \\n）。"""
    return str(text or "").replace("\\", "\\\\").replace('"', '\\"').replace("\r", "").replace("\n", "\\n")


def _comment_lines(text, head="#   ", cont="#     "):
    """把（可能多行的）规则值**逐行**写成注释。
    🔴 只给首行加 # 会让续行裸奔成代码 —— 运行时 exec_module 直接 SyntaxError，
    整个规则集连 RULES 都读不出来（2026-10-20 踩坑：得奇小说.py 第 44 行起全裸）。"""
    out = []
    for i, ln in enumerate(str(text or "").replace("\r", "").split("\n")):
        out.append((head if i == 0 else cont) + ln)
    return out


def _safe_name(name):
    raw = (name or "").strip() or "未命名书源"
    raw = re.sub(r'[<>:"/\\|?*\x00-\x1f]', "_", raw).strip().strip(".")
    return raw[:80] or "未命名书源"


def _base_of(src):
    url = (src.get("bookSourceUrl") or "").strip()
    m = re.match(r"^(https?://[^/]+)", url)
    return m.group(1) if m else url


def _search_url(src):
    """Legado 的 searchUrl → 本项目的 {key} / {page} 占位。"""
    url = (src.get("searchUrl") or "").strip()
    if not url:
        return ""
    # {{key}} / {{page}} / searchKey / searchPage 三种写法都见过
    url = url.replace("{{key}}", "{key}").replace("{{page}}", "{page}")
    url = url.replace("searchKey", "{key}").replace("searchPage", "{page}")
    # 已经带 {key} 的别重复加
    if "{key}" not in url:
        url += ("&" if "?" in url else "?") + "q={key}"
    if "{page}" not in url:
        url += "&p={page}"
    return url


def _python_value(value):
    """legado 的规则值可能是 str / list / dict，统一拍成字符串（list 用 || 连接 = 本项目多候选语法）。"""
    if value is None:
        return ""
    if isinstance(value, str):
        return value.strip()
    if isinstance(value, list):
        parts = [_python_value(v) for v in value]
        return " || ".join(p for p in parts if p)
    return ""


def _build(src, file_name):
    name = (src.get("bookSourceName") or file_name).strip() or file_name
    base = _base_of(src)
    search_url = _search_url(src)

    lines = [
        "# NewToys 规则集（由「⬆ 导入书源」从 Legado 书源转换；可手改）",
        "# name: " + name.replace("\r", " ").replace("\n", " "),
        "# base: " + base,
        "# encoding: utf8",
        "",
        "SOURCE = {",
        '    "name": "%s",' % _q(name),
        '    "base": "%s",' % _q(base),
        '    "encoding": "utf8",',
        '    "header": {},',
    ]
    if search_url:
        lines.append('    "search_url": "%s",' % _q(search_url))
    lines += ["}", "", "# 一条命令一行：(字段名, 表达式)；表达式原样保留 legado 写法（class.x@tag.a@text）"]

    rule_search = src.get("ruleSearch") or {}
    if not isinstance(rule_search, dict):
        rule_search = {}

    rules = []
    for field, src_key in SEARCH_FIELDS:
        expr = _python_value(rule_search.get(src_key))
        if expr:
            rules.append((field, expr))

    if rules:
        lines.append("RULES = [")
        for field, expr in rules:
            lines.append('    ("%s", "%s"),' % (field, _q(expr)))
        lines.append("]")
    else:
        lines.append("RULES = [")
        lines.append('    ("title", "css:h1@text"),   # 书源没给 ruleSearch.name，占位待改')
        lines.append("]")
    book_list = _python_value(rule_search.get("bookList"))
    if book_list:
        lines += ["", "# 列表容器规则（本项目在整页上跑命令，这一条仅供参考）："] + _comment_lines(book_list)

    extras = []
    for key, label in OTHER_BLOCKS:
        block = src.get(key)
        if not block or not isinstance(block, dict):
            continue
        extras.append("")
        extras.append("# ---- %s ----" % label)
        for k, v in block.items():
            text = _python_value(v)
            if text:
                extras += _comment_lines("%s: %s" % (k, text))
    if extras:
        lines += extras

    lines.append("")
    return name, "\n".join(lines), len(rules)


def main():
    # 🔴 worker 以 exec 方式跑本文件，请求参数在全局名 `vars` 里（不是环境变量）
    injected = globals().get("vars")
    args = injected if isinstance(injected, dict) else {}
    # 命令行兜底：python convert_legado.py <书源.json> <规则目录>
    src_path = args.get("in") or (sys.argv[1] if len(sys.argv) > 1 else "")
    out_dir = args.get("outDir") or (sys.argv[2] if len(sys.argv) > 2 else "")

    result = {"ok": False, "written": [], "skipped": []}

    if not src_path or not os.path.isfile(src_path):
        result["error"] = "找不到书源文件：" + str(src_path)
        print(json.dumps(result, ensure_ascii=False))
        return
    if not out_dir:
        result["error"] = "没有指定规则目录"
        print(json.dumps(result, ensure_ascii=False))
        return

    os.makedirs(out_dir, exist_ok=True)

    try:
        with open(src_path, "r", encoding="utf-8-sig") as f:
            data = json.load(f)
    except Exception as e:
        result["error"] = "书源 JSON 读不出来：%s" % e
        print(json.dumps(result, ensure_ascii=False))
        return

    if isinstance(data, dict) and isinstance(data.get("sources"), list):
        sources = data["sources"]
    elif isinstance(data, dict):
        sources = [data]
    elif isinstance(data, list):
        sources = data
    else:
        sources = []

    for i, src in enumerate(sources):
        if not isinstance(src, dict):
            result["skipped"].append({"name": "#%d" % (i + 1), "reason": "不是对象"})
            continue
        name = (src.get("bookSourceName") or "").strip()
        if not name and not src.get("bookSourceUrl"):
            result["skipped"].append({"name": "#%d" % (i + 1), "reason": "既没有名称也没有网址"})
            continue

        base_name = _safe_name(name or "书源%d" % (i + 1))
        path = os.path.join(out_dir, base_name + ".py")
        seq = 2
        while os.path.exists(path):
            path = os.path.join(out_dir, "%s_%d.py" % (base_name, seq))
            seq += 1

        try:
            title, text, rule_count = _build(src, base_name)
            with open(path, "w", encoding="utf-8", newline="\n") as f:
                f.write(text)
            result["written"].append({
                "name": os.path.splitext(os.path.basename(path))[0],
                "path": path,
                "rules": rule_count,
            })
        except Exception as e:
            result["skipped"].append({"name": base_name, "reason": str(e)})

    result["ok"] = True
    print(json.dumps(result, ensure_ascii=False))


main()
