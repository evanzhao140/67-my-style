@echo off
cd /d "%~dp0"
if exist "publish\PlantPulse.exe" (
  start "" "publish\PlantPulse.exe"
) else (
  dotnet run -c Release
)
