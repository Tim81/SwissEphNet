namespace OracleVerify;

/// <summary>
/// Why a row in Tests/oracle/known-diff.tsv is allowed to differ -- see that file's own header
/// for the definition of each:
/// <list type="bullet">
/// <item><see cref="PortVersion"/>: the port is at 2.08, the C reference is 2.10.03. The work
/// queue; must shrink to zero as porting lands, never grow without a reviewed reason.</item>
/// <item><see cref="LibmResidual"/>: a difference traced to a named C runtime function with a
/// pinned maximum ULP. Never assigned automatically -- see <see cref="RowComparer"/>'s remarks --
/// only by a human who has actually traced the row to a specific function, which is why the
/// generator can only ever emit <see cref="PortVersion"/> or <see cref="Retc"/> on its own.</item>
/// <item><see cref="Retc"/>: the integer return code itself differs.</item>
/// <item><see cref="Serr"/>: the return code and every hex value column match, but the
/// <c>serr</c> error-message text does not -- e.g. a check-ordering divergence that reaches the
/// same numeric result by way of a different diagnostic message. See RowComparer's remarks for
/// why this is only ever assigned automatically when it is the sole difference; a row whose
/// retc or hex also differs is categorized by that, not by its serr text.</item>
/// </list>
/// </summary>
internal enum DiffCategory
{
    PortVersion,
    LibmResidual,
    Retc,
    Serr,
}

internal static class DiffCategoryNames
{
    public const string PortVersion = "PORT-VERSION";
    public const string LibmResidual = "LIBM-RESIDUAL";
    public const string Retc = "RETC";
    public const string Serr = "SERR";

    public static string ToName(DiffCategory category) => category switch
    {
        DiffCategory.PortVersion => PortVersion,
        DiffCategory.LibmResidual => LibmResidual,
        DiffCategory.Retc => Retc,
        DiffCategory.Serr => Serr,
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    public static DiffCategory Parse(string name) => name switch
    {
        PortVersion => DiffCategory.PortVersion,
        LibmResidual => DiffCategory.LibmResidual,
        Retc => DiffCategory.Retc,
        Serr => DiffCategory.Serr,
        _ => throw new FormatException($"Unknown category '{name}'."),
    };
}
