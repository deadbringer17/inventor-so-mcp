#requires -Version 7.0
param([Parameter(Mandatory=$true)][string]$OutputDirectory,[Parameter(Mandatory=$true)][string]$AddinAssembly)
$ErrorActionPreference='Stop'
$wrapperDeadline98=[DateTimeOffset]::UtcNow.AddMinutes(35)
$candidate98='D70976D527A541CE0940B0DF13CD318C9775C9D55F548961758D1BFAEC1B2877'
$addinInput98=Get-Item -LiteralPath ([IO.Path]::GetFullPath($AddinAssembly))
if($addinInput98.PSIsContainer -or ($addinInput98.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or $addinInput98.Name -cne 'Inventor.So.AddIn.dll' -or (Get-FileHash -LiteralPath $addinInput98.FullName).Hash -cne $candidate98){throw 'Frozen producer98 candidate required'}
function ReadPublished91([string]$path) {
 $share=[IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete
 $stream=[IO.FileStream]::new($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,$share)
 try {
  if($stream.Length -lt 1 -or $stream.Length -gt 67108864){throw 'Published metadata byte budget exceeded'}
  $reader=[IO.StreamReader]::new($stream,[Text.UTF8Encoding]::new($false),$true,4096,$true)
  try {return ($reader.ReadToEnd() | ConvertFrom-Json)} finally {$reader.Dispose()}
 } finally {$stream.Dispose()}
}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw 'New evidence directory required'}
New-Item -ItemType Directory -Path $out | Out-Null
$project=Join-Path $repo 'bridge/tests/S1SplinePointEditOwnedHost98'
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
[IO.File]::WriteAllBytes((Join-Path $out 'original-addin-manifest.xml'),$original)
@{manifest_path=$manifest;manifest_sha256=$manifestSha;installed_dll_path=$installed;installed_dll_sha256=$installedSha;
  existing_processes=@(Get-Process Inventor -ErrorAction SilentlyContinue|ForEach-Object{@{pid=$_.Id;start_utc=$_.StartTime.ToUniversalTime().ToString('O')}})} |
 ConvertTo-Json -Depth 4|Set-Content (Join-Path $out 'original-installation.json')
$runId=[Guid]::NewGuid().ToString('N')
$stage=Join-Path $env:LOCALAPPDATA "InventorSO/s1-ownedhost73-$runId"
$privateOutput=Join-Path $env:LOCALAPPDATA "InventorSO/s1-host73-output-$runId"
$addinSource=[IO.Path]::GetFullPath($AddinAssembly)

New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item -LiteralPath $addinSource -Destination $stage
$stageDll=Join-Path $stage 'Inventor.So.AddIn.dll'
$stageSha=(Get-FileHash -LiteralPath $stageDll).Hash
if($stageSha -cne $candidate98){throw 'Stage98 candidate differs'}
New-Item -ItemType Directory -Path (Join-Path $out 'producer98') | Out-Null
Copy-Item -LiteralPath $stageDll -Destination (Join-Path $out 'producer98/Inventor.So.AddIn.dll')
$xml.Addin.Assembly=[string]$stageDll
$result=[ordered]@{run_id=$runId;quest_proof=$false;physical_proof=$false;open_s1_gates=18;original_manifest_sha256=$manifestSha;original_dll_sha256=$installedSha;stage_dll_sha256=$stageSha}
$process=$null;$exitCode=1;$stageManifestSha=$null
try {
 if((Get-FileHash -LiteralPath $manifest).Hash -cne $manifestSha){throw 'Manifest changed before staging'}
 $xml.Save($manifest)
 $stageManifestSha=(Get-FileHash -LiteralPath $manifest).Hash
 $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName='dotnet';$start.UseShellExecute=$false;$start.CreateNoWindow=$true
 $start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
 $args73=@((Join-Path $out 'bin/S1SplinePointEditOwnedHost98.dll'),'--run-id',$runId,'--host','127.0.0.1','--output-dir',$privateOutput,'--expected-addin-path',$stageDll,'--expected-addin-sha256',$stageSha,'--manifest-path',$manifest,'--manifest-sha256',$stageManifestSha,'--installed-dll-path',$installed,'--installed-dll-sha256',$installedSha)
 if(@($args73|Where-Object {$_ -match '"' -or $_.EndsWith('\')}).Count){throw 'Unsupported argument encoding'}
 foreach($argument in $args73){$start.ArgumentList.Add($argument)}
 $process=[Diagnostics.Process]::new();$process.StartInfo=$start
 if(-not $process.Start()){throw 'Host did not start'}
 $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
 $ready=Join-Path $privateOutput 'ready.json';$watch=[Diagnostics.Stopwatch]::StartNew()
 while(-not (Test-Path -LiteralPath $ready) -and -not $process.HasExited -and $watch.Elapsed.TotalSeconds -lt 180){Start-Sleep -Milliseconds 250}
 if(-not (Test-Path -LiteralPath $ready)){throw 'No owned ready attestation'}
 $metadata=ReadPublished91 $ready
 if($metadata.status -ne 'ready' -or $metadata.run_id -ne $runId -or $metadata.target_id -ne "inventor-2027-$($metadata.owned_pid)" -or $metadata.loaded_addin.sha256 -ne $stageSha){throw 'Owned attestation differs'}
 if($metadata.http_started -ne $false -or $metadata.bootstrap_exposed -ne $false){throw 'Dependency host must expose neither HTTP nor bootstrap'}
 $process.StandardInput.WriteLine('spline-point-edit98');$process.StandardInput.Flush()
 $probeFile91=Join-Path $privateOutput 'spline-point-edit98.json';$watch.Restart();$probe91=$null
 while(-not $process.HasExited -and $watch.Elapsed.TotalSeconds -lt 1980 -and [DateTimeOffset]::UtcNow -lt $wrapperDeadline98.AddSeconds(-90)){
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
 if($probe91.run_id -cne $runId -or $probe91.target_id -cne $metadata.target_id -or $probe91.status -cne 'PASS_PLUGIN_NATIVE_SPLINE_POINT_EDIT_SUBSET' -or $result.cases -ne 128 -or $result.refusals -ne 14 -or $result.raw_states -ne 540){throw 'Spline point edit98 matrix incomplete'}
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
    if($markers -notmatch '\A(?:(?:READY|ERROR|INSPECTED|SPLINE_POINT_EDIT98_VERIFIED|STOPPED|STOP_FAILED)\r?\n)*\z' -or $stderr.GetAwaiter().GetResult().Length -ne 0){throw 'Unexpected host output suppressed'}
    [IO.File]::WriteAllText((Join-Path $out 'markers.txt'),$markers)
    if($process.ExitCode -ne 0){$exitCode=1}
   } else {$exitCode=1;$result.timeout_action='No kill/ROT; inspect ownership and journal before another native run'}
  }
 } catch {$result.stop_failure=$_.Exception.GetType().FullName;$exitCode=1}
 try {
  $currentManifestSha=(Get-FileHash -LiteralPath $manifest).Hash
  if($currentManifestSha -cne $stageManifestSha -and $currentManifestSha -cne $manifestSha){throw 'Concurrent manifest change; preserve it and inspect backup'}
  if($currentManifestSha -cne $manifestSha){[IO.File]::WriteAllBytes($manifest,$original)}
  $result.manifest_restored=(Get-FileHash -LiteralPath $manifest).Hash -eq $manifestSha
  if(-not $result.manifest_restored){$exitCode=1}
 } catch {$result.manifest_restore_failure=$_.Exception.GetType().FullName;$exitCode=1}
 try {$result.original_dll_unchanged=(Get-FileHash -LiteralPath $installed).Hash -eq $installedSha;if(-not $result.original_dll_unchanged){$exitCode=1}} catch {$exitCode=1}
 try {
  $bootstrap=Join-Path $privateOutput 'bootstrap.json';if(Test-Path -LiteralPath $bootstrap){Remove-Item -LiteralPath $bootstrap}
  if(Test-Path -LiteralPath $privateOutput){
   New-Item -ItemType Directory -Path (Join-Path $out 'worker') | Out-Null
   Get-ChildItem -LiteralPath $privateOutput -File | Where-Object {$_.Name -in @('ready.json','stopped.json','ownership.json','spline-point-edit98.json') -or $_.Name -match '^inspect-[0-9]{3}\.json$' -or $_.Name -cmatch '^spline-native98-[0-9]{3}\.json$'} | Copy-Item -Destination (Join-Path $out 'worker')
   $stopped=ReadPublished91 (Join-Path $privateOutput 'stopped.json')
   $result.owned_pid=$stopped.owned_pid
   $result.owned_pid_absent=$null -eq (Get-Process -Id ([int]$stopped.owned_pid) -ErrorAction SilentlyContinue)
   # A failed test can still clean up completely; keep both outcomes separate.
   $result.fixture_restored=$stopped.owned_final_empty -eq $true -and $stopped.owned_pid_exited -eq $true -and $result.owned_pid_absent -and $null -eq $stopped.native_cleanup_failure
   $result.existing_processes_preserved=$stopped.existing_processes_preserved -eq $true
   if(-not $result.fixture_restored -or -not $result.existing_processes_preserved){$exitCode=1}
  }
 } catch {$exitCode=1}
 $result.exit_code=$exitCode
 $result.wrapper_deadline_utc=$wrapperDeadline98.ToString('O')
 $result|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $out 'wrapper-result.json') -Encoding utf8
 if($null -ne $process){$process.Dispose()}
}
exit $exitCode
