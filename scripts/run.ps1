#Requires -Version 7.0
<#
    启动 NewToys。
      .\scripts\run.ps1                      启动 Release 产物（artifacts\release\NewToys.exe）
      .\scripts\run.ps1 -Configuration Debug 启动 Debug 产物
      .\scripts\run.ps1 -Env test            临时把 NEWTOYS_HOME 指到测试根（只影响本次进程）
      .\scripts\run.ps1 -NoBuild             直接跑，不重新编译
#>
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidateSet("", "test", "prod")]
    [string]$Env = "",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
trap { Write-Host ("❌ 执行出错: " + $_) -ForegroundColor Red; Read-Host "按 Enter 关闭窗口"; exit 1 }

$RepoRoot = Split-Path -Parent $PSScriptRoot

if (-not $NoBuild) {
    & (Join-Path $PSScriptRoot "build.ps1") -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "构建失败" }
}

# -Env 只作用于本进程（测试不需要污染用户级环境变量）
if ($Env -eq "test") {
    $testRoot = Join-Path $RepoRoot "artifacts\env\test"
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    $env:NEWTOYS_HOME = $testRoot
    Write-Host "应用根隔离到: $testRoot" -ForegroundColor DarkGray
}
elseif ($Env -eq "prod") {
    Remove-Item Env:NEWTOYS_HOME -ErrorAction SilentlyContinue
}

$exe = if ($Configuration -eq "Release") {
    Join-Path $RepoRoot "artifacts\release\NewToys.exe"
}
else {
    Join-Path $RepoRoot "artifacts\bin\Shell\Debug\net10.0\NewToys.exe"
}

if (-not (Test-Path $exe)) { throw "找不到可执行文件: $exe" }

Write-Host "==> 启动 $exe" -ForegroundColor Cyan
Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe)

exit 0
