using System;
using System.Globalization;
using System.IO;
using SwissEphNet;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>
/// Points SwissEph's ephemeris path at the external/swisseph submodule's ephe/
/// directory (or an environment-variable override) and mirrors setest's own file
/// handling. It classifies nothing: an iteration that asks for a file the 8
/// declared ones do not contain runs anyway and compares what the library does
/// without it, as setest does.
/// </summary>
public static class EphemerisFileResolver
{
    /// <summary>
    /// Optional path to a real JPL DE file (SWISSEPH_CONFORMANCE_JPL_FILE). Unset by default and in CI:
    /// SEFLG_JPLEPH iterations then run without it, take the library's own fallback and compare the
    /// "not found" text in serr like any other iteration. Set it to run them against a real file.
    /// </summary>
    public static readonly string? JplFilePath =
        Environment.GetEnvironmentVariable("SWISSEPH_CONFORMANCE_JPL_FILE");

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

    // There is deliberately no "needs a file we do not ship" check for any ephemeris request, and
    // there used to be four (SEFLG_JPLEPH, planetary moons 9000-9999, numbered asteroids beyond
    // Ceres/Pallas/Juno/Vesta, SEFLG_CENTER_BODY), which parked 1,101 iterations as DATA-MISSING
    // without running them. Upstream setest skips nothing: on the same 8 files the C runs every one
    // of them, swe_calc takes its fallback and writes the "not found" text to serr, and the
    // iteration is compared against t.exp like any other. Measured against a C-generated corpus of
    // the same iterations (setest built from the pinned tag, run on the 8 files): 595 of the 1,101
    // pass outright, 458 differ only in the ephemeris directory inside serr, and 48 differ in values
    // of kinds the other triaged rows already show. Those that differ are VALUE-MISMATCH rows
    // triaged in Tests/conformance/value-mismatch-triage.tsv, which is where a difference from
    // t.exp (generated with every data file present) belongs.
    //
    // There is deliberately no "needs an era file we do not ship" check for SEFLG_SWIEPH any more.
    // An iteration dated outside 1200-2399 asks for an era file (sepl_06, seas_m06, ...) that the
    // declared 8-file set does not contain; upstream setest, run on that same set, does not skip
    // it either: swe_calc falls back to Moshier, writes the "using Moshier eph." and "not found in
    // PATH" text to serr, and the iteration is compared against t.exp like any other. The
    // comparison then fails on the data environment (the official t.exp was generated with every
    // era file), which is a VALUE-MISMATCH row triaged as SERR-PATH or LIBM-PLATFORM in
    // Tests/conformance/value-mismatch-triage.tsv, not a reason to never run the port. The 157 rows
    // the old check parked as DATA-MISSING were therefore running code hidden from the porting
    // queue; Suite05DateTime and Suite10Solcross now dispatch them.
}
