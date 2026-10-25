# NewToys 页面契约静态检查（P-01 / P-02）
# 用法（在仓库根执行）：pwsh skill/scripts/check-view-pairs.ps1 [-All]
#   -All  列出全部明细（默认最多 30 条，保持输出短）
#
# 🔴 为什么需要它：页面由菜单项上的**字符串** `ViewTypeName` 驱动，解析靠反射 + 命名约定
#    （`XxxView` → `XxxViewModel`，见 Shared/Services/ViewNaming.cs）。
#    一旦名字对不上，编译期不报错、运行期不抛异常 —— 表现为「页面打得开、整页空白」，
#    或者点菜单项得到「找不到视图类型」。这两条就是补回编译期兜底。
#
# P-01 ERROR  Views/ 下声明的 XxxView 在本程序集里找不到对应 XxxViewModel
#             （豁免：类声明上方 5 行内写 `// lint:view-no-vm`，用于纯静态壳页面）
# P-02 ERROR  RegisterFunction 第 5 个实参（viewName）在 src/ 里找不到同名 View 类
#             （名称拼错 / View 改名后漏改注册 ⇒ 菜单项永远打不开）
#
# ⛔ 只报告，不修改任何文件，不以非零码退出（自检是参考，不是门禁）。

param([switch]$All)

$ErrorActionPreference = 'Continue'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$src = Join-Path $repoRoot 'src'
Write-Host "`n=== NewToys 页面契约自检 ===" -ForegroundColor Cyan
Write-Host "仓库根: $repoRoot" -ForegroundColor Gray
if (-not (Test-Path $src)) { Write-Host '[FAIL] 找不到 src/' -ForegroundColor Red; exit 0 }

$findings = New-Object System.Collections.ArrayList
function Add-Finding([string]$id, [string]$level, [string]$loc, [string]$msg) {
    [void]$findings.Add([pscustomobject]@{ Id = $id; Level = $level; Loc = $loc; Msg = $msg })
}

# ---------- 0) 工具：注释置空（保换行 ⇒ 行号仍准确） ----------
function Get-CodeText([string]$path) {
    $raw = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    $raw = [regex]::Replace($raw, '(?s)/\*.*?\*/', { param($m) ($m.Value -replace '[^\r\n]', ' ') })
    $raw = [regex]::Replace($raw, '//[^\r\n]*', { param($m) ($m.Value -replace '[^\r\n]', ' ') })
    return $raw
}

# ---------- 1) 收集全部 View / ViewModel 类（按项目目录 = 程序集分组） ----------
$allCs = @(Get-ChildItem -Path $src -Recurse -Filter *.cs -File |
           Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })

$classRe = [regex]'(?m)^\s*(?:public|internal)\s+(?:sealed\s+|abstract\s+|partial\s+|static\s+)*class\s+([A-Za-z_]\w*)'
$classesByProject = @{}   # project -> HashSet<string>
$viewDecls = New-Object System.Collections.ArrayList

foreach ($f in $allCs) {
    $rel = $f.FullName.Substring($src.Length + 1)
    $project = ($rel -split '\\')[0]
    if (-not $classesByProject.ContainsKey($project)) {
        $classesByProject[$project] = New-Object 'System.Collections.Generic.HashSet[string]'
    }
    $text = [System.IO.File]::ReadAllText($f.FullName, [System.Text.Encoding]::UTF8)
    foreach ($m in $classRe.Matches($text)) {
        [void]$classesByProject[$project].Add($m.Groups[1].Value)
        if ($f.Name -like '*.axaml.cs') {
            [void]$viewDecls.Add([pscustomobject]@{
                Name = $m.Groups[1].Value; File = $f.FullName; Rel = $rel
                Project = $project; Index = $m.Index; Text = $text
            })
        }
    }
}

# View 类名全集（P-02 用）
$viewNames = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($v in $viewDecls) { [void]$viewNames.Add($v.Name) }

Write-Host ("View 类 {0} 个 / 类型集合覆盖 {1} 个项目" -f $viewDecls.Count, $classesByProject.Count) -ForegroundColor Gray

# ---------- 2) P-01：Views/ 下的 View 必须有同程序集的 ViewModel ----------
function Get-ViewModelName([string]$viewName) {
    if ($viewName.EndsWith('View')) { return $viewName.Substring(0, $viewName.Length - 4) + 'ViewModel' }
    return $viewName + 'ViewModel'
}

