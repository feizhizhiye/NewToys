# net.ps1 —— 信息面板默认采集脚本：网络（NT-068）
# 契约：只往 stdout 输出 JSON（{ "rows": [ { "key", "status", "attrs" } ] }），⛔ 不写日志。
$ErrorActionPreference = 'Stop'

$rows = New-Object System.Collections.ArrayList

function AddRow([string]$key, $attrs) {
    [void]$rows.Add([pscustomobject]@{ key = $key; status = 'ok'; attrs = $attrs })
}

$hostName = ''
try { $hostName = [System.Net.Dns]::GetHostName() } catch { $hostName = $env:COMPUTERNAME }
AddRow 'net-host' ([ordered]@{ '属性' = '主机名'; '值' = $hostName; '说明' = 'Dns.GetHostName' })

try {
    $ips = [System.Net.Dns]::GetHostAddresses($hostName) |
        Where-Object { -not $_.IsIPv6LinkLocal -and -not $_.IsIPv6Multicast } |
        ForEach-Object { $_.ToString() } |
        Select-Object -Unique

    $ipText = if ($ips) { $ips -join '、' } else { '（未取到）' }
    AddRow 'net-ip' ([ordered]@{ '属性' = '本机 IP'; '值' = $ipText; '说明' = 'Dns.GetHostAddresses（IPv4 / IPv6）' })
} catch {
    AddRow 'net-ip' ([ordered]@{ '属性' = '本机 IP'; '值' = '（未取到）'; '说明' = 'Dns.GetHostAddresses' })
}

# 默认网关（需要 NetTCPIP 模块；拿不到就跳过，不影响其它行）
try {
    $route = Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction Stop | Select-Object -First 1
    if ($route -and $route.NextHop) {
        AddRow 'net-gw' ([ordered]@{ '属性' = '默认网关'; '值' = $route.NextHop; '说明' = 'Get-NetRoute 0.0.0.0/0' })
    }
} catch { }

[Console]::Out.Write(([pscustomobject]@{ rows = @($rows) } | ConvertTo-Json -Depth 6 -Compress))
