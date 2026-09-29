param(
    [Parameter(Mandatory = $true)][string]$InstallDirectory,
    [Parameter(Mandatory = $true)][string]$Executable,
    [string]$TaskName,
    [switch]$UnregisterTask
)

$ErrorActionPreference = 'Stop'

if ($TaskName) {
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($task -and $task.State -eq 'Running') {
        Stop-ScheduledTask -TaskName $TaskName -ErrorAction Stop
    }
}

$paths = @([IO.Path]::GetFullPath((Join-Path $InstallDirectory $Executable)))
if ($task) {
    foreach ($action in $task.Actions) {
        if ($action.Execute -and [IO.Path]::GetFileName($action.Execute) -eq $Executable) {
            $paths += [IO.Path]::GetFullPath($action.Execute)
        }
    }
}
$processes = @(Get-CimInstance Win32_Process -Filter "Name = '$Executable'" |
    Where-Object {
        $processPath = $_.ExecutablePath
        $processPath -and @($paths | Where-Object {
            [string]::Equals($_, $processPath, [StringComparison]::OrdinalIgnoreCase)
        }).Count -gt 0
    })

foreach ($process in $processes) {
    if ($Executable -eq 'AI.Desktop.exe') {
        $running = Get-Process -Id $process.ProcessId -ErrorAction SilentlyContinue
        if ($running -and -not $running.CloseMainWindow() -and
            (Get-Process -Id $process.ProcessId -ErrorAction SilentlyContinue)) {
            throw "Close AI Client Desktop before continuing. Process ID: $($process.ProcessId)."
        }
    } else {
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
    }
}

foreach ($process in $processes) {
    try {
        Wait-Process -Id $process.ProcessId -Timeout 15 -ErrorAction Stop
    } catch {
        if (Get-Process -Id $process.ProcessId -ErrorAction SilentlyContinue) {
            throw "$Executable did not stop. Process ID: $($process.ProcessId)."
        }
    }
}

if ($TaskName -and $UnregisterTask -and $task) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}
