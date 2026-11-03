#Requires -Version 7.0
<#
════════════════════════════════════════════════════════════════════
  NewToys 静态验收（accept）
────────────────────────────────────────────────────────────────────
  一条命令跑完「静态检查 → 铁律 1 改动范围」，给出 **通过 / 不通过** + 退出码。

  交互模式（手动执行）：
    pwsh skill/scripts/accept.ps1            # 交互式菜单（终端里直接跑；脚本/管道下不会弹菜单）
    pwsh skill/scripts/accept.ps1 -Menu      # 强制弹菜单
    pwsh skill/scripts/accept.ps1 -Only docs # 指定单项：all|static|docs|fix|scope
    pwsh skill/scripts/accept.ps1 -Only fix  # = docs-sync --fix 修 AUTO 块后再查一遍

    pwsh skill/scripts/accept.ps1              # 非交互 = 全部静态检查
    pwsh skill/scripts/accept.ps1 -All         # WARN 明细全展开（透传给子检查）
    pwsh skill/scripts/accept.ps1 -Report      # 额外写 artifacts/accept-report.md
    pwsh skill/scripts/accept.ps1 -SkipStatic  # 只做改动范围检查（不跑 Lint）
    pwsh skill/scripts/accept.ps1 -Brief       # 只打一行结论（完整表格强制写 artifacts/accept-report.md）

  退出码：0 = 全通过；1 = 存在 FAIL（脚本自身报错亦为 1）。
  ⛔ 本脚本只读，不修改 src/ 下任何源文件。
  🔴 本脚本不含任何构建 / 测试 / 启动能力：编译验证归用户本地
     `pwsh -File .\scripts\build.ps1`，运行归 `scripts\run.ps1`。
     skill/ 下的脚本只做静态检查，⛔ 不加 dotnet build / dotnet test / 产物核对 / 冒烟。
  ⛔ 本脚本不替代人工回归：它只证明「静态硬伤为 0 + 改动不越界」，
     既不证明编译通过，也不证明功能符合验收标准。
════════════════════════════════════════════════════════════════════
#>
param(
    # 只做改动范围检查，跳过静态检查
    [switch]$SkipStatic,
    # 兼容历史命令：本脚本已无构建步骤，传与不传等价
    [switch]$SkipBuild,
    # 写 artifacts/accept-report.md
    [switch]$Report,
    # 透传 -All 给支持的子检查（WARN 明细全展开）
    [switch]$All,
    # 只输出一行结论（完整表格强制写入 artifacts/accept-report.md）—— 省 AI 读取成本
    [switch]$Brief,
    # 只跑某一项：all（默认）| static 静态检查 | docs 文档一致性 | fix 文档一致性并修复 | scope 改动范围
    [ValidateSet('all', 'static', 'docs', 'fix', 'scope')]
    [string]$Only = 'all',
    # 强制弹交互菜单（终端里无参数运行时也会自动弹）
    [switch]$Menu,
    # 跑完不等按键（脚本 / 自动化里用；手动跑默认停住看结果）
    [switch]$NoPause
)

$ErrorActionPreference = 'Continue'
$ProgressPreference    = 'SilentlyContinue'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$src      = Join-Path $repoRoot 'src'
if ($Brief) { $Report = $true }

if (-not $Brief) { Write-Host "`n════════ NewToys 静态验收 ════════" -ForegroundColor Cyan }
if (-not $Brief) { Write-Host "仓库根 : $repoRoot" -ForegroundColor DarkGray }
if (-not (Test-Path $src)) {
    Write-Host "[FAIL] 找不到 src/ —— 请从仓库内运行本脚本" -ForegroundColor Red
    exit 1
}

