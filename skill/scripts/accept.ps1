#Requires -Version 7.0
<#
════════════════════════════════════════════════════════════════════
  NewToys 一键验收（accept）
────────────────────────────────────────────────────────────────────
  一条命令跑完「静态检查 → 构建 → 产物核对 →(可选) 冒烟 → 铁律检查」，
  最后给出 **通过 / 不通过** 结论 + 退出码。

    pwsh skill/scripts/accept.ps1                  # 默认 Release：静态 + 构建 + 产物核对
    pwsh skill/scripts/accept.ps1 -SkipBuild       # 只跑静态检查（秒级，改完文档用）
    pwsh skill/scripts/accept.ps1 -Configuration Debug
    pwsh skill/scripts/accept.ps1 -Run             # 追加启动冒烟（默认 8s 内不崩）
    pwsh skill/scripts/accept.ps1 -Run -SmokeSeconds 15
    pwsh skill/scripts/accept.ps1 -Report          # 额外写 artifacts/accept-report.md
    pwsh skill/scripts/accept.ps1 -All             # WARN 明细全展开（透传给支持的子脚本）
    pwsh skill/scripts/accept.ps1 -SkipStatic      # 只构建 + 产物核对（不跑 Lint）

  退出码：0 = 全通过；1 = 存在 FAIL（脚本自身报错亦为 1）。
  ⛔ 本脚本只读 src/ + 只做构建，不修改 src/ 下任何源文件。
  ⛔ 构建用 dotnet 直调（与 scripts/build.ps1 同 slnx / 同配置）——避开
     build.ps1 的 trap → Read-Host，非交互运行不会被挂住。
  ⛔ 本脚本不替代人工回归；它证明「编译干净 + 架构不越界 + 起得来」，
     不证明「功能符合验收标准」。验收标准仍逐条对任务卡。
════════════════════════════════════════════════════════════════════
#>
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    # 只跑静态检查，跳过构建与产物核对
    [switch]$SkipStatic,
    [switch]$SkipBuild,
    # 追加启动冒烟：起 exe，N 秒内不退出即视为通过，然后杀掉
    [switch]$Run,
    [int]$SmokeSeconds = 8,
    # 写 artifacts/accept-report.md
    [switch]$Report,
    # 透传 -All 给支持的子脚本（WARN 明细全展开）
    [switch]$All
)

$ErrorActionPreference = 'Continue'
$ProgressPreference    = 'SilentlyContinue'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$src      = Join-Path $repoRoot 'src'
$pwshExe  = (Get-Process -Id $PID -ErrorAction SilentlyContinue).Path
if (-not $pwshExe) { $pwshExe = 'pwsh' }

Write-Host "`n════════ NewToys 一键验收 ════════" -ForegroundColor Cyan
Write-Host "仓库根 : $repoRoot" -ForegroundColor DarkGray
Write-Host "配置   : $Configuration" -ForegroundColor DarkGray
if (-not (Test-Path $src)) {
    Write-Host "[FAIL] 找不到 src/ —— 请从仓库内运行本脚本" -ForegroundColor Red
    exit 1
}

# ---------- 结果收集 ----------
$script:rows = New-Object System.Collections.ArrayList
function Add-Row([string]$Area, [string]$Name, [string]$Status, [string]$Detail, [string]$Raw = '') {
    [void]$script:rows.Add([pscustomobject]@{
        Area = $Area; Name = $Name; Status = $Status; Detail = $Detail; Raw = $Raw
    })
}

