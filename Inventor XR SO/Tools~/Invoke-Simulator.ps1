param(
    [switch]$SetupOnly,
    [string]$RuntimeManifest,
    [string]$Output,
    [string]$LiveCredentials,
    [ValidateSet('m1','m2','m3','m4','m5','m6','m7','m8','m9','m9n','m9f','m9h','m10','qx','s1')][string]$AcceptanceRunner
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $Output) { $Output = Join-Path (Split-Path -Parent $projectRoot) ('artifacts/meta-xr-simulator/run-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$Output = [System.IO.Path]::GetFullPath($Output)
foreach ($previousEvidence in @('unity.log','result.json','runner-result.json')) {
    if (Test-Path -LiteralPath (Join-Path $Output $previousEvidence)) { throw 'Choose a new output folder; previous simulator evidence is immutable.' }
}
if ($AcceptanceRunner -and -not $LiveCredentials) { throw 'Acceptance runners require owned native fixture credentials from Invoke-SimulatorLive.ps1.' }
New-Item -ItemType Directory -Path $Output -Force | Out-Null
if (-not $RuntimeManifest) {
    $simRegistryPath = 'SOFTWARE\Classes\xrsim\shell\open\command'
    foreach ($registryRoot in @([Microsoft.Win32.Registry]::CurrentUser, [Microsoft.Win32.Registry]::LocalMachine)) {
        $key = $registryRoot.OpenSubKey($simRegistryPath)
        if ($null -ne $key) {
            try {
                $command = [string]$key.GetValue('')
                if ($command -match '^"([^"]+\.exe)"') {
                    $RuntimeManifest = Join-Path (Split-Path -Parent $Matches[1]) 'meta_openxr_simulator.json'
                }
            } finally { $key.Dispose() }
        }
        if ($RuntimeManifest) { break }
    }
    if (-not $RuntimeManifest) {
        $RuntimeManifest = Join-Path $env:LOCALAPPDATA 'MetaXRSimulatorPortable/PFiles/MetaXRSimulator/v207.1/meta_openxr_simulator.json'
    }
}
if (-not (Test-Path -LiteralPath $RuntimeManifest)) { throw 'Meta XR Simulator is missing. Install the official simulator or pass -RuntimeManifest.' }
if ($LiveCredentials -and -not (Test-Path -LiteralPath $LiveCredentials)) { throw 'Live fixture credentials file is missing.' }
if (-not $SetupOnly) {
    $marker = Join-Path $Output 'pass.marker'
    if (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker }
    if (-not $LiveCredentials) {
        & dotnet build (Join-Path $projectRoot 'Tests~/XrSo.TestHost/XrSo.TestHost.csproj') --nologo *> (Join-Path $Output 'testhost-build.log')
        if ($LASTEXITCODE -ne 0) { throw 'HTTPS fixture build failed; see testhost-build.log.' }
    }
}
$simExe = Join-Path (Split-Path -Parent $RuntimeManifest) 'MetaXRSimulator.exe'
$launcher = Start-Process (Get-Command metavr).Source -ArgumentList @('xrsim','app','launch','--app-path',('"'+$simExe+'"')) `
    -RedirectStandardOutput (Join-Path $Output 'frontend-launch.log') -RedirectStandardError (Join-Path $Output 'frontend-error.log') -PassThru -WindowStyle Hidden
# The newly launched GUI may inherit CLI pipes; wait for the CLI process, not EOF from its descendants.
if (-not $launcher.WaitForExit(45000)) { Stop-Process -Id $launcher.Id; throw 'Meta XR Simulator launcher timed out.' }
$launcher.Refresh()
if ($launcher.ExitCode -ne 0) { throw 'Meta XR Simulator frontend did not start; see frontend-launch.log.' }
$previousRuntime = $env:XR_RUNTIME_JSON
$previousLiveCredentials = $env:XR_SO_SIMULATOR_LIVE_CREDENTIALS
$previousRunner = $env:XR_SO_SIMULATOR_RUNNER
$previousOutput = $env:XR_SO_SIMULATOR_OUTPUT
try {
    # Process scope only. Never replace the system-wide Quest Link/OpenXR runtime.
    $env:XR_RUNTIME_JSON = [System.IO.Path]::GetFullPath($RuntimeManifest)
    $env:XR_SO_SIMULATOR_LIVE_CREDENTIALS = if ($LiveCredentials) { [System.IO.Path]::GetFullPath($LiveCredentials) } else { $null }
    $env:XR_SO_SIMULATOR_RUNNER = if ($AcceptanceRunner) { 'xr_' + $AcceptanceRunner + '_acceptance' } else { $null }
    $env:XR_SO_SIMULATOR_OUTPUT = $Output
    $runnerResult = Join-Path $Output 'runner-result.json'
    if (Test-Path -LiteralPath $runnerResult) { Remove-Item -LiteralPath $runnerResult }
    $method = if ($SetupOnly) { 'InventorXrSo.Editor.XrSoSimulator.ConfigureBatch' } else { 'InventorXrSo.Editor.XrSoSimulator.RunSmokeBatch' }
    # A real Game view is required to pump XR frames. Batch mode can create an OpenXR instance without rendering a session.
    $unity = 'C:/Program Files/Unity/Hub/Editor/6000.6.3f1/Editor/Unity.exe'
    $unityArgs = @('-projectPath',('"'+$projectRoot+'"'),'-buildTarget','Win64','-executeMethod',$method,'-xrSimOutput',('"'+$Output+'"'),'-logFile',('"'+(Join-Path $Output 'unity.log')+'"'))
    $editor = Start-Process -FilePath $unity -ArgumentList $unityArgs -PassThru -WindowStyle Hidden
    $deadline = [DateTime]::UtcNow.AddMinutes(30)
    $unityLog = Join-Path $Output 'unity.log'
    while (-not $editor.WaitForExit(1000)) {
        if ((Test-Path -LiteralPath $unityLog) -and (Select-String -LiteralPath $unityLog -Pattern 'error CS[0-9]{4}:' -Quiet)) {
            Stop-Process -Id $editor.Id
            throw 'Unity compilation failed; see unity.log.'
        }
        if ([DateTime]::UtcNow -gt $deadline) { Stop-Process -Id $editor.Id; throw 'Unity simulator run exceeded 30 minutes.' }
    }
    $editor.Refresh()
    $resultCode = $editor.ExitCode
    if (-not $SetupOnly -and $resultCode -eq 0 -and -not (Test-Path -LiteralPath $marker)) {
        throw 'Unity exited without completing the simulator checks; see unity.log.'
    }
} finally {
    $env:XR_RUNTIME_JSON = $previousRuntime; $env:XR_SO_SIMULATOR_LIVE_CREDENTIALS = $previousLiveCredentials
    $env:XR_SO_SIMULATOR_RUNNER = $previousRunner; $env:XR_SO_SIMULATOR_OUTPUT = $previousOutput
}
exit $resultCode
