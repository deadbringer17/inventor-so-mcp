[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
 [ValidatePattern('^verification-copy-core95-[a-zA-Z0-9-]+\.json$')][string]$OutputName='verification-copy-core95-r1.json',
 [switch]$ValidateOnly,
 [switch]$SelfTest
)
$ErrorActionPreference='Stop'
$script:JsonOptions94=@{AsHashtable=$true;Depth=100}
if((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')){$script:JsonOptions94.DateKind='String'}
# Offline stored evidence only. No process/native/COM/network calls or producer execution.
# Baseline94 functions are copied here so this verifier is standalone and preserves
# its full18 workflows/28pairs/10refusals/74raw checkpoint comparison.
function Require94([bool]$Condition,[string]$Assertion){if(-not $Condition){throw [InvalidOperationException]::new($Assertion)}}
function Json94($Value){ConvertTo-Json -InputObject $Value -Compress -Depth 100}
function Equal94($A,$B){[string]::Equals((Json94 $A),(Json94 $B),[StringComparison]::Ordinal)}
function Clone94($Value){Json94 $Value|ConvertFrom-Json @script:JsonOptions94}
function Closed94($Value,[string[]]$Fields){
 Require94 ($Value -is [Collections.IDictionary] -and $Value.Count -eq $Fields.Count) 'Closed descriptor field count differs'
 foreach($key in $Value.Keys){Require94 ($key -cin $Fields) 'Unexpected descriptor field'}
}
function Map94($Rows,[string]$Key='key'){
 $map=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
 foreach($row in $Rows){Require94 ($row -is [Collections.IDictionary] -and -not [string]::IsNullOrEmpty([string]$row[$Key]) -and $map.TryAdd([string]$row[$Key],$row)) 'Missing or duplicate native identity'}
 return ,$map
}
function Read94([string]$Relative){
 $path=[IO.Path]::GetFullPath((Join-Path $script:Root94 $Relative))
 Require94 ($path.StartsWith($script:Root94+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'Evidence path escapes root'
 $file=Get-Item -LiteralPath $path
 Require94 (-not $file.PSIsContainer -and ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -and $file.Length -gt 0 -and $file.Length -le 67108864) 'Bounded regular evidence JSON required (64MiB offline limit)'
 Get-Content -LiteralPath $path -Raw|ConvertFrom-Json @script:JsonOptions94
}
function Near94($Actual,$Expected,[double]$Tolerance=1e-5){
 Require94 ($Actual.Count -eq $Expected.Count -and $Actual.Count -gt 0) 'Coordinate shape differs'
 for($i=0;$i -lt $Actual.Count;$i++){
  $a=[double]$Actual[$i];$b=[double]$Expected[$i]
  Require94 ([double]::IsFinite($a) -and [double]::IsFinite($b) -and [Math]::Abs($a-$b) -le $Tolerance) 'Coordinate/radius/angle differs'
 }
}
function Translate94($Before,$After,$Delta){
 Require94 ($Before.Count -gt 0 -and $Before.Count -eq $After.Count -and $Before.Count%2 -eq 0) 'Translated XY array shape differs'
 for($i=0;$i -lt $Before.Count;$i++){Near94 @($After[$i]) @([double]$Before[$i]+[double]$Delta[$i%2])}
}
function Sketch94($State,[string]$Document,[string]$Sketch){
 $docs=Map94 $State.curve_point_graph92 'document_id';Require94 ($docs.Count -eq 2 -and $docs.ContainsKey($Document)) 'Curve graph document absent'
 $sketches=Map94 $docs[$Document].sketches
 Require94 ($sketches.ContainsKey($Sketch)) 'Target native sketch absent'
 return $sketches[$Sketch]
}
function Unrelated94($State,[string]$Document,[string]$Sketch){
 $clone=Clone94 $State
 foreach($collection in @('documents','curve_point_graph92')){
  $docs=Map94 $clone[$collection] 'document_id';Require94 ($docs.ContainsKey($Document)) 'Unrelated-state document absent'
  $doc=$docs[$Document];$matches=@($doc.sketches|Where-Object {$_.key -ceq $Sketch})
  Require94 ($matches.Count -eq 1) 'Unrelated-state target sketch ambiguous'
  $doc.sketches=@($doc.sketches|Where-Object {$_.key -cne $Sketch})
  if($collection -ceq 'documents'){[void]$doc.Remove('dirty');[void]$doc.Remove('database_revision_id')}
 }
 return $clone
}
function Inventory94($Sketch,$Rows){
 $entities=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$points=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 foreach($key in $Sketch.sketch_entities_keys){Require94 ($Rows.ContainsKey([string]$key) -and $entities.Add([string]$key)) 'SketchEntities inventory contains duplicate/unresolved key'}
 foreach($key in $Sketch.sketch_point_keys){Require94 ($Rows.ContainsKey([string]$key) -and $Rows[[string]$key].native_type -ceq 'kSketchPointObject' -and $points.Add([string]$key)) 'SketchPoints inventory contains duplicate/unresolved/nonpoint key'}
 foreach($key in $Rows.Keys){
  $row=$Rows[$key];Require94 ($row.parent_key -ceq $Sketch.key) 'Native entity parent differs from owning sketch'
  if($row.native_type -ceq 'kSketchPointObject'){Require94 ($points.Contains($key) -and -not $row.Contains('kind')) 'Native point is absent from SketchPoints or mislabeled as curve'}
  else{Require94 ($entities.Contains($key) -and $row.kind -cin @('circle','arc','line')) 'Native curve is absent from SketchEntities or unsupported'}
 }
}
function Analytic94($Curve){
 Require94 ($Curve.kind -cin @('line','circle','arc') -and $Curve.samples_mm.Count -eq 202 -and $Curve.strokes_mm.Count -ge 4 -and $Curve.strokes_mm.Count -le 16384 -and $Curve.strokes_mm.Count%2 -eq 0 -and $Curve.domain.Count -eq 2 -and [double]::IsFinite([double]$Curve.domain[0]) -and [double]::IsFinite([double]$Curve.domain[1]) -and [double]$Curve.domain[1] -gt [double]$Curve.domain[0]) 'Curve evaluator shape/domain invalid'
 $points=$Curve.structural_points
 $roles=if($Curve.kind -ceq 'line'){@('start','end')}elseif($Curve.kind -ceq 'circle'){@('center')}else{@('center','start','end')}
 Closed94 $points $roles
 foreach($role in $roles){Closed94 $points[$role] @('key','point_mm');Require94 ($points[$role].point_mm.Count -eq 2) 'Structural point XY shape invalid'}
 foreach($field in @('samples_mm','strokes_mm')){
  $values=$Curve[$field]
  for($i=0;$i -lt $values.Count;$i+=2){
   $x=[double]$values[$i];$y=[double]$values[$i+1];Require94 ([double]::IsFinite($x) -and [double]::IsFinite($y)) 'Nonfinite evaluator coordinate'
   if($Curve.kind -ceq 'line'){
    $a=$points.start.point_mm;$b=$points.end.point_mm;$dx=[double]$b[0]-[double]$a[0];$dy=[double]$b[1]-[double]$a[1];$length=[Math]::Sqrt($dx*$dx+$dy*$dy)
    Require94 ([double]::IsFinite($length) -and $length -gt 0 -and [Math]::Abs(($x-[double]$a[0])*$dy-($y-[double]$a[1])*$dx)/$length -le 1e-5) 'Evaluator leaves native analytic line'
   }else{
    $r=[double]$Curve.radius_mm;$cx=[double]$points.center.point_mm[0];$cy=[double]$points.center.point_mm[1]
    Require94 ([double]::IsFinite($r) -and $r -gt 0 -and [double]::IsFinite($cx) -and [double]::IsFinite($cy) -and [Math]::Abs([Math]::Sqrt(($x-$cx)*($x-$cx)+($y-$cy)*($y-$cy))-$r) -le 1e-5) 'Evaluator leaves native analytic circle'
   }
  }
 }
 if($Curve.kind -ceq 'arc'){
  foreach($field in @('start_angle_rad','sweep_angle_rad','geometry_start_angle_rad','geometry_sweep_angle_rad')){Require94 ($Curve.Contains($field) -and [double]::IsFinite([double]$Curve[$field])) 'Native arc extent missing/nonfinite'}
  foreach($role in @('start','end')){$p=$points[$role].point_mm;$c=$points.center.point_mm;Near94 @([Math]::Sqrt([Math]::Pow([double]$p[0]-[double]$c[0],2)+[Math]::Pow([double]$p[1]-[double]$c[1],2))) @($Curve.radius_mm)}
 }
}
function Phase94($Before,$After,$Phase,[string]$Document,[string]$Sketch,[string[]]$Selected,$Delta,[string[]]$ExtraPoints){
 $a=Sketch94 $Before $Document $Sketch;$b=Sketch94 $After $Document $Sketch
 Require94 ($b.constraint_status -notmatch '(?i)Over|Unknown' -and $a.dimension_count -eq $b.dimension_count) 'Solver/dimension count differs'
 $old=Map94 $a.entities;$now=Map94 $b.entities;$oldConstraints=Map94 $a.constraints;$nowConstraints=Map94 $b.constraints
 Inventory94 $a $old;Inventory94 $b $now
 foreach($field in @('sketch_entities_keys','sketch_point_keys')){foreach($key in $a[$field]){Require94 ($key -cin $b[$field]) 'Existing native collection member disappeared'}}
 foreach($key in $old.Keys){Require94 ($now.ContainsKey($key) -and (Equal94 $old[$key] $now[$key])) 'Source/prior-copy/native point changed'}
 foreach($key in $oldConstraints.Keys){Require94 ($nowConstraints.ContainsKey($key) -and (Equal94 $oldConstraints[$key] $nowConstraints[$key])) 'Existing native constraint changed'}
 $new=@($now.Values|Where-Object {-not $old.ContainsKey([string]$_.key)});$curves=@($new|Where-Object {$_.Contains('kind')})
 Require94 ($curves.Count -eq $Selected.Count -and @($Selected|Select-Object -Unique).Count -eq $Selected.Count) 'Selected/new curve cardinality differs'
 $map=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal);$usedPoints=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$copiedKeys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 $pairs=Map94 $Phase.pairs 'source_key';Require94 ($pairs.Count -eq $Selected.Count) 'Reported pair cardinality differs'
 $unknowns=[Collections.Generic.List[string]]::new()
 foreach($sourceKey in $Selected){
  Require94 ($old.ContainsKey($sourceKey) -and $pairs.ContainsKey($sourceKey)) 'Selected native source absent'
  $source=$old[$sourceKey];$candidates=@($curves|Where-Object {$_.kind -ceq $source.kind});Require94 ($candidates.Count -eq 1) 'Curve kind does not uniquely pair source/copy'
  $copy=$candidates[0];$pair=$pairs[$sourceKey]
  Require94 ($copy.parent_key -ceq $Sketch -and $copy.constraint_status -notmatch '(?i)Over|Unknown' -and $pair.copy_key -ceq $copy.key -and $map.TryAdd($sourceKey,[string]$copy.key) -and $copiedKeys.Add([string]$copy.key) -and (Equal94 $pair.source $source) -and (Equal94 $pair.copy $copy)) 'Pair/raw identity or parent differs'
  Analytic94 $source;Analytic94 $copy
  Require94 ((Equal94 @($source.structural_points.Keys) @($copy.structural_points.Keys)) -and $pair.point_mapping.Count -eq $source.structural_points.Count) 'Structural native roles differ'
  foreach($role in $source.structural_points.Keys){
   $sp=$source.structural_points[$role];$cp=$copy.structural_points[$role];$p=$pair.point_mapping[$role]
   Require94 ($old.ContainsKey([string]$sp.key) -and $now.ContainsKey([string]$cp.key) -and -not $old.ContainsKey([string]$cp.key) -and $now[[string]$cp.key].native_type -ceq 'kSketchPointObject' -and (Equal94 $now[[string]$cp.key].point_mm $cp.point_mm) -and (Equal94 $old[[string]$sp.key].point_mm $sp.point_mm) -and $p.source_key -ceq $sp.key -and $p.copy_key -ceq $cp.key) 'Structural key reused/unresolved or point mapping differs'
   if($map.ContainsKey([string]$sp.key)){Require94 ($map[[string]$sp.key] -ceq $cp.key) 'Shared native point mapping differs'}else{Require94 (-not $map.ContainsValue([string]$cp.key) -and $map.TryAdd([string]$sp.key,[string]$cp.key)) 'Distinct source points collapse into one copied point'}
   [void]$usedPoints.Add([string]$cp.key);Translate94 $sp.point_mm $cp.point_mm $Delta
  }
  foreach($field in @('native_type','construction','reference','has_reference_component','style_flags','centerline')){Require94 ($source.Contains($field) -eq $copy.Contains($field)) 'Copy property presence differs';if($source.Contains($field)){Require94 (Equal94 $source[$field] $copy[$field]) 'Copy known flags differ'}}
  # Primitive92 translated arc domains differ around1e-16rad. Json.NET's
  # JToken.DeepEquals accepts these via ApproxEquals for floating values; this
  # verifier states the explicit1e-10 numeric domain tolerance instead. Only this
  # source-to-copy analytic comparison uses the stated angular tolerance;
  # unchanged sources/prior copies/unrelated state/Abort remain byte-value exact.
  Near94 $copy.domain $source.domain 1e-10
  foreach($field in @('radius_mm','start_angle_rad','sweep_angle_rad','geometry_start_angle_rad','geometry_sweep_angle_rad')){Require94 ($source.Contains($field) -eq $copy.Contains($field)) 'Analytic property presence differs';if($source.Contains($field)){Near94 @($copy[$field]) @($source[$field]) $(if($field -ceq 'radius_mm'){1e-8}else{1e-10})}}
  foreach($field in @('samples_mm','strokes_mm')){Translate94 $source[$field] $copy[$field] $Delta}
  foreach($owner in $copy.owned_by){Require94 ($now.ContainsKey([string]$owner) -and -not $old.ContainsKey([string]$owner)) 'Copied curve owner enters old geometry'}
  foreach($flag in $copy.style_flags.Keys){if($copy.style_flags[$flag] -is [Collections.IDictionary] -and $copy.style_flags[$flag].status -ceq 'UNKNOWN'){$unknowns.Add($copy.key+':'+$flag)}}
 }
 foreach($row in $new){Require94 ($row.Contains('kind') -or $usedPoints.Contains([string]$row.key) -or [string]$row.key -cin $ExtraPoints) 'Unexplained added entity'}
 $added=@($nowConstraints.Values|Where-Object {-not $oldConstraints.ContainsKey([string]$_.key)});$expected=[Collections.Generic.List[string]]::new()
 foreach($constraint in $oldConstraints.Values){
  Require94 ($constraint.binding_status -ceq 'COMPLETE_TYPED_MEMBERS') 'Unknown source constraint adapter'
  $touches=@($constraint.members|Where-Object {$map.ContainsKey([string]$_.key)}).Count -gt 0
  if($touches){
   $mapped=Clone94 $constraint;[void]$mapped.Remove('key')
   foreach($member in $mapped.members){Require94 ($map.ContainsKey([string]$member.key)) 'Source constraint crosses selection boundary';$member.key=$map[[string]$member.key]}
   $expected.Add((Json94 $mapped))
  }
 }
 foreach($constraint in $added){
  Require94 ($constraint.binding_status -ceq 'COMPLETE_TYPED_MEMBERS') 'Unknown copied constraint adapter'
  foreach($member in $constraint.members){Require94 ($now.ContainsKey([string]$member.key) -and -not $old.ContainsKey([string]$member.key)) 'Copied constraint enters old geometry';Require94 ($member.type -ceq $now[[string]$member.key].native_type) 'Constraint member native type differs'}
  $candidate=Clone94 $constraint;[void]$candidate.Remove('key');$index=$expected.IndexOf((Json94 $candidate));Require94 ($index -ge 0) 'Copied intrinsic graph differs from mapped source graph';$expected.RemoveAt($index)
 }
 Require94 ($expected.Count -eq 0 -and (Equal94 $Phase.new_constraints $added)) 'Missing mapped intrinsic constraint or reported graph differs'
 $newKeys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);foreach($row in @($new)+@($added)){Require94 ($newKeys.Add([string]$row.key)) 'New entity/constraint keys overlap'}
 $declared=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);foreach($key in $Phase.new_entity_keys){Require94 ($declared.Add([string]$key) -and @($new|Where-Object {$_.key -ceq $key}).Count -eq 1) 'Reported new entity missing/duplicate'}
 Require94 ($declared.Count -eq $new.Count) 'Reported new entity set incomplete'
 Require94 ((Equal94 (Unrelated94 $Before $Document $Sketch) (Unrelated94 $After $Document $Sketch))) 'Unrelated native document/sketch/style/parameter changed'
 return ,@($unknowns)
}
function Opaque94([string]$Id,[string]$Document,[string]$Type){
 Require94 ($Id.StartsWith('ent_',[StringComparison]::Ordinal) -and $Id.Length -le 131072) 'Opaque reference absent/oversized'
 $encoded=$Id.Substring(4).Replace('-','+').Replace('_','/');$encoded=$encoded.PadRight($encoded.Length+(4-$encoded.Length%4)%4,'=')
 $decoded=[Text.UTF8Encoding]::new($false,$true).GetString([Convert]::FromBase64String($encoded))|ConvertFrom-Json @script:JsonOptions94
 Closed94 $decoded @('Version','DocumentId','EntityType','Key','Context')
 Require94 ($decoded.Version -eq 1 -and $decoded.DocumentId -ceq $Document -and $decoded.EntityType -ceq $Type -and $decoded.Context -ceq '' -and [Convert]::FromBase64String([string]$decoded.Key).Length -gt 0) 'Opaque native owner/type/key differs'
 return [string]$decoded.Key
}
function Shift94($A,$B,[int]$Stride,$Delta){
 Require94 ($A.Count -gt 0 -and $A.Count -eq $B.Count -and $A.Count%$Stride -eq 0) 'Translation array shape differs'
 for($i=0;$i -lt $A.Count;$i++){$offset=if($i%$Stride -lt 2){[double]$Delta[$i%$Stride]}else{0};Near94 @($B[$i]) @([double]$A[$i]+$offset)}
}
function NativeHash94($State){
 # Match the producer's Json.NET compact serialization, including floating
 # point token spelling. No native assembly or producer helper is executed.
 $token=[Newtonsoft.Json.Linq.JToken]::Parse((Json94 $State))
 $bytes=[Text.UTF8Encoding]::new($false).GetBytes($token.ToString([Newtonsoft.Json.Formatting]::None))
 return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
}
function Texts94($State,[string]$Document,[string]$Sketch){
 $doc=(Map94 $State.documents 'document_id')[$Document];$row=(Map94 $doc.sketches)[$Sketch]
 return ,(Map94 $row.texts)
}
function PriorCore94($Before,$After){
 Require94 ($Before.complete -eq $true -and $After.complete -eq $true -and $Before.partial -eq $false -and $After.partial -eq $false) 'Core snapshot incomplete'
 foreach($collection in @('entities','dimensions','constraints')){
  $old=Map94 $Before[$collection] 'id';$now=Map94 $After[$collection] 'id'
  foreach($key in $old.Keys){Require94 ($now.ContainsKey($key) -and (Equal94 $old[$key] $now[$key])) 'Core source/prior object changed'}
  if($collection -ceq 'dimensions'){Require94 ($old.Count -eq $now.Count) 'Core dimension inventory changed'}
 }
}
function CorePair94($Source,$Copy,[string]$Type){
 Require94 ($Source.id -cne $Copy.id -and $Source.type -ceq $Type -and $Copy.type -ceq $Type -and $Copy.supported -eq $true) 'Core copy type/identity differs'
 $a=Clone94 $Source;$b=Clone94 $Copy;[void]$a.Remove('id');[void]$b.Remove('id')
 if($Type -ceq 'text'){
  foreach($field in @('text_mm','text_bounds_mm')){Shift94 $a[$field] $b[$field] 2 @(12,-8);[void]$a.Remove($field);[void]$b.Remove($field)}
  Require94 ($a.text_glyphs.coordinate_space -ceq 'model_mm' -and $b.text_glyphs.coordinate_space -ceq 'model_mm') 'Core glyph coordinate space differs'
  Shift94 $a.text_glyphs.vertices_mm $b.text_glyphs.vertices_mm 3 @(12,-8);[void]$a.text_glyphs.Remove('vertices_mm');[void]$b.text_glyphs.Remove('vertices_mm')
 }else{
  foreach($field in @('strokes_mm','start_mm','end_mm','center_mm')){Require94 ($a.Contains($field) -eq $b.Contains($field)) 'Core coordinate field presence differs';if($a.Contains($field)){Shift94 $a[$field] $b[$field] 2 @(12,-8);[void]$a.Remove($field);[void]$b.Remove($field)}}
  foreach($field in @('radius_mm','start_angle_rad','sweep_angle_rad','geometry_start_angle_rad','geometry_sweep_angle_rad','domain')){
   Require94 ($a.Contains($field) -eq $b.Contains($field)) 'Core analytic field presence differs'
   if($a.Contains($field)){Near94 @($b[$field]) @($a[$field]) $(if($field -ceq 'radius_mm'){1e-8}else{1e-10});[void]$a.Remove($field);[void]$b.Remove($field)}
  }
 }
 Require94 (Equal94 $a $b) 'Core copy noncoordinate properties/glyph topology differ'
}
function NativeTextPair94($Source,$Copy,$OldGraph,$NewGraph,$UsedPoints){
 $a=$Source.copy_details88;$b=$Copy.copy_details88;$key=[string]$b.origin_key
 Require94 (-not $OldGraph.ContainsKey($key) -and $NewGraph.ContainsKey($key) -and $UsedPoints.Add($key)) 'Native text origin reused/unresolved'
 foreach($field in @('text','formatted_text','fitted','style','rotation_rad','width_scale','color_rgb')){Require94 (Equal94 $Source[$field] $Copy[$field]) 'Native text presentation differs'}
 foreach($field in @('single_line','horizontal','vertical','line_spacing_type','line_spacing','style_key','style_location','font','font_size_mm','bold','italic','underline','color','style_color','raw_indices_native_one_based')){Require94 (Equal94 $a[$field] $b[$field]) 'Native glyph/style/flags differ'}
 Shift94 $a.origin_mm $b.origin_mm 2 @(12,-8);Shift94 $a.raw_vertices_model_mm $b.raw_vertices_model_mm 3 @(12,-8)
 return $key
}
function Verify94([string]$Root){
 $script:Root94=[IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar);$script:Files94=[Collections.Generic.List[string]]::new()
 $wrapper=Read94 'wrapper-result.json';$ready=Read94 'worker/ready.json';$stopped=Read94 'worker/stopped.json';$client=Read94 'worker/copy-client94.json';$native=Read94 'worker/copy-native-verifier94.json';$catalog=Read94 'worker/curve-copy-sources94.json';$cleanup=Read94 'cleanup-verification-r1.json'
 Require94 ($wrapper.exit_code -eq 0 -and $wrapper.worker_exit_code -eq 0 -and $wrapper.worker_exited -eq $true -and $wrapper.client_exit -eq 0 -and $wrapper.manifest_restored -eq $true -and $wrapper.original_dll_unchanged -eq $true -and $wrapper.readonly_inspection -eq $true -and $wrapper.native_copy_checks -eq 28 -and $wrapper.workflows -eq 18 -and $wrapper.refusals -eq 10 -and $wrapper.checkpoints -eq 74 -and $wrapper.archived_raw_checkpoints -eq 74 -and $wrapper.open_s1_gates -eq 18) 'Terminal wrapper matrix/restoration differs'
 $run=[string]$wrapper.run_id;Require94 ($run -cmatch '^[0-9a-f]{32}$' -and $ready.status -ceq 'ready' -and $ready.target_id -ceq ('inventor-2027-'+$ready.owned_pid)) 'Owned target differs'
 foreach($row in @($ready,$stopped,$client,$native,$catalog)){Require94 ($row.run_id -ceq $run -and $row.target_id -ceq $ready.target_id) 'Cross-report owner binding differs'}
 Require94 ($stopped.status -ceq 'stopped' -and $stopped.exit_code -eq 0 -and $stopped.owned_pid -eq $ready.owned_pid -and $stopped.owned_start_utc_ticks -eq $ready.owned_start_utc_ticks -and $stopped.owned_final_empty -eq $true -and $stopped.owned_pid_exited -eq $true -and $stopped.existing_processes_preserved -eq $true -and $stopped.http_stopped -eq $true) 'Normal cleanup incomplete'
 $beforePid=Map94 $ready.existing_processes_before 'pid';$afterPid=Map94 $stopped.existing_processes_after 'pid'
 Require94 (-not $beforePid.ContainsKey([string]$ready.owned_pid) -and (Equal94 $ready.existing_processes_before $stopped.existing_processes_before)) 'Owned process was preexisting'
 foreach($key in $beforePid.Keys){Require94 ($afterPid.ContainsKey($key) -and $afterPid[$key].start_utc_ticks -eq $beforePid[$key].start_utc_ticks) 'Preexisting process identity changed'}
 if($afterPid.ContainsKey([string]$ready.owned_pid)){Require94 ($afterPid[[string]$ready.owned_pid].start_utc_ticks -eq $ready.owned_start_utc_ticks) 'Residual inventory PID reused'}
 Closed94 $cleanup @('schema','run_id','owned_pid','owned_start_utc_ticks','owned_pid_absent','checked_utc','check');$checked=[DateTimeOffset]::MinValue
 Require94 ($cleanup.schema -eq 1 -and $cleanup.run_id -ceq $run -and $cleanup.owned_pid -eq $ready.owned_pid -and $cleanup.owned_start_utc_ticks -eq $ready.owned_start_utc_ticks -and $cleanup.owned_pid_absent -eq $true -and [DateTimeOffset]::TryParse([string]$cleanup.checked_utc,[ref]$checked) -and $checked.Offset -eq [TimeSpan]::Zero -and $checked.UtcDateTime -ge (Get-Item -LiteralPath (Join-Path $script:Root94 'wrapper-result.json')).LastWriteTimeUtc) 'Independent later OS absence evidence missing'
 foreach($row in @($ready.installation_ready,$stopped.installation_after)){Require94 ($row.unchanged -eq $true -and $row.stage_addin_sha256 -ceq $wrapper.stage_dll_sha256 -and $row.original_installed_dll_sha256 -ceq $wrapper.original_dll_sha256) 'Installed/stage DLL binding differs'}
 Require94 ($ready.loaded_addin.pid -eq $ready.owned_pid -and $ready.loaded_addin.sha256 -ceq $wrapper.stage_dll_sha256 -and $ready.loaded_addin.verification -ceq 'owned_process_module_path_plus_disk_sha256' -and $ready.loaded_addin.memory_bytes_hashed -eq $false) 'Loaded candidate evidence differs'
 Require94 ($client.status -ceq 'PASS_PC_CURVE_COPY_SUBSET' -and $client.exit_code -eq 0 -and $client.cases.Count -eq 18 -and $client.refusals.Count -eq 10 -and $client.checkpoint_count -eq 74 -and $native.status -ceq 'PASS_NATIVE_CURVE_COPY_SUBSET' -and $native.checks.Count -eq 28 -and $native.checkpoints -eq 74) 'Client/native terminal matrix incomplete'
 Closed94 $catalog @('schema','run_id','target_id','owned_pid','owned_start_utc_ticks','documents')
 Require94 ($catalog.schema -eq 1 -and $catalog.owned_pid -eq $ready.owned_pid -and $catalog.owned_start_utc_ticks -eq $ready.owned_start_utc_ticks -and (Equal94 $catalog $client.source_catalog)) 'Catalog exact attestation differs'
 $docs=Map94 $catalog.documents 'context';$ownedDocs=Map94 $ready.owned_documents 'document_id';Require94 ($docs.Count -eq 2 -and $ownedDocs.Count -eq 2) 'Two fixture documents required'
 $roles=@('line','text','parameter_text','circle','arc_quarter','arc_wrap','arc_major','arc_clockwise');$profiles=@('circle','arc_quarter','arc_wrap','arc_major','arc_clockwise','mixed_curves','mixed_all','line','text')
 foreach($context in @('Part','Sheet')){
  Require94 ($docs.ContainsKey($context)) 'Catalog context missing';$doc=$docs[$context];Closed94 $doc @('context','document_id','sketch_id','sources');Closed94 $doc.sources $roles
  Require94 ($doc.document_id -ceq $(if($context -ceq 'Part'){$ready.part_document_id}else{$ready.sheet_document_id}) -and $ownedDocs.ContainsKey([string]$doc.document_id)) 'Catalog document differs'
  $owned=$ownedDocs[[string]$doc.document_id];Require94 ($doc.document_id -ceq ('doc_'+$owned.internal_name) -and [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath([string]$owned.saved_path)) -ceq [IO.Path]::GetFullPath([string]$ready.fixture_directory)) 'Owned original path differs'
  [void](Opaque94 $doc.sketch_id $doc.document_id 'sketch');$ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  foreach($role in $roles){[void](Opaque94 $doc.sources[$role] $doc.document_id $(if($role -cin @('text','parameter_text')){'sketch_text'}else{'sketch_entity'}));Require94 ($ids.Add([string]$doc.sources[$role])) 'Catalog source aliases another role'}
 }
 $states=@{};$traces=@{};$nonceSet=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$ordinal=0
 Require94 ($client.checkpoint_trace94.Count -eq 74 -and @(Get-ChildItem -LiteralPath (Join-Path $script:Root94 'worker') -File -Filter 'copy-native-checkpoint94-*.json').Count -eq 74) 'Checkpoint raw/trace inventory differs'
 foreach($trace in $client.checkpoint_trace94){
  $ordinal++;$file='copy-native-checkpoint94-'+$ordinal.ToString('D3')+'.json';$raw=Read94 ('worker/'+$file);Closed94 $trace.request @('run_id','nonce');Closed94 $trace.ack @('run_id','nonce','native_state_sha256','file')
  Require94 ($trace.ordinal -eq $ordinal -and $trace.request.run_id -ceq $run -and $trace.ack.run_id -ceq $run -and $trace.request.nonce -cmatch '^[0-9a-f]{32}$' -and $trace.ack.nonce -ceq $trace.request.nonce -and $trace.ack.file -ceq $file -and $nonceSet.Add([string]$trace.request.nonce) -and $raw.run_id -ceq $run -and $raw.nonce -ceq $trace.request.nonce -and $raw.native_state_sha256 -ceq $trace.ack.native_state_sha256 -and $raw.native_state.run_id -ceq $run -and $raw.native_state.owned_pid -eq $ready.owned_pid -and $raw.native_state.readonly_native_revision_dirty_preserved -eq $true) 'Checkpoint order/binding differs'
  Require94 ((NativeHash94 $raw.native_state) -ceq $raw.native_state_sha256) 'Checkpoint raw compact SHA differs'
  foreach($actual in $raw.native_state.documents){Require94 ($ownedDocs.ContainsKey([string]$actual.document_id) -and $actual.internal_name -ceq $ownedDocs[[string]$actual.document_id].internal_name -and $actual.file_sha256 -ceq $ownedDocs[[string]$actual.document_id].baseline_file_sha256 -and $actual.baseline_file_unchanged -eq $true) 'Checkpoint original disk/owner binding differs'}
  $states[$ordinal]=$raw.native_state;$traces[$ordinal]=$trace;$script:Files94.Add('worker/'+$file)
 }
 $used=[Collections.Generic.HashSet[int]]::new();$allCopies=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$nativeChecks=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal);foreach($check in $native.checks){Require94 ($nativeChecks.TryAdd($check.document_id+':'+$check.copy_key,$check)) 'Duplicate document-scoped native pair'};$pairCount=0;$expectedOrdinal=0;$unknowns=[Collections.Generic.List[string]]::new()
 function UseCP94([int]$Index,[string]$Context,[string]$Workflow,[string]$Stage){Require94 ($Index -eq $script:Sequence94+1 -and $used.Add($Index) -and $traces.ContainsKey($Index) -and $traces[$Index].context -ceq $Context -and $traces[$Index].workflow -ceq $Workflow -and $traces[$Index].stage -ceq $Stage) 'Checkpoint workflow/stage sequence differs';$script:Sequence94=$Index}
 $script:Sequence94=0
 foreach($context in @('Part','Sheet')){
  $doc=$docs[$context];$docId=[string]$doc.document_id;$sketchKey=Opaque94 $doc.sketch_id $docId 'sketch';$work=@($client.cases|Where-Object {$_.context -ceq $context});Require94 ($work.Count -eq 9 -and (Equal94 @($work.workflow) $profiles)) 'Nine ordered workflows per context required'
  $priorCore=$null
  foreach($case in $work){
   $label=[string]$case.workflow;$cp=$case.checkpoints;UseCP94 $cp.preview_before $context $label 'preview_before';UseCP94 $cp.preview_cancel_after $context $label 'preview_cancel_after';UseCP94 $cp.commit_first_core_after $context $label 'commit_first_core_after'
   $a=$states[[int]$cp.preview_before];$restored=$states[[int]$cp.preview_cancel_after];$b=$states[[int]$cp.commit_first_core_after]
   Require94 ((Equal94 $a $restored) -and $case.document_id -ceq $docId -and $case.status -ceq 'PASS_PC_CURVE_COPY_WORKFLOW' -and $case.first_snapshot_immediate_after_last_commit -eq $true -and $case.first_snapshot_attempts -eq 1 -and $case.synthetic_pc_confirmation -eq $true -and $case.represented_preview_rollback_exact -eq $true -and $case.full_native_preview_checkpoint_exact -eq $true -and $case.cancel_discarded_plan -eq $true -and $case.fresh_valid_references -eq $true -and $case.source_represented_exact -eq $true -and $case.prior_copies_represented_exact -eq $true -and (Equal94 $case.delta_mm @(12,-8))) 'Workflow first-read/preview/cancel/confirmation evidence differs'
   Require94 ($case.before_state.document_id -ceq $docId -and $case.after_state.document_id -ceq $docId -and $case.before_state.revision -cne $case.after_state.revision) 'Commit revision/document differs'
   if($null -ne $priorCore){Require94 (Equal94 $priorCore $case.before_snapshot_raw) 'Core workflow chain differs'}
   PriorCore94 $case.before_snapshot_raw $case.first_snapshot_raw;$priorCore=$case.first_snapshot_raw
   $oldGraph=Map94 (Sketch94 $a $docId $sketchKey).entities;$nowGraph=Map94 (Sketch94 $b $docId $sketchKey).entities;$oldTexts=Texts94 $a $docId $sketchKey;$nowTexts=Texts94 $b $docId $sketchKey
   foreach($key in $oldTexts.Keys){Require94 ($nowTexts.ContainsKey($key) -and (Equal94 $oldTexts[$key] $nowTexts[$key])) 'Native prior text/glyph changed'}
   $coreOld=Map94 $case.before_snapshot_raw.entities 'id';$coreNow=Map94 $case.first_snapshot_raw.entities 'id';$expectedRoles=if($label -ceq 'mixed_curves'){@('circle','arc_quarter','line')}elseif($label -ceq 'mixed_all'){@('circle','arc_quarter','line','text')}else{@($label)}
   $seenRoles=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$curvePairs=@();$extraPoints=@();$pointSet=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
   Require94 ($case.pairs.Count -eq $expectedRoles.Count) 'Workflow pair count differs'
   foreach($pair in $case.pairs){
    $pairCount++;$type=[string]$pair.type;$kind=if($type -ceq 'text'){'sketch_text'}else{'sketch_entity'};$sourceId=[string]$pair.source.entity_id;$copyId=[string]$pair.copy.entity_id
    $sourceKey=Opaque94 $sourceId $docId $kind;$copyKey=Opaque94 $copyId $docId $kind;$role=@($expectedRoles|Where-Object {$doc.sources[$_] -ceq $sourceId})
    Require94 ($role.Count -eq 1 -and $seenRoles.Add([string]$role[0]) -and $sourceKey -cne $copyKey -and $allCopies.Add($docId+':'+$copyKey) -and $coreOld.ContainsKey($sourceId) -and $coreNow.ContainsKey($copyId) -and -not $coreOld.ContainsKey($copyId)) 'Pair source catalog/copy identity differs'
    foreach($ref in @($pair.source,$pair.copy)){Closed94 $ref @('document_id','revision','sketch_id','entity_id','type','subpart');Require94 ($ref.document_id -ceq $docId -and $ref.sketch_id -ceq $doc.sketch_id -and $ref.type -ceq $type -and $ref.subpart -ceq 'Entity') 'Typed pair owner differs'}
    Require94 ($pair.source.revision -ceq $case.before_state.revision -and $pair.copy.revision -ceq $case.after_state.revision -and (Equal94 $pair.source_before $coreOld[$sourceId]) -and (Equal94 $pair.copy_first_snapshot $coreNow[$copyId])) 'Pair first snapshot/reference revision differs'
    CorePair94 $coreOld[$sourceId] $coreNow[$copyId] $type
    Require94 ($nativeChecks.ContainsKey($docId+':'+$copyKey) -and $nativeChecks[$docId+':'+$copyKey].document_id -ceq $docId -and $nativeChecks[$docId+':'+$copyKey].source_key -ceq $sourceKey -and $nativeChecks[$docId+':'+$copyKey].workflow -ceq $label -and $nativeChecks[$docId+':'+$copyKey].type -ceq $type -and $nativeChecks[$docId+':'+$copyKey].native_translation_and_original_exact -eq $true -and $nativeChecks[$docId+':'+$copyKey].independent_native_keys_points_glyphs -eq $true) 'Independent native pair attestation differs'
    if($type -ceq 'text'){
     Require94 ($oldTexts.ContainsKey($sourceKey) -and $nowTexts.ContainsKey($copyKey)) 'Native text pair absent';$extraPoints+=NativeTextPair94 $oldTexts[$sourceKey] $nowTexts[$copyKey] $oldGraph $nowGraph $pointSet
     Near94 $coreNow[$copyId].text_mm $nowTexts[$copyKey].copy_details88.origin_mm;Near94 $coreNow[$copyId].text_glyphs.vertices_mm $nowTexts[$copyKey].copy_details88.raw_vertices_model_mm
    }else{
     Require94 ($oldGraph.ContainsKey($sourceKey) -and $nowGraph.ContainsKey($copyKey)) 'Native curve pair absent';$source=$oldGraph[$sourceKey];$copy=$nowGraph[$copyKey];$pm=[ordered]@{}
     foreach($r in $source.structural_points.Keys){$sp=$source.structural_points[$r];$np=$copy.structural_points[$r];$pm[$r]=@{source_key=$sp.key;copy_key=$np.key};Require94 ($pointSet.Add([string]$np.key)) 'Copied curves share a new structural point';$expected=Clone94 $oldGraph[[string]$sp.key];$actual=Clone94 $nowGraph[[string]$np.key];foreach($owner in @($expected.owned_by)){Require94 ($owner -ceq $sourceKey) 'Source point has external owner'};$expected.owned_by=@($expected.owned_by|ForEach-Object {$copyKey});foreach($field in @('key','point_mm')){[void]$expected.Remove($field);[void]$actual.Remove($field)};Require94 (Equal94 $expected $actual) 'Mapped structural point flags/owner differ'}
     $curvePairs+=@{source_key=$sourceKey;copy_key=$copyKey;source=$source;copy=$copy;point_mapping=$pm}
     foreach($r in $copy.structural_points.Keys){$field=if($r -ceq 'center'){'center_mm'}elseif($r -ceq 'start'){'start_mm'}else{'end_mm'};Near94 $coreNow[$copyId][$field] $copy.structural_points[$r].point_mm};if($type -cne 'line'){Near94 @($coreNow[$copyId].radius_mm) @($copy.radius_mm) 1e-8}
    }
   }
   $oldC=Map94 (Sketch94 $a $docId $sketchKey).constraints;$newC=Map94 (Sketch94 $b $docId $sketchKey).constraints;$phase=@{pairs=$curvePairs;new_constraints=@($newC.Values|Where-Object {-not $oldC.ContainsKey([string]$_.key)});new_entity_keys=@($nowGraph.Keys|Where-Object {-not $oldGraph.ContainsKey($_)})}
   foreach($unknown in (Phase94 $a $b $phase $docId $sketchKey @($curvePairs | ForEach-Object {$_.source_key}) @(12,-8) $extraPoints)){$unknowns.Add($context+'/'+$label+'/'+$unknown)}
  }
  $kinds=@('stale_request','foreign_reference','parameter_text_unsupported','mixed_parameter_preflight','post_valid_copy_failure');$refusals=@($client.refusals|Where-Object {$_.context -ceq $context});Require94 ($refusals.Count -eq 5 -and (Equal94 @($refusals.kind) $kinds)) 'Ordered refusal matrix differs'
  foreach($refusal in $refusals){
   $kind=[string]$refusal.kind;UseCP94 $refusal.before_native_checkpoint $context $kind 'refusal_before';UseCP94 $refusal.after_native_checkpoint $context $kind 'refusal_after'
   Require94 ($refusal.document_id -ceq $docId -and (Equal94 $states[[int]$refusal.before_native_checkpoint] $states[[int]$refusal.after_native_checkpoint]) -and (Equal94 $refusal.before_snapshot_raw $refusal.after_snapshot_raw) -and (Equal94 $refusal.actual_state $refusal.after_state) -and $refusal.represented_rollback_exact -eq $true -and $refusal.event_revision_preserved -eq $true -and $refusal.full_native_checkpoint_exact -eq $true) 'Refusal native/Core Abort differs'
   if($kind -ceq 'stale_request'){Require94 ($refusal.code -ceq 'STALE_REVISION' -and $refusal.request_state.revision -cne $refusal.actual_state.revision) 'Stale refusal differs'}else{Require94 ($refusal.code -ceq 'ROLLED_BACK' -and $refusal.step_code -ceq 'INVALID_ARGUMENT' -and $refusal.step_index -eq $(if($kind -ceq 'post_valid_copy_failure'){1}else{0})) 'Refusal exact step code differs'}
   if($kind -ceq 'foreign_reference'){
    $original=$refusal.original_foreign_reference;$adversarial=$refusal.adversarial_foreign_reference;$other=$docs[$(if($context -ceq 'Part'){'Sheet'}else{'Part'})]
    Require94 ($refusal.adversarial_revision_override -eq $true -and $refusal.foreign_identity_fields_unchanged -eq $true -and $original.document_id -ceq $other.document_id -and $original.sketch_id -ceq $other.sketch_id -and $original.entity_id -ceq $other.sources.circle -and $adversarial.revision -ceq $refusal.actual_state.revision) 'Foreign adversarial identity/override differs'
    $aa=Clone94 $original;$bb=Clone94 $adversarial;[void]$aa.Remove('revision');[void]$bb.Remove('revision');Require94 (Equal94 $aa $bb) 'Foreign adversarial reference changed identity fields'
    Require94 (Equal94 $refusal.operations[0].arguments.options.references[0] $adversarial) 'Actual foreign operation differs from disclosed adversarial reference'
   }
  }
 }
 Require94 ($pairCount -eq 28 -and $used.Count -eq 74 -and $script:Sequence94 -eq 74) 'Pair/checkpoint consumption incomplete'
 $allFiles=@('wrapper-result.json','worker/ready.json','worker/stopped.json','worker/copy-client94.json','worker/copy-native-verifier94.json','worker/curve-copy-sources94.json','cleanup-verification-r1.json')+@($script:Files94)
 return [ordered]@{schema=1;status='VERIFIED_PC_CORE_NATIVE_COPY94_SUBSET';run_id=$run;candidate_sha256=$wrapper.stage_dll_sha256;workflows=18;pairs=28;refusals=10;checkpoints=74;post_wrapper_owned_absence=$true;full_s1_pass=$false;quest_proof=$false;physical_proof=$false;open_s1_gates=18;optional_style_unknowns=@($unknowns);files=@($allFiles|ForEach-Object {@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $script:Root94 $_)).Hash}});limits=@('Offline stored-state consistency, not independent native execution/authenticity','No tolerance for unchanged/rollback JSON serialized values; source-copy domain/angles1e-10/radius1e-8mm/XY1e-5mm','First-read ordering is archived client evidence; native cross-check recomputed from checkpoint after that first read','Optional UNKNOWN flags remain unverified','No source updates/reopen, other Copy profiles, UI/Quest/physical or full S1 closure')}
}
function SameKeys95($A,$B){Require94 ($A.Count -eq $B.Count) '95 identity inventory count changed';foreach($key in $A.Keys){Require94 ($B.ContainsKey([string]$key)) '95 identity missing'}}
function SamePath95([string]$A,[string]$B){Require94 ([IO.Path]::IsPathFullyQualified($A) -and [IO.Path]::IsPathFullyQualified($B)) '95 absolute owned paths required';return [string]::Equals([IO.Path]::GetFullPath($A),[IO.Path]::GetFullPath($B),[StringComparison]::OrdinalIgnoreCase)}
function Offset95($P,$Delta){Require94 ($P.Count -eq 2 -and $Delta.Count -eq 2) '95 XY shape differs';return ,@(([double]$P[0]+[double]$Delta[0]),([double]$P[1]+[double]$Delta[1]))}
function Without95($Row,[string[]]$Names){$copy=Clone94 $Row;foreach($name in $Names){[void]$copy.Remove($name)};return $copy}
function ParameterMarkup95([string]$Markup,$Meta){
 Require94 ($Markup.Length -gt 0 -and $Markup.Length -le 4096) '95 parameter XML bound differs'
 $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null;$settings.MaxCharactersInDocument=4111
 $input95=[IO.StringReader]::new('<owner>'+$Markup+'</owner>');$reader95=[Xml.XmlReader]::Create($input95,$settings)
 try{$xml95=[Xml.Linq.XElement]::Load($reader95,[Xml.Linq.LoadOptions]::PreserveWhitespace)}finally{$reader95.Dispose();$input95.Dispose()}
 $nodes=@($xml95.Nodes());Require94 ($nodes.Count -eq 2 -and $nodes[0].GetType() -eq [Xml.Linq.XText] -and $nodes[0].Value -ceq 'Bound=' -and $nodes[1] -is [Xml.Linq.XElement]) '95 parameter grammar differs'
 $field=$nodes[1];$attrs=@($field.Attributes())
 Require94 ($field.Name.ToString() -ceq 'Parameter' -and $attrs.Count -eq 4 -and @($attrs|Where-Object {$_.Name.ToString() -cnotin @('Resolved','ComponentIdentifier','Name','Precision')}).Count -eq 0 -and $field.Attribute('Resolved').Value -ceq 'True' -and $field.Attribute('Name').Value -ceq 'S1Copy88Length' -and $field.Attribute('Precision').Value -ceq '0' -and @($field.Nodes()).Count -eq 1 -and $field.FirstNode.GetType() -eq [Xml.Linq.XText] -and $field.Value -ceq '13') '95 parameter is not exact unmodified13mm field'
 Require94 (SamePath95 ([string]$field.Attribute('ComponentIdentifier').Value) ([string]$Meta.native_path)) '95 parameter owner is outside the attested native file'
 $field.SetAttributeValue('ComponentIdentifier','owned-local:'+$Meta.document_id+'#'+$Meta.parameter_key)
 $canonical=$xml95.ToString([Xml.Linq.SaveOptions]::DisableFormatting);return $canonical.Substring(7,$canonical.Length-15)
}
function ParameterCore95($Row,$Meta){Require94 ($Row.id -ceq $Meta.source_ids.parameter_text -and $Row.type -ceq 'text' -and $Row.text -ceq 'Bound=13') '95 unselected parameter identity/text differs';$clone=Clone94 $Row;$clone.formatted_text=ParameterMarkup95 $Row.formatted_text $Meta;return $clone}
function NativeCanonical95($State,$Meta){
 $clone=Clone94 $State;$doc=(Map94 $clone.documents 'document_id')[[string]$Meta.document_id];Require94 ($doc.internal_name -ceq $Meta.internal_name -and (SamePath95 $doc.path $Meta.native_path)) '95 native parameter document owner differs'
 $users=@($doc.parameters|Where-Object {$_.name -ceq 'S1Copy88Length'});Require94 ($users.Count -eq 1 -and $users[0].key -ceq $Meta.parameter_key -and $users[0].unit -ceq 'mm' -and $users[0].kind -ceq 'kUserParameter' -and [double]$users[0].native_value -eq 1.3) '95 native parameter key/units/value differs'
 $sketchKey=Opaque94 $Meta.sketch_id $Meta.document_id 'sketch';$target=(Map94 $doc.sketches)[$sketchKey];$textKey=Opaque94 $Meta.source_ids.parameter_text $Meta.document_id 'sketch_text';$count=0
 foreach($text in $target.texts){if(([string]$text.formatted_text).Contains('<Parameter',[StringComparison]::Ordinal)){Require94 ($text.key -ceq $textKey -and $text.text -ceq 'Bound=13') '95 unexpected native parameter field in copy sketch';$text.formatted_text=ParameterMarkup95 $text.formatted_text $Meta;$count++}}
 Require94 ($count -eq 1 -and $null -ne $target) '95 exactly one unselected parameter field required';return $clone
}
# Fixture-only attestation for the OTHER sketch; this does not authorize copying
# parameter text and is separate from the unselected13mm Copy88 field grammar.
function UntouchedDocument95($Document,$Binding){
 Closed94 $Binding @('document_id','sketch_key','text_key','parameter_key')
 $guid95=[Guid]::Empty;Require94 ($Binding.document_id -cmatch '^doc_' -and [Guid]::TryParse(([string]$Binding.document_id).Substring(4),[ref]$guid95) -and $Document.document_id -ceq $Binding.document_id -and ('doc_'+$Document.internal_name) -ceq $Binding.document_id -and [IO.Path]::IsPathFullyQualified([string]$Document.path)) '95 untouched native document identity/path differs'
 foreach($key in @('sketch_key','text_key','parameter_key')){Require94 (-not [string]::IsNullOrEmpty([string]$Binding[$key]) -and [Convert]::FromBase64String([string]$Binding[$key]).Length -gt 0) '95 untouched native key absent'}
 $parameters=@($Document.parameters|Where-Object {$_.name -ceq 'S1QuestLength73'})
 Require94 ($parameters.Count -eq 1 -and $parameters[0].key -ceq $Binding.parameter_key -and $parameters[0].expression -ceq '10 mm' -and $parameters[0].unit -ceq 'mm' -and $parameters[0].kind -ceq 'kUserParameter' -and [double]$parameters[0].native_value -eq 1) '95 untouched fixture parameter binding/type/value differs'
 $result=Clone94 $Document;$sketches=@($result.sketches|Where-Object {$_.key -ceq $Binding.sketch_key});Require94 ($sketches.Count -eq 1 -and $sketches[0].name -ceq 'S1_TextRead') '95 untouched fixture sketch binding differs'
 $texts=@($sketches[0].texts|Where-Object {$_.key -ceq $Binding.text_key});Require94 ($texts.Count -eq 1) '95 untouched fixture text identity missing/duplicated';$text=$texts[0];Require94 ($text.key -ceq $Binding.text_key -and $text.native_type -ceq 'kTextBoxObject' -and $text.text -ceq 'L=10') '95 untouched fixture text native identity/value differs'
 $markup=[string]$text.formatted_text;Require94 ($markup.Length -gt 0 -and $markup.Length -le 4096) '95 untouched fixture markup bound differs'
 $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null;$settings.MaxCharactersInDocument=4111
 $input95=[IO.StringReader]::new('<owner>'+$markup+'</owner>');$reader95=[Xml.XmlReader]::Create($input95,$settings)
 try{$xml95=[Xml.Linq.XElement]::Load($reader95,[Xml.Linq.LoadOptions]::PreserveWhitespace)}finally{$reader95.Dispose();$input95.Dispose()}
 $nodes=@($xml95.Nodes());Require94 ($nodes.Count -eq 2 -and $nodes[0].GetType() -eq [Xml.Linq.XText] -and $nodes[0].Value -ceq 'L=' -and $nodes[1] -is [Xml.Linq.XElement]) '95 untouched fixture XML grammar differs'
 $field=$nodes[1];$attrs=@($field.Attributes());Require94 ($field.Name.ToString() -ceq 'Parameter' -and $attrs.Count -eq 4 -and @($attrs|Where-Object {$_.Name.ToString() -cnotin @('Resolved','ComponentIdentifier','Name','Precision')}).Count -eq 0 -and $field.Attribute('Resolved').Value -ceq 'True' -and $field.Attribute('Name').Value -ceq 'S1QuestLength73' -and $field.Attribute('Precision').Value -ceq '0' -and @($field.Nodes()).Count -eq 1 -and $field.FirstNode.GetType() -eq [Xml.Linq.XText] -and $field.Value -ceq '10') '95 untouched fixture XML binding/value differs'
 Require94 (SamePath95 ([string]$field.Attribute('ComponentIdentifier').Value) ([string]$Document.path)) '95 untouched fixture field owner is outside exact native file'
 $field.SetAttributeValue('ComponentIdentifier','owned-local:'+$Binding.document_id+'#'+$Binding.parameter_key);$canonical=$xml95.ToString([Xml.Linq.SaveOptions]::DisableFormatting);$text.formatted_text=$canonical.Substring(7,$canonical.Length-15);return $result
}
function AttestUntouched95($Document){
 $sketches=@($Document.sketches|Where-Object {$_.name -ceq 'S1_TextRead'});Require94 ($sketches.Count -eq 1) '95 untouched prepare sketch ambiguous';$texts=@($sketches[0].texts|Where-Object {$_.text -ceq 'L=10'});Require94 ($texts.Count -eq 1) '95 untouched prepare L10 text ambiguous';$parameters=@($Document.parameters|Where-Object {$_.name -ceq 'S1QuestLength73'});Require94 ($parameters.Count -eq 1) '95 untouched prepare parameter ambiguous'
 $binding=[ordered]@{document_id=$Document.document_id;sketch_key=$sketches[0].key;text_key=$texts[0].key;parameter_key=$parameters[0].key};[void](UntouchedDocument95 $Document $binding);return $binding
}
function Raw95($Descriptor,[int]$Ordinal,[string]$Stage){
 Closed94 $Descriptor @('file','file_sha256','native_state_sha256');$file='copy-native-state95-'+$Ordinal.ToString('D3')+'.json'
 Require94 ($Descriptor.file -ceq $file -and $Descriptor.file_sha256 -cmatch '^[A-F0-9]{64}$' -and $Descriptor.native_state_sha256 -cmatch '^[A-F0-9]{64}$') '95 raw descriptor identity differs'
 $raw=Read94 ('worker/'+$file);Closed94 $raw @('schema','run_id','target_id','ordinal','stage','native_state_sha256','native_state')
 Require94 ((Get-FileHash -LiteralPath (Join-Path $script:Root94 ('worker/'+$file))).Hash -ceq $Descriptor.file_sha256 -and $raw.schema -eq 1 -and $raw.run_id -ceq $script:Run95 -and $raw.target_id -ceq $script:Target95 -and $raw.ordinal -eq $Ordinal -and $raw.stage -ceq $Stage -and $raw.native_state_sha256 -ceq $Descriptor.native_state_sha256 -and (NativeHash94 $raw.native_state) -ceq $raw.native_state_sha256) '95 sidecar file/compact SHA or stage differs'
 Require94 ($raw.native_state.run_id -ceq $script:Run95 -and $raw.native_state.owned_pid -eq $script:Ready95.owned_pid -and $raw.native_state.readonly_native_revision_dirty_preserved -eq $true -and $raw.native_state.documents.Count -eq 2) '95 native state process/owner binding differs'
 [void]$script:Files95.Add('worker/'+$file);return $raw.native_state
}
function BindingRules95($Snapshot,$Meta,$InitialNative){
 $entities=Map94 $Snapshot.entities 'id';$constraints=Map94 $Snapshot.constraints 'id';$byCurve=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal);$byPoint=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal);$bindings=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
 $graph=Sketch94 $InitialNative $Meta.document_id (Opaque94 $Meta.sketch_id $Meta.document_id 'sketch');$nativeRows=Map94 $graph.entities;$nativeConstraints=Map94 $graph.constraints
 foreach($role in $Meta.source_curves.Keys){$curve=$Meta.source_curves[$role];Require94 ($byCurve.TryAdd([string]$curve.entity_key,@{role=$role;curve=$curve})) '95 duplicate source curve key';foreach($part in $curve.point_keys.Keys){Require94 ($byPoint.TryAdd([string]$curve.point_keys[$part],@{role=$part;curve=$curve})) '95 source structural point collision';Require94 ($entities.ContainsKey([string]$curve.point_ids[$part]) -and (Equal94 $entities[[string]$curve.point_ids[$part]].point_mm $curve.point_mm[$part])) '95 frozen source point differs from initial Core'}}
 foreach($entity in $Snapshot.entities){
  $found=@($byCurve.Values|Where-Object {$_.curve.entity_id -ceq $entity.id})
  if($found.Count -eq 1){$curve=$found[0].curve;$key=[string]$curve.entity_key;$bindings[$key]=@([ordered]@{entity_id=$entity.id;type=$entity.type;subpart='Entity'});$parts=if($found[0].role -ceq 'circle'){@('center')}else{@('start','end','center')};foreach($part in $parts){$key=[string]$curve.point_keys[$part];if(-not $bindings.ContainsKey($key)){$bindings[$key]=@()};$bindings[$key]+=,[ordered]@{entity_id=$entity.id;type=$entity.type;subpart=([char]::ToUpperInvariant($part[0])+$part.Substring(1))}}}else{
   $found=@($byPoint.Values|Where-Object {$_.curve.point_ids[$_.role] -ceq $entity.id});if($found.Count -eq 1){$key=[string]$found[0].curve.point_keys[$found[0].role];if(-not $bindings.ContainsKey($key)){$bindings[$key]=@()};$bindings[$key]+=,[ordered]@{entity_id=$entity.id;type='point';subpart='Entity'};$bindings[$key]+=,[ordered]@{entity_id=$entity.id;type='point';subpart='Point'}}
  }
 }
 $rules=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
 foreach($curve in $Meta.source_curves.Values){
  $keys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);foreach($key in $curve.constraint_keys){Require94 ($keys.Add([string]$key)) '95 duplicate source intrinsic key'};Require94 ($curve.constraint_anchors.Count -eq $keys.Count) '95 intrinsic anchor cardinality differs'
  foreach($anchor in $curve.constraint_anchors){
   Closed94 $anchor @('constraint_key','native_type','members','anchor_mm');$key=[string]$anchor.constraint_key
   Require94 ($keys.Contains($key) -and $nativeConstraints.ContainsKey($key)) '95 native intrinsic anchor missing';$native=$nativeConstraints[$key]
   Require94 ($native.deletable -eq $false -and $native.binding_status -ceq 'COMPLETE_TYPED_MEMBERS' -and $native.native_type -ceq $anchor.native_type -and (Equal94 $native.members $anchor.members) -and $native.native_type -cin @('kCoincidentConstraintObject','kConcentricConstraintObject')) '95 actual native intrinsic graph/ordered members differs'
   $expected=@();foreach($member in $anchor.members){Closed94 $member @('role','key','type');Require94 ($nativeRows.ContainsKey([string]$member.key) -and $member.type -ceq $nativeRows[[string]$member.key].native_type -and $bindings.ContainsKey([string]$member.key)) '95 intrinsic typed member is not an attested source';foreach($link in $bindings[[string]$member.key]){if(@($expected|Where-Object {Equal94 $_ $link}).Count -eq 0){$expected+=,$link}}}
   $first=[string]$anchor.members[0].key
   if($byCurve.ContainsKey($first)){$owner=$byCurve[$first];$pointId=$owner.curve.point_ids[$(if($owner.role -ceq 'circle'){'center'}else{'start'})]}else{Require94 ($byPoint.ContainsKey($first)) '95 native first-member anchor unresolved';$owner=$byPoint[$first];$pointId=$owner.curve.point_ids[$owner.role]}
   $matching=@($constraints.Values|Where-Object {(Opaque94 $_.id $Meta.document_id 'sketch_constraint') -ceq $key});Require94 ($matching.Count -eq 1) '95 native/Core constraint identity ambiguous';$row=$matching[0]
   Require94 ($row.type -ceq 'constraint' -and $row.deletable -eq $false -and $row.binding_state -ceq 'complete' -and $row.native_type -ceq $anchor.native_type -and (Equal94 $row.related_entities $expected) -and (Equal94 $row.annotation_mm $entities[[string]$pointId].point_mm) -and (Equal94 $anchor.anchor_mm $entities[[string]$pointId].point_mm)) '95 initial actual first-member anchor/binding differs'
   Require94 ($rules.TryAdd([string]$row.id,[ordered]@{constraint_id=$row.id;constraint_key=$key;native_type=$anchor.native_type;anchor_point_id=$pointId;related_entities=$expected})) '95 shared source intrinsic identity'
  }
 }
 $sourceIds=@($Meta.source_curves.Values|ForEach-Object {$_.entity_id;foreach($part in $_.point_ids.Keys){$_.point_ids[$part]}})
 foreach($constraint in $Snapshot.constraints){if(@($constraint.related_entities|Where-Object {$_.entity_id -cin $sourceIds}).Count -gt 0){Require94 ($rules.ContainsKey([string]$constraint.id)) '95 unsupported external source relation'}}
 return ,$rules
}
function CoreIndependent95($Before,$After,$OldMeta,$Meta,$Rules,[bool]$Reopen){
 Require94 ($OldMeta.document_id -ceq $Meta.document_id -and $OldMeta.parameter_key -ceq $Meta.parameter_key) '95 relocation changed owner/parameter key'
 $oldEntities=Map94 $Before.entities 'id';$newEntities=Map94 $After.entities 'id';$mutable=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
 foreach($role in $Meta.source_curves.Keys){$curve=$Meta.source_curves[$role];$mutable[[string]$curve.entity_id]=$role;foreach($id in $curve.point_ids.Values){$mutable[[string]$id]='point'}}
 foreach($collection in @('entities','dimensions','constraints')){
  $old=Map94 $Before[$collection] 'id';$now=Map94 $After[$collection] 'id';SameKeys95 $old $now
  foreach($id in $old.Keys){
   if($collection -ceq 'entities' -and $id -ceq $Meta.source_ids.parameter_text){Require94 (Equal94 (ParameterCore95 $old[$id] $OldMeta) (ParameterCore95 $now[$id] $Meta)) '95 parameter changed beyond strict owner relocation';continue}
   $a=Clone94 $old[$id];$b=Clone94 $now[$id]
   if(-not $Reopen -and $collection -ceq 'entities' -and $mutable.ContainsKey($id)){$fields=if($mutable[$id] -ceq 'point'){@('point_mm')}elseif($mutable[$id] -ceq 'circle'){@('center_mm','radius_mm','strokes_mm')}else{@('center_mm','radius_mm','start_mm','end_mm','strokes_mm')};foreach($field in $fields){[void]$a.Remove($field);[void]$b.Remove($field)}}
   if(-not $Reopen -and $collection -ceq 'constraints' -and $Rules.ContainsKey($id)){$rule=$Rules[$id];Require94 ((Equal94 $a.annotation_mm $oldEntities[[string]$rule.anchor_point_id].point_mm) -and (Equal94 $b.annotation_mm $newEntities[[string]$rule.anchor_point_id].point_mm) -and (Equal94 $a.related_entities $rule.related_entities) -and (Equal94 $b.related_entities $rule.related_entities)) '95 source annotation is not the exact typed first-member anchor';[void]$a.Remove('annotation_mm');[void]$b.Remove('annotation_mm')}
   Require94 (Equal94 $a $b) '95 copy/unrelated Data or undeclared source property changed'
  }
 }
 foreach($field in @('complete','partial','constraint_status')){Require94 (Equal94 $Before[$field] $After[$field]) '95 solver/completeness changed'}
 Require94 ($After.complete -eq $true -and $After.partial -eq $false) '95 incomplete first Core snapshot'
}
function CoreSource95($Snapshot,$Initial,$Meta,[int]$Pass){
 $delta=if($Pass -eq 1){@(5,7)}else{@(-6,4)};$rows=Map94 $Snapshot.entities 'id';$initialRows=Map94 $Initial.entities 'id'
 foreach($role in $Meta.source_curves.Keys){$curve=$Meta.source_curves[$role];$row=$rows[[string]$curve.entity_id];$radius=if($role -ceq 'circle'){if($Pass -eq 1){12}else{6}}else{9};Require94 ($row.type -ceq $(if($role -ceq 'circle'){'circle'}else{'arc'})) '95 source Core type differs';Near94 $row.center_mm (Offset95 $curve.initial_center_mm $delta);Near94 @($row.radius_mm) @($radius) 1e-8
  foreach($part in $curve.point_ids.Keys){$expected=Offset95 $curve.point_mm[$part] $delta;Near94 $rows[[string]$curve.point_ids[$part]].point_mm $expected;if($part -cin @('start','end')){Near94 $row[($part+'_mm')] $expected}}
  Require94 ($row.strokes_mm.Count -ge 4 -and $row.strokes_mm.Count%2 -eq 0) '95 Core stroke shape invalid'
  for($i=0;$i -lt $row.strokes_mm.Count;$i+=2){$x=[double]$row.strokes_mm[$i]-[double]$row.center_mm[0];$y=[double]$row.strokes_mm[$i+1]-[double]$row.center_mm[1];Near94 @([Math]::Sqrt($x*$x+$y*$y)) @($radius)}
  if($role -ceq 'circle'){Near94 @($row.strokes_mm[0],$row.strokes_mm[1]) @($row.strokes_mm[-2],$row.strokes_mm[-1])}else{Translate94 $initialRows[[string]$curve.entity_id].strokes_mm $row.strokes_mm $delta}
 }
}
function NativeCompare95($Initial,$Actual,$OldMeta,$Meta,[int]$Pass,[bool]$Reopen,$OtherBefore=$Initial){
 $a=NativeCanonical95 $Initial $OldMeta;$b=NativeCanonical95 $Actual $Meta;$docId=[string]$Meta.document_id;$sketchKey=Opaque94 $Meta.sketch_id $docId 'sketch'
 # Source geometry is compared with its prepare baseline; unrelated documents
 # are compared with the immediately preceding state (Part may already be
 # reopened while Sheet is edited). Keys remain scoped to their document.
 foreach($collection in @('documents','curve_point_graph92','local_text_styles88')){
  $other=Map94 $OtherBefore[$collection] 'document_id'
  for($j=0;$j -lt $a[$collection].Count;$j++){if($a[$collection][$j].document_id -cne $docId){$a[$collection][$j]=Clone94 $other[[string]$a[$collection][$j].document_id]}}
 }
 if($Reopen){
  Require94 ($script:UntouchedBindings95.ContainsKey($docId)) '95 untouched fixture prepare attestation missing'
  foreach($state in @($a,$b)){for($j=0;$j -lt $state.documents.Count;$j++){if($state.documents[$j].document_id -ceq $docId){$state.documents[$j]=UntouchedDocument95 $state.documents[$j] $script:UntouchedBindings95[$docId]}}}
 }
 $graphA=Sketch94 $a $docId $sketchKey;$graphB=Sketch94 $b $docId $sketchKey;$old=Map94 $graphA.entities;$now=Map94 $graphB.entities;SameKeys95 $old $now
 if(-not $Reopen){
  $delta=if($Pass -eq 1){@(5,7)}else{@(-6,4)};$mutable=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  foreach($role in $Meta.source_curves.Keys){
   $curve=$Meta.source_curves[$role];$key=[string]$curve.entity_key;Require94 ($old.ContainsKey($key) -and $now.ContainsKey($key)) '95 native source curve missing';$source=$old[$key];$actual=$now[$key];[void]$mutable.Add($key);Analytic94 $source;Analytic94 $actual
   $radius=if($role -ceq 'circle'){if($Pass -eq 1){12}else{6}}else{9};Near94 @($actual.radius_mm) @($radius) 1e-8;Near94 $actual.domain $source.domain 1e-10
   foreach($part in $curve.point_keys.Keys){$pointKey=[string]$curve.point_keys[$part];Require94 ($actual.structural_points[$part].key -ceq $pointKey -and $source.structural_points[$part].key -ceq $pointKey -and $now.ContainsKey($pointKey)) '95 native structural key changed';[void]$mutable.Add($pointKey);$expected=Offset95 $curve.point_mm[$part] $delta;Near94 $now[$pointKey].point_mm $expected;Near94 $actual.structural_points[$part].point_mm $expected;Require94 (Equal94 (Without95 $old[$pointKey] @('point_mm')) (Without95 $now[$pointKey] @('point_mm'))) '95 source point flags/owner changed'}
   if($role -ceq 'circle'){
    Require94 ($actual.samples_mm.Count -eq $source.samples_mm.Count) '95 circle evaluator sample count differs'
    for($i=0;$i -lt $source.samples_mm.Count;$i++){$axis=$i%2;$expected=([double]$source.samples_mm[$i]-[double]$curve.initial_center_mm[$axis])*$radius/[double]$source.radius_mm+[double]$curve.initial_center_mm[$axis]+[double]$delta[$axis];Near94 @($actual.samples_mm[$i]) @($expected)}
   }else{
    foreach($field in @('start_angle_rad','sweep_angle_rad','geometry_start_angle_rad','geometry_sweep_angle_rad')){Near94 @($actual[$field]) @($source[$field]) 1e-10};Translate94 $source.samples_mm $actual.samples_mm $delta;Translate94 $source.strokes_mm $actual.strokes_mm $delta
   }
   $fields=@('structural_points','radius_mm','domain','samples_mm','strokes_mm','start_angle_rad','sweep_angle_rad','geometry_start_angle_rad','geometry_sweep_angle_rad')
   Require94 (Equal94 (Without95 $source $fields) (Without95 $actual $fields)) '95 source native undeclared property/style changed'
  }
  # Restore only validated source geometry in comparison copies. All other
  # graph fields (keys, intrinsic graph, copied points, prior objects) stay exact.
  foreach($key in $old.Keys){if($mutable.Contains($key)){$index=[array]::IndexOf(@($graphB.entities.key),$key);$graphB.entities[$index]=Clone94 $old[$key]}else{Require94 (Equal94 $old[$key] $now[$key]) '95 native copy/unrelated graph entity changed'}}
  $da=(Map94 $a.documents 'document_id')[$docId];$db=(Map94 $b.documents 'document_id')[$docId];$sa=(Map94 $da.sketches)[$sketchKey];$sb=(Map94 $db.sketches)[$sketchKey];$simpleA=Map94 $sa.entities;$simpleB=Map94 $sb.entities;SameKeys95 $simpleA $simpleB
  foreach($key in $simpleA.Keys){if($mutable.Contains($key)){
    $row=$simpleB[$key];$graph=$now[$key]
    if($graph.native_type -ceq 'kSketchPointObject'){Near94 $row.point_mm $graph.point_mm}else{foreach($part in $graph.structural_points.Keys){$field=$part+'_mm';if($row.Contains($field)){Near94 $row[$field] $graph.structural_points[$part].point_mm}};if($row.Contains('radius_mm')){Near94 @($row.radius_mm) @($graph.radius_mm) 1e-8}}
    $fields=@('point_mm','center_mm','start_mm','end_mm','radius_mm');Require94 (Equal94 (Without95 $simpleA[$key] $fields) (Without95 $row $fields)) '95 native simple source unknown property changed';$index=[array]::IndexOf(@($sb.entities.key),$key);$sb.entities[$index]=Clone94 $simpleA[$key]
   }}
 }
 foreach($state in @($a,$b)){
  # Closing and appending a reopened document can change only the outer
  # document collection order. Compare those collections by explicit doc ID;
  # all per-document native entity/point/constraint/style arrays remain exact.
  foreach($collection in @('documents','curve_point_graph92','local_text_styles88')){[void](Map94 $state[$collection] 'document_id');$state[$collection]=@($state[$collection]|Sort-Object document_id -CaseSensitive)}
  [void]$state.Remove('active_document_id');$doc=(Map94 $state.documents 'document_id')[$docId];foreach($field in @('database_revision_id','dirty')){[void]$doc.Remove($field)}
  if($Reopen){foreach($field in @('path','name','file_sha256','baseline_file_unchanged')){[void]$doc.Remove($field)}}
 }
 Require94 (Equal94 $a $b) $(if($Reopen){'95 reopened native objects/styles/params/graphs differ beyond strict owner relocation'}else{'95 native source/prior-copy/unrelated/constraint state differs'})
}
function CoreNative95($Snapshot,$State,$Meta){
 $docId=[string]$Meta.document_id;$sketchKey=Opaque94 $Meta.sketch_id $docId 'sketch';$graph=Map94 (Sketch94 $State $docId $sketchKey).entities;$texts=Texts94 $State $docId $sketchKey
 Require94 ($Snapshot.entities.Count -eq $graph.Count+$texts.Count) '95 native/Core represented entity inventory differs'
 foreach($row in $Snapshot.entities){$key=Opaque94 $row.id $docId $(if($row.type -ceq 'text'){'sketch_text'}else{'sketch_entity'})
  if($row.type -ceq 'text'){
   Require94 ($texts.ContainsKey($key)) '95 Core text missing natively';$native=$texts[$key];Require94 ($row.text -ceq $native.text -and $row.formatted_text -ceq $native.formatted_text) '95 Core/native text or binding differs';Near94 @($row.rotation_deg) @([double]$native.rotation_rad*180/[Math]::PI) 1e-10
   Near94 $row.text_mm $native.bounds_sketch_mm[0];$bounds=@($native.bounds_sketch_mm|ForEach-Object {$_[0];$_[1]});Near94 $row.text_bounds_mm $bounds;Near94 $row.text_glyphs.vertices_mm $native.copy_details88.raw_vertices_model_mm
   Require94 (Equal94 $row.text_glyphs.indices @($native.copy_details88.raw_indices_native_one_based|ForEach-Object {[long]$_-1})) '95 glyph topology differs';Near94 $row.text_glyphs.color_rgb @($native.copy_details88.color.red,$native.copy_details88.color.green,$native.copy_details88.color.blue) 0
  }else{
   Require94 ($graph.ContainsKey($key)) '95 Core curve/point missing natively';$native=$graph[$key]
   if($row.type -ceq 'point'){Require94 ($native.native_type -ceq 'kSketchPointObject') '95 native point type differs';Near94 $row.point_mm $native.point_mm}else{
    Require94 ($row.type -ceq $native.kind) '95 Core/native curve type differs';foreach($part in $native.structural_points.Keys){Near94 $row[($part+'_mm')] $native.structural_points[$part].point_mm};if($row.type -cne 'line'){Near94 @($row.radius_mm) @($native.radius_mm) 1e-8}
   }
  }
 }
}
function ValidateMeta95($Meta,$Catalog,$InitialNative){
 Closed94 $Meta @('context','document_id','internal_name','native_path','display_name','subtype','sketch_id','parameter_key','native_database_revision_id','source_ids','source_curves')
 Closed94 $Meta.source_ids @('line','text','parameter_text','circle','arc_quarter','arc_wrap','arc_major','arc_clockwise');Closed94 $Meta.source_curves @('circle','arc_quarter','arc_wrap','arc_major','arc_clockwise')
 Require94 ($Meta.context -cin @('Part','Sheet') -and $Meta.subtype -ceq $Meta.context -and $Meta.document_id -ceq $Catalog.document_id -and $Meta.sketch_id -ceq $Catalog.sketch_id -and (Equal94 $Meta.source_ids $Catalog.sources) -and $Meta.document_id -ceq ('doc_'+$Meta.internal_name) -and [Convert]::FromBase64String([string]$Meta.parameter_key).Length -gt 0 -and (SamePath95 ([IO.Path]::GetDirectoryName([string]$Meta.native_path)) $script:Ready95.fixture_directory) -and [IO.Path]::GetFileName([string]@($script:Ready95.owned_documents|Where-Object {$_.document_id -ceq $Meta.document_id})[0].saved_path) -ceq $Meta.display_name) '95 ACK document/catalog/owner differs'
 $sketchKey=Opaque94 $Meta.sketch_id $Meta.document_id 'sketch';$graph=Sketch94 $InitialNative $Meta.document_id $sketchKey;$rows=Map94 $graph.entities;$used=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 foreach($role in $Meta.source_curves.Keys){
  $curve=$Meta.source_curves[$role];Closed94 $curve @('entity_id','entity_key','point_ids','point_keys','point_mm','constraint_keys','constraint_anchors','initial_center_mm','radius_mm','angles','domain');$parts=if($role -ceq 'circle'){@('center')}else{@('center','start','end')};Closed94 $curve.point_ids $parts;Closed94 $curve.point_keys $parts;Closed94 $curve.point_mm $parts
  Require94 ($curve.entity_id -ceq $Meta.source_ids[$role] -and (Opaque94 $curve.entity_id $Meta.document_id 'sketch_entity') -ceq $curve.entity_key -and $rows.ContainsKey([string]$curve.entity_key) -and $used.Add([string]$curve.entity_key)) '95 frozen source curve identity differs';$native=$rows[[string]$curve.entity_key];Require94 ($native.kind -ceq $(if($role -ceq 'circle'){'circle'}else{'arc'})) '95 frozen source native type differs';Analytic94 $native
  Near94 $curve.initial_center_mm $native.structural_points.center.point_mm 0;Near94 @($curve.radius_mm) @($native.radius_mm) 0;Near94 @($curve.radius_mm) @(9) 1e-8;Require94 (Equal94 $curve.domain $native.domain) '95 frozen evaluator domain differs'
  $angles=if($role -ceq 'circle'){@()}else{@('start_angle_rad','sweep_angle_rad','geometry_start_angle_rad','geometry_sweep_angle_rad')};Closed94 $curve.angles $angles;foreach($field in $angles){Require94 (Equal94 $curve.angles[$field] $native[$field]) '95 frozen arc angle differs'}
  foreach($part in $parts){$key=[string]$curve.point_keys[$part];Require94 ((Opaque94 $curve.point_ids[$part] $Meta.document_id 'sketch_entity') -ceq $key -and $rows.ContainsKey($key) -and $used.Add($key) -and $rows[$key].native_type -ceq 'kSketchPointObject' -and $native.structural_points[$part].key -ceq $key -and (Equal94 $rows[$key].point_mm $curve.point_mm[$part]) -and (Equal94 $native.structural_points[$part].point_mm $curve.point_mm[$part])) '95 native structural point owner/key/coordinate differs'}
 }
}
function VerifyLifecycle95([string]$Root,$Baseline){
 $script:Root94=$Root;$script:Files95=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 $wrapper=Read94 'wrapper-result.json';$script:Ready95=Read94 'worker/ready.json';$ready=$script:Ready95;$script:Run95=[string]$ready.run_id;$script:Target95=[string]$ready.target_id;$client=Read94 'worker/copy-client95.json';$native=Read94 'worker/copy-native-lifecycle95.json';$catalog=Read94 'worker/curve-copy-sources94.json';$frozen=Read94 'worker/copy-client94.json'
 $run=$script:Run95;$target=$script:Target95
 Require94 ($Baseline.run_id -ceq $run -and $client.run_id -ceq $run -and $native.run_id -ceq $run -and $client.target_id -ceq $target -and $native.target_id -ceq $target -and $client.owned_pid -eq $ready.owned_pid -and $client.owned_start_utc_ticks -eq $ready.owned_start_utc_ticks) '95 run/target/process binding differs'
 Require94 ($wrapper.native_core_lifecycle_subset -eq $true -and $wrapper.core_first_reads -eq 6 -and $wrapper.source_updates -eq 4 -and $wrapper.reopens -eq 2 -and $wrapper.protocol_phases -eq 14 -and $wrapper.owned_acknowledgements -eq 14 -and $wrapper.archived_raw_states95 -eq 12 -and $wrapper.archived_acknowledgements95 -eq 14 -and $wrapper.lifecycle_terminal_status -ceq 'PASS_NATIVE_CURVE_COPY_LIFECYCLE_SUBSET') '95 terminal wrapper matrix incomplete'
 Require94 ($client.status -ceq 'PASS_PC_CURVE_COPY_LIFECYCLE_SUBSET' -and $client.exit_code -eq 0 -and $client.source_updates -eq 4 -and $client.reopens -eq 2 -and $client.first_core_reads -eq 6 -and -not $client.Contains('pending_first_read95') -and $native.status -ceq 'PASS_NATIVE_CURVE_COPY_LIFECYCLE_SUBSET' -and $native.source_updates -eq 4 -and $native.reopens -eq 2) '95 terminal client/native lifecycle incomplete'
 foreach($field in @('cases','refusals','checkpoint_trace94','source_catalog','initializations')){Require94 (Equal94 $client[$field] $frozen[$field]) '95 completed baseline94 was rewritten'}
 foreach($name in @('prerequisite94-ready.json','prerequisite94-verified.json')){$value=Read94 ('worker/'+$name);Closed94 $value @('run_id');Require94 ($value.run_id -ceq $run) '95 prerequisite release owner differs';[void]$script:Files95.Add('worker/'+$name)}
 Require94 ($client.baselines95.Count -eq 2 -and $client.lifecycle.Count -eq 6 -and $client.protocol95.Count -eq 14 -and $native.documents.Count -eq 2 -and $native.protocol.Count -eq 14 -and @(Get-ChildItem -LiteralPath (Join-Path $Root 'worker') -File -Filter 'copy-native-state95-*.json').Count -eq 12 -and @(Get-ChildItem -LiteralPath (Join-Path $Root 'worker') -File -Filter 'copy-core-ack95-*.json').Count -eq 14) '95 complete raw/FSM/context/read inventory required'
 $nativeDocs=Map94 $native.documents 'context';$baselines=Map94 $client.baselines95 'context';$catalogDocs=Map94 $catalog.documents 'context';Require94 ($nativeDocs.ContainsKey('Part') -and $nativeDocs.ContainsKey('Sheet') -and $baselines.ContainsKey('Part') -and $baselines.ContainsKey('Sheet')) '95 contexts differ'
 Require94 (Equal94 $nativeDocs.Part.initial $nativeDocs.Sheet.initial) '95 shared prepare descriptor differs';$initialNative=Raw95 $nativeDocs.Part.initial 1 'prepare'
 $script:UntouchedBindings95=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
 foreach($kind in @('Part','Sheet')){
  $doc=(Map94 $initialNative.documents 'document_id')[[string]$catalogDocs[$kind].document_id];$binding=AttestUntouched95 $doc;$reported=$nativeDocs[$kind].untouched_parameter_binding95;Closed94 $reported @('document_id','sketch_key','text_key','parameter_key');Require94 (Equal94 $binding $reported) '95 untouched prepare attestation differs from actual native identity';Require94 ($script:UntouchedBindings95.TryAdd([string]$binding.document_id,$binding)) '95 untouched prepare document collision'
 }
 $last94=Read94 ('worker/'+$frozen.checkpoint_trace94[73].ack.file)
 Require94 (Equal94 (Without95 $last94.native_state @('active_document_id')) (Without95 $initialNative @('active_document_id'))) '95 initial native state differs from completed74-checkpoint baseline'
 $steps=@('prepare|all|0','edit|Part|1','inspect|Part|1','edit|Part|2','inspect|Part|2','reopen|Part|0','inspect_reopen|Part|0','edit|Sheet|1','inspect|Sheet|1','edit|Sheet|2','inspect|Sheet|2','reopen|Sheet|0','inspect_reopen|Sheet|0','complete|all|0')
 $nonces=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$acks=@();$previous=$null
 for($i=0;$i -lt 14;$i++){
  $row=$client.protocol95[$i];$request=$row.request;$ack=$row.ack;$trace=$native.protocol[$i];Closed94 $request @('schema','run_id','nonce','ordinal','action','context','pass');Closed94 $ack @('schema','run_id','target_id','owned_pid','owned_start_utc_ticks','nonce','ordinal','action','context','pass','status','expires_utc','documents');Closed94 $trace @('nonce','ordinal','action','context','pass','state','ack');Closed94 $trace.ack @('file','file_sha256')
  Require94 ($request.schema -eq 1 -and $request.run_id -ceq $run -and $request.ordinal -eq $i+1 -and "$($request.action)|$($request.context)|$($request.pass)" -ceq $steps[$i] -and $request.nonce -cmatch '^[a-f0-9]{32}$' -and $nonces.Add([string]$request.nonce)) '95 request nonce/order/action differs'
  foreach($field in @('schema','run_id','nonce','ordinal','action','context','pass')){Require94 (Equal94 $request[$field] $ack[$field]) '95 ACK request binding differs'}
  foreach($field in @('nonce','ordinal','action','context','pass')){Require94 (Equal94 $trace[$field] $request[$field]) '95 native consumed FSM differs'}
  $file='copy-core-ack95-'+$request.nonce+'.json';Require94 ($trace.state -ceq 'completed' -and $trace.ack.file -ceq $file -and $trace.ack.file_sha256 -cmatch '^[A-F0-9]{64}$' -and (Get-FileHash -LiteralPath (Join-Path $Root ('worker/'+$file))).Hash -ceq $trace.ack.file_sha256 -and (Equal94 $ack (Read94 ('worker/'+$file)))) '95 archived ACK/hash/native receipt differs';[void]$script:Files95.Add('worker/'+$file)
  Require94 ($ack.status -ceq 'ACK' -and $ack.target_id -ceq $target -and $ack.owned_pid -eq $ready.owned_pid -and $ack.owned_start_utc_ticks -eq $ready.owned_start_utc_ticks -and (Equal94 $ack.expires_utc $ready.expires_utc) -and $ack.documents.Count -eq 2) '95 ACK owner/TTL differs'
  $docs=Map94 $ack.documents 'context';Require94 ($docs.ContainsKey('Part') -and $docs.ContainsKey('Sheet')) '95 ACK contexts absent'
  foreach($kind in @('Part','Sheet')){
   $meta=$docs[$kind];ValidateMeta95 $meta $catalogDocs[$kind] $initialNative
   if($null -ne $previous){$old=$previous[$kind];foreach($field in @('context','document_id','internal_name','subtype','sketch_id','parameter_key','source_ids','source_curves')){Require94 (Equal94 $old[$field] $meta[$field]) '95 ACK frozen identity/anchor map changed'};$reopen=$request.action -ceq 'reopen' -and $request.context -ceq $kind;Require94 ($(if($reopen){-not (SamePath95 $old.native_path $meta.native_path)}else{SamePath95 $old.native_path $meta.native_path})) '95 ACK owned path transition differs';if(-not $reopen){Require94 ($old.display_name -ceq $meta.display_name) '95 ACK display name changed outside reopen'}}else{Require94 ((SamePath95 $meta.native_path ((Map94 $ready.owned_documents 'document_id')[[string]$meta.document_id].saved_path))) '95 prepare ACK original path differs'}
  }
  $acks+=,$docs;$previous=$docs
 }
 # Fixed request is the final published request, not a history substitute.
 $finalRequest=Read94 'worker/copy-core-request95.json';Require94 (Equal94 $finalRequest $client.protocol95[13].request) '95 final request differs from consumed complete';[void]$script:Files95.Add('worker/copy-core-request95.json')
 $rulesByContext=@{};$copiesByContext=@{}
 foreach($kind in @('Part','Sheet')){
  $baseline=$baselines[$kind];$meta=$acks[0][$kind];Require94 ($baseline.state.document_id -ceq $meta.document_id -and $baseline.sketch_id -ceq $meta.sketch_id -and $baseline.state.revision -cmatch '^[a-f0-9]{32}:[0-9]+$' -and $baseline.snapshot_raw.complete -eq $true -and $baseline.snapshot_raw.partial -eq $false) '95 preparatory Core owner/completeness differs'
  $work=@($frozen.cases|Where-Object {$_.context -ceq $kind});Require94 ($work.Count -eq 9 -and (Equal94 $work[8].first_snapshot_raw $baseline.snapshot_raw)) '95 preparatory Core differs from completed94 copies'
  $copyIds=@($work|ForEach-Object {foreach($pair in $_.pairs){$pair.copy.entity_id}});Require94 ($copyIds.Count -eq 14 -and @($copyIds|Select-Object -Unique).Count -eq 14 -and $baseline.copy_ids.Count -eq 14 -and (Equal94 @($copyIds|Sort-Object -CaseSensitive) @($baseline.copy_ids|Sort-Object -CaseSensitive))) '95 explicit copy inventory differs'
  $copiesByContext[$kind]=$copyIds;$rules=BindingRules95 $baseline.snapshot_raw $meta $initialNative;$rulesByContext[$kind]=$rules
  Require94 ($baseline.annotation_rules.Count -eq $rules.Count) '95 stored annotation rules incomplete';$reported=Map94 $baseline.annotation_rules 'constraint_id';foreach($id in $rules.Keys){Require94 ($reported.ContainsKey($id) -and (Equal94 $reported[$id] $rules[$id])) '95 reported annotation rule differs from recomputed native members'}
  CoreNative95 $baseline.snapshot_raw $initialNative $meta
 }
 $specs=@(@('Part',1,1,2,2,3),@('Part',2,3,4,4,5),@('Part',0,5,6,5,6),@('Sheet',1,7,8,7,8),@('Sheet',2,9,10,9,10),@('Sheet',0,11,12,10,11));$lastNative=$initialNative;$nativeByContext=@{Part=$initialNative;Sheet=$initialNative};$verifiedReads=@()
 for($r=0;$r -lt 6;$r++){
  $spec=$specs[$r];$kind=[string]$spec[0];$pass=[int]$spec[1];$isReopen=$pass -eq 0;$meta=$acks[[int]$spec[2]][$kind];$oldMeta=$acks[[int]$spec[2]-1][$kind];$first=$client.lifecycle[$r];$snapshot=$first.snapshot_raw;$baseline=$baselines[$kind];$row=$nativeDocs[$kind]
  Require94 ($first.context -ceq $kind -and $first.pass -eq $pass -and $first.kind -ceq $(if($isReopen){'reopen'}else{'source_edit'}) -and $first.first_core_snapshot -eq $true -and $first.snapshot_attempts -eq 1 -and $first.copies_and_unrelated_exact -eq $true -and $first.fresh_references -eq $true -and $first.state.document_id -ceq $meta.document_id -and $first.revision -ceq $first.state.revision -and $first.event_revision_after -ceq $first.state.revision -and $first.event_revision_before -cne $first.event_revision_after -and $first.state.revision -cmatch '^[a-f0-9]{32}:[0-9]+$' -and $first.event_revision_before -cmatch '^[a-f0-9]{32}:[0-9]+$' -and ([string]$first.event_revision_before).Split(':')[0] -ceq ([string]$first.event_revision_after).Split(':')[0]) '95 first Core read/revision/owner boundary differs'
  $prior=if($pass -eq 1){$baseline.snapshot_raw}else{$client.lifecycle[$r-1].snapshot_raw};$priorRevision=if($pass -eq 1){$baseline.state.revision}else{$client.lifecycle[$r-1].event_revision_after};Require94 ($first.event_revision_before -ceq $priorRevision) '95 first-read revision chain differs'
  CoreIndependent95 $prior $snapshot $oldMeta $meta $rulesByContext[$kind] $isReopen
  $actualCore=Map94 $snapshot.entities 'id';$initialCore=Map94 $baseline.snapshot_raw.entities 'id';foreach($id in $copiesByContext[$kind]){Require94 ($actualCore.ContainsKey([string]$id) -and (Equal94 $initialCore[[string]$id] $actualCore[[string]$id])) '95 explicit copy Data changed from baseline'}
  if(-not $isReopen){
   Require94 ($first.mutation_origin -ceq 'native_fixture_edit' -and $first.source_geometry_checked -eq $true) '95 fixture source edit misrepresented as product commit';CoreSource95 $snapshot $baseline.snapshot_raw $meta $pass
   Require94 ($row.updates.Count -eq 2) '95 native updates incomplete';$update=$row.updates[$pass-1];Require94 ($update.pass -eq $pass -and $update.state -ceq 'native_checked_after_core_first_snapshot' -and $update.native_fixture_edit -eq $true -and $update.product_last_commit -eq $false -and $update.post_end_native_semantic_reads -eq 0 -and $update.copies_independent_exact -eq $true) '95 native source boundary differs'
   Closed94 $update.requested @('offset_from_initial_mm','translation_from_previous_mm','circle_radius_mm','arc_radius_mm','ground_added','move_to_used');$delta=if($pass -eq 1){@(5,7)}else{@(-6,4)};$relative=if($pass -eq 1){@(5,7)}else{@(-11,-3)}
   Require94 ((Equal94 $update.requested.offset_from_initial_mm $delta) -and (Equal94 $update.requested.translation_from_previous_mm $relative) -and $update.requested.circle_radius_mm -eq $(if($pass -eq 1){12}else{6}) -and $update.requested.arc_radius_mm -eq 9 -and $update.requested.ground_added -eq $false -and $update.requested.move_to_used -eq $false) '95 native requested edit differs from prescribed profile'
   $before=Raw95 $update.before_native ([int]$spec[4]) ('before_edit_'+$kind+'_'+$pass);$after=Raw95 $update.first_native_read_after_core ([int]$spec[5]) ('inspect_edit_'+$kind+'_'+$pass)
   Require94 (Equal94 (Without95 $lastNative @('active_document_id')) (Without95 $before @('active_document_id'))) '95 pre-edit native continuity differs'
   NativeCompare95 $initialNative $after $acks[0][$kind] $meta $pass $false $before
   $observed=@($row.source_anchor_observations|Where-Object {$_.pass -eq $pass});$expectedCount=@($meta.source_curves.Values|ForEach-Object {$_.constraint_anchors}).Count;Require94 ($observed.Count -eq $expectedCount) '95 actual native anchor observations incomplete'
   foreach($curve in $meta.source_curves.Values){foreach($anchor in $curve.constraint_anchors){$obs=@($observed|Where-Object {$_.constraint_key -ceq $anchor.constraint_key});Require94 ($obs.Count -eq 1 -and (Equal94 $obs[0].ordered_members $anchor.members)) '95 native observed anchor graph differs';$expected=Offset95 $anchor.anchor_mm $delta;Near94 $obs[0].expected_anchor_mm $expected;Near94 $obs[0].actual_anchor_mm $expected}}
  }else{
   $persist=$row.persistence;Require94 ($persist.state -ceq 'reopened_verified' -and $persist.http_remains_live -eq $true -and $persist.post_open_native_semantic_reads -eq 0 -and $persist.old_typed_refs_reused -eq $false -and $persist.original_disk_unchanged -eq $true -and $persist.copy_disk_readonly -eq $true -and $persist.local_parameter_owner_relocation_verified -eq $true -and $persist.untouched_fixture_parameter_owner_relocation_verified -eq $true -and $first.parameter_owner_relocation_only -eq $true -and (SamePath95 $persist.original_path $oldMeta.native_path) -and (SamePath95 $persist.copy_path $meta.native_path) -and (SamePath95 $first.before_native_path $oldMeta.native_path) -and (SamePath95 $first.reopened_native_path $meta.native_path) -and $persist.reopened_document_id -ceq $meta.document_id) '95 reopen ownership/read-boundary differs'
   Require94 (Equal94 $persist.before_save_copy $row.updates[1].first_native_read_after_core) '95 savecopy baseline descriptor differs';$after=Raw95 $persist.first_reopened_native_read ([int]$spec[5]) ('inspect_reopen_'+$kind)
   NativeCompare95 $nativeByContext[$kind] $after $oldMeta $meta 0 $true
  }
  $actualDoc=(Map94 $after.documents 'document_id')[[string]$meta.document_id];Require94 ($actualDoc.internal_name -ceq $meta.internal_name -and $actualDoc.database_revision_id -ceq $acks[[int]$spec[3]][$kind].native_database_revision_id -and (SamePath95 $actualDoc.path $meta.native_path)) '95 first native read/ACK owner or database revision differs'
  CoreNative95 $snapshot $after $meta;$lastNative=$after;$nativeByContext[$kind]=$after;$verifiedReads+=@{context=$kind;kind=$first.kind;pass=$pass;revision=$first.revision;first_core_snapshot=$true;source_and_copy_geometry_recomputed=$true;unchanged_data_exact=$true}
 }
 $final=Raw95 $native.final_native 12 'complete';Require94 (Equal94 $lastNative $final) '95 final native state changed after last inspected first Core read'
 $archive=Map94 $wrapper.archived_fixtures95 'name';Require94 ($archive.Count -eq 4) '95 four archived IPTs required';$ipts=@()
 foreach($kind in @('Part','Sheet')){
  $row=$nativeDocs[$kind];Require94 ($row.state -ceq 'two_source_updates_and_reopen_observed' -and $row.updates.Count -eq 2) '95 native context lifecycle incomplete'
  foreach($role in @('original','copy')){$persist=$row.persistence;$name=[IO.Path]::GetFileName([string]$persist[($role+'_path')]);$sha=[string]$persist[($role+'_sha256')];Require94 ($sha -cmatch '^[A-F0-9]{64}$' -and $archive.ContainsKey($name) -and $archive[$name].sha256 -ceq $sha -and $archive[$name].context -ceq $kind -and $archive[$name].role -ceq $role) '95 IPT native/wrapper binding differs';$file=Get-Item -LiteralPath (Join-Path $Root ('fixtures/'+$name));Require94 (-not $file.PSIsContainer -and ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -and $file.Length -gt 0 -and (Get-FileHash -LiteralPath $file.FullName).Hash -ceq $sha) '95 archived IPT bytes differ';$ipts+=@{name=$name;context=$kind;role=$role;sha256=$sha};[void]$script:Files95.Add('fixtures/'+$name)}
 }
 Require94 (@($ipts.name|Select-Object -Unique).Count -eq 4 -and @($script:Files95|Where-Object {$_ -like 'worker/copy-native-state95-*'}).Count -eq 12) '95 raw/IPT consumption incomplete'
 foreach($path in @('worker/copy-client95.json','worker/copy-native-lifecycle95.json')){[void]$script:Files95.Add($path)}
 return [ordered]@{schema=1;status='VERIFIED_PC_CURVE_COPY_CORE_LIFECYCLE95_SUBSET';run_id=$run;candidate_sha256=$wrapper.stage_dll_sha256;baseline94_workflows=18;baseline94_pairs=28;baseline94_refusals=10;baseline94_checkpoints=74;protocol_steps=14;native_raw_states=12;source_updates=4;reopens=2;core_first_reads=6;copied_objects_exact=28;reads=$verifiedReads;archived_ipts=$ipts;post_wrapper_owned_absence=$true;full_s1_pass=$false;open_s1_gates=18;quest_proof=$false;physical_proof=$false;files=@($script:Files95|Sort-Object -CaseSensitive|ForEach-Object {@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $Root $_)).Hash}});limits=@('Offline stored-state consistency and recomputation; hashes are not independent authenticity/execution proof','First-read timing/order and full typed-reference freshness rely on archived producer/client source and protocol; raw snapshots do not contain full typed references','Unchanged/source flags/copies/constraints/graphs/styles/glyphs are serialized-exact; only source coordinate1e-5mm/radius1e-8mm/domain-angle1e-10 tolerances apply','Only explicitly bound unselected13mm Copy88 and separately prepare-attested untouched S1_TextRead10mm fixture fields may relocate to their exact owned path; parameter text is not copied','No arc reshaping, new solver constraints, arbitrary copy profiles, UI/Quest/physical/wireless/performance proof, or full S1 closure')}
}
function Verify95([string]$Root){$baseline=Verify94 $Root;$result=VerifyLifecycle95 $Root $baseline;$result.baseline94=$baseline;$result.files=@($baseline.files)+@($result.files);return $result}
function WriteTemp95([string]$Relative,$Value){$path=[IO.Path]::GetFullPath((Join-Path $script:Root94 $Relative));Require94 ($path.StartsWith($script:Root94+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) '95 temporary write escaped';[IO.File]::WriteAllText($path,(Json94 $Value),[Text.UTF8Encoding]::new($false))}
function RefreshDescriptor95($Node,[string]$File,[string]$FileSha,[string]$NativeSha){
 if($Node -is [Collections.IDictionary]){
  if($Node.Contains('file') -and $Node.file -ceq $File -and $Node.Contains('file_sha256') -and $Node.Contains('native_state_sha256')){$Node.file_sha256=$FileSha;$Node.native_state_sha256=$NativeSha}
  foreach($key in @($Node.Keys)){RefreshDescriptor95 $Node[$key] $File $FileSha $NativeSha}
 }elseif($Node -is [Collections.IList]){foreach($item in $Node){RefreshDescriptor95 $item $File $FileSha $NativeSha}}
}
function SelfTests95([string]$Original,$Verified){
 # Full baseline94 is recomputed once by Verify95 before these95-specific
 # semantic negative tests (including four strict untouched10mm refusals). The temporary comparator reuses that verified
 # baseline result; it does not pretend to rerun74baseline checkpoints8times.
 $tempBase=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar);$leaf='s1-copy95-offline-'+[Guid]::NewGuid().ToString('N');$temp=[IO.Path]::GetFullPath((Join-Path $tempBase $leaf));[void](New-Item -ItemType Directory -Path $temp);$results=@()
 try{
  foreach($name in @('copy_geometry','revision','action_order','typed_anchor','source_arc_extent','parameter_external_owner','native_copy_geometry','untouched_external_owner','untouched_wrong_name','untouched_wrong_key','untouched_wrong_value','ipt_sha')){
   $caseRoot=Join-Path $temp $name;[void](New-Item -ItemType Directory -Path $caseRoot)
   $needed=@('wrapper-result.json','worker/ready.json','worker/copy-client94.json','worker/copy-client95.json','worker/copy-native-lifecycle95.json','worker/curve-copy-sources94.json','worker/copy-native-checkpoint94-074.json','worker/prerequisite94-ready.json','worker/prerequisite94-verified.json','worker/copy-core-request95.json')+@($Verified.files|Where-Object {$_.path -like 'worker/copy-core-ack95-*' -or $_.path -like 'worker/copy-native-state95-*' -or $_.path -like 'fixtures/*'}|ForEach-Object {$_.path})
   foreach($relative in @($needed|Select-Object -Unique)){
    $source=[IO.Path]::GetFullPath((Join-Path $Original $relative));$destination=[IO.Path]::GetFullPath((Join-Path $caseRoot $relative));Require94 ($source.StartsWith($Original+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and $destination.StartsWith($caseRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) '95 selftest copy escaped'
    $parent=Split-Path -Parent $destination;if(-not (Test-Path -LiteralPath $parent)){[void](New-Item -ItemType Directory -Path $parent)};Copy-Item -LiteralPath $source -Destination $destination
   }
   $script:Root94=$caseRoot;$client=Read94 'worker/copy-client95.json';$native=Read94 'worker/copy-native-lifecycle95.json';$meta=(Map94 $client.protocol95[0].ack.documents 'context').Part;$copy=@($client.cases|Where-Object {$_.context -ceq 'Part'}|ForEach-Object {$_.pairs}|Where-Object {$_.type -ceq 'circle'})[0].copy.entity_id;$raw=$null;$rawFile=$null;$expect=''
   if($name.StartsWith('untouched_',[StringComparison]::Ordinal)){
    $rawFile='copy-native-state95-006.json';$raw=Read94 ('worker/'+$rawFile);$doc=(Map94 $raw.native_state.documents 'document_id')[[string]$meta.document_id];$binding=(Map94 $native.documents 'context').Part.untouched_parameter_binding95;$sketch=(Map94 $doc.sketches)[[string]$binding.sketch_key];$selectedTexts=@($sketch.texts|Where-Object {$_.key -ceq $binding.text_key});Require94 ($selectedTexts.Count -eq 1) '95 selftest attested untouched text absent';$text=$selectedTexts[0]
    switch($name){
     'untouched_external_owner'{$external=[Security.SecurityElement]::Escape((Join-Path $tempBase 'external-L10-fixture95.ipt'));$text.formatted_text="L=<Parameter Resolved='True' ComponentIdentifier='$external' Name='S1QuestLength73' Precision='0'>10</Parameter>";$expect='untouched fixture field owner is outside'}
     'untouched_wrong_name'{$text.formatted_text=([string]$text.formatted_text).Replace('S1QuestLength73','OtherParameter95');$expect='untouched fixture XML binding/value'}
     'untouched_wrong_key'{$parameter=@($doc.parameters|Where-Object {$_.name -ceq 'S1QuestLength73'})[0];$parameter.key=[Convert]::ToBase64String([byte[]]@(1,9,5,0));$expect='untouched fixture parameter binding/type/value'}
     'untouched_wrong_value'{$text.text='L=11';$text.formatted_text=([string]$text.formatted_text).Replace('>10</Parameter>','>11</Parameter>');$expect='untouched fixture text native identity/value'}
    }
   }
   switch($name){
    'copy_geometry'{$entities=Map94 $client.lifecycle[0].snapshot_raw.entities 'id';$entities[[string]$copy].radius_mm=[double]$entities[[string]$copy].radius_mm+1;$expect='copy/unrelated Data|explicit copy'}
    'revision'{$client.lifecycle[0].state.revision=([string]$client.lifecycle[0].state.revision).Split(':')[0]+':999999999';$expect='revision/owner boundary|revision chain'}
    'action_order'{$swap=$client.protocol95[0];$client.protocol95[0]=$client.protocol95[1];$client.protocol95[1]=$swap;$expect='nonce/order/action'}
    'typed_anchor'{$rule=$client.baselines95[0].annotation_rules[0];$constraints=Map94 $client.lifecycle[0].snapshot_raw.constraints 'id';$constraints[[string]$rule.constraint_id].annotation_mm[0]=[double]$constraints[[string]$rule.constraint_id].annotation_mm[0]+1;$expect='exact typed first-member anchor'}
    'parameter_external_owner'{$entities=Map94 $client.lifecycle[2].snapshot_raw.entities 'id';$text=$entities[[string]$meta.source_ids.parameter_text];$external=[Security.SecurityElement]::Escape((Join-Path $tempBase 'unattested-owner95.ipt'));$text.formatted_text="Bound=<Parameter Resolved='True' ComponentIdentifier='$external' Name='S1Copy88Length' Precision='0'>13</Parameter>";$expect='parameter owner is outside'}
    'source_arc_extent'{$rawFile='copy-native-state95-003.json';$raw=Read94 ('worker/'+$rawFile);$rows=Map94 (Sketch94 $raw.native_state $meta.document_id (Opaque94 $meta.sketch_id $meta.document_id 'sketch')).entities;$curve=$rows[[string]$meta.source_curves.arc_quarter.entity_key];$curve.geometry_sweep_angle_rad=[double]$curve.geometry_sweep_angle_rad+0.1;$expect='Coordinate/radius/angle differs'}
    'native_copy_geometry'{$rawFile='copy-native-state95-003.json';$raw=Read94 ('worker/'+$rawFile);$rows=Map94 (Sketch94 $raw.native_state $meta.document_id (Opaque94 $meta.sketch_id $meta.document_id 'sketch')).entities;$key=Opaque94 $copy $meta.document_id 'sketch_entity';$rows[$key].radius_mm=[double]$rows[$key].radius_mm+1;$expect='native copy/unrelated graph entity'}
    'ipt_sha'{$path=Join-Path $caseRoot ('fixtures/'+$Verified.archived_ipts[0].name);$stream=[IO.FileStream]::new($path,[IO.FileMode]::Append,[IO.FileAccess]::Write,[IO.FileShare]::None);try{$stream.WriteByte(0x7f)}finally{$stream.Dispose()};$expect='archived IPT bytes differ'}
   }
   $updated=$false
   if($null -ne $raw){$raw.native_state_sha256=NativeHash94 $raw.native_state;WriteTemp95 ('worker/'+$rawFile) $raw;$sha=(Get-FileHash -LiteralPath (Join-Path $caseRoot ('worker/'+$rawFile))).Hash;RefreshDescriptor95 $native $rawFile $sha $raw.native_state_sha256;WriteTemp95 'worker/copy-native-lifecycle95.json' $native;$updated=$true}
   if($null -eq $raw -and $name -cne 'ipt_sha'){WriteTemp95 'worker/copy-client95.json' $client}
   $rejected=$false;$reason='';try{[void](VerifyLifecycle95 $caseRoot $Verified.baseline94)}catch{$rejected=$true;$reason=$_.Exception.Message}
   Require94 ($rejected -and $reason -match $expect -and $reason -notmatch 'sidecar file/compact SHA') ('95 semantic selftest missed its intended comparator: '+$name+'; '+$reason)
   $results+=@{name=$name;status='REJECTED_AS_EXPECTED';semantic_comparator=$true;raw_file_compact_sha_and_descriptors_updated=$updated;assertion=$reason}
  }
 }finally{
  $script:Root94=$Original;$resolved=[IO.Path]::GetFullPath($temp);Require94 ([IO.Path]::GetDirectoryName($resolved) -ceq $tempBase -and [IO.Path]::GetFileName($resolved) -ceq $leaf -and $resolved -cne $Original -and ((Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) '95 selftest cleanup target differs'
  Require94 (@(Get-ChildItem -LiteralPath $resolved -Recurse -Force|Where-Object {($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0}).Count -eq 0) '95 selftest cleanup reparse path refused';Remove-Item -LiteralPath $resolved -Recurse -Force
 }
 return ,$results
}
$root=[IO.Path]::GetFullPath($EvidenceDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
Require94 ((Test-Path -LiteralPath $root -PathType Container) -and [IO.Path]::GetFileName($root) -cmatch '^native-r[1-9][0-9]*$' -and [IO.Path]::GetFileName([IO.Path]::GetDirectoryName($root)) -ceq 'increment95' -and ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'Only archived increment95/native-rN evidence accepted; no private/live directory'
if($null -eq [Type]::GetType('Newtonsoft.Json.Linq.JToken, Newtonsoft.Json',$false)){
 $jsonDll=Join-Path $root 'bin/Newtonsoft.Json.dll';Require94 ((Test-Path -LiteralPath $jsonDll -PathType Leaf) -and ((Get-Item -LiteralPath $jsonDll).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'Pure Json.NET offline dependency unavailable';[void][Reflection.Assembly]::LoadFrom($jsonDll)
}
$output=Join-Path $root $OutputName;if(-not $ValidateOnly){Require94 (-not (Test-Path -LiteralPath $output)) 'Verification output exists; choose a new filename'}
$result=Verify95 $root;if($SelfTest){$result.selftests=SelfTests95 $root $result};$result.verifier_sha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash
if($ValidateOnly){$result|ConvertTo-Json -Depth 16;exit 0}
$stream=[IO.FileStream]::new($output,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes(($result|ConvertTo-Json -Depth 16));$stream.Write($bytes);$stream.Flush($true)}finally{$stream.Dispose()}
Write-Output $output