# ═════════ 1) 静态检查 ═════════
# Fail 正则命中 ⇒ FAIL（阻断）；Warn 正则命中 ⇒ WARN（不阻断）。
$staticChecks = @(
    [pscustomobject]@{
        Name = 'AXAML 合法性 / 配对 / 主题键'
        File = 'skill\scripts\check.ps1'
        Fail = @('XML 不合法 \.axaml\s*:\s*([1-9]\d*)', '缺 \.axaml\.cs 配对\s*:\s*([1-9]\d*)')
        Warn = @('白名单外的 \*Brush 引用')
        SupportsAll = $false
    }
    [pscustomobject]@{
        Name = 'XAML 属性名（Avalonia ≠ WPF）'
        File = 'skill\scripts\check-axaml-props.ps1'
        Fail = @('疑似 WPF 专有 / 拼错\s*:\s*([1-9]\d*)')
        Warn = @()
        SupportsAll = $false
    }
    [pscustomobject]@{
        Name = '技术栈硬伤（G-01 ~ G-14）'
        File = 'skill\scripts\check-techstack.ps1'
        Fail = @('\[ERROR\]\s*([1-9]\d*) 条')
        Warn = @('\[WARN\]\s*([1-9]\d*) 条')
        SupportsAll = $true
    }
    [pscustomobject]@{
        Name = '详情卡绑定路径'
        File = 'skill\scripts\check-detail-paths.ps1'
        Fail = @()
        Warn = @('\[WARN\]\s*([1-9]\d*) 条绑定路径')
        SupportsAll = $true
    }
)

if (-not $SkipStatic) {
    Write-Host "`n──── 1/5 静态检查 ────" -ForegroundColor Cyan
    foreach ($c in $staticChecks) {
        $full = Join-Path $repoRoot $c.File
        if (-not (Test-Path $full)) {
            Add-Row '静态检查' $c.Name 'SKIP' '脚本不存在'
            Write-Host ("  [SKIP] {0} —— {1}" -f $c.Name, $c.File) -ForegroundColor DarkYellow
            continue
        }
        # 子进程内强制 PlainText，保证 Write-Host 的内容能被捕获成纯文本
        $inner = "`$PSStyle.OutputRendering = 'PlainText'; & '$full'"
        if ($c.SupportsAll -and $All) { $inner += ' -All' }
        $raw = (& $pwshExe -NoProfile -NonInteractive -Command $inner 2>&1 | Out-String)

        $f = 0; foreach ($re in $c.Fail) { $f += ([regex]::Matches($raw, $re)).Count }
        $w = 0; foreach ($re in $c.Warn) { $w += ([regex]::Matches($raw, $re)).Count }
        $status = if ($f -gt 0) { 'FAIL' } elseif ($w -gt 0) { 'WARN' } else { 'PASS' }
        $detail = "{0} 阻断 / {1} 告警" -f $f, $w
        Add-Row '静态检查' $c.Name $status $detail $raw

        $tag = switch ($status) { 'PASS' { '[PASS] ' } 'WARN' { '[WARN] ' } default { '[FAIL] ' } }
        $color = switch ($status) { 'PASS' { 'Green' } 'WARN' { 'Yellow' } default { 'Red' } }
        Write-Host ("  {0}{1} —— {2}" -f $tag, $c.Name, $detail) -ForegroundColor $color
        if ($status -ne 'PASS') {
            # 打印带 [ERROR]/[WARN] 的明细行，帮定位
            $raw -split "\r?\n" | Where-Object { $_ -match '\[(ERROR|WARN|XML失败|缺配对)\]' } |
                Select-Object -First 12 | ForEach-Object { Write-Host "        $($_.Trim())" -ForegroundColor DarkGray }
        }
    }
}
else {
    Add-Row '静态检查' '全部' 'SKIP' '-SkipStatic'
}

# ═════════ 2) 构建 ═════════
$outDir = if ($Configuration -eq 'Release') {
    Join-Path $repoRoot 'artifacts\release'
} else {
    Join-Path $repoRoot 'artifacts\bin\Shell\Debug\net10.0'
}
$exe = Join-Path $outDir 'NewToys.exe'

