#requires -Version 7.0
param([string]$Output, [string]$RuntimeManifest,
    [ValidateSet('m1','m2','m3','m4','m5','m6','m7','m8','m9','m9n','m9f','m9h','m10','qx','s1')][string]$AcceptanceRunner)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $projectRoot
if (-not $Output) { $Output = Join-Path $repoRoot ('artifacts/meta-xr-simulator/live-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$Output = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath (Join-Path $Output 'native-cleanup.json')) { throw 'Choose a new output folder; previous native evidence is immutable.' }
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$privateDirectory = Join-Path ([IO.Path]::GetTempPath()) ('xrso-simulator-live-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $privateDirectory | Out-Null
$prepared = $false
$hostProcess = $null
$runCode = 1
$restored = $false
$nativeStateUnchanged = $false
$fixture = switch ($AcceptanceRunner) { { $_ -in 'm8','m9','m10' } { 'm6' }; 'qx' { 'm7' }; '' { 'm7' }; default { $AcceptanceRunner } }
if (-not $fixture) { $fixture = 'm7' }
$readOnlyRun = -not $AcceptanceRunner -or $AcceptanceRunner -in 'm1','m2','m7','qx'
Push-Location $repoRoot
try {
    if (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $Output) 'stop.request')) {
        throw 'Queued suite run stopped before fixture preparation; see stop.request.'
    }
    $projectEditors = Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" | Where-Object {
        $_.CommandLine -like '*-projectPath*' -and $_.CommandLine.Contains($projectRoot) -and $_.CommandLine -notlike '*AssetImportWorker*'
    }
    if ($projectEditors) { throw 'Close the Unity Editor for this project before preparing a native fixture.' }
    $inventorProcesses = @(Get-Process Inventor -ErrorAction SilentlyContinue)
    if ($inventorProcesses.Count -ne 1) { throw 'Exactly one Inventor instance with the SO add-in must be open.' }
    & dotnet build bridge/tests/QuestAcceptanceFixtures --nologo *> (Join-Path $Output 'fixture-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Native fixture build failed.' }
    & dotnet build bridge/src/server-http --nologo *> (Join-Path $Output 'host-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'HTTPS host build failed.' }
    $fixtureDll = Join-Path $repoRoot 'bridge/tests/QuestAcceptanceFixtures/bin/Debug/net10.0-windows/QuestAcceptanceFixtures.dll'
    $fixtureArgs = @($fixture)
    if ($fixture -eq 'm4') {
        & dotnet build bridge/tests/M4LiveProbe --nologo *> (Join-Path $Output 'fixture-m4-build.log')
        if ($LASTEXITCODE -ne 0) { throw 'M4 fixture build failed.' }
        $fixtureDll = Join-Path $repoRoot 'bridge/tests/M4LiveProbe/bin/Debug/net10.0-windows/M4LiveProbe.dll'
        $fixtureArgs = @()
    }
    # Each run owns its manifest, so historical fixture evidence is preserved.
    Set-Location $Output
    & dotnet $fixtureDll --prepare-quest @fixtureArgs *> (Join-Path $Output 'fixture-prepare.log')
    if ($LASTEXITCODE -ne 0) { throw 'Native M7 fixture preparation failed; Inventor 2027 must be open.' }
    $prepared = $true
    $manifest = Get-Content (Join-Path $Output ('artifacts/' + $fixture + '-verification/quest-fixture.json')) -Raw | ConvertFrom-Json
    & dotnet $fixtureDll --inspect-quest @fixtureArgs *> (Join-Path $Output 'native-before.log')
    if ($LASTEXITCODE -ne 0) { throw 'Native fixture preflight failed.' }

    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
    $curve = [Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256')
    $key = [Security.Cryptography.ECDsa]::Create($curve)
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=XR simulator fixture', $key, [Security.Cryptography.HashAlgorithmName]::SHA256)
    $san = [Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
    $san.AddDnsName('localhost'); $san.AddIpAddress([Net.IPAddress]::Loopback)
    $request.CertificateExtensions.Add($san.Build())
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature, $true))
    $serverAuth = [Security.Cryptography.OidCollection]::new()
    [void]$serverAuth.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.1'))
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($serverAuth, $false))
    $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-1), [DateTimeOffset]::UtcNow.AddDays(1))
    $certificateFile = Join-Path $privateDirectory 'server.pfx'
    [IO.File]::WriteAllBytes($certificateFile, $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pkcs12))
    $fingerprint = $certificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256).ToLowerInvariant()
    $certificate.Dispose(); $key.Dispose()
    $token = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $runId = [Guid]::NewGuid().ToString('N')
    $clientName = if ($fixture -eq 's1') { 's1-qa-' + $runId } else { 'simulator-live' }
    $tokenFile = Join-Path $privateDirectory 'tokens.txt'
    [IO.File]::WriteAllText($tokenFile, $clientName + ':' + $token)
    $credentialsFile = Join-Path $privateDirectory 'credentials.json'
    $credentials = @{ host = '127.0.0.1'; port = $port; cert_sha256 = $fingerprint; client_name = $clientName; token = $token;
        fixture_name = [IO.Path]::GetFileName($(if ($manifest.active_document) { $manifest.active_document } else { $manifest.assembly }));
        fixture_milestone = $fixture; document_id = $manifest.document_id }
    if ($fixture -eq 's1') {
        $part = $manifest.owned_documents | Where-Object name -eq 'XR_S1_Quest_Acceptance_Part.ipt'
        $sheet = $manifest.owned_documents | Where-Object name -eq 'XR_S1_Quest_Acceptance_Sheet.ipt'
        $credentials.s1_bootstrap = @{ schema = 1; run_id = $runId; expires_utc = [DateTimeOffset]::UtcNow.AddMinutes(25).ToString('O');
            host = '127.0.0.1'; port = $port; cert_sha256 = $fingerprint; client_name = $clientName; token = $token;
            target_id = 'inventor-2027-' + $inventorProcesses[0].Id; part_document_id = $part.document_id; sheet_document_id = $sheet.document_id }
    }
    $credentials | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $credentialsFile -Encoding utf8
    $configFile = Join-Path $privateDirectory 'host-config.json'
    @{ assetDirectory = (Join-Path $Output 'assets'); auditDirectory = (Join-Path $Output 'audit'); publicBaseUrl = "https://127.0.0.1:$port" } |
        ConvertTo-Json | Set-Content -LiteralPath $configFile -Encoding utf8
    $hostDll = Join-Path $repoRoot 'bridge/src/server-http/bin/Debug/net8.0/Inventor.So.Mcp.Http.dll'
    $hostArgs = @(('"'+$hostDll+'"'),'--config',('"'+$configFile+'"'),'--http-urls',"https://127.0.0.1:$port",'--http-cert',('"'+$certificateFile+'"'),
        '--http-token-file',('"'+$tokenFile+'"'),'--target','2027','--enable-experimental')
    $hostProcess = Start-Process dotnet -ArgumentList $hostArgs -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $Output 'host.log') -RedirectStandardError (Join-Path $Output 'host-error.log')
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ($true) {
        if ($hostProcess.HasExited) { throw 'Native HTTPS host exited before readiness.' }
        $probe = [Net.Sockets.TcpClient]::new()
        try { $probe.Connect('127.0.0.1', $port); break } catch {
            if ([DateTime]::UtcNow -gt $deadline) { throw 'Native HTTPS host startup timeout.' }
            Start-Sleep -Milliseconds 200
        } finally { $probe.Dispose() }
    }
    $credentials.host_process_id = $hostProcess.Id
    $credentials.host_start_ticks = $hostProcess.StartTime.ToUniversalTime().Ticks
    $credentials.host_dll = $hostDll; $credentials.host_config = $configFile
    $credentials.host_certificate = $certificateFile; $credentials.host_tokens = $tokenFile
    $credentials | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $credentialsFile -Encoding utf8
    Set-Location $repoRoot
    $runner = Join-Path $PSScriptRoot 'Invoke-Simulator.ps1'
    $runnerArgs = @('-NoProfile','-File',$runner,'-Output',$Output,'-LiveCredentials',$credentialsFile)
    if ($RuntimeManifest) { $runnerArgs += @('-RuntimeManifest',$RuntimeManifest) }
    if ($AcceptanceRunner) { $runnerArgs += @('-AcceptanceRunner',$AcceptanceRunner) }
    & (Join-Path $PSHOME 'pwsh.exe') @runnerArgs
    $runCode = $LASTEXITCODE
}
finally {
    if ($prepared) {
        Set-Location $Output
        & dotnet $fixtureDll --inspect-quest @fixtureArgs *> (Join-Path $Output 'native-after.log')
        if ($LASTEXITCODE -ne 0) { $runCode = 1 }
        else {
            $nativeStateUnchanged = (Get-Content (Join-Path $Output 'native-before.log') -Raw) -eq (Get-Content (Join-Path $Output 'native-after.log') -Raw)
            if ($readOnlyRun -and -not $nativeStateUnchanged) { $runCode = 1; Write-Warning 'Native fixture state changed; compare native-before.log and native-after.log.' }
        }
        & dotnet $fixtureDll --restore-quest @fixtureArgs *> (Join-Path $Output 'fixture-restore.log')
        $restored = $LASTEXITCODE -eq 0
        if (-not $restored) { $runCode = 1; Write-Warning 'Native fixture restoration failed; see fixture-restore.log.' }
    }
    if ($hostProcess -and -not $hostProcess.HasExited) { Stop-Process -Id $hostProcess.Id }
    foreach ($fileName in @('server.pfx','tokens.txt','credentials.json','host-config.json')) {
        $privateFile = Join-Path $privateDirectory $fileName
        if (Test-Path -LiteralPath $privateFile) { Remove-Item -LiteralPath $privateFile }
    }
    Remove-Item -LiteralPath $privateDirectory
    if ($runCode -ne 0) {
        $marker = Join-Path $Output 'pass.marker'
        if (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker }
    }
    @{ fixture_prepared = $prepared; fixture_restored = $restored; native_state_unchanged = $nativeStateUnchanged; native_writes_expected = -not $readOnlyRun; exit_code = $runCode; cad = 'native Inventor 2027'; input = 'synthetic' } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'native-cleanup.json') -Encoding utf8
    Pop-Location
}
exit $runCode
