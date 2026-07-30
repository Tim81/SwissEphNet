// The comparer half of the bit-exact oracle harness: Tools/OracleDump and Tools/CReference/sedump.c
// (see scripts/run-oracle-dump.ps1) each replay Tools/OracleGrid/grid-analytic.tsv and write their
// raw results to external/.c-reference/dump-{c-2.10.03,net}.tsv; this reads both, keys rows by
// case_id, and checks every hex column, the integer return code, and the serr text. A row that
// does not match outright must have an entry in Tests/oracle/known-diff.tsv -- see KnownDiffList.cs
// and OracleVerifyReport.cs for the three-way check that keeps the two in sync.
//
// Never invoke this directly -- see scripts/verify-oracle.ps1 and scripts/regenerate-oracle-known-diff.ps1.
//
// Usage:
//   OracleVerify verify   <c-dump.tsv> <net-dump.tsv> <known-diff.tsv>
//   OracleVerify generate <c-dump.tsv> <net-dump.tsv> <output.tsv>

using System.Globalization;
using OracleVerify;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: OracleVerify verify|generate <c-dump.tsv> <net-dump.tsv> <known-diff.tsv|output.tsv>");
    return 2;
}

var mode = args[0];
if (args.Length != 4)
{
    Console.Error.WriteLine($"Usage: OracleVerify {mode} <c-dump.tsv> <net-dump.tsv> <path>");
    return 2;
}

var cDumpPath = args[1];
var netDumpPath = args[2];
var thirdPath = args[3];

