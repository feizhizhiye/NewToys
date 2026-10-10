#Requires -Version 7.0
<#
.SYNOPSIS
    NewToys 单模块构建脚本 —— 只编译指定的模块（可多选）。

.DESCRIPTION
    密集更新期用 build.ps1（全量 clean + 全量 build）；
    等工程稳定、只有个别模块需要改动时，用本脚本只编那几个模块，省时间。

    与既有工程约定保持一致：
      · 只调 `dotnet build` 单个模块项目，不新增任何 csproj / 脚本拷贝目标；
      · 模块依赖的 Shared / Core 由其 ProjectReference 自动带上并一起编译；
      · Release 的模块 DLL 仍由 src\Directory.Build.props 直出到
        artifacts\release\Modules\（全程零拷贝）。

    产物落点（由 src\Directory.Build.props 决定）：
      Debug   → artifacts\bin\Module.<名>\Debug\net10.0\Module.<名>.dll
      Release → artifacts\release\Modules\Module.<名>.dll

    ⚠️ Debug 补充动作（重要）：
      Debug 下宿主 NewToys.exe 是通过「项目引用」静态引用模块的，模块 DLL 必须
      位于宿主输出目录 artifacts\bin\Shell\Debug\net10.0\ 才会被加载。
      因此 Debug 编译完成后，本脚本会把新产物（本模块 DLL 连同 Shared/Core）
      同步进宿主目录，否则会出现「改了没生效 / 页面一直是旧的」。
      Release 不需要 —— 宿主从 Modules\ 目录扫描加载。

.PARAMETER Module
    模块名（不带 "Module." 前缀），可逗号分隔多个：-Module PcTools,VideoTools

.PARAMETER All
    编译 src\ 下全部模块（只编模块，不含 Shell）。

.PARAMETER Configuration
    Debug（默认）/ Release。

.PARAMETER Restore
    编译前先执行 dotnet restore。

.PARAMETER NoShellSync
    Debug 下跳过「同步进宿主目录」这一步（默认执行）。

.EXAMPLE
    pwsh ./scripts/build-module.ps1
    自动扫描 src\Module.*，弹菜单交互选择（可多选）。

.EXAMPLE
    pwsh ./scripts/build-module.ps1 -Module PcTools
    只编 Module.PcTools（Debug）。

.EXAMPLE
    pwsh ./scripts/build-module.ps1 -All -Configuration Release
    只编全部模块（Release），产物直出 artifacts\release\Modules\。

.NOTES
    只动模块时用它；要是改了 Shared / Core，直接编对应模块即可（依赖会自动带上），
    但 Debug 下宿主目录里的 Shared/Core 也会被本脚本一并同步。
#>
param(
    [string[]]$Module = @(),
    [switch]$All,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$Restore,
    [switch]$NoShellSync
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

# 读一行输入（兼容交互终端与管道重定向；EOF 返回空串）
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
        Write-Host "===== 可编译模块（自动扫描 src/Module.*）=====" -ForegroundColor Cyan
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

# 把模块名数组解析成模块对象数组（大小写不敏感；找不到报错）
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
Write-Host "NewToys 单模块构建" -ForegroundColor White
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

if ($Restore) {
    Write-Host ""
    Write-Step "还原 NuGet 包（全解决方案）"
    dotnet restore (Join-Path $SrcDir "NewToys.slnx")
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore 失败" }
}

$shellOut = Join-Path $RepoRoot "artifacts\bin\Shell\Debug\net10.0"   # Debug 宿主输出目录
$failed   = New-Object System.Collections.Generic.List[string]

foreach ($m in $selected) {
    Write-Host ""
    Write-Step "编译 Module.$($m.Name)  [$Configuration]"

    if (-not (Test-Path -LiteralPath $m.Csproj)) { Write-Warn "缺少项目文件：$($m.Csproj)"; [void]$failed.Add($m.Name); continue }

    dotnet build $m.Csproj -c $Configuration --nologo -v q
    if ($LASTEXITCODE -ne 0) { Write-Warn "编译失败：Module.$($m.Name)"; [void]$failed.Add($m.Name); continue }

    if ($Configuration -eq "Release") {
        $dll = Join-Path $RepoRoot ("artifacts\release\Modules\{0}.dll" -f $m.Dir.Name)
        if (Test-Path -LiteralPath $dll) { Write-Ok "产物: artifacts\release\Modules\$($m.Dir.Name).dll" }
        else { Write-Warn "未找到预期产物：$dll（请确认是从模块项目构建的）" }
    }
    else {
        $modOut = Join-Path $RepoRoot ("artifacts\bin\{0}\Debug\net10.0" -f $m.Dir.Name)
        Write-Ok "产物: artifacts\bin\$($m.Dir.Name)\Debug\net10.0\$($m.Dir.Name).dll"

        if (-not $NoShellSync) {
            if (-not (Test-Path -LiteralPath $shellOut)) {
                Write-Warn "宿主输出目录不存在（先跑一次 build.ps1）：$shellOut —— 已跳过同步"
            }
            else {
                # Debug 下宿主靠项目引用静态加载模块，必须把新产物放进宿主目录，否则「改了没生效」
                $names = @(
                    "$($m.Dir.Name).dll", "$($m.Dir.Name).pdb",
                    "NewToys.Shared.dll", "NewToys.Shared.pdb",
                    "NewToys.Core.dll",   "NewToys.Core.pdb"
                )
                $copied = 0
                foreach ($fn in $names) {
                    $src = Join-Path $modOut $fn
                    if (Test-Path -LiteralPath $src) { Copy-Item -LiteralPath $src -Destination $shellOut -Force; $copied++ }
                }
                Write-Ok "已同步 $copied 个文件 → artifacts\bin\Shell\Debug\net10.0\"
            }
        }
    }
}

Write-Host ""
if ($failed.Count -gt 0) {
    Write-Host ("构建结束：成功 {0} / 失败 {1}（{2}）" -f ($selected.Count - $failed.Count), $failed.Count, ($failed -join ', ')) -ForegroundColor Red
    exit 1
}
Write-Host ("构建完成：共 {0} 个模块（以 build 输出的 error/warning 计数为准）。" -f $selected.Count) -ForegroundColor Green
if ($Configuration -eq "Debug") { Write-Host "提示：Debug 产物已同步进宿主目录，直接 run.ps1 即可看到效果。" -ForegroundColor DarkGray }
