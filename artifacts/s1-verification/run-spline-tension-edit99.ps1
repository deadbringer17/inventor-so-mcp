param([ValidatePattern('^r[1-9][0-9]*$')][string]$Run='r1',[Parameter(Mandatory=$true)][string]$AddinAssembly)
$ErrorActionPreference='Stop'
$wrapperDeadline99=[DateTimeOffset]::UtcNow.AddMinutes(35)
$candidate99='0D7801E7332B0743FCDB00C343CBDE888B9BBD8B1A70281DB14CD200CECC22E1'
$addinInput99=Get-Item -LiteralPath ([IO.Path]::GetFullPath($AddinAssembly))
if($addinInput99.PSIsContainer -or ($addinInput99.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or $addinInput99.Name -cne 'Inventor.So.AddIn.dll' -or (Get-FileHash -LiteralPath $addinInput99.FullName).Hash -cne $candidate99){throw 'Frozen producer99 candidate required'}
function ReadPublished91([string]$path) {
 $share=[IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete
 $stream=[IO.FileStream]::new($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,$share)
 try {
  if($stream.Length -lt 1 -or $stream.Length -gt 67108864){throw 'Published metadata byte budget exceeded'}
  $reader=[IO.StreamReader]::new($stream,[Text.UTF8Encoding]::new($false),$true,4096,$true)
  try {return ($reader.ReadToEnd() | ConvertFrom-Json)} finally {$reader.Dispose()}
 } finally {$stream.Dispose()}
}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $PSScriptRoot "increment99/native-$Run"
if(Test-Path -LiteralPath $out){throw 'New evidence directory required'}
New-Item -ItemType Directory -Path $out | Out-Null
$project=Join-Path $repo 'bridge/tests/S1SplineTensionEditOwnedHost99'
Copy-Item -LiteralPath (Join-Path $project 'bin/Debug/net10.0-windows') -Destination (Join-Path $out 'bin') -Recurse
New-Item -ItemType Directory -Path (Join-Path $out 'sources') | Out-Null
Get-ChildItem -LiteralPath $project -File | Copy-Item -Destination (Join-Path $out 'sources')
Copy-Item -LiteralPath $PSCommandPath -Destination $out
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
if($stageSha -cne $candidate99){throw 'Stage99 candidate differs'}
New-Item -ItemType Directory -Path (Join-Path $out 'producer99') | Out-Null
Copy-Item -LiteralPath $stageDll -Destination (Join-Path $out 'producer99/Inventor.So.AddIn.dll')
$xml.Addin.Assembly=[string]$stageDll
$result=[ordered]@{run_id=$runId;quest_proof=$false;physical_proof=$false;open_s1_gates=18;original_manifest_sha256=$manifestSha;original_dll_sha256=$installedSha;stage_dll_sha256=$stageSha}
$process=$null;$exitCode=1
try {
 $xml.Save($manifest)
 $stageManifestSha=(Get-FileHash -LiteralPath $manifest).Hash
 $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName='dotnet';$start.UseShellExecute=$false;$start.CreateNoWindow=$true
 $start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
 $args73=@((Join-Path $out 'bin/S1SplineTensionEditOwnedHost99.dll'),'--run-id',$runId,'--host','127.0.0.1','--output-dir',$privateOutput,'--expected-addin-path',$stageDll,'--expected-addin-sha256',$stageSha,'--manifest-path',$manifest,'--manifest-sha256',$stageManifestSha,'--installed-dll-path',$installed,'--installed-dll-sha256',$installedSha)
 if(@($args73|Where-Object {$_ -match '"' -or $_.EndsWith('\')}).Count){throw 'Unsupported argument encoding'}
 $start.Arguments=($args73|ForEach-Object {'"'+$_+'"'}) -join ' '
 $process=[Diagnostics.Process]::new();$process.StartInfo=$start
 if(-not $process.Start()){throw 'Host did not start'}
 $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
 $ready=Join-Path $privateOutput 'ready.json';$watch=[Diagnostics.Stopwatch]::StartNew()
 while(-not (Test-Path -LiteralPath $ready) -and -not $process.HasExited -and $watch.Elapsed.TotalSeconds -lt 180){Start-Sleep -Milliseconds 250}
 if(-not (Test-Path -LiteralPath $ready)){throw 'No owned ready attestation'}
 $metadata=ReadPublished91 $ready
 if($metadata.status -ne 'ready' -or $metadata.run_id -ne $runId -or $metadata.target_id -ne "inventor-2027-$($metadata.owned_pid)" -or $metadata.loaded_addin.sha256 -ne $stageSha){throw 'Owned attestation differs'}
 if($metadata.http_started -ne $false -or $metadata.bootstrap_exposed -ne $false){throw 'Dependency host must expose neither HTTP nor bootstrap'}
 $process.StandardInput.WriteLine('spline-tension-edit99');$process.StandardInput.Flush()
 $probeFile91=Join-Path $privateOutput 'spline-tension-edit99.json';$watch.Restart();$probe91=$null
 while(-not $process.HasExited -and $watch.Elapsed.TotalSeconds -lt 1990 -and [DateTimeOffset]::UtcNow -lt $wrapperDeadline99.AddSeconds(-90)){
  if(Test-Path -LiteralPath $probeFile91){
   try{$candidate91=ReadPublished91 $probeFile91;if($candidate91.status -cne 'RUNNING'){$probe91=$candidate91;break}}catch{}
  }
  Start-Sleep -Milliseconds 100
 }
 if($null -eq $probe91){throw 'No terminal native observation report; inspect the same owned process before another run'}
 $result.cases=@($probe91.cases).Count
 $result.refusals=@($probe91.refusals).Count
 $result.raw_states=$probe91.raw_state_count
 $result.status=$probe91.status
 if($probe91.run_id -cne $runId -or $probe91.target_id -cne $metadata.target_id -or $probe91.status -cne 'PASS_PLUGIN_NATIVE_SPLINE_TENSION_EDIT_SUBSET' -or $result.cases -ne 60 -or $result.refusals -ne 16 -or $result.raw_states -ne 272){throw 'Spline point edit99 matrix incomplete'}
 $result.full_s1_pass=$false
 $process.StandardInput.WriteLine('inspect');$process.StandardInput.Flush()
 $inspect=Join-Path $privateOutput 'inspect-001.json';$watch.Restart()
 while(-not (Test-Path -LiteralPath $inspect) -and -not $process.HasExited -and $watch.Elapsed.TotalSeconds -lt 30){Start-Sleep -Milliseconds 100}
 if(-not (Test-Path -LiteralPath $inspect)){throw 'No readonly native inspection'}
 $result.readonly_inspection=$true;$exitCode=0
} catch {$result.failure=[ordered]@{type=$_.Exception.GetType().FullName;hresult=$_.Exception.HResult};$exitCode=1}
finally {
 try {
  if($null -ne $process -and -not $process.HasExited){$process.StandardInput.WriteLine('stop');$process.StandardInput.Flush();$process.StandardInput.Close()}
  if($null -ne $process){
   $result.worker_exited=$process.WaitForExit(60000)
   if($result.worker_exited){
    $result.worker_exit_code=$process.ExitCode
    $markers=$stdout.GetAwaiter().GetResult()
    if($markers -notmatch '\A(?:(?:READY|ERROR|INSPECTED|SPLINE_TENSION_EDIT99_VERIFIED|STOPPED|STOP_FAILED)\r?\n)*\z' -or $stderr.GetAwaiter().GetResult().Length -ne 0){throw 'Unexpected host output suppressed'}
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
   Get-ChildItem -LiteralPath $privateOutput -File | Where-Object {$_.Name -in @('ready.json','stopped.json','ownership.json','spline-tension-edit99.json') -or $_.Name -match '^inspect-[0-9]{3}\.json$' -or $_.Name -cmatch '^spline-native99-[0-9]{3}\.json$'} | Copy-Item -Destination (Join-Path $out 'worker')
  }
 } catch {$exitCode=1}
 $result.exit_code=$exitCode
 $result.wrapper_deadline_utc=$wrapperDeadline99.ToString('O')
 $result|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $out 'wrapper-result.json') -Encoding utf8
 if($null -ne $process){$process.Dispose()}
}
exit $exitCode
