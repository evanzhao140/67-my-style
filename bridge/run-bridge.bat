@echo off
cd /d "%~dp0"
echo Starting MCVibe Bridge...
if exist "publish\MCVibeBridge.exe" (
  "publish\MCVibeBridge.exe"
) else (
  dotnet run -c Release
)
pause
