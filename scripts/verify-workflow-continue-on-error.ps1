#Requires -Version 7.3
<#
.SYNOPSIS
    Asserts continue-on-error appears only at step level, and only on the (file, job) pairs this
    repository currently expects to carry it, across every workflow-shaped file under .github/.

.DESCRIPTION
    Backs .github/workflows/oracle.yml's header-flags-check job. That job used to be entirely
    inline PowerShell inside oracle.yml's own YAML, hardcoded to read exactly one file:
    `$path = '.github/workflows/oracle.yml'`. Its own comment claimed to treat "any job-level
    continue-on-error, on any job" as a failure, but a job-level continue-on-error planted in
    ci.yml, baseline.yml, conformance.yml, or any nested workflow or composite action this scan
    never looked at, went completely unseen -- the job still printed PASS. Demonstrated directly: a
    continue-on-error: true at job level on ci.yml's verify-freeze job neutralises the freeze gate,
    the freeze-log gate and three self-tests, and the single-file scan had nothing to say about it.

    Fixed by scanning every *.yml/*.yaml file under .github/, recursively (reusing
    scripts/lib/WorkflowScan.ps1's Get-WorkflowScanFiles -- the same helper
    scripts/verify-sedump-macro-parity.ps1 uses for the identical "workflow files, composite
    actions, files nested under a subdirectory" shape), and keying the expected step-level
    allow-list on "file:job" rather than a bare job name -- two jobs of the same conventional name
    in two different workflow files must not be conflated into a single entry that would silently
    also excuse the other one.

    Where a continue-on-error sits matters as much as which job it names: at step level it
    suppresses failure for one step, leaving every step before and after it in the same job a real
    gate. At job level it suppresses failure for the WHOLE job, silently swallowing every step in
    it -- the exact defect this gate exists to catch, both for oracle-dump's own now-removed job-
    level flag (see oracle.yml's own header) and for baseline.yml's verify-baseline-linux job,
    whose flag also used to sit at job level before being narrowed to the one step that is
    genuinely diagnostic (see that job's own comment).

    This gate validates against its own hardcoded expectation (Get-ExpectedStepLevelFlags), not by
    parsing any workflow file's prose comments -- the two are things a person keeps in sync by
    hand, deliberately, not something this script derives from the other. Update
    Get-ExpectedStepLevelFlags and the affected workflow file's own comment together when a job's
    continue-on-error changes.

.PARAMETER GithubDir
    Directory to scan, recursively, for *.yml/*.yaml files. Defaults to .github under the repo
    root.

.PARAMETER SelfTest
    Plants each bypass this gate exists to catch (a job-level flag in a file other than
    oracle.yml, a step-level flag on an unexpected (file, job) pair, a missing expected pair, and a
    job-name collision across two different files, which file:job keying must keep distinct) into
    a throwaway .github-shaped directory tree and asserts this gate refuses or accepts as
    appropriate. Touches nothing under the real .github/.
#>
[CmdletBinding()]
param(
    [string] $GithubDir = (Join-Path (Split-Path -Parent $PSScriptRoot) '.github'),
    [switch] $SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'lib/WorkflowScan.ps1')

# The (file, job) pairs this repository currently expects continue-on-error at STEP level, and
# nowhere else -- measured directly (`git grep -n '^\s*continue-on-error:' .github/workflows/*.yml`),
# not guessed. Update this list, and the named job's own comment in its workflow file, together and
# deliberately when a job's continue-on-error changes; this gate exists so the two cannot drift
# apart silently the way oracle.yml's own former header comment did (it kept naming
# macos-exactness/linux-exactness long after commit bc309e3 removed both jobs' flags, while the
# list itself -- then hardcoded inline in that same file -- had already been correctly narrowed to
# 'swetest-diff' alone).
#
#   workflows/oracle.yml:swetest-diff        -- the MSVC-toolchain-sensitive text comparison
#                                      against Tests/swetest/known-diff.tsv, which holds real rows
#                                      captured from one specific compiler; a future windows-latest
#                                      MSVC genuinely can move them without this port changing.
#   workflows/baseline.yml:verify-baseline-linux -- the cross-platform ULP-drift report
#                                      (`verify-baseline.ps1 -ReportOnly`), --report-only by design
#                                      and never meant to gate; see that job's own comment for what
#                                      sitting at job level used to silently absorb (its "Run gate
#                                      unit tests" step, which has nothing to do with cross-platform
#                                      drift).
#   workflows/baseline.yml:verify-baseline-macos -- macOS counterpart to verify-baseline-linux
#                                      above, same script, same --report-only design, same reason.
#   workflows/conformance.yml:conformance-macos -- macOS counterpart to the Windows conformance
#                                      job, added when macOS gained a leg on this instrument for the
#                                      first time; continue-on-error here (unlike macos-exactness in
#                                      oracle.yml, which shed its own initial flag) stays until this
#                                      combination has actually run clean in CI -- see that job's own
#                                      comment.
#
# Keyed on the path RELATIVE TO $GithubDir (below), not a bare filename -- MEDIUM 2's own fix, the
# fourth of the four demonstrated evasions. $label used to be `Split-Path -Leaf $Path`, so a second
# file sharing a leaf name with an expected one (e.g. a planted
# `.github/workflows/extra/oracle.yml` carrying its own `swetest-diff` job) collided with the real
# `.github/workflows/oracle.yml:swetest-diff` entry under the same bare key -- both a real
# step-level flag and a bypass step-level flag on an unrelated file merged into one entry, which
# `Sort-Object -Unique` (Test-ContinueOnErrorPlacement below) then silently absorbed, hiding the
# second site from the printed list entirely. A relative-path label keeps "workflows/oracle.yml"
# and "workflows/extra/oracle.yml" distinct.
function Get-ExpectedStepLevelFlags {
    return @(
        'workflows/oracle.yml:swetest-diff',
        'workflows/baseline.yml:verify-baseline-linux',
        'workflows/baseline.yml:verify-baseline-macos',
        'workflows/conformance.yml:conformance-macos'
    )
}

# Parses one workflow-shaped YAML file's line stream and returns every continue-on-error
# occurrence in it, attributed to (file, job, level). "jobs:" opens attribution; a two-space
# job-name line sets the current job; a continue-on-error line at an indent no deeper than the
# job's own body is job level, anything deeper is step level. Composite action files
# (.github/actions/*/action.yml) have no "jobs:" section at all -- GitHub Actions' composite-action
# schema is "runs: using: composite steps: ...", not "jobs:" -- so a continue-on-error line found
# there with no job ever attributed throws, the same refusal a stray flag before "jobs:" in an
# ordinary workflow file gets. continue-on-error is not a documented composite-action step key, so
# this repository does not expect to ever hit that path for real; it is a loud failure rather than
# a silent skip on principle, matching every other "cannot attribute this" case in this gate.
#
# $BaseDir: $label is $Path made relative to $BaseDir (forward-slashed), not a bare leaf name -- see
# Get-ExpectedStepLevelFlags's own comment above for why a leaf-only label collides across files.
#
# Matching is deliberately NOT limited to a bare, unquoted, line-leading key any more -- three of
# the four demonstrated evasions are valid YAML this pattern used to miss entirely:
#   `"continue-on-error": true`   -- double-quoted key
#   `'continue-on-error': true`   -- single-quoted key
#   `jobname: {runs-on: ..., continue-on-error: true, ...}` -- a flow mapping, key mid-line
# $keyPattern below matches the key bare or quoted (a backreference on the opening/closing quote
# character, so mismatched quotes like `"continue-on-error'` do not match), at the start of a line
# (after any leading whitespace) OR immediately following a flow-mapping opener/separator (`{` or
# `,`, with optional whitespace) so a key embedded mid-line inside a single-line flow mapping is
# caught too. This is a stricter grammar, not a real YAML parser: it does not track flow-mapping
# nesting depth independently of a line's own leading indentation, so a flow mapping whose keys are
# pretty-printed across multiple lines at inconsistent indentation could still evade it -- not a
# shape any file in this repository uses today, and not one of the four demonstrated evasions.
#
# The Job/Step decision for a mid-line match still comes from the LINE's own leading indentation
# (matching the original, bare-key-only decision exactly), not the match's own column: a flow
# mapping opened on the job's own line (`  jobname: {..., continue-on-error: true, ...}`) sits at
# the job's own indent, so it reads as Job level; a flow mapping opened deeper (a step written in
# flow style) sits deeper and reads as Step level -- both without needing bracket-depth tracking.
#
# A job-name line that opens a flow mapping on the SAME line (`  jobname: {...}`) still has to set
# $currentJob the way the bare `  jobname:` form already does, or the mid-line match on that same
# line throws "found ... before this scan could attribute it to a job" instead of being read as
# Job level for that job -- see the second job-name check below.
function Get-ContinueOnErrorAttribution {
    param([string] $Path, [string] $BaseDir)

    $label = [System.IO.Path]::GetRelativePath($BaseDir, $Path).Replace([char]92, [char]47)
    $lines = Get-Content -LiteralPath $Path
    $inJobsSection = $false
    $currentJob = $null
    $currentJobIndent = $null
    $results = [System.Collections.Generic.List[pscustomobject]]::new()

    # (?<q>['"]?) ... \k<q> -- the opening and closing quote character (or the absence of one) must
    # match, via a named backreference, so a stray `"continue-on-error'` cannot match either
    # alternative in $Path's byte stream. `(?:^\s*|[{,]\s*)` is the two attribution shapes: line
    # start (any leading whitespace, matching the original pattern's own `^(\s*)` capture) or right
    # after a flow-mapping opener/separator.
    $keyPattern = "(?:^\s*|[{,]\s*)(?<q>['`"]?)continue-on-error\k<q>\s*:\s*\S"

    foreach ($line in $lines) {
        if ($line -match '^jobs:\s*$') {
            $inJobsSection = $true
            continue
        }

        if ($inJobsSection -and $line -match '^  ([a-z][a-z0-9-]*):\s*$') {
            $currentJob = $Matches[1]
            $currentJobIndent = 2
            continue
        }

        # Flow-mapping job line: `  jobname: {...}` -- sets $currentJob like the bare form above,
        # but deliberately does NOT `continue`: the rest of this same line is exactly where the
        # flow-mapping evasion sits, and the coeMatches scan below still needs to see it.
        if ($inJobsSection -and $line -match '^  ([a-z][a-z0-9-]*):\s*\{') {
            $currentJob = $Matches[1]
            $currentJobIndent = 2
        }

        $coeMatches = [regex]::Matches($line, $keyPattern)
        if ($coeMatches.Count -eq 0) { continue }

        if (-not $currentJob) {
            throw "$Path`: found continue-on-error before this scan could attribute it to a job (before 'jobs:', or before any job-name line, or in a composite-action file with no 'jobs:' section at all) -- job-name/jobs-section detection needs updating, or continue-on-error does not belong here."
        }
        $indent = ([regex]::Match($line, '^(\s*)')).Groups[1].Length
        $jobBodyIndent = $currentJobIndent + 2
        $level = if ($indent -le $jobBodyIndent) { 'Job' } else { 'Step' }
        foreach ($m in $coeMatches) {
            $results.Add([pscustomobject]@{ File = $label; Job = $currentJob; Level = $level; Key = "$label`:$currentJob" })
        }
    }

    return , @($results.ToArray())
}

