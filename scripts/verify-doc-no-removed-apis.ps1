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
    An unbalanced fence or unclosed `<pre>` (an odd number of delimiters), or a historical heading
    still open at end of file (no later heading at the same or a shallower level closed it), is a
    hard failure in its own right, not a silent parity flip or exemption for the remainder of the
    file: this script cannot tell code from prose (or "still historical" from "current again") past
    that point, so it says so rather than guessing.

    A fence only closes on a run of the same character (backtick or tilde) at least as long as the
    one that opened it -- CommonMark's own rule, tracked here rather than toggling on any
    fence-looking line, so a literal ``` shown as content inside an outer ~~~~-fenced block does not
    flip the state early. A fenced sample nested inside a `>` blockquote is recognized as a fence
    too, the same way CommonMark itself renders one. A removed API's name split across a hard-wrapped
    line boundary inside a code sample is also caught, by checking the join of each code line with
    the one before it in addition to each line alone.

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

.PARAMETER SelfTest
    Plant each bypass this check was measured to have into a throwaway document, run the check
    against it, and assert both its exit code and the failure message it gives -- plus the
    exemptions it must keep honouring, and assert those pass. Touches nothing outside a temporary
    directory.

.NOTES
    Vacuity floor: $currentUsageDocs is filtered through Where-Object { Test-Path ... }, so a
    renamed or deleted README.md (this repository's only current-usage doc today) makes that list
    empty. Demonstrated: pointing -RepoRoot at a directory with no README.md at all makes this
    script print "Checked 0 current-usage documentation file(s)" and exit 0 -- PASS, having
    scanned nothing. This matters more here than for most gates in this repository, because it
    runs on ubuntu-latest against a repo whose own path handling is otherwise Windows-flavored
    elsewhere: a README.md renamed to Readme.md would be invisible to this script's exact-case
    'README.md' lookup on that case-sensitive filesystem, and nothing would report it. The
    $checkedFiles -gt 0 guard below closes that: this script only ever passes having actually
    scanned at least one file.
#>
[CmdletBinding()]
param(
    [string] $RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [switch] $SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Symbol name -> what replaced it, for the failure message. Extend this table, not the logic
# below, when a future release removes another public API that documentation could still be
# teaching. A reflection diff of the shipped 2.8.0.2 package against the current build, re-derived
# directly (a MetadataLoadContext comparison of every publicly-reachable member -- public, plus
# protected/protected internal, since those are just as visible to a consumer who subclasses
# SwissEph as a fully public member is -- excluding the enum's compiler-generated `value__`
# backing field), surfaces 25 removed entries across FOUR top-level names, not the three this
# table used to list: OnLoadFile, LoadFileEventArgs and TypeCode were here; `SwissEph.LoadFile`
# (protected internal in 2.8.0.2, gone entirely from the current build, not merely narrowed) was
# missing until this check was added.
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
    # `SwissEph.LoadFile(string)` was `protected internal` in 2.8.0.2 -- reachable from a subclass
    # in any assembly, so a "here is how to override file loading" sample could genuinely call it
    # -- and no longer exists at all in the current build. Unlike the three entries above, its
    # bare name collides with a real, unrelated BCL API a legitimate sample might call:
    # `System.Reflection.Assembly.LoadFile(path)`. The default `\b<name>\b` matching every other
    # entry uses would flag that call too, so this entry gets its own pattern below instead of the
    # default one: `\bLoadFile\b` still fires on a bare or `this.`/`base.`/`swissEph.`-qualified
    # call (SwissEph's own method was never callable through the BCL type's own qualifier), but
    # not when the name is directly preceded by "Assembly." -- the one concrete collision a code
    # sample in this README could plausibly contain.
    'LoadFile'          = 'no replacement -- SwissEph no longer opens ephemeris files through an overridable method; provide an IEphemerisFileProvider instead'
}

# Per-API override for the match pattern used below, keyed by the same name as $RemovedApis.
# Every entry not listed here uses the default `\b<name>\b`; only entries whose bare name
# collides with an unrelated, legitimate API need a narrower pattern.
$RemovedApiPatterns = @{
    'LoadFile' = '(?<!Assembly\.)\bLoadFile\b'
}

# Headings that open a historical/migration region. Matched against heading text case-insensitively;
# a heading's own subsections (deeper '#' nesting) inherit the region until a heading at the same
# or shallower level appears that does not itself match.
$historicalHeadingPattern = '(?i)\b(Breaking changes|Upgrading from|Migration|Change ?log|History|Release notes)\b'

function Invoke-RemovedApiScan {
    # The whole check, in a function so -SelfTest below can drive it against a throwaway document
    # tree instead of this checkout. The `exit` statements inside are unchanged and still terminate
    # the script, so the exit codes a caller (and CI) sees are exactly what they were.
    param([Parameter(Mandatory)][string] $RepoRoot)

    # Kept separately from $currentUsageDocs (below) so the vacuity-floor error message at the bottom
    # of this script can name what it looked for even when every one of those paths turned out not to
    # exist -- the filtered list itself would just be empty at that point.
    $docCandidateNames = @('README.md')
    $currentUsageDocs = $docCandidateNames | ForEach-Object { Join-Path $RepoRoot $_ } |
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
        $fenceChar = $null   # backtick or tilde that opened the current fence; $null when $inFence is false
        $fenceLen = 0        # length of the run that opened it
        $inPre = $false
        $prevCodeLine = $null   # the immediately preceding CODE line's own text, for the split-name check below

        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            $lineNumber = $i + 1

            # Fenced code blocks (``` or ~~~) are tested before the heading test, not after: a shell,
            # yaml or python comment ("# Migration steps for the CLI") inside a fenced sample used to
            # parse as a markdown heading and open a historical region for the rest of the document,
            # exempting everything after it -- including a second, unrelated fenced sample later in
            # the same file. A line that opens or closes a fence is delimiter syntax, not code content
            # to scan, so it still just toggles state and moves on.
            #
            # The delimiter's character AND length are tracked, not just "was a fence-looking line
            # seen" -- CommonMark's own rule is that a fence only closes on a run of the SAME
            # character, at least as long as the one that opened it. Measured: a naive toggle-on-any-
            # fence-looking-line (the previous version of this check) mistakes a literal ``` shown as
            # *content* inside an outer ~~~~-fenced block for a close, flipping $inFence false right
            # before the line that actually needed scanning. `(?:>\s?)*` also tolerates one or more
            # leading blockquote markers, so a fenced sample quoted inside a `>` blockquote -- which
            # CommonMark itself renders as a real, nested fenced code block -- is recognized as a
            # fence at all; without it, a blockquoted removed-API sample was never classified as code
            # in the first place, so the removed-API scan below never got the chance to see it, and it
            # silently passed no matter what it contained.
            if ($inFence) {
                if ($line -match '^(?:>\s?)*\s{0,3}(`{3,}|~{3,})\s*$' -and
                    $Matches[1][0] -eq $fenceChar -and $Matches[1].Length -ge $fenceLen) {
                    $inFence = $false
                    $fenceChar = $null
                    $fenceLen = 0
                    $prevCodeLine = $null
                    continue
                }
                # Else: a fence-looking line of the wrong character, too short, or with trailing
                # content after it -- not a close, so it is fence *content* and falls through to be
                # scanned as code below, same as any other line inside the fence.
            }
            elseif ($line -match '^(?:>\s?)*\s{0,3}(`{3,}|~{3,})') {
                $fenceChar = $Matches[1][0]
                $fenceLen = $Matches[1].Length
                $inFence = $true
                $prevCodeLine = $null
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
            if (-not $isCodeLine -or $inHistorical) {
                $prevCodeLine = $null
                continue
            }

            foreach ($api in $RemovedApis.Keys) {
                $pattern = if ($RemovedApiPatterns.ContainsKey($api)) { $RemovedApiPatterns[$api] } else { "\b$([regex]::Escape($api))\b" }
                if ($line -match $pattern) {
                    $failures.Add(
                        "${relPath}:${lineNumber}: code sample outside any historical section names " +
                        "'$api', which no longer exists. Replace it with $($RemovedApis[$api]), or, if this " +
                        "genuinely is a historical before/after sample, move it under a heading this script " +
                        "recognizes as historical ($historicalHeadingPattern).")
                }
                # An identifier split across a line break (rare, but a real code sample can wrap one)
                # never matches on either line alone. Checked as a second pass against the immediately
                # preceding code line's text concatenated directly with this one -- exactly how the
                # two lines join once whatever hard-wrapped them is undone -- and only reported when
                # that combination matches but this line alone did not, so an ordinary same-line match
                # (already reported above) is never double-counted.
                elseif ($prevCodeLine -and "$prevCodeLine$line" -match $pattern) {
                    $failures.Add(
                        "${relPath}:$($lineNumber - 1)-${lineNumber}: '$api', which no longer exists, appears to be " +
                        "split across these two lines of a code sample outside any historical section. Replace it " +
                        "with $($RemovedApis[$api]), or move the sample under a heading this script recognizes as " +
                        "historical ($historicalHeadingPattern).")
                }
            }
            $prevCodeLine = $line
        }

        if ($inFence -or $inPre) {
            $failures.Add(
                "${relPath}: ends with an unbalanced code block (an odd number of fence delimiters, or " +
                "an unclosed <pre>). This script cannot reliably tell code from prose for the rest of " +
                "the file once that happens -- fix the unbalanced delimiter rather than trust this check's " +
                "verdict on whatever comes after it.")
        }

        if ($inHistorical) {
            $failures.Add(
                "${relPath}: a historical/migration heading is still open at end of file (no later heading at " +
                "the same or a shallower level closed it). Everything from that heading to end of file was " +
                "exempted from the removed-API scan on that basis alone -- the same 'cannot reliably tell past " +
                "this point' reasoning as an unbalanced fence above. Add a closing heading (any heading at that " +
                "level or shallower whose text does not itself match $historicalHeadingPattern), or restructure " +
                "so the historical section is not left open at end of file.")
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

    # Vacuity floor: a PASS with zero files checked is not a pass, it is this check silently doing
    # nothing -- see the .NOTES section above for how that happens ($currentUsageDocs' Test-Path
    # filter finding none of the paths it looked for) and why it matters specifically on this
    # script's ubuntu-latest runner. Every other gate in this repository that can legitimately find
    # nothing to check (verify-freeze-log.ps1, verify-baseline-log.ps1) still requires the *files it
    # is comparing* to exist; this is the one check the review found with no equivalent floor.
    if ($checkedFiles -eq 0) {
        Write-Error @"
Checked zero current-usage documentation file(s) -- none of $($docCandidateNames -join ', ') exist
at $RepoRoot. A run that scanned nothing is not a
pass: on this script's own case-sensitive runner (ubuntu-latest), a README.md silently renamed to
something else -- Readme.md, readme.md -- would produce exactly this state and this check would
report PASS having verified nothing. If README.md was deliberately renamed or removed, update the
`$currentUsageDocs list above (SwissEphNet.csproj's PackageReadmeFile is the source of truth for
what actually ships); if not, restore it.
"@
        exit 1
    }

    Write-Host 'PASS: no current-usage documentation instructs a reader to use a removed API.'
    exit 0
}

# ---------------------------------------------------------------------------------------------

if (-not $SelfTest) {
    Invoke-RemovedApiScan -RepoRoot $RepoRoot
    # Unreachable: Invoke-RemovedApiScan always exits. Present so that a future edit which turns one
    # of those exits into a return cannot silently make this script pass by falling off the end.
    exit 1
}

# ---------------------------------------------------------------------------------------------
# Self-test. Nothing covered this check before, and it was bypassed twice during review; each case
# below is a document that was planted, run, and SEEN to produce the stated exit code, not a case
# that has only ever been green. Cases 1-8 must fail; 9-12 must pass, and are here because an
# over-eager future tightening is just as much a defect as the bypasses above.

$failures = 0
$pwshExe = (Get-Process -Id $PID).Path
$root = Join-Path ([System.IO.Path]::GetTempPath()) ("doc-removed-apis-selftest-" + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null

function New-DocLab {
    # A throwaway repo root holding one README.md -- the only current-usage document this check
    # scans. $Readme left empty creates the directory with no README.md at all, which is the
    # vacuity case.
    param([string] $Name, [string] $Readme = '')
    $dir = Join-Path $root $Name
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    if ($Readme -ne '') {
        [System.IO.File]::WriteAllText((Join-Path $dir 'README.md'), $Readme)
    }
    return $dir
}

function Assert-Gate {
    # Runs this script's own normal path in a CHILD process, the way CI invokes it, and asserts the
    # exit code. A child process rather than an in-process call, for two reasons: the checks under
    # test report their verdict by calling `exit`, and the vacuity floor reports its own with a
    # terminating Write-Error, which in-process would abort the self-test itself instead of being
    # observable as a code. The exit code is read straight from $LASTEXITCODE with no pipeline
    # between -- piping would make it report the last stage of the pipe instead of the gate.
    #
    # -Matching additionally requires the failure output to say what the case claims it says. This
    # script has three independent ways to fail (a removed API in a sample, an unbalanced
    # delimiter, a historical heading left open), so a plant meant to exercise one of them can very
    # easily go red through another and look like it proved something it did not.
    param(
        [string] $Case,
        [ValidateSet('fails', 'passes')][string] $Expect,
        [string] $LabRoot,
        [string] $Matching)

    $output = & $pwshExe -NoProfile -File $PSCommandPath -RepoRoot $LabRoot *>&1
    $code = $LASTEXITCODE
    $text = (@($output) -join "`n")

    $problem = $null
    if ($Expect -eq 'fails' -and $code -eq 0) { $problem = 'expected the gate to fail, got exit 0' }
    elseif ($Expect -eq 'passes' -and $code -ne 0) { $problem = "expected the gate to pass, got exit $code" }
    elseif ($Matching -and $text -notmatch $Matching) {
        $problem = "gate exited $code as expected, but for the wrong reason: nothing in its output matched /$Matching/"
    }

    if (-not $problem) {
        Write-Host ("  PASS  {0} (gate {1}, exit {2})" -f $Case, $Expect, $code)
    }
    else {
        Write-Host ("  FAIL  {0}`n          {1}" -f $Case, $problem)
        foreach ($line in @($output)) { Write-Host "            | $line" }
        $script:failures++
    }
}

# The three failure messages the cases below discriminate between.
$namesRemovedApi = "code sample outside any historical section names 'OnLoadFile'"
$splitAcrossLines = 'appears to be split across these two lines'

Write-Host 'verify-doc-no-removed-apis self-test'
Write-Host ''

# The document every plant below is grafted onto: prose naming a removed API (legitimate), a
# current sample that names none (legitimate), and a deliberate before/after sample under a
# historical heading (legitimate). It must pass on its own -- case 9.
$cleanDoc = @'
# SwissEphNet

`OnLoadFile` was removed in 2.10.3. Saying so in prose is not an instruction to call it.

## Loading files

```csharp
swe.FileProvider = new MyProvider();
```

    // an indented sample naming nothing removed
    swe.FileProvider = new MyProvider();

<pre><code>swe.FileProvider = new MyProvider();</code></pre>

## Breaking changes

### V:2.10.3

```csharp
swe.OnLoadFile += (s, e) => { };   // the old way, shown historically on purpose
```

## Building
'@

# 1. The original defect: a fenced sample outside any historical section telling a reader to
#    subscribe to an event that no longer exists. This is what shipped to the package page.
$doc = $cleanDoc -replace '## Building', @'
## Loading files, the old tutorial

```csharp
swe.OnLoadFile += (s, e) => { e.File = File.OpenRead(e.FileName); };
```

## Building
'@
Assert-Gate 'a fenced sample naming a removed API outside a historical section' 'fails' (New-DocLab 'fenced' $doc) -Matching $namesRemovedApi

# 2. The same sample shown as a 4-space indented block instead of a fenced one. Ignoring indented
#    samples entirely (the earlier behaviour) left markdown's second standard code form unchecked.
$doc = $cleanDoc -replace '## Building', @'
## Loading files, the old tutorial

    swe.OnLoadFile += (s, e) => { };

## Building
'@
Assert-Gate 'a 4-space indented sample naming a removed API' 'fails' (New-DocLab 'indented' $doc) -Matching $namesRemovedApi

# 3. Markdown's third code form: a raw <pre> block, which neither the fence tracker nor the
#    indent test sees.
$doc = $cleanDoc -replace '## Building', @'
## Loading files, the old tutorial

<pre><code>swe.OnLoadFile += (s, e) => { };</code></pre>

## Building
'@
Assert-Gate 'a raw <pre> block naming a removed API' 'fails' (New-DocLab 'pre-block' $doc) -Matching $namesRemovedApi

# 4. The heading-inside-a-fence bypass: a shell comment inside a fenced sample starts with '#'
#    exactly like a markdown heading. Testing headings before fences let that comment open a
#    historical region that exempted the REST OF THE DOCUMENT, including the unrelated offending
#    sample further down. The fence test running first is what closes it.
$doc = $cleanDoc -replace '## Building', @'
## Regenerating the corpus

```bash
# Migration steps for the CLI
swetest -p0 -b1.1.2000
```

## Loading files, the old tutorial

```csharp
swe.OnLoadFile += (s, e) => { };
```

## Building
'@
Assert-Gate 'a heading-shaped comment inside a fence does not exempt the rest of the file' 'fails' (New-DocLab 'fence-comment-heading' $doc) -Matching $namesRemovedApi

# 5. Nested fences. A literal ``` shown as CONTENT inside a longer ~~~~ fence is not a close --
#    CommonMark closes a fence only on a run of the same character at least as long as the opener.
#    A naive toggle-on-any-fence-looking-line flipped state early and left the offending line
#    classified as prose.
$doc = $cleanDoc -replace '## Building', @'
## Loading files, the old tutorial

~~~~
```
swe.OnLoadFile += (s, e) => { };
```
~~~~

## Building
'@
Assert-Gate 'a fence nested inside a longer fence of the other character' 'fails' (New-DocLab 'nested-fence' $doc) -Matching $namesRemovedApi

# 6. A fenced sample quoted inside a `>` blockquote, which CommonMark renders as a real fenced
#    code block. Without the leading-blockquote allowance the sample was never classified as code
#    at all, so the removed-API scan never got to look at it.
$doc = $cleanDoc -replace '## Building', @'
## Loading files, the old tutorial

> ```csharp
> swe.OnLoadFile += (s, e) => { };
> ```

## Building
'@
Assert-Gate 'a fenced sample inside a blockquote' 'fails' (New-DocLab 'blockquote-fence' $doc) -Matching $namesRemovedApi

# 7. The API name hard-wrapped across two lines of a code sample. It matches on neither line
#    alone; only the join of each code line with the one before it sees it.
$doc = $cleanDoc -replace '## Building', @'
## Loading files, the old tutorial

```csharp
swe.OnLoad
File += (s, e) => { };
```

## Building
'@
Assert-Gate 'a removed API name split across two lines of a sample' 'fails' (New-DocLab 'split-name' $doc) -Matching $splitAcrossLines

# 8. The vacuity case: no README.md at all. Before the $checkedFiles floor this printed
#    "Checked 0 current-usage documentation file(s)" and exited 0 -- a PASS having read nothing,
#    which is exactly what a README.md renamed to Readme.md looks like on the case-sensitive
#    runner this check actually runs on.
Assert-Gate 'a scan that finds zero documents to check' 'fails' (New-DocLab 'vacuous') -Matching 'Checked zero current-usage documentation file'

# 9. The clean document -- prose mentions, a current sample, and a historical before/after sample
#    -- must pass. Without this, every case above could be satisfied by a check that fails on
#    everything.
Assert-Gate 'the clean document (prose mention + current sample + historical sample)' 'passes' (New-DocLab 'clean' $cleanDoc)

# 10. An unbalanced fence is a hard failure in its own right, not a silent parity flip: past it
#     this script cannot tell code from prose, so it must say so rather than pass.
$doc = $cleanDoc -replace '## Building', @'
## Loading files, the old tutorial

```csharp
swe.FileProvider = new MyProvider();

## Building
'@
Assert-Gate 'an unbalanced fence' 'fails' (New-DocLab 'unbalanced-fence' $doc) -Matching 'unbalanced code block'

# 11. A historical heading still open at end of file exempted everything after it from the scan on
#     that basis alone. Same reasoning as case 10: report it rather than trust the verdict.
$doc = $cleanDoc -replace '## Building', @'
## Migration
'@
Assert-Gate 'a historical heading left open at end of file' 'fails' (New-DocLab 'open-historical' $doc) -Matching 'historical/migration heading is still open at end of file'

# 12. The one deliberate carve-out: `Assembly.LoadFile` is an unrelated BCL API a legitimate
#     current sample may genuinely call, and the default whole-word match on the removed
#     `SwissEph.LoadFile` would otherwise flag it. It must still pass.
$doc = $cleanDoc -replace '## Building', @'
## Loading a plugin

```csharp
var asm = Assembly.LoadFile(path);
```

## Building
'@
Assert-Gate 'Assembly.LoadFile in a current sample (the BCL name collision)' 'passes' (New-DocLab 'assembly-loadfile' $doc)

Write-Host ''
Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue

if ($failures -gt 0) {
    Write-Host "FAIL: $failures self-test case(s) failed."
    exit 1
}
Write-Host 'PASS: all verify-doc-no-removed-apis self-test cases passed.'
exit 0
