#Requires -Version 7.3
<#
.SYNOPSIS
    Builds and runs both sides of the bit-exact oracle harness's first stage against the
    committed grid, and writes their raw output for a later, separate comparison pass.

.DESCRIPTION
    Tools/OracleGrid/grid-analytic.tsv is the single set of inputs Tools/CReference/sedump.c
    (compiled here, linked against Astrodienst's own C) and Tools/OracleDump (built here,
    against this port) both replay -- see that file's own header for why the grid carries no
    logic of its own and each driver's header for what it does with a row.

    Building sedump.exe needs a libswe .lib to link against. Tools/CReference/build-c.ps1
    produces one at external/.c-reference/build-2.10.03/libswe-2.10.03.lib by default, which is
    also this script's default -LibPath; run that script first if the .lib is missing, this one
    does not build it.

    This script does not compare dump-c-2.10.03.tsv against dump-net.tsv -- that is a separate,
    later task. It only checks that each side emitted exactly as many rows as the grid contains
    (see the row-count guard below), which catches a driver silently truncating its own run
    without saying anything at all about whether the two sides agree on any individual value.

.PARAMETER LibPath
    The libswe .lib sedump.exe links against. Defaults to the 2.10.03 build
    Tools/CReference/build-c.ps1 produces. Pointing this at the 2.08 build instead isolates
    transliteration defects from porting-queue differences -- the same distinction
    Tools/CReference/build-c.ps1's own header draws between the two libraries it builds.

.PARAMETER GridPath
    The grid TSV both drivers replay. Defaults to Tools/OracleGrid/grid-analytic.tsv.

.PARAMETER OutputDir
    Where build products and the two dump files are written. Defaults to external/.c-reference,
    which .gitignore excludes -- these are run outputs of vendored/local source, not source
    themselves. Must resolve to a path under external/.c-reference, the exact path .gitignore
    excludes (not all of external/, most of which is tracked submodule/fetched source); this
    script refuses to write its outputs anywhere .gitignore would not catch.
#>
[CmdletBinding()]
param(
    [string] $LibPath,
    [string] $GridPath,
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
if (-not $OutputDir) { $OutputDir = Join-Path $repoRoot 'external/.c-reference' }

$LibPath = [System.IO.Path]::GetFullPath($LibPath)
$GridPath = [System.IO.Path]::GetFullPath($GridPath)
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

# Counts data rows in grid-analytic.tsv the same way both drivers do: skip '#' comment lines,
# skip the first non-comment line (the column header), count everything after. Not grid logic --
# a line count, used only for the row-count guard below, not to interpret what any row means.
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

$exitCode = 0
try {
    if (-not (Test-Path -LiteralPath $GridPath -PathType Leaf)) {
        Fail "Grid file not found at $GridPath. Run: pwsh Tools/OracleGrid/gen-grid-analytic.ps1"
    }
    if (-not (Test-Path -LiteralPath $LibPath -PathType Leaf)) {
        Fail "Library not found at $LibPath. Run: pwsh Tools/CReference/build-c.ps1"
    }

    $expectedRows = Get-GridDataRowCount -Path $GridPath
    if ($expectedRows -eq 0) { Fail "$GridPath contains zero data rows." }
    Write-Host "Grid: $expectedRows data row(s) in $GridPath"

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

    $cOutputPath = Join-Path $OutputDir 'dump-c-2.10.03.tsv'
    Write-Host 'Running sedump.exe...'
    $sedumpOutput = @(& $sedumpExe $GridPath $cOutputPath 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $sedumpOutput | Write-Host
        Fail "sedump.exe exited $LASTEXITCODE."
    }
    $sedumpOutput | Write-Host

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

    $netOutputPath = Join-Path $OutputDir 'dump-net.tsv'
    Write-Host 'Running OracleDump.exe...'
    $oracleDumpOutput = @(& $oracleDumpExe $GridPath $netOutputPath 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $oracleDumpOutput | Write-Host
        Fail "OracleDump.exe exited $LASTEXITCODE."
    }
    $oracleDumpOutput | Write-Host

    # ---------------------------------------------------------------------------------------
    # The one guard this script owns: neither side may have silently emitted fewer (or more)
    # rows than the grid contains. It says nothing about whether the two sides agree on any
    # individual value -- that comparison is a separate, later task.
    # ---------------------------------------------------------------------------------------

    $cRowCount = Get-FileLineCount -Path $cOutputPath
    $netRowCount = Get-FileLineCount -Path $netOutputPath

    if ($cRowCount -ne $expectedRows) {
        Fail "sedump.exe wrote $cRowCount row(s) to $cOutputPath but the grid has $expectedRows data row(s). A driver that silently emits fewer (or more) rows than the grid must not read as a pass."
    }
    if ($netRowCount -ne $expectedRows) {
        Fail "OracleDump.exe wrote $netRowCount row(s) to $netOutputPath but the grid has $expectedRows data row(s). A driver that silently emits fewer (or more) rows than the grid must not read as a pass."
    }

    Write-Host ''
    Write-Host "PASS: both drivers each wrote $expectedRows row(s), matching the grid." -ForegroundColor Green
    Write-Host "  $cOutputPath"
    Write-Host "  $netOutputPath"
}
catch {
    Write-Host "FAIL: $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 1
}
exit $exitCode
