#!/usr/bin/env python3
"""NewToys 任务上下文打包器（ctx）

一次调用把「本次任务需要的文档段落」拼好打印，替代 AI 逐份 Read 文档。
只读文档，⛔ 不改任何文件。

    python skill/scripts/ctx.py --task ui --module viewer
    python skill/scripts/ctx.py --task feature --module reading
    python skill/scripts/ctx.py --list
    python skill/scripts/ctx.py --doc ui.md --section 12 2.3   # 任意精读

参数
    --task     auto | feature | ui | interaction | fix | usage | verify | card
    --module   script-manager | media-assets | video-tools | web-tools |
               system-tools | backup-center | viewer | reading（也接受模块键别名）
    --max      字符预算，默认 14000（超预算的节只打印标题 + 提示精读）
    --list     列出任务类型与可选模块
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

import facts

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

ROOT = facts.ROOT
DOCS = facts.DOCS

# 中文 / 短名别名是人工知识；规范名一律由 src/Module.* 自举
MODULE_ALIASES = {
    "script-manager": ("script", "脚本中心"),
    "media-assets": ("assets", "媒体资产"),
    "video-tools": ("video", "视频工具"),
    "web-tools": ("web", "网络工具"),
    "system-tools": ("sys", "系统工具"),
    "backup-center": ("backup", "备份中心"),
    "viewer": ("查看器",),
    "reading": ("网络阅读",),
}
MODULES: dict[str, str] = {}
for _asm, _doc in facts.module_dirs().items():
    MODULES[_doc] = _doc
    for _alias in MODULE_ALIASES.get(_doc, ()):
        MODULES[_alias] = _doc

BASE = [
    ("AGENTS.md", ["铁律"]),
    ("index.md", ["二"]),
    ("NewToys-项目状态与路线图.md", ["一"]),
]

RECIPES = {
    "feature": BASE + [
        ("framework.md", ["3.3", "5"]),
        ("ui.md", ["6", "7"]),
        (":module", ["2.5"]),  # :module = 本次模块 md
    ],
    "ui": BASE + [
        ("ui.md", ["1", "2.3", "12"]),
        (":module", ["2.5", "5"]),
    ],
    "interaction": BASE + [
        ("ui.md", ["10"]),
        (":module", ["2.5"]),
    ],
    "fix": BASE + [
        (":module", ["3", "5"]),
        ("framework.md", ["附录 A"]),
    ],
    "usage": [
        ("NewToys-软件说明书.md", ["四", "五"]),
    ],
    "verify": [
        ("AGENTS.md", ["铁律", "交付要求"]),
        ("framework.md", ["9"]),
    ],
    "card": [
        ("AGENTS.md", ["铁律"]),
        ("backlog/README.md", ["四", "三"]),
        ("NewToys-AI协作手册.md", ["流程"]),
    ],
}
RECIPES["auto"] = BASE + [("ui.md", ["10"]), (":module", ["2.5", "3", "5"])]

HEAD = re.compile(r"^(#{2,4})\s+(.*)$")
FENCE = re.compile(r"^\s*(```|~~~)")
CN_NUM = re.compile(r"^([一二三四五六七八九十]+)、")


def read(p: Path) -> str:
    return p.read_text(encoding="utf-8-sig")


def headings(text: str) -> list[tuple[int, int, str]]:
    """返回 (行号, level, 标题)，跳过围栏代码块内的 # 行。"""
    out, in_fence = [], False
    for i, line in enumerate(text.splitlines()):
        if FENCE.match(line):
            in_fence = not in_fence
            continue
        if in_fence:
            continue
        m = HEAD.match(line)
        if m:
            out.append((i, len(m.group(1)), m.group(2).strip()))
    return out


def num_of(title: str) -> str:
    t = title.lstrip("§").strip()
    m = CN_NUM.match(t)
    if m:
        return m.group(1)
    m = re.match(r"(\d+(?:\.\d+)?[a-z]?)", t)
    return m.group(1) if m else ""


def pick(text: str, sel: str) -> list[tuple[str, int, int, str]]:
    """按选择器取节：数字（2 / 2.3）或关键词。返回 [(标题, 起行, 止行, 正文)]。"""
    lines = text.splitlines()
    hs = headings(text)
    hits = []
    for idx, (ln, lvl, title) in enumerate(hs):
        n = num_of(title)
        if title.rstrip().endswith("---"):
            continue
        if re.fullmatch(r"[\d.]+[a-z]?", sel):
            if n != sel:
                continue
        elif CN_NUM.match(title.lstrip("§").strip()) or re.fullmatch(r"[一二三四五六七八九十]+", sel):
            if n != sel:
                continue
        elif sel not in title:
            continue
        end = len(lines)
        for ln2, lvl2, _ in hs[idx + 1:]:
            if lvl2 <= lvl:
                end = ln2
                break
        hits.append((title, ln + 1, end, "\n".join(lines[ln:end]).rstrip()))
    return hits


