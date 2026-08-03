#Requires -Version 7
<#
.SYNOPSIS
    Resolves the base commit that scripts/verify-baseline-log.ps1 should diff HEAD against.

.DESCRIPTION
    The baseline-log check asks "did this branch change a golden file without recording why".
    Answering it requires a base commit that bounds the change set. Getting that base wrong in
    the permissive direction silently disables the whole check, which is exactly what happened
    twice before this script existed:

      * The original resolver walked every ref under refs/remotes/origin/ and kept the most
        recent merge-base. On a push, origin/<the branch being built> is already at HEAD, so
        merge-base(HEAD, that ref) is HEAD, and HEAD is a descendant of every other candidate,
        so it always won. The resolved base was HEAD, the diff window was empty, and the check
        reported "nothing to check" and exited 0 on an unlogged golden change.

      * "Most recent merge-base across every branch" is the wrong quantity even with the
        branch's own ref excluded, because a later base means a smaller window. Any ref at or
        above the offending commit moves the base past it: a backup branch pushed before a
        risky rebase, a stacked branch, a second name for the same tip, a colleague's branch
        built on yours. It is maximally permissive by construction.

    The question is not "what does this branch share with anything" but "what does it add
    relative to the integration branch". That is a single, specific merge-base, and nothing the
    pusher controls can move it later.

    Rules, in order:

      pull_request  Use the event's base SHA. GitHub computes it, it is always reachable, and
                    it is already the correct answer. Nothing here improves on it.

      push          Prefer github.event.before when it is a real, reachable commit that is not
                    HEAD. A rebase or amend orphans it, so it is often unusable; fall back to
                    merge-base(HEAD, integration branch).

                    If that merge-base is HEAD, HEAD is an ancestor of, or equal to, the
                    integration branch. There is no fork point, because this push IS on the
                    integration branch. Fail closed. A direct force-push to the integration
                    branch has no pull_request leg backing it up, so this run is the only gate,
                    and guessing a base there is how an unlogged change lands unnoticed.

.PARAMETER EventName
    'pull_request' or 'push'.

.PARAMETER BeforeSha
    github.event.before. May be empty, all-zeros (first push of a branch), or an orphaned
    commit (rebase or amend followed by force-push).

.PARAMETER PrBaseSha
    github.event.pull_request.base.sha. Required for pull_request.

.PARAMETER IntegrationRef
    Ordered candidate integration refs. The first that exists is used. Defaults to the fork's
    integration branch then trunk.

