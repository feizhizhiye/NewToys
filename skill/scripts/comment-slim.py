#!/usr/bin/env python3
"""方案1 · src 注释瘦身：短契约保留，长段分流（压缩 / WHY 蒸馏 / 删叙事）。

判据（与 lint.py --only comments 的 C-07 对齐）：
  * 连续 `///` 块 <= 5 行 = 短契约，原样保留；
  * 连续 `///` 块 >= 6 行 = 长段，压成 1 行 `/// <summary>…（WHY: ID）</summary>`；
  * 长段里的高信号 WHY 行蒸馏进权威文档，用 `<!-- ANCHOR:ID -->` 标记，代码与文档双向对账；
  * 含机器标记（inheritdoc / lint:）的块走白名单，绝不改。

用法：
  python skill/scripts/comment-slim.py --batch shared             # dry-run，只报统计
  python skill/scripts/comment-slim.py --batch shared --apply     # 改 src + 写文档蒸馏块
"""
from __future__ import annotations

import argparse
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parents[2]
SRC = ROOT / "src"
DOCS = ROOT / "skill" / "docs"

MIN_BLOCK = 6
MARKERS = ("inheritdoc", "lint:")

BATCHES: dict[str, dict] = {
    "shared": {"dirs": ["Shared"], "doc": "framework.md", "prefix": "SH"},
    "core": {"dirs": ["Core"], "doc": "framework.md", "prefix": "CR"},
    "shell": {"dirs": ["Shell"], "doc": "framework.md", "prefix": "SHL"},
    "ui": {"dirs": ["UI"], "doc": "ui.md", "prefix": "UI"},
    "modules": {"dirs": None, "doc": None, "prefix": None},
}

MODULE_PREFIX = {
    "Module.MediaAssets": "MA", "Module.ScriptManager": "SM",
    "Module.VideoTools": "VT", "Module.Reading": "RD",
    "Module.Viewer": "VW", "Module.WebTools": "WT",
    "Module.BackupCenter": "BC", "Module.SystemTools": "ST",
    "NewToys.Tests": "TS",
}
MODULE_DOC = {
    "mediaassets": "media-assets", "scriptmanager": "script-manager",
    "videotools": "video-tools", "reading": "reading", "viewer": "viewer",
    "webtools": "web-tools", "backupcenter": "backup-center",
    "systemtools": "system-tools",
}

XML_TAGS = re.compile(r"</?(?:summary|para|remarks|example|b|i|c|list|item|code)\s*/?>")
# <param>/<returns> 等是「成员级契约」，⛔ 不能并进 <summary>（并进去 = 非法 XML doc，参数说明丢失）。
SIG_LINE = re.compile(r"^\s*///\s*<(?:param|returns|typeparam|exception|value)\b")
CREF = re.compile(r'<see\s+cref="([^"]+)"\s*/>')
DATE = re.compile(r"20\d{2}-\d{2}-\d{2}")
VERSION = re.compile(r"v\d+\.\d+(?:\.\d+)?|NT-\d{3}\b")
# 裸语义版本（12.1.3）一律降成 12.1.x：文档里的具体版本号会和 props 对账冲突，且是时间点事实。
BARE_VERSION = re.compile(r"(?<![\w.§])(\d+)\.(\d+)\.\d+(?![\w.])")
NARRATIVE = re.compile(r"用户拍板|第\s*\d+\s*种|此前是|以前是|曾经是|早先是|实测[三四五六七八九十]轮")
WHY_HINT = re.compile(
    r"🔴|⚠|⛔|必须|不许|否则|契约|一律|不要|注意|因为|为什么|坑|禁止|避免|兜底|根因|只有|⛔|不许|⛔|不参与|不抛|不做|不会|无法"
)
MARKER_LINE = "<!-- WHY-DISTILL:{batch}:start -->"
MARKER_END = "<!-- WHY-DISTILL:{batch}:end -->"


def read_text(p: pathlib.Path) -> tuple[str, bool, str]:
    raw = p.read_bytes()
    bom = raw.startswith(b"\xef\xbb\xbf")
    text = raw.decode("utf-8-sig")
    lines = text.splitlines(keepends=True)
    nl = "\r\n" if lines and lines[0].endswith("\r\n") else "\n"
    return text, bom, nl


def write_text(p: pathlib.Path, body: str, bom: bool) -> None:
    p.write_bytes((b"\xef\xbb\xbf" if bom else b"") + body.encode("utf-8"))


