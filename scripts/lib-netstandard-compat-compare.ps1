#Requires -Version 7
<#
.SYNOPSIS
    Shared comparison functions for the netstandard2.0-vs-net10.0 swe_calc instrument. Dot-sourced
    by both scripts/verify-netstandard-compat.ps1 (the gate) and
    scripts/regenerate-netstandard-compat-known-diff.ps1 (the only supported way to change what
    the gate compares against), so the two scripts cannot drift into disagreeing about what "the
    dumps differ" means.

.DESCRIPTION
    Not a gate and not a regeneration tool itself -- has no -SelfTest of its own. Both
    scripts/verify-netstandard-compat.ps1's own -SelfTest and any future test of
    scripts/regenerate-netstandard-compat-known-diff.ps1 exercise these functions indirectly by
    dot-sourcing this same file, which is the coverage that matters: a bug in one of these
    functions would show up wherever it is dot-sourced from.

    A PowerShell reimplementation of Tools/OracleVerify/UlpMath.cs's totalOrder-based Distance, not
    a call into that C# project: this instrument has no C# comparison tool of its own (see
    Tools/NetStandardCompat/NetStandardCompatDump/Program.cs's own header for why a lean,
    PowerShell-only comparator was chosen instead), so the same bit-pattern-ordering algorithm is
    re-expressed here in PowerShell rather than shared across languages.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$SignBit = [Convert]::ToUInt64('8000000000000000', 16)

function ConvertTo-Double {
    param([string] $Hex)
    $bits = [Convert]::ToUInt64($Hex, 16)
    $signed = [BitConverter]::ToInt64([BitConverter]::GetBytes($bits), 0)
    return [BitConverter]::Int64BitsToDouble($signed)
}

function Get-OrderedKey {
    param([UInt64] $Bits)
    if (($Bits -band $SignBit) -ne 0) {
        return (-bnot $Bits)
    }
    return ($Bits -bor $SignBit)
}

# Returns @{ IsCategorical; MaxUlp } for a single (hexA, hexB) pair. IsCategorical mirrors
# Tools/OracleVerify/UlpMath.cs's CategoricalDistance: one side NaN, the other not (or two
# different NaN payloads) has no meaningful magnitude, so it is tracked as its own state rather
# than coerced into a number. This instrument has never measured a NaN in any committed dump --
# every swe_calc call this grid carries returns finite doubles -- so this branch exists for
# correctness and for -SelfTest to exercise, not because any committed dump needs it today.
function Get-FieldDistance {
    param([string] $HexA, [string] $HexB)
    if ($HexA -eq $HexB) {
        return [pscustomobject]@{ IsCategorical = $false; MaxUlp = [UInt64]0 }
    }
    $a = ConvertTo-Double -Hex $HexA
    $b = ConvertTo-Double -Hex $HexB
    if ([double]::IsNaN($a) -or [double]::IsNaN($b)) {
        return [pscustomobject]@{ IsCategorical = $true; MaxUlp = [UInt64]0 }
    }
    $bitsA = [Convert]::ToUInt64($HexA, 16)
    $bitsB = [Convert]::ToUInt64($HexB, 16)
    $keyA = Get-OrderedKey -Bits $bitsA
    $keyB = Get-OrderedKey -Bits $bitsB
    $distance = if ($keyA -gt $keyB) { $keyA - $keyB } else { $keyB - $keyA }
    return [pscustomobject]@{ IsCategorical = $false; MaxUlp = $distance }
}

# case_id, retc, err, then six (decimal, hex) pairs -- see
# Tools/NetStandardCompat/NetStandardCompatDump/Program.cs's own header for the on-disk shape.
$FieldNames = @('longitude', 'latitude', 'distance', 'longitude-speed', 'latitude-speed', 'distance-speed')

function Read-DumpTable {
    param([string] $Path)
    $table = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
    $lines = Get-Content -LiteralPath $Path
    foreach ($line in $lines) {
        if ([string]::IsNullOrEmpty($line)) { continue }
        $fields = $line -split "`t"
        if ($fields.Count -ne 15) {
            throw "$Path`: expected 15 tab-separated columns (case_id, retc, err, 6x(decimal,hex)), got $($fields.Count): '$line'"
        }
        $hexes = for ($i = 0; $i -lt 6; $i++) { $fields[3 + ($i * 2) + 1] }
        $table[$fields[0]] = [pscustomobject]@{ Retc = $fields[1]; Err = $fields[2]; Hexes = @($hexes) }
    }
    return $table
}

