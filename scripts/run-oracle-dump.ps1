#Requires -Version 7.3
<#
.SYNOPSIS
    Builds and runs both sides of the bit-exact oracle harness against the committed grids, and
    writes their raw output for a later, separate comparison pass.

.DESCRIPTION
    Two grids by default, each replayed by three drivers: Tools/CReference/sedump.c, compiled here and
    linked against Astrodienst's own 2.10.03 C (the port's target); Tools/CReference/build-c.ps1's
    prebuilt sedump-2.08.exe, linked against Astrodienst's own 2.08 C (the port's current
    version) and compiled against 2.08's own headers -- see that script's header for why it is
    built there, not here; and Tools/OracleDump, built here, against this port:

      Tools/OracleGrid/grid-analytic.tsv  -- SEFLG_MOSEPH swe_calc/swe_calc_ut plus
                                              swe_houses/swe_houses_armc. Touches no ephemeris
                                              data file. See that file's own header.
      Tools/OracleGrid/grid-files.tsv     -- SEFLG_SWIEPH swe_calc/swe_calc_ut, the swe_fixstar
                                              family, and swe_get_planet_name. Opens the shipped
                                              .se1/sefstars.txt files under -EpheDir. See that
                                              file's own header.

    A third grid is OPT-IN and off by default:

      Tools/OracleGrid/grid-jpl.tsv       -- SEFLG_JPLEPH swe_calc/swe_calc_ut, including the
                                              SEFLG_JPLHOR/SEFLG_JPLHOR_APPROX combinations no
                                              other grid can reach. See that file's own header.

    THE JPL GRID IS OPT-IN BECAUSE THIS REPO SHIPS NO DE FILE, AND CANNOT

    A JPL DE file is 190 MB (DE406) to 2.6 GB (DE431). Nothing that size goes in git and no CI
    runner has one, so this leg runs only when a DE file is explicitly named -- via -JplFile, or
    the SWISSEPH_ORACLE_JPL_FILE environment variable when that parameter is not passed. With
    neither set, this script prints a SKIP line for the JPL grid and everything else behaves
    exactly as it did before the grid existed: same two grids, same six dump files, same five
    provenance rows. That is the same opt-in shape the correctness oracle already uses for its own
    SEFLG_JPLEPH iterations (SWISSEPH_CONFORMANCE_INCLUDE_JPL/SWISSEPH_CONFORMANCE_JPL_FILE, see
    Tests/SwissEphNet.Conformance.Tests/Dispatch/EphemerisFileResolver.cs).

    Unlike the correctness oracle, this grid does not care WHICH DE file it is given. setest
    hardcodes swe_set_jpl_file("de431.eph") at suite scope, so t.exp's expected values are DE431's
    and running that corpus against another DE file produces real differences indistinguishable
    from port defects. Here both sides open the SAME file on the SAME machine over the SAME inputs
    and their raw bit patterns are compared against each other, so any difference is a port defect
    by construction. Only the grid's date range depends on the file at all -- grid-jpl.tsv's dates
    sit inside DE406's span, which DE431's contains.

    The JPL grid gets its own ephemeris directory (-JplEpheDir, defaulting to the directory the
    DE file itself sits in), NOT -EpheDir, and Assert-EphemerisManifest is not applied to it: the
    declared file set under Tests/conformance/required-ephemeris-files.tsv describes the shipped
    .se1/.txt core set, and a directory holding a DE file is a different thing that this repo makes
    no claim about. Keeping the two directories separate also means a SEFLG_JPLEPH row cannot
    quietly resolve against a .se1 file when the JPL path fails -- if it falls back, it falls back
    to Moshier, which is visible in the result rather than hidden behind a plausible number.

    The 2.08 driver is deliberately not run against this grid. sedump-2.08.exe would build and run
    against it (swe_set_jpl_file exists in 2.08), but scripts/classify-oracle-versions.ps1's
    three-way classification has no JPL grid wired into it, so a 2.08 JPL dump would be produced
    and never read. Adding it belongs with that classifier, not here.

    Building sedump.exe needs a libswe .lib to link against. Tools/CReference/build-c.ps1
    produces one at external/.c-reference/build-2.10.03/libswe-2.10.03.lib by default, which is
    also this script's default -LibPath; run that script first if the .lib is missing, this one
    does not build it. sedump.exe is always compiled against external/swisseph's own headers to
    match, so -LibPath must stay pointed at a library built from that same source tree --
    repointing it at build-2.08/libswe-2.08.lib without also repointing the header search path
    would link a 2.10.03-header binary against 2.08 object code, exactly the mismatch
    Tools/CReference/build-c.ps1's own header warns about. -Sedump208Path below is the supported
    way to bring 2.08 into this comparison: a whole separate binary, built by that script against
    2.08's own headers, not a repointed -LibPath here.

    This script does not compare any of the three sides' output against each other -- that is
    scripts/verify-oracle.ps1's job for the port against 2.10.03 C, and is a new three-way
    classifier's job (see scripts/classify-oracle-versions.ps1) for the port against both C
    versions plus the two C versions against each other. It only checks that each driver emitted
    exactly as many rows as its grid contains (see the row-count guards below), which catches a
    driver silently truncating its own run without saying anything at all about whether the
    sides agree on any individual value.

    PROVENANCE

    A successful run also writes external/.c-reference/oracle-provenance.tsv, recording the
    SHA-256 of both grids, of the port's own source under SwissEphNet/ (every *.cs file plus
    SwissEphNet.csproj, excluding bin/ and obj/), and of the sedump.exe/sedump-2.08.exe that
    produced the two C dumps. scripts/verify-oracle.ps1 reads that file and refuses to report
    PASS when any of those inputs no longer matches what is on disk now -- without it, the gate
    could compare two dumps that no longer reflect either the current grids or the current port,
    and PASS anyway. See that script for the check itself.

    Source is hashed here rather than the built SwissEphNet.dll. SourceLink stamps the current
    git commit into the DLL (Directory.Build.props turns this on under CI via
    PublishRepositoryUrl/EmbedUntrackedSources/ContinuousIntegrationBuild), so any commit at all
    changes the DLL's hash even when nothing under SwissEphNet/ did -- measured on a
    documentation-only commit, and even though the build itself is reproducible (two builds of
    unchanged source hash identically). Hashing the source instead sidesteps that, and lets
    scripts/verify-oracle.ps1 check provenance without building anything.

    THE EPHEMERIS FILE SET IS PART OF THE CONTRACT

    Before running grid-files.tsv, this script asserts that -EpheDir contains exactly the files
    Tests/conformance/required-ephemeris-files.tsv declares -- the same two-way check (missing
    AND extra) Tests/SwissEphNet.Conformance.Tests/Dispatch/EphemerisManifest.cs runs for the
    correctness oracle, reimplemented here in PowerShell rather than referenced directly, since
    this script has no other reason to depend on that test project. Extra files change which
    grid-files.tsv rows resolve against real data and which fall back to Moshier silently --
    Tests/conformance/regenerations.log's second entry records this happening for real, once,
    against the correctness oracle's own known-fail list. grid-analytic.tsv is exempt: nothing
    in it ever opens a file.

