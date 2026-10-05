#requires -Version 7.0
<#
Runs a Quest acceptance APK (built with XR_SO_ACCEPTANCE) through adb and collects the evidence.
Exit codes: 0 PASS, 1 FAIL, 2 TIMEOUT, 3 setup error, 4 PARTIAL (runner finished, some sub-case NOT COVERED; only the M9 runner ends this way).
The run proves programmatic execution only, not physical controller/hand input.
This script never touches Quest test properties (proximity, guardian).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('m1', 'm2', 'm3', 'm4', 'm5', 'm6', 'm7', 'm8', 'm9', 'm9n', 'm9f', 'm9h')][string]$Milestone,
    [Parameter(Mandatory)][string]$Apk,
    [string]$Serial,
    [int]$TimeoutSeconds = 240,
    [string]$OutDir,
    [string]$OrdinaryApk,
    [string]$Package = 'com.occhipinti.inventorxrso',
    [string]$Activity = 'com.unity3d.player.UnityPlayerGameActivity'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# The M6 runner crosses four workspaces and two documents: it needs more than the default of the single-workspace runners.
if ($Milestone -eq 'm6' -and -not $PSBoundParameters.ContainsKey('TimeoutSeconds')) { $TimeoutSeconds = 780 }
if ($Milestone -eq 'm8' -and -not $PSBoundParameters.ContainsKey('TimeoutSeconds')) { $TimeoutSeconds = 900 }
# The M9 runner enters and leaves the fixture documents several times (double Trigger, Torna, desktop change, voice) and applies one feature edit.
if (($Milestone -eq 'm9' -or $Milestone -eq 'm9n' -or $Milestone -eq 'm9f' -or $Milestone -eq 'm9h') -and -not $PSBoundParameters.ContainsKey('TimeoutSeconds')) { $TimeoutSeconds = 960 }
# The M7 runner waits for three Inventor computations on the fixture.
if ($Milestone -eq 'm7' -and -not $PSBoundParameters.ContainsKey('TimeoutSeconds')) { $TimeoutSeconds = 480 }

function Fail-Setup([string]$Message) {
    [Console]::Error.WriteLine("SETUP ERROR: $Message")
    exit 3
}

$adbArgs = @()
function Invoke-Adb {
    $output = & adb @script:adbArgs @args 2>&1
    return [pscustomobject]@{ Code = $LASTEXITCODE; Text = (($output | ForEach-Object { "$_" }) -join "`n") }
}

function Get-DeviceApkHash([string]$Label) {
    $path = Invoke-Adb shell pm path $Package
    if ($path.Code -ne 0) { Fail-Setup "pm path failed ($Label): $($path.Text)" }
    $base = $path.Text -split "`n" | ForEach-Object { $_.Trim() } |
        Where-Object { $_ -match '^package:.*/base\.apk$' } | Select-Object -First 1
    if (-not $base) { Fail-Setup "Could not find installed base.apk ($Label): $($path.Text)" }
    $remote = $base -replace '^package:', ''
    $sum = Invoke-Adb shell sha256sum $remote
    if ($sum.Code -ne 0 -or $sum.Text -notmatch '^([0-9a-fA-F]{64})') { Fail-Setup "sha256sum failed ($Label): $($sum.Text)" }
    return $Matches[1].ToLowerInvariant()
}

function Install-Verified([string]$File, [string]$Label) {
    $expected = (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash.ToLowerInvariant()
    $install = Invoke-Adb install -r $File
    if ($install.Code -ne 0 -or $install.Text -notmatch 'Success') { Fail-Setup "adb install failed ($Label): $($install.Text)" }
    $device = Get-DeviceApkHash $Label
    if ($device -ne $expected) { Fail-Setup "Installed APK hash differs ($Label): local $expected, device $device" }
    return [pscustomobject]@{ Local = $expected; Device = $device }
}

if (-not (Get-Command adb -ErrorAction SilentlyContinue)) { Fail-Setup 'adb was not found on PATH' }
if (-not (Test-Path -LiteralPath $Apk -PathType Leaf)) { Fail-Setup "APK not found: $Apk" }
if ($OrdinaryApk -and -not (Test-Path -LiteralPath $OrdinaryApk -PathType Leaf)) { Fail-Setup "Ordinary APK not found: $OrdinaryApk" }
if (-not $OutDir) { $OutDir = Join-Path 'artifacts' "$Milestone-verification" }

if ($Serial) {
    $adbArgs = @('-s', $Serial)
} else {
    $devices = @(& adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '\tdevice$' })
    if ($devices.Count -ne 1) { Fail-Setup "Expected exactly one adb device, found $($devices.Count); use -Serial" }
    $Serial = ($devices[0] -split "`t")[0]
    $adbArgs = @('-s', $Serial)
}
$state = Invoke-Adb get-state
if ($state.Code -ne 0 -or (($state.Text -split "`r?`n" | Select-Object -Last 1).Trim() -ne 'device')) {
    Fail-Setup "Device $Serial is not ready: $($state.Text)"
}
$power = Invoke-Adb shell dumpsys power
$wakefulnessAtLaunch = if ($power.Code -eq 0 -and $power.Text -match 'mWakefulness=(\w+)') { $Matches[1] } else { 'Unknown' }

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$apkPath = (Resolve-Path -LiteralPath $Apk).Path
$startUtc = [DateTime]::UtcNow
$stamp = (Get-Date -Format 'yyyyMMdd-HHmmss')
$prefix = "quest-acceptance-$stamp-"

