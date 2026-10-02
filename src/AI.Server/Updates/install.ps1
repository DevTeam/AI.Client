param([Parameter(Mandatory = $true)][string]$PlanPath)
$ErrorActionPreference = 'Stop'
$plan = Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
$success = $false
$taskStopped = $false
$logPath = Join-Path (Split-Path -LiteralPath $PlanPath) 'installer.log'
try {
    if ((Get-FileHash -LiteralPath $plan.Package -Algorithm SHA256).Hash -ne $plan.Sha256) {
        throw 'The update package failed its SHA-256 check.'
    }
    # The application has requested a normal shutdown. Never terminate active tasks here.
    $running = Get-Process -Id $plan.Pid -ErrorAction SilentlyContinue
    if ($running) { $running.WaitForExit(60000) | Out-Null }
    if (Get-Process -Id $plan.Pid -ErrorAction SilentlyContinue) { throw 'The application did not stop.' }
    if ($plan.Product -eq 'Host') {
        $task = Get-ScheduledTask -TaskName 'AI.Client.Host' -ErrorAction SilentlyContinue
        if ($task) { Disable-ScheduledTask -TaskName 'AI.Client.Host' | Out-Null; $taskStopped = $true }
    }
    $component = if ($plan.CSharp) { 'main,csharp' } else { 'main' }
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/SP-', '/NORESTART', '/UPDATE=1',
        "/DIR=`"$($plan.InstallDirectory)`"", "/COMPONENTS=`"$component`"", "/LOG=`"$logPath`"")
    $installer = Start-Process -FilePath $plan.Package -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    $success = $installer.ExitCode -eq 0
    if (-not $success) { throw "Installer exit code: $($installer.ExitCode)" }
} catch {
    $_ | Out-String | Add-Content -LiteralPath $logPath
} finally {
    @{ Success = $success; Version = $plan.Version } | ConvertTo-Json -Compress |
        Set-Content -LiteralPath ($plan.Result + '.tmp') -Encoding UTF8
    Move-Item -LiteralPath ($plan.Result + '.tmp') -Destination $plan.Result -Force
    if ($taskStopped) { Enable-ScheduledTask -TaskName 'AI.Client.Host' -ErrorAction SilentlyContinue | Out-Null }
    if (-not (Get-Process -Id $plan.Pid -ErrorAction SilentlyContinue)) {
        # Preserve the original arguments, including custom data directories and URLs.
        function Quote-Argument([string]$value) {
            '"' + [regex]::Replace([regex]::Replace($value, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1') + '"'
        }
        $restartArguments = @($plan.Arguments | ForEach-Object { Quote-Argument $_ })
        if ($plan.Product -eq 'Host' -and $plan.Arguments.Count -eq 1 -and $plan.Arguments[0] -eq '--public-web' -and
            (Get-ScheduledTask -TaskName 'AI.Client.Host' -ErrorAction SilentlyContinue)) {
            Start-ScheduledTask -TaskName 'AI.Client.Host'
        } elseif ($restartArguments.Count -gt 0) {
            Start-Process -FilePath $plan.Executable -ArgumentList $restartArguments -WorkingDirectory $plan.InstallDirectory -WindowStyle Hidden
        } else {
            Start-Process -FilePath $plan.Executable -WorkingDirectory $plan.InstallDirectory -WindowStyle Hidden
        }
    }
}