# ---------- 交互模式（手动执行）----------
# 触发条件：显式 -Menu，或「终端里直接跑、没带任何参数」。管道 / 无终端 / AI 调用不弹菜单。
function Read-MenuKey {
    # 输入被重定向（管道 / 自动化）时退回 Read-Host，⛔ 别让 ReadKey 卡住
    if ([Console]::IsInputRedirected) { return (Read-Host).Trim() }
    try { return [string]$Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown').Character }
    catch { return (Read-Host).Trim() }
}

function Show-Menu {
    $items = @(
        [pscustomobject]@{ Key = '1'; Only = 'all';    Report = $true;  Label = '全部检查（静态 + 文档一致性 + 改动范围），完整表格' }
        [pscustomobject]@{ Key = '2'; Only = 'all';    Report = $false; Label = '全部检查，一行结论（快速看通过没通过）' }
        [pscustomobject]@{ Key = '3'; Only = 'static'; Report = $false; Label = '只跑静态检查（AXAML / 属性名 / 技术栈 / 详情卡 / 注释 / 页面契约）' }
        [pscustomobject]@{ Key = '4'; Only = 'docs';   Report = $false; Label = '只跑文档一致性（AUTO 块 / 菜单 Key / 路径引用 / 卡窗口 / NT 编号）' }
        [pscustomobject]@{ Key = '5'; Only = 'fix';    Report = $false; Label = '修复文档 AUTO 块后复查（docs-sync --fix，会写 index/framework/台账）' }
        [pscustomobject]@{ Key = '6'; Only = 'scope';  Report = $false; Label = '只跑铁律 1（改动是否只在 src/ 与授权区）' }
    )
    Write-Host ''
    Write-Host '  NewToys 静态验收 —— 选择要跑的项目' -ForegroundColor Cyan
    Write-Host '  ────────────────────────────────────────────' -ForegroundColor DarkGray
    foreach ($m in $items) { Write-Host ("   {0})  {1}" -f $m.Key, $m.Label) -ForegroundColor Gray }
    Write-Host '   0)  退出' -ForegroundColor DarkGray
    Write-Host ''
    Write-Host '  按数字键即可（不用回车）… ' -ForegroundColor DarkYellow -NoNewline
    $k = Read-MenuKey
    Write-Host $k -ForegroundColor DarkGray
    if ($k -eq '' -or $k -eq "`e") { return $null }
    if ($k -eq '0') { return 'exit' }
    $hit = $items | Where-Object { $_.Key -eq $k } | Select-Object -First 1
    if (-not $hit) { return $null }
    return $hit
}

$script:IsInteractive = [Environment]::UserInteractive -and -not [Console]::IsInputRedirected -and -not [Console]::IsOutputRedirected
$explicitParams = @($PSBoundParameters.Keys | Where-Object { $_ -ne 'Menu' })
if ($Menu -or ($script:IsInteractive -and $explicitParams.Count -eq 0)) {
    $picked = Show-Menu
    if ($picked -eq 'exit' -or $null -eq $picked) { exit 0 }
    $Only   = $picked.Only
    $Report = $picked.Report
    $Brief  = ($picked.Key -eq '2')
}
if ($Brief) { $Report = $true }

$RunStatic = (-not $SkipStatic) -and ($Only -in @('all', 'static'))
$RunDocs   = (-not $SkipStatic) -and ($Only -in @('all', 'docs', 'fix'))
$RunScope  = ($Only -in @('all', 'scope'))

# ---------- 结果收集 ----------
$script:rows = New-Object System.Collections.ArrayList
function Add-Row([string]$Area, [string]$Name, [string]$Status, [string]$Detail, [string]$Raw = '') {
    [void]$script:rows.Add([pscustomobject]@{
        Area = $Area; Name = $Name; Status = $Status; Detail = $Detail; Raw = $Raw
    })
}

# ═════════ 1) 静态检查 ═════════
# 全部实现在 skill/scripts/lint.py（一个文件多子命令），Only = 传给 --only 的子命令。
# Fail 正则命中 ⇒ FAIL（阻断）；Warn 正则命中 ⇒ WARN（不阻断）。
$staticChecks = @(
    [pscustomobject]@{
        Name = 'AXAML 合法性 / 配对 / 主题键'
        Only = 'axaml'
        Fail = @('XML 不合法 \.axaml\s*:\s*([1-9]\d*)', '缺 \.axaml\.cs 配对\s*:\s*([1-9]\d*)')
        Warn = @('白名单外的 \*Brush 引用')
    }
    [pscustomobject]@{
        Name = 'XAML 属性名（Avalonia ≠ WPF）'
        Only = 'props'
        Fail = @('疑似 WPF 专有 / 拼错\s*:\s*([1-9]\d*)')
        Warn = @()
    }
    [pscustomobject]@{
        Name = '技术栈硬伤（G-01~G-11 + G-14~G-17）'
        Only = 'techstack'
        Fail = @('\[ERROR\]\s*([1-9]\d*) 条')
        Warn = @('\[WARN\]\s*([1-9]\d*) 条')
    }
    [pscustomobject]@{
        Name = '详情卡绑定路径'
        Only = 'detail-paths'
        Fail = @()
        Warn = @('\[WARN\]\s*([1-9]\d*) 条绑定路径')
    }
    # 视频详情卡同源（V-01 别处又 new 一份卡 / V-02 卡引用的属性两页缺一个）
    [pscustomobject]@{
        Name = '视频详情卡同源（预览 = 批量）'
        Only = 'video-card'
        Fail = @('\[ERROR\]\s*([1-9]\d*)\s*条')
        Warn = @()
    }
    # 注释健康度（C-01 版本前缀 / C-02 长注释块 / C-04 历史叙事 = WARN；C-05 View 重名 = ERROR）
    # 🔴 C-05 是唯一能提前拦住「两个程序集有同名 View ⇒ 反射导航静默错页」的检查。
    [pscustomobject]@{
        Name = '注释健康度 / View 类名唯一性'
        Only = 'comments'
        Fail = @('\[ERROR\]\s*([1-9]\d*) 条')
        Warn = @('\[WARN\]\s*([1-9]\d*) 条')
    }
    # 页面契约（P-01 View 找不到同名 VM / P-02 泛型实参不是真 View 类）：
    # 两条都是 ERROR —— 名字对不上时运行期不报错，只是页面空白或打不开。
    [pscustomobject]@{
        Name = '页面契约（View ↔ ViewModel / 菜单注册）'
        Only = 'view-pairs'
        Fail = @('\[ERROR\]\s*([1-9]\d*) 条')
        Warn = @('\[WARN\]\s*([1-9]\d*) 条')
    }
)

if ($RunStatic) {
        if (-not $Brief) { Write-Host "`n──── 1. 静态检查 ────" -ForegroundColor Cyan }
    $lintPy    = Join-Path $repoRoot 'skill\scripts\lint.py'
    $pythonExe = (Get-Command python -ErrorAction SilentlyContinue).Source

    foreach ($c in $staticChecks) {
        if (-not (Test-Path $lintPy)) {
            Add-Row '静态检查' $c.Name 'SKIP' 'lint.py 不存在'
            Write-Host ("  [SKIP] {0} —— skill/scripts/lint.py 不存在" -f $c.Name) -ForegroundColor DarkYellow
            continue
        }
        if (-not $pythonExe) {
            Add-Row '静态检查' $c.Name 'SKIP' '找不到 python'
            Write-Host ("  [SKIP] {0} —— 找不到 python，请在 PATH 中安装 Python 3" -f $c.Name) -ForegroundColor DarkYellow
            continue
        }

        $argv = @($lintPy, '--only', $c.Only)
        if ($All) { $argv += '--all' }
        $prevEnc = [Console]::OutputEncoding
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        $env:PYTHONIOENCODING = 'utf-8'
        try { $raw = (& $pythonExe @argv 2>&1 | Out-String) }
        finally { [Console]::OutputEncoding = $prevEnc }

        $f = 0; foreach ($re in $c.Fail) { $f += ([regex]::Matches($raw, $re)).Count }
        $w = 0; foreach ($re in $c.Warn) { $w += ([regex]::Matches($raw, $re)).Count }
        $status = if ($f -gt 0) { 'FAIL' } elseif ($w -gt 0) { 'WARN' } else { 'PASS' }
        $detail = "{0} 阻断 / {1} 告警" -f $f, $w
        Add-Row '静态检查' $c.Name $status $detail $raw

        $tag = switch ($status) { 'PASS' { '[PASS] ' } 'WARN' { '[WARN] ' } default { '[FAIL] ' } }
        $color = switch ($status) { 'PASS' { 'Green' } 'WARN' { 'Yellow' } default { 'Red' } }
        if (-not $Brief) {
            Write-Host ("  {0}{1} —— {2}" -f $tag, $c.Name, $detail) -ForegroundColor $color
            if ($status -ne 'PASS') {
                # 打印带 [ERROR]/[WARN] 的明细行，帮定位
                $raw -split "\r?\n" | Where-Object { $_ -match '\[(ERROR|WARN|XML失败|缺配对)\]' } |
                    Select-Object -First 12 | ForEach-Object { Write-Host "        $($_.Trim())" -ForegroundColor DarkGray }
            }
        }
    }
}
else {
    if ($SkipStatic) { Add-Row '静态检查' '全部' 'SKIP' '-SkipStatic' }
}

# ═════════ 1b) 文档一致性（docs-sync.py：模块清单 / 页数 / 路径引用 ⇄ src）═════════
# 只读、纯静态：把「文档说 A、代码是 B」变成可跑的 FAIL，避免文档靠人肉同步。
$syncPy = Join-Path $repoRoot 'skill\scripts\docs-sync.py'
$pyExe  = (Get-Command python -ErrorAction SilentlyContinue).Source
if ($RunDocs -and -not $Brief) { Write-Host "`n──── 2. 文档一致性 ────" -ForegroundColor Cyan }
if ($RunDocs -and $pyExe -and (Test-Path $syncPy)) {
    $prevEnc = [Console]::OutputEncoding
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    $env:PYTHONIOENCODING = 'utf-8'
    $syncArg = @($syncPy)
    if ($Only -eq 'fix') { $syncArg += '--fix' }
    try { $syncOut = (& $pyExe @syncArg 2>&1 | Out-String) }
    finally { [Console]::OutputEncoding = $prevEnc }

    $se = 0; $sw = 0
    if ($syncOut -match '结果：ERROR\s*(\d+)\s*｜\s*WARN\s*(\d+)') {
        $se = [int]$Matches[1]; $sw = [int]$Matches[2]
    }
    $sStatus = if ($se -gt 0) { 'FAIL' } elseif ($sw -gt 0) { 'WARN' } else { 'PASS' }
    Add-Row '文档一致性' '文档 ⇄ src（AUTO 块 / 页数 / 路径）' $sStatus ("{0} 阻断 / {1} 告警" -f $se, $sw) $syncOut
    if (-not $Brief) {
        $tag = switch ($sStatus) { 'PASS' { '[PASS] ' } 'WARN' { '[WARN] ' } default { '[FAIL] ' } }
        $color = switch ($sStatus) { 'PASS' { 'Green' } 'WARN' { 'Yellow' } default { 'Red' } }
        Write-Host ("  {0}文档 ⇄ src —— {1} 阻断 / {2} 告警" -f $tag, $se, $sw) -ForegroundColor $color
        if ($sStatus -ne 'PASS') {
            $syncOut -split "\r?\n" | Where-Object { $_ -match '^\[(ERROR|WARN)\]' } |
                Select-Object -First 8 | ForEach-Object { Write-Host "        $($_.Trim())" -ForegroundColor DarkGray }
        }
        if ($Only -eq 'fix') {
            $syncOut -split "\r?\n" | Where-Object { $_ -match '已重写|一致$' } |
                ForEach-Object { Write-Host "        $($_.Trim())" -ForegroundColor DarkCyan }
        }
    }
}

# ═════════ 3) 铁律 1：改动是否只在 src/（skill/ scripts/ AGENTS.md 经授权可改）═════════
if ($RunScope) {
if (-not $Brief) { Write-Host "`n──── 3. 铁律 1（改动范围）────" -ForegroundColor Cyan }
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
    # 授权区：src/（正文）+ skill/（知识库与权威文档）+ scripts/（构建脚本）+ var/（用户自用脚本）+ AGENTS.md（入口）
    $outside = @($changed | Where-Object { $_ -notmatch '^src/' -and $_ -notmatch '^skill/' -and $_ -notmatch '^scripts/' -and $_ -notmatch '^AGENTS\.md$' -and $_ -notmatch '^var/' })
    if ($outside.Count -gt 0) {
        Add-Row '铁律1' 'src/ 之外的改动' 'WARN' ("$($outside.Count) 处越界") ($outside -join "`n")
        if (-not $Brief) {
            Write-Host "  [WARN] src/ 之外有 $($outside.Count) 处改动（skill/ scripts/ AGENTS.md 经授权可改，其余应确认）：" -ForegroundColor Yellow
            $outside | Select-Object -First 10 | ForEach-Object { Write-Host "        $_" -ForegroundColor DarkGray }
        }
    }
    else {
        Add-Row '铁律1' 'src/ 之外的改动' 'PASS' "工作区 $($changed.Count) 个文件，均在 src/ 或 skill/"
        if (-not $Brief) { Write-Host "  [PASS] 工作区 $($changed.Count) 个改动，均在授权区" -ForegroundColor Green }
    }
}
else {
    Add-Row '铁律1' 'src/ 之外的改动' 'SKIP' '找不到 git'
}
}