Write-Warning 'Horizon OS may show the "switch to controllers" dialog on the headset; dismiss it if the run stalls.'
Write-Warning 'This run proves programmatic execution only, not physical controller or hand input.'

$hashes = Install-Verified $apkPath 'acceptance APK'
Write-Host "Acceptance APK sha256: $($hashes.Local)"

$remoteDir = "/sdcard/Android/data/$Package/files"
$remoteLog = "$remoteDir/$Milestone-acceptance.txt"
[void](Invoke-Adb shell am force-stop $Package)
[void](Invoke-Adb shell "rm -f $remoteDir/$Milestone-acceptance*")
$start = Invoke-Adb shell am start -n "$Package/$Activity" --ez "xr_${Milestone}_acceptance" true
if ($start.Code -ne 0 -or $start.Text -match 'Error') { Fail-Setup "am start failed: $($start.Text)" }

$outcome = 'TIMEOUT'
$seen = 0
$runnerStarted = $false
$finalLines = @()
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
while ([DateTime]::UtcNow -lt $deadline) {
    Start-Sleep -Seconds 2
    $cat = Invoke-Adb shell cat $remoteLog
    if ($cat.Code -ne 0) { continue }
    $runnerStarted = $true
    $lines = @($cat.Text -split "`r?`n" | Where-Object { $_ -ne '' })
    $finalLines = $lines
    for ($i = $seen; $i -lt $lines.Count; $i++) { Write-Host $lines[$i] }
    $seen = $lines.Count
    if ($lines | Where-Object { $_ -match '\] PASS COMPLETE;' }) { $outcome = 'PASS'; break }
    # M9: the runner finished but left sub-cases NOT COVERED (never counted as a pass).
    if ($lines | Where-Object { $_ -match '\] PARTIAL;' }) { $outcome = 'PARTIAL'; break }
    if ($lines | Where-Object { $_ -match '\] FAIL;| FAIL;' }) { $outcome = 'FAIL'; break }
}
$endUtc = [DateTime]::UtcNow
$timeoutPhase = if ($outcome -eq 'TIMEOUT') {
    if ($runnerStarted) { 'runner' } else { 'before_runner_start' }
} else { $null }
$checks = @(
    foreach ($line in $finalLines) {
        if ($line -match '\b(PASS|NOT COVERED) \[([^\]]+)\]\s*(.*)$') {
            [ordered]@{ status = $Matches[1]; gate = $Matches[2]; detail = $Matches[3] }
        }
    }
)

$pulled = New-Object System.Collections.Generic.List[string]
$logTarget = Join-Path $OutDir "$prefix$Milestone-acceptance.txt"
$pull = Invoke-Adb pull $remoteLog $logTarget
if ($pull.Code -eq 0) { $pulled.Add($logTarget) } else { Write-Warning "Could not pull log: $($pull.Text)" }
$ls = Invoke-Adb shell "ls $remoteDir/$Milestone-acceptance-*.png"
if ($ls.Code -eq 0) {
    foreach ($remote in ($ls.Text -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -like '*.png' })) {
        $target = Join-Path $OutDir ($prefix + [IO.Path]::GetFileName($remote))
        $p = Invoke-Adb pull $remote $target
        if ($p.Code -eq 0) { $pulled.Add($target) } else { Write-Warning "Could not pull $remote" }
    }
}

$ordinary = $null
if ($OrdinaryApk) {
    $ordinary = Install-Verified (Resolve-Path -LiteralPath $OrdinaryApk).Path 'ordinary APK'
    Write-Host "Ordinary APK reinstalled, sha256: $($ordinary.Local)"
}

$manifest = [ordered]@{
    milestone = $Milestone
    serial = $Serial
    apk = $apkPath
    sha256 = $hashes.Local
    deviceSha256 = $hashes.Device
    startUtc = $startUtc.ToString('o')
    endUtc = $endUtc.ToString('o')
    outcome = $outcome
    timeoutPhase = $timeoutPhase
    runnerStarted = $runnerStarted
    deviceWakefulnessAtLaunch = $wakefulnessAtLaunch
    checks = $checks
    pulledFiles = @($pulled)
    ordinaryApk = if ($ordinary) { $OrdinaryApk } else { $null }
    ordinarySha256 = if ($ordinary) { $ordinary.Local } else { $null }
}
$manifestPath = Join-Path $OutDir "quest-acceptance-run-$stamp.json"
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Host "Outcome: $outcome$(if ($timeoutPhase) { " ($timeoutPhase)" }); manifest: $manifestPath"
Write-Warning 'Programmatic execution only: physical controller/hand input, audio and tracking remain open gates.'

switch ($outcome) { 'PASS' { exit 0 } 'FAIL' { exit 1 } 'PARTIAL' { exit 4 } default { exit 2 } }
