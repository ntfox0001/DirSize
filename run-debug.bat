@echo off
setlocal

rem ============================================================
rem  Run DirSize (Debug) - does NOT build.
rem  Build first with build.bat if needed.
rem ============================================================

set "ROOT=%~dp0"
set "EXE=%ROOT%DirSizeWinUI\bin\x64\Debug\net8.0-windows10.0.19041.0\DirSize.exe"

if not exist "%EXE%" (
    echo   [FAIL] Debug output not found: %EXE%
    echo   Run build.bat Debug x64 first.
    pause
    exit /b 1
)

start "" "%EXE%"
exit /b 0