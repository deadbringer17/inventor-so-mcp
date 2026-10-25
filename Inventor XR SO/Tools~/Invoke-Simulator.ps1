param(
    [switch]$SetupOnly,
    [string]$RuntimeManifest,
    [string]$Output
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $Output) { $Output = Join-Path (Split-Path -Parent $projectRoot) 'artifacts/meta-xr-simulator' }
$Output = [System.IO.Path]::GetFullPath($Output)
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
if (-not $SetupOnly) {
    $marker = Join-Path $Output 'pass.marker'
    if (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker }
    & dotnet build (Join-Path $projectRoot 'Tests~/XrSo.TestHost/XrSo.TestHost.csproj') --nologo *> (Join-Path $Output 'testhost-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'HTTPS fixture build failed; see testhost-build.log.' }
}
$simExe = Join-Path (Split-Path -Parent $RuntimeManifest) 'MetaXRSimulator.exe'
& metavr xrsim app launch --app-path $simExe *> (Join-Path $Output 'frontend-launch.log')
if ($LASTEXITCODE -ne 0) { throw 'Meta XR Simulator frontend did not start; see frontend-launch.log.' }
$previousRuntime = $env:XR_RUNTIME_JSON
try {
    # Process scope only. Never replace the system-wide Quest Link/OpenXR runtime.
    $env:XR_RUNTIME_JSON = [System.IO.Path]::GetFullPath($RuntimeManifest)
    $method = if ($SetupOnly) { 'InventorXrSo.Editor.XrSoSimulator.ConfigureBatch' } else { 'InventorXrSo.Editor.XrSoSimulator.RunSmokeBatch' }
    # A real Game view is required to pump XR frames. Batch mode can create an OpenXR instance without rendering a session.
    $unity = 'C:/Program Files/Unity/Hub/Editor/6000.6.3f1/Editor/Unity.exe'
    $unityArgs = @('-projectPath',('"'+$projectRoot+'"'),'-buildTarget','Win64','-executeMethod',$method,'-xrSimOutput',('"'+$Output+'"'),'-logFile',('"'+(Join-Path $Output 'unity.log')+'"'))
    $editor = Start-Process -FilePath $unity -ArgumentList $unityArgs -PassThru -WindowStyle Hidden
    $editor.WaitForExit(); $editor.Refresh()
    $resultCode = $editor.ExitCode
    if (-not $SetupOnly -and $resultCode -eq 0 -and -not (Test-Path -LiteralPath $marker)) {
        throw 'Unity exited without completing the simulator checks; see unity.log.'
    }
} finally { $env:XR_RUNTIME_JSON = $previousRuntime }
exit $resultCode
