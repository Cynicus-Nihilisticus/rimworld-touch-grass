<#
.SYNOPSIS
  Copies only the files the Steam Workshop needs into a clean folder.

.PARAMETER OutDir
  Destination folder, the one you upload to Steam. Defaults to
  <RimWorld>\Mods\rimworld-touch-grass (the game loads it from there, so it
  must be the only copy in Mods; keep the git clone outside Mods).
#>
param(
    [string]$OutDir = $(
        $mods = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods'
        if (Test-Path -LiteralPath $mods) { Join-Path $mods 'rimworld-touch-grass' }
        else { Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'TouchGrass-WorkshopRelease' }
    )
)
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot

if ((Resolve-Path -LiteralPath (Split-Path -Parent $OutDir) -ErrorAction SilentlyContinue) -and
    ([IO.Path]::GetFullPath($OutDir).TrimEnd('\') -ieq [IO.Path]::GetFullPath($modRoot).TrimEnd('\'))) {
    throw 'OutDir is the git clone itself; it would be deleted. Keep the clone outside Mods.'
}
# Never delete a git folder in OutDir, and never copy one into it.
$guarded = @($OutDir) + @('About', '1.6', '1.7', 'Languages' | ForEach-Object { Join-Path $modRoot $_ })
foreach ($folder in $guarded) {
    if (-not (Test-Path -LiteralPath $folder)) { continue }
    $git = Get-ChildItem -LiteralPath $folder -Recurse -Force |
        Where-Object { $_.Name -eq '.git' } | Select-Object -First 1
    if ($git) {
        throw "Found $($git.FullName). Move it elsewhere first; the upload folder must not contain git data."
    }
}
foreach ($dll in '1.6\Assemblies\TouchGrass.dll', '1.7\Assemblies\TouchGrass.dll') {
    if (-not (Test-Path -LiteralPath (Join-Path $modRoot $dll))) {
        throw "Missing $dll. Run Build.ps1 first."
    }
}

# Keep an existing PublishedFileId.txt so later uploads update the same item.
$about = Join-Path $OutDir 'About'
$keepId = $null
$idPath = Join-Path $about 'PublishedFileId.txt'
if (Test-Path -LiteralPath $idPath) { $keepId = Get-Content -LiteralPath $idPath -Raw }

if (Test-Path -LiteralPath $OutDir) { Remove-Item -LiteralPath $OutDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

foreach ($name in 'About', '1.6', '1.7', 'Languages') {
    Copy-Item -LiteralPath (Join-Path $modRoot $name) -Destination (Join-Path $OutDir $name) -Recurse
}
Copy-Item -LiteralPath (Join-Path $modRoot 'LICENSE') -Destination $OutDir

if ($keepId) { Set-Content -LiteralPath $idPath -Value $keepId -NoNewline }

Write-Host "Workshop release folder ready: $OutDir" -ForegroundColor Green
Get-ChildItem -LiteralPath $OutDir -Recurse -File | ForEach-Object {
    '  ' + $_.FullName.Substring($OutDir.Length + 1)
}
$bytes = (Get-ChildItem -LiteralPath $OutDir -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ('Total size: {0:N0} KB' -f ($bytes / 1KB))
$pv = Join-Path $about 'preview.png'
if ((Test-Path -LiteralPath $pv) -and (Get-Item -LiteralPath $pv).Length -gt 1MB) {
    Write-Warning 'About\preview.png is over 1 MB; the Workshop may reject it.'
}
if (Test-Path -LiteralPath $idPath) {
    $itemId = (Get-Content -LiteralPath $idPath -Raw).Trim()
    Write-Host "Uploading this folder updates Workshop item $itemId."
    Write-Host 'For a brand-new item, delete About\PublishedFileId.txt before uploading.'
}
else {
    Write-Host 'No About\PublishedFileId.txt: uploading creates a new Workshop item.'
    Write-Host "Afterwards, copy the new file into the clone's About folder."
}