if (-not $SkipBuild) {
    Write-Host "`n──── 2/5 构建（$Configuration）────" -ForegroundColor Cyan
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Add-Row '构建' 'dotnet build' 'FAIL' '找不到 dotnet'
        Write-Host '  [FAIL] 找不到 dotnet —— 请确认 SDK 已安装且在 PATH' -ForegroundColor Red
    }
    else {
        $sln = Join-Path $src 'NewToys.slnx'
        $t0 = Get-Date
        $buildRaw = (& dotnet build $sln -c $Configuration --nologo -v q 2>&1 | Out-String)
        $ok = ($LASTEXITCODE -eq 0)
        $sec = [Math]::Round(((Get-Date) - $t0).TotalSeconds, 1)
        # 编译错误计数：按诊断号统计最稳（中英文摘要行格式会随 locale 变）
        $errCount  = ([regex]::Matches($buildRaw, '(?i)\berror\s+(?:CS|AVLN|MSB|NETSDK|MVVMTK|NU)\d{3,5}')).Count
        $warnCount = ([regex]::Matches($buildRaw, '(?i)\bwarning\s+(?:CS|AVLN|MSB|NETSDK|MVVMTK|NU)\d{3,5}')).Count
        $detail = if ($ok) { "成功（{0} 错误 / {1} 警告，{2}s）" -f $errCount, $warnCount, $sec }
                  else { "失败（{0} 错误）" -f $errCount }
        Add-Row '构建' "dotnet build -c $Configuration" $(if ($ok) { 'PASS' } else { 'FAIL' }) $detail $buildRaw
        $tag  = if ($ok) { '[PASS] ' } else { '[FAIL] ' }
        $tcol = if ($ok) { 'Green' } else { 'Red' }
        Write-Host ("  {0}{1}" -f $tag, $detail) -ForegroundColor $tcol
        if (-not $ok) {
            Write-Host '  ── 编译错误（前 15 行）──' -ForegroundColor DarkGray
            $buildRaw -split "\r?\n" | Where-Object { $_ -match '(?i)\berror\b' } |
                Select-Object -First 15 | ForEach-Object { Write-Host "        $($_.Trim())" -ForegroundColor DarkGray }
            Write-Host '  提示：build.ps1 与本次构建同参数，可直接复现。' -ForegroundColor DarkGray
        }
    }
}
else {
    Add-Row '构建' 'dotnet build' 'SKIP' '-SkipBuild'
}

# ═════════ 3) 产物核对 ═════════
if (-not $SkipBuild) {
    Write-Host "`n──── 3/5 产物核对 ────" -ForegroundColor Cyan
    $msgs = New-Object System.Collections.ArrayList
    if (-not (Test-Path $exe)) { [void]$msgs.Add("缺 $exe") }

    if ($Configuration -eq 'Release') {
        $modulesDir = Join-Path $outDir 'Modules'
        $dlls     = @(Get-ChildItem $modulesDir -Filter 'Module.*.dll' -File -ErrorAction SilentlyContinue)
        # NT-062：Module.Settings 已撤销 ⇒ 所有 Module.* 都进 Modules\。
        $expected = @(Get-ChildItem $src -Directory -Filter 'Module.*').Count
        if ($dlls.Count -ne $expected) {
            [void]$msgs.Add("模块 DLL $($dlls.Count) 个 ≠ 期望 $expected 个（从解决方案构建？）")
        }
        $detail = "NewToys.exe + $($dlls.Count)/$expected 模块 DLL"
    }
    else {
        $detail = "NewToys.exe（Debug 输出：$outDir）"
    }

    if ($msgs.Count -eq 0) {
        Add-Row '产物' $Configuration 'PASS' $detail
        Write-Host "  [PASS] $detail" -ForegroundColor Green
    }
    else {
        Add-Row '产物' $Configuration 'FAIL' ($msgs -join '；')
        Write-Host ('  [FAIL] ' + ($msgs -join '；')) -ForegroundColor Red
    }
}
else {
    Add-Row '产物' $Configuration 'SKIP' '-SkipBuild'
}

