#requires -Version 7.0
<#
Owned S1 Quest QA. Does not build, acquire ROT, clear pairing, uninstall, or kill processes.
The only credentials are a one-use owner-only local bootstrap and its app-scoped device copy.
Exit: 0 PASS subset, 1 runner FAIL, 2 timeout, 3 setup/cleanup failure, 4 PARTIAL.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$QaApk,
    [Parameter(Mandatory)][string]$OrdinaryApk,
    [Parameter(Mandatory)][ValidatePattern('\A[A-Za-z0-9_.:-]+\z')][string]$Serial,
    [Parameter(Mandatory)][string]$OutDir,
    [Parameter(Mandatory)][string]$AddinDirectory,
    [string]$AdbPath='adb',
    [string]$HostDirectory=(Join-Path $PSScriptRoot '../bridge/tests/S1QuestOwnedHost73/bin/Debug/net10.0-windows'),
    [ValidateRange(1,1250)][int]$RunnerTimeoutSeconds=1250
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$s75Repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$s75Run=[guid]::NewGuid().ToString('N')
$s75Package='com.occhipinti.inventorxrso'
$s75RemoteDirectory="/sdcard/Android/data/$s75Package/files"
$s75RemoteBootstrap="files/s1-$s75Run.bootstrap.json"
$s75RemoteLog="$s75RemoteDirectory/s1-acceptance.txt"
$s75OrdinarySha='d8d6d341ccbdcf9ba06ff282908576ca3f5fcfcf5dffaf6529aa68ac70b04f81'
$s75Phase='arguments';$s75Exit=3;$s75Outcome='SETUP_ERROR';$s75Host=$null
$s75HostOutputTask=$null;$s75HostErrorTask=$null;$s75HostStarted=$false;$s75HostReady=$false
$s75ManifestStaged=$false;$s75InstallAttempted=$false;$s75ReverseAttempted=$false
$s75BootstrapAttempted=$false;$s75BootstrapPath=$null;$s75Runtime=$null;$s75Ready=$null
$s75SmokeRemote=$null
$s75ManifestPath=$null;$s75ManifestBytes=$null;$s75ManifestOriginalSha=$null;$s75ManifestStageSha=$null
$s75OriginalDll=$null;$s75OriginalDllSha=$null;$s75Port=0;$s75OwnedOutput=$false
$s75Cleanup=[ordered]@{};$s75Failures=[Collections.Generic.List[object]]::new()
$s75Checks=@();$s75RunnerStarted=$false;$s75Start=[DateTimeOffset]::UtcNow
$s75BootstrapConsumed=$false

