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
// exception). String and integer fields must match exactly.
//
// A row whose case id matches a glob in waivers.tsv is reported separately and never
// fails the run BY ITSELF -- but the run still fails if any waiver matched zero rows,
// every row it matched was byte-for-byte identical (both are the stale-waiver case:
// the waiver is not earning its keep), or an area's waived fraction exceeds 5% (a
// waiver list that big is hiding a real problem, not documenting a handful of known
// ones). A waiver can never excuse a missing or added row, only a value difference on
// a row both sides agree exists.
//
// Usage: BaselineVerify [baseline-directory]
// If omitted, the baseline directory is discovered by walking up from this
// assembly's location to find SwissEphNet.sln, then Tests/baseline under that.

using BaselineMatrix;
using BaselineVerify;

const double MaxWaivedFraction = 0.05;

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
WarnIfModuleVersionMatchesSidecar(baselineDir);
Console.WriteLine();

var overallExitCode = 0;
var header = $"{"STATUS",-6} {"AREA",-14} {"TOTAL",7} {"LOCAL-LN",8} {"REF-LN",7} {"EXACT",7} {"TOL-OK",7} {"FAIL",6} {"WAIVED",7} {"ONLY-LOCAL",10} {"ONLY-REF",9}";
Console.WriteLine(header);
Console.WriteLine(new string('-', header.Length));

foreach (var (name, populate) in Areas.All)
{
    var localRows = Areas.Generate(populate);
    var baselinePath = Path.Combine(baselineDir, $"baseline-{name}.tsv");

    if (!File.Exists(baselinePath))
    {
        Console.WriteLine($"{"FAIL",-6} {name,-14} -- no committed baseline file at {baselinePath}");
        overallExitCode = 1;
        continue;
    }

    var referenceRows = File.ReadAllLines(baselinePath);
    var result = Comparer.Compare(localRows, referenceRows, waivers, waiverStats, name);
    var waivedFractionTooHigh = !double.IsNaN(result.WaivedFraction) && result.WaivedFraction > MaxWaivedFraction;
    var areaFailed = result.Fail > 0 || result.OnlyLocal > 0 || result.OnlyReference > 0 || waivedFractionTooHigh;
    var status = areaFailed ? "FAIL" : "PASS";

    Console.WriteLine(
        $"{status,-6} {name,-14} {result.Total,7} {result.LocalLineCount,8} {result.ReferenceLineCount,7} {result.Exact,7} {result.ToleranceOk,7} {result.Fail,6} {result.Waived,7} {result.OnlyLocal,10} {result.OnlyReference,9}");

    if (waivedFractionTooHigh)
    {
        overallExitCode = 1;
        Console.WriteLine(
            $"    FAIL {name}: waived fraction {result.WaivedFraction:P1} exceeds the {MaxWaivedFraction:P0} cap ({result.Waived} of {result.Total} rows waived)");
    }

    if (areaFailed)
    {
        overallExitCode = 1;
        foreach (var detail in result.FailureDetails.Take(50))
        {
            Console.WriteLine($"    FAIL {detail}");
        }
        if (result.FailureDetails.Count > 50)
        {
            Console.WriteLine($"    ... and {result.FailureDetails.Count - 50} more in {name}");
        }
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
    Console.WriteLine($"  {waiver.Glob} -> {stats.Matched} matched, {stats.Differed} differed  (PR {waiver.PrNumber}: {waiver.Reason})");

    if (stats.Matched == 0)
    {
        overallExitCode = 1;
        Console.WriteLine($"    FAIL stale waiver: \"{waiver.Glob}\" matched zero rows. Remove it.");
    }
    else if (stats.Differed == 0)
    {
        overallExitCode = 1;
        Console.WriteLine($"    FAIL stale waiver: \"{waiver.Glob}\" matched {stats.Matched} row(s) but every one was byte-for-byte identical. Remove it.");
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

static void WarnIfModuleVersionMatchesSidecar(string baselineDir)
{
    var sidecarPath = Path.Combine(baselineDir, EnvInfo.SidecarFileName);
    if (!File.Exists(sidecarPath))
    {
        return;
    }

    var committedMvid = EnvInfo.ParseModuleVersionId(File.ReadAllText(sidecarPath));
    var currentMvid = EnvInfo.CurrentModuleVersionId();
    Console.WriteLine($"Current SwissEph ModuleVersionId: {currentMvid:D}");

    if (committedMvid == currentMvid)
    {
        Console.WriteLine(
            $"WARNING: current ModuleVersionId matches the one recorded in {sidecarPath} " +
            "(from the reference-mode generation run). Local mode should be compiling a different " +
            "assembly than the reference package; if this is unexpected, verify BaselineVerify did " +
            "not build against UseReferencePackage=true.");
    }
}
