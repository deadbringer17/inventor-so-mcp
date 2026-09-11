[CmdletBinding()]
param([string]$DotnetPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $DotnetPath) {
    $localSdk = Join-Path $repo '.tools\dotnet\dotnet.exe'
    $DotnetPath = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
}
$package = Join-Path $repo ('artifacts\inventor-so-mcp-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
if (Test-Path -LiteralPath $package) { throw 'Package directory already exists. Retry with a new timestamp.' }
& $DotnetPath publish (Join-Path $repo 'bridge\src\server\Bimwright.Ipt.Server.csproj') -c Release --self-contained false -o (Join-Path $package 'server') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Server publish failed' }
& $DotnetPath publish (Join-Path $repo 'bridge\src\plugin-so27\Inventor.So.AddIn.csproj') -c Release --self-contained false -o (Join-Path $package 'addin') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Inventor 2027 add-in publish failed' }
if (Get-ChildItem -LiteralPath $package -Recurse -Filter 'Autodesk.Inventor.Interop.dll') {
    throw 'Unexpected redistributable interop detected; package must not be distributed.'
}
Copy-Item -LiteralPath (Join-Path $repo 'bridge\LICENSE') -Destination (Join-Path $package 'LICENSE')
Copy-Item -LiteralPath (Join-Path $repo 'bridge\NOTICE') -Destination (Join-Path $package 'NOTICE')
Write-Output ('Built package (not installed): ' + $package)
