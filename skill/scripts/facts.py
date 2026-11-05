#!/usr/bin/env python3
"""NewToys 机器事实唯一解析层。

从 src/ 与 skill/ 抽取结构性事实，供 lint.py / docs-sync.py / ctx.py 消费。
各脚本只消费，不再各自复制正则；新增事实先在此加函数。
"""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "src"
DOCS = ROOT / "skill" / "docs"
SKILL = ROOT / "skill"

RE_REG_FN = re.compile(r'RegisterFunction\s*<[^>]*>\s*\(\s*([^,()]+),\s*"([a-z0-9-]+)"\s*,\s*"([^"]+)"')
RE_PAGE_KEY = re.compile(r'override\s+string\s+PageKey\s*=>\s*"([a-z0-9-]+)"')
RE_REG_MOD = re.compile(
    r'RegisterModule\s*\(\s*(?:"([a-z0-9-]+)"|([A-Za-z_]\w*))\s*,\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*(\d+)')
RE_GROUPKEY = re.compile(r'const\s+string\s+GroupKey\s*=\s*"([a-z0-9-]+)"')
RE_PKG = re.compile(r'<PackageVersion\s+Include="([^"]+)"\s+Version="([^"]+)"')
RE_TFM = re.compile(r"<TargetFramework>([^<]+)</TargetFramework>")
RE_TOPIC = re.compile(r'const\s+string\s+(\w+)\s*=\s*"([^"]+)"')


def read(p: Path) -> str:
    try:
        return p.read_text(encoding="utf-8-sig", errors="replace")
    except OSError:
        return ""


def walk_src(exts: tuple[str, ...]) -> list[Path]:
    out = []
    for p in SRC.rglob("*"):
        if p.suffix in exts and p.is_file() and not ({"bin", "obj", ".git"} & set(p.parts)):
            out.append(p)
    return out


def src_paths() -> list[str]:
    return [p.as_posix() for p in SRC.rglob("*")
            if p.is_file() and not any(x in p.parts for x in ("bin", "obj"))]


# ── 模块 ────────────────────────────────────────────────────────────────────

def module_dirs() -> dict[str, str]:
    """{程序集目录名: 模块文档名}，由 src/Module.* 目录自举。"""
    out = {}
    for d in sorted(SRC.glob("Module.*")):
        if not d.is_dir():
            continue
        stem = d.name.split(".", 1)[1]
        out[d.name] = re.sub(r"(?<!^)(?=[A-Z])", "-", stem).lower()
    return out


def code_keys() -> tuple[dict[str, set[str]], dict[str, str]]:
    """({模块文档名: {页 Key}}, {页 Key: 中文标题})。"""
    out: dict[str, set[str]] = {}
    titles: dict[str, str] = {}
    for d, name in module_dirs().items():
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
    """{模块文档名: {key, title, icon, order, pages, settings}}。"""
    groups: dict[str, dict] = {}
    for d, name in module_dirs().items():
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
            for _mod, k, _title in RE_REG_FN.findall(t):
                if k.startswith("settings-"):
                    settings += 1
                else:
                    pages += 1
        if reg:
            groups[name] = {
                "key": gk or (reg[0] if reg[0] != "GroupKey" else ""),
                "title": reg[1], "icon": reg[2], "order": reg[3],
                "pages": pages, "settings": settings,
            }
    return groups


# ── 版本 / 栈 ───────────────────────────────────────────────────────────────

def package_versions() -> dict[str, str]:
    return dict(RE_PKG.findall(read(SRC / "Directory.Packages.props")))


def target_framework() -> str:
    for p in sorted(SRC.glob("*/*.csproj")):
        m = RE_TFM.search(read(p))
        if m:
            return m.group(1)
    return ""


def stack_line() -> str:
    v = package_versions()
    core = ["Avalonia", "CommunityToolkit.Mvvm",
            "Microsoft.Extensions.DependencyInjection", "Microsoft.Data.Sqlite"]
    tfm = target_framework()
    head = f"{tfm}" if tfm else ".NET"
    return " · ".join([head] + [f"{k} {v[k]}" for k in core if k in v])


def version_rows() -> list[list[str]]:
    return [[k, f"`{v}`"] for k, v in sorted(package_versions().items())]


# ── 区域 / 主题键 ───────────────────────────────────────────────────────────

def _theme() -> str:
    return read(SRC / "Shell" / "App.Theme.cs")


def region_layers() -> dict[str, int]:
    m = re.search(r"RegionLayers\s*=\s*\{(.*?)\n    \};", _theme(), re.S)
    if not m:
        return {}
    return {k: int(v) for k, v in re.findall(r'\(\s*"(\w+)"\s*,\s*(\d+)\s*\)', m.group(1))}


