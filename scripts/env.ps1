#Requires -Version 7.0
<#
    测试 / 生产环境的「应用根」切换（NEWTOYS_HOME）。
    与 WPF 版一致：整根切换 —— config\ / data\ / logs\ 一起隔离，且不做任何数据迁移。

      .\scripts\env.ps1              查看当前环境
      .\scripts\env.ps1 test         切到测试根 artifacts\env\test
      .\scripts\env.ps1 prod         清掉 NEWTOYS_HOME，回到 %LocalAppData%\NewToys
#>
param(
    [ValidateSet("", "test", "prod")]
    [string]$Set = ""
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$TestRoot = Join-Path $RepoRoot "artifacts\env\test"

$current = [Environment]::GetEnvironmentVariable("NEWTOYS_HOME", "User")

if ($Set -eq "") {
    Write-Host "NEWTOYS_HOME(用户级) = $(if ($current) { $current } else { '(未设置 → %LocalAppData%\NewToys)' })"
    return
}

if ($Set -eq "test") {
    New-Item -ItemType Directory -Path $TestRoot -Force | Out-Null
    [Environment]::SetEnvironmentVariable("NEWTOYS_HOME", $TestRoot, "User")
    Write-Host "已切到测试环境: $TestRoot" -ForegroundColor Green
}
else {
    [Environment]::SetEnvironmentVariable("NEWTOYS_HOME", $null, "User")
    Write-Host "已切到生产环境: %LocalAppData%\NewToys" -ForegroundColor Green
}

Write-Host "（新开的进程才会读到新的环境变量）" -ForegroundColor DarkGray
