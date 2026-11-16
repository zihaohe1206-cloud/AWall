@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" > "%~dp0build.log" 2>&1
if errorlevel 1 (
  echo === bin failed, retry bin-next === >> "%~dp0build.log"
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -OutputDirectory bin-next >> "%~dp0build.log" 2>&1
)
echo === exit %errorlevel% === >> "%~dp0build.log"
