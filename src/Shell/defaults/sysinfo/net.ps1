# net.ps1 —— 信息展示页默认脚本：网络（NT-067）
$ErrorActionPreference = 'Stop'

$rows = New-Object System.Collections.ArrayList

function AddRow([string]$key, [string]$name, $value, [string]$note) {
    [void]$rows.Add([pscustomobject]@{ key = $key; status = 'ok'; values = @('网络', $name, [string]$value, $note) })
}

$hostName = ''
try { $hostName = [System.Net.Dns]::GetHostName() } catch { $hostName = $env:COMPUTERNAME }
AddRow 'net-host' '主机名' $hostName 'Dns.GetHostName'

try {
    $ips = [System.Net.Dns]::GetHostAddresses($hostName) |
        Where-Object { -not $_.IsIPv6LinkLocal -and -not $_.IsIPv6Multicast } |
        ForEach-Object { $_.ToString() } |
        Select-Object -Unique

    $ipText = if ($ips) { $ips -join '、' } else { '（未取到）' }
    AddRow 'net-ip' '本机 IP' $ipText 'Dns.GetHostAddresses（IPv4 / IPv6）'
} catch {
    AddRow 'net-ip' '本机 IP' '（未取到）' 'Dns.GetHostAddresses'
}

# 默认网关（需要 NetTCPIP 模块；拿不到就跳过，不影响其它行）
try {
    $route = Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction Stop | Select-Object -First 1
    if ($route -and $route.NextHop) { AddRow 'net-gw' '默认网关' $route.NextHop 'Get-NetRoute 0.0.0.0/0' }
} catch { }

[Console]::Out.Write(([pscustomobject]@{ rows = @($rows) } | ConvertTo-Json -Depth 6 -Compress))
