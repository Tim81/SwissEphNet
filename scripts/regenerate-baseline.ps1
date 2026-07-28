#Requires -Version 7
<#
.SYNOPSIS
    Regenerates Tests/baseline/ from BaselineGen, in reference mode (default) or
    local mode (-FromLocal).

.DESCRIPTION
    Reference mode (default, no switch): builds BaselineGen against the published
    SwissEphNet NuGet package (Tools/BaselineMatrix/EnvInfo.cs's ReferenceVersion).
    This is the only mode that should ever run without a human consciously opting
    into the other one -- it is what "regenerate the baseline" means by default,
    and defaulting to it is what keeps anyone from rebaselining against local code
    by accident. Only needed when the reference package version itself changes.

    Local mode (-FromLocal): builds BaselineGen against the in-repo SwissEphNet
    project instead. This exists for exactly one legitimate reason: a deliberate,
    reviewed behavior change in local code whose effect the matrix can observe,
    and that the committed baseline needs to track from here on (e.g. fixing a
    real bug the matrix happens to exercise). It must never be used to make a
    failing scripts/verify-baseline.ps1 run go green by changing the baseline
    instead of understanding why it failed -- see Tools/BaselineGen/README.md,
    "Local mode -- when it is legitimate," before using this.

    Both modes generate twice into separate temp directories and diff them for
    byte-for-byte reproducibility before touching anything under Tests/baseline/.

    Local mode never overwrites the committed sidecar's original
    SwissEphModuleVersionId/SwissEphAssemblySha256 fields -- those record the
    reference package's identity, and BaselineVerify's assembly-identity check
    (Tools/BaselineVerify/Program.cs, CheckAssemblyIdentity) depends on them
    staying put: it fails the run if the *current* build's ModuleVersionId/SHA-256
    ever matches what is recorded there, since local mode should never accidentally
    compile to the same bytes as the reference package. Instead, local mode appends
    a dated, commit-stamped entry to that file's append-only "Local regenerations"
    log, using -DeviationNote as the description.

.PARAMETER FromLocal
    Generate from the in-repo SwissEphNet project (ProjectReference) instead of
    the published reference NuGet package. Only for a deliberate, reviewed
    behavior change -- see the description above and Tools/BaselineGen/README.md.

.PARAMETER DeviationNote
    Required with -FromLocal. A short description of what changed and why
    (what a reviewer needs to understand the deviation without re-deriving it),
    appended to the sidecar's "Local regenerations" log along with the current
    commit hash and UTC date. Not valid without -FromLocal.

.PARAMETER ExpectedScope
    Required, always (both modes). One or more case-id globs (Tools/BaselineVerify/Waivers.cs
    syntax: '*' matches within one pipe-delimited field, '**' crosses fields, e.g. "H|J|**" to
    scope an entire area) describing every case id this regeneration is expected to add,
    remove, or change.

    Accepts either real multiple values (-ExpectedScope 'H|**','C|**', or repeated -ExpectedScope
    from a caller that supports it) or a single comma-joined string (-ExpectedScope 'H|**,C|**')
    -- both are normalized to the same list below. This matters because invoking a script
    parameter from outside a live PowerShell session (a CI YAML step, a non-PowerShell shell)
    delivers whatever the caller's own argv splitting produced, and "one value per flag" is the
    only form every such caller can produce reliably; comma-splitting internally means neither
    form silently drops a glob.

    Before anything under Tests/baseline/ is touched, this script runs
    Tools/BaselineVerify's --diff-scope mode across every area, comparing the currently
    committed baseline (old) against the freshly generated run (new). If any added, removed,
    or changed case id in any area fails to match at least one -ExpectedScope glob, the
    regeneration is refused outright and the offending case ids are printed -- nothing is
    written. This exists because "diff it yourself and confirm every changed row is explained"
    (the instruction this script used to give and still gives below) cannot actually be
    followed by someone using this script's own console output as their guide: a corrupted
    constant can move thousands of rows by less than the comparison tolerance, failing only
    one area in scripts/verify-baseline.ps1 while -FromLocal's diff is silently much wider than
    that. -ExpectedScope turns "read the full diff yourself" into "read the one line this tool
    already proved true" -- see Tools/BaselineVerify/Program.cs's RunDiffScopeMode.
#>

param(
    [switch]$FromLocal,
    [string]$DeviationNote,
    [string[]]$ExpectedScope
)

$ErrorActionPreference = 'Stop'

if ($FromLocal -and [string]::IsNullOrWhiteSpace($DeviationNote)) {
    Write-Error "-FromLocal requires -DeviationNote describing the deliberate, reviewed behavior change (see Tools/BaselineGen/README.md, 'Local mode -- when it is legitimate')."
    exit 1
}
if (-not $FromLocal -and $DeviationNote) {
    Write-Error "-DeviationNote only applies together with -FromLocal."
    exit 1
}
if (-not $ExpectedScope -or $ExpectedScope.Count -eq 0) {
    Write-Error @"
-ExpectedScope is required (both modes): one or more case-id globs describing every case id
this regeneration is expected to add, remove, or change (Tools/BaselineVerify/Waivers.cs glob
syntax -- '*' is field-local, '**' crosses fields, e.g. -ExpectedScope 'H|J|**'). Nothing is
regenerated until every changed/added/removed case id in every area is proven to match at
least one of these globs. If you cannot state the scope of the change before regenerating,
you are not ready to regenerate -- go find out why the gate failed first (see
Tools/BaselineGen/README.md).
"@
    exit 1
}
# Manual validation (not [Parameter(Mandatory)]) deliberately: a missing mandatory parameter
# makes PowerShell prompt interactively, which would hang a CI run instead of failing it.

# Normalize: split every element on ',' too, so both '-ExpectedScope a,b' (one argv value, the
# only form some external callers can produce) and '-ExpectedScope a,b' (true multi-value
# array, when this script is invoked from within another PowerShell session) end up as the
# same flat list. No glob in this DSL ever legitimately contains a literal comma.
$ExpectedScope = @($ExpectedScope | ForEach-Object { $_ -split ',' } | Where-Object { $_.Length -gt 0 })
if ($ExpectedScope.Count -eq 0) {
    Write-Error "-ExpectedScope resolved to zero non-empty globs after normalization."
    exit 1
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'Tools\BaselineGen\BaselineGen.csproj'
$baselineDir = Join-Path $repoRoot 'Tests\baseline'

$modeArgs = @()
if ($FromLocal) {
    Write-Host "Mode: LOCAL (in-repo SwissEphNet project via ProjectReference)."
    Write-Host "This replaces every committed baseline-*.tsv file wholesale, exactly like"
    Write-Host "reference mode does -- there is no per-row logic anywhere in this script. The"
    Write-Host "diff ending up narrow (only the rows a real behavior change actually touches)"
    Write-Host "is a property of the change you made, not of this script. Before trusting the"
    Write-Host "result, diff it yourself (git diff Tests/baseline) and confirm every changed"
    Write-Host "row is explained by -DeviationNote -- if anything else moved, stop and find out"
    Write-Host "why before committing."
}
else {
    $modeArgs = @('-p:UseReferencePackage=true')
    Write-Host "Mode: REFERENCE (published SwissEphNet NuGet package -- see Tools/BaselineMatrix/EnvInfo.cs's ReferenceVersion)."
}

dotnet build $project -c Release @modeArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$runA = Join-Path ([System.IO.Path]::GetTempPath()) ("baseline-gen-a-" + [Guid]::NewGuid())
$runB = Join-Path ([System.IO.Path]::GetTempPath()) ("baseline-gen-b-" + [Guid]::NewGuid())

Write-Host "Generating run A: $runA"
dotnet run --project $project -c Release @modeArgs --no-build -- $runA
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Generating run B: $runB"
dotnet run --project $project -c Release @modeArgs --no-build -- $runB
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Comparing run A and run B for reproducibility..."
$namesA = (Get-ChildItem $runA -File | Sort-Object Name | Select-Object -ExpandProperty Name) -join ','
$namesB = (Get-ChildItem $runB -File | Sort-Object Name | Select-Object -ExpandProperty Name) -join ','
if ($namesA -ne $namesB) {
    Write-Error "Run A and run B produced a different set of files ($namesA) vs ($namesB)."
    exit 1
}

$mismatch = $false
foreach ($fileA in (Get-ChildItem $runA -File)) {
    $fileB = Join-Path $runB $fileA.Name
    $hashA = (Get-FileHash $fileA.FullName -Algorithm SHA256).Hash
    $hashB = (Get-FileHash $fileB -Algorithm SHA256).Hash
    if ($hashA -ne $hashB) {
        Write-Warning "$($fileA.Name) differs between run A and run B -- generation is not reproducible."
        $mismatch = $true
    }
}

if ($mismatch) {
    Write-Error "Reproducibility check failed. Not touching Tests/baseline/. See warnings above."
    exit 1
}

Write-Host "Reproducible: run A and run B are byte-identical."

# -ExpectedScope gate. Diffs the currently committed baseline ("old") against the freshly
# generated, already reproducibility-checked run ("new", $runA) across every area, by case id.
# Refuses -- prints offenders, writes nothing -- if any added/removed/changed case id in any
# area is not covered by at least one -ExpectedScope glob. See Tools/BaselineVerify/Program.cs,
# RunDiffScopeMode, and Tools/BaselineVerify/Waivers.cs (CompileGlob) for the glob rules this
# reuses rather than reimplementing.
Write-Host ""
Write-Host "Checking regeneration scope against -ExpectedScope ($($ExpectedScope -join ', '))..."
$verifyProject = Join-Path $repoRoot 'Tools\BaselineVerify\BaselineVerify.csproj'
dotnet build $verifyProject -c Release -f net10.0
if ($LASTEXITCODE -ne 0) {
    Remove-Item $runA, $runB -Recurse -Force -ErrorAction SilentlyContinue
    exit $LASTEXITCODE
}

$scopeArgs = @('run', '--project', $verifyProject, '-c', 'Release', '-f', 'net10.0', '--no-build', '--',
    '--diff-scope', $baselineDir, $runA, '--expected-scope') + $ExpectedScope
$scopeOutput = & dotnet @scopeArgs 2>&1
$scopeExitCode = $LASTEXITCODE
$scopeOutput | ForEach-Object { Write-Host $_ }

if ($scopeExitCode -ne 0) {
    Remove-Item $runA, $runB -Recurse -Force -ErrorAction SilentlyContinue
    Write-Error @"
Regeneration touched case id(s) outside -ExpectedScope ($($ExpectedScope -join ', ')) -- see the
OFFENDER lines above. Not touching Tests/baseline/. Either -ExpectedScope is too narrow for a
change you understand and intend (widen it to cover exactly what should have moved, no more),
or the code changed something you did not expect and have not yet explained -- in that case
stop here and find out why before regenerating anything.
"@
    exit 1
}

$changedAreaSummaries = @()
$newRowCountsByArea = @{}
foreach ($line in $scopeOutput) {
    if ($line -match '^CHANGED-AREA (.+)$') {
        $changedAreaSummaries += $Matches[1]
    }
    elseif ($line -match '^ROWCOUNT\s+(\S+)\t(\d+)$') {
        $newRowCountsByArea[$Matches[1]] = [int]$Matches[2]
    }
}
if ($changedAreaSummaries.Count -eq 0) {
    Write-Host "Scope check: SCOPE-OK, no area's case ids changed, were added, or were removed."
}
else {
    Write-Host "Scope check: SCOPE-OK, within -ExpectedScope --"
    $changedAreaSummaries | ForEach-Object { Write-Host "  $_" }
}

try {
    Write-Host "Copying run A's *.tsv files into $baselineDir"
    New-Item -ItemType Directory -Force -Path $baselineDir | Out-Null
    # Delete existing TSVs before copying the fresh set, not just Copy-Item -Force
    # over them: -Force only overwrites files that exist in the source, it does
    # not delete a destination file with no counterpart in $runA. Without this,
    # an area renamed or removed from BaselineMatrix's Areas.All would leave an
    # orphaned baseline-<old-name>.tsv behind that BaselineVerify would never
    # notice (it only ever looks for baseline-<name>.tsv for names it currently
    # knows about), silently keeping stale data around indefinitely.
    Get-ChildItem $baselineDir -Filter 'baseline-*.tsv' -ErrorAction SilentlyContinue | Remove-Item -Force
    Copy-Item (Join-Path $runA 'baseline-*.tsv') $baselineDir

    # Rewrite the row-count manifest (Tests/baseline/row-counts.tsv, checked by
    # Tools/BaselineVerify/RowCounts.cs) wholesale from this run's counts, same as the TSVs
    # themselves -- an area removed from Areas.All must not leave a stale entry behind, any
    # more than it should leave a stale baseline-<name>.tsv behind. Counts come from the
    # --diff-scope run above (ROWCOUNT lines), which counted by case id via Comparer.Index --
    # the same definition of "how many rows does this area have" the gate itself uses.
    $rowCountsPath = Join-Path $baselineDir 'row-counts.tsv'
    $rowCountsLines = @(
        '# Committed expected row count (case id count) per area, checked by BaselineVerify'
        '# (Tools/BaselineVerify/RowCounts.cs, Tools/BaselineVerify/Program.cs) so a baseline file'
        '# silently reduced -- to zero, or just narrowed -- cannot pass as PASS while'
        '# FAIL/ONLY-LOCAL/ONLY-REFERENCE all read zero.'
        '#'
        '# Rewritten by scripts/regenerate-baseline.ps1 in the same pass as the TSVs it describes,'
        '# gated behind -ExpectedScope. Do not edit by hand; a hand edit with no matching'
        '# baseline-*.tsv change (or vice versa) is exactly the drift this file exists to catch.'
        '#'
        '# Format: <area>\t<count>'
    )
    foreach ($area in ($newRowCountsByArea.Keys | Sort-Object)) {
        $rowCountsLines += "$area`t$($newRowCountsByArea[$area])"
    }
    Set-Content -Path $rowCountsPath -Value (($rowCountsLines -join "`n") + "`n") -NoNewline -Encoding utf8NoBOM
    Write-Host "Wrote $($newRowCountsByArea.Count) area row count(s) to $rowCountsPath."

    # Per-area changed/added/removed counts (from the -ExpectedScope check above) and each
    # freshly copied TSV's SHA-256, for the log entry below -- this is what lets a reviewer
    # read one line instead of the full diff (see -ExpectedScope's doc comment above).
    $areaHashLines = @()
    foreach ($tsv in (Get-ChildItem $baselineDir -Filter 'baseline-*.tsv' | Sort-Object Name)) {
        $hash = (Get-FileHash $tsv.FullName -Algorithm SHA256).Hash
        $areaHashLines += "$($tsv.Name)=$hash"
    }
    $scopeSummaryText = if ($changedAreaSummaries.Count -eq 0) { 'no area rows changed' } else { $changedAreaSummaries -join '; ' }

    if (-not $FromLocal) {
        # Reference mode: the sidecar's eight identity fields are a full,
        # honest description of this run (a new reference version), so those
        # get replaced wholesale -- by pattern, not a literal name, since
        # EnvInfo.SidecarFileName is derived from ReferenceVersion and a
        # version bump must not leave a stale-named sidecar sitting next to
        # freshly regenerated TSVs. But if the previous sidecar had
        # accumulated a "Local regenerations" history (entries from past
        # -FromLocal runs), that history is preserved, not silently deleted:
        # a version bump does not retroactively erase the fact that local code
        # once deviated from a prior reference for a deliberate, reviewed
        # reason, and a human looking at the new reference should still see
        # that history to decide whether each entry still applies against it.
        $oldSidecars = @(Get-ChildItem $baselineDir -Filter 'baseline-*.env.txt' -ErrorAction SilentlyContinue)
        $preservedLog = $null
        foreach ($old in $oldSidecars) {
            $oldContent = Get-Content -Raw -Path $old.FullName
            $idx = $oldContent.IndexOf('## Local regenerations')
            if ($idx -ge 0) {
                $preservedLog = $oldContent.Substring($idx).TrimEnd()
            }
        }
        $oldSidecars | Remove-Item -Force
        Copy-Item (Join-Path $runA 'baseline-*.env.txt') $baselineDir
        if ($preservedLog) {
            $newSidecar = @(Get-ChildItem $baselineDir -Filter 'baseline-*.env.txt')[0].FullName
            $newContent = (Get-Content -Raw -Path $newSidecar).TrimEnd()
            Set-Content -Path $newSidecar -Value ($newContent + "`n`n" + $preservedLog + "`n") -NoNewline -Encoding utf8NoBOM
            Write-Host "Preserved the previous sidecar's 'Local regenerations' history across this reference-mode regeneration."
        }
        Write-Host "Scope check ($($ExpectedScope -join ', ')): $scopeSummaryText"
        $areaHashLines | ForEach-Object { Write-Host "  SHA256 $_" }
        Write-Host "Reference-mode regeneration does not append a 'Local regenerations' entry automatically -- add one by hand describing the version bump (see Tools/BaselineGen/README.md), using the scope/SHA-256 lines above."
        Write-Host "Done. Review the diff in $baselineDir (git diff --stat Tests/baseline) and commit if it looks right."
    }
    else {
        # Local mode: never touch the committed sidecar's original reference
        # identity (SwissEphModuleVersionId/SwissEphAssemblySha256) -- append a
        # provenance entry to it instead. The freshly generated sidecar in $runA
        # describes *this* (local) build and is deliberately discarded; keeping it
        # would poison the assembly-identity check BaselineVerify relies on.
        $existingSidecars = @(Get-ChildItem $baselineDir -Filter 'baseline-*.env.txt' -ErrorAction SilentlyContinue)
        if ($existingSidecars.Count -ne 1) {
            Write-Error "Expected exactly one existing baseline-*.env.txt under $baselineDir to append provenance to (found $($existingSidecars.Count)). Local-mode regeneration requires a prior reference-mode baseline; run without -FromLocal first."
            exit 1
        }
        $sidecarPath = $existingSidecars[0].FullName
        $existingContent = Get-Content -Raw -Path $sidecarPath

        if ($existingContent -notmatch '(?m)^SwissEphModuleVersionId=' -or $existingContent -notmatch '(?m)^SwissEphAssemblySha256=') {
            Write-Error "$sidecarPath does not look like a Describe()-shaped sidecar (missing SwissEphModuleVersionId=/SwissEphAssemblySha256=). Refusing to append provenance to it."
            exit 1
        }

        $refVersionMatch = [regex]::Match($existingContent, '(?m)^SwissEphAssemblyVersion=(.+)$')
        $refVersion = if ($refVersionMatch.Success) { $refVersionMatch.Groups[1].Value.Trim() } else { '(unknown)' }

        $commit = (& git -C $repoRoot rev-parse --short HEAD 2>$null)
        if (-not $commit) { $commit = '(uncommitted)' } else { $commit = $commit.Trim() }
        $date = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')

        $scopeDetailLines = @("   Scope check ($($ExpectedScope -join ', ')): $scopeSummaryText")
        foreach ($h in $areaHashLines) { $scopeDetailLines += "   SHA256 $h" }
        $scopeDetailText = $scopeDetailLines -join "`n"

        $marker = '## Local regenerations'
        if ($existingContent -match [regex]::Escape($marker)) {
            $existingEntries = [regex]::Matches($existingContent, '(?m)^\d+\. ')
            $entryNumber = $existingEntries.Count + 1
            $newEntry = "$entryNumber. $commit ($date): $DeviationNote`n$scopeDetailText"
            $updatedContent = $existingContent.TrimEnd() + "`n$newEntry`n"
        }
        else {
            $header = @"


$marker

The eight fields above describe the original reference-mode generation run
(SwissEphNet $refVersion NuGet package) and are kept verbatim as a historical
record: BaselineVerify's assembly-identity check
(Tools/BaselineVerify/Program.cs, CheckAssemblyIdentity) compares the
currently-running build against exactly SwissEphModuleVersionId and
SwissEphAssemblySha256 above to confirm local mode never accidentally
compiles to the same bytes as the reference package. Do not edit those two
fields when regenerating from local code.

Since the fields above no longer describe every row in
Tests/baseline/baseline-*.tsv, this append-only log records each deliberate,
reviewed local-mode regeneration (scripts/regenerate-baseline.ps1 -FromLocal),
most recent last. Never add an entry here to make a failing gate pass without
first understanding why it failed -- see Tools/BaselineGen/README.md, "Local
mode -- when it is legitimate."
"@
            $newEntry = "1. $commit ($date): $DeviationNote`n$scopeDetailText"
            $updatedContent = $existingContent.TrimEnd() + $header.TrimEnd() + "`n`n$newEntry`n"
        }

        Set-Content -Path $sidecarPath -Value $updatedContent -NoNewline -Encoding utf8NoBOM
        Write-Host "Appended provenance entry to $sidecarPath."
        Write-Host "Done. Review the diff in $baselineDir (git diff Tests/baseline) and confirm only the rows the deviation note describes actually changed before committing."
    }
}
finally {
    Remove-Item $runA, $runB -Recurse -Force -ErrorAction SilentlyContinue
}
