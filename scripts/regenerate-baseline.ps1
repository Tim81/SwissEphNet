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
    "When local-mode regeneration is legitimate," before using this.

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
#>

param(
    [switch]$FromLocal,
    [string]$DeviationNote
)

$ErrorActionPreference = 'Stop'

if ($FromLocal -and [string]::IsNullOrWhiteSpace($DeviationNote)) {
    Write-Error "-FromLocal requires -DeviationNote describing the deliberate, reviewed behavior change (see Tools/BaselineGen/README.md, 'When local-mode regeneration is legitimate')."
    exit 1
}
if (-not $FromLocal -and $DeviationNote) {
    Write-Error "-DeviationNote only applies together with -FromLocal."
    exit 1
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'Tools\BaselineGen\BaselineGen.csproj'
$baselineDir = Join-Path $repoRoot 'Tests\baseline'

$modeArgs = @()
if ($FromLocal) {
    Write-Host "Mode: LOCAL (in-repo SwissEphNet project via ProjectReference)."
    Write-Host "This only updates Tests/baseline/*.tsv rows that a deliberate, reviewed local"
    Write-Host "behavior change actually touched. It never re-baselines the whole file, and it"
    Write-Host "never overwrites the committed sidecar's original reference identity."
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

try {
    Write-Host "Copying run A's *.tsv files into $baselineDir"
    New-Item -ItemType Directory -Force -Path $baselineDir | Out-Null
    Copy-Item (Join-Path $runA 'baseline-*.tsv') $baselineDir -Force

    if (-not $FromLocal) {
        # Reference mode: the sidecar is a full, honest description of this run
        # (a new reference version), so replace it wholesale -- by pattern, not a
        # literal name, since EnvInfo.SidecarFileName is derived from
        # ReferenceVersion and a version bump must not leave a stale-named sidecar
        # sitting next to freshly regenerated TSVs.
        Get-ChildItem $baselineDir -Filter 'baseline-*.env.txt' -ErrorAction SilentlyContinue | Remove-Item -Force
        Copy-Item (Join-Path $runA 'baseline-*.env.txt') $baselineDir
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

        $marker = '## Local regenerations'
        if ($existingContent -match [regex]::Escape($marker)) {
            $existingEntries = [regex]::Matches($existingContent, '(?m)^\d+\. ')
            $entryNumber = $existingEntries.Count + 1
            $newEntry = "$entryNumber. $commit ($date): $DeviationNote"
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
first understanding why it failed -- see Tools/BaselineGen/README.md, "When
local-mode regeneration is legitimate."
"@
            $newEntry = "1. $commit ($date): $DeviationNote"
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
