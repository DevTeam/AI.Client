$ErrorActionPreference = 'Stop'
# The log is written two levels below out\ and it already exists, so appending to it moves
# only the log's own write time: a one-level count sees out\ sitting still, a two-level
# count sees the log moving. The writer keeps going long enough for both waits to finish.
$target = Join-Path $PSScriptRoot 'out\level1'
$null = New-Item -ItemType Directory -Force -Path $target
for ($i = 1; $i -le 100; $i++) {
    [IO.File]::AppendAllText((Join-Path $target 'build.log'), "line $i`n")
    Start-Sleep -Milliseconds 400
}
[Console]::WriteLine('WRITER_DONE')
exit 0
