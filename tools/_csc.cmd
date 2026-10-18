@echo off
rem ============================================================
rem  HZ (HEAOZIE) shared Roslyn csc.exe probe
rem  Adapted from launch-center / pc-keeper (same machine, same toolchain).
rem  Usage:  call tools\_csc.cmd   ->  sets CSC + HZREF
rem  Exit codes: 0 = found, 3 = not found
rem  NOTE: keep this file pure ASCII (Chinese in a .cmd is parsed as GBK by cmd.exe).
rem
rem  Why hand-compiled instead of a .csproj: this machine has no .NET SDK and
rem  needs none -- WinForms lives INSIDE .NET Framework 4.8, i.e. it is an OS
rem  component on Win10/11 -> zero installs, zero runtime dependency, small exe.
rem  csc only reads the reference metadata; the GAC resolves the real ones.
rem ============================================================
setlocal enabledelayedexpansion

set "CSC="
for %%P in (
  "%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe"
  "%ProgramFiles%\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\Roslyn\csc.exe"
  "%ProgramFiles%\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\Roslyn\csc.exe"
  "%ProgramFiles%\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
  "%ProgramFiles%\Microsoft Visual Studio\2022\Preview\MSBuild\Current\Bin\Roslyn\csc.exe"
  "%ProgramFiles(x86)%\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
) do (
  if not defined CSC if exist %%P set "CSC=%%~P"
)

rem vswhere fallback (ships with any VS 2017+ install / Build Tools)
if not defined CSC if defined ProgramFiles(x86) (
  for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.Roslyn.LanguageServices -property installationPath 2^>nul`) do (
    if not defined CSC if exist "%%i\MSBuild\Current\Bin\Roslyn\csc.exe" set "CSC=%%i\MSBuild\Current\Bin\Roslyn\csc.exe"
  )
)

if not defined CSC (
  echo [HZ] ERROR: Roslyn csc.exe not found.
  echo [HZ] Install Visual Studio Build Tools, or add its csc.exe path to tools\_csc.cmd.
  endlocal & exit /b 3
)

rem ============================================================
rem  Reference set -- SINGLE POINT OF TRUTH (build.cmd / self-test.cmd share it).
rem  System.Management is for WMI (hardware fallback); it is an OS assembly too.
rem ============================================================
set "FW4DIR=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
set "HZREF=-r:System.dll -r:System.Core.dll -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Management.dll"

rem The path above is unquoted, so a Windows directory containing a space would
rem silently split into bogus arguments. Fail loudly instead of mysteriously.
if not "%FW4DIR%"=="%FW4DIR: =%" (
  echo [HZ] ERROR: Windows directory contains a space; quote the paths in tools\_csc.cmd.
  endlocal & exit /b 3
)
if not exist "%FW4DIR%\System.Windows.Forms.dll" (
  echo [HZ] ERROR: WinForms assemblies not found under "%FW4DIR%".
  endlocal & exit /b 3
)

endlocal & set "CSC=%CSC%" & set "HZREF=%HZREF%"
exit /b 0
