# NewToys 注释健康度静态检查（交付前跑；与 check.ps1 / check-axaml-props.ps1 / check-techstack.ps1 互补）
# 用法（在仓库根执行）：pwsh skill/scripts/check-comments.ps1 [-All]
#   -All  同时列出 WARN 明细（默认只列 ERROR 明细 + WARN 计数，保持输出短）
#
# 规则清单唯一源 = skill/references/tech-guard.md 第二节（C-01 ~ C-06）—— 改这里必须同步改那边。
# ⛔ 只报告，不修改任何文件，不以非零码退出（自检是参考，不是门禁）。
#
# 定位：注释腐烂的四种形态，每种一条机器判据 ——
#   C-01 版本前缀叙事（"v3.66 第八轮：..."）      = git log 的职责，注释里不该出现
#   C-02 散文式长注释块（连续 >= 6 行 //）        = 该进文档，或压成 1-3 行 WHY
#   C-03 单文件注释率排行                          = 仅统计（Top5 参考），**不告警**：model/接口类天然偏高
#   C-04 历史叙事词（"原来 / 此前 / 曾经"）       = 同 C-01，且更难检索
#   C-05 View 类名重复（ERROR）                   = NavigationService 靠类名反射找 View，重名 = 静默错页
#   C-06 注释里出现 NT-xxx 任务编号               = 编号属叙事；编号→结论的唯一索引在 framework.md §11.2

param([switch]$All)

$ErrorActionPreference = 'Continue'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$src = Join-Path $repoRoot 'src'
Write-Host "`n=== NewToys 注释健康度自检 ===" -ForegroundColor Cyan
Write-Host "仓库根: $repoRoot" -ForegroundColor Gray
if (-not (Test-Path $src)) { Write-Host '[FAIL] 找不到 src/' -ForegroundColor Red; exit 0 }

$findings = New-Object System.Collections.ArrayList
function Add-Finding([string]$id, [string]$level, [string]$fullPath, [int]$line, [string]$msg) {
    $rel = $fullPath.Substring($src.Length + 1)
    [void]$findings.Add([pscustomobject]@{ Id = $id; Level = $level; Loc = "${rel}:$line"; Msg = $msg })
}

# ---------- 1) 目标文件（排除 bin / obj / docs / 测试项目）----------
# ⚠️ 测试项目豁免：测试方法名与断言消息本身就是长文本，注释率天然偏高，扫了全是误报。
$targets = @(Get-ChildItem -Path $src -Recurse -File -Filter '*.cs' -ErrorAction SilentlyContinue |
             Where-Object { $_.FullName -notmatch '\\(bin|obj|docs)\\' -and $_.FullName -notmatch '\\NewToys\.Tests\\' })

Write-Host ("扫描：.cs {0} 个" -f $targets.Count) -ForegroundColor Gray

# ---------- 2) 规则常量 ----------
# C-01：以版本号 / 「第 N 轮」开头的注释行 —— 这类前缀只说明"哪次改的"，不说明"为什么"，
#       后代读者无法据此判断代码正确性，而 git blame / git log 已完整记录。
$reVersionPrefix = '^\s*//+\s*(v\d+\.\d+|NT-\d{3}\s*[:：]?\s*v\d+\.\d+|第[一二三四五六七八九十0-9]+轮)'

# C-04：历史叙事词。保守匹配 —— 只认明确指代"过去状态"的短语，
#       ⛔ 不收「改为 / 改成」：它们常出现在正常语义里（"空值改为默认值"），误报率高。
$reHistoryWord = '原来|此前是|以前是|曾经是|早先是'

# C-02 阈值：连续原生注释行数
$maxCommentBlock = 6

# ---------- 3) 逐文件扫描 ----------
$fileStats = New-Object System.Collections.ArrayList
foreach ($f in $targets) {
    $lines = [System.IO.File]::ReadAllLines($f.FullName, [System.Text.Encoding]::UTF8)
    $total = $lines.Count
    if ($total -eq 0) { continue }

    $commentCount = 0
    $blockStart = 0
    $blockLen = 0

    for ($i = 0; $i -lt $total; $i++) {
        $line = $lines[$i]
        $isPlainComment = $line -match '^\s*//(?![/!])'          # // 但不是 /// 或 //!
        $isAnyComment   = $line -match '^\s*(///|//|\*|/\*)'      # 计入注释率（含 XML 文档注释）

        if ($isAnyComment) { $commentCount++ }

        # ---- C-02 连续注释块（只数原生 // 行：/// 是对外契约，允许长） ----
        if ($isPlainComment) {
            if ($blockLen -eq 0) { $blockStart = $i + 1 }
            $blockLen++
        }
        else {
            if ($blockLen -ge $maxCommentBlock) {
                Add-Finding 'C-02' 'WARN' $f.FullName $blockStart "连续 $blockLen 行注释（阈值 $maxCommentBlock）—— 该进文档或压成 1-3 行 WHY"
            }
            $blockLen = 0
        }

        # ---- C-01 / C-04 / C-06 单行判据 ----
        if ($line -match '^\s*//') {
            if ($line -match $reVersionPrefix) {
                Add-Finding 'C-01' 'WARN' $f.FullName ($i + 1) "版本/轮次前缀叙事 —— 删前缀，只留 WHY（git log 已记录）"
            }
            elseif ($line -match $reHistoryWord) {
                Add-Finding 'C-04' 'WARN' $f.FullName ($i + 1) "历史叙事 —— 描述过去状态，无法据此判断当前正确性"
            }
        }

        # ---- C-06 任务编号不进注释（编号 → 结论的唯一索引在 framework.md §11.2）----
        if ($isAnyComment -and $line -match 'NT-\d{3}') {
            Add-Finding 'C-06' 'WARN' $f.FullName ($i + 1) "注释里出现任务编号 —— 删编号，结论查 framework.md §11.2"
        }
    }
    # 文件尾部未闭合的注释块
    if ($blockLen -ge $maxCommentBlock) {
        Add-Finding 'C-02' 'WARN' $f.FullName $blockStart "连续 $blockLen 行注释（阈值 $maxCommentBlock）"
    }

    # ---- C-03 注释率（只统计、不告警）----
    # 🔴 这条**故意不产生 WARN**：实测 51 个文件命中，其中大量是 model / descriptor 类
    #    （ListColumn 47%、DetailCardDescriptor 51%）—— 属性上的注释就是对外契约，
    #    「注释多于代码」在声明型文件里是**正确**形态。审美判据做 lint 必然烂成噪音，
    #    所以这里只留 Top5 排行供人参考，不进汇总计数。
    $isInterfaceOnly = ($lines -match '^\s*(public|internal)\s+(partial\s+)?interface\s') -and
                       -not ($lines -match '^\s*(public|internal)\s+(sealed\s+|partial\s+|static\s+)*class\s')
    if ($isInterfaceOnly) { continue }

    $ratio = $commentCount / $total
    [void]$fileStats.Add([pscustomobject]@{ File = $f.FullName; Ratio = $ratio; Comments = $commentCount; Total = $total })
}

