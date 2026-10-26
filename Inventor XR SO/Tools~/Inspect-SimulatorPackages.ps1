#requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$QaApk,
    [Parameter(Mandatory)][string]$OrdinaryApk,
    [Parameter(Mandatory)][string]$OutputJson
)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputJson) { throw 'Choose a new inspection report path; preserve existing evidence.' }
$runnerNames = @('QuestAcceptanceRunner','M1QuestAcceptance','M2QuestAcceptance','M3QuestAcceptance',
    'M4QuestAcceptance','M5QuestAcceptance','M6QuestAcceptance','M7QuestAcceptance','M8QuestAcceptance',
    'M9QuestAcceptance','M9NestedQuestAcceptance','M9FlexQuestAcceptance','M9HiddenQuestAcceptance',
    'M10QuestAcceptance','S1QuestAcceptance')
$packages = foreach ($item in @(@{kind='qa';path=$QaApk},@{kind='ordinary';path=$OrdinaryApk})) {
    $apk = Get-Item -LiteralPath $item.path
    $zip = [IO.Compression.ZipFile]::OpenRead($apk.FullName)
    try {
        $metadata = $zip.GetEntry('assets/bin/Data/Managed/Metadata/global-metadata.dat')
        if ($null -eq $metadata) { throw "IL2CPP metadata missing: $($apk.Name)" }
        $stream = $metadata.Open(); $buffer = [IO.MemoryStream]::new()
        try { $stream.CopyTo($buffer); $strings = [Text.Encoding]::UTF8.GetString($buffer.ToArray()) }
        finally { $stream.Dispose(); $buffer.Dispose() }
        $runners = [ordered]@{}
        foreach ($name in $runnerNames) { $runners[$name] = $strings.Contains(([string][char]0) + $name + ([string][char]0)) }
        [ordered]@{ kind=$item.kind;path=$apk.FullName;bytes=$apk.Length;sha256=(Get-FileHash -LiteralPath $apk.FullName -Algorithm SHA256).Hash;
            arm64_il2cpp=($null -ne $zip.GetEntry('lib/arm64-v8a/libil2cpp.so'));metadata_bytes=$metadata.Length;runner_markers=$runners }
    }
    finally { $zip.Dispose() }
}
$qa = $packages | Where-Object kind -eq qa
$ordinary = $packages | Where-Object kind -eq ordinary
$passed = $qa.arm64_il2cpp -and $ordinary.arm64_il2cpp -and @($qa.runner_markers.Values | Where-Object { -not $_ }).Count -eq 0 `
    -and @($ordinary.runner_markers.Values | Where-Object { $_ }).Count -eq 0
$report = [ordered]@{passed=$passed;inspection='ARM64 library and exact null-delimited runner names in IL2CPP metadata; no hardware execution';packages=$packages}
$reportPath = [IO.Path]::GetFullPath($OutputJson)
New-Item -ItemType Directory -Path (Split-Path -Parent $reportPath) -Force | Out-Null
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath
if (-not $passed) { throw "APK inspection failed; see $reportPath" }
Write-Output "PASS: QA runner metadata present; ordinary runner metadata absent. $reportPath"
