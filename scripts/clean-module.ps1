#Requires -Version 7.0
<#
.SYNOPSIS
    NewToys 单模块清理脚本 —— 只清理指定模块（可多选）的构建产物。

.DESCRIPTION
    与 clean.ps1 的分工：
      clean.ps1        全量清理（dotnet clean + 可选 -Deep/-Data）—— 密集更新期用。
      clean-module.ps1 只清个别模块 —— 稳定期只动个别模块时用，不动其他模块与宿主产物。

    清理内容（针对所选模块）：
      · dotnet clean <模块项目> -c <Configuration>
      · artifacts\obj\Module.<名>\              （中间产物）
      · artifacts\bin\Module.<名>\              （Debug 输出目录）
        artifacts\release\Modules\Module.<名>.dll(.pdb)   （Release 输出文件）
      · Debug 宿主副本 artifacts\bin\Shell\Debug\net10.0\Module.<名>.dll(.pdb)
        （可用 -KeepHostCopy 保留；注意宿主目录里没有该 DLL 时，Debug 启动会加载不到模块）

    支持 -WhatIf 预览将要删除的内容（只打印，不实际删除）。

.PARAMETER Module
    模块名（不带 "Module." 前缀），可逗号分隔多个：-Module PcTools,VideoTools

.PARAMETER All
    清理 src\ 下的全部模块。

.PARAMETER Configuration
    Debug（默认）/ Release —— 决定 dotnet clean 与要删除的输出目录。

.PARAMETER KeepHostCopy
    Debug 下保留宿主目录里的模块 DLL 副本（默认一并删除）。

.EXAMPLE
    pwsh ./scripts/clean-module.ps1
    自动扫描 src\Module.*，弹菜单交互选择（可多选）。

.EXAMPLE
    pwsh ./scripts/clean-module.ps1 -Module VideoTools -WhatIf
    预览只清理 Module.VideoTools 会删掉哪些路径，但不真的删除。

.EXAMPLE
    pwsh ./scripts/clean-module.ps1 -All -Configuration Release
    清理全部模块的 Release 产物。
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string[]]$Module = @(),
    [switch]$All,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$KeepHostCopy
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Split-Path -Parent $ScriptDir
$SrcDir    = Join-Path $RepoRoot "src"

# ---------- 输出辅助 ----------
function Write-Step { param([string]$Message) Write-Host "==> $Message" -ForegroundColor Cyan }
function Write-Info { param([string]$Message) Write-Host "    $Message" -ForegroundColor Gray }
function Write-Ok   { param([string]$Message) Write-Host "    $Message" -ForegroundColor Green }
function Write-Warn { param([string]$Message) Write-Host "    $Message" -ForegroundColor Yellow }

# ---------- 安全删除 ----------
function Remove-PathSafe {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param([Parameter(Mandatory = $true)][string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    if (-not (Test-Path -LiteralPath $Path)) { return }
    if ($PSCmdlet.ShouldProcess($Path, "删除")) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            Write-Info "删除: $Path"
        }
        catch {
            Write-Warn "跳过（文件被占用或权限不足）: $Path"
        }
    }
}

# ---------- 自动扫描 src\Module.* ----------
function Get-AvailableModules {
    if (-not (Test-Path -LiteralPath $SrcDir)) { throw "找不到源码目录：$SrcDir" }
    Get-ChildItem -LiteralPath $SrcDir -Directory -Filter "Module.*" | Sort-Object Name | ForEach-Object {
        [PSCustomObject]@{
            Name   = $_.Name -replace '^Module\.', ''
            Dir    = $_.FullName
            Csproj = Join-Path $_.FullName ("{0}.csproj" -f $_.Name)
        }
    }
}

function Read-UserLine {
    param([string]$Prompt)
    Write-Host $Prompt -NoNewline
    $line = $null
    try { $line = [Console]::ReadLine() } catch { $line = $null }
    Write-Host ""
    if ($null -eq $line) { return "" }
    return $line
}

