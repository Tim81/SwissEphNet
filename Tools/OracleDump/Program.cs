// The .NET side of the bit-exact comparison harness. Replays the committed grids against this
// port, printing each result's raw IEEE-754 bit pattern so scripts/run-oracle-dump.ps1 can queue
// it up against Tools/CReference/sedump.c's C output for a later, separate comparison pass.
//
//   Tools/OracleGrid/grid-analytic.tsv  -- swe_calc/swe_calc_ut (SEFLG_MOSEPH),
//                                          swe_houses/swe_houses_armc, and the eight crossing
//                                          functions (swe_solcross/_ut, swe_mooncross/_ut,
//                                          swe_mooncross_node/_ut, swe_helio_cross/_ut), also
//                                          under SEFLG_MOSEPH, plus swe_houses_ex (the sidereal/
//                                          radians house path), swe_houses_ex2 and
//                                          swe_houses_armc_ex2 (the speed-bearing forms --
//                                          see ProcessHousesEx2/ProcessHousesArmcEx2 below),
//                                          swe_get_ayanamsa_ut, swe_sidtime, swe_azalt,
//                                          swe_house_name and swe_nod_aps_ut. Touches no
//                                          ephemeris data file. See gen-grid-analytic.ps1's header.
//   Tools/OracleGrid/grid-files.tsv     -- swe_calc/swe_calc_ut (SEFLG_SWIEPH), the swe_fixstar
//                                          family (including swe_fixstar2_mag), swe_get_planet_name,
//                                          the same eight crossing functions under SEFLG_SWIEPH,
//                                          plus swe_houses_ex/swe_houses_ex2 and swe_nod_aps_ut
//                                          (the two of grid-analytic.tsv's new funcs where a real
//                                          .se1 file changes what gets exercised), and
//                                          swe_houses_armc_ex2 (dispatch/schema parity with
//                                          grid-analytic.tsv -- touches no file itself). Opens the
//                                          shipped .se1/sefstars.txt files. See gen-grid-files.ps1's
//                                          header.
//   Tools/OracleGrid/grid-jpl.tsv       -- swe_calc/swe_calc_ut (SEFLG_JPLEPH), including the
//                                          SEFLG_JPLHOR/SEFLG_JPLHOR_APPROX combinations no other
//                                          grid can reach. Opens a JPL DE file this repo does not
//                                          ship, named by the optional fourth argument below. See
//                                          gen-grid-jpl.ps1's header.
//
// Every grid shares one output shape (see sedump.c's own header for the exact layout). Which
// column layout a grid uses is dispatched on in Main below by comparing its header line against
// ExpectedHeaderAnalytic/ExpectedHeaderFiles; grid-jpl.tsv carries grid-files.tsv's header
// verbatim and is therefore read in GridMode.Files, matching sedump.c -- see gen-grid-jpl.ps1's
// own header for why it reuses that layout rather than introducing a third identical one.
//
// INVOCATION
//
//   OracleDump.exe <grid.tsv> <output.tsv> [ephe-dir [jpl-file]]
//
// ephe-dir is optional. grid-analytic.tsv needs it never; grid-files.tsv and grid-jpl.tsv need it
// always -- see AttachEpheDir below. jpl-file is optional too and only grid-jpl.tsv needs it --
// see AttachJplFile, including why it must be applied strictly after AttachEpheDir.
//
// A FRESH SwissEph INSTANCE PER ROW
//
// swe_houses_armc carries a hidden field emulating a C static (saved_sundec) that changes hsys
// 'I'/'i' results depending on what a PRIOR call computed on the SAME instance -- see
// Tools/BaselineGen/Program.cs's header and SwissEphNet/CPort/SweHouse.cs. Every grid-files.tsv
// row additionally depends on which ephemeris segment is cached on the instance that ran it.
// Reusing one instance across rows would make this driver disagree with sedump.c (which calls
// swe_close() before every row, and swe_set_ephe_path() again for grid-files.tsv) for a reason
// that has nothing to do with the port, so a brand new SwissEph is constructed for every row
// here too, and for grid-files.tsv rows, a fresh swe_set_ephe_path() call on that new instance.
//
// A fresh instance is strictly MORE reset than sedump.c's per-row swe_close(), and for one field
// that difference is observable in principle: swe_close() frees swed.dpsi/swed.deps but leaves
// swed.eop_dpsi_loaded set, so the C side carries a prior row's EOP outcome forward while this
// side recomputes it. sedump.c's own header ("ONE PIECE OF STATE swe_close() DOES NOT RESET")
// works through why that costs nothing as long as the EOP text files are absent from ephe-dir --
// the failing path writes ERR every time on both sides -- and what would have to change first if
// they were ever present.

using System.Globalization;
using SwissEphNet;

namespace OracleDump;

internal static class Program
{
    private const int AnalyticColumns = 22;
    private const int FilesColumns = 18;
    private const int CuspCount = 37; // cusp[0..36]
    private const int AscmcCount = 10; // ascmc[0..9]

    // x2cross, dir, t0 and ayan_t0 are appended after sid_mode in both headers, not interleaved
    // among the original columns, so every column this file's other Process* methods already
    // index by a fixed offset keeps that same offset -- matches Tools/CReference/sedump.c's
    // identical choice. t0/ayan_t0 carry swe_set_sid_mode's own SE_SIDM_USER parameters -- see
    // ApplySidMode.
    //
    // method, calc_flag, atpress, attemp, xin0, xin1 (analytic) and method, hsys (files) are a
    // second additive tail, appended after t0/ayan_t0 for the same reason -- see
    // Tools/CReference/sedump.c's identical header comment on EXPECTED_HEADER_ANALYTIC/FILES for
    // which new func needs which column.
    private static readonly string ExpectedHeaderAnalytic = string.Join('\t',
        "case_id", "func", "ipl", "tjd", "iflag", "hsys", "geolon", "geolat", "height", "armc", "eps", "sid_mode", "x2cross", "dir", "t0", "ayan_t0",
        "method", "calc_flag", "atpress", "attemp", "xin0", "xin1");

    private static readonly string ExpectedHeaderFiles = string.Join('\t',
        "case_id", "func", "ipl", "tjd", "iflag", "star", "geolon", "geolat", "height", "sid_mode", "x2cross", "dir", "t0", "ayan_t0",
        "method", "hsys", "armc", "eps");

    private enum GridMode { Analytic, Files }

