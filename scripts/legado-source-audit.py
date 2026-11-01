#!/usr/bin/env python3
"""legado 书源兼容度静态审计。

用途：在不实现求值器的前提下，先算出「一批书源里有多少能跑、卡在哪」。
口径：规则按段（搜索 / 目录 / 正文）分档，段内取最差档；一条源算「可用」= 目标内的段全部通过。

档位
  A  纯选择器（css / xpath / json / regex / 组合子，无 JS）      —— 求值器写完即可跑
  B  JS 且 java.* 全在可等价实现清单内                           —— JS 兼容层写完即可跑
  C  JS 且出现清单外的 java.*（需人工判定能否实现）              —— 补 API 后可能跑
  D  JS 且用到 Android 专有 API（androidId 等，桌面端无法等价）   —— 不可实现
  X  Packages / org.jsoup 直连 / WebView(webJs)                  —— 不做

用法
  python scripts/legado-source-audit.py 书源包.json
  python scripts/legado-source-audit.py 书源目录/ --out report.md
  python scripts/legado-source-audit.py --selftest
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from collections import Counter
from pathlib import Path

# ---------- 判定表 ----------
# 原则：宁窄勿宽。漏收会让 B 档少算（偏保守），多收会高估兼容率（有害）。
# 桌面端可等价实现的 java.* 方法（HTTP / 编码 / 文本 / cookie / 缓存 / 日志 / 选择器）。
IMPLEMENTABLE = {
    "ajax", "get", "post", "connect", "head",
    "base64decode", "base64encode", "base64decodetobytearray",
    "md5encode", "md5encode16",
    "strtobytes", "bytestostr",
    "hexdecodetostring", "hexencodetostring",
    "encodeuri", "urlencode", "htmlformat",
    "timeformat", "log", "logtype", "toast", "longtoast",
    "getcookie", "setcookie", "put",
    "randomuuid",
    # 在 result 上按选择器取值（= 复用自己的求值器，无需 JS 之外的额外能力）
    "getstring", "getelements", "getelement",
    # 纯函数
    "tonumchapter", "aesbase64decodetostring",
    "t2s",   # 繁转简：需字表，工程量大但无技术风险
}

# 语义绑定 Android（设备标识 / WebView / 拉起外部 App），桌面端无法等价。
ANDROID_ONLY = {
    "androidid", "getwebviewua", "startbrowser", "openurl",
    "webview", "startbrowserawait",
}

# `Packages.java.lang.String` 这类包路径会被 java.* 正则误抓，明确排除。
PKG_SEGMENTS = {"lang", "util", "io", "net", "nio", "sql", "math", "text", "security"}

# 书源顶层 key → 段名（其余 key 视作元数据，不参与规则分析）。
SEG_OF_KEY = {
    "ruleSearch": "search", "searchUrl": "search",
    "ruleExplore": "explore", "exploreUrl": "explore",
    "ruleBookInfo": "bookinfo",
    "ruleToc": "toc",
    "ruleContent": "content",
}

SEG_LABEL = {
    "search": "搜索", "explore": "发现", "bookinfo": "详情",
    "toc": "目录", "content": "正文",
}

GRADE_ORDER = {"A": 0, "B": 1, "C": 2, "D": 3, "X": 4}
GRADE_LABEL = {
    "A": "纯选择器", "B": "JS+已知API", "C": "JS+未知API",
    "D": "Android专有", "X": "Packages/WebView",
}


class Feat:
    """一条规则（或一条源的一段）的静态特征。"""

    __slots__ = ("uses_js", "has_expr", "packages", "webview", "calls")

    def __init__(self) -> None:
        self.uses_js = False
        self.has_expr = False
        self.packages = False
        self.webview = False
        self.calls: set[str] = set()

    def merge(self, other: "Feat") -> None:
        self.uses_js |= other.uses_js
        self.has_expr |= other.has_expr
        self.packages |= other.packages
        self.webview |= other.webview
        self.calls |= other.calls


def _is_js_head(text: str) -> bool:
    low = text.lstrip().lower()
    return low.startswith("@js:") or low.startswith("<js>")


def _scan_calls(text: str, f: Feat) -> None:
    """只收集 java.* / 包树特征，不改 uses_js —— 选择器本体也可能出现这些字样。"""
    for name in re.findall(r"\bjava\s*\.\s*([A-Za-z_]\w*)", text):
        low = name.lower()
        if low not in PKG_SEGMENTS:
            f.calls.add(low)
    # Packages / importPackage / 直接 org.jsoup.* 都要求宿主暴露 Java 包树
    if re.search(r"\bPackages\b|\bimportPackage\b|\borg\.jsoup\b|\bimportClass\b", text):
        f.packages = True


def _scan_js(text: str, f: Feat) -> None:
    f.uses_js = True
    _scan_calls(text, f)


def analyze_rule(text: str, key: str = "") -> Feat:
    """静态解析一条规则字符串。

    🔴 求值顺序与 legado 对齐：`{{ }}` → `||` → `&&` → `##`。
    🔴 以 `@js:` / `<js>` 开头的片段**不拆 `||`**（那里的 || 是 JS 逻辑或）。
    """
    f = Feat()
    t = (text or "").strip()
    if not t:
        return f

    if key.lower() == "webjs":
        f.webview = True

    # 🔴 `<js>…</js>` 可出现在规则任意位置（常见于尾部做二次处理，如 `<js>java.t2s(result)</js>`），
    #    必须先整块摘掉再拆分隔符：块内是 JS，`||` 是逻辑或，`{{}}` 也属 JS 语法。
    def _take_js_block(m: re.Match) -> str:
        _scan_js(m.group(1), f)
        return " "

    t = re.sub(r"<js>(.*?)</js>", _take_js_block, t, flags=re.S | re.I)

    for m in re.finditer(r"\{\{(.*?)\}\}", t, re.S):
        f.has_expr = True
        inner = m.group(1)
        before = len(f.calls)
        _scan_calls(inner, f)
        # 🔴 `{{key}}` / `{{page}}` 只是变量插值，不该算 JS 门槛；
        #    出现 java.* 或函数调用形态才真要 JS 引擎。
        if len(f.calls) > before or re.search(r"[A-Za-z_]\w*\s*\(", inner):
            f.uses_js = True

    # 表达式可能是 JS，先把它们摘掉再按规则分隔符拆，避免里面的 || / && 干扰
    t2 = re.sub(r"\{\{.*?\}\}", " ", t, flags=re.S)

    if _is_js_head(t2):
        _scan_js(t2, f)
        return f

    for cand in t2.split("||"):
        cand = cand.strip()
        if not cand:
            continue
        if _is_js_head(cand):
            _scan_js(cand, f)
            continue
        for link in cand.split("&&"):
            link = link.strip()
            if not link:
                continue
            if _is_js_head(link):
                _scan_js(link, f)
                continue
            # `##` 之后是正则 + 替换串，不是规则本体
            _scan_calls(link.split("##")[0], f)

    return f


def grade(f: Feat) -> str:
    if f.webview or f.packages:
        return "X"
    if not f.uses_js:
        return "A"
    names = {c.lower() for c in f.calls}
    if names & ANDROID_ONLY:
        return "D"
    if names - IMPLEMENTABLE - ANDROID_ONLY:
        return "C"
    return "B"


def collect_rules(src: dict) -> list[tuple[str, str, str]]:
    """展平书源 → [(段名, 字段名, 规则文本)]。"""
    out: list[tuple[str, str, str]] = []

    def walk(node, seg: str, key: str) -> None:
        if isinstance(node, dict):
            for k, v in node.items():
                walk(v, SEG_OF_KEY.get(k, seg), k)
        elif isinstance(node, list):
            for v in node:
                walk(v, seg, key)
        elif isinstance(node, str) and seg:
            out.append((seg, key, node))

    walk(src, "", "")
    return out


def audit_one(src: dict) -> dict:
    name = src.get("bookSourceName") or src.get("bookSourceUrl") or "(未命名)"
    url = src.get("bookSourceUrl") or ""
    seg_feat: dict[str, Feat] = {}
    for seg, key, text in collect_rules(src):
        seg_feat.setdefault(seg, Feat()).merge(analyze_rule(text, key))

    seg_grade = {seg: grade(f) for seg, f in seg_feat.items()}
    seg_reason: dict[str, str] = {}
    for seg, f in seg_feat.items():
        g = seg_grade[seg]
        if g == "C":
            unknown = sorted({c.lower() for c in f.calls} - IMPLEMENTABLE - ANDROID_ONLY)
            seg_reason[seg] = "未知 API: " + ", ".join("java." + u for u in unknown)
        elif g == "D":
            bad = sorted({c.lower() for c in f.calls} & ANDROID_ONLY)
            seg_reason[seg] = "Android: " + ", ".join("java." + b for b in bad)
        elif g == "X":
            seg_reason[seg] = "Packages/WebView"
        elif g == "B":
            names = ", ".join("java." + c for c in sorted(f.calls))
            seg_reason[seg] = ("JS: " + names) if names else "JS（未调 java.*）"

    # 兼容性口径只看三段（搜索 / 目录 / 正文）；发现页与详情页单独观察，不拉低主分数
    core = ("search", "toc", "content")
    core_grades = [seg_grade[s] for s in core if s in seg_grade] or ["A"]
    worst = max(core_grades, key=lambda g: GRADE_ORDER[g])
    worst_all = max(seg_grade.values(), key=lambda g: GRADE_ORDER[g]) if seg_grade else "A"
    return {
        "name": name,
        "url": url,
        "type": src.get("bookSourceType", 0),
        "enabled": src.get("enabled", True) is not False,
        "segments": seg_grade,
        "worst": worst,
        "worst_all": worst_all,
        "reasons": seg_reason,
        "calls": sorted({c.lower() for f in seg_feat.values() for c in f.calls}),
    }


def _at_most(row: dict, ceiling: str, segs: tuple[str, ...]) -> bool:
    return all(
        GRADE_ORDER[row["segments"].get(s, "A")] <= GRADE_ORDER[ceiling] for s in segs
    )


def report(rows: list[dict]) -> dict:
    total = len(rows)
    enabled = [r for r in rows if r["enabled"]]
    dist = Counter(r["worst"] for r in rows)

    seg_dist: dict[str, Counter] = {}
    for seg in ("search", "toc", "content", "bookinfo", "explore"):
        c = Counter(r["segments"].get(seg, "A") for r in rows if seg in r["segments"])
        if c:
            seg_dist[seg] = c

    unknown_calls = Counter()
    android_calls = Counter()
    all_calls = Counter()
    for r in rows:
        for c in r["calls"]:
            all_calls[c] += 1
            if c in ANDROID_ONLY:
                android_calls[c] += 1
            elif c not in IMPLEMENTABLE:
                unknown_calls[c] += 1

    def ok(ceiling: str, segs: tuple[str, ...]) -> int:
        return sum(1 for r in rows if _at_most(r, ceiling, segs))

    core = ("search", "toc", "content")
    return {
        "total": total,
        "enabled": len(enabled),
        "dist": dist,
        "seg_dist": seg_dist,
        "now": ok("A", core),
        "after_shim": ok("B", core),
        "after_extend": ok("C", core),
        "never": total - sum(1 for r in rows if _at_most(r, "C", core)),
        "unknown_calls": unknown_calls,
        "android_calls": android_calls,
        "all_calls": all_calls,
        "explore_bad_extra": sum(
            1 for r in rows if r["worst"] == "A" and r["worst_all"] != "A"
        ),
        "types": Counter(r["type"] for r in rows),
        "rows": rows,
    }


def _pct(n: int, d: int) -> str:
    return f"{n}/{d} ({n * 100.0 / d:.1f}%)" if d else f"{n}/0"


def render_markdown(rep: dict, top: int) -> str:
    t = rep["total"]
    lines = [
        "# legado 书源兼容度审计",
        "",
        f"- 源总数：**{t}**（启用 {rep['enabled']}）",
        f"- 现在可跑（纯选择器，三段全 A）：**{_pct(rep['now'], t)}**",
        f"- JS 兼容层完成后可跑（三段全 ≤B）：**{_pct(rep['after_shim'], t)}**",
        f"- 补齐未知 API 后上限（三段全 ≤C）：**{_pct(rep['after_extend'], t)}**",
        f"- 无论如何跑不通（含 Android/WebView/Packages）：**{_pct(rep['never'], t)}**",
        "",
        "## 按最差段分档",
        "",
        "口径：只看搜索 / 目录 / 正文三段，段内取最差档。",
        "",
        "| 档 | 含义 | 条数 |",
        "|---|---|---|",
    ]
    for g in ("A", "B", "C", "D", "X"):
        lines.append(f"| {g} | {GRADE_LABEL[g]} | {rep['dist'].get(g, 0)} |")
    if rep["explore_bad_extra"]:
        lines.append("")
        lines.append(
            f"> 另有 {rep['explore_bad_extra']} 条源「搜索/目录/正文」全 A，"
            "但发现页或详情页用到 JS —— 不影响读，只影响发现/详情。"
        )

    lines += ["", "## 分段通过情况", "", "| 段 | " + " | ".join("ABCDX") + " |", "|---|---|---|---|---|---|"]
    for seg, c in rep["seg_dist"].items():
        lines.append(
            f"| {SEG_LABEL.get(seg, seg)} | " + " | ".join(str(c.get(g, 0)) for g in "ABCDX") + " |"
        )

    if rep["all_calls"]:
        lines += [
            "", f"## 依赖的 java.* API（L1 施工清单，共 {len(rep['all_calls'])} 个）", "",
            "| API | 源数 | 判定 |", "|---|---|---|",
        ]
        for name, n in rep["all_calls"].most_common():
            verdict = "不可实现" if name in ANDROID_ONLY else "可实现"
            lines.append(f"| `java.{name}` | {n} | {verdict} |")

    if rep["android_calls"]:
        lines += ["", "## Android 专有 API（桌面端不可等价）", "", "| API | 出现源数 |", "|---|---|"]
        for name, n in rep["android_calls"].most_common():
            lines.append(f"| `java.{name}` | {n} |")

    type_label = {0: "文本", 1: "音频", 2: "图片", 3: "文件"}
    if rep["types"]:
        lines += ["", "## 书源类型分布", "", "| 类型 | 条数 |", "|---|---|"]
        for k, n in sorted(rep["types"].items()):
            lines.append(f"| {type_label.get(k, str(k))} | {n} |")

    bad = [r for r in rep["rows"] if r["worst"] != "A"]
    bad.sort(key=lambda r: -GRADE_ORDER[r["worst"]])
    if bad:
        lines += [
            "", "## 非 A 档明细", "",
            "B = 等 JS 兼容层；D/X = 不可实现。按严重度排序。", "",
            "| 源 | 搜索 | 目录 | 正文 | 原因 |", "|---|---|---|---|---|",
        ]
        for r in bad[:60]:
            segs = r["segments"]
            cells = [segs.get(s, "A") for s in ("search", "toc", "content")]
            reason = "；".join(f"{SEG_LABEL.get(k, k)}: {v}" for k, v in r["reasons"].items())
            lines.append(
                f"| {r['name']} | " + " | ".join(cells) + f" | {reason or '—'} |"
            )
        if len(bad) > 60:
            lines.append(f"| …共 {len(bad)} 条 | | | | |")
    return "\n".join(lines) + "\n"


# ---------- 输入 ----------
def load_sources(path: Path) -> list[dict]:
    raw = path.read_bytes()
    text = raw.decode("utf-8-sig", errors="replace")
    try:
        doc = json.loads(text)
    except json.JSONDecodeError as ex:
        raise SystemExit(f"{path} 不是合法 JSON（{ex}）；gzip 压缩包请先解压。") from ex

    if isinstance(doc, list):
        items = doc
    elif isinstance(doc, dict) and isinstance(doc.get("bookSources"), list):
        items = doc["bookSources"]
    elif isinstance(doc, dict):
        items = [doc]
    else:
        items = []
    return [x for x in items if isinstance(x, dict)]


def gather(target: Path) -> list[tuple[Path, dict]]:
    files = (
        sorted(target.glob("*.json")) if target.is_dir() else [target]
    )
    out: list[tuple[Path, dict]] = []
    for f in files:
        try:
            for src in load_sources(f):
                out.append((f, src))
        except SystemExit as ex:
            print(f"[跳过] {ex}", file=sys.stderr)
    return out


# ---------- 自测样例 ----------
SELFTEST = [
    {"bookSourceName": "纯选择器", "bookSourceUrl": "a", "bookSourceType": 0,
     "searchUrl": "https://a.com/s?q={{key}}",
     "ruleSearch": {"bookList": "@css:ul li", "name": "@css:a@text", "bookUrl": "@css:a@href"},
     "ruleToc": {"chapterList": "@css:#list dd a", "chapterName": "@text", "chapterUrl": "@href"},
     "ruleContent": {"content": "@css:#content@html"}},
    {"bookSourceName": "JS常用API", "bookSourceUrl": "b", "bookSourceType": 0,
     "ruleSearch": {"bookList": "@js:java.ajax(baseUrl + '/api')"},
     "ruleToc": {"chapterList": "@css:dd"},
     "ruleContent": {"content": "@js:java.base64Decode(result)"}},
    {"bookSourceName": "JS未知API", "bookSourceUrl": "c", "bookSourceType": 0,
     "ruleSearch": {"bookList": "@css:li"},
     "ruleToc": {"chapterList": "@css:dd"},
     "ruleContent": {"content": "@js:java.decryptAes(result, 'k')"}},
    {"bookSourceName": "Android专有", "bookSourceUrl": "d", "bookSourceType": 0,
     "ruleSearch": {"bookList": "@css:li"},
     "ruleToc": {"chapterList": "@css:dd"},
     "ruleContent": {"content": "@js:java.androidId()"}},
    {"bookSourceName": "WebView登录", "bookSourceUrl": "e", "bookSourceType": 0,
     "ruleSearch": {"bookList": "@css:li"},
     "ruleToc": {"chapterList": "@css:dd"},
     "ruleContent": {"content": "@css:#c", "webJs": "window.scrollTo(0,9999)"}},
    {"bookSourceName": "音频源", "bookSourceUrl": "f", "bookSourceType": 1,
     "ruleSearch": {"bookList": "@css:li"},
     "ruleToc": {"chapterList": "@css:dd"},
     "ruleContent": {"content": "@css:audio@src"}},
]


def main() -> int:
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass

    ap = argparse.ArgumentParser(description="legado 书源兼容度静态审计")
    ap.add_argument("target", nargs="?", help="书源 JSON 文件或目录")
    ap.add_argument("--selftest", action="store_true", help="跑内置样例（不需要书源包）")
    ap.add_argument("--out", default="", help="报告落盘路径（.md）")
    ap.add_argument("--top", type=int, default=15, help="待补 API 展示条数")
    args = ap.parse_args()

    if args.selftest:
        rows = [audit_one(s) for s in SELFTEST]
    elif args.target:
        p = Path(args.target)
        if not p.exists():
            raise SystemExit(f"找不到：{p}")
        rows = [audit_one(src) for _, src in gather(p)]
    else:
        ap.print_help()
        return 2

    if not rows:
        print("没有解析到任何书源。")
        return 1

    rep = report(rows)
    md = render_markdown(rep, args.top)

    t = rep["total"]
    print(f"源总数 {t}（启用 {rep['enabled']}）")
    print(f"  现在可跑 A            {_pct(rep['now'], t)}")
    print(f"  JS 兼容层后 ≤B        {_pct(rep['after_shim'], t)}")
    print(f"  补 API 后 ≤C          {_pct(rep['after_extend'], t)}")
    print(f"  跑不通 D/X            {_pct(rep['never'], t)}")
    print("  分档：" + "  ".join(f"{g}={rep['dist'].get(g, 0)}" for g in "ABCDX"))
    if rep["explore_bad_extra"]:
        print(f"  （其中 {rep['explore_bad_extra']} 条三段全 A，但发现/详情页用了 JS）")
    if rep["unknown_calls"]:
        top = ", ".join(f"java.{n}×{c}" for n, c in rep["unknown_calls"].most_common(8))
        print("  待补 API Top8：" + top)

    if args.out:
        out = Path(args.out)
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(md, encoding="utf-8")
        print(f"报告已写入 {out}")
    elif args.selftest:
        print("\n" + md)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