.PARAMETER WidenPastCancellation
    Push only; ignored for pull_request. After resolving $before normally above, also resolve
    where $before itself forks from the integration branch, and prefer that fork point whenever
    it is older than $before.

    Why this exists: this repository's own workflows run with `cancel-in-progress: true` in a
    concurrency group keyed on the branch ref for a push with no open pull request. Push N's own
    CI run can be cancelled by push N+1 before it finishes. Push N+1's own github.event.before is
    push N's tip, so a before-based range alone only ever covers what push N+1 itself introduced
    -- push N's own commits, checked only by the run that got cancelled, are never covered by any
    run that actually completes. Reaching back to $before's own fork point recovers them.

    Computed from $before, not from HEAD: a wider-fork-point search rooted at HEAD would let a
    merge commit in *this* push (one that pulls the integration branch's current tip in) drag the
    fork point forward past $before -- past the very thing this is supposed to widen -- silently
    disabling the widening in exactly the push where the concurrency gap is most likely (multiple
    concurrent pushes to the same integration branch, one of them a merge). merge-base($before,
    ref) is always an ancestor of, or equal to, $before itself, so this only ever widens the
    range, never narrows past what $before alone already covered. A push landing directly on the
    integration branch (where $before already equals its own fork point, so there is nothing
    further back to reach) is correctly left unwidened rather than refused: unlike the "no usable
    before" fallback below, a valid $before already exists here, so there is no reason to fail
    closed.

.PARAMETER SelfTest
    Build throwaway repositories covering every rule above and assert the resolved base.
    Touches nothing outside a temporary directory.
#>
[CmdletBinding()]
param(
    [string] $EventName = 'push',
    [string] $BeforeSha,
    [string] $PrBaseSha,
    [string[]] $IntegrationRef = @('origin/release/2.10.03', 'origin/main'),
    [switch] $WidenPastCancellation,
    [switch] $SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ZeroSha = '0' * 40

function Test-Commit {
    param([string] $Ref)
    if ([string]::IsNullOrWhiteSpace($Ref)) { return $false }
    git cat-file -e "$Ref^{commit}" 2>$null
    return $LASTEXITCODE -eq 0
}

function Resolve-IntegrationForkPoint {
    # Returns the merge-base of $From with the first reachable ref in $IntegrationRef, or $null
    # if none of them exist in this clone or none share history with $From. Never throws --
    # unlike the "no usable before" fallback in Resolve-LogBase below, a caller of this function
    # already has a valid base to fall back to (the widening in Resolve-LogBase is optional), so
    # "could not find a wider base" is a normal, silent no-op here, not a failure.
    param([string] $From, [string[]] $IntegrationRef)
    foreach ($ref in $IntegrationRef) {
        if (-not (Test-Commit $ref)) { continue }
        $mb = (git merge-base $From $ref 2>$null)
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($mb)) { continue }
        return $mb.Trim()
    }
    return $null
}

function Resolve-LogBase {
    param(
        [string] $EventName,
        [string] $BeforeSha,
        [string] $PrBaseSha,
        [string[]] $IntegrationRef,
        [switch] $WidenPastCancellation
    )

    $head = (git rev-parse HEAD).Trim()

    if ($EventName -eq 'pull_request') {
        if (-not (Test-Commit $PrBaseSha)) {
            throw "pull_request base SHA '$PrBaseSha' is not a reachable commit. The workflow must check out enough history (fetch-depth: 0) for this check to diff against it."
        }
        return $PrBaseSha.Trim()
    }

    # Push. github.event.before is the branch's tip before this push. It is correct when it
    # exists, which is the ordinary fast-forward case.
    if ($BeforeSha -and $BeforeSha -ne $ZeroSha -and (Test-Commit $BeforeSha)) {
        $before = $BeforeSha.Trim()
        if ($before -ne $head) {
            if ($WidenPastCancellation) {
                $forkPoint = Resolve-IntegrationForkPoint -From $before -IntegrationRef $IntegrationRef
                if ($forkPoint -and $forkPoint -ne $before) {
                    return $forkPoint
                }
            }
            return $before
        }
        # before == HEAD means the push moved nothing. Nothing to diff, and returning HEAD
        # would be an empty window, so say so explicitly rather than resolving onward -- the
        # comment above always claimed this; the code silently returned HEAD without saying
        # anything until this line existed, which is not the same thing. [Console]::Error, not
        # Write-Warning or Write-Host: every consumer of this script captures its stdout directly
        # as the resolved base (`$base = pwsh scripts/resolve-log-base.ps1 ...`), and in this
        # non-interactive host both Write-Warning and Write-Host land on stdout too -- confirmed
        # by testing, not assumed -- which would silently corrupt $base into "WARNING: ...<sha>"
        # instead of leaving it a clean commit SHA. Only a direct write to the real OS-level
        # stderr handle stays out of that capture.
        [Console]::Error.WriteLine("resolve-log-base.ps1: github.event.before ('$before') equals HEAD: this push moved nothing, so there are no new commits for the caller to check. Returning HEAD, which yields an empty HEAD..HEAD diff window on purpose, not a bug -- a consumer reporting zero commits checked from this base is the expected, correct outcome here, not a silently-defeated gate.")
        return $head
    }

    # No usable before: first push of a branch, or a rebase/amend orphaned it.
    foreach ($ref in $IntegrationRef) {
        if (-not (Test-Commit $ref)) { continue }

        $mb = (git merge-base HEAD $ref 2>$null)
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($mb)) { continue }
        $mb = $mb.Trim()

        if ($mb -eq $head) {
            # HEAD is an ancestor of, or equal to, the integration branch: this push is on the
            # integration branch itself. There is no fork point to find.
            throw @"
Cannot determine a safe base for the baseline-log check.

github.event.before ('$BeforeSha') is not a reachable commit, and HEAD is on the integration
branch ($ref), so there is no fork point to fall back to. This is what a force-push or an
amended commit on the integration branch looks like.

Refusing to guess. A direct push here has no pull_request run backing it up, so this job is
the only thing checking that a golden-file change was recorded, and any base that is not the
real previous tip would either miss the change or demand an entry for unrelated history.

Push the change as a pull request, or if the force-push was intentional, re-run this job
against an explicit base.
"@
        }
        return $mb
    }

    throw "None of the integration refs ($($IntegrationRef -join ', ')) exist in this clone, and github.event.before ('$BeforeSha') is not reachable. The workflow must fetch an integration ref before this check."
}

