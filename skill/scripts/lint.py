#!/usr/bin/env python3
"""NewToys 静态自检（唯一实现）。

规则出处：`skill/references/tech-guard.md`（G-* 技术栈 / C-* 注释）、
`skill/docs/ui.md` §2.3（字号白名单）。改规则必须同步改文档。

⛔ 只报告，不修改任何文件，恒以 0 退出（自检是参考，不是门禁）。
用法：python skill/scripts/lint.py [--only KEY] [--all]
     KEY = axaml | props | techstack | detail-paths | video-card | comments | view-pairs
           | regions | regionpages | family | scripts | conventions
"""
from __future__ import annotations

import argparse
import re
import sys
import xml.parsers.expat as expat
from pathlib import Path

import facts

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "src"
SKIP_DIRS = {"bin", "obj", "docs", ".git", "artifacts"}


# ---------------------------------------------------------------- 通用工具

def read(p: Path) -> str:
    return p.read_text(encoding="utf-8-sig", errors="replace").replace("\r\n", "\n")


def rel(p: Path) -> str:
    return p.relative_to(SRC).as_posix()


def lineno(text: str, idx: int) -> int:
    return text.count("\n", 0, idx) + 1


def _blank(m: re.Match) -> str:
    return re.sub(r"[^\n]", " ", m.group(0))


def strip_comments(text: str, axaml: bool = False) -> str:
    text = re.sub(r"<!--.*?-->", _blank, text, flags=re.S)
    if not axaml:
        text = re.sub(r"/\*.*?\*/", _blank, text, flags=re.S)
        text = re.sub(r"//[^\n]*", _blank, text)
    return text


def walk(exts: tuple[str, ...]) -> list[Path]:
    return [p for p in SRC.rglob("*") if p.suffix in exts and p.is_file()
            and not (SKIP_DIRS & set(p.parts))]


def elements(text: str) -> list[tuple[str, dict[str, str], int]]:
    """按 XML 解析 .axaml，产出 (元素名, 属性, 起始行号)。"""
    p = expat.ParserCreate()
    out: list[tuple[str, dict[str, str], int]] = []
    p.StartElementHandler = lambda name, attrs: out.append((name, attrs, p.CurrentLineNumber))
    p.Parse(text.encode("utf-8"), True)
    return out


def h(title: str) -> None:
    print(f"\n=== {title} ===")


# ---------------------------------------------------------------- 1) axaml 合法性 / 配对 / 主题键

def check_axaml(all_: bool) -> None:
    h("NewToys 静态自检")
    print(f"仓库根: {ROOT}")
    if not SRC.is_dir():
        print("[FAIL] 找不到 src/")
        return

    brush: set[str] = set()
    for p in walk((".axaml", ".cs")):
        t = read(p)
        brush.update(re.findall(r'x:Key="(\w*Brush)"', t))
        brush.update(re.findall(r'S\("(\w*Brush)"', t))
        # 区域键在 App.Theme.cs 里由 RegionLayers 表驱动赋值（不是逐个 S("…") 调用）
        m = re.search(r"RegionLayers\s*=\s*\{(.*?)\n    \};", t, re.S)
        if m:
            brush.update(re.findall(r'\(\s*"(\w*Brush)"\s*,\s*\d+\s*\)', m.group(1)))

    axamls = [p for p in SRC.rglob("*.axaml") if p.is_file()]
    print(f"\n[主题键] 白名单 {len(brush)} 个 *Brush 键")
    print(f"\n[AXAML] 检查 {len(axamls)} 个 .axaml...")
    bad = unpaired = no_init = 0
    for f in axamls:
        raw = read(f)
        try:
            elements(raw)
        except expat.ExpatError as e:
            print(f"  [XML失败] {f} : {e}")
            bad += 1
            continue
        if "x:Class=" not in raw:
            continue
        cs = f.with_name(f.name + ".cs")
        if not cs.exists():
            print(f"  [缺配对] {f} 有 x:Class 但无 .axaml.cs")
            unpaired += 1
            continue
        if f.stem == "App":
            continue
        if not re.search(r"InitializeComponent\s*\(\s*\)", read(cs)):
            print(f"  [缺 InitializeComponent] {cs}")
            no_init += 1

    print("\n[初始化] 检查 .axaml.cs 是否调用 InitializeComponent()...")
    unknown: set[str] = set()
    for p in walk((".axaml", ".cs")):
        if p.name.endswith(".axaml.cs"):
            continue
        for k in re.findall(r"\{(?:DynamicResource|StaticResource)\s+(\w+)", read(p)):
            if k.endswith("Brush") and k not in brush:
                unknown.add(k)

    if unknown:
        print("\n[主题键] 白名单外的 *Brush 引用（疑似旧键/拼错）：")
        for k in sorted(unknown):
            print(f"  - {k}")
    else:
        print("\n[主题键] 所有 *Brush 引用均在白名单内 ✅")

    print("\n=== 汇总 ===")
    print(f"  XML 不合法 .axaml : {bad}")
    print(f"  缺 .axaml.cs 配对 : {unpaired}")
    print(f"  缺 InitializeComponent : {no_init}")
    print(f"  *Brush 白名单      : {len(brush)}")
    print("\n提示：本脚本只做轻量静态检查；完整编译由用户本地 build.ps1 验证。")


# ---------------------------------------------------------------- 2) XAML 属性名 / 字号白名单

WPF_HINTS = {
    "SelectedValuePath": "Avalonia 的 ComboBox ⛔ 没有它 → 改绑 SelectedItem 到**选项对象**",
    "SelectedValue": "同上：Avalonia 用 SelectedItem",
    "StrokeLineJoin": "Avalonia 叫 StrokeJoin",
    "DisplayMemberPath": "Avalonia ⛔ 没有它 → 用 ItemTemplate",
    "Watermark": "Avalonia 用 PlaceholderText",
    "IsDefault": "Avalonia ⛔ 没有它 → 用按钮的 IsDefault 替代方案（回车键命令）",
    "IsCancel": "同上",
    "SharedSizeGroup": "Avalonia ⛔ 不支持 SharedSizeGroup",
    "ValidatesOnExceptions": "Avalonia ⛔ 没有它",
}

MARKUP = {
    "xmlns", "x", "Classes", "Name", "Grid", "Theme", "ItemsPanel", "ItemContainerTheme",
    "DataType", "TargetType", "Selector", "BasedOn", "Property", "Value", "Mode", "Converter",
    "ConverterParameter", "StringFormat", "FallbackValue", "RelativeSource", "ElementName",
    "Source", "Path", "Priority", "UpdateSourceTrigger", "ReflectionBinding", "CompiledBinding",
    "Binding", "StaticResource", "DynamicResource", "Key", "Class", "Style", "AvaloniaProperty",
}

FONT_WHITELIST = {"10", "12", "14", "16", "22", "28", "32"}


