@echo off
setlocal enabledelayedexpansion

set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"

set "TARGET_EXE="
for %%P in (
    "bin\Debug\XenoCPUUtility.exe"
    "bin\Release\XenoCPUUtility.exe"
    "bin\Debug\net8.0-windows\XenoCPUUtility.exe"
    "bin\Release\net8.0-windows\XenoCPUUtility.exe"
    "bin\Debug\net7.0-windows\XenoCPUUtility.exe"
    "bin\Release\net7.0-windows\XenoCPUUtility.exe"
) do (
    set "CANDIDATE=%SCRIPT_DIR%\%%~P"
    if exist "!CANDIDATE!" (
        set "TARGET_EXE=!CANDIDATE!"
        goto :launch
    )
)

echo Could not find XenoCPUUtility.exe in the expected build folders.
echo Run build_XenoCPUUtility.bat first to create it.
goto :pause_and_exit

:launch
echo Launching XenoCPUUtility from:
echo   %TARGET_EXE%
start "XenoCPUUtility" "%TARGET_EXE%"

:pause_and_exit
echo.
echo Press any key to close this window.
pause >nul
endlocal
