using System.Text.RegularExpressions;
using SwissEphNet.Conformance.Tests;
using SwissEphNet.Conformance.Tests.Corpus;
using SwissEphNet.Conformance.Tests.Dispatch;
using SwissEphNet.Conformance.Tests.KnownFail;

// Never invoke this directly -- see scripts/regenerate-known-fail.ps1.
var outputPath = args.Length > 0 ? args[0] : "known-fail.tsv";

Console.WriteLine("Running the full conformance corpus (this dispatches all 12,757 iterations)...");
var startedAt = DateTime.UtcNow;
ExpDocument doc;
System.Collections.Generic.IReadOnlyList<IterationResult> results;
try
{
    (doc, results) = ConformanceRunner.Run();
}
catch (InvalidOperationException ex) when (ex.Message.Contains("does not match the declared ephemeris file set"))
{
    // EphemerisManifest.AssertMatches -- refuse outright rather than regenerate against
    // undeclared data (see that class's remarks). A clean message here, not a raw stack
    // trace: this is an expected, actionable refusal, not a bug in the generator.
    Console.Error.WriteLine();
    Console.Error.WriteLine(ex.Message);
    return 1;
}

var elapsed = DateTime.UtcNow - startedAt;

Console.WriteLine($"Done in {elapsed.TotalSeconds:F1}s. suites={doc.TestSuites.Count} testcases={doc.TotalTestCaseCount} " +
                   $"iterations={doc.TotalIterationCount} valueLines={doc.TotalValueLineCount}");

var entries = new List<KnownFailEntry>();
foreach (var r in results)
{
    if (r.Kind == OutcomeKind.Passed)
    {
        continue;
    }

    var category = FailureCategoryNames.FromOutcomeKind(r.Kind);
    var magnitudeKey = MagnitudeKey.Compute(r.Mismatches);
    var rawReason = r.Reason ?? string.Join("; ", r.Mismatches.Select(m => $"{m.Name}: expected {m.Expected}, got {m.Actual}"));
    var reason = NormalizeReason(rawReason);
    entries.Add(new KnownFailEntry(r.Key, category, magnitudeKey, reason));
}

KnownFailList.Save(outputPath, entries);
Console.WriteLine($"Wrote {entries.Count} known-fail entries to {outputPath}");

Console.WriteLine();
Console.WriteLine("By suite / category:");
foreach (var g in entries
             .GroupBy(e => (e.Key.Suite, e.Category))
             .OrderBy(g => g.Key.Suite)
             .ThenBy(g => g.Key.Category))
{
    Console.WriteLine($"  suite {g.Key.Suite,2} {FailureCategoryNames.ToName(g.Key.Category),-14} {g.Count(),5}");
}

// Emit the per-suite pass-rate table too, since a reviewer regenerating the
// file wants this in the same run, not a second full 12,757-iteration pass.
Console.WriteLine();
var knownFailByKey = entries.ToDictionary(e => e.Key);
var report = ConformanceReport.Build(results, knownFailByKey);
Console.WriteLine(report.FormatSummary());

Console.WriteLine();
Console.WriteLine(report.FormatByTestCase());

return 0;

// Strips reasons of anything that ties them to whoever's machine generated
// them: an absolute filesystem path (Windows drive-letter or POSIX-rooted)
// is never meaningful information for a checked-in known-fail row -- what
// matters is *that* a file wasn't found, not the local checkout path it was
// looked for in.
static string NormalizeReason(string reason)
{
    // Windows: C:\Users\...\thing or C:/Users/.../thing (either slash direction).
    reason = Regex.Replace(reason, @"[A-Za-z]:[\\/](?:[^\s'""]+[\\/])*[^\s'""]*", "<path>");
    // POSIX absolute paths of at least two segments (avoid mangling a bare "/" or a single "/word").
    reason = Regex.Replace(reason, @"(?<![\w.])/(?:[^\s'""/]+/)+[^\s'""/]*", "<path>");
    return reason;
}
