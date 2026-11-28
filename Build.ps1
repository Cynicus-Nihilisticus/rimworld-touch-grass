[CmdletBinding()]
param(
    # Compile only; do not copy the DLL into 1.6\Assemblies or 1.7\Assemblies.
    [switch]$NoDeploy,

    # Skip the post-build API surface check (needs Python 3).
    [switch]$SkipApiCheck,

    # Defaults to two folders above this one (RimWorld\Mods\<this mod>) when
    # that is a RimWorld install, otherwise to the default Steam install.
    [string]$RimWorldDir,

    # Defaults to the Steam Workshop Harmony build for the installed game version.
    [string]$HarmonyDll
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$modRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($RimWorldDir)) {
    $RimWorldDir = [IO.Path]::GetFullPath((Join-Path $modRoot '..\..'))
    $steamInstall = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld'
    if (-not (Test-Path -LiteralPath (Join-Path $RimWorldDir 'Version.txt')) -and
        (Test-Path -LiteralPath (Join-Path $steamInstall 'Version.txt'))) {
        $RimWorldDir = $steamInstall
    }
}

$steamAppsRoot = Split-Path -Parent (Split-Path -Parent $RimWorldDir)
$versionPath = Join-Path $RimWorldDir 'Version.txt'
$sourceRoot = Join-Path $modRoot 'Source'
$buildRoot = Join-Path $modRoot 'Build\Release'
$buildDll = Join-Path $buildRoot 'TouchGrass.dll'

# The .NET Framework compiler that ships with Windows speaks C# 5 only. The
# sources are deliberately written in C# 5 (no ?., no $"", no nameof, no
# expression-bodied members) so no SDK has to be installed to build this mod.
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

Write-Host '=== Touch Grass build ===' -ForegroundColor Cyan

if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw "The .NET Framework C# compiler was not found: $compiler"
}

if (-not (Test-Path -LiteralPath $versionPath -PathType Leaf)) {
    throw "RimWorld Version.txt was not found: $versionPath (pass -RimWorldDir)."
}
$installedVersion = [IO.File]::ReadAllText($versionPath).Trim()
# The same sources build for both game versions; the installed game decides which
# one. 1.6 runs on Unity's Mono (mscorlib + UnityEngine modules); 1.7 runs on
# .NET 10 with a Unity compatibility layer (UnityStubs).
if ($installedVersion -match '^1\.6(?:\.|\s|$)') {
    $gameVersion = '1.6'
}
elseif ($installedVersion -match '^1\.7(?:\.|\s|$)') {
    $gameVersion = '1.7'
}
else {
    throw "Installed RimWorld is '$installedVersion'; this build supports 1.6 and 1.7."
}
$deployRoot = Join-Path $modRoot "$gameVersion\Assemblies"
$deployedDll = Join-Path $deployRoot 'TouchGrass.dll'
Write-Host "[OK] RimWorld $installedVersion (building the $gameVersion DLL)" -ForegroundColor Green

# --- content validation ------------------------------------------------------
$aboutPath = Join-Path $modRoot 'About\About.xml'
$languageDir = Join-Path $modRoot 'Languages\English\Keyed'
$xmlFiles = @($aboutPath) + @(Get-ChildItem -LiteralPath $languageDir -Filter '*.xml' |
    ForEach-Object { $_.FullName })
foreach ($xmlPath in $xmlFiles) {
    if (-not (Test-Path -LiteralPath $xmlPath -PathType Leaf)) {
        throw "Required content file is missing: $xmlPath"
    }
    try {
        [xml](Get-Content -LiteralPath $xmlPath -Raw -Encoding UTF8) | Out-Null
    }
    catch {
        throw "Invalid XML in '$xmlPath': $($_.Exception.Message)"
    }
}
[xml]$about = Get-Content -LiteralPath $aboutPath -Raw -Encoding UTF8
if ($about.ModMetaData.packageId -ne 'Cynicus.TouchGrass') {
    throw 'About.xml packageId must remain Cynicus.TouchGrass.'
}
if ($gameVersion -notin @($about.ModMetaData.supportedVersions.li)) {
    throw "About.xml supportedVersions must include $gameVersion."
}
if ('brrainz.harmony' -notin @($about.ModMetaData.modDependencies.li.packageId)) {
    throw 'About.xml must retain Harmony as a hard dependency.'
}

# Every translation key used by the code must exist in the Keyed language files.
$usedKeys = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Filter '*.cs' |
    Select-String -Pattern '"(TG_[A-Za-z0-9_]+)"' -AllMatches |
    ForEach-Object { $_.Matches } |
    ForEach-Object { $_.Groups[1].Value } |
    Sort-Object -Unique)
$definedKeys = @()
foreach ($xmlPath in $xmlFiles | Where-Object { $_ -like "$languageDir*" }) {
    [xml]$lang = Get-Content -LiteralPath $xmlPath -Raw -Encoding UTF8
    $definedKeys += @($lang.LanguageData.ChildNodes |
        Where-Object { $_.NodeType -eq 'Element' } |
        ForEach-Object { $_.Name })
}
$missingKeys = @($usedKeys | Where-Object { $_ -notin $definedKeys })
if ($missingKeys.Count -gt 0) {
    throw "Translation keys used in code but missing from Languages: $($missingKeys -join ', ')"
}
Write-Host "[OK] XML, package metadata and $($usedKeys.Count) translation keys validated." -ForegroundColor Green

