using System;
using System.IO;
using SwissEphNet;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>
/// Wires SwissEph's OnLoadFile event to the external/swisseph submodule's ephe/
/// directory (or an environment-variable override), and classifies iterations
/// whose data files this repo does not ship (JPL DE files, planetary-moon .se1
/// files) so the runner can mark them DATA-MISSING instead of running them.
/// </summary>
public static class EphemerisFileResolver
{
    /// <summary>Opt in to running SEFLG_JPLEPH iterations, provided a real DE file path is supplied.</summary>
    public static readonly bool IncludeJpl =
        Environment.GetEnvironmentVariable("SWISSEPH_CONFORMANCE_INCLUDE_JPL") == "1";

    public static readonly string? JplFilePath =
        Environment.GetEnvironmentVariable("SWISSEPH_CONFORMANCE_JPL_FILE");

    /// <summary>Opt in to running planetary-moon iterations (ipl 9000-9999), provided ephe/sat/ is available.</summary>
    public static readonly bool IncludeMoons =
        Environment.GetEnvironmentVariable("SWISSEPH_CONFORMANCE_INCLUDE_MOONS") == "1";

    public static void Attach(SwissEph swe)
    {
        swe.swe_set_ephe_path(RepoLocator.EpheDir);
        if (!string.IsNullOrEmpty(JplFilePath))
        {
            swe.swe_set_jpl_file(Path.GetFileName(JplFilePath));
        }

        swe.OnLoadFile += (_, e) =>
        {
            var candidate = e.FileName;
            if (!string.IsNullOrEmpty(JplFilePath) && string.Equals(Path.GetFileName(candidate), Path.GetFileName(JplFilePath), StringComparison.OrdinalIgnoreCase))
            {
                candidate = JplFilePath;
            }

            e.File = File.Exists(candidate) ? File.OpenRead(candidate) : null;
        };
    }

    /// <summary>SEFLG_JPLEPH set and we have nowhere to get a multi-hundred-MB DE file from.</summary>
    public static bool NeedsJplDataWeDoNotHave(int iephe) =>
        (iephe & SwissEph.SEFLG_JPLEPH) != 0 && (!IncludeJpl || string.IsNullOrEmpty(JplFilePath));

    /// <summary>Planetary-moon body (ipl 9000-9999) and we have not opted into ephe/sat/.</summary>
    public static bool NeedsMoonDataWeDoNotHave(int ipl) =>
        ipl is >= 9000 and < SwissEph.SE_AST_OFFSET && !IncludeMoons;

    /// <summary>
    /// SEFLG_SWIEPH requested for a date outside the era this repo's shipped core ephemeris
    /// files (sepl/semo/seas_12.se1 and _18.se1) cover.
    /// </summary>
    /// <remarks>
    /// Mirrors external/swisseph/swephlib.c:3610 <c>swi_gen_filename</c>'s file-naming logic:
    /// era files are named ..._&lt;icty&gt;, where icty is the calendar year's century floored
    /// to a multiple of <c>NCTIES</c> (sweph.h:249, 6 centuries = 600 years per file). "_12"
    /// covers years 1200-1799, "_18" covers 1800-2399 -- the two this repo ships (see
    /// README.md's "Correctness oracle" section and .github/workflows/conformance.yml's
    /// sparse-checkout list). Anything outside [1200, 2400) needs a different era file
    /// (e.g. "_06", "_00", "_m06", "_m24", ...) this repo does not ship. A request in that
    /// range still returns a numeric answer -- Swiss Ephemeris falls back to Moshier
    /// internally -- but emits a "using Moshier eph." warning in <c>serr</c> that a
    /// reference run with the full file set never sees, so the numbers can (and for
    /// delta-T, do not, since delta-T is not itself read from these files) still agree while
    /// <c>serr</c> does not. Found via suite 5 testcase 3 iteration 6 (JD 1173182.5, "18.12.-1501",
    /// requesting SEFLG_SWIEPH): passes with this repo's full, non-sparse submodule checkout
    /// (every era file present) and fails -- correctly, since the two environments now differ
    /// in exactly the ephemeris data available -- with only the declared 8-file core set.
    /// </remarks>
    public static bool NeedsEraFileWeDoNotShip(SwissEph swe, double jd)
    {
        var gregflag = jd >= 2305447.5 ? 1 : 0; // swi_gen_filename's own Julian/Gregorian switch (1582-10-15)
        int year = 0, month = 0, day = 0;
        double hour = 0;
        swe.swe_revjul(jd, gregflag, ref year, ref month, ref day, ref hour);
        return year < 1200 || year >= 2400;
    }
}
