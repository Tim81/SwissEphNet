#Requires -Version 7.3
<#
.SYNOPSIS
    Shared file-discovery helper: every *.yml/*.yaml file under a directory tree, recursively.

.DESCRIPTION
    Originally scripts/verify-sedump-macro-parity.ps1's own Get-WorkflowScanFiles. Passing the
    .github directory itself (not .github/workflows) recursively is what makes one scan cover a
    workflow saved under a subdirectory (.github/workflows/<subdir>/x.yml) and a composite action
    (.github/actions/*/action.yml, a real, supported GitHub Actions file that also carries a .yml
    extension), not just top-level files directly inside .github/workflows. GitHub Actions accepts
    both .yml and .yaml for either kind of file; a scan that only globbed *.yml would silently drop
    an entire workflow's worth of coverage.

    Moved into scripts/lib/ so a second consumer -- scripts/verify-workflow-continue-on-error.ps1,
    which backs .github/workflows/oracle.yml's header-flags-check job -- can reuse the identical,
    already-self-tested logic instead of reimplementing (or hand-copying) it a second time and
    risking the two drifting apart. Dot-source this file rather than
    scripts/verify-sedump-macro-parity.ps1 itself: that script runs a full gate and calls `exit`
    unconditionally once past its own -SelfTest block, so dot-sourcing IT from another script would
    terminate the caller. This file defines one function and has no other side effect.
#>

Set-StrictMode -Version Latest

function Get-WorkflowScanFiles {
    param([string] $WorkflowsDir)
    return @(Get-ChildItem -LiteralPath $WorkflowsDir -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -in '.yml', '.yaml' } |
            ForEach-Object { $_.FullName })
}
