#requires -Version 7.0
<# M9 uses only the dedicated M6 fixture (like M8). Always restore the user's document and the ordinary APK. #>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Apk,
    [Parameter(Mandatory)][string]$OrdinaryApk,
    [Parameter(Mandatory)][string]$Serial,
    [string]$OutDir = 'artifacts/m9-verification/device'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    foreach ($file in @($Apk, $OrdinaryApk)) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "APK missing: $file" }
    }
    if (-not (Get-Command adb -ErrorAction SilentlyContinue)) { throw 'adb must be on PATH' }
    $power = & adb -s $Serial shell dumpsys power
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
    $awake = $LASTEXITCODE -eq 0 -and ($power -join "`n") -match 'mWakefulness=Awake'
    [ordered]@{ milestone='m9'; serial=$Serial; checkedUtc=[DateTime]::UtcNow.ToString('o');
        phase='before_fixture_prepare'; deviceAwake=$awake; fixturePrepared=$false; runnerStarted=$false;
        outcome=$(if ($awake) { 'READY' } else { 'NOT_RUN_DEVICE_ASLEEP' }) } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutDir 'preflight.json') -Encoding utf8
    if (-not $awake) {
        throw 'Quest must be awake and worn before creating the Inventor fixture.'
    }
    & dotnet build bridge/tests/QuestAcceptanceFixtures
    if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed' }
    $prepared = $false
    $runCode = 3
    try {
        & dotnet run --no-build --project bridge/tests/QuestAcceptanceFixtures -- --prepare-quest m6
        if ($LASTEXITCODE -ne 0) { throw 'Could not prepare dedicated M6 fixture' }
        $prepared = $true
        Copy-Item -LiteralPath artifacts/m6-verification/quest-fixture.json -Destination (Join-Path $OutDir 'quest-fixture-m6.json')
        # Child process: run-quest-acceptance uses exit; it must not bypass this finally.
        & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File (Join-Path $PSScriptRoot 'run-quest-acceptance.ps1') `
            -Milestone m9 -Apk $Apk -OrdinaryApk $OrdinaryApk -Serial $Serial -OutDir $OutDir
        $runCode = $LASTEXITCODE
    }
    finally {
        $cleanupFailed = $false
        if ($prepared) {
            & dotnet run --no-build --project bridge/tests/QuestAcceptanceFixtures -- --inspect-quest m6 2>&1 | Tee-Object -FilePath (Join-Path $OutDir 'native-fixture-inspection.log')
            & dotnet run --no-build --project bridge/tests/QuestAcceptanceFixtures -- --restore-quest m6 2>&1 | Tee-Object -FilePath (Join-Path $OutDir 'fixture-restoration.log')
            if ($LASTEXITCODE -ne 0) { $cleanupFailed = $true; Write-Error 'Fixture restoration failed' -ErrorAction Continue }
        }
        # Also covers interruption/setup failure in the child before its own restoration step.
        & adb -s $Serial install -r $OrdinaryApk
        if ($LASTEXITCODE -ne 0) { $cleanupFailed = $true; Write-Error 'Ordinary APK restoration failed' -ErrorAction Continue }
        else {
            $remote = (& adb -s $Serial shell pm path com.occhipinti.inventorxrso | Where-Object { $_ -match '^package:.*/base\.apk' } | Select-Object -First 1) -replace '^package:', ''
            $deviceHash = & adb -s $Serial shell sha256sum $remote.Trim()
            $expected = (Get-FileHash -LiteralPath $OrdinaryApk -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($LASTEXITCODE -ne 0 -or ($deviceHash -join '') -notmatch "^$expected\s") {
                $cleanupFailed = $true; Write-Error 'Restored ordinary APK hash mismatch' -ErrorAction Continue
            }
        }
        if ($cleanupFailed) { $runCode = 3 }
    }
    exit $runCode
}
finally { Pop-Location }
