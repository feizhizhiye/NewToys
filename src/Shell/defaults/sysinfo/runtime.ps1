# runtime.ps1 —— 信息面板默认采集脚本：运行时（NT-068）
# 显示的是「跑这个脚本的宿主」，不是 NewToys 自己 —— 用来核对解释器链路是否按预期工作。
$ErrorActionPreference = 'Stop'

$rows = New-Object System.Collections.ArrayList

function AddRow([string]$key, $attrs) {
    [void]$rows.Add([pscustomobject]@{ key = $key; status = 'ok'; attrs = $attrs })
}

AddRow 'rt-ps'      ([ordered]@{ '属性' = 'PS 版本'; '值' = ($PSVersionTable.PSVersion.ToString()); '说明' = '脚本当前所在的宿主' })
AddRow 'rt-edition' ([ordered]@{ '属性' = '宿主版本类型'; '值' = ($PSVersionTable.PSEdition); '说明' = 'Desktop / Core' })

try {
    $exe = [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
    AddRow 'rt-exe' ([ordered]@{ '属性' = '解释器路径'; '值' = $exe; '说明' = '取的是当前进程的可执行文件' })
} catch { }

AddRow 'rt-arch'     ([ordered]@{ '属性' = '进程架构'; '值' = $env:PROCESSOR_ARCHITECTURE; '说明' = 'PROCESSOR_ARCHITECTURE' })
AddRow 'rt-encoding' ([ordered]@{ '属性' = '输出编码'; '值' = ([Console]::OutputEncoding.WebName); '说明' = '宿主已在启动时设成 utf-8' })

$dir = $env:NEWTOYS_SYSINFO_DIR
if ([string]::IsNullOrWhiteSpace($dir)) { $dir = (Get-Location).Path }
AddRow 'rt-dir' ([ordered]@{ '属性' = '脚本目录'; '值' = $dir; '说明' = '环境变量 NEWTOYS_SYSINFO_DIR' })

[Console]::Out.Write(([pscustomobject]@{ rows = @($rows) } | ConvertTo-Json -Depth 6 -Compress))
