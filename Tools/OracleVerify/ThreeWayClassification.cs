using System.Globalization;

namespace OracleVerify;

/// <summary>
/// Which of the two prior comparisons (port-vs-2.08, port-vs-2.10.03) a case_id falls into --
/// see Program.cs's "classify" mode, and Tests/oracle/version-classification.tsv's own header
/// for what this is for and why it exists separately from Tests/oracle/known-diff.tsv's
/// PORT-VERSION category.
///
/// PORT-VERSION in known-diff.tsv only ever means "the port differs from 2.10.03 C" -- it cannot
/// say whether that difference also holds against 2.08, the version the port actually tracks,
/// because known-diff.tsv is never compared against a 2.08 dump. Every row below is instead
/// classified against both C versions. The four names report which C version(s) the port's
/// result matched -- nothing more; they describe the measurement, not a cause, so pick the
/// reading up from <see cref="ThreeWayRow.C208VsC210"/> where a cause is what's needed:
/// <list type="bullet">
/// <item><see cref="AgreesBoth"/>: the port agrees with 2.08 C and with 2.10.03 C.</item>
/// <item><see cref="Tracks208"/>: the port agrees with 2.08 C, differs from 2.10.03 C. Porting
/// work outstanding -- closing this gap is what upgrading to 2.10.03 is for.</item>
/// <item><see cref="Tracks210"/>: the port agrees with 2.10.03 C, differs from 2.08 C. Already
/// ported: some earlier change brought this case_id's behaviour to 2.10.03 even though the port
/// as a whole has not moved off 2.08 yet. Every SIDEREAL-flag row in the analytic grid falls
/// here, for instance, from the ayanamsha and constants work already landed.</item>
/// <item><see cref="TracksNeither"/>: the port agrees with neither C version. During a partial
/// port this is the expected state for any case_id whose result combines a ported component with
/// an unported one -- it is not, by itself, evidence of a defect. FIXSTAR and FIXSTAR2 have zero
/// rows here, while their _UT twins (FIXSTAR_UT, FIXSTAR2_UT) have 32 each: the star-position
/// code is identical between the two, and the only difference is the UT-to-ET conversion, which
/// runs through delta T. swephlib.c, where delta T lives, is already at 2.10.03; sweph.c is not.
/// 2.10.03 delta T feeding a 2.08-tracked position matches neither pure C version, correctly --
/// CALCUT (440 such rows) against CALC (200) shows the same asymmetry. Telling that expected
/// mixed state apart from a genuine bug needs a human looking at which components a row
/// exercises, but <see cref="ThreeWayRow.C208VsC210"/> narrows the search: when it reads MATCH,
/// 2.08 C and 2.10.03 C agree with each other and only the port differs, meaning the
/// 2.08-to-2.10.03 C diff never touched this code path at all -- the divergence predates 2.08
/// and is fixable independently of porting progress. All 40 analytic-grid TracksNeither rows are
/// this case: the Moshier-Moon-out-of-range serr wording, byte-identical across both C versions,
/// is a real port bug and the clearest actionable item this classification produces.</item>
/// </list>
/// </summary>
internal enum VersionClassification
{
    AgreesBoth,
    Tracks208,
    Tracks210,
    TracksNeither,
}

internal static class VersionClassificationNames
{
    public const string AgreesBoth = "AGREES-BOTH";
    public const string Tracks208 = "TRACKS-2.08";
    public const string Tracks210 = "TRACKS-2.10.03";
    public const string TracksNeither = "TRACKS-NEITHER";

    public static string ToName(VersionClassification classification) => classification switch
    {
        VersionClassification.AgreesBoth => AgreesBoth,
        VersionClassification.Tracks208 => Tracks208,
        VersionClassification.Tracks210 => Tracks210,
        VersionClassification.TracksNeither => TracksNeither,
        _ => throw new ArgumentOutOfRangeException(nameof(classification)),
    };
}

