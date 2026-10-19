param([Parameter(Mandatory=$true)][string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $EvidenceDirectory).Path
function Require91([bool]$condition,[string]$message){if(-not $condition){throw $message}}
function Read91([string]$relative){
 $file=Get-Item -LiteralPath (Join-Path $root $relative)
 Require91 (-not $file.PSIsContainer -and ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -and $file.Length -gt 0 -and $file.Length -le 67108864) 'Bounded regular evidence file required'
 Get-Content -LiteralPath $file.FullName -Raw|ConvertFrom-Json -Depth 100
}
$wrapper=Read91 'wrapper-result.json';$ready=Read91 'worker/ready.json';$stopped=Read91 'worker/stopped.json';$probe=Read91 'worker/parameter-dependencies91.json'
Require91 ($wrapper.exit_code -eq 0 -and $wrapper.worker_exited -eq $true -and $wrapper.worker_exit_code -eq 0 -and $wrapper.manifest_restored -eq $true -and $wrapper.original_dll_unchanged -eq $true) 'Normal terminal wrapper/restore evidence required'
Require91 ($stopped.status -ceq 'stopped' -and $stopped.owned_final_empty -eq $true -and $stopped.owned_pid_exited -eq $true -and $stopped.existing_processes_preserved -eq $true) 'Owned native cleanup evidence required'
foreach($item in @($ready,$stopped,$probe)){
 Require91 ($item.run_id -ceq $wrapper.run_id -and $item.target_id -ceq $ready.target_id -and $item.owned_pid -eq $ready.owned_pid) 'Run/target/process binding differs'
}
Require91 ($ready.target_id -ceq "inventor-2027-$($ready.owned_pid)" -and $ready.owned_start_utc_ticks -eq $stopped.owned_start_utc_ticks -and $ready.loaded_addin.sha256 -ceq $wrapper.stage_dll_sha256) 'Native process/start/candidate binding differs'
Require91 ($ready.http_started -eq $false -and $ready.bootstrap_exposed -eq $false -and $probe.http_started -eq $false -and $probe.product_pass -eq $false) 'Observation must not expose HTTP/bootstrap or claim product proof'
Require91 ($probe.status -ceq 'OBSERVATIONS_COMPLETE' -and $probe.readonly_full_native_exact -eq $true) 'Completed read-only observation required'
# Compare every serialized native field independently. Identical property order is
# deliberately stricter than semantic equality; nothing is removed or normalized.
$before=ConvertTo-Json -InputObject $probe.native_before -Depth 100 -Compress
$after=ConvertTo-Json -InputObject $probe.native_after -Depth 100 -Compress
Require91 ($before -ceq $after -and $before.Length -gt 100) 'Raw full native before/after differs'
Require91 (@($probe.targets).Count -eq 4 -and @($probe.parameters).Count -eq $probe.parameter_count_observed -and $probe.parameter_count_observed -ge 2) 'Fixture/parameter observation inventory differs'
foreach($context in @('Part','Sheet')){
 $targets=@($probe.targets|Where-Object context -CEQ $context)
 Require91 ($targets.Count -eq 2 -and @($targets|Where-Object role -CEQ 'literal').Count -eq 1 -and @($targets|Where-Object role -CEQ 'parameter_field').Count -eq 1) 'Two creator-known text roles per context required'
 Require91 (@($targets.document_id|Select-Object -Unique).Count -eq 1 -and @($targets.text_key|Select-Object -Unique).Count -eq 2) 'Known target identity collision'
}
$members=0;$observedEdges=@()
foreach($parameter in $probe.parameters){
 $direct=0
 foreach($dependent in $parameter.dependents){
  $members++
  foreach($edge in $dependent.target_same_com){
   if($edge.status -ceq 'UNKNOWN'){continue}
   $known=@($probe.targets|Where-Object {$_.text_key -ceq $edge.target_text_key -and $_.role -ceq $edge.target_role -and ('doc_'+$edge.target_internal_name) -ceq $_.document_id})
   Require91 ($known.Count -eq 1 -and $edge.same_com -is [bool]) 'Dependent-to-known-target observation identity invalid'
   if($edge.same_com){$direct++;$observedEdges+=[ordered]@{parameter_document=$parameter.document_id;parameter_key=$parameter.key;target_document=$known[0].document_id;target_text_key=$edge.target_text_key;target_role=$edge.target_role}}
  }
 }
 if($null -ne $parameter.direct_known_textbox_edges){Require91 ($parameter.direct_known_textbox_edges -eq $direct) 'Direct observed edge count differs'}
}
Require91 ($members -eq $probe.dependent_members_observed) 'Dependent observation count differs'
$output=[ordered]@{status='VERIFIED_OBSERVATIONS_READONLY_ONLY';run_id=$wrapper.run_id;raw_native_exact=$true;parameter_count=$probe.parameter_count_observed;dependent_members=$members;unknown_parameter_rows=$probe.unknown_parameter_rows;direct_observed_edges=$observedEdges;generic_field_owner_resolver_proved=$false;product_pass=$false;open_s1_gates=18;quest_proof=$false}
$path=Join-Path $root 'verification-dependencies91-r1.json'
Require91 (-not (Test-Path -LiteralPath $path)) 'New immutable verification output required'
$output|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $path -Encoding utf8
$output|ConvertTo-Json -Depth 8
