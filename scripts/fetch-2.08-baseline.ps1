#Requires -Version 7
<#
.SYNOPSIS
    Downloads and verifies the Swiss Ephemeris 2.08 C baseline used to diff against the
    2.10.03 upstream vendored at external/swisseph, so a porter can see exactly what changed
    per file.

.DESCRIPTION
    The correct 2.08 baseline is the libswe/ directory vendored inside the PyPI
    `pyswisseph 2.08.00-1` sdist. That, and only that, is what this script downloads.

    It is deliberately NOT the `v2.08.00a` tag in the aloistr/swisseph git repository. That tag
    is an incomplete snapshot: it is missing swecl.c, swehouse.c and swehel.c entirely, and its
    swephexp.h is truncated (about 14 KB, against the real 38,410 bytes this script verifies
    below). Diffing against that tag silently produces a wrong work queue for three of the five
    files the 2.10.03 port touches -- three files simply would not appear in the diff at all.
    This script has exactly one 2.08 source (the constant below) and no code path that can
    reach the git tag instead; that is a structural guarantee, not a comment.

    The sdist itself is downloaded and its own sha256 checked. Then every file libswe/ contains
    is checked against scripts/pyswisseph-2.08.manifest.tsv (sha256, byte size, line count).
    Any mismatch, in the sdist or in an individual file, is a hard failure: this script never
    silently proceeds on a verification failure.

    Nothing this script produces is committed. The output directory is gitignored; only this
    script and the manifest are tracked.

.PARAMETER OutputDir
    Where libswe/ is extracted to. Defaults to external/pyswisseph-2.08 (gitignored).

.PARAMETER ManifestPath
    Defaults to scripts/pyswisseph-2.08.manifest.tsv.

.PARAMETER Force
    Re-download the sdist even if a correctly-hashed copy is already cached.