/// <summary>One classified case_id -- one row of Tests/oracle/version-classification.tsv or -files.tsv.</summary>
internal sealed record ThreeWayRow(
    string CaseId,
    VersionClassification Classification,
    string PortVs208,
    string PortVs210,
    string C208VsC210);

/// <summary>
/// Builds <see cref="ThreeWayRow"/>s from three dumps of the same grid (2.10.03 C, 2.08 C, the
/// port), reusing <see cref="RowComparer"/> for all three pairings it needs -- that comparer only
/// cares that two <see cref="DumpRow"/>s share a case_id and a value-field count, not which side
/// is "C" and which is ".NET", so comparing two C dumps against each other (2.08 vs 2.10.03) works
/// unchanged from comparing the port against either one.
/// </summary>
internal static class ThreeWayClassifier
{
    /// <summary>
    /// Loads all three dumps, requires every one to cover exactly the same case_id set (the same
    /// posture Program.cs's LoadAndCompare takes for two dumps -- silently comparing an
    /// intersection would silently narrow coverage), and returns one classified row per case_id,
    /// sorted by case_id for deterministic output.
    /// </summary>
    public static IReadOnlyList<ThreeWayRow> LoadAndClassify(string c210Path, string c208Path, string netPath)
    {
        var c210Rows = DumpFile.Load(c210Path);
        var c208Rows = DumpFile.Load(c208Path);
        var netRows = DumpFile.Load(netPath);

        if (c210Rows.Count != c208Rows.Count || c210Rows.Count != netRows.Count)
        {
            throw new FormatException(
                $"Dumps have different row counts: {c210Path} has {c210Rows.Count}, {c208Path} has {c208Rows.Count}, " +
                $"{netPath} has {netRows.Count}. All three should have been produced by the same run of scripts/run-oracle-dump.ps1 against the same grid.");
        }

        var onlyInC210 = c210Rows.Keys.Where(id => !c208Rows.ContainsKey(id) || !netRows.ContainsKey(id)).ToList();
        var onlyInC208 = c208Rows.Keys.Where(id => !c210Rows.ContainsKey(id) || !netRows.ContainsKey(id)).ToList();
        var onlyInNet = netRows.Keys.Where(id => !c210Rows.ContainsKey(id) || !c208Rows.ContainsKey(id)).ToList();
        if (onlyInC210.Count > 0 || onlyInC208.Count > 0 || onlyInNet.Count > 0)
        {
            throw new FormatException(
                "Dumps disagree on which case ids are present " +
                $"({onlyInC210.Count} not shared by all three from {c210Path}, {onlyInC208.Count} from {c208Path}, {onlyInNet.Count} from {netPath}). " +
                $"First offender: {(onlyInC210.Count > 0 ? onlyInC210[0] : onlyInC208.Count > 0 ? onlyInC208[0] : onlyInNet[0])}.");
        }

        var rows = c210Rows.Keys
            .Select(id => Classify(c210Rows[id], c208Rows[id], netRows[id]))
            .OrderBy(r => r.CaseId, StringComparer.Ordinal)
            .ToList();

        if (rows.Count == 0)
        {
            throw new FormatException("Zero rows were classified -- a run that classified nothing is not a pass.");
        }

        return rows;
    }

    private static ThreeWayRow Classify(DumpRow c210, DumpRow c208, DumpRow net)
    {
        var portVs210 = RowComparer.Compare(c210, net);
        var portVs208 = RowComparer.Compare(c208, net);
        var c208VsC210 = RowComparer.Compare(c208, c210);

        // The four classifications are exactly the cross-tab of these two booleans -- the name a
        // case_id gets never infers a cause, only reports which C version(s) the port's result
        // matched. c208VsC210 is still measured and stored on every row (see
        // ThreeWayRow.C208VsC210) because it is the documented way to tell a TracksNeither row
        // that is a genuine, independently-fixable port bug (c208VsC210 matches -- see
        // VersionClassification's doc comment) apart from one that is an expected mid-port mix --
        // but it plays no part in choosing the classification itself.
        var classification = (portVs208.Matches, portVs210.Matches) switch
        {
            (true, true) => VersionClassification.AgreesBoth,
            (true, false) => VersionClassification.Tracks208,
            (false, true) => VersionClassification.Tracks210,
            (false, false) => VersionClassification.TracksNeither,
        };

        return new ThreeWayRow(c210.CaseId, classification, Describe(portVs208), Describe(portVs210), Describe(c208VsC210));
    }

