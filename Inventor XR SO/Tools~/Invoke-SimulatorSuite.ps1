#requires -Version 7.0
param(
    [string]$Output,
    [ValidateSet('m1','m2','m3','m4','m5','m6','m7','m8','m9','m9n','m9f','m9h','m10','s1')]
    [string[]]$Runners = @('m1','m2','m3','m4','m5','m6','m7','m8','m9','m9n','m9f','m9h','m10')
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $Output) { $Output = Join-Path $repoRoot ('artifacts/simulator-full/suite-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$Output = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$results = [Collections.Generic.List[object]]::new()
foreach ($runner in $Runners) {
    if (Test-Path -LiteralPath (Join-Path $Output 'stop.request')) { break }
    $runOutput = Join-Path $Output ($runner + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    @{ runner = $runner; state = 'running'; output = $runOutput } | ConvertTo-Json | Set-Content (Join-Path $Output 'progress.json')
    Write-Output "[SimulatorSuite] Starting $runner"
    & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File (Join-Path $PSScriptRoot 'Invoke-SimulatorLive.ps1') -AcceptanceRunner $runner -Output $runOutput
    $code = $LASTEXITCODE
    $cleanup = Get-Content (Join-Path $runOutput 'native-cleanup.json') -Raw | ConvertFrom-Json
    $evidence = $null
    $resultFile = Join-Path $runOutput 'runner-result.json'
    if (Test-Path -LiteralPath $resultFile) { $evidence = Get-Content $resultFile -Raw | ConvertFrom-Json }
    $results.Add([ordered]@{ runner = $runner; exit_code = $code; output = $runOutput; cleanup = $cleanup; evidence = $evidence })
    $results | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $Output 'results.json')
    Write-Output "[SimulatorSuite] Finished $runner exit=$code restored=$($cleanup.fixture_restored)"
    if ($cleanup.fixture_prepared -and -not $cleanup.fixture_restored) { throw 'Fixture restoration failed; suite stopped before touching another fixture.' }
}
$failed = @($results | Where-Object { $_.exit_code -ne 0 })
$partial = @($results | Where-Object { $_.evidence.verdict -like 'PARTIAL*' })
$withGaps = @($results | Where-Object { $_.evidence.not_covered.Count -gt 0 })
@{ state = $(if ($results.Count -lt $Runners.Count) { 'stopped' } else { 'finished' }); runs = $results.Count; failures = $failed.Count; partial_runs = $partial.Count;
    runs_with_not_covered = $withGaps.Count; coverage = 'Inspect every runner not_covered list; exit code 0 does not mean all gates are covered.';
    input = 'synthetic'; platform = 'Meta XR Simulator / Unity Editor'; physical_test = 'excluded' } |
    ConvertTo-Json | Set-Content (Join-Path $Output 'progress.json')
if ($failed.Count -gt 0) { exit 1 }
exit 0
