#Requires -Version 7
<#
.SYNOPSIS
    Regenerates Tests/baseline/ from BaselineGen in reference mode.

.DESCRIPTION
    Only needed when the reference package version changes -- not a step in normal
    development. Builds BaselineGen against the SwissEphNet 2.8.0.2 NuGet package,
    generates twice into separate temp directories to confirm the run is
    reproducible, then copies the result into Tests/baseline/ for you to review
    and commit.

    To verify current in-repo code against the (already committed) baseline
    instead, use scripts/verify-baseline.ps1 -- that is the everyday check.
#>

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'Tools\BaselineGen\BaselineGen.csproj'
$baselineDir = Join-Path $repoRoot 'Tests\baseline'

dotnet build $project -c Release -p:UseReferencePackage=true
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$runA = Join-Path ([System.IO.Path]::GetTempPath()) ("baseline-gen-a-" + [Guid]::NewGuid())
$runB = Join-Path ([System.IO.Path]::GetTempPath()) ("baseline-gen-b-" + [Guid]::NewGuid())

Write-Host "Generating run A: $runA"
dotnet run --project $project -c Release -p:UseReferencePackage=true --no-build -- $runA
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Generating run B: $runB"
dotnet run --project $project -c Release -p:UseReferencePackage=true --no-build -- $runB
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

# The sidecar filename is derived from EnvInfo.ReferenceVersion (baseline-<version>.env.txt),
# not hardcoded here -- a version bump must not leave a stale-named sidecar sitting next
# to freshly regenerated TSVs. Delete and copy by pattern, not by a literal name, for both
# file kinds.
try {
    Write-Host "Copying run A into $baselineDir"
    New-Item -ItemType Directory -Force -Path $baselineDir | Out-Null
    Get-ChildItem $baselineDir -Filter 'baseline-*.tsv' -ErrorAction SilentlyContinue | Remove-Item -Force
    Get-ChildItem $baselineDir -Filter 'baseline-*.env.txt' -ErrorAction SilentlyContinue | Remove-Item -Force
    Copy-Item (Join-Path $runA 'baseline-*.tsv') $baselineDir
    Copy-Item (Join-Path $runA 'baseline-*.env.txt') $baselineDir
}
finally {
    Remove-Item $runA, $runB -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Done. Review the diff in $baselineDir (git diff --stat Tests/baseline) and commit if it looks right."
