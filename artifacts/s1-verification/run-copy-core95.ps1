param([ValidatePattern('^r[1-9][0-9]*$')][string]$Run='r1',[Parameter(Mandatory=$true)][string]$AddinAssembly)
$ErrorActionPreference='Stop'
function ReadPublished94([string]$Path){
 $stream89=[IO.FileStream]::new($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
 try{$reader89=[IO.StreamReader]::new($stream89,[Text.Encoding]::UTF8);try{$reader89.ReadToEnd()|ConvertFrom-Json}finally{$reader89.Dispose()}}finally{$stream89.Dispose()}
}
function AssertCoreAck95([string]$Nonce,[int]$Ordinal){
 $ack95=ReadPublished94 (Join-Path $privateOutput "copy-core-ack95-$Nonce.json")
 $phase95=$coreSequence[$Ordinal-1].Split('|')
 if($ack95.schema -ne 1 -or $ack95.status -cne 'ACK' -or $ack95.run_id -cne $runId -or $ack95.target_id -cne $metadata.target_id -or $ack95.owned_pid -ne $metadata.owned_pid -or $ack95.owned_start_utc_ticks -ne $metadata.owned_start_utc_ticks -or $ack95.nonce -cne $Nonce -or $ack95.ordinal -ne $Ordinal -or $ack95.action -cne $phase95[0] -or $ack95.context -cne $phase95[1] -or $ack95.pass -ne [int]$phase95[2] -or [DateTimeOffset]$ack95.expires_utc -le [DateTimeOffset]::UtcNow -or @($ack95.documents).Count -ne 2){throw 'Native acknowledgement ownership/order differs'}
 $contexts95=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 if(@($ack95.PSObject.Properties).Count -ne 13){throw 'Native95 ACK closed field count differs'}
 foreach($doc95 in $ack95.documents){
  if(-not $contexts95.Add([string]$doc95.context) -or $doc95.context -cnotin @('Part','Sheet') -or [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath([string]$doc95.native_path)) -cne [IO.Path]::GetFullPath([string]$metadata.fixture_directory) -or [string]::IsNullOrEmpty([string]$doc95.document_id) -or [string]::IsNullOrEmpty([string]$doc95.sketch_id)){throw 'Native acknowledgement document binding differs'}
  if(@($doc95.PSObject.Properties).Count -ne 11 -or @($doc95.source_ids.PSObject.Properties).Count -ne 8 -or @($doc95.source_curves.PSObject.Properties).Count -ne 5){throw 'Native95 document/source descriptor shape differs'}
  $frozen95=$doc95.source_curves|ConvertTo-Json -Depth 40 -Compress
  if($Ordinal -eq 1){$frozenSource95[[string]$doc95.context]=$frozen95}else{if($frozenSource95[[string]$doc95.context] -cne $frozen95){throw 'Native95 frozen source metadata changed'}}
 }
 [void]$validatedCoreAcks95.Add($Nonce)
}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $repo "artifacts/s1-verification/increment95/native-$Run"
if(Test-Path -LiteralPath $out){throw 'New evidence directory required'}
New-Item -ItemType Directory -Path $out | Out-Null
$project=Join-Path $repo 'bridge/tests/S1CurveCopyLifecycleOwnedHost95'
Copy-Item -LiteralPath (Join-Path $project 'bin/Debug/net10.0-windows') -Destination (Join-Path $out 'bin') -Recurse
New-Item -ItemType Directory -Path (Join-Path $out 'sources') | Out-Null
Get-ChildItem -LiteralPath $project -File | Copy-Item -Destination (Join-Path $out 'sources')
Copy-Item -LiteralPath $PSCommandPath -Destination $out
$clientProject=Join-Path $repo 'artifacts/s1-verification/increment95/client'
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
if((Get-FileHash -LiteralPath $addinSource).Hash -cne '31680B0110529BBA8E20B5ECF9023E286D2E3385943509C459634C8C9FA7A8D8'){throw 'Immutable93 candidate required'}
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
 $args73=@((Join-Path $out 'bin/S1CurveCopyLifecycleOwnedHost95.dll'),'--run-id',$runId,'--host','127.0.0.1','--output-dir',$privateOutput,'--expected-addin-path',$stageDll,'--expected-addin-sha256',$stageSha,'--manifest-path',$manifest,'--manifest-sha256',$stageManifestSha,'--installed-dll-path',$installed,'--installed-dll-sha256',$installedSha)
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
 foreach($arg80 in @((Join-Path $out 'client-bin/S1CopyCoreHttpProbe95.dll'),$runId,$bootstrap,(Join-Path $privateOutput 'copy-client95.json'),(Join-Path $privateOutput 'curve-copy-sources94.json'))){$clientStart.ArgumentList.Add([string]$arg80)}
 $client=[Diagnostics.Process]::new();$client.StartInfo=$clientStart
 try{
  if(-not $client.Start()){throw 'First-read client did not start'}
  $clientStdout=$client.StandardOutput.ReadToEndAsync();$clientStderr=$client.StandardError.ReadToEndAsync()
  $clientWatch=[Diagnostics.Stopwatch]::StartNew();$lastCheckpoint='';$baselineVerified95=$false;$lastCoreNonce='';$coreOrdinal=0
  $coreSequence=@('prepare|all|0','edit|Part|1','inspect|Part|1','edit|Part|2','inspect|Part|2','reopen|Part|0','inspect_reopen|Part|0','edit|Sheet|1','inspect|Sheet|1','edit|Sheet|2','inspect|Sheet|2','reopen|Sheet|0','inspect_reopen|Sheet|0','complete|all|0')
  $coreNonces=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$validatedCoreAcks95=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$frozenSource95=@{}
  $checkpointNonces94=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  while(-not $client.HasExited -and $clientWatch.Elapsed.TotalSeconds -lt 1680 -and $wrapperWatch94.Elapsed.TotalSeconds -lt 1830){
   if($process.HasExited){throw 'Owned host exited while client was active'}
   $checkpointRequest=Join-Path $privateOutput 'copy-checkpoint-request94.json'
   if(Test-Path -LiteralPath $checkpointRequest){
    $request94=$null;try{$request94=ReadPublished94 $checkpointRequest}catch{}
    if($null -ne $request94 -and $request94.nonce -cne $lastCheckpoint){
     if(@($request94.PSObject.Properties).Count -ne 2 -or $request94.run_id -cne $runId -or $request94.nonce -cnotmatch '^[0-9a-f]{32}$' -or -not $checkpointNonces94.Add([string]$request94.nonce) -or $checkpointNonces94.Count -gt 74){throw 'Checkpoint94 request replay/ownership differs'}
     $process.StandardInput.WriteLine('copy-checkpoint94');$process.StandardInput.Flush();$lastCheckpoint=$request94.nonce
    }
   }
   $prerequisite95=Join-Path $privateOutput 'prerequisite94-ready.json'
   if(-not $baselineVerified95 -and (Test-Path -LiteralPath $prerequisite95)){
    $prerequisite=ReadPublished94 $prerequisite95
    $baselineClient=ReadPublished94 (Join-Path $privateOutput 'copy-client94.json')
    if(@($prerequisite.PSObject.Properties).Count -ne 1 -or $prerequisite.run_id -cne $runId -or $baselineClient.run_id -cne $runId -or $baselineClient.target_id -cne $metadata.target_id -or $baselineClient.status -cne 'PASS_PC_CURVE_COPY_SUBSET' -or @($baselineClient.cases).Count -ne 18 -or @($baselineClient.refusals).Count -ne 10 -or $checkpointNonces94.Count -ne 74){throw 'Baseline94 readiness evidence differs'}
    $process.StandardInput.WriteLine('verify-copy94');$process.StandardInput.Flush()
    $nativePath95=Join-Path $privateOutput 'copy-native-verifier94.json';$nativeWait95=[Diagnostics.Stopwatch]::StartNew()
    while(-not (Test-Path -LiteralPath $nativePath95) -and -not $process.HasExited -and $nativeWait95.Elapsed.TotalSeconds -lt 120){Start-Sleep -Milliseconds 100}
    if(-not (Test-Path -LiteralPath $nativePath95)){throw 'Baseline94 native verifier missing before95'}
    $nativeBaseline=ReadPublished94 $nativePath95
    if($nativeBaseline.run_id -cne $runId -or $nativeBaseline.target_id -cne $metadata.target_id -or $nativeBaseline.status -cne 'PASS_NATIVE_CURVE_COPY_SUBSET' -or @($nativeBaseline.checks).Count -ne 28 -or $nativeBaseline.checkpoints -ne 74){throw 'Baseline94 independent matrix failed'}
    $release95=Join-Path $privateOutput 'prerequisite94-verified.json';$releaseTemp95=$release95+'.tmp'
    if((Test-Path -LiteralPath $release95) -or (Test-Path -LiteralPath $releaseTemp95)){throw 'Baseline95 release already exists'}
    [IO.File]::WriteAllText($releaseTemp95,(@{run_id=$runId}|ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false));[IO.File]::Move($releaseTemp95,$release95)
    $baselineVerified95=$true
   }
   $coreRequestPath=Join-Path $privateOutput 'copy-core-request95.json'
   if(Test-Path -LiteralPath $coreRequestPath){
    $request95=$null;try{$request95=ReadPublished94 $coreRequestPath}catch{}
    if($null -ne $request95 -and $request95.nonce -cne $lastCoreNonce){
     if(-not $baselineVerified95){throw 'Lifecycle95 request preceded native94 prerequisite'}
     if($coreOrdinal -gt 0){AssertCoreAck95 $lastCoreNonce $coreOrdinal}
     if(@($request95.PSObject.Properties).Count -ne 7 -or @($request95.PSObject.Properties|Where-Object {$_.Name -cnotin @('schema','run_id','nonce','ordinal','action','context','pass')}).Count -ne 0 -or $request95.schema -ne 1 -or $request95.run_id -cne $runId -or $request95.nonce -cnotmatch '^[0-9a-f]{32}$' -or $request95.ordinal -ne $coreOrdinal+1 -or $coreOrdinal -ge 14 -or "$($request95.action)|$($request95.context)|$($request95.pass)" -cne $coreSequence[$coreOrdinal] -or -not $coreNonces.Add([string]$request95.nonce)){throw 'Lifecycle95 request ownership/order/replay differs'}
     $process.StandardInput.WriteLine('copy-core95');$process.StandardInput.Flush();$lastCoreNonce=$request95.nonce;$coreOrdinal++
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
 $process.StandardInput.WriteLine('inspect');$process.StandardInput.Flush()
 $inspect=Join-Path $privateOutput 'inspect-001.json';$watch.Restart()
 while(-not (Test-Path -LiteralPath $inspect) -and -not $process.HasExited -and $watch.Elapsed.TotalSeconds -lt 30){Start-Sleep -Milliseconds 100}
 if(-not (Test-Path -LiteralPath $inspect)){throw 'Native94 final inspection absent'}
 if($coreOrdinal -gt 0){AssertCoreAck95 $lastCoreNonce $coreOrdinal}
 $life95=ReadPublished94 (Join-Path $privateOutput 'copy-native-lifecycle95.json');$client95=ReadPublished94 (Join-Path $privateOutput 'copy-client95.json')
 if(-not $baselineVerified95 -or $coreOrdinal -ne 14 -or $validatedCoreAcks95.Count -ne 14 -or $life95.status -cne 'PASS_NATIVE_CURVE_COPY_LIFECYCLE_SUBSET' -or $life95.run_id -cne $runId -or $life95.target_id -cne $metadata.target_id -or $life95.source_updates -ne 4 -or $life95.reopens -ne 2 -or @($life95.protocol).Count -ne 14 -or $client95.status -cne 'PASS_PC_CURVE_COPY_LIFECYCLE_SUBSET' -or $client95.run_id -cne $runId -or $client95.target_id -cne $metadata.target_id -or @($client95.lifecycle).Count -ne 6){throw 'Lifecycle95 terminal Core/native matrix differs'}
 foreach($first95 in $client95.lifecycle){if($first95.first_core_snapshot -ne $true -or $first95.copies_and_unrelated_exact -ne $true -or $first95.fresh_references -ne $true){throw 'Lifecycle95 ONE first-read assertion missing'}}
 $result.native_core_lifecycle_subset=$true;$result.core_first_reads=6;$result.source_updates=4;$result.reopens=2;$result.protocol_phases=14;$result.owned_acknowledgements=14
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
    if($markers -notmatch '\A(?:(?:READY|ERROR|BOOTSTRAP_WRITTEN|INSPECTED|COPY_CHECKPOINTED|COPY_NATIVE_VERIFIED|COPY_CORE95_ACKNOWLEDGED|STOPPED|STOP_FAILED)\r?\n)*\z' -or $stderr.GetAwaiter().GetResult().Length -ne 0){throw 'Unexpected host output suppressed'}
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
   Get-ChildItem -LiteralPath $privateOutput -File | Where-Object {$_.Name -cin @('ready.json','stopped.json','ownership.json','curve-copy-sources94.json','copy-client94.json','copy-native-verifier94.json','copy-checkpoint-request94.json','copy-checkpoint-ack94.json','copy-client95.json','copy-native-lifecycle95.json','copy-core-request95.json','prerequisite94-ready.json','prerequisite94-verified.json') -or $_.Name -cmatch '^inspect-[0-9]{3}\.json$' -or $_.Name -cmatch '^copy-native-checkpoint94-[0-9]{3}\.json$' -or $_.Name -cmatch '^copy-native-state95-[0-9]{3}\.json$' -or $_.Name -cmatch '^copy-core-ack95-[0-9a-f]{32}\.json$'} | Copy-Item -Destination (Join-Path $out 'worker')
   $result.archived_raw_checkpoints=@(Get-ChildItem -LiteralPath (Join-Path $out 'worker') -File|Where-Object {$_.Name -cmatch '^copy-native-checkpoint94-[0-9]{3}\.json$'}).Count
   if($exitCode -eq 0 -and $result.archived_raw_checkpoints -ne 74){throw 'Archived94 raw count differs'}
   $lifePath95=Join-Path $privateOutput 'copy-native-lifecycle95.json'
   if(Test-Path -LiteralPath $lifePath95){
    $saved95=ReadPublished94 $lifePath95;$result.lifecycle_terminal_status=$saved95.status
    if($saved95.status -ceq 'PASS_NATIVE_CURVE_COPY_LIFECYCLE_SUBSET'){
     $fixtures95=Join-Path $out 'fixtures';New-Item -ItemType Directory -Path $fixtures95|Out-Null
     $stopped95=ReadPublished94 (Join-Path $privateOutput 'stopped.json');$ownedDirectory95=[IO.Path]::GetFullPath([string]$stopped95.fixture_directory)
     if([IO.Path]::GetFileName($ownedDirectory95) -cnotmatch "^s1-ownedquest73-$runId-[0-9a-f]{32}$" -or (Get-Item -LiteralPath $ownedDirectory95).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Owned95 archive directory differs'}
     $archived95=@();$paths95=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
     foreach($row95 in $saved95.documents){
      $transition95=$row95.persistence
      foreach($role95 in @('original','copy')){
       $path95=[IO.Path]::GetFullPath([string]$transition95.($role95+'_path'))
       if([IO.Path]::GetDirectoryName($path95) -cne $ownedDirectory95 -or [IO.Path]::GetExtension($path95) -cne '.ipt' -or -not $paths95.Add($path95) -or (Get-Item -LiteralPath $path95).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Owned95 original/copy path/reparse/uniqueness differs'}
       $destination95=Join-Path $fixtures95 ([IO.Path]::GetFileName($path95));if(Test-Path -LiteralPath $destination95){throw 'Owned95 archive filename collision'}
       Copy-Item -LiteralPath $path95 -Destination $destination95;$sha95=(Get-FileHash -LiteralPath $destination95).Hash
       if($sha95 -cne $transition95.($role95+'_sha256')){throw 'Owned95 archived IPT differs from native verified hash'}
       $archived95+=[ordered]@{name=[IO.Path]::GetFileName($path95);sha256=$sha95;role=$role95;context=$row95.context}
      }
     }
     if($archived95.Count -ne 4){throw 'Owned95 four IPT archive incomplete'};$result.archived_fixtures95=$archived95
    }
   }
   $result.archived_raw_states95=@(Get-ChildItem -LiteralPath (Join-Path $out 'worker') -File|Where-Object {$_.Name -cmatch '^copy-native-state95-[0-9]{3}\.json$'}).Count
   $result.archived_acknowledgements95=@(Get-ChildItem -LiteralPath (Join-Path $out 'worker') -File|Where-Object {$_.Name -cmatch '^copy-core-ack95-[0-9a-f]{32}\.json$'}).Count
   if($exitCode -eq 0 -and ($result.archived_raw_states95 -ne 12 -or $result.archived_acknowledgements95 -ne 14)){throw 'Owned95 raw-state/ACK archive matrix incomplete'}
  }
 } catch {$exitCode=1}
 $result.exit_code=$exitCode
 $result|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $out 'wrapper-result.json') -Encoding utf8
 if($null -ne $process){$process.Dispose()}
}
exit $exitCode