# The comparison itself, factored out so -SelfTest can drive it against synthetic file sets without
# touching disk beyond a throwaway lab directory.
function Test-ContinueOnErrorPlacement {
    param([string[]] $ScanFiles, [string[]] $ExpectedStepLevelFlags, [string] $BaseDir)

    $all = [System.Collections.Generic.List[pscustomobject]]::new()
    foreach ($f in $ScanFiles) {
        foreach ($r in (Get-ContinueOnErrorAttribution -Path $f -BaseDir $BaseDir)) { $all.Add($r) }
    }

    $jobLevel = @($all | Where-Object { $_.Level -eq 'Job' } | ForEach-Object { $_.Key } | Sort-Object -Unique)
    $stepLevel = @($all | Where-Object { $_.Level -eq 'Step' } | ForEach-Object { $_.Key } | Sort-Object -Unique)

    $extraJobLevel = @($jobLevel)
    $extraStepLevel = @($stepLevel | Where-Object { $_ -notin $ExpectedStepLevelFlags })
    $missingStepLevel = @($ExpectedStepLevelFlags | Where-Object { $_ -notin $stepLevel })

    $problems = [System.Collections.Generic.List[string]]::new()
    if ($extraJobLevel.Count -gt 0) { $problems.Add("At job level (never expected -- job level absorbs every step in the job, not just the one the exemption was written for): $($extraJobLevel -join ', ')") }
    if ($extraStepLevel.Count -gt 0) { $problems.Add("At step level but not in Get-ExpectedStepLevelFlags: $($extraStepLevel -join ', ')") }
    if ($missingStepLevel.Count -gt 0) { $problems.Add("Expected at step level but not found there: $($missingStepLevel -join ', ')") }

    return [pscustomobject]@{
        Problems  = @($problems.ToArray())
        StepLevel = $stepLevel
        JobLevel  = $jobLevel
    }
}

