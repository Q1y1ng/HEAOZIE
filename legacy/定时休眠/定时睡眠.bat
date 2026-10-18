@echo off
chcp 65001 >nul
cd /d "%~dp0"

set CMD=
where py >nul 2>nul && set CMD=py -3
if not defined CMD (
    where python3 >nul 2>nul && set CMD=python3
)
if not defined CMD (
    where python >nul 2>nul && set CMD=python
)
if not defined CMD (
    echo [ERR] Python 3 not found
    pause
    exit /b 1
)

start /b "" %CMD% "%~dp0sleep_timer.py" >nul 2>&1
exit
