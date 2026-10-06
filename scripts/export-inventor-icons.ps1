# M10: extract named .NET image resources without connecting to Inventor.
[CmdletBinding()]
param(
    [string]$InventorBin,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/m10-icons'),
    [ValidateSet('color', 'light', 'dark')][string[]]$Variants = @('color', 'light', 'dark'),
    [switch]$SkipZip
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (-not $InventorBin) {
    $install = (Get-ItemProperty -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Autodesk\Inventor\RegistryVersion31.0').InstallLocation
    $InventorBin = Join-Path $install 'Bin'
}
$InventorBin = [IO.Path]::GetFullPath($InventorBin)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$packDirectory = Join-Path $OutputDirectory 'pack'
[IO.Directory]::CreateDirectory($packDirectory) | Out-Null
$libraries = @{ color = 'InvAIRLookColorImages.dll'; light = 'InvAIRLookImages.dll'; dark = 'InvAIRLookImagesDark.dll' }
$sources = [Collections.Generic.List[object]]::new()
$images = [Collections.Generic.List[object]]::new()
$failures = [Collections.Generic.List[object]]::new()
$skipped = [Collections.Generic.List[object]]::new()
function Get-BytesHash([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
foreach ($variant in ($Variants | Select-Object -Unique)) {
    $dllPath = Join-Path $InventorBin $libraries[$variant]
    if (-not (Test-Path -LiteralPath $dllPath)) { throw "Missing icon library: $dllPath" }
    $dllHash = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($dllPath).FileVersion
    $sources.Add([ordered]@{ variant = $variant; file = $libraries[$variant]; version = $version; sha256 = $dllHash })
    $assembly = [Reflection.Assembly]::LoadFile($dllPath)
    foreach ($resourceName in $assembly.GetManifestResourceNames()) {
        if (-not $resourceName.EndsWith('.g.resources')) { continue }
        $stream = $assembly.GetManifestResourceStream($resourceName)
        $reader = [Resources.ResourceReader]::new($stream)
        try {
            $entries = $reader.GetEnumerator()
            while ($entries.MoveNext()) {
                $key = [string]$entries.Key
                if ($key -notmatch '\.(ico|png|bmp)$') { continue }
                $image = $null; $icon = $null; $memory = $null
                try {
                    # Preserve the original bytes separately from the decoded PNG.
                    $original = [IO.MemoryStream]::new()
                    try {
                        $value = $entries.Value
                        if ($value -isnot [IO.Stream]) { throw "Unsupported resource value: $($value.GetType())" }
                        $value.Position = 0
                        $value.CopyTo($original)
                        $bytes = $original.ToArray()
                    } finally { $original.Dispose() }
                    $safeKey = $key.Replace('\', '/')
                    if ($safeKey.StartsWith('/') -or $safeKey.Split('/') -contains '..') { throw 'Invalid resource path' }
                    $originalRelative = "$variant/original/$safeKey"
                    $pngRelative = "$variant/png/" + [IO.Path]::ChangeExtension($safeKey, '.png')
                    $originalPath = Join-Path $packDirectory $originalRelative
                    $pngPath = Join-Path $packDirectory $pngRelative
                    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($originalPath)) | Out-Null
                    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($pngPath)) | Out-Null
                    [IO.File]::WriteAllBytes($originalPath, $bytes)
                    $memory = [IO.MemoryStream]::new($bytes, $false)
                    # Some Autodesk resources have .ico names but contain BMP/PNG bytes.
                    $isIco = $bytes.Length -ge 6 -and $bytes[0] -eq 0 -and $bytes[1] -eq 0 -and $bytes[2] -eq 1 -and $bytes[3] -eq 0
                    if ($isIco -and $bytes[4] -eq 0 -and $bytes[5] -eq 0) {
                        $skipped.Add([ordered]@{ variant = $variant; resource = $key; original = $originalRelative;
                            originalSha256 = (Get-BytesHash $bytes); reason = 'Empty ICO container (zero images); original preserved.' })
                        continue
                    }
                    if ($isIco) {
                        $icon = [Drawing.Icon]::new($memory)
                        $image = $icon.ToBitmap()
                    } else { $image = [Drawing.Image]::FromStream($memory) }
                    $image.Save($pngPath, [Drawing.Imaging.ImageFormat]::Png)
                    $images.Add([ordered]@{ variant = $variant; source = $libraries[$variant]; resource = $key;
                        width = $image.Width; height = $image.Height; original = $originalRelative;
                        originalSha256 = (Get-BytesHash $bytes); png = $pngRelative;
                        pngSha256 = (Get-FileHash -LiteralPath $pngPath -Algorithm SHA256).Hash.ToLowerInvariant() })
                } catch { $failures.Add([ordered]@{ variant = $variant; resource = $key; error = $_.Exception.Message }) }
                finally {
                    if ($image) { $image.Dispose() }; if ($icon) { $icon.Dispose() }; if ($memory) { $memory.Dispose() }
                }
            }
        } finally { $reader.Dispose(); $stream.Dispose() }
    }
    Write-Host "$variant extracted; cumulative images=$($images.Count), errors=$($failures.Count)"
}
$orderedImages = @($images | Sort-Object variant, resource)
$manifest = [ordered]@{ schemaVersion = 1; product = 'Autodesk Inventor 2027';
    generatedAtUtc = [DateTime]::UtcNow.ToString('o'); sources = @($sources.ToArray());
    notice = 'Autodesk proprietary resources from a local installation. No redistribution license has been verified. Not covered by repository MIT/Apache licenses.';
    images = $orderedImages; skipped = @($skipped.ToArray()); failures = @($failures.ToArray()) }
$json = ConvertTo-Json -InputObject $manifest -Depth 8
[IO.File]::WriteAllText((Join-Path $packDirectory 'manifest.json'), $json, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $packDirectory 'NOTICE.txt'), $manifest.notice, [Text.UTF8Encoding]::new($false))
# Embed JSON so the catalogue also works through file://, without a fetch or server.
$catalogueData = (ConvertTo-Json -InputObject $orderedImages -Depth 5 -Compress).Replace('<', '\u003c')
$html = @'
<!doctype html><html lang="it"><meta charset="utf-8"><title>M10 — icone Inventor 2027</title>
<style>body{font:16px system-ui;background:#0d2030;color:#f4f8fb;margin:24px}input,select{font:inherit;padding:10px;margin:8px}#grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(240px,1fr));gap:10px}.card{background:#1a3a4a;padding:14px;overflow-wrap:anywhere}.card img{width:32px;height:32px;object-fit:contain;margin-right:14px}.card small{display:block;margin-top:10px}a{color:#fdd11b}</style>
<h1>M10 — icone Inventor 2027</h1><p>Risorse Autodesk da installazione locale. Licenza di redistribuzione non verificata.</p>
<input id="query" placeholder="Cerca: extrude, flange, sketch…"><select id="variant"><option value="">Tutte le varianti</option><option>color</option><option>light</option><option>dark</option></select>
<select id="size"><option value="32">32 pixel</option><option value="16">16 pixel</option><option value="">Tutte le dimensioni</option></select><p id="count"></p><div id="grid"></div>
<script id="data" type="application/json">__DATA__</script><script>
const items=JSON.parse(document.getElementById('data').textContent),q=document.getElementById('query'),v=document.getElementById('variant'),s=document.getElementById('size'),grid=document.getElementById('grid');
function render(){const found=items.filter(i=>(!v.value||i.variant===v.value)&&(!s.value||i.width===Number(s.value))&&i.resource.toLowerCase().includes(q.value.toLowerCase()));document.getElementById('count').textContent=found.length+' icone';grid.replaceChildren();for(const i of found){const c=document.createElement('div');c.className='card';const img=document.createElement('img');img.src=i.png;img.loading='lazy';img.alt=i.resource;const a=document.createElement('a');a.href=i.png;a.textContent=i.resource.split('/').pop();const note=document.createElement('small');note.textContent=i.variant+' · '+i.width+'×'+i.height;c.append(img,a,note);grid.append(c)}}q.oninput=v.onchange=s.onchange=render;render();</script></html>
'@
[IO.File]::WriteAllText((Join-Path $packDirectory 'index.html'), $html.Replace('__DATA__', $catalogueData), [Text.UTF8Encoding]::new($false))
if (-not $SkipZip) {
    $zipPath = Join-Path $OutputDirectory 'inventor-2027-icons.zip'
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath }
    [IO.Compression.ZipFile]::CreateFromDirectory($packDirectory, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)
    Write-Host "ZIP: $zipPath"
}
Write-Host "Catalogue: $(Join-Path $packDirectory 'index.html'); empty containers skipped=$($skipped.Count)"
if ($failures.Count -gt 0) { throw "$($failures.Count) images could not be converted; see manifest failures." }
