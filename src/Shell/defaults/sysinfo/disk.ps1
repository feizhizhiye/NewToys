# disk.ps1 —— 信息面板默认采集脚本：磁盘（NT-068）
# 故意不用 CIM：DriveInfo 是 BCL，起得快且不依赖 WMI 服务。
$ErrorActionPreference = 'Stop'

$rows = New-Object System.Collections.ArrayList

function AddRow([string]$key, $attrs) {
    [void]$rows.Add([pscustomobject]@{ key = $key; status = 'ok'; attrs = $attrs })
}

function Fmt-Size([double]$bytes) {
    if ($bytes -le 0) { return '—' }
    if ($bytes -ge 1GB) { return '{0} GB' -f [math]::Round($bytes / 1GB, 2) }
    if ($bytes -ge 1MB) { return '{0} MB' -f [math]::Round($bytes / 1MB, 1) }
    return '{0} KB' -f [math]::Round($bytes / 1KB, 0)
}

$count = 0
foreach ($d in [System.IO.DriveInfo]::GetDrives()) {
    try {
        if (-not $d.IsReady) { continue }
        if (@('Fixed', 'Removable', 'Network') -notcontains $d.DriveType.ToString()) { continue }

        $total = [double]$d.TotalSize
        if ($total -le 0) { continue }

        $free = [double]$d.AvailableFreeSpace
        $pct = [int](($total - $free) * 100 / $total)

        $label = $d.Name
        try { if (-not [string]::IsNullOrWhiteSpace($d.VolumeLabel)) { $label = '{0}（{1}）' -f $d.Name, $d.VolumeLabel } } catch { }

        AddRow $d.Name ([ordered]@{
            '磁盘'   = $label
            '可用'   = (Fmt-Size $free)
            '总容量' = (Fmt-Size $total)
            '说明'   = ('已用 {0}% · 文件系统 {1} · 类型 {2}' -f $pct, $d.DriveFormat, $d.DriveType)
        })
        $count++
    } catch { }
}

if ($count -eq 0) {
    AddRow 'disk-none' ([ordered]@{ '磁盘' = '（没有可读的磁盘）'; '可用' = ''; '总容量' = ''; '说明' = 'DriveInfo.GetDrives' })
}

[Console]::Out.Write(([pscustomobject]@{ rows = @($rows) } | ConvertTo-Json -Depth 6 -Compress))