# ═════════ 汇总 ═════════
$fails = @($script:rows | Where-Object { $_.Status -eq 'FAIL' })
$warns = @($script:rows | Where-Object { $_.Status -eq 'WARN' })
$skips = @($script:rows | Where-Object { $_.Status -eq 'SKIP' })
$pass  = @($script:rows | Where-Object { $_.Status -eq 'PASS' })

if (-not $Brief) { Write-Host "`n════════ 验收结果 ════════" -ForegroundColor Cyan }
$verdict = if ($fails.Count -eq 0) { '✅ 通过' } else { '❌ 不通过' }
$totalColor = if ($fails.Count -eq 0) { 'Green' } else { 'Red' }

if ($Brief) {
    # 一行结论：AI 只需读取这一行，完整表格在 artifacts/accept-report.md
    $warnNames = ($warns | ForEach-Object { $_.Name }) -join '、'
    $tail = if ($warnNames) { "  ｜ 告警：$warnNames" } else { '' }
    Write-Host ("`naccept: {0} ｜ PASS {1} / WARN {2} / FAIL {3} / SKIP {4}{5} ｜ 明细 artifacts/accept-report.md" -f `
        $verdict, $pass.Count, $warns.Count, $fails.Count, $skips.Count, $tail) -ForegroundColor $totalColor
}
else {
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

    Write-Host "`n  $verdict  （PASS $($pass.Count) / WARN $($warns.Count) / FAIL $($fails.Count) / SKIP $($skips.Count)）" -ForegroundColor $totalColor
    if ($fails.Count -gt 0) {
        Write-Host '  未通过项：' -ForegroundColor Red
        $fails | ForEach-Object { Write-Host "    - [$($_.Area)] $($_.Name)：$($_.Detail)" -ForegroundColor Red }
    }
    if ($warns.Count -gt 0) {
        Write-Host '  告警项（不阻断，建议确认）：' -ForegroundColor Yellow
        $warns | ForEach-Object { Write-Host "    - [$($_.Area)] $($_.Name)：$($_.Detail)" -ForegroundColor Yellow }
    }
    Write-Host "`n  注：本脚本只证明「静态硬伤为 0 + 改动不越界」；编译与运行由用户本地 build.ps1 / run.ps1 验证。" -ForegroundColor DarkGray
}

# ═════════ 报告（可选）═════════
if ($Report) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('# NewToys 静态验收报告')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("- 时间：$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
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

$exitCode = if ($fails.Count -eq 0) { 0 } else { 1 }

# 终端里手动跑：跑完停住等按键（窗口不会一闪而过），R 可回菜单再跑一次
if ($script:IsInteractive -and -not $Brief -and -not $NoPause) {
    Write-Host ''
    Write-Host '  按任意键退出；R = 回菜单… ' -ForegroundColor DarkGray -NoNewline
    $k = Read-MenuKey
    if ($k -in @('r', 'R')) {
        Write-Host ''
        & $PSCommandPath -Menu
        exit $LASTEXITCODE
    }
}
exit $exitCode

