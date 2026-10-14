# NewToys XAML 属性名核对（交付前跑，与 check.ps1 互补）
# 用法（在仓库根执行）：pwsh skill/scripts/check-axaml-props.ps1
#
# 定位：把 `.axaml` 里用到的**属性名**逐个到 Avalonia 的 ref 程序集里搜存在性 ——
#       专治 **Avalonia ≠ WPF 的属性名差异**（实测踩过：`SelectedValuePath` / `StrokeLineJoin`
#       在 Avalonia 里根本不存在，编译期直接抛 AVLN2000）。
# ⛔ 只报告，不修改任何文件，不以非零码退出（自检是参考，不是门禁）。
#
# 判定口径：
#   · 属性名在 Avalonia 的 ref 程序集里能找到 → 通过；
#   · 本仓自定义控件（Controls/*.cs 里注册的 StyledProperty / AttachedProperty）→ 豁免；
#   · 其余找不到的 → 报出来，并尽量给出「Avalonia 里该叫什么」的提示。

$ErrorActionPreference = 'Continue'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$src = Join-Path $repoRoot 'src'
Write-Host "`n=== NewToys XAML 属性名核对 ===" -ForegroundColor Cyan
Write-Host "仓库根: $repoRoot" -ForegroundColor Gray
if (-not (Test-Path $src)) { Write-Host '[FAIL] 找不到 src/' -ForegroundColor Red; exit 0 }

# ---------- 1) Avalonia 版本（唯一源 = src/Directory.Packages.props） ----------
$ver = $null
$propsPath = Join-Path $src 'Directory.Packages.props'
if (Test-Path $propsPath) {
    $m = Select-String -Path $propsPath -Pattern 'Include="Avalonia"\s+Version="([^"]+)"' | Select-Object -First 1
    if ($m) { $ver = $m.Matches[0].Groups[1].Value }
}
if (-not $ver) { Write-Host '[FAIL] 读不到 Avalonia 版本（src/Directory.Packages.props）' -ForegroundColor Red; exit 0 }
Write-Host "Avalonia 版本: $ver" -ForegroundColor Gray

# ---------- 2) 载入「候选名字来源」：Avalonia ref 程序集 + 本仓自定义控件 ----------
$nuget = Join-Path $env:USERPROFILE ".nuget\packages"
$ascii = [System.Text.Encoding]::GetEncoding('ISO-8859-1')
$corpus = New-Object System.Text.StringBuilder
$loaded = 0

# 2a) Avalonia 各包的 ref 程序集（ISO-8859-1 解码后当文本搜名字，ASCII 名字 1:1 无损）
$patterns = @(
    (Join-Path $nuget "avalonia\$ver\ref\**\*.dll"),
    (Join-Path $nuget "avalonia.themes.fluent\$ver\ref\**\*.dll"),
    (Join-Path $nuget "avalonia.desktop\$ver\ref\**\*.dll"),
    (Join-Path $nuget "avalonia.fonts.inter\$ver\ref\**\*.dll")
)
foreach ($pat in $patterns) {
    foreach ($f in Get-ChildItem -Path $pat -File -ErrorAction SilentlyContinue) {
        [void]$corpus.Append($ascii.GetString([System.IO.File]::ReadAllBytes($f.FullName)))
        $loaded++
    }
}
Write-Host "已载入 Avalonia ref 程序集: $loaded 个" -ForegroundColor Gray
if ($loaded -eq 0) { Write-Host '[WARN] 一个 Avalonia ref 程序集都没找到（NuGet 缓存路径变了？）——本检查无意义' -ForegroundColor Yellow; exit 0 }

# 2b) 本仓自定义控件 / 附加属性（Controls/*.cs 与任何 *.cs 里注册的 XxxProperty）
$ownNames = @{}
Get-ChildItem $src -Recurse -Include '*.cs' -File | ForEach-Object {
    $t = Get-Content $_.FullName -Raw
    [regex]::Matches($t, '(?:StyledProperty|DirectProperty|AttachedProperty)<[^>]*>\s+(\w+?)Property\b') | ForEach-Object {
        $ownNames[$_.Groups[1].Value] = $true
    }
}
Write-Host "本仓自定义属性（豁免）: $($ownNames.Count) 个" -ForegroundColor Gray

