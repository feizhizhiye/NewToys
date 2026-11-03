#!/usr/bin/env python3
"""NewToys 文档同步检查（docs-sync）

把「改交互必须双写」「任务卡滑动窗口」「台账 ↔ 卡双向」这些**靠人记的纪律**
变成可跑的静态检查。只读，⛔ 不修改任何文件。

    python skill/scripts/docs-sync.py            # 全部检查，WARN 不阻断
    python skill/scripts/docs-sync.py --strict   # 有 ERROR 时退出码 1
    python skill/scripts/docs-sync.py --only keys

检查项
    keys    菜单 Key：代码 ⇄ modules/<模块>.md ⇄ 软件说明书
    menu    模块清单四方：代码 RegisterModule / RegisterFunction ⇄ framework §4.2 ⇄ index §二 ⇄ 台账基线 ⇄ 模块 md 头部（键 / 显示名 / Order / 图标 / 页数）
    paths   文档里的路径引用（``Xxx/Yyy.cs``）在 src 是否存在
    cards   backlog 每目录卡数（>5 且含已验收卡 ⇒ 应淘汰）、模块 md §2.5 存在性
    ledger  NT 编号：卡文件 ⇄ 台账 / framework §11.2 索引
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

ROOT = Path(__file__).resolve().parents[2]
DOCS = ROOT / "skill" / "docs"
SRC = ROOT / "src"

# src/Module.Xxx → modules/xxx-yyy.md
MODULE_DIR = {
    "Module.ScriptManager": "script-manager",
    "Module.MediaAssets": "media-assets",
    "Module.VideoTools": "video-tools",
    "Module.WebTools": "web-tools",
    "Module.SystemTools": "system-tools",
    "Module.BackupCenter": "backup-center",
    "Module.Viewer": "viewer",
    "Module.Reading": "reading",
}

RE_REG_FN = re.compile(r'RegisterFunction\s*<[^>]*>\s*\(\s*([^,()]+),\s*"([a-z0-9-]+)"\s*,\s*"([^"]+)"')
RE_PAGE_KEY = re.compile(r'override\s+string\s+PageKey\s*=>\s*"([a-z0-9-]+)"')
RE_MD_ROW = re.compile(r"^\|\s*`([a-z0-9-]+)`\s*\|")
RE_TICK = re.compile(r"`([a-z0-9]+-[a-z0-9-]+)`")
RE_NT = re.compile(r"NT-(\d{3})")
RE_REG_MOD = re.compile(
    r'RegisterModule\s*\(\s*(?:"([a-z0-9-]+)"|([A-Za-z_]\w*))\s*,\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*(\d+)')
RE_GROUPKEY = re.compile(r'const\s+string\s+GroupKey\s*=\s*"([a-z0-9-]+)"')
RE_MD_PAGES = re.compile(r"##\s*2\.\s*当前功能（(\d+)\s*页(?:\s*\+\s*(\d+)\s*设置页)?）")
RE_MD_HEAD = re.compile(r"模块键\*\*：`([a-z0-9-]+)`.*?Order\s*\*{0,2}(\d+)\*{0,2}.*?图标\s*`([A-Za-z]+)`")
RE_FW_ROW = re.compile(r"^\|\s*([^|]+?)\s*\|\s*`([a-z0-9-]+)`\s*\|\s*(\d+)\s*\|\s*`([A-Za-z]+)`\s*\|")
RE_LEDGER_ROW = re.compile(r"^\|\s*([^|（]+?)（?`([a-z0-9-]+)`）?[^|]*\|\s*(\d+)")
RE_IDX_ROW = re.compile(r"^\|\s*([^|]+?)\s*\|\s*`Module\.[A-Za-z]+`[^|]*\|\s*`([a-z0-9-]+)`")
RE_PATH_TOK = re.compile(r"`([A-Za-z0-9_.\-]+(?:/[A-Za-z0-9_.\-]+)+\.(?:cs|axaml))`")

ERRORS: list[str] = []
WARNS: list[str] = []


def read(p: Path) -> str:
    try:
        return p.read_text(encoding="utf-8-sig")
    except OSError:
        return ""


def code_keys() -> tuple[dict[str, set[str]], dict[str, str]]:
    """返回 ({模块: {页 Key}}, {页 Key: 中文标题})。"""
    out: dict[str, set[str]] = {}
    titles: dict[str, str] = {}
    for d, name in MODULE_DIR.items():
        base = SRC / d
        if not base.exists():
            continue
        keys: set[str] = set()
        for p in base.rglob("*.cs"):
            t = read(p)
            for _mod, k, title in RE_REG_FN.findall(t):
                keys.add(k)
                titles.setdefault(k, title)
            keys.update(RE_PAGE_KEY.findall(t))
        out[name] = keys
    return out, titles


def code_groups() -> dict[str, dict]:
    """返回 {模块文档名: {key, title, icon, order, pages, settings}}，全部从代码扫出。"""
    groups: dict[str, dict] = {}
    for d, name in MODULE_DIR.items():
        base = SRC / d
        if not base.exists():
            continue
        gk = ""
        reg: tuple[str, str, str, int] | None = None
        pages = settings = 0
        for p in base.rglob("*.cs"):
            t = read(p)
            gk = gk or (m.group(1) if (m := RE_GROUPKEY.search(t)) else "")
            for lit, var, title, icon, order in RE_REG_MOD.findall(t):
                reg = (lit or var, title, icon, int(order))
            for mod, k, _title in RE_REG_FN.findall(t):
                if k.startswith("settings-"):
                    settings += 1
                else:
                    pages += 1
        if reg:
            groups[name] = {
                "key": gk or (reg[0] if reg[0] != "GroupKey" else ""),
                "title": reg[1],
                "icon": reg[2],
                "order": reg[3],
                "pages": pages,
                "settings": settings,
            }
    return groups


def md_keys(md: Path) -> set[str]:
    keys = set()
    for line in read(md).splitlines():
        m = RE_MD_ROW.match(line)
        if m:
            keys.add(m.group(1))
    return keys


def check_keys() -> None:
    print("── 1 菜单 Key 对账（代码 ⇄ 模块 md ⇄ 说明书）──")
    ck, titles = code_keys()
    all_code = set().union(*ck.values()) if ck else set()
    manual = read(DOCS / "NewToys-软件说明书.md")
    manual_ticks = set(RE_TICK.findall(manual))
    total_md = 0

    for mod, keys in sorted(ck.items()):
        md = DOCS / "modules" / f"{mod}.md"
        if not md.exists():
            ERRORS.append(f"缺模块文档 modules/{mod}.md（代码有 {len(keys)} 个页 Key）")
            continue
        mk = md_keys(md)
        total_md += len(mk & keys)
        miss = sorted(keys - mk)
        stale = sorted(mk - keys)
        if miss:
            WARNS.append(f"modules/{mod}.md 缺页：{', '.join(miss)}")
        if stale:
            WARNS.append(f"modules/{mod}.md 有、代码无（文档过期）：{', '.join(stale)}")

    # 说明书用「用户语言」写页面（中文标题），⛔ 不要求出现菜单 Key；设置页不要求入说明书
    miss_manual = sorted(
        k for k in all_code
        if not k.startswith("settings-") and titles.get(k) and titles[k] not in manual
    )
    if miss_manual:
        WARNS.append(f"说明书 §四/§五 未提到 {len(miss_manual)} 个页：{', '.join(miss_manual[:8])}"
                     + (" …" if len(miss_manual) > 8 else ""))
    print(f"   代码页 Key {len(all_code)} 个 ｜ 模块 md 命中 {total_md} ｜ 说明书命中 {len(all_code) - len(miss_manual)}")


def check_menu() -> None:
    print("── 1b 模块清单四方对账（代码 ⇄ framework §4.2 ⇄ index §二 ⇄ 台账 ⇄ 模块 md）──")
    groups = code_groups()
    if not groups:
        WARNS.append("未能从 src 解析出任何 RegisterModule")
        return
    by_key = {g["key"]: (name, g) for name, g in groups.items() if g["key"]}

    # (a) framework.md §4.2 分组现状表
    fw_rows = {}
    for line in read(DOCS / "framework.md").splitlines():
        if m := RE_FW_ROW.match(line):
            fw_rows[m.group(2)] = (m.group(1).strip("* "), int(m.group(3)), m.group(4))
    for k, (name, g) in by_key.items():
        if k not in fw_rows:
            ERRORS.append(f"framework.md §4.2 分组现状缺模块 `{k}`（{g['title']} / Order {g['order']} / {g['icon']}）")
        else:
            _n, order, icon = fw_rows[k]
            if order != g["order"]:
                ERRORS.append(f"framework.md §4.2 `{k}` Order 写 {order}，代码 {g['order']}")
            if icon != g["icon"]:
                ERRORS.append(f"framework.md §4.2 `{k}` 图标写 `{icon}`，代码 `{g['icon']}`")
    extra = set(fw_rows) - set(by_key) - {"system"}
    if extra:
        ERRORS.append(f"framework.md §4.2 有、代码无分组：{', '.join(sorted(extra))}")

    # (b) index.md §二 模块清单
    idx_seen = set()
    for line in read(DOCS / "index.md").splitlines():
        m = RE_IDX_ROW.match(line)
        if not m:
            continue
        idx_seen.add(m.group(2))
        hit = by_key.get(m.group(2))
        if not hit:
            WARNS.append(f"index.md §二 有 `{m.group(2)}`，代码无此分组")
        elif hit[1]["title"] not in m.group(1):
            WARNS.append(f"index.md §二 `{m.group(2)}` 显示名写「{m.group(1).strip()}」，代码「{hit[1]['title']}」")
    for k in by_key:
        if k not in idx_seen:
            ERRORS.append(f"index.md §二 模块清单缺 `{k}`（{by_key[k][1]['title']}）")

    # (c) 模块 md 头部（键 / Order / 图标）+ §2 页数
    for name, g in sorted(groups.items()):
        md = DOCS / "modules" / f"{name}.md"
        if not md.exists():
            continue
        t = read(md)
        if h := RE_MD_HEAD.search(t):
            k, order, icon = h.group(1), int(h.group(2)), h.group(3)
            if k != g["key"]:
                ERRORS.append(f"modules/{name}.md 模块键写 `{k}`，代码 `{g['key']}`")
            if order != g["order"]:
                ERRORS.append(f"modules/{name}.md Order 写 {order}，代码 {g['order']}")
            if icon != g["icon"]:
                WARNS.append(f"modules/{name}.md 图标写 `{icon}`，代码 `{g['icon']}`")
        else:
            WARNS.append(f"modules/{name}.md 头部未按模板写「模块键 / Order / 图标」")
        if p := RE_MD_PAGES.search(t):
            want = g["pages"] + g["settings"]
            if int(p.group(1)) != g["pages"]:
                ERRORS.append(f"modules/{name}.md §2 写 {p.group(1)} 页，代码功能页 {g['pages']} 个")
            if g["settings"] and p.group(2) is None:
                WARNS.append(f"modules/{name}.md §2 未写「+ {g['settings']} 设置页」，代码有设置页")
        else:
            WARNS.append(f"modules/{name}.md §2 标题未按「## 2. 当前功能（N 页 + M 设置页）」写")

    # (d) 台账基线表（菜单项数）
    ledger = {}
    for line in read(DOCS / "NewToys-项目状态与路线图.md").splitlines():
        if m := RE_LEDGER_ROW.match(line):
            ledger[m.group(2)] = int(m.group(3))
    for k, (name, g) in by_key.items():
        if k not in ledger:
            ERRORS.append(f"台账「一、当前基线」表缺模块 `{k}`")
        elif ledger[k] != g["pages"] + g["settings"]:
            ERRORS.append(f"台账基线 `{k}` 写 {ledger[k]} 个菜单项，代码 {g['pages'] + g['settings']} 个")

    print(f"   代码模块 {len(groups)} ｜ framework 表 {len(fw_rows) - 1} 行 ｜ index 命中 {len(idx_seen)} ｜ 台账 {len(ledger)} 行")
    print("   " + " ｜ ".join(f"{g['key']}={g['pages']}+{g['settings']}" for g in groups.values()))


def check_paths() -> None:
    print("── 1c 文档路径引用 ⇄ src 实际文件 ──")
    src_paths = [p.as_posix() for p in SRC.rglob("*")
                 if p.is_file() and not any(x in p.parts for x in ("bin", "obj"))]
    live, old = [], []
    for md in sorted(DOCS.rglob("*.md")):
        rel = md.relative_to(DOCS).as_posix()
        seen = set()
        for tok in RE_PATH_TOK.findall(read(md)):
            if tok in seen or ".." in tok or "Xxx" in tok or tok.startswith("/") or "/" not in tok:
                continue
            seen.add(tok)
            if not any(s.endswith("/" + tok) for s in src_paths):
                (old if rel.startswith("backlog/") else live).append(f"{rel} → `{tok}`")
    for line in live:
        ERRORS.append(f"路径失效（正文文档）：{line}")
    if old:
        WARNS.append(f"backlog 历史卡引用已失效路径 {len(old)} 处（卡是历史工作稿，淘汰时一并消失，无需修）"
                     + "：" + ", ".join(o.split(" → ")[0] for o in old[:6]) + (" …" if len(old) > 6 else ""))
    print(f"   正文文档失效 {len(live)} 处 ｜ backlog 历史卡 {len(old)} 处")


def check_cards() -> None:
    print("── 2 任务卡窗口 / 模块 md 骨架 ──")
    bl = DOCS / "backlog"
    if not bl.exists():
        WARNS.append("缺 skill/docs/backlog/ 目录")
        return
    for d in sorted(p for p in bl.iterdir() if p.is_dir()):
        cards = sorted(d.glob("NT-*.md"))
        if not cards:
            continue
        accepted = [c for c in cards if "已验收" in read(c)]
        n = len(cards) - len(accepted)
        flag = "⚠" if len(accepted) > 5 else " "
        print(f"   {flag} backlog/{d.name:<16} 卡 {len(cards):>2} ｜ 已验收 {len(accepted):>2} ｜ 未验收 {n:>2}")
        if len(accepted) > 5:
            WARNS.append(f"backlog/{d.name} 已有 {len(accepted)} 张已验收卡（>5）⇒ 先蒸馏进 modules/{d.name}.md 再淘汰最旧")
    for md in sorted((DOCS / "modules").glob("*.md")):
        t = read(md)
        if "2.5" not in t:
            WARNS.append(f"modules/{md.name} 缺 §2.5 操作逻辑（模板第 3 节）")


def check_ledger() -> None:
    print("── 3 NT 编号：卡 ⇄ 台账 / framework §11.2 ──")
    files = {m.group(1) for p in (DOCS / "backlog").rglob("NT-*.md") if (m := RE_NT.search(p.name))}
    ledger_text = read(DOCS / "NewToys-项目状态与路线图.md")
    ledger = set(RE_NT.findall(ledger_text))
    fw = read(DOCS / "framework.md")
    idx = set(RE_NT.findall(fw.split("### 11.2")[-1] if "### 11.2" in fw else ""))
    known = ledger | idx
    orphan = sorted(files - known)
    # 只对「未验收」的条目报缺卡：已验收被淘汰的卡属正常
    open_nt: set[str] = set()
    for line in ledger_text.splitlines():
        if not line.lstrip().startswith("| NT-"):
            continue
        if "🗑" in line:  # 已蒸馏 / 已作废后删卡属正常
            continue
        if re.search(r"📝|✅\s*\*\*就绪|🔄", line):
            open_nt.update(RE_NT.findall(line))
    ghost = sorted(open_nt - files)
    if orphan:
        WARNS.append(f"卡存在但台账/§11.2 未登记：{', '.join('NT-' + n for n in orphan)}")
    if ghost:
        WARNS.append(f"台账有、卡已删（确认是否已作废）：{', '.join('NT-' + n for n in ghost[:10])}")
    # 台账表格列数：防「行被拼接」与「正文竖线未转义」导致错列
    rows = [l for l in ledger_text.splitlines() if l.lstrip().startswith("| NT-")]
    bad = [(i, l) for i, l in enumerate(ledger_text.splitlines(), 1)
           if l.lstrip().startswith("| NT-") and len(re.findall(r"(?<!\\)\|", l)) != 6]
    if bad:
        WARNS.append("台账表格列数异常（行拼接 / 竖线未转义）：行 " + ", ".join(str(i) for i, _ in bad[:8]))
    print(f"   卡文件 {len(files)} ｜ 台账 {len(ledger)} ｜ §11.2 索引 {len(idx)} ｜ 表格 {len(rows)} 行")


CHECKS = {"keys": check_keys, "menu": check_menu, "paths": check_paths,
          "cards": check_cards, "ledger": check_ledger}


def main() -> int:
    ap = argparse.ArgumentParser(description="NewToys 文档同步检查")
    ap.add_argument("--only", choices=sorted(CHECKS))
    ap.add_argument("--strict", action="store_true", help="有 ERROR 时退出码 1")
    a = ap.parse_args()

    for name, fn in CHECKS.items():
        if a.only and name != a.only:
            continue
        fn()
        print()

    for e in ERRORS:
        print(f"[ERROR] {e}")
    for w in WARNS:
        print(f"[WARN]  {w}")
    print(f"\n结果：ERROR {len(ERRORS)} ｜ WARN {len(WARNS)} ｜ 只读检查，不改文件")
    return 1 if (a.strict and ERRORS) else 0


if __name__ == "__main__":
    sys.exit(main())