def clean_contract(line: str) -> str:
    s = line.strip()
    s = re.sub(r"^\s*///\s?", "", s)
    s = XML_TAGS.sub("", s)
    s = CREF.sub(r"`\1`", s)
    s = s.replace("**", "").replace("`", "")
    s = re.sub(r"；\s*20\d{2}-\d{2}-\d{2}[^；。）]*", "", s)
    s = re.sub(r"（[^）]*20\d{2}-\d{2}-\d{2}[^）]*）", "", s)
    s = VERSION.sub("", s)
    s = BARE_VERSION.sub(r"\1.\2.x", s)
    s = DATE.sub("", s)
    s = re.sub(r"（\s*(?:增|新增)[^）]*）", "", s)
    s = re.sub(r"（\s*[；;，,、]*\s*）", "", s)
    s = re.sub(r"；\s*；", "；", s)
    s = re.sub(r"\s{2,}", " ", s).strip()
    s = re.sub(r"^[\s，,；;、]+", "", s)
    return s


def clean_why(line: str) -> str:
    s = line.strip()
    s = re.sub(r"^\s*///\s?", "", s)
    s = XML_TAGS.sub("", s)
    s = CREF.sub(r"`\1`", s)
    s = s.replace("**", "")
    s = VERSION.sub("", s)
    s = BARE_VERSION.sub(r"\1.\2.x", s)
    s = DATE.sub("", s)
    s = re.sub(r"\s{2,}", " ", s).strip()
    return s


def split_block(lines: list[str]) -> tuple[list[str], str, list[str], list[str]]:
    """返回（正文行, 契约句, WHY 行列表, 成员级契约行）。"""
    keep = [l for l in lines if SIG_LINE.match(l)]
    body = [l for l in lines
            if not SIG_LINE.match(l)
            and not re.match(r"^\s*///\s*</?(summary|para|remarks|example)\s*/?>\s*$", l.strip("\r\n"))]
    cleaned = [clean_contract(l) for l in body]
    contract = next((c for c in cleaned if len(c) >= 8), cleaned[0] if cleaned else "")
    used = max(1, cleaned.index(contract) + 1) if contract in cleaned else 1
    while (contract and not re.search(r"[。；;!?]$", contract)
           and used < len(cleaned) and used <= 3
           and len(cleaned[used]) >= 4 and not re.match(r"^[🔴⚠⛔]", cleaned[used])):
        contract = (contract + " " + cleaned[used]).strip()
        used += 1
    if len(contract) > 190:
        cut = contract.find("。")
        contract = contract[: cut + 1] if 0 < cut < 190 else contract[:190] + "…"
    why: list[str] = []
    for l in body[used:]:
        t = l.strip()
        if any(m in t for m in MARKERS) or not WHY_HINT.search(t):
            continue
        if NARRATIVE.search(t):
            continue
        w = clean_why(l)
        if len(w) < 6:
            continue
        why.append(w)
        if len(why) >= 6:
            break
    return body, contract, why, keep


def doc_target(batch: str, assembly: str) -> tuple[pathlib.Path, str, str]:
    if batch == "modules":
        if assembly == "NewToys.Tests":
            # 测试工程不是业务模块，没有 modules/*.md ⇒ WHY 蒸馏进 framework.md（标记 WHY-DISTILL:tests）
            return DOCS / "framework.md", "TS", "tests"
        key = assembly.removeprefix("Module.").lower()
        stem = MODULE_DOC.get(key, key)
        return DOCS / "modules" / f"{stem}.md", MODULE_PREFIX.get(assembly, "MOD"), f"modules-{stem}"
    cfg = BATCHES[batch]
    return DOCS / cfg["doc"], cfg["prefix"], batch


