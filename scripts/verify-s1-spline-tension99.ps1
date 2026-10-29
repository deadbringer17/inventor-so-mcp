#requires -Version 7.0
param([Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Add-Type -Path (Join-Path $repo 'bridge/src/server/bin/Debug/net8.0/Newtonsoft.Json.dll')
$root=[IO.Path]::GetFullPath($EvidenceDirectory)
function Require99([bool]$Condition,[string]$Reason){if(-not $Condition){throw $Reason}}
function Read99([string]$Name){
 Require99 ($Name -match '\A(?:spline-tension-edit99|spline-native99-[0-9]{3})\.json\z') 'Invalid evidence name'
 return ,([Newtonsoft.Json.Linq.JObject]::Parse([IO.File]::ReadAllText((Join-Path $root $Name))))
}
function Compact99($Token){return $Token.ToString([Newtonsoft.Json.Formatting]::None)}
function Equal99($A,$B){Require99 ((Compact99 $A) -ceq (Compact99 $B)) 'Native evidence differs'}
$report=Read99 'spline-tension-edit99.json'
$pass='PASS_PLUGIN_NATIVE_SPLINE_TENSION_EDIT_SUBSET'
Require99 ([string]$report['status'] -ceq $pass -and $report['cases'].Count -eq 60 -and $report['refusals'].Count -eq 16 -and [int]$report['raw_state_count'] -eq 272) 'Incomplete native matrix'
$seen=[Collections.Generic.HashSet[string]]::new()
function State99($Reference){
 $name=[string]$Reference['file'];Require99 ($seen.Add($name)) 'Duplicate native sidecar reference'
 $raw=Read99 $name
 Require99 ([string]$raw['run_id'] -ceq [string]$report['run_id'] -and [string]$raw['target_id'] -ceq [string]$report['target_id']) 'Foreign native sidecar'
 $bytes=[Text.Encoding]::UTF8.GetBytes((Compact99 $raw['native_state']))
 $sha=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
 Require99 ($sha -ieq [string]$Reference['native_state_sha256']) 'Native sidecar SHA256 differs'
 return ,$raw['native_state']
}
$cases=[Collections.Generic.HashSet[string]]::new()
foreach($row in $report['cases']){
 Require99 ([string]$row['status'] -ceq $pass) 'Positive case did not pass'
 $caseKey=([string]$row['context'])+'/'+([int]$row['fit_method'])+'/'+([bool]$row['initial_closed'])+'/'+([int]$row['requested_tension'])
 Require99 ($cases.Add($caseKey)) 'Repeated positive case'
 $prepared=State99 $row['native_before_case_preparation']
 $before=State99 $row['native_before'];$preview=State99 $row['native_after_preview'];$cleanup=State99 $row['native_after_case_cleanup']
 Equal99 $before $preview
 Require99 ([string]$row['state_before']['revision'] -ceq [string]$row['state_after_preview']['revision'] -and
  [string]$row['state_before']['revision'] -cne [string]$row['state_after_commit']['revision'] -and [int]$row['first_snapshot_attempts'] -eq 1) 'Preview/commit revision or first snapshot differs'
 Require99 ([bool]$row['oracle_abort_full_native_exact'] -and [bool]$row['native_identity_preserved'] -and [bool]$row['case_cleanup_native_contents_exact']) 'Native preservation assertion absent'
 Require99 ([string]$row['native_initial']['key'] -ceq [string]$row['native_committed']['key'] -and
  [string]$row['native_committed']['key'] -cne [string]$row['native_oracle']['key']) 'Product/oracle identity differs'
 Equal99 $row['oracle_before_sha256'] $row['oracle_after_abort_sha256']
 foreach($field in @('native_type','construction','reference','constraint_status','closed','fit_method','tension')){
  Equal99 $row['native_committed'][$field] $row['native_oracle'][$field]
 }
 Require99 ([int]$row['native_committed']['tension'] -eq [int]$row['requested_tension']) 'Requested tension differs'
 foreach($field in @('domain','samples_mm','strokes_mm')){
  $a=$row['native_committed'][$field];$b=$row['native_oracle'][$field]
  Require99 ($a.Count -eq $b.Count -and $a.Count -gt 0) 'Oracle geometry count differs'
  $tolerance=if($field -eq 'domain'){1e-10}else{1e-5}
  for($i=0;$i -lt $a.Count;$i++){
   Require99 ([double]::IsFinite([double]$a[$i]) -and [double]::IsFinite([double]$b[$i]) -and [Math]::Abs([double]$a[$i]-[double]$b[$i]) -le $tolerance) 'Oracle geometry differs'
  }
 }
}
foreach($row in $report['refusals']){
 Require99 ([string]$row['status'] -ceq $pass -and [string]$row['code'] -ceq 'ROLLED_BACK' -and
  [string]$row['details']['step_code'] -ceq [string]$row['expected_step_code']) 'Refusal result differs'
 Equal99 (State99 $row['native_before']) (State99 $row['native_after'])
 Equal99 $row['state_before']['revision'] $row['state_after']['revision']
}
foreach($context in @('Part','Sheet')){foreach($method in 26369,26370,26371){foreach($closed in $false,$true){foreach($tension in 0,25,50,75,100){
 Require99 ($cases.Contains("$context/$method/$closed/$tension")) 'Missing matrix combination'
}}}}
Require99 ($seen.Count -eq 272 -and @(Get-ChildItem -LiteralPath $root -Filter 'spline-native99-*.json').Count -eq 272) 'Native sidecar inventory differs'
[pscustomobject]@{status='VERIFIED_NATIVE_TENSION99_SUBSET';cases=60;refusals=16;sidecars=272;full_s1_pass=$false;quest_proof=$false} | ConvertTo-Json