.PARAMETER LibPath
    The libswe .lib sedump.exe links against, compiled here against external/swisseph's own
    headers -- see the .DESCRIPTION for why this must stay a 2.10.03 library. Defaults to the
    2.10.03 build Tools/CReference/build-c.ps1 produces.

.PARAMETER Sedump208Path
    The prebuilt sedump-2.08.exe Tools/CReference/build-c.ps1 produces, already linked against
    libswe-2.08.lib and compiled against external/pyswisseph-2.08's own headers. Defaults to
    sedump-2.08.exe under -OutputDir. Run that script first if it is missing; this script only
    runs it, it does not build it (unlike sedump.exe against 2.10.03, which this script does
    compile itself -- see the .DESCRIPTION for why the 2.08 driver's build step lives there
    instead).

.PARAMETER GridPath
    The analytic grid TSV both drivers replay. Defaults to Tools/OracleGrid/grid-analytic.tsv.

.PARAMETER FilesGridPath
    The file-backed grid TSV both drivers replay. Defaults to Tools/OracleGrid/grid-files.tsv.

.PARAMETER EpheDir
    Directory both drivers open grid-files.tsv's ephemeris data files from. Defaults to
    external/swisseph/ephe (the sparse submodule checkout CONTRIBUTING.md documents). Never
    passed to grid-analytic.tsv's run, and never to grid-jpl.tsv's either -- see the .DESCRIPTION.

.PARAMETER JplGridPath
    The JPL grid TSV both drivers replay when the JPL leg runs at all. Defaults to
    Tools/OracleGrid/grid-jpl.tsv.

.PARAMETER JplFile
    The JPL DE file to pass to swe_set_jpl_file, e.g. de406.eph. Supplying it (or setting
    SWISSEPH_ORACLE_JPL_FILE when it is not supplied) is what opts the JPL leg in; with neither,
    that leg is skipped entirely. May be an absolute path or a bare file name -- a bare name is
    resolved relative to -JplEpheDir, and an absolute path has its directory become -JplEpheDir's
    default, since swe_set_jpl_file resolves the name it is handed against the ephemeris path
    rather than against the working directory.

.PARAMETER JplEpheDir
    Ephemeris directory the JPL leg runs under. Defaults to the directory -JplFile resolves to.
    Deliberately separate from -EpheDir and not checked against
    Tests/conformance/required-ephemeris-files.tsv -- see the .DESCRIPTION.

.PARAMETER OutputDir
    Where build products and the dump files are written. Defaults to external/.c-reference,
    which .gitignore excludes -- these are run outputs of vendored/local source, not source
    themselves. Must resolve to a path under external/.c-reference, the exact path .gitignore
    excludes (not all of external/, most of which is tracked submodule/fetched source); this
    script refuses to write its outputs anywhere .gitignore would not catch.
#>
[CmdletBinding()]
param(
    [string] $LibPath,
    [string] $Sedump208Path,
    [string] $GridPath,
    [string] $FilesGridPath,
    [string] $EpheDir,
    [string] $OutputDir,
    [string] $JplGridPath,
    [string] $JplFile,
    [string] $JplEpheDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# cl, link and dotnet below are all native commands; see Tools/CReference/build-c.ps1's own copy
# of this line for why this is set even though it changes nothing under the pwsh version this
# was written against.
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $LibPath) { $LibPath = Join-Path $repoRoot 'external/.c-reference/build-2.10.03/libswe-2.10.03.lib' }
if (-not $GridPath) { $GridPath = Join-Path $repoRoot 'Tools/OracleGrid/grid-analytic.tsv' }
if (-not $FilesGridPath) { $FilesGridPath = Join-Path $repoRoot 'Tools/OracleGrid/grid-files.tsv' }
if (-not $EpheDir) { $EpheDir = Join-Path $repoRoot 'external/swisseph/ephe' }
if (-not $OutputDir) { $OutputDir = Join-Path $repoRoot 'external/.c-reference' }
# Depends on $OutputDir's own default just above -- Tools/CReference/build-c.ps1 writes
# sedump-2.08.exe to that same directory by default.
if (-not $Sedump208Path) { $Sedump208Path = Join-Path $OutputDir 'sedump-2.08.exe' }
if (-not $JplGridPath) { $JplGridPath = Join-Path $repoRoot 'Tools/OracleGrid/grid-jpl.tsv' }

