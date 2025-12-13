@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PROJECT_FILE=%SCRIPT_DIR%XenoCPUUtility.csproj"

if not exist "%PROJECT_FILE%" (
    echo Could not locate XenoCPUUtility.csproj next to this script.
    goto :pause_and_exit
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo The dotnet CLI is not available on PATH.
    echo Install the .NET SDK from https://dotnet.microsoft.com/ or open a Developer Command Prompt.
    goto :pause_and_exit
)

echo Restoring and building using dotnet...
dotnet build "%PROJECT_FILE%" -c Release %*
if errorlevel 1 (
    echo.
    echo Build failed. Review the errors above.
    goto :pause_and_exit
)

echo.
echo Build succeeded. Output binaries are under bin\Release.

:pause_and_exit
echo.
echo Press any key to close this window.
pause >nul
endlocal
