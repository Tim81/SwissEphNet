// Compares the current in-repo SwissEphNet code (local mode, always -- this project
// never builds against the NuGet reference package) against the committed golden
// files under Tests/baseline/. This is the "prove no numbers changed" step; running
// BaselineGen by hand and eyeballing the output does not count as verification.
//
// Numeric fields are compared with a relative epsilon (~1e-13), since CPort calls
// Math.Sin/Cos/Tan/Pow/Asin/Acos/Atan/Atan2/Log/Exp hundreds of times and .NET does
// not guarantee bit-identical transcendental results across OS, architecture or
// runtime version (Math.Sqrt is the one exception). String and integer fields must
// match exactly. A row whose case id matches a glob in waivers.tsv is reported
// separately and never fails the run.
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

Console.WriteLine(EnvInfo.Describe());
Console.WriteLine($"Baseline directory: {baselineDir}");
Console.WriteLine($"Waivers file: {waiversPath} ({waivers.Count} entries)");
Console.WriteLine();

var overallExitCode = 0;
var header = $"{"STATUS",-6} {"AREA",-14} {"TOTAL",7} {"EXACT",7} {"TOL-OK",7} {"FAIL",6} {"WAIVED",7} {"ONLY-LOCAL",10} {"ONLY-REF",9}";
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
    var result = Comparer.Compare(localRows, referenceRows, waivers);
    var areaFailed = result.Fail > 0 || result.OnlyLocal > 0 || result.OnlyReference > 0;
    var status = areaFailed ? "FAIL" : "PASS";

    Console.WriteLine(
        $"{status,-6} {name,-14} {result.Total,7} {result.Exact,7} {result.ToleranceOk,7} {result.Fail,6} {result.Waived,7} {result.OnlyLocal,10} {result.OnlyReference,9}");

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
