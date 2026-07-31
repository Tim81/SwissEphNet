using SwissEphNet;

namespace BaselineMatrix;

/// <summary>
/// Always reports "file not found", so every SwissEph instance that inherits it (via
/// <see cref="SwissEph.DefaultFileProvider"/>) falls back to Moshier exactly as it did
/// when no OnLoadFile handler was ever subscribed. See docs/known-issues.md's OnLoadFile
/// entry: with SwissEph.OpenBinary defaulting to the real filesystem, a matrix area that
/// forgot to configure this would silently start reading whatever ephemeris files happen
/// to be present on the machine that runs it.
/// </summary>
public sealed class NoEphemerisFilesProvider : SwissEph.IEphemerisFileProvider
{
    public static readonly NoEphemerisFilesProvider Instance = new();

    public Stream Open(string path) => null!; // SwissEphNet itself is not nullable-annotated; the interface contract is "null means not found"
}

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
        ("houses-armc", rows => { Houses.AddRows(rows); Houses.AddSunshineStateRows(rows); Houses.AddStatefulPairRows(rows); }),
        ("houses", HousesEx.AddRows),
        ("house-pos", rows => { HousePos.AddRows(rows); HouseName.AddRows(rows); }),
        ("calc", Calc.AddRows),
        ("pheno", Pheno.AddRows),
        ("nodaps", NodAps.AddRows),
        ("ayanamsa", Ayanamsa.AddRows),
        ("datetime", DateTime_.AddRows),
        ("coord", CoordHelpers.AddRows),
        ("format", FormatHelpers.AddRows),
        ("misc", Misc.AddRows),
        ("pheno-ast", PhenoAst.AddRows),
        ("eclipse", Eclipse.AddRows),
        ("risetrans", RiseTrans.AddRows),
        ("atmo", Atmo.AddRows),
        ("orbit", Orbit.AddRows),
        ("gauquelin", Gauquelin.AddRows),
        ("astromodels", AstroModels.AddRows),
        ("calc-defaulteph", CalcDefaultEph.AddRows),
    ];

    /// <summary>Runs one area's generator and returns its rows, sorted deterministically.</summary>
    public static List<string> Generate(Action<List<string>> populate)
    {
        // Every new SwissEph() constructed anywhere in this matrix -- there are several
        // hundred call sites -- must never be able to read a real ephemeris file (see
        // NoEphemerisFilesProvider above). Setting the static default here, in the one
        // choke point every area's generation goes through, is structural rather than
        // relying on every caller (Tools/BaselineGen, Tools/BaselineVerify) to remember.
        SwissEph.DefaultFileProvider = NoEphemerisFilesProvider.Instance;
        var rows = new List<string>();
        populate(rows);
        rows.Sort(StringComparer.Ordinal);
        return rows;
    }
}