def shrink(rel: str, title: str, body: str, module: str | None) -> str:
    """全局表按模块裁剪：只留本次模块的行，省掉无关模块的版面。"""
    if not module:
        return body
    lines = body.splitlines()
    if "index.md" in rel and "模块清单" in title:
        head, rest = lines[:4], lines[4:]
        return "\n".join(head + [l for l in rest if f"modules/{module}.md" in l])
    if "项目状态与路线图" in rel:
        head, rest = lines[:4], lines[4:]
        keep = [l for l in rest if re.search(r"NT-\d+", l) or module in l][:10]
        return "\n".join(head + keep)
    return body


def resolve(name: str, module: str | None) -> Path | None:
    if name == ":module":
        if not module:
            return None
        p = DOCS / "modules" / f"{module}.md"
        return p if p.exists() else None
    p = ROOT / name
    return p if p.exists() else DOCS / name


def module_cards(module: str) -> str:
    d = DOCS / "backlog" / module
    if not d.exists():
        return ""
    rows = []
    for p in sorted(d.glob("NT-*.md")):
        first = ""
        status = ""
        for line in read(p).splitlines()[:12]:
            if line.startswith("# ") and not first:
                first = line[2:].strip()
            if not status and re.search(r"📝|✅|🔄|✔", line):
                status = line.strip("> -").strip()
        rows.append(f"- {p.name}: {first} ｜ {status}")
    if not rows:
        return ""
    return "## 任务卡（backlog/%s）\n%s\n" % (module, "\n".join(rows))


def main() -> int:
    ap = argparse.ArgumentParser(description="NewToys 任务上下文打包器")
    ap.add_argument("--task", default="auto", choices=sorted(RECIPES))
    ap.add_argument("--module")
    ap.add_argument("--max", type=int, default=18000, help="字符预算（默认 14000）")
    ap.add_argument("--doc", help="直接指定文档（相对仓库根或 skill/docs 内文件名）")
    ap.add_argument("--section", nargs="*", help="与 --doc 配合：节号（2 / 2.3 / 四）或标题关键词")
    ap.add_argument("--list", action="store_true")
    a = ap.parse_args()

    if a.list:
        print("任务类型：")
        for k in sorted(RECIPES):
            print("  %-12s %s" % (k, " / ".join(f"{f}:{','.join(s)}" for f, s in RECIPES[k])))
        print("\n模块：")
        print("  " + "  ".join(sorted({v for v in MODULES.values()})))
        return 0

    module = MODULES.get((a.module or "").strip(), (a.module or "").strip()) or None
    if module and not (DOCS / "modules" / f"{module}.md").exists():
        print(f"[WARN] 找不到模块文档 modules/{module}.md，模块相关节将跳过", file=sys.stderr)
        module = None

    if a.doc:
        p = resolve(a.doc, module)
        if not p or not p.exists():
            print(f"[FAIL] 找不到文档：{a.doc}", file=sys.stderr)
            return 1
        plan = [(str(p.relative_to(ROOT)), a.section or [])]
        task = "custom"
    else:
        plan = RECIPES[a.task]
        task = a.task

    out = [f"# ctx · task={task} · module={module or '-'} · budget={a.max}",
           "> 本文件由 skill/scripts/ctx.py 生成；已按任务裁剪，未列出的内容表示本次不需要。",
           "> 需要更多时精读：`python skill/scripts/ctx.py --doc <文件> --section <节号>`\n"]
    used = sum(len(x) for x in out)
    omitted = []

    # 第一遍：收集所有节（带优先级）。预算优先级：铁律 > 任务相关节 > 全局面板
    blocks = []
    for fname, sels in plan:
        p = resolve(fname, module)
        if not p or not p.exists():
            continue
        text = read(p)
        rel = str(p.relative_to(ROOT)).replace("\\", "/")
        for sel in sels:
            for title, start, end, body in pick(text, sel):
                body = shrink(rel, title, body, module)
                if not body.strip():
                    continue
                prio = 1
                if "AGENTS.md" in rel and "铁律" in title:
                    prio = 3
                elif "index.md" in rel or "项目状态与路线图" in rel:
                    prio = 0
                elif "/modules/" in rel:
                    prio = 2
                elif "ui.md" in rel and ("§12" in title or "§10" in title):
                    prio = 2
                blocks.append((prio, len(blocks), f"## {rel} › {title}  [L{start}-{end}]", body))

    seen = set()
    blocks = [b for b in blocks if not (b[2] in seen or seen.add(b[2]))]
    kept = []
    for prio, _, head, body in sorted(blocks, key=lambda b: (-b[0], b[1])):
        block = head + "\n" + body + "\n"
        if used + len(block) > a.max:
            omitted.append(f"{head[3:]}（{len(block)} 字符）")
            continue
        kept.append((_, block))
        used += len(block)
    for _, block in sorted(kept):
        out.append(block)

    if module:
        mc = module_cards(module)
        if mc and used + len(mc) <= a.max:
            out.append(mc)
            used += len(mc)

    out.append(f"\n--- 输出 {used} 字符 ---")
    if omitted:
        out.append("超预算已省略（需要时按 --doc/--section 精读）：")
        out += [f"  - {o}" for o in omitted]
    print("\n".join(out))
    return 0


if __name__ == "__main__":
    sys.exit(main())
