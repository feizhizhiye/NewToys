# ============================================================
# NewToys - 导出模块源码（自动扫描 + 交互式选择，免手敲模块名）
# 用法：
#   pwsh ./scripts/export-module.ps1                 # 自动扫描 src/Module.*，弹菜单让你选（可多选）
#   pwsh ./scripts/export-module.ps1 -List           # 只列出可导出模块（含文件数），不导出
#   pwsh ./scripts/export-module.ps1 -ModuleName Settings
#   pwsh ./scripts/export-module.ps1 -All            # 一次导出全部模块
#   pwsh ./scripts/export-module.ps1 -ModuleName PcTools -OutPath x.md
# 说明：
#   不带参数时自动扫描 src/ 下的 Module.* 目录并弹出编号菜单：
#     输入 1 / 1,3 / 1-3 选若干；输入 A 选全部；输入 0 或直接回车 = 取消。
#   仍兼容原来的 -ModuleName 显式写法，方便自动化调用。
#   模块目录名匹配规则：src/Module.<Name>/
# ============================================================

param(
    [string]$ModuleName = "",
    [string]$OutPath = "",
    [switch]$All,
    [switch]$List
)

$ErrorActionPreference = 'Stop'
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir   = Split-Path -Parent $ScriptDir
$SrcDir    = Join-Path $RootDir "src"

# 代码块围栏：6 个反引号（避免与文件内普通 ``` 冲突）
$Fence = '`' * 6

# 排除规则
$ExcludePatterns = @(
    '*.dll','*.exe','*.pdb','*.cache','*.user','*.generated.cs',
    'Directory.Build.props','Directory.Packages.props'
)
$ExcludeDirs = @('bin','obj','publish')

# ---------------- 工具函数 ----------------

function Get-CodeLang {
    param([string]$FileName)
    $ext = [System.IO.Path]::GetExtension($FileName).TrimStart('.').ToLowerInvariant()
    switch ($ext) {
        'cs'     { 'csharp' }
        'axaml'  { 'xml' }
        'xaml'   { 'xml' }
        'csproj' { 'xml' }
        'json'   { 'json' }
        'md'     { 'markdown' }
        'txt'    { 'text' }
        default  { 'text' }
    }
}

# 取某模块目录下要导出的源码文件（排除 bin/obj/publish 与生成物、零字节文件）
function Get-ModuleSourceFiles {
    param([string]$ModuleDirPath)
    Get-ChildItem -Path $ModuleDirPath -Recurse -File |
        Where-Object {
            $dn = Split-Path $_.DirectoryName -Leaf
            if ($ExcludeDirs -contains $dn) { return $false }
            foreach ($pat in $ExcludePatterns) {
                if ($_.Name -like $pat) { return $false }
            }
            # 零字节文件跳过（空文件在 PS 7 Get-Content -Raw 返回 $null，会导致后续 .TrimEnd() 报 NRE）
            if ($_.Length -eq 0) {
                Write-Warning "跳过零字节文件：$($_.FullName.Substring($RootDir.Length + 1))"
                return $false
            }
            return $true
        } | Sort-Object FullName
}

# 安全读取文件文本：容错 Get-Content -Raw 在 PS7 对空文件返回 $null 的情况
function Read-FileTextSafe {
    param([string]$Path)
    try {
        $raw = Get-Content -Path $Path -Raw -Encoding UTF8
    } catch {
        Write-Warning "读取失败（按空串处理）：$Path  ->  $($_.Exception.Message)"
        return ""
    }
    if ($null -eq $raw) { return "" }
    return [string]$raw
}

# 自动扫描 src/ 下所有 Module.* 模块
function Get-AvailableModules {
    if (-not (Test-Path $SrcDir)) {
        Write-Error "找不到 src 目录：$SrcDir"
        exit 1
    }
    $dirs = Get-ChildItem -Path $SrcDir -Directory -Filter "Module.*" | Sort-Object Name
    foreach ($d in $dirs) {
        $name = $d.Name -replace '^Module\.',''
        [PSCustomObject]@{
            Name  = $name
            Dir   = $d.FullName
            Count = @(Get-ModuleSourceFiles -ModuleDirPath $d.FullName).Count
        }
    }
}

# 读一行输入（兼容交互终端与管道重定向；EOF 返回空串）
function Read-UserLine {
    param([string]$Prompt)
    Write-Host $Prompt -NoNewline
    $line = $null
    try { $line = [Console]::ReadLine() } catch { $line = $null }
    Write-Host ""
    if ($null -eq $line) { return "" }
    return $line
}