# The JPL leg's opt-in switch. -JplFile wins; SWISSEPH_ORACLE_JPL_FILE is the environment-variable
# form CI (which never sets it) and a contributor without a DE file both rely on -- see this
# script's own .DESCRIPTION. An empty or whitespace-only value means "not set", so exporting the
# variable as an empty string does not accidentally opt in and then fail on a missing file.
if (-not $JplFile) { $JplFile = $env:SWISSEPH_ORACLE_JPL_FILE }
$runJplGrid = -not [string]::IsNullOrWhiteSpace($JplFile)

$LibPath = [System.IO.Path]::GetFullPath($LibPath)
$Sedump208Path = [System.IO.Path]::GetFullPath($Sedump208Path)
$GridPath = [System.IO.Path]::GetFullPath($GridPath)
$FilesGridPath = [System.IO.Path]::GetFullPath($FilesGridPath)
$EpheDir = [System.IO.Path]::GetFullPath($EpheDir)
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
$JplGridPath = [System.IO.Path]::GetFullPath($JplGridPath)

# -JplEpheDir defaults to wherever -JplFile resolves to, and the name actually handed to
# swe_set_jpl_file is always the bare file name: the C strips everything up to the last DIR_GLUE
# before storing it (sweph.c:1490-1497) and then resolves that basename against swed.ephepath, so
# passing a full path through would resolve to "<ephepath>/<basename>" regardless. Splitting it
# here makes the directory the drivers are actually pointed at explicit rather than implied.
$jplFileName = $null
if ($runJplGrid) {
    $JplFile = $JplFile.Trim()
    if ([System.IO.Path]::IsPathRooted($JplFile)) {
        $JplFile = [System.IO.Path]::GetFullPath($JplFile)
        if (-not $JplEpheDir) { $JplEpheDir = [System.IO.Path]::GetDirectoryName($JplFile) }
    }
    elseif (-not $JplEpheDir) {
        Write-Host "FAIL: -JplFile ('$JplFile') is a bare file name, so there is nothing to derive -JplEpheDir from. Pass an absolute path, or pass -JplEpheDir as well." -ForegroundColor Red
        exit 1
    }
    $jplFileName = [System.IO.Path]::GetFileName($JplFile)
    $JplEpheDir = [System.IO.Path]::GetFullPath($JplEpheDir)
}

# Only external/.c-reference/ (not all of external/, most of which is tracked submodule/fetched
# source -- see .gitignore) is excluded by .gitignore. Resolved and checked the same way
# Tools/CReference/build-c.ps1 checks its own -OutputDir against external/.
$safeRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'external/.c-reference'))
$safeRootWithSeparator = $safeRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if ($OutputDir -ne $safeRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) -and
    -not $OutputDir.StartsWith($safeRootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
    Write-Host "FAIL: -OutputDir resolves to '$OutputDir', which is not under '$safeRoot'." -ForegroundColor Red
    Write-Host 'Only external/.c-reference/ is excluded by .gitignore; refusing to write build products elsewhere.'
    exit 1
}

function Fail($message) {
    # Thrown, not exited: a bare `exit` here would skip the try/catch below that gives every
    # failure the same "FAIL: ..." banner, matching Tools/CReference/build-c.ps1's own pattern.
    throw $message
}

# ---------------------------------------------------------------------------------------
# Toolchain -- same recipe as Tools/CReference/build-c.ps1 and scripts/verify-crt-parity.ps1,
# kept as a separate copy here rather than dot-sourcing either, since dot-sourcing build-c.ps1
# would run its entire library build as a side effect.
# ---------------------------------------------------------------------------------------

function Get-VcVarsPath {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) {
        Fail "vswhere.exe not found at $vswhere. Visual Studio with the C++ toolset is required."
    }
    $install = & $vswhere -latest -products * `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath
    if (-not $install) {
        Fail 'No Visual Studio installation with the x64 C++ toolset was found.'
    }
    $vcvars = Join-Path $install 'VC/Auxiliary/Build/vcvars64.bat'
    if (-not (Test-Path -LiteralPath $vcvars)) { Fail "vcvars64.bat not found under $install." }
    return $vcvars
}

# Runs a command inside the MSVC x64 environment. vcvars64.bat only sets variables for the shell
# it runs in, so the compiler invocation has to happen in that same cmd process.
function Invoke-InVcEnv {
    param([string] $VcVars, [string] $WorkingDir, [string] $Command)
    $full = "`"$VcVars`" >nul 2>&1 && cd /d `"$WorkingDir`" && $Command"
    $output = cmd /c $full 2>&1
    return @{ ExitCode = $LASTEXITCODE; Output = $output }
}

