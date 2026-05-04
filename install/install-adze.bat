@echo off
REM Adze for SOLIDWORKS — installer entry point.
REM Always runs install-adze.ps1. The Adze Manager GUI is a separate
REM post-install tool (launch Adze.Manager.exe directly to use it).
REM
REM Behavior is identical whether run from a source checkout or from
REM an extracted release zip — install-adze.ps1 auto-detects the mode.

setlocal

if not exist "%~dp0install-adze.ps1" (
    echo ERROR: install-adze.ps1 not found next to this script.
    echo Expected: %~dp0install-adze.ps1
    echo.
    pause
    exit /b 1
)

echo Installing Adze for SOLIDWORKS...
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-adze.ps1" %*
set EXITCODE=%ERRORLEVEL%

if %EXITCODE% NEQ 0 (
    echo.
    echo Install failed with exit code %EXITCODE%. Scroll up for details.
    echo.
    pause
    exit /b %EXITCODE%
)

echo.
echo Install complete. Launch Adze.Manager.exe to manage settings, view logs, or uninstall.
echo.
pause
endlocal
