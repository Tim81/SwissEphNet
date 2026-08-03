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
    against 2.10.03 in FIVE places -- once in scripts/run-oracle-dump.ps1 and four times in
    .github/workflows/oracle.yml (two clang, two gcc). (Six, not five, if the deliberate 2.08
    build in Tools/CReference/build-c.ps1 is counted alongside them -- but that build is compiled
    against 2.08, not 2.10.03, and is the one site required to define NONE of these macros, so
    folding it into "compiled against 2.10.03" undercounts the 2.10.03 sites by one; an earlier
    revision of this sentence did exactly that and the six-vs-five confusion it caused reached a
    PR description before being caught here.) When SWISSEPH_HAS_HOUSES_EX2 was added it was added to the
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

# Get-WorkflowScanFiles -- shared with scripts/verify-workflow-continue-on-error.ps1 so both
# scanners agree, byte for byte, on what counts as a workflow-shaped file under .github/. See that
# library file's own header for why it is dot-sourced rather than the two scripts sharing code by
# copy-paste.
. (Join-Path $PSScriptRoot 'lib/WorkflowScan.ps1')

function Get-RequiredMacros {
    param([string] $SedumpPath)
    $text = [System.IO.File]::ReadAllText($SedumpPath, [System.Text.UTF8Encoding]::new($false, $true))
    # Only #ifdef/#ifndef/#if/#elif lines, not prose mentions in the header comment -- the file's
    # own comment block names these macros many times and must not inflate the set.
    $names = [System.Collections.Generic.SortedSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($m in [regex]::Matches($text, '(?m)^\s*#\s*(?:ifdef|ifndef)\s+(SWISSEPH_HAS_[A-Z0-9_]+)')) {
        [void]$names.Add($m.Groups[1].Value)
    }
    # #if / #elif, one or more defined(...) terms per line -- e.g. "#if defined(A) || defined(B)".
    # Matched per LINE first, then every defined(...) occurrence on THAT line extracted
    # independently. A single file-wide regex anchored at the line start with one .*? capture group
    # (this function's own earlier form) can only ever find the FIRST defined() on a line: once it
    # matches, [regex]::Matches resumes searching right after that match -- no longer at a line
    # start -- so a second defined() on the same #if never gets a chance to match, and #elif was not
    # matched at all under an #if-only pattern. Splitting "find the qualifying lines" from "extract
    # every defined() on each one" fixes both at once.
    foreach ($lineMatch in [regex]::Matches($text, '(?m)^\s*#\s*(?:if|elif)\b.*$')) {
        foreach ($dm in [regex]::Matches($lineMatch.Value, '\bdefined\s*\(\s*(SWISSEPH_HAS_[A-Z0-9_]+)\s*\)')) {
            [void]$names.Add($dm.Groups[1].Value)
        }
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
#
# Comment lines are excluded ('#' is a comment marker in every file this gate scans -- PowerShell
# and the bash `run:` blocks inside GitHub Actions workflow YAML alike): a prose comment that
# happens to mention "-DSWISSEPH_HAS_..." must not count as a compile site, or deleting every real
# -D flag and leaving one such stale comment behind would keep this gate green. This repository
# already has a comment that goes out of its way to avoid the literal "/D" + name adjacency this
# scan matches (see run-oracle-dump.ps1's own comment on its $commonFlags line) precisely because
# of this risk; excluding comment lines outright removes the need for every future comment to be
# equally careful.
function Get-MacroBearingLines {
    param([string[]] $Files)
    $sites = @()
    foreach ($file in $Files) {
        if (-not (Test-Path -LiteralPath $file)) { continue }
        $lineNo = 0
        foreach ($line in [System.IO.File]::ReadAllLines($file)) {
            $lineNo++
            if ($line.TrimStart().StartsWith('#')) { continue }
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

# scripts/*.sh -- MEDIUM 3's own review: a compile site in a shell script under scripts/ (this
# repository has none today, but nothing stops one being added, e.g. a Linux/macOS-oriented
# helper alongside the .ps1 scripts) is invisible to a scan that only ever looked at .ps1 files and
# workflow YAML. Recursive, matching Get-WorkflowScanFiles' own posture -- scripts/ nests scripts
# under scripts/lib/ already.
function Get-ShellScriptScanFiles {
    param([string] $RepoRoot)
    $scriptsDir = Join-Path $RepoRoot 'scripts'
    if (-not (Test-Path -LiteralPath $scriptsDir -PathType Container)) { return @() }
    return @(Get-ChildItem -LiteralPath $scriptsDir -File -Recurse -Filter '*.sh' -ErrorAction SilentlyContinue |
            ForEach-Object { $_.FullName })
}

# Any file literally named Makefile, anywhere in the repository this gate controls -- MEDIUM 3's
# own review names this as a third invisible-site shape alongside composite actions and shell
# scripts. Excludes external/ (vendored/submodule source this repository does not control, the
# same exclusion scripts/verify-oracle.ps1's own Get-PortSourceHash applies for the same reason)
# and bin/obj/.git (build output and VCS internals, never a real compile site).
function Get-MakefileScanFiles {
    param([string] $RepoRoot)
    return @(Get-ChildItem -LiteralPath $RepoRoot -File -Recurse -Filter 'Makefile' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '[\\/](external|bin|obj|\.git)[\\/]' } |
            ForEach-Object { $_.FullName })
}

function Test-Parity {
    param([string] $SedumpPath, [string[]] $ScanFiles, [string] $Build208File, [int] $ExpectedSiteCount)

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

    # The count itself is asserted, not just "at least one site found". A compile site that dropped
    # every SWISSEPH_HAS_* macro (defining none at all, rather than some) is invisible to the -D scan
    # above -- it simply never becomes a "site" -- so the per-site loop below never sees it and has
    # nothing to complain about. That is the worst form of the original defect: not a site with a
    # missing guard, but a whole compile line no check even knows exists. Comparing the found count
    # against the number of 2.10.03 compile sites this repository actually has (measured, not
    # guessed -- see $expectedSiteCount below) is what catches a site vanishing from this scan
    # entirely, the same way it would catch a spurious extra one.
    if ($sites.Count -ne $ExpectedSiteCount) {
        $siteList = ($sites | ForEach-Object { "$($_.File):$($_.Line)" }) -join ', '
        $problems += "found $($sites.Count) compile site(s) defining at least one SWISSEPH_HAS_* guard macro ($siteList), expected exactly $ExpectedSiteCount. A site that dropped every guard macro (so it defines none) is invisible to this scan on its own -- this count is what catches it."
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
# .github (not .github/workflows) -- MEDIUM 3: recursive over the whole tree so a workflow saved
# under a subdirectory and a composite action under .github/actions/*/action.yml are both in
# scope, not just top-level files directly inside .github/workflows. See Get-WorkflowScanFiles'
# own comment.
$githubDir = Join-Path $repoRoot '.github'
$scan = @(
    (Join-Path $repoRoot 'scripts/run-oracle-dump.ps1')
    (Join-Path $repoRoot 'Tools/CReference/build-c.ps1')
) + @(Get-WorkflowScanFiles -WorkflowsDir $githubDir) + @(Get-ShellScriptScanFiles -RepoRoot $repoRoot) + @(Get-MakefileScanFiles -RepoRoot $repoRoot)

# Measured, not guessed: scripts/run-oracle-dump.ps1's own $commonFlags line (one site) plus the
# four clang/gcc invocations in .github/workflows/oracle.yml (two clang -- the strict build and the
# builtins-left-on diagnostic -- and two gcc, the same pair) -- see this file's own .DESCRIPTION.
# Tools/CReference/build-c.ps1's 2.08 build defines none of these macros by design, so it never
# becomes a "site" under Get-MacroBearingLines' -D scan and is correctly excluded from this count.
$expectedSiteCount = 5

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

        # ExpectedSiteCount is per-case: it is how many lines in that case's own fixture actually
        # define at least one SWISSEPH_HAS_* macro (via the -D scan), which the site-count assertion
        # requires Test-Parity be told up front -- exactly as the real invocation is told 5, below.
        $cases = @(
            @{ Name = 'both macros on the one line'; Lines = @(
                'gcc -O2 -DSWISSEPH_HAS_CROSSING=1 -DSWISSEPH_HAS_HOUSES_EX2=1 -o sedump sedump.c'); Expect = 0; ExpectedSiteCount = 1 }
            @{ Name = 'the real defect: a line updated for one macro and not the other'; Lines = @(
                'gcc -O2 -DSWISSEPH_HAS_CROSSING=1 -o sedump sedump.c'); Expect = 1; ExpectedSiteCount = 1 }
            @{ Name = 'the other way round'; Lines = @(
                'clang -DSWISSEPH_HAS_HOUSES_EX2=1 -o sedump-nb sedump.c'); Expect = 1; ExpectedSiteCount = 1 }
            @{ Name = 'four lines, one of them stale'; Lines = @(
                'clang -DSWISSEPH_HAS_CROSSING=1 -DSWISSEPH_HAS_HOUSES_EX2=1 -o a sedump.c'
                'clang -DSWISSEPH_HAS_CROSSING=1 -DSWISSEPH_HAS_HOUSES_EX2=1 -o b sedump.c'
                'gcc   -DSWISSEPH_HAS_CROSSING=1 -o c sedump.c'
                'gcc   -DSWISSEPH_HAS_CROSSING=1 -DSWISSEPH_HAS_HOUSES_EX2=1 -o d sedump.c'); Expect = 1; ExpectedSiteCount = 4 }
            @{ Name = 'MSVC slash-D spelling counts as defined'; Lines = @(
                '$commonFlags = ''/O2 /DSWISSEPH_HAS_CROSSING=1 /DSWISSEPH_HAS_HOUSES_EX2=1 /MD'''); Expect = 0; ExpectedSiteCount = 1 }
            @{ Name = 'spaces between -D and the name'; Lines = @(
                'gcc -D SWISSEPH_HAS_CROSSING=1 -D SWISSEPH_HAS_HOUSES_EX2=1 -o sedump sedump.c'); Expect = 0; ExpectedSiteCount = 1 }
            @{ Name = 'prose naming sedump.c and gcc is not a definition and must not be flagged'; Lines = @(
                'echo "::error::Tools/CReference/sedump.c now calls sincos() -- can only be gcc''s own substitution"'
                'gcc -DSWISSEPH_HAS_CROSSING=1 -DSWISSEPH_HAS_HOUSES_EX2=1 -o sedump sedump.c'); Expect = 0; ExpectedSiteCount = 1 }
            @{ Name = 'nothing defines any macro at all is a vacuous pass, not a pass'; Lines = @(
                '# nothing here defines anything'); Expect = 1; ExpectedSiteCount = 0 }
            # Bypass (d): a stale comment claiming both macros, with no real compile line anywhere.
            # Before comment lines were excluded from Get-MacroBearingLines, this fixture's single
            # comment line was itself counted as "a site defining both macros", so Test-Parity
            # reported zero problems -- exactly "deleting every real -D and leaving one stale
            # comment keeps it green". With comments excluded, this fixture has zero real sites, so
            # it falls into the same "no line anywhere defines a macro" refusal as the case above.
            @{ Name = 'bypass (d): a stale comment mentioning both macros, no real invocation anywhere'; Lines = @(
                '# stale: gcc -DSWISSEPH_HAS_CROSSING=1 -DSWISSEPH_HAS_HOUSES_EX2=1 -o sedump sedump.c'); Expect = 1; ExpectedSiteCount = 0 }
            # Bypass (b): one real, fully-defined site plus one compile invocation that dropped
            # every guard macro. The second line defines nothing at all, so Get-MacroBearingLines'
            # -D scan cannot see it as a site to begin with -- there is no "missing macro" for the
            # per-site loop to complain about, because the loop never visits a line that isn't a
            # site. Only comparing the found count (1) against how many 2.10.03 compile sites this
            # fixture is DECLARED to have (2, passed as ExpectedSiteCount) catches it.
            @{ Name = 'bypass (b): a compile site dropping every guard macro is invisible to the -D scan alone'; Lines = @(
                'gcc -O2 -DSWISSEPH_HAS_CROSSING=1 -DSWISSEPH_HAS_HOUSES_EX2=1 -o a sedump.c'
                'gcc -O2 -o b sedump.c'); Expect = 1; ExpectedSiteCount = 2 }
        )

        $failed = 0
        foreach ($case in $cases) {
            $wf = Join-Path $lab 'fake-workflow.yml'
            $case.Lines | Set-Content -LiteralPath $wf -Encoding utf8
            $result = Test-Parity -SedumpPath $fakeSedump -ScanFiles @($wf) -Build208File $build208 -ExpectedSiteCount $case.ExpectedSiteCount
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
        $r = Test-Parity -SedumpPath $threeMacro -ScanFiles @($wf) -Build208File $build208 -ExpectedSiteCount 1
        if ($r.Problems.Count -eq 0) {
            Write-Host "  SELFTEST FAIL: a macro sedump.c tests but no compile line defines was not caught" -ForegroundColor Red
            $failed++
        } else {
            Write-Host "  ok: a newly added guard with no compile site updated is caught" -ForegroundColor DarkGray
        }

        # Bypass (c): #elif, and more than one defined(...) on the same #if/#elif line. An earlier
        # version's file-wide, single-capture-per-line pattern anchored on "#if" alone found only
        # the FIRST defined() on a line and never matched "#elif" at all -- a guard added in either
        # shape was silently exempt from every check below it, since Get-RequiredMacros never even
        # knew it existed.
        $elifSedump = Join-Path $lab 'sedump-elif.c'
        @(
            '#if defined(SWISSEPH_HAS_CROSSING) || defined(SWISSEPH_HAS_HOUSES_EX2)'
            '#elif defined(SWISSEPH_HAS_CALC_PCTR)'
            '#endif'
        ) | Set-Content -LiteralPath $elifSedump -Encoding utf8
        $elifRequired = Get-RequiredMacros -SedumpPath $elifSedump
        $expectedElif = @('SWISSEPH_HAS_CALC_PCTR', 'SWISSEPH_HAS_CROSSING', 'SWISSEPH_HAS_HOUSES_EX2')
        $elifDiff = @(Compare-Object $expectedElif $elifRequired -SyncWindow 0)
        if ($elifDiff.Count -eq 0) {
            Write-Host "  ok: bypass (c): #elif and multiple defined() on one #if line are both recognized" -ForegroundColor DarkGray
        } else {
            Write-Host "  SELFTEST FAIL: bypass (c): #elif/multi-defined() -- expected [$($expectedElif -join ', ')], got [$($elifRequired -join ', ')]" -ForegroundColor Red
            $failed++
        }

        # Bypass (a): *.yaml, not just *.yml. GitHub Actions accepts both extensions for a workflow
        # file; a scan that only globbed *.yml silently dropped an entire workflow's worth of
        # compile sites with no error at all -- the site COUNT this gate now asserts (see bypass
        # (b) above) would have caught the resulting undercount too, but this exercises the file
        # discovery itself directly.
        $wfLab = Join-Path $lab 'workflows'
        New-Item -ItemType Directory -Force -Path $wfLab | Out-Null
        'gcc -DSWISSEPH_HAS_CROSSING=1 -DSWISSEPH_HAS_HOUSES_EX2=1 -o a sedump.c' | Set-Content -LiteralPath (Join-Path $wfLab 'a.yml') -Encoding utf8
        'gcc -DSWISSEPH_HAS_CROSSING=1 -DSWISSEPH_HAS_HOUSES_EX2=1 -o b sedump.c' | Set-Content -LiteralPath (Join-Path $wfLab 'b.yaml') -Encoding utf8
        $found = @(Get-WorkflowScanFiles -WorkflowsDir $wfLab)
        if ($found.Count -eq 2 -and @($found | Where-Object { $_ -like '*.yaml' }).Count -eq 1) {
            Write-Host "  ok: bypass (a): both .yml and .yaml workflow files are scanned" -ForegroundColor DarkGray
        } else {
            Write-Host "  SELFTEST FAIL: bypass (a): expected 2 files (one .yml, one .yaml), got $($found.Count): $($found -join ', ')" -ForegroundColor Red
            $failed++
        }

        # MEDIUM 3: a compile site in .github/workflows/<subdir>/x.yml (a nested workflow) and in
        # .github/actions/*/action.yml (a real, supported GitHub Actions composite action) are both
        # invisible to a scan of .github/workflows alone -- neither path sits directly inside that
        # one directory. Passing the .github directory itself, recursively, is the fix; this
        # exercises exactly the two shapes the review named, against $githubLab (a fresh .github
        # tree, not reused from the bypass (a) fixture above so this case does not depend on it).
        $githubLab = Join-Path $lab 'dot-github'
        $nestedWorkflowDir = Join-Path $githubLab 'workflows/nested'
        $compositeActionDir = Join-Path $githubLab 'actions/build-sedump'
        New-Item -ItemType Directory -Force -Path $nestedWorkflowDir | Out-Null
        New-Item -ItemType Directory -Force -Path $compositeActionDir | Out-Null
        'gcc -DSWISSEPH_HAS_CROSSING=1 -DSWISSEPH_HAS_HOUSES_EX2=1 -o a sedump.c' |
            Set-Content -LiteralPath (Join-Path $nestedWorkflowDir 'x.yml') -Encoding utf8
        'gcc -DSWISSEPH_HAS_CROSSING=1 -DSWISSEPH_HAS_HOUSES_EX2=1 -o b sedump.c' |
            Set-Content -LiteralPath (Join-Path $compositeActionDir 'action.yml') -Encoding utf8
        $githubFound = @(Get-WorkflowScanFiles -WorkflowsDir $githubLab)
        $foundNested = @($githubFound | Where-Object { $_ -like '*nested*x.yml' }).Count -eq 1
        $foundAction = @($githubFound | Where-Object { $_ -like '*actions*action.yml' }).Count -eq 1
        if ($foundNested -and $foundAction) {
            Write-Host "  ok: MEDIUM 3: a nested workflow (.github/workflows/<subdir>/x.yml) and a composite action (.github/actions/*/action.yml) are both scanned" -ForegroundColor DarkGray
        } else {
            Write-Host "  SELFTEST FAIL: MEDIUM 3: expected both a nested workflow and a composite action file, got $($githubFound.Count) file(s): $($githubFound -join ', ')" -ForegroundColor Red
            $failed++
        }

        # MEDIUM 3: scripts/*.sh -- a compile site in a shell script under scripts/ (none exist in
        # this repository today; nothing stops one being added).
        $shLab = Join-Path $lab 'scripts-sh'
        $shScriptsDir = Join-Path $shLab 'scripts'
        New-Item -ItemType Directory -Force -Path $shScriptsDir | Out-Null
        'gcc -DSWISSEPH_HAS_CROSSING=1 -DSWISSEPH_HAS_HOUSES_EX2=1 -o a sedump.c' |
            Set-Content -LiteralPath (Join-Path $shScriptsDir 'build.sh') -Encoding utf8
        $shFound = @(Get-ShellScriptScanFiles -RepoRoot $shLab)
        if ($shFound.Count -eq 1 -and $shFound[0] -like '*build.sh') {
            Write-Host "  ok: MEDIUM 3: scripts/*.sh is scanned" -ForegroundColor DarkGray
        } else {
            Write-Host "  SELFTEST FAIL: MEDIUM 3: scripts/*.sh -- expected 1 file (build.sh), got $($shFound.Count): $($shFound -join ', ')" -ForegroundColor Red
            $failed++
        }

        # MEDIUM 3: a Makefile -- and NOT one that happens to sit under external/ (vendored source
        # this gate does not control, excluded the same way scripts/verify-oracle.ps1's own
        # Get-PortSourceHash excludes bin/obj), which this second fixture proves is actually
        # excluded rather than merely untested.
        $makeLab = Join-Path $lab 'makefile-scan'
        New-Item -ItemType Directory -Force -Path $makeLab | Out-Null
        New-Item -ItemType Directory -Force -Path (Join-Path $makeLab 'external/some-vendored-lib') | Out-Null
        'gcc -DSWISSEPH_HAS_CROSSING=1 -DSWISSEPH_HAS_HOUSES_EX2=1 -o a sedump.c' |
            Set-Content -LiteralPath (Join-Path $makeLab 'Makefile') -Encoding utf8
        'gcc -DSWISSEPH_HAS_CROSSING=1 -DSWISSEPH_HAS_HOUSES_EX2=1 -o vendored sedump.c' |
            Set-Content -LiteralPath (Join-Path $makeLab 'external/some-vendored-lib/Makefile') -Encoding utf8
        $makeFound = @(Get-MakefileScanFiles -RepoRoot $makeLab)
        if ($makeFound.Count -eq 1 -and $makeFound[0] -notmatch '[\\/]external[\\/]') {
            Write-Host "  ok: MEDIUM 3: a root Makefile is scanned, one under external/ is excluded" -ForegroundColor DarkGray
        } else {
            Write-Host "  SELFTEST FAIL: MEDIUM 3: Makefile scan -- expected exactly 1 file (not under external/), got $($makeFound.Count): $($makeFound -join ', ')" -ForegroundColor Red
            $failed++
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

$result = Test-Parity -SedumpPath $sedump -ScanFiles $scan -Build208File $build208 -ExpectedSiteCount $expectedSiteCount

Write-Host "sedump.c guards: $($result.Required -join ', ')"
Write-Host "compile sites found: $($result.Sites.Count) (expected $expectedSiteCount)"

if ($result.Problems.Count -gt 0) {
    foreach ($p in $result.Problems) { Write-Host "FAIL: $p" -ForegroundColor Red }
    exit 1
}

Write-Host "PASS: every 2.10.03 compile site ($($result.Sites.Count) of them) defines all $($result.Required.Count) guard macro(s); the 2.08 build defines none." -ForegroundColor Green
exit 0