# ═════════ 4) 铁律 1：改动是否只在 src/（skill/ 经授权可改）═════════
Write-Host "`n──── 4/5 铁律 1（改动范围）────" -ForegroundColor Cyan
$changed = @()
if (Get-Command git -ErrorAction SilentlyContinue) {
    $porcelain = @(& git -C $repoRoot status --porcelain -uall 2>$null)
    foreach ($line in $porcelain) {
        if ($line -match '^\s*\S{1,2}\s+(.+?)\s*$') {
            $p = $Matches[1].Trim().Trim('"')
            if ($p -match ' -> ') { $p = ($p -split ' -> ')[-1].Trim().Trim('"') }
            $changed += $p
        }
    }
    $outside = @($changed | Where-Object { $_ -notmatch '^src/' -and $_ -notmatch '^skill/' })
    if ($outside.Count -gt 0) {
        Add-Row '铁律1' 'src/ 之外的改动' 'WARN' ("$($outside.Count) 处越界") ($outside -join "`n")
        Write-Host "  [WARN] src/ 之外有 $($outside.Count) 处改动（scripts/ 经授权可改，其余应确认）：" -ForegroundColor Yellow
        $outside | Select-Object -First 10 | ForEach-Object { Write-Host "        $_" -ForegroundColor DarkGray }
    }
    else {
        Add-Row '铁律1' 'src/ 之外的改动' 'PASS' "工作区 $($changed.Count) 个文件，均在 src/ 或 skill/"
        Write-Host "  [PASS] 工作区 $($changed.Count) 个改动，均在 src/ 或 skill/" -ForegroundColor Green
    }
}
else {
    Add-Row '铁律1' 'src/ 之外的改动' 'SKIP' '找不到 git'
}

# ═════════ 5) 冒烟 ═════════
if ($Run) {
    Write-Host "`n──── 5/5 冒烟（$SmokeSeconds s）────" -ForegroundColor Cyan
    if (-not (Test-Path $exe)) {
        Add-Row '冒烟' '启动' 'FAIL' '可执行文件不存在（先构建）'
        Write-Host '  [FAIL] 可执行文件不存在' -ForegroundColor Red
    }
    else {
        # ⚠️ 变量名**不能**叫 $home：PowerShell 的 $HOME 是只读自动变量，且变量名不分大小写
        #    ⇒ 赋值会报「无法覆盖变量 HOME」，$env:NEWTOYS_HOME 就变成用户主目录，把配置建到用户主目录下。
        $smokeHome = Join-Path $repoRoot 'artifacts\env\accept'
        New-Item -ItemType Directory -Path $smokeHome -Force | Out-Null
        $env:NEWTOYS_HOME = $smokeHome        # 隔离用户配置，避免验收污染真实根
        try {
            $p = Start-Process -FilePath $exe -WorkingDirectory $outDir -PassThru
            Start-Sleep -Seconds $SmokeSeconds
            $p.Refresh()
            if ($p.HasExited) {
                Add-Row '冒烟' '启动' 'FAIL' "启动 $SmokeSeconds s 内退出（ExitCode=$($p.ExitCode)）"
                Write-Host "  [FAIL] 进程 $SmokeSeconds s 内退出，ExitCode=$($p.ExitCode) —— 看日志/事件查看器" -ForegroundColor Red
            }
            else {
                [void]$p.CloseMainWindow()
                Start-Sleep -Milliseconds 800
                $p.Refresh(); if (-not $p.HasExited) { $p.Kill() }
                Add-Row '冒烟' '启动' 'PASS' "$SmokeSeconds s 内未退出（NEWTOYS_HOME=$smokeHome）"
                Write-Host "  [PASS] $SmokeSeconds s 内未退出（配置已隔离到 artifacts\env\accept）" -ForegroundColor Green
            }
        }
        finally {
            Remove-Item Env:NEWTOYS_HOME -ErrorAction SilentlyContinue
        }
    }
}
else {
    Add-Row '冒烟' '启动' 'SKIP' '未加 -Run'
}