    /// <summary>
    /// "MATCH", or a short summary of what differs -- deliberately terser than
    /// Program.cs's BuildReason (worst field only, not "first two plus worst"): three of these
    /// columns sit on one TSV row here, against BuildReason's one column on a known-diff.tsv row,
    /// so the same level of detail would make every row three times as wide.
    /// </summary>
    private static string Describe(RowOutcome outcome)
    {
        if (outcome.Matches)
        {
            return "MATCH";
        }

        var parts = new List<string>();
        if (!outcome.RetcMatches)
        {
            parts.Add($"retc {outcome.CRetc.ToString(CultureInfo.InvariantCulture)}!={outcome.NetRetc.ToString(CultureInfo.InvariantCulture)}");
        }
        if (!outcome.ErrMatches)
        {
            parts.Add("serr differs");
        }
        if (outcome.FieldDiffs.Count > 0)
        {
            var worst = outcome.FieldDiffs.OrderByDescending(f => f.Ulp).ThenBy(f => f.Index).First();
            var tag = worst.Ulp switch
            {
                UlpMath.CategoricalDistance => "categorical",
                > UlpMath.UnrelatedThreshold => "unrelated",
                _ => $"ulp={worst.Ulp.ToString(CultureInfo.InvariantCulture)}",
            };
            parts.Add($"{outcome.FieldDiffs.Count.ToString(CultureInfo.InvariantCulture)} field(s) differ, worst {worst.Label} ({tag})");
        }

        return string.Join("; ", parts);
    }
}

/// <summary>
/// Reads/writes Tests/oracle/version-classification.tsv and version-classification-files.tsv --
/// same read/write shape as KnownDiffList.cs, hard-failing (not skipping) on a bad header or a
/// wrong column count for the same reason: a tolerant reader could silently compare against a
/// truncated or corrupted file. Unlike KnownDiffList.cs's file, this one leads with a block of
/// '#'-prefixed comment lines (see <see cref="HeaderComment"/>) documenting the classification --
/// Load skips them the same way it skips blank lines, so they cost nothing on the read side but
/// mean the interpretation travels with the data instead of living only in this source file.
/// </summary>
internal static class ThreeWayClassificationFile
{
    private static readonly string[] Header = ["case_id", "classification", "port_vs_2.08", "port_vs_2.10.03", "c208_vs_c210"];

