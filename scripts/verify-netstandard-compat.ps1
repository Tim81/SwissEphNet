#Requires -Version 7
<#
.SYNOPSIS
    Measures whether SwissEphNet's netstandard2.0 asset's swe_calc agrees bit-for-bit across
    target frameworks, and gates on a committed record of where it does not.

.DESCRIPTION
    README.md's "V:2.10.3" section makes a claim in prose: running the same netstandard2.0 asset's
    swe_calc under net48 differs from net10.0 on some fraction of a swept grid, net8.0 and net10.0
    agree on all of it, and the cause is .NET Framework 4.8's own Math.Sin/Math.Tan being less
    accurate near pi. Before this script, nothing in the repository re-derived those numbers; they
    were measured once, ad hoc, and asserted. This is that instrument, built the same shape as
    scripts/verify-oracle.ps1's own bit-exact harness:

      Tools/NetStandardCompat/grid-netstandard.tsv       -- the committed input grid (34 bodies x
                                                             3 epochs, swe_calc only, see
                                                             Tools/NetStandardCompat/
                                                             gen-grid-netstandard.ps1's own header)
      Tools/NetStandardCompat/NetStandardCompatDump/     -- replays the grid, once per target
                                                             framework, and dumps the raw bit
                                                             pattern of every result
      Tools/NetStandardCompat/dump-net10.0.tsv           -- the committed reference dump, net10.0
      Tests/netstandard-compat/known-diff-<fw>.tsv       -- the committed divergence set for each
                                                             non-reference framework

    Four target frameworks the dump project builds: net10.0 (the reference), net8.0, net462, net48.
    This script always rebuilds and reruns net10.0 fresh and diffs it byte-for-byte against the
    committed reference dump before comparing anything else -- a stale reference is a hard failure,
    not a silently-trusted file (same reasoning as scripts/verify-oracle.ps1's own stale-dump
    check, simplified here since this instrument needs no external C toolchain or provenance
    sidecar: net10.0 is cheap enough to just rerun on every invocation).

    For each other selected framework, this script runs the dump tool, compares every row against
    the (now confirmed fresh) net10.0 reference using the same totalOrder ULP distance
    Tools/OracleVerify/UlpMath.cs uses (reimplemented here in PowerShell rather than shared, since
    this instrument has no C# comparison project of its own -- see Tools/NetStandardCompat/
    NetStandardCompatDump/Program.cs's own header for why a lean, PowerShell-only comparator was
    chosen over a second C# tool), and checks the result against that framework's known-diff list:
    a differing case_id absent from the list is a regression; a listed case_id that now matches
    outright must be pruned; a listed case_id whose current max ULP exceeds what is recorded, or
    whose categorical/numeric state has flipped, fails even though it is still "on the list" --
    the same three checks scripts/verify-oracle.ps1's own header describes for its known-diff
    lists, at the same finer-than-count granularity.

    This script does not regenerate anything. See
    scripts/regenerate-netstandard-compat-known-diff.ps1 for the only supported way to change a
    known-diff-<fw>.tsv, or Tools/NetStandardCompat/gen-grid-netstandard.ps1 for the grid.

.PARAMETER Framework
    'Net8', 'Net462', 'Net48' or 'All' (default). Which non-reference framework(s) to compare
    against the net10.0 reference.

.PARAMETER SelfTest
    Exercises this script's own comparison functions (Get-UlpDistance, Compare-Dumps,
    Read-KnownDiffTable, Get-KnownDiffFailures) against small, synthetic, in-memory fixtures --
    never builds, never runs a dump tool, never touches a tracked file. Plants each bypass this
    gate exists to catch (a new unlisted diff, a listed diff that grew past its recorded max ULP, a
    categorical/numeric flip, a stale listed diff that now matches, a substituted reference dump)
    and asserts this script's own logic refuses each.
#>

[CmdletBinding()]
param(
    [ValidateSet('Net8', 'Net462', 'Net48', 'All')]
    [string] $Framework = 'All',

    [switch] $SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gridPath = Join-Path $repoRoot 'Tools/NetStandardCompat/grid-netstandard.tsv'
$referenceDumpPath = Join-Path $repoRoot 'Tools/NetStandardCompat/dump-net10.0.tsv'
$dumpProject = Join-Path $repoRoot 'Tools/NetStandardCompat/NetStandardCompatDump/NetStandardCompatDump.csproj'
$dumpBinDir = Join-Path $repoRoot 'Tools/NetStandardCompat/NetStandardCompatDump/bin/Release'

# Get-FieldDistance, Read-DumpTable, Compare-Dumps and the rest of the totalOrder ULP comparison --
# shared with scripts/regenerate-netstandard-compat-known-diff.ps1 so the gate and the only
# supported way to change what it gates on cannot drift into disagreeing about what "the dumps
# differ" means. See that file's own header for why it is a dot-sourced library rather than code
# duplicated into both scripts.
. (Join-Path $PSScriptRoot 'lib-netstandard-compat-compare.ps1')

# Tests/netstandard-compat/known-diff-<fw>.tsv: case_id, category, max_ulp, reason. category is a
# fixed label today (RUNTIME-MATH for a numeric field diff, RETC-OR-SERR for a retc/err diff) --
# a single value each, not a rich taxonomy the way Tests/oracle/known-diff.tsv's PORT-VERSION/
# RETC/SERR/LIBM-RESIDUAL split is, because this instrument compares two .NET runtimes against
# each other, not a port against a C reference, so most of that taxonomy has no counterpart here.
# max_ulp is the literal text "categorical" for a retc/err (or NaN-involving) row, matching
# Tools/OracleVerify/KnownDiffEntry.cs's own convention.
function Read-KnownDiffTable {
    param([string] $Path)
    $table = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
    $lines = @(Get-Content -LiteralPath $Path)
    if ($lines.Count -eq 0) {
        throw "$Path has no header row."
    }
    $expectedHeader = @('case_id', 'category', 'max_ulp', 'reason') -join "`t"
    if ($lines[0] -ne $expectedHeader) {
        throw "$Path`: expected header '$expectedHeader', got '$($lines[0])'."
    }
    for ($i = 1; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ([string]::IsNullOrEmpty($line)) { continue }
        $cols = $line -split "`t"
        if ($cols.Count -ne 4) {
            throw "$Path`:$($i + 1): expected 4 tab-separated columns, got $($cols.Count): '$line'"
        }
        $isCategorical = $cols[2] -eq 'categorical'
        $maxUlp = if ($isCategorical) { [UInt64]0 } else { [UInt64]$cols[2] }
        $table[$cols[0]] = [pscustomobject]@{ Category = $cols[1]; MaxUlp = $maxUlp; IsCategorical = $isCategorical; Reason = $cols[3] }
    }
    return $table
}

# The gate itself: compares a freshly computed $Current divergence table (Compare-Dumps' own
# return shape, keyed by only-the-differing case_ids) against $Known (Read-KnownDiffTable's own
# return shape). Returns a list of human-readable failure descriptions; empty means the known-diff
# list is exactly accurate -- every current divergence is listed at or under its recorded max, and
# nothing listed has silently started passing.
function Get-KnownDiffFailures {
    param($Current, $Known)

    $failures = [System.Collections.Generic.List[string]]::new()

    # $knownRow, not $known: the same case-insensitive name-collision trap Compare-Dumps' own
    # $refRow/$otherRow comment above describes -- a local $known would be the same variable as
    # this function's own $Known parameter, and by the time the second loop below reads
    # $Known.Keys, $Known would silently hold only the last row this loop assigned into it instead
    # of the whole table. Found by this script's own -SelfTest (case 5, the stale-row check, which
    # depends on the second loop below actually running against the real $Known).
    foreach ($caseId in $Current.Keys) {
        $cur = $Current[$caseId]
        if (-not $Known.ContainsKey($caseId)) {
            $failures.Add("$caseId differs (reason: $($cur.Reason)) but is not in the known-diff list -- a regression, or a case newly covered by the grid.")
            continue
        }
        $knownRow = $Known[$caseId]
        if ($knownRow.IsCategorical -ne $cur.IsCategorical) {
            $from = if ($knownRow.IsCategorical) { 'categorical' } else { $knownRow.MaxUlp }
            $to = if ($cur.IsCategorical) { 'categorical' } else { $cur.MaxUlp }
            $failures.Add("$caseId`'s categorical/numeric state changed: recorded $from, now $to.")
            continue
        }
        if (-not $cur.IsCategorical -and $cur.MaxUlp -gt $knownRow.MaxUlp) {
            $failures.Add("$caseId`'s max ULP distance grew: recorded $($knownRow.MaxUlp), now $($cur.MaxUlp).")
        }

        # The Reason column names which fields diverge, and until this check existed it was parsed
        # into both tables and then never compared -- so a field that had never diverged could start
        # diverging on a listed case and pass, provided it stayed under that row's recorded ULP
        # ceiling. The ceilings are not small: NSC|45|2451545 records 16,517,719,336,359 ULP, about
        # 3.7e-3 relative, so a newly-diverging field had most of a percent to move in silence.
        #
        # Compared as a set, not as a string: the field order within the reason is not guaranteed
        # stable and a pure string compare would fire on a reordering that means nothing. Only
        # fields appearing now and NOT recorded are a failure; a field that stops diverging is
        # caught by the stale-row sweep in the second loop rather than here.
        $knownFields = @($knownRow.Reason -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        $curFields = @($cur.Reason -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        $newFields = @($curFields | Where-Object { $knownFields -notcontains $_ })
        if ($newFields.Count -gt 0) {
            $failures.Add("$caseId`: field(s) not previously diverging now diverge: $($newFields -join ', ') -- recorded '$($knownRow.Reason)', now '$($cur.Reason)'.")
        }
    }

    foreach ($caseId in $Known.Keys) {
        if (-not $Current.ContainsKey($caseId)) {
            $failures.Add("$caseId is listed in the known-diff file but now matches outright -- prune it (scripts/regenerate-netstandard-compat-known-diff.ps1).")
        }
    }

    return , $failures
}

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

function Get-ExePath {
    param([string] $Tfm)
    return Join-Path $dumpBinDir "$Tfm/NetStandardCompatDump.exe"
}

# MEDIUM 5's own vacuity floor: a reference dump with zero rows compares nothing against nothing,
# every per-framework loop iteration below finds $current empty and $known's own SHA-256 staleness
# check is satisfied trivially (an empty freshly-generated dump hashes identically to an empty
# committed one) -- so a PASS here would mean this gate verified nothing, not that nothing
# diverges. Compare scripts/verify-doc-no-removed-apis.ps1's own $checkedFiles -eq 0 floor: "a run
# that scanned nothing is not a pass." The realistic route to this state is not the reference dump
# itself going empty (a real byte-for-byte SHA-256 check already guards that from drifting silently)
# but Tools/NetStandardCompat/grid-netstandard.tsv shrinking to nothing -- both dumps would then
# legitimately agree on zero rows, and this floor is what refuses to call that a pass.
function Test-ReferenceHasRows {
    param([System.Collections.Generic.Dictionary[string, object]] $Reference)
    return $Reference.Count -gt 0
}

# ---------------------------------------------------------------------------------------
# Self-test -- see -SelfTest above. Exercises Get-FieldDistance/Compare-Dumps/Read-KnownDiffTable/
# Get-KnownDiffFailures directly against small, hand-built fixtures. Builds nothing, runs no dump
# tool, touches no tracked file.
# ---------------------------------------------------------------------------------------

if ($SelfTest) {
    $failures = 0
    $lab = Join-Path ([System.IO.Path]::GetTempPath()) ('verify-netstandard-compat-selftest-' + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $lab | Out-Null
    try {
        function New-LabFile {
            param([string] $RelPath, [string[]] $Lines)
            $full = Join-Path $lab $RelPath
            New-Item -ItemType Directory -Force -Path (Split-Path $full -Parent) | Out-Null
            [System.IO.File]::WriteAllText($full, (($Lines -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))
            return $full
        }

        function Assert-True {
            param([string] $Case, [bool] $Condition, [string] $Detail = '')
            if ($Condition) {
                Write-Host "  PASS  $Case" -ForegroundColor DarkGray
            }
            else {
                Write-Host "  FAIL  $Case`n          $Detail" -ForegroundColor Red
                $script:failures++
            }
        }

        Write-Host 'verify-netstandard-compat self-test'
        Write-Host ''

        # A one-field-differing pair vs. an identical pair, to pin Get-FieldDistance's basic shape.
        $identical = Get-FieldDistance -HexA '3ff0000000000000' -HexB '3ff0000000000000'
        Assert-True 'identical hex is zero ULP, not categorical' (-not $identical.IsCategorical -and $identical.MaxUlp -eq 0)

        $oneUlpApart = Get-FieldDistance -HexA '3ff0000000000000' -HexB '3ff0000000000001'
        Assert-True 'adjacent bit patterns are exactly 1 ULP apart' (-not $oneUlpApart.IsCategorical -and $oneUlpApart.MaxUlp -eq 1)

        # +0.0 vs -0.0: different hex, must NOT read as zero ULP (the double.Equals trap
        # Tools/OracleVerify/UlpMath.cs's own remarks describe and this reimplementation must not
        # repeat).
        $signedZero = Get-FieldDistance -HexA '0000000000000000' -HexB '8000000000000000'
        Assert-True '+0.0 vs -0.0 is a nonzero ULP distance, not treated as equal' (-not $signedZero.IsCategorical -and $signedZero.MaxUlp -gt 0) "got IsCategorical=$($signedZero.IsCategorical) MaxUlp=$($signedZero.MaxUlp)"

        # NaN vs finite: categorical, not a bogus huge ULP count.
        $nanCase = Get-FieldDistance -HexA '7ff8000000000000' -HexB '3ff0000000000000'
        Assert-True 'NaN vs finite is categorical' $nanCase.IsCategorical

        # --- Compare-Dumps / Get-KnownDiffFailures, against a tiny synthetic dump pair ---

        function New-Row {
            param([string] $Retc, [string] $Err, [string[]] $Hexes)
            return [pscustomobject]@{ Retc = $Retc; Err = $Err; Hexes = $Hexes }
        }
        $sixZeroHex = @('0000000000000000') * 6

        $reference = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
        $reference['A'] = New-Row -Retc '0' -Err '' -Hexes $sixZeroHex
        $reference['B'] = New-Row -Retc '0' -Err '' -Hexes @('3ff0000000000000', '0000000000000000', '0000000000000000', '0000000000000000', '0000000000000000', '0000000000000000')
        $reference['C'] = New-Row -Retc '0' -Err '' -Hexes $sixZeroHex

        # 1. A field that newly differs, with no known-diff row at all -- the core regression this
        #    gate exists to catch.
        $otherNewDiff = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
        $otherNewDiff['A'] = New-Row -Retc '0' -Err '' -Hexes @('3ff0000000000000', '0000000000000000', '0000000000000000', '0000000000000000', '0000000000000000', '0000000000000000')
        $otherNewDiff['B'] = $reference['B']
        $otherNewDiff['C'] = $reference['C']
        $currentNewDiff = Compare-Dumps -Reference $reference -Other $otherNewDiff
        Assert-True 'Compare-Dumps finds exactly the one newly-differing case_id' ($currentNewDiff.Count -eq 1 -and $currentNewDiff.ContainsKey('A'))
        $emptyKnown = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
        $failuresNewDiff = Get-KnownDiffFailures -Current $currentNewDiff -Known $emptyKnown
        Assert-True 'an unlisted new diff is refused' ($failuresNewDiff.Count -eq 1 -and $failuresNewDiff[0] -match 'not in the known-diff list') "got: $($failuresNewDiff -join ' | ')"

        # 2. A listed row whose current ULP distance exceeds its recorded max -- must fail even
        #    though the case_id IS on the list (verify-oracle.ps1's own "gates on magnitude, not
        #    just category" behavior, reproduced here).
        $knownSmall = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
        $knownSmall['A'] = [pscustomobject]@{ Category = 'RUNTIME-MATH'; MaxUlp = [UInt64]1; IsCategorical = $false; Reason = 'longitude' }
        $failuresGrew = Get-KnownDiffFailures -Current $currentNewDiff -Known $knownSmall
        Assert-True 'a listed row whose ULP distance grew past its recorded max is refused' ($failuresGrew.Count -eq 1 -and $failuresGrew[0] -match 'grew') "got: $($failuresGrew -join ' | ')"

        # 3. The same case_id, recorded with a max ULP at least as large as what is currently
        #    measured -- must pass.
        $bitsA = [Convert]::ToUInt64('3ff0000000000000', 16)
        $currentUlp = (Get-OrderedKey -Bits $bitsA) - (Get-OrderedKey -Bits ([Convert]::ToUInt64('0000000000000000', 16)))
        $knownEnough = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
        $knownEnough['A'] = [pscustomobject]@{ Category = 'RUNTIME-MATH'; MaxUlp = $currentUlp; IsCategorical = $false; Reason = 'longitude' }
        $failuresOk = Get-KnownDiffFailures -Current $currentNewDiff -Known $knownEnough
        Assert-True 'a listed row at or above its current ULP distance passes' ($failuresOk.Count -eq 0) "got: $($failuresOk -join ' | ')"

        # 3b. A field that was not previously diverging starts diverging on a listed row, with the
        #     ULP distance still well under the recorded ceiling. Before the Reason set was
        #     compared, this passed: the Reason column was parsed into both tables and never read,
        #     so the only thing standing between a newly-diverging field and a green gate was that
        #     row's recorded max -- and the largest recorded max in the shipped lists is
        #     16,517,719,336,359 ULP, roughly 3.7e-3 relative.
        $knownOneField = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
        $knownOneField['A'] = [pscustomobject]@{ Category = 'RUNTIME-MATH'; MaxUlp = $currentUlp; IsCategorical = $false; Reason = 'latitude-speed,distance-speed' }
        $currentTwoFields = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
        $currentTwoFields['A'] = [pscustomobject]@{ Category = 'RUNTIME-MATH'; MaxUlp = [uint64]1; IsCategorical = $false; Reason = 'latitude-speed,distance-speed,longitude' }
        $failuresNewField = Get-KnownDiffFailures -Current $currentTwoFields -Known $knownOneField
        Assert-True 'a field that was not previously diverging is refused even below the recorded ULP ceiling' ($failuresNewField.Count -eq 1 -and $failuresNewField[0] -match 'not previously diverging') "got: $($failuresNewField -join ' | ')"

        # 3c. The control for 3b: the same fields in a different order must NOT fire. The order
        #     within the Reason column is not guaranteed stable, and a gate that fails on a
        #     reordering is one that gets its list regenerated to silence it.
        $currentReordered = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
        $currentReordered['A'] = [pscustomobject]@{ Category = 'RUNTIME-MATH'; MaxUlp = [uint64]1; IsCategorical = $false; Reason = 'distance-speed,latitude-speed' }
        $failuresReordered = Get-KnownDiffFailures -Current $currentReordered -Known $knownOneField
        Assert-True 'the same diverging fields in a different order pass' ($failuresReordered.Count -eq 0) "got: $($failuresReordered -join ' | ')"

        # 4. A categorical/numeric flip in either direction is refused even when the case_id stays
        #    listed -- a magnitude comparison would be meaningless across that boundary.
        $knownCategorical = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
        $knownCategorical['A'] = [pscustomobject]@{ Category = 'RUNTIME-MATH'; MaxUlp = [UInt64]0; IsCategorical = $true; Reason = 'longitude' }
        $failuresFlip = Get-KnownDiffFailures -Current $currentNewDiff -Known $knownCategorical
        Assert-True 'a categorical-to-numeric flip is refused' ($failuresFlip.Count -eq 1 -and $failuresFlip[0] -match 'categorical/numeric state changed') "got: $($failuresFlip -join ' | ')"

        # 5. A stale known-diff row (the case_id no longer differs at all) must be refused --
        #    otherwise a known-diff list only ever grows, and a real improvement goes unnoticed.
        $currentNoDiff = Compare-Dumps -Reference $reference -Other $reference
        $knownStale = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
        $knownStale['A'] = [pscustomobject]@{ Category = 'RUNTIME-MATH'; MaxUlp = [UInt64]5; IsCategorical = $false; Reason = 'longitude' }
        $failuresStale = Get-KnownDiffFailures -Current $currentNoDiff -Known $knownStale
        Assert-True 'a stale (no-longer-differing) known-diff row is refused' ($failuresStale.Count -eq 1 -and $failuresStale[0] -match 'now matches outright') "got: $($failuresStale -join ' | ')"

        # 6. A retc/err disagreement is treated as categorical, not silently ignored or given a
        #    bogus ULP count.
        $otherRetcDiff = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
        $otherRetcDiff['A'] = New-Row -Retc '-1' -Err 'some error' -Hexes $sixZeroHex
        $otherRetcDiff['B'] = $reference['B']
        $otherRetcDiff['C'] = $reference['C']
        $currentRetcDiff = Compare-Dumps -Reference $reference -Other $otherRetcDiff
        Assert-True 'a retc/err disagreement is categorical' ($currentRetcDiff.Count -eq 1 -and $currentRetcDiff['A'].IsCategorical -and $currentRetcDiff['A'].Reason -eq 'retc,err') "got: $($currentRetcDiff['A'] | ConvertTo-Json -Compress)"

        # 7. Compare-Dumps refuses a case_id-set mismatch outright rather than silently comparing a
        #    subset -- the same "a missing dump is a different problem from a differing one"
        #    reasoning scripts/verify-oracle.ps1's own header gives for its analogous check.
        $otherMissingRow = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
        $otherMissingRow['A'] = $reference['A']
        $otherMissingRow['B'] = $reference['B']
        $threwOnMismatch = $false
        try { Compare-Dumps -Reference $reference -Other $otherMissingRow | Out-Null }
        catch { $threwOnMismatch = $true }
        Assert-True 'a case_id-set mismatch between the two dumps throws rather than comparing a subset' $threwOnMismatch

        # 8. Read-KnownDiffTable round-trips a real file, including "categorical" in the max_ulp
        #    column.
        $knownDiffFile = New-LabFile 'known-diff-lab.tsv' @(
            ('case_id', 'category', 'max_ulp', 'reason') -join "`t"
            ('NSC|1|1', 'RUNTIME-MATH', '42', 'longitude') -join "`t"
            ('NSC|2|2', 'RETC-OR-SERR', 'categorical', 'retc') -join "`t"
        )
        $roundTripped = Read-KnownDiffTable -Path $knownDiffFile
        $rowOk = $roundTripped.Count -eq 2 -and $roundTripped['NSC|1|1'].MaxUlp -eq 42 -and -not $roundTripped['NSC|1|1'].IsCategorical -and $roundTripped['NSC|2|2'].IsCategorical
        Assert-True 'Read-KnownDiffTable parses numeric and categorical max_ulp correctly' $rowOk

        # 9. MEDIUM 5's vacuity floor: a reference dump with zero rows must be refused, not read as
        #    "nothing differs".
        $emptyReference = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
        Assert-True 'Test-ReferenceHasRows refuses a zero-row reference dump' (-not (Test-ReferenceHasRows -Reference $emptyReference))
        Assert-True 'Test-ReferenceHasRows accepts a reference dump with at least one row' (Test-ReferenceHasRows -Reference $reference)

        Write-Host ''

        # 10. MEDIUM 5: everything above is an in-process function test -- none of it ever invokes
        #     this script's own real path in a CHILD process the way scripts/verify-freeze.ps1's own
        #     -SelfTest does, so the build -> staleness check -> per-framework comparison loop ->
        #     exit-code path was covered by nothing. -Framework Net8 (the fastest framework to
        #     compare, and the one with an empty known-diff list today) keeps this cheap while still
        #     exercising the full real pipeline end to end: `dotnet build` of the dump project, the
        #     net10.0 staleness re-run and SHA-256 compare, one framework's dump-and-compare loop,
        #     and this script's own real exit code. Runs against this actual checkout, not a
        #     scratch repo -- there is nothing here for a synthetic fixture to stand in for; the
        #     grid, the reference dump and the known-diff files it exercises are exactly the
        #     committed inputs this gate exists to check in CI.
        Write-Host 'Child-process case (the real path: build, staleness check, comparison loop, exit code)'
        $pwshExe = (Get-Process -Id $PID).Path
        $childOutput = & $pwshExe -NoProfile -File $PSCommandPath -Framework Net8 *>&1
        $childCode = $LASTEXITCODE
        $childText = (@($childOutput) -join "`n")
        Assert-True 'a real child-process run (-Framework Net8) against this checkout exits 0 and reports PASS' `
            ($childCode -eq 0 -and $childText -match 'matches the current run exactly') `
            "exit=$childCode output: $childText"
        if ($failures -gt 0) {
            Write-Host "FAIL: $failures self-test case(s) did not behave as required." -ForegroundColor Red
            exit 1
        }
        Write-Host 'PASS: all verify-netstandard-compat self-test cases behaved as required.' -ForegroundColor Green
        exit 0
    }
    finally {
        Remove-Item -LiteralPath $lab -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# ---------------------------------------------------------------------------------------
# Real run.
# ---------------------------------------------------------------------------------------

$exitCode = 0
try {
    if (-not (Test-Path -LiteralPath $gridPath -PathType Leaf)) {
        throw "Grid not found at $gridPath. Run: pwsh Tools/NetStandardCompat/gen-grid-netstandard.ps1"
    }
    if (-not (Test-Path -LiteralPath $referenceDumpPath -PathType Leaf)) {
        throw "Reference dump not found at $referenceDumpPath."
    }

    Write-Host "Building $dumpProject (Release, all target frameworks)..."
    $buildOutput = & dotnet build $dumpProject -c Release --nologo -v minimal 2>&1
    if ($LASTEXITCODE -ne 0) {
        $buildOutput | Write-Host
        throw 'dotnet build Tools/NetStandardCompat/NetStandardCompatDump failed.'
    }
    Write-Host ''

    # Stale-reference check: always rerun net10.0 fresh and diff it byte-for-byte against the
    # committed reference. Cheap (net10.0, no ephemeris file, 102 rows), so this runs on every
    # invocation rather than behind a provenance sidecar the way scripts/verify-oracle.ps1's
    # C-toolchain-backed equivalent needs.
    Write-Host 'Checking the committed net10.0 reference dump is not stale...'
    $freshReferencePath = [System.IO.Path]::GetTempFileName()
    try {
        $net10Exe = Get-ExePath -Tfm 'net10.0'
        if (-not (Test-Path -LiteralPath $net10Exe -PathType Leaf)) {
            throw "net10.0 build of NetStandardCompatDump not found at $net10Exe."
        }
        & $net10Exe $gridPath $freshReferencePath 2>&1 | Write-Host
        if ($LASTEXITCODE -ne 0) { throw 'NetStandardCompatDump (net10.0) failed while regenerating the reference for the staleness check.' }

        $freshHash = (Get-FileHash -LiteralPath $freshReferencePath -Algorithm SHA256).Hash
        $committedHash = (Get-FileHash -LiteralPath $referenceDumpPath -Algorithm SHA256).Hash
        if ($freshHash -ne $committedHash) {
            throw "The committed reference dump ($referenceDumpPath) no longer matches a fresh net10.0 run (recorded $committedHash, now $freshHash). Either the grid or the port changed since it was generated -- regenerate it: run the net10.0 build of Tools/NetStandardCompat/NetStandardCompatDump against $gridPath and commit the result."
        }
        Write-Host "PASS: $referenceDumpPath matches a fresh net10.0 run ($committedHash)." -ForegroundColor Green
        Write-Host ''
    }
    finally {
        Remove-Item -LiteralPath $freshReferencePath -Force -ErrorAction SilentlyContinue
    }

    $reference = Read-DumpTable -Path $referenceDumpPath
    if (-not (Test-ReferenceHasRows -Reference $reference)) {
        throw "$referenceDumpPath has zero rows -- compares nothing against nothing. A PASS from here would mean this gate verified nothing, not that nothing diverges (see this script's own Test-ReferenceHasRows comment). If Tools/NetStandardCompat/grid-netstandard.tsv genuinely shrank to zero rows on purpose, that is not something this gate can pass through silently -- fix the grid deliberately (Tools/NetStandardCompat/gen-grid-netstandard.ps1 -Reason '...') rather than let it drift to empty."
    }

    $frameworks = if ($Framework -eq 'All') { @('Net8', 'Net462', 'Net48') } else { @($Framework) }
    $failedFrameworks = @()
    foreach ($fw in $frameworks) {
        $info = Get-FrameworkInfo -Name $fw
        Write-Host "=== $fw ($($info.Tfm)) ===" -ForegroundColor Cyan

        $exePath = Get-ExePath -Tfm $info.Tfm
        if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
            Write-Host "FAIL: $($info.Tfm) build of NetStandardCompatDump not found at $exePath." -ForegroundColor Red
            $failedFrameworks += $fw
            continue
        }
        if (-not (Test-Path -LiteralPath $info.KnownDiffPath -PathType Leaf)) {
            Write-Host "FAIL: known-diff file not found at $($info.KnownDiffPath)." -ForegroundColor Red
            $failedFrameworks += $fw
            continue
        }

        $otherDumpPath = [System.IO.Path]::GetTempFileName()
        try {
            & $exePath $gridPath $otherDumpPath 2>&1 | Write-Host
            if ($LASTEXITCODE -ne 0) {
                Write-Host "FAIL: NetStandardCompatDump ($($info.Tfm)) exited $LASTEXITCODE." -ForegroundColor Red
                $failedFrameworks += $fw
                continue
            }

            $other = Read-DumpTable -Path $otherDumpPath
            $current = Compare-Dumps -Reference $reference -Other $other
            $known = Read-KnownDiffTable -Path $info.KnownDiffPath
            $gateFailures = Get-KnownDiffFailures -Current $current -Known $known

            Write-Host "Currently differing: $($current.Count) of $($reference.Count) rows. Known-diff lists: $($known.Count)."
            if ($gateFailures.Count -gt 0) {
                Write-Host "FAIL: $($info.KnownDiffPath) does not match the current run:" -ForegroundColor Red
                foreach ($f in $gateFailures) { Write-Host "  - $f" -ForegroundColor Red }
                $failedFrameworks += $fw
            }
            else {
                Write-Host "PASS: $($info.KnownDiffPath) matches the current run exactly." -ForegroundColor Green
            }
        }
        finally {
            Remove-Item -LiteralPath $otherDumpPath -Force -ErrorAction SilentlyContinue
        }
        Write-Host ''
    }

    if ($failedFrameworks.Count -gt 0) {
        Write-Host "FAIL: $($failedFrameworks -join ', ')" -ForegroundColor Red
        $exitCode = 1
    }
}
catch {
    Write-Host "FAIL: $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 1
}

exit $exitCode
