# NewToys 示例脚本 · PowerShell
# 用法：脚本库 →「示例：系统概览（PowerShell）」→ ▶ 运行（弹出终端窗口直接跑）
# 想改成自己的：右键「打开所在目录」，编辑本文件即可（示例文件只播种一次，删掉不会再回来）。

$ErrorActionPreference = 'Continue'

Write-Host '==== NewToys 示例脚本（PowerShell）====' -ForegroundColor Cyan
Write-Host ("时间       ：" + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
Write-Host ("计算机名   ：" + $env:COMPUTERNAME)
Write-Host ("当前用户   ：" + $env:USERNAME)
Write-Host ("PowerShell ：" + $PSVersionTable.PSVersion)
Write-Host ("脚本位置   ：" + $PSScriptRoot)
Write-Host ''

try
{
    $os = Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop
    Write-Host ("系统       ：" + $os.Caption + "（" + $os.Version + "）")
    Write-Host ("开机时长   ：" + [int]((Get-Date) - $os.LastBootUpTime).TotalHours + " 小时")
}
catch
{
    Write-Host ("系统信息读取失败：" + $_.Exception.Message) -ForegroundColor Yellow
}

Write-Host ''
Write-Host '提示：脚本库「🔍 扫描目录」可以把脚本目录下的新脚本一次性登记进来。' -ForegroundColor DarkGray