def check_props(all_: bool) -> None:
    h("NewToys XAML 属性名核对")
    print(f"仓库根: {ROOT}")
    if not SRC.is_dir():
        print("[FAIL] 找不到 src/")
        return

    props_file = SRC / "Directory.Packages.props"
    m = re.search(r'Include="Avalonia"\s+Version="([^"]+)"', read(props_file)) if props_file.exists() else None
    if not m:
        print("[FAIL] 读不到 Avalonia 版本（src/Directory.Packages.props）")
        return
    ver = m.group(1)
    print(f"Avalonia 版本: {ver}")

    nuget = Path.home() / ".nuget" / "packages"
    blobs: list[bytes] = []
    for pkg in ("avalonia", "avalonia.themes.fluent", "avalonia.desktop", "avalonia.fonts.inter"):
        for dll in (nuget / pkg / ver / "ref").rglob("*.dll"):
            blobs.append(dll.read_bytes())
    if not blobs:
        print("[WARN] 一个 Avalonia ref 程序集都没找到（NuGet 缓存路径变了？）——本检查无意义")
        return
    corpus = b"".join(blobs)
    print(f"已载入 Avalonia ref 程序集: {len(blobs)} 个")

    own: set[str] = set()
    for p in walk((".cs",)):
        own.update(re.findall(
            r"(?:StyledProperty|DirectProperty|AttachedProperty)<(?:[^<>]|<[^<>]*>)*>\s+(\w+?)Property\b",
            read(p)))
    print(f"本仓自定义属性（豁免）: {len(own)} 个")

    axamls = [p for p in SRC.rglob("*.axaml") if p.is_file()]
    print(f"\n[AXAML] 核对 {len(axamls)} 个 .axaml 的属性名...")

    problems = checked = font_problems = slider_problems = 0
    for f in axamls:
        raw = read(f)
        try:
            tags = elements(raw)
        except expat.ExpatError:
            print(f"  [XML失败] {rel(f)} —— 属性核对已跳过（先修 XML）")
            continue
        for _name, attrs, line in tags:
            # Slider ⛔ 硬压高度：Fluent 模板被压扁后轨道/thumb 与同一行按钮视觉错位
            if _name.lower().endswith("slider") and ("Height" in attrs or "MinHeight" in attrs):
                print(f"  [{rel(f)}:{line}] Slider 设了 Height / MinHeight —— 模板会被压变形（轨道与按钮错位）；"
                      f"紧凑靠容器 Padding / 负 Margin")
                slider_problems += 1
            for attr, val in attrs.items():
                # 命名空间声明与带前缀的附加属性（xmlns:* / chrome:A.B / ui:C.D）不参与存在性判定
                if attr in MARKUP or ":" in attr or attr.startswith("Classes."):
                    continue
                checked += 1
                for part in attr.split("."):
                    if len(part) < 2 or part in own:
                        continue
                    if part.encode() in corpus:
                        continue
                    hint = f"  ← {WPF_HINTS[part]}" if part in WPF_HINTS else ""
                    print(f'  [{rel(f)}:{line}] 属性名 "{attr}" 在 Avalonia 程序集里找不到{hint}')
                    problems += 1
                    break
                if attr == "FontSize" and re.fullmatch(r"[0-9.]+", val or ""):
                    if val not in FONT_WHITELIST:
                        print(f"  [{rel(f)}:{line}] 字号 {val} 不在白名单（ui.md §2.3）")
                        font_problems += 1

    print("\n=== 汇总 ===")
    print(f"  核对属性名出现次数 : {checked}")
    print(f"  疑似 WPF 专有 / 拼错 : {problems}")
    print(f"  字号越界（§2.3）    : {font_problems}")
    print(f"  硬压 Slider 高度    : {slider_problems}")
    if problems == 0:
        print("\n✅ 全部属性名均在 Avalonia 程序集（或本仓自定义控件）中存在。")
    else:
        print("\n⚠️ 上面报出的名字多半是 WPF 专有写法（Avalonia 名字不同）或拼写错误 —— 编译期会抛 AVLN2000。")
    print("\n提示：本检查按**名字存在性**判定（弱于编译器），只用来在编译前抓 WPF/Avalonia 名字差异。")


# ---------------------------------------------------------------- 3) 技术栈硬伤

CLASH = {
    "TaskStatus", "MenuGroup", "MenuItem", "MenuFlyoutItem", "Menu", "MenuFlyout",
    "Color", "Colors", "Brush", "SolidColorBrush", "Path", "Border", "Window", "Button",
    "Panel", "Decorator", "Control", "Style", "Size", "Point", "Rect", "Grid", "Image",
    "TextBlock", "Icon", "Thickness", "CornerRadius", "Theme", "ThemeVariant",
}
ICON_FONT = re.compile(
    r"(awesome|fluent[\s\-]?icons|mdl2|material[\s\-]?icons|icomoon|iconfont"
    r"|bootstrap[\s\-]?icons|octicons|tabler|remixicon|themify|glyphicons|devicon)", re.I)
CHECK_CHAR = re.compile(r'(?:Text|Content)\s*=\s*"\s*[\u2611\u2610\u25A3]\s*"')
EMOJI = re.compile(r"[\u2600-\u27BF\u2B00-\u2BFF\uFE0F]|[\U0001F000-\U0001FAFF]")
MODULE_SETTINGS = re.compile(r"Module\.[^/]+/Views/[^/]*Settings[^/]*\.axaml$")
CLR_ASSEMBLY = re.compile(r'clr-namespace:[^"]*;\s*assembly\s*=')


