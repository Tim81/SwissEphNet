namespace BaselineMatrix;

/// <summary>
/// The named areas the matrix is split into. Each area writes to its own file, so
/// a change confined to one function family (say, swe_calc) only rewrites that
/// area's file and leaves every other area byte-identical -- the whole point of
/// splitting what used to be a single 24 MB blob.
/// </summary>
public static class Areas
{
    public static readonly (string Name, Action<List<string>> Populate)[] All =
    [
        ("houses-armc", rows => { Houses.AddRows(rows); Houses.AddSunshineStateRows(rows); }),
        ("houses", HousesEx.AddRows),
        ("house-pos", rows => { HousePos.AddRows(rows); HouseName.AddRows(rows); }),
        ("calc", Calc.AddRows),
        ("pheno", Pheno.AddRows),
        ("ayanamsa", Ayanamsa.AddRows),
        ("datetime", DateTime_.AddRows),
        ("coord", CoordHelpers.AddRows),
        ("format", FormatHelpers.AddRows),
        ("misc", Misc.AddRows),
    ];

    /// <summary>Runs one area's generator and returns its rows, sorted deterministically.</summary>
    public static List<string> Generate(Action<List<string>> populate)
    {
        var rows = new List<string>();
        populate(rows);
        rows.Sort(StringComparer.Ordinal);
        return rows;
    }
}
