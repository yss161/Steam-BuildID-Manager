@echo off
rem Starts the bundled Inno Setup installer with a writable temp folder.
rem Inno extracts itself into %TEMP% before any installer code runs, so a locked-down
rem temp folder fails with "Error 5: Access is denied". This wrapper picks a working
rem folder first; the user still sees the normal installation wizard.
setlocal
set "SCRIPT=%~dp0Start-Setup.ps1"
if not exist "%SCRIPT%" (
  echo Start-Setup.ps1 was not found next to this file.
  pause
  exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%SCRIPT%" -Elevate %*
set "CODE=%ERRORLEVEL%"
if not "%CODE%"=="0" pause
exit /b %CODE%
