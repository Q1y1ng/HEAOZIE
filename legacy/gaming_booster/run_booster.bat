@echo off
chcp 65001 >nul
cd /d "%~dp0"

REM 寻找 Python 3 — 优先用 py (Python Launcher, Windows 标准)
set CMD=
where py >nul 2>nul && set CMD=py -3
if not defined CMD (
    where python3 >nul 2>nul && set CMD=python3
)
if not defined CMD (
    where python >nul 2>nul && set CMD=python
)
if not defined CMD (
    echo [错误] 找不到 Python 3，请先安装 Python 3
    pause
    exit /b 1
)

REM 检查/安装 psutil
%CMD% -c "import psutil" 2>nul
if errorlevel 1 (
    echo [安装] psutil 模块（仅首次运行需要）...
    %CMD% -m pip install psutil -q
)

REM 启动 GUI 程序（隐藏控制台窗口）
echo 启动中...
start /b "" %CMD% "%~dp0gaming_booster.py"
timeout /t 2 /nobreak >nul

REM 检查是否运行成功
tasklist /fi "IMAGENAME eq python.exe" 2>nul | find /i "python.exe" >nul
if errorlevel 1 (
    tasklist /fi "IMAGENAME eq python3.exe" 2>nul | find /i "python3.exe" >nul
    if errorlevel 1 (
        echo [警告] 启动似乎异常，直接运行查看错误...
        %CMD% "%~dp0gaming_booster.py"
        pause
        exit /b 1
    )
)
echo 游戏性能加速器已启动！