function Assert-S75([bool]$Condition,[string]$Code){if(-not $Condition){throw [InvalidOperationException]::new($Code)}}
function Safe-Failure75([string]$Phase,[Exception]$Error){
    # Never serialize Message, ErrorRecord, command lines, child output, headers or bootstrap content.
    return [ordered]@{phase=$Phase;type=$Error.GetType().FullName;hresult=$Error.HResult}
}
function Hash-S75([string]$Path){
    Assert-S75 (Test-Path -LiteralPath $Path -PathType Leaf) 'regular_file_missing'
    Assert-S75 (((Get-Item -LiteralPath $Path).Attributes -band [IO.FileAttributes]::ReparsePoint)-eq 0) 'reparse_file_rejected'
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}
function Write-Public75([string]$Name,$Value){
    Assert-S75 ($Name -match '\A[a-z0-9_.-]+\.json\z') 'public_filename_invalid'
    $path=Join-Path $script:s75Out $Name
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($Value|ConvertTo-Json -Depth 25))
    $stream=[IO.FileStream]::new($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    try{$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
}
function Write-Bytes75([string]$Path,[byte[]]$Bytes){
    $stream=[IO.FileStream]::new($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    try{$stream.Write($Bytes,0,$Bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
}
function Write-Text75([string]$Path,[string]$Text){Write-Bytes75 $Path ([Text.UTF8Encoding]::new($false).GetBytes($Text))}
function New-Private75([string]$Path){
    Assert-S75 (-not (Test-Path -LiteralPath $Path)) 'private_directory_already_exists'
    $directory=New-Item -ItemType Directory -Path $Path
    $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl=[Security.AccessControl.DirectorySecurity]::new();$acl.SetOwner($sid);$acl.SetAccessRuleProtection($true,$false)
    $rule=[Security.AccessControl.FileSystemAccessRule]::new($sid,[Security.AccessControl.FileSystemRights]::FullControl,
        ([Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit),
        [Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow)
    $acl.AddAccessRule($rule);Set-Acl -LiteralPath $directory.FullName -AclObject $acl
}
function Copy-Tree75([string]$Source,[string]$Destination){
    $sourceRoot=[IO.Path]::GetFullPath($Source).TrimEnd('\','/')
    Assert-S75 (Test-Path -LiteralPath $sourceRoot -PathType Container) 'source_directory_missing'
    Assert-S75 (((Get-Item -LiteralPath $sourceRoot).Attributes -band [IO.FileAttributes]::ReparsePoint)-eq 0) 'source_root_reparse_rejected'
    Assert-S75 (-not (Test-Path -LiteralPath $Destination)) 'snapshot_already_exists'
    $null=New-Item -ItemType Directory -Path $Destination
    $queue=[Collections.Generic.Stack[string]]::new();$queue.Push($sourceRoot)
    while($queue.Count){
        $current=$queue.Pop()
        foreach($item in Get-ChildItem -LiteralPath $current -Force){
            Assert-S75 (($item.Attributes -band [IO.FileAttributes]::ReparsePoint)-eq 0) 'snapshot_reparse_rejected'
            $relative=[IO.Path]::GetRelativePath($sourceRoot,$item.FullName)
            Assert-S75 (-not $relative.StartsWith('..')) 'snapshot_escape_rejected'
            $target=Join-Path $Destination $relative
            if($item.PSIsContainer){$null=New-Item -ItemType Directory -Path $target;$queue.Push($item.FullName);continue}
            # Compiled/source snapshots never include credential/private runtime material.
            Assert-S75 ($item.Name -notmatch '(?i)(bootstrap.*\.json|pairing\.dat|token.*\.(json|txt)|credential|appsettings|\.(pfx|p12|pem|key)$)') 'private_material_in_snapshot_source'
            [IO.File]::Copy($item.FullName,$target,$false)
            Assert-S75 ((Hash-S75 $item.FullName)-eq (Hash-S75 $target)) 'snapshot_hash_mismatch'
        }
    }
}
function Run-Process75([string]$Executable,[string[]]$Arguments,[int]$TimeoutSeconds=30,[string]$InputFile=$null){
    $info=[Diagnostics.ProcessStartInfo]::new();$info.FileName=$Executable;$info.UseShellExecute=$false
    $info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true;$info.RedirectStandardInput=$true
    foreach($argument in $Arguments){$info.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::new();$process.StartInfo=$info
    try{
        Assert-S75 ($process.Start()) 'child_start_failed'
        $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        if($InputFile){
            # Byte transfer only. Credential bytes never become a PowerShell string/argument/output.
            $bootstrapStream=[IO.File]::OpenRead($InputFile)
            try{Assert-S75 ($bootstrapStream.Length -gt 0 -and $bootstrapStream.Length -le 16384) 'bootstrap_byte_budget';$bootstrapStream.CopyTo($process.StandardInput.BaseStream)}finally{$bootstrapStream.Dispose()}
        }
        $process.StandardInput.Close()
        Assert-S75 ($process.WaitForExit($TimeoutSeconds*1000)) 'child_timeout_no_kill'
        $out=$stdout.GetAwaiter().GetResult();$err=$stderr.GetAwaiter().GetResult()
        Assert-S75 ($out.Length -le 8388608 -and $err.Length -le 1048576) 'child_output_budget'
        return [pscustomobject]@{Code=$process.ExitCode;Text=$out;ErrorText=$err}
    }finally{$process.Dispose()}
}
function Adb75([string[]]$Arguments,[int]$TimeoutSeconds=30,[string]$InputFile=$null){
    return Run-Process75 $script:s75Adb (@('-s',$script:Serial)+$Arguments) $TimeoutSeconds $InputFile
}
function Require-Adb75([string[]]$Arguments,[int]$TimeoutSeconds=30,[string]$InputFile=$null){
    $result=Adb75 $Arguments $TimeoutSeconds $InputFile
    Assert-S75 ($result.Code-eq 0) 'adb_operation_failed'
    return $result
}
function Device-Hash75 {
    $path=Require-Adb75 @('shell','pm','path',$script:s75Package)
    $apkLines=@($path.Text -split '\r?\n' | Where-Object {$_ -match '\Apackage:/[A-Za-z0-9_./=+~-]+/base\.apk\s*\z'})
    Assert-S75 ($apkLines.Count-eq 1) 'device_apk_path_ambiguous'
    $remote=$apkLines[0].Trim().Substring('package:'.Length)
    $sum=Require-Adb75 @('shell','sha256sum',$remote)
    Assert-S75 ($sum.Text -match '\A([0-9a-fA-F]{64})\s') 'device_apk_hash_missing'
    return $Matches[1].ToLowerInvariant()
}
function Install75([string]$Path,[string]$Sha){
    $result=Require-Adb75 @('install','-r',$Path) 120
    Assert-S75 ($result.Text -match '\bSuccess\b') 'apk_install_unconfirmed'
    Assert-S75 ((Device-Hash75)-eq $Sha) 'device_apk_hash_mismatch'
}
function Awake75 {
    $power=Require-Adb75 @('shell','dumpsys','power')
    return $power.Text -match 'mWakefulness=Awake\b'
}
function Read-Public75([string]$Path){
    Assert-S75 ((Get-Item -LiteralPath $Path).Length-le 8388608) 'host_public_file_budget'
    $value=Get-Content -LiteralPath $Path -Raw|ConvertFrom-Json
    if($null-eq $value -or $value-is [Array]){throw [ArgumentException]::new('partial_public_json')}
    return $value
}
function Wait-HostFile75([string]$Path,[int]$Seconds){
    $deadline=[DateTimeOffset]::UtcNow.AddSeconds($Seconds)
    while($true){
        if(Test-Path -LiteralPath $Path){
            try{return Read-Public75 $Path}
            catch [IO.IOException]{}
            catch [ArgumentException]{}
            catch [System.Management.Automation.RuntimeException]{}
        }
        Assert-S75 (-not $script:s75Host.HasExited) 'owned_host_exited_before_marker'
        Assert-S75 ([DateTimeOffset]::UtcNow-lt $deadline) 'owned_host_marker_timeout'
        Start-Sleep -Milliseconds 250
    }
}
function Restore-Manifest75 {
    Assert-S75 ((Hash-S75 $script:s75OriginalDll)-eq $script:s75OriginalDllSha) 'original_installed_dll_changed'
    $current=Hash-S75 $script:s75ManifestPath
    Assert-S75 ($current-eq $script:s75ManifestStageSha -or $current-eq $script:s75ManifestOriginalSha) 'manifest_changed_by_another_actor'
    if($current-ne $script:s75ManifestOriginalSha){Write-Manifest75 $script:s75ManifestBytes 'restore'}
    Assert-S75 ((Hash-S75 $script:s75ManifestPath)-eq $script:s75ManifestOriginalSha) 'manifest_restore_hash_mismatch'
}
function Write-Manifest75([byte[]]$Bytes,[string]$Phase){
    $temporary=Join-Path (Split-Path -Parent $script:s75ManifestPath) ('.s1-'+$script:s75Run+'-'+$Phase+'.tmp')
    try{
        $stream=[IO.FileStream]::new($temporary,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        try{$stream.Write($Bytes,0,$Bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
        [IO.File]::Move($temporary,$script:s75ManifestPath,$true)
    }finally{if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary}}
}
function Cleanup75([string]$Name,[scriptblock]$Work){
    try{& $Work;$script:s75Cleanup[$Name]=$true}
    catch{$script:s75Cleanup[$Name]=$false;$script:s75Failures.Add((Safe-Failure75 $Name $_.Exception));$script:s75Exit=3}
}

try{
    Assert-S75 ($IsWindows) 'windows_required'
    $s75Out=[IO.Path]::GetFullPath($OutDir);Assert-S75 (-not (Test-Path -LiteralPath $s75Out)) 'output_directory_must_be_new'
    $null=New-Item -ItemType Directory -Path $s75Out;$s75OwnedOutput=$true
    $s75Qa=[IO.Path]::GetFullPath($QaApk);$s75Ordinary=[IO.Path]::GetFullPath($OrdinaryApk)
    $s75QaSha=Hash-S75 $s75Qa
    Assert-S75 ((Hash-S75 $s75Ordinary)-eq $s75OrdinarySha) 'ordinary_apk_is_not_frozen38'
    Assert-S75 ($s75QaSha-ne $s75OrdinarySha) 'qa_and_ordinary_must_differ'
    $s75Adb=(Get-Command $AdbPath -CommandType Application -ErrorAction Stop).Source
    $s75Phase='device_preflight'
    $device=Require-Adb75 @('get-state');Assert-S75 ($device.Text.Trim()-eq 'device') 'quest_not_connected'
    Assert-S75 ((Device-Hash75)-eq $s75OrdinarySha) 'initial_device_is_not_frozen38'
    $awake=Awake75
    Write-Public75 'preflight.json' ([ordered]@{schema=1;run_id=$s75Run;serial=$Serial;checked_utc=[DateTimeOffset]::UtcNow.ToString('O');device_awake=$awake;fixture_prepared=$false;runner_started=$false;ordinary_sha256=$s75OrdinarySha;qa_sha256=$s75QaSha})
    if(-not $awake){$s75Outcome='NOT_RUN_DEVICE_ASLEEP';throw [InvalidOperationException]::new('quest_asleep_before_fixture')}

    $s75Phase='snapshot_before_run'
    $s75Runtime=Join-Path $env:LOCALAPPDATA "InventorSO/qa-runs/s1-owned75-$s75Run"
    New-Private75 $s75Runtime
    $s75Stage=Join-Path $s75Runtime 'addin';Copy-Tree75 ([IO.Path]::GetFullPath($AddinDirectory)) $s75Stage
    Copy-Tree75 $s75Stage (Join-Path $s75Out 'addin-stage')
    Copy-Tree75 ([IO.Path]::GetFullPath($HostDirectory)) (Join-Path $s75Out 'host-bin')
    $sourceEvidence=Join-Path $s75Out 'host-source';$null=New-Item -ItemType Directory -Path $sourceEvidence
    $hostSources=@('Program.cs','Ownership73.cs','Options73.cs','LoadedAddin73.cs','Fixtures73.cs','Files73.cs','S1QuestOwnedHost73.csproj')
    foreach($name in $hostSources){[IO.File]::Copy((Join-Path $s75Repo "bridge/tests/S1QuestOwnedHost73/$name"),(Join-Path $sourceEvidence $name),$false)}
    [IO.File]::Copy($PSCommandPath,(Join-Path $s75Out 'wrapper-source.ps1'),$false)
    $s75HostExe=Join-Path $s75Out 'host-bin/S1QuestOwnedHost73.exe'
    $null=Hash-S75 $s75HostExe
    $s75StageDll=Join-Path $s75Stage 'Inventor.So.AddIn.dll';$s75StageSha=Hash-S75 $s75StageDll
    $s75ManifestPath=Join-Path $env:APPDATA 'Autodesk/Inventor 2027/Addins/InventorSO/Inventor.So.addin'
    $s75ManifestOriginalSha=Hash-S75 $s75ManifestPath;$s75ManifestBytes=[IO.File]::ReadAllBytes($s75ManifestPath)
    Write-Bytes75 (Join-Path $s75Out 'original-manifest.bin') $s75ManifestBytes
    $xml=[Xml.XmlDocument]::new();$xml.XmlResolver=$null
    $xmlInput=[IO.MemoryStream]::new($s75ManifestBytes,$false)
    try{$xml.Load($xmlInput)}finally{$xmlInput.Dispose()}
    Assert-S75 ($xml.Addin.ClassId-eq '{7EE3B69B-8A24-42DD-9422-50E8C5279F40}') 'unexpected_addin_classid'
    $assembly=$xml.SelectSingleNode('/Addin/Assembly');Assert-S75 ($null-ne $assembly) 'manifest_assembly_missing'
    $s75OriginalDll=if([IO.Path]::IsPathFullyQualified($assembly.InnerText)){[IO.Path]::GetFullPath($assembly.InnerText)}else{[IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $s75ManifestPath) $assembly.InnerText))}
    $s75OriginalDllSha=Hash-S75 $s75OriginalDll
    $assembly.InnerText=$s75StageDll;$s75Replacement=Join-Path $s75Runtime 'stage-manifest.addin';$xml.Save($s75Replacement)
    $s75ManifestStageSha=Hash-S75 $s75Replacement
    [IO.File]::Copy($s75Replacement,(Join-Path $s75Out 'stage-manifest.addin'),$false)
    Write-Public75 'binaries.json' ([ordered]@{schema=1;run_id=$s75Run;stage_addin_sha256=$s75StageSha;original_installed_dll_sha256=$s75OriginalDllSha;original_manifest_sha256=$s75ManifestOriginalSha;stage_manifest_sha256=$s75ManifestStageSha;host_exe_sha256=(Hash-S75 $s75HostExe);qa_sha256=$s75QaSha;ordinary_sha256=$s75OrdinarySha})
    $snapshots=@(Get-ChildItem -LiteralPath $s75Out -File -Recurse | ForEach-Object {[ordered]@{path=[IO.Path]::GetRelativePath($s75Out,$_.FullName);bytes=$_.Length;sha256=(Hash-S75 $_.FullName)}})
    Write-Public75 'snapshot-files.json' ([ordered]@{schema=1;run_id=$s75Run;files=$snapshots;credentials_included=$false})

    $s75Phase='qa_install';$s75InstallAttempted=$true;Install75 $s75Qa $s75QaSha
    $null=Require-Adb75 @('shell','run-as',$s75Package,'id')
    # Exercise intent JNI + private one-shot file consumption before opening
    # Inventor. An invalid schema must fail immediately, without using pairing.
    $s75Phase='invalid_bootstrap_preflight'
    $smokeRun=[guid]::NewGuid().ToString('N')
    $s75SmokeRemote="files/s1-$smokeRun.bootstrap.json"
    $smokeLocal=Join-Path $s75Runtime 'invalid-bootstrap.json'
    Write-Text75 $smokeLocal '{}'
    $null=Require-Adb75 @('shell','am','force-stop',$s75Package)
    $null=Require-Adb75 @('shell','rm','-f',$s75RemoteLog)
    try{
        $null=Require-Adb75 @('shell','-T',"run-as $s75Package sh -c 'umask 077; mkdir -p files; cat > $s75SmokeRemote'") 30 $smokeLocal
    }finally{Remove-Item -LiteralPath $smokeLocal}
    $null=Require-Adb75 @('shell','am','start','-n',"$s75Package/com.unity3d.player.UnityPlayerGameActivity",'--ez','xr_s1_acceptance','true','--es','xr_s1_bootstrap_run',$smokeRun)
    $smokeDeadline=[DateTimeOffset]::UtcNow.AddSeconds(60);$smokeText=''
    do{
        $smokeRead=Adb75 @('shell','head','-c','16384',$s75RemoteLog) 10
        if($smokeRead.Code-eq 0){$smokeText=$smokeRead.Text;if($smokeText-match '\] FAIL;'){break}}
        Start-Sleep -Milliseconds 500
    }while([DateTimeOffset]::UtcNow-lt $smokeDeadline)
    Write-Text75 (Join-Path $s75Out 'bootstrap-negative.txt') $smokeText
    Assert-S75 ($smokeText-match 'S1 bootstrap rejected at stage read_bootstrap; no CAD case executed\.') 'invalid_bootstrap_wrong_rejection_stage'
    $null=Require-Adb75 @('shell',"run-as $s75Package test ! -e $s75SmokeRemote")
    Write-Public75 'bootstrap-negative.json' ([ordered]@{outcome='PASS_INVALID_BOOTSTRAP_REJECTED';private_file_consumed=$true;native_fixture_created=$false;cad_case_executed=$false;pairing_fallback=$false})
    $null=Require-Adb75 @('shell','am','force-stop',$s75Package)
    $null=Require-Adb75 @('shell','rm','-f',$s75RemoteLog)
    Assert-S75 (Awake75) 'quest_asleep_before_owned_launch'
    $s75Phase='manifest_stage';$s75ManifestStaged=$true
    Write-Manifest75 ([IO.File]::ReadAllBytes($s75Replacement)) 'stage'
    Assert-S75 ((Hash-S75 $s75ManifestPath)-eq $s75ManifestStageSha) 'staged_manifest_hash_mismatch'

    $s75Phase='owned_host_launch';$s75HostRuntime=Join-Path $s75Runtime 'host-live'
    $info=[Diagnostics.ProcessStartInfo]::new();$info.FileName=$s75HostExe;$info.UseShellExecute=$false;$info.CreateNoWindow=$true
    $info.WorkingDirectory=$s75Repo;$info.RedirectStandardInput=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    foreach($argument in @('--run-id',$s75Run,'--host','127.0.0.1','--output-dir',$s75HostRuntime,
        '--expected-addin-path',$s75StageDll,'--expected-addin-sha256',$s75StageSha,'--manifest-path',$s75ManifestPath,
        '--manifest-sha256',$s75ManifestStageSha,'--installed-dll-path',$s75OriginalDll,'--installed-dll-sha256',$s75OriginalDllSha)){$info.ArgumentList.Add($argument)}
    $s75Host=[Diagnostics.Process]::new();$s75Host.StartInfo=$info;Assert-S75 ($s75Host.Start()) 'owned_host_start_failed';$s75HostStarted=$true
    $s75HostOutputTask=$s75Host.StandardOutput.ReadToEndAsync();$s75HostErrorTask=$s75Host.StandardError.ReadToEndAsync()
    $s75Ready=Wait-HostFile75 (Join-Path $s75HostRuntime 'ready.json') 240;$s75HostReady=$true
    Assert-S75 ($s75Ready.run_id-eq $s75Run -and $s75Ready.status-eq 'ready' -and $s75Ready.host-eq '127.0.0.1') 'ready_run_binding_mismatch'
    Assert-S75 ($s75Ready.owned_pid-gt 0 -and $s75Ready.owned_pid-ne 91056 -and $s75Ready.target_id-eq ('inventor-2027-'+$s75Ready.owned_pid)) 'ready_owned_target_mismatch'
    Assert-S75 ($s75Ready.loaded_addin.sha256.ToLowerInvariant()-eq $s75StageSha -and $s75Ready.loaded_addin.pid-eq $s75Ready.owned_pid -and
        [IO.Path]::GetFullPath($s75Ready.loaded_addin.path)-eq [IO.Path]::GetFullPath($s75StageDll)) 'ready_loaded_addin_mismatch'
    $s75Port=[int]$s75Ready.port;Assert-S75 ($s75Port-ge 1 -and $s75Port-le 65535) 'ready_port_invalid'
    # PowerShell 7.5 converts ISO JSON dates to DateTime automatically. Preserve
    # that UTC value directly instead of round-tripping through locale text.
    $expiry=if($s75Ready.expires_utc -is [DateTime]){
        [DateTimeOffset]::new($s75Ready.expires_utc)
    }else{
        [DateTimeOffset]::Parse([string]$s75Ready.expires_utc,[Globalization.CultureInfo]::InvariantCulture)
    }
    Assert-S75 ($expiry-gt [DateTimeOffset]::UtcNow.AddSeconds($RunnerTimeoutSeconds+120)) 'insufficient_bootstrap_lifetime'
    $s75Phase='usb_reverse'
    $null=Require-Adb75 @('reverse','--no-rebind',"tcp:$s75Port","tcp:$s75Port")
    $s75ReverseAttempted=$true
    $null=Require-Adb75 @('shell','am','force-stop',$s75Package)
    $s75BootstrapPath=Join-Path $s75HostRuntime 'bootstrap.json'
    $s75Host.StandardInput.WriteLine('bootstrap');$s75Host.StandardInput.Flush()
    $bootstrapDeadline=[DateTimeOffset]::UtcNow.AddSeconds(15)
    while(-not (Test-Path -LiteralPath $s75BootstrapPath)){
        Assert-S75 (-not $s75Host.HasExited -and [DateTimeOffset]::UtcNow-lt $bootstrapDeadline) 'bootstrap_not_written'
        Start-Sleep -Milliseconds 100
    }
    # File existence precedes its final flush/close. Probe sharing only, never print/read credential content.
    while($true){
        try{$probe=[IO.File]::OpenRead($s75BootstrapPath);try{Assert-S75 ($probe.Length-gt 0 -and $probe.Length-le 16384) 'bootstrap_length_invalid'}finally{$probe.Dispose()};break}
        catch [IO.IOException]{Assert-S75 ([DateTimeOffset]::UtcNow-lt $bootstrapDeadline) 'bootstrap_flush_timeout';Start-Sleep -Milliseconds 100}
    }
    $s75Phase='bootstrap_stdin_transfer';$s75BootstrapAttempted=$true
    $null=Require-Adb75 @('shell',"run-as $s75Package mkdir -p files")
    $null=Require-Adb75 @('shell',"run-as $s75Package test -d files")
    $null=Require-Adb75 @('shell','-T',"run-as $s75Package sh -c 'umask 077; cat > $s75RemoteBootstrap'") 30 $s75BootstrapPath
    Remove-Item -LiteralPath $s75BootstrapPath
    $null=Require-Adb75 @('shell',"run-as $s75Package test -f $s75RemoteBootstrap")
    $mode=Require-Adb75 @('shell',"run-as $s75Package stat -c %a $s75RemoteBootstrap")
    Assert-S75 ($mode.Text.Trim()-eq '600') 'bootstrap_device_mode_not_private'
    Write-Public75 'bootstrap-transfer.json' ([ordered]@{run_id=$s75Run;location='Android private getFilesDir';mode='600';stdin_only=$true;credential_content_recorded=$false})
    $oldImages=Adb75 @('shell','ls',"$s75RemoteDirectory/s1-acceptance-*.png")
    $s75OldImages=@($oldImages.Text -split '\r?\n' | Where-Object {$_ -match '\A/sdcard/Android/data/com\.occhipinti\.inventorxrso/files/s1-acceptance-[A-Za-z0-9_.-]+\.png\z'})
    $null=Require-Adb75 @('shell','rm','-f',$s75RemoteLog)
    Assert-S75 (Awake75) 'quest_asleep_before_runner_launch'
    $s75Phase='quest_runner'
    $null=Require-Adb75 @('shell','am','start','-n',"$s75Package/com.unity3d.player.UnityPlayerGameActivity",'--ez','xr_s1_acceptance','true','--es','xr_s1_bootstrap_run',$s75Run)
    $deadline=[DateTimeOffset]::UtcNow.AddSeconds($RunnerTimeoutSeconds);$s75Log='';$s75Outcome='TIMEOUT'
    while([DateTimeOffset]::UtcNow-lt $deadline){
        Assert-S75 (-not $s75Host.HasExited) 'owned_host_exited_during_runner'
        $log=Adb75 @('shell','head','-c','8388609',$s75RemoteLog) 10
        if($log.Code-eq 0){
            $s75RunnerStarted=$true;$s75Log=$log.Text
            if($s75Log-match '\] FAIL;| FAIL;'){$s75Outcome='FAIL';break}
            if($s75Log-match '\] PARTIAL;'){$s75Outcome='PARTIAL';break}
            if($s75Log-match '\] PASS COMPLETE;'){$s75Outcome='PASS_SUBSET';break}
        }
        Start-Sleep -Seconds 2
    }
    Write-Text75 (Join-Path $s75Out 's1-acceptance.txt') $s75Log
    $s75Checks=@(foreach($line in $s75Log -split '\r?\n'){if($line-match '\b(PASS|NOT COVERED) \[([^\]]+)\]\s*(.*)$'){[ordered]@{status=$Matches[1];gate=$Matches[2];detail=$Matches[3]}}})
    $s75Exit=switch($s75Outcome){'PASS_SUBSET'{0};'PARTIAL'{4};'FAIL'{1};default{2}}
}
catch{$s75Failures.Add((Safe-Failure75 $s75Phase $_.Exception));$s75Exit=3}
finally{
    if($s75InstallAttempted){
        if($s75RunnerStarted){Cleanup75 'verify_bootstrap_consumed_by_app' {
            $consumed=Adb75 @('shell',"run-as $s75Package test ! -e $s75RemoteBootstrap")
            $script:s75BootstrapConsumed=$consumed.Code-eq 0
            Assert-S75 $script:s75BootstrapConsumed 'qa_bootstrap_was_not_consumed'
        }}
        if($s75RunnerStarted){Cleanup75 'capture_final_compositor' {$null=Require-Adb75 @('shell','screencap','-p',"$s75RemoteDirectory/s1-acceptance-owned75-$s75Run-compositor.png")}}
        Cleanup75 'stop_qa_app' {$null=Require-Adb75 @('shell','am','force-stop',$s75Package)}
    }
    if($s75HostStarted){
        Cleanup75 'inspect_owned_fixture' {
            Assert-S75 ($s75HostReady -and -not $s75Host.HasExited) 'owned_inspection_unavailable'
            $s75Host.StandardInput.WriteLine('inspect');$s75Host.StandardInput.Flush()
            $inspection=Wait-HostFile75 (Join-Path $s75HostRuntime 'inspect-001.json') 45
            Assert-S75 ($inspection.run_id-eq $s75Run -and $inspection.readonly_native_revision_dirty_preserved) 'native_inspection_mismatch'
        }
        Cleanup75 'stop_owned_host' {
            if(-not $s75Host.HasExited){$s75Host.StandardInput.WriteLine('stop');$s75Host.StandardInput.Flush();$s75Host.StandardInput.Close()}
            Assert-S75 ($s75Host.WaitForExit(90000)) 'owned_shutdown_timeout_no_kill'
            $stopped=Read-Public75 (Join-Path $s75HostRuntime 'stopped.json')
            Assert-S75 ($s75Host.ExitCode-eq 0 -and $stopped.exit_code-eq 0 -and $stopped.status-eq 'stopped' -and
                $stopped.owned_final_empty -and $stopped.owned_pid_exited -and $stopped.existing_processes_preserved) 'owned_cleanup_not_verified'
        }
    }
    if($s75InstallAttempted){
        Cleanup75 'collect_runner_evidence' {
            # Pull only this run's fixed log and newly created S1 PNGs; never a directory or logcat.
            $log=Adb75 @('shell','head','-c','8388609',$s75RemoteLog) 10
            if($s75RunnerStarted -and $log.Code-eq 0 -and -not (Test-Path -LiteralPath (Join-Path $s75Out 's1-acceptance.txt'))){Write-Text75 (Join-Path $s75Out 's1-acceptance.txt') $log.Text}
            if($s75RunnerStarted){
                $names=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                foreach($line in $log.Text -split '\r?\n'){
                    if($line -match 'Screenshot: (?:/sdcard|/storage/emulated/0)/Android/data/com\.occhipinti\.inventorxrso/files/(s1-acceptance-[A-Za-z0-9_.-]+\.png)\s*\z'){
                        $null=$names.Add($Matches[1])
                    }
                }
                $null=$names.Add("s1-acceptance-owned75-$s75Run-compositor.png")
                foreach($name in $names){
                    $remote="$s75RemoteDirectory/$name"
                    $target=Join-Path $s75Out $name;Assert-S75 (-not (Test-Path -LiteralPath $target)) 'screenshot_evidence_exists'
                    $null=Require-Adb75 @('pull',$remote,$target) 30
                }
            }
        }
    }
    if($s75BootstrapAttempted){Cleanup75 'remove_device_bootstrap' {$null=Require-Adb75 @('shell',"run-as $s75Package rm -f $s75RemoteBootstrap")}}
    if($s75SmokeRemote){Cleanup75 'remove_invalid_bootstrap' {$null=Require-Adb75 @('shell',"run-as $s75Package rm -f $s75SmokeRemote")}}
    if($s75BootstrapPath){Cleanup75 'remove_local_bootstrap' {if(Test-Path -LiteralPath $s75BootstrapPath){Remove-Item -LiteralPath $s75BootstrapPath}}}
    if($s75ReverseAttempted){Cleanup75 'remove_usb_reverse' {$null=Require-Adb75 @('reverse','--remove',"tcp:$s75Port")}}
    if($s75ManifestStaged){Cleanup75 'restore_original_manifest_and_verify_dll' {Restore-Manifest75}}
    if($s75HostStarted){
        Cleanup75 'archive_public_host_evidence' {
            Assert-S75 ($s75Host.HasExited) 'host_still_running_no_archive'
            $destination=Join-Path $s75Out 'host-public';$null=New-Item -ItemType Directory -Path $destination
            foreach($file in Get-ChildItem -LiteralPath $s75HostRuntime -File){
                if($file.Name -notmatch '\A(ready|stopped|ownership|inspect-[0-9]{3})\.json\z'){continue}
                $null=Read-Public75 $file.FullName;[IO.File]::Copy($file.FullName,(Join-Path $destination $file.Name),$false)
            }
        }
        Cleanup75 'dispose_host_process_handle' {$s75Host.Dispose()}
    }
    if($s75InstallAttempted){Cleanup75 'restore_ordinary38_and_verify_remote_hash' {Install75 $s75Ordinary $s75OrdinarySha}}
    if($s75OwnedOutput){
        try{
            Write-Public75 'restoration.json' ([ordered]@{schema=1;run_id=$s75Run;serial=$Serial;started_utc=$s75Start.ToString('O');completed_utc=[DateTimeOffset]::UtcNow.ToString('O');outcome=$s75Outcome;exit_code=$s75Exit;runner_started=$s75RunnerStarted;bootstrap_consumed_by_app=$s75BootstrapConsumed;checks=$s75Checks;cleanup=$s75Cleanup;failures=@($s75Failures);ordinary_sha256=$s75OrdinarySha;transport='pinned_https_over_usb_adb_reverse';wireless_proof=$false;physical_proof=$false;full_s1_complete=$false;open_s1_gates=18})
        }catch{$s75Exit=3}
    }
}
Write-Output ("S1 owned75: "+$s75Outcome+"; exit="+$s75Exit+"; full S1 remains open.")
exit $s75Exit
