# 游戏性能加速器 — PowerShell 启动脚本
# 右键 → 用 PowerShell 运行，或用 bat 启动

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ScriptPath = Join-Path $ScriptDir "gaming_booster.py"

# 寻找 Python 3
$python = $null

# 1. 尝试 py (Python Launcher)
$pyVer = & py -3 --version 2>$null
if ($LASTEXITCODE -eq 0) {
    $python = "py -3"
    Write-Host "使用 Python Launcher (py -3)" -ForegroundColor Green
}

# 2. 尝试 python3
if (-not $python) {
    $py3Ver = & python3 --version 2>$null
    if ($LASTEXITCODE -eq 0) {
        $python = "python3"
        Write-Host "使用 python3" -ForegroundColor Green
    }
}

# 3. 尝试 python (需确认是 Python 3)
if (-not $python) {
    $pyVer = & python --version 2>$null
    if ($LASTEXITCODE -eq 0 -and $pyVer -match "3\.") {
        $python = "python"
        Write-Host "使用 python" -ForegroundColor Green
    }
}

if (-not $python) {
    Write-Host "错误: 找不到 Python 3！请先安装 Python 3" -ForegroundColor Red
    Read-Host "按 Enter 退出"
    exit 1
}

# 检查 psutil
$psutilCheck = & $python -c "import psutil" 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "正在安装 psutil..." -ForegroundColor Yellow
    & $python -m pip install psutil -q
    if ($LASTEXITCODE -ne 0) {
        Write-Host "错误: psutil 安装失败" -ForegroundColor Red
        Read-Host "按 Enter 退出"
        exit 1
    }
}

# 启动 GUI 程序（隐藏 PowerShell 窗口）
Write-Host "启动游戏性能加速器..." -ForegroundColor Green
$startArgs = "-WindowStyle Hidden -File `"$ScriptPath`""

# 启动新进程，隐藏窗口
Start-Process -FilePath "python3" -ArgumentList "`"$ScriptPath`"" -WindowStyle Hidden
Start-Sleep -Seconds 1

# 检查是否启动成功
$proc = Get-Process | Where-Object { $_.ProcessName -eq "python" -or $_.ProcessName -eq "python3" }
if ($proc) {
    Write-Host "启动成功！" -ForegroundColor Green
    Start-Sleep -Seconds 1
} else {
    Write-Host "尝试直接运行查看错误..." -ForegroundColor Yellow
    & $python "`"$ScriptPath`""
}
