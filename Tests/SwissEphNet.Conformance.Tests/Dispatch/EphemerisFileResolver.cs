using System;
using System.Globalization;
using System.IO;
using SwissEphNet;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>
/// Points SwissEph's ephemeris path at the external/swisseph submodule's ephe/
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

    /// <summary>
    /// Opt in to running SEFLG_SWIEPH iterations for dates outside the shipped _12/_18 core era
    /// files, provided the specific era files those dates need are actually present under
    /// <see cref="RepoLocator.EpheDir"/>. See <see cref="NeedsEraFileWeDoNotShip"/>.
    /// </summary>
    public static readonly bool IncludeEra =
        Environment.GetEnvironmentVariable("SWISSEPH_CONFORMANCE_INCLUDE_ERA") == "1";

    /// <summary>
    /// Wires the file handler and establishes the ephemeris path, in that order.
    /// </summary>
    /// <remarks>
    /// The order used to be the other way round. swe_set_ephe_path is not a setter:
    /// sweph.c:1315-1350 closes every open file (swi_close_keep_topo_etc, :1323), then
    /// eagerly calls swe_calc(J2000, SE_MOON, SEFLG_SWIEPH|...) at :1347 and, if the lunar
    /// file opened, pins tidal acceleration from that file's DE number via swi_set_tid_acc
    /// at :1349. With the handler attached afterwards, that eager open could not reach a
    /// file, fptr stayed null, and tid_acc was never pinned there.
    ///
    /// Measured, this half of the change moves no iteration on its own -- the per-suite
    /// reset in ConformanceRunner is what fixes the 110 rows, and it fixes them with either
    /// attach order. The order is corrected because the sequence should mean what it says,
    /// not because it was the defect.
    /// </remarks>
    public static void Attach(SwissEph swe)
    {
        ResetEphePath(swe);
        SetJplFile(swe);
    }

    /// <summary>
    /// The equivalent of setest's suite-scope swe_set_jpl_file("de431.eph"), issued by
    /// suite_01_calc.c:11 and suite_10_solcross.c:11 immediately after their path reset.
    /// </summary>
    /// <remarks>
    /// Named rather than skipped even when no DE file is available, because the call has a
    /// side effect independent of whether the file exists: sweph.c:1481 routes it through
    /// swi_close_keep_topo_etc, which memsets swed.fidat (sweph.c:1205) and therefore
    /// clears fidat[SEI_FILE_MOON].sweph_denum -- the field calc_deltat reads at
    /// swephlib.c:2565 to resolve tid_acc. Skipping it left those suites holding the DE
    /// number that swe_set_ephe_path's eager lunar open had just established, where setest
    /// enters them with it cleared.
    /// </remarks>
    public static void SetJplFile(SwissEph swe)
    {
        swe.swe_set_jpl_file(string.IsNullOrEmpty(JplFilePath)
            ? "de431.eph"
            : Path.GetFileName(JplFilePath));
    }

    /// <summary>
    /// The equivalent of setest's swe_set_ephe_path(NULL). Every suite file issues one at
    /// suite scope except suite_03_misc.c, which issues none; suite_01_calc.c and
    /// suite_02_fixstar.c additionally repeat it inside some testcase bodies, which run per
    /// iteration. ConformanceRunner has the exact placement.
    /// </summary>
    /// <remarks>
    /// Passing the resolved directory rather than null is deliberate, and null would be
    /// wrong here rather than merely different. This port does not read $SE_EPHE_PATH
    /// (CPort/Sweph.cs, commented out) and defines SE_EPHE_PATH as the placeholder
    /// "[ephe]" (SwissEph.swephexp.h.cs), so null would set swed.ephepath to a location
    /// holding nothing, the eager lunar open would find no file, and tid_acc would never
    /// pin -- reintroducing what this is meant to avoid. t.exp was generated with
    /// SE_EPHE_PATH pointing at Astrodienst's real files, so a real directory is the closer
    /// reproduction. It cannot mask a failure either: it only makes files reachable, and
    /// exactly which files are reachable is asserted by EphemerisManifest.
    ///
    /// When a JPL DE file path is supplied (<see cref="JplFilePath"/>) and lives outside
    /// <see cref="RepoLocator.EpheDir"/>, its directory is appended as a second search
    /// entry -- swi_fopen tries every PATH_SEPARATOR-delimited entry in order (sweph.c:2374
    /// -2395), so this reaches the file by its own name without needing a
    /// SwissEph.IEphemerisFileProvider to redirect a mismatched path, the way this used to
    /// work through OnLoadFile.
    /// </remarks>
    public static void ResetEphePath(SwissEph swe)
    {
        var path = RepoLocator.EpheDir;
        if (!string.IsNullOrEmpty(JplFilePath))
        {
            var jplDir = Path.GetDirectoryName(Path.GetFullPath(JplFilePath));
            if (!string.IsNullOrEmpty(jplDir) && !string.Equals(jplDir, Path.GetFullPath(RepoLocator.EpheDir).TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase))
            {
                path = path + SwissEph.PATH_SEPARATOR[0] + jplDir;
            }
        }
        swe.swe_set_ephe_path(path);
    }

    /// <summary>SEFLG_JPLEPH set and we have nowhere to get a multi-hundred-MB DE file from.</summary>
    public static bool NeedsJplDataWeDoNotHave(int iephe) =>
        (iephe & SwissEph.SEFLG_JPLEPH) != 0 && (!IncludeJpl || string.IsNullOrEmpty(JplFilePath));

    /// <summary>Planetary-moon body (ipl 9000-9999) and we have not opted into ephe/sat/.</summary>
    public static bool NeedsMoonDataWeDoNotHave(int ipl) =>
        ipl is >= 9000 and < SwissEph.SE_AST_OFFSET && !IncludeMoons;

    /// <summary>
    /// SEFLG_CENTER_BODY requested and we have not opted into ephe/sat/.
    /// </summary>
    /// <remarks>
    /// swi_gen_filename's own asteroid-or-planetary-moon default branch (external/swisseph/
    /// swephlib.c:3639-3644) routes any SEI_* planet number in (SE_PLMOON_OFFSET, SE_AST_OFFSET)
    /// to "sat&lt;DIR_GLUE&gt;sepm&lt;N&gt;.se1" -- the same ephe/sat/ directory
    /// <see cref="NeedsMoonDataWeDoNotHave"/> already gates on. SEFLG_CENTER_BODY (center-of-body
    /// correction) reads that same per-planet sepm9Nxx.se1 record even for a major-planet ipl
    /// like Jupiter (5) that is nowhere near the 9000-9999 range itself, so a plain ipl check
    /// alone misses it: found via known-fail.tsv suite 1 testcase 1 iterations 91-128 (Jupiter
    /// through Pluto, SEFLG_CENTER_BODY, citing sepm9599.se1 through sepm9999.se1 -- none of
    /// which required an in-range ipl at all) reporting VALUE-MISMATCH when both the port and a
    /// fresh MSVC build of Astrodienst's own 2.10.03 C return an identical "file not found" serr,
    /// which is DATA-MISSING, not a value mismatch (regenerations.log's Phase 6 probe entry notes
    /// the same directory already flips these once ephe/sat/ is actually present, without needing
    /// SWISSEPH_CONFORMANCE_INCLUDE_MOONS at all -- this checks the flag that predicts it instead
    /// of waiting for the data to be present to notice).
    /// </remarks>
    public static bool NeedsCenterBodySatFileWeDoNotHave(int iflag) =>
        (iflag & SwissEph.SEFLG_CENTER_BODY) != 0 && !IncludeMoons;

    /// <summary>
    /// A numbered asteroid beyond the four with built-in orbital elements (Ceres/Pallas/Juno/
    /// Vesta, ipl SE_AST_OFFSET+1..+4, which read from the shipped sepl_NN.se1 main-planet file)
    /// -- these need their own per-asteroid file, which this repo never ships at any tier.
    /// </summary>
    /// <remarks>
    /// external/swisseph/swephlib.c:3639-3649's swi_gen_filename resolves any ipli beyond
    /// SE_AST_OFFSET+4 (Sweph.cs:1213's own boundary for the built-in four) to
    /// "ast&lt;N/1000&gt;&lt;DIR_GLUE&gt;se&lt;N%05d&gt;s.se1" -- a directory this repo's
    /// required-ephemeris-files.tsv core set has never contained a single file from (verified:
    /// zero "se*s.se1" or "ast*" entries in that list or in external/swisseph/ephe/). Unlike
    /// <see cref="NeedsJplDataWeDoNotHave"/> and <see cref="NeedsMoonDataWeDoNotHave"/>, this
    /// carries no opt-in env var: nothing in this repo's toolchain currently fetches or verifies
    /// per-asteroid files the way SWISSEPH_CONFORMANCE_JPL_FILE or ephe/sat/ do, so unlike those
    /// two there is no "provide the file and opt in" path to wire up yet. Found via known-fail.tsv
    /// suite 1 testcases 1/5, suite 4 testcase 1 and suite 7 testcase 3 all citing
    /// se00433s.se1 (433 Eros) or se00010s.se1 (10 Hygiea) as VALUE-MISMATCH when both the port
    /// and a fresh MSVC build of Astrodienst's own 2.10.03 C return an identical "file not found"
    /// serr, which is DATA-MISSING, not a value mismatch -- the same shape as the already-known
    /// suite 4 Eros row this method's sibling checks were modeled on, just not previously routed
    /// through any DataMissing check at all (this ipl range predates every existing check here).
    /// </remarks>
    public static bool NeedsAsteroidFileWeDoNotShip(int ipl) =>
        ipl > SwissEph.SE_AST_OFFSET + 4;

    /// <summary>
    /// SEFLG_SWIEPH requested for a date outside the era this repo's shipped core ephemeris
    /// files (sepl/semo/seas_12.se1 and _18.se1) cover, and either <see cref="IncludeEra"/> is
    /// not set or the specific era files that date needs are not actually present.
    /// </summary>
    /// <remarks>
    /// Mirrors external/swisseph/swephlib.c:3610 <c>swi_gen_filename</c>'s file-naming logic:
    /// era files are named ..._&lt;icty&gt;, where icty is the calendar year's century floored
    /// to a multiple of <c>NCTIES</c> (sweph.h:249, 6 centuries = 600 years per file). "_12"
    /// covers years 1200-1799, "_18" covers 1800-2399 -- the two this repo ships by default
    /// (see README.md's "Correctness oracle" section and .github/workflows/conformance.yml's
    /// sparse-checkout list). Anything outside [1200, 2400) needs a different era file
    /// (e.g. "_06", "_00", "_m06", "_m24", ...) this repo does not ship by default. A request
    /// in that range still returns a numeric answer -- Swiss Ephemeris falls back to Moshier
    /// internally -- but emits a "using Moshier eph." warning in <c>serr</c> that a
    /// reference run with the full file set never sees, so the numbers can (and for
    /// delta-T, do not, since delta-T is not itself read from these files) still agree while
    /// <c>serr</c> does not. Found via suite 5 testcase 3 iteration 6 (JD 1173182.5, "18.12.-1501",
    /// requesting SEFLG_SWIEPH): passes with this repo's full, non-sparse submodule checkout
    /// (every era file present) and fails -- correctly, since the two environments now differ
    /// in exactly the ephemeris data available -- with only the declared 8-file core set.
    ///
    /// Until this method carried an opt-in and a real file check, it was a bare year range test:
    /// every date outside [1200, 2400) reported DATA-MISSING unconditionally, with no way to ask
    /// it "but what if the file were actually there". A Phase 6 probe (dated 2026-07-31 in
    /// Tests/conformance/regenerations.log) widened the checkout to all 150 era files, set a
    /// then-temporary bypass, and re-ran the corpus: of 157 SEFLG_SWIEPH rows this method routed
    /// to DATA-MISSING, zero passed once the files were actually present. They are genuine
    /// VALUE-MISMATCH rows wearing a DATA-MISSING label, which hid them from
    /// ConformanceReport.IsActionable's porting queue. This method's shape now matches
    /// <see cref="NeedsJplDataWeDoNotHave"/>'s (opt-in and a real file check, not just an
    /// opt-in): with <see cref="IncludeEra"/> unset (the default), behavior is unchanged from
    /// before -- every out-of-range date still reports true, since the files genuinely are not
    /// shipped by default. Opting in only changes the outcome for a checkout that actually has
    /// the file swi_gen_filename would resolve to for that date; it does not make this method
    /// optimistic about data it cannot see.
    /// </remarks>
    public static bool NeedsEraFileWeDoNotShip(SwissEph swe, double jd)
    {
        var gregflag = jd >= 2305447.5 ? 1 : 0; // swi_gen_filename's own Julian/Gregorian switch (1582-10-15)
        int year = 0, month = 0, day = 0;
        double hour = 0;
        swe.swe_revjul(jd, gregflag, ref year, ref month, ref day, ref hour);
        if (year >= 1200 && year < 2400)
        {
            return false; // covered by the shipped _12/_18 core files regardless of IncludeEra
        }

        if (!IncludeEra)
        {
            return true;
        }

        return !EraFilesExist(year);
    }

    /// <summary>
    /// True when every one of the three era files (sepl, semo, seas) swi_gen_filename would
    /// resolve <paramref name="year"/> to is present under <see cref="RepoLocator.EpheDir"/>.
    /// All three, not just the one a specific testcase's body happens to need: this method has
    /// no ipl to work from (<see cref="NeedsEraFileWeDoNotShip"/> is called before the body is
    /// known, from suite dispatch code shared across every body a testcase might exercise), and
    /// era files ship in same-century triples in practice -- the Phase 6 probe this documents
    /// widened the checkout to all 150 that way, not per body.
    /// </summary>
    private static bool EraFilesExist(int year)
    {
        var suffix = EraFileSuffix(year);
        foreach (var prefix in EraFilePrefixes)
        {
            if (!File.Exists(Path.Combine(RepoLocator.EpheDir, prefix + suffix + ".se1")))
            {
                return false;
            }
        }

        return true;
    }

    private static readonly string[] EraFilePrefixes = ["sepl", "semo", "seas"];

    /// <summary>
    /// Port of external/swisseph/swephlib.c:3663-3684's icty computation and "_&lt;icty&gt;" /
    /// "m&lt;icty&gt;" suffix formatting -- the part of swi_gen_filename that does not depend on
    /// which body ipl names (that part only selects the "sepl"/"semo"/"seas" prefix, handled by
    /// <see cref="EraFilePrefixes"/> instead). C's truncating integer division and modulo match
    /// C#'s for this arithmetic, so the port is direct rather than needing Euclidean division.
    /// </summary>
    private static string EraFileSuffix(int year)
    {
        const int Ncties = 6; // sweph.h:249 NCTIES
        var sgn = year < 0 ? -1 : 1;
        var icty = year / 100;
        if (sgn < 0 && year % 100 != 0)
        {
            icty -= 1;
        }
        while (icty % Ncties != 0)
        {
            icty--;
        }

        var prefix = icty < 0 ? "m" : "_";
        icty = Math.Abs(icty);
        return prefix + icty.ToString("D2", CultureInfo.InvariantCulture);
    }
}
