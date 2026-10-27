@echo off
rem Removes the "downloaded from the internet" marker from this folder so Windows SmartScreen
rem stops blocking SteamBuildIDManager.exe. It does not change any system security setting.
setlocal
echo Removing the mark-of-the-web from "%~dp0" ...
powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command "Get-ChildItem -LiteralPath '%~dp0' -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue"
echo Done. You can start SteamBuildIDManager.exe now.
pause