# ---------------------------------------------------------------------------------------------

if (-not $SelfTest) {
    $base = Resolve-LogBase -EventName $EventName -BeforeSha $BeforeSha -PrBaseSha $PrBaseSha -IntegrationRef $IntegrationRef -WidenPastCancellation:$WidenPastCancellation
    Write-Output $base
    exit 0
}

# ---------------------------------------------------------------------------------------------
# Self-test. Nothing here covers the resolver otherwise, which is why two permissive defects
# shipped in a row. Each case builds a real repository rather than mocking git.

$failures = 0
$root = Join-Path ([System.IO.Path]::GetTempPath()) ("resolve-log-base-selftest-" + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null

function New-Lab {
    param([string] $Name)
    $dir = Join-Path $root $Name
    New-Item -ItemType Directory -Path (Join-Path $dir 'origin') -Force | Out-Null
    git init -q --bare (Join-Path $dir 'origin') | Out-Null
    git clone -q (Join-Path $dir 'origin') (Join-Path $dir 'work') 2>$null | Out-Null
    Push-Location (Join-Path $dir 'work')
    git config user.email 'selftest@example.invalid'
    git config user.name 'selftest'
    New-Item -ItemType Directory -Path 'Tests/baseline' -Force | Out-Null
    'seed' | Set-Content 'Tests/baseline/x.tsv'
    git add -A; git commit -q -m 'c1'
    git push -q origin HEAD:refs/heads/main 2>$null
    # The integration branch this repo actually uses.
    git push -q origin HEAD:refs/heads/release/2.10.03 2>$null
    git fetch -q origin '+refs/heads/*:refs/remotes/origin/*' 2>$null
    return (Join-Path $dir 'work')
}

function Assert-Base {
    param([string] $Case, [string] $Expected, [string] $Actual)
    if ($Expected -eq $Actual) {
        Write-Host ("  PASS  {0}" -f $Case)
    }
    else {
        Write-Host ("  FAIL  {0}`n          expected {1}`n          actual   {2}" -f $Case, $Expected, $Actual)
        $script:failures++
    }
}

function Assert-Throws {
    param([string] $Case, [scriptblock] $Action)
    try {
        & $Action | Out-Null
        Write-Host ("  FAIL  {0}`n          expected a refusal, got a resolved base" -f $Case)
        $script:failures++
    }
    catch {
        Write-Host ("  PASS  {0} (refused)" -f $Case)
    }
}

Write-Host 'resolve-log-base self-test'
Write-Host ''

# 1. The defect that shipped twice: a feature branch pushed to origin, where the branch's own
#    ref is already at HEAD. The base must be the fork point, never HEAD.
$lab = New-Lab 'feature-first-push'
git checkout -q -b feature
'changed' | Set-Content 'Tests/baseline/x.tsv'
git commit -q -am 'golden change'
git push -q origin feature 2>$null
git fetch -q origin '+refs/heads/*:refs/remotes/origin/*' 2>$null
$head = (git rev-parse HEAD).Trim()
$fork = (git merge-base HEAD origin/release/2.10.03).Trim()
$got = Resolve-LogBase -EventName 'push' -BeforeSha $ZeroSha -PrBaseSha '' -IntegrationRef @('origin/release/2.10.03', 'origin/main')
Assert-Base 'first push resolves to the fork point, not HEAD' $fork $got
if ($got -eq $head) { Write-Host '          (this is the exact bug: an empty diff window)' }
Pop-Location

# 2. A backup branch at the pre-rebase tip must not move the base. This defeated the
#    "exclude only the branch's own ref" fix.
$lab = New-Lab 'backup-branch'
git checkout -q -b feature
'changed' | Set-Content 'Tests/baseline/x.tsv'
git commit -q -am 'golden change'
git push -q origin feature 2>$null
git push -q origin "feature:refs/heads/backup/feature-before-rebase" 2>$null
git fetch -q origin '+refs/heads/*:refs/remotes/origin/*' 2>$null
$fork = (git merge-base HEAD origin/release/2.10.03).Trim()
$got = Resolve-LogBase -EventName 'push' -BeforeSha $ZeroSha -PrBaseSha '' -IntegrationRef @('origin/release/2.10.03', 'origin/main')
Assert-Base 'a backup branch at the same tip does not move the base' $fork $got
Pop-Location

# 3. Force-push on a feature branch: before is orphaned, so fall back to the fork point.
#    The re-clone matters. After `git commit --amend` the original object is still present
#    locally, so a test that stayed in the pushing clone would find it reachable and never
#    exercise the fallback at all. CI always works from a fresh clone, where it is genuinely
#    gone. Getting this wrong makes the test pass for the wrong reason.
$lab = New-Lab 'feature-force-push'
git checkout -q -b feature
'changed' | Set-Content 'Tests/baseline/x.tsv'
git commit -q -am 'golden change'
$orphan = (git rev-parse HEAD).Trim()
git commit -q --amend -m 'golden change, amended'
git push -q -f origin feature 2>$null
$originPath = (git remote get-url origin).Trim()
Pop-Location
$fresh = Join-Path $root 'feature-force-push-ci'
git clone -q --branch feature $originPath $fresh 2>$null
Push-Location $fresh
git fetch -q origin '+refs/heads/*:refs/remotes/origin/*' 2>$null
$fork = (git merge-base HEAD origin/release/2.10.03).Trim()
$got = Resolve-LogBase -EventName 'push' -BeforeSha $orphan -PrBaseSha '' -IntegrationRef @('origin/release/2.10.03', 'origin/main')
Assert-Base 'force-push falls back to the fork point' $fork $got
Pop-Location

# 4. Force-push to the integration branch itself: no fork point exists. Must refuse rather
#    than resolve to something permissive. This run is the only gate on a direct push.
$lab = New-Lab 'integration-force-push'
git checkout -q -B 'release/2.10.03' origin/release/2.10.03 2>$null
'changed' | Set-Content 'Tests/baseline/x.tsv'
git commit -q -am 'golden change on integration'
$orphan = (git rev-parse HEAD).Trim()
git commit -q --amend -m 'amended on integration'
git push -q -f origin 'release/2.10.03' 2>$null
$originPath = (git remote get-url origin).Trim()
Pop-Location
$fresh = Join-Path $root 'integration-force-push-ci'
git clone -q --branch 'release/2.10.03' $originPath $fresh 2>$null
Push-Location $fresh
git fetch -q origin '+refs/heads/*:refs/remotes/origin/*' 2>$null
Assert-Throws 'force-push to the integration branch refuses' {
    Resolve-LogBase -EventName 'push' -BeforeSha $orphan -PrBaseSha '' -IntegrationRef @('origin/release/2.10.03', 'origin/main')
}
Pop-Location

# 5. An ordinary fast-forward push keeps github.event.before.
$lab = New-Lab 'fast-forward'
git checkout -q -b feature
'one' | Set-Content 'Tests/baseline/x.tsv'
git commit -q -am 'first'
git push -q origin feature 2>$null
$before = (git rev-parse HEAD).Trim()
'two' | Set-Content 'Tests/baseline/x.tsv'
git commit -q -am 'second'
git push -q origin feature 2>$null
git fetch -q origin '+refs/heads/*:refs/remotes/origin/*' 2>$null
$got = Resolve-LogBase -EventName 'push' -BeforeSha $before -PrBaseSha '' -IntegrationRef @('origin/release/2.10.03', 'origin/main')
Assert-Base 'fast-forward push uses github.event.before' $before $got
Pop-Location

# 6. pull_request uses the event's base SHA unchanged.
$lab = New-Lab 'pull-request'
git checkout -q -b feature
$prBase = (git rev-parse origin/release/2.10.03).Trim()
'changed' | Set-Content 'Tests/baseline/x.tsv'
git commit -q -am 'golden change'
$got = Resolve-LogBase -EventName 'pull_request' -BeforeSha '' -PrBaseSha $prBase -IntegrationRef @('origin/release/2.10.03', 'origin/main')
Assert-Base 'pull_request uses the event base SHA' $prBase $got
Pop-Location

# 7. An unreachable pull_request base must refuse, not silently fall through.
$lab = New-Lab 'pr-bad-base'
git checkout -q -b feature
'changed' | Set-Content 'Tests/baseline/x.tsv'
git commit -q -am 'golden change'
Assert-Throws 'unreachable pull_request base refuses' {
    Resolve-LogBase -EventName 'pull_request' -BeforeSha '' -PrBaseSha ('d' * 40) -IntegrationRef @('origin/release/2.10.03', 'origin/main')
}
Pop-Location

# 8. before == HEAD (github.event.before equals the current tip -- a no-op push, e.g. a force-push
#    that lands on the exact same commit) resolves to HEAD, an intentionally empty diff window,
#    not a bug -- and says so explicitly rather than silently, on the real OS-level stderr handle
#    (never Write-Warning or Write-Host: both land on stdout in a non-interactive host and would
#    corrupt every real consumer's `$base = pwsh scripts/resolve-log-base.ps1 ...` capture).
$lab = New-Lab 'noop-push'
$head = (git rev-parse HEAD).Trim()
$originalConsoleError = [Console]::Error
$capturedStderr = New-Object System.IO.StringWriter
[Console]::SetError($capturedStderr)
try {
    $got = Resolve-LogBase -EventName 'push' -BeforeSha $head -PrBaseSha '' -IntegrationRef @('origin/release/2.10.03', 'origin/main')
}
finally {
    [Console]::SetError($originalConsoleError)
}
Assert-Base 'before == HEAD (no-op push) resolves to HEAD, not a thrown refusal' $head $got
if ($capturedStderr.ToString() -match 'equals HEAD') {
    Write-Host '  PASS  before == HEAD says so explicitly, on stderr, instead of resolving silently'
}
else {
    Write-Host ("  FAIL  before == HEAD says so explicitly, on stderr, instead of resolving silently`n" +
        "          expected stderr to mention 'equals HEAD', got: [$($capturedStderr.ToString())]")
    $script:failures++
}
Pop-Location

# 9. -WidenPastCancellation recovers a cancelled push's own commits: push N's commits (between
#    the fork point and push N's own tip) must still be covered when push N+1's github.event.before
#    is push N's tip -- which alone only bounds push N+1's own new commits, not push N's.
$lab = New-Lab 'widen-recovers-cancelled-push'
git checkout -q -b feature
'push-n' | Set-Content 'Tests/baseline/x.tsv'
git commit -q -am 'push N (would have been cancelled)'
git push -q origin feature 2>$null
$beforeForPushNPlus1 = (git rev-parse HEAD).Trim()
'push-n-plus-1' | Set-Content 'Tests/baseline/x.tsv'
git commit -q -am 'push N+1 (the surviving run)'
git push -q origin feature 2>$null
git fetch -q origin '+refs/heads/*:refs/remotes/origin/*' 2>$null
$fork = (git merge-base $beforeForPushNPlus1 origin/release/2.10.03).Trim()
$gotUnwidened = Resolve-LogBase -EventName 'push' -BeforeSha $beforeForPushNPlus1 -PrBaseSha '' -IntegrationRef @('origin/release/2.10.03', 'origin/main')
Assert-Base 'without -WidenPastCancellation, still just github.event.before (unchanged default)' $beforeForPushNPlus1 $gotUnwidened
$gotWidened = Resolve-LogBase -EventName 'push' -BeforeSha $beforeForPushNPlus1 -PrBaseSha '' -IntegrationRef @('origin/release/2.10.03', 'origin/main') -WidenPastCancellation
Assert-Base '-WidenPastCancellation reaches back to the fork point, covering push N too' $fork $gotWidened
Pop-Location

# 10. -WidenPastCancellation on a push landing directly on the integration branch: $before already
#     equals its own fork point (there is no earlier history to reach), so this must return $before
#     unchanged rather than throw -- unlike the "no usable before" fallback's refusal, a valid
#     $before already exists here.
$lab = New-Lab 'widen-on-integration-branch-noop'
git checkout -q -B 'release/2.10.03' origin/release/2.10.03 2>$null
$beforeOnIntegration = (git rev-parse HEAD).Trim()
'on-integration' | Set-Content 'Tests/baseline/x.tsv'
git commit -q -am 'direct push to the integration branch'
git push -q origin 'release/2.10.03' 2>$null
git fetch -q origin '+refs/heads/*:refs/remotes/origin/*' 2>$null
$got = Resolve-LogBase -EventName 'push' -BeforeSha $beforeOnIntegration -PrBaseSha '' -IntegrationRef @('origin/release/2.10.03', 'origin/main') -WidenPastCancellation
Assert-Base 'widening on a push to the integration branch itself is a no-op, not a refusal' $beforeOnIntegration $got
Pop-Location

# 11. -WidenPastCancellation survives a push that merges the integration branch in -- the second
#     scenario a HEAD-rooted fork-point search would get wrong: after the merge, HEAD's own
#     merge-base with the integration branch is the integration branch's *current* tip, which can
#     be newer than $before if anyone else advanced the integration branch meanwhile, silently
#     disabling the widening in exactly the push where the concurrency gap is most likely. Rooting
#     the search at $before instead of HEAD must stay unaffected by this push's own merge commit.
$lab = New-Lab 'widen-survives-merge-commit'
git checkout -q -b feature
'feature-work' | Set-Content 'Tests/baseline/x.tsv'
git commit -q -am 'feature branch diverges'
git push -q origin feature 2>$null
$beforeMerge = (git rev-parse HEAD).Trim()
$originalFork = (git merge-base $beforeMerge origin/release/2.10.03).Trim()
# Someone else advances the integration branch after the feature branch forked. `git add -A`,
# not just `commit -am`: -am only stages *modified tracked* files, and this is a brand new,
# still-untracked file -- without the explicit add, this commit silently had nothing staged and
# the integration branch never actually advanced, which made this whole scenario a no-op.
git checkout -q -B 'release/2.10.03' origin/release/2.10.03 2>$null
'someone-elses-work' | Set-Content 'Tests/baseline/other.tsv'
git add -A
git commit -q -m 'unrelated commit landing on the integration branch'
git push -q origin 'release/2.10.03' 2>$null
git fetch -q origin '+refs/heads/*:refs/remotes/origin/*' 2>$null
# The feature branch's next push merges the now-advanced integration branch in. --no-ff: the
# feature branch is otherwise a strict ancestor of the now-advanced integration branch, so a
# plain merge would fast-forward instead of creating the real merge commit this scenario needs.
git checkout -q feature
git merge -q --no-ff origin/release/2.10.03 -m 'merge the integration branch in' 2>$null
git push -q origin feature 2>$null
git fetch -q origin '+refs/heads/*:refs/remotes/origin/*' 2>$null
$headRootedForkPoint = (git merge-base HEAD origin/release/2.10.03).Trim()
if ($headRootedForkPoint -eq $originalFork) {
    Write-Host '  SKIP  widen-survives-merge-commit (this git merged fast-forward with nothing to distinguish HEAD-rooted from before-rooted; the scenario needs a real merge commit)'
}
else {
    $got = Resolve-LogBase -EventName 'push' -BeforeSha $beforeMerge -PrBaseSha '' -IntegrationRef @('origin/release/2.10.03', 'origin/main') -WidenPastCancellation
    Assert-Base 'widening rooted at $before is unaffected by this push''s own merge of the integration branch' $originalFork $got
}
Pop-Location

Write-Host ''
Set-Location ([System.IO.Path]::GetTempPath())
Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue

if ($failures -gt 0) {
    Write-Host "FAIL: $failures self-test case(s) failed."
    exit 1
}
Write-Host 'PASS: all resolve-log-base self-test cases passed.'
exit 0
