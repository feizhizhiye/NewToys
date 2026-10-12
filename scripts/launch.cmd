@echo off
setlocal
REM =================================================================
REM  NewToys 统一启动器 —— 解决「每个脚本都开一个终端、还得手动关」的痛点
REM
REM  用法:
REM    launch.cmd <脚本名(不含 .ps1)> [参数...]
REM  例:
REM    launch.cmd build
REM    launch.cmd build -Configuration Release -Runtime linux-x64
REM    launch.cmd run-NewToys -Configuration Release
REM    launch.cmd clean -Deep
REM
REM  行为:
REM    成功 -> pwsh 退出码 0 -> 本窗口自动关闭（不堆积）
REM    失败 -> pwsh 退出码非 0 -> 暂停等待查看，按任意键关闭
REM
REM  建议：为每个常用命令建一个指向本文件的快捷方式，
REM        目标形如  "C:\...\scripts\launch.cmd" build
REM =================================================================

if "%~1"=="" (
    echo 用法: launch.cmd ^<脚本名(不含 .ps1)^> [参数...]
    echo 例:   launch.cmd build
    echo       launch.cmd run-NewToys -Configuration Release
    echo       launch.cmd clean -Deep
    pause
    exit /b 1
)

set "SCRIPT=%~dp0%~1.ps1"
if not exist "%SCRIPT%" (
    echo 找不到脚本: %SCRIPT%
    pause
    exit /b 1
)

shift
pwsh -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" %*
set "RC=%errorlevel%"
if %RC% neq 0 (
    echo.
    echo [脚本以错误退出，退出码 %RC%；按任意键关闭窗口]
    pause >nul
)
exit /b %RC%
