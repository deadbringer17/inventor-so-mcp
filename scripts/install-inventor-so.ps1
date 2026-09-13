[CmdletBinding()]
param([Parameter(Mandatory)][string]$Package)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$packagePath = (Resolve-Path -LiteralPath $Package).Path
$source = Join-Path $packagePath 'addin'
$manifestPath = Join-Path $source 'Inventor.So.addin'
[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
if ($manifest.Addin.ClassId -ne '{7EE3B69B-8A24-42DD-9422-50E8C5279F40}') { throw 'Unexpected add-in identity' }
if (-not (Test-Path -LiteralPath (Join-Path $source 'Inventor.So.AddIn.dll'))) { throw 'Missing add-in binary' }
$version = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
$stage = Join-Path $env:LOCALAPPDATA ('InventorSO\packages\' + $version)
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item -LiteralPath $source -Destination (Join-Path $stage 'addin') -Recurse
Copy-Item -LiteralPath (Join-Path $packagePath 'server') -Destination (Join-Path $stage 'server') -Recurse
Copy-Item -LiteralPath (Join-Path $packagePath 'LICENSE'),(Join-Path $packagePath 'NOTICE') -Destination $stage
$installDir = Join-Path $env:APPDATA 'Autodesk\Inventor 2027\Addins\InventorSO'
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
$installedManifest = Join-Path $installDir 'Inventor.So.addin'
if (Test-Path -LiteralPath $installedManifest) {
    # Keep rollback metadata OUTSIDE Inventor's recursively scanned Addins directory.
    Copy-Item -LiteralPath $installedManifest -Destination (Join-Path $stage 'previous-manifest.xml')
}
$manifest.SelectSingleNode('/Addin/Assembly').InnerText = [string](Join-Path $stage 'addin\Inventor.So.AddIn.dll')
$manifest.Save($installedManifest)
Write-Output ('Installed manifest points to: ' + $manifest.Addin.Assembly)
Write-Output ('MCP command: dotnet "' + (Join-Path $stage 'server\Inventor.So.Mcp.Server.dll') + '" --target 2027 --read-only --disable-toolbaker')
Write-Output 'Running Inventor may retain the previous assembly. Restart normally after saving your work to load this version. This script never closes Inventor.'
