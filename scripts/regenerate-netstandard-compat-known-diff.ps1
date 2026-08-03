#Requires -Version 7
<#
.SYNOPSIS
    Regenerates Tests/netstandard-compat/known-diff-<fw>.tsv from a live run of
    Tools/NetStandardCompat/NetStandardCompatDump against the current build.

.DESCRIPTION
    The only supported way to change a known-diff-<fw>.tsv file scripts/verify-netstandard-compat.ps1
    gates on. Builds Tools/NetStandardCompat/NetStandardCompatDump.csproj (all four target
    frameworks), runs net10.0 as the reference and the selected other framework(s), computes the
    same totalOrder ULP comparison scripts/verify-netstandard-compat.ps1 itself uses (dot-sourced
    from that script -- see below, no separate copy of the comparison logic exists), and overwrites
    the selected known-diff-<fw>.tsv file(s) wholesale with whatever the current run produces.

    Because a full regenerate can add a row, worsen a recorded max ULP, or flip a
    categorical/numeric state -- exactly the changes that would otherwise make a red
    scripts/verify-netstandard-compat.ps1 run green by writing the failure into the list instead
    of fixing it -- this script always requires -Reason, and always appends the row-count delta to
    Tests/netstandard-compat/regenerations.log. There is no -PruneOnly mode here (unlike
    scripts/regenerate-oracle-known-diff.ps1's own): this instrument's divergence set moves only
    when the .NET Framework or .NET runtime itself changes underfoot, which is rare enough that the
    lighter-weight, review-everything full regenerate is judged the better default rather than a
    second mode maintained for a case that essentially never occurs in practice.

.PARAMETER Reason
    Required. A short description of why the known-diff list is changing (e.g. a .NET Framework or
    .NET SDK version bump that shifted which rows differ).

.PARAMETER Framework
    'Net8', 'Net462', 'Net48' or 'All' (default). Which known-diff-<fw>.tsv file(s) to regenerate.

.PARAMETER PR
    Optional. The pull request number this regeneration belongs to, e.g. "34" -- same convention as
    scripts/regenerate-oracle-known-diff.ps1's own -PR.

.PARAMETER SelfTest
    Asserts this script's own row-count-delta log-line formatting and -Reason requirement, against
    scratch files in a temporary directory. Never builds, never runs a dump tool, never touches
    Tests/netstandard-compat/.
#>

param(
    [string] $Reason,

    [ValidateSet('Net8', 'Net462', 'Net48', 'All')]
    [string] $Framework = 'All',

    [string] $PR,

    [switch] $SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$verifyScript = Join-Path $repoRoot 'scripts/verify-netstandard-compat.ps1'
$gridPath = Join-Path $repoRoot 'Tools/NetStandardCompat/grid-netstandard.tsv'
$referenceDumpPath = Join-Path $repoRoot 'Tools/NetStandardCompat/dump-net10.0.tsv'
$dumpProject = Join-Path $repoRoot 'Tools/NetStandardCompat/NetStandardCompatDump/NetStandardCompatDump.csproj'
$dumpBinDir = Join-Path $repoRoot 'Tools/NetStandardCompat/NetStandardCompatDump/bin/Release'
$logPath = Join-Path $repoRoot 'Tests/netstandard-compat/regenerations.log'

function Get-RowCount {
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return 0 }
    return [Math]::Max(0, (Get-Content -LiteralPath $Path | Measure-Object -Line).Lines - 1) # minus header
}

function Write-RegenerationLogLine {
    param([int] $Before, [int] $After, [string] $ReasonText, [string] $PrNumber)
    $prLabel = if ([string]::IsNullOrWhiteSpace($PrNumber)) { 'no PR yet' } else { "PR #$PrNumber" }
    $date = (Get-Date).ToString('yyyy-MM-dd')
    $delta = if ($After -eq $Before) { 'no change in row count' }
    elseif ($After -gt $Before) { "$($After - $Before) more row(s)" }
    else { "$($Before - $After) fewer rows" }
    $line = "$date $prLabel ($Before -> $After, $delta): $ReasonText"
    Add-Content -LiteralPath $logPath -Value $line
    return $line
}

if ($SelfTest) {
    $failures = 0
    $lab = Join-Path ([System.IO.Path]::GetTempPath()) ('regenerate-netstandard-compat-known-diff-selftest-' + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $lab | Out-Null
    try {
        function Assert-True {
            param([string] $Case, [bool] $Condition, [string] $Detail = '')
            if ($Condition) { Write-Host "  PASS  $Case" -ForegroundColor DarkGray }
            else { Write-Host "  FAIL  $Case`n          $Detail" -ForegroundColor Red; $script:failures++ }
        }

        Write-Host 'regenerate-netstandard-compat-known-diff self-test'
        Write-Host ''

        # -Reason is required outside -SelfTest -- the bypass this gate matters most for: without
        # it, a red scripts/verify-netstandard-compat.ps1 run could be made green by writing the
        # failure into a known-diff-<fw>.tsv file with no record of why. Run as a real child-process
        # invocation with neither -Reason nor -SelfTest, which reaches this script's own top-level
        # guard (below) and must refuse before it ever builds anything.
        $pwshExe = (Get-Process -Id $PID).Path
        $output = & $pwshExe -NoProfile -File $PSCommandPath -Framework Net8 *>&1
        $code = $LASTEXITCODE
        $text = (@($output) -join "`n")
        Assert-True '-Reason is required and refused before any build runs' `
            ($code -ne 0 -and $text -match '-Reason is required') "exit=$code output: $text"

        # Row-count-delta formatting: the three shapes a real run can log.
        $labLog = Join-Path $lab 'regenerations.log'
        New-Item -ItemType File -Path $labLog -Force | Out-Null
        $script:logPath = $labLog

        $lineGrew = Write-RegenerationLogLine -Before 10 -After 15 -ReasonText 'test' -PrNumber '99'
        Assert-True 'growth is logged as "N more row(s)"' ($lineGrew -match '10 -> 15, 5 more row\(s\)') "got: $lineGrew"

        $lineShrank = Write-RegenerationLogLine -Before 15 -After 10 -ReasonText 'test' -PrNumber '99'
        Assert-True 'shrinkage is logged as "N fewer rows"' ($lineShrank -match '15 -> 10, 5 fewer rows') "got: $lineShrank"

        $lineSame = Write-RegenerationLogLine -Before 10 -After 10 -ReasonText 'test' -PrNumber '99'
        Assert-True 'no change is logged as "no change in row count"' ($lineSame -match '10 -> 10, no change in row count') "got: $lineSame"

        $lineNoPr = Write-RegenerationLogLine -Before 0 -After 1 -ReasonText 'test' -PrNumber ''
        Assert-True 'an empty -PR logs the "no PR yet" placeholder' ($lineNoPr -match 'no PR yet') "got: $lineNoPr"

        $loggedLines = @(Get-Content -LiteralPath $labLog)
        Assert-True 'every call appended exactly one line, in order' ($loggedLines.Count -eq 4)

        Write-Host ''
        if ($failures -gt 0) {
            Write-Host "FAIL: $failures self-test case(s) did not behave as required." -ForegroundColor Red
            exit 1
        }
        Write-Host 'PASS: all regenerate-netstandard-compat-known-diff self-test cases behaved as required.' -ForegroundColor Green
        exit 0
    }
    finally {
        Remove-Item -LiteralPath $lab -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ([string]::IsNullOrWhiteSpace($Reason)) {
    Write-Error '-Reason is required.'
    exit 1
}

# Get-FieldDistance/Read-DumpTable/Compare-Dumps -- the same shared library
# scripts/verify-netstandard-compat.ps1 itself dot-sources, so this script's regenerated
# known-diff-<fw>.tsv files are computed by exactly the same comparison that gate later checks
# them against. See that library's own header for why it exists as a separate file rather than
# code duplicated into both scripts.
. (Join-Path $PSScriptRoot 'lib-netstandard-compat-compare.ps1')

function Get-FrameworkInfo {
    param([string] $Name)
    if ($Name -eq 'Net8') {
        return [pscustomobject]@{ Tfm = 'net8.0'; KnownDiffPath = Join-Path $repoRoot 'Tests/netstandard-compat/known-diff-net8.0.tsv' }
    }
    if ($Name -eq 'Net462') {
        return [pscustomobject]@{ Tfm = 'net462'; KnownDiffPath = Join-Path $repoRoot 'Tests/netstandard-compat/known-diff-net462.tsv' }
    }
    return [pscustomobject]@{ Tfm = 'net48'; KnownDiffPath = Join-Path $repoRoot 'Tests/netstandard-compat/known-diff-net48.tsv' }
}

Write-Host "Building $dumpProject (Release, all target frameworks)..."
$buildOutput = & dotnet build $dumpProject -c Release --nologo -v minimal 2>&1
if ($LASTEXITCODE -ne 0) {
    $buildOutput | Write-Host
    throw 'dotnet build Tools/NetStandardCompat/NetStandardCompatDump failed.'
}

$reference = Read-DumpTable -Path $referenceDumpPath

$frameworks = if ($Framework -eq 'All') { @('Net8', 'Net462', 'Net48') } else { @($Framework) }
foreach ($fw in $frameworks) {
    $info = Get-FrameworkInfo -Name $fw
    Write-Host "--- $fw ($($info.Tfm)) ---" -ForegroundColor Cyan

    $exePath = Join-Path $dumpBinDir "$($info.Tfm)/NetStandardCompatDump.exe"
    $tempDumpPath = [System.IO.Path]::GetTempFileName()
    try {
        & $exePath $gridPath $tempDumpPath 2>&1 | Write-Host
        if ($LASTEXITCODE -ne 0) { throw "NetStandardCompatDump ($($info.Tfm)) failed." }

        $other = Read-DumpTable -Path $tempDumpPath
        $current = Compare-Dumps -Reference $reference -Other $other

        $before = Get-RowCount -Path $info.KnownDiffPath

        $lines = [System.Collections.Generic.List[string]]::new()
        $lines.Add((@('case_id', 'category', 'max_ulp', 'reason') -join "`t"))
        # Ordinal sort, not Sort-Object's default culture-aware comparison: same reasoning as
        # scripts/regenerate-oracle-known-diff.ps1's own Read-KnownDiffTable comment on why its
        # case_id table is keyed ordinally -- a stable, culture-independent row order is what makes
        # the regenerated file's diff reviewable and byte-reproducible across machines.
        $sortedCaseIds = [string[]]$current.Keys
        [System.Array]::Sort($sortedCaseIds, [System.StringComparer]::Ordinal)
        foreach ($caseId in $sortedCaseIds) {
            $row = $current[$caseId]
            $category = if ($row.IsCategorical) { 'RETC-OR-SERR' } else { 'RUNTIME-MATH' }
            $maxUlpText = if ($row.IsCategorical) { 'categorical' } else { $row.MaxUlp.ToString() }
            $lines.Add((@($caseId, $category, $maxUlpText, $row.Reason) -join "`t"))
        }
        [System.IO.File]::WriteAllText($info.KnownDiffPath, (($lines -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))

        $after = Get-RowCount -Path $info.KnownDiffPath
        $logLine = Write-RegenerationLogLine -Before $before -After $after -ReasonText $Reason -PrNumber $PR
        Write-Host "Wrote $($info.KnownDiffPath): $before -> $after row(s)."
        Write-Host "Logged: $logLine"
    }
    finally {
        Remove-Item -LiteralPath $tempDumpPath -Force -ErrorAction SilentlyContinue
    }
    Write-Host ''
}

Write-Host 'Done. Review the diff (including Tests/netstandard-compat/regenerations.log) before committing.' -ForegroundColor Green
