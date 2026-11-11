#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://127.0.0.1:52176/v1',
    [Parameter(Mandatory)][string]$ApiKey,
    [string]$Model = 'qwen3.5-2b-local',
    [Parameter(Mandatory)][string]$ReportPath
)
$ErrorActionPreference = 'Stop'
$headers = @{ Authorization = "Bearer $ApiKey" }
$tools = @(
    @{ type='function'; function=@{ name='get_weather'; description='Get weather for a city.'; parameters=@{type='object';properties=@{city=@{type='string'}};required=@('city')} } },
    @{ type='function'; function=@{ name='read_file'; description='Read a UTF-8 workspace file.'; parameters=@{type='object';properties=@{path=@{type='string'}};required=@('path')} } },
    @{ type='function'; function=@{ name='list_directory'; description='List entries in a workspace directory.'; parameters=@{type='object';properties=@{path=@{type='string'}};required=@('path')} } },
    @{ type='function'; function=@{ name='search_files'; description='Search text in workspace files.'; parameters=@{type='object';properties=@{query=@{type='string'};directory=@{type='string'}};required=@('query','directory')} } },
    @{ type='function'; function=@{ name='set_filter'; description='Set a named task filter.'; parameters=@{type='object';properties=@{filter=@{type='object';properties=@{status=@{type='string';enum=@('open','closed')};limit=@{type='integer'}};required=@('status','limit')}};required=@('filter')} } }
)
$cases = @(
    @{id='weather-en';prompt='Get the weather in Moscow.';name='get_weather';expected=@{city='Moscow'}},
    @{id='weather-ru';prompt='Узнай погоду в городе Moscow.';name='get_weather';expected=@{city='Moscow'}},
    @{id='read-en';prompt='Read the workspace file docs/README.md.';name='read_file';expected=@{path='docs/README.md'}},
    @{id='read-ru';prompt='Прочитай файл рабочего каталога docs/README.md.';name='read_file';expected=@{path='docs/README.md'}},
    @{id='list-en';prompt='List the entries in the src directory.';name='list_directory';expected=@{path='src'}},
    @{id='list-ru';prompt='Покажи список файлов каталога src.';name='list_directory';expected=@{path='src'}},
    @{id='search-en';prompt='Search for TODO in directory src.';name='search_files';expected=@{query='TODO';directory='src'}},
    @{id='filter-ru';prompt='Установи фильтр задач: status open, limit 5.';name='set_filter';expected=@{filter=@{status='open';limit=5}}},
    @{id='no-tool-en';prompt='Reply with the word Hello. No tools are needed.';name=$null;expected=@{}},
    @{id='missing-path-ru';prompt='Прочитай файл. Сначала спроси у меня путь; пока не вызывай инструменты.';name=$null;expected=@{}}
)
function Test-Expected($actual, $expected) {
    if ($expected -is [System.Collections.IDictionary]) {
        if ($null -eq $actual -or $actual -is [array]) { return $false }
        if (@($actual.PSObject.Properties.Name).Count -ne $expected.Count) { return $false }
        foreach ($key in $expected.Keys) {
            if (-not (Test-Expected $actual.$key $expected[$key])) { return $false }
        }
        return $true
    }
    return "$actual" -ceq "$expected"
}
$results = @()
foreach ($case in $cases) {
    foreach ($seed in 1..5) {
        $body = @{model=$Model;messages=@(@{role='user';content=$case.prompt});tools=$tools;stream=$false;max_tokens=192;temperature=0.8;top_p=0.8;seed=$seed}
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $reply = Invoke-RestMethod "$BaseUrl/chat/completions" -Method Post -Headers $headers -ContentType 'application/json' -TimeoutSec 60 -Body ($body | ConvertTo-Json -Depth 20)
        $calls = @($reply.choices[0].message.tool_calls | Where-Object { $null -ne $_ })
        $passed = $false
        if ($null -eq $case.name) { $passed = $calls.Count -eq 0 -and -not [string]::IsNullOrWhiteSpace($reply.choices[0].message.content) }
        elseif ($calls.Count -eq 1 -and $calls[0].function.name -eq $case.name) {
            try { $passed = Test-Expected ($calls[0].function.arguments | ConvertFrom-Json) $case.expected } catch { $passed = $false }
        }
        $results += [pscustomobject]@{Case=$case.id;Seed=$seed;Passed=$passed;Seconds=$timer.Elapsed.TotalSeconds;Request=$body;Response=$reply}
        Write-Host "$($case.id) seed=$seed passed=$passed"
        # Keep partial results if a later request fails or is cancelled.
        $results | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $ReportPath -Encoding utf8
    }
}
$results | Group-Object Case | ForEach-Object { [pscustomobject]@{Case=$_.Name;Passed=@($_.Group | Where-Object Passed).Count;Total=$_.Count} } | Format-Table
