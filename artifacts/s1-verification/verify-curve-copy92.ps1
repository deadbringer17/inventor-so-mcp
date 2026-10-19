[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
 [ValidatePattern('^verification-curve-copy92-[a-zA-Z0-9-]+\.json$')][string]$OutputName='verification-curve-copy92-r1.json',
 [switch]$ValidateOnly,
 [switch]$SelfTest
)
$ErrorActionPreference='Stop'
$script:JsonOptions92=@{AsHashtable=$true;Depth=100}
# PowerShell7.5+ otherwise converts ISO strings into CLR DateTime values and
# changes their exact JSON representation/timezone when serialized again.
if((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')){$script:JsonOptions92.DateKind='String'}
# Offline consistency verifier, not an executable/native oracle. No COM,
# network, process control, assembly compilation, or producer-report writes.
function Require92([bool]$Condition,[string]$Assertion){if(-not $Condition){throw [InvalidOperationException]::new($Assertion)}}
function Json92($Value){ConvertTo-Json -InputObject $Value -Compress -Depth 100}
function Equal92($A,$B){[string]::Equals((Json92 $A),(Json92 $B),[StringComparison]::Ordinal)}
# Stable, complete serialization avoids an interpreted recursive comparison of
# millions of glyph coordinates. Object property order is conservatively exact;
# no keys, arrays, numerical values or unknown observations are discarded.
function Clone92($Value){Json92 $Value|ConvertFrom-Json @script:JsonOptions92}
function Closed92($Value,[string[]]$Fields){
 Require92 ($Value -is [Collections.IDictionary] -and $Value.Count -eq $Fields.Count) 'Closed descriptor field count differs'
 foreach($key in $Value.Keys){Require92 ($key -cin $Fields) 'Unexpected descriptor field'}
}
function Map92($Rows,[string]$Key='key'){
 $map=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
 foreach($row in $Rows){Require92 ($row -is [Collections.IDictionary] -and -not [string]::IsNullOrEmpty([string]$row[$Key]) -and $map.TryAdd([string]$row[$Key],$row)) 'Missing or duplicate native identity'}
 return ,$map
}
function Read92([string]$Relative){
 $path=[IO.Path]::GetFullPath((Join-Path $script:Root92 $Relative))
 Require92 ($path.StartsWith($script:Root92+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'Evidence path escapes root'
 $file=Get-Item -LiteralPath $path
 Require92 (-not $file.PSIsContainer -and ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -and $file.Length -gt 0 -and $file.Length -le 67108864) 'Bounded regular evidence JSON required (64MiB offline limit)'
 Get-Content -LiteralPath $path -Raw|ConvertFrom-Json @script:JsonOptions92
}
function State92($Descriptor,[string]$Run){
 Closed92 $Descriptor @('file','file_sha256')
 Require92 ($Descriptor.file -cmatch '^curve-state92-[0-9]{3}\.json$' -and $Descriptor.file_sha256 -cmatch '^[A-F0-9]{64}$' -and $script:Used92.Add([string]$Descriptor.file)) 'Sidecar name/hash missing or duplicate'
 $relative='worker/'+$Descriptor.file;$path=Join-Path $script:Root92 $relative
 $state=Read92 $relative
 Require92 ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ceq $Descriptor.file_sha256) 'Sidecar bytes SHA differs from descriptor'
 Closed92 $state @('run_id','native_state')
 Require92 ($state.run_id -ceq $Run -and $state.native_state.run_id -ceq $Run -and $state.native_state.owned_pid -eq $script:Pid92 -and $state.native_state.readonly_native_revision_dirty_preserved -eq $true) 'Sidecar run/process/read-only binding differs'
 $documents=Map92 $state.native_state.documents 'document_id';Require92 ($documents.Count -eq 2) 'Sidecar owned document count differs'
 foreach($id in $script:Documents92.Keys){
  Require92 ($documents.ContainsKey($id)) 'Sidecar document is outside the owned fixtures'
  $expected=$script:Documents92[$id];$actual=$documents[$id]
  Require92 ($actual.internal_name -ceq $expected.internal_name -and [string]::Equals([IO.Path]::GetFullPath([string]$actual.path),[IO.Path]::GetFullPath([string]$expected.saved_path),[StringComparison]::OrdinalIgnoreCase) -and $actual.file_sha256 -ceq $expected.baseline_file_sha256 -and $actual.baseline_file_unchanged -eq $true) 'Sidecar owned path/internal identity/original file hash differs'
 }
 $script:Files92.Add($relative)
 return $state.native_state
}
function Near92($Actual,$Expected,[double]$Tolerance=1e-5){
 Require92 ($Actual.Count -eq $Expected.Count -and $Actual.Count -gt 0) 'Coordinate shape differs'
 for($i=0;$i -lt $Actual.Count;$i++){
  $a=[double]$Actual[$i];$b=[double]$Expected[$i]
  Require92 ([double]::IsFinite($a) -and [double]::IsFinite($b) -and [Math]::Abs($a-$b) -le $Tolerance) 'Coordinate/radius/angle differs'
 }
}
function Translate92($Before,$After,$Delta){
 Require92 ($Before.Count -gt 0 -and $Before.Count -eq $After.Count -and $Before.Count%2 -eq 0) 'Translated XY array shape differs'
 for($i=0;$i -lt $Before.Count;$i++){Near92 @($After[$i]) @([double]$Before[$i]+[double]$Delta[$i%2])}
}
function Sketch92($State,[string]$Document,[string]$Sketch){
 $docs=Map92 $State.curve_point_graph92 'document_id';Require92 ($docs.Count -eq 2 -and $docs.ContainsKey($Document)) 'Curve graph document absent'
 $sketches=Map92 $docs[$Document].sketches
 Require92 ($sketches.ContainsKey($Sketch)) 'Target native sketch absent'
 return $sketches[$Sketch]
}
function Unrelated92($State,[string]$Document,[string]$Sketch){
 $clone=Clone92 $State
 foreach($collection in @('documents','curve_point_graph92')){
  $docs=Map92 $clone[$collection] 'document_id';Require92 ($docs.ContainsKey($Document)) 'Unrelated-state document absent'
  $doc=$docs[$Document];$matches=@($doc.sketches|Where-Object {$_.key -ceq $Sketch})
  Require92 ($matches.Count -eq 1) 'Unrelated-state target sketch ambiguous'
  $doc.sketches=@($doc.sketches|Where-Object {$_.key -cne $Sketch})
  if($collection -ceq 'documents'){[void]$doc.Remove('dirty');[void]$doc.Remove('database_revision_id')}
 }
 return $clone
}
function Inventory92($Sketch,$Rows){
 $entities=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$points=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 foreach($key in $Sketch.sketch_entities_keys){Require92 ($Rows.ContainsKey([string]$key) -and $entities.Add([string]$key)) 'SketchEntities inventory contains duplicate/unresolved key'}
 foreach($key in $Sketch.sketch_point_keys){Require92 ($Rows.ContainsKey([string]$key) -and $Rows[[string]$key].native_type -ceq 'kSketchPointObject' -and $points.Add([string]$key)) 'SketchPoints inventory contains duplicate/unresolved/nonpoint key'}
 foreach($key in $Rows.Keys){
  $row=$Rows[$key];Require92 ($row.parent_key -ceq $Sketch.key) 'Native entity parent differs from owning sketch'
  if($row.native_type -ceq 'kSketchPointObject'){Require92 ($points.Contains($key) -and -not $row.Contains('kind')) 'Native point is absent from SketchPoints or mislabeled as curve'}
  else{Require92 ($entities.Contains($key) -and $row.kind -cin @('circle','arc','line')) 'Native curve is absent from SketchEntities or unsupported'}
 }
}
function Analytic92($Curve){
 Require92 ($Curve.kind -cin @('line','circle','arc') -and $Curve.samples_mm.Count -eq 202 -and $Curve.strokes_mm.Count -ge 4 -and $Curve.strokes_mm.Count -le 16384 -and $Curve.strokes_mm.Count%2 -eq 0 -and $Curve.domain.Count -eq 2 -and [double]::IsFinite([double]$Curve.domain[0]) -and [double]::IsFinite([double]$Curve.domain[1]) -and [double]$Curve.domain[1] -gt [double]$Curve.domain[0]) 'Curve evaluator shape/domain invalid'
 $points=$Curve.structural_points
 $roles=if($Curve.kind -ceq 'line'){@('start','end')}elseif($Curve.kind -ceq 'circle'){@('center')}else{@('center','start','end')}
 Closed92 $points $roles
 foreach($role in $roles){Closed92 $points[$role] @('key','point_mm');Require92 ($points[$role].point_mm.Count -eq 2) 'Structural point XY shape invalid'}
 foreach($field in @('samples_mm','strokes_mm')){
  $values=$Curve[$field]
  for($i=0;$i -lt $values.Count;$i+=2){
   $x=[double]$values[$i];$y=[double]$values[$i+1];Require92 ([double]::IsFinite($x) -and [double]::IsFinite($y)) 'Nonfinite evaluator coordinate'
   if($Curve.kind -ceq 'line'){
    $a=$points.start.point_mm;$b=$points.end.point_mm;$dx=[double]$b[0]-[double]$a[0];$dy=[double]$b[1]-[double]$a[1];$length=[Math]::Sqrt($dx*$dx+$dy*$dy)
    Require92 ([double]::IsFinite($length) -and $length -gt 0 -and [Math]::Abs(($x-[double]$a[0])*$dy-($y-[double]$a[1])*$dx)/$length -le 1e-5) 'Evaluator leaves native analytic line'
   }else{
    $r=[double]$Curve.radius_mm;$cx=[double]$points.center.point_mm[0];$cy=[double]$points.center.point_mm[1]
    Require92 ([double]::IsFinite($r) -and $r -gt 0 -and [double]::IsFinite($cx) -and [double]::IsFinite($cy) -and [Math]::Abs([Math]::Sqrt(($x-$cx)*($x-$cx)+($y-$cy)*($y-$cy))-$r) -le 1e-5) 'Evaluator leaves native analytic circle'
   }
  }
 }
 if($Curve.kind -ceq 'arc'){
  foreach($field in @('start_angle_rad','sweep_angle_rad','geometry_start_angle_rad','geometry_sweep_angle_rad')){Require92 ($Curve.Contains($field) -and [double]::IsFinite([double]$Curve[$field])) 'Native arc extent missing/nonfinite'}
  foreach($role in @('start','end')){$p=$points[$role].point_mm;$c=$points.center.point_mm;Near92 @([Math]::Sqrt([Math]::Pow([double]$p[0]-[double]$c[0],2)+[Math]::Pow([double]$p[1]-[double]$c[1],2))) @($Curve.radius_mm)}
 }
}
function Phase92($Before,$After,$Phase,[string]$Document,[string]$Sketch,[string[]]$Selected,$Delta){
 $a=Sketch92 $Before $Document $Sketch;$b=Sketch92 $After $Document $Sketch
 Require92 ($b.constraint_status -notmatch '(?i)Over|Unknown' -and $a.dimension_count -eq $b.dimension_count) 'Solver/dimension count differs'
 $old=Map92 $a.entities;$now=Map92 $b.entities;$oldConstraints=Map92 $a.constraints;$nowConstraints=Map92 $b.constraints
 Inventory92 $a $old;Inventory92 $b $now
 foreach($field in @('sketch_entities_keys','sketch_point_keys')){foreach($key in $a[$field]){Require92 ($key -cin $b[$field]) 'Existing native collection member disappeared'}}
 foreach($key in $old.Keys){Require92 ($now.ContainsKey($key) -and (Equal92 $old[$key] $now[$key])) 'Source/prior-copy/native point changed'}
 foreach($key in $oldConstraints.Keys){Require92 ($nowConstraints.ContainsKey($key) -and (Equal92 $oldConstraints[$key] $nowConstraints[$key])) 'Existing native constraint changed'}
 $new=@($now.Values|Where-Object {-not $old.ContainsKey([string]$_.key)});$curves=@($new|Where-Object {$_.Contains('kind')})
 Require92 ($curves.Count -eq $Selected.Count -and @($Selected|Select-Object -Unique).Count -eq $Selected.Count) 'Selected/new curve cardinality differs'
 $map=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal);$usedPoints=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$copiedKeys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 $pairs=Map92 $Phase.pairs 'source_key';Require92 ($pairs.Count -eq $Selected.Count) 'Reported pair cardinality differs'
 $unknowns=[Collections.Generic.List[string]]::new()
 foreach($sourceKey in $Selected){
  Require92 ($old.ContainsKey($sourceKey) -and $pairs.ContainsKey($sourceKey)) 'Selected native source absent'
  $source=$old[$sourceKey];$candidates=@($curves|Where-Object {$_.kind -ceq $source.kind});Require92 ($candidates.Count -eq 1) 'Curve kind does not uniquely pair source/copy'
  $copy=$candidates[0];$pair=$pairs[$sourceKey]
  Require92 ($copy.parent_key -ceq $Sketch -and $copy.constraint_status -notmatch '(?i)Over|Unknown' -and $pair.copy_key -ceq $copy.key -and $map.TryAdd($sourceKey,[string]$copy.key) -and $copiedKeys.Add([string]$copy.key) -and (Equal92 $pair.source $source) -and (Equal92 $pair.copy $copy)) 'Pair/raw identity or parent differs'
  Analytic92 $source;Analytic92 $copy
  Require92 ((Equal92 @($source.structural_points.Keys) @($copy.structural_points.Keys)) -and $pair.point_mapping.Count -eq $source.structural_points.Count) 'Structural native roles differ'
  foreach($role in $source.structural_points.Keys){
   $sp=$source.structural_points[$role];$cp=$copy.structural_points[$role];$p=$pair.point_mapping[$role]
   Require92 ($old.ContainsKey([string]$sp.key) -and $now.ContainsKey([string]$cp.key) -and -not $old.ContainsKey([string]$cp.key) -and $now[[string]$cp.key].native_type -ceq 'kSketchPointObject' -and (Equal92 $now[[string]$cp.key].point_mm $cp.point_mm) -and (Equal92 $old[[string]$sp.key].point_mm $sp.point_mm) -and $p.source_key -ceq $sp.key -and $p.copy_key -ceq $cp.key) 'Structural key reused/unresolved or point mapping differs'
   if($map.ContainsKey([string]$sp.key)){Require92 ($map[[string]$sp.key] -ceq $cp.key) 'Shared native point mapping differs'}else{Require92 (-not $map.ContainsValue([string]$cp.key) -and $map.TryAdd([string]$sp.key,[string]$cp.key)) 'Distinct source points collapse into one copied point'}
   [void]$usedPoints.Add([string]$cp.key);Translate92 $sp.point_mm $cp.point_mm $Delta
  }
  foreach($field in @('native_type','construction','reference','has_reference_component','style_flags','centerline')){Require92 ($source.Contains($field) -eq $copy.Contains($field)) 'Copy property presence differs';if($source.Contains($field)){Require92 (Equal92 $source[$field] $copy[$field]) 'Copy known flags differ'}}
  # Archived92r2 translated arc domains differ around1e-16rad. Json.NET's
  # JToken.DeepEquals accepts these via ApproxEquals for floating values; this
  # verifier states the explicit1e-10 numeric domain tolerance instead. Only this
  # source-to-copy analytic comparison uses the stated angular tolerance;
  # unchanged sources/prior copies/unrelated state/Abort remain byte-value exact.
  Near92 $copy.domain $source.domain 1e-10
  foreach($field in @('radius_mm','start_angle_rad','sweep_angle_rad','geometry_start_angle_rad','geometry_sweep_angle_rad')){Require92 ($source.Contains($field) -eq $copy.Contains($field)) 'Analytic property presence differs';if($source.Contains($field)){Near92 @($copy[$field]) @($source[$field]) $(if($field -ceq 'radius_mm'){1e-8}else{1e-10})}}
  foreach($field in @('samples_mm','strokes_mm')){Translate92 $source[$field] $copy[$field] $Delta}
  foreach($owner in $copy.owned_by){Require92 ($now.ContainsKey([string]$owner) -and -not $old.ContainsKey([string]$owner)) 'Copied curve owner enters old geometry'}
  foreach($flag in $copy.style_flags.Keys){if($copy.style_flags[$flag] -is [Collections.IDictionary] -and $copy.style_flags[$flag].status -ceq 'UNKNOWN'){$unknowns.Add($copy.key+':'+$flag)}}
 }
 foreach($row in $new){Require92 ($row.Contains('kind') -or $usedPoints.Contains([string]$row.key)) 'Unexplained added entity'}
 $added=@($nowConstraints.Values|Where-Object {-not $oldConstraints.ContainsKey([string]$_.key)});$expected=[Collections.Generic.List[string]]::new()
 foreach($constraint in $oldConstraints.Values){
  Require92 ($constraint.binding_status -ceq 'COMPLETE_TYPED_MEMBERS') 'Unknown source constraint adapter'
  $touches=@($constraint.members|Where-Object {$map.ContainsKey([string]$_.key)}).Count -gt 0
  if($touches){
   $mapped=Clone92 $constraint;[void]$mapped.Remove('key')
   foreach($member in $mapped.members){Require92 ($map.ContainsKey([string]$member.key)) 'Source constraint crosses selection boundary';$member.key=$map[[string]$member.key]}
   $expected.Add((Json92 $mapped))
  }
 }
 foreach($constraint in $added){
  Require92 ($constraint.binding_status -ceq 'COMPLETE_TYPED_MEMBERS') 'Unknown copied constraint adapter'
  foreach($member in $constraint.members){Require92 ($now.ContainsKey([string]$member.key) -and -not $old.ContainsKey([string]$member.key)) 'Copied constraint enters old geometry';Require92 ($member.type -ceq $now[[string]$member.key].native_type) 'Constraint member native type differs'}
  $candidate=Clone92 $constraint;[void]$candidate.Remove('key');$index=$expected.IndexOf((Json92 $candidate));Require92 ($index -ge 0) 'Copied intrinsic graph differs from mapped source graph';$expected.RemoveAt($index)
 }
 Require92 ($expected.Count -eq 0 -and (Equal92 $Phase.new_constraints $added)) 'Missing mapped intrinsic constraint or reported graph differs'
 $newKeys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);foreach($row in @($new)+@($added)){Require92 ($newKeys.Add([string]$row.key)) 'New entity/constraint keys overlap'}
 $declared=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);foreach($key in $Phase.new_entity_keys){Require92 ($declared.Add([string]$key) -and @($new|Where-Object {$_.key -ceq $key}).Count -eq 1) 'Reported new entity missing/duplicate'}
 Require92 ($declared.Count -eq $new.Count) 'Reported new entity set incomplete'
 $returned=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 foreach($row in $Phase.returned){
  Require92 ($newKeys.Contains([string]$row.key) -and $returned.Add([string]$row.key)) 'Returned enumerator contains old/unresolved/duplicate key'
  if($row.category -ceq 'entity'){Require92 ($now.ContainsKey([string]$row.key) -and $row.type -ceq $now[[string]$row.key].native_type) 'Returned entity native type differs'}
  elseif($row.category -ceq 'constraint'){$clone=Clone92 $row;[void]$clone.Remove('category');Require92 ($nowConstraints.ContainsKey([string]$row.key) -and (Equal92 $clone $nowConstraints[[string]$row.key])) 'Returned constraint differs'}
  else{throw 'Returned object lacks supported typed category'}
 }
 foreach($key in $copiedKeys){Require92 ($returned.Contains($key)) 'Returned enumerator omitted a copied curve'}
 Require92 ((Equal92 (Unrelated92 $Before $Document $Sketch) (Unrelated92 $After $Document $Sketch))) 'Unrelated native document/sketch/style/parameter changed'
 Require92 ($Phase.status -ceq 'OBSERVED_TRANSLATION_GRAPH_AND_KNOWN_FLAGS' -and $Phase.existing_and_prior_copies_exact -eq $true -and $Phase.intrinsic_graph_from_native_members_exact -eq $true -and $Phase.known_style_flags_exact -eq $true) 'Phase assertion/status absent'
 $reported=@($Phase.optional_style_unknowns_are_not_coverage|Sort-Object);$computed=@($unknowns|Sort-Object);Require92 (Equal92 $reported $computed) 'Optional UNKNOWN coverage inventory differs'
 return ,@($computed)
}
function Verify92([string]$Root){
 $script:Root92=[IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar);$script:Used92=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$script:Files92=[Collections.Generic.List[string]]::new()
 $wrapper=Read92 'wrapper-result.json';$ready=Read92 'worker/ready.json';$stopped=Read92 'worker/stopped.json';$report=Read92 'worker/curve-copy92.json';$cleanup=Read92 'cleanup-verification-r1.json';$script:Pid92=$ready.owned_pid
 Require92 ($wrapper.exit_code -eq 0 -and $wrapper.worker_exit_code -eq 0 -and $wrapper.worker_exited -eq $true -and $wrapper.manifest_restored -eq $true -and $wrapper.original_dll_unchanged -eq $true -and $wrapper.observation_only -eq $true -and $wrapper.readonly_inspection -eq $true -and $wrapper.product_pass -eq $false -and $wrapper.case_count -eq 10 -and $wrapper.copy_observations -eq 20 -and $wrapper.observation_status -ceq 'OBSERVATIONS_COMPLETE_SCOPED') 'Terminal wrapper/cleanup or observation scope differs'
 $run=[string]$wrapper.run_id;Require92 ($run -cmatch '^[0-9a-f]{32}$' -and $ready.status -ceq 'ready' -and $ready.target_id -ceq ('inventor-2027-'+$ready.owned_pid) -and $ready.http_started -eq $false -and $ready.bootstrap_exposed -eq $false) 'Owned research target/http/bootstrap binding differs'
 foreach($row in @($ready,$stopped,$report)){Require92 ($row.run_id -ceq $run -and $row.target_id -ceq $ready.target_id) 'Cross-report run/target binding differs'}
 Require92 ($stopped.status -ceq 'stopped' -and $stopped.exit_code -eq 0 -and $stopped.owned_pid -eq $ready.owned_pid -and $stopped.owned_start_utc_ticks -eq $ready.owned_start_utc_ticks -and $stopped.owned_final_empty -eq $true -and $stopped.owned_pid_exited -eq $true -and $stopped.existing_processes_preserved -eq $true -and $stopped.http_stopped -eq $true) 'Owned cleanup/process evidence incomplete'
 $before=Map92 $ready.existing_processes_before 'pid';$after=Map92 $stopped.existing_processes_after 'pid'
 foreach($key in $before.Keys){Require92 ($after.ContainsKey($key) -and $before[$key].start_utc_ticks -eq $after[$key].start_utc_ticks) 'Preexisting process PID/start identity changed'}
 Require92 (-not $before.ContainsKey([string]$ready.owned_pid) -and (Equal92 $ready.existing_processes_before $stopped.existing_processes_before)) 'Owned process was preexisting or initial process inventory differs'
 if($after.ContainsKey([string]$ready.owned_pid)){Require92 ($after[[string]$ready.owned_pid].start_utc_ticks -eq $ready.owned_start_utc_ticks -and $stopped.owned_pid_exited -eq $true) 'Final inventory owned entry has a different process identity or unconfirmed exit'}
 Closed92 $cleanup @('schema','run_id','owned_pid','owned_start_utc_ticks','owned_pid_absent','checked_utc','check')
 $checkedUtc=[DateTimeOffset]::MinValue
 Require92 ($cleanup.schema -eq 1 -and $cleanup.run_id -ceq $run -and $cleanup.owned_pid -eq $ready.owned_pid -and $cleanup.owned_start_utc_ticks -eq $ready.owned_start_utc_ticks -and $cleanup.owned_pid_absent -eq $true -and [DateTimeOffset]::TryParse([string]$cleanup.checked_utc,[ref]$checkedUtc) -and $checkedUtc.Offset -eq [TimeSpan]::Zero -and $checkedUtc.UtcDateTime -ge (Get-Item -LiteralPath (Join-Path $script:Root92 'wrapper-result.json')).LastWriteTimeUtc) 'Independent post-wrapper OS absence evidence is missing, earlier than wrapper publication, or identity differs'
 foreach($row in @($ready.installation_ready,$stopped.installation_after)){
  Require92 ($row.unchanged -eq $true -and $row.stage_addin_sha256 -ceq $wrapper.stage_dll_sha256 -and $row.original_installed_dll_sha256 -ceq $wrapper.original_dll_sha256) 'Staged/original DLL binding differs'
 }
 Require92 ($ready.loaded_addin.pid -eq $ready.owned_pid -and $ready.loaded_addin.sha256 -ceq $wrapper.stage_dll_sha256 -and $ready.loaded_addin.verification -ceq 'owned_process_module_path_plus_disk_sha256' -and $ready.loaded_addin.memory_bytes_hashed -eq $false) 'Loaded candidate attestation differs'
 Require92 ($report.status -ceq 'OBSERVATIONS_COMPLETE_SCOPED' -and $report.product_pass -eq $false -and $report.cases.Count -eq 10 -and $report.copy_observations -eq 20 -and $report.native_state_files -eq 60) 'Terminal native observation matrix incomplete'
 $documents=Map92 $ready.owned_documents 'document_id';$script:Documents92=$documents;Require92 ($documents.Count -eq 2 -and $documents.ContainsKey([string]$ready.part_document_id) -and $documents.ContainsKey([string]$ready.sheet_document_id)) 'Owned fixture document identities absent'
 foreach($doc in $documents.Values){Require92 ($doc.document_id -ceq ('doc_'+$doc.internal_name) -and [IO.Path]::IsPathFullyQualified([string]$doc.saved_path) -and [string]::Equals([IO.Path]::GetFullPath([IO.Path]::GetDirectoryName([string]$doc.saved_path)),[IO.Path]::GetFullPath([string]$ready.fixture_directory),[StringComparison]::OrdinalIgnoreCase)) 'Owned document path/root/internal identity differs'}
 $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$unknowns=[Collections.Generic.List[string]]::new();$summary=@();$sequence=0
 $profiles=@('circle','arc_quarter','arc_wrap','arc_major','arc_negative_ctor');$starts=@(0,0,170,30,30);$sweeps=@(360,90,90,270,-90)
 foreach($case in $report.cases){
  Require92 ($case.context -cin @('Part','Sheet') -and $case.profile -cin $profiles -and $seen.Add($case.context+'|'+$case.profile) -and $case.status -ceq 'OBSERVED_TWO_NATIVE_COPY_PHASES' -and $case.phases.Count -eq 2 -and $case.transaction_started -eq $true -and $case.owned_abort_returned -eq $true -and $case.rollback_full_native_exact -eq $true) 'Case matrix/transaction/Abort flags differ'
  $index=[Array]::IndexOf($profiles,[string]$case.profile);$request=$case.constructor_request
  Near92 $request.center_mm @(17,-11);Near92 @($request.radius_mm) @(9) 0;Near92 @($request.requested_start_deg) @($starts[$index]) 0;Near92 @($request.requested_sweep_deg) @($sweeps[$index]) 0
  Require92 ($request.constructor -ceq $(if($index -eq 0){'AddByCenterRadius'}else{'AddByCenterStartEndPoint'}) -and $request.counterclockwise -eq ($sweeps[$index] -gt 0)) 'Constructor request differs from declared profile'
  $document=if($case.context -ceq 'Part'){$ready.part_document_id}else{$ready.sheet_document_id}
  $baseline=State92 $case.full_native_before $run;$sequence++;Require92 ($case.full_native_before.file -ceq ('curve-state92-'+$sequence.ToString('D3')+'.json')) 'State file ordinal differs'
  $primary=[string]$case.phases[0].pairs[0].source_key;$sketch=[string]$case.phases[0].pairs[0].source.parent_key
  $baseSketch=Sketch92 $baseline $document $sketch;Require92 ($baseSketch.name -ceq ('S1_CurveCopy92_'+$index)) 'Case sketch name/profile differs'
  $baseEntities=Map92 $baseSketch.entities;$basePrimary=$baseEntities[$primary]
  Require92 ($basePrimary.kind -ceq $(if($index -eq 0){'circle'}else{'arc'})) 'Primary curve kind differs'
  Near92 $basePrimary.structural_points.center.point_mm @(17,-11);Near92 @($basePrimary.radius_mm) @(9) 1e-8
  if($index -gt 0){
   # The constructor may normalize clockwise arcs by exchanging native roles.
   # Match both endpoint locations and the absolute geometric extent; preserve
   # the actual role/direction values exactly in source-to-copy comparisons.
   $startRadians=$starts[$index]*[Math]::PI/180;$endRadians=($starts[$index]+$sweeps[$index])*[Math]::PI/180
   $expectedStart=@((17+9*[Math]::Cos($startRadians)),(-11+9*[Math]::Sin($startRadians)));$expectedEnd=@((17+9*[Math]::Cos($endRadians)),(-11+9*[Math]::Sin($endRadians)))
   $nativeStart=$basePrimary.structural_points.start.point_mm;$nativeEnd=$basePrimary.structural_points.end.point_mm
   $direct=[Math]::Abs([double]$nativeStart[0]-$expectedStart[0]) -le 1e-5 -and [Math]::Abs([double]$nativeStart[1]-$expectedStart[1]) -le 1e-5
   if($direct){Near92 $nativeStart $expectedStart;Near92 $nativeEnd $expectedEnd}else{Near92 $nativeStart $expectedEnd;Near92 $nativeEnd $expectedStart}
   Near92 @([Math]::Abs([double]$basePrimary.geometry_sweep_angle_rad)) @([Math]::Abs($sweeps[$index])*[Math]::PI/180) 1e-10
  }
  $previous=$null
  for($phaseIndex=0;$phaseIndex -lt 2;$phaseIndex++){
   $phase=$case.phases[$phaseIndex];$a=State92 $phase.before $run;$sequence++;Require92 ($phase.before.file -ceq ('curve-state92-'+$sequence.ToString('D3')+'.json')) 'Before sidecar ordinal differs'
   $b=State92 $phase.after $run;$sequence++;Require92 ($phase.after.file -ceq ('curve-state92-'+$sequence.ToString('D3')+'.json')) 'After sidecar ordinal differs'
   Require92 (Equal92 $(if($phaseIndex -eq 0){$baseline}else{$previous}) $a) 'Phase state chain differs'
   $delta=if($phaseIndex -eq 0){@(12,-8)}else{@(34,17)};Near92 $phase.delta_mm $delta 0
   Require92 ($phase.selection -ceq $(if($phaseIndex -eq 0){'primary_only'}else{'primary_companion_freeLine'}) -and $phase.pairs.Count -eq $(if($phaseIndex -eq 0){1}else{3})) 'Selection phase differs'
   $selected=@($phase.pairs|ForEach-Object {[string]$_.source_key});Require92 ($selected[0] -ceq $primary) 'Primary source changed between phases'
   if($phaseIndex -eq 1){$kinds=@($selected|ForEach-Object {$baseEntities[$_].kind}|Sort-Object);Require92 (Equal92 $kinds @('arc','circle','line')) 'Mixed selection must have three distinct curve kinds'}
   foreach($unknown in (Phase92 $a $b $phase $document $sketch $selected $delta)){$unknowns.Add($case.context+'/'+$case.profile+'/'+$phaseIndex+'/'+$unknown)}
   $previous=$b
  }
  $restored=State92 $case.full_native_after_abort $run;$sequence++;Require92 ($case.full_native_after_abort.file -ceq ('curve-state92-'+$sequence.ToString('D3')+'.json') -and (Equal92 $baseline $restored)) 'Abort raw full native state differs'
  $summary+=@{context=$case.context;profile=$case.profile;phases=2;full_abort_exact=$true;geometry_and_intrinsic_graph_recomputed=$true}
 }
 Require92 ($sequence -eq 60 -and @(Get-ChildItem -LiteralPath (Join-Path $script:Root92 'worker') -Filter 'curve-state92-*.json' -File).Count -eq 60) 'Raw sidecar inventory differs'
 $allFiles=@('wrapper-result.json','worker/ready.json','worker/stopped.json','worker/curve-copy92.json','cleanup-verification-r1.json')+@($script:Files92)
 $hashes=@($allFiles|ForEach-Object {@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $script:Root92 $_) -Algorithm SHA256).Hash}})
 return [ordered]@{schema=1;status='VERIFIED_ABORTED_NATIVE_CURVE_COPY92_OBSERVATIONS_ONLY';run_id=$run;candidate_sha256=$wrapper.stage_dll_sha256;cases=$summary;native_copy_phases=20;exact_aborts=10;raw_state_files=60;optional_style_unknowns=@($unknowns);post_wrapper_owned_absence=$true;owned_entry_in_stop_inventory=$after.ContainsKey([string]$ready.owned_pid);product_pass=$false;full_s1_pass=$false;quest_proof=$false;physical_proof=$false;open_s1_gates=18;files=$hashes;not_covered=@($report.not_covered);limits=@('Offline stored-state consistency; native execution and attribution depend on archived host/guards','Exact serialized full JSON comparison includes object-property order; only target sketch and target document Dirty/DBRevision are excluded during phases','Source-to-copy domain/angles tolerance1e-10rad, radius1e-8mm, XY1e-5mm; no tolerance in unchanged-state or Abort comparisons','Inventory73 is not a live-process filter; any residual owned entry requires exact start ticks, exit attestation and independent later OS absence evidence','Returned enumerator may omit new points/constraints; it must include all new curves and only valid uniquely typed new objects','Optional UNKNOWN style getters remain unverified','No source/copy edit, persistence, product preview/Apply, Core/HTTPS, desktop or Quest proof; no full production/S1 claim','Hashes attest current evidence bytes, not independent authenticity')}
}
function SelfTest92([string]$Original,$Verified){
 $tempBase=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar);$leaf='s1-curve92-offline-'+[Guid]::NewGuid().ToString('N');$temp=Join-Path $tempBase $leaf
 [void](New-Item -ItemType Directory -Path $temp);$results=@()
 try{
  foreach($name in @('copy_geometry','old_structural_point','constraint_external_member','unrelated_parameter','abort_dirty')){
   $caseRoot=Join-Path $temp $name;[void](New-Item -ItemType Directory -Path $caseRoot)
   foreach($entry in $Verified.files){$relative=[string]$entry.path;$source=[IO.Path]::GetFullPath((Join-Path $Original $relative));$destination=[IO.Path]::GetFullPath((Join-Path $caseRoot $relative));Require92 ($source.StartsWith($Original+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and $destination.StartsWith($caseRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'Selftest path escapes explicit corpus';$parent=Split-Path -Parent $destination;if(-not (Test-Path -LiteralPath $parent)){[void](New-Item -ItemType Directory -Path $parent)};Copy-Item -LiteralPath $source -Destination $destination}
   $script:Root92=$caseRoot;$report=Read92 'worker/curve-copy92.json';$case=$report.cases[0];$phase=$case.phases[0];$descriptor=if($name -ceq 'abort_dirty'){$case.full_native_after_abort}else{$phase.after};$state=Read92 ('worker/'+$descriptor.file);$document=$phase.pairs[0].source.parent_key;$graphDoc=@($state.native_state.curve_point_graph92|Where-Object {@($_.sketches|Where-Object {$_.key -ceq $document}).Count -eq 1})[0];$sketch=Sketch92 $state.native_state $graphDoc.document_id $document;$entities=Map92 $sketch.entities;$copy=$entities[[string]$phase.pairs[0].copy_key]
   switch($name){
    'copy_geometry'{$copy.radius_mm=[double]$copy.radius_mm+1;$phase.pairs[0].copy.radius_mm=$copy.radius_mm}
    'old_structural_point'{$oldPoint=$phase.pairs[0].source.structural_points.center.key;$copy.structural_points.center.key=$oldPoint;$phase.pairs[0].copy.structural_points.center.key=$oldPoint;$phase.pairs[0].point_mapping.center.copy_key=$oldPoint}
    'constraint_external_member'{$added=Map92 $sketch.constraints;$key=$phase.new_constraints[0].key;$added[$key].members[0].key=$phase.pairs[0].source_key;$phase.new_constraints[0].members[0].key=$phase.pairs[0].source_key}
    'unrelated_parameter'{$doc=@($state.native_state.documents|Where-Object {$_.document_id -ceq $graphDoc.document_id})[0];$doc.parameters[0].expression='tampered-independent-parameter'}
    'abort_dirty'{$state.native_state.documents[0].dirty=-not $state.native_state.documents[0].dirty}
   }
   $path=Join-Path $caseRoot ('worker/'+$descriptor.file);[IO.File]::WriteAllText($path,(Json92 $state),[Text.UTF8Encoding]::new($false));$descriptor.file_sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
   [IO.File]::WriteAllText((Join-Path $caseRoot 'worker/curve-copy92.json'),(Json92 $report),[Text.UTF8Encoding]::new($false))
   $rejected=$false;$reason='';try{[void](Verify92 $caseRoot)}catch{$rejected=$true;$reason=$_.Exception.Message};Require92 $rejected ('Semantic tamper accepted: '+$name);$results+=@{name=$name;status='REJECTED_AS_EXPECTED';sidecar_sha_updated=$true;assertion=$reason}
  }
 }finally{
  $resolved=[IO.Path]::GetFullPath($temp);Require92 ([IO.Path]::GetDirectoryName($resolved) -ceq $tempBase -and [IO.Path]::GetFileName($resolved) -ceq $leaf -and $resolved -cne $Original -and ((Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'Selftest cleanup owned path check failed';Remove-Item -LiteralPath $resolved -Recurse -Force;$script:Root92=$Original
 }
 return ,$results
}
$root=[IO.Path]::GetFullPath($EvidenceDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
Require92 ((Test-Path -LiteralPath $root -PathType Container) -and ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'Regular evidence directory required'
$output=Join-Path $root $OutputName;if(-not $ValidateOnly){Require92 (-not (Test-Path -LiteralPath $output)) 'Verification output exists; choose a new name'}
$result=Verify92 $root;if($SelfTest){$result.selftests=SelfTest92 $root $result};$result.verifier_sha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
if($ValidateOnly){$result|ConvertTo-Json -Depth 12;exit 0}
$stream=[IO.FileStream]::new($output,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes(($result|ConvertTo-Json -Depth 12));$stream.Write($bytes);$stream.Flush($true)}finally{$stream.Dispose()}
Write-Output $output
