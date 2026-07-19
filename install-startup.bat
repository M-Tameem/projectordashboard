@echo off
rem Adds Dashboard.exe to the current user's Startup folder so it launches
rem automatically when Windows boots. Run from the extracted release folder.
setlocal
if not exist "%~dp0Dashboard.exe" (
    echo Dashboard.exe not found. Extract the complete release ZIP first.
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$s=(New-Object -ComObject WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Startup')+'\Projector Dashboard.lnk');" ^
  "$s.TargetPath='%~dp0Dashboard.exe';" ^
  "$s.WorkingDirectory='%~dp0';" ^
  "$s.Description='Bedside projector dashboard';" ^
  "$s.Save()"

if errorlevel 1 (
    echo Failed to create the startup shortcut.
    pause
    exit /b 1
)

echo Done. The dashboard will start automatically at every boot.
echo To undo this, run uninstall-startup.bat.
pause
