// Compares the current in-repo SwissEphNet code (local mode, always -- BaselineVerify's
// build fails outright if UseReferencePackage=true, see BaselineVerify.csproj) against
// the committed golden files under Tests/baseline/. This is the "prove no numbers
// changed" step; running BaselineGen by hand and eyeballing the output does not count
// as verification.
//
// Numeric fields are compared with an epsilon that combines a relative and an
// absolute tolerance, since CPort calls Math.Sin/Cos/Tan/Pow/Asin/Acos/Atan/Atan2/
// Log/Exp hundreds of times and .NET does not guarantee bit-identical transcendental
// results across OS, architecture or runtime version (Math.Sqrt is the one
// exception). Every field that parses as a number -- including plain integers like
// return codes and iflag values -- goes through that same numeric comparison; only
// fields that do not parse as a number (strings, the EXCEPTION marker, serr text)
// require an exact string match. In practice this makes no difference for integers,
// since a real difference between them is always many orders of magnitude past the
// tolerance, but the numeric path is what actually runs for them.
//
// A row whose case id matches a glob in Tests/baseline/waivers.tsv is reported separately and never
// fails the run BY ITSELF -- but the run still fails if any waiver matched zero rows,
// every row it matched passed on its own (exact or within tolerance, so the waiver
// never actually excused a failure), an area's waived-failures fraction exceeds 5%, or
// an area's waiver match breadth (rows touched at all, regardless of outcome) exceeds
// 5%. All of the PASS/FAIL policy above lives in Verdict.cs, not here, specifically so
// it can be unit tested. A waiver can never excuse a missing or added row, only a
// value difference on a row both sides agree exists.
//
// The baseline was generated on Windows and the gate is deliberately locked to
// Windows -- see Tools/BaselineGen/README.md for the measured cross-platform
// divergence that makes a single tolerance loose enough to pass everywhere
// unacceptable (it would also hide real regressions). --report-only runs the same
// comparison but never fails: it exists purely to track cross-platform drift over
// time (see the non-blocking ubuntu-latest job in .github/workflows/baseline.yml),
// printing a divergence distribution instead of a PASS/FAIL verdict, and always
// exiting 0.
//
// Usage: BaselineVerify [--report-only] [--dump-failures <path>] [baseline-directory]
//        BaselineVerify --diff-scope <old-baseline-dir> <new-baseline-dir> --expected-scope <glob> [<glob> ...]
//        BaselineVerify --list-prefixes
// If the directory is omitted, it is discovered by walking up from this assembly's
// location to find SwissEphNet.sln, then Tests/baseline under that.
//
// --diff-scope is a wholly separate mode used by scripts/regenerate-baseline.ps1's
// -ExpectedScope gate (see RunDiffScopeMode below): it never runs the matrix at all,
// it only diffs two already-generated baseline directories against each other by
// case id and checks every changed/added/removed one against the given globs. It also
// prints, on SCOPE-OK, the case-id prefixes present in the rows just regenerated (PrefixMap.cs)
// -- the same information --list-prefixes reports standalone, useful for confirming an
// -ExpectedScope glob's leading segment actually matches something.
//
// --list-prefixes runs the matrix and prints, per area, every distinct case-id prefix it
// produces (see PrefixMap.cs and Tools/BaselineGen/README.md's "Case id prefixes by area").
// It exists so that mapping can be looked up before writing an -ExpectedScope glob, rather
// than only being discoverable after the fact via a SCOPE-VIOLATION's OFFENDER lines.

using System.Security.Cryptography;
using BaselineMatrix;
using BaselineVerify;

// All argv parsing (both modes, including the --dump-failures/positional-argument index math
// that used to silently drop the baseline-directory argument whenever --dump-failures was
// absent -- see CliTests) lives in Cli.Parse, a pure function unit-tested directly in
// BaselineVerify.Tests without spinning up this process. Everything below this point is
// orchestration: turn a parsed request into actual directory resolution and I/O, exactly the
// rule Verdict.cs's own doc comment states for Program.cs.
var parsed = Cli.Parse(args);
if (parsed.IsError)
{
    Console.Error.WriteLine(parsed.Error);
    return 2;
}

if (parsed.IsListPrefixes)
{
    return RunListPrefixesMode();
}

if (parsed.IsDiffScope)
{
    var request = parsed.DiffScope!;
    return RunDiffScopeMode(Path.GetFullPath(request.OldDir), Path.GetFullPath(request.NewDir), request.Globs);
}

