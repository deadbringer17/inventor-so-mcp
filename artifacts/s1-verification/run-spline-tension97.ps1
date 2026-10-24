param([ValidatePattern('^r[1-9][0-9]*$')][string]$Run='r1',[Parameter(Mandatory=$true)][string]$AddinAssembly)
$ErrorActionPreference='Stop'
$wrapperDeadline97=[DateTimeOffset]::UtcNow.AddMinutes(20)
$candidate97='2AF7E77A8A34EA82052AD84445398CDFA6858A2E9BC2AF13A6B577B6A13D15E0'
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
$out=Join-Path $PSScriptRoot "increment97/native-$Run"
if(Test-Path -LiteralPath $out){throw 'New evidence directory required'}
New-Item -ItemType Directory -Path $out | Out-Null
$project=Join-Path $repo 'bridge/tests/S1SplineTensionOwnedHost97'
$built97=[IO.Path]::GetFullPath((Join-Path $project 'bin/Debug/net10.0-windows'))
$bin97=Join-Path $out 'bin';New-Item -ItemType Directory -Path $bin97|Out-Null
# Archive and execute only fixed runtime file kinds, never configuration,
# descriptors, credentials, browser state, bootstrap or unrelated outputs.
foreach($file97 in Get-ChildItem -LiteralPath $built97 -Recurse -File){
 if(($file97.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Runtime reparse file refused'}
 if($file97.Name -notmatch '\.(dll|pdb|exe)$|\.(deps|runtimeconfig|staticwebassets\.endpoints)\.json$'){continue}
 $relative97=[IO.Path]::GetRelativePath($built97,$file97.FullName)
 if($relative97.StartsWith('..') -or [IO.Path]::IsPathRooted($relative97)){throw 'Runtime path escapes build directory'}
 $destination97=Join-Path $bin97 $relative97
 [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination97))|Out-Null
 Copy-Item -LiteralPath $file97.FullName -Destination $destination97
}
New-Item -ItemType Directory -Path (Join-Path $out 'sources') | Out-Null
Get-ChildItem -LiteralPath $project -File | Where-Object { $_.Name -in @('Program.cs','SplineTension97.cs','CopyFixture88.cs','Fixtures73.cs','Files73.cs','Options73.cs','Ownership73.cs','LoadedAddin73.cs','S1SplineTensionOwnedHost97.csproj','README.md') } | Copy-Item -Destination (Join-Path $out 'sources')
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
if((Get-FileHash -LiteralPath $addinSource).Hash -cne $candidate97){throw 'Frozen candidate96 SHA differs; no launch or manifest mutation allowed'}

New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item -LiteralPath $addinSource -Destination $stage
$stageDll=Join-Path $stage 'Inventor.So.AddIn.dll'
$stageSha=(Get-FileHash -LiteralPath $stageDll).Hash
if($stageSha -cne $candidate97){throw 'Frozen stage SHA differs'}
$xml.Addin.Assembly=[string]$stageDll
$result=[ordered]@{run_id=$runId;quest_proof=$false;physical_proof=$false;open_s1_gates=18;original_manifest_sha256=$manifestSha;original_dll_sha256=$installedSha;stage_dll_sha256=$stageSha}
$process=$null;$exitCode=1
try {
 $xml.Save($manifest)
 $stageManifestSha=(Get-FileHash -LiteralPath $manifest).Hash
 $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName='dotnet';$start.UseShellExecute=$false;$start.CreateNoWindow=$true
 $start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
 $args73=@((Join-Path $out 'bin/S1SplineTensionOwnedHost97.dll'),'--run-id',$runId,'--host','127.0.0.1','--output-dir',$privateOutput,'--expected-addin-path',$stageDll,'--expected-addin-sha256',$stageSha,'--manifest-path',$manifest,'--manifest-sha256',$stageManifestSha,'--installed-dll-path',$installed,'--installed-dll-sha256',$installedSha)
 if(@($args73|Where-Object {$_ -match '"' -or $_.EndsWith('\')}).Count){throw 'Unsupported argument encoding'}
 $start.Arguments=($args73|ForEach-Object {'"'+$_+'"'}) -join ' '
 $process=[Diagnostics.Process]::new();$process.StartInfo=$start
 if(-not $process.Start()){throw 'Host did not start'}
 $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
 $ready=Join-Path $privateOutput 'ready.json';$watch=[Diagnostics.Stopwatch]::StartNew()
 while(-not (Test-Path -LiteralPath $ready) -and -not $process.HasExited -and $watch.Elapsed.TotalSeconds -lt 180 -and [DateTimeOffset]::UtcNow -lt $wrapperDeadline97){Start-Sleep -Milliseconds 250}
 if(-not (Test-Path -LiteralPath $ready)){throw 'No owned ready attestation'}
 $metadata=ReadPublished91 $ready
 if($metadata.status -ne 'ready' -or $metadata.run_id -ne $runId -or $metadata.target_id -ne "inventor-2027-$($metadata.owned_pid)" -or $metadata.loaded_addin.sha256 -ne $stageSha){throw 'Owned attestation differs'}
 if($metadata.owned_pid -le 0 -or $metadata.owned_start_utc_ticks -le 0 -or $metadata.loaded_addin.pid -ne $metadata.owned_pid -or [IO.Path]::GetFullPath($metadata.loaded_addin.path) -cne $stageDll -or @($metadata.existing_processes_before|Where-Object {$_.pid -eq $metadata.owned_pid}).Count -ne 0){throw 'Owned PID/start or loaded module binding differs'}
 if($metadata.http_started -ne $false -or $metadata.bootstrap_exposed -ne $false){throw 'Dependency host must expose neither HTTP nor bootstrap'}
 $process.StandardInput.WriteLine('spline-tension97');$process.StandardInput.Flush()
 $probeFile91=Join-Path $privateOutput 'spline-tension97.json';$watch.Restart();$probe91=$null
 while(-not $process.HasExited -and $watch.Elapsed.TotalSeconds -lt 900 -and [DateTimeOffset]::UtcNow -lt $wrapperDeadline97){
  if(Test-Path -LiteralPath $probeFile91){
   try{$candidate91=ReadPublished91 $probeFile91;if($candidate91.status -cne 'RUNNING'){$probe91=$candidate91;break}}catch{}
  }
  Start-Sleep -Milliseconds 100
 }
 if($null -eq $probe91){throw 'No terminal native observation report; inspect the same owned process before another run'}
 $result.cases=@($probe91.cases).Count
 $result.exact_aborts=$probe91.exact_aborts; $result.setter_returned_cases=$probe91.setter_returned_cases; $result.api_error_observations=$probe91.api_error_observations
 $result.status=$probe91.status
 if($probe91.run_id -cne $runId -or $probe91.target_id -cne $metadata.target_id -or $probe91.status -cne 'OBSERVATION_ONLY' -or $result.cases -ne 60 -or $result.exact_aborts -ne 60 -or $probe91.candidate_sha256 -cne $candidate97){throw 'Spline tension97 research matrix or rollback incomplete'}
 $combinations97=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 foreach($row97 in $probe91.cases){
  if($row97.context -cnotin @('Part','Sheet') -or $row97.requested_fit_method -notin @(26369,26370,26371) -or $row97.requested_closed -isnot [bool] -or $row97.requested_tension -notin @(0,25,50,75,100) -or $row97.status -cne 'OBSERVATION_ONLY' -or $row97.full_native_abort_exact -ne $true){throw 'Research observation/rollback differs'}
  $combination97=$row97.context+':'+$row97.requested_fit_method+':'+$row97.requested_closed+':'+$row97.requested_tension
  if(-not $combinations97.Add($combination97)){throw 'Duplicate native observation'}
  $expectedName97='spline-tension97-case-'+([int]$row97.number).ToString('D3')+'.json'
  if($row97.sidecar -cne $expectedName97){throw 'Case sidecar filename differs'}
  $sidecarPath97=Join-Path $privateOutput $expectedName97
  if((Get-FileHash -LiteralPath $sidecarPath97).Hash -cne $row97.sidecar_file_sha256){throw 'Native sidecar file SHA differs'}
  $sidecar97=ReadPublished91 $sidecarPath97
  if($sidecar97.run_id -cne $runId -or $sidecar97.case_number -ne $row97.number -or $sidecar97.native_before_sha256 -cne $sidecar97.native_after_abort_sha256 -or $sidecar97.result.full_native_abort_exact -ne $true){throw 'Native sidecar binding/exact rollback differs'}
 }
 $result.sidecars_bound_and_hashed=60
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
   $remaining97=[Math]::Max(0,[Math]::Min(60000,[int]($wrapperDeadline97-[DateTimeOffset]::UtcNow).TotalMilliseconds))
  $result.worker_exited=$process.WaitForExit($remaining97)
   if($result.worker_exited){
    $result.worker_exit_code=$process.ExitCode
    $markers=$stdout.GetAwaiter().GetResult()
    if($markers -notmatch '\A(?:(?:READY|ERROR|INSPECTED|SPLINE_TENSION97_OBSERVED|STOPPED|STOP_FAILED)\r?\n)*\z' -or $stderr.GetAwaiter().GetResult().Length -ne 0){throw 'Unexpected host output suppressed'}
    [IO.File]::WriteAllText((Join-Path $out 'markers.txt'),$markers)
    if($process.ExitCode -ne 0){$exitCode=1}
    $stoppedPath97=Join-Path $privateOutput 'stopped.json'
    if(-not (Test-Path -LiteralPath $stoppedPath97)){throw 'No owned terminal cleanup report'}
    $stopped97=ReadPublished91 $stoppedPath97
    if($stopped97.run_id -cne $runId -or $stopped97.owned_pid -ne $metadata.owned_pid -or $stopped97.owned_start_utc_ticks -ne $metadata.owned_start_utc_ticks -or $stopped97.owned_pid_exited -ne $true -or $stopped97.owned_final_empty -ne $true -or $stopped97.existing_processes_preserved -ne $true){throw 'Owned cleanup/preservation not attested'}
    $result.owned_pid_exited=$stopped97.owned_pid_exited
    $result.preexisting_pid_start_preserved=$stopped97.existing_processes_preserved
   } else {$exitCode=1;$result.timeout_action='No kill/ROT; inspect ownership and journal before another native run'}
  }
 } catch {$result.stop_failure=$_.Exception.GetType().FullName;$exitCode=1}
 try {[IO.File]::WriteAllBytes($manifest,$original);$result.manifest_restored=(Get-FileHash -LiteralPath $manifest).Hash -eq $manifestSha;if(-not $result.manifest_restored){$exitCode=1}} catch {$result.manifest_restore_failure=$_.Exception.GetType().FullName;$exitCode=1}
 try {$result.original_dll_unchanged=(Get-FileHash -LiteralPath $installed).Hash -eq $installedSha;if(-not $result.original_dll_unchanged){$exitCode=1}} catch {$exitCode=1}
 try {
  $bootstrap=Join-Path $privateOutput 'bootstrap.json';if(Test-Path -LiteralPath $bootstrap){Remove-Item -LiteralPath $bootstrap}
  if(Test-Path -LiteralPath $privateOutput){
   New-Item -ItemType Directory -Path (Join-Path $out 'worker') | Out-Null
   Get-ChildItem -LiteralPath $privateOutput -File | Where-Object {$_.Name -in @('ready.json','stopped.json','ownership.json','spline-tension97.json') -or $_.Name -match '^inspect-[0-9]{3}\.json$' -or $_.Name -cmatch '^spline-tension97-case-[0-9]{3}\.json$'} | Copy-Item -Destination (Join-Path $out 'worker')
  }
 } catch {$exitCode=1}
 $result.exit_code=$exitCode
 $result|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $out 'wrapper-result.json') -Encoding utf8
 if($null -ne $process){$process.Dispose()}
}
exit $exitCode
