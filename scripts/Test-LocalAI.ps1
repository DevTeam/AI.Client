#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://127.0.0.1:52176/v1',
    [Parameter(Mandatory)][string]$ApiKey,
    [string]$Model = 'qwen3.5-2b-local',
    [string]$Prompt = 'Use get_weather to get the weather in Moscow.',
    [ValidateSet('auto','required')][string]$ToolChoice = 'auto',
    [int]$Seed = 1
)
$ErrorActionPreference = 'Stop'
$headers = @{ Authorization = "Bearer $ApiKey" }
$tool = @{ type = 'function'; function = @{
    name = 'get_weather'; description = 'Get the weather for a city.'
    parameters = @{ type = 'object'; properties = @{ city = @{ type = 'string' } }; required = @('city') }
} }
$messages = @(@{ role = 'user'; content = $Prompt })
$clock = [Diagnostics.Stopwatch]::StartNew()
$reply = Invoke-RestMethod "$BaseUrl/chat/completions" -Method Post -Headers $headers -ContentType 'application/json' -Body (@{
    model = $Model; messages = $messages; tools = @($tool); stream = $false
    max_tokens = 128; tool_choice = $ToolChoice; seed = $Seed; temperature = 0.8; top_p = 0.8
} | ConvertTo-Json -Depth 20)
$elapsed = $clock.Elapsed.TotalSeconds
if (-not $reply.choices[0].message.tool_calls) {
    throw 'The endpoint returned no structured tool_calls. Text containing JSON does not pass this protocol check.'
}
$call = $reply.choices[0].message.tool_calls[0]
if ($call.function.name -ne 'get_weather') { throw 'Expected a structured get_weather tool call.' }
$arguments = $call.function.arguments | ConvertFrom-Json
if ($arguments.city -notmatch 'Moscow|Москва') { throw 'Wrong city in tool arguments.' }
$messages += $reply.choices[0].message
$messages += @{ role = 'tool'; tool_call_id = $call.id; content = '{"temperature_c":12,"condition":"rain"}' }
$answer = Invoke-RestMethod "$BaseUrl/chat/completions" -Method Post -Headers $headers -ContentType 'application/json' -Body (@{
    model = $Model; messages = $messages; tools = @($tool); stream = $false; max_tokens = 128
} | ConvertTo-Json -Depth 20)
if (-not $answer.choices[0].message.content) { throw 'No final answer after the tool result.' }
[pscustomobject]@{ ToolCallSeconds = $elapsed; TotalSeconds = $clock.Elapsed.TotalSeconds; Answer = $answer.choices[0].message.content }
