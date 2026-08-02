namespace OracleVerify;

/// <summary>
/// The value-field names for each func a dump row can carry, in on-disk column order --
/// see Tools/OracleDump/Program.cs's and Tools/CReference/sedump.c's shared header comments for
/// why CALC/CALC_UT always emit xx[0..5] and HOUSES/HOUSES_ARMC always emit cusp[0..36] then
/// ascmc[0..9], regardless of which house system or iflag a given row used.
///
/// FIXSTAR/FIXSTARUT/FIXSTAR2/FIXSTAR2UT (Tools/OracleGrid/grid-files.tsv) share the CALC/CALCUT
/// shape -- swe_fixstar and friends fill the same xx[0..5] output. FIXSTARMAG carries a single
/// value (the magnitude). NAME (swe_get_planet_name) carries none at all: the returned string
/// has no double to hex-encode, so it is written into the row's err column instead -- see
/// gen-grid-files.ps1's header for why that is the right column for it, not a workaround.
///
/// SOLCROSS/SOLCROSSUT/MOONCROSS/MOONCROSSUT/HELIOCROSS/HELIOCROSSUT carry a single value, the
/// crossing time (jd_cross) -- see Tools/CReference/sedump.c's own top-of-file comment ("THE
/// CROSSING FUNCTIONS' retc COLUMN") for why swe_helio_cross's real int32 retc and the other
/// five's synthetic one both land in the shared retc column rather than as a value field.
/// MOONCROSSNODE/MOONCROSSNODEUT carry three: jd_cross, then the ecliptic longitude/latitude
/// (xlon, xlat) swe_mooncross_node(_ut) writes through its own output parameters at the crossing.
///
/// AYANAMSA/AYANAMSAEX/AYANAMSAEXUT (Tools/OracleGrid/grid-analytic.tsv only) carry a single
/// value, the ayanamsa itself (daya for the _EX/_EX_UT forms, the bare return value for plain
/// AYANAMSA) -- see gen-grid-analytic.ps1's header for why these three func tokens exist and
/// Tools/CReference/sedump.c's process_ayanamsa/process_ayanamsa_ex for why AYANAMSA's own err
/// column is always empty (swe_get_ayanamsa has no serr parameter) rather than repurposed the way
/// NAME's is.
///
/// HOUSESEX (both grids) shares HOUSES/HOUSESARMC's cusp[0..36]+ascmc[0..9] shape -- it is the
/// sidereal/radians-capable sibling of swe_houses, with the same fixed column count regardless of
/// house system. AYANAMSAUT (grid-analytic.tsv only) carries a single value like AYANAMSA, the UT
/// sibling of swe_get_ayanamsa. SIDTIME (grid-analytic.tsv only) carries a single value, the
/// sidereal time itself. AZALT (grid-analytic.tsv only) carries three: swe_azalt's xaz[0..2]
/// (azimuth, true altitude, apparent altitude). HOUSENAME (grid-analytic.tsv only) carries none
/// at all, the same reason NAME does not: swe_house_name returns a string, not a double, so its
/// returned name is written into the err column instead -- see
/// Tools/CReference/sedump.c's process_house_name. NODAPSUT (both grids) carries 24: swe_nod_aps_ut's
/// four six-double output arrays (xnasc, xndsc, xperi, xaphe), in that order.
/// </summary>
internal static class FieldLabels
{
    private static readonly string[] XxLabels = BuildLabels("xx", 6);
    private static readonly string[] HouseLabels = BuildLabels("cusp", 37)
        .Concat(BuildLabels("ascmc", 10))
        .ToArray();
    private static readonly string[] MagLabels = ["mag"];
    private static readonly string[] NameLabels = [];
    private static readonly string[] JdCrossLabels = ["jd_cross"];
    private static readonly string[] MoonCrossNodeLabels = ["jd_cross", "xlon", "xlat"];
    private static readonly string[] AyanamsaLabels = ["ayanamsa"];
    private static readonly string[] AyanamsaExLabels = ["daya"];
    private static readonly string[] SidtimeLabels = ["tsid"];
    private static readonly string[] AzAltLabels = BuildLabels("xaz", 3);
    private static readonly string[] NodApsLabels = BuildLabels("xnasc", 6)
        .Concat(BuildLabels("xndsc", 6))
        .Concat(BuildLabels("xperi", 6))
        .Concat(BuildLabels("xaphe", 6))
        .ToArray();

    public static IReadOnlyList<string> For(string func, string caseId) => func switch
    {
        "CALC" or "CALCUT" => XxLabels,
        "HOUSES" or "HOUSESARMC" or "HOUSESEX" => HouseLabels,
        "FIXSTAR" or "FIXSTARUT" or "FIXSTAR2" or "FIXSTAR2UT" => XxLabels,
        "FIXSTARMAG" => MagLabels,
        "NAME" or "HOUSENAME" => NameLabels,
        "SOLCROSS" or "SOLCROSSUT" or "MOONCROSS" or "MOONCROSSUT" or "HELIOCROSS" or "HELIOCROSSUT" => JdCrossLabels,
        "MOONCROSSNODE" or "MOONCROSSNODEUT" => MoonCrossNodeLabels,
        "AYANAMSA" or "AYANAMSAUT" => AyanamsaLabels,
        "AYANAMSAEX" or "AYANAMSAEXUT" => AyanamsaExLabels,
        "SIDTIME" => SidtimeLabels,
        "AZALT" => AzAltLabels,
        "NODAPSUT" => NodApsLabels,
        _ => throw new FormatException($"case {caseId}: unrecognized func token '{func}' at the start of case_id."),
    };

    private static string[] BuildLabels(string name, int count)
    {
        var labels = new string[count];
        for (var i = 0; i < count; i++)
        {
            labels[i] = $"{name}[{i}]";
        }
        return labels;
    }
}
