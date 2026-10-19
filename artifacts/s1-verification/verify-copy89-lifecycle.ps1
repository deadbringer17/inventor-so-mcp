param([Parameter(Mandatory=$true)][string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($EvidenceDirectory)
$output=Join-Path $root 'verification-lifecycle-r1.json'
if(Test-Path -LiteralPath $output){throw 'Lifecycle verification evidence exists'}
function Require89([bool]$Condition,[string]$Assertion){if(-not $Condition){throw $Assertion}}
function Read89([string]$Relative){Get-Content -LiteralPath (Join-Path $root $Relative) -Raw|ConvertFrom-Json}
function Same89($First,$Second){(ConvertTo-Json -InputObject $First -Depth 100 -Compress) -ceq (ConvertTo-Json -InputObject $Second -Depth 100 -Compress)}
function OwnedField89($State,[string]$OwnedPath){
 Require89 ([IO.Path]::IsPathFullyQualified($OwnedPath)) 'Absolute native owner path required'
 $clone=ConvertTo-Json -InputObject $State -Depth 100 -Compress|ConvertFrom-Json
 $parameters=@($clone.user_parameters|Where-Object name -CEQ 'S1Copy88Length')
 Require89 ($parameters.Count -eq 1 -and $parameters[0].units -ceq 'mm' -and [double]$parameters[0].value -eq 1.3 -and -not [string]::IsNullOrEmpty([string]$parameters[0].key)) 'Exact native fixture parameter absent'
 $count=0
 foreach($text in $clone.sketch.texts){
  if(-not ([string]$text.formatted_text).Contains('<Parameter',[StringComparison]::Ordinal)){continue}
  $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null;$settings.MaxCharactersInDocument=4096
  $input89=[IO.StringReader]::new('<owner>'+$text.formatted_text+'</owner>');$reader89=[Xml.XmlReader]::Create($input89,$settings)
  try{$xml89=[Xml.Linq.XElement]::Load($reader89,[Xml.Linq.LoadOptions]::PreserveWhitespace)}finally{$reader89.Dispose();$input89.Dispose()}
  $nodes89=@($xml89.Nodes());Require89 ($nodes89.Count -eq 2 -and $nodes89[0] -is [Xml.Linq.XText] -and $nodes89[0].Value -ceq 'Bound=' -and $nodes89[1] -is [Xml.Linq.XElement]) 'Unexpected fixture field markup'
  $field89=$nodes89[1];$attributes89=@($field89.Attributes())
  Require89 ($field89.Name.ToString() -ceq 'Parameter' -and $attributes89.Count -eq 4 -and @($attributes89|Where-Object {$_.Name.ToString() -cnotin @('Resolved','ComponentIdentifier','Name','Precision')}).Count -eq 0 -and $field89.Attribute('Resolved').Value -ceq 'True' -and $field89.Attribute('Name').Value -ceq 'S1Copy88Length' -and $field89.Attribute('Precision').Value -ceq '0' -and @($field89.Nodes()).Count -eq 1 -and $field89.FirstNode -is [Xml.Linq.XText] -and $field89.Value -ceq '13') 'Unexpected fixture parameter binding'
  $owner89=$field89.Attribute('ComponentIdentifier').Value
  Require89 ([IO.Path]::IsPathFullyQualified($owner89) -and [string]::Equals([IO.Path]::GetFullPath($owner89),[IO.Path]::GetFullPath($OwnedPath),[StringComparison]::OrdinalIgnoreCase)) 'Field owner is not the exact owned native document'
  $field89.SetAttributeValue('ComponentIdentifier','owned-local:'+$clone.document_id+'#'+$parameters[0].key)
  $canonical89=$xml89.ToString([Xml.Linq.SaveOptions]::DisableFormatting);$text.formatted_text=$canonical89.Substring(7,$canonical89.Length-15);$count++
 }
 Require89 ($count -eq 1) 'Exactly one unselected fixture parameter field required'
 return $clone
}
$baseline=Read89 'verification-r1.json'
$wrapper=Read89 'wrapper-result.json'
$report=Read89 'worker/copy-lifecycle89.json'
$native=Read89 'worker/copy-native-verifier88.json'
$stopped=Read89 'worker/stopped.json'
Require89 ($baseline.status -ceq 'PASS_SCOPED_EVIDENCE' -and $wrapper.exit_code -eq 0 -and $wrapper.native_lifecycle_subset -eq $true -and $report.status -ceq 'PASS_NATIVE_LIFECYCLE_SUBSET') 'Lifecycle or prerequisite matrix failed'
Require89 ($baseline.run_id -ceq $report.run_id -and $wrapper.run_id -ceq $report.run_id -and $native.target_id -ceq $report.target_id -and $stopped.http_stopped_before_lifecycle89 -eq $true) 'Lifecycle binding or HTTP boundary differs'
Require89 (@($report.documents).Count -eq 2 -and @($wrapper.archived_fixtures89).Count -eq 4) 'Lifecycle context/archive count differs'
$contexts=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach($row in $report.documents){
 Require89 ($contexts.Add([string]$row.context) -and @($row.updates).Count -eq 2 -and $row.state -ceq 'two_source_updates_and_reopen_observed') 'Lifecycle row incomplete'
 $sources=@($native.checks|Where-Object document_id -CEQ $row.original_document_id)
 $lineSet=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 $textSet=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 foreach($sourceCheck in $sources){
  if($sourceCheck.type -ceq 'line'){[void]$lineSet.Add([string]$sourceCheck.source_key)}
  if($sourceCheck.type -ceq 'text'){[void]$textSet.Add([string]$sourceCheck.source_key)}
 }
 $lineKeys=@($lineSet);$textKeys=@($textSet)
 Require89 ($lineKeys.Count -eq 1 -and $textKeys.Count -eq 1) 'Lifecycle source identity ambiguous'
 $lineKey=[string]$lineKeys[0];$textKey=[string]$textKeys[0]
 $initialLine=@($row.initial.sketch.entities|Where-Object key -CEQ $lineKey)
 $initialText=@($row.initial.sketch.texts|Where-Object key -CEQ $textKey)
 Require89 ($initialLine.Count -eq 1 -and $initialText.Count -eq 1) 'Native initial source absent'
 $excluded=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 foreach($key in @($lineKey,$initialLine[0].start_key,$initialLine[0].end_key,$initialText[0].copy_details88.origin_key)){[void]$excluded.Add([string]$key)}
 $stages=@()
 for($i=0;$i -lt 2;$i++){
  $update=$row.updates[$i]
  Require89 ($update.pass -eq $i+1 -and $update.state -ceq 'owned_update_committed' -and $update.copies_independent_exact -eq $true) 'Source update not verified'
  $stages+=@($update.before_commit_readback,$update.after_commit_first_native_read)
  $a=$update.before_commit_readback;$b=$update.after_commit_first_native_read
  foreach($field in @('sketch','styles','user_parameters')){Require89 (Same89 $a.$field $b.$field) 'Commit changed checked native state'}
  $source=@($b.sketch.texts|Where-Object key -CEQ $textKey)[0]
  Require89 ($source.formatted_text -ceq $update.requested.formatted_text -and @($source.copy_details88.raw_vertices_model_mm).Count -gt 0 -and $source.glyph_coordinates_sha256 -cne @($update.before.sketch.texts|Where-Object key -CEQ $textKey)[0].glyph_coordinates_sha256) 'Source text edit/glyph change absent'
  $line=@($b.sketch.entities|Where-Object key -CEQ $lineKey)[0]
  foreach($field in @('start_mm','end_mm')){
   for($axis=0;$axis -lt 2;$axis++){
    $actual=[double]$line.$field[$axis];$expected=[double]$update.requested.$field[$axis]
    Require89 ([double]::IsFinite($actual) -and [double]::IsFinite($expected) -and [Math]::Abs($actual-$expected) -le 1e-5) 'Source line edit differs'
   }
  }
 }
 $stages+=@($row.before_persistence,$row.persistence.first_reopened_native_read)
 $initialComparable=OwnedField89 $row.initial $row.persistence.original_path
 for($stageIndex=0;$stageIndex -lt $stages.Count;$stageIndex++){
  $ownerPath=if($stageIndex -eq $stages.Count-1){$row.persistence.copy_path}else{$row.persistence.original_path}
  $stage=OwnedField89 $stages[$stageIndex] $ownerPath
  Require89 ($stage.document_id -ceq $row.original_document_id -and $stage.internal_name -ceq $row.initial.internal_name) 'Native lifecycle document identity differs'
  foreach($collection in @('entities','texts')){
   $before=@($initialComparable.sketch.$collection);$after=@($stage.sketch.$collection)
   Require89 ($before.Count -eq $after.Count) 'Native entity count changed'
   foreach($entity in $before){
    $matches=@($after|Where-Object key -CEQ $entity.key)
    Require89 ($matches.Count -eq 1) 'Native identity missing or duplicated'
    if($collection -ceq 'texts' -and $entity.key -ceq $textKey){Require89 ($matches[0].copy_details88.origin_key -ceq $entity.copy_details88.origin_key) 'Source text origin identity lost';continue}
    if(-not $excluded.Contains([string]$entity.key)){Require89 (Same89 $entity $matches[0]) 'Copy or unrelated native object changed'}
    if($entity.key -ceq $lineKey){Require89 ($matches[0].start_key -ceq $entity.start_key -and $matches[0].end_key -ceq $entity.end_key) 'Source line endpoint identity lost'}
   }
  }
  foreach($field in @('constraints','dimensions')){Require89 (Same89 $initialComparable.sketch.$field $stage.sketch.$field) 'Native relation changed'}
  foreach($field in @('styles','user_parameters')){Require89 (Same89 $initialComparable.$field $stage.$field) 'Shared style or user parameter changed'}
 }
 $persist=$row.persistence
 Require89 ($persist.state -ceq 'reopened_verified' -and $persist.original_disk_unchanged -eq $true -and $persist.saved_copy_disk_readonly -eq $true -and $persist.old_http_reused -eq $false) 'Persistence boundary not verified'
 Require89 ($persist.local_parameter_owner_relocation_verified -eq $true) 'Native owner relocation proof absent'
 $beforeSave=OwnedField89 $row.before_persistence $persist.original_path
 $afterOpen=OwnedField89 $persist.first_reopened_native_read $persist.copy_path
 foreach($field in @('sketch','styles','user_parameters')){Require89 (Same89 $beforeSave.$field $afterOpen.$field) 'First reopened native state differs beyond verified local owner relocation'}
 foreach($kind in @('original','copy')){
  $pathProperty=$kind+'_path';$hashProperty=$kind+'_sha256'
  $file=Join-Path (Join-Path $root 'fixtures') ([IO.Path]::GetFileName([string]$persist.$pathProperty))
  Require89 ((Get-FileHash -LiteralPath $file).Hash -ceq $persist.$hashProperty) 'Archived fixture hash differs'
 }
}
Require89 ($contexts.Contains('Part') -and $contexts.Contains('Sheet')) 'Missing native context'
[ordered]@{schema=1;status='PASS_NATIVE_LIFECYCLE_SCOPED_EVIDENCE';run_id=$report.run_id;producer_sha256=$wrapper.stage_dll_sha256;source_updates=4;savecopy_reopen=2;archived_ipts=4;core_post_edit_proof=$false;core_post_reopen_proof=$false;quest_proof=$false;physical_proof=$false;open_s1_gates=18;report_sha256=(Get-FileHash -LiteralPath (Join-Path $root 'worker/copy-lifecycle89.json')).Hash;wrapper_sha256=(Get-FileHash -LiteralPath (Join-Path $root 'wrapper-result.json')).Hash}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $output -Encoding utf8
