param([ValidatePattern('^r[1-9][0-9]*$')][string]$Run='r1',[Parameter(Mandatory=$true)][string]$AddinAssembly)
$ErrorActionPreference='Stop'
function ReadPublished94([string]$Path){
 $stream89=[IO.FileStream]::new($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
 try{$reader89=[IO.StreamReader]::new($stream89,[Text.Encoding]::UTF8);try{$reader89.ReadToEnd()|ConvertFrom-Json}finally{$reader89.Dispose()}}finally{$stream89.Dispose()}
}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $repo "artifacts/s1-verification/increment94/native-$Run"
if(Test-Path -LiteralPath $out){throw 'New evidence directory required'}
New-Item -ItemType Directory -Path $out | Out-Null
$project=Join-Path $repo 'bridge/tests/S1CurveCopyCoreOwnedHost94'
Copy-Item -LiteralPath (Join-Path $project 'bin/Debug/net10.0-windows') -Destination (Join-Path $out 'bin') -Recurse
New-Item -ItemType Directory -Path (Join-Path $out 'sources') | Out-Null
Get-ChildItem -LiteralPath $project -File | Copy-Item -Destination (Join-Path $out 'sources')
Copy-Item -LiteralPath $PSCommandPath -Destination $out
$clientProject=Join-Path $repo 'artifacts/s1-verification/increment94/client'
Copy-Item -LiteralPath (Join-Path $clientProject 'bin/Debug/net8.0') -Destination (Join-Path $out 'client-bin') -Recurse
New-Item -ItemType Directory -Path (Join-Path $out 'client-sources') | Out-Null
Get-ChildItem -LiteralPath $clientProject -File | Copy-Item -Destination (Join-Path $out 'client-sources')
Copy-Item -LiteralPath (Join-Path $repo 'bridge/tests/S1ImageHttpProbe/ProbeTransport.cs') -Destination (Join-Path $out 'client-sources')
$manifest=Join-Path $env:APPDATA 'Autodesk/Inventor 2027/Addins/InventorSO/Inventor.So.addin'
$original=[IO.File]::ReadAllBytes($manifest)
$manifestSha=(Get-FileHash -LiteralPath $manifest).Hash
[xml]$xml=Get-Content -LiteralPath $manifest -Raw
$installed=[string]$xml.Addin.Assembly
$installedSha=(Get-FileHash -LiteralPath $installed).Hash
$runId=[Guid]::NewGuid().ToString('N')
$stage=Join-Path $env:LOCALAPPDATA "InventorSO/s1-ownedhost73-$runId"
$privateOutput=Join-Path $env:LOCALAPPDATA "InventorSO/s1-host73-output-$runId"
$addinSource=[IO.Path]::GetFullPath($AddinAssembly)
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item -LiteralPath $addinSource -Destination $stage
$stageDll=Join-Path $stage 'Inventor.So.AddIn.dll'
$stageSha=(Get-FileHash -LiteralPath $stageDll).Hash
$xml.Addin.Assembly=[string]$stageDll
$result=[ordered]@{run_id=$runId;quest_proof=$false;physical_proof=$false;open_s1_gates=18;original_manifest_sha256=$manifestSha;original_dll_sha256=$installedSha;stage_dll_sha256=$stageSha}
$process=$null;$exitCode=1;$wrapperWatch94=[Diagnostics.Stopwatch]::StartNew()
try {
 $xml.Save($manifest)
 $stageManifestSha=(Get-FileHash -LiteralPath $manifest).Hash
 $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName='dotnet';$start.UseShellExecute=$false;$start.CreateNoWindow=$true
 $start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
 $args73=@((Join-Path $out 'bin/S1CurveCopyCoreOwnedHost94.dll'),'--run-id',$runId,'--host','127.0.0.1','--output-dir',$privateOutput,'--expected-addin-path',$stageDll,'--expected-addin-sha256',$stageSha,'--manifest-path',$manifest,'--manifest-sha256',$stageManifestSha,'--installed-dll-path',$installed,'--installed-dll-sha256',$installedSha)
 if(@($args73|Where-Object {$_ -match '"' -or $_.EndsWith('\')}).Count){throw 'Unsupported argument encoding'}
 $start.Arguments=($args73|ForEach-Object {'"'+$_+'"'}) -join ' '
 $process=[Diagnostics.Process]::new();$process.StartInfo=$start
 if(-not $process.Start()){throw 'Host did not start'}
 $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
 $ready=Join-Path $privateOutput 'ready.json';$watch=[Diagnostics.Stopwatch]::StartNew()
 while(-not (Test-Path -LiteralPath $ready) -and -not $process.HasExited -and $watch.Elapsed.TotalSeconds -lt 180){Start-Sleep -Milliseconds 250}
 if(-not (Test-Path -LiteralPath $ready)){throw 'No owned ready attestation'}
 $metadata=ReadPublished94 $ready
 if($metadata.status -ne 'ready' -or $metadata.run_id -ne $runId -or $metadata.target_id -ne "inventor-2027-$($metadata.owned_pid)" -or $metadata.loaded_addin.sha256 -ne $stageSha){throw 'Owned attestation differs'}
 $process.StandardInput.WriteLine('bootstrap');$process.StandardInput.Flush()
 $bootstrap=Join-Path $privateOutput 'bootstrap.json';$watch.Restart()
 while(-not (Test-Path -LiteralPath $bootstrap) -and -not $process.HasExited -and $watch.Elapsed.TotalSeconds -lt 20){Start-Sleep -Milliseconds 100}
 if(-not (Test-Path -LiteralPath $bootstrap)){throw 'No one-shot bootstrap produced'}
 # Do not read/print/archive credential content. ACL and file size are sufficient
 # for this host smoke; parsing and Android consumption are separate checks.
 $result.bootstrap_private_acl=(Get-Acl -LiteralPath $bootstrap).AreAccessRulesProtected
 $result.bootstrap_size=(Get-Item -LiteralPath $bootstrap).Length
 if(-not $result.bootstrap_private_acl -or $result.bootstrap_size -gt 16384){throw 'Bootstrap storage policy differs'}
 $clientStart=[Diagnostics.ProcessStartInfo]::new();$clientStart.FileName='dotnet';$clientStart.UseShellExecute=$false;$clientStart.CreateNoWindow=$true
 $clientStart.RedirectStandardOutput=$true;$clientStart.RedirectStandardError=$true
 foreach($arg80 in @((Join-Path $out 'client-bin/S1CopyCoreHttpProbe94.dll'),$runId,$bootstrap,(Join-Path $privateOutput 'copy-client94.json'),(Join-Path $privateOutput 'curve-copy-sources94.json'))){$clientStart.ArgumentList.Add([string]$arg80)}
 $client=[Diagnostics.Process]::new();$client.StartInfo=$clientStart
 try{
  if(-not $client.Start()){throw 'First-read client did not start'}
  $clientStdout=$client.StandardOutput.ReadToEndAsync();$clientStderr=$client.StandardError.ReadToEndAsync()
  $clientWatch=[Diagnostics.Stopwatch]::StartNew();$lastCheckpoint=''
  $checkpointNonces94=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  while(-not $client.HasExited -and $clientWatch.Elapsed.TotalSeconds -lt 1440 -and $wrapperWatch94.Elapsed.TotalSeconds -lt 1500){
   if($process.HasExited){throw 'Owned host exited while client was active'}
   $checkpointRequest=Join-Path $privateOutput 'copy-checkpoint-request94.json'
   if(Test-Path -LiteralPath $checkpointRequest){
    $request94=$null;try{$request94=ReadPublished94 $checkpointRequest}catch{}
    if($null -ne $request94 -and $request94.nonce -cne $lastCheckpoint){
     if(@($request94.PSObject.Properties).Count -ne 2 -or $request94.run_id -cne $runId -or $request94.nonce -cnotmatch '^[0-9a-f]{32}$' -or -not $checkpointNonces94.Add([string]$request94.nonce) -or $checkpointNonces94.Count -gt 74){throw 'Checkpoint94 request replay/ownership differs'}
     $process.StandardInput.WriteLine('copy-checkpoint94');$process.StandardInput.Flush();$lastCheckpoint=$request94.nonce
    }
   }
   Start-Sleep -Milliseconds 100
  }
  if(-not $client.HasExited){throw 'Client timed out; no kill/second native run'}
  $result.client_exit=$client.ExitCode
  if($clientStdout.GetAwaiter().GetResult().Length -ne 0 -or $clientStderr.GetAwaiter().GetResult().Length -ne 0){throw 'Unexpected first-read client output suppressed'}
 }finally{$client.Dispose()}
 if($result.client_exit -ne 0){throw 'Core94 client failed; preserve its first result without diagnostic CAD retry'}
 $client94=ReadPublished94 (Join-Path $privateOutput 'copy-client94.json')
 if($client94.status -cne 'PASS_PC_CURVE_COPY_SUBSET' -or $client94.run_id -cne $runId -or $client94.target_id -cne $metadata.target_id -or @($client94.cases).Count -ne 18 -or @($client94.refusals).Count -ne 10 -or $checkpointNonces94.Count -ne 74){throw 'Core94 client matrix incomplete'}
 $raw94=@(Get-ChildItem -LiteralPath $privateOutput -File|Where-Object {$_.Name -cmatch '^copy-native-checkpoint94-[0-9]{3}\.json$'})
 if($raw94.Count -ne 74){throw 'Checkpoint94 raw inventory differs'}
 $seenRaw94=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 foreach($rawFile94 in $raw94){
  $entry94=ReadPublished94 $rawFile94.FullName
  if($entry94.run_id -cne $runId -or -not $checkpointNonces94.Contains([string]$entry94.nonce) -or -not $seenRaw94.Add([string]$entry94.nonce) -or $entry94.native_state_sha256 -cnotmatch '^[0-9A-F]{64}$' -or $null -eq $entry94.native_state){throw 'Checkpoint94 archive binding differs'}
 }
 $process.StandardInput.WriteLine('verify-copy94');$process.StandardInput.Flush()
 $verifier94=Join-Path $privateOutput 'copy-native-verifier94.json';$watch.Restart()
 while(-not (Test-Path -LiteralPath $verifier94) -and -not $process.HasExited -and $watch.Elapsed.TotalSeconds -lt 120 -and $wrapperWatch94.Elapsed.TotalSeconds -lt 1500){Start-Sleep -Milliseconds 100}
 if(-not (Test-Path -LiteralPath $verifier94)){throw 'Native94 final verifier absent'}
 $native94=ReadPublished94 $verifier94
 if($native94.status -cne 'PASS_NATIVE_CURVE_COPY_SUBSET' -or $native94.run_id -cne $runId -or $native94.target_id -cne $metadata.target_id -or @($native94.checks).Count -ne 28 -or $native94.checkpoints -ne 74){throw 'Native94 final matrix differs'}
 $process.StandardInput.WriteLine('inspect');$process.StandardInput.Flush()
 $inspect=Join-Path $privateOutput 'inspect-001.json';$watch.Restart()
 while(-not (Test-Path -LiteralPath $inspect) -and -not $process.HasExited -and $watch.Elapsed.TotalSeconds -lt 30){Start-Sleep -Milliseconds 100}
 if(-not (Test-Path -LiteralPath $inspect)){throw 'Native94 final inspection absent'}
 $result.native_copy_checks=28;$result.workflows=18;$result.refusals=10;$result.checkpoints=74;$result.readonly_inspection=$true
 $exitCode=0
} catch {$result.failure=[ordered]@{type=$_.Exception.GetType().FullName;hresult=$_.Exception.HResult};$exitCode=1}
finally {
 try {
  if($null -ne $process -and -not $process.HasExited){$process.StandardInput.WriteLine('stop');$process.StandardInput.Flush();$process.StandardInput.Close()}
  if($null -ne $process){
   $result.worker_exited=$process.WaitForExit(60000)
   if($result.worker_exited){
    $result.worker_exit_code=$process.ExitCode
    $markers=$stdout.GetAwaiter().GetResult()
    if($markers -notmatch '\A(?:(?:READY|ERROR|BOOTSTRAP_WRITTEN|INSPECTED|COPY_CHECKPOINTED|COPY_NATIVE_VERIFIED|STOPPED|STOP_FAILED)\r?\n)*\z' -or $stderr.GetAwaiter().GetResult().Length -ne 0){throw 'Unexpected host output suppressed'}
    [IO.File]::WriteAllText((Join-Path $out 'markers.txt'),$markers)
    if($process.ExitCode -ne 0){$exitCode=1}
   } else {$exitCode=1;$result.timeout_action='No kill/ROT; inspect ownership and journal before another native run'}
  }
 } catch {$result.stop_failure=$_.Exception.GetType().FullName;$exitCode=1}
 try {[IO.File]::WriteAllBytes($manifest,$original);$result.manifest_restored=(Get-FileHash -LiteralPath $manifest).Hash -eq $manifestSha;if(-not $result.manifest_restored){$exitCode=1}} catch {$result.manifest_restore_failure=$_.Exception.GetType().FullName;$exitCode=1}
 try {$result.original_dll_unchanged=(Get-FileHash -LiteralPath $installed).Hash -eq $installedSha;if(-not $result.original_dll_unchanged){$exitCode=1}} catch {$exitCode=1}
 try {
  $bootstrap=Join-Path $privateOutput 'bootstrap.json';if(Test-Path -LiteralPath $bootstrap){Remove-Item -LiteralPath $bootstrap}
  if(Test-Path -LiteralPath $privateOutput){
   New-Item -ItemType Directory -Path (Join-Path $out 'worker') | Out-Null
   Get-ChildItem -LiteralPath $privateOutput -File | Where-Object {$_.Name -cin @('ready.json','stopped.json','ownership.json','curve-copy-sources94.json','copy-client94.json','copy-native-verifier94.json','copy-checkpoint-request94.json','copy-checkpoint-ack94.json') -or $_.Name -cmatch '^inspect-[0-9]{3}\.json$' -or $_.Name -cmatch '^copy-native-checkpoint94-[0-9]{3}\.json$'} | Copy-Item -Destination (Join-Path $out 'worker')
   $result.archived_raw_checkpoints=@(Get-ChildItem -LiteralPath (Join-Path $out 'worker') -File|Where-Object {$_.Name -cmatch '^copy-native-checkpoint94-[0-9]{3}\.json$'}).Count
   if($exitCode -eq 0 -and $result.archived_raw_checkpoints -ne 74){throw 'Archived94 raw count differs'}
  }
 } catch {$exitCode=1}
 $result.exit_code=$exitCode
 $result|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $out 'wrapper-result.json') -Encoding utf8
 if($null -ne $process){$process.Dispose()}
}
exit $exitCode

