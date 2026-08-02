#Requires -Version 7.3
<#
.SYNOPSIS
    Asserts that every place sedump.c is compiled against 2.10.03 defines the same SWISSEPH_HAS_*
    macro set, and that the 2.08 build defines none of them.

.DESCRIPTION
    Tools/CReference/sedump.c guards the API that does not exist in Swiss Ephemeris 2.08 behind
    #ifdef SWISSEPH_HAS_* macros, with an #else branch that emits a fixed sentinel row per case
    (NOT_IN_208_RETC plus an explanatory serr, at the same column count the real branch uses). That
    lets one source file serve both the 2.10.03 driver and the 2.08 one.

    The failure mode this gate exists for, measured rather than imagined: sedump.c is compiled
    against 2.10.03 in SIX places -- once in scripts/run-oracle-dump.ps1 and four times in
    .github/workflows/oracle.yml (two clang, two gcc), plus the deliberate 2.08 build in
    Tools/CReference/build-c.ps1. When SWISSEPH_HAS_HOUSES_EX2 was added it was added to the
    Windows build only. The four non-Windows lines kept taking the #else branch, so the C side
    emitted the sentinel for 4,500 analytic rows while the port computed real values, and
    linux-exactness and macos-exactness failed at their cmp step. Reproduced under gcc in a
    container before this gate was written: 4,500 differing rows with the CI compile line,
    bit-identical with both macros.

    Nothing caught it earlier. The #else branch compiles cleanly, so there is no build error; the
    row COUNT still matches, because the sentinel branch emits the same number of columns, so
    run-oracle-dump.ps1's own row-count guards stay green; and the Windows job passes, because the
    Windows build is the one that was updated. Only a full cross-platform replay shows it, which is
    exactly the thing that runs last and costs the most.

    The required macro set is derived from sedump.c itself -- every SWISSEPH_HAS_* symbol it
    actually tests -- rather than hardcoded here. A macro added to sedump.c with no compile site
    updated therefore fails this gate immediately, which is the case that matters: the list cannot
    go stale relative to the source it describes.

.PARAMETER SelfTest
    Plants each known bypass into a copy of the inputs and asserts this gate refuses. Runs no
    compiler and touches no tracked file.
#>
[CmdletBinding()]
param([switch] $SelfTest)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

