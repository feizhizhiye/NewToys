# os.ps1 —— 信息展示页默认脚本：操作系统（NT-067）
# 契约：只往 stdout 输出 JSON（{ "rows": [...] }），⛔ 不写日志；调试信息走 stderr。
# 每个 values = @(分类, 项目, 值, 说明)，与 manifest 的 columns[].key 对齐。
$ErrorActionPreference = 'Stop'

$rows = New-Object System.Collections.ArrayList

function AddRow([string]$key, [string]$name, $value, [string]$note) {
    [void]$rows.Add([pscustomobject]@{ key = $key; status = 'ok'; values = @('操作系统', $name, [string]$value, $note) })
}

AddRow 'os-arch' '平台架构' ($env:PROCESSOR_ARCHITECTURE) '进程环境变量 PROCESSOR_ARCHITECTURE'
AddRow 'os-machine' '计算机名' ($env:COMPUTERNAME) 'COMPUTERNAME'
AddRow 'os-user' '当前用户' ($env:USERNAME) 'USERNAME'

try {
    $os = Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop
    AddRow 'os-version' '系统版本' ($os.Caption + ' ' + $os.Version) 'Win32_OperatingSystem'

    $up = (Get-Date) - $os.LastBootUpTime
    $upText = if ($up.TotalDays -ge 1) {
        '{0} 天 {1} 小时 {2} 分' -f [int]$up.TotalDays, $up.Hours, $up.Minutes
    } else {
        '{0} 小时 {1} 分 {2} 秒' -f $up.Hours, $up.Minutes, $up.Seconds
    }
    AddRow 'os-uptime' '系统已运行' $upText 'LastBootUpTime 到现在'
} catch {
    AddRow 'os-version' '系统版本' ([System.Environment]::OSVersion.VersionString) 'Environment.OSVersion'
    AddRow 'os-uptime' '系统已运行' '（取不到）' '需要 CIM / WMI'
}

# 开机启动项：读的是注册表的真实状态（不是配置）
$enabled = '未启用'
try {
    $run = Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'NewToys' -ErrorAction Stop
    if (-not [string]::IsNullOrWhiteSpace($run.NewToys)) { $enabled = '已启用' }
} catch { }
AddRow 'os-startup' '开机启动项' $enabled 'HKCU\Software\Microsoft\Windows\CurrentVersion\Run 下的 NewToys'

[Console]::Out.Write(([pscustomobject]@{ rows = @($rows) } | ConvertTo-Json -Depth 6 -Compress))