# ---------- 4) C-05：View 类名唯一性（ERROR —— 反射导航的硬前提）----------
# NavigationService.NavigateTo 按 ViewTypeName 字符串遍历已加载程序集找同名类，
# FirstOrDefault 命中即用 ⇒ 一旦两个程序集里有同名 View，先加载的获胜，表现为「点开是另一个页面」，
# 编译期不报错、运行期无异常。这条是唯一能提前拦住它的检查。
$viewNames = @{}
$axamlCs = @(Get-ChildItem -Path $src -Recurse -File -ErrorAction SilentlyContinue |
             Where-Object { $_.Name -like '*.axaml.cs' -and $_.FullName -notmatch '\\(bin|obj)\\' })

foreach ($f in $axamlCs) {
    $text = [System.IO.File]::ReadAllText($f.FullName, [System.Text.Encoding]::UTF8)
    foreach ($m in [regex]::Matches($text, '(?m)^\s*(?:public|internal)\s+(?:sealed\s+)?partial\s+class\s+([A-Za-z_]\w*)')) {
        $name = $m.Groups[1].Value
        if (-not $viewNames.ContainsKey($name)) { $viewNames[$name] = New-Object System.Collections.ArrayList }
        [void]$viewNames[$name].Add($f.FullName)
    }
}
foreach ($kv in $viewNames.GetEnumerator()) {
    if ($kv.Value.Count -le 1) { continue }
    $where = ($kv.Value | ForEach-Object { $_.Substring($src.Length + 1) }) -join ' / '
    Add-Finding 'C-05' 'ERROR' $kv.Value[0] 1 ("View 类名重复：{0} —— {1}" -f $kv.Key, $where)
}

# ---------- 5) 汇总 ----------
# 注释率最高 5 个文件（只作参考，不单独告警）
Write-Host "`n[注释率 Top5]" -ForegroundColor Cyan
foreach ($s in ($fileStats | Sort-Object Ratio -Descending | Select-Object -First 5)) {
    $rel = $s.File.Substring($src.Length + 1)
    Write-Host ("  {0,6:P0}  {1,5}/{2,-5}  {3}" -f $s.Ratio, $s.Comments, $s.Total, $rel) -ForegroundColor Gray
}

$errors = @($findings | Where-Object { $_.Level -eq 'ERROR' })
$warns  = @($findings | Where-Object { $_.Level -eq 'WARN' })

Write-Host "`n=== 汇总 ===" -ForegroundColor Cyan
Write-Host ("  [ERROR] {0} 条" -f $errors.Count) -ForegroundColor $(if ($errors.Count -gt 0) { 'Red' } else { 'Gray' })
Write-Host ("  [WARN]  {0} 条" -f $warns.Count) -ForegroundColor $(if ($warns.Count -gt 0) { 'Yellow' } else { 'Gray' })

if ($errors.Count -gt 0) {
    Write-Host "`n[ERROR 明细]" -ForegroundColor Red
    foreach ($e in $errors) { Write-Host ("  {0}  {1}  {2}" -f $e.Id, $e.Loc, $e.Msg) -ForegroundColor Red }
}

if ($All -and $warns.Count -gt 0) {
    Write-Host "`n[WARN 明细]" -ForegroundColor Yellow
    # 按规则分组输出，避免单个规则刷屏
    foreach ($g in ($warns | Group-Object Id | Sort-Object Name)) {
        Write-Host ("  {0}（{1} 条）" -f $g.Name, $g.Count) -ForegroundColor Yellow
        foreach ($w in $g.Group) { Write-Host ("    {0}  {1}" -f $w.Loc, $w.Msg) -ForegroundColor DarkYellow }
    }
}

Write-Host "`n提示：C-01/C-02/C-04/C-06 是 WARN（不阻断），C-05 是 ERROR（View 重名会静默错页）。" -ForegroundColor Gray
Write-Host "      C-03 只出排行不告警（审美判据做 lint 会烂成噪音）。" -ForegroundColor Gray
