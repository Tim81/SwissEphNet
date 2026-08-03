using SwissEphNet;
using static BaselineMatrix.Format;

namespace BaselineMatrix;

/// <summary>
/// swe_sol_eclipse_where/how/when_glob/when_loc, swe_lun_eclipse_how/when/when_loc and
/// swe_lun_occult_where/when_glob/when_loc, all reachable under SEFLG_MOSEPH with no real
/// ephemeris files available (SwissEph.DefaultFileProvider is a no-op provider) for the
/// eight classical planets. None of these functions are exercised anywhere else in the matrix.
///
/// The four "_where"/"_how" functions compute directly at a given time and are cheap;
/// the "_when_*" search functions iterate forward or backward from a start time looking
/// for the next (or previous) eclipse/occultation and are measurably slower -- timed
/// directly against this branch: sol_eclipse_when_glob/when_loc a few ms to ~160ms per
/// call depending on start time and geopos, lun_occult_when_glob/when_loc similarly
/// variable and slowest for Pluto (tens to ~90ms; occultations of outer planets by the
/// Moon are rare, so the search runs longer). Grids for the search functions are
/// deliberately small (a handful of start times, both search directions) to keep total
/// runtime bounded; the cheap functions get a wider sweep since it costs almost nothing.
///
/// The six asteroids (Chiron, Pholus, Ceres, Pallas, Juno, Vesta) appended to
/// OccultBodies/OccultSearchBodies are included for breadth, but every row for them is
/// SwissEph.ERR -- confirmed directly, and explained in full in PhenoAst.cs's doc comment:
/// swe_calc's minor-planet dispatch always requires a file, regardless of SEFLG_MOSEPH, so
/// eclipse_where/eclipse_how's own swe_calc call for the body fails immediately. This is
/// why lun_occult_when_glob/when_loc measured near-instantly for Chiron/Ceres during
/// timing (an immediate error, not a fast search) -- not a sign the search itself is cheap
/// for asteroids. They still freeze real, jd-dependent behavior (which ephemeris file gets
/// requested, and Chiron's date-range guard), just not real occultation geometry.
/// </summary>
internal static class Eclipse
{
    private const int MOSEPH = SwissEph.SEFLG_MOSEPH;

    // The eight classical planets (the only OccultBodies that produce a real occultation
    // computation -- see the class doc comment) plus the six asteroids, included for
    // breadth even though every asteroid row is a "file not found"/date-range error.
    private static readonly int[] OccultBodies =
    [
        SwissEph.SE_MERCURY, SwissEph.SE_VENUS, SwissEph.SE_MARS, SwissEph.SE_JUPITER,
        SwissEph.SE_SATURN, SwissEph.SE_URANUS, SwissEph.SE_NEPTUNE, SwissEph.SE_PLUTO,
        SwissEph.SE_CHIRON, SwissEph.SE_PHOLUS, SwissEph.SE_CERES, SwissEph.SE_PALLAS,
        SwissEph.SE_JUNO, SwissEph.SE_VESTA,
    ];

    // A smaller subset for the expensive when_glob/when_loc occultation searches --
    // Mercury and Mars are fast, Pluto is the measured worst case, Chiron and Ceres stand
    // in for the asteroid branch (which errors immediately rather than searching -- see
    // the class doc comment).
    private static readonly int[] OccultSearchBodies =
        [SwissEph.SE_MERCURY, SwissEph.SE_MARS, SwissEph.SE_JUPITER, SwissEph.SE_PLUTO, SwissEph.SE_CHIRON, SwissEph.SE_CERES];

    // London, Los Angeles, Sydney, and a near-polar site -- deliberately including one
    // extreme latitude, since eclipse visibility/refraction geometry is most likely to
    // diverge there.
    private static readonly (double Lon, double Lat, double Height)[] Observers =
    [
        (0, 51.5, 0),
        (-118.24, 34.05, 100),
        (151.2, -33.87, 50),
        (0, 89, 0),
    ];

    private static readonly (string Name, int IflType)[] SolarIflTypes =
        [("ANY", 0), ("TOTAL", SwissEph.SE_ECL_TOTAL)];

    private static readonly (string Name, int IflType)[] LunarIflTypes =
        [("ANY", 0), ("TOTAL", SwissEph.SE_ECL_TOTAL)];

    private static readonly double[] StartJds = Grids.JdSpread(3);

