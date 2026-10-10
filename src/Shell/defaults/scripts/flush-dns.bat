@echo off
rem ─────────────────────────────────────────────────────────────
rem  NewToys sample script (.bat -> run by cmd.exe)
rem  Flush the local DNS resolver cache.
rem  Keep this file ASCII-only: cmd reads .bat bytes using the
rem  active code page, so non-ASCII text may turn into mojibake.
rem ─────────────────────────────────────────────────────────────
chcp 65001 >nul
echo Flushing DNS cache ...
ipconfig /flushdns
if errorlevel 1 (
  echo [FAILED] ipconfig /flushdns returned an error.
  exit /b 1
)
echo [OK] DNS cache flushed.
