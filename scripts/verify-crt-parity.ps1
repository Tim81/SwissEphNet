#Requires -Version 7.3
<#
.SYNOPSIS
    Checks the one assumption the whole bit-exact oracle is built on: that MSVC C and
    .NET compute the same IEEE-754 bits for the C runtime math functions on Windows x64.

.DESCRIPTION
    The port is being upgraded from 2.08 to 2.10.03 with a bit-exact target, and that
    target is only reachable because of a specific fact about the runtime, not because
    the port is careful. On Windows x64, CoreCLR implements Math.Sin/Cos/Tan/Asin/Acos/
    Atan/Atan2/Exp/Log/Pow as [MethodImpl(MethodImplOptions.InternalCall)] FCALLs in
    src/coreclr/vm/floatdouble.cpp whose bodies are `return sin(x);` and so on, straight
    into the C runtime -- and on this platform that C runtime is ucrtbase.dll, the same
    DLL an MSVC build compiled /MD binds. Math.Sqrt is the one function here that does not
    depend on this: the JIT emits the sqrtsd instruction directly, and IEEE 754 requires
    that instruction to be exactly rounded, so MSVC and .NET agree on sqrt for a different
    and stronger reason than the rest.

    That fact was measured once, by hand, over 200 values, comparing raw bit patterns:
    MSVC C /MD against .NET 10 gave zero differences. That measurement lived nowhere in
    the repo. Two things could invalidate it silently: a future .NET release moving any of
    these functions to managed code (other parts of the BCL's math surface have gone that
    way already), or ucrtbase.dll servicing diverging from what MSVC links against. Either
    one would make the port look like it regressed against the conformance oracle for a
    reason that has nothing to do with sweph.c, and every residual record from that point
    on would be measuring CRT drift instead of port defects, with nothing pointing at the
    real cause. This script re-checks the assumption instead of trusting the one-time
    measurement forever.

    It compiles Tools/CReference/crt-parity.c with the same flags and CRT linkage
    Tools/CReference/build-c.ps1 uses to build the reference libraries this repo compares
    against (/O2 /fp:precise /MD, x64, MSVC located through vswhere, CL/_CL_ cleared before
    compiling for the same reason build-c.ps1 clears them -- see that script's own
    .DESCRIPTION), runs it, builds and runs Tools/CrtParity (the .NET counterpart), and
    diffs the two programs' output line by line. Both programs emit the same fixed,
    documented spread of double values (see crt-parity.c's header for how the values were
    chosen and why the two files' tables have to be hand-kept in step) through sin, cos,
    tan, asin, acos, atan, atan2, exp, log, log10, pow, sqrt, fmod, floor and ceil, and
    print each result's raw bit pattern as sixteen lowercase hex digits.

    This is deliberately not a disassembly check the way build-c.ps1's FMA/fp:precise
    probes are. Those exist because they cannot see the actual computed values -- a build
    step, not a value comparison. Here the computed values are exactly what gets compared,
    which is a stronger check of the same underlying question: if fp-contraction, argument
    reduction, or anything else about the two toolchains' math actually differed, it would
    show up directly as a bit mismatch below, with no need to infer it from disassembly.

    A FAIL here is not something to work around. If a function genuinely differs, that is
    the gate doing exactly what it exists to do -- report which function, which input
    index, and both bit patterns, and leave it failing.

.PARAMETER MinComparisons
    The run fails if fewer than this many lines were compared, even if every comparison
    that did run agreed -- a build or run that silently produced almost nothing must not
    read as a pass. Defaults to 200, the count the one-time measurement in this script's
    own .DESCRIPTION used; the current fixed value tables produce more than that.

.PARAMETER MinPerOpComparisons
    The run fails if any of the $ExpectedOps functions (below) was compared fewer than this
    many times -- including zero, i.e. the function never appeared in either program's output
    at all. -MinComparisons alone counts lines, not which functions those lines belong to: the
    fixed value tables produce well over 200 lines total even with three whole functions
    dropped from crt-parity.c's emit() calls (a bad merge, an accidental deletion), so a
    total-line floor cannot catch that -- it only asks "was enough compared", never "was the
    right thing compared". Defaults to 5, comfortably below the smallest per-function count any
    of the current fixed tables produce (fmod's pair table, the smallest, has 12 pairs) while
    still requiring more than a token single comparison per function.
#>
[CmdletBinding()]
param(
    [int] $MinComparisons = 200,
    [int] $MinPerOpComparisons = 5
)

# The complete set of CRT functions crt-parity.c's emit() calls exercise (see this script's own
# .DESCRIPTION and crt-parity.c's Main) -- kept here, not derived from the C source or the .NET
# output, specifically so a function silently dropped from one or both programs' emit() calls has
# something external to be checked against instead of the coverage floor only ever seeing
# whatever the two programs currently happen to agree on producing.
$ExpectedOps = @('sin', 'cos', 'tan', 'atan', 'exp', 'floor', 'ceil', 'asin', 'acos', 'log', 'log10', 'sqrt', 'atan2', 'pow', 'fmod')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# See Tools/CReference/build-c.ps1's own copy of this line for why it is set even though
# it changes nothing under the pwsh version this was written against: cl, link and dotnet
# below are all native commands, and this keeps a future default flip from swallowing the
# diagnostic output those commands print on failure.
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = Split-Path -Parent $PSScriptRoot
$cSourcePath = Join-Path $repoRoot 'Tools/CReference/crt-parity.c'
$csprojPath = Join-Path $repoRoot 'Tools/CrtParity/CrtParity.csproj'

function Fail($message) {
    # Thrown, not exited: a bare `exit` would skip the finally block below that cleans up
    # the temp build directory. See the top-level try/catch/finally.
    throw $message
}

if (-not (Test-Path -LiteralPath $cSourcePath -PathType Leaf)) {
    Write-Host "FAIL: C source not found at $cSourcePath."
    exit 1
}
if (-not (Test-Path -LiteralPath $csprojPath -PathType Leaf)) {
    Write-Host "FAIL: csproj not found at $csprojPath."
    exit 1
}

# ---------------------------------------------------------------------------------------
# Toolchain -- same recipe as Tools/CReference/build-c.ps1, kept as a separate copy here
# rather than dot-sourcing that script, since dot-sourcing it would run its entire library
# build as a side effect. Consistency with it is a review property, not something to
# achieve by sharing code between the two.
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

# Runs a command inside the MSVC x64 environment. vcvars64.bat only sets variables for the
# shell it runs in, so the compiler invocation has to happen in that same cmd process.
function Invoke-InVcEnv {
    param([string] $VcVars, [string] $WorkingDir, [string] $Command)
    $full = "`"$VcVars`" >nul 2>&1 && cd /d `"$WorkingDir`" && $Command"
    $output = cmd /c $full 2>&1
    return @{ ExitCode = $LASTEXITCODE; Output = $output }
}

$tempRoot = $null
$exitCode = 0
try {
    $vcvars = Get-VcVarsPath
    Write-Host "Toolchain: $vcvars"

    # cl.exe silently prepends %CL% and appends %_CL_% to every invocation it runs, and
    # vcvars64.bat does not clear either -- see build-c.ps1's own comment on this same
    # step for the measured example of how that silently changes what gets compiled while
    # nothing recorded about the run admits it.
    $envClOriginal = $env:CL
    $envClUnderscoreOriginal = $env:_CL_
    if ($envClOriginal) { Write-Host "NOTE: clearing inherited CL='$envClOriginal' before compiling." -ForegroundColor Yellow }
    if ($envClUnderscoreOriginal) { Write-Host "NOTE: clearing inherited _CL_='$envClUnderscoreOriginal' before compiling." -ForegroundColor Yellow }
    Remove-Item Env:\CL -ErrorAction SilentlyContinue
    Remove-Item Env:\_CL_ -ErrorAction SilentlyContinue

    # Identical to $commonFlags in Tools/CReference/build-c.ps1: /MD so this compiles
    # against the same shared ucrtbase.dll CoreCLR's own FCALLs bind, /fp:precise so
    # neither side reassociates or contracts a multiply-add, /O2 because the reference
    # build itself is optimized, not the unoptimized debug build Astrodienst ships.
    $commonFlags = '/O2 /fp:precise /D_CRT_SECURE_NO_WARNINGS /MD'

    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "crt-parity-$([System.Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null

    # =====================================================================================
    # C side
    # =====================================================================================

    Write-Host 'Compiling crt-parity.c...'
    $cExePath = Join-Path $tempRoot 'crt-parity.exe'
    $compile = "cl /nologo /TC $commonFlags /Fe:`"$cExePath`" `"$cSourcePath`""
    $result = Invoke-InVcEnv -VcVars $vcvars -WorkingDir $tempRoot -Command $compile
    if ($result.ExitCode -ne 0) {
        $result.Output | Write-Host
        Fail 'Compiling crt-parity.c failed.'
    }
    if (-not (Test-Path -LiteralPath $cExePath -PathType Leaf)) {
        Fail "cl reported success but $cExePath does not exist."
    }

    Write-Host 'Running crt-parity.exe...'
    $cOutput = @(& $cExePath)
    if ($LASTEXITCODE -ne 0) {
        $cOutput | Write-Host
        Fail "crt-parity.exe exited $LASTEXITCODE."
    }

    # =====================================================================================
    # .NET side
    # =====================================================================================

    Write-Host 'Building Tools/CrtParity...'
    $dotnetOutDir = Join-Path $tempRoot 'dotnet-out'
    $buildOutput = & dotnet build $csprojPath -c Release -o $dotnetOutDir --nologo -v minimal 2>&1
    if ($LASTEXITCODE -ne 0) {
        $buildOutput | Write-Host
        Fail 'dotnet build Tools/CrtParity failed.'
    }

    $dotnetExePath = Join-Path $dotnetOutDir 'CrtParity.exe'
    if (-not (Test-Path -LiteralPath $dotnetExePath -PathType Leaf)) {
        Fail "dotnet build reported success but $dotnetExePath does not exist. Is this running on Windows (the apphost .exe is Windows-only)?"
    }

    Write-Host 'Running CrtParity.exe...'
    $dotnetOutput = @(& $dotnetExePath)
    if ($LASTEXITCODE -ne 0) {
        $dotnetOutput | Write-Host
        Fail "CrtParity.exe exited $LASTEXITCODE."
    }

    # =====================================================================================
    # Compare -- vacuity guards first, so a run that produced almost nothing cannot read as
    # agreement just because zero disagreements were found among zero comparisons.
    # =====================================================================================

    if ($cOutput.Count -eq 0) {
        Fail 'crt-parity.exe produced no output. A gate that compared nothing is not a pass.'
    }
    if ($dotnetOutput.Count -eq 0) {
        Fail 'CrtParity.exe produced no output. A gate that compared nothing is not a pass.'
    }
    if ($cOutput.Count -ne $dotnetOutput.Count) {
        Fail "crt-parity.exe printed $($cOutput.Count) line(s) but CrtParity.exe printed $($dotnetOutput.Count). The two programs' value tables have drifted out of step -- see crt-parity.c's header comment on how they are meant to be kept identical."
    }
    if ($cOutput.Count -lt $MinComparisons) {
        Fail "Only $($cOutput.Count) comparison(s) ran, below the -MinComparisons floor of $MinComparisons. Too few to say anything about CRT parity."
    }

    $mismatches = [System.Collections.Generic.List[string]]::new()
    $opIndex = @{}

    for ($i = 0; $i -lt $cOutput.Count; $i++) {
        $cFields = $cOutput[$i] -split "`t"
        $dotnetFields = $dotnetOutput[$i] -split "`t"
        if ($cFields.Count -ne 2) {
            Fail "crt-parity.exe line $($i + 1) is not in 'name<TAB>bits' form: '$($cOutput[$i])'"
        }
        if ($dotnetFields.Count -ne 2) {
            Fail "CrtParity.exe line $($i + 1) is not in 'name<TAB>bits' form: '$($dotnetOutput[$i])'"
        }

        $cName, $cBits = $cFields
        $dotnetName, $dotnetBits = $dotnetFields

        if ($cName -ne $dotnetName) {
            Fail "Line $($i + 1): crt-parity.exe emitted operation '$cName' but CrtParity.exe emitted '$dotnetName' at the same position. The two programs' call order has drifted out of step, not just a value -- see crt-parity.c's header comment."
        }

        if (-not $opIndex.ContainsKey($cName)) { $opIndex[$cName] = 0 }
        $index = $opIndex[$cName]
        $opIndex[$cName] = $index + 1

        if ($cBits -ne $dotnetBits) {
            $mismatches.Add("$cName input #$index (line $($i + 1)): C = 0x$cBits, .NET = 0x$dotnetBits")
        }
    }

    # Per-op coverage floor, not just a total-line floor: $cOutput.Count -lt $MinComparisons
    # above counts lines regardless of which function produced them, so dropping an entire
    # function's emit() calls from crt-parity.c (a bad merge, an accidental deletion) still
    # clears it as long as the remaining functions' lines add up past $MinComparisons -- the
    # current fixed value tables comfortably do, even missing three whole functions. This checks
    # the thing -MinComparisons cannot: that every function this gate exists to cover was
    # actually exercised, not just that enough lines came out of *something*.
    $missingOps = [System.Collections.Generic.List[string]]::new()
    foreach ($op in $ExpectedOps) {
        $seen = if ($opIndex.ContainsKey($op)) { $opIndex[$op] } else { 0 }
        if ($seen -lt $MinPerOpComparisons) {
            $missingOps.Add("$op ($seen comparison(s), below the -MinPerOpComparisons floor of $MinPerOpComparisons)")
        }
    }
    if ($missingOps.Count -gt 0) {
        Fail "Per-op coverage floor not met for $($missingOps.Count) of $($ExpectedOps.Count) expected function(s): $($missingOps -join '; '). crt-parity.c's emit() calls (or Tools/CrtParity's C# counterpart) may be missing a function entirely -- $($cOutput.Count) total line(s) cleared -MinComparisons, but that says nothing about which functions they covered."
    }

    if ($mismatches.Count -gt 0) {
        Write-Host ''
        Write-Host "FAIL: $($mismatches.Count) of $($cOutput.Count) comparison(s) disagree between MSVC C and .NET." -ForegroundColor Red
        Write-Host 'This is a real difference in what the two toolchains compute, not a harness problem.'
        Write-Host 'Do not loosen this comparison or drop the offending function to make the gate pass --'
        Write-Host 'report which function and which input diverged; the bit-exact oracle premise this'
        Write-Host 'gate exists to check no longer holds for at least the case(s) below.'
        Write-Host ''
        foreach ($m in $mismatches) { Write-Host "  $m" }
        $exitCode = 1
    }
    else {
        Write-Host ''
        Write-Host "PASS: $($cOutput.Count) values compared across sin/cos/tan/asin/acos/atan/atan2/exp/log/log10/pow/sqrt/fmod/floor/ceil -- MSVC C and .NET agree bit-for-bit on every one." -ForegroundColor Green
    }
}
catch {
    Write-Host "FAIL: $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 1
}
finally {
    if ($tempRoot -and (Test-Path -LiteralPath $tempRoot)) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
exit $exitCode
