# NewToys 示例采集器 · 输出格式演示
#
# 采集器的唯一契约：往 stdout 打一段 JSON，形如
#   { "rows": [ { "key": "host", "status": "ok", "attrs": { "属性名": "值" } } ] }
#
#   key    = 行标识。同一行每次采集都用同一个 key，界面才能就地更新而不是整表重建。
#   status = ok / warn / error（信息面板节头状态点的颜色）。
#   attrs  = 属性名 → 值。属性名就是信息面板里的列名；某采集器首次成功采集时按出现顺序自动生成列，
#            之后新出现的属性追加到末尾（已有列的顺序 / 宽度不会被改）。
#
# 规则：⛔ 不要把日志 / 进度打到 stdout，只打这一段 JSON；写日志请用 Write-Host。
# 用法：集合页把本条目勾进任意 kind=信息 的集合，信息面板就会出现一节。

$ErrorActionPreference = 'Stop'

try
{
    $os = Get-CimInstance -ClassName Win32_OperatingSystem
    $cs = Get-CimInstance -ClassName Win32_ComputerSystem

    $totalGB = [math]::Round($cs.TotalPhysicalMemory / 1GB, 2)
    $freeGB = [math]::Round($os.FreePhysicalMemory / 1MB, 2)
    $usedPct = if ($totalGB -gt 0) { [math]::Round(($totalGB - $freeGB) / $totalGB * 100, 1) } else { 0 }

    $status = if ($usedPct -ge 90) { 'warn' } else { 'ok' }

    $payload = [ordered]@{
        rows = @(
        [ordered]@{
            key    = 'host'
            status = $status
            attrs  = [ordered]@{
                '项目'     = '本机'
                '计算机名' = $env:COMPUTERNAME
                '域/工作组' = $cs.Domain
                '内存总量' = "$totalGB GB"
                '内存占用' = "$usedPct %"
                '采集时间' = (Get-Date -Format 'HH:mm:ss')
            }
        }
        )
    }

    [Console]::Out.Write(($payload | ConvertTo-Json -Depth 6 -Compress))
}
catch
{
    # 失败也要给一段合法 JSON：界面会把这节标成 error，并保留上一次的数据。
    $payload = [ordered]@{
        rows = @(
        [ordered]@{
            key    = 'host'
            status = 'error'
            attrs  = [ordered]@{ '项目' = '本机'; '错误' = $_.Exception.Message }
        }
        )
    }
    [Console]::Out.Write(($payload | ConvertTo-Json -Depth 6 -Compress))
}
