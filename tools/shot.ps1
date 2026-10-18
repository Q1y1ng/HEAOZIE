# 窗口截图（开发期自检：验证自绘 UI 是否正常）
#
# 用法： pwsh -NoProfile -File tools\shot.ps1 -ProcName HZ -Out logs\shot.png -Print
# 说明：先 SetProcessDPIAware 让坐标变成物理像素（否则 150% 缩放下截图会偏），
#       再找出该进程面积最大的可见顶层窗口，最后按窗口矩形截图（带 8px 边距）。
#       -Print 用 PrintWindow 渲染窗口自身像素：窗口被挡住/在托盘里也能拍到真实自绘内容。
# 来源：launch-center\tools\shot.ps1（同机同作者的成熟做法），改类名与默认值适配 HZ。

param(
    [string]$ProcName = "HZ",
    [string]$Out = "logs\shot.png",
    [int]$Margin = 8,
    [switch]$Print
)

Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class HzWin {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr lParam);

    /// <summary>找出进程里面积最大的可见顶层窗口（MainWindowHandle 实测会指向小隐藏窗）</summary>
    public static IntPtr LargestVisibleWindow(uint targetPid) {
        IntPtr best = IntPtr.Zero;
        long bestArea = 0;
        EnumWindows((h, l) => {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid != targetPid || !IsWindowVisible(h)) return true;
            if (GetWindowTextLength(h) == 0) return true;
            RECT r;
            if (!GetWindowRect(h, out r)) return true;
            long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
            if (area > bestArea) { bestArea = area; best = h; }
            return true;
        }, IntPtr.Zero);
        return best;
    }
}
"@

[void][HzWin]::SetProcessDPIAware()

$proc = Get-Process -Name $ProcName -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Error "没有找到进程：$ProcName"; exit 1 }

$h = [HzWin]::LargestVisibleWindow([uint32]$proc.Id)
if ($h -eq [IntPtr]::Zero) { Write-Error "进程 $($proc.Id) 没有可见的顶层窗口"; exit 1 }
[void][HzWin]::ShowWindow($h, 9)     # SW_RESTORE
[void][HzWin]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 700

$r = New-Object HzWin+RECT
if (-not [HzWin]::GetWindowRect($h, [ref]$r)) { Write-Error "GetWindowRect 失败"; exit 2 }

$w = $r.Right - $r.Left + $Margin * 2
$hh = $r.Bottom - $r.Top + $Margin * 2
if ($w -le 0 -or $hh -le 0) { Write-Error "窗口尺寸异常：$w x $hh"; exit 3 }

$bmp = New-Object System.Drawing.Bitmap $w, $hh
$g = [System.Drawing.Graphics]::FromImage($bmp)
if ($Print) {
    $hdc = $g.GetHdc()
    [void][HzWin]::PrintWindow($h, $hdc, 2)   # PW_RENDERFULLCONTENT
    $g.ReleaseHdc($hdc)
} else {
    $g.CopyFromScreen($r.Left - $Margin, $r.Top - $Margin, 0, 0, (New-Object System.Drawing.Size($w, $hh)))
}
$g.Dispose()

$dir = Split-Path -Parent $Out
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

Write-Output "OK: $Out  ($w x $hh)  window=$($proc.ProcessName)#$($proc.Id)"