function Get-RequiredMacros {
    param([string] $SedumpPath)
    $text = [System.IO.File]::ReadAllText($SedumpPath, [System.Text.UTF8Encoding]::new($false, $true))
    # Only #ifdef/#ifndef/#if defined() lines, not prose mentions in the header comment -- the
    # file's own comment block names these macros many times and must not inflate the set.
    $names = [System.Collections.Generic.SortedSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($m in [regex]::Matches($text, '(?m)^\s*#\s*(?:ifdef|ifndef)\s+(SWISSEPH_HAS_[A-Z0-9_]+)')) {
        [void]$names.Add($m.Groups[1].Value)
    }
    foreach ($m in [regex]::Matches($text, '(?m)^\s*#\s*if\s+.*?\bdefined\s*\(\s*(SWISSEPH_HAS_[A-Z0-9_]+)\s*\)')) {
        [void]$names.Add($m.Groups[1].Value)
    }
    return , @($names)
}

# Rather than try to recognise a C compiler invocation -- which means parsing shell
# continuations, PowerShell string building, and telling a real command from an ::error:: message
# whose prose happens to contain both "sedump.c" and "gcc" (this gate's first draft flagged exactly
# that line) -- invert the rule. Find every line that defines ANY SWISSEPH_HAS_* macro, and require
# each one to define ALL of them. That is precisely the defect: a compile line updated for one
# macro and not the other. Ordinal matching throughout; PowerShell's -match and -like are
# culture-aware and case-insensitive by default and this repository has been bitten by both.
function Get-MacroBearingLines {
    param([string[]] $Files)
    $sites = @()
    foreach ($file in $Files) {
        if (-not (Test-Path -LiteralPath $file)) { continue }
        $lineNo = 0
        foreach ($line in [System.IO.File]::ReadAllLines($file)) {
            $lineNo++
            if ([regex]::IsMatch($line, '[/-]D\s*SWISSEPH_HAS_[A-Z0-9_]+')) {
                $sites += [pscustomobject]@{ File = $file; Line = $lineNo; Text = $line }
            }
        }
    }
    return , $sites
}

function Get-DefinedMacros {
    param([string] $Text)
    $found = [System.Collections.Generic.SortedSet[string]]::new([System.StringComparer]::Ordinal)
    # Both spellings: MSVC /D and gcc/clang -D.
    foreach ($m in [regex]::Matches($Text, '[/-]D\s*(SWISSEPH_HAS_[A-Z0-9_]+)')) {
        [void]$found.Add($m.Groups[1].Value)
    }
    return , @($found)
}

function Test-Parity {
    param([string] $SedumpPath, [string[]] $ScanFiles, [string] $Build208File)

    $problems = @()
    $required = Get-RequiredMacros -SedumpPath $SedumpPath
    if ($required.Count -eq 0) {
        $problems += "sedump.c tests no SWISSEPH_HAS_* macro at all. Either the guards were removed (in which case delete this gate) or the pattern this gate matches no longer matches the source -- a vacuous pass is not a pass."
        return [pscustomobject]@{ Required = $required; Problems = $problems; Sites = @() }
    }

    $sites = Get-MacroBearingLines -Files $ScanFiles
    if ($sites.Count -eq 0) {
        $problems += "no line anywhere defines a SWISSEPH_HAS_* macro, yet sedump.c guards $($required.Count) of them. Every 2.10.03 build would take the 2.08 sentinel branch. A gate that matches nothing is not a passing gate."
        return [pscustomobject]@{ Required = $required; Problems = $problems; Sites = $sites }
    }

    foreach ($site in $sites) {
        $defined = Get-DefinedMacros -Text $site.Text
        $is208 = $site.File.EndsWith($Build208File, [System.StringComparison]::Ordinal)
        if ($is208) {
            # The 2.08 build must define NONE of them: defining one would make it call an API that
            # does not exist in that library, which fails to link rather than silently misbehaving,
            # but the intent is worth asserting where it is visible.
            if ($defined.Count -gt 0) {
                $problems += "$($site.File):$($site.Line) is the 2.08 build and defines $($defined -join ', '). It must define none -- the #else sentinel branch is the whole point of that build."
            }
            continue
        }
        $missing = @($required | Where-Object { $defined -notcontains $_ })
        if ($missing.Count -gt 0) {
            $problems += "$($site.File):$($site.Line) defines $($defined -join ', ') but not $($missing -join ', '). A build from this line takes the 2.08 sentinel branch for the missing guard, and its dump will disagree with the port's."
        }
    }

    return [pscustomobject]@{ Required = $required; Problems = $problems; Sites = $sites }
}

# ---------------------------------------------------------------------------------------

$sedump = Join-Path $repoRoot 'Tools/CReference/sedump.c'
$build208 = 'build-c.ps1'
$scan = @(
    (Join-Path $repoRoot 'scripts/run-oracle-dump.ps1')
    (Join-Path $repoRoot 'Tools/CReference/build-c.ps1')
) + @(Get-ChildItem -LiteralPath (Join-Path $repoRoot '.github/workflows') -Filter *.yml -ErrorAction SilentlyContinue |
        ForEach-Object { $_.FullName })

if ($SelfTest) {
    $lab = Join-Path ([System.IO.Path]::GetTempPath()) ("sedump-macro-parity-selftest-" + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $lab | Out-Null
    try {
        $fakeSedump = Join-Path $lab 'sedump.c'
        @(
            '#ifdef SWISSEPH_HAS_CROSSING'
            '#endif'
            '#ifdef SWISSEPH_HAS_HOUSES_EX2'
            '#endif'
        ) | Set-Content -LiteralPath $fakeSedump -Encoding utf8

        $cases = @(
            @{ Name = 'both macros on the one line'; Lines = @(
                'gcc -O2 -DSWISSEPH_HAS_CROSSING=1 -DSWISSEPH_HAS_HOUSES_EX2=1 -o sedump sedump.c'); Expect = 0 }
            @{ Name = 'the real defect: a line updated for one macro and not the other'; Lines = @(
                'gcc -O2 -DSWISSEPH_HAS_CROSSING=1 -o sedump sedump.c'); Expect = 1 }
            @{ Name = 'the other way round'; Lines = @(
                'clang -DSWISSEPH_HAS_HOUSES_EX2=1 -o sedump-nb sedump.c'); Expect = 1 }
            @{ Name = 'four lines, one of them stale'; Lines = @(
                'clang -DSWISSEPH_HAS_CROSSING=1 -DSWISSEPH_HAS_HOUSES_EX2=1 -o a sedump.c'
                'clang -DSWISSEPH_HAS_CROSSING=1 -DSWISSEPH_HAS_HOUSES_EX2=1 -o b sedump.c'
                'gcc   -DSWISSEPH_HAS_CROSSING=1 -o c sedump.c'
                'gcc   -DSWISSEPH_HAS_CROSSING=1 -DSWISSEPH_HAS_HOUSES_EX2=1 -o d sedump.c'); Expect = 1 }
            @{ Name = 'MSVC slash-D spelling counts as defined'; Lines = @(
                '$commonFlags = ''/O2 /DSWISSEPH_HAS_CROSSING=1 /DSWISSEPH_HAS_HOUSES_EX2=1 /MD'''); Expect = 0 }
            @{ Name = 'spaces between -D and the name'; Lines = @(
                'gcc -D SWISSEPH_HAS_CROSSING=1 -D SWISSEPH_HAS_HOUSES_EX2=1 -o sedump sedump.c'); Expect = 0 }
            @{ Name = 'prose naming sedump.c and gcc is not a definition and must not be flagged'; Lines = @(
                'echo "::error::Tools/CReference/sedump.c now calls sincos() -- can only be gcc''s own substitution"'
                'gcc -DSWISSEPH_HAS_CROSSING=1 -DSWISSEPH_HAS_HOUSES_EX2=1 -o sedump sedump.c'); Expect = 0 }
            @{ Name = 'nothing defines any macro at all is a vacuous pass, not a pass'; Lines = @(
                '# nothing here defines anything'); Expect = 1 }
        )

        $failed = 0
        foreach ($case in $cases) {
            $wf = Join-Path $lab 'fake-workflow.yml'
            $case.Lines | Set-Content -LiteralPath $wf -Encoding utf8
            $result = Test-Parity -SedumpPath $fakeSedump -ScanFiles @($wf) -Build208File $build208
            $actual = if ($result.Problems.Count -gt 0) { 1 } else { 0 }
            if ($actual -ne $case.Expect) {
                Write-Host "  SELFTEST FAIL: $($case.Name) -- expected $($case.Expect), got $actual" -ForegroundColor Red
                foreach ($p in $result.Problems) { Write-Host "      $p" }
                $failed++
            } else {
                Write-Host "  ok: $($case.Name)" -ForegroundColor DarkGray
            }
        }

        # A macro tested by sedump.c but defined nowhere must fail, which is what keeps the
        # required set honest rather than a restatement of what the compile lines already say.
        $threeMacro = Join-Path $lab 'sedump3.c'
        @('#ifdef SWISSEPH_HAS_CROSSING', '#endif', '#ifdef SWISSEPH_HAS_BRAND_NEW', '#endif') |
            Set-Content -LiteralPath $threeMacro -Encoding utf8
        $wf = Join-Path $lab 'fake-workflow.yml'
        @('gcc -DSWISSEPH_HAS_CROSSING=1 -o sedump sedump.c') | Set-Content -LiteralPath $wf -Encoding utf8
        $r = Test-Parity -SedumpPath $threeMacro -ScanFiles @($wf) -Build208File $build208
        if ($r.Problems.Count -eq 0) {
            Write-Host "  SELFTEST FAIL: a macro sedump.c tests but no compile line defines was not caught" -ForegroundColor Red
            $failed++
        } else {
            Write-Host "  ok: a newly added guard with no compile site updated is caught" -ForegroundColor DarkGray
        }

        if ($failed -gt 0) {
            Write-Host "FAIL: $failed self-test case(s) did not behave as required." -ForegroundColor Red
            exit 1
        }
        Write-Host "PASS: all self-test cases behaved as required." -ForegroundColor Green
        exit 0
    }
    finally {
        Remove-Item -LiteralPath $lab -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$result = Test-Parity -SedumpPath $sedump -ScanFiles $scan -Build208File $build208

Write-Host "sedump.c guards: $($result.Required -join ', ')"
Write-Host "compile sites found: $($result.Sites.Count)"

if ($result.Problems.Count -gt 0) {
    foreach ($p in $result.Problems) { Write-Host "FAIL: $p" -ForegroundColor Red }
    exit 1
}

Write-Host "PASS: every 2.10.03 compile site defines all $($result.Required.Count) guard macro(s); the 2.08 build defines none." -ForegroundColor Green
exit 0
