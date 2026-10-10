<#
.SYNOPSIS
    NewToys 项目清理脚本。

.DESCRIPTION
    默认只做轻度清理：dotnet clean + 清理仓库根目录残留的 Modules/。
    可选开关（可任意组合）：
      -Deep   深度清理：artifacts/、production/ 以及所有 bin/、obj/
      -Data   清理用户数据：var/logs、var/cache、var/temp
      -All    等价于 -Deep -Data

    支持 -WhatIf 预览将要删除的内容（只打印，不实际删除）。

.PARAMETER Deep
    深度清理构建产物：artifacts/、production/ 目录，以及仓库内所有 bin/ obj/。

.PARAMETER Data
    清理用户数据：var/logs、var/cache、var/temp（不会删除 var/config）。

.PARAMETER All
    等价于同时指定 -Deep 与 -Data。

.EXAMPLE
    pwsh ./scripts/clean.ps1
    默认清理：dotnet clean + 根目录残留 Modules/。

.EXAMPLE
    pwsh ./scripts/clean.ps1 -Deep
    额外清空 artifacts/、production/ 和所有 bin/ obj/。

.EXAMPLE
    pwsh ./scripts/clean.ps1 -All -WhatIf
    预览 -All 会删除哪些路径，但不真的删除。

.NOTES
    仓库结构假设（可自行调整变量）：
      <RepoRoot>/
        src/
        scripts/
        artifacts/     <- 构建输出（项目根目录下）
        production/    <- 发布产物
        var/
          config/      <- 配置，不会被本脚本删除
          logs/ cache/ temp/
        Modules/       <- 运行期残留目录，默认清理
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$Deep,
    [switch]$Data,
    [switch]$All
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Split-Path -Parent $ScriptDir

# ---------- 输出辅助 ----------
function Write-Step { param([string]$Message) Write-Host "==> $Message" -ForegroundColor Cyan }
function Write-Info { param([string]$Message) Write-Host "    $Message" -ForegroundColor Gray }
function Write-Ok   { param([string]$Message) Write-Host "    $Message" -ForegroundColor Green }
function Write-Warn { param([string]$Message) Write-Host "    $Message" -ForegroundColor Yellow }

# ---------- 安全删除辅助 ----------
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

Write-Host ""
Write-Host "NewToys 清理脚本" -ForegroundColor White
Write-Host "仓库根: $RepoRoot" -ForegroundColor DarkGray
Write-Host ""

# ---------- 1. dotnet clean ----------
Write-Step "清理构建产物（dotnet clean）"

# 优先在 src/ 下找解决方案，找不到再找仓库根；两者都没有则退化为清理 Shell 项目
$solution = $null
foreach ($dir in @((Join-Path $RepoRoot "src"), $RepoRoot)) {
    if (-not (Test-Path -LiteralPath $dir)) { continue }
    $solution = Get-ChildItem -LiteralPath $dir -File -Force -ErrorAction SilentlyContinue |
                Where-Object { $_.Extension -in @(".slnx", ".sln") } |
                Select-Object -First 1
    if ($solution) { break }
}

Push-Location $RepoRoot
try {
    if ($solution) {
        Write-Info "解决方案: $($solution.FullName)"
        dotnet clean $solution.FullName
    }
    else {
        $shellProj = Join-Path $RepoRoot "src/Shell/Shell.csproj"
        if (Test-Path -LiteralPath $shellProj) {
            Write-Warn "未找到 .slnx/.sln，改为清理 Shell 项目"
            dotnet clean $shellProj
        }
        else {
            Write-Warn "未找到 .slnx/.sln 及 src/Shell/Shell.csproj，跳过 dotnet clean"
        }
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Warn "dotnet clean 退出码为 $LASTEXITCODE（项目可能未构建过，可忽略）"
    }
}
finally {
    Pop-Location
}

# ---------- 2. 深度清理 ----------
if ($Deep -or $All) {
    Write-Step "深度清理 artifacts / production / bin / obj"

    $targets   = New-Object System.Collections.Generic.List[string]
    $skipRoots = @("artifacts", "production", ".git", ".vs") |
                 ForEach-Object { Join-Path $RepoRoot $_ }

    # 整体产物目录（artifacts 位于项目根目录下）
    foreach ($name in @("artifacts", "production")) {
        $p = Join-Path $RepoRoot $name
        if (Test-Path -LiteralPath $p) { $targets.Add($p) }
    }

    # 仓库内所有 bin / obj（跳过 artifacts、production、.git、.vs 内部）
    Get-ChildItem -LiteralPath $RepoRoot -Directory -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object {
            if ($_.Name -notin @("bin", "obj")) { return $false }
            foreach ($skip in $skipRoots) {
                if ($_.FullName.StartsWith($skip, [System.StringComparison]::OrdinalIgnoreCase)) {
                    return $false
                }
            }
            return $true
        } |
        ForEach-Object { $targets.Add($_.FullName) }

    # 去重后按路径深度倒序删除：先删最深的，避免父目录先删导致子路径失效
    $targets |
        Sort-Object -Property { ($_ -split '[\\/]').Count } -Descending |
        Select-Object -Unique |
        ForEach-Object { Remove-PathSafe -Path $_ }
}

# ---------- 3. 用户数据 ----------
if ($Data -or $All) {
    Write-Step "清理用户数据（var/logs、var/cache、var/temp）"
    $varDir = Join-Path $RepoRoot "var"
    foreach ($name in @("logs", "cache", "temp")) {
        Remove-PathSafe -Path (Join-Path $varDir $name)
    }
    Write-Info "var/config 未清理（保留配置）"
}

# ---------- 4. 根目录残留 Modules ----------
$rootModules = Join-Path $RepoRoot "Modules"
if (Test-Path -LiteralPath $rootModules) {
    Write-Step "清理根目录残留的 Modules/"
    Remove-PathSafe -Path $rootModules
}

Write-Host ""
Write-Host "✅ 清理完成" -ForegroundColor Green

if ($Deep -or $All) {
    Write-Host "提示：构建产物已清空，运行前请先执行 ./scripts/build.ps1 重新生成。" -ForegroundColor Yellow
}
