# cpu.ps1 —— 信息面板默认采集脚本：CPU（NT-068）
# 契约：只往 stdout 输出 JSON（{ "rows": [ { "key", "status", "attrs" } ] }），⛔ 不写日志。
$ErrorActionPreference = 'Stop'

$rows = New-Object System.Collections.ArrayList

function AddRow([string]$key, $attrs) {
    [void]$rows.Add([pscustomobject]@{ key = $key; status = 'ok'; attrs = $attrs })
}

$name = ''
try {
    $p = Get-ItemProperty -Path 'HKLM:\HARDWARE\DESCRIPTION\System\CentralProcessor\0' -ErrorAction Stop
    if ($p.ProcessorNameString) { $name = ($p.ProcessorNameString).Trim() }
} catch { }

if ([string]::IsNullOrWhiteSpace($name)) { $name = '（未知）' }

AddRow 'cpu-name'  ([ordered]@{ '属性' = '处理器'; '值' = $name; '说明' = 'HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0' })
AddRow 'cpu-count' ([ordered]@{ '属性' = '逻辑处理器数'; '值' = ([Environment]::ProcessorCount); '说明' = 'Environment.ProcessorCount（含超线程）' })
AddRow 'cpu-arch'  ([ordered]@{ '属性' = '进程位数'; '值' = $env:PROCESSOR_ARCHITECTURE; '说明' = '脚本宿主进程的架构' })

[Console]::Out.Write(([pscustomobject]@{ rows = @($rows) } | ConvertTo-Json -Depth 6 -Compress))
