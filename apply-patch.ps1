#Requires -Version 7.0
<#
.SYNOPSIS
    NewToys 补丁交互式应用助手。

.DESCRIPTION
    自动扫描仓库根与 patches/ 目录下的 *.patch 文件（按 p<编号> 排序），
    弹出编号菜单供你点选；选中后先用 `git apply --check` 预检查，全部通过才真正应用。
    支持多选、区间、全选、只列出、取消。

.PARAMETER PatchDir
    指定补丁所在目录。不给则在仓库根与 ./patches 下扫描。

.PARAMETER SearchDir
    默认扫描目录（相对仓库根），默认 @('.', 'patches')。

.PARAMETER DryRun
    只做预检查，不实际应用。

.PARAMETER Auto
    直接应用指定补丁（文件名精确或包含匹配），不弹菜单，便于批处理。

.EXAMPLE
    pwsh ./apply-patch.ps1

.EXAMPLE
    pwsh ./apply-patch.ps1 -PatchDir D:\Downloads

.EXAMPLE
    pwsh ./apply-patch.ps1 -DryRun

.EXAMPLE
    pwsh ./apply-patch.ps1 -Auto p21-fix-configfileentry-names.patch
#>
[CmdletBinding()]
param(
    [string]   $PatchDir  = '',
    [string[]] $SearchDir = @('.', 'patches'),
    [switch]   $DryRun,
    [string]   $Auto      = ''
)

$ErrorActionPreference = 'Stop'

try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

# ---------------- 输出小工具 ----------------
function Write-Title { param([string]$T) Write-Host $T -ForegroundColor Cyan }
function Write-Ok    { param([string]$T) Write-Host "  [OK]   $T" -ForegroundColor Green }
function Write-Bad   { param([string]$T) Write-Host "  [X]    $T" -ForegroundColor Red }
function Write-Warn2 { param([string]$T) Write-Host "  [!]    $T" -ForegroundColor Yellow }
function Write-Dimln { param([string]$T) Write-Host "         $T" -ForegroundColor DarkGray }

# ---------------- 定位仓库根 ----------------
function Get-RepoRoot {
    $dir = (Get-Location).Path
    while (-not [string]::IsNullOrEmpty($dir)) {
        if (Test-Path -LiteralPath (Join-Path $dir '.git')) { return $dir }
        $parent = Split-Path -Parent $dir
        if ([string]::IsNullOrEmpty($parent) -or $parent -eq $dir) { break }
        $dir = $parent
    }
    return (Get-Location).Path
}