def check_techstack(all_: bool) -> None:
    h("NewToys 技术栈硬伤自检")
    print(f"仓库根: {ROOT}")
    if not SRC.is_dir():
        print("[FAIL] 找不到 src/")
        return

    findings: list[tuple[str, str, str, str]] = []

    def add(rid: str, level: str, p: Path, line: int, msg: str) -> None:
        findings.append((rid, level, f"{rel(p)}:{line}", msg))

    targets = walk((".cs", ".axaml", ".csproj"))
    axamls = [p for p in targets if p.suffix == ".axaml"]
    css = [p for p in targets if p.suffix == ".cs"]
    projs = [p for p in targets if p.suffix == ".csproj"]
    print(f"扫描：.axaml {len(axamls)} / .cs {len(css)} / .csproj {len(projs)}")

    for f in axamls:
        clean = strip_comments(read(f), axaml=True)
        res_def = bool(re.search(r'x:Key="\w+Brush"', clean))
        for i, ln in enumerate(clean.split("\n"), 1):
            if re.search(r"x:DataType\s*=", ln):
                add("G-09", "WARN", f, i, "出现 x:DataType —— 编译绑定已全工程关闭，只写 {Binding Xxx}")
            if CHECK_CHAR.search(ln):
                add("G-05", "ERROR", f, i, "用字符当勾选框（Text/Content = ☑/☐/▣）—— 表头勾选必须原生三态 CheckBox")
            if re.search(r"MergedDictionaries\s*\.\s*Clear\s*\(", ln):
                add("G-02", "ERROR", f, i, "MergedDictionaries.Clear() —— ApplyTheme 必须幂等，清空会丢 Fluent 内置样式")
            if not res_def:
                m = re.search(r"#[0-9A-Fa-f]{6}\b", ln)
                if m:
                    add("G-01", "WARN", f, i, f"硬编码颜色 {m.group(0)} —— 改用 DynamicResource 语义键")
            m = re.search(r'FontFamily\s*=\s*"([^"]*)"', ln)
            if m and ICON_FONT.search(m.group(1)):
                add("G-06", "ERROR", f, i, f"FontFamily 指向图标字体（{m.group(1)}）—— 图标走 Path 几何资源键，禁图标字体")
            if MODULE_SETTINGS.search(rel(f)) and "ScrollViewer" in ln:
                add("G-16", "ERROR", f, i, "模块设置页里出现 ScrollViewer —— 滚动统一由 SettingsSectionHost 管，节内 ⛔ 再包滚动条（双滚动条/磁吸失效）")
            if CLR_ASSEMBLY.search(ln):
                add("G-17", "ERROR", f, i, 'xmlns 用了 clr-namespace + assembly= —— 跨程序集必须 xmlns:v="using:NewToys.UI.Controls"')
        for m in re.finditer(r"(?s)<TextBlock\b(?:(?!>).)*?MinHeight\s*=", clean):
            add("G-03", "WARN", f, lineno(clean, m.start()), "TextBlock 上用 MinHeight 撑行高 —— 文字会贴顶；行高基线放 ListBoxItem MinHeight=28")

    for f in css:
        clean = strip_comments(read(f))
        for i, ln in enumerate(clean.split("\n"), 1):
            if re.search(r"public\s+override\s+void\s+On(?:AttachedToVisualTree|DetachedFromVisualTree)\s*\(", ln):
                add("G-04", "ERROR", f, i, "可视树回调写了 public override —— 必须是 protected override（否则 CS0507）")
            if re.search(r"MergedDictionaries\s*\.\s*Clear\s*\(", ln):
                add("G-02", "ERROR", f, i, "MergedDictionaries.Clear() —— ApplyTheme 必须幂等")
            m = re.search(r"\b(?:class|record|struct|enum)\s+([A-Za-z_]\w*)\b", ln)
            if m and m.group(1) in CLASH:      # 区分大小写：RECT/POINT 与 Rect/Point 不冲突
                add("G-07", "ERROR", f, i, f"类型名 {m.group(1)} 撞框架类型 —— 改名避开（新增模型先 grep Avalonia ref）")
            m = re.search(r'FontFamily\s*=\s*"([^"]*)"', ln)
            if m and ICON_FONT.search(m.group(1)):
                add("G-06", "ERROR", f, i, f"FontFamily 指向图标字体（{m.group(1)}）—— 图标走 Path 几何资源键，禁图标字体")
            if not re.search(r'\$@"|@\$"', ln):
                for im in re.finditer(r'\$"[^"\n]*"', ln):
                    if re.search(r'(?<!\\)\\[^\\"0abfnrtvux{}]', im.group(0)):
                        add("G-14", "ERROR", f, i, "插值串里有单个 \\ —— CS8087/CS1009；反斜杠必须写 \\\\")
                        break
            m = re.search(r'(?i)\bicon\s*[=:]\s*"([^"]*)"', ln)
            if m and EMOJI.search(m.group(1)):
                add("G-15", "WARN", f, i, "icon 字段用了 emoji —— 应收敛到几何资源键")

    for p in projs:
        name = p.stem
        is_module = name.startswith("Module.")
        is_base = name in ("Shared", "Core")
        clean = strip_comments(read(p))
        for i, ln in enumerate(clean.split("\n"), 1):
            if re.search(r'Include\s*=\s*"[^"]*defaults[^"]*"', ln):
                add("G-08", "ERROR", p, i, 'defaults\\ 用了 Include= —— 会 NETSDK1022；必须 None Update="defaults\\**"')
            m = re.search(r'ProjectReference\s+Include\s*=\s*"([^"]+)"', ln)
            if is_module and m:
                ref = Path(m.group(1).replace("\\", "/")).stem
                if "shell" in ref.lower():
                    add("G-10", "ERROR", p, i, f"模块引了 Shell（{ref}）—— 分层单向 Shared <- Core <- Shell <- Module.*")
                elif ref.startswith("Module.") and ref != name:
                    add("G-10", "ERROR", p, i, f"模块互引（{name} -> {ref}）—— 跨模块契约只放 Shared / Core")
            m = re.search(r'PackageReference\s+Include\s*=\s*"([^"]+)"', ln)
            if is_base and m and "avalonia" in m.group(1).lower():
                add("G-11", "ERROR", p, i, f"{name} 引了 Avalonia 包（{m.group(1)}）—— Shared / Core 不引 Avalonia")

    errs = [x for x in findings if x[1] == "ERROR"]
    warns = [x for x in findings if x[1] == "WARN"]
    print("\n=== 汇总 ===")
    if errs:
        print(f"\n[ERROR] {len(errs)} 条（会编译失败 / 运行必错 / 破坏架构边界）：")
        for rid, _l, loc, msg in errs:
            print(f"  [{rid}] {loc}  {msg}")
    else:
        print("\n[ERROR] 0 条 ✅")
    if warns:
        if all_:
            print(f"\n[WARN] {len(warns)} 条（违反约定但仍能编译）：")
            for rid, _l, loc, msg in warns:
                print(f"  [{rid}] {loc}  {msg}")
        else:
            print(f"[WARN] {len(warns)} 条（明细加 --all 查看）")
    else:
        print("[WARN] 0 条 ✅")
    print("\n提示：规则出处 skill/references/tech-guard.md；本脚本只抓常见硬伤，完整编译由用户本地 build.ps1 验证。")


# ---------------------------------------------------------------- 4) 详情卡绑定路径

PATH_PROPS = ("ValuePath|SecondValuePath|VisibleWhen|EnabledWhen|OptionsPath|OptionValuePath"
              "|OptionDisplayPath|ChipTextPath|ChipSelectedPath|ChipCommandPath|IsEnabledPath"
              "|IsVisiblePath|TogglePath|IsExpandedPath|VisiblePath|BodyVisiblePath|SamplePath")
DECL_RE = re.compile(r"(?:public|internal|protected)\s+(?:static\s+|readonly\s+|partial\s+|override\s+|virtual\s+)*"
                     r"[A-Za-z_][\w<>\[\],\.\? ]*\s+([A-Za-z_]\w*)\s*(?:\{|=>|;|=)")


def check_detail_paths(all_: bool) -> None:
    h("NewToys 详情卡绑定路径自检")
    print(f"仓库根: {ROOT}")
    if not SRC.is_dir():
        print("[FAIL] 找不到 src/")
        return

    names: set[str] = set()
    for p in walk((".cs",)):
        names.update(DECL_RE.findall(strip_comments(read(p))))
    print(f"属性 / 字段名共 {len(names)} 个（用于比对）")

    assign = re.compile(rf"(?:{PATH_PROPS})\s*=\s*\"([^\"]*)\"")
    findings: list[tuple[str, str]] = []
    scanned = 0
    for p in walk((".cs",)):
        txt = strip_comments(read(p))
        if "IDetailCardSource" not in txt and "DetailCardDescriptor" not in txt:
            continue
        scanned += 1
        for m in assign.finditer(txt):
            path = m.group(1)
            if not path.strip():
                continue
            head = path.lstrip("!").split(".")[0]
            if not head:
                continue
            if head not in names:
                findings.append((f"{rel(p)}:{lineno(txt, m.start())}", path))

    print(f"含描述符的 .cs 文件 {scanned} 个")
    if not findings:
        print("[OK] 未发现可疑的绑定路径。")
        return
    print(f"[WARN] {len(findings)} 条绑定路径的首段在 src/ 里找不到同名属性（改名 / 拼错？）")
    limit = len(findings) if all_ else min(30, len(findings))
    for loc, path in findings[:limit]:
        print(f"  {loc}  →  {path}")
    if limit < len(findings):
        print(f"  …另有 {len(findings) - limit} 条，加 --all 看全。")


