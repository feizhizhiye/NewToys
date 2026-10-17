# cpu.ps1 —— 信息展示页默认脚本：CPU（NT-067）
$ErrorActionPreference = 'Stop'

$rows = New-Object System.Collections.ArrayList

function AddRow([string]$key, [string]$name, $value, [string]$note) {
    [void]$rows.Add([pscustomobject]@{ key = $key; status = 'ok'; values = @('CPU', $name, [string]$value, $note) })
}

$name = ''
try {
    $p = Get-ItemProperty -Path 'HKLM:\HARDWARE\DESCRIPTION\System\CentralProcessor\0' -ErrorAction Stop
    if ($p.ProcessorNameString) { $name = ($p.ProcessorNameString).Trim() }
} catch { }

if ([string]::IsNullOrWhiteSpace($name)) { $name = '（未知）' }

AddRow 'cpu-name' '处理器' $name 'HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0'
AddRow 'cpu-count' '逻辑处理器数' ([Environment]::ProcessorCount) 'Environment.ProcessorCount（含超线程）'
AddRow 'cpu-arch' '进程位数' ($env:PROCESSOR_ARCHITECTURE) '脚本宿主进程的架构'

[Console]::Out.Write(([pscustomobject]@{ rows = @($rows) } | ConvertTo-Json -Depth 6 -Compress))
