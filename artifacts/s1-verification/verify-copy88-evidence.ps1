param([Parameter(Mandatory=$true)][string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($EvidenceDirectory)
$output=Join-Path $root 'verification-r1.json'
if(Test-Path -LiteralPath $output){throw 'Verification evidence already exists'}
function Require88([bool]$Condition,[string]$Assertion){if(-not $Condition){throw $Assertion}}
function Read88([string]$Relative){Get-Content -LiteralPath (Join-Path $root $Relative) -Raw|ConvertFrom-Json}
$wrapper=Read88 'wrapper-result.json'
$client=Read88 'worker/copy-client88.json'
$native=Read88 'worker/copy-native-verifier88.json'
$stopped=Read88 'worker/stopped.json'
Require88 ($wrapper.exit_code -eq 0 -and $wrapper.client_exit -eq 0 -and $wrapper.worker_exit_code -eq 0) 'Wrapper or worker failed'
Require88 ($wrapper.manifest_restored -eq $true -and $wrapper.original_dll_unchanged -eq $true) 'Installation restoration absent'
Require88 ($stopped.http_stopped -eq $true -and $stopped.owned_final_empty -eq $true -and $stopped.owned_pid_exited -eq $true -and $stopped.existing_processes_preserved -eq $true) 'Native cleanup absent'
Require88 ($client.status -ceq 'PASS_PC_REPRESENTED_SUBSET' -and $native.status -ceq 'PASS_NATIVE_READBACK_SUBSET') 'Scoped terminal result differs'
Require88 ($wrapper.run_id -ceq $client.run_id -and $native.run_id -ceq $client.run_id -and $stopped.run_id -ceq $client.run_id -and $native.target_id -ceq $client.target_id) 'Evidence binding differs'
Require88 (@($client.cases).Count -eq 6 -and @($client.refusals).Count -eq 10 -and @($native.checks).Count -eq 8) 'Evidence matrix count differs'
$cases=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$nativePairs=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach($check in $native.checks){
 Require88 ($check.native_translation_and_original_exact -eq $true -and $check.independent_native_keys_points_glyphs -eq $true -and $check.source_key -cne $check.copy_key) 'Native pair comparison absent'
 Require88 ($nativePairs.Add("$($check.document_id)|$($check.source_key)|$($check.copy_key)|$($check.type)")) 'Duplicate native pair'
}
foreach($case in $client.cases){
 Require88 ($cases.Add("$($case.context)|$($case.workflow)")) 'Duplicate workflow'
 Require88 ($case.first_snapshot_immediate_after_last_commit -eq $true -and $case.source_represented_exact -eq $true -and $case.represented_preview_rollback_exact -eq $true -and $case.synthetic_pc_confirmation -eq $true) 'Workflow evidence absent'
 Require88 (@($case.pairs).Count -eq $(if($case.workflow -ceq 'mixed'){2}else{1})) 'Workflow pair count differs'
 foreach($pair in $case.pairs){
  $keys=@()
  foreach($reference in @($pair.source,$pair.copy)){
   Require88 ($reference.document_id -ceq $case.document_id -and $reference.type -ceq $pair.type -and $reference.subpart -ceq 'Entity') 'Pair reference binding differs'
   $id=[string]$reference.entity_id
   Require88 ($id.StartsWith('ent_',[StringComparison]::Ordinal)) 'Portable native reference absent'
   $encoded=$id.Substring(4).Replace('-','+').Replace('_','/')
   $encoded=$encoded.PadRight($encoded.Length+(4-$encoded.Length%4)%4,'=')
   $decoded=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($encoded))|ConvertFrom-Json
   Require88 ($decoded.DocumentId -ceq $case.document_id -and $decoded.EntityType -ceq $(if($pair.type -ceq 'text'){'sketch_text'}else{'sketch_entity'}) -and $decoded.Context -ceq '') 'Decoded native reference differs'
   $keys += [string]$decoded.Key
  }
  Require88 ($nativePairs.Contains("$($case.document_id)|$($keys[0])|$($keys[1])|$($pair.type)")) 'Client pair missing in independent native verifier'
 }
}
foreach($context in @('Part','Sheet')){foreach($workflow in @('line','text','mixed')){Require88 ($cases.Contains("$context|$workflow")) 'Missing context/workflow'}}
$refusals=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach($refusal in $client.refusals){
 Require88 ($refusals.Add("$($refusal.document_id)|$($refusal.kind)")) 'Duplicate refusal'
 Require88 ($refusal.represented_rollback_exact -eq $true -and $refusal.event_revision_preserved -eq $true -and $refusal.full_native_checkpoint_exact -eq $true) 'Refusal comparison absent'
 if($refusal.kind -ceq 'post_valid_copy_failure'){Require88 ($refusal.code -ceq 'ROLLED_BACK' -and $refusal.step_code -ceq 'INVALID_ARGUMENT' -and $refusal.step_index -eq 1) 'Second-step failure not verified'}
}
$checkpoints=@(Get-ChildItem -LiteralPath (Join-Path $root 'worker') -Filter 'copy-native-checkpoint-*.json'|Sort-Object Name)
Require88 ($checkpoints.Count -eq 20) 'Native checkpoint matrix incomplete'
for($i=0;$i -lt 20;$i+=2){
 $before=Get-Content -LiteralPath $checkpoints[$i].FullName -Raw|ConvertFrom-Json
 $after=Get-Content -LiteralPath $checkpoints[$i+1].FullName -Raw|ConvertFrom-Json
 Require88 ($before.run_id -ceq $client.run_id -and $after.run_id -ceq $client.run_id -and $before.native_state_sha256 -cmatch '^[A-F0-9]{64}$' -and $before.native_state_sha256 -ceq $after.native_state_sha256) 'Native refusal checkpoint differs'
}
$files=@('wrapper-result.json','worker/copy-client88.json','worker/copy-native-verifier88.json','worker/stopped.json')+@($checkpoints|ForEach-Object {'worker/'+$_.Name})
$hashes=@($files|ForEach-Object {[ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $root $_)).Hash}})
[ordered]@{schema=1;status='PASS_SCOPED_EVIDENCE';run_id=$client.run_id;producer_sha256=$wrapper.stage_dll_sha256;workflows=6;native_pairs=8;refusals=10;native_checkpoints=20;quest_proof=$false;physical_proof=$false;persistence_proof=$false;source_update_proof=$false;open_s1_gates=18;files=$hashes}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $output -Encoding utf8
