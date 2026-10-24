# Launch a process inside a Windows Job Object with a hard CPU-rate cap.
#
# Why a job object: Set-Priority / affinity only change *scheduling*, they do not
# bound CPU. JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP makes the OS throttle the whole
# process tree to N% of the machine, which is what "别超过 40%" actually means.
#
# Usage:
#   powershell -File cpu_cap.ps1 -Percent 40 -FilePath .\engine2.exe -ArgumentList '--size 4 --games 3 --budget 100'
#   powershell -File cpu_cap.ps1 -Percent 40 -Command 'py -3 play_engine.py --size 5 --games 2 --budget 200'
#
# Notes:
#   - Windows 8+ required (CPU rate control).
#   - The cap applies to the process and every child it spawns.
#   - Percent is of the whole machine (40 = 40% of total CPU capacity).
param(
    [int]$Percent = 40,
    [string]$Exe = $null,
    [string[]]$ExeArgs = @(),
    [string]$Cmd = $null,
    [switch]$PassThru
)

$ErrorActionPreference = 'Stop'
if ($Percent -lt 1 -or $Percent -gt 100) { throw "Percent must be 1..100" }

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class CpuRateJob {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int infoClass, IntPtr lpInfo, int cbInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_CPU_RATE_CONTROL_INFORMATION {
        public UInt32 ControlFlags;
        public UInt32 CpuRate;
    }

    // CLASS = JobObjectCpuRateControlInformation (15)
    // ControlFlags: 0x1 = JOB_OBJECT_CPU_RATE_CONTROL_ENABLE, 0x2 = HARD_CAP
    // (HARD_CAP is rejected with ERROR_INVALID_PARAMETER on some Windows builds;
    //  fall back to ENABLE-only rate control, which still enforces the average.)
    public static string CreateAndAssign(IntPtr processHandle, int percent) {
        IntPtr job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        string mode;
        if (!SetRate(job, 0x1 | 0x2, percent)) {
            if (!SetRate(job, 0x1, percent))
                throw new System.ComponentModel.Win32Exception();
            mode = "rate-control(avg)";
        } else {
            mode = "hard-cap";
        }
        if (!AssignProcessToJobObject(job, processHandle))
            throw new System.ComponentModel.Win32Exception();
        return mode;
    }

    private static bool SetRate(IntPtr job, uint flags, int percent) {
        var info = new JOBOBJECT_CPU_RATE_CONTROL_INFORMATION();
        info.ControlFlags = flags;
        info.CpuRate = (UInt32)(percent * 100);      // 1/100 of a percent
        IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf(info));
        try {
            Marshal.StructureToPtr(info, p, false);
            return SetInformationJobObject(job, 15, p, Marshal.SizeOf(info));
        } finally {
            Marshal.FreeHGlobal(p);
        }
    }
}
"@

function Start-Capped {
    param([string]$Exe, [string[]]$CliArgs, [int]$Pct)
    $p = Start-Process -FilePath $Exe -ArgumentList $CliArgs -PassThru -NoNewWindow
    $script:CapMode = [CpuRateJob]::CreateAndAssign($p.Handle, $Pct)
    # BelowNormal keeps foreground games responsive even inside the cap
    try { $p.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::BelowNormal } catch {}
    return $p
}

if ($Cmd) {
    $p = Start-Capped -Exe 'powershell' -CliArgs @('-NoProfile', '-Command', $Cmd) -Pct $Percent
} elseif ($Exe) {
    $p = Start-Capped -Exe $Exe -CliArgs $ExeArgs -Pct $Percent
} else {
    throw "give -Exe ... -ExeArgs ... or -Cmd ..."
}

Write-Host ("started pid={0} with CPU cap {1}% via job object ({2})" -f $p.Id, $Percent, $script:CapMode)
# Keep the job handle open for the child's whole life: when the last job handle
# closes, Windows terminates every process still inside the job.
$p | Wait-Process
exit $p.ExitCode

