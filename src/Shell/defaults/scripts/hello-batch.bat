@echo off
chcp 65001 >nul
rem NewToys sample script - Batch (.bat)
rem The script library runs it like a double-click, so a console window opens.
echo ==== NewToys sample script (Batch) ====
echo Time      : %DATE% %TIME%
echo Host      : %COMPUTERNAME%
echo User      : %USERNAME%
echo ScriptDir : %~dp0
echo.
echo Tip: edit this file, or drop your own .bat / .cmd here and use "Scan folder".
