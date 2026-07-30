#Requires -Version 7.3
<#
.SYNOPSIS
    Builds and runs both sides of the bit-exact oracle harness against both committed grids, and
    writes their raw output for a later, separate comparison pass.

.DESCRIPTION
    Two grids, both replayed by the same pair of drivers (Tools/CReference/sedump.c, compiled
    here and linked against Astrodienst's own C; Tools/OracleDump, built here, against this
    port):

      Tools/OracleGrid/grid-analytic.tsv  -- SEFLG_MOSEPH swe_calc/swe_calc_ut plus
                                              swe_houses/swe_houses_armc. Touches no ephemeris
                                              data file. See that file's own header.
      Tools/OracleGrid/grid-files.tsv     -- SEFLG_SWIEPH swe_calc/swe_calc_ut, the swe_fixstar
                                              family, and swe_get_planet_name. Opens the shipped
                                              .se1/sefstars.txt files under -EpheDir. See that
                                              file's own header.

    Building sedump.exe needs a libswe .lib to link against. Tools/CReference/build-c.ps1
    produces one at external/.c-reference/build-2.10.03/libswe-2.10.03.lib by default, which is
    also this script's default -LibPath; run that script first if the .lib is missing, this one
    does not build it.

    This script does not compare the two sides' output against each other -- that is
    scripts/verify-oracle.ps1's job. It only checks that each side emitted exactly as many rows
    as its grid contains (see the row-count guards below), which catches a driver silently
    truncating its own run without saying anything at all about whether the two sides agree on
    any individual value.

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
    The libswe .lib sedump.exe links against. Defaults to the 2.10.03 build
    Tools/CReference/build-c.ps1 produces. Pointing this at the 2.08 build instead isolates
    transliteration defects from porting-queue differences -- the same distinction
    Tools/CReference/build-c.ps1's own header draws between the two libraries it builds.

.PARAMETER GridPath
    The analytic grid TSV both drivers replay. Defaults to Tools/OracleGrid/grid-analytic.tsv.

.PARAMETER FilesGridPath
    The file-backed grid TSV both drivers replay. Defaults to Tools/OracleGrid/grid-files.tsv.

.PARAMETER EpheDir
    Directory both drivers open grid-files.tsv's ephemeris data files from. Defaults to
    external/swisseph/ephe (the sparse submodule checkout CONTRIBUTING.md documents). Never
    passed to grid-analytic.tsv's run -- see the .DESCRIPTION.

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
    [string] $GridPath,
    [string] $FilesGridPath,
    [string] $EpheDir,
    [string] $OutputDir
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

$LibPath = [System.IO.Path]::GetFullPath($LibPath)
$GridPath = [System.IO.Path]::GetFullPath($GridPath)
$FilesGridPath = [System.IO.Path]::GetFullPath($FilesGridPath)
$EpheDir = [System.IO.Path]::GetFullPath($EpheDir)
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)

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

    $expectedAnalyticRows = Get-GridDataRowCount -Path $GridPath
    if ($expectedAnalyticRows -eq 0) { Fail "$GridPath contains zero data rows." }
    Write-Host "Analytic grid: $expectedAnalyticRows data row(s) in $GridPath"

    $expectedFilesRows = Get-GridDataRowCount -Path $FilesGridPath
    if ($expectedFilesRows -eq 0) { Fail "$FilesGridPath contains zero data rows." }
    Write-Host "Files grid:    $expectedFilesRows data row(s) in $FilesGridPath"

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
    # compiles against the identical toolchain the linked .lib itself was built with.
    $commonFlags = '/O2 /fp:precise /D_CRT_SECURE_NO_WARNINGS /MD'
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
    # Row-count guards: neither side may have silently emitted fewer (or more) rows than its
    # grid contains. Says nothing about whether the two sides agree on any individual value --
    # that comparison is scripts/verify-oracle.ps1's job.
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

    Write-Host ''
    Write-Host "PASS: both drivers wrote $expectedAnalyticRows analytic-grid row(s) and $expectedFilesRows files-grid row(s), matching their grids." -ForegroundColor Green
    Write-Host "  $cOutputPath"
    Write-Host "  $netOutputPath"
    Write-Host "  $cFilesOutputPath"
    Write-Host "  $netFilesOutputPath"
}
catch {
    Write-Host "FAIL: $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 1
}
exit $exitCode
