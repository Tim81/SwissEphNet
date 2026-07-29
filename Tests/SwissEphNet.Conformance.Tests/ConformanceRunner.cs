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
            // SUITE SETUP: every setest suite file except suite_03_misc.c issues
            // swe_set_ephe_path(NULL) at suite scope. TESTSUITE emits the suite body once
            // (testsuite.m4), so it runs once per suite, not per testcase.
            //
            // This is the half of the ephemeris-state fix that actually moves results, and
            // what it recovers from is state left behind by an *earlier suite*, not lazily
            // resolved tid_acc. Measured: with no reset, 360 of suite 6 testcase 3's 1080
            // iterations differ depending on whether the corpus runs whole or that suite
            // runs alone, by up to 30 degrees; with it, none do. The predecessor that
            // poisons them is suite 2 -- after its fixed-star work, SE_SIDM_TRUE_CITRA
            // could no longer resolve Spica, so swi_get_ayanamsa_ex returned ERR with an
            // ayanamsa of zero and the cusps came out tropical, about 24 degrees off.
            //
            // The underlying port defect there is fixed separately (CFile.Seek left EOF
            // set, so C.rewind could not re-read the star catalogue) and is covered by
            // tests in Tests/SwissEphNet.Tests rather than by this corpus, which was only
            // catching it by accident.
            if (suite.Id != 3)
            {
                // suite_06_houses.c:9 precedes its reset with swe_close(), which clears
                // strictly more than swi_close_keep_topo_etc does -- topocentric and
                // sidereal state, ayana_is_set, geopos_is_set, last_epheflag, dpsi/deps and
                // the loaded fixed-star array. Measured as inert for every one of that
                // suite's iterations, but mirrored so the sequence matches the reference
                // rather than relying on it staying inert.
                if (suite.Id == 6)
                {
                    swe.swe_close();
                }

                EphemerisFileResolver.ResetEphePath(swe);

                // suite_01_calc.c:11 and suite_10_solcross.c:11 follow the reset with
                // swe_set_jpl_file("de431.eph"). That is not inert either: sweph.c:1481
                // routes it through swi_close_keep_topo_etc, which memsets swed.fidat
                // (sweph.c:1205) and so zeroes fidat[SEI_FILE_MOON].sweph_denum -- exactly
                // the field calc_deltat reads at swephlib.c:2565. Without it those two
                // suites started with the DE number still set by swe_set_ephe_path's eager
                // lunar open, where setest starts with it cleared.
                if (suite.Id is 1 or 10)
                {
                    EphemerisFileResolver.SetJplFile(swe);
                }
            }

            foreach (var testCase in suite.TestCases)
            {
                var precision = precisionTable.TryGetValue((suite.Id, testCase.Id), out var p)
                    ? p
                    : Precision.Default;

                foreach (var iteration in testCase.Iterations)
                {
                    // Some testcases repeat the call in their own body. The testcase body is
                    // driven by a do { ... } while (has_more_iterations(ctx)) loop
                    // (testsuite.m4:52-68), so those are per-iteration resets, not
                    // per-testcase ones: suite_01_calc.c:31,40,50 (testcases 2,3,4) and
                    // suite_02_fixstar.c:50,62,76,89 (testcases 4,5,6,7). Read the m4, not
                    // testsuite_facade.h -- that header says at line 1 that it holds
                    // provisional definitions only so the C reads as complete in an IDE,
                    // and its TESTCASE carries no loop.
                    if ((suite.Id == 1 && testCase.Id is 2 or 3 or 4) ||
                        (suite.Id == 2 && testCase.Id is 4 or 5 or 6 or 7))
                    {
                        EphemerisFileResolver.ResetEphePath(swe);
                    }

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
