#Requires -Version 7.0
<#
    打包源码给 AI / 转发给别人（对齐 WPF 版 scripts\pack.ps1）。
    复用 export-framework.ps1，产出单文件 markdown + 自动排除 bin/obj/artifacts。
#>
param(
    [string]$OutFile = "",
    [string]$Module = ""
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($OutFile)) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmm"
    $OutFile = Join-Path $RepoRoot "artifacts\context\NewToys-$stamp.md"
}

New-Item -ItemType Directory -Path (Split-Path -Parent $OutFile) -Force | Out-Null

$exporter = Join-Path $PSScriptRoot "export-framework.ps1"
if (-not (Test-Path $exporter)) { throw "找不到 export-framework.ps1" }

Write-Host "==> 导出源码快照到 $OutFile" -ForegroundColor Cyan
& $exporter -Module $Module -OutFile $OutFile
if ($LASTEXITCODE -ne 0) { throw "导出失败" }

if (Test-Path $OutFile) {
    $size = [math]::Round((Get-Item $OutFile).Length / 1KB, 1)
    Write-Host "`n完成: $OutFile ($size KB)" -ForegroundColor Green
}