#>
[CmdletBinding()]
param(
    [string] $OutputDir,
    [string] $ManifestPath,
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

# The one and only 2.08 input this script will ever fetch. Do not add a fallback to the
# aloistr/swisseph `v2.08.00a` tag here -- see docs/known-issues.md and CONTRIBUTING.md for why
# that tag silently produces a wrong work queue (missing swecl.c/swehouse.c/swehel.c, truncated
# swephexp.h).
$SdistUrl = 'https://files.pythonhosted.org/packages/8d/63/a0373099b5e888a2ad42d3f1668893e42afb4655dbc5ba06e0b615005eb4/pyswisseph-2.08.00-1.tar.gz'
$SdistSha256 = '6b4818c0224d309c0b01f3c52df2432900dddcde345364408d99eafc9cdd1e71'
$SdistFileName = 'pyswisseph-2.08.00-1.tar.gz'
$SdistRootDir = 'pyswisseph-2.08.00-1'

if (-not $OutputDir) {
    $OutputDir = Join-Path $repoRoot 'external/pyswisseph-2.08'
}
if (-not $ManifestPath) {
    $ManifestPath = Join-Path $PSScriptRoot 'pyswisseph-2.08.manifest.tsv'
}

function Get-Sha256Hex {
    param([string] $Path)
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

if (-not (Test-Path -LiteralPath $ManifestPath)) {
    Write-Host "FAIL: manifest not found at $ManifestPath."
    exit 1
}

$expected = @{}
foreach ($line in [System.IO.File]::ReadAllLines($ManifestPath)) {
    if ($line.StartsWith('#') -or $line.Trim() -eq '') { continue }
    $f = $line -split "`t"
    if ($f.Count -ne 4) {
        Write-Host "FAIL: malformed manifest row: $line"
        exit 1
    }
    $expected[$f[0]] = [pscustomobject]@{ Sha256 = $f[1]; Bytes = [int64]$f[2]; Lines = [int]$f[3] }
}
if ($expected.Count -eq 0) {
    Write-Host "FAIL: manifest at $ManifestPath has no rows."
    exit 1
}

# --- Download + verify the sdist itself -----------------------------------------------------

$downloadDir = Join-Path $repoRoot 'external/.pyswisseph-2.08-download'
New-Item -ItemType Directory -Force -Path $downloadDir | Out-Null
$tarPath = Join-Path $downloadDir $SdistFileName

$needDownload = $Force -or (-not (Test-Path -LiteralPath $tarPath))
if (-not $needDownload) {
    $existingHash = Get-Sha256Hex -Path $tarPath
    if ($existingHash -ne $SdistSha256) {
        Write-Host "Cached $SdistFileName has the wrong hash, re-downloading."
        $needDownload = $true
    }
}

if ($needDownload) {
    Write-Host "Downloading $SdistUrl"
    Invoke-WebRequest -Uri $SdistUrl -OutFile $tarPath -UseBasicParsing
}

$actualSdistHash = Get-Sha256Hex -Path $tarPath
if ($actualSdistHash -ne $SdistSha256) {
    Write-Host 'FAIL: pyswisseph sdist hash mismatch.'
    Write-Host "  expected: $SdistSha256"
    Write-Host "  actual:   $actualSdistHash"
    Write-Host 'This is a hard failure: the file that was downloaded is not the pinned sdist.'
    exit 1
}
Write-Host "PASS: sdist sha256 verified ($SdistSha256)."

# --- Extract libswe/ --------------------------------------------------------------------------

$extractDir = Join-Path $downloadDir 'extracted'
if (Test-Path -LiteralPath $extractDir) {
    Remove-Item -LiteralPath $extractDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $extractDir | Out-Null

# tar ships with Windows, macOS and Linux alike; no extra dependency needed for a .tar.gz.
# On a Windows box with Git for Windows installed, Git's own MSYS tar.exe can shadow the
# real (bsdtar) one Windows ships in System32 -- and MSYS tar reads a `C:\...` path as an
# old-style `host:path` remote spec, not a local file. Prefer the System32 one explicitly
# when it exists; fall back to whatever `tar` resolves to elsewhere (Linux, macOS).
$systemTar = Join-Path $env:SystemRoot 'System32/tar.exe'
$tarExe = if (Test-Path -LiteralPath $systemTar) { $systemTar } else { 'tar' }

& $tarExe -xzf $tarPath -C $extractDir
if ($LASTEXITCODE -ne 0) {
    Write-Host 'FAIL: extracting the sdist failed.'
    exit 1
}

$libsweDir = Join-Path $extractDir "$SdistRootDir/libswe"
if (-not (Test-Path -LiteralPath $libsweDir -PathType Container)) {
    Write-Host "FAIL: $libsweDir not found inside the sdist. Layout may have changed upstream."
    exit 1
}

if (Test-Path -LiteralPath $OutputDir) {
    Remove-Item -LiteralPath $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

foreach ($name in $expected.Keys) {
    $src = Join-Path $libsweDir $name
    if (-not (Test-Path -LiteralPath $src -PathType Leaf)) {
        Write-Host "FAIL: $name is in the manifest but was not found in the sdist's libswe/."
        exit 1
    }
    Copy-Item -LiteralPath $src -Destination (Join-Path $OutputDir $name)
}

# --- Verify every extracted file against the manifest ----------------------------------------

$failed = $false
$header = '{0,-24} {1,-10} {2,14} {3,14}' -f 'FILE', 'CHECK', 'EXPECTED', 'ACTUAL'

foreach ($name in ($expected.Keys | Sort-Object)) {
    $want = $expected[$name]
    $path = Join-Path $OutputDir $name

    $bytes = (Get-Item -LiteralPath $path).Length
    $text = [System.IO.File]::ReadAllText($path)
    $lines = ($text.ToCharArray() | Where-Object { $_ -eq "`n" }).Count
    $sha = Get-Sha256Hex -Path $path

    if ($bytes -ne $want.Bytes) {
        if (-not $failed) { Write-Host $header; Write-Host ('-' * $header.Length) }
        Write-Host ('{0,-24} {1,-10} {2,14} {3,14}' -f $name, 'bytes', $want.Bytes, $bytes)
        $failed = $true
    }
    if ($lines -ne $want.Lines) {
        if (-not $failed) { Write-Host $header; Write-Host ('-' * $header.Length) }
        Write-Host ('{0,-24} {1,-10} {2,14} {3,14}' -f $name, 'lines', $want.Lines, $lines)
        $failed = $true
    }
    if ($sha -ne $want.Sha256) {
        if (-not $failed) { Write-Host $header; Write-Host ('-' * $header.Length) }
        Write-Host ('{0,-24} {1,-10} {2,14} {3,14}' -f $name, 'sha256', $want.Sha256, $sha)
        $failed = $true
    }
}

if ($failed) {
    Write-Host ''
    Write-Host 'FAIL: one or more libswe/ files do not match scripts/pyswisseph-2.08.manifest.tsv.'
    Write-Host 'Do not proceed with a diff against a baseline that failed verification.'
    exit 1
}

Write-Host "PASS: $($expected.Count) files verified against the manifest."
Write-Host "2.08 baseline ready at $OutputDir"
exit 0
