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

# -OutputDir is recursively deleted further down (to guarantee a clean extraction, not a
# merge with whatever was there before). Refuse to do that to anything outside external/ --
# there is no legitimate reason for -OutputDir to point elsewhere, and without this check a
# typo or a bad caller could silently wipe an unrelated directory.
$externalRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'external'))
$resolvedOutputDir = [System.IO.Path]::GetFullPath($OutputDir)
$externalRootWithSeparator = $externalRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if ($resolvedOutputDir -ne $externalRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) -and
    -not $resolvedOutputDir.StartsWith($externalRootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
    Write-Host "FAIL: -OutputDir '$OutputDir' resolves to '$resolvedOutputDir', which is not under '$externalRoot'."
    Write-Host "Refusing to recursively delete a directory outside external/."
    exit 1
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

# CONTRIBUTING.md, "The 2.08 baseline trap", documents these exact totals (31 files, 24
# .c/.h). Asserting them here, not just trusting whatever rows happen to be in the file,
# closes the same failure mode fetch-2.08-baseline.ps1 exists to prevent: a manifest with
# the swecl.c/swehouse.c/swehel.c rows quietly removed reproduces the v2.08.00a bug (three
# files silently missing from the diff) while every remaining row still verifies and this
# script still exits 0.
if ($expected.Count -ne 31) {
    Write-Host "FAIL: manifest at $ManifestPath has $($expected.Count) row(s), expected the documented 31 (CONTRIBUTING.md, 'The 2.08 baseline trap')."
    Write-Host "A manifest silently missing rows (e.g. swecl.c/swehouse.c/swehel.c) reproduces the v2.08.00a failure mode -- files silently absent from the work queue -- while still exiting 0 otherwise."
    exit 1
}
$manifestCOrHCount = @($expected.Keys | Where-Object { $_ -like '*.c' -or $_ -like '*.h' }).Count
if ($manifestCOrHCount -ne 24) {
    Write-Host "FAIL: manifest at $ManifestPath has $manifestCOrHCount .c/.h row(s), expected the documented 24 (CONTRIBUTING.md, 'The 2.08 baseline trap')."
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
#
# $env:SystemRoot is unset off Windows, and Join-Path with a null/empty -Path throws a
# terminating parameter-binding error -- before ever reaching the Test-Path fallback below.
# Gate the whole System32 lookup on $IsWindows (only defined and true on Windows in
# PowerShell 7+) as well as $env:SystemRoot being non-empty, so Linux/macOS fall straight
# through to the plain 'tar' on PATH instead of crashing here.
$systemTar = if ($IsWindows -and $env:SystemRoot) { Join-Path $env:SystemRoot 'System32/tar.exe' } else { $null }
$tarExe = if ($systemTar -and (Test-Path -LiteralPath $systemTar)) { $systemTar } else { 'tar' }

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

# The copy loop below only ever walks $expected.Keys (the manifest's own rows), so a file
# present in libswe/ but absent from the manifest would previously be silently skipped and
# never mentioned anywhere -- the manifest, not libswe/, would quietly become the source of
# truth for what "the 2.08 baseline" contains. Check the other direction explicitly. The
# sdist's libswe/ carries two packaging artifacts that are never meant to be part of the
# tracked baseline (a stray `.git` file -- a gitlink pointer, not a real directory here -- and
# `.gitignore`); those are expected and not reported. Anything else unlisted is not.
$libsweFileNames = @(Get-ChildItem -LiteralPath $libsweDir -File | ForEach-Object { $_.Name })
$knownNonManifestFiles = @('.git', '.gitignore')
$unlistedInManifest = @($libsweFileNames | Where-Object { -not $expected.ContainsKey($_) -and $_ -notin $knownNonManifestFiles })
if ($unlistedInManifest.Count -gt 0) {
    Write-Host "FAIL: libswe/ in the sdist contains file(s) not listed in $ManifestPath and not a known packaging artifact -- the manifest is stale or incomplete:"
    $unlistedInManifest | Sort-Object | ForEach-Object { Write-Host "  $_" }
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
    # Was ($text.ToCharArray() | Where-Object { $_ -eq "`n" }).Count: under Set-StrictMode
    # (which this script sets -- see the top), a file with no `n` at all makes the pipeline
    # emit zero matches, so the result is $null, and $null.Count throws instead of being 0.
    # It also pushes every character of a file up to ~1.6 MB through Where-Object one at a
    # time. Counting via string length arithmetic is both StrictMode-safe and avoids that.
    $lines = $text.Length - $text.Replace("`n", '').Length
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

# Stamp: records the manifest's own sha256 alongside the verified output, so a consumer
# (scripts/gen-delta.ps1) can tell "this directory was produced by a run of this script that
# passed verification against the CURRENT manifest" apart from "a directory that merely has
# files in it" -- without re-hashing all 31 files on every invocation. gen-delta.ps1 treats a
# missing or mismatched stamp the same as a missing directory and re-invokes this script.
$stampPath = Join-Path $OutputDir '.manifest-sha256'
Set-Content -LiteralPath $stampPath -Value (Get-Sha256Hex -Path $ManifestPath) -NoNewline -Encoding ascii

exit 0