# ---------------------------------------------------------------- 5) 视频详情卡同源

def check_video_card(all_: bool) -> None:
    h("NewToys 视频详情卡同源自检")
    module = SRC / "Module.VideoTools"
    card = module / "Details" / "VideoTaskCard.cs"
    if not card.exists():
        print("[ERROR] 1 条  —— 找不到唯一卡定义：src/Module.VideoTools/Details/VideoTaskCard.cs")
        return

    errors: list[str] = []
    for p in module.rglob("*.cs"):
        if p.resolve() == card.resolve() or (SKIP_DIRS & set(p.parts)):
            continue
        n = len(re.findall(r"new\s+DetailCardDescriptor", read(p)))
        if n:
            errors.append(f"V-01  {p.relative_to(ROOT).as_posix()} 又新建了 DetailCardDescriptor（{n} 处）"
                          " —— 卡只能由 Details/VideoTaskCard.cs 产出")

    card_txt = read(card)
    names = re.findall(r'public\s+const\s+string\s+\w+\s*=\s*"([^"]+)"', card_txt)
    m = re.search(r"BatchOnlyPrefixes\s*=\s*\{([^}]*)\}", card_txt)
    only = re.findall(r'"([^"]+)"', m.group(1)) if m else []

    texts = {}
    for label, pat in (("预览页", "VideoPreviewViewModel*.cs"), ("批量页", "VideoBatchViewModel*.cs")):
        texts[label] = "\n".join(read(p) for p in (module / "ViewModels").glob(pat))

    for name in names:
        targets = ["批量页"] if any(name.startswith(x) for x in only) else ["预览页", "批量页"]
        camel = "_" + name[:1].lower() + name[1:]
        for page in targets:
            if not (re.search(rf"\b{re.escape(name)}\b", texts[page])
                    or re.search(rf"\b{re.escape(camel)}\b", texts[page])):
                errors.append(f'V-02  {page} 缺属性 "{name}"（VideoTaskCard 的卡里引用了它）')

    print(f"卡定义引用属性 {len(names)} 个；批量页专属前缀：{', '.join(only)}")
    if not errors:
        print("[OK] 0 条 —— 两页详情卡同源，属性齐全")
        return
    print(f"[ERROR] {len(errors)} 条")
    for e in (errors if all_ else errors[:30]):
        print(f"  {e}")
    if not all_ and len(errors) > 30:
        print(f"  …（还有 {len(errors) - 30} 条，加 --all 看全部）")


# ---------------------------------------------------------------- 6) 注释健康度 / View 类名唯一性

RE_VERSION = re.compile(r"^\s*//+\s*(v\d+\.\d+|NT-\d{3}\s*[:：]?\s*v\d+\.\d+|第[一二三四五六七八九十0-9]+轮)")
RE_HISTORY = re.compile(r"原来|此前是|以前是|曾经是|早先是")
MAX_BLOCK = 6


def check_comments(all_: bool) -> None:
    h("NewToys 注释健康度自检")
    print(f"仓库根: {ROOT}")
    if not SRC.is_dir():
        print("[FAIL] 找不到 src/")
        return

    targets = [p for p in walk((".cs",)) if "NewToys.Tests" not in p.parts]
    print(f"扫描：.cs {len(targets)} 个")

    findings: list[tuple[str, str, str, str]] = []

    def add(rid: str, level: str, loc: str, msg: str) -> None:
        findings.append((rid, level, loc, msg))

    stats: list[tuple[float, int, int, str]] = []
    for f in targets:
        lines = read(f).split("\n")
        total = len(lines)
        if not total:
            continue
        comments = block_start = block_len = 0
        for i, ln in enumerate(lines, 1):
            plain = re.match(r"^\s*//(?![/!])", ln)
            any_comment = re.match(r"^\s*(///|//|\*|/\*)", ln)
            if any_comment:
                comments += 1
            if plain:
                if block_len == 0:
                    block_start = i
                block_len += 1
            else:
                if block_len >= MAX_BLOCK:
                    add("C-02", "WARN", f"{rel(f)}:{block_start}",
                        f"连续 {block_len} 行注释（阈值 {MAX_BLOCK}）—— 该进文档或压成 1-3 行 WHY")
                block_len = 0
            if ln.strip().startswith("//"):
                if RE_VERSION.match(ln):
                    add("C-01", "WARN", f"{rel(f)}:{i}", "版本/轮次前缀叙事 —— 删前缀，只留 WHY（git log 已记录）")
                elif RE_HISTORY.search(ln):
                    add("C-04", "WARN", f"{rel(f)}:{i}", "历史叙事 —— 描述过去状态，无法据此判断当前正确性")
            if any_comment and re.search(r"NT-\d{3}", ln):
                add("C-06", "WARN", f"{rel(f)}:{i}", "注释里出现任务编号 —— 删编号，结论查 framework.md §11.2")
        if block_len >= MAX_BLOCK:
            add("C-02", "WARN", f"{rel(f)}:{block_start}", f"连续 {block_len} 行注释（阈值 {MAX_BLOCK}）")

        text = "\n".join(lines)
        if (re.search(r"^\s*(public|internal)\s+(partial\s+)?interface\s", text, re.M)
                and not re.search(r"^\s*(public|internal)\s+(sealed\s+|partial\s+|static\s+)*class\s", text, re.M)):
            continue
        stats.append((comments / total, comments, total, rel(f)))

    # C-05：View 类名唯一性（反射导航的硬前提）
    views: dict[str, list[Path]] = {}
    for p in walk((".cs",)):
        if not p.name.endswith(".axaml.cs"):
            continue
        for nm in re.findall(r"(?m)^\s*(?:public|internal)\s+(?:sealed\s+)?partial\s+class\s+([A-Za-z_]\w*)", read(p)):
            views.setdefault(nm, []).append(p)
    for nm, files in views.items():
        if len(files) > 1:
            where = " / ".join(rel(x) for x in files)
            add("C-05", "ERROR", f"{rel(files[0])}:1", f"View 类名重复：{nm} —— {where}")

    print("\n[注释率 Top5]")
    for ratio, c, t, r in sorted(stats, reverse=True)[:5]:
        print(f"  {ratio:6.0%}  {c:5}/{t:<5}  {r}")

    errors = [x for x in findings if x[1] == "ERROR"]
    warns = [x for x in findings if x[1] == "WARN"]
    print("\n=== 汇总 ===")
    print(f"  [ERROR] {len(errors)} 条")
    print(f"  [WARN]  {len(warns)} 条")
    if errors:
        print("\n[ERROR 明细]")
        for rid, _l, loc, msg in errors:
            print(f"  {rid}  {loc}  {msg}")
    if all_ and warns:
        print("\n[WARN 明细]")
        for rid in sorted({x[0] for x in warns}):
            group = [x for x in warns if x[0] == rid]
            print(f"  {rid}（{len(group)} 条）")
            for _r, _l, loc, msg in group:
                print(f"    {loc}  {msg}")
    print("\n提示：C-01/C-02/C-04/C-06 是 WARN（不阻断），C-05 是 ERROR（View 重名会静默错页）。")
    print("      C-03 只出排行不告警（审美判据做 lint 会烂成噪音）。")