def region_adjacency() -> list[tuple[str, str]]:
    m = re.search(r"RegionAdjacency\s*=\s*\{(.*?)\n    \};", _theme(), re.S)
    if not m:
        return []
    return re.findall(r'\(\s*"(\w+)"\s*,\s*"(\w+)"\s*\)', m.group(1))


def same_layer_ok() -> set[frozenset]:
    """App.Theme.cs 里标注「允许同档」的边。"""
    m = re.search(r"RegionAdjacency\s*=\s*\{(.*?)\n    \};", _theme(), re.S)
    if not m:
        return set()
    out = set()
    for line in m.group(1).splitlines():
        pair = re.search(r'\(\s*"(\w+)"\s*,\s*"(\w+)"\s*\)', line)
        if pair and "允许同档" in line:
            out.add(frozenset(pair.groups()))
    return out


def card_layer() -> int:
    """详情卡所在档：RegionLayers 里 DetailCardBrush 的档位。"""
    return region_layers().get("DetailCardBrush", 2)


def brush_keys() -> set[str]:
    out: set[str] = set()
    for p in walk_src((".axaml", ".cs")):
        t = read(p)
        out.update(re.findall(r'x:Key="(\w*Brush)"', t))
        out.update(re.findall(r'S\("(\w*Brush)"', t))
    out.update(region_layers())
    return out


def font_pool() -> set[str]:
    """ui.md 字号角色表里出现的字号值。"""
    t = read(DOCS / "ui.md")
    sec = t.split("## 2.3")[-1].split("\n## ")[0] if "## 2.3" in t else ""
    return set(re.findall(r"\b(\d{2})\s*(?:SemiBold|Regular|等宽|Bold)?", sec)) & {"10", "12", "14", "16", "22", "28", "32"}


# ── 契约 / 主题常量 ─────────────────────────────────────────────────────────

def service_interfaces() -> list[tuple[str, str, str]]:
    """(接口, 文件, 首个实现类)。"""
    impl: dict[str, str] = {}
    for p in walk_src((".cs",)):
        for cls, iface in re.findall(r"class\s+(\w+)\s*:[^{]*?\b(I[A-Z]\w+)", read(p)):
            impl.setdefault(iface, cls)
    out = []
    for p in sorted((SRC / "Shared" / "Services").glob("I*.cs")):
        m = re.search(r"public\s+interface\s+(I\w+)", read(p))
        if m:
            out.append((m.group(1), p.relative_to(SRC).as_posix(), impl.get(m.group(1), "—")))
    return out


def bus_topics() -> list[tuple[str, str, str]]:
    """(常量名, 主题字符串, 文件)。只认显式 Topics 常量。"""
    out = []
    for p in sorted(walk_src((".cs",))):
        if p.name != "IModuleBus.cs" and not p.name.endswith("Topics.cs"):
            continue
        for name, val in RE_TOPIC.findall(read(p)):
            out.append((name, val, p.relative_to(SRC).as_posix()))
    return out


# ── 文档结构 ────────────────────────────────────────────────────────────────

def spec_outline() -> list[tuple[str, str]]:
    """需求规格的编号章节树 → [(节号, 标题)]。"""
    out = []
    for line in read(DOCS / "NewToys-需求规格.md").splitlines():
        m = re.match(r"^#{2,4}\s+(\d+(?:\.\d+)*)[.、\s]+(.+)$", line)
        if m:
            out.append((m.group(1), m.group(2).strip()))
    return out


def cards_index() -> list[tuple[str, str, str]]:
    """(NT 号, 模块, 卡标题)。"""
    out = []
    for p in sorted((DOCS / "backlog").rglob("NT-*.md")):
        m = re.search(r"NT-(\d{3})", p.name)
        if not m:
            continue
        title = ""
        for line in read(p).splitlines()[:12]:
            if line.startswith("# "):
                title = line[2:].strip()
                break
        out.append(("NT-" + m.group(1), p.parent.name, title))
    return out


def check_items() -> list[tuple[str, str, str]]:
    """(类别, 键, 显示名)：lint.py 检查项 + docs-sync.py 检查项。"""
    lint_txt = read(SKILL / "scripts" / "lint.py")
    sync_txt = read(SKILL / "scripts" / "docs-sync.py")
    acc_txt = read(SKILL / "scripts" / "accept.py")
    labels = dict(re.findall(r'^\s*\("([^"]+)",\s*"([\w-]+)"\),', acc_txt, re.M))
    labels = {v: k for k, v in labels.items()}
    out = []
    for k in re.findall(r'"([\w-]+)":\s*check_', lint_txt):
        out.append(("静态检查", k, labels.get(k, k)))
    for k in re.findall(r'"([\w-]+)":\s*check_', sync_txt):
        out.append(("文档一致性", k, k))
    return out
