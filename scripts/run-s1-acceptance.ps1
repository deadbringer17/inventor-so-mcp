#requires -Version 7.0
<# S1 incremental synthetic QA; owns fixture lifecycle and always restores the ordinary APK. #>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Apk,
    [Parameter(Mandatory)][string]$OrdinaryApk,
    [Parameter(Mandatory)][string]$Serial,
    [string]$OutDir = 'artifacts/s1-verification/device'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$s1Root = Split-Path -Parent $PSScriptRoot
Push-Location $s1Root
try {
    foreach ($s1File in @($Apk, $OrdinaryApk)) {
        if (-not (Test-Path -LiteralPath $s1File -PathType Leaf)) { throw "APK missing: $s1File" }
    }
    if (-not (Get-Command adb -ErrorAction SilentlyContinue)) { throw 'adb must be on PATH' }
    $s1Qa = (Resolve-Path -LiteralPath $Apk).Path
    $s1Ordinary = (Resolve-Path -LiteralPath $OrdinaryApk).Path
    $s1ExpectedHash = (Get-FileHash -LiteralPath $s1Ordinary -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($s1ExpectedHash -eq (Get-FileHash -LiteralPath $s1Qa -Algorithm SHA256).Hash.ToLowerInvariant()) {
        throw 'Acceptance and ordinary APK must be distinct builds.'
    }
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
    $s1Power = & adb -s $Serial shell dumpsys power
    $s1Awake = $LASTEXITCODE -eq 0 -and ($s1Power -join "`n") -match 'mWakefulness=Awake'
    [ordered]@{milestone='s1'; checkedUtc=[DateTime]::UtcNow.ToString('O'); serial=$Serial;
        phase='before_fixture_prepare'; deviceAwake=$s1Awake; fixturePrepared=$false; runnerStarted=$false;
        outcome=$(if($s1Awake){'READY'}else{'NOT_RUN_DEVICE_ASLEEP'})} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutDir 'preflight.json') -Encoding utf8
    if (-not $s1Awake) { throw 'Quest must be awake before creating the dedicated Inventor fixture.' }
    & dotnet build bridge/tests/QuestAcceptanceFixtures --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed' }
    $s1Prepared = $false
    $s1RunCode = 3
    $s1RestoreOk = $false
    $s1ApkRestored = $false
    try {
        & dotnet run --no-build --project bridge/tests/QuestAcceptanceFixtures -- --prepare-quest s1
        if ($LASTEXITCODE -ne 0) { throw 'Could not prepare dedicated S1 fixture' }
        $s1Prepared = $true
        Copy-Item -LiteralPath artifacts/s1-verification/quest-fixture.json -Destination (Join-Path $OutDir 'quest-fixture-s1.json')
        & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File (Join-Path $PSScriptRoot 'run-quest-acceptance.ps1') `
            -Milestone s1 -Apk $s1Qa -OrdinaryApk $s1Ordinary -Serial $Serial -OutDir $OutDir
        $s1RunCode = $LASTEXITCODE
    }
    finally {
        # Keep inspection, fixture restore and APK restore independent: one failure cannot skip the next.
        try {
            if ($s1Prepared) {
                & dotnet run --no-build --project bridge/tests/QuestAcceptanceFixtures -- --inspect-quest s1 2>&1 |
                    Tee-Object -FilePath (Join-Path $OutDir 'native-fixture-inspection.log')
                if ($LASTEXITCODE -ne 0) { $s1RunCode=3 }
            }
        } catch { $s1RunCode=3; Write-Warning "Native inspection failed: $_" }
        try {
            if ($s1Prepared) {
                & dotnet run --no-build --project bridge/tests/QuestAcceptanceFixtures -- --restore-quest s1 2>&1 |
                    Tee-Object -FilePath (Join-Path $OutDir 'fixture-restoration.log')
                $s1RestoreOk=$LASTEXITCODE -eq 0
                if (-not $s1RestoreOk) { $s1RunCode=3 }
            }
        } catch { $s1RunCode=3; Write-Warning "Fixture restoration failed: $_" }
        try {
            & adb -s $Serial install -r $s1Ordinary
            if ($LASTEXITCODE -ne 0) { throw 'Ordinary APK restoration failed' }
            $s1Remote = (& adb -s $Serial shell pm path com.occhipinti.inventorxrso |
                Where-Object { $_ -match '^package:.*/base\.apk' } | Select-Object -First 1) -replace '^package:', ''
            if ([string]::IsNullOrWhiteSpace($s1Remote)) { throw 'Restored APK path unavailable' }
            $s1Hash = & adb -s $Serial shell sha256sum $s1Remote.Trim()
            $s1ApkRestored=$LASTEXITCODE -eq 0 -and ($s1Hash -join '') -match "^$s1ExpectedHash\s"
            if (-not $s1ApkRestored) { throw 'Restored ordinary APK hash mismatch' }
        } catch { $s1RunCode=3; Write-Warning "APK restoration failed: $_" }
        [ordered]@{milestone='s1'; completedUtc=[DateTime]::UtcNow.ToString('O'); runnerExit=$s1RunCode;
            fixturePrepared=$s1Prepared; fixtureRestored=$s1RestoreOk; ordinaryApkRestored=$s1ApkRestored;
            ordinarySha256=$s1ExpectedHash; fullS1Complete=$false} |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutDir 'restoration.json') -Encoding utf8
    }
    exit $s1RunCode
}
finally { Pop-Location }
