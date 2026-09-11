$lines = Get-Content 'C:\Projects\DevTeam\AI.Client\src\AI.Client.Web\wwwroot\css\app.css'
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'workspace-changes|workspace-change\b|workspace-added|workspace-removed|workspace-change-counts|workspace-changes-note|workspace-changes-header|workspace-changes-title|workspace-changes-count|workspace-changes-totals') {
        $start = [Math]::Max(0, $i - 1)
        $end = [Math]::Min($lines.Count - 1, $i + 10)
        for ($j = $start; $j -le $end; $j++) {
            '{0,5}: {1}' -f ($j + 1), $lines[$j]
        }
        Write-Output '-----'
    }
}
