#requires -Version 7.0
param([string]$Output, [string]$RuntimeManifest)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $projectRoot
if (-not $Output) { $Output = Join-Path $repoRoot ('artifacts/meta-xr-simulator/live-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$Output = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$privateDirectory = Join-Path ([IO.Path]::GetTempPath()) ('xrso-simulator-live-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $privateDirectory | Out-Null
$prepared = $false
$hostProcess = $null
$runCode = 1
$restored = $false
$nativeStateUnchanged = $false
Push-Location $repoRoot
try {
    & dotnet build bridge/tests/QuestAcceptanceFixtures --nologo *> (Join-Path $Output 'fixture-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Native fixture build failed.' }
    & dotnet build bridge/src/server-http --nologo *> (Join-Path $Output 'host-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'HTTPS host build failed.' }
    $fixtureDll = Join-Path $repoRoot 'bridge/tests/QuestAcceptanceFixtures/bin/Debug/net10.0-windows/QuestAcceptanceFixtures.dll'
    # Each run owns its manifest, so historical fixture evidence is preserved.
    Set-Location $Output
    & dotnet $fixtureDll --prepare-quest m7 *> (Join-Path $Output 'fixture-prepare.log')
    if ($LASTEXITCODE -ne 0) { throw 'Native M7 fixture preparation failed; Inventor 2027 must be open.' }
    $prepared = $true
    $manifest = Get-Content (Join-Path $Output 'artifacts/m7-verification/quest-fixture.json') -Raw | ConvertFrom-Json
    & dotnet $fixtureDll --inspect-quest m7 *> (Join-Path $Output 'native-before.log')
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
    $tokenFile = Join-Path $privateDirectory 'tokens.txt'
    [IO.File]::WriteAllText($tokenFile, 'simulator-live:' + $token)
    $credentialsFile = Join-Path $privateDirectory 'credentials.json'
    @{ host = '127.0.0.1'; port = $port; cert_sha256 = $fingerprint; client_name = 'simulator-live'; token = $token;
        fixture_name = 'XR_M7_Quest_Acceptance.iam'; document_id = $manifest.document_id } |
        ConvertTo-Json | Set-Content -LiteralPath $credentialsFile -Encoding utf8
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
    Set-Location $repoRoot
    $runner = Join-Path $PSScriptRoot 'Invoke-Simulator.ps1'
    $runnerArgs = @('-NoProfile','-File',$runner,'-Output',$Output,'-LiveCredentials',$credentialsFile)
    if ($RuntimeManifest) { $runnerArgs += @('-RuntimeManifest',$RuntimeManifest) }
    & (Join-Path $PSHOME 'pwsh.exe') @runnerArgs
    $runCode = $LASTEXITCODE
}
finally {
    if ($prepared) {
        Set-Location $Output
        & dotnet $fixtureDll --inspect-quest m7 *> (Join-Path $Output 'native-after.log')
        if ($LASTEXITCODE -ne 0) { $runCode = 1 }
        else {
            $nativeStateUnchanged = (Get-Content (Join-Path $Output 'native-before.log') -Raw) -eq (Get-Content (Join-Path $Output 'native-after.log') -Raw)
            if (-not $nativeStateUnchanged) { $runCode = 1; Write-Warning 'Native fixture state changed; compare native-before.log and native-after.log.' }
        }
        & dotnet $fixtureDll --restore-quest m7 *> (Join-Path $Output 'fixture-restore.log')
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
    @{ fixture_prepared = $prepared; fixture_restored = $restored; native_state_unchanged = $nativeStateUnchanged; exit_code = $runCode; cad = 'native Inventor 2027'; input = 'synthetic' } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'native-cleanup.json') -Encoding utf8
    Pop-Location
}
exit $runCode