    private static int Main(string[] args)
    {
        if (args.Length is < 2 or > 4)
        {
            Console.Error.WriteLine("Usage: OracleDump <grid.tsv> <output.tsv> [ephe-dir [jpl-file]]");
            return 1;
        }

        var gridPath = args[0];
        var outputPath = args[1];
        var epheDir = args.Length >= 3 ? args[2] : null;
        var jplFile = args.Length == 4 ? args[3] : null;

        // swe_set_jpl_file resolves its argument against swed.ephepath, so a jpl-file with no
        // ephe-dir would resolve against the compiled-in default -- almost certainly not finding
        // the file, and then silently falling back through SEFLG_SWIEPH to Moshier on every row.
        // Rejected here rather than left to produce a run that looks fine and measures nothing.
        // Mirrors sedump.c's identical guard in main().
        if (jplFile != null && string.IsNullOrEmpty(epheDir))
        {
            Console.Error.WriteLine(
                "OracleDump: jpl-file was given but ephe-dir is empty; swe_set_jpl_file resolves against the ephemeris path, so both are required together.");
            return 1;
        }

        using var reader = new StreamReader(gridPath);
        using var writer = new StreamWriter(outputPath, append: false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            NewLine = "\n"
        };

        var headerSeen = false;
        var mode = GridMode.Analytic;
        var expectedColumns = AnalyticColumns;
        var rowCount = 0;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0)
            {
                continue;
            }
            if (line[0] == '#')
            {
                continue;
            }

            if (!headerSeen)
            {
                if (line == ExpectedHeaderAnalytic)
                {
                    mode = GridMode.Analytic;
                    expectedColumns = AnalyticColumns;
                }
                else if (line == ExpectedHeaderFiles)
                {
                    mode = GridMode.Files;
                    expectedColumns = FilesColumns;
                }
                else
                {
                    throw new InvalidDataException(
                        "grid header does not match either header this driver expects.\n" +
                        $"analytic: {ExpectedHeaderAnalytic}\nfiles:    {ExpectedHeaderFiles}\ngot:      {line}");
                }
                headerSeen = true;
                continue;
            }

            var fields = line.Split('\t');
            if (fields.Length != expectedColumns)
            {
                throw new InvalidDataException($"row has {fields.Length} column(s), expected {expectedColumns}: {line}");
            }