var verifyRequest = parsed.Verify!;
var reportOnly = verifyRequest.ReportOnly;
var dumpFailuresPath = verifyRequest.DumpFailuresPath;

var baselineDir = verifyRequest.BaselineDir is not null ? Path.GetFullPath(verifyRequest.BaselineDir) : DiscoverBaselineDir();
if (!Directory.Exists(baselineDir))
{
    Console.Error.WriteLine($"Baseline directory not found: {baselineDir}");
    return 2;
}

if (reportOnly)
{
    return RunReportMode(baselineDir);
}

var waiversPath = Path.Combine(AppContext.BaseDirectory, "waivers.tsv");
var waivers = Waivers.Load(waiversPath);
var waiverStats = Waivers.InitStats(waivers);

var rowCountsPath = Path.Combine(baselineDir, RowCounts.FileName);
var rowCounts = RowCounts.Load(rowCountsPath);

Console.WriteLine(EnvInfo.Describe());
Console.WriteLine($"Baseline directory: {baselineDir}");
Console.WriteLine($"Waivers file: {waiversPath} ({waivers.Count} entries)");
Console.WriteLine($"Row-counts file: {rowCountsPath} ({rowCounts.Count} entries)");

var overallExitCode = 0;
var fullFieldDump = dumpFailuresPath is not null ? new List<string>() : null;

if (CheckAssemblyIdentity(baselineDir))
{
    overallExitCode = 1;
}

Console.WriteLine();
var presentFileNames = Directory.EnumerateFiles(baselineDir).Select(Path.GetFileName).OfType<string>();
var orphanedBaselineFiles = Verdict.FindOrphanedBaselineFiles(presentFileNames, Areas.All.Select(static a => a.Name));
foreach (var orphan in orphanedBaselineFiles)
{
    var orphanPath = Path.Combine(baselineDir, orphan);
    var verdict = Verdict.OrphanedBaselineFile(orphanPath);
    Console.WriteLine($"{"FAIL",-6} {orphan,-14} -- {verdict.Reasons[0]}");
    overallExitCode = 1;
}

var orphanedRowCountEntries = Verdict.FindOrphanedRowCountEntries(rowCounts.Keys, Areas.All.Select(static a => a.Name));
foreach (var orphan in orphanedRowCountEntries)
{
    var verdict = Verdict.OrphanedRowCountEntry(orphan);
    Console.WriteLine($"{"FAIL",-6} {orphan,-14} -- {verdict.Reasons[0]}");
    overallExitCode = 1;
}

Console.WriteLine();
var header = $"{"STATUS",-6} {"AREA",-14} {"TOTAL",7} {"LOCAL-LN",8} {"REF-LN",7} {"EXACT",7} {"TOL-OK",7} {"FAIL",6} {"WAIVED",7} {"ONLY-LOCAL",10} {"ONLY-REF",9}";
Console.WriteLine(header);
Console.WriteLine(new string('-', header.Length));

foreach (var (name, populate) in Areas.All)
{
    var baselinePath = Path.Combine(baselineDir, $"baseline-{name}.tsv");
    try
    {
        if (!File.Exists(baselinePath))
        {
            var missing = Verdict.MissingBaselineFile(baselinePath);
            Console.WriteLine($"{"FAIL",-6} {name,-14} -- {missing.Reasons[0]}");
            overallExitCode = 1;
            continue;
        }

        var localRows = Areas.Generate(populate);
        var referenceRows = File.ReadAllLines(baselinePath);
        var result = Comparer.Compare(localRows, referenceRows, waivers, waiverStats, name, fullFieldDump);
        var verdict = Verdict.ForArea(result);

        // Row-count check is independent of, and additional to, Verdict.ForArea:
        // an area can have zero FAIL/ONLY-LOCAL/ONLY-REFERENCE rows and still have
        // silently lost coverage if both sides shrank together (a narrowed generator
        // paired with a matching regeneration). See RowCounts.cs.
        var rowCountVerdict = rowCounts.TryGetValue(name, out var expectedCount)
            ? (result.Total == expectedCount ? AreaVerdict.Pass() : Verdict.RowCountMismatch(name, expectedCount, result.Total))
            : Verdict.MissingRowCountEntry(name);

        var passed = verdict.Passed && rowCountVerdict.Passed;
        Console.WriteLine(
            $"{(passed ? "PASS" : "FAIL"),-6} {name,-14} {result.Total,7} {result.LocalLineCount,8} {result.ReferenceLineCount,7} " +
            $"{result.Exact,7} {result.ToleranceOk,7} {result.Fail,6} {result.Waived,7} {result.OnlyLocal,10} {result.OnlyReference,9}");

        if (!passed)
        {
            overallExitCode = 1;
            foreach (var reason in verdict.Reasons)
            {
                Console.WriteLine($"    FAIL {name}: {reason}");
            }
            foreach (var reason in rowCountVerdict.Reasons)
            {
                Console.WriteLine($"    FAIL {name}: {reason}");
            }
            foreach (var detail in result.FailureDetails.Take(50))
            {
                Console.WriteLine($"    FAIL {detail}");
            }
            if (result.FailureDetails.Count > 50)
            {
                Console.WriteLine($"    ... and {result.FailureDetails.Count - 50} more FAIL rows in {name}");
            }
        }

        foreach (var detail in result.WaivedDetails.Take(20))
        {
            Console.WriteLine($"    WAIVED {detail}");
        }
        if (result.WaivedDetails.Count > 20)
        {
            Console.WriteLine($"    ... and {result.WaivedDetails.Count - 20} more WAIVED rows in {name}");
        }
    }
    catch (Exception ex)
    {
        // Deliberately caught here rather than left to crash the process: one bad
        // area (e.g. a duplicate case id) should not prevent every other area from
        // being checked and reported.
        overallExitCode = 1;
        Console.WriteLine($"{"ERROR",-6} {name,-14} -- {ex.GetType().Name}: {ex.Message}");
    }
}

