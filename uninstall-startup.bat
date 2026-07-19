@echo off
rem Removes the automatic-start shortcut created by install-startup.bat.
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Remove-Item -ErrorAction SilentlyContinue ([Environment]::GetFolderPath('Startup')+'\Projector Dashboard.lnk')"
echo Startup shortcut removed (if it existed).
pause
