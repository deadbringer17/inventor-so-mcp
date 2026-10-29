#requires -Version 7.0
param([Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Add-Type -Path (Join-Path $repo 'bridge/src/server/bin/Debug/net8.0/Newtonsoft.Json.dll')
$root=[IO.Path]::GetFullPath($EvidenceDirectory)
function Require98([bool]$Condition,[string]$Reason){if(-not $Condition){throw $Reason}}
function Read98([string]$Name){
 Require98 ($Name -cmatch '\A(?:spline-point-edit98|spline-native98-[0-9]{3})\.json\z') 'Invalid evidence name'
 $path=Join-Path $root $Name
 Require98 (((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'Evidence must be a regular file'
 return ,([Newtonsoft.Json.Linq.JObject]::Parse([IO.File]::ReadAllText($path)))
}
function Compact98($Token){if($null -eq $Token){return 'null'};if($Token -isnot [Newtonsoft.Json.Linq.JToken]){$Token=[Newtonsoft.Json.Linq.JToken]::FromObject($Token)};return $Token.ToString([Newtonsoft.Json.Formatting]::None)}
function Equal98($A,$B){Require98 ((Compact98 $A) -ceq (Compact98 $B)) 'Native evidence differs'}
function Near98($A,$B,[double]$Tolerance=1e-5){
 Require98 ($null -ne $A -and $null -ne $B -and $A.Count -eq $B.Count -and $A.Count -gt 0) 'Geometry count differs'
 for($i=0;$i -lt $A.Count;$i++){
  Require98 ($A[$i].Type -in @('Integer','Float') -and $B[$i].Type -in @('Integer','Float')) 'Non-numeric geometry'
  $x=[double]$A[$i];$y=[double]$B[$i]
  Require98 ([double]::IsFinite($x) -and [double]::IsFinite($y) -and [Math]::Abs($x-$y) -le $Tolerance) 'Native oracle geometry differs'
 }
}
function Identity98($Before,$After){
 Equal98 $Before['key'] $After['key']
 Require98 ($Before['points'].Count -ge 2 -and $Before['points'].Count -eq $After['points'].Count) 'Native member cardinality differs'
 for($i=0;$i -lt $Before['points'].Count;$i++){Equal98 $Before['points'][$i]['key'] $After['points'][$i]['key']}
}
function Decode98([string]$Id,[string]$Document,[string]$Kind='sketch_entity'){
 Require98 ($Id.StartsWith('ent_',[StringComparison]::Ordinal) -and $Id.Length -le 131072) 'Invalid portable reference'
 $base=$Id.Substring(4).Replace('-','+').Replace('_','/')
 $base=$base.PadRight([int]([Math]::Ceiling($base.Length/4.0)*4),'=')
 $decoded=[Newtonsoft.Json.Linq.JObject]::Parse([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($base)))
 Require98 ([int]$decoded['Version'] -eq 1 -and [string]$decoded['DocumentId'] -ceq $Document -and
  [string]$decoded['EntityType'] -ceq $Kind -and [string]$decoded['Context'] -ceq '' -and [Convert]::FromBase64String([string]$decoded['Key']).Length -gt 0) 'Portable reference binding differs'
 return [string]$decoded['Key']
}
$report=Read98 'spline-point-edit98.json'
$pass='PASS_PLUGIN_NATIVE_SPLINE_POINT_EDIT_SUBSET'
Require98 ([int]$report['schema'] -eq 1 -and [string]$report['status'] -ceq $pass -and
 $report['cases'].Count -eq 128 -and $report['refusals'].Count -eq 14 -and [int]$report['raw_state_count'] -eq 540 -and
 [string]$report['candidate_sha256'] -ceq 'D70976D527A541CE0940B0DF13CD318C9775C9D55F548961758D1BFAEC1B2877') 'Incomplete native matrix or wrong candidate'
Require98 ([string]$report['run_id'] -cmatch '\A[0-9a-f]{32}\z' -and [string]$report['target_id'] -cmatch '\Ainventor-2027-[0-9]+\z') 'Invalid report identity'
$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
function State98($Reference){
 $name=[string]$Reference['file'];Require98 ($seen.Add($name)) 'Duplicate native sidecar reference'
 $raw=Read98 $name
 Require98 ([int]$raw['schema'] -eq 1 -and [string]$raw['run_id'] -ceq [string]$report['run_id'] -and [string]$raw['target_id'] -ceq [string]$report['target_id']) 'Foreign native sidecar'
 $bytes=[Text.Encoding]::UTF8.GetBytes((Compact98 $raw['native_state']))
 Require98 ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)) -ceq [string]$Reference['native_state_sha256']) 'Native sidecar SHA256 differs'
 return ,$raw['native_state']
}
function Contents98($State,[string]$Document){
 $copy=$State.DeepClone();$docs=@($copy['documents']|Where-Object{[string]$_['document_id'] -ceq $Document})
 Require98 ($docs.Count -eq 1) 'Cleanup document binding differs'
 $docs[0]['dirty'].Parent.Remove();$docs[0]['database_revision_id'].Parent.Remove()
 return ,$copy
}
$cases=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach($row in $report['cases']){
 Require98 ([string]$row['status'] -ceq $pass) 'Positive case did not pass'
 $key=([string]$row['context'])+'/'+([string]$row['variant'])+'/'+([int]$row['fit_method'])+'/'+([bool]$row['initial_closed'])+'/'+([int]$row['input_count'])+'/'+([string]$row['role'])
 Require98 ($cases.Add($key)) 'Repeated positive case'
 $prepared=State98 $row['native_before_case_preparation'];$before=State98 $row['native_before']
 $preview=State98 $row['native_after_preview'];$cleanup=State98 $row['native_after_case_cleanup']
 Equal98 $before $preview
 $document=[string]$row['state_before']['document_id'];$revision=[string]$row['state_before']['revision']
 Equal98 $row['state_before'] $row['state_after_preview']
 Require98 ($revision -cne [string]$row['state_after_commit']['revision'] -and [string]$row['state_after_commit']['document_id'] -ceq $document -and
  [string]$row['preview_result']['status'] -ceq 'preview_rolled_back' -and [string]$row['commit_result']['status'] -ceq 'committed') 'Preview/commit revision or result differs'
 Equal98 (Contents98 $prepared $document) (Contents98 $cleanup $document)
 Require98 ([bool]$row['oracle_abort_full_native_exact'] -and [bool]$row['event_revision_preserved_after_preview'] -and [bool]$row['native_identity_preserved'] -and [bool]$row['case_cleanup_native_contents_exact']) 'Native preservation assertion absent'
 Equal98 $row['oracle_before_sha256'] $row['oracle_after_abort_sha256']
 $initial=$row['native_initial'];$actual=$row['native_committed'];$oracle=$row['native_oracle'];$oracleInitial=$row['oracle_initial']
 $definitions=@($before['spline_definitions98']|Where-Object{[string]$_['document_id'] -ceq $document})
 Require98 ($definitions.Count -eq 1) 'Initial native sidecar document differs'
 $recorded=@(foreach($sketch in $definitions[0]['sketches']){foreach($family in @('fit','control')){foreach($curve in $sketch[$family]){if([string]$curve['key'] -ceq [string]$initial['key']){,$curve}}}})
 Require98 ($recorded.Count -eq 1) 'Initial native curve absent or ambiguous in sidecar'
 Equal98 $initial $recorded[0]
 Identity98 $initial $actual;Identity98 $oracleInitial $oracle
 Require98 ([string]$actual['key'] -cne [string]$oracle['key']) 'Oracle aliases product curve'
 $sourceKeys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 $null=$sourceKeys.Add([string]$initial['key']);foreach($point in $initial['points']){$null=$sourceKeys.Add([string]$point['key'])}
 Require98 (-not $sourceKeys.Contains([string]$oracleInitial['key'])) 'Oracle aliases product identity'
 foreach($point in $oracleInitial['points']){Require98 (-not $sourceKeys.Contains([string]$point['key'])) 'Oracle aliases product point'}
 foreach($pair in @(@($initial,$oracleInitial),@($actual,$oracle))){
  $a=$pair[0];$b=$pair[1]
  foreach($field in @('native_type','construction','reference','constraint_status','closed','fit_method','tension')){Equal98 $a[$field] $b[$field]}
  Near98 $a['domain'] $b['domain'] 1e-10;Near98 $a['samples_mm'] $b['samples_mm'];Near98 $a['strokes_mm'] $b['strokes_mm']
  Require98 ($a['points'].Count -eq $b['points'].Count) 'Oracle member count differs'
  for($i=0;$i -lt $a['points'].Count;$i++){
   Near98 $a['points'][$i]['xy'] $b['points'][$i]['xy']
   for($j=0;$j -lt $i;$j++){Require98 (([string]$a['points'][$i]['key'] -ceq [string]$a['points'][$j]['key']) -eq ([string]$b['points'][$i]['key'] -ceq [string]$b['points'][$j]['key'])) 'Oracle alias topology differs'}
  }
 }
 Require98 ([bool]$initial['closed'] -eq [bool]$row['initial_closed'] -and [bool]$actual['closed'] -eq [bool]$row['initial_closed'] -and [bool]$oracle['closed'] -eq [bool]$row['initial_closed'] -and [bool]$row['closure_preserved']) 'Closure differs'
 $options=$row['options'];$refs=$options['references'];$ordinal=[int]$row['selected_native_member_ordinal']
 $expectedOrdinal=switch([string]$row['role']){'Start'{1};'End'{$initial['points'].Count};'Interior'{[int][Math]::Floor(($initial['points'].Count+1)/2)};default{throw 'Invalid role'}}
 Require98 ($ordinal -eq $expectedOrdinal -and @($options.Properties()).Count -eq 2 -and $refs.Count -eq 2 -and $row['position_mm'].Count -eq 2) 'Point selection/options differ'
 Equal98 $options['position_mm'] $row['position_mm']
 $curveType=if([string]$row['variant'] -ceq 'spline_fit'){'spline'}else{'control_point_spline'}
 for($i=0;$i -lt 2;$i++){
  Require98 (@($refs[$i].Properties()).Count -eq 6 -and [string]$refs[$i]['document_id'] -ceq $document -and [string]$refs[$i]['revision'] -ceq $revision -and
   [string]$refs[$i]['sketch_id'] -ceq [string]$refs[0]['sketch_id']) 'Operation reference binding differs'
 }
 Require98 ([string]$refs[0]['type'] -ceq $curveType -and [string]$refs[0]['subpart'] -ceq 'Entity' -and
  [string]$refs[1]['type'] -ceq 'point' -and [string]$refs[1]['subpart'] -ceq 'Point') 'Operation reference type differs'
 Equal98 (Decode98 ([string]$refs[0]['entity_id']) $document) ([string]$initial['key'])
 Equal98 (Decode98 ([string]$refs[1]['entity_id']) $document) ([string]$row['selected_native_point_key'])
 Equal98 $initial['points'][$ordinal-1]['key'] $row['selected_native_point_key']
 Near98 $actual['points'][$ordinal-1]['xy'] $row['actual_selected_position_mm'];Near98 $oracle['points'][$ordinal-1]['xy'] $row['oracle_selected_position_mm']
 Near98 $row['actual_selected_position_mm'] $row['oracle_selected_position_mm']
 for($i=0;$i -lt 2;$i++){
  Require98 ([Math]::Abs([double]$row['position_mm'][$i]-([double]$initial['points'][$ordinal-1]['xy'][$i]+@(3,-2)[$i])) -le 1e-10) 'Requested displacement differs'
  foreach($prefix in @('actual','oracle')){Require98 ([Math]::Abs([double]$row[$prefix+'_request_residual_mm'][$i]-([double]$row[$prefix+'_selected_position_mm'][$i]-[double]$row['position_mm'][$i])) -le 1e-10) 'Recorded residual differs'}
 }
 $snapshot=$row['first_snapshot'];Require98 ([int]$row['first_snapshot_attempts'] -eq 1 -and [string]$snapshot['document_id'] -ceq $document -and
  [string]$snapshot['revision'] -ceq [string]$row['state_after_commit']['revision'] -and [string]$snapshot['sketch_id'] -ceq [string]$refs[0]['sketch_id'] -and
  $snapshot['partial'].Type -eq 'Boolean' -and -not [bool]$snapshot['partial'] -and [string]$snapshot['mutation_completeness']['state'] -ceq 'complete' -and [int]$snapshot['mutation_completeness']['read_error_count'] -eq 0) 'First snapshot incomplete or binding differs'
 $map=[Collections.Generic.Dictionary[string,Newtonsoft.Json.Linq.JToken]]::new([StringComparer]::Ordinal)
 foreach($entity in $snapshot['entities']){$nativeKey=Decode98 ([string]$entity['id']) $document;Require98 (-not $map.ContainsKey($nativeKey)) 'Snapshot identity duplicated';$map[$nativeKey]=$entity}
 foreach($collection in @('entities','constraints','dimensions')){Require98 ($snapshot[$collection].Count -eq [int]$snapshot['mutation_completeness']['collections'][$collection]['native_count'] -and $snapshot[$collection].Count -eq [int]$snapshot['mutation_completeness']['collections'][$collection]['represented_count']) 'Snapshot collection coverage differs'}
 $shown=$map[[string]$actual['key']];Require98 ($null -ne $shown -and [bool]$shown['supported'] -and [string]$shown['type'] -ceq $curveType) 'Committed curve absent from first snapshot'
 foreach($field in @('native_type','construction','closed')){Equal98 $shown[$field] $actual[$field]}
 if($null -ne $shown['reference']){Equal98 $shown['reference'] $actual['reference']}
 Require98 ([bool]$row['first_snapshot_reference_flag_exposed'] -eq ($null -ne $shown['reference'])) 'Reference exposure differs'
 Near98 $shown['strokes_mm'] $actual['strokes_mm']
 $countField=if($curveType -ceq 'spline'){'fit_point_count'}else{'control_point_count'}
 Require98 ([int]$shown[$countField] -eq $actual['points'].Count) 'Snapshot point cardinality differs'
 if($curveType -ceq 'spline'){Equal98 $shown['fit_method'] $actual['fit_method'];Require98 ([int]$actual['fit_method'] -eq [int]$row['fit_method']) 'Fit method differs'}
 foreach($point in $actual['points']){$displayed=$map[[string]$point['key']];Require98 ($null -ne $displayed -and [string]$displayed['type'] -ceq 'point') 'Snapshot member missing';Near98 $displayed['point_mm'] $point['xy']}
}
$refusals=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach($row in $report['refusals']){
 $kind=[string]$row['kind'];Require98 ($refusals.Add(([string]$row['context'])+'/'+$kind)) 'Repeated refusal'
 $expectedCode=if($kind -ceq 'stale_reference'){'STALE_REVISION'}else{'INVALID_ARGUMENT'}
 $expectedStep=if($kind -ceq 'failure_after_valid_update'){1}else{0}
 Require98 ($row['preview'].Type -eq 'Boolean' -and -not [bool]$row['preview'] -and $row['operations'].Count -eq $expectedStep+1 -and
  [string]$row['operations'][0]['command'] -ceq 'edit_sketch' -and [string]$row['operations'][0]['arguments']['variant'] -ceq 'spline_point_update') 'Refusal is not the expected attempted atomic commit'
 Require98 ([string]$row['status'] -ceq $pass -and [string]$row['code'] -ceq 'ROLLED_BACK' -and [string]$row['details']['step_code'] -ceq $expectedCode -and
  [int]$row['details']['step_index'] -eq $expectedStep -and [bool]$row['event_revision_preserved'] -and [bool]$row['full_native_rollback_exact']) 'Refusal structured result differs'
 Equal98 (State98 $row['native_before']) (State98 $row['native_after']);Equal98 $row['state_before'] $row['state_after']
}
foreach($context in @('Part','Sheet')){
 foreach($variant in @('spline_fit','spline_control')){
  $methods=if($variant -ceq 'spline_fit'){@(26369,26370,26371)}else{@(0)}
  foreach($method in $methods){foreach($closed in @($false,$true)){foreach($count in @(2,5,64)){
   $roles=if($count -eq 2){@('Start','End')}else{@('Start','Interior','End')}
   foreach($role in $roles){Require98 ($cases.Contains("$context/$variant/$method/$closed/$count/$role")) 'Missing matrix combination'}
  }}}
 }
 foreach($kind in @('different_spline_same_sketch','stale_reference','foreign_document_point','wrong_point_type','malformed_position','swapped_references','failure_after_valid_update')){Require98 ($refusals.Contains("$context/$kind")) 'Missing refusal combination'}
 $cleanups=@($report['refusal_fixture_cleanup']|Where-Object{[string]$_['context'] -ceq $context});Require98 ($cleanups.Count -eq 1 -and [bool]$cleanups[0]['native_contents_exact']) 'Refusal fixture cleanup absent'
 $before=$cleanups[0]['before'];$after=$cleanups[0]['after'];$doc=@($before['documents']|Where-Object{[string]$_['context'] -ceq $(if($context -ceq 'Part'){'part'}else{'sheetmetal'})})
 Require98 ($doc.Count -eq 1) 'Refusal fixture context differs';Equal98 (Contents98 $before ([string]$doc[0]['document_id'])) (Contents98 $after ([string]$doc[0]['document_id']))
}
Require98 ($seen.Count -eq 540 -and @(Get-ChildItem -LiteralPath $root -Filter 'spline-native98-*.json').Count -eq 540) 'Native sidecar inventory differs'
[pscustomobject]@{status='VERIFIED_NATIVE_SPLINE_POINT98_SUBSET';cases=128;refusals=14;sidecars=540;full_s1_pass=$false;quest_proof=$false;scope='Recorded native matrix, snapshot, oracle geometry and rollback; not every Inventor property or NURBS coefficient'}|ConvertTo-Json