Console.WriteLine();
Console.WriteLine("Waiver usage:");
if (waivers.Count == 0)
{
    Console.WriteLine("  (none)");
}
foreach (var waiver in waivers)
{
    var stats = waiverStats[waiver];
    Console.WriteLine($"  {waiver.Glob} -> {stats.Matched} matched, {stats.Waived} waived  (PR {waiver.PrNumber}: {waiver.Reason})");

    var waiverVerdict = Verdict.ForWaiver(waiver, stats);
    if (waiverVerdict.Stale)
    {
        overallExitCode = 1;
        Console.WriteLine($"    FAIL stale waiver: {waiverVerdict.Reason}");
    }
}

if (dumpFailuresPath is not null)
{
    File.WriteAllLines(dumpFailuresPath, fullFieldDump!);
    Console.WriteLine();
    Console.WriteLine($"Dumped {fullFieldDump!.Count} full field-difference line(s) (every non-exact row, every differing field, not just the first) to {dumpFailuresPath}.");
}

Console.WriteLine();
Console.WriteLine(overallExitCode == 0 ? "PASS" : "FAIL");
return overallExitCode;

// scripts/regenerate-baseline.ps1's -ExpectedScope gate. Diffs every area's TSV between
// two already-generated baseline directories (old = currently committed, new = the fresh
// run about to replace it) by case id, and refuses (nonzero exit) if any added, removed, or
// changed case id in any area fails to match at least one of the given globs. Never runs the
// matrix itself -- both directories are assumed already generated (by BaselineGen, via the
// calling script).
//
// Reuses Waivers.CompileGlob for the same anti-bypass rules a waiver glob must satisfy (no
// catch-all, no wildcard before the first '|', no match against the synthetic probe ids) --
// see that method's doc comment for why a second implementation was rejected.
//
// Not a `///` doc comment: a local function inside a top-level-statements file is not a
// documentable member as far as the compiler is concerned, and `///` there is CS1587 ("XML
// comment is not placed on a valid language element"). A plain `//` block says the same thing
// without the warning.
static int RunDiffScopeMode(string oldDir, string newDir, string[] globs)
{
    List<(string Glob, System.Text.RegularExpressions.Regex Pattern)> compiled;
    try
    {
        compiled = globs.Select(g => (g, Waivers.CompileGlob(g, "--expected-scope", "-ExpectedScope glob"))).ToList();
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 2;
    }

    var offenders = new List<string>();
    var summaries = new List<string>();
    var sha256Lines = new List<string>();
    var rowCountLines = new List<string>();
    var newRowPrefixesByArea = new List<(string Name, IReadOnlyList<string> Prefixes)>();

    // Area names come from the baseline-*.tsv files actually present on disk, in either
    // directory, not from Areas.All. An area removed from Areas.All (BaselineMatrix no
    // longer generates it) still has a committed baseline-<name>.tsv sitting in oldDir --
    // trusting Areas.All here would skip that file entirely, so its rows would never be
    // classified as "removed" and the deletion would pass with SCOPE-OK's "no areas
    // changed". Reading both directories' filenames catches that: a name present only in
    // oldDir still gets diffed, with newRows empty, so ScopeDiff.ComputeArea reports every
    // one of its case ids as removed and -ExpectedScope has to cover them explicitly.
    var areaNames = new SortedSet<string>(StringComparer.Ordinal);
    areaNames.UnionWith(AreaNamesFromBaselineFiles(oldDir));
    areaNames.UnionWith(AreaNamesFromBaselineFiles(newDir));

    foreach (var name in areaNames)
    {
        var oldPath = Path.Combine(oldDir, $"baseline-{name}.tsv");
        var newPath = Path.Combine(newDir, $"baseline-{name}.tsv");
        var oldRows = File.Exists(oldPath) ? File.ReadAllLines(oldPath) : [];
        var newRows = File.Exists(newPath) ? File.ReadAllLines(newPath) : [];

        var areaResult = ScopeDiff.ComputeArea(name, oldRows, newRows, compiled);
        offenders.AddRange(areaResult.Offenders);

        if (areaResult.Changed + areaResult.Added + areaResult.Removed > 0)
        {
            // The percentage is the magnitude signal -ExpectedScope's per-case-id guarantee does
            // not itself provide (see the doc comment on AreaResult.TouchedFraction and
            // Tools/BaselineGen/README.md's "-ExpectedScope: proving the diff, not just
            // describing it"): every changed/added/removed id here is provably covered by a
            // glob, but a single glob can cover a large fraction of the area, and this is where
            // a reviewer sees how large. Deliberately not a pass/fail threshold -- see this
            // area's own reasoning in the README for why a hard cap here would be a knob nobody
            // could keep set correctly for legitimate, often area-wide porting changes.
            summaries.Add(
                $"{name}: {areaResult.Changed:N0} changed / {areaResult.Added:N0} added / {areaResult.Removed:N0} removed " +
                $"({areaResult.TouchedFraction:P1} of the area's {areaResult.UnionRowCount:N0} case ids)");
        }

        // An area present only in oldDir (deleted outright, see the areaNames comment above)
        // has no newPath at all: PREFIX/SHA256/ROWCOUNT all describe the rows just
        // regenerated, and there are none to describe here. Emitting any of the three for
        // such an area would be actively wrong, not just uninformative -- an empty "PREFIX
        // <name>: " line, and worse, a "ROWCOUNT <name> 0" line that
        // scripts/regenerate-baseline.ps1 would then write into row-counts.tsv for an area no
        // longer in Areas.All, which the very next verify run rejects as an orphaned entry
        // row-counts.tsv's own header says must never be hand-edited to remove.
        if (File.Exists(newPath))
        {
            newRowPrefixesByArea.Add((name, PrefixMap.Discover(newRows)));

            using var stream = File.OpenRead(newPath);
            var sha = Convert.ToHexString(SHA256.HashData(stream));
            sha256Lines.Add($"{name}\t{sha}");

            rowCountLines.Add($"{name}\t{areaResult.NewRowCount}");
        }
    }

    if (offenders.Count > 0)
    {
        Console.WriteLine("SCOPE-VIOLATION");
        Console.WriteLine($"{offenders.Count} case id(s) changed, added, or removed outside -ExpectedScope ({string.Join(", ", globs)}):");
        foreach (var offender in offenders)
        {
            var fields = offender.Split('\t');
            Console.WriteLine($"OFFENDER area={fields[0]} caseid={fields[1]} ({fields[2]})");
        }
        Console.WriteLine("SCOPE-FAIL");
        return 1;
    }

    Console.WriteLine("SCOPE-OK");
    if (summaries.Count == 0)
    {
        Console.WriteLine("No areas changed.");
    }
    foreach (var summary in summaries)
    {
        Console.WriteLine($"CHANGED-AREA {summary}");
    }
    foreach (var line in sha256Lines)
    {
        Console.WriteLine($"SHA256 {line}");
    }
    foreach (var line in rowCountLines)
    {
        Console.WriteLine($"ROWCOUNT {line}");
    }

    // Emits the area -> case-id-prefix mapping computed from the rows just regenerated (the
    // "new" dir), so whoever wrote -ExpectedScope for this run sees, at the moment it succeeds,
    // exactly which prefixes exist and can confirm their glob's leading segment matches one --
    // and, for every area they did NOT intend to touch, that none of its prefixes appear here
    // as something they should have scoped. This is the discoverability half of the fix; the
    // static reference table lives in Tools/BaselineGen/README.md ("Case id prefixes by area").
    foreach (var (name, prefixes) in newRowPrefixesByArea)
    {
        Console.WriteLine($"PREFIX {name}: {string.Join(", ", prefixes)}");
    }

    return 0;
}