    public static void AddRows(List<string> rows)
    {
        AddSolEclipseWhereRows(rows);
        AddSolEclipseHowRows(rows);
        AddLunEclipseHowRows(rows);
        AddLunOccultWhereRows(rows);
        AddSolEclipseWhenGlobRows(rows);
        AddSolEclipseWhenLocRows(rows);
        AddLunEclipseWhenRows(rows);
        AddLunEclipseWhenLocRows(rows);
        AddLunOccultWhenGlobRows(rows);
        AddLunOccultWhenLocRows(rows);
    }

    private static void AddSolEclipseWhereRows(List<string> rows)
    {
        foreach (var jd in Grids.JdSpread(60))
        {
            var caseId = $"SEW|{D(jd)}";
            rows.Add(SafeRow(caseId, () =>
            {
                using var swe = new SwissEph();
                var geopos = new double[10];
                var attr = new double[20];
                string? serr = null;
                var retc = swe.swe_sol_eclipse_where(jd, MOSEPH, geopos, attr, ref serr);
                return
                [
                    I(retc), D(geopos[0]), D(geopos[1]),
                    D(attr[0]), D(attr[1]), D(attr[2]), D(attr[3]), S(serr),
                ];
            }));
        }
    }

    private static void AddSolEclipseHowRows(List<string> rows)
    {
        foreach (var jd in Grids.JdSpread(30))
        {
            foreach (var observer in Observers)
            {
                var caseId = $"SEH|{D(jd)}|{D(observer.Lon)},{D(observer.Lat)},{D(observer.Height)}";
                rows.Add(SafeRow(caseId, () =>
                {
                    using var swe = new SwissEph();
                    var geopos = new[] { observer.Lon, observer.Lat, observer.Height };
                    var attr = new double[20];
                    string? serr = null;
                    var retc = swe.swe_sol_eclipse_how(jd, MOSEPH, geopos, attr, ref serr);
                    return
                    [
                        I(retc), D(attr[0]), D(attr[1]), D(attr[2]), D(attr[3]),
                        D(attr[4]), D(attr[5]), D(attr[6]), S(serr),
                    ];
                }));
            }
        }
    }

    private static void AddLunEclipseHowRows(List<string> rows)
    {
        foreach (var jd in Grids.JdSpread(30))
        {
            // null geopos exercises the documented "geopos[] is not used so far; may
            // be NULL" branch; a real geopos exercises the same call shape as the
            // topocentric functions above for comparison.
            rows.Add(BuildLunEclipseHowRow(jd, null));
            foreach (var observer in Observers)
            {
                rows.Add(BuildLunEclipseHowRow(jd, [observer.Lon, observer.Lat, observer.Height]));
            }
        }
    }