# ---------------------------------------------------------------- 8) 区域色差（相邻撞档）

def check_regions(all_: bool) -> None:
    h("NewToys 区域色差自检")
    theme = SRC / "Shell" / "App.Theme.cs"
    if not theme.exists():
        print("[FAIL] 找不到 src/Shell/App.Theme.cs")
        return

    txt = read(theme)
    m = re.search(r"RegionLayers\s*=\s*\{(.*?)\n    \};", txt, re.S)
    a = re.search(r"RegionAdjacency\s*=\s*\{(.*?)\n    \};", txt, re.S)
    if not m or not a:
        print("[ERROR] 1 条  —— App.Theme.cs 里找不到 RegionLayers / RegionAdjacency 表"
              "（区域颜色的唯一事实源丢了，相邻色差无从核对）")
        return

    layers: dict[str, int] = {}
    errors: list[str] = []
    for key, n in re.findall(r'\(\s*"(\w+)"\s*,\s*(\d+)\s*\)', m.group(1)):
        if key in layers:
            errors.append(f'R-01  RegionLayers 里 "{key}" 重复登记')
        layers[key] = int(n)

    pairs = re.findall(r'\(\s*"(\w+)"\s*,\s*"(\w+)"\s*\)', a.group(1))

    for key, n in layers.items():
        if not 1 <= n <= 5:
            errors.append(f'R-02  "{key}" 档位 {n} 越界 —— 色阶常量表只有 5 档面（1~5），区域实际只用 1~3 档')

    used: set[str] = set()
    for x, y in pairs:
        used.update((x, y))
        if x not in layers or y not in layers:
            errors.append(f'R-03  相邻对 "{x}" ↔ "{y}" 里的键没在 RegionLayers 登记')
            continue
        if layers[x] == layers[y]:
            if layers[x] == CARD_LAYER or frozenset((x, y)) in SAME_LAYER_OK:
                continue
            errors.append(f'R-04  "{x}" 与 "{y}" 都落在 {layers[x]} 档 —— 直接相接却同色，接缝会消失')

    for key in layers:
        if key not in used:
            errors.append(f'R-05  "{key}" 没登记任何相邻对 —— 等于没被检查（补一条边或从表里删掉）')

    print(f"区域 {len(layers)} 个 / 相邻对 {len(pairs)} 对（其中允许同档 {len(SAME_LAYER_OK)} 对）；档位分布 "
          + " / ".join(f"{n} 档 {sum(1 for v in layers.values() if v == n)} 个" for n in range(1, 6)))
    if not errors:
        print("[OK] 0 条 —— 所有直接相接的区域都不同档（同材质相接的几对已登记为允许同档）")
        return
    print(f"[ERROR] {len(errors)} 条")
    for e in (errors if all_ else errors[:30]):
        print(f"  {e}")
    if not all_ and len(errors) > 30:
        print(f"  …（还有 {len(errors) - 30} 条，加 --all 看全部）")


# ---------------------------------------------------------------- 7) 页面契约

CLASS_RE = re.compile(r"(?m)^\s*(?:public|internal)\s+(?:sealed\s+|abstract\s+|partial\s+|static\s+)*class\s+([A-Za-z_]\w*)")


def check_view_pairs(all_: bool) -> None:
    h("NewToys 页面契约自检")
    print(f"仓库根: {ROOT}")
    if not SRC.is_dir():
        print("[FAIL] 找不到 src/")
        return

    findings: list[tuple[str, str, str, str]] = []
    by_project: dict[str, set[str]] = {}
    view_decls: list[tuple[str, str, int, str]] = []

    for p in walk((".cs",)):
        rp = rel(p)
        project = rp.split("/")[0]
        text = read(p)
        bucket = by_project.setdefault(project, set())
        for m in CLASS_RE.finditer(text):
            bucket.add(m.group(1))
            if p.name.endswith(".axaml.cs"):
                view_decls.append((m.group(1), rp, m.start(), text))

    view_names = {v[0] for v in view_decls}
    print(f"View 类 {len(view_decls)} 个 / 类型集合覆盖 {len(by_project)} 个项目")

    for name, rp, idx, text in view_decls:
        if "/Views/" not in rp or not name.endswith("View"):
            continue
        if "lint:view-no-vm" in "\n".join(text[:idx].split("\n")[-5:]):
            continue
        vm = name[:-4] + "ViewModel"
        if vm not in by_project[rp.split("/")[0]]:
            findings.append(("P-01", "ERROR", rp, f"{name} 找不到 {vm}（同程序集）—— 页面会静默空白"))

    call_re = re.compile(r"RegisterFunction\s*<\s*([A-Za-z_][\w\.]*)\s*>")
    calls = 0
    for p in walk((".cs",)):
        if p.name in ("MenuService.cs", "IMenuService.cs") or "NewToys.Tests" in p.parts:
            continue
        text = strip_comments(read(p))
        if "RegisterFunction" not in text:
            continue
        rp = rel(p)
        for m in call_re.finditer(text):
            calls += 1
            simple = m.group(1).split(".")[-1]
            loc = f"{rp}:{lineno(text, m.start())}"
            if not simple.endswith("View"):
                findings.append(("P-02", "ERROR", loc, f"泛型实参「{simple}」不是 *View 命名 —— 菜单项注册的应是页面类"))
            elif simple not in view_names:
                findings.append(("P-02", "ERROR", loc, f"泛型实参「{simple}」在 src/ 里找不到对应的 View 类"))

    print(f"RegisterFunction 注册点（泛型）{calls} 处")

    errors = [x for x in findings if x[1] == "ERROR"]
    warns = [x for x in findings if x[1] == "WARN"]
    print("\n=== 汇总 ===")
    print(f"  [ERROR] {len(errors)} 条")
    print(f"  [WARN]  {len(warns)} 条")
    if errors:
        print("\n[ERROR 明细]")
        for rid, _l, loc, msg in errors[: len(errors) if all_ else 30]:
            print(f"  {rid}  {loc}  {msg}")
        if not all_ and len(errors) > 30:
            print(f"  …另有 {len(errors) - 30} 条，加 --all 看全。")
    print("\n提示：P-01 / P-02 都是 ERROR —— 命名对不上时页面不报错，只是空白或打不开。")


# ---------------------------------------------------------------- 入口

# ---------------------------------------------------------------- 8) 区域色差（页面级）

