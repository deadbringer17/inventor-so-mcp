# Runs Unity 6000.6.3f1 in batch mode on this project and waits for it (Unity.exe is a GUI program).
param(
    [Parameter(Mandatory = $true)][string[]]$Arguments,
    [string]$Log = (Join-Path $env:TEMP "xrso-unity.log")
)
$unity = "C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe"
$project = Split-Path -Parent $PSScriptRoot
$all = @("-batchmode", "-projectPath", "`"$project`"", "-logFile", "`"$Log`"") + $Arguments
$process = Start-Process -FilePath $unity -ArgumentList $all -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0) { Get-Content $Log -Tail 80 }
exit $process.ExitCode
