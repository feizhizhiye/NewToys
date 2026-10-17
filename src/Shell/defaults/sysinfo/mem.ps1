# mem.ps1 —— 信息面板默认采集脚本：内存（NT-068）
# ⚠️ 本脚本的输出含中文（"12.3 GB"），是「编码链路」的验收样本：宿主已用 UTF-8 解码 stdout。
$ErrorActionPreference = 'Stop'

$rows = New-Object System.Collections.ArrayList

function AddRow([string]$key, $attrs) {
    [void]$rows.Add([pscustomobject]@{ key = $key; status = 'ok'; attrs = $attrs })
}

function Fmt-Gb([double]$bytes) {
    if ($bytes -le 0) { return '—' }
    return '{0} GB' -f [math]::Round($bytes / 1GB, 2)
}

try {
    $os = Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop
    $total = [double]$os.TotalVisibleMemorySize * 1KB
    $free = [double]$os.FreePhysicalMemory * 1KB
    $used = $total - $free
    $pct = if ($total -gt 0) { [math]::Round($used * 100 / $total, 0) } else { 0 }

    AddRow 'mem-total' ([ordered]@{ '属性' = '物理内存'; '值' = (Fmt-Gb $total); '说明' = 'Win32_OperatingSystem.TotalVisibleMemorySize' })
    AddRow 'mem-used'  ([ordered]@{ '属性' = '内存已用'; '值' = (Fmt-Gb $used); '说明' = ('占用 {0}%' -f $pct) })
    AddRow 'mem-free'  ([ordered]@{ '属性' = '可用内存'; '值' = (Fmt-Gb $free); '说明' = 'Win32_OperatingSystem.FreePhysicalMemory' })
} catch {
    AddRow 'mem-total' ([ordered]@{ '属性' = '物理内存'; '值' = '（取不到）'; '说明' = '需要 CIM / WMI' })
}

$proc = [System.Diagnostics.Process]::GetCurrentProcess()
AddRow 'mem-proc' ([ordered]@{ '属性' = '本脚本进程占用'; '值' = (Fmt-Gb $proc.WorkingSet64); '说明' = '工作集（跑本脚本的那个解释器进程）' })

[Console]::Out.Write(([pscustomobject]@{ rows = @($rows) } | ConvertTo-Json -Depth 6 -Compress))
