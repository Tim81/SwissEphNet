using SwissEphNet;
using static BaselineMatrix.Format;

namespace BaselineMatrix;

/// <summary>swe_get_planet_name and swe_version.</summary>
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
    }
}
