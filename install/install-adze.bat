@echo off
REM Adze for SOLIDWORKS — double-click installer entry point.
REM Launches Adze.Manager.exe when bundled alongside this script (release zip).
REM Falls back to the PowerShell installer when running from a source checkout.

setlocal

if exist "%~dp0Adze.Manager.exe" (
    start "" "%~dp0Adze.Manager.exe"
    exit /b 0
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
pause
endlocal
