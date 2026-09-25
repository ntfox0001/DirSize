@echo off
setlocal

rem ============================================================
rem  One-click build for DirSize (WinUI3)
rem  Usage: build.bat [Config] [Platform]
rem    Config  : Debug / Release, default Release
rem    Platform: x64, default x64
rem ============================================================

set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Release"
set "PLATFORM=%~2"
if "%PLATFORM%"=="" set "PLATFORM=x64"

set "ROOT=%~dp0"

echo ============================================================
echo   Build config : %CONFIG%  (%PLATFORM%)
echo ============================================================

dotnet build "%ROOT%DirSizeWinUI\DirSizeWinUI.csproj" -c %CONFIG% -p:Platform=%PLATFORM%
if errorlevel 1 goto :fail

echo.
echo   [OK] Build succeeded: %CONFIG% (%PLATFORM%)
echo        Output: DirSizeWinUI\bin\%PLATFORM%\%CONFIG%\net8.0-windows10.0.19041.0\
exit /b 0

:fail
echo.
echo   [FAIL] Build failed, check errors above.
pause
exit /b 1