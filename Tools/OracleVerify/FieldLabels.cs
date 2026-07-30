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
/// </summary>
internal static class FieldLabels
{
    private static readonly string[] XxLabels = BuildLabels("xx", 6);
    private static readonly string[] HouseLabels = BuildLabels("cusp", 37)
        .Concat(BuildLabels("ascmc", 10))
        .ToArray();
    private static readonly string[] MagLabels = ["mag"];
    private static readonly string[] NameLabels = [];

    public static IReadOnlyList<string> For(string func, string caseId) => func switch
    {
        "CALC" or "CALCUT" => XxLabels,
        "HOUSES" or "HOUSESARMC" => HouseLabels,
        "FIXSTAR" or "FIXSTARUT" or "FIXSTAR2" or "FIXSTAR2UT" => XxLabels,
        "FIXSTARMAG" => MagLabels,
        "NAME" => NameLabels,
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
