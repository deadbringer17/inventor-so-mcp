[CmdletBinding()]
param([string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$captureRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $EvidenceDirectory) { $EvidenceDirectory=Join-Path $captureRepo ('artifacts\reality-capture\rc2-pc-'+(Get-Date -Format 'yyyyMMdd-HHmmss')) }
$captureEvidence=[IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $captureEvidence) { throw 'Use a new evidence directory; previous evidence is immutable.' }
New-Item -ItemType Directory -Path $captureEvidence | Out-Null
& (Join-Path $PSScriptRoot 'build-capture-worker.ps1') -OutputDirectory (Join-Path $captureEvidence 'worker') *> (Join-Path $captureEvidence 'publish.log')
$capturePriorWorker=$env:XR_SO_TEST_CAPTURE_WORKER
$capturePriorEvidence=$env:XR_SO_CAPTURE_EVIDENCE_DIR
try {
    $env:XR_SO_TEST_CAPTURE_WORKER=Join-Path $captureEvidence 'worker\Inventor.So.Capture.Worker.dll'
    $env:XR_SO_CAPTURE_EVIDENCE_DIR=$captureEvidence
    foreach ($captureSuite in @(
        @{Name='worker'; Project='bridge\tests\Capture.Worker.Tests'},
        @{Name='backend'; Project='bridge\tests\Bimwright.Ipt.Tests'},
        @{Name='core'; Project='Inventor XR SO\Tests~\XrSo.Core.Tests'}
    )) {
        & dotnet test (Join-Path $captureRepo $captureSuite.Project) --logger ('trx;LogFileName='+$captureSuite.Name+'.trx') --results-directory $captureEvidence *> (Join-Path $captureEvidence ($captureSuite.Name+'.log'))
        if ($LASTEXITCODE -ne 0) { throw ('Failed suite: '+$captureSuite.Name+'. Inspect the saved log; no Quest run was performed.') }
        Write-Output ($captureSuite.Name+': PASS')
    }
}
finally {
    $env:XR_SO_TEST_CAPTURE_WORKER=$capturePriorWorker
    $env:XR_SO_CAPTURE_EVIDENCE_DIR=$capturePriorEvidence
}
Write-Output ('PC-only synthetic evidence: '+$captureEvidence)
