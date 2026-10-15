# NewToys 详情卡描述符「绑定路径」静态检查（NT-054 / 需求 §4.5.1）
# 用法（在仓库根执行）：pwsh skill/scripts/check-detail-paths.ps1 [-All]
#   -All  列出全部 WARN 明细（默认最多列 30 条，保持输出短）
#
# 🔴 为什么需要它：声明式详情卡的 `ValuePath` / `VisibleWhen` / `OptionsPath` … 都是**字符串**，
#    页面 VM 属性改名后**不报错、不崩溃**，只是白板（同「首页 AppMenuItem.Icon 字符串驱动」那类技术债）。
#    这条检查就是补回编译期兜底 —— ⛔ 替代不了 `check-axaml-props.ps1`，两者互补。
#
# ⛔ 只报告，不修改任何文件，不以非零码退出（自检是参考，不是门禁）。
# ⛔ 口径：WARN = 路径**首段**在 src/**/*.cs 里找不到同名属性（可能是改名了，也可能是拼错）。
#    启发式：全仓库收集属性名 + 记录名，命中即通过 → 允许极少量假阴性。

param([switch]$All)

$ErrorActionPreference = 'Continue'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$src = Join-Path $repoRoot 'src'
Write-Host "`n=== NewToys 详情卡绑定路径自检 ===" -ForegroundColor Cyan
Write-Host "仓库根: $repoRoot" -ForegroundColor Gray
if (-not (Test-Path $src)) { Write-Host '[FAIL] 找不到 src/' -ForegroundColor Red; exit 0 }

# ---------- 0) 工具：注释置空（保换行 ⇒ 行号仍准确） ----------
function Get-CodeText([string]$path) {
    $raw = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    $raw = [regex]::Replace($raw, '(?s)<!--.*?-->', { param($m) ($m.Value -replace '[^\r\n]', ' ') })
    $raw = [regex]::Replace($raw, '(?s)/\*.*?\*/', { param($m) ($m.Value -replace '[^\r\n]', ' ') })
    $raw = [regex]::Replace($raw, '//[^\r\n]*', { param($m) ($m.Value -replace '[^\r\n]', ' ') })
    return $raw
}
function Get-LineNo([string]$text, [int]$index) {
    return (($text.Substring(0, $index) -split "\r?\n").Count)
}

$csFiles = Get-ChildItem -Path $src -Recurse -Filter *.cs -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }

# ---------- 1) 全仓库属性名 / 字段名集合（宽松：public / internal / protected 都收） ----------
$names = New-Object 'System.Collections.Generic.HashSet[string]'
$declRe = [regex]'(?:public|internal|protected)\s+(?:static\s+|readonly\s+|partial\s+|override\s+|virtual\s+)*[A-Za-z_][\w<>\[\],\.\? ]*\s+([A-Za-z_]\w*)\s*(?:\{|=>|;|=)'
foreach ($f in $csFiles) {
    $txt = Get-CodeText $f.FullName
    foreach ($m in $declRe.Matches($txt)) { [void]$names.Add($m.Groups[1].Value) }
}
Write-Host ("属性 / 字段名共 {0} 个（用于比对）" -f $names.Count) -ForegroundColor Gray

# ---------- 2) 扫含描述符路径的文件 ----------
$pathProps = 'ValuePath|VisibleWhen|EnabledWhen|OptionsPath|OptionValuePath|OptionDisplayPath|ChipTextPath|ChipSelectedPath|ChipCommandPath|IsEnabledPath|IsVisiblePath|TogglePath|IsExpandedPath|BodyVisiblePath|SamplePath'
$assignRe = [regex]("(?:$pathProps)\s*=\s*""([^""]*)""")

$findings = New-Object System.Collections.ArrayList
$scanned = 0
foreach ($f in $csFiles) {
    $txt = Get-CodeText $f.FullName
    if ($txt -notmatch 'IDetailCardSource|DetailCardDescriptor') { continue }
    $scanned++
    foreach ($m in $assignRe.Matches($txt)) {
        $path = $m.Groups[1].Value
        if ([string]::IsNullOrWhiteSpace($path)) { continue }
        $head = $path.TrimStart('!').Split('.')[0]
        # 常见情形：`VisibleWhen = "!" + nameof(X)` —— 字符串字面量只有取反前缀，首段为空 ⇒ 跳过（不是路径）
        if ([string]::IsNullOrEmpty($head)) { continue }
        if (-not $names.Contains($head)) {
            $line = Get-LineNo $txt $m.Index
            $rel = $f.FullName.Substring($src.Length + 1)
            [void]$findings.Add([pscustomobject]@{ Loc = "${rel}:$line"; Path = $path })
        }
    }
}

Write-Host ("含描述符的 .cs 文件 {0} 个" -f $scanned) -ForegroundColor Gray

# ---------- 3) 输出 ----------
if ($findings.Count -eq 0) {
    Write-Host "[OK] 未发现可疑的绑定路径。" -ForegroundColor Green
    Write-Host ""
    exit 0
}

Write-Host ("[WARN] {0} 条绑定路径的首段在 src/ 里找不到同名属性（改名 / 拼错？）" -f $findings.Count) -ForegroundColor Yellow
$limit = if ($All) { $findings.Count } else { [Math]::Min(30, $findings.Count) }
for ($i = 0; $i -lt $limit; $i++) {
    Write-Host ("  {0}  →  {1}" -f $findings[$i].Loc, $findings[$i].Path) -ForegroundColor Yellow
}
if ($limit -lt $findings.Count) { Write-Host ("  …另有 {0} 条，加 -All 看全。" -f ($findings.Count - $limit)) -ForegroundColor DarkGray }
Write-Host ""
exit 0
