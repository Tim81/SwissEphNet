using SwissEphNet;
using static BaselineMatrix.Format;

namespace BaselineMatrix;

/// <summary>
/// swe_houses_armc: pure computation, no ephemeris or file dependency, so this is
/// the densest matrix in the harness. Every row in AddRows and
/// AddSunshineStateRows uses a brand new SwissEph instance -- swe_houses_armc
/// keeps a hidden field (saved_sundec) that emulates a C static and would
/// otherwise make hsys 'I'/'i' depend on call order. AddStatefulPairRows is the
/// one deliberate, explicitly-named exception: see its doc comment.
///
/// Every row records cusp[0..36], not just cusp[0..12]: for hsys 'G' (Gauquelin
/// sectors) the library writes all 36 sectors, and that range is otherwise
/// completely invisible to the baseline. For every other house system, cusp[13..36]
/// are left at their zero-initialized default and simply pad the row out -- a fixed
/// column count keeps the file mechanically diffable across house systems.
/// </summary>
internal static class Houses
{
    private const int CuspCount = 37; // cusp[0..36]

    public static void AddRows(List<string> rows)
    {
        foreach (var hsys in Grids.HouseSystems)
        {
            foreach (var eps in Grids.Eps)
            {
                foreach (var geolat in Grids.GeoLats)
                {
                    foreach (var armc in Grids.Armcs)
                    {
                        rows.Add(BuildRow(hsys, eps, geolat, armc));
                    }
                }
            }
        }
    }

    private static string BuildRow(char hsys, double eps, double geolat, double armc)
    {
        var caseId = $"H|{hsys}|{D(eps)}|{D(geolat)}|{D(armc)}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            var cusp = new double[40];
            var ascmc = new double[10];
            var retc = swe.swe_houses_armc(armc, geolat, eps, hsys, cusp, ascmc);
            return Fields(retc, cusp, ascmc);
        });
    }

    /// <summary>
    /// Dedicated coverage for the saved_sundec hazard: hsys 'I'/'i' with
    /// ascmc[9] pre-set to the sentinel 99 (use previous state) versus a real
    /// declination. On a fresh instance saved_sundec always starts at 99, so the
    /// sentinel case is deterministic here -- but it is exactly the input shape
    /// that would NOT be reproducible if instances were reused across rows.
    /// </summary>
    public static void AddSunshineStateRows(List<string> rows)
    {
        char[] sunHsys = ['I', 'i'];
        double[] geolats = [-80, -60, -30, 0, 30, 60, 80];
        double[] armcs = [0, 45, 90, 135, 180, 225, 270, 315];
        (string Name, double Value)[] ascmc9Inits = [("sentinel99", 99), ("dec15_5", 15.5), ("decNeg20", -20.0)];

        foreach (var hsys in sunHsys)
        {
            foreach (var eps in Grids.Eps)
            {
                foreach (var geolat in geolats)
                {
                    foreach (var armc in armcs)
                    {
                        foreach (var (name, value) in ascmc9Inits)
                        {
                            rows.Add(BuildSunshineRow(hsys, eps, geolat, armc, name, value));
                        }
                    }
                }
            }
        }
    }

    private static string BuildSunshineRow(char hsys, double eps, double geolat, double armc, string initName, double ascmc9Init)
    {
        var caseId = $"HSUN|{hsys}|{D(eps)}|{D(geolat)}|{D(armc)}|{initName}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            var cusp = new double[40];
            var ascmc = new double[10];
            ascmc[9] = ascmc9Init;
            var retc = swe.swe_houses_armc(armc, geolat, eps, hsys, cusp, ascmc);
            return Fields(retc, cusp, ascmc);
        });
    }

    private static string[] Fields(int retc, double[] cusp, double[] ascmc)
    {
        var fields = new string[1 + CuspCount + 10];
        var i = 0;
        fields[i++] = I(retc);
        for (var c = 0; c < CuspCount; c++)
        {
            fields[i++] = D(cusp[c]);
        }
        for (var a = 0; a <= 9; a++)
        {
            fields[i++] = D(ascmc[a]);
        }
        return fields;
    }

    /// <summary>
    /// Deliberately violates the "fresh SwissEph per row" rule used everywhere
    /// else in this file -- on purpose, and only here. saved_sundec is dead code
    /// across every other row in the baseline: every one of them constructs a
    /// fresh instance, so saved_sundec is always its 99 default, and the "use the
    /// previously saved declination" branch in SweHouse.cs
    /// (<c>if (saved_sundec != 99) h.sundec = saved_sundec;</c>) never actually
    /// executes. Confirmed in the data: across all 1,008 AddSunshineStateRows
    /// rows, every sentinel-99 case resolves to 0, because there is never a prior
    /// call on the same instance to have stored anything else.
    ///
    /// The behavior that actually matters -- a real declination stored by one call
    /// and consumed by a later sentinel call on the SAME instance -- is exactly
    /// the shape where a C `static` and a C# instance field can diverge in the
    /// port, so it gets its own small, explicitly-named set of rows that share one
    /// instance across an ordered pair of calls. The "HSTATE" case-id prefix and
    /// the explicit call1/call2 field pairing make the ordering dependency visible
    /// in the data itself, not just in this comment.
    /// </summary>
    public static void AddStatefulPairRows(List<string> rows)
    {
        char[] sunHsys = ['I', 'i'];
        double[] geolats = [-60, 0, 60];
        (double Armc1, double Armc2)[] armcPairs = [(45, 225), (100, 300)];
        double[] realDecs = [15.5, -20.0];

        foreach (var hsys in sunHsys)
        {
            foreach (var eps in Grids.Eps)
            {
                foreach (var geolat in geolats)
                {
                    foreach (var (armc1, armc2) in armcPairs)
                    {
                        foreach (var realDec in realDecs)
                        {
                            rows.Add(BuildStatefulPairRow(hsys, eps, geolat, armc1, armc2, realDec));
                        }
                    }
                }
            }
        }
    }

    private static string BuildStatefulPairRow(char hsys, double eps, double geolat, double armc1, double armc2, double realDec)
    {
        var caseId = $"HSTATE|{hsys}|{D(eps)}|{D(geolat)}|{D(armc1)}|{D(armc2)}|{D(realDec)}";
        return SafeRow(caseId, () =>
        {
            // ONE shared instance for both calls -- this is the entire point of
            // the row. Do not "fix" this to a fresh instance per call; that would
            // silently turn these rows back into the same dead branch as
            // AddSunshineStateRows.
            using var swe = new SwissEph();

            var cusp1 = new double[40];
            var ascmc1 = new double[10];
            ascmc1[9] = realDec; // call 1: a real declination, populates the hidden saved_sundec field
            var retc1 = swe.swe_houses_armc(armc1, geolat, eps, hsys, cusp1, ascmc1);

            var cusp2 = new double[40];
            var ascmc2 = new double[10];
            ascmc2[9] = 99; // call 2, same instance: sentinel, must resolve via what call 1 stored
            var retc2 = swe.swe_houses_armc(armc2, geolat, eps, hsys, cusp2, ascmc2);

            return
            [
                I(retc1), D(ascmc1[9]), D(cusp1[1]), D(cusp1[10]),
                I(retc2), D(ascmc2[9]), D(cusp2[1]), D(cusp2[10]),
            ];
        });
    }
}