            ProcessRow(mode, fields, epheDir, jplFile, writer);
            rowCount++;
        }

        if (!headerSeen)
        {
            throw new InvalidDataException($"grid file {gridPath} had no header row.");
        }
        if (rowCount == 0)
        {
            throw new InvalidDataException($"grid file {gridPath} produced zero rows -- a run that processed nothing is not a pass.");
        }

        Console.Error.WriteLine($"OracleDump: wrote {rowCount} row(s) to {outputPath}");
        return 0;
    }

    private static void ProcessRow(GridMode mode, string[] fields, string? epheDir, string? jplFile, TextWriter writer)
    {
        var caseId = fields[0];
        var func = fields[1];

        // Fresh library state before every row -- see this file's header comment.
        using var swe = new SwissEph();
        if (epheDir != null)
        {
            AttachEpheDir(swe, epheDir);
        }
        // Strictly after AttachEpheDir, never before -- see AttachJplFile.
        if (jplFile != null)
        {
            AttachJplFile(swe, jplFile);
        }

        if (mode == GridMode.Analytic)
        {
            switch (func)
            {
                case "CALC":
                case "CALC_UT":
                    ProcessCalc(swe, caseId, func, fields, sidModeIndex: 11, writer);
                    break;
                case "HOUSES":
                    ProcessHouses(swe, caseId, fields, sidModeIndex: 11, writer);
                    break;
                case "HOUSES_ARMC":
                    ProcessHousesArmc(swe, caseId, fields, sidModeIndex: 11, writer);
                    break;
                case "SOLCROSS":
                case "SOLCROSS_UT":
                case "MOONCROSS":
                case "MOONCROSS_UT":
                    ProcessCrossingDeg(swe, caseId, func, fields, sidModeIndex: 11, x2crossIndex: 12, writer);
                    break;
                case "MOONCROSS_NODE":
                case "MOONCROSS_NODE_UT":
                    ProcessMoonCrossNode(swe, caseId, func, fields, sidModeIndex: 11, writer);
                    break;
                case "HELIO_CROSS":
                case "HELIO_CROSS_UT":
                    ProcessHelioCross(swe, caseId, func, fields, sidModeIndex: 11, x2crossIndex: 12, dirIndex: 13, writer);
                    break;
                case "AYANAMSA":
                    ProcessAyanamsa(swe, caseId, fields, writer);
                    break;
                case "AYANAMSA_EX":
                case "AYANAMSA_EX_UT":
                    ProcessAyanamsaEx(swe, caseId, func, fields, writer);
                    break;
                case "HOUSES_EX":
                    ProcessHousesEx(swe, caseId, fields, sidModeIndex: 11, hsysIndex: 5, writer);
                    break;
                case "HOUSES_EX2":
                    ProcessHousesEx2(swe, caseId, fields, sidModeIndex: 11, hsysIndex: 5, writer);
                    break;
                case "HOUSES_ARMC_EX2":
                    ProcessHousesArmcEx2(swe, caseId, func, fields, sidModeIndex: 11, hsysIndex: 5, armcIndex: 9, epsIndex: 10, writer);
                    break;
                case "AYANAMSA_UT":
                    ProcessAyanamsaUt(swe, caseId, fields, writer);
                    break;
                case "SIDTIME":
                    ProcessSidtime(swe, caseId, fields, writer);
                    break;
                case "AZALT":
                    ProcessAzalt(swe, caseId, fields, writer);
                    break;
                case "HOUSE_NAME":
                    ProcessHouseName(swe, caseId, fields, writer);
                    break;
                case "NOD_APS_UT":
                    ProcessNodApsUt(swe, caseId, fields, sidModeIndex: 11, methodIndex: 16, writer);
                    break;
                default:
                    throw new InvalidDataException($"unknown func '{func}' at case {caseId}");
            }
        }
        else
        {
            switch (func)
            {
                case "CALC":
                case "CALC_UT":
                    ProcessCalc(swe, caseId, func, fields, sidModeIndex: 9, writer);
                    break;
                case "FIXSTAR":
                case "FIXSTAR_UT":
                case "FIXSTAR2":
                case "FIXSTAR2_UT":
                    ProcessFixstar(swe, caseId, func, fields, sidModeIndex: 9, writer);
                    break;
                case "FIXSTAR_MAG":
                case "FIXSTAR2_MAG":
                    ProcessFixstarMag(swe, caseId, func, fields, writer);
                    break;
                case "GET_PLANET_NAME":
                    ProcessName(swe, caseId, fields, writer);
                    break;
                case "SOLCROSS":
                case "SOLCROSS_UT":
                case "MOONCROSS":
                case "MOONCROSS_UT":
                    ProcessCrossingDeg(swe, caseId, func, fields, sidModeIndex: 9, x2crossIndex: 10, writer);
                    break;
                case "MOONCROSS_NODE":
                case "MOONCROSS_NODE_UT":
                    ProcessMoonCrossNode(swe, caseId, func, fields, sidModeIndex: 9, writer);
                    break;
                case "HELIO_CROSS":
                case "HELIO_CROSS_UT":
                    ProcessHelioCross(swe, caseId, func, fields, sidModeIndex: 9, x2crossIndex: 10, dirIndex: 11, writer);
                    break;
                case "HOUSES_EX":
                    ProcessHousesEx(swe, caseId, fields, sidModeIndex: 9, hsysIndex: 15, writer);
                    break;
                case "HOUSES_EX2":
                    ProcessHousesEx2(swe, caseId, fields, sidModeIndex: 9, hsysIndex: 15, writer);
                    break;
                case "HOUSES_ARMC_EX2":
                    ProcessHousesArmcEx2(swe, caseId, func, fields, sidModeIndex: 9, hsysIndex: 15, armcIndex: 16, epsIndex: 17, writer);
                    break;
                case "NOD_APS_UT":
                    ProcessNodApsUt(swe, caseId, fields, sidModeIndex: 9, methodIndex: 14, writer);
                    break;
                default:
                    throw new InvalidDataException($"unknown func '{func}' at case {caseId}");
            }
        }
    }

    // swe_set_ephe_path is not a setter (sweph.c:1315-1350) -- it closes every open file and
    // eagerly calls swe_calc internally to pin tidal acceleration from the lunar file it opens.
    // With SwissEph.OpenBinary defaulting to the real filesystem, that eager open now reaches
    // real files on its own, without the OnLoadFile handler this used to need attached first
    // (see Tests/SwissEphNet.Conformance.Tests/Dispatch/EphemerisFileResolver.cs's Attach for
    // the ordering history that motivated the old attach-before-set sequencing). This driver
    // receives its ephemeris directory as an explicit argument rather than through
    // RepoLocator/environment-variable resolution; sedump.c gets the same path the same way
    // (its own optional third argv).
    private static void AttachEpheDir(SwissEph swe, string epheDir)
    {
        swe.swe_set_ephe_path(epheDir);
    }

    // grid-jpl.tsv only. swe_set_jpl_file is not a setter either: it closes every open file and
    // then opens the named DE file immediately, resolving the name against swed.ephepath as it
    // stands at that moment (sweph.c:1499-1505). Calling it before AttachEpheDir would resolve
    // against the compiled-in default path instead of the directory the DE file lives in, fail to
    // open it, and so never reach the jpldenum >= 403 branch that calls load_dpsi_deps -- and
    // every SEFLG_JPLEPH row would then fall back through SEFLG_SWIEPH to Moshier and compare
    // bit-identical against a C side doing exactly the same thing, measuring nothing about the JPL
    // backend at all. sedump.c issues the identical call in the identical position for the same
    // reason; this driver receives the file name as an explicit argument rather than through
    // environment-variable resolution, the same way it already receives its ephemeris directory.
    private static void AttachJplFile(SwissEph swe, string jplFile)
    {
        swe.swe_set_jpl_file(jplFile);
    }

    // t0/ayan_t0 (swe_set_sid_mode's own SE_SIDM_USER parameters) always sit exactly 3 and 4
    // columns after sid_mode in both grids -- sid_mode, x2cross, dir, t0, ayan_t0, in that fixed
    // relative order -- matching Tools/CReference/sedump.c's identical apply_sid_mode. A row with
    // no sid_mode never reads t0/ayan_t0 at all; an empty t0/ayan_t0 on a row that does set
    // sid_mode means 0.0, the same default swe_set_sid_mode(sidMode, 0, 0) always passed before
    // this driver could express SE_SIDM_USER at all.
    private static void ApplySidMode(SwissEph swe, string[] fields, string caseId, int sidModeIndex)
    {
        if (!HasValue(fields[sidModeIndex]))
        {
            return;
        }
        var sidMode = ParseInt(fields[sidModeIndex], caseId, "sid_mode");
        var t0 = HasValue(fields[sidModeIndex + 3]) ? ParseDouble(fields[sidModeIndex + 3], caseId, "t0") : 0.0;
        var ayanT0 = HasValue(fields[sidModeIndex + 4]) ? ParseDouble(fields[sidModeIndex + 4], caseId, "ayan_t0") : 0.0;
        swe.swe_set_sid_mode(sidMode, t0, ayanT0);
    }

    // MOONCROSS_NODE(_UT), HELIO_CROSS(_UT), the FIXSTAR family (FIXSTAR/FIXSTAR_UT/FIXSTAR2/
    // FIXSTAR2_UT) and HOUSES/HOUSES_ARMC never call ApplySidMode: none of the C functions behind
    // them (swe_mooncross_node(_ut), swe_helio_cross(_ut), swe_fixstar(2)(_ut), swe_houses(_armc))
    // takes a sidereal-frame parameter at all in Astrodienst's own API, so there is nothing for
    // this driver to apply. Every grid row for these funcs is therefore expected to carry an empty
    // sid_mode column -- and today, every one of them does (verified: this guard has never fired
    // against Tools/OracleGrid/grid-analytic.tsv or grid-files.tsv).
    //
    // This hard-fails instead of silently ignoring a non-empty sid_mode, because "silently ignore
    // it" is exactly the failure mode that made this a blind spot in the first place: a future
    // sidereal MOONCROSS_NODE row would have both drivers ignore the column the same way, the row
    // would compare bit-identical between them, and the comparison would prove nothing about
    // either driver's (non-existent) sidereal handling for that func -- see this file's sibling
    // check in Tools/CReference/sedump.c's refuse_if_sid_mode_set for the C side of the same guard.
    private static void RefuseIfSidModeSet(string caseId, string func, string[] fields, int sidModeIndex)
    {
        if (HasValue(fields[sidModeIndex]))
        {
            throw new InvalidDataException(
                $"{caseId}: func '{func}' has a non-empty sid_mode ('{fields[sidModeIndex]}'), but this driver never " +
                $"calls ApplySidMode for it -- {func} has no sidereal-frame parameter in Astrodienst's C API. Either " +
                "this row's sid_mode should be empty (a grid-generation defect), or ApplySidMode needs to be wired " +
                "up for this func (an API change this driver has not caught up with).");
        }
    }

    private static void ProcessCalc(SwissEph swe, string caseId, string func, string[] fields, int sidModeIndex, TextWriter writer)
    {
        var ipl = ParseInt(fields[2], caseId, "ipl");
        var tjd = ParseDouble(fields[3], caseId, "tjd");
        var iflag = ParseInt(fields[4], caseId, "iflag");

        if (HasValue(fields[6]) || HasValue(fields[7]) || HasValue(fields[8]))
        {
            var geolon = ParseDouble(fields[6], caseId, "geolon");
            var geolat = ParseDouble(fields[7], caseId, "geolat");
            var height = ParseDouble(fields[8], caseId, "height");
            swe.swe_set_topo(geolon, geolat, height);
        }
        ApplySidMode(swe, fields, caseId, sidModeIndex);

        var xx = new double[6];
        string? serr = null;
        var retc = func == "CALC"
            ? swe.swe_calc(tjd, ipl, iflag, xx, ref serr)
            : swe.swe_calc_ut(tjd, ipl, iflag, xx, ref serr);

        writer.Write(caseId);
        writer.Write('\t');
        writer.Write(retc.ToString(CultureInfo.InvariantCulture));
        writer.Write('\t');
        writer.Write(EscapeErr(serr));
        for (var i = 0; i < 6; i++)
        {
            EmitValue(writer, xx[i]);
        }
        writer.Write('\n');
    }

    // SOLCROSS, SOLCROSS_UT, MOONCROSS, MOONCROSS_UT: all four share one C# signature shape --
    // double f(x2cross, tjd, iflag, ref serr) -- and one error convention, per Astrodienst's own
    // doc comment on each (external/swisseph/sweph.c:8319, 8353, 8387, 8421): "Errors are
    // indicated by returning a jd < jd_et [or jd_ut]!", not by a separate int return code the way
    // swe_calc/swe_helio_cross use. There is no int retc to report at all, so this driver
    // computes one itself -- SwissEph.ERR when the returned jd is less than the input jd,
    // SwissEph.OK otherwise -- purely so the row still fits the shared "case_id, retc, err,
    // values..." shape every other func in this file already uses. Tools/CReference/sedump.c's
    // process_crossing_deg computes the identical value from the identical returned bits, so this
    // synthetic column can never disagree between the two sides on its own. x2crossIndex is the
    // one difference between the two grids (analytic carries armc/eps before sid_mode; files does
    // not), matching ProcessCalc's own sidModeIndex parameter for the same reason.
    private static void ProcessCrossingDeg(SwissEph swe, string caseId, string func, string[] fields, int sidModeIndex, int x2crossIndex, TextWriter writer)
    {
        var x2cross = ParseDouble(fields[x2crossIndex], caseId, "x2cross");
        var tjd = ParseDouble(fields[3], caseId, "tjd");
        var iflag = ParseInt(fields[4], caseId, "iflag");

        ApplySidMode(swe, fields, caseId, sidModeIndex);

        string? serr = null;
        var result = func switch
        {
            "SOLCROSS" => swe.swe_solcross(x2cross, tjd, iflag, ref serr),
            "SOLCROSS_UT" => swe.swe_solcross_ut(x2cross, tjd, iflag, ref serr),
            "MOONCROSS" => swe.swe_mooncross(x2cross, tjd, iflag, ref serr),
            _ => swe.swe_mooncross_ut(x2cross, tjd, iflag, ref serr),
        };
        var retc = result < tjd ? SwissEph.ERR : SwissEph.OK;

        writer.Write(caseId);
        writer.Write('\t');
        writer.Write(retc.ToString(CultureInfo.InvariantCulture));
        writer.Write('\t');
        writer.Write(EscapeErr(serr));
        EmitValue(writer, result);
        writer.Write('\n');
    }

    // MOONCROSS_NODE, MOONCROSS_NODE_UT: same double-return, jd-less-than-input error convention
    // as ProcessCrossingDeg above (external/swisseph/sweph.c:8454, 8491), plus two output
    // parameters (xlon, xlat) this driver zero-initializes before the call -- swe_mooncross_node
    // only writes them on the convergence path (external/swisseph/sweph.c:8480-8481), leaving
    // them untouched on every early ERR return, so zero-initializing here is what makes an
    // errored row's xlon/xlat columns a deterministic, comparable 0.0 on both sides instead of
    // comparing whatever each side's uninitialized local happened to hold.
    private static void ProcessMoonCrossNode(SwissEph swe, string caseId, string func, string[] fields, int sidModeIndex, TextWriter writer)
    {
        RefuseIfSidModeSet(caseId, func, fields, sidModeIndex);

        var tjd = ParseDouble(fields[3], caseId, "tjd");
        var iflag = ParseInt(fields[4], caseId, "iflag");

        var xlon = 0.0;
        var xlat = 0.0;
        string? serr = null;
        var result = func == "MOONCROSS_NODE"
            ? swe.swe_mooncross_node(tjd, iflag, ref xlon, ref xlat, ref serr)
            : swe.swe_mooncross_node_ut(tjd, iflag, ref xlon, ref xlat, ref serr);
        var retc = result < tjd ? SwissEph.ERR : SwissEph.OK;

        writer.Write(caseId);
        writer.Write('\t');
        writer.Write(retc.ToString(CultureInfo.InvariantCulture));
        writer.Write('\t');
        writer.Write(EscapeErr(serr));
        EmitValue(writer, result);
        EmitValue(writer, xlon);
        EmitValue(writer, xlat);
        writer.Write('\n');
    }

    // HELIO_CROSS, HELIO_CROSS_UT: the one pair among these eight with a real int retc (OK/ERR)
    // and an output parameter (jdCross) written only on the OK path -- zero-initialized here for
    // the same reason ProcessMoonCrossNode zero-initializes xlon/xlat.
    private static void ProcessHelioCross(SwissEph swe, string caseId, string func, string[] fields, int sidModeIndex, int x2crossIndex, int dirIndex, TextWriter writer)
    {
        RefuseIfSidModeSet(caseId, func, fields, sidModeIndex);

        var ipl = ParseInt(fields[2], caseId, "ipl");
        var x2cross = ParseDouble(fields[x2crossIndex], caseId, "x2cross");
        var tjd = ParseDouble(fields[3], caseId, "tjd");
        var iflag = ParseInt(fields[4], caseId, "iflag");
        var dir = ParseInt(fields[dirIndex], caseId, "dir");

        var jdCross = 0.0;
        string? serr = null;
        var retc = func == "HELIO_CROSS"
            ? swe.swe_helio_cross(ipl, x2cross, tjd, iflag, dir, ref jdCross, ref serr)
            : swe.swe_helio_cross_ut(ipl, x2cross, tjd, iflag, dir, ref jdCross, ref serr);

        writer.Write(caseId);
        writer.Write('\t');
        writer.Write(retc.ToString(CultureInfo.InvariantCulture));
        writer.Write('\t');
        writer.Write(EscapeErr(serr));
        EmitValue(writer, jdCross);
        writer.Write('\n');
    }

    // grid-analytic.tsv only: direct coverage of swe_get_ayanamsa/_ex/_ex_ut -- see this file's
    // own top-of-file comment. sidModeIndex is always 11, the analytic grid's own fixed sid_mode
    // column position; these func tokens never appear in a grid-files.tsv row.
    //
    // AYANAMSA has no serr output parameter -- swe_get_ayanamsa returns a bare double with no
    // error signal at all -- so its retc is a fixed SwissEph.OK and its err column stays empty,
    // the same convention WriteHousesRow already uses for a .NET API with nothing to report there.
    private static void ProcessAyanamsa(SwissEph swe, string caseId, string[] fields, TextWriter writer)
    {
        var tjd = ParseDouble(fields[3], caseId, "tjd");
        ApplySidMode(swe, fields, caseId, sidModeIndex: 11);
        var value = swe.swe_get_ayanamsa(tjd);

        writer.Write(caseId);
        writer.Write('\t');
        writer.Write(SwissEph.OK.ToString(CultureInfo.InvariantCulture));
        writer.Write('\t');
        EmitValue(writer, value);
        writer.Write('\n');
    }

    private static void ProcessAyanamsaEx(SwissEph swe, string caseId, string func, string[] fields, TextWriter writer)
    {
        var tjd = ParseDouble(fields[3], caseId, "tjd");
        var iflag = ParseInt(fields[4], caseId, "iflag");
        ApplySidMode(swe, fields, caseId, sidModeIndex: 11);

        string? serr = null;
        var retc = func == "AYANAMSA_EX"
            ? swe.swe_get_ayanamsa_ex(tjd, iflag, out var daya, ref serr)
            : swe.swe_get_ayanamsa_ex_ut(tjd, iflag, out daya, ref serr);

        writer.Write(caseId);
        writer.Write('\t');
        writer.Write(retc.ToString(CultureInfo.InvariantCulture));
        writer.Write('\t');
        writer.Write(EscapeErr(serr));
        EmitValue(writer, daya);
        writer.Write('\n');
    }

    // HOUSES_EX: swe_houses_ex, the sidereal/radians-capable sibling of HOUSES. Shared by both
    // grids -- hsysIndex is the one difference (analytic's hsys sits at fields[5], shared with
    // HOUSES/HOUSES_ARMC; the files grid has no hsys column of its own, so it gets the new
    // trailing one instead), matching ProcessHousesArmc's own sidModeIndex-style parameter for
    // the same reason.
    private static void ProcessHousesEx(SwissEph swe, string caseId, string[] fields, int sidModeIndex, int hsysIndex, TextWriter writer)
    {
        var tjd = ParseDouble(fields[3], caseId, "tjd");
        var iflag = ParseInt(fields[4], caseId, "iflag");
        var hsys = ParseHsys(fields[hsysIndex], caseId);
        var geolon = ParseDouble(fields[6], caseId, "geolon");
        var geolat = ParseDouble(fields[7], caseId, "geolat");

        ApplySidMode(swe, fields, caseId, sidModeIndex);

        var cusp = new double[40];
        var ascmc = new double[10];
        // swe_houses_ex takes geolat before geolon -- opposite of this grid's own
        // geolon-then-geolat column order -- matches ProcessHouses's identical care for plain
        // swe_houses.
        var retc = swe.swe_houses_ex(tjd, iflag, geolat, geolon, hsys, cusp, ascmc);

        WriteHousesRow(writer, caseId, retc, cusp, ascmc);
    }

    // HOUSES_EX2: swe_houses_ex2, the 2.10.03 speed-bearing sibling of HOUSES_EX. swe_houses/
    // swe_houses_ex reach the underlying swe_houses_armc_ex2 already (SwissEphNet/CPort/
    // SweHouse.cs), but always with cuspSpeed/ascmcSpeed/serr null, so the port's own
    // h.do_speed/h.do_hspeed gating (matching swehouse.c:642-647) stays false and the speed
    // writes never happen that way. This calls swe_houses_ex2 directly with real arrays, so those
    // writes execute -- see sedump.c's own top-of-file comment for the C-side citations this
    // shares. Unlike ProcessHousesEx, this one has a real serr (swe_houses_ex2 forwards whatever
    // the delegated call wrote), and cuspSpeed/ascmcSpeed are zero-initialized by `new double[...]`
    // (.NET arrays are always zero-initialized) before the call, matching sedump.c's explicit
    // `= { 0 }` for the same reason process_helio_cross zero-initializes jdCross.
    private static void ProcessHousesEx2(SwissEph swe, string caseId, string[] fields, int sidModeIndex, int hsysIndex, TextWriter writer)
    {
        var tjd = ParseDouble(fields[3], caseId, "tjd");
        var iflag = ParseInt(fields[4], caseId, "iflag");
        var hsys = ParseHsys(fields[hsysIndex], caseId);
        var geolon = ParseDouble(fields[6], caseId, "geolon");
        var geolat = ParseDouble(fields[7], caseId, "geolat");

        ApplySidMode(swe, fields, caseId, sidModeIndex);

        var cusp = new double[40];
        var ascmc = new double[10];
        var cuspSpeed = new double[40];
        var ascmcSpeed = new double[10];
        string? serr = null;
        var retc = swe.swe_houses_ex2(tjd, iflag, geolat, geolon, hsys, cusp, ascmc, cuspSpeed, ascmcSpeed, ref serr);

        WriteHousesSpeedRow(writer, caseId, retc, serr, cusp, ascmc, cuspSpeed, ascmcSpeed);
    }

    // HOUSES_ARMC_EX2: swe_houses_armc_ex2, the 2.10.03 speed-bearing sibling of HOUSES_ARMC.
    // hsysIndex/armcIndex/epsIndex are the differences between the two grids: analytic's hsys
    // sits at fields[5] (shared with HOUSES/HOUSES_ARMC) and its armc/eps at fields[9]/[10]
    // (shared with HOUSES_ARMC); the files grid's own hsys sits at fields[15] (shared with
    // HOUSES_EX, not fields[5] -- that grid's star column) and has no armc/eps columns of its own
    // before this addition, so it gets the two new trailing ones instead -- see sedump.c's own
    // process_houses_armc_ex2 comment for the full rationale, including why ascmc is
    // zero-initialized before the call exactly as ProcessHousesArmc's already is (ascmc[9] stays
    // 0.0, not 99, so the saved_sundec-reading branch is never taken here either).
    private static void ProcessHousesArmcEx2(SwissEph swe, string caseId, string func, string[] fields, int sidModeIndex, int hsysIndex, int armcIndex, int epsIndex, TextWriter writer)
    {
        RefuseIfSidModeSet(caseId, func, fields, sidModeIndex);

        var armc = ParseDouble(fields[armcIndex], caseId, "armc");
        var eps = ParseDouble(fields[epsIndex], caseId, "eps");
        var hsys = ParseHsys(fields[hsysIndex], caseId);
        var geolat = ParseDouble(fields[7], caseId, "geolat");

        var cusp = new double[40];
        var ascmc = new double[10];
        var cuspSpeed = new double[40];
        var ascmcSpeed = new double[10];
        string? serr = null;
        var retc = swe.swe_houses_armc_ex2(armc, geolat, eps, hsys, cusp, ascmc, cuspSpeed, ascmcSpeed, ref serr);

        WriteHousesSpeedRow(writer, caseId, retc, serr, cusp, ascmc, cuspSpeed, ascmcSpeed);
    }

    // Shared by ProcessHousesEx2/ProcessHousesArmcEx2: cusp[0..36]+ascmc[0..9]+cuspSpeed[0..36]+
    // ascmcSpeed[0..9], 94 doubles -> 188 value columns, plus the real serr both funcs carry --
    // see WriteHousesRow's own comment for why HOUSES/HOUSES_ARMC/HOUSES_EX never write an err
    // column at all, unlike these two.
    private static void WriteHousesSpeedRow(TextWriter writer, string caseId, int retc, string? serr, double[] cusp, double[] ascmc, double[] cuspSpeed, double[] ascmcSpeed)
    {
        writer.Write(caseId);
        writer.Write('\t');
        writer.Write(retc.ToString(CultureInfo.InvariantCulture));
        writer.Write('\t');
        writer.Write(EscapeErr(serr));
        for (var c = 0; c < CuspCount; c++)
        {
            EmitValue(writer, cusp[c]);
        }
        for (var a = 0; a < AscmcCount; a++)
        {
            EmitValue(writer, ascmc[a]);
        }
        for (var c = 0; c < CuspCount; c++)
        {
            EmitValue(writer, cuspSpeed[c]);
        }
        for (var a = 0; a < AscmcCount; a++)
        {
            EmitValue(writer, ascmcSpeed[a]);
        }
        writer.Write('\n');
    }

    // AYANAMSA_UT: swe_get_ayanamsa_ut, the UT sibling of AYANAMSA -- same fixed-OK, empty-err
    // convention as ProcessAyanamsa, and the same ApplySidMode call, since the ayanamsa it
    // returns still depends on whichever sid_mode swe_set_sid_mode last configured.
    // Analytic-grid only: opens no ephemeris file, so this func token never appears in a
    // grid-files.tsv row.
    private static void ProcessAyanamsaUt(SwissEph swe, string caseId, string[] fields, TextWriter writer)
    {
        var tjd = ParseDouble(fields[3], caseId, "tjd");
        ApplySidMode(swe, fields, caseId, sidModeIndex: 11);
        var value = swe.swe_get_ayanamsa_ut(tjd);

        writer.Write(caseId);
        writer.Write('\t');
        writer.Write(SwissEph.OK.ToString(CultureInfo.InvariantCulture));
        writer.Write('\t');
        EmitValue(writer, value);
        writer.Write('\n');
    }

    // SIDTIME: swe_sidtime. A bare double with no serr and no sid_mode dependence of its own
    // (sidereal *time*, not the ayanamsha) -- RefuseIfSidModeSet guards the latter the same way
    // ProcessFixstar/ProcessHouses already guard funcs with no sidereal-frame parameter.
    private static void ProcessSidtime(SwissEph swe, string caseId, string[] fields, TextWriter writer)
    {
        RefuseIfSidModeSet(caseId, "SIDTIME", fields, sidModeIndex: 11);
        var tjd = ParseDouble(fields[3], caseId, "tjd");
        var value = swe.swe_sidtime(tjd);

        writer.Write(caseId);
        writer.Write('\t');
        writer.Write(SwissEph.OK.ToString(CultureInfo.InvariantCulture));
        writer.Write('\t');
        EmitValue(writer, value);
        writer.Write('\n');
    }

    // AZALT: swe_azalt. Analytic-grid only. geopos is {lon, lat, height}, reusing this grid's
    // existing geolon/geolat/height columns; xin[2] is never a grid column because swe_azalt's
    // own body only ever reads xin[0]/xin[1] -- a column nothing reads is exactly the dead-input
    // trap this repo has already been burned by, so this driver does not add one. atpress == 0
    // takes the pressure-estimate branch, so this grid deliberately carries rows with atpress = 0
    // and a non-zero height so that branch is exercised, not just asserted.
    private static void ProcessAzalt(SwissEph swe, string caseId, string[] fields, TextWriter writer)
    {
        RefuseIfSidModeSet(caseId, "AZALT", fields, sidModeIndex: 11);

        var tjd = ParseDouble(fields[3], caseId, "tjd");
        var geopos = new[]
        {
            ParseDouble(fields[6], caseId, "geolon"),
            ParseDouble(fields[7], caseId, "geolat"),
            ParseDouble(fields[8], caseId, "height"),
        };
        var calcFlag = ParseInt(fields[17], caseId, "calc_flag");
        var atpress = ParseDouble(fields[18], caseId, "atpress");
        var attemp = ParseDouble(fields[19], caseId, "attemp");
        var xin = new[]
        {
            ParseDouble(fields[20], caseId, "xin0"),
            ParseDouble(fields[21], caseId, "xin1"),
        };
        var xaz = new double[3];

        // swe_azalt reads const_lapse_rate, a swecl.c static settable only through
        // swe_set_lapse_rate -- neither driver ever calls that, and both reset all other library
        // state before every row (a fresh SwissEph here, swe_close() in sedump.c), so both sides
        // see SE_LAPSE_RATE, the compiled-in default, on every single row.
        swe.swe_azalt(tjd, calcFlag, geopos, atpress, attemp, xin, xaz);

        writer.Write(caseId);
        writer.Write('\t');
        writer.Write(SwissEph.OK.ToString(CultureInfo.InvariantCulture)); // swe_azalt returns void -- no retc, no serr
        writer.Write('\t');
        for (var i = 0; i < 3; i++)
        {
            EmitValue(writer, xaz[i]);
        }
        writer.Write('\n');
    }

    // HOUSE_NAME: swe_house_name. Analytic-grid only; a pure lookup, so it opens no ephemeris
    // file either way. Returns a string, never null -- same "write the string into the err
    // column, fixed retc 0" convention as ProcessName (GET_PLANET_NAME).
    private static void ProcessHouseName(SwissEph swe, string caseId, string[] fields, TextWriter writer)
    {
        var hsys = ParseHsys(fields[5], caseId);
        var name = swe.swe_house_name(hsys);

        writer.Write(caseId);
        writer.Write('\t');
        writer.Write(0.ToString(CultureInfo.InvariantCulture));
        writer.Write('\t');
        writer.Write(EscapeErr(name));
        writer.Write('\n');
    }

    // NOD_APS_UT: swe_nod_aps_ut, which adds swe_deltat_ex to tjd_ut and delegates to
    // swe_nod_aps. Real retc and serr. Shared by both grids -- methodIndex is the one difference
    // (analytic carries the new method column after t0/ayan_t0; files carries it right before
    // its own trailing hsys column), matching ProcessHousesEx's own hsysIndex-style parameter for
    // the same reason. No sidereal-frame parameter in Astrodienst's API, so this func gets the
    // same RefuseIfSidModeSet guard ProcessMoonCrossNode/ProcessHelioCross already use.
    private static void ProcessNodApsUt(SwissEph swe, string caseId, string[] fields, int sidModeIndex, int methodIndex, TextWriter writer)
    {
        RefuseIfSidModeSet(caseId, "NOD_APS_UT", fields, sidModeIndex);

        var ipl = ParseInt(fields[2], caseId, "ipl");
        var tjd = ParseDouble(fields[3], caseId, "tjd");
        var iflag = ParseInt(fields[4], caseId, "iflag");
        var method = ParseInt(fields[methodIndex], caseId, "method");

        var xnasc = new double[6];
        var xndsc = new double[6];
        var xperi = new double[6];
        var xaphe = new double[6];
        string? serr = null;
        // Zero-initialized above regardless of outcome -- swe_nod_aps only zeroes these four
        // arrays itself on its "not implemented" reject branch; every other ERR return (e.g. an
        // inner swe_calc failure) leaves them untouched, matching ProcessHelioCross's identical
        // rule for jdCross, for the identical reason.
        var retc = swe.swe_nod_aps_ut(tjd, ipl, iflag, method, xnasc, xndsc, xperi, xaphe, ref serr);

        writer.Write(caseId);
        writer.Write('\t');
        writer.Write(retc.ToString(CultureInfo.InvariantCulture));
        writer.Write('\t');
        writer.Write(EscapeErr(serr));
        for (var i = 0; i < 6; i++)
        {
            EmitValue(writer, xnasc[i]);
        }
        for (var i = 0; i < 6; i++)
        {
            EmitValue(writer, xndsc[i]);
        }
        for (var i = 0; i < 6; i++)
        {
            EmitValue(writer, xperi[i]);
        }
        for (var i = 0; i < 6; i++)
        {
            EmitValue(writer, xaphe[i]);
        }
        writer.Write('\n');
    }

    // grid-files.tsv only: star is fields[5], iflag always carries SEFLG_SWIEPH already OR-ed in
    // by gen-grid-files.ps1. swe_fixstar and its siblings can rewrite `star` in place with the
    // star's canonical name; this driver does not read it back afterward, matching sedump.c's own
    // process_fixstar.
    private static void ProcessFixstar(SwissEph swe, string caseId, string func, string[] fields, int sidModeIndex, TextWriter writer)
    {
        RefuseIfSidModeSet(caseId, func, fields, sidModeIndex);

        var star = fields[5];
        var tjd = ParseDouble(fields[3], caseId, "tjd");
        var iflag = ParseInt(fields[4], caseId, "iflag");

        var xx = new double[6];
        string? serr = null;
        var retc = func switch
        {
            "FIXSTAR" => swe.swe_fixstar(ref star, tjd, iflag, xx, ref serr),
            "FIXSTAR_UT" => swe.swe_fixstar_ut(ref star, tjd, iflag, xx, ref serr),
            "FIXSTAR2" => swe.swe_fixstar2(ref star, tjd, iflag, xx, ref serr),
            _ => swe.swe_fixstar2_ut(ref star, tjd, iflag, xx, ref serr),
        };

        writer.Write(caseId);
        writer.Write('\t');
        writer.Write(retc.ToString(CultureInfo.InvariantCulture));
        writer.Write('\t');
        writer.Write(EscapeErr(serr));
        for (var i = 0; i < 6; i++)
        {
            EmitValue(writer, xx[i]);
        }
        writer.Write('\n');
    }

    // grid-files.tsv only: swe_fixstar_mag and swe_fixstar2_mag both take no date or flag, only
    // the star search string -- share this one method the same way ProcessFixstar shares FIXSTAR/
    // FIXSTAR_UT/FIXSTAR2/FIXSTAR2_UT. Mirrors sedump.c's identical process_fixstar_mag.
    private static void ProcessFixstarMag(SwissEph swe, string caseId, string func, string[] fields, TextWriter writer)
    {
        var star = fields[5];
        var mag = 0.0;
        string? serr = null;
        var retc = func == "FIXSTAR_MAG"
            ? swe.swe_fixstar_mag(ref star, ref mag, ref serr)
            : swe.swe_fixstar2_mag(ref star, ref mag, ref serr);

        writer.Write(caseId);
        writer.Write('\t');
        writer.Write(retc.ToString(CultureInfo.InvariantCulture));
        writer.Write('\t');
        writer.Write(EscapeErr(serr));
        EmitValue(writer, mag);
        writer.Write('\n');
    }

    // grid-files.tsv only: swe_get_planet_name returns a string, not a double -- see this file's
    // own top-of-file comment for why that string is written into the err column instead of a
    // value column, and gen-grid-files.ps1's header for the fuller rationale. retc is a fixed 0;
    // the .NET API has no error code to report here either.
    private static void ProcessName(SwissEph swe, string caseId, string[] fields, TextWriter writer)
    {
        var ipl = ParseInt(fields[2], caseId, "ipl");
        var name = swe.swe_get_planet_name(ipl);

        writer.Write(caseId);
        writer.Write('\t');
        writer.Write(0.ToString(CultureInfo.InvariantCulture));
        writer.Write('\t');
        writer.Write(EscapeErr(name));
        writer.Write('\n');
    }

    private static void ProcessHouses(SwissEph swe, string caseId, string[] fields, int sidModeIndex, TextWriter writer)
    {
        RefuseIfSidModeSet(caseId, "HOUSES", fields, sidModeIndex);

        var tjd = ParseDouble(fields[3], caseId, "tjd");
        var hsys = ParseHsys(fields[5], caseId);
        var geolon = ParseDouble(fields[6], caseId, "geolon");
        var geolat = ParseDouble(fields[7], caseId, "geolat");

        var cusp = new double[40];
        var ascmc = new double[10];
        var retc = swe.swe_houses(tjd, geolat, geolon, hsys, cusp, ascmc);

        WriteHousesRow(writer, caseId, retc, cusp, ascmc);
    }

    private static void ProcessHousesArmc(SwissEph swe, string caseId, string[] fields, int sidModeIndex, TextWriter writer)
    {
        RefuseIfSidModeSet(caseId, "HOUSES_ARMC", fields, sidModeIndex);

        var armc = ParseDouble(fields[9], caseId, "armc");
        var eps = ParseDouble(fields[10], caseId, "eps");
        var hsys = ParseHsys(fields[5], caseId);
        var geolat = ParseDouble(fields[7], caseId, "geolat");

        var cusp = new double[40];
        var ascmc = new double[10];
        var retc = swe.swe_houses_armc(armc, geolat, eps, hsys, cusp, ascmc);

        WriteHousesRow(writer, caseId, retc, cusp, ascmc);
    }

    // No serr output parameter on swe_houses/swe_houses_armc -- the err column stays empty for
    // these two funcs, which is not a driver defect, the C API genuinely has nothing to report
    // there (see sedump.c's header).
    private static void WriteHousesRow(TextWriter writer, string caseId, int retc, double[] cusp, double[] ascmc)
    {
        writer.Write(caseId);
        writer.Write('\t');
        writer.Write(retc.ToString(CultureInfo.InvariantCulture));
        writer.Write('\t');
        for (var c = 0; c < CuspCount; c++)
        {
            EmitValue(writer, cusp[c]);
        }
        for (var a = 0; a < AscmcCount; a++)
        {
            EmitValue(writer, ascmc[a]);
        }
        writer.Write('\n');
    }

    private static char ParseHsys(string field, string caseId)
    {
        if (field.Length != 1)
        {
            throw new InvalidDataException($"hsys must be exactly one character at case {caseId}: '{field}'");
        }
        return field[0];
    }

    private static bool HasValue(string field) => field.Length != 0;

    private static double ParseDouble(string field, string caseId, string column)
    {
        if (field.Length == 0 || !double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidDataException($"cannot parse '{column}' as a double at case {caseId}: '{field}'");
        }
        return value;
    }

    private static int ParseInt(string field, string caseId, string column)
    {
        if (field.Length == 0 || !int.TryParse(field, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidDataException($"cannot parse '{column}' as an int at case {caseId}: '{field}'");
        }
        return value;
    }

    // Mirrors sedump.c's emit_escaped and Tools/BaselineMatrix/Format.cs's S(): a raw serr
    // string could in principle contain a tab or newline and corrupt the TSV shape if printed
    // as-is.
    private static string EscapeErr(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }
        return value
            .Replace("\\", "\\\\")
            .Replace("\t", "\\t")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }

    // %.17g on the C side; G17 is the round-trip-precise .NET counterpart, but the two runtimes
    // do not spell the same digits the same way: G17 renders an exponent as "E-05", %.17g as
    // "e-05". ToCStyleG17 below rewrites G17's output into %.17g's exact spelling (lowercase
    // exponent marker, C's minimum-two-digit exponent width) so this decimal column is directly
    // diffable against sedump.c's, instead of only agreeing once you already trust the hex
    // column beside it. The hex column stays the one a comparison pass treats as authoritative --
    // this fix makes the decimal column stop lying about that, not replaces it.
    private static void EmitValue(TextWriter writer, double value)
    {
        var bits = BitConverter.DoubleToUInt64Bits(value);
        writer.Write('\t');
        writer.Write(ToCStyleG17(value));
        writer.Write('\t');
        writer.Write(bits.ToString("x16", CultureInfo.InvariantCulture));
    }

    private static readonly char[] ExponentMarkers = ['e', 'E'];

    // Rewrites .NET's G17 exponent spelling into C's %.17g spelling. The mantissa digits
    // themselves are not touched -- both sides are printing the exact same double (the hex
    // column beside this one is the proof), and the mantissa is where the only meaningful
    // divergence could hide, so leaving it untouched is what makes this a formatting fix and
    // not a second, independent number-rendering path.
    private static string ToCStyleG17(double value)
    {
        var text = value.ToString("G17", CultureInfo.InvariantCulture);
        var markerIndex = text.IndexOfAny(ExponentMarkers);
        if (markerIndex < 0)
        {
            return text;
        }

        var mantissa = text[..markerIndex];
        var exponent = text[(markerIndex + 1)..];

        var sign = '+';
        if (exponent.Length > 0 && (exponent[0] == '+' || exponent[0] == '-'))
        {
            sign = exponent[0];
            exponent = exponent[1..];
        }

        exponent = exponent.TrimStart('0');
        if (exponent.Length < 2)
        {
            exponent = exponent.PadLeft(2, '0');
        }

        return mantissa + "e" + sign + exponent;
    }
}