# 交互式选择要导出的模块（支持 1 / 1,3 / 1-3 / A / 0），输入非法会重新提示
function Select-ModulesInteractive {
    param([object[]]$Modules)
    while ($true) {
        Write-Host ""
        Write-Host "===== 可导出模块（自动扫描 src/Module.*）=====" -ForegroundColor Cyan
        for ($i = 0; $i -lt $Modules.Count; $i++) {
            $m = $Modules[$i]
            Write-Host ("  [{0}] {1,-16} {2,4} 个文件    src/Module.{3}/" -f ($i + 1), $m.Name, $m.Count, $m.Name)
        }
        Write-Host "  [A] 全部模块"
        Write-Host "  [0] 取消"
        Write-Host ""
        $raw = Read-UserLine "请输入序号（可多选：1 / 1,3 / 1-3；A=全部；回车=取消）> "
        $raw = "$raw".Trim()
        if ([string]::IsNullOrWhiteSpace($raw)) { Write-Host "已取消。" -ForegroundColor Yellow; return @() }
        if ($raw -imatch '^(a|all|\*)$') { return @($Modules) }
        if ($raw -eq '0') { Write-Host "已取消。" -ForegroundColor Yellow; return @() }

        $picked = New-Object System.Collections.Generic.List[object]
        $bad    = New-Object System.Collections.Generic.List[string]
        $ok     = $true
        foreach ($tok in ($raw -split '[,\s;，、]+' | Where-Object { $_ -ne '' })) {
            if ($tok -match '^(\d+)-(\d+)$') {
                $a = [int]$matches[1]; $b = [int]$matches[2]
                if ($a -gt $b) { $t = $a; $a = $b; $b = $t }
                for ($k = $a; $k -le $b; $k++) {
                    if ($k -ge 1 -and $k -le $Modules.Count) { [void]$picked.Add($Modules[$k - 1]) }
                    else { [void]$bad.Add("$k"); $ok = $false }
                }
            }
            elseif ($tok -match '^\d+$') {
                $n = [int]$tok
                if ($n -ge 1 -and $n -le $Modules.Count) { [void]$picked.Add($Modules[$n - 1]) }
                else { [void]$bad.Add($tok); $ok = $false }
            }
            else { [void]$bad.Add($tok); $ok = $false }
        }

        if (-not $ok -or $picked.Count -eq 0) {
            Write-Host ("输入无效：" + (($bad | Select-Object -Unique) -join ', ') + "，请重新输入。") -ForegroundColor Red
            continue
        }
        return @($picked | Sort-Object -Property Name -Unique)
    }
}

