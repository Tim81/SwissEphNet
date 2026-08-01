using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SwissEphNet.Conformance.Tests.Corpus;
using SwissEphNet.Conformance.Tests.KnownFail;
using Xunit;

namespace SwissEphNet.Conformance.Tests;

/// <summary>
/// Reads/validates Tests/conformance/value-mismatch-triage.tsv: the one-time forensic record
/// (README.md, "How this is checked...") of driving Astrodienst's own MSVC-built 2.10.03 C
/// through all 668 VALUE-MISMATCH rows that were in known-fail.tsv at the time it ran, and
/// classifying each as DRIFT (the port's answer agrees with that C build, so the mismatch is
/// drift between this environment and whatever produced setest's reference values, not a port
/// bug) or PORT-DEFECT (the port's answer disagrees with both t.exp and the C build -- an actual
/// bug). Unlike known-fail.tsv, this file is not regenerated from a live run and is not meant to
/// be: it is a point-in-time snapshot, kept as the evidence for the README's specific claim
/// ("found 4 confirmed port defects ... That guard is fixed now and the four rows are pruned").
///
/// It had no mechanical consumer at all before this test: nothing parsed it, and nothing checked
/// that its central claim -- the 4 PORT-DEFECT rows were fixed -- actually stays true. This test
/// gives it two, without requiring the underlying C driver (unavailable outside a machine with
/// the same MSVC toolchain and Astrodienst's C source built) to reproduce the file's own findings:
/// a schema/self-consistency check anyone can run, and a regression guard tying the file's one
/// substantive claim to the current known-fail.tsv it was originally checked against.
/// </summary>
public class ValueMismatchTriageTests
{
    private const string FileName = "value-mismatch-triage.tsv";
    private static readonly string[] Header =
        ["suite", "testcase", "iteration", "magnitude_key", "classification", "note", "fields (texp=t.exp expected, port=SwissEphNet output, c=MSVC-built Astrodienst 2.10.03 C output)"];

    private static readonly HashSet<string> AllowedClassifications = new(StringComparer.Ordinal) { "DRIFT", "PORT-DEFECT" };

    private sealed record TriageRow(IterationKey Key, string MagnitudeKey, string Classification, string Note, string Fields);

    private static string ResolvePath() => Path.Combine(RepoLocator.ConformanceDataDir, FileName);

    private static IReadOnlyList<TriageRow> Load(string path)
    {
        using var reader = new StreamReader(path);
        var headerLine = reader.ReadLine();
        Assert.NotNull(headerLine);
        Assert.Equal(Header, headerLine!.Split('\t'));

        var rows = new List<TriageRow>();
        var seen = new HashSet<IterationKey>();
        string? line;
        var lineNumber = 1;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (line.Length == 0)
            {
                continue;
            }

            // The trailing "fields" column freely contains tabs? No -- it is TSV, so it must
            // not; but it does contain many "; "-joined field diffs, so split with a count cap
            // to keep a stray literal tab inside a future note from silently shifting columns.
            var parts = line.Split('\t', 7);
            if (parts.Length != 7)
            {
                throw new FormatException($"{path}:{lineNumber}: expected 7 tab-separated columns, got {parts.Length}: '{line}'");
            }

            var key = new IterationKey(
                int.Parse(parts[0], CultureInfo.InvariantCulture),
                int.Parse(parts[1], CultureInfo.InvariantCulture),
                int.Parse(parts[2], CultureInfo.InvariantCulture));

            if (!seen.Add(key))
            {
                throw new FormatException($"{path}:{lineNumber}: duplicate entry for iteration {key}.");
            }

            rows.Add(new TriageRow(key, parts[3], parts[4], parts[5], parts[6]));
        }

