# NewToys 技术栈硬伤静态检查（交付前跑；与 check.ps1 / check-axaml-props.ps1 互补）
# 用法（在仓库根执行）：pwsh skill/scripts/check-techstack.ps1 [-All]
#   -All  同时列出 WARN 明细（默认只列 ERROR 明细 + WARN 计数，保持输出短）
#
# 规则清单唯一源 = skill/references/tech-guard.md 第一节（G-01 ~ G-11）—— 改这里必须同步改那边。
# ⛔ 只报告，不修改任何文件，不以非零码退出（自检是参考，不是门禁）。
# ⛔ 口径：ERROR = 会编译失败 / 运行必错 / 破坏架构边界；WARN = 违反约定但仍能编译。
# ⛔ 不扫 src/docs/（文档里大量引用反例写法，扫了就全是误报）。

param([switch]$All)

$ErrorActionPreference = 'Continue'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$src = Join-Path $repoRoot 'src'
Write-Host "`n=== NewToys 技术栈硬伤自检 ===" -ForegroundColor Cyan
Write-Host "仓库根: $repoRoot" -ForegroundColor Gray
if (-not (Test-Path $src)) { Write-Host '[FAIL] 找不到 src/' -ForegroundColor Red; exit 0 }

# ---------- 0) 工具 ----------
# 注释置空（保换行 ⇒ 行号仍准确）：.axaml 只认 <!-- -->；.cs / .csproj 另认 /* */ 与 //
# ⚠️ .axaml 里 // 不是注释，所以按扩展名分流（.axaml.cs 结尾是 .cs，走 C# 分支，正确）
function Get-CodeText([string]$path) {
    $raw = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    $raw = [regex]::Replace($raw, '(?s)<!--.*?-->', { param($m) ($m.Value -replace '[^\r\n]', ' ') })
    if ($path -notmatch '\.axaml$') {
        $raw = [regex]::Replace($raw, '(?s)/\*.*?\*/', { param($m) ($m.Value -replace '[^\r\n]', ' ') })
        $raw = [regex]::Replace($raw, '//[^\r\n]*',        { param($m) ($m.Value -replace '[^\r\n]', ' ') })
    }
    return $raw
}

$findings = New-Object System.Collections.ArrayList
function Add-Finding([string]$id, [string]$level, [string]$fullPath, [int]$line, [string]$msg) {
    $rel = $fullPath.Substring($src.Length + 1)
    [void]$findings.Add([pscustomobject]@{ Id = $id; Level = $level; Loc = "${rel}:$line"; Msg = $msg })
}
function Get-LineNo([string]$text, [int]$index) {
    return (($text.Substring(0, $index) -split "\r?\n").Count)
}

# ---------- 1) 目标文件（一律排除 docs / bin / obj） ----------
# ⚠️ 不用 -Include（PS7 下 -File + -Include 数组会触发 SwitchParameter 绑定错误），改用扩展名过滤
# ⚠️ 变量别叫 $all —— PowerShell 变量名不分大小写，会与 param([switch]$All) 撞名（赋值数组直接报
#    "无法将值 System.Object[] 转换为 SwitchParameter"）。故起名 $targets。
$targets = @(Get-ChildItem -Path $src -Recurse -File -ErrorAction SilentlyContinue |
             Where-Object { $_.Extension -in @('.cs', '.axaml', '.csproj') -and $_.FullName -notmatch '\\(bin|obj|docs)\\' })
$axamls  = @($targets | Where-Object { $_.Extension -eq '.axaml' })
$csFiles = @($targets | Where-Object { $_.Extension -eq '.cs' })
$projs   = @($targets | Where-Object { $_.Extension -eq '.csproj' })

Write-Host ("扫描：.axaml {0} / .cs {1} / .csproj {2}" -f $axamls.Count, $csFiles.Count, $projs.Count) -ForegroundColor Gray