# ---------- 3) 常见 WPF → Avalonia 属性名对照（报错时给提示） ----------
$wpfHints = @{
    'SelectedValuePath' = 'Avalonia 的 ComboBox ⛔ 没有它 → 改绑 SelectedItem 到**选项对象**'
    'SelectedValue'     = '同上：Avalonia 用 SelectedItem'
    'StrokeLineJoin'    = 'Avalonia 叫 StrokeJoin'
    'DisplayMemberPath' = 'Avalonia ⛔ 没有它 → 用 ItemTemplate'
    'Watermark'         = 'Avalonia 用 PlaceholderText'
    'IsDefault'         = 'Avalonia ⛔ 没有它 → 用按钮的 IsDefault 替代方案（回车键命令）'
    'IsCancel'          = '同上'
    'SharedSizeGroup'   = 'Avalonia ⛔ 不支持 SharedSizeGroup'
    'ValidatesOnExceptions' = 'Avalonia ⛔ 没有它'
}

# ---------- 4) 扫全部 .axaml，提取属性名并核对 ----------
# XAML / 标记级名字（不是控件属性），跳过
$skip = @('xmlns','x','Classes','Name','Grid','Theme','ItemsPanel','ItemContainerTheme',
          'DataType','TargetType','Selector','BasedOn','Property','Value','Mode','Converter',
          'ConverterParameter','StringFormat','FallbackValue','RelativeSource','ElementName',
          'Source','Path','Priority','UpdateSourceTrigger','ReflectionBinding','CompiledBinding',
          'Binding','StaticResource','DynamicResource','Key','Class','Style','AvaloniaProperty')

$axamls = Get-ChildItem $src -Recurse -Filter '*.axaml' -File | Where-Object { $_.Name -notmatch '\.axaml\.cs$' }
Write-Host "`n[AXAML] 核对 $($axamls.Count) 个 .axaml 的属性名..." -ForegroundColor Cyan

$problems = 0
$checkedAttrs = 0
foreach ($f in $axamls) {
    # 先把 <!-- ... --> 注释整段**置空（保换行）**——注释里写 `Xxx = "yyy"` 不算属性用法；
    # 保换行是为了报错时行号仍然准确。`[regex]::Replace` 用 MatchEvaluator 逐字符换空格。
    $raw = Get-Content $f.FullName -Raw
    $clean = [regex]::Replace($raw, '(?s)<!--.*?-->', { param($mm) ($mm.Value -replace '[^\r\n]', ' ') })
    $lines = $clean -split "\r?\n"
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $ln = $lines[$i]
        # ⚠️ 后视排除 `:` —— 否则 `xmlns:views="..."` 会把前缀 views 当成属性名（实测误报源）
        foreach ($m in [regex]::Matches($ln, '(?<![\w.:-])([A-Za-z_][\w]*(?:\.[A-Za-z_][\w]*)*)\s*=\s*"')) {
            $attr = $m.Groups[1].Value
            if ($skip -contains $attr -or $attr.StartsWith('x:')) { continue }
            $checkedAttrs++
            foreach ($part in $attr.Split('.')) {
                if ($part.Length -lt 2) { continue }
                if ($ownNames.ContainsKey($part)) { continue }
                if ($corpus.ToString().Contains($part)) { continue }
                $hint = ''
                if ($wpfHints.ContainsKey($part)) { $hint = "  ← $($wpfHints[$part])" }
                $rel = $f.FullName.Substring($src.Length + 1)
                Write-Host "  [${rel}:$($i + 1)] 属性名 `"$attr`" 在 Avalonia 程序集里找不到$hint" -ForegroundColor Yellow
                $problems++
                break
            }
        }
    }
}

Write-Host "`n=== 汇总 ===" -ForegroundColor Cyan
Write-Host "  核对属性名出现次数 : $checkedAttrs"
Write-Host "  疑似 WPF 专有 / 拼错 : $problems"
if ($problems -eq 0) {
    Write-Host "`n✅ 全部属性名均在 Avalonia 程序集（或本仓自定义控件）中存在。" -ForegroundColor Green
} else {
    Write-Host "`n⚠️ 上面报出的名字多半是 WPF 专有写法（Avalonia 名字不同）或拼写错误 —— 编译期会抛 AVLN2000。" -ForegroundColor Yellow
}
Write-Host "`n提示：本脚本按**名字存在性**判定（弱于编译器），只用来在编译前抓 WPF/Avalonia 名字差异。" -ForegroundColor Gray
exit 0
