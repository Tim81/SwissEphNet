using SwissEphNet;
using static BaselineMatrix.Format;

namespace BaselineMatrix;

/// <summary>swe_get_planet_name, swe_version, swe_get_current_file_data.</summary>
internal static class Misc
{
    // SE_SUN .. SE_INTP_PERG (0..22, SE_NPLANETS values), plus a spread of the
    // Uranian/fictitious bodies and the special ipl values ECL_NUT and FIXSTAR.
    private static readonly int[] PlanetIds =
    [
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22,
        SwissEph.SE_CUPIDO, SwissEph.SE_HADES, SwissEph.SE_ZEUS, SwissEph.SE_KRONOS,
        SwissEph.SE_APOLLON, SwissEph.SE_ADMETOS, SwissEph.SE_VULKANUS, SwissEph.SE_POSEIDON,
        SwissEph.SE_ECL_NUT, SwissEph.SE_FIXSTAR,
    ];

    public static void AddRows(List<string> rows)
    {
        foreach (var ipl in PlanetIds)
        {
            var caseId = $"PN|{I(ipl)}";
            rows.Add(SafeRow(caseId, () =>
            {
                using var swe = new SwissEph();
                return [S(swe.swe_get_planet_name(ipl))];
            }));
        }

        rows.Add(SafeRow("VER|1", () =>
        {
            using var swe = new SwissEph();
            return [S(swe.swe_version())];
        }));

#if !USE_REFERENCE_PACKAGE
        // swe_get_current_file_data: previously uncovered anywhere in this matrix
        // (docs/known-issues.md, "31 of 107 public swe_* entry points have no matrix
        // coverage"). Under this harness's NoEphemerisFilesProvider (Areas.cs) no file is
        // ever actually open, so ifno 0..4 all resolve through the same "no file open"
        // branch (Sweph.cs's swe_get_current_file_data: swed.fidat[ifno].fnam is always
        // empty) -- a one-row-per-ifno addition, but the "no file open" response itself is
        // behavior worth freezing, and ifno -1/5 exercise the out-of-range guard.
        //
        // Guarded out under USE_REFERENCE_PACKAGE: this function does not exist in the
        // reference package (SwissEphNet 2.8.0.2, pre-2.10.03) at all, so reference mode's
        // "compile-only regression guard" build has no such method to call.
        foreach (var ifno in new[] { -1, 0, 1, 2, 3, 4, 5 })
        {
            var caseId = $"CFD|{I(ifno)}";
            rows.Add(SafeRow(caseId, () =>
            {
                using var swe = new SwissEph();
                double tfstart = 0, tfend = 0;
                int denum = 0;
                var fname = swe.swe_get_current_file_data(ifno, ref tfstart, ref tfend, ref denum);
                return [S(fname), D(tfstart), D(tfend), I(denum)];
            }));
        }
#endif
    }
}
