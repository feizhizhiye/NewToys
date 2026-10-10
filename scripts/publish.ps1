#Requires -Version 7.0
<#
    发布 NewToys 到指定目录（默认 production\NewToys_win-x64）。
    ------------------------------------------------------------------
    不依赖 dotnet publish：直接把 Release 的输出目录重定向过去，
    因此模块 DLL 由 MSBuild 自己落进 <目标>\Modules\，脚本里没有任何拷贝步骤。

      .\scripts\publish.ps1
      .\scripts\publish.ps1 -Target D:\NewToys
#>
param(
    [ValidateSet("win-x64", "linux-x64", "osx-x64")]
    [string]$Runtime = "win-x64",
    [string]$Target = "",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Sln      = Join-Path $RepoRoot "src\NewToys.slnx"

if ([string]::IsNullOrWhiteSpace($Target)) {
    $Target = Join-Path $RepoRoot "production\NewToys_$Runtime"
}
# NewToysReleaseDir 必须以分隔符结尾，否则 OutDir 会被当成文件名
if (-not $Target.EndsWith([IO.Path]::DirectorySeparatorChar)) {
    $Target += [IO.Path]::DirectorySeparatorChar
}

Write-Host "==> 发布目标: $Target" -ForegroundColor Cyan

$args = @(
    "build", $Sln, "-c", "Release", "--nologo", "-v", "q",
    "-r", $Runtime,
    "--self-contained", $(if ($SelfContained) { "true" } else { "false" }),
    "/p:NewToysReleaseDir=$Target"
)
dotnet @args
if ($LASTEXITCODE -ne 0) { throw "发布失败" }

$modules = Join-Path $Target "Modules"
if (Test-Path $modules) {
    $dlls = @(Get-ChildItem $modules -Filter "Module.*.dll" -File)
    Write-Host "  模块 DLL: $($dlls.Count) 个 → Modules\" -ForegroundColor Gray
    if ($dlls.Count -eq 0) { Write-Host "  [警告] Modules\ 是空的" -ForegroundColor Yellow }
}

Write-Host "`n发布完成: $Target" -ForegroundColor Green
