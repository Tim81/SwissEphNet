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
    public static (ExpDocument Document, IReadOnlyList<IterationResult> Iterations) Run()
    {
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