# ---------- 2) 规则常量 ----------
# G-07：撞框架类型的类名（实测撞过 + 与 Avalonia.Controls / Media / 基础类型同名会造成命名空间遮蔽）
$clashNames = @(
    'TaskStatus', 'MenuGroup', 'MenuItem', 'MenuFlyoutItem',
    'Menu', 'MenuFlyout', 'Color', 'Colors', 'Brush', 'SolidColorBrush',
    'Path', 'Border', 'Window', 'Button', 'Panel', 'Decorator', 'Control',
    'Style', 'Size', 'Point', 'Rect', 'Grid', 'Image', 'TextBlock', 'Icon',
    'Thickness', 'CornerRadius', 'Theme', 'ThemeVariant'
)
# G-06：图标字体名特征（大小写不敏感；合法的等宽/正文字体如 Consolas / Inter / Segoe UI 不在内）
$iconFontRe = '(?i)(awesome|fluent[\s\-]?icons|mdl2|material[\s\-]?icons|icomoon|iconfont|bootstrap[\s\-]?icons|octicons|tabler|remixicon|themify|glyphicons|devicon)'
# G-05：字符当勾选框（☑ U+2611 / ☐ U+2610 / ▣ U+25A3）—— 用 \u 转义，不依赖脚本文件编码
$checkCharRe = '(?:Text|Content)\s*=\s*"\s*[\u2611\u2610\u25A3]\s*"'

# ---------- 3) .axaml ----------
foreach ($f in $axamls) {
    $clean = Get-CodeText $f.FullName
    $isResDef = ($clean -match 'x:Key="[\w]+Brush"')   # 主题/资源定义文件：G-01 放行
    $lines = $clean -split "\r?\n"
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $ln = $lines[$i]
        $no = $i + 1

        if ($ln -match 'x:DataType\s*=') {
            Add-Finding 'G-09' 'WARN' $f.FullName $no '出现 x:DataType —— 编译绑定已全工程关闭，只写 {Binding Xxx}'
        }
        if ($ln -match $checkCharRe) {
            Add-Finding 'G-05' 'ERROR' $f.FullName $no '用字符当勾选框（Text/Content = ☑/☐/▣）—— 表头勾选必须原生三态 CheckBox'
        }
        if ($ln -match 'MergedDictionaries\s*\.\s*Clear\s*\(') {
            Add-Finding 'G-02' 'ERROR' $f.FullName $no 'MergedDictionaries.Clear() —— ApplyTheme 必须幂等，清空会丢 Fluent 内置样式'
        }
        if (-not $isResDef -and $ln -match '#[0-9A-Fa-f]{6}\b') {
            Add-Finding 'G-01' 'WARN' $f.FullName $no "硬编码颜色 $($Matches[0]) —— 改用 DynamicResource 语义键"
        }
        if ($ln -match 'FontFamily\s*=\s*"([^"]*)"') {
            $fv = $Matches[1]
            if ($fv -match $iconFontRe) {
                Add-Finding 'G-06' 'ERROR' $f.FullName $no "FontFamily 指向图标字体（$fv）—— 图标走 Path 几何资源键，禁图标字体"
            }
        }
    }
    # G-03：跨行元素（<TextBlock … MinHeight= …>，中途不越过标签结束符）
    foreach ($m in [regex]::Matches($clean, '(?s)<TextBlock\b(?:(?!>).)*?MinHeight\s*=')) {
        Add-Finding 'G-03' 'WARN' $f.FullName (Get-LineNo $clean $m.Index) 'TextBlock 上用 MinHeight 撑行高 —— 文字会贴顶；行高基线放 ListBoxItem MinHeight=28'
    }
}

# ---------- 4) .cs ----------
foreach ($f in $csFiles) {
    $clean = Get-CodeText $f.FullName
    $lines = $clean -split "\r?\n"
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $ln = $lines[$i]
        $no = $i + 1

        if ($ln -match 'public\s+override\s+void\s+On(?:AttachedToVisualTree|DetachedFromVisualTree)\s*\(') {
            Add-Finding 'G-04' 'ERROR' $f.FullName $no '可视树回调写了 public override —— 必须是 protected override（否则 CS0507）'
        }
        if ($ln -match 'MergedDictionaries\s*\.\s*Clear\s*\(') {
            Add-Finding 'G-02' 'ERROR' $f.FullName $no 'MergedDictionaries.Clear() —— ApplyTheme 必须幂等'
        }
        if ($ln -match '\b(?:class|record|struct|enum)\s+([A-Za-z_]\w*)\b') {
            $nm = $Matches[1]
            # ⚠️ 必须用 -ccontains（区分大小写）：C# 区分大小写，Win32 P/Invoke 的 struct RECT / POINT
            #    与 Avalonia 的 Rect / Point 是两个类型、并不冲突；用不区分大小写的 -contains 会误报。
            if ($clashNames -ccontains $nm) {
                Add-Finding 'G-07' 'ERROR' $f.FullName $no "类型名 $nm 撞框架类型 —— 改名避开（新增模型先 grep Avalonia ref）"
            }
        }
        if ($ln -match 'FontFamily\s*=\s*"([^"]*)"') {
            $fv = $Matches[1]
            if ($fv -match $iconFontRe) {
                Add-Finding 'G-06' 'ERROR' $f.FullName $no "FontFamily 指向图标字体（$fv）—— 图标走 Path 几何资源键，禁图标字体"
            }
        }
        # G-14：插值串里的单个 \（CS8087 + CS1009）
        #   · 先取出 $"..."; 再看内容里有没有「\ + 非法转义字符」；(?<!\\) 排除 \\ 的第二个反斜杠
        #   · $@"..." verbatim 插值串里 \ 不是转义 ⇒ 整行跳过
        if ($ln -notmatch '\$@"|@\$"') {
            foreach ($im in [regex]::Matches($ln, '\$"[^"\r\n]*"')) {
                if ($im.Value -match '(?<!\\)\\[^\\"0abfnrtvux{}]') {
                    Add-Finding 'G-14' 'ERROR' $f.FullName $no '插值串里有单个 \ —— CS8087/CS1009；反斜杠必须写 \\'
                    break
                }
            }
        }
    }
}

