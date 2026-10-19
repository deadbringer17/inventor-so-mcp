# Runs Unity 6000.6.3f1 in batch mode on this project and waits for it (Unity.exe is a GUI program).
param(
    [Parameter(Mandatory = $true)][string[]]$Arguments,
    [string]$Log = (Join-Path $env:TEMP "xrso-unity.log")
)
$unity = "C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe"
$project = Split-Path -Parent $PSScriptRoot
$quotedArguments = @($Arguments | ForEach-Object {
    if ($_ -match '\s' -and $_ -notmatch '^".*"$') { '"' + $_ + '"' } else { $_ }
})
$all = @("-batchmode", "-projectPath", "`"$project`"", "-logFile", "`"$Log`"") + $quotedArguments
$process = Start-Process -FilePath $unity -ArgumentList $all -PassThru -WindowStyle Hidden
# Wait for the editor itself: -Wait also waits for descendant licensing helpers,
# which can remain alive after Unity has saved the results and exited.
$process.WaitForExit()
$process.Refresh()
if ($process.ExitCode -ne 0) { Get-Content $Log -Tail 80 }
exit $process.ExitCode
