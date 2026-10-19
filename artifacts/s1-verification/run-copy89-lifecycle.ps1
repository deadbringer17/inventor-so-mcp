param([ValidatePattern('^r[1-9][0-9]*$')][string]$Run='r1',[Parameter(Mandatory=$true)][string]$AddinAssembly)
$ErrorActionPreference='Stop'
function ReadPublished89([string]$Path){
 $stream89=[IO.FileStream]::new($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
 try{$reader89=[IO.StreamReader]::new($stream89,[Text.Encoding]::UTF8);try{$reader89.ReadToEnd()|ConvertFrom-Json}finally{$reader89.Dispose()}}finally{$stream89.Dispose()}
}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $repo "artifacts/s1-verification/increment89/native-$Run"
if(Test-Path -LiteralPath $out){throw 'New evidence directory required'}
New-Item -ItemType Directory -Path $out | Out-Null
$project=Join-Path $repo 'bridge/tests/S1CopyLifecycleOwnedHost89'
Copy-Item -LiteralPath (Join-Path $project 'bin/Debug/net10.0-windows') -Destination (Join-Path $out 'bin') -Recurse
New-Item -ItemType Directory -Path (Join-Path $out 'sources') | Out-Null
Get-ChildItem -LiteralPath $project -File | Copy-Item -Destination (Join-Path $out 'sources')
Copy-Item -LiteralPath $PSCommandPath -Destination $out
$clientProject=Join-Path $repo 'artifacts/s1-verification/increment88/copy-client'
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
 $args73=@((Join-Path $out 'bin/S1CopyLifecycleOwnedHost89.dll'),'--run-id',$runId,'--host','127.0.0.1','--output-dir',$privateOutput,'--expected-addin-path',$stageDll,'--expected-addin-sha256',$stageSha,'--manifest-path',$manifest,'--manifest-sha256',$stageManifestSha,'--installed-dll-path',$installed,'--installed-dll-sha256',$installedSha)
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
 foreach($arg80 in @((Join-Path $out 'client-bin/S1CopyHttpProbe88.dll'),$runId,$bootstrap,(Join-Path $privateOutput 'copy-client88.json'))){$clientStart.ArgumentList.Add([string]$arg80)}
 $client=[Diagnostics.Process]::new();$client.StartInfo=$clientStart
 try{
  if(-not $client.Start()){throw 'First-read client did not start'}
  $clientStdout=$client.StandardOutput.ReadToEndAsync();$clientStderr=$client.StandardError.ReadToEndAsync()
  $clientWatch=[Diagnostics.Stopwatch]::StartNew();$lastCheckpoint=''
  while(-not $client.HasExited -and $clientWatch.Elapsed.TotalSeconds -lt 900){
   if($process.HasExited){throw 'Owned host exited while client was active'}
   $checkpointRequest=Join-Path $privateOutput 'copy-checkpoint-request88.json'
   if(Test-Path -LiteralPath $checkpointRequest){
    $request88=$null
    try{$request88=ReadPublished89 $checkpointRequest}catch{}
    if($null -ne $request88 -and $request88.run_id -ceq $runId -and $request88.nonce -match '^[0-9a-f]{32}$' -and $request88.nonce -cne $lastCheckpoint){
     $process.StandardInput.WriteLine('checkpoint-copy');$process.StandardInput.Flush();$lastCheckpoint=$request88.nonce
    }
   }
   Start-Sleep -Milliseconds 100
  }
  if(-not $client.HasExited){throw 'Client timed out; no kill/second native run'}
  $result.client_exit=$client.ExitCode
  if($clientStdout.GetAwaiter().GetResult().Length -ne 0 -or $clientStderr.GetAwaiter().GetResult().Length -ne 0){throw 'Unexpected first-read client output suppressed'}
 }finally{$client.Dispose()}
 $process.StandardInput.WriteLine('inspect');$process.StandardInput.Flush()
 $inspect=Join-Path $privateOutput 'inspect-001.json';$watch.Restart()
 while(-not (Test-Path -LiteralPath $inspect) -and -not $process.HasExited -and $watch.Elapsed.TotalSeconds -lt 30){Start-Sleep -Milliseconds 100}
 if(-not (Test-Path -LiteralPath $inspect)){throw 'No readonly native inspection'}
 $result.readonly_inspection=$true
 if($result.client_exit -ne 0){throw 'Copy client failed; preserve failure and native inspection'}
 $process.StandardInput.WriteLine('verify-copy');$process.StandardInput.Flush()
 $verifier=Join-Path $privateOutput 'copy-native-verifier88.json';$watch.Restart()
 while(-not (Test-Path -LiteralPath $verifier) -and -not $process.HasExited -and $watch.Elapsed.TotalSeconds -lt 60){Start-Sleep -Milliseconds 100}
 if(-not (Test-Path -LiteralPath $verifier)){throw 'No independent native copy verification'}
 $native88=Get-Content -LiteralPath $verifier -Raw|ConvertFrom-Json
 if($native88.run_id -cne $runId -or $native88.target_id -cne $metadata.target_id -or $native88.status -cne 'PASS_NATIVE_READBACK_SUBSET' -or @($native88.checks).Count -ne 8){throw 'Native copy readback matrix failed'}
 $result.native_copy_checks=8;$result.native_copy_subset=$true
 $process.StandardInput.WriteLine('copy-lifecycle89');$process.StandardInput.Flush()
 $lifecyclePath=Join-Path $privateOutput 'copy-lifecycle89.json';$watch.Restart();$lifecycle=$null
 while(-not $process.HasExited -and $watch.Elapsed.TotalSeconds -lt 300){
  if(Test-Path -LiteralPath $lifecyclePath){
   try{$lifecycle=ReadPublished89 $lifecyclePath}catch{}
   if($null -ne $lifecycle -and $lifecycle.status -in @('PASS_NATIVE_LIFECYCLE_SUBSET','FAILED')){break}
  }
  Start-Sleep -Milliseconds 250
 }
 if($null -eq $lifecycle -or $lifecycle.status -cne 'PASS_NATIVE_LIFECYCLE_SUBSET' -or $lifecycle.run_id -cne $runId -or $lifecycle.target_id -cne $metadata.target_id -or @($lifecycle.documents).Count -ne 2){throw 'Native lifecycle matrix failed or is not terminal; no kill or second native run'}
 $result.native_lifecycle_subset=$true
 foreach($document89 in $lifecycle.documents){
  if(@($document89.updates).Count -ne 2 -or $document89.state -cne 'two_source_updates_and_reopen_observed' -or $document89.persistence.state -cne 'reopened_verified'){throw 'Native lifecycle evidence incomplete'}
  foreach($update89 in $document89.updates){if($update89.copies_independent_exact -ne $true){throw 'Source update changed copies'}}
 }
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
    if($markers -notmatch '\A(?:(?:READY|ERROR|BOOTSTRAP_WRITTEN|INSPECTED|COPY_CHECKPOINTED|COPY_NATIVE_VERIFIED|COPY_LIFECYCLE_OBSERVED|STOPPED|STOP_FAILED)\r?\n)*\z' -or $stderr.GetAwaiter().GetResult().Length -ne 0){throw 'Unexpected host output suppressed'}
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
   Get-ChildItem -LiteralPath $privateOutput -File | Where-Object {$_.Name -in @('ready.json','stopped.json','ownership.json','copy-client88.json','copy-native-verifier88.json','copy-lifecycle89.json') -or $_.Name -match '^(inspect|copy-native-checkpoint)-[0-9]{3}\.json$'} | Copy-Item -Destination (Join-Path $out 'worker')
   $finalLifecycle89=Join-Path $privateOutput 'copy-lifecycle89.json'
   if(Test-Path -LiteralPath $finalLifecycle89){
    $saved89=Get-Content -LiteralPath $finalLifecycle89 -Raw|ConvertFrom-Json
    $result.lifecycle_terminal_status=$saved89.status
    if($saved89.status -ceq 'PASS_NATIVE_LIFECYCLE_SUBSET'){
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