    private static string BuildLunEclipseHowRow(double jd, double[]? geopos)
    {
        var geoposLabel = geopos is null ? "NULL" : $"{D(geopos[0])},{D(geopos[1])},{D(geopos[2])}";
        var caseId = $"LEH|{D(jd)}|{geoposLabel}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            var attr = new double[20];
            string? serr = null;
            var retc = swe.swe_lun_eclipse_how(jd, MOSEPH, geopos!, attr, ref serr);
            return
            [
                I(retc), D(attr[0]), D(attr[1]), D(attr[2]), D(attr[3]),
                D(attr[4]), D(attr[5]), D(attr[6]), S(serr),
            ];
        });
    }

    private static void AddLunOccultWhereRows(List<string> rows)
    {
        foreach (var ipl in OccultBodies)
        {
            foreach (var jd in Grids.JdSpread(40))
            {
                var caseId = $"LOW|{I(ipl)}|{D(jd)}";
                rows.Add(SafeRow(caseId, () =>
                {
                    using var swe = new SwissEph();
                    var geopos = new double[10];
                    var attr = new double[20];
                    string? serr = null;
                    var retc = swe.swe_lun_occult_where(jd, ipl, null, MOSEPH, geopos, attr, ref serr);
                    return
                    [
                        I(retc), D(geopos[0]), D(geopos[1]),
                        D(attr[0]), D(attr[1]), D(attr[2]), D(attr[3]), S(serr),
                    ];
                }));
            }
        }
    }

    private static void AddSolEclipseWhenGlobRows(List<string> rows)
    {
        foreach (var startJd in StartJds)
        {
            foreach (var (typeName, iflType) in SolarIflTypes)
            {
                foreach (var backward in new[] { false, true })
                {
                    var caseId = $"SWG|{D(startJd)}|{typeName}|{B(backward)}";
                    rows.Add(SafeRow(caseId, () =>
                    {
                        using var swe = new SwissEph();
                        var tret = new double[10];
                        string? serr = null;
                        var retc = swe.swe_sol_eclipse_when_glob(startJd, MOSEPH, iflType, tret, backward, ref serr);
                        return [I(retc), D(tret[0]), D(tret[2]), D(tret[3]), S(serr)];
                    }));
                }
            }
        }
    }

    private static void AddSolEclipseWhenLocRows(List<string> rows)
    {
        foreach (var startJd in StartJds)
        {
            foreach (var observer in Observers)
            {
                foreach (var backward in new[] { false, true })
                {
                    var caseId = $"SWL|{D(startJd)}|{D(observer.Lon)},{D(observer.Lat)},{D(observer.Height)}|{B(backward)}";
                    rows.Add(SafeRow(caseId, () =>
                    {
                        using var swe = new SwissEph();
                        var geopos = new[] { observer.Lon, observer.Lat, observer.Height };
                        var tret = new double[10];
                        var attr = new double[20];
                        string? serr = null;
                        var retc = swe.swe_sol_eclipse_when_loc(startJd, MOSEPH, geopos, tret, attr, backward, ref serr);
                        return [I(retc), D(tret[0]), D(tret[2]), D(tret[3]), D(attr[0]), S(serr)];
                    }));
                }
            }
        }
    }

    private static void AddLunEclipseWhenRows(List<string> rows)
    {
        foreach (var startJd in StartJds)
        {
            foreach (var (typeName, iflType) in LunarIflTypes)
            {
                foreach (var backward in new[] { false, true })
                {
                    var caseId = $"LEW|{D(startJd)}|{typeName}|{B(backward)}";
                    rows.Add(SafeRow(caseId, () =>
                    {
                        using var swe = new SwissEph();
                        var tret = new double[10];
                        string? serr = null;
                        var retc = swe.swe_lun_eclipse_when(startJd, MOSEPH, iflType, tret, backward, ref serr);
                        return [I(retc), D(tret[0]), D(tret[2]), D(tret[3]), S(serr)];
                    }));
                }
            }
        }
    }

    private static void AddLunEclipseWhenLocRows(List<string> rows)
    {
        foreach (var startJd in StartJds)
        {
            foreach (var observer in Observers)
            {
                foreach (var backward in new[] { false, true })
                {
                    var caseId = $"LWL|{D(startJd)}|{D(observer.Lon)},{D(observer.Lat)},{D(observer.Height)}|{B(backward)}";
                    rows.Add(SafeRow(caseId, () =>
                    {
                        using var swe = new SwissEph();
                        var geopos = new[] { observer.Lon, observer.Lat, observer.Height };
                        var tret = new double[10];
                        var attr = new double[20];
                        string? serr = null;
                        var retc = swe.swe_lun_eclipse_when_loc(startJd, MOSEPH, geopos, tret, attr, backward, ref serr);
                        return [I(retc), D(tret[0]), D(tret[2]), D(tret[3]), D(attr[0]), S(serr)];
                    }));
                }
            }
        }
    }

    private static void AddLunOccultWhenGlobRows(List<string> rows)
    {
        foreach (var ipl in OccultSearchBodies)
        {
            foreach (var startJd in StartJds)
            {
                foreach (var backward in new[] { false, true })
                {
                    var caseId = $"LOG|{I(ipl)}|{D(startJd)}|{B(backward)}";
                    rows.Add(SafeRow(caseId, () =>
                    {
                        using var swe = new SwissEph();
                        var tret = new double[10];
                        string? serr = null;
                        var retc = swe.swe_lun_occult_when_glob(startJd, ipl, null, MOSEPH, 0, tret, backward, ref serr);
                        return [I(retc), D(tret[0]), D(tret[2]), D(tret[3]), S(serr)];
                    }));
                }
            }
        }
    }

    private static void AddLunOccultWhenLocRows(List<string> rows)
    {
        foreach (var ipl in OccultSearchBodies)
        {
            foreach (var startJd in StartJds)
            {
                var observer = Observers[0];
                foreach (var backward in new[] { false, true })
                {
                    var caseId = $"LOL|{I(ipl)}|{D(startJd)}|{B(backward)}";
                    rows.Add(SafeRow(caseId, () =>
                    {
                        using var swe = new SwissEph();
                        var geopos = new[] { observer.Lon, observer.Lat, observer.Height };
                        var tret = new double[10];
                        var attr = new double[20];
                        string? serr = null;
                        var retc = swe.swe_lun_occult_when_loc(startJd, ipl, null, MOSEPH, geopos, tret, attr, backward, ref serr);
                        return [I(retc), D(tret[0]), D(tret[2]), D(tret[3]), D(attr[0]), S(serr)];
                    }));
                }
            }
        }
    }
}