// Every area name RunDiffScopeMode has a baseline-<name>.tsv for in the given directory,
// derived from the filenames actually present rather than from Areas.All -- see the doc
// comment where this is called. Returns nothing if the directory does not exist (the
// caller already treats a missing side as "no rows" via File.Exists on each individual
// path).
static IEnumerable<string> AreaNamesFromBaselineFiles(string dir)
{
    if (!Directory.Exists(dir))
    {
        yield break;
    }

    foreach (var path in Directory.EnumerateFiles(dir, "baseline-*.tsv"))
    {
        var fileName = Path.GetFileName(path);
        yield return fileName["baseline-".Length..^".tsv".Length];
    }
}

// Diagnostic-only pass: same matrix, same rows, no waivers, no PASS/FAIL of any
// kind. Prints how many numeric fields differ at all between local and reference,
// the relative-difference distribution across the differing ones (median/p90/p99/
// max), and per-area exact/tolerance/beyond-tolerance row counts for context.
// Always returns 0, regardless of what it finds -- including if an area throws.
// (Plain `//`, not `///`: see RunDiffScopeMode's comment above for why.)
static int RunReportMode(string baselineDir)
{
    Console.WriteLine(EnvInfo.Describe());
    Console.WriteLine($"Baseline directory: {baselineDir}");
    Console.WriteLine("Mode: REPORT ONLY -- always exits 0. This tracks cross-platform/cross-runtime drift; it is not a gate.");
    Console.WriteLine();

    var header = $"{"AREA",-14} {"FIELDS",8} {"DIFFER",8} {"BEYOND",7} {"DIFFER%",8} {"EXACT-RN",9} {"TOL-RN",7} {"FAIL-RN",8} {"MED-REL",10} {"P90-REL",10} {"P99-REL",10} {"MAX-REL",10}";
    Console.WriteLine(header);
    Console.WriteLine("(FIELDS/DIFFER/BEYOND are per numeric field; -RN columns are per row, from the same row-level comparison the gate uses)");
    Console.WriteLine(new string('-', header.Length));

    var totalFieldsCompared = 0;
    var totalFieldsDiffering = 0;
    var totalFieldsBeyondTolerance = 0;
    var allDiffs = new List<double>();

    foreach (var (name, populate) in Areas.All)
    {
        var baselinePath = Path.Combine(baselineDir, $"baseline-{name}.tsv");
        if (!File.Exists(baselinePath))
        {
            Console.WriteLine($"{name,-14} -- no committed baseline file at {baselinePath}");
            continue;
        }

        try
        {
            var localRows = Areas.Generate(populate);
            var referenceRows = File.ReadAllLines(baselinePath);

            var result = Comparer.Compare(localRows, referenceRows, [], [], name);
            var divergence = DivergenceReport.Collect(localRows, referenceRows, name);

            totalFieldsCompared += divergence.FieldsCompared;
            totalFieldsDiffering += divergence.FieldsDiffering;
            totalFieldsBeyondTolerance += divergence.FieldsBeyondTolerance;
            allDiffs.AddRange(divergence.SortedRelativeDiffs);

            var differPct = divergence.FieldsCompared > 0 ? divergence.FieldsDiffering / (double)divergence.FieldsCompared : 0;
            Console.WriteLine(
                $"{name,-14} {divergence.FieldsCompared,8} {divergence.FieldsDiffering,8} {divergence.FieldsBeyondTolerance,7} {differPct,8:P2} " +
                $"{result.Exact,9} {result.ToleranceOk,7} {result.Fail,8} " +
                $"{divergence.Median,10:E2} {divergence.P90,10:E2} {divergence.P99,10:E2} {divergence.Max,10:E2}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{name,-14} -- {ex.GetType().Name}: {ex.Message}");
        }
    }

    allDiffs.Sort();
    Console.WriteLine();
    var overallPct = totalFieldsCompared > 0 ? totalFieldsDiffering / (double)totalFieldsCompared : 0;
    Console.WriteLine(
        $"Overall: {totalFieldsCompared} numeric fields compared, {totalFieldsDiffering} differing ({overallPct:P4}), " +
        $"{totalFieldsBeyondTolerance} still beyond tolerance after the angle-wraparound allowance.");
    if (allDiffs.Count > 0)
    {
        Console.WriteLine(
            $"Overall relative-difference distribution across differing fields: " +
            $"median={DivergenceStats.Percentile(allDiffs, 50):E2} " +
            $"p90={DivergenceStats.Percentile(allDiffs, 90):E2} " +
            $"p99={DivergenceStats.Percentile(allDiffs, 99):E2} " +
            $"max={allDiffs[^1]:E2}");
    }

    Console.WriteLine();
    Console.WriteLine("Report complete.");
    return 0;
}