# --- references --------------------------------------------------------------
# Compiling against the game's own assemblies, and nothing else, produces a DLL
# that binds to exactly what the running game provides.
if ([string]::IsNullOrWhiteSpace($HarmonyDll)) {
    $HarmonyDll = Join-Path $steamAppsRoot `
        "workshop\content\294100\2009463077\$gameVersion\Assemblies\0Harmony.dll"
}
if ($gameVersion -eq '1.7') {
    $gameReferences = @(
        'System.Private.CoreLib.dll',
        'System.Runtime.dll',
        'System.Runtime.Extensions.dll',
        'System.Collections.dll',
        'System.Linq.dll',
        'System.Reflection.dll',
        'System.Globalization.dll',
        'System.Diagnostics.Tools.dll',
        'Assembly-CSharp.dll',
        'UnityStubs.dll'
    ) | ForEach-Object { Join-Path $RimWorldDir $_ }
    $versionDefine = 'RIMWORLD_1_7'
}
else {
    $managedRoot = Join-Path $RimWorldDir 'RimWorldWin64_Data\Managed'
    $gameReferences = @(
        'mscorlib.dll',
        'System.dll',
        'System.Core.dll',
        'netstandard.dll',
        'Assembly-CSharp.dll',
        'UnityEngine.dll',
        'UnityEngine.CoreModule.dll',
        'UnityEngine.IMGUIModule.dll',
        'UnityEngine.TextRenderingModule.dll'
    ) | ForEach-Object { Join-Path $managedRoot $_ }
    $versionDefine = 'RIMWORLD_1_6'
}
$referencePaths = @($gameReferences) + @($HarmonyDll)
foreach ($referencePath in $referencePaths) {
    if (-not (Test-Path -LiteralPath $referencePath -PathType Leaf)) {
        throw "Required compiler reference is missing: $referencePath"
    }
}

$sourceFiles = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]' } |
    Sort-Object FullName)
if ($sourceFiles.Count -eq 0) {
    throw "No C# source files were found below $sourceRoot"
}

if (-not (Test-Path -LiteralPath $buildRoot -PathType Container)) {
    [void](New-Item -ItemType Directory -Path $buildRoot -Force)
}
if (Test-Path -LiteralPath $buildDll -PathType Leaf) {
    Remove-Item -LiteralPath $buildDll -Force
}

$compilerArguments = @(
    '/nologo',
    '/noconfig',
    '/nostdlib+',
    '/codepage:65001',
    '/target:library',
    '/platform:anycpu',
    '/langversion:5',
    '/optimize+',
    '/debug-',
    '/warn:4',
    '/warnaserror+',
    # CS1684/CS1685: benign duplicate-definition notes that appear when both
    # the System.Runtime facade and System.Private.CoreLib are referenced.
    '/nowarn:1684,1685',
    "/define:$versionDefine",
    "/out:$buildDll"
)
foreach ($referencePath in $referencePaths) {
    $compilerArguments += "/reference:$referencePath"
}
foreach ($sourceFile in $sourceFiles) {
    $compilerArguments += $sourceFile.FullName
}

Write-Host "Compiling $($sourceFiles.Count) source file(s)..."
& $compiler @compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw "C# compilation failed with exit code $LASTEXITCODE. No DLL was deployed."
}
if (-not (Test-Path -LiteralPath $buildDll -PathType Leaf)) {
    throw 'The compiler reported success but did not produce TouchGrass.dll.'
}

$builtHash = (Get-FileHash -LiteralPath $buildDll -Algorithm SHA256).Hash
Write-Host "[OK] Built: $buildDll" -ForegroundColor Green
Write-Host "     SHA-256 $builtHash"

# --- API surface check --------------------------------------------------------
if (-not $SkipApiCheck) {
    $apiCheck = Join-Path $modRoot 'tools\ApiCheck\apicheck.py'
    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($null -eq $python) {
        Write-Host '[--] Python not found; skipped the API surface check.' -ForegroundColor Yellow
    }
    else {
        & $python.Source -I $apiCheck `
            --dll $buildDll `
            --game $RimWorldDir `
            --harmony $HarmonyDll
        if ($LASTEXITCODE -ne 0) {
            throw 'API surface check failed. No DLL was deployed.'
        }
    }
}

if ($NoDeploy) {
    Write-Host '[OK] Compile-only build complete; deployment was skipped.' -ForegroundColor Yellow
    return
}

$runningGame = Get-Process -Name 'RimWorld', 'RimWorldWin64' -ErrorAction SilentlyContinue
if ($null -ne $runningGame) {
    throw 'RimWorld is running. Compilation succeeded, but deployment was refused. Close the game and run Build.ps1 again.'
}

if (-not (Test-Path -LiteralPath $deployRoot -PathType Container)) {
    [void](New-Item -ItemType Directory -Path $deployRoot -Force)
}
Copy-Item -LiteralPath $buildDll -Destination $deployedDll -Force

$deployedHash = (Get-FileHash -LiteralPath $deployedDll -Algorithm SHA256).Hash
if ($builtHash -cne $deployedHash) {
    throw "Deployment hash mismatch. Built: $builtHash; deployed: $deployedHash"
}

Write-Host "[OK] Deployed: $deployedDll" -ForegroundColor Green
Write-Host "[OK] SHA-256 verified: $deployedHash" -ForegroundColor Green
Write-Host 'Restart RimWorld after rebuilding.'
