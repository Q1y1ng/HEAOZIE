@echo off
rem ============================================================
rem  HEAOZIE (HZ) build script
rem  Usage: build.cmd [output-name]      (default: HZ.exe)
rem  NOTE: keep this file pure ASCII. Chinese in a .cmd is parsed
rem        as GBK by cmd.exe and can break the script.
rem  Why an output name: when HZ.exe is already running it holds the file,
rem  so csc fails with CS2021/CS2012 "file is being used by another process".
rem  Pass another name (e.g. HZ.new.exe) to verify a build anyway -- the app
rem  derives its root from BaseDirectory, so a renamed build still runs.
rem ============================================================
setlocal
cd /d "%~dp0"

set "OUT=%~1"
if not defined OUT set "OUT=HZ.exe"

call "%~dp0tools\_csc.cmd"
if errorlevel 1 exit /b 3

rem Icon is optional: M0 ships without one (tray icon is drawn at runtime),
rem assets\hz.ico is picked up automatically once it exists.
set "ICON="
if exist "%~dp0assets\hz.ico" set "ICON=-win32icon:%~dp0assets\hz.ico"

echo [HZ] compiler: %CSC%
"%CSC%" -nologo -langversion:7.3 -target:winexe -optimize+ -platform:anycpu ^
  -out:%OUT% ^
  -win32manifest:"%~dp0src\app.manifest" ^
  %ICON% %HZREF% ^
  -recurse:"%~dp0src\*.cs"
if errorlevel 1 (
  echo [HZ] BUILD FAILED
  exit /b 1
)
echo [HZ] built: %OUT%
exit /b 0
