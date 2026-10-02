param([Parameter(Mandatory = $true)][string]$InstallDirectory, [switch]$NoStart)
$ErrorActionPreference = 'Stop'
$taskName = 'AI.Client.Host'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$action = New-ScheduledTaskAction -Execute (Join-Path $InstallDirectory 'AI.Host.exe') -Argument '--public-web' -WorkingDirectory $InstallDirectory
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $identity
$principal = New-ScheduledTaskPrincipal -UserId $identity -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Seconds 0) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -StartWhenAvailable
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
if (-not $NoStart) { Start-ScheduledTask -TaskName $taskName }
