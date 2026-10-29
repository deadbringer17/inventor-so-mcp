#requires -Version 7.0
param([Parameter(Mandatory)][string]$EvidenceDirectory,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Add-Type -Path (Join-Path $repo 'bridge/src/server/bin/Debug/net8.0/Newtonsoft.Json.dll')
$source=[IO.Path]::GetFullPath($EvidenceDirectory);$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw 'Choose a new negative-test directory'}
$originalReport=Join-Path $source 'spline-point-edit98.json'
$originalSha=(Get-FileHash -LiteralPath $originalReport).Hash
$baseline=[Newtonsoft.Json.Linq.JObject]::Parse([IO.File]::ReadAllText($originalReport))
if([string]$baseline['status'] -cne 'PASS_PLUGIN_NATIVE_SPLINE_POINT_EDIT_SUBSET' -or $baseline['cases'].Count -ne 128){throw 'Completed native baseline required'}
New-Item -ItemType Directory -Path $out|Out-Null
$first=$baseline['cases'][0]
$names=@('native_before_case_preparation','native_before','native_after_preview','native_after_case_cleanup'|ForEach-Object{[string]$first[$_]['file']})
$originalRaw=@{};foreach($name in $names){
 if($name -cnotmatch '\Aspline-native98-[0-9]{3}\.json\z'){throw 'Invalid first-case sidecar name'}
 $originalRaw[$name]=(Get-FileHash -LiteralPath (Join-Path $source $name)).Hash
}
$rows=@()
foreach($kind in @('oracle_geometry','requested_position','first_snapshot','native_sidecar','missing_case')){
 $folder=Join-Path $out $kind;New-Item -ItemType Directory -Path $folder|Out-Null
 # Deliberately copy only the four first-case sidecars: every altered first case
 # must fail for its exact invariant before traversal reaches any later file.
 foreach($name in $names){Copy-Item -LiteralPath (Join-Path $source $name) -Destination $folder}
 $report=$baseline.DeepClone();$row=$report['cases'][0]
 switch($kind){
  'oracle_geometry' {$row['native_oracle']['samples_mm'][0]=[Newtonsoft.Json.Linq.JValue]::new([double]$row['native_oracle']['samples_mm'][0]+1);$expected='Native oracle geometry differs'}
  'requested_position' {
   $v=[double]$row['position_mm'][0]+1;$row['position_mm'][0]=[Newtonsoft.Json.Linq.JValue]::new($v);$row['options']['position_mm'][0]=[Newtonsoft.Json.Linq.JValue]::new($v)
   $expected='Requested displacement differs'
  }
  'first_snapshot' {$row['first_snapshot']['partial']=[Newtonsoft.Json.Linq.JValue]::new($true);$expected='First snapshot incomplete or binding differs'}
  'native_sidecar' {
   $path=Join-Path $folder $names[0];$raw=[Newtonsoft.Json.Linq.JObject]::Parse([IO.File]::ReadAllText($path));$raw['native_state']['negative_test']=[Newtonsoft.Json.Linq.JValue]::new('altered copy')
   [IO.File]::WriteAllText($path,$raw.ToString([Newtonsoft.Json.Formatting]::None));$expected='Native sidecar SHA256 differs'
  }
  'missing_case' {$report['cases'][0].Remove();$expected='Incomplete native matrix or wrong candidate'}
 }
 [IO.File]::WriteAllText((Join-Path $folder 'spline-point-edit98.json'),$report.ToString([Newtonsoft.Json.Formatting]::None))
 $rejected=$false;$reason=''
 try {$unexpected=& (Join-Path $PSScriptRoot 'verify-s1-spline-point98.ps1') -EvidenceDirectory $folder}
 catch {$reason=$_.Exception.Message;$rejected=$reason -ceq $expected}
 $rows+=[pscustomobject]@{mutation=$kind;rejected_for_expected_reason=$rejected;expected=$expected;actual=$reason}
 if(-not $rejected){$rows|ConvertTo-Json -Depth 4|Set-Content (Join-Path $out 'results.json');throw 'Verifier negative test did not fail for the expected invariant'}
}
$unchanged=(Get-FileHash -LiteralPath $originalReport).Hash -ceq $originalSha
foreach($name in $names){$unchanged=$unchanged -and (Get-FileHash -LiteralPath (Join-Path $source $name)).Hash -ceq $originalRaw[$name]}
if(-not $unchanged){throw 'Original evidence changed'}
[ordered]@{passed=$true;original_report_sha256=$originalSha;original_evidence_unchanged=$unchanged;cases=$rows;note='Offline corrupted-copy tests, no CAD. Four copied first-case sidecars per negative fixture; later sidecars intentionally absent. Exact expected rejection is required.'}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $out 'results.json')
'PASS: five corrupted copies rejected for their expected invariants; original evidence unchanged.'