# 相邻区域灰度差下限：深色下大面积纯色块直接相邻，5 灰阶就能看出边界；
# 浅色是白底 + 低饱和，8 以下整片糊。两套皮肤分别核。
MIN_DELTA_DARK = 6.0
MIN_DELTA_LIGHT = 8.0
FLUENT_SLACK = 2.0    # Fluent 皮肤走 Blend 取整，允许 2 的误差

# 卡内统一档位：窗口里只有「主显示区 + 详情列」两张卡，卡内所有面共用一个档位。
# 卡内相邻对同档是设计意图（一张卡内部本来就不该有色阶），R-04 与着色都整体跳过。
CARD_LAYER = facts.card_layer()
SAME_LAYER_OK = facts.same_layer_ok()


def _gray(hexv: str) -> float:
    h = hexv.lstrip("#")
    return 0.299 * int(h[0:2], 16) + 0.587 * int(h[2:4], 16) + 0.114 * int(h[4:6], 16)


def _fluent_layers(steps: list[float], base: float, dark: bool) -> list[float]:
    """Fluent 皮肤的区域档 = 底色按 steps 向白（暗底）或向黑（亮底）递进混色，与 App.Theme.cs 同公式。"""
    return [base] + [base + (255 - base) * s if dark else base * (1 - s) for s in steps]


def _fluent_delta(steps: list[float], lo: int, hi: int, dark: bool = True) -> float:
    """两档在 Fluent 皮肤下的最坏灰度差（底色未知 ⇒ 在暗底 / 亮底区间内扫描取最小）。"""
    rng = range(16, 81) if dark else range(200, 256)
    return min(abs(_fluent_layers(steps, b, dark)[hi - 1] - _fluent_layers(steps, b, dark)[lo - 1])
               for b in rng)


def _page_chain(p: Path, detail_vms: set[str]) -> tuple[list[str], bool] | None:
    t = strip_comments(read(p), axaml=True)
    has_toolbar = 'Classes="toolbar"' in t or "ToolbarHost" in t
    has_header = "TableViewHost" in t or "ListHeaderBrush" in t or 'Classes="list-header"' in t
    has_body = "ListPageSkeleton" in t or "ListBodyBrush" in t
    has_footer = "ListFooterBar" in t
    if not (has_toolbar or has_header or has_body):
        return None                                # 设置页 / 卡片页，不走列表骨架
    chain = ["TitleBarBrush"]
    if has_toolbar:
        chain.append("ToolbarBrush")
    if has_header:
        chain.append("ListHeaderBrush")
    if has_body:
        chain.append("ListBodyBrush")
    if has_footer:
        chain.append("FooterBarBrush")
    if not has_body and not has_header:
        # 工具条与状态栏之间永远隔着内容区（页面自己画的 View / 播放器 / 设置节），
        # 由 Shell 的 `.content-area` 上色：有工具条 = ContentBrush，否则 ContentBareBrush。
        chain.append("ContentBrush" if has_toolbar else "ContentBareBrush")
    chain.append("StatusBarBrush")
    detail = (p.stem + "Model") in detail_vms or p.stem in detail_vms
    return chain, detail


def check_region_pages(all_: bool) -> None:
    theme = read(SRC / "Shell" / "App.Theme.cs")
    ml = re.search(r"RegionLayers\s*=\s*\{(.*?)\n    \};", theme, re.S)
    ma = re.search(r"RegionAdjacency\s*=\s*\{(.*?)\n    \};", theme, re.S)
    if not (ml and ma):
        print("[ERROR] 1 条  —— App.Theme.cs 里找不到 RegionLayers / RegionAdjacency 表")
        return

    layers = {k: int(v) for k, v in re.findall(r'\("(\w+)",\s*(\d)\)', ml.group(1))}
    pairs = re.findall(r'\("(\w+)",\s*"(\w+)"\)', ma.group(1))
    pal: dict[bool, list[float] | None] = {}
    for name, dark in (("Dark", True), ("Light", False)):
        found = re.findall(rf'private const string {name}Bg(\d) = "(#[0-9A-Fa-f]{{6}})";', theme)
        pal[dark] = [_gray(v) for _, v in sorted(found)] if len(found) >= 4 else None
    steps = [float(x) for x in re.findall(r"Blend\(toward, baseCol, ([0-9.]+)\)", theme)]

    errors: list[str] = []

    # ① 档位组合 ⇄ 真实灰度差（两套皮肤各算一遍；Fluent 底色未知 ⇒ 全底色扫描取最坏）
    combos = sorted({(min(layers[a], layers[b]), max(layers[a], layers[b]))
                     for a, b in pairs if a in layers and b in layers})
    combos = [c for c in combos if c[0] != c[1]]
    for lo, hi in combos:
        for dark in (True, False):
            p = pal[dark]
            limit = MIN_DELTA_DARK if dark else MIN_DELTA_LIGHT
            if p and abs(p[hi - 1] - p[lo - 1]) < limit:
                errors.append(f'R-10  {lo} 档 ⇄ {hi} 档 在 {"暗" if dark else "亮"}色 NewToys 下灰度差 '
                              f'{abs(p[hi - 1] - p[lo - 1]):.0f} < {limit:.0f}')
        if len(steps) >= 3:
            for dark in (True, False):
                limit = (MIN_DELTA_DARK if dark else MIN_DELTA_LIGHT) - FLUENT_SLACK
                worst = _fluent_delta(steps, lo, hi, dark)
                if worst < limit:
                    errors.append(f'R-11  {lo} 档 ⇄ {hi} 档 在 Fluent {"暗" if dark else "亮"}色下最坏灰度差 '
                                  f'{worst:.0f} < {limit:.0f}')

    # ② 页面级：把每个 View 的纵向区域链还原出来，逐对查登记与色差
    detail_vms: set[str] = set()
    for f in walk((".cs",)):
        t = read(f)
        if "IDetailCardSource" not in t:
            continue
        m = re.search(r"partial class (\w+)", t)
        if m:
            detail_vms.add(m.group(1))

    views = [p for p in walk((".axaml",)) if p.name.endswith("View.axaml")]
    pages = 0
    for v in views:
        r = _page_chain(v, detail_vms)
        if r is None:
            continue
        chain, has_detail = r
        pages += 1
        for x, y in zip(chain, chain[1:]):
            if not any({x, y} == {a, b} for a, b in pairs):
                errors.append(f'R-12  {rel(v)}："{x}" ↔ "{y}" 没登记相邻对（这条接缝不会被任何检查覆盖）')
        if has_detail and chain.count("ListBodyBrush"):
            if not any({a, b} == {"ListBodyBrush", "DetailCardBrush"} for a, b in pairs):
                errors.append(f'R-13  {rel(v)}：行区 ↔ 右侧详情列没登记相邻对')

    # ③ 档数够不够：相邻图做贪心着色，给出「最少需要几档」
    adj: dict[str, set[str]] = {}
    for a, b in pairs:
        if frozenset((a, b)) in SAME_LAYER_OK:
            continue
        if layers.get(a) == layers.get(b) == CARD_LAYER:
            continue
        adj.setdefault(a, set()).add(b)
        adj.setdefault(b, set()).add(a)
    color: dict[str, int] = {}
    for k in sorted(adj, key=lambda k: -len(adj[k])):
        used = {color[n] for n in adj[k] if n in color}
        c = 1
        while c in used:
            c += 1
        color[k] = c
    need = max(color.values()) if color else 0

    print(f"区域 {len(layers)} 个 / 相邻对 {len(pairs)} 对 / 扫描 {len(views)} 个 View（列表页 {pages} 个）")
    for lo, hi in combos:
        p = pal[True]
        nv = f'{abs(p[hi - 1] - p[lo - 1]):.0f}' if p else "?"
        fl = (f'{min(_fluent_delta(steps, lo, hi), _fluent_delta(steps, lo, hi, dark=False)):.0f}'
              if len(steps) >= 3 else "?")
        print(f"  {lo} 档 ⇄ {hi} 档：NewToys 灰差 {nv} / Fluent 最坏 {fl}"
              f"（下限 深 {MIN_DELTA_DARK:.0f} / 浅 {MIN_DELTA_LIGHT:.0f}）")
    used = max(layers.values()) if layers else 0
    print(f"  相邻图最少需要 {need} 档，现用 {used} 档 ⇒ {'够' if need <= used else '不够，需加档'}")

    if not errors:
        print("[OK] 0 条 —— 每页相邻区域都有色差，且接缝都被相邻表覆盖")
        return
    print(f"[ERROR] {len(errors)} 条")
    for e in (errors if all_ else errors[:30]):
        print(f"  {e}")
    if not all_ and len(errors) > 30:
        print(f"  …（还有 {len(errors) - 30} 条，加 --all 看全部）")


