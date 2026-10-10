<#
.SYNOPSIS
  导出 NewToys 框架层源码（Shared + Core + Shell + 根配置）到 sources/framework.md，
  供 AI 协作续作时上传使用。
.DESCRIPTION
  - 自动扫描 src/Shared、src/Core、src/Shell 下的 *.cs / *.axaml / *.axaml.cs / app.manifest
  - 同时导出根目录配置：NewToys.slnx、Directory.Build.props、Directory.Packages.props、global.json
  - 输出为单文件 Markdown，每段以 #### FILE: <相对路径> 开头、用 ``` 代码块包裹
  - 可配合 import-sources.ps1 反向还原到 src/
.PARAMETER OutPath
  输出 md 路径，默认 sources/framework.md
.PARAMETER List
  只列出将要导出的文件，不实际写入
.EXAMPLE
  pwsh ./scripts/export-framework.ps1
  pwsh ./scripts/export-framework.ps1 -OutPath D:\fw.md
#>
param(
    [string]$OutPath = "",
    [switch]$List = $false
)

$ErrorActionPreference = "Stop"

# 定位仓库根（脚本所在目录的上一级）
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..")
if (-not $OutPath) { $OutPath = Join-Path $repoRoot "sources\framework.md" }

# 要导出的目录与文件
$dirs = @(
    @{ Rel = "src\Shared";         Filter = @("*.cs","*.csproj") },
    @{ Rel = "src\Core";           Filter = @("*.cs","*.csproj") },
    @{ Rel = "src\Shell";          Filter = @("*.cs","*.axaml","*.axaml.cs","*.csproj","app.manifest","App.axaml") }
)
$rootFiles = @(
    "NewToys.slnx","Directory.Build.props","Directory.Packages.props","global.json"
)

$all = @()
foreach ($d in $dirs) {
    $abs = Join-Path $repoRoot $d.Rel
    if (-not (Test-Path $abs)) { Write-Warning "目录不存在，跳过：$abs"; continue }
    foreach ($pat in $d.Filter) {
        Get-ChildItem -Path $abs -Recurse -File -Filter $pat | ForEach-Object {
            $rel = $_.FullName.Substring($repoRoot.Length + 1).Replace("/","\")
            $all += [PSCustomObject]@{ Rel = $rel; Full = $_.FullName; Size = $_.Length }
        }
    }
}
foreach ($rf in $rootFiles) {
    $abs = Join-Path $repoRoot $rf
    if (Test-Path $abs) {
        $fi = Get-Item $abs
        $all += [PSCustomObject]@{ Rel = $rf; Full = $abs; Size = $fi.Length }
    } else {
        Write-Warning "根配置缺失：$abs"
    }
}
$all = $all | Sort-Object Rel

Write-Host "===== 框架层待导出文件（共 $($all.Count) 个）====="
$all | ForEach-Object { Write-Host ("  {0,-55} {1,8} bytes" -f $_.Rel, $_.Size) }

if ($List) { return }

$outDir = Split-Path -Parent $OutPath
if ($outDir -and -not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("# NewToys 框架源码（Framework Export）")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("> **用途**：AI 协作续作用的最小框架快照。新会话开始时上传本文件，AI 即可读取框架层全部代码。")
[void]$sb.AppendLine(">")
[void]$sb.AppendLine("> **包含范围**：Shared + Core + Shell + 根目录配置文件")
[void]$sb.AppendLine("> **不包含**：各业务模块（Module.*）、脚本（scripts/）、生成物（artifacts/）")
[void]$sb.AppendLine("> **文件格式**：FILE: <相对路径> 段标记，可配合 import-sources.ps1 反向还原")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("---")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("## 1. 元信息")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("| 项 | 值 |")
[void]$sb.AppendLine("|---|---|")
[void]$sb.AppendLine("| 导出生成时间 | $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') |")
[void]$sb.AppendLine("| 机器名 | $env:COMPUTERNAME |")
[void]$sb.AppendLine("| 仓库根 | $repoRoot |")
[void]$sb.AppendLine("| 导出类型 | framework |")
[void]$sb.AppendLine("| 文件总数 | $($all.Count) |")
[void]$sb.AppendLine("")

foreach ($f in $all) {
    $ext = [System.IO.Path]::GetExtension($f.Full).ToLower()
    $lang = switch ($ext) {
        ".cs"   { "csharp" }
        ".axaml"{ "xml" }
        ".xml"  { "xml" }
        ".json" { "json" }
        ".csproj" { "xml" }
        ".props"{ "xml" }
        ".slnx" { "xml" }
        default { "text" }
    }
    # 根配置用三级标题，src/ 下文件用四级标题（与历史导出格式保持一致）
    $heading = if ($f.Rel.StartsWith("src\")) { "####" } else { "###" }
    [void]$sb.AppendLine("$heading ``FILE: $($f.Rel.Replace('\','/'))``")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("``````$lang")
    $content = [System.IO.File]::ReadAllText($f.Full)
    if ($content -eq $null) { $content = "" }
    $content = $content.TrimEnd("`r","`n")
    [void]$sb.AppendLine($content)
    [void]$sb.AppendLine("``````")
    [void]$sb.AppendLine("")
}

[System.IO.File]::WriteAllText($OutPath, $sb.ToString(), (New-Object System.Text.UTF8Encoding $false))
Write-Host ""
Write-Host "✔ 已导出到 $OutPath" -ForegroundColor Green
