param([ValidatePattern('^r[1-9][0-9]*$')][string]$Run='r1',[Parameter(Mandatory=$true)][string]$AddinAssembly)
$ErrorActionPreference='Stop'
function ReadPublished89([string]$Path){
 $stream89=[IO.FileStream]::new($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
 try{$reader89=[IO.StreamReader]::new($stream89,[Text.Encoding]::UTF8);try{$reader89.ReadToEnd()|ConvertFrom-Json}finally{$reader89.Dispose()}}finally{$stream89.Dispose()}
}
function AssertCoreAck90([string]$Nonce,[int]$Ordinal){
 $ack90=ReadPublished89 (Join-Path $privateOutput "copy-core-ack90-$Nonce.json")
 $phase90=$coreSequence[$Ordinal-1].Split('|')
 if($ack90.schema -ne 1 -or $ack90.status -cne 'ACK' -or $ack90.run_id -cne $runId -or $ack90.target_id -cne $metadata.target_id -or $ack90.owned_pid -ne $metadata.owned_pid -or $ack90.owned_start_utc_ticks -ne $metadata.owned_start_utc_ticks -or $ack90.nonce -cne $Nonce -or $ack90.ordinal -ne $Ordinal -or $ack90.action -cne $phase90[0] -or $ack90.context -cne $phase90[1] -or $ack90.pass -ne [int]$phase90[2] -or [DateTimeOffset]$ack90.expires_utc -le [DateTimeOffset]::UtcNow -or @($ack90.documents).Count -ne 2){throw 'Native acknowledgement ownership/order differs'}
 $contexts90=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 foreach($doc90 in $ack90.documents){
  if(-not $contexts90.Add([string]$doc90.context) -or $doc90.context -cnotin @('Part','Sheet') -or [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath([string]$doc90.native_path)) -cne [IO.Path]::GetFullPath([string]$metadata.fixture_directory) -or [string]::IsNullOrEmpty([string]$doc90.document_id) -or [string]::IsNullOrEmpty([string]$doc90.sketch_id)){throw 'Native acknowledgement document binding differs'}
 }
 [void]$validatedCoreAcks90.Add($Nonce)
}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $repo "artifacts/s1-verification/increment90/native-$Run"
if(Test-Path -LiteralPath $out){throw 'New evidence directory required'}
New-Item -ItemType Directory -Path $out | Out-Null
$project=Join-Path $repo 'bridge/tests/S1CopyCoreLifecycleOwnedHost90'
Copy-Item -LiteralPath (Join-Path $project 'bin/Debug/net10.0-windows') -Destination (Join-Path $out 'bin') -Recurse
New-Item -ItemType Directory -Path (Join-Path $out 'sources') | Out-Null
Get-ChildItem -LiteralPath $project -File | Copy-Item -Destination (Join-Path $out 'sources')
Copy-Item -LiteralPath $PSCommandPath -Destination $out
$clientProject=Join-Path $repo 'artifacts/s1-verification/increment90/client'
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
$process=$null;$exitCode=1
try {
 $xml.Save($manifest)
 $stageManifestSha=(Get-FileHash -LiteralPath $manifest).Hash
 $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName='dotnet';$start.UseShellExecute=$false;$start.CreateNoWindow=$true
 $start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
 $args73=@((Join-Path $out 'bin/S1CopyCoreLifecycleOwnedHost90.dll'),'--run-id',$runId,'--host','127.0.0.1','--output-dir',$privateOutput,'--expected-addin-path',$stageDll,'--expected-addin-sha256',$stageSha,'--manifest-path',$manifest,'--manifest-sha256',$stageManifestSha,'--installed-dll-path',$installed,'--installed-dll-sha256',$installedSha)
 if(@($args73|Where-Object {$_ -match '"' -or $_.EndsWith('\')}).Count){throw 'Unsupported argument encoding'}
 $start.Arguments=($args73|ForEach-Object {'"'+$_+'"'}) -join ' '
 $process=[Diagnostics.Process]::new();$process.StartInfo=$start
 if(-not $process.Start()){throw 'Host did not start'}
 $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
 $ready=Join-Path $privateOutput 'ready.json';$watch=[Diagnostics.Stopwatch]::StartNew()
 while(-not (Test-Path -LiteralPath $ready) -and -not $process.HasExited -and $watch.Elapsed.TotalSeconds -lt 180){Start-Sleep -Milliseconds 250}
 if(-not (Test-Path -LiteralPath $ready)){throw 'No owned ready attestation'}
 $metadata=ReadPublished89 $ready
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
 foreach($arg80 in @((Join-Path $out 'client-bin/S1CopyCoreHttpProbe90.dll'),$runId,$bootstrap,(Join-Path $privateOutput 'copy-client90.json'))){$clientStart.ArgumentList.Add([string]$arg80)}
 $client=[Diagnostics.Process]::new();$client.StartInfo=$clientStart
 try{
  if(-not $client.Start()){throw 'First-read client did not start'}
  $clientStdout=$client.StandardOutput.ReadToEndAsync();$clientStderr=$client.StandardError.ReadToEndAsync()
  $clientWatch=[Diagnostics.Stopwatch]::StartNew();$lastCheckpoint='';$lastCoreNonce='';$coreOrdinal=0
  $coreSequence=@('prepare|all|0','edit|Part|1','inspect|Part|1','edit|Part|2','inspect|Part|2','reopen|Part|0','inspect_reopen|Part|0','edit|Sheet|1','inspect|Sheet|1','edit|Sheet|2','inspect|Sheet|2','reopen|Sheet|0','inspect_reopen|Sheet|0','complete|all|0')
  $coreNonces=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  $validatedCoreAcks90=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  while(-not $client.HasExited -and $clientWatch.Elapsed.TotalSeconds -lt 1500){
   if($process.HasExited){throw 'Owned host exited while client was active'}
   $checkpointRequest=Join-Path $privateOutput 'copy-checkpoint-request88.json'
   if(Test-Path -LiteralPath $checkpointRequest){
    $request88=$null
    try{$request88=ReadPublished89 $checkpointRequest}catch{}
    if($null -ne $request88 -and $request88.run_id -ceq $runId -and $request88.nonce -match '^[0-9a-f]{32}$' -and $request88.nonce -cne $lastCheckpoint){
     $process.StandardInput.WriteLine('checkpoint-copy');$process.StandardInput.Flush();$lastCheckpoint=$request88.nonce
    }
   }
   $coreRequestPath=Join-Path $privateOutput 'copy-core-request90.json'
   if(Test-Path -LiteralPath $coreRequestPath){
    $request90=$null;try{$request90=ReadPublished89 $coreRequestPath}catch{}
    if($null -ne $request90 -and $request90.nonce -cne $lastCoreNonce){
     if($coreOrdinal -gt 0){AssertCoreAck90 $lastCoreNonce $coreOrdinal}
     if(@($request90.PSObject.Properties).Count -ne 7 -or @($request90.PSObject.Properties|Where-Object {$_.Name -cnotin @('schema','run_id','nonce','ordinal','action','context','pass')}).Count -ne 0 -or $request90.schema -ne 1 -or $request90.run_id -cne $runId -or $request90.nonce -cnotmatch '^[0-9a-f]{32}$' -or $request90.ordinal -ne $coreOrdinal+1 -or $coreOrdinal -ge 14 -or "$($request90.action)|$($request90.context)|$($request90.pass)" -cne $coreSequence[$coreOrdinal] -or -not $coreNonces.Add([string]$request90.nonce)){throw 'Core lifecycle request binding/order differs; no replay'}
     $process.StandardInput.WriteLine('copy-core90');$process.StandardInput.Flush();$lastCoreNonce=$request90.nonce;$coreOrdinal++
    }
   }
   Start-Sleep -Milliseconds 100
  }
  if(-not $client.HasExited){throw 'Client timed out; no kill/second native run'}
  $result.client_exit=$client.ExitCode
  if($clientStdout.GetAwaiter().GetResult().Length -ne 0 -or $clientStderr.GetAwaiter().GetResult().Length -ne 0){throw 'Unexpected first-read client output suppressed'}
 }finally{$client.Dispose()}
 if($result.client_exit -ne 0){throw 'Core first-read client failed; preserve its first result without a diagnostic CAD retry'}
 $process.StandardInput.WriteLine('inspect');$process.StandardInput.Flush()
 $inspect=Join-Path $privateOutput 'inspect-001.json';$watch.Restart()
 while(-not (Test-Path -LiteralPath $inspect) -and -not $process.HasExited -and $watch.Elapsed.TotalSeconds -lt 30){Start-Sleep -Milliseconds 100}
 if(-not (Test-Path -LiteralPath $inspect)){throw 'No readonly native inspection'}
 $result.readonly_inspection=$true
 $native88=ReadPublished89 (Join-Path $privateOutput 'copy-native-verifier88.json')
 $lifecycle=ReadPublished89 (Join-Path $privateOutput 'copy-native-lifecycle90.json')
 $coreClient=ReadPublished89 (Join-Path $privateOutput 'copy-client90.json')
 if($coreOrdinal -gt 0){AssertCoreAck90 $lastCoreNonce $coreOrdinal}
 if($coreOrdinal -ne 14 -or $native88.run_id -cne $runId -or $native88.target_id -cne $metadata.target_id -or $native88.status -cne 'PASS_NATIVE_READBACK_SUBSET' -or @($native88.checks).Count -ne 8){throw 'Initial native copy matrix or protocol incomplete'}
 if($lifecycle.status -cne 'PASS_NATIVE_CORE_LIFECYCLE_SUBSET' -or $lifecycle.run_id -cne $runId -or $lifecycle.target_id -cne $metadata.target_id -or $lifecycle.source_updates -ne 4 -or $lifecycle.reopens -ne 2 -or @($lifecycle.documents).Count -ne 2){throw 'Native core lifecycle matrix failed'}
 if($coreClient.status -cne 'PASS_PC_CORE_LIFECYCLE_SUBSET' -or $coreClient.run_id -cne $runId -or $coreClient.target_id -cne $metadata.target_id -or @($coreClient.lifecycle).Count -ne 6){throw 'Core first-read matrix failed'}
 foreach($first90 in $coreClient.lifecycle){if($first90.first_core_snapshot -ne $true -or $first90.copies_and_unrelated_exact -ne $true -or $first90.fresh_references -ne $true){throw 'Core first-read evidence absent'}}
 if($validatedCoreAcks90.Count -ne 14){throw 'Native acknowledgement matrix incomplete'}
 $result.native_copy_checks=8;$result.native_copy_subset=$true;$result.native_core_lifecycle_subset=$true;$result.core_first_reads=6;$result.protocol_phases=14;$result.owned_acknowledgements=14
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
    if($markers -notmatch '\A(?:(?:READY|ERROR|BOOTSTRAP_WRITTEN|INSPECTED|COPY_CHECKPOINTED|COPY_NATIVE_VERIFIED|COPY_CORE90_ACKNOWLEDGED|STOPPED|STOP_FAILED)\r?\n)*\z' -or $stderr.GetAwaiter().GetResult().Length -ne 0){throw 'Unexpected host output suppressed'}
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
   Get-ChildItem -LiteralPath $privateOutput -File | Where-Object {$_.Name -in @('ready.json','stopped.json','ownership.json','copy-client88.json','copy-client90.json','copy-native-verifier88.json','copy-native-lifecycle90.json','copy-core-request90.json') -or $_.Name -match '^(inspect|copy-native-checkpoint)-[0-9]{3}\.json$' -or $_.Name -match '^copy-core-ack90-[0-9a-f]{32}\.json$'} | Copy-Item -Destination (Join-Path $out 'worker')
   $finalLifecycle89=Join-Path $privateOutput 'copy-native-lifecycle90.json'
   if(Test-Path -LiteralPath $finalLifecycle89){
    $saved89=Get-Content -LiteralPath $finalLifecycle89 -Raw|ConvertFrom-Json
    $result.lifecycle_terminal_status=$saved89.status
    if($saved89.status -ceq 'PASS_NATIVE_CORE_LIFECYCLE_SUBSET'){
     $fixtures89=Join-Path $out 'fixtures';New-Item -ItemType Directory -Path $fixtures89|Out-Null
     $archived89=@()
     foreach($row89 in $saved89.documents){
      $transition89=$row89.persistence
      $original89=[IO.Path]::GetFullPath([string]$transition89.original_path)
      $copy89=[IO.Path]::GetFullPath([string]$transition89.copy_path)
      $stopped89=Get-Content -LiteralPath (Join-Path $privateOutput 'stopped.json') -Raw|ConvertFrom-Json
      $ownedDirectory89=[IO.Path]::GetFullPath([string]$stopped89.fixture_directory)
      if([IO.Path]::GetDirectoryName($original89) -cne $ownedDirectory89 -or [IO.Path]::GetDirectoryName($copy89) -cne $ownedDirectory89 -or [IO.Path]::GetFileName($ownedDirectory89) -cnotmatch "^s1-ownedquest73-$runId-[0-9a-f]{32}$" -or [IO.Path]::GetExtension($copy89) -cne '.ipt' -or (Get-Item -LiteralPath $ownedDirectory89).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Owned fixture archive path differs'}
      foreach($file89 in @($original89,$copy89)){
       if((Get-Item -LiteralPath $file89).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Owned fixture archive reparse file'}
       $destination89=Join-Path $fixtures89 ([IO.Path]::GetFileName($file89));Copy-Item -LiteralPath $file89 -Destination $destination89
       $hash89=(Get-FileHash -LiteralPath $destination89).Hash
       if($file89 -ceq $copy89 -and $hash89 -cne $transition89.copy_sha256){throw 'Saved copy hash differs'}
       if($file89 -ceq $original89 -and $hash89 -cne $transition89.original_sha256){throw 'Archived original differs from native verified baseline'}
       $archived89+=[ordered]@{name=[IO.Path]::GetFileName($file89);sha256=$hash89}
      }
     }
     $result.archived_fixtures89=$archived89
    }
   }
  }
 } catch {$exitCode=1}
 $result.exit_code=$exitCode
 $result|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $out 'wrapper-result.json') -Encoding utf8
 if($null -ne $process){$process.Dispose()}
}
exit $exitCode

