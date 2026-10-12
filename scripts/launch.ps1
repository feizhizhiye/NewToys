#Requires -Version 7.0
<#
.SYNOPSIS
    NewToys 统一启动器（纯 .ps1）—— 成功自动关窗 / 失败暂停留窗。

.DESCRIPTION
    包裹 scripts/ 下的其它 .ps1 脚本：成功（退出码 0）则直接返回，由其启动方式决定窗口关闭；
    失败（退出码非 0）则暂停等待人工查看，按任意键再关。
    用途：频繁 build / clean / release 时，避免每次都开一个需手动关闭的终端窗口
          （替代被禁用 .cmd 后的统一入口，本身仍是合规的 .ps1）。

    用法：  pwsh -File launch.ps1 <脚本名(不含 .ps1)> [参数...]
    例：    pwsh -File launch.ps1 build -Configuration Release
            pwsh -File launch.ps1 run-NewToys -Configuration Release
            pwsh -File launch.ps1 clean -Deep

    建议为每个常用命令建一个快捷方式，目标填
        pwsh -NoProfile -File "C:\...\scripts\launch.ps1" build
    双击即跑、成功自动关窗；出错时窗口停留供查看（按任意键关闭）。
#>
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Script,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$target    = Join-Path $ScriptDir ($Script + ".ps1")

if (-not (Test-Path -LiteralPath $target)) {
    Write-Host "✋ 找不到脚本: $target" -ForegroundColor Red
    Read-Host "按 Enter 键退出"
    exit 1
}

# 标记由本启动器拉起，子脚本据此跳过自身暂停（避免双重暂停）
$env:NEWTOYS_LAUNCHER = "1"

Write-Host "==> 启动 $target $($Arguments -join ' ')" -ForegroundColor Cyan
& pwsh -NoProfile -ExecutionPolicy Bypass -File $target @Arguments
$rc = $LASTEXITCODE

if ($rc -ne 0) {
    Write-Host ""
    Write-Host "[脚本以错误退出，退出码 $rc；按任意键关闭窗口]" -ForegroundColor Yellow
    Read-Host
}
exit $rc
