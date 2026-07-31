#Requires -Version 7
<#
.SYNOPSIS
    Fails if a documentation file that ships with the package teaches a removed API as something
    to call, rather than only mentioning that it once existed.

.DESCRIPTION
    README.md kept a "Loading files" tutorial telling readers to subscribe to `OnLoadFile` for one
    release after the event itself was deleted (replaced by `SwissEph.FileProvider` /
    `IEphemerisFileProvider`) -- and README.md is packaged (`PackageReadmeFile` in
    SwissEphNet.csproj), so that tutorial shipped to every consumer who installed the library and
    read its own package page. Nothing caught it because nothing distinguished "this document
    mentions a removed API" (legitimate: the breaking-changes list has to keep saying `OnLoadFile`
    was removed, or the removal itself becomes undiscoverable) from "this document tells a reader
    to use a removed API right now" (a defect).

    Historical mentions are legitimate and have to stay checkable-safe, so this script does not
    flag every occurrence of a removed API's name. It flags only a removed API's name appearing
    inside a fenced code block (a ```` ``` ````-delimited sample presented as something to copy and
    run) that sits outside a recognized historical/migration section of the document. A fenced
    code sample is the concrete shape the actual defect took -- prose narrative ("`OnLoadFile` is
    gone", "used to raise the `OnLoadFile` event") does not look like an instruction to call
    anything, but a code block does, regardless of what surrounds it in prose. Combined with the
    section check, a deliberate "here is the old code, here is the new code" comparison placed
    inside a heading this script recognizes as historical (Breaking changes, Upgrading from,
    Migration, Change log, History, Release notes, and their subsections) stays exempt too.

    This is a deliberately narrow signal, chosen over a broader "flag any mention outside a
    historical section" rule specifically because the broader rule produces a false positive on
    this repository's own README.md today: the current "Loading files" section (legitimately
    "teaches the present" -- it shows `FileProvider`, not `OnLoadFile`) ends with "See the
    'V:2.10.3' entry above for what this replaces (`OnLoadFile`) and why" -- a backward-pointing
    parenthetical in prose, not an instruction. Between under-flagging a rare deliberate
    old-code-outside-a-historical-section sample and over-flagging every backward-reference like
    that one, this script accepts the false negative: a prose mention is never enough to fail this
    check, only a fenced code block is.

    Scope: only files that actually ship as documentation for current use --
    SwissEphNet.csproj's PackageReadmeFile (README.md) today. docs/known-issues.md and
    docs/compliance-2.10.03.md are excluded on purpose: neither packages with the library, and
    known-issues.md exists specifically as a historical record (its own removed-API entries, e.g.
    "OnLoadFile superseded", are the kind of content this script would otherwise have to
    special-case line by line). docs/upstream/ is untracked and out of scope for every check in
    this repository, not just this one.

.PARAMETER RepoRoot
    Repository root. Defaults to the checkout containing this script.
#>
[CmdletBinding()]
param(
    [string] $RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Symbol name -> what replaced it, for the failure message. Extend this table, not the logic
# below, when a future release removes another public API that documentation could still be
# teaching.
$RemovedApis = [ordered]@{
    'OnLoadFile'         = 'SwissEph.FileProvider (an IEphemerisFileProvider)'
    'LoadFileEventArgs'  = 'no replacement -- IEphemerisFileProvider.Open returns a Stream directly'
}

# Headings that open a historical/migration region. Matched against heading text case-insensitively;
# a heading's own subsections (deeper '#' nesting) inherit the region until a heading at the same
# or shallower level appears that does not itself match.
$historicalHeadingPattern = '(?i)\b(Breaking changes|Upgrading from|Migration|Change ?log|History|Release notes)\b'

$currentUsageDocs = @('README.md') | ForEach-Object { Join-Path $RepoRoot $_ } |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }

$failures = [System.Collections.Generic.List[string]]::new()
$checkedFiles = 0

foreach ($docPath in $currentUsageDocs) {
    $checkedFiles++
    $relPath = [System.IO.Path]::GetRelativePath($RepoRoot, $docPath).Replace('\', '/')
    $lines = Get-Content -LiteralPath $docPath

    $inHistorical = $false
    $historicalStartLevel = 0
    $inFence = $false

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        $lineNumber = $i + 1

        if ($line -match '^(#+)\s+(.*)$') {
            $level = $Matches[1].Length
            $text = $Matches[2]
            if ($text -match $historicalHeadingPattern) {
                $inHistorical = $true
                $historicalStartLevel = $level
            }
            elseif ($inHistorical -and $level -le $historicalStartLevel) {
                $inHistorical = $false
                $historicalStartLevel = 0
            }
            continue
        }

        # Fenced code blocks: ``` or ~~~, optionally with a language tag on the opening fence.
        if ($line -match '^\s*(```|~~~)') {
            $inFence = -not $inFence
            continue
        }

        if (-not $inFence -or $inHistorical) { continue }

        foreach ($api in $RemovedApis.Keys) {
            if ($line -match "\b$([regex]::Escape($api))\b") {
                $failures.Add(
                    "${relPath}:${lineNumber}: fenced code block outside any historical section names " +
                    "'$api', which no longer exists. Replace it with $($RemovedApis[$api]), or, if this " +
                    "genuinely is a historical before/after sample, move it under a heading this script " +
                    "recognizes as historical ($historicalHeadingPattern).")
            }
        }
    }
}

Write-Host "Checked $checkedFiles current-usage documentation file(s) for $($RemovedApis.Count) removed API name(s)."

if ($failures.Count -gt 0) {
    Write-Host ''
    foreach ($failure in $failures) { Write-Host "  $failure" }
    Write-Host ''
    Write-Host 'FAIL: current-usage documentation teaches an API this library no longer exposes.'
    exit 1
}

Write-Host 'PASS: no current-usage documentation instructs a reader to use a removed API.'
exit 0