        return rows;
    }

    [Fact]
    public void EveryRow_HasAKnownClassification_AndNonEmptyFieldsColumn()
    {
        var path = ResolvePath();
        var rows = Load(path);

        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            Assert.Contains(row.Classification, AllowedClassifications);
            Assert.False(string.IsNullOrWhiteSpace(row.Fields), $"{row.Key}: 'fields' column is empty.");
        }
    }

    /// <summary>
    /// README.md's specific claim: the four PORT-DEFECT rows (a missing JD-range guard on
    /// interpolated lunar perigee, docs/compliance-2.10.03.md section 3a) are fixed, and the guard
    /// keeps them out of known-fail.tsv's VALUE-MISMATCH set. If that guard ever regressed, these
    /// four iterations would start failing again and reappear there -- this is exactly the
    /// silent-regression shape a one-time forensic file cannot catch on its own, since nothing
    /// ever re-runs the analysis that produced it. Pinned to the four (suite, testcase, iteration)
    /// triples the file itself records as PORT-DEFECT, not to a count, so a change to which rows
    /// are classified PORT-DEFECT is itself visible as a test failure here (either an entry this
    /// list doesn't know about, or one of these four no longer being classified as such).
    /// </summary>
    [Fact]
    public void PortDefectRows_MatchTheFourDocumentedRows_AndStayAbsentFromKnownFail()
    {
        var expectedPortDefects = new[]
        {
            new IterationKey(1, 1, 377),
            new IterationKey(1, 1, 379),
            new IterationKey(1, 1, 383),
            new IterationKey(1, 1, 385),
        };

        var rows = Load(ResolvePath());
        var actualPortDefects = rows.Where(r => r.Classification == "PORT-DEFECT").Select(r => r.Key).OrderBy(k => k.Iteration).ToList();

        Assert.Equal(expectedPortDefects, actualPortDefects);

        var knownFailPath = Path.Combine(RepoLocator.ConformanceDataDir, "known-fail.tsv");
        var knownFail = KnownFailList.Load(knownFailPath);

        foreach (var key in expectedPortDefects)
        {
            if (knownFail.TryGetValue(key, out var entry))
            {
                Assert.True(
                    entry.Category != FailureCategory.ValueMismatch,
                    $"{key} is a documented PORT-DEFECT (missing JD-range guard on interpolated lunar perigee, " +
                    "docs/compliance-2.10.03.md section 3a) that README.md claims is fixed, but it is back in " +
                    $"known-fail.tsv as VALUE-MISMATCH ({entry.Reason}). Either the fix regressed, or the README's " +
                    "claim needs correcting.");
            }
        }
    }

    /// <summary>
    /// Every row this file names must correspond to a real, still-existing iteration in the
    /// current setest corpus -- catches the file quietly drifting out of sync with a t.exp
    /// regeneration (a suite/testcase/iteration renumbering) without anyone noticing, since
    /// nothing else ever reads it.
    /// </summary>
    [Fact]
    public void EveryRow_ReferencesAnIterationThatStillExistsInTheCorpus()
    {
        var rows = Load(ResolvePath());

        // Parse-only, like ConformanceSuiteTests.CorpusParsesToExpectedTotals -- this does
        // not need a full 12,757-iteration dispatch run, just the set of iteration keys t.exp
        // currently defines.
        var expPath = Path.Combine(RepoLocator.SetestDir, "t.exp");
        var doc = ExpReader.Read(expPath);

        var validKeys = new HashSet<IterationKey>();
        foreach (var suite in doc.TestSuites)
        {
            foreach (var testCase in suite.TestCases)
            {
                foreach (var iteration in testCase.Iterations)
                {
                    validKeys.Add(new IterationKey(suite.Id, testCase.Id, iteration.Id));
                }
            }
        }

        var missing = rows.Where(r => !validKeys.Contains(r.Key)).Select(r => r.Key).ToList();
        Assert.True(missing.Count == 0, $"{missing.Count} row(s) in value-mismatch-triage.tsv reference an iteration no longer in the corpus: {string.Join(", ", missing.Take(20))}.");
    }
}