# ---------------------------------------------------------------- 10) 同族页矩阵对账

FAMILY_DOC = ROOT / "skill" / "docs" / "NewToys-列表页范本-全部资产.md"
FAMILY_VIEW_RE = re.compile(r"`Views/(\w+)\.axaml`")
FAMILY_CLASS_RE = re.compile(
    r"^\s*(?:public|internal)\s+(?:sealed\s+|abstract\s+|partial\s+)*class\s+(\w+)([^{]*)\{",
    re.M)
FAMILY_LABELS = ("①", "②", "③", "④")


def _family_detail_vms() -> set[str]:
    """实现 IDetailCardSource 的 VM，含从基类继承的接口实现。"""
    decls: list[tuple[str, str]] = []
    for p in walk((".cs",)):
        for m in FAMILY_CLASS_RE.finditer(strip_comments(read(p))):
            decls.append((m.group(1), m.group(2)))
    vms = {n for n, bases in decls if "IDetailCardSource" in bases}
    for _ in range(3):
        more = {n for n, bases in decls
                if n not in vms and any(b in vms for b in re.findall(r"\b\w+ViewModel\b", bases))}
        if not more:
            break
        vms |= more
    return vms


def _family_facts(p: Path, vms: set[str]) -> tuple[list[bool], bool]:
    """四段代码事实 + 是否列表骨架页；先剥 XAML 注释，避免注释里的标签名假命中。"""
    t = strip_comments(read(p), axaml=True)
    fields = [
        "FilterBar.FieldsContent" in t,
        bool(re.search(r"<[A-Za-z0-9_.]*:?TableViewHost\b", t)),
        "ShowFooter=" in t and "TotalCount=" in t and "PageSize=" in t,
        p.stem[:-4] + "ViewModel" in vms,
    ]
    return fields, ("ListPageSkeleton" in t or fields[1])


def _family_matrix() -> tuple[dict[str, tuple[str, list[bool]]], str | None]:
    """解析范本 §八：View stem → (页面 Key, 四段声明)。8.1 / 8.2 的小表没有源文件指针，自动跳过。"""
    if not FAMILY_DOC.is_file():
        return {}, "找不到范本 NewToys-列表页范本-全部资产.md"
    parts = read(FAMILY_DOC).split("## 八、同族页矩阵", 1)
    if len(parts) < 2:
        return {}, "范本缺 §八 同族页矩阵"
    rows: dict[str, tuple[str, list[bool]]] = {}
    for line in parts[1].splitlines():
        if not line.startswith("|"):
            continue
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if len(cells) < 7:
            continue
        m = FAMILY_VIEW_RE.search(cells[1])
        if not m:
            continue
        rows[m.group(1)] = (cells[0].strip("`"), [c == "✓" for c in cells[2:6]])
    return rows, None


def check_family(all_: bool) -> None:
    h("NewToys 同族页矩阵对账")
    print(f"仓库根: {ROOT}")
    vms = _family_detail_vms()
    matrix, err = _family_matrix()
    if err:
        print(f"[ERROR] 1 条  —— {err}")
        return

    views = {p.stem: p for p in walk((".axaml",)) if p.parent.name == "Views"}
    errors: list[str] = []
    warns: list[str] = []
    facts_by_view = {stem: _family_facts(p, vms) for stem, p in views.items()}

    for stem, (key, marks) in matrix.items():
        p = views.get(stem)
        if p is None:
            errors.append(f"F-01  {key}  矩阵登记的 Views/{stem}.axaml 在 src/ 里找不到")
            continue
        facts = facts_by_view[stem][0]
        for i, label in enumerate(FAMILY_LABELS):
            if marks[i] and not facts[i]:
                errors.append(f"F-01  {key}  {label} 矩阵登记 ✓，代码里检不出")
            elif not marks[i] and facts[i]:
                errors.append(f"F-03  {key}  {label} 矩阵登记 —，代码里检出了")

    for stem, (_facts, is_list) in facts_by_view.items():
        if is_list and stem not in matrix:
            warns.append(f"F-02  Views/{stem}.axaml 是列表骨架页，矩阵漏登记")

    n_list = sum(1 for _f, ok in facts_by_view.values() if ok)
    print(f"矩阵 {len(matrix)} 页 / 列表骨架页 {n_list} 页 / 详情卡 VM {len(vms)} 个")
    print("\n=== 汇总 ===")
    if errors:
        print(f"[ERROR] {len(errors)} 条")
        for e in (errors if all_ else errors[:30]):
            print(f"  {e}")
    if warns:
        print(f"[WARN] {len(warns)} 条")
        for w in (warns if all_ else warns[:30]):
            print(f"  {w}")
    if not errors and not warns:
        print("[OK] 0 条 —— 矩阵 ⇄ 代码四段一致，列表骨架页全部登记")


# ---------------------------------------------------------------- 11) 工具脚本语言 / 落点

BLOCKED_SCRIPT_EXT = {".ps1", ".bat", ".cmd"}


