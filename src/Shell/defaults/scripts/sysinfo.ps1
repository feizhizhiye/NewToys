# ─────────────────────────────────────────────────────────────
#  NewToys sample script (.ps1 -> run by pwsh, or Windows PowerShell 5.1)
#  Quick system overview: OS / CPU / RAM / free space on C:.
#
#  NOTE: keep this file ASCII-only. Windows PowerShell 5.1 decodes a
#  BOM-less file using the ANSI code page, so non-ASCII literals would
#  be garbled. (If you want Chinese output, save the file as
#  "UTF-8 with BOM".)
# ─────────────────────────────────────────────────────────────

$ErrorActionPreference = 'Stop'

$os  = Get-CimInstance Win32_OperatingSystem
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$sys = $os.TotalVisibleMemorySize
$free = $os.FreePhysicalMemory

Write-Output ("OS      : {0} (build {1})" -f $os.Caption, $os.BuildNumber)
Write-Output ("CPU     : {0}" -f $cpu.Name.Trim())
Write-Output ("CPU core: {0} cores / {1} threads" -f $cpu.NumberOfCores, $cpu.NumberOfLogicalProcessors)
Write-Output ("Memory  : {0:N1} GB total, {1:N1} GB free" -f ($sys / 1MB), ($free / 1MB))

Get-CimInstance Win32_LogicalDisk -Filter "DriveType=3" | ForEach-Object {
    Write-Output ("Disk {0} : {1:N1} GB free / {2:N1} GB total" -f $_.DeviceID,
        ($_.FreeSpace / 1GB), ($_.Size / 1GB))
}

Write-Output "[OK] done."
