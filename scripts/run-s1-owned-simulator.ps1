#requires -Version 7.0
[CmdletBinding()]
param(
 [Parameter(Mandatory)][string]$OutputDirectory,
 [Parameter(Mandatory)][string]$AddinDirectory,
 [ValidateSet('full','closure','tension','points')][string]$Scope='full',
 [string]$HostDirectory=(Join-Path $PSScriptRoot '../bridge/tests/S1QuestOwnedHost73/bin/Debug/net10.0-windows')
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw 'Choose a new evidence directory'}
$addin=Join-Path ([IO.Path]::GetFullPath($AddinDirectory)) 'Inventor.So.AddIn.dll'
$hostDll=Join-Path ([IO.Path]::GetFullPath($HostDirectory)) 'S1QuestOwnedHost73.dll'
foreach($file in @($addin,$hostDll)){if(-not (Test-Path -LiteralPath $file -PathType Leaf)){throw 'Build the dedicated add-in and owned host first'}}
New-Item -ItemType Directory -Path $out | Out-Null
Add-Type -Path (Join-Path ([IO.Path]::GetFullPath($HostDirectory)) 'Newtonsoft.Json.dll')
function Read-OwnerJson([string]$Path){
 $stream=[IO.FileStream]::new($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
 try {
  if($stream.Length -lt 1 -or $stream.Length -gt 67108864){throw 'Owned metadata byte budget exceeded'}
  $reader=[IO.StreamReader]::new($stream,[Text.UTF8Encoding]::new($false),$true,4096,$true)
  try {
   $settings=[Newtonsoft.Json.JsonSerializerSettings]::new();$settings.DateParseHandling=[Newtonsoft.Json.DateParseHandling]::None
   return ,([Newtonsoft.Json.JsonConvert]::DeserializeObject($reader.ReadToEnd(),[Newtonsoft.Json.Linq.JObject],$settings))
  } finally {$reader.Dispose()}
 } finally {$stream.Dispose()}
}
function Wait-OwnerJson([string]$Path,[int]$Seconds){
 $deadline=[DateTimeOffset]::UtcNow.AddSeconds($Seconds)
 while($true){
  if(Test-Path -LiteralPath $Path){return ,(Read-OwnerJson $Path)}
  if($process.HasExited -or [DateTimeOffset]::UtcNow -gt $deadline){throw 'Owned host did not reach the required phase'}
  Start-Sleep -Milliseconds 250
 }
}
$run=[guid]::NewGuid().ToString('N')
$private=Join-Path $env:LOCALAPPDATA "InventorSO/s1-simulator-private-$run"
$manifest=Join-Path $env:APPDATA 'Autodesk/Inventor 2027/Addins/InventorSO/Inventor.So.addin'
$original=[IO.File]::ReadAllBytes($manifest);$originalSha=(Get-FileHash -LiteralPath $manifest).Hash
[IO.File]::WriteAllBytes((Join-Path $out 'original-addin-manifest.xml'),$original)
[xml]$xml=[IO.File]::ReadAllText($manifest)
$installed=[string]$xml.Addin.Assembly;$installedSha=(Get-FileHash -LiteralPath $installed).Hash
$addinSha=(Get-FileHash -LiteralPath $addin).Hash
@{run_id=$run;original_manifest_sha256=$originalSha;original_dll=$installed;original_dll_sha256=$installedSha;
  qa_dll=$addin;qa_dll_sha256=$addinSha;host_dll_sha256=(Get-FileHash -LiteralPath $hostDll).Hash;
  existing_processes=@(Get-Process Inventor -ErrorAction SilentlyContinue | ForEach-Object {@{pid=$_.Id;start_utc=$_.StartTime.ToUniversalTime().ToString('O')}})} |
 ConvertTo-Json -Depth 5 | Set-Content (Join-Path $out 'installation-before.json')
$process=$null;$ready=$null;$stopped=$null;$stageSha=$null;$code=1;$failure=$null
$previousPoints=$env:XR_SO_S1_SPLINE_POINTS_ONLY
$previousClosure=$env:XR_SO_S1_SPLINE_CLOSURE_ONLY;$previousTension=$env:XR_SO_S1_SPLINE_TENSION_ONLY
try {
 if((Get-FileHash -LiteralPath $manifest).Hash -ne $originalSha){throw 'Manifest changed before staging'}
 $xml.Addin.Assembly=[string]$addin;$xml.Save($manifest);$stageSha=(Get-FileHash -LiteralPath $manifest).Hash
 $info=[Diagnostics.ProcessStartInfo]::new();$info.FileName='dotnet';$info.UseShellExecute=$false;$info.CreateNoWindow=$true
 $info.RedirectStandardInput=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
 foreach($arg in @($hostDll,'--run-id',$run,'--host','127.0.0.1','--output-dir',$private,
  '--expected-addin-path',$addin,'--expected-addin-sha256',$addinSha,'--manifest-path',$manifest,'--manifest-sha256',$stageSha,
  '--installed-dll-path',$installed,'--installed-dll-sha256',$installedSha)){$info.ArgumentList.Add($arg)}
 $process=[Diagnostics.Process]::Start($info)
 $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
 $ready=Wait-OwnerJson (Join-Path $private 'ready.json') 240
 if([string]$ready['run_id'] -cne $run -or [string]$ready['target_id'] -cne "inventor-2027-$([int]$ready['owned_pid'])" -or
  [string]$ready['loaded_addin']['sha256'] -ine $addinSha){throw 'Owned target/module attestation differs'}
 $process.StandardInput.WriteLine('bootstrap');$process.StandardInput.Flush()
 $bootstrap=Wait-OwnerJson (Join-Path $private 'bootstrap.json') 30
 if([string]$bootstrap['run_id'] -cne $run -or [string]$bootstrap['target_id'] -cne [string]$ready['target_id']){throw 'Bootstrap target/run differs'}
 # The host generates credentials and the owner-only directory. Only add fixture metadata;
 # preserve the bootstrap's exact expiry string and never print or archive its token.
 $credentials=[Newtonsoft.Json.Linq.JObject]::new()
 $credentials['fixture_name']=[Newtonsoft.Json.Linq.JValue]::new('XR_S1_Quest_Acceptance_Part.ipt')
 $credentials['fixture_milestone']=[Newtonsoft.Json.Linq.JValue]::new('s1')
 $credentials['document_id']=$ready['part_document_id'].DeepClone();$credentials['s1_bootstrap']=$bootstrap.DeepClone()
 $credentialsPath=Join-Path $private 'simulator-credentials.json'
 [IO.File]::WriteAllText($credentialsPath,$credentials.ToString([Newtonsoft.Json.Formatting]::None),[Text.UTF8Encoding]::new($false))
 $env:XR_SO_S1_SPLINE_POINTS_ONLY=if($Scope -eq 'points'){'1'}else{$null}
 $env:XR_SO_S1_SPLINE_CLOSURE_ONLY=if($Scope -eq 'closure'){'1'}else{$null}
 $env:XR_SO_S1_SPLINE_TENSION_ONLY=if($Scope -eq 'tension'){'1'}else{$null}
 Write-Output "Owned QA ready; scope=$Scope; pid=$([int]$ready['owned_pid'])"
 & (Join-Path $repo 'Inventor XR SO/Tools~/Invoke-Simulator.ps1') -AcceptanceRunner s1 -LiveCredentials $credentialsPath -Output (Join-Path $out 'simulator')
 $code=$LASTEXITCODE
 $process.StandardInput.WriteLine('inspect');$process.StandardInput.Flush()
 $null=Wait-OwnerJson (Join-Path $private 'inspect-001.json') 60
 if($code -ne 0){throw 'Simulator did not pass its scoped acceptance cases'}
} catch {
 $failure=@{type=$_.Exception.GetType().FullName;hresult=$_.Exception.HResult};$code=1
} finally {
 $env:XR_SO_S1_SPLINE_POINTS_ONLY=$previousPoints
 $env:XR_SO_S1_SPLINE_CLOSURE_ONLY=$previousClosure;$env:XR_SO_S1_SPLINE_TENSION_ONLY=$previousTension
 $cleanup=[ordered]@{run_id=$run;input='synthetic';scope=$Scope;cad='native owned Inventor 2027';fixture_prepared=$null -ne $ready;
  native_writes_expected=$true;fixture_restored=$false;existing_processes_preserved=$false;manifest_restored=$false}
 if($failure){$cleanup.failure=$failure}
 try {
  if($null -ne $process){
   if(-not $process.HasExited){$process.StandardInput.WriteLine('stop');$process.StandardInput.Flush();$process.StandardInput.Close()}
   if(-not $process.WaitForExit(60000)){throw 'Owned host did not stop; no process kill or ROT cleanup is permitted'}
   $cleanup.host_exit_code=$process.ExitCode
   $markers=$stdout.GetAwaiter().GetResult();$errorText=$stderr.GetAwaiter().GetResult()
   if($markers -notmatch '\A(?:(?:READY|BOOTSTRAP_WRITTEN|INSPECTED|ERROR|STOPPED|STOP_FAILED)\r?\n)*\z' -or $errorText.Length -ne 0){throw 'Unexpected host output suppressed'}
   [IO.File]::WriteAllText((Join-Path $out 'host-markers.txt'),$markers)
   $stopped=Read-OwnerJson (Join-Path $private 'stopped.json')
   $cleanup.owned_pid=[int]$stopped['owned_pid'];$cleanup.owned_pid_absent=$null -eq (Get-Process -Id $cleanup.owned_pid -ErrorAction SilentlyContinue)
   $cleanup.fixture_restored=[int]$stopped['exit_code'] -eq 0 -and [bool]$stopped['owned_final_empty'] -and [bool]$stopped['owned_pid_exited'] -and $cleanup.owned_pid_absent
   $cleanup.existing_processes_preserved=[bool]$stopped['existing_processes_preserved']
   if(-not $cleanup.fixture_restored -or -not $cleanup.existing_processes_preserved){$code=1}
  }
 } catch {$cleanup.host_cleanup_error=$_.Exception.GetType().FullName;$code=1}
 try {
  $current=(Get-FileHash -LiteralPath $manifest).Hash
  if($current -ne $stageSha -and $current -ne $originalSha){throw 'Concurrent manifest change; preserve it and inspect the backup'}
  if($current -ne $originalSha){[IO.File]::WriteAllBytes($manifest,$original)}
  $cleanup.manifest_restored=(Get-FileHash -LiteralPath $manifest).Hash -eq $originalSha
  $cleanup.original_dll_unchanged=(Get-FileHash -LiteralPath $installed).Hash -eq $installedSha
  if(-not $cleanup.manifest_restored -or -not $cleanup.original_dll_unchanged){$code=1}
 } catch {$cleanup.manifest_cleanup_error=$_.Exception.GetType().FullName;$code=1}
 try {
  if(Test-Path -LiteralPath $private){
   foreach($name in @('bootstrap.json','simulator-credentials.json')){if(Test-Path -LiteralPath (Join-Path $private $name)){Remove-Item -LiteralPath (Join-Path $private $name)}}
   New-Item -ItemType Directory -Path (Join-Path $out 'host-evidence') | Out-Null
   Get-ChildItem -LiteralPath $private -File | Where-Object {$_.Name -in @('ready.json','stopped.json','ownership.json') -or $_.Name -match '^inspect-[0-9]{3}\.json$'} | Copy-Item -Destination (Join-Path $out 'host-evidence')
  }
  if($null -ne $stopped -and $cleanup.fixture_restored){
   $fixture=[IO.Path]::GetFullPath([string]$stopped['fixture_directory'])
   $prefix=Join-Path ([IO.Path]::GetTempPath()) "s1-ownedquest73-$run-"
   if(-not $fixture.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or (((Get-Item -LiteralPath $fixture).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)){throw 'Unexpected owned certificate directory'}
   $certificate=Join-Path $fixture 'host.pfx';if(Test-Path -LiteralPath $certificate){Remove-Item -LiteralPath $certificate}
   $cleanup.private_credentials_removed=$true
  }
 } catch {$cleanup.private_cleanup_error=$_.Exception.GetType().FullName;$code=1}
 $cleanup.exit_code=$code
 $cleanup | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $out 'native-cleanup.json')
 if($null -ne $process){$process.Dispose()}
}
if($code -ne 0){throw "Owned S1 simulator did not complete; inspect $out/native-cleanup.json"}
Write-Output 'PASS owned S1 simulator subset; fixture restored and preexisting processes preserved.'