# Counts data rows in a grid TSV the same way both drivers do: skip '#' comment lines, skip the
# first non-comment line (the column header), count everything after. Not grid logic -- a line
# count, used only for the row-count guards below, not to interpret what any row means. Works
# unchanged for either grid, since both share the same comment/header/data shape and differ only
# in column count, which this function never looks at.
function Get-GridDataRowCount {
    param([string] $Path)
    $count = 0
    $headerSeen = $false
    foreach ($textLine in [System.IO.File]::ReadLines($Path)) {
        if ($textLine.Length -eq 0) { continue }
        if ($textLine[0] -eq '#') { continue }
        if (-not $headerSeen) { $headerSeen = $true; continue }
        $count++
    }
    if (-not $headerSeen) { Fail "$Path has no header row." }
    return $count
}

function Get-FileLineCount {
    param([string] $Path)
    $count = 0
    foreach ($textLine in [System.IO.File]::ReadLines($Path)) {
        if ($textLine.Length -gt 0) { $count++ }
    }
    return $count
}

# Used to build the provenance sidecar this script writes on success -- see PROVENANCE in the
# header comment and scripts/verify-oracle.ps1, which reads what this writes.
function Get-Sha256Hex {
    param([string] $Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

# Fingerprints the port's own source: every *.cs under SwissEphNet/ plus
# SwissEphNet/SwissEphNet.csproj, excluding bin/ and obj/ -- see PROVENANCE above for why. Kept
# as a separate copy in scripts/verify-oracle.ps1, matching how this script already keeps its own
# copy of the toolchain functions above instead of dot-sourcing Tools/CReference/build-c.ps1.
# Modeled on scripts/verify-freeze.ps1's Get-Fingerprint: -LiteralPath and -Force (this repo has
# SwissEphNet/[Events].cs; square brackets are wildcards under -Path, and a -Force-less
# enumeration hides dotfiles on Unix but not Windows), an ordinal sort of repo-relative paths so
# the order never depends on culture, hashing the path alongside the content so moving code
# between files counts as a change, and line-ending normalization so a CRLF/LF difference between
# checkouts does not read as a source change.
function Get-PortSourceHash {
    param([string] $RepoRoot)

    $srcDir = Join-Path $RepoRoot 'SwissEphNet'
    $csprojPath = Join-Path $srcDir 'SwissEphNet.csproj'
    if (-not (Test-Path -LiteralPath $csprojPath -PathType Leaf)) {
        Fail "SwissEphNet.csproj not found at $csprojPath."
    }

    $allFiles = @(Get-ChildItem -LiteralPath $srcDir -Recurse -File -Force)
    $csFiles = @($allFiles | Where-Object {
        $_.Extension -eq '.cs' -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
    })
    $files = [object[]] (@($csFiles) + @(Get-Item -LiteralPath $csprojPath))

    $keys = [string[]] ($files | ForEach-Object { $_.FullName.Substring($RepoRoot.Length).Replace([char]92, [char]47) })
    [System.Array]::Sort($keys, $files, [System.StringComparer]::Ordinal)

    $contents = [System.Collections.Generic.List[string]]::new()
    for ($i = 0; $i -lt $files.Count; $i++) {
        $text = [System.IO.File]::ReadAllText($files[$i].FullName)
        $normalized = ($text -split "`r`n|`n|`r") -join "`n"
        [void]$contents.Add($keys[$i])
        [void]$contents.Add($normalized)
    }

    return [System.BitConverter]::ToString(
        [System.Security.Cryptography.SHA256]::HashData(
            [System.Text.Encoding]::UTF8.GetBytes(($contents -join "`n")))).Replace('-', '').ToLowerInvariant()
}

# Two-way check (missing AND extra), matching
# Tests/SwissEphNet.Conformance.Tests/Dispatch/EphemerisManifest.cs's EphemerisManifestResult --
# see this script's .DESCRIPTION for why an extra file is as much a problem as a missing one.
function Assert-EphemerisManifest {
    param([string] $ManifestPath, [string] $EpheDir)

    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        Fail "Required ephemeris file list not found at $ManifestPath."
    }
    $required = @(Get-Content -LiteralPath $ManifestPath |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_.Length -gt 0 -and -not $_.StartsWith('#') })
    if ($required.Count -eq 0) {
        Fail "$ManifestPath parsed to zero required files."
    }

    if (-not (Test-Path -LiteralPath $EpheDir -PathType Container)) {
        Fail "$EpheDir does not exist. Run the sparse-checkout recipe in CONTRIBUTING.md's `"The upstream C is vendored at external/swisseph`" section."
    }

    $present = @{}
    Get-ChildItem -LiteralPath $EpheDir -Force | ForEach-Object {
        $name = if ($_.PSIsContainer) { "$($_.Name)/" } else { $_.Name }
        $present[$name] = $true
    }

    $requiredSet = @{}
    foreach ($r in $required) { $requiredSet[$r.ToLowerInvariant()] = $true }

    $missing = @($required | Where-Object { -not $present.ContainsKey($_) })
    $extra = @($present.Keys | Where-Object { -not $requiredSet.ContainsKey($_.ToLowerInvariant()) } | Sort-Object)

    if ($missing.Count -eq 0 -and $extra.Count -eq 0) {
        Write-Host "PASS: $EpheDir matches the declared ephemeris file set ($($required.Count) file(s))." -ForegroundColor Green
        return
    }

    $message = "$EpheDir does not match the declared ephemeris file set ($ManifestPath).`n"
    if ($missing.Count -gt 0) {
        $message += "Missing ($($missing.Count)): $($missing -join ', ')`n"
        $message += "Fetch the declared sparse core set -- see CONTRIBUTING.md's `"The upstream C is vendored at external/swisseph`".`n"
    }
    if ($extra.Count -gt 0) {
        $shown = $extra | Select-Object -First 20
        $message += "Extra ($($extra.Count)): $($shown -join ', ')"
        if ($extra.Count -gt 20) { $message += ", ... and $($extra.Count - 20) more" }
        $message += "`n"
        $message += "This usually means the submodule was checked out with a plain 'git submodule update --init' " +
            "instead of the sparse recipe in CONTRIBUTING.md -- reset the sparse-checkout patterns " +
            "(git -C external/swisseph sparse-checkout reapply) rather than adding the extra files to the manifest, " +
            "unless deliberately changing what this repo declares as its data set.`n"
    }
    Fail $message
}

$exitCode = 0
try {
    if (-not (Test-Path -LiteralPath $GridPath -PathType Leaf)) {
        Fail "Analytic grid file not found at $GridPath. Run: pwsh Tools/OracleGrid/gen-grid-analytic.ps1"
    }
    if (-not (Test-Path -LiteralPath $FilesGridPath -PathType Leaf)) {
        Fail "Files grid not found at $FilesGridPath. Run: pwsh Tools/OracleGrid/gen-grid-files.ps1"
    }
    if (-not (Test-Path -LiteralPath $LibPath -PathType Leaf)) {
        Fail "Library not found at $LibPath. Run: pwsh Tools/CReference/build-c.ps1"
    }
    if (-not (Test-Path -LiteralPath $Sedump208Path -PathType Leaf)) {
        Fail "sedump-2.08.exe not found at $Sedump208Path. Run: pwsh Tools/CReference/build-c.ps1"
    }

    $expectedAnalyticRows = Get-GridDataRowCount -Path $GridPath
    if ($expectedAnalyticRows -eq 0) { Fail "$GridPath contains zero data rows." }
    Write-Host "Analytic grid: $expectedAnalyticRows data row(s) in $GridPath"

    $expectedFilesRows = Get-GridDataRowCount -Path $FilesGridPath
    if ($expectedFilesRows -eq 0) { Fail "$FilesGridPath contains zero data rows." }
    Write-Host "Files grid:    $expectedFilesRows data row(s) in $FilesGridPath"

    # ---------------------------------------------------------------------------------------
    # JPL leg (opt-in) -- validated here, before anything is built, so a missing DE file or a
    # missing grid fails immediately rather than after a full C build and two grid runs. Not
    # reaching this block at all is the normal, expected case; see this script's .DESCRIPTION.
    # ---------------------------------------------------------------------------------------

    $expectedJplRows = 0
    if ($runJplGrid) {
        if (-not (Test-Path -LiteralPath $JplGridPath -PathType Leaf)) {
            Fail "JPL grid not found at $JplGridPath. Run: pwsh Tools/OracleGrid/gen-grid-jpl.ps1"
        }
        if (-not (Test-Path -LiteralPath $JplEpheDir -PathType Container)) {
            Fail "-JplEpheDir '$JplEpheDir' does not exist."
        }
        $jplFileFullPath = Join-Path $JplEpheDir $jplFileName
        if (-not (Test-Path -LiteralPath $jplFileFullPath -PathType Leaf)) {
            Fail "JPL ephemeris file not found at $jplFileFullPath. swe_set_jpl_file resolves '$jplFileName' against the ephemeris path, so it has to sit directly in -JplEpheDir."
        }
        $expectedJplRows = Get-GridDataRowCount -Path $JplGridPath
        if ($expectedJplRows -eq 0) { Fail "$JplGridPath contains zero data rows." }
        Write-Host "JPL grid:      $expectedJplRows data row(s) in $JplGridPath"
        Write-Host "JPL file:      $jplFileFullPath ($([System.IO.FileInfo]::new($jplFileFullPath).Length) bytes)"
    }
    else {
        Write-Host "SKIP: the JPL grid ($JplGridPath) needs a DE file this repo does not ship. Pass -JplFile, or set SWISSEPH_ORACLE_JPL_FILE, to opt in." -ForegroundColor Yellow
    }

    $requiredFilesManifest = Join-Path $repoRoot 'Tests/conformance/required-ephemeris-files.tsv'
    Assert-EphemerisManifest -ManifestPath $requiredFilesManifest -EpheDir $EpheDir

    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

    $vcvars = Get-VcVarsPath
    Write-Host "Toolchain: $vcvars"

    # See Tools/CReference/build-c.ps1's own comment on this same step for the measured example
    # of how an inherited CL/_CL_ silently changes what gets compiled while nothing recorded
    # about the run admits it.
    $envClOriginal = $env:CL
    $envClUnderscoreOriginal = $env:_CL_
    if ($envClOriginal) { Write-Host "NOTE: clearing inherited CL='$envClOriginal' before compiling." -ForegroundColor Yellow }
    if ($envClUnderscoreOriginal) { Write-Host "NOTE: clearing inherited _CL_='$envClUnderscoreOriginal' before compiling." -ForegroundColor Yellow }
    Remove-Item Env:\CL -ErrorAction SilentlyContinue
    Remove-Item Env:\_CL_ -ErrorAction SilentlyContinue

    # Same flags as Tools/CReference/build-c.ps1's $commonFlags: /O2 /fp:precise /MD so this
    # compiles against the identical toolchain the linked .lib itself was built with. The four
    # SWISSEPH_HAS_* guard macros below (spelled out together, with their /D prefix, only on the
    # $commonFlags line itself -- scripts/verify-sedump-macro-parity.ps1 treats each physical line
    # mentioning "-D"/"/D" plus a guard name as a compile site and requires every site to define
    # the same set, so this comment refers to them by bare name, never split across lines with the
    # /D prefix attached, to avoid tripping that gate on its own prose) are this build's own
    # addition, not shared with build-c.ps1's 2.08 build: swe_solcross/swe_mooncross/
    # swe_mooncross_node/swe_helio_cross and their _ut variants (SWISSEPH_HAS_CROSSING),
    # swe_houses_ex2/swe_houses_armc_ex2 (SWISSEPH_HAS_HOUSES_EX2), swe_calc_pctr
    # (SWISSEPH_HAS_CALC_PCTR) and swe_get_current_file_data (SWISSEPH_HAS_GET_CURRENT_FILE_DATA)
    # do not exist in Swiss Ephemeris 2.08 at all (see sedump.c's own top-of-file comment on each
    # macro), so only the 2.10.03 build linked here defines any of them; the 2.08 build's compile
    # command in build-c.ps1 is left untouched and picks up all four macros' #else branches by
    # simply not defining them.
    $commonFlags = '/O2 /fp:precise /D_CRT_SECURE_NO_WARNINGS /DSWISSEPH_HAS_CROSSING=1 /DSWISSEPH_HAS_HOUSES_EX2=1 /DSWISSEPH_HAS_CALC_PCTR=1 /DSWISSEPH_HAS_GET_CURRENT_FILE_DATA=1 /MD'
    $swephIncludeDir = Join-Path $repoRoot 'external/swisseph'
    $sedumpSource = Join-Path $repoRoot 'Tools/CReference/sedump.c'
    $cBuildDir = Join-Path $OutputDir 'oracle-dump-c'
    New-Item -ItemType Directory -Force -Path $cBuildDir | Out-Null
    $sedumpExe = Join-Path $cBuildDir 'sedump.exe'

    Write-Host 'Compiling sedump.c...'
    $compile = "cl /nologo /TC $commonFlags /I`"$swephIncludeDir`" /Fe:`"$sedumpExe`" `"$sedumpSource`" /link `"$LibPath`""
    $result = Invoke-InVcEnv -VcVars $vcvars -WorkingDir $cBuildDir -Command $compile
    if ($result.ExitCode -ne 0) {
        $result.Output | Write-Host
        Fail 'Compiling sedump.c failed.'
    }

    $netBuildDir = Join-Path $OutputDir 'oracle-dump-net'
    $csprojPath = Join-Path $repoRoot 'Tools/OracleDump/OracleDump.csproj'
    Write-Host 'Building Tools/OracleDump...'
    # No -p:ContinuousIntegrationBuild=true: the provenance sidecar below hashes SwissEphNet/'s
    # source directly (see PROVENANCE above and Get-PortSourceHash), not this build's
    # SwissEphNet.dll, so nothing here depends on the DLL hashing the same way across machines or
    # commits. This build still has to run -- OracleDump.exe is what actually replays the grids
    # below against the port -- it just no longer needs that flag for provenance's sake.
    $buildOutput = & dotnet build $csprojPath -c Release -o $netBuildDir --nologo -v minimal 2>&1
    if ($LASTEXITCODE -ne 0) {
        $buildOutput | Write-Host
        Fail 'dotnet build Tools/OracleDump failed.'
    }
    $oracleDumpExe = Join-Path $netBuildDir 'OracleDump.exe'
    if (-not (Test-Path -LiteralPath $oracleDumpExe -PathType Leaf)) {
        Fail "dotnet build reported success but $oracleDumpExe does not exist. Is this running on Windows (the apphost .exe is Windows-only)?"
    }

    # ---------------------------------------------------------------------------------------
    # Analytic grid -- unchanged from before this script covered two grids: two-argument
    # invocation, no ephemeris directory, output file names unchanged.
    # ---------------------------------------------------------------------------------------

    $cOutputPath = Join-Path $OutputDir 'dump-c-2.10.03.tsv'
    Write-Host 'Running sedump.exe (analytic grid)...'
    $sedumpOutput = @(& $sedumpExe $GridPath $cOutputPath 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $sedumpOutput | Write-Host
        Fail "sedump.exe exited $LASTEXITCODE on the analytic grid."
    }
    $sedumpOutput | Write-Host

    $netOutputPath = Join-Path $OutputDir 'dump-net.tsv'
    Write-Host 'Running OracleDump.exe (analytic grid)...'
    $oracleDumpOutput = @(& $oracleDumpExe $GridPath $netOutputPath 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $oracleDumpOutput | Write-Host
        Fail "OracleDump.exe exited $LASTEXITCODE on the analytic grid."
    }
    $oracleDumpOutput | Write-Host

    # ---------------------------------------------------------------------------------------
    # Files grid -- three-argument invocation (ephemeris directory), separate output files.
    # ---------------------------------------------------------------------------------------

    $cFilesOutputPath = Join-Path $OutputDir 'dump-c-2.10.03-files.tsv'
    Write-Host 'Running sedump.exe (files grid)...'
    $sedumpFilesOutput = @(& $sedumpExe $FilesGridPath $cFilesOutputPath $EpheDir 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $sedumpFilesOutput | Write-Host
        Fail "sedump.exe exited $LASTEXITCODE on the files grid."
    }
    $sedumpFilesOutput | Write-Host

    $netFilesOutputPath = Join-Path $OutputDir 'dump-net-files.tsv'
    Write-Host 'Running OracleDump.exe (files grid)...'
    $oracleDumpFilesOutput = @(& $oracleDumpExe $FilesGridPath $netFilesOutputPath $EpheDir 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $oracleDumpFilesOutput | Write-Host
        Fail "OracleDump.exe exited $LASTEXITCODE on the files grid."
    }
    $oracleDumpFilesOutput | Write-Host

    # ---------------------------------------------------------------------------------------
    # JPL grid -- four-argument invocation (its own ephemeris directory, plus the DE file name
    # swe_set_jpl_file is handed), separate output files, and only when opted in. The 2.08 driver
    # is deliberately not run here -- see the .DESCRIPTION.
    # ---------------------------------------------------------------------------------------

    $cJplOutputPath = Join-Path $OutputDir 'dump-c-2.10.03-jpl.tsv'
    $netJplOutputPath = Join-Path $OutputDir 'dump-net-jpl.tsv'
    if ($runJplGrid) {
        Write-Host 'Running sedump.exe (JPL grid)...'
        $sedumpJplOutput = @(& $sedumpExe $JplGridPath $cJplOutputPath $JplEpheDir $jplFileName 2>&1)
        if ($LASTEXITCODE -ne 0) {
            $sedumpJplOutput | Write-Host
            Fail "sedump.exe exited $LASTEXITCODE on the JPL grid."
        }
        $sedumpJplOutput | Write-Host

        Write-Host 'Running OracleDump.exe (JPL grid)...'
        $oracleDumpJplOutput = @(& $oracleDumpExe $JplGridPath $netJplOutputPath $JplEpheDir $jplFileName 2>&1)
        if ($LASTEXITCODE -ne 0) {
            $oracleDumpJplOutput | Write-Host
            Fail "OracleDump.exe exited $LASTEXITCODE on the JPL grid."
        }
        $oracleDumpJplOutput | Write-Host
    }

    # ---------------------------------------------------------------------------------------
    # 2.08 grid runs -- the prebuilt driver, not compiled by this script (see the .DESCRIPTION
    # and -Sedump208Path). swe_close() runs at the top of every row inside sedump.c regardless of
    # which library it is linked against, so this needs no fresh-state handling beyond what the
    # 2.10.03 runs above already rely on.
    # ---------------------------------------------------------------------------------------

    $c208OutputPath = Join-Path $OutputDir 'dump-c-2.08.tsv'
    Write-Host 'Running sedump-2.08.exe (analytic grid)...'
    $sedump208Output = @(& $Sedump208Path $GridPath $c208OutputPath 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $sedump208Output | Write-Host
        Fail "sedump-2.08.exe exited $LASTEXITCODE on the analytic grid."
    }
    $sedump208Output | Write-Host

    $c208FilesOutputPath = Join-Path $OutputDir 'dump-c-2.08-files.tsv'
    Write-Host 'Running sedump-2.08.exe (files grid)...'
    $sedump208FilesOutput = @(& $Sedump208Path $FilesGridPath $c208FilesOutputPath $EpheDir 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $sedump208FilesOutput | Write-Host
        Fail "sedump-2.08.exe exited $LASTEXITCODE on the files grid."
    }
    $sedump208FilesOutput | Write-Host

    # ---------------------------------------------------------------------------------------
    # Row-count guards: no side may have silently emitted fewer (or more) rows than its grid
    # contains. Says nothing about whether the sides agree on any individual value -- that
    # comparison is scripts/verify-oracle.ps1's job for the port against 2.10.03 C, and
    # scripts/classify-oracle-versions.ps1's job for the three-way comparison against 2.08 C too.
    # ---------------------------------------------------------------------------------------

    $cRowCount = Get-FileLineCount -Path $cOutputPath
    $netRowCount = Get-FileLineCount -Path $netOutputPath
    if ($cRowCount -ne $expectedAnalyticRows) {
        Fail "sedump.exe wrote $cRowCount row(s) to $cOutputPath but the analytic grid has $expectedAnalyticRows data row(s). A driver that silently emits fewer (or more) rows than the grid must not read as a pass."
    }
    if ($netRowCount -ne $expectedAnalyticRows) {
        Fail "OracleDump.exe wrote $netRowCount row(s) to $netOutputPath but the analytic grid has $expectedAnalyticRows data row(s). A driver that silently emits fewer (or more) rows than the grid must not read as a pass."
    }

    $cFilesRowCount = Get-FileLineCount -Path $cFilesOutputPath
    $netFilesRowCount = Get-FileLineCount -Path $netFilesOutputPath
    if ($cFilesRowCount -ne $expectedFilesRows) {
        Fail "sedump.exe wrote $cFilesRowCount row(s) to $cFilesOutputPath but the files grid has $expectedFilesRows data row(s). A driver that silently emits fewer (or more) rows than the grid must not read as a pass."
    }
    if ($netFilesRowCount -ne $expectedFilesRows) {
        Fail "OracleDump.exe wrote $netFilesRowCount row(s) to $netFilesOutputPath but the files grid has $expectedFilesRows data row(s). A driver that silently emits fewer (or more) rows than the grid must not read as a pass."
    }

    $c208RowCount = Get-FileLineCount -Path $c208OutputPath
    if ($c208RowCount -ne $expectedAnalyticRows) {
        Fail "sedump-2.08.exe wrote $c208RowCount row(s) to $c208OutputPath but the analytic grid has $expectedAnalyticRows data row(s). A driver that silently emits fewer (or more) rows than the grid must not read as a pass."
    }
    $c208FilesRowCount = Get-FileLineCount -Path $c208FilesOutputPath
    if ($c208FilesRowCount -ne $expectedFilesRows) {
        Fail "sedump-2.08.exe wrote $c208FilesRowCount row(s) to $c208FilesOutputPath but the files grid has $expectedFilesRows data row(s). A driver that silently emits fewer (or more) rows than the grid must not read as a pass."
    }

    if ($runJplGrid) {
        $cJplRowCount = Get-FileLineCount -Path $cJplOutputPath
        $netJplRowCount = Get-FileLineCount -Path $netJplOutputPath
        if ($cJplRowCount -ne $expectedJplRows) {
            Fail "sedump.exe wrote $cJplRowCount row(s) to $cJplOutputPath but the JPL grid has $expectedJplRows data row(s). A driver that silently emits fewer (or more) rows than the grid must not read as a pass."
        }
        if ($netJplRowCount -ne $expectedJplRows) {
            Fail "OracleDump.exe wrote $netJplRowCount row(s) to $netJplOutputPath but the JPL grid has $expectedJplRows data row(s). A driver that silently emits fewer (or more) rows than the grid must not read as a pass."
        }
    }

    # ---------------------------------------------------------------------------------------
    # Provenance sidecar -- written only once every row-count guard above has passed, so a run
    # that fails never leaves behind a sidecar claiming a dump it did not finish producing. See
    # this script's own PROVENANCE header section and scripts/verify-oracle.ps1, which reads this
    # file and refuses PASS when any recorded hash no longer matches what is on disk now.
    # ---------------------------------------------------------------------------------------

    # Each row is built as a single joined string here, rather than as a three-element array
    # literal joined afterwards: PowerShell unrolls (flattens) an array literal that sits as one
    # element of an outer @(...), so the array-then-join shape would silently flatten all five
    # rows into one 15-element list before the join ever ran, splitting every field onto its own
    # line.
    $srcDir = Join-Path $repoRoot 'SwissEphNet'
    $sourceHash = Get-PortSourceHash -RepoRoot $repoRoot

    $provenanceHeader = @('name', 'path', 'sha256') -join "`t"
    $provenanceRows = @(
        (@('grid_analytic', $GridPath, (Get-Sha256Hex -Path $GridPath)) -join "`t")
        (@('grid_files', $FilesGridPath, (Get-Sha256Hex -Path $FilesGridPath)) -join "`t")
        (@('swisseph_net_source', $srcDir, $sourceHash) -join "`t")
        (@('sedump_exe', $sedumpExe, (Get-Sha256Hex -Path $sedumpExe)) -join "`t")
        (@('sedump_208_exe', $Sedump208Path, (Get-Sha256Hex -Path $Sedump208Path)) -join "`t")
    )

    # Two more rows, and only when the JPL leg actually ran. A skipped run must leave no trace of
    # a grid it did not replay -- scripts/verify-oracle.ps1 -Grid Jpl requires both rows and fails
    # with a "re-run with -JplFile" message when they are absent, which is exactly the right answer
    # for "the JPL dumps on disk are left over from some earlier run". jpl_ephe_file is hashed like
    # any other input: it is 190 MB - 2.6 GB and lives outside the repo, so it is the one recorded
    # input a reader has no other way to identify, and swapping DE406 for DE431 under the same file
    # name would otherwise change every value in the dump with nothing recording that it had.
    if ($runJplGrid) {
        $provenanceRows += (@('grid_jpl', $JplGridPath, (Get-Sha256Hex -Path $JplGridPath)) -join "`t")
        $provenanceRows += (@('jpl_ephe_file', $jplFileFullPath, (Get-Sha256Hex -Path $jplFileFullPath)) -join "`t")
    }
    $provenanceLines = @(
        '# Written by scripts/run-oracle-dump.ps1 on a successful run. scripts/verify-oracle.ps1'
        '# reads this and refuses to report PASS when any row no longer matches what is on disk'
        '# now -- swisseph_net_source is a hash over every *.cs file under SwissEphNet/ plus'
        '# SwissEphNet.csproj, recomputed the same way at verify time; it is not a hash of any'
        '# built artifact. See verify-oracle.ps1 for the check itself.'
    ) + $provenanceHeader + $provenanceRows
    $provenancePath = Join-Path $OutputDir 'oracle-provenance.tsv'
    [System.IO.File]::WriteAllText($provenancePath, ($provenanceLines -join "`n") + "`n")

    Write-Host ''
    $jplSummary = if ($runJplGrid) { " and $expectedJplRows JPL-grid row(s)" } else { '' }
    Write-Host "PASS: every driver wrote $expectedAnalyticRows analytic-grid row(s) and $expectedFilesRows files-grid row(s)$jplSummary, matching their grids." -ForegroundColor Green
    Write-Host "  $cOutputPath"
    Write-Host "  $c208OutputPath"
    Write-Host "  $netOutputPath"
    Write-Host "  $cFilesOutputPath"
    Write-Host "  $c208FilesOutputPath"
    Write-Host "  $netFilesOutputPath"
    if ($runJplGrid) {
        Write-Host "  $cJplOutputPath"
        Write-Host "  $netJplOutputPath"
    }
    Write-Host "  $provenancePath"
}
catch {
    Write-Host "FAIL: $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 1
}
exit $exitCode