# 交互式选择（同 export-module.ps1：1 / 1,3 / 1-3 / A / 0）
function Select-ModulesInteractive {
    param([object[]]$Modules)
    while ($true) {
        Write-Host ""
        Write-Host "===== 可清理模块（自动扫描 src/Module.*）=====" -ForegroundColor Cyan
        for ($i = 0; $i -lt $Modules.Count; $i++) {
            Write-Host ("  [{0}] {1,-16} Module.{1}" -f ($i + 1), $Modules[$i].Name)
        }
        Write-Host "  [A] 全部模块"
        Write-Host "  [0] 取消"
        Write-Host ""
        $raw = "$(Read-UserLine '请输入序号（可多选：1 / 1,3 / 1-3；A=全部；回车=取消）> ')".Trim()
        if ([string]::IsNullOrWhiteSpace($raw)) { Write-Host "已取消。" -ForegroundColor Yellow; return @() }
        if ($raw -imatch '^(a|all|\*)$') { return @($Modules) }
        if ($raw -eq '0') { Write-Host "已取消。" -ForegroundColor Yellow; return @() }

        $picked = New-Object System.Collections.Generic.List[object]
        $bad    = New-Object System.Collections.Generic.List[string]
        $ok     = $true
        foreach ($tok in ($raw -split '[,\s;，、]+' | Where-Object { $_ -ne '' })) {
            if ($tok -match '^(\d+)-(\d+)$') {
                $a = [int]$matches[1]; $b = [int]$matches[2]
                if ($a -gt $b) { $t = $a; $a = $b; $b = $t }
                for ($k = $a; $k -le $b; $k++) {
                    if ($k -ge 1 -and $k -le $Modules.Count) { [void]$picked.Add($Modules[$k - 1]) }
                    else { [void]$bad.Add("$k"); $ok = $false }
                }
            }
            elseif ($tok -match '^\d+$') {
                $n = [int]$tok
                if ($n -ge 1 -and $n -le $Modules.Count) { [void]$picked.Add($Modules[$n - 1]) }
                else { [void]$bad.Add($tok); $ok = $false }
            }
            else { [void]$bad.Add($tok); $ok = $false }
        }

        if (-not $ok -or $picked.Count -eq 0) {
            Write-Host ("输入无效：" + (($bad | Select-Object -Unique) -join ', ') + "，请重新输入。") -ForegroundColor Red
            continue
        }
        return @($picked | Sort-Object -Property Name -Unique)
    }
}

function Resolve-ModulesByName {
    param([object[]]$Available, [string[]]$Names)
    $resolved = New-Object System.Collections.Generic.List[object]
    foreach ($n in $Names) {
        if ([string]::IsNullOrWhiteSpace($n)) { continue }
        $hit = $Available | Where-Object { $_.Name -ieq "$n".Trim() } | Select-Object -First 1
        if ($null -eq $hit) {
            $avail = ($Available | ForEach-Object { $_.Name }) -join ', '
            throw "找不到模块：Module.$n（可用模块：$avail）"
        }
        [void]$resolved.Add($hit)
    }
    return @($resolved)
}

# ---------- 主流程 ----------
$modules = @(Get-AvailableModules)
if ($modules.Count -eq 0) { throw "src\ 下没有找到任何 Module.* 模块目录：$SrcDir" }

Write-Host ""
Write-Host "NewToys 单模块清理" -ForegroundColor White
Write-Host "配置: $Configuration    仓库根: $RepoRoot" -ForegroundColor DarkGray

if ($All) {
    $selected = @($modules)
}
elseif ($Module.Count -gt 0) {
    $selected = @(Resolve-ModulesByName -Available $modules -Names $Module)
}
else {
    $selected = @(Select-ModulesInteractive -Modules $modules)
}

if ($selected.Count -eq 0) { Write-Host "未选择任何模块，已退出。" -ForegroundColor Yellow; return }

$shellOut = Join-Path $RepoRoot "artifacts\bin\Shell\Debug\net10.0"   # Debug 宿主输出目录

foreach ($m in $selected) {
    Write-Host ""
    Write-Step "清理 Module.$($m.Name)  [$Configuration]"

    # 1. dotnet clean（项目未构建过时退出码非 0，可忽略）
    if (Test-Path -LiteralPath $m.Csproj) {
        if ($PSCmdlet.ShouldProcess($m.Csproj, "dotnet clean")) {
            Push-Location $RepoRoot
            try { dotnet clean $m.Csproj -c $Configuration --nologo -v q }
            finally { Pop-Location }
            if ($LASTEXITCODE -ne 0) { Write-Warn "dotnet clean 退出码为 $LASTEXITCODE（项目可能未构建过，可忽略）" }
        }
    }
    else {
        Write-Warn "缺少项目文件，跳过 dotnet clean：$($m.Csproj)"
    }

    # 2. 中间产物
    Remove-PathSafe -Path (Join-Path $RepoRoot ("artifacts\obj\{0}" -f $m.Dir.Name))

    # 3. 输出产物
    if ($Configuration -eq "Release") {
        $modDir = Join-Path $RepoRoot "artifacts\release\Modules"
        Remove-PathSafe -Path (Join-Path $modDir ("{0}.dll" -f $m.Dir.Name))
        Remove-PathSafe -Path (Join-Path $modDir ("{0}.pdb" -f $m.Dir.Name))
    }
    else {
        Remove-PathSafe -Path (Join-Path $RepoRoot ("artifacts\bin\{0}" -f $m.Dir.Name))

        if (-not $KeepHostCopy) {
            Remove-PathSafe -Path (Join-Path $shellOut ("{0}.dll" -f $m.Dir.Name))
            Remove-PathSafe -Path (Join-Path $shellOut ("{0}.pdb" -f $m.Dir.Name))
        }
        else { Write-Info "保留宿主目录副本（-KeepHostCopy）" }
    }
}

Write-Host ""
Write-Host "✅ 模块清理完成" -ForegroundColor Green
Write-Host "提示：对应模块下次需重新 build（build-module.ps1 -Module <名>）。" -ForegroundColor Yellow
