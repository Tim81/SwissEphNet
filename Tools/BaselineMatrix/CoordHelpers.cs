using SwissEphNet;
using static BaselineMatrix.Format;

namespace BaselineMatrix;

/// <summary>swe_cotrans, swe_cotrans_sp, swe_azalt, swe_azalt_rev.</summary>
internal static class CoordHelpers
{
    private static readonly double[] Lons = [0, 90, 180, 270, 359.999];
    private static readonly double[] Lats = [-89, -45, 0, 45, 89];
    private static readonly double[] Epsilons = [23.4392911, 0.0, 40.0];

    // swe_cotrans forces xpn[2] = xpo[2] (sweph.c's swe_cotrans just copies the
    // radius component through verbatim; only xpn[0]/xpn[1] are recomputed from
    // the rotation) -- swe_cotrans_sp does the same for both xpo[2] and xpo[5].
    // AddCotrans/AddCotransSp below always pass 1.0, so that passthrough was
    // never actually exercised: a broken copy (e.g. a hardcoded 1.0 written back
    // instead of the real xpo[2]) would pass unnoticed as long as the caller also
    // happened to pass 1.0. This dedicated sweep varies the radius so the
    // passthrough itself is observed, without touching AddCotrans/AddCotransSp's
    // own existing rows (a distinct "CTR"/"CTSR" case id prefix, added
    // alongside them, keeps every pre-existing "CT"/"CTS" row byte-identical).
    private static readonly double[] Radii = [1.0, 2.5];

    public static void AddRows(List<string> rows)
    {
        AddCotrans(rows);
        AddCotransSp(rows);
        AddCotransRadius(rows);
        AddCotransSpRadius(rows);
        AddAzalt(rows);
        AddAzaltRev(rows);
        CheckRadiusPassthroughReached(rows);
    }

    // The CTR/CTSR sweeps exist for one behavior and one only: swe_cotrans copying
    // xpo[2] into xpn[2] verbatim, and swe_cotrans_sp doing the same for xpo[2] and
    // xpo[5]. That copy has no observable effect unless two rows pass different radii,
    // which is why the whole area proved nothing about it while every caller passed
    // 1.0 -- "copy the caller's radius" and "write a hardcoded 1.0" produce the same
    // 100 rows. Adding the sweeps fixed that, but nothing yet stopped a later edit
    // from folding a constant back into the case body (leaving the case id still
    // parameterized by radius) or trimming Radii to a single entry: either one puts
    // the area straight back into the state it was added to get out of, with the same
    // row count, the same case ids and a green gate.
    //
    // So assert the evidence rather than the row: for each (lon, lat), the rows for
    // the different radii must not be byte-identical. This runs before any of this
    // area's rows reach a file (see Reachability's doc comment).
    private static void CheckRadiusPassthroughReached(List<string> rows)
    {
        if (Radii.Length < 2)
        {
            throw new InvalidOperationException(
                "CTR/CTSR sweeps need at least 2 radii to make the radius passthrough observable at all, but Radii " +
                $"has {Radii.Length}. With a single radius the sweep still emits every row it always did, and not one " +
                "of them distinguishes a real passthrough from a hardcoded constant -- exactly the blind spot these " +
                "two sweeps were added to close.");
        }

        var index = Reachability.IndexPayloads(rows);
        foreach (var lon in Lons)
        {
            foreach (var lat in Lats)
            {
                Reachability.RequireDistinctPayloads(
                    index,
                    "CTR",
                    "swe_cotrans's radius passthrough (xpn[2] = xpo[2])",
                    "Reaching it requires the radius handed to swe_cotrans to be the one named in the case id, not a constant.",
                    [.. Radii.Select(radius => $"CTR|{D(lon)}|{D(lat)}|{D(radius)}")]);

                // Per field, not per row. swe_cotrans_sp copies two fields, xpn[2] from xpo[2]
                // and xpn[5] from xpo[5], and this sweep derives both from the same radius
                // (xpo[5] is radius * 0.01, just above). A whole-row comparison is therefore
                // satisfied by either one on its own: hardcoding xpn[2] to a constant while
                // leaving xpo[5] alone leaves the rows still distinct, the check silent, and
                // half the branch this sweep exists for unreached. Measured, not supposed.
                //
                // Two calls rather than one so a failure names which field regressed. The
                // committed baseline would also catch that particular mutation today, but
                // leaning on it is the mistake this whole check exists to stop: the baseline
                // stops objecting the moment it is regenerated, which is how all three sweeps
                // came to be unreachable in the first place.
                var ctsrIds = Radii.Select(radius => $"CTSR|{D(lon)}|{D(lat)}|{D(radius)}").ToArray();
                Reachability.RequireDistinctFields(
                    index,
                    "CTSR",
                    "swe_cotrans_sp's position radius passthrough (xpn[2] = xpo[2])",
                    "Reaching it requires the radius handed to swe_cotrans_sp to be the one named in the case id, not a constant.",
                    2,
                    ctsrIds);
                Reachability.RequireDistinctFields(
                    index,
                    "CTSR",
                    "swe_cotrans_sp's speed radius passthrough (xpn[5] = xpo[5])",
                    "Reaching it requires the speed radius handed to swe_cotrans_sp to vary with the case id's radius, not a constant.",
                    5,
                    ctsrIds);
            }
        }
    }

