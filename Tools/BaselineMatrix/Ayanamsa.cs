using SwissEphNet;
using static BaselineMatrix.Format;

namespace BaselineMatrix;

/// <summary>
/// swe_get_ayanamsa / swe_get_ayanamsa_ut / swe_get_ayanamsa_ex / swe_get_ayanamsa_ex_ut
/// for every predefined sidereal mode (0 .. SidModeSweepCount-1), after swe_set_sid_mode,
/// plus SE_SIDM_USER with real t0/ayan_t0 and the SE_SIDBIT_ECL_T0 / SE_SIDBIT_SSY_PLANE
/// bits. A handful of the predefined modes are defined relative to a named fixed star
/// and need sefstars.txt, which is unavailable here (SwissEph.DefaultFileProvider is a
/// no-op provider, see Tools/BaselineMatrix/Areas.cs); whatever
/// those modes currently produce without the file is itself frozen behavior.
/// </summary>
internal static class Ayanamsa
{
    // Deliberately a literal, not SwissEph.SE_NSIDM_PREDEF: the committed baseline's row
    // set is fixed at 47 sid_modes (ids 0..46), and that count is a property of the frozen
    // matrix, not of whichever assembly happens to be under test. Reference mode resolves
    // SwissEph from the SwissEphNet 2.8.0.2 NuGet package, whose own SE_NSIDM_PREDEF is 43
    // (confirmed by loading that package's assembly directly) -- four sidereal modes short
    // of the port's 47. Reading the bound off that constant made reference mode sweep only
    // ids 0..42 and silently omit AY/AYUT/AYEX/AYEXUT rows for ids 43..46 (192 rows, all
    // present in the committed baseline), which is exactly the shape of bug -ExpectedScope
    // cannot catch: those 192 case ids are simply never generated on that side, so a
    // reference-mode regeneration would delete them with SCOPE-OK, not SCOPE-VIOLATION,
    // since deletion by omission never appears as a changed/added/removed id for a run that
    // never produced the id at all. Pinning the sweep to the literal the baseline was
    // actually built against keeps both modes' row counts equal regardless of which
    // package or local build SE_NSIDM_PREDEF happens to report.
    //
    // Nothing else re-derives this number, so a local-mode SwissEphNet that grows a 48th
    // predefined sidereal mode would leave this literal untouched: row-counts.tsv still
    // says 2,464, the sweep still stops at 46, and every gate stays green while the new
    // mode goes ungenerated. Tools/BaselineVerify.Tests/AyanamsaSweepCoverageTests.cs
    // guards against exactly that by asserting SidModeSweepCount &lt;= SwissEph.SE_NSIDM_PREDEF
    // in local mode only (never reference mode, where SE_NSIDM_PREDEF is 43 and the
    // assertion would fail every time for the reason explained above) -- internal, not
    // private, so that test can read it without re-deriving it a second way. See that
    // file's own doc comment for why the guard is directional (&lt;=, never ==) and why it
    // lives in a test rather than a runtime check.
    internal const int SidModeSweepCount = 47;

    private static readonly double[] Jds = Grids.JdSpread(8);

    private static readonly (string Name, int Flag)[] ExIflagCombos =
        [("0", 0), ("NONUT", SwissEph.SEFLG_NONUT)];

    // A representative subset for the SE_SIDM_USER and SIDBIT sweeps -- these exist
    // to characterize the mechanism itself, not to re-sweep every predefined mode.
    private static readonly int[] SidBitModes =
    [
        SwissEph.SE_SIDM_FAGAN_BRADLEY, SwissEph.SE_SIDM_LAHIRI, SwissEph.SE_SIDM_J2000,
        SwissEph.SE_SIDM_GALCENT_0SAG, SwissEph.SE_SIDM_TRUE_CITRA,
    ];

    private static readonly (string Name, int Bit)[] SidBits =
    [
        ("ECL_T0", SwissEph.SE_SIDBIT_ECL_T0),
        ("SSY_PLANE", SwissEph.SE_SIDBIT_SSY_PLANE),
    ];

    private static readonly (double T0, double AyanT0)[] UserModeParams =
    [
        (2451545.0, 0.0),
        (2415020.0, 24.0),
        (2299160.5, -5.5),
    ];

    public static void AddRows(List<string> rows)
    {
        for (var sidMode = 0; sidMode < SidModeSweepCount; sidMode++)
        {
            foreach (var jd in Jds)
            {
                rows.Add(BuildRow("AY", sidMode, jd, useUt: false));
                rows.Add(BuildRow("AYUT", sidMode, jd, useUt: true));

                foreach (var (flagName, flag) in ExIflagCombos)
                {
                    rows.Add(BuildExRow("AYEX", sidMode, jd, flagName, flag, useUt: false));
                    rows.Add(BuildExRow("AYEXUT", sidMode, jd, flagName, flag, useUt: true));
                }
            }
        }

        AddUserModeRows(rows);
        AddSidBitRows(rows);
        AddAyanamsaNameRows(rows);
    }

