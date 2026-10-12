# NewToys 静态自检（交付前跑，不替代用户本地 build.ps1）
# 用法（在仓库根执行）：pwsh skill/scripts/check.ps1
# 定位：把 AI 人肉做的 .axaml 合法性 / .axaml.cs 配对 / 主题键白名单 自动化，
#       只报告问题，不修改任何文件、不以非零码退出（自检是参考，不是门禁）。

$ErrorActionPreference = 'Continue'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$src = Join-Path $repoRoot 'src'
Write-Host "`n=== NewToys 静态自检 ===" -ForegroundColor Cyan
Write-Host "仓库根: $repoRoot" -ForegroundColor Gray
if (-not (Test-Path $src)) { Write-Host '[FAIL] 找不到 src/' -ForegroundColor Red; exit 0 }

# 主题键白名单：XAML 中 x:Key="...Brush" + 代码注入 S("...Brush")
$brushKeys = @()
Get-ChildItem $src -Recurse -Include '*.axaml','*.cs' | ForEach-Object {
    $t = Get-Content $_.FullName -Raw
    [regex]::Matches($t, 'x:Key="([\w]*Brush)"') | ForEach-Object { $brushKeys += $_.Groups[1].Value }
    [regex]::Matches($t, 'S\("([\w]*Brush)"')    | ForEach-Object { $brushKeys += $_.Groups[1].Value }
}
$brushKeys = $brushKeys | Sort-Object -Unique
Write-Host "`n[主题键] 白名单 $($brushKeys.Count) 个 *Brush 键" -ForegroundColor Cyan

# .axaml 合法性 + 仅含 x:Class 的才需配对 .axaml.cs（纯样式/资源字典无 Code-behind）
$axamls = Get-ChildItem $src -Recurse -Filter '*.axaml' | Where-Object { $_.Name -notmatch '\.axaml\.cs$' }
$bad = 0; $unpaired = 0
Write-Host "`n[AXAML] 检查 $($axamls.Count) 个 .axaml..." -ForegroundColor Cyan
foreach ($f in $axamls) {
    $raw = Get-Content $f.FullName -Raw
    try { $xml = [xml]$raw } catch {
        Write-Host "  [XML失败] $($f.FullName) : $($_.Exception.Message)" -ForegroundColor Red
        $bad++; continue
    }
    if ($raw -match 'x:Class=') {
        $cs = Join-Path $f.DirectoryName ($f.BaseName + '.axaml.cs')
        if (-not (Test-Path $cs)) {
            Write-Host "  [缺配对] $($f.FullName) 有 x:Class 但无 .axaml.cs" -ForegroundColor Yellow
            $unpaired++
        }
    }
}

# DynamicResource/StaticResource 引用的 *Brush 键是否都在白名单
if ($brushKeys.Count -gt 0) {
    $unknown = @{}
    Get-ChildItem $src -Recurse -Include '*.cs','*.axaml' | Where-Object { $_.Name -notmatch '\.axaml\.cs$' } | ForEach-Object {
        $t = Get-Content $_.FullName -Raw
        [regex]::Matches($t, '\{(?:DynamicResource|StaticResource)\s+([\w]+)') | ForEach-Object {
            $k = $_.Groups[1].Value
            if ($k -like '*Brush' -and $k -notin $brushKeys) { $unknown[$k] = $true }
        }
    }
    if ($unknown.Count -gt 0) {
        Write-Host "`n[主题键] 白名单外的 *Brush 引用（疑似旧键/拼错）：" -ForegroundColor Yellow
        $unknown.Keys | Sort-Object | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
    } else {
        Write-Host "`n[主题键] 所有 *Brush 引用均在白名单内 ✅" -ForegroundColor Green
    }
}

Write-Host "`n=== 汇总 ===" -ForegroundColor Cyan
Write-Host "  XML 不合法 .axaml : $bad"
Write-Host "  缺 .axaml.cs 配对 : $unpaired"
Write-Host "  *Brush 白名单      : $($brushKeys.Count)"
Write-Host "`n提示：本脚本只做轻量静态检查；完整编译由用户本地 build.ps1 验证。" -ForegroundColor Gray
exit 0