# ---------------------------------------------------------------------------------------------

if ($SelfTest) {
    $failures = 0
    $lab = Join-Path ([System.IO.Path]::GetTempPath()) ('verify-workflow-continue-on-error-selftest-' + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $lab | Out-Null

    function Write-LabFile {
        param([string] $RelPath, [string[]] $Lines)
        $full = Join-Path $lab $RelPath
        New-Item -ItemType Directory -Force -Path (Split-Path $full -Parent) | Out-Null
        Set-Content -LiteralPath $full -Value $Lines -Encoding utf8
        return $full
    }

    function Assert-True {
        param([string] $Case, [bool] $Condition, [string] $Detail = '')
        if ($Condition) { Write-Host "  ok: $Case" -ForegroundColor DarkGray }
        else { Write-Host "  SELFTEST FAIL: $Case`n      $Detail" -ForegroundColor Red; $script:failures++ }
    }

    try {
        Write-Host 'verify-workflow-continue-on-error self-test'
        Write-Host ''

        $expected = @('oracle.yml:swetest-diff', 'baseline.yml:verify-baseline-linux')

        # 1. The control: exactly the expected shape (two files, each with the one expected
        #    step-level flag) must pass clean.
        $okOracle = Write-LabFile 'oracle.yml' @(
            'jobs:'
            '  swetest-diff:'
            '    runs-on: windows-latest'
            '    steps:'
            '      - name: compare'
            '        continue-on-error: true'
            '        run: echo hi'
        )
        $okBaseline = Write-LabFile 'baseline.yml' @(
            'jobs:'
            '  verify-baseline-linux:'
            '    runs-on: ubuntu-latest'
            '    steps:'
            '      - name: report'
            '        continue-on-error: true'
            '        run: echo hi'
        )
        $r = Test-ContinueOnErrorPlacement -ScanFiles @($okOracle, $okBaseline) -ExpectedStepLevelFlags $expected -BaseDir $lab
        Assert-True 'the expected shape (both files, both step-level flags present) passes clean' ($r.Problems.Count -eq 0) ($r.Problems -join '; ')

        # 2. HIGH 2's own bypass, reproduced: a job-level continue-on-error in a file OTHER than
        #    oracle.yml. Before this gate scanned every file, this went completely unseen.
        $ciLab = Join-Path $lab 'bypass-other-file'
        New-Item -ItemType Directory -Force -Path $ciLab | Out-Null
        $badCi = Join-Path $ciLab 'ci.yml'
        Set-Content -LiteralPath $badCi -Encoding utf8 -Value @(
            'jobs:'
            '  verify-freeze:'
            '    runs-on: ubuntu-latest'
            '    continue-on-error: true'
            '    steps:'
            '      - name: freeze gate'
            '        run: echo hi'
        )
        $r = Test-ContinueOnErrorPlacement -ScanFiles @($badCi) -ExpectedStepLevelFlags $expected -BaseDir $ciLab
        Assert-True 'a job-level flag in a file other than oracle.yml is caught (HIGH 2''s own demonstrated bypass)' `
            ($r.Problems.Count -gt 0 -and ($r.Problems -join ' ') -match 'ci\.yml:verify-freeze') `
            ($r.Problems -join '; ')

        # 2b-2d. MEDIUM 2's own three quoted/flow-style evasions, all demonstrated at JOB level
        #        (the shape that neutralises a whole gate, not just one step) on the same
        #        'verify-freeze' job the reviewer's own plant used. All three are valid YAML GitHub
        #        Actions honours and all three used to exit 0 under the original bare,
        #        line-leading, unquoted `^(\s*)continue-on-error:\s*\S` pattern.
        $quotedDoubleLab = Join-Path $lab 'quoted-double-key'
        New-Item -ItemType Directory -Force -Path $quotedDoubleLab | Out-Null
        $quotedDoubleFile = Join-Path $quotedDoubleLab 'ci.yml'
        Set-Content -LiteralPath $quotedDoubleFile -Encoding utf8 -Value @(
            'jobs:'
            '  verify-freeze:'
            '    runs-on: ubuntu-latest'
            '    "continue-on-error": true'
            '    steps:'
            '      - name: freeze gate'
            '        run: echo hi'
        )
        $r = Test-ContinueOnErrorPlacement -ScanFiles @($quotedDoubleFile) -ExpectedStepLevelFlags $expected -BaseDir $quotedDoubleLab
        Assert-True 'a double-quoted key ("continue-on-error": true) at job level is caught' `
            ($r.Problems.Count -gt 0 -and ($r.Problems -join ' ') -match 'ci\.yml:verify-freeze') `
            ($r.Problems -join '; ')

        $quotedSingleLab = Join-Path $lab 'quoted-single-key'
        New-Item -ItemType Directory -Force -Path $quotedSingleLab | Out-Null
        $quotedSingleFile = Join-Path $quotedSingleLab 'ci.yml'
        Set-Content -LiteralPath $quotedSingleFile -Encoding utf8 -Value @(
            'jobs:'
            '  verify-freeze:'
            '    runs-on: ubuntu-latest'
            "    'continue-on-error': true"
            '    steps:'
            '      - name: freeze gate'
            '        run: echo hi'
        )
        $r = Test-ContinueOnErrorPlacement -ScanFiles @($quotedSingleFile) -ExpectedStepLevelFlags $expected -BaseDir $quotedSingleLab
        Assert-True "a single-quoted key ('continue-on-error': true) at job level is caught" `
            ($r.Problems.Count -gt 0 -and ($r.Problems -join ' ') -match 'ci\.yml:verify-freeze') `
            ($r.Problems -join '; ')

        $flowMappingLab = Join-Path $lab 'flow-mapping-job-level'
        New-Item -ItemType Directory -Force -Path $flowMappingLab | Out-Null
        $flowMappingFile = Join-Path $flowMappingLab 'ci.yml'
        Set-Content -LiteralPath $flowMappingFile -Encoding utf8 -Value @(
            'jobs:'
            '  verify-freeze: {runs-on: ubuntu-latest, continue-on-error: true, steps: [name: x, run: echo hi]}'
        )
        $r = Test-ContinueOnErrorPlacement -ScanFiles @($flowMappingFile) -ExpectedStepLevelFlags $expected -BaseDir $flowMappingLab
        Assert-True 'a job-level continue-on-error inside a single-line flow mapping is caught' `
            ($r.Problems.Count -gt 0 -and ($r.Problems -join ' ') -match 'ci\.yml:verify-freeze') `
            ($r.Problems -join '; ')

        # 3. A step-level flag on a (file, job) pair NOT in the expected list.
        $unexpectedLab = Join-Path $lab 'unexpected-step-level'
        New-Item -ItemType Directory -Force -Path $unexpectedLab | Out-Null
        $unexpectedFile = Join-Path $unexpectedLab 'oracle.yml'
        Set-Content -LiteralPath $unexpectedFile -Encoding utf8 -Value @(
            'jobs:'
            '  oracle-dump:'
            '    runs-on: windows-latest'
            '    steps:'
            '      - name: compare'
            '        continue-on-error: true'
            '        run: echo hi'
        )
        $r = Test-ContinueOnErrorPlacement -ScanFiles @($unexpectedFile) -ExpectedStepLevelFlags $expected -BaseDir $unexpectedLab
        Assert-True 'a step-level flag on an unexpected (file, job) pair is caught' `
            ($r.Problems.Count -gt 0 -and ($r.Problems -join ' ') -match 'oracle\.yml:oracle-dump') `
            ($r.Problems -join '; ')

        # 4. An expected pair that is missing entirely (e.g. someone dropped swetest-diff's flag
        #    without updating Get-ExpectedStepLevelFlags) must also fail -- this list is a two-way
        #    assertion, not a one-way allow-list.
        $r = Test-ContinueOnErrorPlacement -ScanFiles @($okOracle) -ExpectedStepLevelFlags $expected -BaseDir $lab
        Assert-True 'a missing expected pair (baseline.yml not scanned here) is caught' `
            ($r.Problems.Count -gt 0 -and ($r.Problems -join ' ') -match 'baseline\.yml:verify-baseline-linux') `
            ($r.Problems -join '; ')

        # 5. Job-name collision across two different files sharing the SAME LEAF NAME, in different
        #    directories -- MEDIUM 2's fourth demonstrated evasion, reproduced at the reviewer's own
        #    literal shape (`.github/workflows/extra/oracle.yml`). Before this fix, $label was
        #    `Split-Path -Leaf $Path`, so a second 'oracle.yml' elsewhere collided with the real
        #    '.github/workflows/oracle.yml:swetest-diff' expectation under the identical bare key --
        #    both sites merged into one Key, `Sort-Object -Unique` (inside
        #    Test-ContinueOnErrorPlacement) collapsed them to a single entry, and the second,
        #    unrelated file's flag was silently absorbed into the expected one instead of being
        #    reported as extra. A shared -BaseDir here is what makes the collision possible to
        #    reproduce at all: relative-path keying is exactly what tells "workflows/oracle.yml" and
        #    "workflows/extra/oracle.yml" apart.
        $collisionLab = Join-Path $lab 'leaf-name-collision'
        $realOracle = Write-LabFile 'leaf-name-collision/workflows/oracle.yml' @(
            'jobs:'
            '  swetest-diff:'
            '    runs-on: windows-latest'
            '    steps:'
            '      - name: compare'
            '        continue-on-error: true'
            '        run: echo hi'
        )
        $bypassOracle = Write-LabFile 'leaf-name-collision/workflows/extra/oracle.yml' @(
            'jobs:'
            '  swetest-diff:'
            '    runs-on: windows-latest'
            '    steps:'
            '      - name: compare'
            '        continue-on-error: true'
            '        run: echo hi'
        )
        $collisionExpected = @('workflows/oracle.yml:swetest-diff', 'workflows/baseline.yml:verify-baseline-linux')
        $r = Test-ContinueOnErrorPlacement -ScanFiles @($realOracle, $bypassOracle) -ExpectedStepLevelFlags $collisionExpected -BaseDir $collisionLab
        Assert-True 'a same-leaf-name file in a different directory is a DISTINCT, unexpected entry, not silently merged into the real one (relative-path keying)' `
            ($r.Problems.Count -gt 0 -and ($r.Problems -join ' ') -match 'workflows/extra/oracle\.yml:swetest-diff') `
            ($r.Problems -join '; ')
        # The real site must still be recognized as satisfying its own expectation, not itself
        # flagged as extra merely for sharing a leaf name with the bypass -- proving the fix
        # distinguishes the two rather than merely making both look wrong.
        Assert-True 'the real, expected site is still recognized (not itself flagged, and not merged with the bypass)' `
            (($r.Problems -join ' ') -notmatch "workflows/oracle\.yml:swetest-diff'" -and ($r.StepLevel -contains 'workflows/oracle.yml:swetest-diff') -and ($r.StepLevel -contains 'workflows/extra/oracle.yml:swetest-diff')) `
            ($r.Problems -join '; ')

        # 6. A quoted 'true' and a `${{ expression }}` both suppress a step's failure exactly like
        #    the bare word does -- the matching pattern requires `:\s*\S`, not the literal word
        #    `true`.
        $exprFile = Write-LabFile 'expr-form.yml' @(
            'jobs:'
            '  swetest-diff:'
            '    runs-on: windows-latest'
            '    steps:'
            '      - name: compare'
            "        continue-on-error: `${{ always() }}"
            '        run: echo hi'
        )
        $attribution = Get-ContinueOnErrorAttribution -Path $exprFile -BaseDir $lab
        Assert-True 'a ${{ expression }} form of continue-on-error is recognized, not just the bare word true' `
            ($attribution.Count -eq 1 -and $attribution[0].Level -eq 'Step')

        # 7. GitHub Actions accepts both .yml and .yaml, and a composite action nested under
        #    .github/actions/*/action.yml must be discovered too -- exercised through
        #    Get-WorkflowScanFiles directly (shared with verify-sedump-macro-parity.ps1's own
        #    self-test of the same function).
        $discoverLab = Join-Path $lab 'discovery'
        $nestedDir = Join-Path $discoverLab 'workflows/nested'
        $actionsDir = Join-Path $discoverLab 'actions/build'
        New-Item -ItemType Directory -Force -Path $nestedDir | Out-Null
        New-Item -ItemType Directory -Force -Path $actionsDir | Out-Null
        'jobs:' | Set-Content -LiteralPath (Join-Path $nestedDir 'x.yaml') -Encoding utf8
        'runs:' | Set-Content -LiteralPath (Join-Path $actionsDir 'action.yml') -Encoding utf8
        $found = @(Get-WorkflowScanFiles -WorkflowsDir $discoverLab)
        Assert-True 'a nested .yaml workflow and a composite action.yml are both discovered' `
            ($found.Count -eq 2) "found: $($found -join ', ')"

        # 8. A flag before any job-name line is attributed to (before 'jobs:', or before any job
        #    exists yet) must throw rather than silently attribute it to the wrong job or drop it.
        $strayFile = Write-LabFile 'stray.yml' @(
            'on: push'
            'continue-on-error: true'
            'jobs:'
            '  a-job:'
            '    runs-on: ubuntu-latest'
        )
        $threw = $false
        try { Get-ContinueOnErrorAttribution -Path $strayFile -BaseDir $lab | Out-Null }
        catch { $threw = $true }
        Assert-True 'a continue-on-error line before any job can be attributed to throws' $threw

        Write-Host ''
        if ($failures -gt 0) {
            Write-Host "FAIL: $failures self-test case(s) did not behave as required." -ForegroundColor Red
            exit 1
        }
        Write-Host 'PASS: all self-test cases behaved as required.' -ForegroundColor Green
        exit 0
    }
    finally {
        Remove-Item -LiteralPath $lab -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# ---------------------------------------------------------------------------------------------

if (-not (Test-Path -LiteralPath $GithubDir -PathType Container)) {
    Write-Error "GithubDir '$GithubDir' does not exist or is not a directory."
    exit 1
}

$scanFiles = @(Get-WorkflowScanFiles -WorkflowsDir $GithubDir)
if ($scanFiles.Count -eq 0) {
    Write-Error "Found zero *.yml/*.yaml files under $GithubDir. A scan that found nothing to check is not a pass."
    exit 1
}

$expected = Get-ExpectedStepLevelFlags
$result = Test-ContinueOnErrorPlacement -ScanFiles $scanFiles -ExpectedStepLevelFlags $expected -BaseDir $GithubDir

Write-Host "Scanned $($scanFiles.Count) workflow-shaped file(s) under $GithubDir."
Write-Host "continue-on-error at step level: $($result.StepLevel -join ', ')"
Write-Host "continue-on-error at job level: $(if ($result.JobLevel.Count -gt 0) { $result.JobLevel -join ', ' } else { '(none)' })"

if ($result.Problems.Count -gt 0) {
    foreach ($p in $result.Problems) { Write-Host "FAIL: $p" -ForegroundColor Red }
    Write-Host "Update Get-ExpectedStepLevelFlags in this script, and the affected job's own comment in its workflow file, together and deliberately." -ForegroundColor Red
    exit 1
}

Write-Host "PASS: continue-on-error appears only at step level, and only on the expected (file, job) pairs." -ForegroundColor Green
exit 0
