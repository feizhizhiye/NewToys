# runtime.ps1 —— 信息展示页默认脚本：运行时（NT-067）
# 显示的是「跑这个脚本的宿主」，不是 NewToys 自己 —— 用来核对解释器链路是否按预期工作。
$ErrorActionPreference = 'Stop'

$rows = New-Object System.Collections.ArrayList

function AddRow([string]$key, [string]$name, $value, [string]$note) {
    [void]$rows.Add([pscustomobject]@{ key = $key; status = 'ok'; values = @('运行时', $name, [string]$value, $note) })
}

AddRow 'rt-ps' 'PowerShell 版本' ($PSVersionTable.PSVersion.ToString()) '脚本当前所在的宿主'
AddRow 'rt-edition' '宿主版本类型' ($PSVersionTable.PSEdition) 'Desktop / Core'

try {
    $exe = [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
    AddRow 'rt-exe' '解释器路径' $exe '取的是当前进程的可执行文件'
} catch { }

AddRow 'rt-arch' '进程架构' ($env:PROCESSOR_ARCHITECTURE) 'PROCESSOR_ARCHITECTURE'
AddRow 'rt-encoding' '输出编码' ([Console]::OutputEncoding.WebName) '宿主已在启动时设成 utf-8'

$dir = $env:NEWTOYS_SYSINFO_DIR
if ([string]::IsNullOrWhiteSpace($dir)) { $dir = (Get-Location).Path }
AddRow 'rt-dir' '脚本目录' $dir '环境变量 NEWTOYS_SYSINFO_DIR（脚本定位同目录资源用）'

[Console]::Out.Write(([pscustomobject]@{ rows = @($rows) } | ConvertTo-Json -Depth 6 -Compress))
