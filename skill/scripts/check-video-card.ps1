# NewToys 「视频详情卡同源」静态检查
# 用法（在仓库根执行）：pwsh skill/scripts/check-video-card.ps1 [-All]
#
# 🔴 为什么需要它：视频预览页与批量处理页共用的那张详情卡，路径是**字符串**
#    （DetailDescriptor 里全是 ValuePath / VisibleWhen / OptionsPath …）。两页任一 VM 把属性
#    改名或漏加，Avalonia 不报错、只留一行绑定警告 —— 卡上就少一块、白一块。
#    历史上这两页各写了一份 BuildCard()，改一处忘另一处，界面直接长歪。
#
# 检查项：
#   V-01 `new DetailCardDescriptor` 只允许出现在 Details/VideoTaskCard.cs（否则又变成两份卡）
#   V-02 VideoTaskCard 引用的每个属性名，必须在预览页与批量页 VM 里都存在
#        （`BatchOnlyPrefixes` 里的前缀只查批量页；`_camelCase` 字段形式也算命中）
#
# ⛔ 只报告，不修改任何文件。V-01 / V-02 是 ERROR（accept.ps1 按 `[ERROR] N 条` 判失败）。

param([switch]$All)

$ErrorActionPreference = 'Continue'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$moduleDir = Join-Path $repoRoot 'src\Module.VideoTools'
$cardFile = Join-Path $moduleDir 'Details\VideoTaskCard.cs'

Write-Host "`n=== NewToys 视频详情卡同源自检 ===" -ForegroundColor Cyan

if (-not (Test-Path $cardFile)) {
    Write-Host "[ERROR] 1 条  —— 找不到唯一卡定义：src/Module.VideoTools/Details/VideoTaskCard.cs" -ForegroundColor Red
    exit 0
}

function Read-Utf8([string]$path) { [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8) }

$errors = New-Object System.Collections.ArrayList

# ---------- V-01：别处再 new 一份卡 ----------
$csFiles = Get-ChildItem -Path $moduleDir -Recurse -Filter *.cs -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
foreach ($f in $csFiles) {
    if ($f.FullName -ieq (Resolve-Path $cardFile)) { continue }
    $txt = Read-Utf8 $f.FullName
    $n = ([regex]::Matches($txt, 'new\s+DetailCardDescriptor')).Count
    if ($n -gt 0) {
        $rel = $f.FullName.Substring($repoRoot.Path.Length + 1).Replace('\', '/')
        [void]$errors.Add("V-01  $rel 又新建了 DetailCardDescriptor（$n 处）—— 卡只能由 Details/VideoTaskCard.cs 产出")
    }
}

# ---------- V-02：卡引用的属性两页必须都有 ----------
$cardTxt = Read-Utf8 $cardFile
$names = @([regex]::Matches($cardTxt, 'public\s+const\s+string\s+\w+\s*=\s*"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })

$onlyPrefixes = @()
$mPrefix = [regex]::Match($cardTxt, 'BatchOnlyPrefixes\s*=\s*\{([^}]*)\}')
if ($mPrefix.Success) {
    $onlyPrefixes = @([regex]::Matches($mPrefix.Groups[1].Value, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
}

$vmFiles = @{
    '预览页' = @(Get-ChildItem -Path (Join-Path $moduleDir 'ViewModels') -Filter 'VideoPreviewViewModel*.cs' -File)
    '批量页' = @(Get-ChildItem -Path (Join-Path $moduleDir 'ViewModels') -Filter 'VideoBatchViewModel*.cs' -File)
}

$texts = @{}
foreach ($k in $vmFiles.Keys) {
    $texts[$k] = ($vmFiles[$k] | ForEach-Object { Read-Utf8 $_.FullName }) -join "`n"
}

foreach ($name in $names) {
    $targets = @('预览页', '批量页')
    foreach ($p in $onlyPrefixes) {
        if ($name.StartsWith($p)) { $targets = @('批量页') }
    }
    $camel = '_' + $name.Substring(0, 1).ToLowerInvariant() + $name.Substring(1)
    foreach ($page in $targets) {
        $hit = [regex]::IsMatch($texts[$page], "\b$([regex]::Escape($name))\b") -or
               [regex]::IsMatch($texts[$page], "\b$([regex]::Escape($camel))\b")
        if (-not $hit) {
            [void]$errors.Add("V-02  $page 缺属性 `"$name`"（VideoTaskCard 的卡里引用了它）")
        }
    }
}

# ---------- 汇总 ----------
Write-Host ("卡定义引用属性 {0} 个；批量页专属前缀：{1}" -f $names.Count, ($onlyPrefixes -join ', ')) -ForegroundColor Gray

if ($errors.Count -eq 0) {
    Write-Host "[OK] 0 条 —— 两页详情卡同源，属性齐全" -ForegroundColor Green
    exit 0
}

Write-Host ("[ERROR] {0} 条" -f $errors.Count) -ForegroundColor Red
$show = if ($All) { $errors } else { $errors | Select-Object -First 30 }
$show | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
if (-not $All -and $errors.Count -gt 30) { Write-Host ("  …（还有 {0} 条，加 -All 看全部）" -f ($errors.Count - 30)) -ForegroundColor DarkGray }

exit 0
