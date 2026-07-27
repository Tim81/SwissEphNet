#Requires -Version 7
<#
.SYNOPSIS
    Builds BaselineVerify in Release and runs it against the committed baseline.

.DESCRIPTION
    Verification always uses local mode: BaselineVerify's ProjectReference to
    SwissEphNet resolves the in-repo library, never the reference NuGet package
    (the UseReferencePackage property is never passed here). Comparisons must run
    -c Release -- see Tools/BaselineGen/README.md for why Debug is not equivalent.

    Exit code is 0 only if every area passes (exact match or within the 1e-13
    relative tolerance) after applying Tools/BaselineVerify/waivers.tsv.
#>

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'Tools\BaselineVerify\BaselineVerify.csproj'

dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet run --project $project -c Release --no-build
exit $LASTEXITCODE