# ---------------- 计算相对路径 ----------------
function Get-RelPath {
    param([string]$Base, [string]$Full)
    $b0 = $Base.TrimEnd('\', '/')
    $f0 = $Full.TrimEnd('\', '/')
    if ($f0 -ieq $b0) { return '.' }
    $sep = [System.IO.Path]::DirectorySeparatorChar
    $b = $b0 + $sep
    if ($f0.StartsWith($b, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $f0.Substring($b.Length).Replace('\', '/')
    }
    return $Full
}

# ---------------- 从补丁文件名解析编号 ----------------
function Get-PatchNumber {
    param([string]$Name)
    if ($Name -match '^p(\d+)') { return [int]$Matches[1] }
    return [int]::MaxValue
}

# ---------------- 扫描补丁 ----------------
function Get-AvailablePatches {
    param([string]$RepoRoot, [string]$PatchDir, [string[]]$SearchDir)

    $roots = @()
    if (-not [string]::IsNullOrWhiteSpace($PatchDir)) {
        $roots += $PatchDir
    }
    else {
        foreach ($d in $SearchDir) {
            if ([System.IO.Path]::IsPathRooted($d)) { $roots += $d }
            else { $roots += (Join-Path $RepoRoot $d) }
        }
    }

    $list = New-Object System.Collections.Generic.List[object]
    $seen = @{}
    foreach ($r in $roots) {
        if (-not (Test-Path -LiteralPath $r -PathType Container)) { continue }
        $files = Get-ChildItem -LiteralPath $r -File -Filter '*.patch' -ErrorAction SilentlyContinue
        foreach ($f in $files) {
            $full = $f.FullName
            if ($seen.ContainsKey($full)) { continue }
            $seen[$full] = $true
            $list.Add([pscustomobject]@{
                    Number = Get-PatchNumber $f.Name
                    Name   = $f.Name
                    Full   = $full
                    Size   = $f.Length
                    MTime  = $f.LastWriteTime
                    Dir    = (Get-RelPath -Base $RepoRoot -Full $f.DirectoryName)
                })
        }
    }
    return ($list | Sort-Object Number, Name)
}

# ---------------- 菜单 ----------------
function Show-PatchMenu {
    param($Patches)
    Write-Host ''
    Write-Title ('===== 可用补丁（共 {0} 个，按编号排序）=====' -f $Patches.Count)
    for ($i = 0; $i -lt $Patches.Count; $i++) {
        $p = $Patches[$i]
        $sizeKb = '{0,8:N1} KB' -f ($p.Size / 1KB)
        $time = $p.MTime.ToString('MM-dd HH:mm')
        Write-Host ('  [{0,2}]  {1,-44} {2}  {3}  {4}' -f ($i + 1), $p.Name, $sizeKb, $time, $p.Dir)
    }
    Write-Host '  [ A]  全部应用'
    Write-Host '  [ L]  只列出详情，不应用'
    Write-Host '  [ 0]  取消'
    Write-Host ''
}

# ---------------- 解析选择 ----------------
function Resolve-Selection {
    param([string]$Text, [int]$Count)
    $raw = ([string]$Text).Trim()
    if ([string]::IsNullOrWhiteSpace($raw)) { return @{ Action = 'Cancel'; Indices = @() } }

    $u = $raw.ToUpperInvariant()
    if ($u -eq 'A') { return @{ Action = 'Apply'; Indices = @(1..$Count) } }
    if ($u -eq 'L') { return @{ Action = 'List'; Indices = @() } }
    if ($u -eq '0') { return @{ Action = 'Cancel'; Indices = @() } }

    $idx = New-Object System.Collections.Generic.List[int]
    foreach ($part in ($raw -split ',')) {
        $p = $part.Trim()
        if ($p -match '^\d+$') {
            $n = [int]$p
            if ($n -lt 1 -or $n -gt $Count) { throw "序号超出范围：$n（有效范围 1-$Count）" }
            [void]$idx.Add($n)
        }
        elseif ($p -match '^(\d+)\s*-\s*(\d+)$') {
            $a = [int]$Matches[1]; $b = [int]$Matches[2]
            if ($a -lt 1 -or $b -gt $Count -or $a -gt $b) { throw "区间无效：$p（有效范围 1-$Count）" }
            for ($i = $a; $i -le $b; $i++) { [void]$idx.Add($i) }
        }
        else {
            throw "无法识别的输入：`"$p`"（示例：1 / 1,3 / 1-3 / A / L / 0）"
        }
    }
    return @{ Action = 'Apply'; Indices = @($idx | Select-Object -Unique | Sort-Object) }
}

# ================================================================
#  主流程
# ================================================================
$repoRoot = Get-RepoRoot
Write-Host ''
Write-Title "仓库根：$repoRoot"
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot '.git'))) {
    Write-Warn2 '未检测到 .git 目录，git apply 可能失败（请确认在 Git 仓库内运行）。'
}

$patches = @(Get-AvailablePatches -RepoRoot $repoRoot -PatchDir $PatchDir -SearchDir $SearchDir)

# 仓库内没找到时，兜底扫一下用户下载目录
if ($patches.Count -eq 0 -and [string]::IsNullOrWhiteSpace($PatchDir)) {
    $dl = Join-Path $HOME 'Downloads'
    if (Test-Path -LiteralPath $dl) {
        $patches = @(Get-AvailablePatches -RepoRoot $repoRoot -PatchDir $dl -SearchDir $SearchDir)
        if ($patches.Count -gt 0) { Write-Warn2 "仓库内未找到补丁，已自动改扫下载目录：$dl" }
    }
}

if ($patches.Count -eq 0) {
    Write-Warn2 '没有找到任何 *.patch 文件。'
    if (-not [string]::IsNullOrWhiteSpace($PatchDir)) {
        Write-Dimln "扫描目录：$PatchDir"
    }
    else {
        $shown = foreach ($d in $SearchDir) {
            if ([System.IO.Path]::IsPathRooted($d)) { $d } else { Join-Path $repoRoot $d }
        }
        Write-Dimln ('扫描目录：' + ($shown -join ' , '))
    }
    return
}

# ---------------- 选择补丁 ----------------
$selected = $null

if (-not [string]::IsNullOrWhiteSpace($Auto)) {
    $hit = $patches | Where-Object { $_.Name -ieq $Auto } | Select-Object -First 1
    if (-not $hit) {
        $hit = $patches | Where-Object { $_.Name -like "*$Auto*" } | Select-Object -First 1
    }
    if (-not $hit) {
        Write-Bad "未找到匹配的补丁：$Auto"
        return
    }
    $selected = @($hit)
    Write-Host "自动模式：选中 $($hit.Name)"
}
else {
    while ($null -eq $selected) {
        Show-PatchMenu $patches
        $ans = Read-Host '请输入序号（可多选：1 / 1,3 / 1-3；A=全部；L=只列出；回车=取消）'
        try {
            $sel = Resolve-Selection -Text $ans -Count $patches.Count
        }
        catch {
            Write-Bad $_.Exception.Message
            Write-Host '  请重新输入。' -ForegroundColor Yellow
            continue
        }

        switch ($sel.Action) {
            'Cancel' {
                Write-Host '已取消，未应用任何补丁。' -ForegroundColor Yellow
                return
            }
            'List' {
                Write-Host ''
                Write-Title '补丁详情：'
                foreach ($p in $patches) {
                    Write-Host ("  {0}" -f $p.Name)
                    Write-Dimln ("路径：{0}" -f $p.Full)
                    Write-Dimln ('大小：{0:N1} KB    修改：{1}' -f ($p.Size / 1KB), $p.MTime.ToString('yyyy-MM-dd HH:mm:ss'))
                }
                [void](Read-Host '按回车返回菜单')
            }
            'Apply' {
                $picked = @($sel.Indices)
                if ($picked.Count -eq 0) { continue }
                $selected = @($picked | ForEach-Object { $patches[$_ - 1] })
            }
        }
    }
}

# ---------------- 应用 ----------------
$okCount = 0
$failCount = 0
$applied = New-Object System.Collections.Generic.List[string]

foreach ($p in $selected) {
    Write-Host ''
    Write-Title "---- [预检查] $($p.Name) ----"
    $check = & git -C $repoRoot apply --check --whitespace=nowarn -- $p.Full 2>&1
    $checkOk = ($LASTEXITCODE -eq 0)
    if (-not $checkOk) {
        Write-Bad '预检查未通过（补丁可能已应用过，或与当前代码冲突）'
        foreach ($l in @($check)) { Write-Dimln $l }
        $failCount++
        $go = Read-Host '  是否继续尝试后续补丁？(y/N)'
        if ($go -notmatch '^[yY]') { break }
        continue
    }
    Write-Ok '预检查通过'

    if ($DryRun) {
        Write-Warn2 'DryRun 模式：只预检查，不实际应用'
        $okCount++
        continue
    }

    Write-Title "---- [应用] $($p.Name) ----"
    $ap = & git -C $repoRoot apply --whitespace=nowarn -- $p.Full 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Ok '应用成功'
        $okCount++
        [void]$applied.Add($p.Name)
    }
    else {
        Write-Bad '应用失败'
        foreach ($l in @($ap)) { Write-Dimln $l }
        $failCount++
        $go = Read-Host '  是否继续尝试后续补丁？(y/N)'
        if ($go -notmatch '^[yY]') { break }
    }
}

# ---------------- 汇总 ----------------
Write-Host ''
if ($DryRun) {
    Write-Title ('预检查完成：通过 {0} / 失败 {1}（DryRun，未修改任何文件）' -f $okCount, $failCount)
}
else {
    $color = if ($failCount -eq 0) { 'Green' } else { 'Yellow' }
    Write-Host ('全部完成：成功 {0} / 失败 {1}' -f $okCount, $failCount) -ForegroundColor $color
    if ($applied.Count -gt 0) {
        Write-Host '已应用补丁：' -ForegroundColor Green
        foreach ($n in $applied) { Write-Dimln $n }
        Write-Host ''
        Write-Host '提示：如需回滚某个补丁，执行  git apply -R <补丁文件路径>' -ForegroundColor DarkGray
    }
}
