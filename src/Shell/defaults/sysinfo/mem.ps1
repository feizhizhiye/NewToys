# mem.ps1 —— 信息展示页默认脚本：内存（NT-067）
# ⚠️ 本脚本的输出含中文（"内存已用 12.3 GB"），是「编码链路」的验收样本：
#    宿主已用 UTF-8 解码 stdout，脚本自己也把 OutputEncoding 设成 UTF-8（见 manifest 的 launcher）。
$ErrorActionPreference = 'Stop'

$rows = New-Object System.Collections.ArrayList

function AddRow([string]$key, [string]$name, $value, [string]$note) {
    [void]$rows.Add([pscustomobject]@{ key = $key; status = 'ok'; values = @('内存', $name, [string]$value, $note) })
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

    AddRow 'mem-total' '物理内存' (Fmt-Gb $total) 'Win32_OperatingSystem.TotalVisibleMemorySize'
    AddRow 'mem-used' '内存已用' ('内存已用 {0} GB' -f [math]::Round($used / 1GB, 1)) ('占用 {0}%' -f $pct)
    AddRow 'mem-free' '可用内存' (Fmt-Gb $free) 'Win32_OperatingSystem.FreePhysicalMemory'
} catch {
    AddRow 'mem-total' '物理内存' '（取不到）' '需要 CIM / WMI'
}

$proc = [System.Diagnostics.Process]::GetCurrentProcess()
AddRow 'mem-proc' '本脚本进程占用' (Fmt-Gb $proc.WorkingSet64) '工作集（跑本脚本的那个解释器进程）'

[Console]::Out.Write(([pscustomobject]@{ rows = @($rows) } | ConvertTo-Json -Depth 6 -Compress))