foreach ($v in $viewDecls) {
    if ($v.Rel -notmatch '\\Views\\') { continue }
    if ($v.Name -notlike '*View') { continue }

    # 豁免标记：类声明前 5 行内出现 `lint:view-no-vm`
    $head = $v.Text.Substring(0, $v.Index)
    $lines = $head -split "\r?\n"
    $tail = ($lines | Select-Object -Last 5) -join "`n"
    if ($tail -match 'lint:view-no-vm') { continue }

    $vmName = Get-ViewModelName $v.Name
    if (-not $classesByProject[$v.Project].Contains($vmName)) {
        Add-Finding 'P-01' 'ERROR' $v.Rel ("{0} 找不到 {1}（同程序集）—— 页面会静默空白" -f $v.Name, $vmName)
    }
}

# ---------- 3) P-02：RegisterFunction 的 viewName 必须能对上 View 类 ----------
# 参数位序：moduleKey, key, title, icon, viewName, order, desc, category
function Get-CallArgs([string]$text, [int]$openIdx) {
    $depth = 0; $start = $openIdx + 1
    $list = New-Object System.Collections.ArrayList
    $inStr = $false; $verbatim = $false
    for ($i = $openIdx; $i -lt $text.Length; $i++) {
        $c = $text[$i]
        if ($inStr) {
            if ($verbatim) {
                if ($c -eq '"') {
                    if ($i + 1 -lt $text.Length -and $text[$i + 1] -eq '"') { $i++ } else { $inStr = $false }
                }
            }
            else {
                if ($c -eq '\') { $i++ }
                elseif ($c -eq '"') { $inStr = $false }
            }
            continue
        }
        if ($c -eq '"') {
            $verbatim = ($i -gt 0 -and $text[$i - 1] -eq '@')
            $inStr = $true; continue
        }
        if ($c -eq '(') { $depth++; continue }
        if ($c -eq ')') {
            $depth--
            if ($depth -eq 0) { [void]$list.Add($text.Substring($start, $i - $start)); return $list }
            continue
        }
        if ($c -eq ',' -and $depth -eq 1) {
            [void]$list.Add($text.Substring($start, $i - $start)); $start = $i + 1; continue
        }
    }
    return $null
}

$callRe = [regex]'RegisterFunction\s*\('
$checkedCalls = 0
foreach ($f in $allCs) {
    if ($f.Name -eq 'MenuService.cs' -or $f.Name -eq 'IMenuService.cs') { continue }
    $text = Get-CodeText $f.FullName
    if ($text -notmatch 'RegisterFunction\s*\(') { continue }
    $rel = $f.FullName.Substring($src.Length + 1)

    foreach ($m in $callRe.Matches($text)) {
        $openIdx = $m.Index + $m.Length - 1
        $args = Get-CallArgs $text $openIdx
        if ($null -eq $args -or $args.Count -lt 5) { continue }
        $raw = $args[4].Trim()
        if ($raw -notmatch '^"([^"]+)"$') { continue }   # 非字面量（nameof / 变量）⇒ 跳过
        $viewName = $Matches[1]
        $checkedCalls++
        if (-not $viewNames.Contains($viewName)) {
            $line = (($text.Substring(0, $m.Index) -split "\r?\n").Count)
            Add-Finding 'P-02' 'ERROR' "${rel}:$line" ("注册的 viewName「{0}」在 src/ 里找不到同名 View 类" -f $viewName)
        }
    }
}

Write-Host ("RegisterFunction 注册点（字面量）{0} 处" -f $checkedCalls) -ForegroundColor Gray

# ---------- 4) 汇总 ----------
$errors = @($findings | Where-Object { $_.Level -eq 'ERROR' })
$warns  = @($findings | Where-Object { $_.Level -eq 'WARN' })

Write-Host "`n=== 汇总 ===" -ForegroundColor Cyan
Write-Host ("  [ERROR] {0} 条" -f $errors.Count) -ForegroundColor $(if ($errors.Count -gt 0) { 'Red' } else { 'Gray' })
Write-Host ("  [WARN]  {0} 条" -f $warns.Count) -ForegroundColor $(if ($warns.Count -gt 0) { 'Yellow' } else { 'Gray' })

if ($errors.Count -gt 0) {
    Write-Host "`n[ERROR 明细]" -ForegroundColor Red
    $limit = if ($All) { $errors.Count } else { [Math]::Min(30, $errors.Count) }
    for ($i = 0; $i -lt $limit; $i++) {
        Write-Host ("  {0}  {1}  {2}" -f $errors[$i].Id, $errors[$i].Loc, $errors[$i].Msg) -ForegroundColor Red
    }
    if ($limit -lt $errors.Count) { Write-Host ("  …另有 {0} 条，加 -All 看全。" -f ($errors.Count - $limit)) -ForegroundColor DarkGray }
}

Write-Host "`n提示：P-01 / P-02 都是 ERROR —— 命名对不上时页面不报错，只是空白或打不开。" -ForegroundColor Gray
