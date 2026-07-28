using System;
using System.Collections.Generic;
using System.IO;
using SwissEphNet.Conformance.Tests.Corpus;
using SwissEphNet.Conformance.Tests.Dispatch;
using SwissEphNet.Conformance.Tests.KnownFail;

namespace SwissEphNet.Conformance.Tests;

/// <summary>
/// Loads the t.exp/t.fix corpus, dispatches every iteration to the port, and
/// classifies the result. Does not know about the known-fail list -- see
/// <see cref="ConformanceReport"/> for the PASS/FAIL/REPORT comparison.
/// </summary>
public static class ConformanceRunner
{
    /// <summary>
    /// Keys every iteration may legitimately carry without any dispatcher
    /// ever reading them: "section-id" is consumed by the reader itself (see
    /// ExpReader.RequireId) but is included here defensively; "section-descr"
    /// is a purely decorative comment some iterations echo back (e.g. "Mars
    /// in cor solis"); "initialize" is read by this runner's own TEARDOWN
    /// step, not by a suite dispatcher.
    /// </summary>
    private static readonly HashSet<string> DecorativeKeys = new(StringComparer.Ordinal)
    {
        "section-id",
        "section-descr",
        "initialize",
    };

    public static (ExpDocument Document, IReadOnlyList<IterationResult> Iterations) Run()
    {
        // Fail fast, and loudly, before dispatching a single iteration, if the resolved
        // EpheDir does not contain exactly the declared core set -- see EphemerisManifest's
        // remarks. Running against undeclared data (missing OR extra) produces a
        // known-fail.tsv nobody else can reproduce, which is exactly how suite 5 testcase 3
        // iteration 6 ended up wrongly pruned once already.
        EphemerisManifest.AssertMatches();

        var expPath = Path.Combine(RepoLocator.SetestDir, "t.exp");
        var fixPath = Path.Combine(RepoLocator.SetestDir, "t.fix");

        var doc = ExpReader.Read(expPath);
        var precisionTable = FixPrecisionReader.Resolve(fixPath, doc);

        var results = new List<IterationResult>();

        using var swe = new SwissEph();
        EphemerisFileResolver.Attach(swe);
        var dispatcher = new ConformanceDispatcher();

        foreach (var suite in doc.TestSuites)
        {
            foreach (var testCase in suite.TestCases)
            {
                var precision = precisionTable.TryGetValue((suite.Id, testCase.Id), out var p)
                    ? p
                    : Precision.Default;

                foreach (var iteration in testCase.Iterations)
                {
                    var key = new IterationKey(suite.Id, testCase.Id, iteration.Id);
                    DispatchOutcome outcome;
                    try
                    {
                        outcome = dispatcher.Dispatch(swe, suite, testCase, iteration, precision);
                    }
                    catch (Exception ex)
                    {
                        var debugPath = Environment.GetEnvironmentVariable("SWISSEPH_CONFORMANCE_DEBUG_STACK");
                        if (!string.IsNullOrEmpty(debugPath))
                        {
                            File.AppendAllText(debugPath, $"[{key}] {ex}\n\n");
                        }

                        outcome = DispatchOutcome.Error($"{ex.GetType().Name}: {ex.Message}");
                    }

                    // Completeness guard: for an outcome that actually attempted a
                    // comparison (Passed or ValueMismatch), every non-decorative key
                    // t.exp carries for this iteration must have been read by
                    // something -- either as an input, or as an expected value in a
                    // Check* call. A leftover key means a dispatcher never even looked
                    // at an asserted value (e.g. an undersized buffer stopping a
                    // CHECK_DD short), which would otherwise pass silently and
                    // wrongly. Converts that whole class of bug from a silent false
                    // pass into a loud, reported failure.
                    if (outcome.Kind is OutcomeKind.Passed or OutcomeKind.ValueMismatch)
                    {
                        var unconsumed = iteration.Fields.UnconsumedKeys(DecorativeKeys);
                        if (unconsumed.Count > 0)
                        {
                            outcome = DispatchOutcome.Error(
                                $"harness completeness guard: {unconsumed.Count} field(s) present in t.exp for this iteration " +
                                $"were never read by the dispatcher (neither as input nor as a comparison): {string.Join(", ", unconsumed)}. " +
                                "This means a Check* call is missing or a buffer is undersized relative to what t.exp actually recorded.");
                        }
                    }

                    results.Add(new IterationResult(
                        key,
                        suite.Description ?? $"suite {suite.Id}",
                        testCase.Description,
                        outcome.Kind,
                        outcome.Reason,
                        outcome.Mismatches));

                    // TEARDOWN: several suites re-initialize the library between
                    // iterations when the iteration's own "initialize" flag says
                    // to (external/swisseph/setest/suite_0*.c TEARDOWN blocks).
                    if (iteration.Fields.TryGetInt("initialize") == 1)
                    {
                        try
                        {
                            swe.swe_close();
                        }
                        catch
                        {
                            // A close failure here must not take down the whole run;
                            // it will surface as failures on whatever comes next instead.
                        }
                    }
                }
            }
        }

        return (doc, results);
    }
}
