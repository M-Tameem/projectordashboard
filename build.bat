@echo off
rem ============================================================
rem  Builds Dashboard.exe using the C# compiler that ships with
rem  Windows 8.1 itself (.NET Framework 4.x). No Visual Studio,
rem  no SDK, no internet connection required.
rem  Run this once on the tablet, from this folder.
rem ============================================================
setlocal
set DASHBOARD_CI=
if /I "%~1"=="--ci" set DASHBOARD_CI=1

set FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319
if not exist "%FW%\csc.exe" (
    echo Could not find the .NET compiler at %FW%\csc.exe
    echo This script must run on Windows with .NET Framework 4.x installed.
    if not defined DASHBOARD_CI pause
    exit /b 1
)

echo Building Dashboard.exe ...
"%FW%\csc.exe" /nologo /target:winexe /platform:x86 /optimize+ /warn:1 /out:Dashboard.exe ^
  /r:"%FW%\System.dll" ^
  /r:"%FW%\System.Core.dll" ^
  /r:"%FW%\System.Xml.dll" ^
  /r:"%FW%\System.Web.Extensions.dll" ^
  /r:"%FW%\System.Xaml.dll" ^
  /r:"%FW%\System.Management.dll" ^
  /r:"%FW%\System.Windows.Forms.dll" ^
  /r:"%FW%\System.Drawing.dll" ^
  /r:"%FW%\WPF\WindowsBase.dll" ^
  /r:"%FW%\WPF\PresentationCore.dll" ^
  /r:"%FW%\WPF\PresentationFramework.dll" ^
  src\*.cs

if errorlevel 1 (
    echo.
    echo Build FAILED. See errors above.
    if not defined DASHBOARD_CI pause
    exit /b 1
)

echo.
echo Build OK: %CD%\Dashboard.exe
echo Next step: run install-startup.bat to launch it automatically at boot,
echo or just double-tap Dashboard.exe to try it now.
if not defined DASHBOARD_CI pause
