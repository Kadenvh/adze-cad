@echo off
REM Adze for SOLIDWORKS — double-click uninstaller entry point.
REM Launches Adze.Manager.exe when bundled (preferred — UI uninstall flow).
REM Falls back to the PowerShell -Uninstall path for source checkouts.

setlocal

if exist "%~dp0Adze.Manager.exe" (
    start "" "%~dp0Adze.Manager.exe"
    exit /b 0
)

echo Uninstalling Adze for SOLIDWORKS...
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-adze.ps1" -Uninstall %*
set EXITCODE=%ERRORLEVEL%

if %EXITCODE% NEQ 0 (
    echo.
    echo Uninstall failed with exit code %EXITCODE%. Scroll up for details.
    echo.
    pause
    exit /b %EXITCODE%
)

echo.
pause
endlocal
