#Requires -Version 5.1
<#
.SYNOPSIS
  NewToys · Python 环境准备（NT-083）—— 建共享 venv 并安装规则集依赖。

.DESCRIPTION
  幂等：venv 已存在就只补装依赖；依赖已满足时 pip 会秒退。
  环境位置：%LOCALAPPDATA%\NewToys\tools\py\venv  （与 env.json 同级，⛔ 不进 profiles\）
  脚本本身不带任何 C# 逻辑 —— 只有这个文件 + python-requirements.txt。

.PARAMETER PythonExe
  基础解释器路径。留空 = 依次试 PATH 里的 python / python3。

.PARAMETER Force
  删掉旧 venv 重建（依赖装崩了、或换了 Python 大版本时用）。

.PARAMETER IndexUrl
  pip 镜像源。留空 = pip 默认（国内建议 https://pypi.tuna.tsinghua.edu.cn/simple）。

.EXAMPLE
  pwsh -File .\scripts\setup-python-env.ps1
  pwsh -File .\scripts\setup-python-env.ps1 -IndexUrl https://pypi.tuna.tsinghua.edu.cn/simple
  pwsh -File .\scripts\setup-python-env.ps1 -Force
#>
[CmdletBinding()]
param(
    [string]$PythonExe = "",
    [switch]$Force,
    [string]$IndexUrl = ""
)

$ErrorActionPreference = "Stop"

function Write-Step { param([string]$Text) Write-Host "==> $Text" -ForegroundColor Cyan }
function Write-Ok   { param([string]$Text) Write-Host "  OK  $Text" -ForegroundColor Green }
function Fail       { param([string]$Text) Write-Host "  !!  $Text" -ForegroundColor Red; exit 1 }

# ---------- 路径 ----------
if (-not $env:LOCALAPPDATA) { Fail "LOCALAPPDATA 为空，无法确定环境目录。" }
$root = Join-Path $env:LOCALAPPDATA "NewToys\tools\py"
$venv = Join-Path $root "venv"
$vpy  = Join-Path $venv "Scripts\python.exe"
$req  = Join-Path $PSScriptRoot "python-requirements.txt"

if (-not (Test-Path $req)) { Fail "找不到依赖清单：$req" }

# ---------- 找基础解释器 ----------
function Resolve-Python {
    param([string]$Explicit)
    if ($Explicit) {
        if (-not (Test-Path $Explicit)) { Fail "指定的解释器不存在：$Explicit" }
        return $Explicit
    }
    foreach ($name in @("python", "python3")) {
        $cmd = Get-Command $name -ErrorAction SilentlyContinue
        if ($cmd) { return $cmd.Source }
    }
    return $null
}

Write-Step "准备环境目录 $root"
New-Item -ItemType Directory -Force -Path $root | Out-Null

if ($Force -and (Test-Path $venv)) {
    Write-Step "删除旧 venv（-Force）"
    Remove-Item -Recurse -Force $venv
}

# ---------- 建 venv ----------
if (Test-Path $vpy) {
    Write-Ok "venv 已存在，跳过创建"
} else {
    $base = Resolve-Python $PythonExe
    if (-not $base) {
        Fail "未找到 Python。请装 Python 3.10+ 并勾选 Add to PATH，或用 -PythonExe 指定路径。"
    }
    $ver = & $base -c "import sys;print('%d.%d' % sys.version_info[:2])" 2>&1
    if ($LASTEXITCODE -ne 0) { Fail "无法运行 $base ：$ver" }
    Write-Step "基础解释器 $base（Python $ver）"
    if ([version]$ver -lt [version]"3.10") { Fail "需要 Python 3.10+，当前 $ver。" }

    Write-Step "创建 venv → $venv"
    & $base -m venv $venv
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $vpy)) { Fail "venv 创建失败（退出码 $LASTEXITCODE）。" }
    Write-Ok "venv 创建完成"
}

# ---------- 装依赖 ----------
$pipArgs = @("-m", "pip", "install", "--upgrade", "pip", "--disable-pip-version-check", "--no-input")
if ($IndexUrl) { $pipArgs += @("-i", $IndexUrl) }
Write-Step "升级 pip"
& $vpy @pipArgs
if ($LASTEXITCODE -ne 0) { Write-Host "  (pip 自升级失败，继续安装依赖)" -ForegroundColor Yellow }

$installArgs = @("-m", "pip", "install", "-r", $req, "--disable-pip-version-check", "--no-input")
if ($IndexUrl) { $installArgs += @("-i", $IndexUrl) }
Write-Step "安装规则集依赖（$req）"
& $vpy @installArgs
if ($LASTEXITCODE -ne 0) {
    Fail "依赖安装失败。可尝试：① 加 -IndexUrl 换镜像；② 加 -Force 重建 venv；③ 检查网络/代理。"
}

# ---------- 自检 ----------
Write-Step "自检"
& $vpy -c "import sys, requests, lxml, parsel; print('python', sys.version.split()[0]); print('requests', requests.__version__); print('lxml', lxml.__version__); print('parsel', parsel.__version__)"
if ($LASTEXITCODE -ne 0) { Fail "依赖自检失败（上面有 import 报错）。" }

Write-Host ""
Write-Ok "环境就绪"
Write-Host "  解释器：$vpy" -ForegroundColor Gray
Write-Host "  如软件未自动识别，请在「设置 → 运行环境 → Python 解释器」填上面的路径。" -ForegroundColor Gray
