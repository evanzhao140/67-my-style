@echo off
cd /d "%~dp0"
echo Starting Plant Bridge...
if exist "publish\PlantBridge.exe" (
  "publish\PlantBridge.exe"
) else (
  dotnet run -c Release
)
pause
