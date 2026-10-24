[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
 [ValidatePattern('^verification-spline-tension97-[a-zA-Z0-9-]+\.json$')][string]$OutputName='verification-spline-tension97-r1.json',
 [switch]$SelfTest,
 [switch]$ValidateOnly
)
$ErrorActionPreference='Stop'
$script:Candidate97='2AF7E77A8A34EA82052AD84445398CDFA6858A2E9BC2AF13A6B577B6A13D15E0'
$script:JsonOptions97=@{AsHashtable=$true;Depth=100}
if((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')){$script:JsonOptions97.DateKind='String'}
# OFFLINE ONLY. Never load Inventor, plugin or worker assemblies, execute their
# code, query processes, contact a target, repair native data or rewrite evidence.
function Require97([bool]$Value,[string]$Assertion){if(-not $Value){throw [InvalidOperationException]::new($Assertion)}}
function Json97($Value){ConvertTo-Json -InputObject $Value -Compress -Depth 100}
function Equal97($A,$B){[string]::Equals((Json97 $A),(Json97 $B),[StringComparison]::Ordinal)}
function Integer97($Value){return ($Value -is [int] -or $Value -is [long]) -and $Value -ge [int]::MinValue -and $Value -le [int]::MaxValue}
function Number97($Value){return ($Value -is [int] -or $Value -is [long] -or $Value -is [double] -or $Value -is [decimal]) -and [double]::IsFinite([double]$Value)}
function True97($Value){return $Value -is [bool] -and $Value}
function False97($Value){return $Value -is [bool] -and -not $Value}
function Closed97($Value,[string[]]$Required,[string[]]$Optional=@()){
 Require97 ($Value -is [Collections.IDictionary]) 'JSON object required'
 foreach($field in $Required){Require97 ($Value.Contains($field)) ('Required field missing: '+$field)}
 foreach($field in $Value.Keys){Require97 ($field -cin $Required -or $field -cin $Optional) ('Unexpected field: '+$field)}
}
function Map97($Rows,[string]$Key='key'){
 $map=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
 foreach($row in $Rows){Require97 ($row -is [Collections.IDictionary] -and -not [string]::IsNullOrEmpty([string]$row[$Key]) -and $map.TryAdd([string]$row[$Key],$row)) 'Missing or duplicate scoped native identity'}
 return ,$map
}
function Key97($Value){
 Require97 ($Value -is [string] -and $Value.Length -gt 0 -and $Value.Length -le 87384) 'Bounded native key required'
 $bytes=[Convert]::FromBase64String($Value)
 Require97 ($bytes.Length -gt 0 -and $bytes.Length -le 65536 -and [Convert]::ToBase64String($bytes) -ceq $Value) 'Native key is not canonical Base64'
}
function Path97([string]$Relative){
 Require97 (-not [IO.Path]::IsPathRooted($Relative)) 'Absolute evidence path refused'
 $path=[IO.Path]::GetFullPath((Join-Path $script:Root97 $Relative))
 Require97 ($path.StartsWith($script:Root97+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'Evidence path escapes root'
 $walk=$path
 while($walk -cne $script:Root97){
  $item=Get-Item -LiteralPath $walk
  Require97 (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'Evidence reparse path refused'
  $walk=[IO.Path]::GetDirectoryName($walk)
 }
 return $path
}
function Text97([string]$Relative){
 $path=Path97 $Relative;$file=Get-Item -LiteralPath $path
 Require97 (-not $file.PSIsContainer -and $file.Length -gt 0 -and $file.Length -le 67108864) 'Bounded regular evidence file required'
 [void]$script:Files97.Add($Relative)
 if($script:Overlay97.ContainsKey($Relative)){return [string]$script:Overlay97[$Relative]}
 return [IO.File]::ReadAllText($path)
}
function Token97([string]$Relative){
 if(-not $script:Tokens97.ContainsKey($Relative)){
  $settings=[Newtonsoft.Json.Linq.JsonLoadSettings]::new();$settings.DuplicatePropertyNameHandling=[Newtonsoft.Json.Linq.DuplicatePropertyNameHandling]::Error
  $script:Tokens97.Add($Relative,[Newtonsoft.Json.Linq.JObject]::Parse((Text97 $Relative),$settings))
 }
 return ,$script:Tokens97[$Relative]
}
function Read97([string]$Relative){(Text97 $Relative)|ConvertFrom-Json @script:JsonOptions97}
function Compact97($Token){return $Token.ToString([Newtonsoft.Json.Formatting]::None)}
function HashText97([string]$Text){[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($Text)))}
function HashFile97([string]$Relative){
 if($script:Overlay97.ContainsKey($Relative)){return HashText97 ([string]$script:Overlay97[$Relative])}
 (Get-FileHash -LiteralPath (Path97 $Relative) -Algorithm SHA256).Hash
}
function FiniteArray97($Values,[int]$Minimum,[int]$Maximum){
 Require97 ($Values -is [Collections.IList] -and $Values.Count -ge $Minimum -and $Values.Count -le $Maximum) 'Native numeric array shape differs'
 foreach($value in $Values){Require97 (Number97 $value) 'Native numeric array contains nonfinite/non-numeric data'}
}
function ExistingKeys97($State,[string]$Document){
 $keys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 # Reference keys are scoped to ONE document. Equal bytes in the other
 # document are not an alias; never union Part and Sheet key namespaces.
 foreach($doc in $State.documents){if($doc.document_id -cne $Document){continue};foreach($sketch in $doc.sketches){
  [void]$keys.Add([string]$sketch.key)
  foreach($group in @('entities','texts','dimensions','constraints')){foreach($entity in $sketch[$group]){[void]$keys.Add([string]$entity.key)}}
 }}
 return ,$keys
}
function Curve97($Curve,$Existing){
 Closed97 $Curve @('key','native_type','fit_method','fit_method_name','tension','closed','construction','reference','constraint_status','fit_points','domain','samples_mm','stroke_tolerance_mm','strokes_mm')
 Key97 $Curve.key
 Require97 ($Curve.native_type -ceq 'kSketchSplineObject' -and (Integer97 $Curve.fit_method) -and (Integer97 $Curve.tension) -and $Curve.closed -is [bool] -and $Curve.construction -is [bool] -and $Curve.reference -is [bool] -and $Curve.constraint_status -is [string]) 'Recorded native spline scalar schema differs'
 $names=@{26369='kSmoothSplineFit';26370='kSweetSplineFit';26371='kACADSplineFit'}
 $methodName=if($names.ContainsKey([int]$Curve.fit_method)){$names[[int]$Curve.fit_method]}else{([int]$Curve.fit_method).ToString([Globalization.CultureInfo]::InvariantCulture)}
 Require97 ($Curve.fit_method_name -ceq $methodName) 'Native fit-method name contradicts its value'
 Require97 (-not $Existing.Contains([string]$Curve.key)) 'Fresh native curve reused a baseline identity in its document'
 Require97 ($Curve.fit_points -is [Collections.IList] -and $Curve.fit_points.Count -ge 2 -and $Curve.fit_points.Count -le 64) 'Recorded fit-point cardinality differs'
 for($i=0;$i -lt $Curve.fit_points.Count;$i++){
  $point=$Curve.fit_points[$i];Closed97 $point @('index','key','xy_mm');Key97 $point.key;FiniteArray97 $point.xy_mm 2 2
  Require97 ($point.index -eq $i+1 -and $point.key -cne $Curve.key -and -not $Existing.Contains([string]$point.key)) 'Fit-point index or document-scoped identity differs'
 }
 # Closed native curves may repeat/alias their closing point. Do not invent
 # all-distinct fit-point identities or exact equality with constructor input.
 FiniteArray97 $Curve.domain 2 2;FiniteArray97 $Curve.samples_mm 202 202;FiniteArray97 $Curve.strokes_mm 4 16384
 Require97 ([double]$Curve.domain[1] -gt [double]$Curve.domain[0] -and $Curve.strokes_mm.Count%2 -eq 0 -and (Number97 $Curve.stroke_tolerance_mm) -and [double]$Curve.stroke_tolerance_mm -eq .001) 'Native evaluator domain/stroke schema differs'
}
function State97($State,[string]$Document){
 Require97 ((Integer97 $State.schema) -and $State.schema -eq 1 -and $State.run_id -ceq $script:Run97 -and $State.owned_pid -eq $script:Pid97 -and $State.active_document_id -ceq $Document -and (True97 $State.readonly_native_revision_dirty_preserved)) 'Native full-state run/PID/active owner differs'
 $docs=Map97 $State.documents 'document_id';$styles=Map97 $State.local_text_styles88 'document_id';$splines=Map97 $State.spline_definitions97 'document_id'
 Require97 ($docs.Count -eq 2 -and $styles.Count -eq 2 -and $splines.Count -eq 2) 'Native full-state document/style/spline coverage differs'
 foreach($id in $script:Documents97.Keys){
  Require97 ($docs.ContainsKey($id) -and $styles.ContainsKey($id) -and $splines.ContainsKey($id)) 'Native full-state owner missing'
  $doc=$docs[$id];$owner=$script:Documents97[$id]
  Require97 ($doc.internal_name -ceq $owner.internal_name -and $doc.document_id -ceq $owner.document_id -and $doc.context -ceq $owner.context -and [string]::Equals([IO.Path]::GetFullPath([string]$doc.path),[IO.Path]::GetFullPath([string]$owner.saved_path),[StringComparison]::OrdinalIgnoreCase) -and $doc.file_sha256 -ceq $owner.baseline_file_sha256 -and (True97 $doc.baseline_file_unchanged) -and $doc.dirty -is [bool] -and -not [string]::IsNullOrEmpty([string]$doc.database_revision_id)) 'Native full-state saved owner/Dirty/DBRevision/disk baseline differs'
  [void](Map97 $doc.parameters);[void](Map97 $styles[$id].styles)
  $sketches=Map97 $doc.sketches;$splineSketches=Map97 $splines[$id].sketches 'sketch_key'
  Require97 ($sketches.Count -eq $splineSketches.Count) 'Full-state spline definitions omit a native sketch'
  foreach($key in $sketches.Keys){
   Require97 ($splineSketches.ContainsKey($key)) 'Full-state spline sketch key differs';Key97 $key
   $sketch=$sketches[$key];$definitions=$splineSketches[$key]
   $native=Map97 $sketch.entities;$curves=Map97 $definitions.splines
   Require97 ($definitions.spline_count -eq $curves.Count -and @($sketch.entities|Where-Object {$_.native_type -ceq 'kSketchSplineObject'}).Count -eq $curves.Count) 'Full-state native spline counts differ'
   foreach($curve in $curves.Values){
    Require97 ($native.ContainsKey([string]$curve.key) -and $native[[string]$curve.key].native_type -ceq 'kSketchSplineObject') 'Native spline definition owner/type differs'
    $empty=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);Curve97 $curve $empty
   }
  }
 }
}
function Flags97($Row){
 if($Row.Contains('after_update')){
  Require97 ($Row.Contains('tension_readback_matches_request') -and $Row.tension_readback_matches_request -is [bool] -and $Row.tension_readback_matches_request -eq ($Row.after_update.tension -eq $Row.requested_tension)) 'Computed tension readback flag contradicts actual native snapshot'
 }else{Require97 (-not $Row.Contains('tension_readback_matches_request')) 'Tension match flag exists without its native readback'}
 if($Row.Contains('after_update')){
  Require97 ($Row.Contains('before_tension') -and $Row.Contains('after_tension') -and $Row.fit_method_changed_immediately -is [bool] -and $Row.fit_method_changed_immediately -eq ($Row.before_tension.fit_method -ne $Row.after_tension.fit_method)) 'Computed method-change flag contradicts recorded native snapshots'
 }else{Require97 (-not $Row.Contains('fit_method_changed_immediately')) 'Method-change flag exists before producer computes it'}
}
function Error97($Failure,[string[]]$Stages,[switch]$Readback){
 Closed97 $Failure @('stage','type','hresult','hresult_hex')
 $typeValid=if($Readback){$Failure.type -is [string] -and $Failure.type.Length -le 512 -and $Failure.type -cmatch '^[A-Za-z_][A-Za-z0-9_.+`]*$'}else{$Failure.type -ceq 'System.Runtime.InteropServices.COMException'}
 Require97 ($Failure.stage -cin $Stages -and $typeValid -and (Integer97 $Failure.hresult) -and $Failure.hresult -lt 0) 'Partial API observation error stage/COM type/HRESULT differs'
 $unsigned=[uint32]([long]$Failure.hresult -band 4294967295L)
 Require97 ($Failure.hresult_hex -ceq ('0x'+$unsigned.ToString('X8'))) 'Native HRESULT hex contradicts signed value'
}
function Row97($Row,$Sidecar,$Before){
 $base=@('number','context','document_id','requested_fit_method','requested_closed','requested_tension','status','setter_returned','solve_calls','update_calls','sidecar','outcome','abort_returned','full_native_abort_exact','sidecar_file_sha256')
 $optional=@('after_add','before_tension','after_tension','after_update','update_returned','fit_method_changed_immediately','tension_readback_matches_request','failure','after_error_readback','after_error_readback_failure')
 Closed97 $Row $base $optional
 Require97 ($Row.status -ceq 'OBSERVATION_ONLY' -and $Row.setter_returned -is [bool] -and (Integer97 $Row.solve_calls) -and (Integer97 $Row.update_calls) -and $Row.solve_calls -in @(0,1) -and $Row.update_calls -in @(0,1) -and (True97 $Row.abort_returned) -and (True97 $Row.full_native_abort_exact)) 'Observation status/call counts/Abort claim differs'
 Flags97 $Row
 $existing=ExistingKeys97 $Before $Row.document_id
 foreach($name in @('after_add','before_tension','after_tension','after_update','after_error_readback')){
  if($Row.Contains($name)){
   Curve97 $Row[$name] $existing
   # This research does not claim key/topology preservation by the setter.
   # Each actual key is retained and scoped against its document baseline.
  }
 }
 if($Row.outcome -cin @('API_COMPLETED_OBSERVATION','NATIVE_UPDATE_RETURNED_FALSE')){
  Require97 (-not $Row.Contains('failure') -and -not $Row.Contains('after_error_readback') -and -not $Row.Contains('after_error_readback_failure') -and (True97 $Row.setter_returned) -and $Row.solve_calls -eq 1 -and $Row.update_calls -eq 1 -and $Row.update_returned -is [bool]) 'Completed native observation contradicts producer stages'
  foreach($name in @('after_add','before_tension','after_tension','after_update')){Require97 ($Row.Contains($name)) 'Completed observation omitted native snapshot'}
  Require97 ($Row.outcome -ceq $(if($Row.update_returned){'API_COMPLETED_OBSERVATION'}else{'NATIVE_UPDATE_RETURNED_FALSE'}) -and $Sidecar.Contains('native_after_attempt')) 'Native Update false/completion outcome differs'
 }else{
  Require97 ($Row.outcome -ceq 'NATIVE_API_ERROR_OBSERVATION' -and $Row.Contains('failure')) 'Unverifiable non-API failure cannot be a terminal observation'
  $stages=@('start_transaction','add_sketch','add_spline','capture_after_add','set_closed','capture_before_tension','set_tension','capture_after_tension','solve_once','update_once','capture_after_update')
  Error97 $Row.failure $stages;$index=[Array]::IndexOf($stages,[string]$Row.failure.stage)
  foreach($capture in @(@('after_add',3),@('before_tension',5),@('after_tension',7))){Require97 ($Row.Contains([string]$capture[0]) -eq ($index -gt [int]$capture[1])) 'Partial API observation captured an impossible stage prefix'}
  Require97 ($Row.setter_returned -eq ($index -gt 6)) 'Partial API setter-returned claim contradicts error stage'
  Require97 ($(if($index -lt 8){$Row.solve_calls -eq 0}elseif($index -gt 8){$Row.solve_calls -eq 1}else{$true}) -and $(if($index -lt 9){$Row.update_calls -eq 0}elseif($index -gt 9){$Row.update_calls -eq 1}else{$true})) 'Partial API Solve/Update count contradicts stage'
  Require97 ($Row.Contains('update_returned') -eq ($index -gt 9) -and (-not $Row.Contains('update_returned') -or $Row.update_returned -is [bool])) 'Partial API update-return presence differs'
  Require97 (-not $Row.Contains('after_update') -or $index -eq 10) 'Partial API after-update snapshot precedes update'
  Require97 (-not $Sidecar.Contains('native_after_attempt')) 'Failed full-state capture cannot contain a completed native_after_attempt'
  if($index -ge 3){Require97 ($Row.Contains('after_error_readback') -xor $Row.Contains('after_error_readback_failure')) 'Partial API fit object lacks explicit after-error observation'}
  else{Require97 (-not $Row.Contains('after_error_readback') -and -not $Row.Contains('after_error_readback_failure')) 'After-error curve exists before native Add returned'}
  if($Row.Contains('after_error_readback_failure')){Error97 $Row.after_error_readback_failure @('capture_after_error') -Readback}
 }
 if($Sidecar.Contains('native_after_attempt')){
  State97 $Sidecar.native_after_attempt $Row.document_id
  $doc=@($Sidecar.native_after_attempt.spline_definitions97|Where-Object {$_.document_id -ceq $Row.document_id})[0]
  $nativeMatches97=@($doc.sketches|ForEach-Object {$_.splines}|Where-Object {$_.key -ceq $Row.after_update.key})
  Require97 ($nativeMatches97.Count -eq 1 -and (Equal97 $nativeMatches97[0] $Row.after_update)) 'Native after-attempt spline contradicts recorded final readback'
 }
}
function Verify97([string]$Root,$Overlay=@{}){
 $script:Root97=[IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar);$script:Overlay97=$Overlay
 $script:Files97=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$script:Tokens97=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
 $wrapper=Read97 'wrapper-result.json';$ready=Read97 'worker/ready.json';$stopped=Read97 'worker/stopped.json';$report=Read97 'worker/spline-tension97.json';$cleanup=Read97 'cleanup-verification-r1.json'
 Require97 ($wrapper.exit_code -eq 0 -and $wrapper.worker_exit_code -eq 0 -and (True97 $wrapper.worker_exited) -and (True97 $wrapper.manifest_restored) -and (True97 $wrapper.original_dll_unchanged) -and (True97 $wrapper.readonly_inspection) -and (True97 $wrapper.owned_pid_exited) -and (True97 $wrapper.preexisting_pid_start_preserved) -and $wrapper.cases -eq 60 -and $wrapper.exact_aborts -eq 60 -and $wrapper.sidecars_bound_and_hashed -eq 60 -and $wrapper.status -ceq 'OBSERVATION_ONLY') 'Terminal wrapper observations/cleanup differs'
 Require97 ((False97 $wrapper.full_s1_pass) -and (False97 $wrapper.quest_proof) -and (False97 $wrapper.physical_proof) -and $wrapper.open_s1_gates -eq 18) 'Wrapper overclaims product/device scope'
 $script:Run97=[string]$wrapper.run_id;$script:Pid97=$ready.owned_pid
 Require97 ($script:Run97 -cmatch '^[0-9a-f]{32}$' -and (Integer97 $script:Pid97) -and $script:Pid97 -gt 0 -and $script:Pid97 -notin @(91056,77764) -and $ready.owned_start_utc_ticks -gt 0 -and $ready.status -ceq 'ready' -and $ready.target_id -ceq ('inventor-2027-'+$script:Pid97) -and (False97 $ready.http_started) -and (False97 $ready.bootstrap_exposed)) 'Owned native-only target/PID/start differs'
 foreach($value in @($ready,$stopped,$report)){Require97 ($value.run_id -ceq $script:Run97 -and $value.target_id -ceq $ready.target_id) 'Native report run/target differs'}
 Require97 ($stopped.status -ceq 'stopped' -and $stopped.exit_code -eq 0 -and $stopped.owned_pid -eq $script:Pid97 -and $stopped.owned_start_utc_ticks -eq $ready.owned_start_utc_ticks -and (True97 $stopped.owned_final_empty) -and (True97 $stopped.owned_pid_exited) -and (True97 $stopped.existing_processes_preserved) -and (True97 $stopped.http_stopped)) 'Owned normal cleanup incomplete'
 $pre=Map97 $ready.existing_processes_before 'pid';$post=Map97 $stopped.existing_processes_after 'pid'
 Require97 (-not $pre.ContainsKey([string]$script:Pid97) -and (Equal97 $ready.existing_processes_before $stopped.existing_processes_before)) 'Owned process was preexisting'
 foreach($id in $pre.Keys){Require97 ($post.ContainsKey($id) -and $post[$id].start_utc_ticks -eq $pre[$id].start_utc_ticks) 'Preexisting PID/start changed'}
 if($post.ContainsKey([string]$script:Pid97)){Require97 ($post[[string]$script:Pid97].start_utc_ticks -eq $ready.owned_start_utc_ticks) 'Residual inventory PID reused'}
 Closed97 $cleanup @('schema','run_id','owned_pid','owned_start_utc_ticks','owned_pid_absent','checked_utc','check');$checked=[DateTimeOffset]::MinValue
 Require97 ((Integer97 $cleanup.schema) -and $cleanup.schema -eq 1 -and $cleanup.run_id -ceq $script:Run97 -and $cleanup.owned_pid -eq $script:Pid97 -and $cleanup.owned_start_utc_ticks -eq $ready.owned_start_utc_ticks -and (True97 $cleanup.owned_pid_absent) -and $cleanup.check -is [string] -and -not [string]::IsNullOrWhiteSpace($cleanup.check) -and [DateTimeOffset]::TryParse([string]$cleanup.checked_utc,[ref]$checked) -and $checked.Offset -eq [TimeSpan]::Zero -and $checked.UtcDateTime -ge (Get-Item -LiteralPath (Path97 'wrapper-result.json')).LastWriteTimeUtc) 'Independent later seven-field OS absence attestation differs'
 Require97 ($wrapper.stage_dll_sha256 -ceq $script:Candidate97 -and $report.candidate_sha256 -ceq $script:Candidate97 -and $ready.loaded_addin.pid -eq $script:Pid97 -and $ready.loaded_addin.sha256 -ceq $script:Candidate97 -and $ready.loaded_addin.verification -cin @('owned_process_module_path_plus_disk_sha256','owned_process_exact_mapped_file_path_plus_disk_sha256') -and (False97 $ready.loaded_addin.memory_bytes_hashed) -and [IO.Path]::IsPathFullyQualified([string]$ready.loaded_addin.path) -and [IO.Path]::GetFileName([string]$ready.loaded_addin.path) -ceq 'Inventor.So.AddIn.dll') 'Frozen96 candidate/loaded-module attribution differs'
 foreach($hash in @($wrapper.original_manifest_sha256,$wrapper.original_dll_sha256)){Require97 ($hash -cmatch '^[A-F0-9]{64}$') 'Original installation hash missing'}
 foreach($install in @($ready.installation_ready,$stopped.installation_after)){
  Require97 ((True97 $install.unchanged) -and $install.stage_addin_sha256 -ceq $script:Candidate97 -and $install.original_installed_dll_sha256 -ceq $wrapper.original_dll_sha256 -and $install.manifest_sha256 -cmatch '^[A-F0-9]{64}$') 'Stage/installed DLL/manifest attestation differs'
 }
 Require97 ($ready.installation_ready.manifest_sha256 -ceq $stopped.installation_after.manifest_sha256) 'Staged manifest changed before owned shutdown'
 $script:Documents97=Map97 $ready.owned_documents 'document_id'
 Require97 ($script:Documents97.Count -eq 2 -and $script:Documents97.ContainsKey([string]$ready.part_document_id) -and $script:Documents97.ContainsKey([string]$ready.sheet_document_id) -and $ready.part_document_id -cne $ready.sheet_document_id) 'Owned Part/Sheet document binding missing'
 foreach($id in $script:Documents97.Keys){$d=$script:Documents97[$id];Require97 ($d.document_id -ceq ('doc_'+$d.internal_name) -and $d.context -ceq $(if($id -ceq $ready.part_document_id){'part'}else{'sheetmetal'}) -and [IO.Path]::IsPathFullyQualified([string]$d.saved_path) -and [string]::Equals([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath([string]$d.saved_path)),[IO.Path]::GetFullPath([string]$ready.fixture_directory),[StringComparison]::OrdinalIgnoreCase) -and $d.baseline_file_sha256 -cmatch '^[A-F0-9]{64}$') 'Owned native saved path/context/identity differs'}
 Closed97 $report @('schema','run_id','target_id','owned_pid','candidate_sha256','status','evidence','cases','expected_cases','input_points_mm','input_points_grounded','writes_global_sketch_options','http_started','bootstrap_exposed','product_pass','full_s1_pass','quest_proof','physical_proof','not_covered','completed_cases','exact_aborts','setter_returned_cases','api_error_observations')
 Require97 ((Integer97 $report.schema) -and $report.schema -eq 1 -and $report.status -ceq 'OBSERVATION_ONLY' -and $report.evidence -ceq 'NATIVE_API_RESEARCH_NOT_PRODUCT_PARITY' -and $report.owned_pid -eq $script:Pid97 -and $report.cases.Count -eq 60 -and $report.expected_cases -eq 60 -and $report.completed_cases -eq 60 -and $report.exact_aborts -eq 60) 'Native research report schema/matrix/status differs'
 foreach($flag in @('product_pass','full_s1_pass','quest_proof','physical_proof','http_started','bootstrap_exposed','writes_global_sketch_options','input_points_grounded')){Require97 (False97 $report[$flag]) 'Native research report overclaims product/global setting scope'}
 $declared=@(@(0d,0d),@(12d,7d),@(25d,-4d),@(39d,11d),@(52d,2d))
 Require97 ($report.input_points_mm.Count -eq 5) 'Declared constructor point count differs'
 for($i=0;$i -lt 5;$i++){FiniteArray97 $report.input_points_mm[$i] 2 2;for($axis=0;$axis -lt 2;$axis++){Require97 ([double]$report.input_points_mm[$i][$axis] -eq [double]$declared[$i][$axis]) 'Declared constructor input differs'}}
 Require97 (Equal97 $report.not_covered @('Product wire/preview/commit','Desktop equivalence','Point/handle editing','Persistence/dependencies','Quest')) 'Native-only research limits changed'
 $reportToken=Token97 'worker/spline-tension97.json';$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 $summary=@();$setter=0;$apiErrors=0;$readbacks=0;$matchingReadbacks97=0;$methodPairs=0;$methodChanges=0;$updatesFalse=0;$ordinal=0
 foreach($row in $report.cases){
  $ordinal++;$doc=if($row.context -ceq 'Part'){$ready.part_document_id}else{$ready.sheet_document_id}
  Require97 ((Integer97 $row.number) -and $row.number -eq $ordinal -and $row.context -cin @('Part','Sheet') -and $row.document_id -ceq $doc) 'Case context/document owner/ordinal differs'
  Require97 ((Integer97 $row.requested_fit_method) -and $row.requested_fit_method -in @(26369,26370,26371) -and $row.requested_closed -is [bool] -and (Integer97 $row.requested_tension) -and $row.requested_tension -in @(0,25,50,75,100)) 'Requested method/closure/tension outside research matrix'
  $combination=$row.context+'|'+$row.requested_fit_method+'|'+$row.requested_closed+'|'+$row.requested_tension
  Require97 ($seen.Add($combination)) 'Duplicate research matrix combination'
  $name='spline-tension97-case-'+$ordinal.ToString('D3')+'.json';$relative='worker/'+$name
  Require97 ($row.sidecar -ceq $name -and $row.sidecar_file_sha256 -cmatch '^[A-F0-9]{64}$' -and (HashFile97 $relative) -ceq $row.sidecar_file_sha256) 'Case sidecar name/file SHA differs'
  $raw=Read97 $relative;$token=Token97 $relative
  Closed97 $raw @('schema','run_id','case_number','native_before','native_before_sha256','native_after_abort','native_after_abort_sha256','result') @('native_after_attempt')
  Require97 ((Integer97 $raw.schema) -and $raw.schema -eq 1 -and $raw.run_id -ceq $script:Run97 -and (Integer97 $raw.case_number) -and $raw.case_number -eq $ordinal) 'Sidecar run/case binding differs'
  $main=$reportToken['cases'][$ordinal-1].DeepClone();[void]$main.Remove('sidecar_file_sha256')
  Require97 ([string]::Equals((Compact97 $main),(Compact97 $token['result']),[StringComparison]::Ordinal)) 'Main row minus self-SHA differs from sidecar embedded result'
  $beforeCompact=Compact97 $token['native_before'];$afterCompact=Compact97 $token['native_after_abort']
  Require97 ((HashText97 $beforeCompact) -ceq $raw.native_before_sha256 -and (HashText97 $afterCompact) -ceq $raw.native_after_abort_sha256) 'Native compact state SHA differs from C#97 serialization'
  State97 $raw.native_before $doc;State97 $raw.native_after_abort $doc
  Require97 ([string]::Equals($beforeCompact,$afterCompact,[StringComparison]::Ordinal)) 'Full native after-Abort changed exact baseline data including Dirty/DBRevision/spline definitions'
  Row97 $row $raw $raw.native_before
  if($row.setter_returned){$setter++};if($row.outcome -ceq 'NATIVE_API_ERROR_OBSERVATION'){$apiErrors++}
  if($row.Contains('after_update')){$readbacks++;if($row.after_update.tension -eq $row.requested_tension){$matchingReadbacks97++}}
  if($row.Contains('before_tension') -and $row.Contains('after_tension')){$methodPairs++;if($row.before_tension.fit_method -ne $row.after_tension.fit_method){$methodChanges++}}
  if($row.Contains('update_returned') -and -not $row.update_returned){$updatesFalse++}
  $summary+=@{number=$ordinal;context=$row.context;document_id=$doc;requested_fit_method=$row.requested_fit_method;requested_closed=$row.requested_closed;requested_tension=$row.requested_tension;outcome=$row.outcome;setter_returned=$row.setter_returned;before_tension=$row.before_tension.tension;after_tension=$row.after_tension.tension;after_update_tension=$row.after_update.tension;before_method=$row.before_tension.fit_method;after_method=$row.after_tension.fit_method;after_update_method=$row.after_update.fit_method;failure=$row.failure;full_native_abort_exact=$true}
 }
 Require97 ($seen.Count -eq 60 -and @(Get-ChildItem -LiteralPath (Join-Path $script:Root97 'worker') -File -Filter 'spline-tension97-case-*.json').Count -eq 60) 'Complete60 matrix/sidecar inventory differs'
 Require97 ($report.setter_returned_cases -eq $setter -and $report.api_error_observations -eq $apiErrors -and $wrapper.setter_returned_cases -eq $setter -and $wrapper.api_error_observations -eq $apiErrors) 'Producer/wrapper aggregate differs from actual observations'
 foreach($relative in @('sources/SplineTension97.cs','sources/Program.cs','sources/S1SplineTensionOwnedHost97.csproj','run-spline-tension97.ps1','bin/Newtonsoft.Json.dll')){[void](Path97 $relative);[void]$script:Files97.Add($relative)}
 return [ordered]@{schema=1;status='VERIFIED_NATIVE_TENSION97_OBSERVATIONS';run_id=$script:Run97;candidate_sha256=$script:Candidate97;observations=60;exact_aborts=60;setter_returned=$setter;complete_update_readbacks=$readbacks;tension_matches_request=$matchingReadbacks97;tension_readback_mismatches=($readbacks-$matchingReadbacks97);before_after_method_pairs=$methodPairs;method_changes=$methodChanges;api_error_observations=$apiErrors;update_returned_false=$updatesFalse;cases=$summary;post_wrapper_owned_absence=$true;product_pass=$false;full_s1_pass=$false;core_https_proof=$false;quest_proof=$false;physical_proof=$false;open_s1_gates=18;files=@($script:Files97|Sort-Object|ForEach-Object {@{path=$_;sha256=(HashFile97 $_)}});limits=@('Offline archived-observation consistency, nativeAPI research only; no support/parity assertion for requested tension values','Native attribution relies on owned-process/loaded-module metadata and producer reports, not independent authenticity','Global SketchOptions non-write is producer/source scope, not an independent complete settings snapshot','No conversion to26370 or default50 required; all recorded native values retained, mismatches/refusals/UpdateFalse are observations','Full captured baseline73/88 plus spline_definitions97 compared exactly including Dirty/DBRevision; not universal Inventor object enumeration','No exact fit-input equality or invented fit-point ownership; native keys scoped per document','Manifest/DLL restoration is wrapper/worker attestation; original manifest bytes are not archived independently','Only pure Json.NET serialization dependency loaded; all compact state hashes must match C#97 before accepting results','No Core/HTTPS, product preview/commit, editing, persistence, Quest, promotion or fullS1 proof')}
}
function SelfTest97([string]$Original,$Verified){
 $tests=@('actual_tension_flag','method_change_flag','after_abort_local_parameter','case_owner_context','product_pass_claim','requested_method_outside_matrix','duplicate_matrix')
 $results=@()
 foreach($name in $tests){
  [void](Verify97 $Original)
  $report=(Token97 'worker/spline-tension97.json').DeepClone();$index=0
  if($name -cin @('actual_tension_flag','method_change_flag')){
   $found=-1;for($i=0;$i -lt $report['cases'].Count;$i++){if($null -ne $report['cases'][$i]['after_update']){$found=$i;break}}
   if($found -lt 0){$results+=@{name=$name;status='NOT_APPLICABLE_NO_COMPLETE_UPDATE_READBACK';semantic_rejection_proven=$false};continue};$index=$found
  }
  $row=$report['cases'][$index];$relative='worker/'+[string]$row['sidecar'];$sidecar=(Token97 $relative).DeepClone();$rawUpdated=$false
  switch($name){
   'actual_tension_flag'{
    $requested=[int]$row['requested_tension'];$oldMatch=[bool]$row['tension_readback_matches_request']
    $row['after_update']['tension']=if($oldMatch){$requested+1}else{$requested}
   }
   'method_change_flag'{$row['fit_method_changed_immediately']=-not [bool]$row['fit_method_changed_immediately']}
   'after_abort_local_parameter'{
    $owner=[string]$row['document_id'];$target=$null
    foreach($doc in $sidecar['native_after_abort']['documents']){if([string]$doc['document_id'] -ceq $owner){$target=$doc;break}}
    Require97 ($null -ne $target -and $target['parameters'].Count -gt 0) 'Selftest local parameter fixture unavailable'
    $target['parameters'][0]['expression']=[Newtonsoft.Json.Linq.JValue]::new('tampered97-local-parameter')
    $sidecar['native_after_abort_sha256']=[Newtonsoft.Json.Linq.JValue]::new([string](HashText97 (Compact97 $sidecar['native_after_abort'])));$rawUpdated=$true
   }
   'case_owner_context'{$row['context']=[Newtonsoft.Json.Linq.JValue]::new([string]$(if([string]$row['context'] -ceq 'Part'){'Sheet'}else{'Part'}))}
   'product_pass_claim'{$report['product_pass']=$true}
   'requested_method_outside_matrix'{$row['requested_fit_method']=26372}
   'duplicate_matrix'{foreach($field in @('context','document_id','requested_fit_method','requested_closed','requested_tension')){$report['cases'][1][$field]=$report['cases'][0][$field].DeepClone()};$index=1;$row=$report['cases'][1];$relative='worker/'+[string]$row['sidecar'];$sidecar=(Token97 $relative).DeepClone()}
  }
  $embedded=$row.DeepClone();[void]$embedded.Remove('sidecar_file_sha256');$sidecar['result']=$embedded
  $sidecarText=$sidecar.ToString([Newtonsoft.Json.Formatting]::Indented);$row['sidecar_file_sha256']=[Newtonsoft.Json.Linq.JValue]::new([string](HashText97 $sidecarText))
  $overlay=@{};$overlay[$relative]=$sidecarText;$overlay['worker/spline-tension97.json']=$report.ToString([Newtonsoft.Json.Formatting]::Indented)
  $rejected=$false;$reason=''
  try{[void](Verify97 $Original $overlay)}catch{$rejected=$true;$reason=$_.Exception.Message}
  Require97 ($rejected -and $reason -notmatch 'SHA|self-SHA|later seven-field|Terminal wrapper|Required field missing') ('Semantic selftest accepted or failed unrelated integrity: '+$name+' '+$reason)
  $results+=@{name=$name;status='REJECTED_AS_EXPECTED';semantic_rejection_proven=$true;sidecar_file_sha_updated=$true;raw_compact_sha_updated=$rawUpdated;assertion=$reason}
 }
 return ,$results
}
$root=[IO.Path]::GetFullPath($EvidenceDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
Require97 ((Test-Path -LiteralPath $root -PathType Container) -and [IO.Path]::GetFileName($root) -cmatch '^native-r[1-9][0-9]*$' -and [IO.Path]::GetFileName([IO.Path]::GetDirectoryName($root)) -ceq 'increment97' -and ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'Only archived increment97/native-rN terminal evidence accepted'
$jsonDll=Join-Path $root 'bin/Newtonsoft.Json.dll'
Require97 ((Test-Path -LiteralPath $jsonDll -PathType Leaf) -and ((Get-Item -LiteralPath $jsonDll).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'Archived pure Json.NET dependency unavailable'
[void][Reflection.Assembly]::LoadFrom($jsonDll) # Pure JSON only, never native/producer code.
$output=Join-Path $root $OutputName
if(-not $ValidateOnly){Require97 (-not (Test-Path -LiteralPath $output)) 'Verification output exists; choose a unique OutputName'}
$result=Verify97 $root
if($SelfTest){$result.selftests=SelfTest97 $root $result}
$result.verifier_sha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
if($ValidateOnly){$result|ConvertTo-Json -Depth 15;exit 0}
$stream=[IO.FileStream]::new($output,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes(($result|ConvertTo-Json -Depth 15));$stream.Write($bytes);$stream.Flush($true)}finally{$stream.Dispose()}
Write-Output $output