    /// <summary>
    /// swe_get_ayanamsa_name: a pure lookup table keyed by sid_mode, no calculation and no
    /// ephemeris/file dependency at all -- previously uncovered anywhere in this matrix
    /// (docs/known-issues.md, "31 of 107 public swe_* entry points have no matrix
    /// coverage"). Sweeps every predefined mode (0..SidModeSweepCount-1, same bound the
    /// AY/AYUT/AYEX/AYEXUT sweep above uses, for the same reason -- see that constant's own
    /// doc comment), plus a couple of out-of-range and SIDBIT-combined values to exercise
    /// the function's own `isidmode %= SE_SIDBITS` wraparound and its null return for
    /// isidmode &gt;= SE_NSIDM_PREDEF (Sweph.cs's swe_get_ayanamsa_name).
    /// </summary>
    private static void AddAyanamsaNameRows(List<string> rows)
    {
        for (var sidMode = 0; sidMode < SidModeSweepCount; sidMode++)
        {
            rows.Add(BuildAyanamsaNameRow(sidMode));
        }

        // Out of the predefined range: SidModeSweepCount itself (just past the last valid
        // id) and SE_SIDM_USER (255) fall through to the null-return branch.
        rows.Add(BuildAyanamsaNameRow(SidModeSweepCount));
        rows.Add(BuildAyanamsaNameRow(SwissEph.SE_SIDM_USER));

        // SE_SIDBITS wraparound: a predefined mode with SIDBIT_ECL_T0/SSY_PLANE OR'd in must
        // resolve to the same name as the bare mode, since swe_get_ayanamsa_name's own first
        // line is `isidmode %= SE_SIDBITS` before anything else looks at it.
        foreach (var sidMode in SidBitModes)
        {
            foreach (var (bitName, bit) in SidBits)
            {
                rows.Add(BuildAyanamsaNameRow(sidMode | bit, $"|{bitName}"));
            }
        }
    }

    private static string BuildAyanamsaNameRow(int sidMode, string suffix = "")
    {
        var caseId = $"AYNAME|{I(sidMode)}{suffix}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            var name = swe.swe_get_ayanamsa_name(sidMode);
            return [S(name)];
        });
    }

    private static string BuildRow(string prefix, int sidMode, double jd, bool useUt)
    {
        var caseId = $"{prefix}|{I(sidMode)}|{D(jd)}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            swe.swe_set_sid_mode(sidMode, 0, 0);
            var value = useUt ? swe.swe_get_ayanamsa_ut(jd) : swe.swe_get_ayanamsa(jd);
            return [D(value)];
        });
    }

    private static string BuildExRow(string prefix, int sidMode, double jd, string flagName, int flag, bool useUt)
    {
        var caseId = $"{prefix}|{I(sidMode)}|{D(jd)}|{flagName}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            swe.swe_set_sid_mode(sidMode, 0, 0);
            string? serr = null;
            double daya;
            int retc;
            if (useUt)
            {
                retc = swe.swe_get_ayanamsa_ex_ut(jd, flag, out daya, ref serr);
            }
            else
            {
                retc = swe.swe_get_ayanamsa_ex(jd, flag, out daya, ref serr);
            }
            return [I(retc), D(daya), S(serr)];
        });
    }

    private static void AddUserModeRows(List<string> rows)
    {
        foreach (var (t0, ayanT0) in UserModeParams)
        {
            foreach (var jd in Jds)
            {
                rows.Add(BuildUserModeRow(t0, ayanT0, jd, useUt: false));
                rows.Add(BuildUserModeRow(t0, ayanT0, jd, useUt: true));
            }
        }
    }

    private static string BuildUserModeRow(double t0, double ayanT0, double jd, bool useUt)
    {
        var caseId = $"AYUSER|{D(t0)}|{D(ayanT0)}|{D(jd)}|{B(useUt)}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            swe.swe_set_sid_mode(SwissEph.SE_SIDM_USER, t0, ayanT0);
            var value = useUt ? swe.swe_get_ayanamsa_ut(jd) : swe.swe_get_ayanamsa(jd);
            return [D(value)];
        });
    }

    private static void AddSidBitRows(List<string> rows)
    {
        foreach (var sidMode in SidBitModes)
        {
            foreach (var (bitName, bit) in SidBits)
            {
                foreach (var jd in Jds)
                {
                    rows.Add(BuildSidBitRow(sidMode, bitName, bit, jd, useUt: false));
                    rows.Add(BuildSidBitRow(sidMode, bitName, bit, jd, useUt: true));
                }
            }
        }
    }

    private static string BuildSidBitRow(int sidMode, string bitName, int bit, double jd, bool useUt)
    {
        var caseId = $"AYBIT|{I(sidMode)}|{bitName}|{D(jd)}|{B(useUt)}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            swe.swe_set_sid_mode(sidMode | bit, 0, 0);
            var value = useUt ? swe.swe_get_ayanamsa_ut(jd) : swe.swe_get_ayanamsa(jd);
            return [D(value)];
        });
    }
}
