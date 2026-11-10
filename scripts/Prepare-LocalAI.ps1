#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RuntimeDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$RuntimeVersion,
    [ValidateRange(1024,65535)][int]$Port = 52176,
    [ValidateSet('MnnPocketQat','Qwen3')][string]$ModelPreset = 'Qwen3',
    [ValidateSet('win-x64','win-arm64','linux-x64','linux-arm64','osx-x64','osx-arm64')]
    [string]$RuntimeIdentifier = [Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
)
$ErrorActionPreference = 'Stop'
$runtimeRoot = (Resolve-Path -LiteralPath $RuntimeDirectory).Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if ($outputRoot.Equals($runtimeRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $outputRoot.StartsWith($runtimeRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be outside the runtime directory.' }
if (Test-Path -LiteralPath $outputRoot) { throw 'Choose a new output directory; installed packages are never overwritten.' }
$executable = if ($IsWindows) { 'llama-server.exe' } else { 'llama-server' }
if (-not (Test-Path -LiteralPath (Join-Path $runtimeRoot $executable))) { throw "Missing $executable in runtime directory." }
if ($ModelPreset -eq 'MnnPocketQat') {
    $modelName = 'MNN-Pocket-0.3B-Instruct-Q4_1.gguf'
    $modelHash = 'a281c127e388b16537fd3f5d31ab32d3f87739e6dc514ac1093c80d9eac8d097'
    $modelUrl = "https://huggingface.co/taobao-mnn/MNN-Pocket-0.3B-GGUF/resolve/791b7e92fa790a5ca5bbd7604feb1ce1d2429d14/$modelName"
    $licenseUrl = 'https://huggingface.co/taobao-mnn/MNN-Pocket-0.3B-GGUF/raw/791b7e92fa790a5ca5bbd7604feb1ce1d2429d14/LICENSE.txt'
    $modelAlias = 'mnn-pocket-0.3b-local'
    $connectionName = 'Local MNN-Pocket 0.3B Q4_1 QAT'
} else {
    $modelName = 'Qwen_Qwen3-0.6B-Q4_K_M.gguf'
    $modelHash = '9acfc1e001311f34b4252001b626f2e466d592a42065f66571bff3790d4e1b14'
    $modelUrl = "https://huggingface.co/bartowski/Qwen_Qwen3-0.6B-GGUF/resolve/main/$modelName"
    $licenseUrl = 'https://huggingface.co/Qwen/Qwen3-0.6B/raw/main/LICENSE'
    $modelAlias = 'qwen3-0.6b-local'
    $connectionName = 'Local Qwen3 0.6B'
}
New-Item -ItemType Directory -Path $outputRoot | Out-Null
$runtimeOutput = Join-Path $outputRoot 'runtime'
New-Item -ItemType Directory -Path $runtimeOutput | Out-Null
Get-ChildItem -LiteralPath $runtimeRoot | Copy-Item -Destination $runtimeOutput -Recurse
$modelPath = Join-Path $outputRoot $modelName
Write-Host "Downloading $modelName..."
Invoke-WebRequest -Uri ($modelUrl + '?download=true') -OutFile "$modelPath.partial" -TimeoutSec 300
if ((Get-FileHash -LiteralPath "$modelPath.partial" -Algorithm SHA256).Hash -ne $modelHash) {
    throw 'Model checksum mismatch. The incomplete package has no manifest and cannot be loaded.'
}
Move-Item -LiteralPath "$modelPath.partial" -Destination $modelPath
Invoke-WebRequest $licenseUrl -OutFile (Join-Path $outputRoot 'LICENSE-Model')
# Runtime distributions must retain their own license notices.
@{
    Executable = "runtime/$executable"; Model = $modelName; ModelSha256 = $modelHash
    Port = $Port; ContextTokens = 4096; RuntimeVersion = $RuntimeVersion; RuntimeIdentifier = $RuntimeIdentifier
    ModelSource = $modelUrl; ModelAlias = $modelAlias; ConnectionName = $connectionName
    DisableThinking = ($ModelPreset -eq 'Qwen3')
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputRoot 'manifest.json') -Encoding utf8
$bytes = (Get-ChildItem -LiteralPath $outputRoot -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host "Prepared optional component: $outputRoot ($bytes bytes including runtime)."
