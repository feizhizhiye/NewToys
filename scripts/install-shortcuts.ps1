#Requires -Version 7.0
<#
.SYNOPSIS
    生成本机「双击即用」的 NewToys 快捷方式（.lnk），底层调用 launch.ps1 实现自动关窗。

.DESCRIPTION
    在 scripts\Shortcuts\ 下生成一组 .lnk，目标为
        pwsh -NoProfile -File "<repo>\scripts\launch.ps1" <命令> [参数...]
    双击这些 .lnk 即可运行对应脚本：成功自动关窗、失败暂停留窗，全程不敲命令。
    .lnk 含本机绝对路径，已写入 .gitignore 不入库；换机或路径变了重跑本脚本即可重建。

    用法：  pwsh -File scripts/install-shortcuts.ps1
    （只需跑一次；生成的 .lnk 可复制到桌面等处随时双击）
#>
$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Split-Path -Parent $ScriptDir
$Launch    = Join-Path $ScriptDir "launch.ps1"
$OutDir    = Join-Path $ScriptDir "Shortcuts"

# 解析 pwsh 路径：优先 $PSHOME，回退 Get-Command
$pwsh = if ($PSHOME -and (Test-Path -LiteralPath (Join-Path $PSHOME "pwsh.exe"))) {
    Join-Path $PSHOME "pwsh.exe"
}
else {
    $c = Get-Command pwsh -ErrorAction SilentlyContinue
    if (-not $c) { throw "找不到 pwsh，请确认 PowerShell 7 已安装并在 PATH 中" }
    $c.Source
}

if (-not (Test-Path -LiteralPath $Launch)) { throw "找不到 $Launch" }
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$ws = New-Object -ComObject WScript.Shell

$entries = @(
    @{ Name = "NewToys 构建 Debug";   Args = "build" }
    @{ Name = "NewToys 构建 Release"; Args = "build -Configuration Release" }
    @{ Name = "NewToys 全量发布";      Args = "release" }
    @{ Name = "NewToys 清理";          Args = "clean" }
    @{ Name = "NewToys 深度清理";      Args = "clean -Deep" }
    @{ Name = "NewToys 运行 Debug";   Args = "run-NewToys" }
    @{ Name = "NewToys 运行 Release"; Args = "run-NewToys -Configuration Release" }
)

foreach ($e in $entries) {
    $lnkPath = Join-Path $OutDir ($e.Name + ".lnk")
    $sc = $ws.CreateShortcut($lnkPath)
    $sc.TargetPath = $pwsh
    $sc.Arguments = "-NoProfile -File `"" + $Launch + "`" " + $e.Args
    $sc.WorkingDirectory = $RepoRoot
    $sc.Description = "NewToys: " + $e.Name
    $sc.Save()
    Write-Host "已生成: $lnkPath" -ForegroundColor Green
}

Write-Host ""
Write-Host "共生成 $($entries.Count) 个快捷方式，位于: $OutDir" -ForegroundColor Cyan
Write-Host "双击即可运行；成功自动关窗，失败暂停留窗。" -ForegroundColor Gray
