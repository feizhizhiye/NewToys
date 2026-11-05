#!/usr/bin/env python3
"""注释契约门禁 · 只判定 git diff 的新增行，阻断式（退出码 1）。

与 lint.py 的分工（唯一源 = skill/references/tech-guard.md §3.1.1）：
  lint.py  = 全量自检，只报告、恒 0 退出（参考，不是门禁）；
  本脚本   = 增量门禁，新增注释违反最小契约即非零退出，供 CI / pre-commit 调用。

用法（仓库根执行）：
  python skill/scripts/comment-gate.py --cached                 # pre-commit：暂存区
  python skill/scripts/comment-gate.py --working               # 本地预检：HEAD -> 工作区
  python skill/scripts/comment-gate.py --base origin/main       # CI：merge-base -> HEAD
  python skill/scripts/comment-gate.py --all                   # 全量诊断（不报 M-02 长段）
"""
from __future__ import annotations

import argparse
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
SRC = ROOT / "src"
DOCS = ROOT / "skill" / "docs"

DOC_BLOCK_MAX = 6
DOC_MARKERS = ("inheritdoc", "lint:")

RE_VERSION = re.compile(r"^\s*//[/!]?\s*(?:v?\d+\.\d+|NT-\d+\s+v?\d+\.\d+|第\s*\d+\s*轮)")
RE_HISTORY = re.compile(r"原来是|此前是|以前是|曾经是|早先是")
RE_NT = re.compile(r"NT-\d{3}")
RE_WHY = re.compile(r"WHY:\s*([A-Z]+-\d{3})")
RE_ANCHOR = re.compile(r"<!--\s*ANCHOR:([A-Z]+-\d{3})\s*-->")
RE_MARKED = re.compile(r"\b(SAFETY|TODO|FIXME|HACK|NOTE)\s*:\s*(.*)$")


def say(s: str = "") -> None:
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except AttributeError:
        pass
    print(s)


def git_diff(args: list[str]) -> str:
    cp = subprocess.run(["git", "-C", str(ROOT), "diff", "-U0", *args, "--", "src"],
                        capture_output=True, text=True, encoding="utf-8", errors="replace")
    if cp.returncode != 0:
        say(f"[FAIL] git diff 失败：{cp.stderr.strip()}")
        sys.exit(2)
    return cp.stdout or ""


def added_lines(diff: str) -> dict[str, set[int]]:
    """解析 -U0 diff，返回 {路径: 新增行号集合}。"""
    out: dict[str, set[int]] = {}
    cur: str | None = None
    no = 0
    for ln in diff.splitlines():
        if ln.startswith("+++ b/"):
            cur = ln[6:].strip()
            out.setdefault(cur, set())
        elif ln.startswith("@@"):
            m = re.search(r"\+(\d+)", ln)
            no = int(m.group(1)) if m else 0
        elif ln.startswith("+") and not ln.startswith("+++"):
            if cur:
                out[cur].add(no)
            no += 1
        elif ln.startswith(" "):
            no += 1
    return {k: v for k, v in out.items() if k.endswith(".cs") and v}


def doc_anchors() -> set[str]:
    ids: set[str] = set()
    for p in DOCS.rglob("*.md"):
        ids.update(RE_ANCHOR.findall(p.read_text(encoding="utf-8-sig", errors="ignore")))
    return ids