    private static void AddCotrans(List<string> rows)
    {
        foreach (var lon in Lons)
        {
            foreach (var lat in Lats)
            {
                foreach (var eps in Epsilons)
                {
                    var caseId = $"CT|{D(lon)}|{D(lat)}|{D(eps)}";
                    rows.Add(SafeRow(caseId, () =>
                    {
                        using var swe = new SwissEph();
                        double[] xpo = [lon, lat, 1.0];
                        var xpn = new double[3];
                        swe.swe_cotrans(xpo, xpn, eps);
                        return [D(xpn[0]), D(xpn[1]), D(xpn[2])];
                    }));
                }
            }
        }
    }

    private static void AddCotransSp(List<string> rows)
    {
        foreach (var lon in Lons)
        {
            foreach (var lat in Lats)
            {
                foreach (var eps in Epsilons)
                {
                    var caseId = $"CTS|{D(lon)}|{D(lat)}|{D(eps)}";
                    rows.Add(SafeRow(caseId, () =>
                    {
                        using var swe = new SwissEph();
                        double[] xpo = [lon, lat, 1.0, 1.0, 0.1, 0.01];
                        var xpn = new double[6];
                        swe.swe_cotrans_sp(xpo, xpn, eps);
                        return [D(xpn[0]), D(xpn[1]), D(xpn[2]), D(xpn[3]), D(xpn[4]), D(xpn[5])];
                    }));
                }
            }
        }
    }

    private static void AddCotransRadius(List<string> rows)
    {
        foreach (var lon in Lons)
        {
            foreach (var lat in Lats)
            {
                foreach (var radius in Radii)
                {
                    var caseId = $"CTR|{D(lon)}|{D(lat)}|{D(radius)}";
                    rows.Add(SafeRow(caseId, () =>
                    {
                        using var swe = new SwissEph();
                        double[] xpo = [lon, lat, radius];
                        var xpn = new double[3];
                        swe.swe_cotrans(xpo, xpn, Epsilons[0]);
                        return [D(xpn[0]), D(xpn[1]), D(xpn[2])];
                    }));
                }
            }
        }
    }

    private static void AddCotransSpRadius(List<string> rows)
    {
        foreach (var lon in Lons)
        {
            foreach (var lat in Lats)
            {
                foreach (var radius in Radii)
                {
                    var caseId = $"CTSR|{D(lon)}|{D(lat)}|{D(radius)}";
                    rows.Add(SafeRow(caseId, () =>
                    {
                        using var swe = new SwissEph();
                        double[] xpo = [lon, lat, radius, 1.0, 0.1, radius * 0.01];
                        var xpn = new double[6];
                        swe.swe_cotrans_sp(xpo, xpn, Epsilons[0]);
                        return [D(xpn[0]), D(xpn[1]), D(xpn[2]), D(xpn[3]), D(xpn[4]), D(xpn[5])];
                    }));
                }
            }
        }
    }

    private static readonly double[] AzaltJds = Grids.JdSpread(4);
    private static readonly (string Name, int Flag)[] AzaltDirections =
        [("ECL2HOR", SwissEph.SE_ECL2HOR), ("EQU2HOR", SwissEph.SE_EQU2HOR)];
    private static readonly double[][] Geoposes =
        [[0, 45, 0], [-73.5, 40.7, 10], [0, 89, 0]];
    private static readonly double[][] AzaltXins =
        [[0, 0], [90, 45], [270, -30]];

    private static void AddAzalt(List<string> rows)
    {
        foreach (var jd in AzaltJds)
        {
            foreach (var (dirName, dir) in AzaltDirections)
            {
                foreach (var geopos in Geoposes)
                {
                    foreach (var xin in AzaltXins)
                    {
                        var caseId = $"AZ|{D(jd)}|{dirName}|{D(geopos[0])},{D(geopos[1])},{D(geopos[2])}|{D(xin[0])},{D(xin[1])}";
                        rows.Add(SafeRow(caseId, () =>
                        {
                            using var swe = new SwissEph();
                            var xaz = new double[3];
                            swe.swe_azalt(jd, dir, geopos, 1013.25, 15.0, xin, xaz);
                            return [D(xaz[0]), D(xaz[1]), D(xaz[2])];
                        }));
                    }
                }
            }
        }
    }

    private static readonly (string Name, int Flag)[] AzaltRevDirections =
        [("HOR2ECL", SwissEph.SE_HOR2ECL), ("HOR2EQU", SwissEph.SE_HOR2EQU)];
    private static readonly double[][] AzaltRevXins =
        [[0, 0], [90, 45], [270, -30]];

    private static void AddAzaltRev(List<string> rows)
    {
        foreach (var jd in AzaltJds)
        {
            foreach (var (dirName, dir) in AzaltRevDirections)
            {
                foreach (var geopos in Geoposes)
                {
                    foreach (var xin in AzaltRevXins)
                    {
                        var caseId = $"AZR|{D(jd)}|{dirName}|{D(geopos[0])},{D(geopos[1])},{D(geopos[2])}|{D(xin[0])},{D(xin[1])}";
                        rows.Add(SafeRow(caseId, () =>
                        {
                            using var swe = new SwissEph();
                            var xout = new double[2];
                            swe.swe_azalt_rev(jd, dir, geopos, xin, xout);
                            return [D(xout[0]), D(xout[1])];
                        }));
                    }
                }
            }
        }
    }
}
