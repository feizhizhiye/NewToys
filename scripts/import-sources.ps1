<#
.SYNOPSIS
  从 AI 返回的源码快照 md（framework.md / module-*.md）反向还原到 src/ 目录。
.DESCRIPTION
  - 支持 FILE: <相对路径> 段标记（### / #### 两种标题级别）
  - 还原前自动把原文件备份到 src/.bak-<时间戳>/ 下
  - 支持 -DryRun 预览
  - 自动把 CRLF/LF 统一为 LF（避免跨平台换行差异）
  - 路径穿越校验：禁止 FILE 路径跳出仓库根
.PARAMETER File
  md 文件路径；不传则自动扫描 sources/*.md 与仓库根 *.md，弹菜单选择
.PARAMETER DryRun
  只预览不写入
.PARAMETER NoBackup
  不备份原文件（危险，仅在全新仓库使用）
.EXAMPLE
  pwsh ./scripts/import-sources.ps1
  pwsh ./scripts/import-sources.ps1 -File sources\module-settings.md -DryRun
#>
param(
    [string]$File = "",
    [switch]$DryRun = $false,
    [switch]$NoBackup = $false
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..")

function Select-SourceFile {
    $candidates = @()
    foreach ($p in @(
        (Join-Path $repoRoot "sources\*.md"),
        (Join-Path $repoRoot "*.md")
    )) {
        $candidates += Get-Item $p -ErrorAction SilentlyContinue
    }
    $candidates = $candidates | Sort-Object LastWriteTime -Descending | Where-Object {
        # 只挑包含 FILE: 标记的 md
        (Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue) -match 'FILE:\s'
    } | Select-Object -Unique
    if (-not $candidates) {
        Write-Host "未找到任何源码快照 md（sources/*.md 或 *.md）。" -ForegroundColor Yellow
        Write-Host "请用 -File <路径> 显式指定。"
        return $null
    }
    Write-Host ""
    Write-Host "===== 可用源码快照（共 $($candidates.Count) 个）====="
    for ($i = 0; $i -lt $candidates.Count; $i++) {
        $c = $candidates[$i]
        $sizeKb = [math]::Round($c.Length / 1KB, 1)
        $rel = $c.FullName.Substring($repoRoot.Length + 1)
        Write-Host ("  [{0,2}]  {1,-50} {2,6} KB  {3}" -f ($i+1), $rel, $sizeKb, $c.LastWriteTime.ToString("MM-dd HH:mm"))
    }
    Write-Host "  [ M]  手动输入路径"
    Write-Host "  [ 0]  取消"
    Write-Host ""
    while ($true) {
        $ans = Read-Host "请输入序号"
        if (-not $ans -or $ans -eq "0") { return $null }
        if ($ans -eq "M") {
            $p = Read-Host "请输入 md 完整路径"
            if (Test-Path $p) { return (Resolve-Path $p).Path }
            Write-Host "文件不存在：$p" -ForegroundColor Red; continue
        }
        $idx = 0
        if ([int]::TryParse($ans, [ref]$idx) -and $idx -ge 1 -and $idx -le $candidates.Count) {
            return $candidates[$idx-1].FullName
        }
        Write-Host "输入无效：$ans，请重新输入" -ForegroundColor Yellow
    }
}

if (-not $File) {
    $File = Select-SourceFile
    if (-not $File) { Write-Host "已取消。"; return }
}
if (-not (Test-Path $File)) { Write-Error "文件不存在：$File"; return }
$File = Resolve-Path $File

Write-Host ""
Write-Host "读取快照：$File" -ForegroundColor Cyan
$text = [System.IO.File]::ReadAllText($File)

# 解析 FILE: 段
$matches = [regex]::Matches($text, '(?m)^#{3,4}\s*`FILE:\s*(.+?)`\s*$')
if ($matches.Count -eq 0) { Write-Error "未找到任何 FILE: 段标记，文件不是有效的源码快照。"; return }

$files = @()
for ($i = 0; $i -lt $matches.Count; $i++) {
    $m = $matches[$i]
    $rel = $m.Groups[1].Value.Trim().Replace("/","\")
    $lineStart = $m.Index + $m.Length
    # 找下一个 ``` 代码块开始
    $nextLineIdx = $text.IndexOf("`n", $lineStart)
    if ($nextLineIdx -lt 0) { $nextLineIdx = $text.Length }
    # 从 lineStart 往后找 ``` 开头的行
    $fenceStart = $text.IndexOf("``````", $lineStart)
    if ($fenceStart -lt 0) { continue }
    # 跳过 ``` 这一行到下一个换行
    $afterFence = $text.IndexOf("`n", $fenceStart)
    if ($afterFence -lt 0) { $afterFence = $text.Length } else { $afterFence += 1 }
    $fenceEnd = $text.IndexOf("``````", $afterFence)
    if ($fenceEnd -lt 0) { $fenceEnd = $text.Length }
    $body = $text.Substring($afterFence, $fenceEnd - $afterFence)
    # 去掉末尾 \r\n
    $body = $body.TrimEnd("`r","`n")
    # 统一 LF
    $body = $body -replace "`r`n","`n"
    $files += [PSCustomObject]@{ Rel = $rel; Content = $body }
}

Write-Host "共解析到 $($files.Count) 个文件。" -ForegroundColor Cyan
if ($DryRun) { Write-Host "[DryRun] 仅预览，不会写入。" -ForegroundColor Yellow }

# 备份目录
$backupDir = $null
if (-not $NoBackup -and -not $DryRun) {
    $backupDir = Join-Path $repoRoot ("src\.bak-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
}

$ok = 0; $skip = 0; $fail = 0
foreach ($f in $files) {
    # 路径穿越校验
    $target = Join-Path $repoRoot $f.Rel
    try { $target = [System.IO.Path]::GetFullPath($target) } catch { Write-Host "  ✗ 路径非法：$($f.Rel)"; $fail++; continue }
    if (-not $target.StartsWith($repoRoot.PathTrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        Write-Host "  ✗ 路径越界（已拦截）：$($f.Rel)" -ForegroundColor Red; $fail++; continue
    }
    $parent = Split-Path -Parent $target
    if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }

    if ((Test-Path $target) -and $backupDir) {
        $bakTarget = Join-Path $backupDir $f.Rel
        $bakParent = Split-Path -Parent $bakTarget
        if (-not (Test-Path $bakParent)) { New-Item -ItemType Directory -Path $bakParent -Force | Out-Null }
        Copy-Item $target $bakTarget -Force
    }

    if ($DryRun) {
        $exist = if (Test-Path $target) { "[覆盖]" } else { "[新建]" }
        Write-Host ("  {0} {1}" -f $exist, $f.Rel)
        $ok++
    } else {
        try {
            [System.IO.File]::WriteAllText($target, $f.Content + "`n", (New-Object System.Text.UTF8Encoding $false))
            Write-Host ("  ✔ {0}" -f $f.Rel) -ForegroundColor Green
            $ok++
        } catch {
            Write-Host ("  ✗ {0}: {1}" -f $f.Rel, $_.Exception.Message) -ForegroundColor Red
            $fail++
        }
    }
}

Write-Host ""
Write-Host ("完成：成功 {0} / 失败 {1}" -f $ok, $fail) -ForegroundColor $(if ($fail -gt 0) { "Red" } else { "Green" })
if ($backupDir) { Write-Host "原文件已备份到：$backupDir" -ForegroundColor DarkGray }
