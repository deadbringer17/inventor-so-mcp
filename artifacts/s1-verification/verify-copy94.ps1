[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
 [ValidatePattern('^verification-copy94-[a-zA-Z0-9-]+\.json$')][string]$OutputName='verification-copy94-r1.json',
 [switch]$ValidateOnly,
 [switch]$SelfTest
)
$ErrorActionPreference='Stop'
$script:JsonOptions94=@{AsHashtable=$true;Depth=100}
# PowerShell7.5+ otherwise converts ISO strings into CLR DateTime values and
# changes their exact JSON representation/timezone when serialized again.
if((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')){$script:JsonOptions94.DateKind='String'}
# Offline consistency verifier, not an executable/native oracle. No COM,
# network, process control, assembly compilation, or producer-report writes.
function Require94([bool]$Condition,[string]$Assertion){if(-not $Condition){throw [InvalidOperationException]::new($Assertion)}}
function Json94($Value){ConvertTo-Json -InputObject $Value -Compress -Depth 100}
function Equal94($A,$B){[string]::Equals((Json94 $A),(Json94 $B),[StringComparison]::Ordinal)}
# Stable, complete serialization avoids an interpreted recursive comparison of
# millions of glyph coordinates. Object property order is conservatively exact;
# no keys, arrays, numerical values or unknown observations are discarded.
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
 $run=[string]$wrapper.run_id;Require94 ($run -cmatch '^[0-9a-f]{32}$' -and $ready.status -ceq 'ready' -and $ready.target_id -ceq ('inventor-2027-'+$ready.owned_pid) -and $ready.owned_pid -notin @(91056,77764)) 'Owned target differs'
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
 $used=[Collections.Generic.HashSet[int]]::new();$allCopies=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$nativeChecks=Map94 $native.checks 'copy_key';$pairCount=0;$expectedOrdinal=0;$unknowns=[Collections.Generic.List[string]]::new()
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
    Require94 ($nativeChecks.ContainsKey($copyKey) -and $nativeChecks[$copyKey].document_id -ceq $docId -and $nativeChecks[$copyKey].source_key -ceq $sourceKey -and $nativeChecks[$copyKey].workflow -ceq $label -and $nativeChecks[$copyKey].type -ceq $type -and $nativeChecks[$copyKey].native_translation_and_original_exact -eq $true -and $nativeChecks[$copyKey].independent_native_keys_points_glyphs -eq $true) 'Independent native pair attestation differs'
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
function SelfTest94([string]$Original,$Verified){
 $tempBase=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar);$leaf='s1-copy94-offline-'+[Guid]::NewGuid().ToString('N');$temp=Join-Path $tempBase $leaf
 [void](New-Item -ItemType Directory -Path $temp);$results=@()
 try{
  foreach($name in @('copy_radius','old_copy_point','unrelated_parameter','abort_dirty','first_snapshot','checkpoint_order','foreign_identity','constraint_external_member')){
   $caseRoot=Join-Path $temp $name;[void](New-Item -ItemType Directory -Path $caseRoot)
   foreach($entry in $Verified.files){
    $relative=[string]$entry.path;$source=[IO.Path]::GetFullPath((Join-Path $Original $relative));$destination=[IO.Path]::GetFullPath((Join-Path $caseRoot $relative))
    Require94 ($source.StartsWith($Original+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and $destination.StartsWith($caseRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'Selftest corpus path escaped'
    $parent=Split-Path -Parent $destination;if(-not (Test-Path -LiteralPath $parent)){[void](New-Item -ItemType Directory -Path $parent)};Copy-Item -LiteralPath $source -Destination $destination
   }
   $script:Root94=$caseRoot;$client=Read94 'worker/copy-client94.json';$case=$client.cases[0];$trace=$null;$raw=$null;$rawFile=$null
   if($name -cin @('copy_radius','old_copy_point','unrelated_parameter','constraint_external_member')){
    $index=[int]$case.checkpoints.commit_first_core_after;$trace=$client.checkpoint_trace94[$index-1];$rawFile='worker/'+$trace.ack.file;$raw=Read94 $rawFile
    $sketchKey=Opaque94 $case.pairs[0].source.sketch_id $case.document_id 'sketch';$graph=Sketch94 $raw.native_state $case.document_id $sketchKey;$entities=Map94 $graph.entities
    $copyKey=Opaque94 $case.pairs[0].copy.entity_id $case.document_id 'sketch_entity';$sourceKey=Opaque94 $case.pairs[0].source.entity_id $case.document_id 'sketch_entity';$copy=$entities[$copyKey]
    switch($name){
     'copy_radius'{$copy.radius_mm=[double]$copy.radius_mm+1}
     'old_copy_point'{$copy.structural_points.center.key=$entities[$sourceKey].structural_points.center.key}
     'unrelated_parameter'{$doc=(Map94 $raw.native_state.documents 'document_id')[[string]$case.document_id];Require94 ($doc.parameters.Count -gt 0) 'Selftest parameter fixture absent';$doc.parameters[0].expression='tampered-owned-parameter'}
     'constraint_external_member'{$old=Map94 (Sketch94 ((Read94 ('worker/'+$client.checkpoint_trace94[[int]$case.checkpoints.preview_before-1].ack.file)).native_state) $case.document_id $sketchKey).constraints;$added=@($graph.constraints|Where-Object {-not $old.ContainsKey([string]$_.key)});Require94 ($added.Count -gt 0) 'Selftest copied intrinsic fixture absent';$added[0].members[0].key=$sourceKey}
    }
   }elseif($name -ceq 'abort_dirty'){
    $index=[int]$case.checkpoints.preview_cancel_after;$trace=$client.checkpoint_trace94[$index-1];$rawFile='worker/'+$trace.ack.file;$raw=Read94 $rawFile;$raw.native_state.documents[0].dirty=-not $raw.native_state.documents[0].dirty
   }elseif($name -ceq 'first_snapshot'){
    $map=Map94 $case.first_snapshot_raw.entities 'id';$map[[string]$case.pairs[0].copy.entity_id].radius_mm=[double]$map[[string]$case.pairs[0].copy.entity_id].radius_mm+1
   }elseif($name -ceq 'checkpoint_order'){
    $swap=$client.checkpoint_trace94[0];$client.checkpoint_trace94[0]=$client.checkpoint_trace94[1];$client.checkpoint_trace94[1]=$swap
   }elseif($name -ceq 'foreign_identity'){
    $row=@($client.refusals|Where-Object {$_.kind -ceq 'foreign_reference'})[0];$row.adversarial_foreign_reference.document_id=$row.document_id;$row.operations[0].arguments.options.references[0].document_id=$row.document_id
   }
   $hashUpdated=$false
   if($null -ne $raw){
    $raw.native_state_sha256=NativeHash94 $raw.native_state;$trace.ack.native_state_sha256=$raw.native_state_sha256
    [IO.File]::WriteAllText((Join-Path $caseRoot $rawFile),(Json94 $raw),[Text.UTF8Encoding]::new($false));$hashUpdated=$true
   }
   [IO.File]::WriteAllText((Join-Path $caseRoot 'worker/copy-client94.json'),(Json94 $client),[Text.UTF8Encoding]::new($false))
   $rejected=$false;$reason='';try{[void](Verify94 $caseRoot)}catch{$rejected=$true;$reason=$_.Exception.Message}
   Require94 ($rejected -and $reason -notmatch 'compact SHA|later OS absence|Terminal wrapper') ('Semantic tamper escaped or was rejected by unrelated metadata: '+$name)
   $results+=@{name=$name;status='REJECTED_AS_EXPECTED';raw_digest_and_trace_updated=$hashUpdated;assertion=$reason}
  }
 }finally{
  $resolved=[IO.Path]::GetFullPath($temp)
  Require94 ([IO.Path]::GetDirectoryName($resolved) -ceq $tempBase -and [IO.Path]::GetFileName($resolved) -ceq $leaf -and $resolved -cne $Original -and ((Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'Selftest cleanup target differs'
  Require94 (@(Get-ChildItem -LiteralPath $resolved -Recurse -Force|Where-Object {($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0}).Count -eq 0) 'Selftest cleanup reparse path refused'
  Remove-Item -LiteralPath $resolved -Recurse -Force;$script:Root94=$Original
 }
 return ,$results
}
$root=[IO.Path]::GetFullPath($EvidenceDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
Require94 ((Test-Path -LiteralPath $root -PathType Container) -and [IO.Path]::GetFileName($root) -cmatch '^native-r[1-9][0-9]*$' -and [IO.Path]::GetFileName([IO.Path]::GetDirectoryName($root)) -ceq 'increment94' -and ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'Only archived increment94/native-rN evidence is accepted; no private/live directory'
# Pure JSON dependency only. Never load Inventor, host or candidate assemblies.
if($null -eq [Type]::GetType('Newtonsoft.Json.Linq.JToken, Newtonsoft.Json',$false)){
 $jsonDll=Join-Path $root 'bin/Newtonsoft.Json.dll';Require94 ((Test-Path -LiteralPath $jsonDll -PathType Leaf) -and ((Get-Item -LiteralPath $jsonDll).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'Json.NET offline serialization dependency unavailable'
 [void][Reflection.Assembly]::LoadFrom($jsonDll)
}
$output=Join-Path $root $OutputName;if(-not $ValidateOnly){Require94 (-not (Test-Path -LiteralPath $output)) 'Verification output exists; choose a new name'}
$result=Verify94 $root;if($SelfTest){$result.selftests=SelfTest94 $root $result};$result.verifier_sha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash
if($ValidateOnly){$result|ConvertTo-Json -Depth 12;exit 0}
$stream=[IO.FileStream]::new($output,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes(($result|ConvertTo-Json -Depth 12));$stream.Write($bytes);$stream.Flush($true)}finally{$stream.Dispose()}
Write-Output $output