# ═════════ 汇总 ═════════
$fails = @($script:rows | Where-Object { $_.Status -eq 'FAIL' })
$warns = @($script:rows | Where-Object { $_.Status -eq 'WARN' })
$skips = @($script:rows | Where-Object { $_.Status -eq 'SKIP' })
$pass  = @($script:rows | Where-Object { $_.Status -eq 'PASS' })

Write-Host "`n════════ 验收结果 ════════" -ForegroundColor Cyan
foreach ($area in ($script:rows | Select-Object -ExpandProperty Area -Unique)) {
    foreach ($r in ($script:rows | Where-Object { $_.Area -eq $area })) {
        $mark = switch ($r.Status) {
            'PASS'  { '[PASS]' } 'WARN' { '[WARN]' } 'FAIL' { '[FAIL]' } default { '[SKIP]' }
        }
        $color = switch ($r.Status) {
            'PASS'  { 'Green' }  'WARN' { 'Yellow' } 'FAIL' { 'Red' }   default { 'DarkGray' }
        }
        Write-Host ("  {0} {1,-10} {2} —— {3}" -f $mark, $area, $r.Name, $r.Detail) -ForegroundColor $color
    }
}

$verdict = if ($fails.Count -eq 0) { '✅ 通过' } else { '❌ 不通过' }
$totalColor = if ($fails.Count -eq 0) { 'Green' } else { 'Red' }
Write-Host "`n  $verdict  （PASS $($pass.Count) / WARN $($warns.Count) / FAIL $($fails.Count) / SKIP $($skips.Count)）" -ForegroundColor $totalColor
if ($fails.Count -gt 0) {
    Write-Host '  未通过项：' -ForegroundColor Red
    $fails | ForEach-Object { Write-Host "    - [$($_.Area)] $($_.Name)：$($_.Detail)" -ForegroundColor Red }
}
if ($warns.Count -gt 0) {
    Write-Host '  告警项（不阻断，建议确认）：' -ForegroundColor Yellow
    $warns | ForEach-Object { Write-Host "    - [$($_.Area)] $($_.Name)：$($_.Detail)" -ForegroundColor Yellow }
}
Write-Host "`n  注：本脚本证明「编译干净 + 架构不越界 + 起得来」，不替代逐条人工回归。" -ForegroundColor DarkGray

# ═════════ 报告（可选）═════════
if ($Report) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('# NewToys 验收报告')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("- 时间：$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
    [void]$sb.AppendLine("- 配置：$Configuration")
    [void]$sb.AppendLine("- 仓库：$repoRoot")
    [void]$sb.AppendLine("- 结论：**$verdict**（PASS $($pass.Count) / WARN $($warns.Count) / FAIL $($fails.Count) / SKIP $($skips.Count)）")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('| 区域 | 检查项 | 结果 | 说明 |')
    [void]$sb.AppendLine('|---|---|---|---|')
    foreach ($r in $script:rows) {
        [void]$sb.AppendLine("| $($r.Area) | $($r.Name) | $($r.Status) | $($r.Detail -replace '\|','\|') |")
    }
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('## 原始输出')
    foreach ($r in ($script:rows | Where-Object { $_.Raw })) {
        [void]$sb.AppendLine()
        [void]$sb.AppendLine("### [$($r.Area)] $($r.Name)")
        [void]$sb.AppendLine('```')
        [void]$sb.AppendLine(($r.Raw -replace '`', "'").TrimEnd())
        [void]$sb.AppendLine('```')
    }
    $reportPath = Join-Path $repoRoot 'artifacts\accept-report.md'
    New-Item -ItemType Directory -Path (Split-Path $reportPath) -Force | Out-Null
    Set-Content -Path $reportPath -Value $sb.ToString() -Encoding utf8
    Write-Host "  报告已写：$reportPath" -ForegroundColor Green
}

exit $(if ($fails.Count -eq 0) { 0 } else { 1 })