# ---------- 5) .csproj ----------
foreach ($p in $projs) {
    $projName = $p.BaseName                        # Module.PcTools / Shell / Shared / Core
    $isModule = ($projName -match '^Module\.')
    $isBase   = ($projName -match '^(Shared|Core)$')
    $clean = Get-CodeText $p.FullName
    $lines = $clean -split "\r?\n"
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $ln = $lines[$i]
        $no = $i + 1

        if ($ln -match 'Include\s*=\s*"[^"]*defaults[^"]*"') {
            Add-Finding 'G-08' 'ERROR' $p.FullName $no 'defaults\ 用了 Include= —— 会 NETSDK1022；必须 None Update="defaults\**"'
        }
        if ($isModule -and $ln -match 'ProjectReference\s+Include\s*=\s*"([^"]+)"') {
            $ref = [System.IO.Path]::GetFileNameWithoutExtension($Matches[1])
            if ($ref -match '(?i)shell') {
                Add-Finding 'G-10' 'ERROR' $p.FullName $no "模块引了 Shell（$ref）—— 分层单向 Shared <- Core <- Shell <- Module.*"
            } elseif ($ref -match '^Module\.' -and $ref -ne $projName) {
                Add-Finding 'G-10' 'ERROR' $p.FullName $no "模块互引（$projName -> $ref）—— 跨模块契约只放 Shared / Core"
            }
        }
        if ($isBase -and $ln -match 'PackageReference\s+Include\s*=\s*"([^"]+)"') {
            if ($Matches[1] -match '(?i)avalonia') {
                Add-Finding 'G-11' 'ERROR' $p.FullName $no "$projName 引了 Avalonia 包（$($Matches[1])）—— Shared / Core 不引 Avalonia"
            }
        }
    }
}

# ---------- 6) 输出 ----------
$errs  = @($findings | Where-Object { $_.Level -eq 'ERROR' })
$warns = @($findings | Where-Object { $_.Level -eq 'WARN' })

Write-Host "`n=== 汇总 ===" -ForegroundColor Cyan
if ($errs.Count -gt 0) {
    Write-Host "`n[ERROR] $($errs.Count) 条（会编译失败 / 运行必错 / 破坏架构边界）：" -ForegroundColor Red
    foreach ($r in $errs) { Write-Host ("  [{0}] {1}  {2}" -f $r.Id, $r.Loc, $r.Msg) -ForegroundColor Red }
} else {
    Write-Host "`n[ERROR] 0 条 ✅" -ForegroundColor Green
}

if ($warns.Count -gt 0) {
    if ($All) {
        Write-Host "`n[WARN] $($warns.Count) 条（违反约定但仍能编译）：" -ForegroundColor Yellow
        foreach ($r in $warns) { Write-Host ("  [{0}] {1}  {2}" -f $r.Id, $r.Loc, $r.Msg) -ForegroundColor Yellow }
    } else {
        Write-Host "[WARN] $($warns.Count) 条（明细加 -All 查看）" -ForegroundColor Yellow
    }
} else {
    Write-Host "[WARN] 0 条 ✅" -ForegroundColor Green
}

Write-Host "`n提示：规则出处 skill/references/tech-guard.md；本脚本只抓常见硬伤，完整编译由用户本地 build.ps1 验证。" -ForegroundColor Gray
exit 0
