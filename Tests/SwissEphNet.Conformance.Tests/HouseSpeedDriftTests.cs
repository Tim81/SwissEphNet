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
/// Reads/validates Tests/conformance/house-speed-drift-6c989727.tsv: the evidence for the setest
/// testcase 6.8 (swe_houses_ex2) and 6.9 (swe_houses_armc_ex2) rows that known-fail.tsv gained
/// when swehouse.c was synced to upstream 6c989727. That commit replaced finite-difference house
/// cusp speeds with analytic derivatives; t.exp, a static Astrodienst snapshot, still records the
/// old speeds. The file was produced by Tools/CReference/house-speed-texp-check.c linked against
/// the MSVC-built C at 6c989727, which the port matches bit for bit: for every field outside
/// t.exp's tolerance it compares the library's speed with the numerical derivative of the
/// library's own positions and classifies the iteration CORRECTION (the library is right and
/// t.exp is not), STEP (no derivative exists: a cusp jump inside the difference interval) or
/// UNEXPLAINED. Like value-mismatch-triage.tsv it is a point-in-time record, not regenerated from
/// a live run; these tests keep it honest against the files it describes.
/// </summary>
public class HouseSpeedDriftTests
{
    private const string FileName = "house-speed-drift-6c989727.tsv";
    private static readonly string[] Header =
        ["suite", "testcase", "iteration", "classification", "fields (texp=t.exp expected, c=C library output, num=numerical derivative of the library's own positions)"];

    private static readonly HashSet<string> AllowedClassifications = new(StringComparer.Ordinal) { "CORRECTION", "STEP" };

    private sealed record DriftRow(IterationKey Key, string Classification, string Fields);

    private static IReadOnlyList<DriftRow> Load()
    {
        var path = Path.Combine(RepoLocator.ConformanceDataDir, FileName);
        var lines = File.ReadAllLines(path);
        Assert.NotEmpty(lines);
        Assert.Equal(Header, lines[0].Split('\t'));

        var rows = new List<DriftRow>();
        var seen = new HashSet<IterationKey>();
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Length == 0)
            {
                continue;
            }

            var parts = lines[i].Split('\t', 5);
            if (parts.Length != 5)
            {
                throw new FormatException($"{path}:{i + 1}: expected 5 tab-separated columns, got {parts.Length}.");
            }

            var key = new IterationKey(
                int.Parse(parts[0], CultureInfo.InvariantCulture),
                int.Parse(parts[1], CultureInfo.InvariantCulture),
                int.Parse(parts[2], CultureInfo.InvariantCulture));
            if (!seen.Add(key))
            {
                throw new FormatException($"{path}:{i + 1}: duplicate entry for iteration {key}.");
            }

            rows.Add(new DriftRow(key, parts[3], parts[4]));
        }

        return rows;
    }

    [Fact]
    public void EveryRow_IsAnExplainedHouseSpeedRow()
    {
        var rows = Load();

        Assert.Equal(972, rows.Count);
        foreach (var row in rows)
        {
            Assert.True(row.Key.Suite == 6 && (row.Key.TestCase == 8 || row.Key.TestCase == 9), $"{row.Key} is not a 6.8/6.9 iteration.");
            Assert.Contains(row.Classification, AllowedClassifications);

            // Only speed fields moved: a position field in this file would mean the sync changed
            // a cusp, which nothing in 6c989727's house code does at these inputs.
            foreach (var field in row.Fields.Split("; "))
            {
                Assert.True(
                    field.StartsWith("cusp_speed[", StringComparison.Ordinal) || field.StartsWith("ascmc_speed[", StringComparison.Ordinal),
                    $"{row.Key}: unexpected non-speed field '{field}'.");
            }
        }
    }

    /// <summary>
    /// The two iterations where a whole sign cusp ingresses inside the difference interval, so the
    /// numerical derivative is a step and the library's 0 is the defined answer. Pinned to the
    /// keys, not a count, so a change in which rows are classified STEP is itself a failure here.
    /// </summary>
    [Fact]
    public void StepRows_AreTheTwoWholeSignIngressIterations()
    {
        var steps = Load().Where(r => r.Classification == "STEP").Select(r => r.Key).OrderBy(k => k.Iteration).ToList();

        Assert.Equal(new[] { new IterationKey(6, 8, 285), new IterationKey(6, 8, 645) }, steps);
    }

    /// <summary>
    /// The file and known-fail.tsv must describe the same iterations: every 6.8/6.9 row in
    /// known-fail.tsv has evidence here, every row here is in known-fail.tsv, and all of them are
    /// VALUE-MISMATCH. A 6.8/6.9 row that starts passing (an updated t.exp) or a new one that
    /// appears without evidence shows up here.
    /// </summary>
    [Fact]
    public void Rows_MatchKnownFail_OneToOne()
    {
        var drift = Load().Select(r => r.Key).ToHashSet();
        var knownFail = KnownFailList.Load(Path.Combine(RepoLocator.ConformanceDataDir, "known-fail.tsv"));
        var houseSpeedKnownFail = knownFail.Values
            .Where(e => e.Key.Suite == 6 && (e.Key.TestCase == 8 || e.Key.TestCase == 9))
            .ToList();

        Assert.All(houseSpeedKnownFail, e => Assert.Equal(FailureCategory.ValueMismatch, e.Category));
        var knownFailKeys = houseSpeedKnownFail.Select(e => e.Key).ToHashSet();

        var missingEvidence = knownFailKeys.Except(drift).ToList();
        var notInKnownFail = drift.Except(knownFailKeys).ToList();
        Assert.True(missingEvidence.Count == 0, $"{missingEvidence.Count} 6.8/6.9 known-fail row(s) have no evidence row: {string.Join(", ", missingEvidence.Take(20))}.");
        Assert.True(notInKnownFail.Count == 0, $"{notInKnownFail.Count} evidence row(s) are not in known-fail.tsv: {string.Join(", ", notInKnownFail.Take(20))}.");
    }

    [Fact]
    public void EveryRow_ReferencesAnIterationThatStillExistsInTheCorpus()
    {
        var doc = ExpReader.Read(Path.Combine(RepoLocator.SetestDir, "t.exp"));
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

        var missing = Load().Where(r => !validKeys.Contains(r.Key)).Select(r => r.Key).ToList();
        Assert.True(missing.Count == 0, $"{missing.Count} row(s) reference an iteration no longer in the corpus: {string.Join(", ", missing.Take(20))}.");
    }
}
