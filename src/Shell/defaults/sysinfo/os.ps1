# os.ps1 —— 信息面板默认采集脚本：操作系统（NT-068）
# 契约：只往 stdout 输出 JSON（{ "rows": [ { "key", "status", "attrs" } ] }），⛔ 不写日志；调试信息走 stderr。
# attrs 的属性名 = 列名（首次成功采集时自动生成列 schema，按出现顺序）。
$ErrorActionPreference = 'Stop'

$rows = New-Object System.Collections.ArrayList

function AddRow([string]$key, $attrs) {
    [void]$rows.Add([pscustomobject]@{ key = $key; status = 'ok'; attrs = $attrs })
}

AddRow 'os-arch'    ([ordered]@{ '属性' = '平台架构'; '值' = $env:PROCESSOR_ARCHITECTURE; '说明' = 'PROCESSOR_ARCHITECTURE' })
AddRow 'os-machine' ([ordered]@{ '属性' = '计算机名'; '值' = $env:COMPUTERNAME; '说明' = 'COMPUTERNAME' })
AddRow 'os-user'    ([ordered]@{ '属性' = '当前用户'; '值' = $env:USERNAME; '说明' = 'USERNAME' })

try {
    $os = Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop
    AddRow 'os-version' ([ordered]@{ '属性' = '系统版本'; '值' = ($os.Caption + ' ' + $os.Version); '说明' = 'Win32_OperatingSystem' })

    $up = (Get-Date) - $os.LastBootUpTime
    $upText = if ($up.TotalDays -ge 1) {
        '{0} 天 {1} 小时 {2} 分' -f [int]$up.TotalDays, $up.Hours, $up.Minutes
    } else {
        '{0} 小时 {1} 分 {2} 秒' -f $up.Hours, $up.Minutes, $up.Seconds
    }
    AddRow 'os-uptime' ([ordered]@{ '属性' = '系统已运行'; '值' = $upText; '说明' = 'LastBootUpTime 到现在' })
} catch {
    AddRow 'os-version' ([ordered]@{ '属性' = '系统版本'; '值' = ([System.Environment]::OSVersion.VersionString); '说明' = 'Environment.OSVersion' })
    AddRow 'os-uptime' ([ordered]@{ '属性' = '系统已运行'; '值' = '（取不到）'; '说明' = '需要 CIM / WMI' })
}

# 开机启动项：读的是注册表的真实状态（不是配置）
$enabled = '未启用'
try {
    $run = Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'NewToys' -ErrorAction Stop
    if (-not [string]::IsNullOrWhiteSpace($run.NewToys)) { $enabled = '已启用' }
} catch { }
AddRow 'os-startup' ([ordered]@{ '属性' = '开机启动项'; '值' = $enabled; '说明' = 'HKCU\Software\Microsoft\Windows\CurrentVersion\Run' })

[Console]::Out.Write(([pscustomobject]@{ rows = @($rows) } | ConvertTo-Json -Depth 6 -Compress))
