[Console]::WriteLine('TESTS_STARTED')
Start-Sleep -Seconds 120
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'unexpected-success.txt'), 'unexpected')
