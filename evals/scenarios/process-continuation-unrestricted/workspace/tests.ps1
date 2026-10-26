$ErrorActionPreference = 'Stop'
[IO.File]::AppendAllText((Join-Path $PSScriptRoot 'launches.txt'), "1`n")
[Console]::WriteLine('TESTS_STARTED')
[Console]::WriteLine(('x' * 40000))
Start-Sleep -Seconds 75
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'result.txt'), 'TESTS_PASSED total=3')
[Console]::WriteLine('TESTS_PASSED total=3')
exit 0