    /// <summary>
    /// Written verbatim, one '#' comment line each, above the column header by <see cref="Save"/>.
    /// Restates <see cref="VersionClassification"/>'s doc comment for a reader who opens the TSV
    /// directly and never looks at the source: what each classification means, and -- because
    /// TRACKS-NEITHER is the one value the file can't just name its way out of explaining -- that
    /// it is the expected mid-port state, not itself a defect, plus the FIXSTAR/FIXSTAR_UT
    /// example that isolates delta T as the cause and the c208_vs_c210 column as the way to spot
    /// the exception (a genuine, independently-fixable port bug) among the expected mix.
    /// </summary>
    private static readonly string[] HeaderComment =
    [
        "# Classifies every oracle case_id by which Swiss Ephemeris C version(s) the port's result",
        "# matches: 2.08, the version the port tracks, and 2.10.03, the upgrade target. Regenerated",
        "# by scripts/classify-oracle-versions.ps1 -- see Tools/OracleVerify/ThreeWayClassification.cs",
        "# for the classifier. Not a gate: nothing in CI reads this file.",
        "#",
        "# classification values (see the port_vs_2.08 / port_vs_2.10.03 / c208_vs_c210 columns for",
        "# the measurement behind each):",
        "#   AGREES-BOTH     port matches 2.08 C and 2.10.03 C.",
        "#   TRACKS-2.08     port matches 2.08 C, differs from 2.10.03 C -- porting work",
        "#                   outstanding; this is the case_id set that upgrading to 2.10.03 exists",
        "#                   to close.",
        "#   TRACKS-2.10.03  port matches 2.10.03 C, differs from 2.08 C -- already ported: some",
        "#                   earlier change brought this case_id to 2.10.03 behaviour even though",
        "#                   the port as a whole has not moved off 2.08 yet.",
        "#   TRACKS-NEITHER  port matches neither C version. Expected mid-port, not by itself a",
        "#                   defect: any case_id whose result mixes a ported component with an",
        "#                   unported one lands here. Worked example -- FIXSTAR and FIXSTAR2 have",
        "#                   no TRACKS-NEITHER rows; their _UT twins have 32 each, because the",
        "#                   only difference between the UT and non-UT calls is the UT-to-ET",
        "#                   conversion, which runs through delta T, and swephlib.c (where delta T",
        "#                   lives) is already at 2.10.03 while sweph.c is not. Telling that",
        "#                   expected mix apart from a genuine bug needs a human reading which",
        "#                   components a row exercises -- but the c208_vs_c210 column narrows the",
        "#                   search: MATCH there means 2.08 C and 2.10.03 C agree with each other",
        "#                   and only the port differs, so the 2.08-to-2.10.03 C diff never touched",
        "#                   this code path and the divergence predates 2.08, fixable independently",
        "#                   of porting progress.",
    ];

    public static void Save(string path, IEnumerable<ThreeWayRow> rows)
    {
        using var writer = new StreamWriter(path, append: false);
        writer.NewLine = "\n";
        foreach (var commentLine in HeaderComment)
        {
            writer.WriteLine(commentLine);
        }
        writer.WriteLine(string.Join('\t', Header));

        foreach (var row in rows.OrderBy(r => r.CaseId, StringComparer.Ordinal))
        {
            writer.WriteLine(string.Join(
                '\t',
                row.CaseId,
                VersionClassificationNames.ToName(row.Classification),
                row.PortVs208,
                row.PortVs210,
                row.C208VsC210));
        }
    }

    public static IReadOnlyList<ThreeWayRow> Load(string path)
    {
        using var reader = new StreamReader(path);

        string? headerLine;
        var lineNumber = 0;
        do
        {
            headerLine = reader.ReadLine();
            lineNumber++;
        } while (headerLine is not null && headerLine.StartsWith('#'));

        if (headerLine is null || !headerLine.Split('\t').SequenceEqual(Header, StringComparer.Ordinal))
        {
            throw new FormatException($"{path}: expected header '{string.Join('\t', Header)}', got '{headerLine}'.");
        }

        var rows = new List<ThreeWayRow>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length != 5)
            {
                throw new FormatException($"{path}:{lineNumber}: expected 5 tab-separated columns, got {parts.Length}: '{line}'");
            }

            var classification = parts[1] switch
            {
                VersionClassificationNames.AgreesBoth => VersionClassification.AgreesBoth,
                VersionClassificationNames.Tracks208 => VersionClassification.Tracks208,
                VersionClassificationNames.Tracks210 => VersionClassification.Tracks210,
                VersionClassificationNames.TracksNeither => VersionClassification.TracksNeither,
                _ => throw new FormatException($"{path}:{lineNumber}: unknown classification '{parts[1]}'."),
            };

            rows.Add(new ThreeWayRow(parts[0], classification, parts[2], parts[3], parts[4]));
        }

        return rows;
    }
}
