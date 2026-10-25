[CmdletBinding()]
param([string]$OutputDirectory,[string]$DotnetPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$captureRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $DotnetPath) { $DotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $captureRepo ('artifacts\capture-worker-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$captureOutput = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $captureOutput) { throw 'Output directory already exists; use a new directory to preserve previous builds.' }
& $DotnetPath publish (Join-Path $captureRepo 'bridge\src\capture-worker\Inventor.So.Capture.Worker.csproj') -c Release -r win-x64 --self-contained false -o $captureOutput --nologo
if ($LASTEXITCODE -ne 0) { throw 'Capture worker publish failed.' }
$captureHashes = Get-ChildItem -LiteralPath $captureOutput -Recurse -File | ForEach-Object {
    [ordered]@{ path = [IO.Path]::GetRelativePath($captureOutput, $_.FullName); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
}
[ordered]@{
    build_utc = [DateTimeOffset]::UtcNow.ToString('o')
    runtime = 'win-x64; .NET 8; OpenCvSharp4 4.13.0.20260627'
    physical_validation = 'not performed'
    files = @($captureHashes)
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $captureOutput 'build-manifest.json') -Encoding utf8
Write-Output ('Worker built, not installed: ' + (Join-Path $captureOutput 'Inventor.So.Capture.Worker.dll'))
Write-Output 'Configure INVENTOR_SO_CAPTURE_WORKER on the experimental HTTP host to this absolute DLL path.'