def check_scripts(all_: bool) -> None:
    h("S-01 工具脚本语言统一（skill/scripts 仅 .py）")
    d = ROOT / "skill" / "scripts"
    errs: list[tuple[str, str]] = []
    if d.is_dir():
        for p in sorted(d.iterdir()):
            if p.is_file() and p.suffix.lower() in BLOCKED_SCRIPT_EXT:
                errs.append((p.name, "skill/scripts/ 只放 .py；操作类 .ps1 归仓库根 scripts/"))
    print("\n=== 汇总 ===")
    if errs:
        print(f"\n[ERROR] {len(errs)} 条（AI 侧工具脚本必须单一解释器）：")
        for name, msg in errs:
            print(f"  [S-01] {name}  {msg}")
    else:
        print("\n[ERROR] 0 条 ✅")
    print("[WARN] 0 条 ✅")
    print("提示：skill/docs/ 正文的示例脚本片段不受此规则约束；"
          "src/**/defaults/** 是产品内置脚本，不属操作脚本。")


# ---------------------------------------------------------------- 12) 代码约定
RE_BUS_CALL = re.compile(r'\.(?:Post|Take|TakeAll|Subscribe|PendingCount)\s*\(\s*"([^"]+)"')


def check_conventions(all_: bool) -> None:
    h("NewToys 代码约定自检")
    warns: list[str] = []
    topics = {v for _n, v, _f in facts.bus_topics()}

    for p in walk((".cs",)):
        path = rel(p)
        t = strip_comments(read(p))
        if p.name != "IModuleBus.cs" and not p.name.endswith("Topics.cs"):
            for topic in RE_BUS_CALL.findall(t):
                if topic not in topics and re.fullmatch(r"[a-z]+\.[a-z0-9.-]+", topic):
                    warns.append(f"V-10  {path}: 主题字面量 `{topic}` —— 一律引用 Topics 常量")
        fk = {m[1] for m in facts.RE_REG_FN.findall(t)}
        pk = set(facts.RE_PAGE_KEY.findall(t))
        if fk and pk and fk != pk:
            warns.append(f"V-11  {path}: RegisterFunction {sorted(fk)} ⇄ PageKey {sorted(pk)} 不一致")
        if path.startswith("Module."):
            m = re.search(r"^namespace\s+(NewToys(?:\.\w+)+)", t, re.M)
            if m:
                stem = path.split("/", 1)[0].split(".", 1)[1]
                if m.group(1).split(".")[1] != stem:
                    warns.append(f"V-13  {path}: namespace `{m.group(1)}` 与程序集 `{stem}` 不符")

    # 只查随包脚本：scripts/ 下的构建脚本由 pwsh 执行（默认 UTF-8），不受 PS 5.1 ANSI 读取影响
    for p in SRC.rglob("*.ps1"):
        if not p.is_file() or "bin" in p.parts or "obj" in p.parts:
            continue
        if not p.read_bytes().startswith(b"\xef\xbb\xbf"):
            warns.append(f"V-12  {p.relative_to(ROOT).as_posix()}: 随包 .ps1 缺 UTF-8 BOM（PS 5.1 按 ANSI 读中文会乱码）")

    if not warns:
        print("[WARN] 0 条 ✅")
        return
    print(f"[WARN] {len(warns)} 条")
    for w in (warns if all_ else warns[:30]):
        print("  " + w)
    if not all_ and len(warns) > 30:
        print(f"  …（还有 {len(warns) - 30} 条，加 --all 看全部）")


CHECKS = {
    "axaml": check_axaml,
    "props": check_props,
    "techstack": check_techstack,
    "detail-paths": check_detail_paths,
    "video-card": check_video_card,
    "comments": check_comments,
    "view-pairs": check_view_pairs,
    "regions": check_regions,
    "regionpages": check_region_pages,
    "family": check_family,
    "scripts": check_scripts,
    "conventions": check_conventions,
}

# 汇总行 → 结构化计数。与 accept.py 的旧正则等价，但由本脚本自己产出，
# 使调用方不再依赖中文措辞。改汇总格式必须同步改这张表，否则 stale 会置位。
META_RULES: dict[str, tuple[tuple[str, ...], tuple[str, ...]]] = {
    "axaml": ((r"XML 不合法 \.axaml\s*:\s*([1-9]\d*)", r"缺 \.axaml\.cs 配对\s*:\s*([1-9]\d*)"),
              (r"白名单外的 \*Brush 引用",)),
    "props": ((r"疑似 WPF 专有 / 拼错\s*:\s*([1-9]\d*)", r"硬压 Slider 高度\s*:\s*([1-9]\d*)"), ()),
    "techstack": ((r"\[ERROR\]\s*([1-9]\d*)\s*条",), (r"\[WARN\]\s*([1-9]\d*)\s*条",)),
    "detail-paths": ((), (r"\[WARN\]\s*([1-9]\d*)\s*条绑定路径",)),
    "video-card": ((r"\[ERROR\]\s*([1-9]\d*)\s*条",), ()),
    "comments": ((r"\[ERROR\]\s*([1-9]\d*)\s*条",), (r"\[WARN\]\s*([1-9]\d*)\s*条",)),
    "view-pairs": ((r"\[ERROR\]\s*([1-9]\d*)\s*条",), (r"\[WARN\]\s*([1-9]\d*)\s*条",)),
    "regions": ((r"\[ERROR\]\s*([1-9]\d*)\s*条",), ()),
    "regionpages": ((r"\[ERROR\]\s*([1-9]\d*)\s*条",), ()),
    "family": ((r"\[ERROR\]\s*([1-9]\d*)\s*条",), (r"\[WARN\]\s*([1-9]\d*)\s*条",)),
    "scripts": ((r"\[ERROR\]\s*([1-9]\d*)\s*条",), (r"\[WARN\]\s*([1-9]\d*)\s*条",)),
    "conventions": ((), (r"\[WARN\]\s*([1-9]\d*)\s*条",)),
}


def meta_of(key: str, out: str) -> dict:
    fails, warns = META_RULES.get(key, ((), ()))
    f = sum(len(re.findall(p, out)) for p in fails)
    w = sum(len(re.findall(p, out)) for p in warns)
    # 输出里明明报了正数条数，却一个规则都没匹配上 ⇒ 汇总格式已变，判定口径失效
    stale = bool(re.search(r"\[(?:ERROR|WARN)\]\s+[1-9]\d*\s*条", out)) and f == 0 and w == 0
    m = re.search(r"\[WARN\]\s+(\d+)\s*条", out)
    return {"key": key, "fail": f, "warn": w,
            "warn_count": int(m.group(1)) if m else 0, "stale": stale}


def main() -> int:
    import io
    import json
    from contextlib import redirect_stdout

    ap = argparse.ArgumentParser(description="NewToys 静态检查")
    ap.add_argument("--only", choices=sorted(CHECKS), help="只跑一项")
    ap.add_argument("--all", action="store_true", help="列出全部 WARN 明细")
    args = ap.parse_args()

    for out in (sys.stdout, sys.stderr):
        try:
            out.reconfigure(encoding="utf-8", errors="replace")
        except AttributeError:
            pass

    keys = [args.only] if args.only else list(CHECKS)
    for k in keys:
        buf = io.StringIO()
        with redirect_stdout(buf):
            CHECKS[k](args.all)
        text = buf.getvalue()
        sys.stdout.write(text)
        print("__LINT_META__ " + json.dumps(meta_of(k, text), ensure_ascii=False))
        if len(keys) > 1:
            print()
    return 0


if __name__ == "__main__":
    sys.exit(main())
