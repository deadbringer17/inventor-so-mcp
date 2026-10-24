[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
 [ValidatePattern('^verification-spline-closure96-[a-zA-Z0-9-]+\.json$')][string]$OutputName='verification-spline-closure96-r1.json',
 [switch]$ValidateOnly,
 [switch]$SelfTest
)
$ErrorActionPreference='Stop'
$script:JsonOptions96=@{AsHashtable=$true;Depth=100}
# PowerShell7.5+ otherwise converts ISO strings into CLR DateTime values and
# changes their exact JSON representation/timezone when serialized again.
if((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')){$script:JsonOptions96.DateKind='String'}
# Offline consistency verifier, not an executable/native oracle. No COM,
# network, process control, assembly compilation, or producer-report writes.
function Require96([bool]$Condition,[string]$Assertion){if(-not $Condition){throw [InvalidOperationException]::new($Assertion)}}
function Json96($Value){ConvertTo-Json -InputObject $Value -Compress -Depth 100}
function Equal96($A,$B){[string]::Equals((Json96 $A),(Json96 $B),[StringComparison]::Ordinal)}
# Stable, complete serialization avoids an interpreted recursive comparison of
# millions of glyph coordinates. Object property order is conservatively exact;
# no keys, arrays, numerical values or unknown observations are discarded.
function Clone96($Value){Json96 $Value|ConvertFrom-Json @script:JsonOptions96}
function Closed96($Value,[string[]]$Fields){
 Require96 ($Value -is [Collections.IDictionary] -and $Value.Count -eq $Fields.Count) 'Closed descriptor field count differs'
 foreach($key in $Value.Keys){Require96 ($key -cin $Fields) 'Unexpected descriptor field'}
}
function Map96($Rows,[string]$Key='key'){
 $map=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
 foreach($row in $Rows){Require96 ($row -is [Collections.IDictionary] -and -not [string]::IsNullOrEmpty([string]$row[$Key]) -and $map.TryAdd([string]$row[$Key],$row)) 'Missing or duplicate native identity'}
 return ,$map
}
function Read96([string]$Relative){
 $path=[IO.Path]::GetFullPath((Join-Path $script:Root96 $Relative))
 Require96 ($path.StartsWith($script:Root96+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'Evidence path escapes root'
 $file=Get-Item -LiteralPath $path
 Require96 (-not $file.PSIsContainer -and ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -and $file.Length -gt 0 -and $file.Length -le 67108864) 'Bounded regular evidence JSON required (64MiB offline limit)'
 Get-Content -LiteralPath $path -Raw|ConvertFrom-Json @script:JsonOptions96
}
function Near96($A,$B,[double]$Tolerance=1e-5){
 Require96 ($A.Count -gt 0 -and $A.Count -eq $B.Count) 'Native coordinates have different shapes'
 for($i=0;$i -lt $A.Count;$i++){Require96 ([double]::IsFinite([double]$A[$i]) -and [double]::IsFinite([double]$B[$i]) -and [Math]::Abs([double]$A[$i]-[double]$B[$i]) -le $Tolerance) 'Native coordinate/domain differs'}
}
function CompactHash96([string]$Json){
 $native=[Newtonsoft.Json.Linq.JToken]::Parse($Json)
 $bytes=[Text.UTF8Encoding]::new($false).GetBytes($native.ToString([Newtonsoft.Json.Formatting]::None))
 [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
}
function Opaque96([string]$Id,[string]$Document,[string]$Type){
 Require96 ($Id.StartsWith('ent_',[StringComparison]::Ordinal) -and $Id.Length -le 131072) 'Native opaque reference missing/oversized'
 $encoded=$Id.Substring(4).Replace('-','+').Replace('_','/');$encoded=$encoded.PadRight($encoded.Length+(4-$encoded.Length%4)%4,'=')
 $ref=[Text.UTF8Encoding]::new($false,$true).GetString([Convert]::FromBase64String($encoded))|ConvertFrom-Json @script:JsonOptions96
 Closed96 $ref @('Version','DocumentId','EntityType','Key','Context')
 Require96 ($ref.Version -eq 1 -and $ref.DocumentId -ceq $Document -and $ref.EntityType -ceq $Type -and $ref.Context -ceq '' -and [Convert]::FromBase64String([string]$ref.Key).Length -gt 0) 'Native opaque owner/type/key differs'
 return [string]$ref.Key
}
function State96($Descriptor,[string]$Run){
 Closed96 $Descriptor @('file','native_state_sha256');$script:Sequence96++
 $file='spline-native96-'+$script:Sequence96.ToString('D3')+'.json'
 Require96 ($Descriptor.file -ceq $file -and $Descriptor.native_state_sha256 -cmatch '^[A-F0-9]{64}$' -and $script:Used96.Add($file)) 'Raw sidecar ordinal/hash/uniqueness differs'
 $relative='worker/'+$file;$raw=Read96 $relative;Closed96 $raw @('run_id','native_state')
 $text=[IO.File]::ReadAllText((Join-Path $script:Root96 $relative));$token=[Newtonsoft.Json.Linq.JObject]::Parse($text)
 $actualHash=CompactHash96 ($token['native_state'].ToString([Newtonsoft.Json.Formatting]::None))
 Require96 ($actualHash -ceq $Descriptor.native_state_sha256 -and $raw.run_id -ceq $Run -and $raw.native_state.run_id -ceq $Run -and $raw.native_state.owned_pid -eq $script:Pid96 -and $raw.native_state.readonly_native_revision_dirty_preserved -eq $true) 'Raw compact SHA/read-only/run/process binding differs'
 $docs=Map96 $raw.native_state.documents 'document_id';Require96 ($docs.Count -eq 2) 'Raw native document count differs'
 foreach($id in $script:Documents96.Keys){
  Require96 ($docs.ContainsKey($id)) 'Raw native document missing';$d=$docs[$id];$owned=$script:Documents96[$id]
  Require96 ($d.internal_name -ceq $owned.internal_name -and [string]::Equals([IO.Path]::GetFullPath([string]$d.path),[IO.Path]::GetFullPath([string]$owned.saved_path),[StringComparison]::OrdinalIgnoreCase) -and $d.file_sha256 -ceq $owned.baseline_file_sha256 -and $d.baseline_file_unchanged -eq $true) 'Raw owned path/internal name/disk baseline differs'
 }
 $script:Files96.Add($relative);return $raw.native_state
}
function Content96($State,[string]$Document){
 $copy=Clone96 $State
 foreach($doc in $copy.documents){if($doc.document_id -ceq $Document){[void]$doc.Remove('dirty');[void]$doc.Remove('database_revision_id')}}
 return $copy
}
function NativeKeys96($State,[string]$Document){
 $keys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 foreach($doc in $State.documents){
  if($doc.document_id -cne $Document){continue} # Native keys are document-scoped.
  foreach($sketch in $doc.sketches){[void]$keys.Add([string]$sketch.key);foreach($group in @('entities','texts','dimensions','constraints')){foreach($row in $sketch[$group]){[void]$keys.Add([string]$row.key)}}}
 }
 return ,$keys
}
function Curve96($Actual,$Oracle,$Row,$Existing){
 $control=$Row.variant -ceq 'spline_control';$nativeType=if($control){'kSketchControlPointSplineObject'}else{'kSketchSplineObject'}
 Require96 ($Actual.key -is [string] -and $Oracle.key -is [string] -and $Actual.key -cne $Oracle.key -and -not $Existing.Contains([string]$Actual.key) -and -not $Existing.Contains([string]$Oracle.key)) 'Curve native identity was reused or aliases oracle'
 foreach($field in @('native_type','construction','reference','constraint_status','closed','fit_method','tension')){
  Require96 ($Actual.Contains($field) -eq $Oracle.Contains($field)) 'Native property presence differs';if($Actual.Contains($field)){Require96 (Equal96 $Actual[$field] $Oracle[$field]) 'Native oracle flags/closure/method/tension differ'}
 }
 Require96 ($Actual.native_type -ceq $nativeType -and $Actual.closed -is [bool] -and $Actual.closed -eq $Row.closed -and $Actual.construction -eq $false -and $Actual.reference -eq $false -and $Actual.constraint_status -notmatch '(?i)Over|Unknown') 'Native spline type/closure/flags/solver differs'
 if($control){Require96 (-not $Actual.Contains('fit_method') -and -not $Actual.Contains('tension')) 'CV spline pretends to expose fit options'}else{Require96 ($Actual.fit_method -eq $Row.fit_method -and [double]::IsFinite([double]$Actual.tension)) 'Fit method/tension differs'}
 foreach($curve in @($Actual,$Oracle)){
  Require96 ($curve.domain.Count -eq 2 -and [double]::IsFinite([double]$curve.domain[0]) -and [double]::IsFinite([double]$curve.domain[1]) -and [double]$curve.domain[1] -gt [double]$curve.domain[0] -and $curve.samples_mm.Count -eq 202 -and $curve.strokes_mm.Count -ge 4 -and $curve.strokes_mm.Count -le 16384 -and $curve.strokes_mm.Count%2 -eq 0) 'Native evaluator domain/sample/stroke shape differs'
  $samples=$curve.samples_mm;$n=$samples.Count;$distance=[Math]::Sqrt([Math]::Pow([double]$samples[0]-[double]$samples[$n-2],2)+[Math]::Pow([double]$samples[1]-[double]$samples[$n-1],2))
  Require96 ([double]::IsFinite($distance) -and $(if($Row.closed){$distance -le 1e-5}else{$distance -gt 1e-5})) 'Native evaluator endpoint closure contradicts requested Closed'
 }
 Near96 $Actual.domain $Oracle.domain 1e-10;Near96 $Actual.samples_mm $Oracle.samples_mm;Near96 $Actual.strokes_mm $Oracle.strokes_mm
 Require96 ($Actual.points.Count -eq $Oracle.points.Count -and $Actual.points.Count -ge $Row.input_count -and $Actual.points.Count -le $Row.input_count+1) 'Typed fit/control point cardinality differs'
 # Both native spline kinds may append the closing fit/control point. The
 # independent oracle, coordinates and alias topology remain mandatory below.
 if(-not $Row.closed){Require96 ($Actual.points.Count -eq $Row.input_count) 'Open point cardinality differs from native input'}
 $actualKeys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$oracleKeys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 for($i=0;$i -lt $Actual.points.Count;$i++){
  $a=$Actual.points[$i];$b=$Oracle.points[$i];Closed96 $a @('key','xy');Closed96 $b @('key','xy')
  Require96 ($a.key -is [string] -and $b.key -is [string] -and -not [string]::IsNullOrEmpty($a.key) -and -not [string]::IsNullOrEmpty($b.key) -and -not $Existing.Contains([string]$a.key) -and -not $Existing.Contains([string]$b.key) -and $a.key -cne $Actual.key -and $b.key -cne $Oracle.key) 'Native fit/control point aliases old geometry or curve'
  [void]$actualKeys.Add([string]$a.key);[void]$oracleKeys.Add([string]$b.key);Near96 $a.xy $b.xy
  # The native fit solver may approximate the requested input (also observed
  # in the independent direct-native oracle). Do not invent exact interpolation
  # semantics. Typed fit points still match that oracle at 1e-5mm above.
  if($control){$index=if($i -lt $Row.input_count){$i}else{0};Near96 $a.xy @(($index*10d),([Math]::Sin($index*.7)*10))}
  for($j=0;$j -lt $i;$j++){Require96 (($a.key -ceq $Actual.points[$j].key) -eq ($b.key -ceq $Oracle.points[$j].key)) 'Native closed-point alias topology differs from independent oracle'}
 }
 foreach($key in $actualKeys){Require96 (-not $oracleKeys.Contains($key)) 'Product and independent oracle share a native point'}
}
function Verify96([string]$Root){
 $script:Root96=[IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar);$script:Sequence96=0;$script:Used96=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$script:Files96=[Collections.Generic.List[string]]::new()
 $wrapper=Read96 'wrapper-result.json';$ready=Read96 'worker/ready.json';$stopped=Read96 'worker/stopped.json';$report=Read96 'worker/spline-closure96.json';$cleanup=Read96 'cleanup-verification-r1.json';$script:Pid96=$ready.owned_pid
 Require96 ($wrapper.exit_code -eq 0 -and $wrapper.worker_exit_code -eq 0 -and $wrapper.worker_exited -eq $true -and $wrapper.manifest_restored -eq $true -and $wrapper.original_dll_unchanged -eq $true -and $wrapper.readonly_inspection -eq $true -and $wrapper.cases -eq 64 -and $wrapper.refusals -eq 14 -and $wrapper.raw_states -eq 284 -and $wrapper.status -ceq 'PASS_PLUGIN_NATIVE_SPLINE_CLOSURE_SUBSET' -and $wrapper.full_s1_pass -eq $false -and $wrapper.open_s1_gates -eq 18) 'Terminal wrapper/subset scope differs'
 $run=[string]$wrapper.run_id;Require96 ($run -cmatch '^[0-9a-f]{32}$' -and $ready.status -ceq 'ready' -and $ready.target_id -ceq ('inventor-2027-'+$ready.owned_pid) -and $ready.owned_pid -notin @(91056,77764) -and $ready.http_started -eq $false -and $ready.bootstrap_exposed -eq $false) 'Owned NamedPipe-only target binding differs'
 foreach($row in @($ready,$stopped,$report)){Require96 ($row.run_id -ceq $run -and $row.target_id -ceq $ready.target_id) 'Report run/target differs'}
 Require96 ($stopped.status -ceq 'stopped' -and $stopped.exit_code -eq 0 -and $stopped.owned_pid -eq $ready.owned_pid -and $stopped.owned_start_utc_ticks -eq $ready.owned_start_utc_ticks -and $stopped.owned_final_empty -eq $true -and $stopped.owned_pid_exited -eq $true -and $stopped.existing_processes_preserved -eq $true -and $stopped.http_stopped -eq $true) 'Owned normal cleanup incomplete'
 $pre=Map96 $ready.existing_processes_before 'pid';$post=Map96 $stopped.existing_processes_after 'pid'
 Require96 (-not $pre.ContainsKey([string]$ready.owned_pid) -and (Equal96 $ready.existing_processes_before $stopped.existing_processes_before)) 'Owned process was preexisting'
 foreach($id in $pre.Keys){Require96 ($post.ContainsKey($id) -and $post[$id].start_utc_ticks -eq $pre[$id].start_utc_ticks) 'Preexisting PID/start identity changed'}
 if($post.ContainsKey([string]$ready.owned_pid)){Require96 ($post[[string]$ready.owned_pid].start_utc_ticks -eq $ready.owned_start_utc_ticks) 'Residual inventory PID reused'}
 Closed96 $cleanup @('schema','run_id','owned_pid','owned_start_utc_ticks','owned_pid_absent','checked_utc','check');$checked=[DateTimeOffset]::MinValue
 Require96 ($cleanup.schema -eq 1 -and $cleanup.run_id -ceq $run -and $cleanup.owned_pid -eq $ready.owned_pid -and $cleanup.owned_start_utc_ticks -eq $ready.owned_start_utc_ticks -and $cleanup.owned_pid_absent -eq $true -and [DateTimeOffset]::TryParse([string]$cleanup.checked_utc,[ref]$checked) -and $checked.Offset -eq [TimeSpan]::Zero -and $checked.UtcDateTime -ge (Get-Item -LiteralPath (Join-Path $script:Root96 'wrapper-result.json')).LastWriteTimeUtc) 'Independent later OS absence proof unavailable'
 Require96 ($wrapper.stage_dll_sha256 -cmatch '^[A-F0-9]{64}$' -and $ready.loaded_addin.pid -eq $ready.owned_pid -and $ready.loaded_addin.sha256 -ceq $wrapper.stage_dll_sha256 -and $ready.loaded_addin.verification -ceq 'owned_process_module_path_plus_disk_sha256' -and $ready.loaded_addin.memory_bytes_hashed -eq $false) 'Loaded96 candidate binding differs'
 foreach($install in @($ready.installation_ready,$stopped.installation_after)){Require96 ($install.unchanged -eq $true -and $install.stage_addin_sha256 -ceq $wrapper.stage_dll_sha256 -and $install.original_installed_dll_sha256 -ceq $wrapper.original_dll_sha256) 'Stage/original DLL metadata differs'}
 Require96 ($report.schema -eq 1 -and $report.status -ceq 'PASS_PLUGIN_NATIVE_SPLINE_CLOSURE_SUBSET' -and $report.cases.Count -eq 64 -and $report.refusals.Count -eq 14 -and $report.raw_state_count -eq 284 -and $report.quest_proof -eq $false -and $report.full_s1_pass -eq $false) 'Native terminal matrix/subset claim differs'
 $script:Documents96=Map96 $ready.owned_documents 'document_id';Require96 ($script:Documents96.Count -eq 2 -and $script:Documents96.ContainsKey([string]$ready.part_document_id) -and $script:Documents96.ContainsKey([string]$ready.sheet_document_id)) 'Owned documents missing'
 foreach($doc in $script:Documents96.Values){Require96 ($doc.document_id -ceq ('doc_'+$doc.internal_name) -and [IO.Path]::IsPathFullyQualified([string]$doc.saved_path) -and [string]::Equals([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath([string]$doc.saved_path)),[IO.Path]::GetFullPath([string]$ready.fixture_directory),[StringComparison]::OrdinalIgnoreCase)) 'Owned saved document root/identity differs'}
 $refusalKinds=@('closed_string','closed_number','closed_null','bad_fit_method','unknown_tension','bad_coordinate','failure_after_valid_closed');$seenRef=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 foreach($refusal in $report.refusals){
  Require96 ($refusal.context -cin @('Part','Sheet') -and $refusal.kind -cin $refusalKinds -and $seenRef.Add($refusal.context+'|'+$refusal.kind) -and $refusal.event_revision_preserved -eq $true -and $refusal.full_native_rollback_exact -eq $true -and $refusal.details.step_code -ceq 'INVALID_ARGUMENT' -and $refusal.details.step_index -eq $(if($refusal.kind -ceq 'failure_after_valid_closed'){1}else{0})) 'Native refusal kind/step/revision assertion differs'
  $a=State96 $refusal.native_before $run;$b=State96 $refusal.native_after $run;Require96 (Equal96 $a $b) 'Refusal full native rollback differs'
 }
 Require96 ($seenRef.Count -eq 14) 'Refusal matrix incomplete';$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$summary=@()
 foreach($case in $report.cases){
  $control=$case.variant -ceq 'spline_control';$label=$case.context+'|'+$case.variant+'|'+$case.input_count+'|'+$case.closed+'|'+$case.fit_method
  Require96 ($case.context -cin @('Part','Sheet') -and $case.variant -cin @('spline_fit','spline_control') -and $case.input_count -in @(2,3,5,64) -and $case.closed -is [bool] -and $case.fit_method -in $(if($control){@(0)}else{@(26369,26370,26371)}) -and $seen.Add($label) -and $case.status -ceq 'PASS_PLUGIN_NATIVE_SPLINE_CLOSURE_SUBSET' -and $case.case_cleanup_native_contents_exact -eq $true -and $case.first_snapshot_attempts -eq 1) 'Case method/closure/input matrix/status differs'
  $doc=if($case.context -ceq 'Part'){$ready.part_document_id}else{$ready.sheet_document_id}
  $baseline=State96 $case.native_before_case_preparation $run;$before=State96 $case.native_before $run;$preview=State96 $case.native_after_preview $run;$after=State96 $case.native_after_case_cleanup $run
  Require96 ((Equal96 $before $preview) -and (Equal96 (Content96 $baseline $doc) (Content96 $after $doc))) 'Preview exact rollback or case cleanup changed native contents'
  $snapshot=$case.first_snapshot;Require96 ($snapshot.schema_version -eq 1 -and $snapshot.document_id -ceq $doc -and -not [string]::IsNullOrEmpty([string]$snapshot.revision) -and $snapshot.partial -eq $false -and $snapshot.mutation_completeness.state -ceq 'complete' -and $snapshot.mutation_completeness.read_error_count -eq 0 -and $snapshot.sketch_name -ceq 'S1_Spline96_Case') 'ONE first product snapshot owner/completeness differs'
  foreach($collection in @('entities','dimensions','constraints')){Require96 ($snapshot.mutation_completeness.collections[$collection].native_count -eq $snapshot[$collection].Count -and $snapshot.mutation_completeness.collections[$collection].represented_count -eq $snapshot[$collection].Count) 'First product collection coverage differs'}
  $sketch=Opaque96 $snapshot.sketch_id $doc 'sketch';$baseDocs=Map96 $baseline.documents 'document_id';$beforeDocs=Map96 $before.documents 'document_id';$baseSketches=Map96 $baseDocs[$doc].sketches;$prepared=Map96 $beforeDocs[$doc].sketches
  Require96 (-not $baseSketches.ContainsKey($sketch) -and $prepared.ContainsKey($sketch) -and $prepared.Count -eq $baseSketches.Count+1 -and $prepared[$sketch].name -ceq 'S1_Spline96_Case' -and $prepared[$sketch].entities.Count -eq 0 -and $prepared[$sketch].texts.Count -eq 0 -and $prepared[$sketch].dimensions.Count -eq 0 -and $prepared[$sketch].constraints.Count -eq 0 -and $prepared[$sketch].image_count -eq 0) 'Case preparation did not add exactly one empty owned sketch'
  $remove=Clone96 $before;foreach($d in $remove.documents){if($d.document_id -ceq $doc){$d.sketches=@($d.sketches|Where-Object {$_.key -cne $sketch})}}
  Require96 (Equal96 (Content96 $baseline $doc) (Content96 $remove $doc)) 'Preparation changed unrelated native content'
  $existing=NativeKeys96 $before $doc
  try{Curve96 $case.native_committed $case.native_oracle $case $existing}catch{throw ($label+': '+$_.Exception.Message+'; '+$_.ScriptStackTrace)}
  $type=if($control){'control_point_spline'}else{'spline'};$curves=@($snapshot.entities|Where-Object {$_.type -ceq $type})
  $auxiliary=@($snapshot.entities|Where-Object {$_.type -cne 'point' -and $_.type -cne $type})
  Require96 ($curves.Count -eq 1 -and $(if($control){@($auxiliary|Where-Object {$_.type -cne 'line' -or $_.native_type -cne 'kSketchLineObject' -or $_.construction -ne $true -or $_.supported -ne $true}).Count -eq 0 -and $auxiliary.Count -eq $case.native_committed.points.Count-1}else{$auxiliary.Count -eq 0})) 'First product snapshot contains ambiguous/unexpected curve types'
  # Native CV creation also represents its control polygon. Compare the
  # displayed segments with the ordered typed native control points, without
  # inferring native segment ownership from equal coordinates.
  for($segment=0;$segment -lt $auxiliary.Count;$segment++){
   $a=$case.native_committed.points[$segment].xy;$b=$case.native_committed.points[$segment+1].xy
   Near96 $auxiliary[$segment].start_mm $a;Near96 $auxiliary[$segment].end_mm $b
   Near96 $auxiliary[$segment].strokes_mm @($a[0],$a[1],$b[0],$b[1])
  }
  $snapshotKeys=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
  foreach($entity in $snapshot.entities){$key=Opaque96 $entity.id $doc 'sketch_entity';Require96 ($snapshotKeys.TryAdd($key,$entity) -and -not $existing.Contains($key)) 'Product entity duplicates or aliases preexisting native geometry'}
  foreach($point in $case.native_committed.points){Require96 ($snapshotKeys.ContainsKey($point.key) -and $snapshotKeys[$point.key].type -ceq 'point') 'Native typed point absent from first product snapshot';Near96 $snapshotKeys[$point.key].point_mm $point.xy}
  $curve=$curves[0];Require96 ((Opaque96 $curve.id $doc 'sketch_entity') -ceq $case.native_committed.key -and $curve.native_type -ceq $case.native_committed.native_type -and $curve.supported -eq $true -and $curve.closed -is [bool] -and $curve.closed -eq $case.closed -and $curve.construction -is [bool] -and $curve.construction -eq $case.native_committed.construction) 'First product curve native key/type/Closed/construction differs'
  Require96 ($curve.strokes_mm.Count -ge 4 -and $curve.strokes_mm.Count%2 -eq 0) 'First product strokes missing';Near96 $curve.strokes_mm $case.native_committed.strokes_mm
  $countField=if($control){'control_point_count'}else{'fit_point_count'};Require96 ($curve[$countField] -eq $case.native_committed.points.Count) 'Product typed point count differs'
  if(-not $control){Require96 ($curve.fit_method -eq $case.fit_method) 'Product fit method differs'}
  $summary+=@{context=$case.context;variant=$case.variant;input_count=$case.input_count;closed=$case.closed;fit_method=$case.fit_method;native_oracle_recomputed=$true;preview_full_native_exact=$true;case_cleanup_contents_exact=$true;one_first_product_snapshot=$true}
 }
 Require96 ($seen.Count -eq 64 -and $script:Sequence96 -eq 284 -and @(Get-ChildItem -LiteralPath (Join-Path $script:Root96 'worker') -File -Filter 'spline-native96-*.json').Count -eq 284) 'Case/raw sidecar inventory incomplete'
 $files=@('wrapper-result.json','worker/ready.json','worker/stopped.json','worker/spline-closure96.json','cleanup-verification-r1.json')+@($script:Files96)
 return [ordered]@{schema=1;status='VERIFIED_PLUGIN_NATIVE_SPLINE_CLOSURE96_SUBSET';run_id=$run;candidate_sha256=$wrapper.stage_dll_sha256;cases=$summary;case_count=64;refusals=14;raw_states=284;post_wrapper_owned_absence=$true;core_https_proof=$false;quest_proof=$false;physical_proof=$false;full_s1_pass=$false;open_s1_gates=18;files=@($files|ForEach-Object {@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $script:Root96 $_)).Hash}});limits=@('Offline archived-state consistency; native attribution relies on loaded-module and producer evidence','Native plugin NamedPipe end-to-end only, not Core/HTTPS/Quest','Preview event revision and ROLLED_BACK code are producer assertions: individual preview revision values/code are not archived; refusal step and revision-preserved flag checked','Only target document Dirty/DBRevision excluded for preparation/cleanup; all preview/refusal contents serialized exactly','Source/oracle curves and typed point identities disjoint; closure alias topology compared with oracle, no invented point-owner API','Geometry1e-5mm and domain1e-10; no full NURBS-definition, editing/associativity/reopen or full S1 claim','Hashes attest bytes/consistency, not independent authenticity')}
}
function SelfTest96([string]$Original,$Verified){
 $tempBase=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar);$leaf='s1-spline96-offline-'+[Guid]::NewGuid().ToString('N');$temp=Join-Path $tempBase $leaf
 [void](New-Item -ItemType Directory -Path $temp);$results=@()
 try{
  foreach($name in @('native_closed_flip','native_curve_geometry','old_native_point','unrelated_parameter','preview_dirty','first_snapshot_closed','first_snapshot_construction','refusal_step')){
   $caseRoot=Join-Path $temp $name;[void](New-Item -ItemType Directory -Path $caseRoot)
   foreach($entry in $Verified.files){
    $relative=[string]$entry.path;$source=[IO.Path]::GetFullPath((Join-Path $Original $relative));$destination=[IO.Path]::GetFullPath((Join-Path $caseRoot $relative))
    Require96 ($source.StartsWith($Original+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and $destination.StartsWith($caseRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'Selftest path escaped owned corpus'
    $parent=Split-Path -Parent $destination;if(-not (Test-Path -LiteralPath $parent)){[void](New-Item -ItemType Directory -Path $parent)};Copy-Item -LiteralPath $source -Destination $destination
   }
   $script:Root96=$caseRoot;$report=Read96 'worker/spline-closure96.json';$case=$report.cases[0];$descriptor=$null;$raw=$null
   switch($name){
    'native_closed_flip'{$case.native_committed.closed=-not $case.native_committed.closed}
    'native_curve_geometry'{$case.native_committed.samples_mm[2]=[double]$case.native_committed.samples_mm[2]+1}
    'old_native_point'{
     $state=Read96 ('worker/'+$case.native_before.file);$oldPoints=@($state.native_state.documents|ForEach-Object {$_.sketches}|ForEach-Object {$_.entities}|Where-Object {$_.native_type -ceq 'kSketchPointObject'})
     Require96 ($oldPoints.Count -gt 0) 'Selftest old point fixture unavailable';$case.native_committed.points[0].key=$oldPoints[0].key
    }
    'unrelated_parameter'{
     $descriptor=$case.native_after_case_cleanup;$raw=Read96 ('worker/'+$descriptor.file);Require96 ($raw.native_state.documents[0].parameters.Count -gt 0) 'Selftest unrelated parameter unavailable'
     $raw.native_state.documents[0].parameters[0].expression='tampered-independent-parameter'
    }
    'preview_dirty'{$descriptor=$case.native_after_preview;$raw=Read96 ('worker/'+$descriptor.file);$raw.native_state.documents[0].dirty=-not $raw.native_state.documents[0].dirty}
    'first_snapshot_closed'{$curve=@($case.first_snapshot.entities|Where-Object {$_.type -ceq 'spline'})[0];$curve.closed=-not $curve.closed}
    'first_snapshot_construction'{$curve=@($case.first_snapshot.entities|Where-Object {$_.type -ceq 'spline'})[0];$curve.construction=-not $curve.construction}
    'refusal_step'{$report.refusals[0].details.step_index=1}
   }
   $updated=$false
   if($null -ne $raw){
    $descriptor.native_state_sha256=CompactHash96 (Json96 $raw.native_state)
    [IO.File]::WriteAllText((Join-Path $caseRoot ('worker/'+$descriptor.file)),(Json96 $raw),[Text.UTF8Encoding]::new($false));$updated=$true
   }
   [IO.File]::WriteAllText((Join-Path $caseRoot 'worker/spline-closure96.json'),(Json96 $report),[Text.UTF8Encoding]::new($false))
   $rejected=$false;$reason='';try{[void](Verify96 $caseRoot)}catch{$rejected=$true;$reason=$_.Exception.Message}
   Require96 ($rejected -and $reason -notmatch 'compact SHA|later OS absence|Terminal wrapper') ('Semantic tamper was accepted or failed unrelated metadata: '+$name)
   $results+=@{name=$name;status='REJECTED_AS_EXPECTED';raw_compact_sha_updated=$updated;embedded_report_only=($null -eq $raw);assertion=$reason}
  }
 }finally{
  $resolved=[IO.Path]::GetFullPath($temp)
  Require96 ([IO.Path]::GetDirectoryName($resolved) -ceq $tempBase -and [IO.Path]::GetFileName($resolved) -ceq $leaf -and $resolved -cne $Original -and ((Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'Selftest cleanup target differs'
  Require96 (@(Get-ChildItem -LiteralPath $resolved -Recurse -Force|Where-Object {($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0}).Count -eq 0) 'Selftest cleanup reparse directory refused'
  Remove-Item -LiteralPath $resolved -Recurse -Force;$script:Root96=$Original
 }
 return ,$results
}
$root=[IO.Path]::GetFullPath($EvidenceDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
Require96 ((Test-Path -LiteralPath $root -PathType Container) -and [IO.Path]::GetFileName($root) -cmatch '^native-r[1-9][0-9]*$' -and [IO.Path]::GetFileName([IO.Path]::GetDirectoryName($root)) -ceq 'increment96' -and ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'Only archived increment96/native-rN terminal evidence is accepted'
# Pure JSON serialization only: never load a native Inventor/plugin/host assembly.
if($null -eq [Type]::GetType('Newtonsoft.Json.Linq.JToken, Newtonsoft.Json',$false)){
 $jsonDll=Join-Path $root 'bin/Newtonsoft.Json.dll';Require96 ((Test-Path -LiteralPath $jsonDll -PathType Leaf) -and ((Get-Item -LiteralPath $jsonDll).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'Json.NET pure offline dependency unavailable'
 [void][Reflection.Assembly]::LoadFrom($jsonDll)
}
$output=Join-Path $root $OutputName;if(-not $ValidateOnly){Require96 (-not (Test-Path -LiteralPath $output)) 'Verification output exists; use a new output name'}
$result=Verify96 $root;if($SelfTest){$result.selftests=SelfTest96 $root $result};$result.verifier_sha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash
if($ValidateOnly){$result|ConvertTo-Json -Depth 12;exit 0}
$stream=[IO.FileStream]::new($output,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes(($result|ConvertTo-Json -Depth 12));$stream.Write($bytes);$stream.Flush($true)}finally{$stream.Dispose()}
Write-Output $output
