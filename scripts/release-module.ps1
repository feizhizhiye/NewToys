#Requires -Version 7.0
<#
.SYNOPSIS
    NewToys 指定模块 Release 构建（封装 build-module.ps1 -Configuration Release）。

.DESCRIPTION
    与 build-module.ps1 / clean-module.ps1 一组：只构建指定模块（可多选）的 Release 产物，
    直出 artifacts\release\Modules\。
    当前平台限定为 win-x64，不暴露 -Runtime（见 build-module.ps1 约定）。

.PARAMETER Module
    模块名（不带 "Module." 前缀），可逗号分隔多个：-Module PcTools,VideoTools
.PARAMETER All
    构建全部模块（只编模块，不含 Shell）。
.PARAMETER Restore
    构建前先还原 NuGet 包。
.PARAMETER NoShellSync
    Debug 下跳过宿主目录同步（Release 本就不需要，保留开关以对齐 build-module.ps1）。

.EXAMPLE
    pwsh ./scripts/release-module.ps1 -Module VideoTools
    只构建 Module.VideoTools 的 Release 产物到 Modules\。
#>
param(
    [string[]]$Module = @(),
    [switch]$All,
    [switch]$Restore,
    [switch]$NoShellSync
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

& "$ScriptDir/build-module.ps1" -Configuration Release @PSBoundParameters