// Prints the area -> case-id-prefix mapping (see PrefixMap.cs), computed directly from the
// matrix's current output rather than any committed file, so this can never drift from the
// code: an area or a prefix that Areas.All can produce is what this reports, full stop. This
// is the command Tools/BaselineGen/README.md's "Case id prefixes by area" table tells a reader
// to run to refresh that table, and the one a reviewer can run standalone (no baseline
// directory, no -ExpectedScope globs) just to look up a prefix before writing one. (Plain
// `//`, not `///`: see RunDiffScopeMode's comment above for why.)
static int RunListPrefixesMode()
{
    foreach (var (name, populate) in Areas.All)
    {
        var rows = Areas.Generate(populate);
        var prefixes = PrefixMap.Discover(rows);
        Console.WriteLine($"{name}\t{string.Join(", ", prefixes)}");
    }

    return 0;
}

static string DiscoverBaselineDir()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SwissEphNet.sln")))
    {
        dir = dir.Parent;
    }

    if (dir is null)
    {
        throw new InvalidOperationException(
            "Could not find SwissEphNet.sln by walking up from " + AppContext.BaseDirectory +
            ". Pass the baseline directory explicitly.");
    }

    return Path.Combine(dir.FullName, "Tests", "baseline");
}

// Reads the sidecar (if any), delegates the actual decision to Verdict.CheckAssemblyIdentity,
// prints the outcome, and returns whether it should fail the run. (Plain `//`, not `///`: see
// RunDiffScopeMode's comment above for why.)
static bool CheckAssemblyIdentity(string baselineDir)
{
    var sidecarPath = Path.Combine(baselineDir, EnvInfo.SidecarFileName);
    string? content = File.Exists(sidecarPath) ? File.ReadAllText(sidecarPath) : null;
    var currentMvid = EnvInfo.CurrentModuleVersionId();
    var currentSha256 = EnvInfo.CurrentSha256();
    var verdict = Verdict.CheckAssemblyIdentity(content, currentMvid, currentSha256);

    Console.WriteLine($"Current SwissEph ModuleVersionId: {currentMvid:D}");
    Console.WriteLine($"Current SwissEph SHA-256: {currentSha256}");

    switch (verdict.MvidOutcome)
    {
        case MvidCheckOutcome.Skipped:
            Console.WriteLine($"Assembly-identity check SKIPPED: no sidecar found at {sidecarPath}.");
            break;
        case MvidCheckOutcome.Unparseable:
            Console.WriteLine($"Assembly-identity check SKIPPED: {sidecarPath} exists but has no parseable SwissEphModuleVersionId= line.");
            break;
        case MvidCheckOutcome.Matches:
            Console.WriteLine(
                $"FAIL assembly-identity check: current ModuleVersionId matches the one recorded in {sidecarPath} " +
                "(from the reference-mode generation run). Local mode should be compiling a different assembly " +
                "than the reference package -- verify BaselineVerify did not build with UseReferencePackage=true.");
            break;
        case MvidCheckOutcome.Differs:
            Console.WriteLine("Assembly-identity check OK: ModuleVersionId differs from the reference build, as expected for local mode.");
            break;
    }

    if (verdict.MvidOutcome is MvidCheckOutcome.Skipped or MvidCheckOutcome.Unparseable)
    {
        return verdict.IsSuspiciousMatch;
    }

    if (!verdict.Sha256Comparable)
    {
        Console.WriteLine($"SHA-256 check SKIPPED: {sidecarPath} has no parseable SwissEphAssemblySha256= line.");
    }
    else if (verdict.Sha256Matches)
    {
        Console.WriteLine(
            "FAIL assembly-identity check: current assembly SHA-256 matches the reference build's. " +
            "This should not happen for local mode -- investigate before trusting this run.");
    }
    else
    {
        Console.WriteLine("SHA-256 check OK: differs from the reference build, as expected for local mode.");
    }

    return verdict.IsSuspiciousMatch;
}
