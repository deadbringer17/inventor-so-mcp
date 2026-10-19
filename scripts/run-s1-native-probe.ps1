param([string]$ReportRoot = '',[switch]$Isolated,[switch]$PatternExpressions,[switch]$PatternIndependentOptions,[switch]$PatternMultipleSuppression,
    [ValidateSet('all','constraints','dimensions','transforms','stretch','offsets','patterns','scale-api','midpoint','state',
        'ellipse-ui-prepare','ellipse-ui-prepare-sheet','ellipse-ui-next','ellipse-ui-top','ellipse-ui-read','ellipse-ui-api','ellipse-ui-batch','ellipse-ui-cleanup',
        'arc-ui-prepare','arc-ui-prepare-sheet','arc-ui-next','arc-ui-top','arc-ui-read','arc-ui-batch','arc-ui-threepoint-batch','arc-ui-spline-batch','arc-ui-spline-methods','arc-ui-spline-mixed-batch','arc-ui-spline-constraints','arc-ui-spline-options','arc-ui-spline-readonly-batch','arc-ui-topology-reconstruct','arc-ui-topology-relations','arc-ui-topology-constraint-replay','arc-ui-line-split-dependencies','arc-ui-fillet-research','arc-ui-fillet-radius','arc-ui-bmp-images','arc-ui-bmp-lifecycle','arc-ui-bmp-product','arc-ui-cleanup',
        'slot-ui-prepare','slot-ui-prepare-sheet','slot-ui-next','slot-ui-top','slot-ui-read','slot-ui-batch','slot-ui-cleanup',
        'polygon-ui-prepare','polygon-ui-prepare-sheet','polygon-ui-next','polygon-ui-top','polygon-ui-read','polygon-ui-batch','polygon-ui-cleanup',
        'arcslot-ui-prepare','arcslot-ui-prepare-sheet','arcslot-ui-next','arcslot-ui-top','arcslot-ui-read','arcslot-ui-batch','arcslot-ui-cleanup')][string]$Variant='all')

$ErrorActionPreference = 'Stop'
if($Isolated -and ($Variant.StartsWith('ellipse-ui-') -or $Variant.StartsWith('arc-ui-') -or $Variant.StartsWith('slot-ui-') -or $Variant.StartsWith('polygon-ui-') -or $Variant.StartsWith('arcslot-ui-'))) {throw 'Staged desktop oracle requires the already-open Inventor instance; do not use -Isolated.'}
if(($PatternExpressions -or $PatternIndependentOptions -or $PatternMultipleSuppression) -and $Variant -ne 'patterns') {throw 'Pattern options require -Variant patterns.'}
$s1Repo = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ReportRoot)) { $ReportRoot = Join-Path $s1Repo 'artifacts/s1-verification' }
$s1ReportRoot = [IO.Path]::GetFullPath($ReportRoot)
New-Item -ItemType Directory -Path $s1ReportRoot -Force | Out-Null
$s1Plugin = Join-Path $s1Repo 'bridge/src/plugin-so27/Inventor.So.AddIn.csproj'
$s1Probe = Join-Path $s1Repo 'bridge/tests/S1LiveProbe/S1LiveProbe.csproj'

# Compile only: the probe loads this assembly out of process and never deploys it to Inventor.
& dotnet build $s1Plugin -p:SoExperimental=true --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'S1 experimental add-in build failed.' }
$s1ReportName=if($Variant -eq 'all') {'native-probe.json'} elseif($PatternMultipleSuppression) {'native-patterns-multiple-suppression-probe.json'} elseif($PatternIndependentOptions) {'native-patterns-independent-options-probe.json'} elseif($PatternExpressions) {'native-patterns-expressions-probe.json'} else {'native-'+$Variant+'-probe.json'}
# Keep accepted and failed runs immutable, including the frozen native-probe.json baseline.
if(Test-Path -LiteralPath (Join-Path $s1ReportRoot $s1ReportName)) {
    $s1ReportName=[IO.Path]::GetFileNameWithoutExtension($s1ReportName)+'-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff')+'.json'
}
$s1ProbeArguments=@((Join-Path $s1ReportRoot $s1ReportName))
if($Variant -ne 'all') { $s1ProbeArguments+=$Variant }
if($Isolated) { $s1ProbeArguments+='isolated' }
$s1PreviousExpressions=$env:S1_PROBE_PATTERN_EXPRESSIONS
$s1PreviousIndependent=$env:S1_PROBE_PATTERN_INDEPENDENT_OPTIONS
$s1PreviousSuppression=$env:S1_PROBE_PATTERN_MULTIPLE_SUPPRESSION
try {
    $env:S1_PROBE_PATTERN_EXPRESSIONS=if($PatternExpressions){'1'}else{'0'}
    $env:S1_PROBE_PATTERN_INDEPENDENT_OPTIONS=if($PatternIndependentOptions){'1'}else{'0'}
    $env:S1_PROBE_PATTERN_MULTIPLE_SUPPRESSION=if($PatternMultipleSuppression){'1'}else{'0'}
    & dotnet run --project $s1Probe -- @s1ProbeArguments
    if ($LASTEXITCODE -ne 0) { throw ('S1 native probe failed; inspect '+$s1ReportName+', including previous_restored.') }
} finally {
    $env:S1_PROBE_PATTERN_EXPRESSIONS=$s1PreviousExpressions
    $env:S1_PROBE_PATTERN_INDEPENDENT_OPTIONS=$s1PreviousIndependent
    $env:S1_PROBE_PATTERN_MULTIPLE_SUPPRESSION=$s1PreviousSuppression
}
Write-Output ('S1 native evidence: ' + (Join-Path $s1ReportRoot $s1ReportName))


