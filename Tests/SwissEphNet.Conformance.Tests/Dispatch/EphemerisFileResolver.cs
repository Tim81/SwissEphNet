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
    /// The equivalent of setest's suite-scope swe_set_jpl_file("de441.eph"), issued by
    /// suite_01_calc.c and suite_10_solcross.c (their SUITE_INIT blocks) immediately after their
    /// path reset. Upstream passed "de431.eph" until v2.10.04 (cb67a4a3).
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
            ? SetestJplFileName
            : Path.GetFileName(JplFilePath));
    }

    /// <summary>
    /// The file name setest's SUITE_INIT passes to swe_set_jpl_file: upstream's de441.eph since
    /// v2.10.04 (cb67a4a3), de431.eph before. ConformanceRunner checks this against the pinned
    /// suite_01_calc.c and suite_10_solcross.c, so it cannot silently go stale again.
    /// </summary>
    public const string SetestJplFileName = "de441.eph";

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

    // There is deliberately no "needs an era file we do not ship" check for SEFLG_SWIEPH any more.
    // An iteration dated outside 1200-2399 asks for an era file (sepl_06, seas_m06, ...) that the
    // declared 8-file set does not contain; upstream setest, run on that same set, does not skip
    // it either: swe_calc falls back to Moshier, writes the "using Moshier eph." and "not found in
    // PATH" text to serr, and the iteration is compared against t.exp like any other. The
    // comparison then fails on the data environment (the official t.exp was generated with every
    // era file), which is a VALUE-MISMATCH row triaged as DATA-ENV or SERR-PATH in
    // Tests/conformance/value-mismatch-triage.tsv, not a reason to never run the port. The 157 rows
    // the old check parked as DATA-MISSING were therefore running code hidden from the porting
    // queue; Suite05DateTime and Suite10Solcross now dispatch them.
}
