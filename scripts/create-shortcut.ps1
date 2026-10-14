#Requires -Version 7.0
<#
.SYNOPSIS
    为 Debug 产物 NewToys.exe 生成快捷方式到脚本所在目录。
    生成后请手动把它拖到任务栏，或右键"固定到任务栏"。
#>
param(
    [string]$Name = "NewToys (Debug)"
)

$ErrorActionPreference = "Stop"
trap {
    Write-Host ""
    Write-Host ("❌ 执行出错: " + $_) -ForegroundColor Red
    Read-Host "按 Enter 关闭窗口"
    exit 1
}

$RepoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $RepoRoot "artifacts\bin\Shell\Debug\net10.0\NewToys.exe"

if (-not (Test-Path -LiteralPath $exe)) {
    Write-Host ""
    Write-Host "✋ 未找到 Debug 产物: $exe" -ForegroundColor Yellow
    Write-Host "   请先构建 Debug 版本。" -ForegroundColor Gray
    Read-Host "按 Enter 键退出"
    exit 1
}

$lnkPath = Join-Path $PSScriptRoot "$Name.lnk"

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($lnkPath)
$shortcut.TargetPath       = $exe
$shortcut.WorkingDirectory = Split-Path -Parent $exe
$shortcut.IconLocation     = "$exe,0"
$shortcut.Description      = "启动 NewToys (Debug)"
$shortcut.Save()

Write-Host ""
Write-Host "✅ 已生成快捷方式：" -ForegroundColor Green
Write-Host "   $lnkPath" -ForegroundColor Gray
Write-Host "   目标: $exe" -ForegroundColor DarkGray
Write-Host ""
Write-Host "👉 接下来手动操作：" -ForegroundColor Cyan
Write-Host "   1. 打开上面的目录，找到 $Name.lnk" -ForegroundColor DarkGray
Write-Host "   2. 右键 → 固定到任务栏（或直接拖到任务栏）" -ForegroundColor DarkGray
Write-Host "   3. 固定后可以把 .lnk 删掉，任务栏上的快捷方式不受影响" -ForegroundColor DarkGray
Write-Host ""
Read-Host "按 Enter 键退出"