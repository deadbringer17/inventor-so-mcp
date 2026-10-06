[CmdletBinding()]
param([string]$PackDirectory = (Join-Path $PSScriptRoot '../artifacts/m10-icons/pack'))
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$mapping = Get-Content -LiteralPath (Join-Path $repo 'assets/inventor-icons/m10-catalog.json') -Raw | ConvertFrom-Json
$manifest = Get-Content -LiteralPath (Join-Path $PackDirectory 'manifest.json') -Raw | ConvertFrom-Json
$target = Join-Path $repo 'Inventor XR SO/Assets/XrSo/Ui/Resources/InventorIcons'
$selected = @()
$keys = @{}
# Validate all entries before copying a partially usable set into Unity.
foreach ($icon in $mapping.icons) {
    if ($icon.key -notmatch '^[a-z][a-z0-9-]*$' -or $keys.ContainsKey($icon.key)) { throw "Invalid or repeated key: $($icon.key)" }
    $keys[$icon.key] = $true
    $entries = @($manifest.images | Where-Object { $_.variant -eq $mapping.variant -and $_.resource -ceq $icon.resource })
    if ($entries.Count -ne 1) { throw "Expected one source for $($icon.key), found $($entries.Count)" }
    $entry = $entries[0]
    $path = Join-Path $PackDirectory $entry.png
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -cne $entry.pngSha256 -or $entry.width -ne 32 -or $entry.height -ne 32) { throw "Invalid PNG for $($icon.key)" }
    $selected += [ordered]@{ key = $icon.key; action = $icon.action; actions = @($icon.actions); label = $icon.label; resource = $entry.resource;
        source = $entry.source; variant = $entry.variant; width = $entry.width; height = $entry.height;
        originalSha256 = $entry.originalSha256; pngSha256 = $hash; packPng = $entry.png }
}
[IO.Directory]::CreateDirectory($target) | Out-Null
foreach ($entry in $selected) {
    $destination = Join-Path $target ($entry.key + '.png')
    if (-not (Test-Path -LiteralPath $destination) -or (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant() -cne $entry.pngSha256) {
        Copy-Item -LiteralPath (Join-Path $PackDirectory $entry.packPng) -Destination $destination
    }
}
$provenance = [ordered]@{ schemaVersion = 1; product = $manifest.product; sources = $manifest.sources;
    notice = $manifest.notice; icons = $selected }
$uiRoot = Join-Path $repo 'Inventor XR SO/Assets/XrSo/Ui'
[IO.File]::WriteAllText((Join-Path $uiRoot 'inventor-icons-provenance.json'),
    (ConvertTo-Json -InputObject $provenance -Depth 8), [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $uiRoot 'InventorIcons-NOTICE.txt'), $manifest.notice, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $uiRoot 'Resources/InventorIconsManifest.json'),
    (ConvertTo-Json -InputObject $provenance -Depth 8), [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $uiRoot 'Resources/InventorIconsNotice.txt'), $manifest.notice, [Text.UTF8Encoding]::new($false))
Write-Host "$($selected.Count) verified icons prepared for Unity; the M10 importer creates Sprite metadata."
