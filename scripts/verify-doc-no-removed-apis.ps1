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
    flag every occurrence of a removed API's name. It flags only a removed API's name appearing in
    a code sample presented as something to copy and run -- a ```` ``` ````/`~~~`-delimited fenced
    block, a 4-space/tab-indented block, or a raw `<pre>...</pre>` block, markdown's three standard
    ways to show one -- that sits outside a recognized historical/migration section of the
    document. A code sample is the concrete shape the actual defect took -- prose narrative
    ("`OnLoadFile` is gone", "used to raise the `OnLoadFile` event") does not look like an
    instruction to call anything, but a code sample does, regardless of what surrounds it in
    prose. Combined with the section check, a deliberate "here is the old code, here is the new
    code" comparison placed inside a heading this script recognizes as historical (Breaking
    changes, Upgrading from, Migration, Change log, History, Release notes, and their subsections)
    stays exempt too.

    The fence test runs before the heading test, not after: a shell/yaml/python comment inside a
    fenced sample (`# Migration steps for the CLI`) starts with `#` like a markdown heading does,
    and testing headings first let it open a historical region for the rest of the document,
    exempting everything after it -- including an unrelated fenced sample later in the same file.
    An unbalanced fence or unclosed `<pre>` (an odd number of delimiters) is a hard failure in its
    own right, not a silent parity flip for the remainder of the file: this script cannot tell code
    from prose past that point, so it says so rather than guessing.

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
# teaching. A reflection diff of the shipped 2.8.0.2 package against the current build surfaces
# 32 removed public entries across three top-level names; only the first two were listed here
# until this check was added.
$RemovedApis = [ordered]@{
    'OnLoadFile'        = 'SwissEph.FileProvider (an IEphemerisFileProvider)'
    'LoadFileEventArgs' = 'no replacement -- IEphemerisFileProvider.Open returns a Stream directly'
    # Bare "TypeCode" (unqualified, as SwissEphNet.TypeCode read under `using SwissEphNet;`) is
    # what this catches; it also matches System.TypeCode, its own replacement, but this
    # repository's current source spells that usage `Type.GetTypeCode(...)`, never the bare type
    # name, so that collision has no legitimate sample to false-positive on today.
    'TypeCode'          = 'System.TypeCode -- the conditionally-compiled SwissEphNet.TypeCode ' +
                          '(shipped in 2.8.0.2 for target frameworks lacking System.TypeCode) was ' +
                          'dropped once every shipped target framework has the BCL one'
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
    $inPre = $false

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        $lineNumber = $i + 1

        # Fenced code blocks (``` or ~~~) are tested before the heading test, not after: a shell,
        # yaml or python comment ("# Migration steps for the CLI") inside a fenced sample used to
        # parse as a markdown heading and open a historical region for the rest of the document,
        # exempting everything after it -- including a second, unrelated fenced sample later in
        # the same file. A line that opens or closes a fence is delimiter syntax, not code content
        # to scan, so it still just toggles state and moves on.
        if ($line -match '^\s*(```|~~~)') {
            $inFence = -not $inFence
            continue
        }

        # Raw HTML <pre>...</pre> is markdown's third way of showing a code sample (the other two
        # are the fence above and the indented block below). Approximate, not a full HTML parser:
        # an opening or closing tag toggles state, and either tag's own line is itself treated as
        # code content (not skipped) so `<pre><code>OnLoadFile();</code></pre>` written on one
        # line is still caught.
        $preOpensHere = $line -match '(?i)<pre\b'
        $preClosesHere = $line -match '(?i)</pre\s*>'
        $inPreLine = $inPre -or $preOpensHere -or $preClosesHere
        if ($preOpensHere -and -not $preClosesHere) { $inPre = $true }
        if ($preClosesHere) { $inPre = $false }

        if (-not $inFence -and -not $inPreLine -and $line -match '^(#+)\s+(.*)$') {
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

        # Indented code block: 4+ spaces or a leading tab, CommonMark's other standard way to mark
        # a line as code with no delimiter at all. Judged per line, not as an open/close region
        # like the two block styles above -- there is no delimiter to track state from, only the
        # line's own leading whitespace. This is a heuristic, not a CommonMark parser: it does not
        # account for list-item continuation indentation, so a deeply nested list item could in
        # principle be misread as code. Accepted, because this repository's current documentation
        # has no such nesting and the alternative (ignoring indented samples entirely) is the
        # actual bypass this exists to close.
        $isIndentedCode = $line -match '^(\t| {4,})\S'

        $isCodeLine = $inFence -or $inPreLine -or $isIndentedCode
        if (-not $isCodeLine -or $inHistorical) { continue }

        foreach ($api in $RemovedApis.Keys) {
            if ($line -match "\b$([regex]::Escape($api))\b") {
                $failures.Add(
                    "${relPath}:${lineNumber}: code sample outside any historical section names " +
                    "'$api', which no longer exists. Replace it with $($RemovedApis[$api]), or, if this " +
                    "genuinely is a historical before/after sample, move it under a heading this script " +
                    "recognizes as historical ($historicalHeadingPattern).")
            }
        }
    }

    if ($inFence -or $inPre) {
        $failures.Add(
            "${relPath}: ends with an unbalanced code block (an odd number of fence delimiters, or " +
            "an unclosed <pre>). This script cannot reliably tell code from prose for the rest of " +
            "the file once that happens -- fix the unbalanced delimiter rather than trust this check's " +
            "verdict on whatever comes after it.")
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