# Compares every case_id present in $Reference against $Other (both must carry the identical
# case_id set -- a mismatched set is a grid-vs-dump-tool desync, not a numeric divergence, and
# fails outright rather than silently comparing a subset). Returns a dictionary of only the
# DIFFERING case_ids: case_id -> @{ IsCategorical; MaxUlp; Reason }. Retc/err disagreement is
# folded into "categorical" (no ULP magnitude applies to an integer return code or an error
# string), matching Get-FieldDistance's own NaN handling.
function Compare-Dumps {
    param([System.Collections.Generic.Dictionary[string, object]] $Reference, [System.Collections.Generic.Dictionary[string, object]] $Other)

    $missingInOther = @($Reference.Keys | Where-Object { -not $Other.ContainsKey($_) })
    $missingInReference = @($Other.Keys | Where-Object { -not $Reference.ContainsKey($_) })
    if ($missingInOther.Count -gt 0 -or $missingInReference.Count -gt 0) {
        throw "Compare-Dumps: the two dumps do not cover the identical case_id set (missing in other: $($missingInOther.Count), missing in reference: $($missingInReference.Count)) -- this is a grid/dump-tool desync, not a numeric divergence."
    }

    # $refRow/$otherRow, not $ref/$other: PowerShell variable names are case-insensitive, so a
    # local $other would be the SAME variable as this function's own $Other parameter (typed
    # System.Collections.Generic.Dictionary[string, object]) -- assigning a single row into it
    # then fails with a bewildering "cannot convert row to Dictionary" error, since PowerShell
    # still enforces the parameter's declared type on every assignment to that variable, including
    # ones that only look like a different name. Found by scripts/verify-netstandard-compat.ps1's
    # own -SelfTest.
    #
    # A real Dictionary, not [ordered]@{}: callers of this function (both scripts that dot-source
    # this file) call .ContainsKey on whatever this returns, and a Hashtable/OrderedDictionary only
    # has .Contains (a different method name) for that same check -- keeping this the same concrete
    # type as Read-KnownDiffTable's own return value in scripts/verify-netstandard-compat.ps1 means
    # every caller can use .ContainsKey uniformly.
    $result = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
    foreach ($caseId in $Reference.Keys) {
        $refRow = $Reference[$caseId]
        $otherRow = $Other[$caseId]

        if ($refRow.Retc -ne $otherRow.Retc -or $refRow.Err -ne $otherRow.Err) {
            $reasonParts = [System.Collections.Generic.List[string]]::new()
            if ($refRow.Retc -ne $otherRow.Retc) { $reasonParts.Add('retc') }
            if ($refRow.Err -ne $otherRow.Err) { $reasonParts.Add('err') }
            $result[$caseId] = [pscustomobject]@{ IsCategorical = $true; MaxUlp = [UInt64]0; Reason = ($reasonParts -join ',') }
            continue
        }

        $maxUlp = [UInt64]0
        $isCategorical = $false
        $reasonFields = [System.Collections.Generic.List[string]]::new()
        for ($i = 0; $i -lt 6; $i++) {
            $d = Get-FieldDistance -HexA $refRow.Hexes[$i] -HexB $otherRow.Hexes[$i]
            if ($d.IsCategorical) {
                $isCategorical = $true
                $reasonFields.Add($FieldNames[$i])
            }
            elseif ($d.MaxUlp -gt 0) {
                if ($d.MaxUlp -gt $maxUlp) { $maxUlp = $d.MaxUlp }
                $reasonFields.Add($FieldNames[$i])
            }
        }
        if ($reasonFields.Count -gt 0) {
            $result[$caseId] = [pscustomobject]@{ IsCategorical = $isCategorical; MaxUlp = $maxUlp; Reason = ($reasonFields -join ',') }
        }
    }
    return $result
}
