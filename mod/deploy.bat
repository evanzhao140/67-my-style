@echo off
REM Build output -> Modrinth App profile "267" (canonical deploy target).
set "SRC=%~dp0build\libs\plantapi-1.0.0.jar"
set "DST=%APPDATA%\ModrinthApp\profiles\267\mods"
if not exist "%SRC%" (
  echo Build output not found: %SRC%
  echo Run first: gradlew.bat --no-daemon build
  pause
  exit /b 1
)
copy /Y "%SRC%" "%DST%\plantapi-1.0.0.jar"
echo.
echo Deployed plantapi-1.0.0.jar -^> %DST%
pause
