using SwissEphNet;
using static BaselineGen.Format;

namespace BaselineGen;

/// <summary>
/// swe_get_ayanamsa / swe_get_ayanamsa_ut for every predefined sidereal mode
/// (0 .. SE_NSIDM_PREDEF-1), after swe_set_sid_mode. A handful of the predefined
/// modes are defined relative to a named fixed star and need sefstars.txt, which
/// is unavailable here (no OnLoadFile handler); whatever those modes currently
/// produce without the file is itself frozen behavior.
/// </summary>
internal static class Ayanamsa
{
    private static readonly double[] Jds = Grids.JdSpread(8);

    public static void AddRows(List<string> rows)
    {
        for (var sidMode = 0; sidMode < SwissEph.SE_NSIDM_PREDEF; sidMode++)
        {
            foreach (var jd in Jds)
            {
                rows.Add(BuildRow("AY", sidMode, jd, useUt: false));
                rows.Add(BuildRow("AYUT", sidMode, jd, useUt: true));
            }
        }
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
}