IReadOnlyList<RowOutcome> outcomes;
try
{
    outcomes = LoadAndCompare(cDumpPath, netDumpPath);
}
catch (Exception ex) when (ex is IOException or FormatException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

switch (mode)
{
    case "verify":
        return RunVerify(outcomes, thirdPath);
    case "generate":
        return RunGenerate(outcomes, thirdPath);
    default:
        Console.Error.WriteLine($"Unknown mode '{mode}'. Expected 'verify' or 'generate'.");
        return 2;
}

// Loads both dumps and returns one RowOutcome per case_id, sorted by case_id for deterministic
// output. Fails loudly (not by returning an empty/partial result) on any of: a missing dump file,
// the two dumps disagreeing on row count, or a case_id present in only one of them -- a comparer
// that quietly compared the intersection would silently narrow coverage exactly the way
// Tools/BaselineVerify/Comparer.cs's existence checks exist to prevent.
static IReadOnlyList<RowOutcome> LoadAndCompare(string cDumpPath, string netDumpPath)
{
    var cRows = DumpFile.Load(cDumpPath);
    var netRows = DumpFile.Load(netDumpPath);

    if (cRows.Count != netRows.Count)
    {
        throw new FormatException(
            $"Dumps have different row counts: {cDumpPath} has {cRows.Count}, {netDumpPath} has {netRows.Count}. " +
            "Both sides should have been produced by the same run of scripts/run-oracle-dump.ps1 against the same grid.");
    }

    var onlyInC = cRows.Keys.Where(id => !netRows.ContainsKey(id)).ToList();
    var onlyInNet = netRows.Keys.Where(id => !cRows.ContainsKey(id)).ToList();
    if (onlyInC.Count > 0 || onlyInNet.Count > 0)
    {
        throw new FormatException(
            $"Dumps disagree on which case ids are present ({onlyInC.Count} only in {cDumpPath}, {onlyInNet.Count} only in {netDumpPath}). " +
            $"First offender: {(onlyInC.Count > 0 ? onlyInC[0] : onlyInNet[0])}.");
    }

    var outcomes = cRows
        .Select(kvp => RowComparer.Compare(kvp.Value, netRows[kvp.Key]))
        .OrderBy(o => o.CaseId, StringComparer.Ordinal)
        .ToList();

    if (outcomes.Count == 0)
    {
        throw new FormatException("Zero rows were compared -- a run that compared nothing is not a pass.");
    }

    return outcomes;
}

static int RunVerify(IReadOnlyList<RowOutcome> outcomes, string knownDiffPath)
{
    if (!File.Exists(knownDiffPath))
    {
        Console.Error.WriteLine(
            $"known-diff.tsv not found at {knownDiffPath}. Run scripts/regenerate-oracle-known-diff.ps1 to create it.");
        return 2;
    }

    IReadOnlyDictionary<string, KnownDiffEntry> knownDiff;
    try
    {
        knownDiff = KnownDiffList.Load(knownDiffPath);
    }
    catch (FormatException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 2;
    }

    var report = OracleVerifyReport.Build(outcomes, knownDiff);

    Console.WriteLine(report.FormatSummary());
    Console.WriteLine();
    Console.WriteLine(report.FormatCategoryBreakdown(knownDiff));

    Console.WriteLine(report.Passed ? "PASS" : "FAIL");
    return report.Passed ? 0 : 1;
}

static int RunGenerate(IReadOnlyList<RowOutcome> outcomes, string outputPath)
{
    var entries = outcomes
        .Where(o => !o.Matches)
        .Select(BuildEntry)
        .ToList();

    KnownDiffList.Save(outputPath, entries);
    Console.WriteLine($"Compared {outcomes.Count} total row(s).");
    Console.WriteLine($"Wrote {entries.Count} known-diff entries to {outputPath}");

    foreach (var g in entries
                 .GroupBy(e => e.Category)
                 .OrderBy(g => DiffCategoryNames.ToName(g.Key), StringComparer.Ordinal))
    {
        Console.WriteLine($"  {DiffCategoryNames.ToName(g.Key),-14} {g.Count(),6}");
    }

    return 0;
}

// A hex-only difference is always recorded as PORT-VERSION here, never LIBM-RESIDUAL -- see
// FailureShape's remarks for why this comparer cannot tell the two apart on its own. A row only
// ever moves to LIBM-RESIDUAL by a human editing known-diff.tsv after tracing it to a specific
// named C runtime function and its pinned ULP bound (scripts/verify-crt-parity.ps1), which is
// exactly the kind of deliberate, reasoned change scripts/regenerate-oracle-known-diff.ps1's full
// (non -PruneOnly) mode requires a -Reason for. Likewise, SERR is only ever assigned when it is
// the row's sole difference -- see FailureShape's remarks on shape priority.
static KnownDiffEntry BuildEntry(RowOutcome outcome)
{
    var category = outcome.Shape switch
    {
        FailureShape.RetcDiffers => DiffCategory.Retc,
        FailureShape.ErrOnlyDiffers => DiffCategory.Serr,
        _ => DiffCategory.PortVersion,
    };

    // null means "at least one field diff is categorical" -- see KnownDiffEntry.MaxUlp's remarks
    // on why that is recorded as its own state rather than as ulong.MaxValue.
    var maxUlp = outcome.HasCategoricalFieldDiff ? (ulong?)null : outcome.MaxUlp;

    return new KnownDiffEntry(outcome.CaseId, category, maxUlp, BuildReason(outcome));
}

// Kept short and deterministic on purpose: the count of differing fields, the first one or two
// (in on-disk field order), and the single worst one -- not every differing field's full values,
// which is what made the old known-diff.tsv unreadable (roughly 2 KB of reason text per row,
// repeating "c=..., net=..." for every one of up to 47 cusp/ascmc fields). A human tracking
// porting progress needs to see how many fields are wrong and how bad the worst one is, not a
// transcript of all of them; the full detail is still reproducible at any time by rerunning
// OracleVerify against the live dumps.
static string BuildReason(RowOutcome outcome)
{
    var parts = new List<string>();
    if (!outcome.RetcMatches)
    {
        parts.Add($"retc: c={outcome.CRetc}, net={outcome.NetRetc}");
    }
    if (!outcome.ErrMatches)
    {
        parts.Add($"serr: c=\"{outcome.CErr}\", net=\"{outcome.NetErr}\"");
    }

    if (outcome.FieldDiffs.Count > 0)
    {
        // FieldDiffs is already in on-disk field order (RowComparer walks the row left to right),
        // so Take/skip here needs no separate sort to stay deterministic.
        var firstCount = Math.Min(2, outcome.FieldDiffs.Count);
        var first = outcome.FieldDiffs.Take(firstCount).Select(DescribeField);
        // Ties (e.g. two categorical fields, which both carry UlpMath.CategoricalDistance) are
        // broken by field index so the choice of "worst" is stable across regenerations.
        var worst = outcome.FieldDiffs
            .OrderByDescending(f => f.Ulp)
            .ThenBy(f => f.Index)
            .First();

        parts.Add($"{outcome.FieldDiffs.Count} field(s) differ");
        parts.Add($"first: {string.Join(", ", first)}");
        parts.Add($"worst: {DescribeField(worst)}");
    }

    return string.Join("; ", parts);
}

// "unrelated" for a distance above UlpMath.UnrelatedThreshold: see that constant's remarks for
// why a totalOrder bit distance that large no longer means "the same value, off by a rounding
// error" -- reporting it as a ULP count would claim a precision the comparison does not have.
static string DescribeField(FieldDiff diff)
{
    var tag = diff.Ulp switch
    {
        UlpMath.CategoricalDistance => "categorical",
        > UlpMath.UnrelatedThreshold => "unrelated",
        _ => $"ulp={diff.Ulp.ToString(CultureInfo.InvariantCulture)}",
    };
    return $"{diff.Label}: c={FormatValue(diff.CValue)}, net={FormatValue(diff.NetValue)} ({tag})";
}

static string FormatValue(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