def check(path: str, lines: set[int], anchors: set[str], full: bool) -> list[tuple[str, int, str]]:
    f = ROOT / path
    if not f.is_file():
        return []
    text = f.read_text(encoding="utf-8-sig", errors="replace")
    src = text.splitlines()
    bad: list[tuple[str, int, str]] = []

    for no in sorted(lines):
        if no < 1 or no > len(src):
            continue
        ln = src[no - 1]
        if not re.match(r"^\s*//", ln):
            continue
        if RE_VERSION.match(ln):
            bad.append(("M-01", no, "版本 / 轮次前缀叙事 —— 删（git log 已记录）"))
        elif RE_HISTORY.search(ln):
            bad.append(("M-01", no, "历史叙事 —— 写当前结论，不写过去状态"))
        if RE_NT.search(ln):
            bad.append(("M-01", no, "任务编号 —— 删，结论查 framework.md §11.2"))
        for aid in RE_WHY.findall(ln):
            if aid not in anchors:
                bad.append(("M-03", no, f"WHY: {aid} 在 skill/docs 没有 ANCHOR —— 正文必须先落文档"))
        m = RE_MARKED.search(ln)
        if m:
            tag, body = m.group(1), m.group(2).strip()
            if tag in ("SAFETY", "TODO"):
                need = 6 if tag == "SAFETY" else 8
                if len(body) < need:
                    bad.append(("M-04", no, f"{tag}: 缺有效说明（至少 {need} 字，写清前置条件 / 触发条件）"))
            else:
                bad.append(("M-04", no, f"{tag} 不在标记表内 —— 改用 WHY: <ID> / SAFETY: / TODO:"))

    if not full:
        # 只判「新增行自己攒出来的长段」：改动存量长段里的一行不算新写，
        # 存量块按「改到哪清到哪」消化，全量对账由 lint.py 的 C-07 / A-01 负责。
        run: list[str] = []
        start = 0

        def flush() -> None:
            if len(run) >= DOC_BLOCK_MAX and not any(mk in " ".join(run) for mk in DOC_MARKERS):
                bad.append(("M-02", start,
                            f"新增 /// 长段 {len(run)} 行（上限 5）—— 压成 1 行契约 + WHY: <ID>"))
            run.clear()

        for i, ln in enumerate(src, 1):
            if re.match(r"^\s*///", ln) and i in lines:
                if not run:
                    start = i
                run.append(ln)
            else:
                flush()
        flush()
    return bad


def main() -> None:
    ap = argparse.ArgumentParser()
    g = ap.add_mutually_exclusive_group()
    g.add_argument("--cached", action="store_true", help="暂存区（pre-commit 默认）")
    g.add_argument("--working", action="store_true", help="HEAD -> 工作区，本地预检")
    g.add_argument("--base", metavar="REF", help="CI：merge-base(REF, HEAD) -> HEAD")
    g.add_argument("--all", action="store_true", help="全量诊断（跳过 M-02 长段）")
    a = ap.parse_args()

    full = a.all
    if full:
        targets = {str(p.relative_to(ROOT).as_posix()): set(range(1, len(p.read_text(
            encoding="utf-8-sig", errors="replace").splitlines()) + 1))
            for p in SRC.rglob("*.cs") if not any(x in p.parts for x in ("bin", "obj"))}
        say(f"[诊断] 全量 {len(targets)} 个 .cs（M-02 长段不判）")
    elif a.base:
        targets = added_lines(git_diff([f"{a.base}...HEAD"]))
        say(f"[门禁] PR 区间 {a.base}...HEAD · 新增注释行文件 {len(targets)} 个")
    elif a.working:
        targets = added_lines(git_diff(["HEAD"]))
        say(f"[门禁] HEAD -> 工作区 · 新增注释行文件 {len(targets)} 个")
    else:
        targets = added_lines(git_diff(["--cached"]))
        say(f"[门禁] 暂存区 · 新增注释行文件 {len(targets)} 个")

    anchors = doc_anchors()
    findings: list[tuple[str, str, int, str]] = []
    for path in sorted(targets):
        for rid, no, msg in check(path, targets[path], anchors, full):
            findings.append((rid, path, no, msg))

    for rid, path, no, msg in findings:
        say(f"  [{rid}] {path}:{no}  {msg}")
    say(f"=== 违规 {len(findings)} 条 · 锚点 {len(anchors)} 个 ===")
    if findings:
        say("门禁阻断：修完再提交。规则表 = skill/references/tech-guard.md §3.1.1")
        sys.exit(1)
    say("PASS")


if __name__ == "__main__":
    main()
