[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
 [ValidatePattern('^verification-core-lifecycle90-[a-zA-Z0-9-]+\.json$')][string]$OutputName='verification-core-lifecycle90-r1.json',
 [switch]$ValidateOnly,
 [switch]$SelfTest
)
$ErrorActionPreference='Stop'
# Offline only. No executable launch, network, COM, process control, or native API.
# A terminal producer report is input evidence, never an authorization to rerun CAD.
function Require90([bool]$Condition,[string]$Assertion){if(-not $Condition){throw [InvalidOperationException]::new($Assertion)}}
function Read90([string]$Relative){
 $path=[IO.Path]::GetFullPath((Join-Path $script:EvidenceRoot90 $Relative))
 Require90 ($path.StartsWith($script:EvidenceRoot90+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'Evidence path escapes run directory'
 $file=Get-Item -LiteralPath $path
 Require90 (-not $file.PSIsContainer -and ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -and $file.Length -gt 0 -and $file.Length -le 67108864) 'Evidence must be a bounded regular JSON file (offline limit64MiB)'
 Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable -Depth 100
}
function Clone90($Value){ConvertTo-Json -InputObject $Value -Depth 100 -Compress | ConvertFrom-Json -AsHashtable -Depth 100}
function Equal90($A,$B){
 if($null -eq $A -or $null -eq $B){return ($null -eq $A -and $null -eq $B)}
 if($A -is [Collections.IDictionary] -or $B -is [Collections.IDictionary]){
  if($A -isnot [Collections.IDictionary] -or $B -isnot [Collections.IDictionary] -or $A.Count -ne $B.Count){return $false}
  $keys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);foreach($k in $B.Keys){[void]$keys.Add([string]$k)}
  foreach($k in $A.Keys){if(-not $keys.Contains([string]$k) -or -not (Equal90 $A[$k] $B[$k])){return $false}};return $true
 }
 if($A -is [Collections.IList] -or $B -is [Collections.IList]){
  if($A -isnot [Collections.IList] -or $B -isnot [Collections.IList] -or $A.Count -ne $B.Count){return $false}
  for($i=0;$i -lt $A.Count;$i++){if(-not (Equal90 $A[$i] $B[$i])){return $false}};return $true
 }
 return ($A.GetType() -eq $B.GetType() -and $A.Equals($B))
}
function Closed90($Object,[string[]]$Names){
 Require90 ($Object -is [Collections.IDictionary] -and $Object.Count -eq $Names.Count) 'Closed metadata field count differs'
 foreach($name in $Object.Keys){Require90 ($name -cin $Names) 'Closed metadata contains unexpected field'}
}
function SamePath90([string]$A,[string]$B){
 Require90 ([IO.Path]::IsPathFullyQualified($A) -and [IO.Path]::IsPathFullyQualified($B)) 'Absolute owned paths required'
 [string]::Equals([IO.Path]::GetFullPath($A),[IO.Path]::GetFullPath($B),[StringComparison]::OrdinalIgnoreCase)
}
function Map90($Rows,[string]$Key){
 $map=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
 foreach($row in $Rows){Require90 ($row -is [Collections.IDictionary] -and -not [string]::IsNullOrEmpty([string]$row[$Key]) -and $map.TryAdd([string]$row[$Key],$row)) 'Missing/duplicate identity in represented collection'}
 return ,$map
}
function SameKeys90($A,$B){
 Require90 ($A.Count -eq $B.Count) 'Native/Core identity inventory count changed'
 foreach($key in $A.Keys){Require90 ($B.ContainsKey($key)) 'Native/Core identity missing'}
}
function Opaque90([string]$Id,[string]$Document,[string]$Type){
 Require90 ($Id.StartsWith('ent_',[StringComparison]::Ordinal) -and $Id.Length -le 131072) 'Opaque native identity absent/oversized'
 $encoded=$Id.Substring(4).Replace('-','+').Replace('_','/');$encoded=$encoded.PadRight($encoded.Length+(4-$encoded.Length%4)%4,'=')
 $decoded=[Text.UTF8Encoding]::new($false,$true).GetString([Convert]::FromBase64String($encoded))|ConvertFrom-Json -AsHashtable
 Closed90 $decoded @('Version','DocumentId','EntityType','Key','Context')
 Require90 ($decoded.Version -eq 1 -and $decoded.DocumentId -ceq $Document -and $decoded.EntityType -ceq $Type -and $decoded.Context -ceq '' -and [Convert]::FromBase64String([string]$decoded.Key).Length -gt 0) 'Opaque native document/type/context/key differs'
 return [string]$decoded.Key
}
function NearArray90($Actual,$Expected,[double]$Tolerance=1e-5){
 Require90 ($Actual.Count -eq $Expected.Count -and $Actual.Count -gt 0) 'Geometry array dimensions differ'
 for($i=0;$i -lt $Actual.Count;$i++){
  $a=[double]$Actual[$i];$b=[double]$Expected[$i]
  Require90 ([double]::IsFinite($a) -and [double]::IsFinite($b) -and [Math]::Abs($a-$b) -le $Tolerance) 'Geometry coordinates differ'
 }
}
function Shift90($Before,$After,[int]$Stride,$Delta){
 Require90 ($Before.Count -eq $After.Count -and $Before.Count -gt 0 -and $Before.Count%$Stride -eq 0) 'Copy translation array dimensions differ'
 for($i=0;$i -lt $Before.Count;$i++){$shift=if($i%$Stride -eq 0){[double]$Delta[0]}elseif($i%$Stride -eq 1){[double]$Delta[1]}else{0};NearArray90 @($After[$i]) @([double]$Before[$i]+$shift)}
}
function ParameterMarkup90([string]$Markup,$Metadata){
 Require90 ($Markup.Length -gt 0 -and $Markup.Length -le 4096 -and [IO.Path]::IsPathFullyQualified([string]$Metadata.native_path)) 'Owned parameter markup/path invalid'
 $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null;$settings.MaxCharactersInDocument=4111
 $input90=[IO.StringReader]::new('<owner>'+$Markup+'</owner>');$reader90=[Xml.XmlReader]::Create($input90,$settings)
 try{$root90=[Xml.Linq.XElement]::Load($reader90,[Xml.Linq.LoadOptions]::PreserveWhitespace)}finally{$reader90.Dispose();$input90.Dispose()}
 $nodes=@($root90.Nodes());Require90 ($nodes.Count -eq 2 -and $nodes[0].GetType() -eq [Xml.Linq.XText] -and $nodes[0].Value -ceq 'Bound=' -and $nodes[1] -is [Xml.Linq.XElement]) 'Parameter field grammar differs'
 $field=$nodes[1];$attrs=@($field.Attributes())
 Require90 ($field.Name.ToString() -ceq 'Parameter' -and $attrs.Count -eq 4 -and @($attrs|Where-Object {$_.Name.ToString() -cnotin @('Resolved','ComponentIdentifier','Name','Precision')}).Count -eq 0 -and $field.Attribute('Resolved').Value -ceq 'True' -and $field.Attribute('Name').Value -ceq 'S1Copy88Length' -and $field.Attribute('Precision').Value -ceq '0' -and @($field.Nodes()).Count -eq 1 -and $field.FirstNode.GetType() -eq [Xml.Linq.XText] -and $field.Value -ceq '13') 'Parameter field is not exact unchanged13mm fixture grammar'
 Require90 (SamePath90 ([string]$field.Attribute('ComponentIdentifier').Value) ([string]$Metadata.native_path)) 'Parameter field owner is outside the exact attested native document'
 $field.SetAttributeValue('ComponentIdentifier','owned-local:'+$Metadata.document_id+'#'+$Metadata.parameter_key)
 $canonical=$root90.ToString([Xml.Linq.SaveOptions]::DisableFormatting);return $canonical.Substring(7,$canonical.Length-15)
}
function CanonicalCore90($Row,$Metadata){
 Require90 ($Row.id -ceq $Metadata.source_ids.parameter_text -and $Row.type -ceq 'text' -and $Row.text -ceq 'Bound=13') 'Only explicitly attested NONcopied parameter text may relocate'
 $result=Clone90 $Row;$result.formatted_text=ParameterMarkup90 ([string]$Row.formatted_text) $Metadata;return $result
}
function CanonicalNative90($State,$Metadata){
 Require90 ($State.document_id -ceq $Metadata.document_id -and $State.internal_name -ceq $Metadata.internal_name) 'Native parameter document identity differs'
 $users=@($State.user_parameters|Where-Object {$_.name -ceq 'S1Copy88Length'})
 Require90 ($users.Count -eq 1 -and $users[0].key -ceq $Metadata.parameter_key -and $users[0].units -ceq 'mm' -and [double]$users[0].value -eq 1.3) 'Native parameter key/value/units differs'
 $result=Clone90 $State;$key=Opaque90 $Metadata.source_ids.parameter_text $Metadata.document_id 'sketch_text';$fields=0
 foreach($text in $result.sketch.texts){
  if(([string]$text.formatted_text).Contains('<Parameter',[StringComparison]::Ordinal)){
   Require90 ($text.key -ceq $key -and $text.text -ceq 'Bound=13') 'Unexpected native parameter text or field elsewhere'
   $text.formatted_text=ParameterMarkup90 ([string]$text.formatted_text) $Metadata;$fields++
  }
 }
 Require90 ($fields -eq 1) 'Exactly one native NONcopied fixture parameter field required'
 return $result
}
function SourceIncidence90($Constraint,$Metadata){
 if($Constraint.type -cne 'constraint' -or $Constraint.native_type -cne 'kCoincidentConstraintObject' -or $Constraint.deletable -isnot [bool] -or $Constraint.deletable -or $Constraint.binding_state -cne 'complete'){return $false}
 foreach($endpoint in @(@('start','Start'),@('end','End'))){
  $expected=@(
   @{entity_id=$Metadata.source_ids.line;type='line';subpart='Entity'},
   @{entity_id=$Metadata.source_ids[$endpoint[0]];type='point';subpart='Entity'},
   @{entity_id=$Metadata.source_ids[$endpoint[0]];type='point';subpart='Point'},
   @{entity_id=$Metadata.source_ids.line;type='line';subpart=$endpoint[1]})
  if(Equal90 $Constraint.related_entities $expected){return $true}
 };return $false
}
function CoreIndependence90($Before,$After,$OldMetadata,$NewMetadata,[bool]$Reopen){
 Require90 ($OldMetadata.document_id -ceq $NewMetadata.document_id -and $OldMetadata.parameter_key -ceq $NewMetadata.parameter_key) 'Core relocation owner/key changed'
 $intrinsic=@($Before.constraints|Where-Object {SourceIncidence90 $_ $OldMetadata})
 Require90 ($intrinsic.Count -eq 2) 'Exactly two intrinsic source-line constraints required'
 $beforeEntityMap=Map90 $Before.entities 'id';$afterEntityMap=Map90 $After.entities 'id'
 foreach($collection in @('entities','dimensions','constraints')){
  $a=Map90 $Before[$collection] 'id';$b=Map90 $After[$collection] 'id';SameKeys90 $a $b
  foreach($id in $a.Keys){
   if(-not $Reopen -and $collection -ceq 'constraints' -and @($intrinsic|Where-Object {$_.id -ceq $id}).Count -eq 1){
    Require90 ((Equal90 $a[$id].annotation_mm $beforeEntityMap[$OldMetadata.source_ids.line].start_mm) -and (Equal90 $b[$id].annotation_mm $afterEntityMap[$NewMetadata.source_ids.line].start_mm)) 'Intrinsic incidence annotation does not follow exact source Start anchor'
    $oldConstraint=Clone90 $a[$id];$newConstraint=Clone90 $b[$id]
    [void]$oldConstraint.Remove('annotation_mm');[void]$newConstraint.Remove('annotation_mm')
    Require90 (Equal90 $oldConstraint $newConstraint) 'Intrinsic source constraint changed beyond exact annotation anchor';continue
   }
   if($collection -ceq 'entities' -and $id -ceq $OldMetadata.source_ids.parameter_text){Require90 (Equal90 (CanonicalCore90 $a[$id] $OldMetadata) (CanonicalCore90 $b[$id] $NewMetadata)) 'Uncopied Core parameter changed beyond owned path relocation';continue}
   $role=@(@('line','start','end','text','origin')|Where-Object {$OldMetadata.source_ids[$_] -ceq $id})
   if(-not $Reopen -and @($role).Count -eq 1){
    $one=Clone90 $a[$id];$two=Clone90 $b[$id]
    $fields=if($role[0] -ceq 'line'){@('start_mm','end_mm','strokes_mm')}elseif($role[0] -ceq 'text'){@('text','formatted_text','text_mm','text_bounds_mm','text_glyphs')}else{@('point_mm')}
    foreach($field in $fields){[void]$one.Remove($field);[void]$two.Remove($field)}
    Require90 (Equal90 $one $two) 'Source edit changed undeclared Core property'
   }else{Require90 (Equal90 $a[$id] $b[$id]) 'Copied/unrelated Core Data changed'}
  }
 }
 foreach($field in @('complete','partial','constraint_status')){Require90 (Equal90 $Before[$field] $After[$field]) 'Core completeness/solver representation changed'}
}
function NativeIndependence90($Before,$After,$Metadata){
 $a=CanonicalNative90 $Before $Metadata;$b=CanonicalNative90 $After $Metadata
 $excluded=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 foreach($role in @('line','start','end','text','origin')){[void]$excluded.Add((Opaque90 $Metadata.source_ids[$role] $Metadata.document_id $(if($role -ceq 'text'){'sketch_text'}else{'sketch_entity'})))}
 foreach($collection in @('entities','texts')){
  $one=Map90 $a.sketch[$collection] 'key';$two=Map90 $b.sketch[$collection] 'key';SameKeys90 $one $two
  foreach($key in $one.Keys){
   if(-not $excluded.Contains($key)){Require90 (Equal90 $one[$key] $two[$key]) 'Copy/unrelated native Data changed';continue}
   $oldRow=Clone90 $one[$key];$newRow=Clone90 $two[$key]
   if($collection -ceq 'texts'){
    # Only declared source content/pose/WidthScale/color and derived glyph/bounds
    # may change. Style identity/font/alignment/spacing/rotation remain exact.
    foreach($field in @('text','formatted_text','origin_mm','width_scale','bounds_sketch_mm','glyph_vertices','glyph_faces','glyph_min_model_mm','glyph_max_model_mm','glyph_coordinates_sha256','glyph_indices_sha256','color_rgb')){[void]$oldRow.Remove($field);[void]$newRow.Remove($field)}
    foreach($field in @('origin_mm','raw_vertices_model_mm','raw_indices_native_one_based')){[void]$oldRow.copy_details88.Remove($field);[void]$newRow.copy_details88.Remove($field)}
    foreach($field in @('red','green','blue')){[void]$oldRow.copy_details88.color.Remove($field);[void]$newRow.copy_details88.color.Remove($field)}
   }else{
    foreach($field in @('start_mm','end_mm','point_mm')){[void]$oldRow.Remove($field);[void]$newRow.Remove($field)}
   }
   Require90 (Equal90 $oldRow $newRow) 'Source edit changed undeclared native property/style/key'
  }
 }
 foreach($field in @('constraints','dimensions')){Require90 (Equal90 $a.sketch[$field] $b.sketch[$field]) 'Native relations/dimensions changed'}
 $oldSketch=Clone90 $a.sketch;$newSketch=Clone90 $b.sketch
 foreach($field in @('entities','texts')){[void]$oldSketch.Remove($field);[void]$newSketch.Remove($field)}
 Require90 (Equal90 $oldSketch $newSketch) 'Native sketch identity/visibility/images/relations changed'
 foreach($field in @('styles','user_parameters')){Require90 (Equal90 $a[$field] $b[$field]) 'Shared native style/parameter changed'}
}
function CoreNative90($Snapshot,$Native,$Metadata){
 $entities=Map90 $Native.sketch.entities 'key';$texts=Map90 $Native.sketch.texts 'key'
 Require90 ($Snapshot.entities.Count -eq $entities.Count+$texts.Count) 'Core/native represented entity count differs'
 foreach($row in $Snapshot.entities){
  $key=Opaque90 $row.id $Metadata.document_id $(if($row.type -ceq 'text'){'sketch_text'}else{'sketch_entity'})
  if($row.type -ceq 'text'){
   Require90 ($texts.ContainsKey($key)) 'Core text absent from native readback';$nativeText=$texts[$key]
   Require90 ($row.text -ceq $nativeText.text -and $row.formatted_text -ceq $nativeText.formatted_text -and $row.rotation_deg -eq $nativeText.rotation_rad*180/[Math]::PI) 'Core/native text content or rotation differs'
   # Producer text_mm is RangeBox.MinPoint, not the justification-dependent Origin.
   NearArray90 $row.text_mm $nativeText.bounds_sketch_mm[0]
   $flatBounds=@($nativeText.bounds_sketch_mm|ForEach-Object {$_[0];$_[1]});NearArray90 $row.text_bounds_mm $flatBounds
   NearArray90 $row.text_glyphs.vertices_mm $nativeText.copy_details88.raw_vertices_model_mm
   $indices=@($nativeText.copy_details88.raw_indices_native_one_based|ForEach-Object {[long]$_-1});Require90 (Equal90 $row.text_glyphs.indices $indices) 'Core/native glyph topology differs'
   NearArray90 $row.text_glyphs.color_rgb @($nativeText.copy_details88.color.red,$nativeText.copy_details88.color.green,$nativeText.copy_details88.color.blue) 0
  }else{
   Require90 ($entities.ContainsKey($key)) 'Core curve/point absent from native readback';$entity=$entities[$key]
   Require90 ($row.type -ceq $entity.type) 'Core/native type differs'
   if($row.type -ceq 'line'){NearArray90 $row.start_mm $entity.start_mm;NearArray90 $row.end_mm $entity.end_mm}
   elseif($row.type -ceq 'point'){NearArray90 $row.point_mm $entity.point_mm}
   else{throw 'Unexpected geometry in copy90 profile'}
  }
 }
}
function Verify90([string]$Root){
 $script:EvidenceRoot90=[IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
 $wrapper=Read90 'wrapper-result.json';$ready=Read90 'worker/ready.json';$stopped=Read90 'worker/stopped.json'
 $baseline=Read90 'worker/copy-client88.json';$client=Read90 'worker/copy-client90.json';$native88=Read90 'worker/copy-native-verifier88.json';$native=Read90 'worker/copy-native-lifecycle90.json'
 Require90 ($wrapper.exit_code -eq 0 -and $wrapper.client_exit -eq 0 -and $wrapper.worker_exit_code -eq 0 -and $wrapper.worker_exited -eq $true -and $wrapper.manifest_restored -eq $true -and $wrapper.original_dll_unchanged -eq $true -and $wrapper.native_core_lifecycle_subset -eq $true -and $wrapper.core_first_reads -eq 6 -and $wrapper.protocol_phases -eq 14 -and $wrapper.owned_acknowledgements -eq 14) 'Terminal wrapper or cleanup failed'
 Require90 ($stopped.http_stopped -eq $true -and $stopped.owned_final_empty -eq $true -and $stopped.owned_pid_exited -eq $true -and $stopped.existing_processes_preserved -eq $true) 'Owned shutdown/process preservation absent'
 Require90 ($baseline.status -ceq 'PASS_PC_REPRESENTED_SUBSET' -and $client.status -ceq 'PASS_PC_CORE_LIFECYCLE_SUBSET' -and $client.exit_code -eq 0 -and $native88.status -ceq 'PASS_NATIVE_READBACK_SUBSET' -and $native.status -ceq 'PASS_NATIVE_CORE_LIFECYCLE_SUBSET' -and $native.source_updates -eq 4 -and $native.reopens -eq 2) 'Scoped prerequisite/Core/native terminal status differs'
 $run=[string]$client.run_id;$target=[string]$client.target_id
 Require90 ($run -cmatch '^[0-9a-f]{32}$' -and $target -ceq "inventor-2027-$($ready.owned_pid)" -and $ready.status -ceq 'ready') 'Run/owned target binding invalid'
 foreach($row in @($wrapper,$ready,$stopped,$baseline,$native88,$native)){Require90 ($row.run_id -ceq $run) 'Cross-report run binding differs'}
 foreach($row in @($ready,$stopped,$baseline,$native88,$native)){Require90 ($row.target_id -ceq $target) 'Cross-report target differs'}
 Require90 ($ready.owned_pid -eq $stopped.owned_pid -and $ready.owned_start_utc_ticks -eq $stopped.owned_start_utc_ticks -and $ready.loaded_addin.sha256 -ceq $wrapper.stage_dll_sha256) 'Owned process/staged addin identity differs'
 $beforeProcesses=Map90 $stopped.existing_processes_before 'pid';$afterProcesses=Map90 $stopped.existing_processes_after 'pid'
 Require90 (-not $beforeProcesses.ContainsKey([string]$ready.owned_pid)) 'Owned PID was preexisting'
 if($afterProcesses.ContainsKey([string]$ready.owned_pid)){
  Require90 ($afterProcesses[[string]$ready.owned_pid].start_utc_ticks -eq $ready.owned_start_utc_ticks) 'Final process inventory owned PID/start differs'
 }
 # A signaled Windows process may remain enumerable during handle teardown.
 # Require a separate OS observation after wrapper exit rather than equating
 # an inventory entry with a running process or discarding that observation.
 $cleanup=Read90 'cleanup-verification-r1.json'
 Require90 ($cleanup.run_id -ceq $run -and $cleanup.owned_pid -eq $ready.owned_pid -and $cleanup.owned_start_utc_ticks -eq $ready.owned_start_utc_ticks -and $cleanup.owned_pid_absent -eq $true) 'Independent post-wrapper owned process absence required'
 foreach($pidKey in $beforeProcesses.Keys){Require90 ($afterProcesses.ContainsKey($pidKey) -and $beforeProcesses[$pidKey].start_utc_ticks -eq $afterProcesses[$pidKey].start_utc_ticks) 'Preexisting PID/start preservation differs from cleanup flag'}
 Require90 ((Equal90 $ready.existing_processes_before $stopped.existing_processes_before) -and $stopped.installation_after.unchanged -eq $true -and $stopped.installation_after.stage_addin_sha256 -ceq $wrapper.stage_dll_sha256 -and $stopped.installation_after.original_installed_dll_sha256 -ceq $wrapper.original_dll_sha256) 'Staging/installation/preexisting inventory evidence differs'
 foreach($field in @('cases','refusals')){Require90 (Equal90 $baseline[$field] $client[$field]) 'Frozen88 matrix changed in final90 report'}
 Require90 ($baseline.cases.Count -eq 6 -and $baseline.refusals.Count -eq 10 -and $native88.checks.Count -eq 8) 'Baseline88 matrix counts incomplete'
 $nativePairs=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 foreach($check in $native88.checks){Require90 ($check.native_translation_and_original_exact -eq $true -and $check.independent_native_keys_points_glyphs -eq $true -and $check.source_key -cne $check.copy_key -and $nativePairs.Add("$($check.document_id)|$($check.source_key)|$($check.copy_key)|$($check.type)")) 'Independent native88 pair missing/duplicated'}
 $workflows=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$copies=@{Part=@();Sheet=@()};$sourceMap=@{Part=@{};Sheet=@{}}
 foreach($case in $baseline.cases){
  Require90 ($case.context -cin @('Part','Sheet') -and $case.workflow -cin @('line','text','mixed') -and $workflows.Add("$($case.context)|$($case.workflow)") -and $case.pairs.Count -eq $(if($case.workflow -ceq 'mixed'){2}else{1})) 'Baseline88 workflow count/identity invalid'
  foreach($flag in @('first_snapshot_immediate_after_last_commit','source_represented_exact','represented_preview_rollback_exact','synthetic_pc_confirmation')){Require90 ($case[$flag] -eq $true) 'Baseline88 workflow assertion absent'}
  foreach($pair in $case.pairs){
   Require90 ($pair.type -cin @('line','text')) 'Baseline pair type unsupported'
   $keys=@();foreach($reference in @($pair.source,$pair.copy)){
    Closed90 $reference @('document_id','revision','sketch_id','entity_id','type','subpart')
    Require90 ($reference.document_id -ceq $case.document_id -and $reference.type -ceq $pair.type -and $reference.subpart -ceq 'Entity' -and $reference.revision -cmatch '^[0-9a-f]{32}:[0-9]+$') 'Baseline pair typed reference differs'
    $keys+=Opaque90 $reference.entity_id $case.document_id $(if($pair.type -ceq 'text'){'sketch_text'}else{'sketch_entity'})
   }
   Require90 ($nativePairs.Contains("$($case.document_id)|$($keys[0])|$($keys[1])|$($pair.type)")) 'Baseline Core pair lacks independent native key match'
   $copies[$case.context]+=[string]$pair.copy.entity_id
   if($sourceMap[$case.context].ContainsKey($pair.type)){Require90 ($sourceMap[$case.context][$pair.type] -ceq $pair.source.entity_id) 'Baseline source identity ambiguous'}else{$sourceMap[$case.context][$pair.type]=[string]$pair.source.entity_id}
   if($pair.type -ceq 'line'){foreach($field in @('start_mm','end_mm','strokes_mm')){Shift90 $pair.source_before[$field] $pair.copy_first_snapshot[$field] 2 $case.delta_mm}}
   else{foreach($field in @('text','formatted_text','rotation_deg')){Require90 (Equal90 $pair.source_before[$field] $pair.copy_first_snapshot[$field]) 'Baseline copied text property differs'};Shift90 $pair.source_before.text_glyphs.vertices_mm $pair.copy_first_snapshot.text_glyphs.vertices_mm 3 $case.delta_mm}
  }
 }
 foreach($kind in @('Part','Sheet')){Require90 ($copies[$kind].Count -eq 4 -and @($copies[$kind]|Select-Object -Unique).Count -eq 4) 'Baseline explicit copy IDs incomplete';foreach($workflow in @('line','text','mixed')){Require90 ($workflows.Contains("$kind|$workflow")) 'Baseline workflow missing'}}
 $refusals=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 foreach($row in $baseline.refusals){Require90 ($refusals.Add("$($row.document_id)|$($row.kind)") -and $row.represented_rollback_exact -eq $true -and $row.event_revision_preserved -eq $true -and $row.full_native_checkpoint_exact -eq $true) 'Baseline refusal evidence absent/duplicated';if($row.kind -ceq 'post_valid_copy_failure'){Require90 ($row.code -ceq 'ROLLED_BACK' -and $row.step_code -ceq 'INVALID_ARGUMENT' -and $row.step_index -eq 1) 'Baseline second-step rollback invalid'}}
 foreach($document in @($ready.part_document_id,$ready.sheet_document_id)){
  foreach($kind in @('stale_request','foreign_reference','parameter_text_unsupported','mixed_parameter_preflight','post_valid_copy_failure')){
   Require90 ($refusals.Contains("$document|$kind")) 'Required baseline refusal case missing'
  }
 }
 $checkpoints=@(Get-ChildItem -LiteralPath (Join-Path $script:EvidenceRoot90 'worker') -Filter 'copy-native-checkpoint-*.json'|Sort-Object Name)
 Require90 ($checkpoints.Count -eq 20) 'Baseline native refusal checkpoints incomplete'
 for($i=0;$i -lt 20;$i+=2){$a=Read90 ('worker/'+$checkpoints[$i].Name);$b=Read90 ('worker/'+$checkpoints[$i+1].Name);Require90 ($a.run_id -ceq $run -and $b.run_id -ceq $run -and $a.native_state_sha256 -cmatch '^[A-F0-9]{64}$' -and $a.native_state_sha256 -ceq $b.native_state_sha256 -and $a.native_state -is [Collections.IDictionary] -and $b.native_state -is [Collections.IDictionary] -and $a.native_state.Count -gt 0 -and (Equal90 $a.native_state $b.native_state)) 'Baseline native refusal raw state/digest differs'}
 $steps=@('prepare|all|0','edit|Part|1','inspect|Part|1','edit|Part|2','inspect|Part|2','reopen|Part|0','inspect_reopen|Part|0','edit|Sheet|1','inspect|Sheet|1','edit|Sheet|2','inspect|Sheet|2','reopen|Sheet|0','inspect_reopen|Sheet|0','complete|all|0')
 Require90 ($client.protocol90.Count -eq 14 -and $native.protocol.Count -eq 14 -and @(Get-ChildItem -LiteralPath (Join-Path $script:EvidenceRoot90 'worker') -Filter 'copy-core-ack90-*.json').Count -eq 14) 'Lifecycle ACK/trace counts differ'
 $nonces=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$acks=@();$previous=$null
 for($i=0;$i -lt 14;$i++){
  $request=$client.protocol90[$i].request;$ack=$client.protocol90[$i].ack;$trace=$native.protocol[$i]
  Closed90 $request @('schema','run_id','nonce','ordinal','action','context','pass')
  Closed90 $ack @('schema','run_id','target_id','owned_pid','owned_start_utc_ticks','nonce','ordinal','action','context','pass','status','expires_utc','documents')
  Require90 ($request.schema -eq 1 -and $request.run_id -ceq $run -and $request.ordinal -eq $i+1 -and "$($request.action)|$($request.context)|$($request.pass)" -ceq $steps[$i] -and $request.nonce -cmatch '^[0-9a-f]{32}$' -and $nonces.Add([string]$request.nonce)) 'Lifecycle request nonce/ordinal/action differs'
  foreach($field in @('schema','run_id','nonce','ordinal','action','context','pass')){Require90 (Equal90 $request[$field] $ack[$field]) 'ACK request binding differs'}
  Require90 ($ack.status -ceq 'ACK' -and $ack.target_id -ceq $target -and $ack.owned_pid -eq $ready.owned_pid -and $ack.owned_start_utc_ticks -eq $ready.owned_start_utc_ticks -and (Equal90 $ack.expires_utc $ready.expires_utc) -and $ack.documents.Count -eq 2) 'ACK process/TTL/document count differs'
  Require90 (Equal90 $ack (Read90 ('worker/copy-core-ack90-'+$ack.nonce+'.json'))) 'Archived ACK differs from client receipt'
  foreach($field in @('nonce','ordinal','action','context','pass')){Require90 (Equal90 $trace[$field] $request[$field]) 'Native consumed FSM differs from client request'}
  Require90 ($trace.state -ceq 'completed') 'Native action did not complete'
  $docs=Map90 $ack.documents 'context';Require90 ($docs.ContainsKey('Part') -and $docs.ContainsKey('Sheet')) 'ACK contexts absent'
  foreach($kind in @('Part','Sheet')){
   $meta=$docs[$kind];Closed90 $meta @('context','document_id','internal_name','native_path','display_name','subtype','sketch_id','parameter_key','native_database_revision_id','source_ids')
   Closed90 $meta.source_ids @('line','start','end','text','origin','parameter_text')
   Require90 ($meta.subtype -ceq $kind -and (SamePath90 ([IO.Path]::GetDirectoryName([string]$meta.native_path)) ([string]$ready.fixture_directory)) -and $meta.document_id -ceq ('doc_'+$meta.internal_name)) 'ACK subtype/path/internal identity differs'
   [void](Opaque90 $meta.sketch_id $meta.document_id 'sketch')
   foreach($role in @('line','start','end','text','origin','parameter_text')){[void](Opaque90 $meta.source_ids[$role] $meta.document_id $(if($role -cin @('text','parameter_text')){'sketch_text'}else{'sketch_entity'}))}
   Require90 ($meta.source_ids.line -ceq $sourceMap[$kind].line -and $meta.source_ids.text -ceq $sourceMap[$kind].text -and [Convert]::FromBase64String([string]$meta.parameter_key).Length -gt 0) 'ACK source/parameter native key differs'
   if($null -ne $previous){$old=$previous[$kind];foreach($field in @('document_id','internal_name','sketch_id','parameter_key','source_ids')){Require90 (Equal90 $old[$field] $meta[$field]) 'ACK persistent identities changed'};$reopen=$request.action -ceq 'reopen' -and $request.context -ceq $kind;Require90 ($(if($reopen){-not (SamePath90 $old.native_path $meta.native_path)}else{SamePath90 $old.native_path $meta.native_path})) 'ACK path transition differs'}
  }
  $previous=$docs;$acks+=,$docs
 }
 Require90 ($client.lifecycle.Count -eq 6 -and $native.documents.Count -eq 2 -and $client.baselines90.Count -eq 2) 'Core/native lifecycle context/read counts differ'
 $baselines=Map90 $client.baselines90 'context';$nativeDocs=Map90 $native.documents 'context'
 $readSpec=@(@('Part',1,1,2),@('Part',2,3,4),@('Part',0,5,6),@('Sheet',1,7,8),@('Sheet',2,9,10),@('Sheet',0,11,12))
 for($r=0;$r -lt 6;$r++){
  $spec=$readSpec[$r];$kind=$spec[0];$pass=[int]$spec[1];$meta=$acks[[int]$spec[2]][$kind];$first=$client.lifecycle[$r];$initial=$baselines[$kind].snapshot_raw;$snapshot=$first.snapshot_raw;$nativeRow=$nativeDocs[$kind];$isReopen=$pass -eq 0
  Require90 ($first.context -ceq $kind -and $first.pass -eq $pass -and $first.kind -ceq $(if($isReopen){'reopen'}else{'source_edit'}) -and $first.first_core_snapshot -eq $true -and $first.copies_and_unrelated_exact -eq $true -and $first.fresh_references -eq $true -and $snapshot.complete -eq $true -and $snapshot.partial -eq $false) 'First Core read state/flags/completeness differs'
  Require90 ($first.state.document_id -ceq $meta.document_id -and $first.revision -ceq $first.state.revision -and $first.event_revision_after -ceq $first.state.revision -and $first.event_revision_before -cne $first.event_revision_after -and $first.state.revision -cmatch '^[0-9a-f]{32}:[0-9]+$' -and ([string]$first.event_revision_before).Split(':')[0] -ceq ([string]$first.event_revision_after).Split(':')[0]) 'First Core state revision/epoch differs'
  if($pass -eq 1){Require90 ($first.event_revision_before -ceq $baselines[$kind].state.revision) 'First edit does not use preparatory baseline revision'}else{Require90 ($first.event_revision_before -ceq $client.lifecycle[$r-1].event_revision_after) 'Lifecycle revision chain is discontinuous'}
  $initialMeta=$acks[0][$kind]
  if($isReopen){
   $prior=$client.lifecycle[$r-1].snapshot_raw;$priorMeta=$acks[[int]$spec[2]-1][$kind];CoreIndependence90 $prior $snapshot $priorMeta $meta $true
   $persist=$nativeRow.persistence
   Require90 ($persist.state -ceq 'reopened_verified' -and $persist.http_remains_live -eq $true -and $persist.post_open_native_semantic_reads -eq 0 -and $persist.old_typed_refs_reused -eq $false -and $persist.original_disk_unchanged -eq $true -and $persist.copy_disk_readonly -eq $true -and $persist.local_parameter_owner_relocation_verified -eq $true -and $null -eq $persist.first_difference) 'Native reopen metadata/read boundary failed'
   Require90 ((SamePath90 $persist.original_path $priorMeta.native_path) -and (SamePath90 $persist.copy_path $meta.native_path)) 'Native reopen path/ACK binding differs'
   $one=CanonicalNative90 $persist.before_save_copy $priorMeta;$two=CanonicalNative90 $persist.first_reopened_native_read $meta
   foreach($field in @('sketch','styles','user_parameters')){Require90 (Equal90 $one[$field] $two[$field]) 'Reopened native semantics changed beyond strict owner relocation'}
   CoreNative90 $snapshot $persist.first_reopened_native_read $meta
  }else{
   Require90 ($first.mutation_origin -ceq 'native_fixture_edit') 'Fixture edit misrepresented as product commit'
   CoreIndependence90 $initial $snapshot $initialMeta $meta $false
   $update=@($nativeRow.updates|Where-Object {$_.pass -eq $pass});Require90 ($update.Count -eq 1) 'Native source edit missing/duplicated';$update=$update[0]
   Require90 ($update.state -ceq 'native_checked_after_core_first_snapshot' -and $update.native_fixture_edit -eq $true -and $update.product_last_commit -eq $false -and $update.post_end_native_semantic_reads -eq 0 -and $update.copies_independent_exact -eq $true) 'Native source edit boundary/independence absent'
   NativeIndependence90 $nativeRow.initial $update.first_native_read_after_core $meta
   CoreNative90 $snapshot $update.first_native_read_after_core $meta
   $map=Map90 $snapshot.entities 'id';$line=$map[$meta.source_ids.line];$text=$map[$meta.source_ids.text]
   $wanted=if($pass -eq 1){@{start=@(8,14);end=@(40,22);origin=@(41,37);markup='Source89 pass1 &amp; changed';literal='Source89 pass1 & changed';scale=1.5;rgb=@(33,117,155)}}else{@{start=@(13,4);end=@(29,9);origin=@(16,44);markup='Source89 second content';literal='Source89 second content';scale=.75;rgb=@(95,42,61)}}
   NearArray90 $line.start_mm $wanted.start;NearArray90 $line.end_mm $wanted.end;NearArray90 $text.text_mm $wanted.origin;NearArray90 $map[$meta.source_ids.start].point_mm $wanted.start;NearArray90 $map[$meta.source_ids.end].point_mm $wanted.end;NearArray90 $map[$meta.source_ids.origin].point_mm $wanted.origin
   Require90 ($text.text -ceq $wanted.literal -and $text.formatted_text -ceq $wanted.markup -and $text.text_glyphs.vertices_mm.Count -gt 0 -and $text.text_glyphs.indices.Count -gt 0) 'First source text/content/glyphs differ'
   NearArray90 $text.text_glyphs.color_rgb $wanted.rgb 0
   $oldTextMap=Map90 $(if($pass -eq 1){$initial.entities}else{$client.lifecycle[$r-1].snapshot_raw.entities}) 'id'
   Require90 (-not (Equal90 $oldTextMap[$meta.source_ids.text].text_glyphs.vertices_mm $text.text_glyphs.vertices_mm)) 'First edited source glyphs are stale'
   $nativeTextKey=Opaque90 $meta.source_ids.text $meta.document_id 'sketch_text';$nativeText=(Map90 $update.first_native_read_after_core.sketch.texts 'key')[$nativeTextKey]
   Require90 ([double]$nativeText.width_scale -eq $wanted.scale -and $nativeText.formatted_text -ceq $wanted.markup -and $nativeText.text -ceq $wanted.literal) 'Native source content/width scale differs'
   NearArray90 $nativeText.origin_mm $wanted.origin;NearArray90 $nativeText.copy_details88.origin_mm $wanted.origin
   NearArray90 $nativeText.color_rgb $wanted.rgb 0
   Closed90 $update.requested @('start_mm','end_mm','origin_mm','formatted_text','width_scale','color_rgb')
   NearArray90 $update.requested.start_mm $wanted.start;NearArray90 $update.requested.end_mm $wanted.end;NearArray90 $update.requested.origin_mm $wanted.origin;NearArray90 $update.requested.color_rgb $wanted.rgb 0
   Require90 ($update.requested.formatted_text -ceq $wanted.markup -and [double]$update.requested.width_scale -eq $wanted.scale) 'Native fixture request differs from protocol constants'
  }
  $actualMap=Map90 $snapshot.entities 'id';$initialMap=Map90 $initial.entities 'id'
  foreach($copyId in $copies[$kind]){Require90 ($actualMap.ContainsKey($copyId) -and (Equal90 $initialMap[$copyId] $actualMap[$copyId])) 'Explicit copied object changed in Core first read'}
 }
 Require90 ($wrapper.archived_fixtures89.Count -eq 4) 'Four archived IPTs required'
 $archiveMap=Map90 $wrapper.archived_fixtures89 'name';$verifiedIPTs=@()
 foreach($kind in @('Part','Sheet')){
  Require90 ($nativeDocs[$kind].state -ceq 'two_source_updates_and_reopen_observed' -and $nativeDocs[$kind].updates.Count -eq 2) 'Native context lifecycle incomplete'
  foreach($role in @('original','copy')){
   $persist=$nativeDocs[$kind].persistence;$name=[IO.Path]::GetFileName([string]$persist[($role+'_path')]);$hash=[string]$persist[($role+'_sha256')]
   Require90 ($hash -cmatch '^[A-F0-9]{64}$' -and $archiveMap.ContainsKey($name) -and $archiveMap[$name].sha256 -ceq $hash) 'IPT native/wrapper SHA disagrees'
   $file=Get-Item -LiteralPath (Join-Path (Join-Path $script:EvidenceRoot90 'fixtures') $name)
   Require90 (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -and (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ceq $hash) 'Archived IPT bytes differ from verified native SHA'
   $verifiedIPTs+=@{name=$name;sha256=$hash}
  }
 }
 Require90 (@($verifiedIPTs.name|Select-Object -Unique).Count -eq 4) 'Archived IPT names collide'
 $files=@('wrapper-result.json','cleanup-verification-r1.json','worker/ready.json','worker/stopped.json','worker/copy-client88.json','worker/copy-client90.json','worker/copy-native-verifier88.json','worker/copy-native-lifecycle90.json')+@($checkpoints|ForEach-Object {'worker/'+$_.Name})+@($client.protocol90|ForEach-Object {'worker/copy-core-ack90-'+$_.ack.nonce+'.json'})
 $hashes=@($files|ForEach-Object {@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $script:EvidenceRoot90 $_) -Algorithm SHA256).Hash}})
 return [ordered]@{schema=1;status='PASS_PC_NATIVE_CORE_LIFECYCLE_SCOPED_EVIDENCE';run_id=$run;producer_sha256=$wrapper.stage_dll_sha256;baseline88_workflows=6;baseline88_refusals=10;native_pairs=8;native_refusal_checkpoints=20;protocol_steps=14;core_first_reads=6;source_updates=4;savecopy_reopens=2;archived_ipts=$verifiedIPTs;quest_proof=$false;physical_proof=$false;full_s1_pass=$false;open_s1_gates=18;files=$hashes;limits=@('Offline consistency and stored-data recomputation; no new CAD execution','First-read ordering/fresh typedref creation depends on archived client/host source and protocol; raw snapshots contain no full typed references','SHA records current evidence bytes, not independent authenticity or execution attestation','Only exact NONcopied13mm parameter owner-path relocation is normalized','No claim for parameter/rich/rotated/multiline text copy, arbitrary geometry/constraints, desktop or Quest')}
}
function SelfTests90([string]$OriginalRoot,$Verified){
 $tempBase=[IO.Path]::GetFullPath([IO.Path]::GetTempPath());$leaf='s1-offline-verifier90-'+[Guid]::NewGuid().ToString('N')
 $tempRoot=[IO.Path]::GetFullPath((Join-Path $tempBase $leaf));[void](New-Item -ItemType Directory -Path $tempRoot)
 $results=@();$original=[IO.Path]::GetFullPath($OriginalRoot)
 try{
  $names=@('copy_geometry','parameter_external_owner','ack_nonce','ack_ordinal','source_undeclared_property','wrong_intrinsic_annotation','ipt_sha')
  foreach($name in $names){
   $caseRoot=Join-Path $tempRoot $name;[void](New-Item -ItemType Directory -Path $caseRoot)
   $relativeFiles=@($Verified.files|ForEach-Object {$_.path})+@($Verified.archived_ipts|ForEach-Object {'fixtures/'+$_.name})
   foreach($relative in $relativeFiles){
    $source=[IO.Path]::GetFullPath((Join-Path $original $relative));$destination=[IO.Path]::GetFullPath((Join-Path $caseRoot $relative))
    Require90 ($source.StartsWith($original+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and $destination.StartsWith($caseRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'Selftest corpus path escapes owned directory'
    $parent=Split-Path -Parent $destination;if(-not (Test-Path -LiteralPath $parent)){[void](New-Item -ItemType Directory -Path $parent)}
    Copy-Item -LiteralPath $source -Destination $destination
   }
   $script:EvidenceRoot90=$caseRoot
   $clientPath=Join-Path $caseRoot 'worker/copy-client90.json';$changed=Read90 'worker/copy-client90.json'
   $metadata=$changed.protocol90[1].ack.documents|Where-Object {$_.context -ceq 'Part'}|Select-Object -First 1
   switch($name){
    'copy_geometry'{
     $id=@($changed.baselines90|Where-Object {$_.context -ceq 'Part'})[0].copy_ids|Where-Object {$id90=$_;@($changed.lifecycle[0].snapshot_raw.entities|Where-Object {$_.id -ceq $id90 -and $_.type -ceq 'line'}).Count -eq 1}|Select-Object -First 1
     $row=@($changed.lifecycle[0].snapshot_raw.entities|Where-Object {$_.id -ceq $id})[0];$row.start_mm[0]=[double]$row.start_mm[0]+1
    }
    'parameter_external_owner'{
     $row=@($changed.lifecycle[0].snapshot_raw.entities|Where-Object {$_.id -ceq $metadata.source_ids.parameter_text})[0]
     $owner=[IO.Path]::GetFullPath((Join-Path $tempBase 'foreign-owner-not-the-fixture.ipt'))
     $escaped=[Security.SecurityElement]::Escape($owner)
     $row.formatted_text="Bound=<Parameter Resolved='True' ComponentIdentifier='$escaped' Name='S1Copy88Length' Precision='0'>13</Parameter>"
    }
    'ack_nonce'{
     $nonce=$changed.protocol90[1].ack.nonce;$ackPath=Join-Path $caseRoot ('worker/copy-core-ack90-'+$nonce+'.json')
     $ack=Read90 ('worker/copy-core-ack90-'+$nonce+'.json');$ack.nonce=[Guid]::NewGuid().ToString('N')
     [IO.File]::WriteAllText($ackPath,(ConvertTo-Json -InputObject $ack -Depth 100),[Text.UTF8Encoding]::new($false))
    }
    'ack_ordinal'{$changed.protocol90[1].request.ordinal=99}
    'source_undeclared_property'{
     $row=@($changed.lifecycle[0].snapshot_raw.entities|Where-Object {$_.id -ceq $metadata.source_ids.line})[0];$row.construction=-not $row.construction
    }
    'wrong_intrinsic_annotation'{
     $row=@($changed.lifecycle[0].snapshot_raw.constraints|Where-Object {SourceIncidence90 $_ $metadata})[0]
     $row.annotation_mm[0]=[double]$row.annotation_mm[0]+2
    }
    'ipt_sha'{
     $filePath=Join-Path $caseRoot ('fixtures/'+$Verified.archived_ipts[0].name)
     $stream=[IO.FileStream]::new($filePath,[IO.FileMode]::Append,[IO.FileAccess]::Write,[IO.FileShare]::None)
     try{$stream.WriteByte(0x7f)}finally{$stream.Dispose()}
    }
   }
   if($name -cnotin @('ack_nonce','ipt_sha')){[IO.File]::WriteAllText($clientPath,(ConvertTo-Json -InputObject $changed -Depth 100),[Text.UTF8Encoding]::new($false))}
   $rejected=$false;$reason=''
   try{[void](Verify90 $caseRoot)}catch{$rejected=$true;$reason=$_.Exception.Message}
   Require90 $rejected ('Tamper case was wrongly accepted: '+$name)
   $results+=@{name=$name;status='REJECTED_AS_EXPECTED';assertion=$reason}
  }
 }finally{
  $script:EvidenceRoot90=$original
  # Delete only the exact freshly created local test root, after checking both
  # resolved absolute parent and unique leaf. Never delete input evidence.
  $resolved=[IO.Path]::GetFullPath($tempRoot)
  Require90 ([string]::Equals([IO.Path]::GetDirectoryName($resolved),$tempBase.TrimEnd([IO.Path]::DirectorySeparatorChar),[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($resolved) -ceq $leaf -and $resolved -cne $original -and (((Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0)) 'Selftest cleanup target proof failed'
  Remove-Item -LiteralPath $resolved -Recurse -Force
 }
 return ,$results
}
$evidenceRoot=[IO.Path]::GetFullPath($EvidenceDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
Require90 ((Test-Path -LiteralPath $evidenceRoot -PathType Container) -and (((Get-Item -LiteralPath $evidenceRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0)) 'Regular evidence run directory required'
$outputPath=Join-Path $evidenceRoot $OutputName
if(-not $ValidateOnly){Require90 (-not (Test-Path -LiteralPath $outputPath)) 'Verification evidence already exists; choose a new output filename'}
$result=Verify90 $evidenceRoot
if($SelfTest){$result.selftests=SelfTests90 $evidenceRoot $result}
$result.verifier_sha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
if($ValidateOnly){$result|ConvertTo-Json -Depth 10;exit 0}
# CreateNew guarantees no producer report or prior verification can be overwritten.
$json=$result|ConvertTo-Json -Depth 10
$stream=[IO.FileStream]::new($outputPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes($json);$stream.Write($bytes);$stream.Flush($true)}finally{$stream.Dispose()}
Write-Output $outputPath
