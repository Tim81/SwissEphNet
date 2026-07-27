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
// A row whose case id matches a glob in waivers.tsv is reported separately and never
// fails the run BY ITSELF -- but the run still fails if any waiver matched zero rows,
// every row it matched passed on its own (exact or within tolerance, so the waiver
// never actually excused a failure), an area's waived-failures fraction exceeds 5%, or
// an area's waiver match breadth (rows touched at all, regardless of outcome) exceeds
// 5%. All of the PASS/FAIL policy above lives in Verdict.cs, not here, specifically so
// it can be unit tested. A waiver can never excuse a missing or added row, only a
// value difference on a row both sides agree exists.
//
// Usage: BaselineVerify [baseline-directory]
// If omitted, the baseline directory is discovered by walking up from this
// assembly's location to find SwissEphNet.sln, then Tests/baseline under that.

using BaselineMatrix;
using BaselineVerify;

var baselineDir = args.Length > 0 ? Path.GetFullPath(args[0]) : DiscoverBaselineDir();
if (!Directory.Exists(baselineDir))
{
    Console.Error.WriteLine($"Baseline directory not found: {baselineDir}");
    return 2;
}

var waiversPath = Path.Combine(AppContext.BaseDirectory, "waivers.tsv");
var waivers = Waivers.Load(waiversPath);
var waiverStats = Waivers.InitStats(waivers);

Console.WriteLine(EnvInfo.Describe());
Console.WriteLine($"Baseline directory: {baselineDir}");
Console.WriteLine($"Waivers file: {waiversPath} ({waivers.Count} entries)");

var overallExitCode = 0;

if (CheckAssemblyIdentity(baselineDir))
{
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
        var result = Comparer.Compare(localRows, referenceRows, waivers, waiverStats, name);
        var verdict = Verdict.ForArea(result);

        Console.WriteLine(
            $"{(verdict.Passed ? "PASS" : "FAIL"),-6} {name,-14} {result.Total,7} {result.LocalLineCount,8} {result.ReferenceLineCount,7} " +
            $"{result.Exact,7} {result.ToleranceOk,7} {result.Fail,6} {result.Waived,7} {result.OnlyLocal,10} {result.OnlyReference,9}");

        if (!verdict.Passed)
        {
            overallExitCode = 1;
            foreach (var reason in verdict.Reasons)
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

Console.WriteLine();
Console.WriteLine(overallExitCode == 0 ? "PASS" : "FAIL");
return overallExitCode;

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

/// <summary>Reads the sidecar (if any), delegates the actual decision to Verdict.CheckAssemblyIdentity, prints the outcome, and returns whether it should fail the run.</summary>
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
