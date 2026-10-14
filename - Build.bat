@echo off
setlocal

title AudioPlugins - Build

set "PROJECT_ROOT=%~dp0"
set "RESULTS_LOG=%PROJECT_ROOT%Results.log"

rem Run the complete build and save all output to Results.log.
call :RUN_BUILD > "%RESULTS_LOG%" 2>&1
set "BUILD_RESULT=%ERRORLEVEL%"

rem Show the same output in the Command Prompt.
type "%RESULTS_LOG%"

echo.
echo Results saved to:
echo %RESULTS_LOG%
echo.
pause

exit /b %BUILD_RESULT%


:RUN_BUILD

echo.
echo ============================================================
echo  AudioPlugins SyMenu Plugin - Build
echo ============================================================
echo.

set "DOTNET_ROOT=%PROJECT_ROOT%..\_Tools\dotNet\_10.0.401"
set "DOTNET_EXE=%DOTNET_ROOT%\dotnet.exe"

rem Keep .NET CLI support files with the portable development tools.
set "DOTNET_CLI_HOME=%PROJECT_ROOT%..\_Tools\dotNet\_CliHome"

rem Avoid unnecessary first-run behaviour and telemetry.
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"

echo Portable .NET SDK:
echo %DOTNET_EXE%
echo.

if not exist "%DOTNET_EXE%" (
    echo [FAIL] Portable .NET SDK was not found.
    echo.
    echo Expected:
    echo %DOTNET_EXE%
    echo.
    echo ============================================================
    echo  BUILD FAILED
    echo ============================================================
    exit /b 1
)

if not exist "%PROJECT_ROOT%SyMenu.Plugin.AudioPlugins.csproj" (
    echo [FAIL] Project file was not found.
    echo.
    echo Expected:
    echo %PROJECT_ROOT%SyMenu.Plugin.AudioPlugins.csproj
    echo.
    echo ============================================================
    echo  BUILD FAILED
    echo ============================================================
    exit /b 1
)

echo Checking SDK...
"%DOTNET_EXE%" --version

if errorlevel 1 (
    echo.
    echo [FAIL] The portable .NET SDK could not be started.
    echo.
    echo ============================================================
    echo  BUILD FAILED
    echo ============================================================
    exit /b 1
)

echo.
echo Building plugin...
echo.

if exist "%PROJECT_ROOT%Build" rmdir /s /q "%PROJECT_ROOT%Build"

"%DOTNET_EXE%" build "%PROJECT_ROOT%SyMenu.Plugin.AudioPlugins.csproj" ^
    --configuration Release ^
    --output "%PROJECT_ROOT%Build"

if errorlevel 1 (
    echo.
    echo ============================================================
    echo  BUILD FAILED
    echo ============================================================
    exit /b 1
)

rem SyMenu supplies this DLL itself. It must not be distributed
rem alongside the plugin.
if exist "%PROJECT_ROOT%Build\SyMenu.Plugin.Resources.dll" (
    del /q "%PROJECT_ROOT%Build\SyMenu.Plugin.Resources.dll"
)

echo.
echo ============================================================
echo  BUILD SUCCESSFUL
echo ============================================================
echo.
echo Plugin:
echo %PROJECT_ROOT%Build\SyMenu.Plugin.AudioPlugins.dll
echo.

exit /b 0