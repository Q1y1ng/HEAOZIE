@echo off
rem ============================================================
rem  HEAOZIE (HZ) self-test runner -- no GUI, safe to run any time.
rem  Writes logs\selftest.txt and returns the app's exit code (0 = pass).
rem  NOTE: keep this file pure ASCII (Chinese in a .cmd is parsed as GBK).
rem ============================================================
setlocal
cd /d "%~dp0"
if not exist "HZ.exe" (
  echo [HZ] HZ.exe not found -- run build.cmd first.
  exit /b 2
)
"HZ.exe" --selftest
set "RC=%ERRORLEVEL%"
echo.
echo [HZ] exit code: %RC%   report: %~dp0logs\selftest.txt
exit /b %RC%
