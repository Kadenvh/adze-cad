@echo off
REM Adze for SOLIDWORKS — uninstaller entry point.
REM Always runs install-adze.ps1 -Uninstall. Use Adze.Manager.exe directly
REM if you want the GUI uninstall flow (with the user-data prompt).
REM
REM Behavior is identical whether run from a source checkout or from
REM an extracted release zip.

setlocal

if not exist "%~dp0install-adze.ps1" (
    echo ERROR: install-adze.ps1 not found next to this script.
    echo Expected: %~dp0install-adze.ps1
    echo.
    pause
    exit /b 1
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