def distill_section(batch: str, entries: list[tuple[str, str, list[str]]]) -> str:
    out = [MARKER_LINE.format(batch=batch), ""]
    out.append("> 由 `skill/scripts/comment-slim.py` 生成（方案1 · 白名单与锚点口径见 `tech-guard.md` §3.1）。")
    out.append("> 代码里只留 1 行契约 + `WHY: <ID>`；此处是 ID 对应的 WHY 正文。`lint.py --only comments` 双向对账。")
    out.append("")
    for anchor, loc, why in entries:
        out.append(f"<!-- ANCHOR:{anchor} -->")
        out.append(f"- **{anchor}** · `{loc}`")
        for w in why:
            out.append(f"  - {w}")
        out.append("")
    out.append(MARKER_END.format(batch=batch))
    return "\r\n".join(out) + "\r\n"


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--batch", required=True, choices=sorted(BATCHES))
    ap.add_argument("--apply", action="store_true")
    args = ap.parse_args()
    batch = args.batch

    cfg = BATCHES[batch]
    dirs = cfg["dirs"] or sorted(
        d.name for d in SRC.iterdir()
        if d.is_dir() and (d.name.startswith("Module.") or d.name == "NewToys.Tests")
    )
    files: list[pathlib.Path] = []
    for d in dirs:
        files += [p for p in (SRC / d).rglob("*.cs") if not any(x in p.parts for x in ("bin", "obj"))]
    files.sort()

    per_doc: dict[pathlib.Path, list[tuple[str, str, list[str]]]] = {}
    doc_keys: dict[pathlib.Path, str] = {}
    counters = {"blocks": 0, "slim": 0, "whitelist": 0, "kept": 0, "removed_lines": 0, "why_lines": 0}
    seq: dict[str, int] = {}

    for p in files:
        text, bom, nl = read_text(p)
        lines = text.splitlines(keepends=True)
        out: list[str] = []
        i = 0
        changed = False
        while i < len(lines):
            if not re.match(r"^\s*///", lines[i]):
                out.append(lines[i])
                i += 1
                continue
            j = i
            while j < len(lines) and re.match(r"^\s*///", lines[j]):
                j += 1
            block = lines[i:j]
            counters["blocks"] += 1
            joined = " ".join(x.strip() for x in block)
            if any(m in joined for m in MARKERS):
                counters["whitelist"] += 1
                out += block
                i = j
                continue
            if len(block) < MIN_BLOCK:
                counters["kept"] += 1
                out += block
                i = j
                continue

            indent = re.match(r"^\s*", block[0]).group(0)
            body, contract, why, sig = split_block(block)
            if len(block) - 1 - len(sig) <= 0:
                # 已经是「1 行 summary + 成员级契约行」，再压不会变短 ⇒ 原样保留（避免无意义改写与编号漂移）
                counters["kept"] += 1
                out += block
                i = j
                continue
            doc, prefix, key = doc_target(batch, p.relative_to(SRC).parts[0])
            doc_keys.setdefault(doc, key)
            seq[prefix] = seq.get(prefix, 0) + 1
            anchor = f"{prefix}-{seq[prefix]:03d}"
            if contract and not re.search(r"[。；;!?]$", contract):
                contract += "。"
            tail = f"（WHY: {anchor}）" if why else ""
            if contract:
                new_line = f"{indent}/// <summary>{contract}{tail}</summary>{nl}"
            else:
                new_line = f"{indent}/// <summary>见接口契约（{anchor}）。</summary>{nl}"
            out.append(new_line)
            out += sig
            if why:
                loc = p.relative_to(ROOT).as_posix()
                per_doc.setdefault(doc, []).append((anchor, loc, why))
                counters["why_lines"] += len(why)
            counters["slim"] += 1
            counters["removed_lines"] += len(block) - 1 - len(sig)
            changed = True
            i = j
        if changed and args.apply:
            write_text(p, "".join(out), bom)

    print(f"批次 {batch} · 文件 {len(files)} · /// 块 {counters['blocks']}")
    print(f"  短契约保留 {counters['kept']} · 机器标记白名单 {counters['whitelist']}")
    print(f"  长段压缩 {counters['slim']} · 删除 {counters['removed_lines']} 行 · WHY 蒸馏 {counters['why_lines']} 行")
    for doc, entries in sorted(per_doc.items()):
        print(f"  → {doc.relative_to(ROOT)} : {len(entries)} 个 anchor")
        if args.apply:
            k = doc_keys[doc]
            text, bom, nl = read_text(doc)
            section = distill_section(k, entries)
            if nl == "\n":
                section = section.replace("\r\n", "\n")
            start = MARKER_LINE.format(batch=k)
            end = MARKER_END.format(batch=k)
            if start in text and end in text:
                head, rest = text.split(start, 1)
                _, tail = rest.split(end, 1)
                text = head + section.rstrip("\r\n") + tail
            else:
                text = text.rstrip("\r\n") + "\r\n\r\n" + section
            write_text(doc, text, bom)
    if not args.apply:
        print("  [dry-run] 未落盘；加 --apply 才改 src 与文档")


if __name__ == "__main__":
    main()
