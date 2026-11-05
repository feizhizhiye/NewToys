#!/usr/bin/env python3
"""NewToys 静态验收（accept）—— 一键编排静态检查 / 文档一致性 / 改动范围。

⛔ 纯静态：不含构建 / 测试 / 产物核对 / 冒烟；编译归 scripts/build.ps1，运行归 scripts/run.ps1。
⛔ 不改 src/；写操作只有两处：--only fix 调 docs-sync.py --fix 重写文档 AUTO 块、--report 写报告。

用法：
  python skill/scripts/accept.py                  # 非交互 = 全部检查；终端里无参数则弹菜单
  python skill/scripts/accept.py --menu
  python skill/scripts/accept.py --only all|static|docs|fix|scope|family|scripts
  python skill/scripts/accept.py --all           # WARN 明细全展开（透传给子检查）
  python skill/scripts/accept.py --brief         # 一行结论（强制写 artifacts/accept-report.md）
  python skill/scripts/accept.py --report
  python skill/scripts/accept.py --skip-static
  python skill/scripts/accept.py --skip-build    # 兼容旧命令；已无构建步骤，传与不传等价
  python skill/scripts/accept.py --no-pause
退出码：0 = 无 FAIL；1 = 存在 FAIL 或脚本自身出错。
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "src"
SKILL_SCRIPTS = ROOT / "skill" / "scripts"
LINT_PY = SKILL_SCRIPTS / "lint.py"
SYNC_PY = SKILL_SCRIPTS / "docs-sync.py"
REPORT = ROOT / "artifacts" / "accept-report.md"

# 每项 = (显示名, lint.py 的 --only 键)。计数读 lint.py 产出的 __LINT_META__ 行，
# ⛔ 不再用中文汇总行正则 —— 措辞一改就会静默假 PASS。
STATIC: list[tuple[str, str]] = [
    ("AXAML 合法性 / 配对 / 主题键", "axaml"),
    ("XAML 属性名（Avalonia ≠ WPF）", "props"),
    ("技术栈硬伤（G-01~G-11 + G-14~G-17）", "techstack"),
    ("详情卡绑定路径", "detail-paths"),
    ("视频详情卡同源（预览 = 批量）", "video-card"),
    ("注释健康度 / View 类名唯一性", "comments"),
    ("页面契约（View ↔ ViewModel / 菜单注册）", "view-pairs"),
    ("区域色差（相邻区域不同档）", "regions"),
    ("区域色差（页面级 · 两套皮肤）", "regionpages"),
    ("同族页矩阵对账（范本 §八 ⇄ src）", "family"),
    ("工具脚本语言统一（skill/scripts 仅 .py）", "scripts"),
    ("代码约定（主题字面量 / 页 Key / BOM / namespace）", "conventions"),
]

# 存量告警基线（P0 实测）。超过基线 = 新引入告警 ⇒ 阻断；存量不追溯。
WARN_BASELINE = {"techstack": 7, "comments": 0}
DOCS_WARN_BASELINE = 1

MENU = [
    ("1", dict(only="all", report=True, brief=False), "全部检查（静态 + 文档一致性 + 改动范围），完整表格"),
    ("2", dict(only="all", report=False, brief=True), "全部检查，一行结论（快速看通过没通过）"),
    ("3", dict(only="static", report=False, brief=False), "只跑静态检查（AXAML / 属性名 / 技术栈 / 注释 / 页面契约 / 脚本语言）"),
    ("4", dict(only="docs", report=False, brief=False), "只跑文档一致性（AUTO 块 / 菜单 Key / 路径引用 / 卡窗口 / NT 编号）"),
    ("5", dict(only="fix", report=False, brief=False), "修复文档 AUTO 块后复查（docs-sync --fix，会写 index/framework/台账）"),
    ("6", dict(only="scope", report=False, brief=False), "只跑铁律 1（改动是否只在 src/ 与授权区）"),
    ("7", dict(only="scripts", report=False, brief=False), "只跑工具脚本语言检查（skill/scripts 仅 .py）"),
]


def _utf8_stdout() -> None:
    for out in (sys.stdout, sys.stderr):
        try:
            out.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass


def _py_run(script: Path, *args: str) -> str:
    env = dict(os.environ, PYTHONIOENCODING="utf-8")
    try:
        cp = subprocess.run([sys.executable, str(script), *args], cwd=str(ROOT), env=env,
                            capture_output=True, text=True, encoding="utf-8", errors="replace")
        return (cp.stdout or "") + (cp.stderr or "")
    except OSError as e:
        return f"[ERROR] 无法启动 Python：{e}"


def _read_key() -> str:
    if not sys.stdin.isatty():
        try:
            return input().strip()[:1]
        except EOFError:
            return ""
    try:
        import msvcrt  # Windows：单键，不用回车
        ch = msvcrt.getch()
        return ch.decode("utf-8", "replace") if isinstance(ch, bytes) else str(ch)
    except Exception:
        try:
            return input().strip()[:1]
        except EOFError:
            return ""


def _show_menu() -> dict | str | None:
    print("\n  NewToys 静态验收 —— 选择要跑的项目")
    print("  ────────────────────────────────────────────")
    for key, _sel, label in MENU:
        print(f"   {key})  {label}")
    print("   0)  退出")
    print("\n  按数字键即可（不用回车）… ", end="", flush=True)
    k = _read_key()
    print(k)
    if k in ("", "\x1b"):
        return None
    if k == "0":
        return "exit"
    for key, sel, _label in MENU:
        if key == k:
            return sel
    return None


def _add(rows: list[dict], area: str, name: str, status: str, detail: str, raw: str = "") -> None:
    rows.append(dict(area=area, name=name, status=status, detail=detail, raw=raw))


def _run(opts: dict) -> tuple[list[dict], int]:
    rows: list[dict] = []
    only = opts["only"]
    brief = opts["brief"]
    all_ = opts["all"]
    run_static = (not opts["skip_static"]) and only in ("all", "static", "family", "scripts")
    run_docs = (not opts["skip_static"]) and only in ("all", "docs", "fix")
    run_scope = only in ("all", "scope")

    if not brief:
        print("\n════════ NewToys 静态验收 ════════")
        print(f"仓库根 : {ROOT}")
    if not SRC.is_dir():
        print("[FAIL] 找不到 src/ —— 请从仓库内运行本脚本")
        return rows, 1

    # 1) 静态检查：逐项调 lint.py --only <key>，按汇总行数判 PASS/WARN/FAIL
    if run_static:
        if not brief:
            print("\n──── 1. 静态检查 ────")
        if not LINT_PY.is_file():
            _add(rows, "静态检查", "全部", "SKIP", "lint.py 不存在")
        for name, key in STATIC:
            if only in ("family", "scripts") and key != only:
                continue
            if not LINT_PY.is_file():
                _add(rows, "静态检查", name, "SKIP", "lint.py 不存在")
                continue
            raw = _py_run(LINT_PY, "--only", key, *(["--all"] if all_ else []))
            m = re.search(r"^__LINT_META__ (\{.*\})$", raw, re.M)
            if not m:
                _add(rows, "静态检查", name, "FAIL", "缺 __LINT_META__（判定口径失效，⛔ 不判 PASS）", raw)
                if not brief:
                    print(f"  [FAIL] {name} —— 缺 __LINT_META__（判定口径失效）")
                continue
            try:
                meta = json.loads(m.group(1))
                f, w = int(meta["fail"]), int(meta["warn"])
            except (ValueError, KeyError, TypeError):
                _add(rows, "静态检查", name, "FAIL", "__LINT_META__ 解析失败", raw)
                if not brief:
                    print(f"  [FAIL] {name} —— __LINT_META__ 解析失败")
                continue
            note = ""
            if meta.get("stale"):
                f += 1
                note = " · 汇总格式变化，判定口径失效"
            wc = int(meta.get("warn_count", 0))
            base = WARN_BASELINE.get(key, 0)
            if wc > base:
                f += 1
                note += f" · 告警 {wc} 条超基线 {base}"
            status = "FAIL" if f else ("WARN" if w else "PASS")
            _add(rows, "静态检查", name, status, f"{f} 阻断 / {w} 告警{note}", raw)
            if not brief:
                print(f"  [{status}] {name} —— {f} 阻断 / {w} 告警")
                if status != "PASS":
                    lines = [ln.strip() for ln in raw.splitlines()
                             if re.search(r"\[(ERROR|WARN|XML失败|缺配对)\]", ln)]
                    for ln in lines[:12]:
                        print(f"        {ln}")
    elif opts["skip_static"]:
        _add(rows, "静态检查", "全部", "SKIP", "--skip-static")

    # 2) 文档一致性：docs-sync.py（--fix 会重写 AUTO 块）
    if run_docs and SYNC_PY.is_file():
        if not brief:
            print("\n──── 2. 文档一致性 ────")
        raw = _py_run(SYNC_PY, *(["--fix"] if only == "fix" else []))
        m = re.search(r"结果：ERROR\s*(\d+)\s*｜\s*WARN\s*(\d+)", raw)
        se, sw = (int(m.group(1)), int(m.group(2))) if m else (0, 0)
        if sw > DOCS_WARN_BASELINE:
            se += 1
        status = "FAIL" if se else ("WARN" if sw else "PASS")
        _add(rows, "文档一致性", "文档 ⇄ src（AUTO 块 / 页数 / 路径）", status,
             f"{se} 阻断 / {sw} 告警", raw)
        if not brief:
            print(f"  [{status}] 文档 ⇄ src —— {se} 阻断 / {sw} 告警")
            if status != "PASS":
                for ln in [x.strip() for x in raw.splitlines() if x.startswith("[ERROR]") or x.startswith("[WARN]")][:8]:
                    print(f"        {ln}")
            if only == "fix":
                for ln in [x.strip() for x in raw.splitlines() if "已重写" in x or x.rstrip().endswith("一致")]:
                    print(f"        {ln}")

    # 3) 铁律 1：改动范围（src/ + skill/ + scripts/ + .workbuddy/ + .github/ + .githooks/ + AGENTS.md 为授权区）
    if run_scope:
        if not brief:
            print("\n──── 3. 铁律 1（改动范围）────")
        try:
            cp = subprocess.run(["git", "-C", str(ROOT), "status", "--porcelain", "-uall"],
                                capture_output=True, text=True, encoding="utf-8", errors="replace")
            changed = []
            for ln in (cp.stdout or "").splitlines():
                m = re.match(r"^\s*\S{1,2}\s+(.+?)\s*$", ln)
                if m:
                    p = m.group(1).strip().strip('"')
                    if " -> " in p:
                        p = p.split(" -> ")[-1].strip().strip('"')
                    changed.append(p)
            outside = [p for p in changed if not re.match(
                r"^(src/|skill/|scripts/|\.workbuddy/|\.github/|\.githooks/|AGENTS\.md$)", p)]
            if outside:
                _add(rows, "铁律1", "src/ 之外的改动", "WARN", f"{len(outside)} 处越界", "\n".join(outside))
                if not brief:
                    print(f"  [WARN] src/ 之外有 {len(outside)} 处改动（授权区：src/ skill/ scripts/ .workbuddy/ .github/ .githooks/ AGENTS.md）：")
                    for p in outside[:10]:
                        print(f"        {p}")
            else:
                _add(rows, "铁律1", "src/ 之外的改动", "PASS", f"工作区 {len(changed)} 个文件，均在授权区")
                if not brief:
                    print(f"  [PASS] 工作区 {len(changed)} 个改动，均在授权区")
        except OSError:
            _add(rows, "铁律1", "src/ 之外的改动", "SKIP", "找不到 git")

    fails = [r for r in rows if r["status"] == "FAIL"]
    warns = [r for r in rows if r["status"] == "WARN"]
    skips = [r for r in rows if r["status"] == "SKIP"]
    passes = [r for r in rows if r["status"] == "PASS"]
    verdict = "✅ 通过" if not fails else "❌ 不通过"

    if brief:
        tail = f"  ｜ 告警：{'、'.join(r['name'] for r in warns)}" if warns else ""
        print(f"\naccept: {verdict} ｜ PASS {len(passes)} / WARN {len(warns)} / FAIL {len(fails)} / SKIP {len(skips)}{tail}"
              " ｜ 明细 artifacts/accept-report.md")
    else:
        print("\n════════ 验收结果 ════════")
        for r in rows:
            print(f"  [{r['status']}] {r['area']:<10} {r['name']} —— {r['detail']}")
        print(f"\n  {verdict}  （PASS {len(passes)} / WARN {len(warns)} / FAIL {len(fails)} / SKIP {len(skips)}）")
        if fails:
            print("  未通过项：")
            for r in fails:
                print(f"    - [{r['area']}] {r['name']}：{r['detail']}")
        if warns:
            print("  告警项（不阻断，建议确认）：")
            for r in warns:
                print(f"    - [{r['area']}] {r['name']}：{r['detail']}")
        print("\n  注：本脚本只证明「静态硬伤为 0 + 改动不越界」；编译与运行由用户本地 build.ps1 / run.ps1 验证。")

    if opts["report"]:
        lines = ["# NewToys 静态验收报告", "",
                 f"- 时间：{datetime.now():%Y-%m-%d %H:%M:%S}",
                 f"- 仓库：{ROOT}",
                 f"- 结论：**{verdict}**（PASS {len(passes)} / WARN {len(warns)} / FAIL {len(fails)} / SKIP {len(skips)}）",
                 "", "| 区域 | 检查项 | 结果 | 说明 |", "|---|---|---|---|"]
        for r in rows:
            lines.append(f"| {r['area']} | {r['name']} | {r['status']} | {r['detail'].replace('|', '\\|')} |")
        lines += ["", "## 原始输出"]
        for r in rows:
            if r["raw"]:
                lines += ["", f"### [{r['area']}] {r['name']}", "```", r["raw"].replace("`", "'").rstrip(), "```"]
        REPORT.parent.mkdir(parents=True, exist_ok=True)
        REPORT.write_text("\n".join(lines) + "\n", encoding="utf-8")
        print(f"  报告已写：{REPORT}")

    return rows, 0 if not fails else 1


def main() -> int:
    _utf8_stdout()
    ap = argparse.ArgumentParser(description="NewToys 静态验收（纯静态，不含构建）", allow_abbrev=False)
    # 单破折号写法为旧 accept.ps1 命令的兼容别名，历史任务卡里的参数可原样沿用
    ap.add_argument("-SkipStatic", "--skip-static", dest="skip_static", action="store_true")
    ap.add_argument("-SkipBuild", "--skip-build", dest="skip_build", action="store_true",
                    help="兼容旧命令；已无构建步骤")
    ap.add_argument("-Report", "--report", dest="report", action="store_true")
    ap.add_argument("-All", "--all", dest="all", action="store_true")
    ap.add_argument("-Brief", "--brief", dest="brief", action="store_true")
    ap.add_argument("-Only", "--only", dest="only", default="all",
                    choices=["all", "static", "docs", "fix", "scope", "family", "scripts"])
    ap.add_argument("-Menu", "--menu", dest="menu", action="store_true")
    ap.add_argument("-NoPause", "--no-pause", dest="no_pause", action="store_true")
    args = ap.parse_args()

    opts = dict(only=args.only, report=args.report, brief=args.brief,
                all=args.all, skip_static=args.skip_static, no_pause=args.no_pause)
    interactive = sys.stdin.isatty() and sys.stdout.isatty()
    explicit = [a for a in sys.argv[1:] if a not in ("-Menu", "--menu")]

    while True:
        if args.menu or (interactive and not explicit):
            picked = _show_menu()
            if picked in (None, "exit") or isinstance(picked, str):
                return 0
            opts.update(picked)
        if opts["brief"]:
            opts["report"] = True
        _rows, code = _run(opts)
        opts["brief"] = args.brief
        if not (interactive and not args.brief and not args.no_pause):
            return code
        print("\n  按任意键退出；R = 回菜单… ", end="", flush=True)
        k = _read_key()
        if k.lower() != "r":
            return code
        args.menu = True
        explicit = []


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as e:  # 脚本自身报错也返回 1
        print(f"[FAIL] accept 执行异常：{e}")
        sys.exit(1)