# 计算输出路径：默认 sources/module-<小写名>.md；多选时 -OutPath 视为输出目录
function Resolve-OutputPath {
    param([string]$Name, [string]$OutFile, [int]$MultiCount)
    if ([string]::IsNullOrWhiteSpace($OutFile)) {
        $SourcesDir = Join-Path $RootDir "sources"
        if (-not (Test-Path $SourcesDir)) { New-Item -ItemType Directory -Path $SourcesDir | Out-Null }
        return (Join-Path $SourcesDir ("module-{0}.md" -f $Name.ToLowerInvariant()))
    }
    if ($MultiCount -gt 1) {
        if (-not (Test-Path $OutFile)) { New-Item -ItemType Directory -Path $OutFile -Force | Out-Null }
        return (Join-Path $OutFile ("module-{0}.md" -f $Name.ToLowerInvariant()))
    }
    $d = Split-Path -Parent $OutFile
    if ($d -and -not (Test-Path $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
    return $OutFile
}

# 导出单个模块 → md（内容格式与原脚本完全一致，import-sources.ps1 可反向还原）
function Export-OneModule {
    param([string]$Name, [string]$Dir, [string]$OutFile)

    $files = @(Get-ModuleSourceFiles -ModuleDirPath $Dir)

    $Sb = [System.Text.StringBuilder]::new()
    [void]$Sb.AppendLine("# NewToys 模块源码 - $Name")
    [void]$Sb.AppendLine("")
    [void]$Sb.AppendLine("> **用途**：单模块源码快照。在已发送 framework.md 的基础上，改哪个模块就传哪个的 md。")
    [void]$Sb.AppendLine(">")
    [void]$Sb.AppendLine("> **包含范围**：src/Module.$Name/ 下所有源码")
    [void]$Sb.AppendLine("> **不包含**：框架层（Shared/Core/Shell）、其他模块")
    [void]$Sb.AppendLine("> **文件格式**：``FILE: <相对路径>`` 段标记，可配合 import-sources.ps1 反向还原")
    [void]$Sb.AppendLine("")
    [void]$Sb.AppendLine("---")
    [void]$Sb.AppendLine("")
    [void]$Sb.AppendLine("## 1. 元信息")
    [void]$Sb.AppendLine("")
    [void]$Sb.AppendLine("| 项 | 值 |")
    [void]$Sb.AppendLine("|---|---|")
    [void]$Sb.AppendLine("| 模块名 | $Name |")
    [void]$Sb.AppendLine("| 模块目录 | src/Module.$Name/ |")
    [void]$Sb.AppendLine("| 导出生成时间 | $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') |")
    [void]$Sb.AppendLine("| 仓库根 | $RootDir |")
    [void]$Sb.AppendLine("| 导出类型 | module |")
    [void]$Sb.AppendLine("| 规范版本 | v1.9 |")
    [void]$Sb.AppendLine("")
    [void]$Sb.AppendLine("---")
    [void]$Sb.AppendLine("")
    [void]$Sb.AppendLine("## 2. 模块源码文件")
    [void]$Sb.AppendLine("")

    $count = 0
    foreach ($file in $files) {
        $rel  = $file.FullName.Substring($SrcDir.Length + 1).Replace('\','/')
        $lang = Get-CodeLang -FileName $file.Name
        [void]$Sb.AppendLine("### ``FILE: $rel``")
        [void]$Sb.AppendLine("")
        [void]$Sb.AppendLine("$Fence$lang")
        $content = Read-FileTextSafe -Path $file.FullName
        [void]$Sb.AppendLine($content.TrimEnd())
        [void]$Sb.AppendLine("")
        [void]$Sb.AppendLine("$Fence")
        [void]$Sb.AppendLine("")
        $count++
    }

    [void]$Sb.AppendLine("---")
    [void]$Sb.AppendLine("")
    [void]$Sb.AppendLine("## 3. 导出统计")
    [void]$Sb.AppendLine("")
    [void]$Sb.AppendLine("| 项 | 值 |")
    [void]$Sb.AppendLine("|---|---|")
    [void]$Sb.AppendLine("| 文件数 | $count |")
    [void]$Sb.AppendLine("| 模块 | Module.$Name |")
    [void]$Sb.AppendLine("")

    $Sb.ToString() | Set-Content -Path $OutFile -Encoding UTF8 -NoNewline
    $sizeKB = [math]::Round((Get-Item $OutFile).Length / 1KB, 1)
    return [PSCustomObject]@{ Name = $Name; OutFile = $OutFile; Count = $count; SizeKB = $sizeKB }
}

# ---------------- 主流程 ----------------

$modules = @(Get-AvailableModules)
if ($modules.Count -eq 0) {
    Write-Error "src/ 下没有找到任何 Module.* 模块目录：$SrcDir"
    exit 1
}

if ($List) {
    Write-Host ""
    Write-Host "可导出模块（自动扫描 src/Module.*）：" -ForegroundColor Cyan
    $modules | ForEach-Object { Write-Host ("  - {0,-16} {1,4} 个文件" -f $_.Name, $_.Count) }
    Write-Host ""
    exit 0
}

$selected = @()
if (-not [string]::IsNullOrWhiteSpace($ModuleName)) {
    $hit = $modules | Where-Object { $_.Name -ieq $ModuleName.Trim() } | Select-Object -First 1
    if ($null -eq $hit) {
        $avail = ($modules | ForEach-Object { $_.Name }) -join ', '
        Write-Error "找不到模块：Module.$ModuleName（可用模块：$avail）"
        exit 1
    }
    $selected = @($hit)
}
elseif ($All) {
    $selected = @($modules)
}
else {
    $selected = @(Select-ModulesInteractive -Modules $modules)
}

if ($selected.Count -eq 0) {
    Write-Host "未选择任何模块，已退出。" -ForegroundColor Yellow
    exit 0
}

$results = @()
foreach ($m in $selected) {
    $outFile = Resolve-OutputPath -Name $m.Name -OutFile $OutPath -MultiCount $selected.Count
    $results += Export-OneModule -Name $m.Name -Dir $m.Dir -OutFile $outFile
}

Write-Host ""
Write-Host "===== 导出完成 =====" -ForegroundColor Green
foreach ($r in $results) {
    Write-Host ("  [OK] {0,-16} {1,4} 个文件  {2,7} KB  ->  {3}" -f $r.Name, $r.Count, $r.SizeKB, $r.OutFile)
}
Write-Host ""
Write-Host "提示：已发过 framework.md 的会话中，上传这些 md 就可以针对对应模块做修改。" -ForegroundColor Cyan
