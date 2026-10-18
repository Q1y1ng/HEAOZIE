# 安装快捷方式：桌面 + 开始菜单（替换旧的 .bat / 失效 .lnk 入口）。
#
# 用法： pwsh -NoProfile -File tools\install-shortcut.ps1
#        pwsh -NoProfile -File tools\install-shortcut.ps1 -Remove     # 卸载快捷方式
#
# 只创建/删除本脚本自己管理的两个 .lnk，不动别的任何东西。

param(
    [switch]$Remove
)

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$exe = Join-Path $root "HZ.exe"
$desktop = [Environment]::GetFolderPath("Desktop")
$startMenu = Join-Path ([Environment]::GetFolderPath("Programs")) "HEAOZIE"
$links = @(
    (Join-Path $desktop "HEAOZIE 系统工具箱.lnk"),
    (Join-Path $startMenu "HEAOZIE 系统工具箱.lnk")
)

if ($Remove) {
    foreach ($l in $links) {
        if (Test-Path -LiteralPath $l) { Remove-Item -LiteralPath $l -Force; Write-Output "removed: $l" }
    }
    if (Test-Path -LiteralPath $startMenu) {
        $left = Get-ChildItem -LiteralPath $startMenu -Force
        if (-not $left) { Remove-Item -LiteralPath $startMenu -Force; Write-Output "removed dir: $startMenu" }
    }
    exit 0
}

if (-not (Test-Path -LiteralPath $exe)) {
    Write-Error "没有找到 $exe —— 先跑 build.cmd"
    exit 2
}

if (-not (Test-Path -LiteralPath $startMenu)) { New-Item -ItemType Directory -Path $startMenu | Out-Null }

$shell = New-Object -ComObject WScript.Shell
foreach ($l in $links) {
    $sc = $shell.CreateShortcut($l)
    $sc.TargetPath = $exe
    $sc.WorkingDirectory = $root
    $sc.Description = "HEAOZIE 系统工具箱（游戏加速 / 定时电源 / 定时关闭应用）"
    $sc.IconLocation = "$exe,0"
    $sc.Save()
    Write-Output "created: $l"
}
Write-Output "完成。托盘常驻：关窗不退出，退出用托盘右键菜单。"
