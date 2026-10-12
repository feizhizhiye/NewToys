#Requires -Version 7.0
<#
.SYNOPSIS
    NewToys 全量 Release 构建（封装 build.ps1 -Configuration Release）。

.DESCRIPTION
    与 build.ps1 / clean.ps1 一组：全量（所有模块）Release 构建。
    当前平台限定为 win-x64（由 src\Directory.Build.props 锁定，框架依赖）。
    跨平台构建以后再说，本脚本不暴露 -Runtime。

.EXAMPLE
    pwsh ./scripts/release.ps1
    全量 Release 构建（默认不还原 NuGet 包）。

.EXAMPLE
    pwsh ./scripts/release.ps1 -Restore
    先还原再全量 Release 构建。
#>
param(
    [switch]$Restore
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

& "$ScriptDir/build.ps1" -Configuration Release @PSBoundParameters
