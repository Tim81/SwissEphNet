/*
 * sedump.c -- the C side of the bit-exact comparison harness.
 *
 * Replays the committed grids against Astrodienst's own C, linked in as libswe -- see
 * scripts/run-oracle-dump.ps1, which builds this file and picks which .lib it links against
 * (2.10.03 by default, 2.08 for isolating transliteration defects from porting differences).
 * Tools/OracleDump/Program.cs is this file's .NET counterpart; the two must produce output in
 * the same shape for a later, separate pass to diff.
 *
 *   Tools/OracleGrid/grid-analytic.tsv  -- swe_calc/swe_calc_ut (SEFLG_MOSEPH),
 *                                          swe_houses/swe_houses_armc, the eight crossing
 *                                          functions (swe_solcross/_ut, swe_mooncross/_ut,
 *                                          swe_mooncross_node/_ut, swe_helio_cross/_ut), also
 *                                          under SEFLG_MOSEPH, swe_get_ayanamsa/_ex/_ex_ut
 *                                          (direct ayanamsa coverage -- every predefined sid_mode
 *                                          plus SE_SIDM_USER; AYANAMSA_EX/AYANAMSA_EX_UT rows now
 *                                          always carry SEFLG_MOSEPH too -- see
 *                                          gen-grid-analytic.ps1's $AyanamsaExIflagCombos comment),
 *                                          swe_houses_ex
 *                                          (the sidereal/radians house path), swe_houses_ex2 and
 *                                          swe_houses_armc_ex2 (the 2.10.03 speed-bearing forms
 *                                          of swe_houses_ex/swe_houses_armc -- see
 *                                          SWISSEPH_HAS_HOUSES_EX2 below), swe_get_ayanamsa_ut,
 *                                          swe_sidtime, swe_azalt, swe_house_name and
 *                                          swe_nod_aps_ut. Touches no ephemeris data file.
 *                                          See gen-grid-analytic.ps1's header.
 *   Tools/OracleGrid/grid-files.tsv     -- swe_calc/swe_calc_ut (SEFLG_SWIEPH), the
 *                                          swe_fixstar family (including swe_fixstar2_mag, not
 *                                          just swe_fixstar_mag), swe_get_planet_name, the same
 *                                          eight crossing functions under SEFLG_SWIEPH,
 *                                          swe_houses_ex/swe_houses_ex2 and swe_nod_aps_ut (the
 *                                          two of the six new funcs from grid-analytic.tsv's list
 *                                          where a real .se1 file changes what gets exercised --
 *                                          the sidereal ayanamsa behind swe_houses_ex(2), and
 *                                          swe_nod_aps_ut's planetary positions), and
 *                                          swe_houses_armc_ex2 (added for dispatch/schema parity
 *                                          with grid-analytic.tsv even though it touches no file
 *                                          itself -- pure geometry, like swe_houses_armc), plus
 *                                          swe_calc_pctr (PCTR) and swe_get_current_file_data
 *                                          (GET_CURRENT_FILE_DATA), the remaining two of the
 *                                          twelve entry points new in 2.10.03. Both are
 *                                          files-grid-only: swe_calc_pctr forces SEFLG_BARYCTR
 *                                          unconditionally (sweph.c:8061) and
 *                                          SEFLG_BARYCTR|SEFLG_MOSEPH is rejected outright
 *                                          (sweph.c:634-638), so grid-analytic.tsv's forced-
 *                                          SEFLG_MOSEPH rows could only ever reach that reject,
 *                                          never real geometry -- see process_pctr's own comment;
 *                                          swe_get_current_file_data reads swed.fidat, which
 *                                          grid-analytic.tsv's rows never populate at all. Opens
 *                                          the shipped .se1/sefstars.txt files.
 *                                          See gen-grid-files.ps1's header.
 *   Tools/OracleGrid/grid-jpl.tsv       -- swe_calc/swe_calc_ut (SEFLG_JPLEPH), including the
 *                                          SEFLG_JPLHOR/SEFLG_JPLHOR_APPROX combinations no other
 *                                          grid can reach (sweph.c:6110-6112 strips both unless
 *                                          the ephemeris flag is SEFLG_JPLEPH). Opens a JPL DE
 *                                          file this repo does not ship, named by the optional
 *                                          fourth argument below. See gen-grid-jpl.ps1's header.
 *
 * Every grid shares one output shape (see OUTPUT COLUMN LAYOUT below) and one row-processing loop
 * in main(); which column layout a given input file uses dispatches on its header line, checked
 * against EXPECTED_HEADER_ANALYTIC and EXPECTED_HEADER_FILES below -- those two layouts have
 * different column counts (22 vs 18), so a header mismatch is caught before any row is parsed.
 * grid-jpl.tsv carries grid-files.tsv's header verbatim and is therefore read in MODE_FILES: it
 * needs exactly the columns that layout already defines, and what makes it a distinct grid is the
 * ephemeris flag its rows carry and the JPL file this driver is pointed at, not its schema -- see
 * gen-grid-jpl.ps1's own header for why a third, identical-but-differently-named header would
 * have bought nothing but a third parsing mode.
 *
 * SWISSEPH_HAS_CROSSING: THE EIGHT CROSSING FUNCTIONS DO NOT EXIST IN 2.08
 *
 * This same source file is compiled twice -- once here against external/swisseph (2.10.03),
 * once by Tools/CReference/build-c.ps1 against external/pyswisseph-2.08 -- and swe_solcross,
 * swe_mooncross, swe_mooncross_node, swe_helio_cross and their _ut variants are absent from
 * pyswisseph-2.08 entirely (verified: zero matches for "solcross", "mooncross" or "helio_cross"
 * anywhere under external/pyswisseph-2.08/), so a build against the 2.08 headers has no
 * declaration to call. scripts/run-oracle-dump.ps1 defines SWISSEPH_HAS_CROSSING=1 on the command
 * line when it compiles this file against 2.10.03; Tools/CReference/build-c.ps1's 2.08 build does
 * not define it, so the #else branch below applies there by default, with no change needed to
 * that script. The #else branch cannot call the real API, but it still emits exactly one row
 * per crossing case, with the same column count the real branch would use for that func, and a
 * clearly out-of-band retc (NOT_IN_208_RETC) plus an explanatory serr -- so a 2.08 build's row
 * count for a crossing-bearing grid still matches the grid's own row count (see
 * scripts/run-oracle-dump.ps1's own row-count guards, which fail loudly on any mismatch) and the
 * row still parses cleanly for any future three-way classification that reads the 2.08 dump.
 *
 * SWISSEPH_HAS_HOUSES_EX2: swe_houses_ex2/swe_houses_armc_ex2 DO NOT EXIST IN 2.08 EITHER
 *
 * Same shape as SWISSEPH_HAS_CROSSING immediately above, for a different pair of functions:
 * swe_houses_ex2 and swe_houses_armc_ex2 are absent from external/pyswisseph-2.08/swephexp.h
 * entirely (verified: zero matches for either name anywhere under external/pyswisseph-2.08/), so
 * a 2.08 build has no declaration for either. scripts/run-oracle-dump.ps1 also defines
 * SWISSEPH_HAS_HOUSES_EX2=1 alongside SWISSEPH_HAS_CROSSING when it compiles this file against
 * 2.10.03; Tools/CReference/build-c.ps1's 2.08 build does not define it either, so the #else
 * branch (process_houses_ex2/process_houses_armc_ex2's own NOT_IN_208 path) applies there,
 * emitting the same 94-double (188 value column) shape the real branch would, with
 * NOT_IN_208_RETC and an explanatory serr, for the same row-count-parity reason
 * SWISSEPH_HAS_CROSSING's own comment gives. swe_fixstar2_mag needs no such guard: it is declared
 * (and implemented) in external/pyswisseph-2.08/swephexp.h:708.
 *
 * SWISSEPH_HAS_CALC_PCTR / SWISSEPH_HAS_GET_CURRENT_FILE_DATA: NEITHER EXISTS IN 2.08
 *
 * A third pair, same shape again, one macro per function this time rather than one macro for
 * both: swe_calc_pctr and swe_get_current_file_data are two unrelated features (planetocentric
 * coordinates; ephemeris-file introspection), unlike swe_houses_ex2/swe_houses_armc_ex2 above,
 * which are two entry points onto the same speed-bearing house feature. Both are absent from
 * external/pyswisseph-2.08/swephexp.h entirely (verified: `grep -oE '\bswe_[A-Za-z0-9_]+\s*\('
 * external/pyswisseph-2.08/swephexp.h` finds neither name anywhere in that file's 96 distinct
 * swe_* declarations -- do not assume otherwise from how small the diff to add them looks; the
 * same check against external/swisseph/swephexp.h, the 2.10.03 header, finds both among its 108).
 * scripts/run-oracle-dump.ps1 defines SWISSEPH_HAS_CALC_PCTR=1 and
 * SWISSEPH_HAS_GET_CURRENT_FILE_DATA=1 alongside the other two when it compiles this file against
 * 2.10.03; Tools/CReference/build-c.ps1's 2.08 build defines neither, so the #else branches
 * (process_pctr/process_get_current_file_data's own NOT_IN_208 paths) apply there, at the same
 * column count the real branch uses, for the same row-count-parity reason SWISSEPH_HAS_CROSSING's
 * own comment gives.
 *
 * THE SENTINEL EPHEMERIS PATH: grid-analytic.tsv MUST NOT DEPEND ON THE ENVIRONMENT OR CWD
 *
 * grid-analytic.tsv's own header claims every row "depends on no ephemeris data file and is
 * reproducible on any machine". Before this addition, the two-argument invocation (no ephe-dir)
 * left swed.ephepath at whatever swi_init_swed_if_start() set it to at process start
 * (sweph.c:1186: strcpy(swed.ephepath, SE_EPHE_PATH), the compiled-in default) UNTIL the first row
 * whose epheflag was not SEFLG_MOSEPH ran far enough to trigger sweph.c:639-640's lazy
 * `swe_set_ephe_path(NULL)` -- and swe_set_ephe_path checks getenv("SE_EPHE_PATH") before it looks
 * at its own argument at all (sweph.c:1327-1330), so from that row onward, for the rest of the
 * process, swed.ephepath reflected whichever of the environment variable or the compiled default
 * happened to apply on THIS run, on THIS machine. main() below now calls swe_set_ephe_path
 * unconditionally before every row -- SENTINEL_EPHE_DIR when the grid gave no ephe-dir argument,
 * the real ephe-dir otherwise -- so ephe_path_is_set is TRUE from row 1 and every row sees the
 * same, deterministic, guaranteed-nonexistent path regardless of iteration order, CWD or whether
 * "\sweph\ephe\" happens to exist on this machine. See SENTINEL_EPHE_DIR's own comment for the
 * separate fix below (CLEAR_INHERITED_SE_EPHE_PATH) that closes the one thing pinning the
 * sentinel could not: SE_EPHE_PATH overriding it whenever the variable happens to be set in this
 * process's own environment.
 *
 * CLEAR_INHERITED_SE_EPHE_PATH: THIS PROCESS'S OWN SE_EPHE_PATH, NOT ANY OTHER PROCESS'S
 *
 * swe_set_ephe_path gives getenv("SE_EPHE_PATH") priority over the path it was passed
 * (sweph.c:1327-1330: "environment variable SE_EPHE_PATH has priority"), faithfully ported at
 * SwissEphNet/CPort/Sweph.cs:1569-1583 -- and that priority applies to an explicit, real ephe-dir
 * argument exactly as much as it applies to SENTINEL_EPHE_DIR above; nothing about "a real
 * directory was passed" makes swe_set_ephe_path skip the getenv check. Measured on grid-files.tsv
 * (the grid CI actually gates, with a real ephe-dir passed on the command line and SE_EPHE_PATH
 * pointed at an empty directory): 2,223 of 3,251 rows change, 2,219 of them in value columns --
 * a contributor with that variable exported would have this driver silently read from their
 * directory instead of the one this invocation actually named, both sides would agree because
 * both sides are equally hijacked, and verify-oracle would stay green while measuring the wrong
 * data. main() below clears SE_EPHE_PATH from this process's own environment, once, before the
 * row loop starts and before swe_set_ephe_path is ever called -- so getenv("SE_EPHE_PATH") in
 * swe_set_ephe_path returns NULL for every row regardless of what this process inherited, and the
 * path argument (SENTINEL_EPHE_DIR or the real ephe-dir) is what actually governs. This is a
 * driver-level change made from outside swe_set_ephe_path, not an edit to that function -- it
 * remains a frozen, faithful transliteration of sweph.c:1315-1350, priority check included.
 *
 * What this closes: the recorded dump artefacts (dump-c-2.10.03.tsv, dump-net.tsv, and their
 * SHA-256s) and the files-grid comparison verify-oracle gates on no longer depend on whether the
 * machine running this driver happens to have SE_EPHE_PATH exported, for either grid.
 *
 * What this does NOT close: it clears the variable in THIS process only, using _putenv_s (POSIX
 * builds of this same file use unsetenv instead -- see main()) rather than editing the parent
 * shell's or CI runner's own environment, which a child process cannot do and should not try to.
 * It also does not reach swetest.exe -- a separate binary, built by this same script from
 * swetest.c, and exercised only by scripts/verify-swetest-diff.ps1, not by this driver -- so a
 * SE_EPHE_PATH set on a machine running the swetest text-diff still resolves against that
 * variable exactly as sweph.c:1327-1330 always intended. Nor does it change swe_set_ephe_path's
 * own priority for any OTHER caller of this library; it only ever affects what THIS process's own
 * getenv("SE_EPHE_PATH") returns.
 *
 * INVOCATION
 *
 *   sedump.exe <grid.tsv> <output.tsv> [ephe-dir [jpl-file]]
 *
 * ephe-dir is optional. grid-analytic.tsv needs it never (every row forces SEFLG_MOSEPH, so no
 * row ever opens a file) and the existing two-argument invocation is untouched -- passing it is
 * new, additive behavior, not a change to how the analytic grid has always been run. grid-files.tsv
 * needs it always: when given, swe_set_ephe_path(ephe-dir) runs at the top of every row's
 * processing, right after swe_close() (see FRESH LIBRARY STATE PER ROW below) -- this is the "The
 * C side just needs swe_set_ephe_path to the same directory" half of the fix
 * Tests/SwissEphNet.Conformance.Tests/Dispatch/EphemerisFileResolver.cs's Attach describes for
 * the .NET side (sweph.c:1315-1350: swe_set_ephe_path is not a setter, it closes every open file
 * and eagerly opens the Moon file to pin tidal acceleration, so the path has to be set before any
 * row-specific call runs, not after).
 *
 * jpl-file is optional too, and only grid-jpl.tsv needs it. When given, swe_set_jpl_file(jpl-file)
 * runs immediately AFTER swe_set_ephe_path, once per row. That order is not incidental and cannot
 * be swapped: swe_set_jpl_file opens the file eagerly, right there in the call, resolving the name
 * against swed.ephepath as it stands at that moment (sweph.c:1499-1505). Called before
 * swe_set_ephe_path it would resolve against whatever path was left over -- SE_EPHE_PATH's
 * compiled-in default on the first row -- almost certainly fail to find the file, and so never
 * reach the jpldenum >= 403 branch below; swe_set_ephe_path would then close the JPL file it did
 * not manage to open anyway. Every SEFLG_JPLEPH row would fall back through SEFLG_SWIEPH to
 * Moshier (sweph.c:894-913) and compare bit-identical between the two sides while measuring
 * nothing about the JPL backend at all. For the same reason, passing jpl-file with an empty
 * ephe-dir is rejected outright in main() instead of being left to resolve against SE_EPHE_PATH.
 *
 * Passing jpl-file also has one effect no other argument does: swe_set_jpl_file is the only caller
 * of load_dpsi_deps in the whole library (sweph.c:1503-1504, on the branch where the file it just
 * opened reports jpldenum >= 403), so this argument is the only way either driver reaches that
 * function at all. gen-grid-jpl.ps1's header describes how the SEFLG_JPLHOR rows make that
 * reachability observable in the err column instead of merely asserted.
 *
 * ONE PIECE OF STATE swe_close() DOES NOT RESET: swed.eop_dpsi_loaded
 *
 * swe_close() frees swed.dpsi and swed.deps but leaves swed.eop_dpsi_loaded at whatever
 * load_dpsi_deps last wrote (sweph.c's swe_close: the two free() calls have no accompanying
 * assignment, and the port mirrors that faithfully in SwissEphNet/CPort/Sweph.cs). This driver's
 * per-row swe_close() therefore does NOT give a row a fresh eop state, while
 * Tools/OracleDump/Program.cs's fresh SwissEph instance does -- the same shape of difference this
 * file's FRESH LIBRARY STATE PER ROW section already documents for swe_houses_armc_ex2's
 * saved_sundec, and like that one it does not currently bite, for a reason worth writing down:
 *
 *   load_dpsi_deps returns early only when eop_dpsi_loaded > 0, i.e. only after a SUCCESSFUL
 *   load. With neither eop_1962_today.txt nor eop_finals.txt in ephe-dir -- which is the case for
 *   every directory this repo declares -- the very first row's call fails at swi_fopen and writes
 *   ERR (-1). -1 is not > 0, so every later row runs the same code and writes the same -1, and the
 *   C side's carried-over value is indistinguishable from the .NET side's freshly-computed one.
 *
 * Put those two files in ephe-dir and that stops being true: row 1 would write 1 or 2 and
 * allocate dpsi/deps, row 2's swe_close() would free both arrays while leaving the > 0 marker in
 * place, and load_dpsi_deps would then return early without reallocating -- leaving the C side
 * claiming loaded EOP data it no longer has, against a .NET side that reloaded it. That is a real
 * asymmetry in this harness (arguably a latent defect in the C's own swe_close), so if this driver
 * is ever pointed at a directory carrying the EOP files, it needs a way to reset that field
 * between rows before the resulting diff can be read as a statement about the port.
 *
 * FRESH LIBRARY STATE PER ROW
 *
 * swe_houses_armc_ex2 keeps a hidden C static, saved_sundec (external/swisseph/swehouse.c:636),
 * that changes hsys 'I'/'i' results depending on what a PRIOR call computed (see
 * Tools/BaselineGen/Program.cs's header and SwissEphNet/CPort/SweHouse.cs). swe_close() does not
 * touch it and cannot: saved_sundec is a function-local static; swe_close() only resets fields of
 * swed. That does not currently bite: both drivers zero-initialize
 * ascmc, so ascmc[9] == 0 on every row, and swe_houses_armc_ex2's hsys 'I' branch only ever reads
 * saved_sundec when ascmc[9] == 99 (Astrodienst's documented "no Sun declination supplied"
 * signal) -- with ascmc[9] == 0, the function always takes the branch that WRITES saved_sundec,
 * never the one that reads a value carried over from a prior row. Every hsys 'I'/'i' row grid-
 * files.tsv contains (792 of them) takes the write branch, so this driver's C state and the
 * .NET side's per-instance state (saved_sundec is an instance field there, never shared across
 * calls) stay observably equivalent despite the difference in how each implements "fresh".
 * A future grid row that sets ascmc[9] = 99 on purpose would change that: it would make the C
 * side read whatever a prior row last wrote to saved_sundec, carrying state this driver has no
 * way to reset between rows, while the .NET side started that row with a clean instance -- the
 * two sides would disagree for a reason that has nothing to do with the port being compared.
 * Every grid-files.tsv row additionally depends on which segment of which file is currently
 * cached (free_planets, the fidat table); swe_close() does reset that (it is swed state), so it
 * runs before every row here, not just once at the end -- getting this wrong would make this
 * driver disagree with Tools/OracleDump/Program.cs (which constructs a fresh SwissEph instance,
 * and for grid-files.tsv rows, a fresh swe_set_ephe_path() call, per row) for a reason that has
 * nothing to do with the port being compared.
 *
 * OUTPUT COLUMN LAYOUT
 *
 * One line per data row, tab separated:
 *
 *   case_id, retc, err, then every double the row's func returns as a (decimal, hex) pair
 *
 * "every double the row's func returns" is fixed per func, not per row, so the column count for
 * a given func never depends on which house system, iflag or star name a particular row happens
 * to use:
 *
 *   CALC, CALC_UT                            xx[0..5]                 (6 doubles  -> 12 value columns)
 *   HOUSES, HOUSES_ARMC                      cusp[0..36], ascmc[0..9] (47 doubles -> 94 value columns)
 *   FIXSTAR, FIXSTAR_UT, FIXSTAR2, FIXSTAR2_UT  xx[0..5]              (6 doubles  -> 12 value columns)
 *   FIXSTAR_MAG, FIXSTAR2_MAG                mag                      (1 double   -> 2 value columns)
 *   GET_PLANET_NAME                          (none)                   (0 value columns)
 *   SOLCROSS, SOLCROSS_UT, MOONCROSS,
 *     MOONCROSS_UT, HELIO_CROSS,
 *     HELIO_CROSS_UT                         jd_cross                 (1 double   -> 2 value columns)
 *   MOONCROSS_NODE, MOONCROSS_NODE_UT        jd_cross, xlon, xla      (3 doubles  -> 6 value columns)
 *   AYANAMSA, AYANAMSA_EX, AYANAMSA_EX_UT    daya                     (1 double   -> 2 value columns)
 *   HOUSES_EX                                cusp[0..36], ascmc[0..9] (47 doubles -> 94 value columns)
 *   HOUSES_EX2, HOUSES_ARMC_EX2              cusp[0..36], ascmc[0..9],
 *                                            cusp_speed[0..36],
 *                                            ascmc_speed[0..9]        (94 doubles -> 188 value columns)
 *   AYANAMSA_UT                              daya                     (1 double   -> 2 value columns)
 *   SIDTIME                                  tsid                     (1 double   -> 2 value columns)
 *   AZALT                                    xaz[0..2]                (3 doubles  -> 6 value columns)
 *   HOUSE_NAME                               (none)                   (0 value columns)
 *   NOD_APS_UT             xnasc[0..5], xndsc[0..5], xperi[0..5], xaphe[0..5] (24 doubles -> 48 value columns)
 *   PCTR                                      xxret[0..5]              (6 doubles  -> 12 value columns)
 *   GET_CURRENT_FILE_DATA                    tfstart, tfend, denum    (3 doubles  -> 6 value columns)
 *
 * GET_PLANET_NAME has no value column at all: swe_get_planet_name returns a string, not a
 * double, so there is nothing to hex-encode. Its returned name is written into the err column
 * instead of a value column -- see gen-grid-files.ps1's header for why that column, specifically,
 * is the right one for it. AYANAMSA (plain swe_get_ayanamsa) has an err column too, but it is
 * always empty rather than repurposed: swe_get_ayanamsa has no serr output parameter and no error
 * signal of any kind, so there is nothing to write there -- see process_ayanamsa. HOUSES/
 * HOUSES_ARMC's cusp[0..36], not just cusp[1..12], is because
 * hsys 'G' (Gauquelin sectors) populates cusp[1..36] and a fixed column count keeps every func's
 * row mechanically the same width regardless of house system -- cusp[13..36] simply stay at
 * their zero-initialized default for every other system (matches Tools/BaselineMatrix/Houses.cs's
 * own reasoning for the same choice). retc/err come right after case_id, not after the doubles,
 * purely so a reader can see whether a row errored before scanning past however many value
 * columns that func has.
 *
 * HOUSES_EX (swehouse.c:178) has no serr parameter either -- it forwards to swe_houses_ex2 with
 * serr hardcoded NULL (swehouse.c:186) -- so its err column is empty, the same convention HOUSES/
 * HOUSES_ARMC already use, and its retc/cusp/ascmc columns are laid out identically to HOUSES.
 * AYANAMSA_UT (swe_get_ayanamsa_ut, sweph.c:3260) and SIDTIME (swe_sidtime, swephlib.c:3580) are
 * both bare doubles with no serr and no error signal at all, matching AYANAMSA's own convention:
 * a fixed OK retc and an empty err column. HOUSE_NAME (swe_house_name, swehouse.c:827) returns
 * const char *, never NULL, so it has no value column at all and its name goes into the err
 * column, matching GET_PLANET_NAME's own convention; its retc is a fixed 0, the same "the C API
 * genuinely has nothing to report there" reason GET_PLANET_NAME's is. AZALT (swe_azalt,
 * swecl.c:2788) returns void -- no retc, no serr -- so its retc is a fixed OK and its err column
 * stays empty; see process_azalt for why xin[2] never gets a grid column. NOD_APS_UT
 * (swe_nod_aps_ut, swecl.c:5645, delegating to swe_nod_aps at swecl.c:5064) is the one of these
 * six with a real int32 retc and a real serr, and the one whose four six-double output arrays
 * (xnasc, xndsc, xperi, xaphe) are zeroed only on its "not implemented" reject branch
 * (swecl.c:5134-5146) -- see process_nod_aps_ut for why both drivers zero-initialize all four
 * before every call regardless.
 *
 * HOUSES_EX2 (swe_houses_ex2, swehouse.c:207) and HOUSES_ARMC_EX2 (swe_houses_armc_ex2,
 * swehouse.c:622) are the 2.10.03 forms HOUSES_EX/HOUSES_ARMC/HOUSES call with cusp_speed/
 * ascmc_speed/serr hardcoded NULL (swehouse.c:173, 186, 598, 173-again via swe_houses ->
 * swe_houses_armc_ex2), switching the speed feature off entirely (h.do_speed/h.do_hspeed stay
 * FALSE whenever both pointers are NULL, swehouse.c:642-647). Both take real, non-NULL
 * cusp_speed/ascmc_speed arrays here, so do_speed/do_hspeed are TRUE and swehouse.c:663,671,685
 * actually write. Both also have a REAL serr, unlike HOUSES/HOUSES_ARMC/HOUSES_EX above (whose err
 * columns are either always empty or repurposed): swe_houses_armc_ex2 writes serr on its hsys 'I'
 * out-of-range-declination reject (swehouse.c:656-659, "House system I (Sunshine) needs valid Sun
 * declination in ascmc[9]") and on CalcH failure (swehouse.c:667, strcpy(serr, h.serr)); swe_houses_ex2
 * forwards whatever the delegated armc_ex2/sidereal_houses_* call wrote (swehouse.c:277,
 * 271-275). cusp_speed/ascmc_speed are zero-initialized before every call, the same rule
 * process_helio_cross applies to jd_cross: swehouse.c:663/671 write cusp_speed[0..ito] only when
 * do_hspeed is TRUE (always true here) but only up to `ito` (12 for every hsys but 'G', which
 * uses 36), so cusp_speed[ito+1..36] is never written by that loop on a non-'G' row and would
 * otherwise hold whatever was on the stack; ascmc_speed[0..9] is written in full whenever
 * do_speed is TRUE (swehouse.c:685-696), but only inside that guard, so a caller that zero-inits
 * first is safe regardless of which branch runs. See process_houses_armc_ex2's own comment for
 * why ascmc[9] is zero-initialized too, exactly as HOUSES_ARMC already does, and what that means
 * for the saved_sundec static this file's own "FRESH LIBRARY STATE PER ROW" section already
 * documents.
 *
 * PCTR (swe_calc_pctr, sweph.c:8042) has a real int32 retc and a real serr, same shape as CALC --
 * see process_pctr for why xxret is zero-initialized before the call. GET_CURRENT_FILE_DATA
 * (swe_get_current_file_data, sweph.c:8297) returns const char *, NULL on either of two reject
 * branches (ifno out of [0,4]; swed.fidat[ifno].fnam empty) -- same "the string goes in the err
 * column" convention as GET_PLANET_NAME/HOUSE_NAME, but with a synthesized retc (OK when non-NULL,
 * ERR when NULL) rather than a fixed one, since NULL vs non-NULL is this func's only outcome to
 * report and both drivers already compute a synthetic retc for the crossing functions the same
 * way -- see process_get_current_file_data.
 *
 * THE CROSSING FUNCTIONS' retc COLUMN: ONE REAL, SIX SYNTHETIC
 *
 * swe_helio_cross(_ut) is the only one of the eight with a real int32 return code (OK/ERR); its
 * jd_cross output parameter is written only on the OK path (external/swisseph/sweph.c:8567,8613),
 * left untouched on every ERR return, so this driver zero-initializes it before the call -- an
 * ERR row's jd_cross column is then a deterministic 0.0 on both sides, not whatever happened to
 * be on each side's stack. The other six (swe_solcross/_ut, swe_mooncross/_ut,
 * swe_mooncross_node/_ut) return the crossing time itself as a double, with no int32 at all;
 * Astrodienst's own doc comment on each says errors are "indicated by returning a jd < jd_et [or
 * jd_ut]!" (external/swisseph/sweph.c:8319, 8353, 8387, 8421, 8454, 8491). This driver computes a
 * retc for those six itself -- ERR (-1) when the returned jd is less than the input jd, OK (0)
 * otherwise -- purely so the row still fits the shared "case_id, retc, err, values..." shape
 * every other func in this file already uses. Tools/OracleDump/Program.cs computes the identical
 * value from the identical returned bits, so this synthetic column can never disagree between the
 * two sides on its own. swe_mooncross_node(_ut)'s xlon/xla output parameters follow the same
 * zero-initialize-before-the-call rule as swe_helio_cross's jd_cross, for the same reason: they
 * are written only on the convergence path (external/swisseph/sweph.c:8480-8481, 8517-8518).
 *
 * Decimal columns (%.17g) are for a human reading the file; the hex columns are what a
 * comparison pass should actually diff; two decimal strings from two different printf/ToString
 * implementations are not guaranteed to render identically even when they represent the exact
 * same bits. swe_houses, swe_houses_armc and swe_get_planet_name have no error-string output
 * parameter at all, so their err column is either always empty (houses) or repurposed to carry
 * the return value itself (GET_PLANET_NAME) -- that is not a driver defect, the C API genuinely
 * has nothing else to report there.
 *
 * A malformed row (wrong column count, unparseable number, unknown func) is a hard failure: this
 * driver must not silently skip a row and emit fewer lines than the grid contains, which would
 * let a later comparison pass quietly run over a truncated set of cases.
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <stdarg.h>
#include <errno.h>
#include "swephexp.h"

#define MAX_LINE 4096
#define ANALYTIC_COLUMNS 22
#define FILES_COLUMNS 20
#define CUSP_COUNT 37   /* cusp[0..36] */
#define ASCMC_COUNT 10  /* ascmc[0..9] */
#define STAR_BUF_LEN AS_MAXCH
/* Out-of-band retc for a crossing-func row emitted by a build with SWISSEPH_HAS_CROSSING
 * undefined (2.08) -- see this file's own top-of-file comment. Never a value swe_solcross and
 * friends (or this driver's own synthetic OK/ERR for them) could produce, so it cannot be
 * mistaken for a real result. */
#define NOT_IN_208_RETC (-9999)

/* Pinned when a row's grid gives no ephe-dir argument (today: only grid-analytic.tsv's
 * two-argument invocation -- see main()'s own SENTINEL_EPHE_DIR use). Deliberately contains '?',
 * which is not a legal character in a Windows path component, so no real directory can ever
 * match it there: swi_fopen's search fails the same deterministic way regardless of the
 * machine's current directory, whether "\sweph\ephe\" (the compiled-in SE_EPHE_PATH default
 * under MSDOS -- swephexp.h:399-408) happens to exist, or which files that default or a stray
 * CWD happen to contain. That guarantee is Windows-specific, not universal: '?' is a legal
 * filename byte on ext4 and APFS (POSIX forbids only NUL and '/' in a pathname component; APFS
 * additionally reserves ':', not '?'), and this same driver, built with this same sentinel, runs
 * under both linux-exactness and macos-exactness. The practical risk stays negligible -- a real
 * directory named literally "swisseph-oracle-sentinel-path?that-cannot-exist" would have to exist
 * on the CI runner or a contributor's machine -- but the sentence above is an absolute that is
 * false on two of the three platforms this driver is gated on, so it is stated as a Windows fact,
 * not a cross-platform one. On its own this constant does not insulate a row from the SE_EPHE_PATH environment
 * variable: swe_set_ephe_path checks getenv("SE_EPHE_PATH") BEFORE looking at the path argument
 * passed to it at all (sweph.c:1327-1330) and uses the environment value instead when it is set,
 * so a caller with SE_EPHE_PATH exported would still see that path, sentinel argument or not.
 * main()'s CLEAR_INHERITED_SE_EPHE_PATH step (see the header comment above) is what closes that
 * gap now, by clearing the variable from this process's own environment before the row loop
 * starts -- from outside swe_set_ephe_path, not by editing it, since that function is a frozen
 * transliteration. This constant's own job stays narrower: removing the grid's dependency on the
 * compiled-in default and the process's current directory. See
 * scripts/run-oracle-dump.ps1's own sentinel-path measurement for what this does and does not
 * close. */
#define SENTINEL_EPHE_DIR "swisseph-oracle-sentinel-path?that-cannot-exist"

/* Mode dispatches on which of these two headers the grid's first non-comment line matches --
 * see this file's own top-of-file comment. x2cross, dir, t0 and ayan_t0 are appended after
 * sid_mode in both headers, not interleaved among the original columns, so every column this
 * file's other process_* functions already index by a fixed offset keeps that same offset. t0/
 * ayan_t0 carry swe_set_sid_mode's own SE_SIDM_USER parameters -- see apply_sid_mode.
 *
 * method, calc_flag, atpress, attemp, xin0, xin1 (analytic) and method, hsys (files) are a second
 * additive tail, appended after t0/ayan_t0 for the same reason x2cross/dir/t0/ayan_t0 themselves
 * were: HOUSES_EX/AYANAMSA_UT/SIDTIME/AZALT/HOUSE_NAME/NOD_APS_UT (see the dispatch table in
 * main() and the six new process_* functions below) need columns none of the funcs already in
 * this grid used, and every existing column's offset had to keep meaning what it always meant.
 * method carries swe_nod_aps_ut's own method bitmask; calc_flag/atpress/attemp/xin0/xin1 carry
 * swe_azalt's own parameters (xin[2] is never read by swe_azalt -- see process_azalt -- so there
 * is no xin2 column); hsys (files grid only) carries HOUSES_EX's house-system letter, since the
 * files grid has no hsys column of its own the way the analytic grid's HOUSES/HOUSES_ARMC rows
 * already share at fields[5]. armc/eps (files grid only) are a third additive tail, for
 * HOUSES_ARMC_EX2 (see gen-grid-files.ps1's own header). iplctr/ifno (files grid only) are a
 * fourth: iplctr carries swe_calc_pctr's second body (PCTR reuses ipl/tjd/iflag for its first
 * body and iflag the same way CALC does -- see process_pctr); ifno carries
 * swe_get_current_file_data's file-slot index (GET_CURRENT_FILE_DATA also reuses ipl/tjd/iflag,
 * to trigger an optional preceding swe_calc, and star, to trigger an optional preceding
 * swe_fixstar2 -- see process_get_current_file_data). Neither PCTR nor GET_CURRENT_FILE_DATA
 * appears in grid-analytic.tsv (see this file's own top-of-file comment for why), so
 * EXPECTED_HEADER_ANALYTIC carries neither column. */
static const char *EXPECTED_HEADER_ANALYTIC =
    "case_id\tfunc\tipl\ttjd\tiflag\thsys\tgeolon\tgeolat\theight\tarmc\teps\tsid_mode\tx2cross\tdir\tt0\tayan_t0"
    "\tmethod\tcalc_flag\tatpress\tattemp\txin0\txin1";
static const char *EXPECTED_HEADER_FILES =
    "case_id\tfunc\tipl\ttjd\tiflag\tstar\tgeolon\tgeolat\theight\tsid_mode\tx2cross\tdir\tt0\tayan_t0"
    "\tmethod\thsys\tarmc\teps\tiplctr\tifno";

enum grid_mode { MODE_ANALYTIC, MODE_FILES };

static void die(const char *fmt, ...)
{
    va_list args;
    va_start(args, fmt);
    vfprintf(stderr, fmt, args);
    va_end(args);
    fprintf(stderr, "\n");
    exit(1);
}

static uint64_t bits_of(double x)
{
    uint64_t bits;
    memcpy(&bits, &x, sizeof bits);
    return bits;
}

static void rtrim(char *s)
{
    size_t len = strlen(s);
    while (len > 0 && (s[len - 1] == '\n' || s[len - 1] == '\r')) {
        s[--len] = '\0';
    }
}

/*
 * Splits line in place on tabs. Unlike strtok, this preserves empty fields between consecutive
 * tabs -- the grid relies on that to mean "this column does not apply to this row's func", and
 * silently collapsing "a\t\tb" into two fields instead of three would misalign every column
 * after the first empty one.
 *
 * Returns the total field count, which may exceed max_fields; fields beyond max_fields are not
 * written into the fields[] array (to avoid writing past its end), so the caller must check the
 * returned count against what it expects before indexing into fields[].
 */
static int split_fields(char *line, char *fields[], int max_fields)
{
    int count = 0;
    char *p = line;
    if (count < max_fields) fields[count] = p;
    count++;
    while (*p) {
        if (*p == '\t') {
            *p = '\0';
            if (count < max_fields) fields[count] = p + 1;
            count++;
        }
        p++;
    }
    return count;
}

static int has_value(const char *s)
{
    return s[0] != '\0';
}

static double parse_double(const char *s, const char *case_id, const char *col)
{
    char *end;
    double v;
    if (s[0] == '\0') die("missing required field '%s' at case %s", col, case_id);
    errno = 0;
    v = strtod(s, &end);
    if (end == s || *end != '\0') die("cannot parse '%s' as a double at case %s: '%s'", col, case_id, s);
    return v;
}

static long parse_int(const char *s, const char *case_id, const char *col)
{
    char *end;
    long v;
    if (s[0] == '\0') die("missing required field '%s' at case %s", col, case_id);
    errno = 0;
    v = strtol(s, &end, 10);
    if (end == s || *end != '\0') die("cannot parse '%s' as an int at case %s: '%s'", col, case_id, s);
    return v;
}

static int parse_hsys(const char *s, const char *case_id)
{
    if (s[0] == '\0' || s[1] != '\0') {
        die("hsys must be exactly one character at case %s: '%s'", case_id, s);
    }
    return (unsigned char)s[0];
}

static void emit_value(FILE *out, double v)
{
    fprintf(out, "\t%.17g\t%016llx", v, (unsigned long long)bits_of(v));
}

/* Mirrors Tools/OracleDump/Program.cs's EscapeErr and Tools/BaselineMatrix/Format.cs's S(): a
 * raw serr string could in principle contain a tab or newline and corrupt the TSV shape if
 * printed as-is. */
static void emit_escaped(FILE *out, const char *s)
{
    for (; *s; s++) {
        switch (*s) {
            case '\\': fputs("\\\\", out); break;
            case '\t': fputs("\\t", out); break;
            case '\r': fputs("\\r", out); break;
            case '\n': fputs("\\n", out); break;
            default:   fputc(*s, out);
        }
    }
}

/*
 * Applies swe_set_sid_mode when the row's sid_mode column is non-empty. t0/ayan_t0 (swe_set_sid_mode's
 * own SE_SIDM_USER parameters) always sit exactly 3 and 4 columns after sid_mode in both grids --
 * sid_mode, x2cross, dir, t0, ayan_t0, in that fixed relative order, for both the 16-column
 * analytic layout (sid_mode_idx 11) and the 14-column files layout (sid_mode_idx 9) -- see
 * gen-grid-analytic.ps1's and gen-grid-files.ps1's own header comments on why x2cross/dir/t0/
 * ayan_t0 are appended in that order rather than interleaved among the original columns. A row
 * with no sid_mode never reads t0/ayan_t0 at all: an empty sid_mode column means "this row's func
 * does not touch the sidereal frame" and t0/ayan_t0 mean nothing without it. An empty t0/ayan_t0
 * on a row that DOES set sid_mode means 0.0 -- the same default swe_set_sid_mode(sid_mode, 0, 0)
 * always passed before this driver could express SE_SIDM_USER at all.
 */
static void apply_sid_mode(char *fields[], const char *case_id, int sid_mode_idx)
{
    int32 sid_mode;
    double t0, ayan_t0;

    if (!has_value(fields[sid_mode_idx])) return;

    sid_mode = (int32)parse_int(fields[sid_mode_idx], case_id, "sid_mode");
    t0 = has_value(fields[sid_mode_idx + 3]) ? parse_double(fields[sid_mode_idx + 3], case_id, "t0") : 0.0;
    ayan_t0 = has_value(fields[sid_mode_idx + 4]) ? parse_double(fields[sid_mode_idx + 4], case_id, "ayan_t0") : 0.0;
    swe_set_sid_mode(sid_mode, t0, ayan_t0);
}

/*
 * MOONCROSS_NODE(_UT), HELIO_CROSS(_UT), the FIXSTAR family (FIXSTAR/FIXSTAR_UT/FIXSTAR2/
 * FIXSTAR2_UT) and HOUSES/HOUSES_ARMC never call apply_sid_mode: none of the C functions behind
 * them (swe_mooncross_node(_ut), swe_helio_cross(_ut), swe_fixstar(2)(_ut), swe_houses(_armc))
 * takes a sidereal-frame parameter at all in Astrodienst's own API, so there is nothing for this
 * driver to apply. Every grid row for these funcs is therefore expected to carry an empty
 * sid_mode column -- and today, every one of them does (verified: this guard has never fired
 * against Tools/OracleGrid/grid-analytic.tsv or grid-files.tsv).
 *
 * This hard-fails instead of silently ignoring a non-empty sid_mode, because "silently ignore
 * it" is exactly the failure mode that made this a blind spot in the first place: a future
 * sidereal MOONCROSS_NODE row would have both drivers ignore the column the same way, the row
 * would compare bit-identical between them, and the comparison would prove nothing about either
 * driver's (non-existent) sidereal handling for that func -- see this file's sibling check in
 * Tools/OracleDump/Program.cs's RefuseIfSidModeSet for the .NET side of the same guard.
 */
static void refuse_if_sid_mode_set(const char *case_id, const char *func, char *fields[], int sid_mode_idx)
{
    if (has_value(fields[sid_mode_idx])) {
        die("%s: func '%s' has a non-empty sid_mode ('%s'), but this driver never calls "
            "apply_sid_mode for it -- %s has no sidereal-frame parameter in Astrodienst's C API. "
            "Either this row's sid_mode should be empty (a grid-generation defect), or "
            "apply_sid_mode needs to be wired up for this func (an API change this driver has not "
            "caught up with).",
            case_id, func, fields[sid_mode_idx], func);
    }
}

/*
 * Shared by both grids: the two column layouts agree on ipl/tjd/iflag/geolon/geolat/height at
 * fields[2..8], and only disagree on where sid_mode lives (analytic's 12-column layout carries
 * hsys/armc/eps between height and sid_mode; the 10-column files layout does not) -- sid_mode_idx
 * is the one difference the two callers below pass in.
 */
static void process_calc(FILE *out, const char *case_id, const char *func, char *fields[], int sid_mode_idx)
{
    int ipl = (int)parse_int(fields[2], case_id, "ipl");
    double tjd = parse_double(fields[3], case_id, "tjd");
    int32 iflag = (int32)parse_int(fields[4], case_id, "iflag");
    double xx[6] = { 0 };
    char serr[AS_MAXCH];
    int retc, i;

    serr[0] = '\0';

    if (has_value(fields[6]) || has_value(fields[7]) || has_value(fields[8])) {
        double geolon = parse_double(fields[6], case_id, "geolon");
        double geolat = parse_double(fields[7], case_id, "geolat");
        double height = parse_double(fields[8], case_id, "height");
        swe_set_topo(geolon, geolat, height);
    }
    apply_sid_mode(fields, case_id, sid_mode_idx);

    if (strcmp(func, "CALC") == 0)
        retc = swe_calc(tjd, ipl, iflag, xx, serr);
    else
        retc = swe_calc_ut(tjd, ipl, iflag, xx, serr);

    fprintf(out, "%s\t%d\t", case_id, retc);
    emit_escaped(out, serr);
    for (i = 0; i < 6; i++) emit_value(out, xx[i]);
    fputc('\n', out);
}

/* grid-files.tsv only: star is fields[5], iflag always carries SEFLG_SWIEPH already OR-ed in by
 * gen-grid-files.ps1. swe_fixstar/swe_fixstar2 and their _ut variants can rewrite the star buffer
 * in place with the star's canonical name -- STAR_BUF_LEN gives that write plenty of room, and
 * this driver does not read the buffer back afterward, matching Tools/OracleDump/Program.cs (see
 * its own comment on the same point). */
static void process_fixstar(FILE *out, const char *case_id, const char *func, char *fields[], int sid_mode_idx)
{
    char star[STAR_BUF_LEN];
    double tjd = parse_double(fields[3], case_id, "tjd");
    int32 iflag = (int32)parse_int(fields[4], case_id, "iflag");
    double xx[6] = { 0 };
    char serr[AS_MAXCH];
    int retc, i;

    refuse_if_sid_mode_set(case_id, func, fields, sid_mode_idx);

    strncpy(star, fields[5], sizeof star - 1);
    star[sizeof star - 1] = '\0';
    serr[0] = '\0';

    if (strcmp(func, "FIXSTAR") == 0)
        retc = swe_fixstar(star, tjd, iflag, xx, serr);
    else if (strcmp(func, "FIXSTAR_UT") == 0)
        retc = swe_fixstar_ut(star, tjd, iflag, xx, serr);
    else if (strcmp(func, "FIXSTAR2") == 0)
        retc = swe_fixstar2(star, tjd, iflag, xx, serr);
    else
        retc = swe_fixstar2_ut(star, tjd, iflag, xx, serr);

    fprintf(out, "%s\t%d\t", case_id, retc);
    emit_escaped(out, serr);
    for (i = 0; i < 6; i++) emit_value(out, xx[i]);
    fputc('\n', out);
}

/* grid-files.tsv only: swe_fixstar_mag and swe_fixstar2_mag both take no date or flag, only the
 * star search string -- share this one function the same way process_fixstar shares FIXSTAR/
 * FIXSTAR_UT/FIXSTAR2/FIXSTAR2_UT. swe_fixstar2_mag needs no 2.08 version guard (unlike
 * swe_houses_ex2/swe_houses_armc_ex2 below): it is declared and implemented in
 * external/pyswisseph-2.08/swephexp.h:708 -- see this file's own top-of-file comment. */
static void process_fixstar_mag(FILE *out, const char *case_id, const char *func, char *fields[])
{
    char star[STAR_BUF_LEN];
    double mag = 0;
    char serr[AS_MAXCH];
    int retc;

    strncpy(star, fields[5], sizeof star - 1);
    star[sizeof star - 1] = '\0';
    serr[0] = '\0';

    if (strcmp(func, "FIXSTAR_MAG") == 0)
        retc = swe_fixstar_mag(star, &mag, serr);
    else
        retc = swe_fixstar2_mag(star, &mag, serr);

    fprintf(out, "%s\t%d\t", case_id, retc);
    emit_escaped(out, serr);
    emit_value(out, mag);
    fputc('\n', out);
}

/* grid-files.tsv only: swe_get_planet_name returns a string, not a double -- see this file's own
 * top-of-file comment for why that string is written into the err column instead of a value
 * column, and gen-grid-files.ps1's header for the fuller rationale. retc is a fixed 0; the C API
 * has no error code to report here (swe_get_planet_name returns char *, never NULL). */
static void process_name(FILE *out, const char *case_id, char *fields[])
{
    int ipl = (int)parse_int(fields[2], case_id, "ipl");
    char name[STAR_BUF_LEN];

    name[0] = '\0';
    swe_get_planet_name(ipl, name);

    fprintf(out, "%s\t%d\t", case_id, 0);
    emit_escaped(out, name);
    fputc('\n', out);
}

static void process_houses(FILE *out, const char *case_id, char *fields[], int sid_mode_idx)
{
    double tjd, geolon, geolat;
    int hsys, retc, i;
    double cusp[40] = { 0 };
    double ascmc[10] = { 0 };

    refuse_if_sid_mode_set(case_id, "HOUSES", fields, sid_mode_idx);

    tjd = parse_double(fields[3], case_id, "tjd");
    hsys = parse_hsys(fields[5], case_id);
    geolon = parse_double(fields[6], case_id, "geolon");
    geolat = parse_double(fields[7], case_id, "geolat");

    retc = swe_houses(tjd, geolat, geolon, hsys, cusp, ascmc);

    fprintf(out, "%s\t%d\t", case_id, retc); /* no serr param on swe_houses */
    for (i = 0; i < CUSP_COUNT; i++) emit_value(out, cusp[i]);
    for (i = 0; i < ASCMC_COUNT; i++) emit_value(out, ascmc[i]);
    fputc('\n', out);
}

static void process_houses_armc(FILE *out, const char *case_id, char *fields[], int sid_mode_idx)
{
    double armc, eps, geolat;
    int hsys, retc, i;
    double cusp[40] = { 0 };
    double ascmc[10] = { 0 };

    refuse_if_sid_mode_set(case_id, "HOUSES_ARMC", fields, sid_mode_idx);

    armc = parse_double(fields[9], case_id, "armc");
    eps = parse_double(fields[10], case_id, "eps");
    hsys = parse_hsys(fields[5], case_id);
    geolat = parse_double(fields[7], case_id, "geolat");

    retc = swe_houses_armc(armc, geolat, eps, hsys, cusp, ascmc);

    fprintf(out, "%s\t%d\t", case_id, retc); /* no serr param on swe_houses_armc */
    for (i = 0; i < CUSP_COUNT; i++) emit_value(out, cusp[i]);
    for (i = 0; i < ASCMC_COUNT; i++) emit_value(out, ascmc[i]);
    fputc('\n', out);
}

/*
 * SOLCROSS, SOLCROSS_UT, MOONCROSS, MOONCROSS_UT: all four share one C signature shape --
 * double f(double x2cross, double jd, int32 flag, char *serr) -- and one error convention, per
 * Astrodienst's own doc comment on each (external/swisseph/sweph.c:8319, 8353, 8387, 8421):
 * "Errors are indicated by returning a jd < jd_et [or jd_ut]!", not by a separate int return code
 * the way swe_calc/swe_helio_cross use. There is no int32 retc to report at all, so this driver
 * computes one itself -- see this file's own top-of-file comment, "THE CROSSING FUNCTIONS' retc
 * COLUMN". x2cross_idx is the one difference between the two grids (analytic carries armc/eps
 * before sid_mode; files does not), matching process_calc's own sid_mode_idx parameter for the
 * same reason.
 */
static void process_crossing_deg(FILE *out, const char *case_id, const char *func, char *fields[], int sid_mode_idx, int x2cross_idx)
{
#ifdef SWISSEPH_HAS_CROSSING
    double x2cross = parse_double(fields[x2cross_idx], case_id, "x2cross");
    double tjd = parse_double(fields[3], case_id, "tjd");
    int32 iflag = (int32)parse_int(fields[4], case_id, "iflag");
    char serr[AS_MAXCH];
    double result;
    int retc;

    serr[0] = '\0';
    apply_sid_mode(fields, case_id, sid_mode_idx);

    if (strcmp(func, "SOLCROSS") == 0)
        result = swe_solcross(x2cross, tjd, iflag, serr);
    else if (strcmp(func, "SOLCROSS_UT") == 0)
        result = swe_solcross_ut(x2cross, tjd, iflag, serr);
    else if (strcmp(func, "MOONCROSS") == 0)
        result = swe_mooncross(x2cross, tjd, iflag, serr);
    else
        result = swe_mooncross_ut(x2cross, tjd, iflag, serr);

    retc = (result < tjd) ? ERR : OK;

    fprintf(out, "%s\t%d\t", case_id, retc);
    emit_escaped(out, serr);
    emit_value(out, result);
    fputc('\n', out);
#else
    /* swe_solcross/swe_mooncross(_ut) do not exist in 2.08 -- see this file's own top-of-file
     * comment on SWISSEPH_HAS_CROSSING. */
    char not_in_208_msg[AS_MAXCH];
    sprintf(not_in_208_msg, "%s does not exist in Swiss Ephemeris 2.08", func);
    (void)fields; (void)sid_mode_idx; (void)x2cross_idx;
    fprintf(out, "%s\t%d\t", case_id, NOT_IN_208_RETC);
    emit_escaped(out, not_in_208_msg);
    emit_value(out, 0.0);
    fputc('\n', out);
#endif
}

/*
 * MOONCROSS_NODE, MOONCROSS_NODE_UT: same double-return, jd-less-than-input error convention as
 * process_crossing_deg above (external/swisseph/sweph.c:8454, 8491), plus two output parameters
 * (xlon, xla) this driver zero-initializes before the call -- see this file's own top-of-file
 * comment.
 */
static void process_mooncross_node(FILE *out, const char *case_id, const char *func, char *fields[], int sid_mode_idx)
{
#ifdef SWISSEPH_HAS_CROSSING
    double tjd = parse_double(fields[3], case_id, "tjd");
    int32 iflag = (int32)parse_int(fields[4], case_id, "iflag");
    char serr[AS_MAXCH];
    double result, xlon = 0.0, xla = 0.0;
    int retc;

    /* Runs regardless of SWISSEPH_HAS_CROSSING (see the #else branch below for the other half of
     * this same call): a row's sid_mode column is a property of the grid row, not of which C
     * version this translation unit is linked against, so the guard applies the same way whether
     * this branch actually calls swe_mooncross_node(_ut) or the #else branch below takes the "not
     * in 2.08" sentinel path instead. Placed after this branch's own declarations, not before
     * them, to keep every declaration in this function preceding the first statement in its own
     * block -- this file targets a C89-safe subset throughout. */
    refuse_if_sid_mode_set(case_id, func, fields, sid_mode_idx);
    serr[0] = '\0';

    if (strcmp(func, "MOONCROSS_NODE") == 0)
        result = swe_mooncross_node(tjd, iflag, &xlon, &xla, serr);
    else
        result = swe_mooncross_node_ut(tjd, iflag, &xlon, &xla, serr);

    retc = (result < tjd) ? ERR : OK;

    fprintf(out, "%s\t%d\t", case_id, retc);
    emit_escaped(out, serr);
    emit_value(out, result);
    emit_value(out, xlon);
    emit_value(out, xla);
    fputc('\n', out);
#else
    /* swe_mooncross_node(_ut) does not exist in 2.08 -- see this file's own top-of-file comment
     * on SWISSEPH_HAS_CROSSING. The sid_mode guard still runs here (see the #ifdef branch above
     * for why): a 2.08 build takes this sentinel path for every row regardless of sid_mode, but
     * the grid row itself is still expected to carry an empty sid_mode column, the same as a
     * 2.10.03 build would require. */
    char not_in_208_msg[AS_MAXCH];
    refuse_if_sid_mode_set(case_id, func, fields, sid_mode_idx);
    sprintf(not_in_208_msg, "%s does not exist in Swiss Ephemeris 2.08", func);
    (void)fields;
    fprintf(out, "%s\t%d\t", case_id, NOT_IN_208_RETC);
    emit_escaped(out, not_in_208_msg);
    emit_value(out, 0.0);
    emit_value(out, 0.0);
    emit_value(out, 0.0);
    fputc('\n', out);
#endif
}

/*
 * HELIO_CROSS, HELIO_CROSS_UT: the one pair among these eight with a real int32 return code
 * (OK/ERR) and an output parameter (jd_cross) written only on the OK path -- see this file's own
 * top-of-file comment.
 */
static void process_helio_cross(FILE *out, const char *case_id, const char *func, char *fields[], int sid_mode_idx, int x2cross_idx, int dir_idx)
{
#ifdef SWISSEPH_HAS_CROSSING
    int ipl = (int)parse_int(fields[2], case_id, "ipl");
    double x2cross = parse_double(fields[x2cross_idx], case_id, "x2cross");
    double tjd = parse_double(fields[3], case_id, "tjd");
    int32 iflag = (int32)parse_int(fields[4], case_id, "iflag");
    int dir = (int)parse_int(fields[dir_idx], case_id, "dir");
    char serr[AS_MAXCH];
    double jd_cross = 0.0;
    int32 retc;

    /* Runs regardless of SWISSEPH_HAS_CROSSING (see the #else branch below for the other half of
     * this same call) -- see process_mooncross_node's identical comment above for why. Placed
     * after this branch's own declarations to keep every declaration in this function preceding
     * the first statement in its own block. */
    refuse_if_sid_mode_set(case_id, func, fields, sid_mode_idx);
    serr[0] = '\0';

    if (strcmp(func, "HELIO_CROSS") == 0)
        retc = swe_helio_cross(ipl, x2cross, tjd, iflag, dir, &jd_cross, serr);
    else
        retc = swe_helio_cross_ut(ipl, x2cross, tjd, iflag, dir, &jd_cross, serr);

    fprintf(out, "%s\t%d\t", case_id, retc);
    emit_escaped(out, serr);
    emit_value(out, jd_cross);
    fputc('\n', out);
#else
    /* swe_helio_cross(_ut) does not exist in 2.08 -- see this file's own top-of-file comment on
     * SWISSEPH_HAS_CROSSING. The sid_mode guard still runs here -- see process_mooncross_node's
     * identical #else comment above for why. */
    char not_in_208_msg[AS_MAXCH];
    refuse_if_sid_mode_set(case_id, func, fields, sid_mode_idx);
    sprintf(not_in_208_msg, "%s does not exist in Swiss Ephemeris 2.08", func);
    (void)fields; (void)x2cross_idx; (void)dir_idx;
    fprintf(out, "%s\t%d\t", case_id, NOT_IN_208_RETC);
    emit_escaped(out, not_in_208_msg);
    emit_value(out, 0.0);
    fputc('\n', out);
#endif
}

/*
 * AYANAMSA, AYANAMSA_EX, AYANAMSA_EX_UT: direct coverage of swe_get_ayanamsa/_ex/_ex_ut -- see
 * this file's own top-of-file comment. Analytic-grid only (sid_mode_idx is always 11, the
 * analytic grid's own fixed sid_mode column position): none of the three opens an ephemeris data
 * file, so these func tokens never appear in a grid-files.tsv row and this driver never needs to
 * handle them at any other sid_mode_idx.
 *
 * AYANAMSA has no serr output parameter -- swe_get_ayanamsa returns a bare double, with no error
 * signal at all -- so its retc is a fixed OK and its err column stays empty, the same convention
 * process_houses/process_houses_armc already use for a C API with nothing to report there.
 */
static void process_ayanamsa(FILE *out, const char *case_id, char *fields[])
{
    double tjd = parse_double(fields[3], case_id, "tjd");
    double daya;

    apply_sid_mode(fields, case_id, 11);
    daya = swe_get_ayanamsa(tjd);

    fprintf(out, "%s\t%d\t", case_id, OK);
    emit_value(out, daya);
    fputc('\n', out);
}

static void process_ayanamsa_ex(FILE *out, const char *case_id, const char *func, char *fields[])
{
    double tjd = parse_double(fields[3], case_id, "tjd");
    int32 iflag = (int32)parse_int(fields[4], case_id, "iflag");
    char serr[AS_MAXCH];
    double daya = 0.0;
    int32 retc;

    serr[0] = '\0';
    apply_sid_mode(fields, case_id, 11);

    if (strcmp(func, "AYANAMSA_EX") == 0)
        retc = swe_get_ayanamsa_ex(tjd, iflag, &daya, serr);
    else
        retc = swe_get_ayanamsa_ex_ut(tjd, iflag, &daya, serr);

    fprintf(out, "%s\t%d\t", case_id, retc);
    emit_escaped(out, serr);
    emit_value(out, daya);
    fputc('\n', out);
}

/*
 * HOUSES_EX: swe_houses_ex (swehouse.c:178), the sidereal/radians-capable sibling of HOUSES.
 * Shared by both grids -- hsys_idx is the one difference (analytic's hsys sits at fields[5],
 * shared with HOUSES/HOUSES_ARMC; the files grid has no hsys column of its own, so it gets the
 * new trailing one instead), matching process_houses_armc's own sid_mode_idx-style parameter for
 * the same reason.
 */
static void process_houses_ex(FILE *out, const char *case_id, char *fields[], int sid_mode_idx, int hsys_idx)
{
    double tjd = parse_double(fields[3], case_id, "tjd");
    int32 iflag = (int32)parse_int(fields[4], case_id, "iflag");
    int hsys = parse_hsys(fields[hsys_idx], case_id);
    double geolon = parse_double(fields[6], case_id, "geolon");
    double geolat = parse_double(fields[7], case_id, "geolat");
    double cusp[40] = { 0 };
    double ascmc[10] = { 0 };
    int retc, i;

    apply_sid_mode(fields, case_id, sid_mode_idx);

    /* swehouse.c:178 takes geolat before geolon -- opposite of this grid's own geolon-then-geolat
     * column order -- matches process_houses's identical care for plain swe_houses. */
    retc = swe_houses_ex(tjd, iflag, geolat, geolon, hsys, cusp, ascmc);

    fprintf(out, "%s\t%d\t", case_id, retc); /* no serr param on swe_houses_ex */
    for (i = 0; i < CUSP_COUNT; i++) emit_value(out, cusp[i]);
    for (i = 0; i < ASCMC_COUNT; i++) emit_value(out, ascmc[i]);
    fputc('\n', out);
}

/* cusp + ascmc + cusp_speed + ascmc_speed = 94 doubles -> 188 value columns -- see this file's own
 * top-of-file OUTPUT COLUMN LAYOUT comment. */
#define HOUSES_EX2_DOUBLE_COUNT (CUSP_COUNT + ASCMC_COUNT + CUSP_COUNT + ASCMC_COUNT)

/* Emits HOUSES_EX2_DOUBLE_COUNT zero doubles -- the #else (2.08) branch of process_houses_ex2 and
 * process_houses_armc_ex2 below, matching process_crossing_deg's identical #else pattern for the
 * eight crossing functions (see SWISSEPH_HAS_HOUSES_EX2 in this file's own top-of-file comment). */
static void emit_not_in_208_houses_ex2(FILE *out, const char *case_id, const char *func)
{
    char msg[AS_MAXCH];
    int i;
    sprintf(msg, "%s does not exist in Swiss Ephemeris 2.08", func);
    fprintf(out, "%s\t%d\t", case_id, NOT_IN_208_RETC);
    emit_escaped(out, msg);
    for (i = 0; i < HOUSES_EX2_DOUBLE_COUNT; i++) emit_value(out, 0.0);
    fputc('\n', out);
}

/*
 * HOUSES_EX2: swe_houses_ex2 (swehouse.c:207), the 2.10.03 speed-bearing sibling of HOUSES_EX --
 * see this file's own top-of-file comment for the do_speed/do_hspeed gating and the zero-init
 * rule cusp_speed/ascmc_speed both need. Same input columns and hsys_idx-style parameter as
 * process_houses_ex; unlike that function, this one HAS a real serr (see this file's own
 * top-of-file comment on why).
 */
static void process_houses_ex2(FILE *out, const char *case_id, const char *func, char *fields[], int sid_mode_idx, int hsys_idx)
{
#ifdef SWISSEPH_HAS_HOUSES_EX2
    double tjd = parse_double(fields[3], case_id, "tjd");
    int32 iflag = (int32)parse_int(fields[4], case_id, "iflag");
    int hsys = parse_hsys(fields[hsys_idx], case_id);
    double geolon = parse_double(fields[6], case_id, "geolon");
    double geolat = parse_double(fields[7], case_id, "geolat");
    double cusp[40] = { 0 };
    double ascmc[10] = { 0 };
    double cusp_speed[40] = { 0 };
    double ascmc_speed[10] = { 0 };
    char serr[AS_MAXCH];
    int retc, i;

    serr[0] = '\0';
    apply_sid_mode(fields, case_id, sid_mode_idx);

    retc = swe_houses_ex2(tjd, iflag, geolat, geolon, hsys, cusp, ascmc, cusp_speed, ascmc_speed, serr);

    fprintf(out, "%s\t%d\t", case_id, retc);
    emit_escaped(out, serr);
    for (i = 0; i < CUSP_COUNT; i++) emit_value(out, cusp[i]);
    for (i = 0; i < ASCMC_COUNT; i++) emit_value(out, ascmc[i]);
    for (i = 0; i < CUSP_COUNT; i++) emit_value(out, cusp_speed[i]);
    for (i = 0; i < ASCMC_COUNT; i++) emit_value(out, ascmc_speed[i]);
    fputc('\n', out);
#else
    (void)fields; (void)sid_mode_idx; (void)hsys_idx;
    emit_not_in_208_houses_ex2(out, case_id, func);
#endif
}

/*
 * HOUSES_ARMC_EX2: swe_houses_armc_ex2 (swehouse.c:622), the 2.10.03 speed-bearing sibling of
 * HOUSES_ARMC. hsys_idx/armc_idx/eps_idx are the differences between the two grids: analytic's
 * hsys sits at fields[5] (shared with HOUSES/HOUSES_ARMC) and its armc/eps at fields[9]/[10]
 * (shared with HOUSES_ARMC); the files grid has its own hsys at fields[15] (shared with
 * HOUSES_EX, not fields[5] -- that grid's star column) and no armc/eps columns of its own before
 * this addition, so it gets the two new trailing ones instead. Matches process_houses_ex's own
 * hsys_idx-style parameter for the same reason. ascmc is zero-initialized before the call exactly
 * as process_houses_armc's already is, so ascmc[9] is 0.0 (not 99) on every row --
 * swehouse.c:648-660 only ever reads the saved_sundec static when ascmc[9] == 99, so this
 * driver's per-row swe_close() (which does not reset that static -- see this file's own "FRESH
 * LIBRARY STATE PER ROW" section) still never bites, for hsys 'I'/'i' rows from HOUSES_ARMC_EX2
 * exactly as it already does not bite for HOUSES_ARMC's own hsys 'I'/'i' rows.
 */
static void process_houses_armc_ex2(FILE *out, const char *case_id, const char *func, char *fields[], int sid_mode_idx, int hsys_idx, int armc_idx, int eps_idx)
{
#ifdef SWISSEPH_HAS_HOUSES_EX2
    double armc, eps, geolat;
    int hsys, retc, i;
    double cusp[40] = { 0 };
    double ascmc[10] = { 0 };
    double cusp_speed[40] = { 0 };
    double ascmc_speed[10] = { 0 };
    char serr[AS_MAXCH];

    refuse_if_sid_mode_set(case_id, func, fields, sid_mode_idx);

    armc = parse_double(fields[armc_idx], case_id, "armc");
    eps = parse_double(fields[eps_idx], case_id, "eps");
    hsys = parse_hsys(fields[hsys_idx], case_id);
    geolat = parse_double(fields[7], case_id, "geolat");
    serr[0] = '\0';

    retc = swe_houses_armc_ex2(armc, geolat, eps, hsys, cusp, ascmc, cusp_speed, ascmc_speed, serr);

    fprintf(out, "%s\t%d\t", case_id, retc);
    emit_escaped(out, serr);
    for (i = 0; i < CUSP_COUNT; i++) emit_value(out, cusp[i]);
    for (i = 0; i < ASCMC_COUNT; i++) emit_value(out, ascmc[i]);
    for (i = 0; i < CUSP_COUNT; i++) emit_value(out, cusp_speed[i]);
    for (i = 0; i < ASCMC_COUNT; i++) emit_value(out, ascmc_speed[i]);
    fputc('\n', out);
#else
    /* refuse_if_sid_mode_set still runs here -- see process_mooncross_node's identical #else
     * comment for why: a row's sid_mode column is a property of the grid row, not of which C
     * version this translation unit is linked against. */
    refuse_if_sid_mode_set(case_id, func, fields, sid_mode_idx);
    (void)hsys_idx; (void)armc_idx; (void)eps_idx;
    emit_not_in_208_houses_ex2(out, case_id, func);
#endif
}

/* AYANAMSA_UT: swe_get_ayanamsa_ut (sweph.c:3260), the UT sibling of AYANAMSA -- same fixed-OK,
 * empty-err convention as process_ayanamsa, and the same apply_sid_mode call, since the ayanamsa
 * it returns still depends on whichever sid_mode swe_set_sid_mode last configured. Analytic-grid
 * only: opens no ephemeris file, so this func token never appears in a grid-files.tsv row. */
static void process_ayanamsa_ut(FILE *out, const char *case_id, char *fields[])
{
    double tjd = parse_double(fields[3], case_id, "tjd");
    double daya;

    apply_sid_mode(fields, case_id, 11);
    daya = swe_get_ayanamsa_ut(tjd);

    fprintf(out, "%s\t%d\t", case_id, OK);
    emit_value(out, daya);
    fputc('\n', out);
}

/* SIDTIME: swe_sidtime (swephlib.c:3580). A bare double with no serr and no sid_mode dependence
 * of its own (sidereal *time*, not the ayanamsha) -- refuse_if_sid_mode_set guards the latter the
 * same way process_fixstar/process_houses already guard funcs with no sidereal-frame parameter. */
static void process_sidtime(FILE *out, const char *case_id, char *fields[])
{
    double tjd = parse_double(fields[3], case_id, "tjd");
    double tsid;

    refuse_if_sid_mode_set(case_id, "SIDTIME", fields, 11);
    tsid = swe_sidtime(tjd);

    fprintf(out, "%s\t%d\t", case_id, OK);
    emit_value(out, tsid);
    fputc('\n', out);
}

/*
 * AZALT: swe_azalt (swecl.c:2788). Analytic-grid only. geopos is {lon, lat, height}, reusing this
 * grid's existing geolon/geolat/height columns; xin[2] is never a grid column because swe_azalt's
 * own body only ever reads xin[0]/xin[1] (`for (i = 0; i < 2; i++) xra[i] = xin[i]; xra[2] = 1;`,
 * swecl.c:2801-2803) -- a column nothing reads is exactly the dead-input trap this repo has
 * already been burned by, so this driver does not add one. atpress == 0 takes the pressure-
 * estimate branch (swecl.c:2819-2822); this grid deliberately carries rows with atpress = 0 and a
 * non-zero height so that branch is exercised, not just asserted.
 */
static void process_azalt(FILE *out, const char *case_id, char *fields[])
{
    double tjd = parse_double(fields[3], case_id, "tjd");
    double geopos[3];
    int32 calc_flag;
    double atpress, attemp;
    double xin[2];
    double xaz[3] = { 0 };

    refuse_if_sid_mode_set(case_id, "AZALT", fields, 11);

    geopos[0] = parse_double(fields[6], case_id, "geolon");
    geopos[1] = parse_double(fields[7], case_id, "geolat");
    geopos[2] = parse_double(fields[8], case_id, "height");
    calc_flag = (int32)parse_int(fields[17], case_id, "calc_flag");
    atpress = parse_double(fields[18], case_id, "atpress");
    attemp = parse_double(fields[19], case_id, "attemp");
    xin[0] = parse_double(fields[20], case_id, "xin0");
    xin[1] = parse_double(fields[21], case_id, "xin1");

    /* swe_azalt reads const_lapse_rate, a swecl.c static settable only through
     * swe_set_lapse_rate (swecl.c:2988) -- neither driver ever calls that, and both reset all
     * other library state before every row (swe_close() here, a fresh SwissEph there), so both
     * sides see SE_LAPSE_RATE, the compiled-in default, on every single row. */
    swe_azalt(tjd, calc_flag, geopos, atpress, attemp, xin, xaz);

    fprintf(out, "%s\t%d\t", case_id, OK); /* swe_azalt returns void -- no retc, no serr */
    emit_value(out, xaz[0]);
    emit_value(out, xaz[1]);
    emit_value(out, xaz[2]);
    fputc('\n', out);
}

/* HOUSE_NAME: swe_house_name (swehouse.c:827). Analytic-grid only; a pure lookup, so it opens no
 * ephemeris file either way. Returns const char *, never NULL -- same "write the string into the
 * err column, fixed retc 0" convention as process_name (GET_PLANET_NAME). */
static void process_house_name(FILE *out, const char *case_id, char *fields[])
{
    int hsys = parse_hsys(fields[5], case_id);
    const char *name = swe_house_name(hsys);

    fprintf(out, "%s\t%d\t", case_id, 0);
    emit_escaped(out, name);
    fputc('\n', out);
}

/*
 * NOD_APS_UT: swe_nod_aps_ut (swecl.c:5645), which adds swe_deltat_ex to tjd_ut and delegates to
 * swe_nod_aps (swecl.c:5064). Real int32 retc and serr. Shared by both grids -- method_idx is the
 * one difference (analytic carries the new method column after t0/ayan_t0; files carries it
 * right before its own trailing hsys column), matching process_houses_ex's own hsys_idx-style
 * parameter for the same reason. No sidereal-frame parameter in Astrodienst's API, so this func
 * gets the same refuse_if_sid_mode_set guard process_mooncross_node/process_helio_cross already
 * use for the same reason.
 */
static void process_nod_aps_ut(FILE *out, const char *case_id, char *fields[], int sid_mode_idx, int method_idx)
{
    int ipl = (int)parse_int(fields[2], case_id, "ipl");
    double tjd = parse_double(fields[3], case_id, "tjd");
    int32 iflag = (int32)parse_int(fields[4], case_id, "iflag");
    int32 method = (int32)parse_int(fields[method_idx], case_id, "method");
    double xnasc[6] = { 0 }, xndsc[6] = { 0 }, xperi[6] = { 0 }, xaphe[6] = { 0 };
    char serr[AS_MAXCH];
    int32 retc;
    int i;

    refuse_if_sid_mode_set(case_id, "NOD_APS_UT", fields, sid_mode_idx);
    serr[0] = '\0';

    /* Zero-initialized above regardless of outcome: swe_nod_aps only zeroes xnasc/xndsc/xperi/
     * xaphe itself on its "nodes/apsides ... are not implemented" reject branch
     * (swecl.c:5134-5146); every other ERR return (e.g. an inner swe_calc failure) leaves them
     * untouched. Matches process_helio_cross's identical rule for jd_cross, for the identical
     * reason -- an ERR row's value columns must be a deterministic 0.0 on both sides, not
     * whatever happened to be left on each side's stack. */
    retc = swe_nod_aps_ut(tjd, ipl, iflag, method, xnasc, xndsc, xperi, xaphe, serr);

    fprintf(out, "%s\t%d\t", case_id, retc);
    emit_escaped(out, serr);
    for (i = 0; i < 6; i++) emit_value(out, xnasc[i]);
    for (i = 0; i < 6; i++) emit_value(out, xndsc[i]);
    for (i = 0; i < 6; i++) emit_value(out, xperi[i]);
    for (i = 0; i < 6; i++) emit_value(out, xaphe[i]);
    fputc('\n', out);
}

/*
 * PCTR: swe_calc_pctr (sweph.c:8042), planetocentric coordinates -- new in 2.10.03, guarded
 * behind SWISSEPH_HAS_CALC_PCTR (see this file's own top-of-file comment). grid-files.tsv only:
 * iflag2 forces SEFLG_BARYCTR unconditionally (sweph.c:8061) regardless of what the caller's own
 * iflag requests, and SEFLG_BARYCTR|SEFLG_MOSEPH is rejected outright, before any geometry runs
 * (sweph.c:634-638, "barycentric Moshier positions are not supported"). grid-analytic.tsv OR-s
 * SEFLG_MOSEPH into every row it carries and never configures an ephemeris path, so every PCTR row
 * there would hit that reject and nothing else -- the identical SE_CHIRON category error
 * gen-grid-analytic.ps1's own $HelioCrossValidIpl comment already documents and gen-grid-files.ps1
 * moved SE_CHIRON out of grid-analytic.tsv to avoid a second time. ipl/tjd/iflag are the same
 * columns CALC already reads (fields[2]/fields[3]/fields[4]); iplctr_idx is this addition's own
 * new, additive-tail column. xxret is zero-initialized before the call: the two inner swe_calc
 * calls (sweph.c:8063,8066) both `return ERR` without touching xxret on failure, the same
 * zero-init rule process_helio_cross already applies to jd_cross. sid_mode_idx applies
 * swe_set_sid_mode the same way process_calc does, since a PCTR row can carry SEFLG_SIDEREAL in
 * its iflag exactly as a CALC row can.
 */
static void process_pctr(FILE *out, const char *case_id, char *fields[], int sid_mode_idx, int iplctr_idx)
{
#ifdef SWISSEPH_HAS_CALC_PCTR
    int ipl = (int)parse_int(fields[2], case_id, "ipl");
    int iplctr = (int)parse_int(fields[iplctr_idx], case_id, "iplctr");
    double tjd = parse_double(fields[3], case_id, "tjd");
    int32 iflag = (int32)parse_int(fields[4], case_id, "iflag");
    double xxret[6] = { 0 };
    char serr[AS_MAXCH];
    int32 retc;
    int i;

    serr[0] = '\0';
    apply_sid_mode(fields, case_id, sid_mode_idx);

    retc = swe_calc_pctr(tjd, ipl, iplctr, iflag, xxret, serr);

    fprintf(out, "%s\t%d\t", case_id, retc);
    emit_escaped(out, serr);
    for (i = 0; i < 6; i++) emit_value(out, xxret[i]);
    fputc('\n', out);
#else
    /* swe_calc_pctr does not exist in 2.08 -- see this file's own top-of-file comment on
     * SWISSEPH_HAS_CALC_PCTR. */
    char not_in_208_msg[AS_MAXCH];
    int i;
    sprintf(not_in_208_msg, "swe_calc_pctr does not exist in Swiss Ephemeris 2.08");
    (void)fields; (void)sid_mode_idx; (void)iplctr_idx;
    fprintf(out, "%s\t%d\t", case_id, NOT_IN_208_RETC);
    emit_escaped(out, not_in_208_msg);
    for (i = 0; i < 6; i++) emit_value(out, 0.0);
    fputc('\n', out);
#endif
}

/*
 * GET_CURRENT_FILE_DATA: swe_get_current_file_data (sweph.c:8297-8306) -- new in 2.10.03, guarded
 * behind SWISSEPH_HAS_GET_CURRENT_FILE_DATA (see this file's own top-of-file comment). Returns
 * const char *, never empty on success -- same "the string goes in the err column, retc is
 * synthesized" convention process_name/process_house_name already use for a C function with
 * nothing else to report there. Synthesized retc is OK when the returned pointer is non-NULL, ERR
 * when it is NULL (sweph.c:8299,8301's two reject branches: ifno outside [0,4], or
 * swed.fidat[ifno].fnam empty). tfstart/tfend/denum are only WRITTEN on the non-NULL path
 * (sweph.c:8302-8304); zero-initialized here so an ERR row's three value columns are a
 * deterministic 0.0 on both sides rather than stack garbage, the same zero-init rule
 * process_helio_cross already applies to jd_cross. denum is widened to double for emit_value --
 * exact for any int32.
 *
 * ifno alone tests the boundary/no-data branches. Whether a slot is already POPULATED before this
 * row runs is a property of what ran earlier in THIS row, not of ifno by itself: main()'s own
 * swe_set_ephe_path() call before every grid-files.tsv row (sweph.c:1315-1350) already opens the
 * lunar ephemeris to pin tidal acceleration, so ifno 1 (SEI_FILE_MOON, sweph.h:174) reports real
 * data with no other input on this row at all. ipl_idx/tjd_idx/iflag_idx (when both ipl and tjd
 * are non-empty) trigger a preceding swe_calc first -- the same three columns CALC already reads
 * -- so this row can also observe ifno 0 (SEI_FILE_PLANET) or ifno 2 (SEI_FILE_MAIN_AST) with real
 * data; star_idx (when both star and tjd are non-empty, and ipl is empty) triggers a preceding
 * swe_fixstar2 instead, for ifno 4 (SEI_FILE_FIXSTAR) -- both preceding calls' own retc/serr/xx
 * are discarded here; only what they leave in swed.fidat matters to this row. ifno 3
 * (SEI_FILE_ANY_AST -- an individually-numbered asteroid or planetary-moon file) is not reachable
 * with real data by any row in this grid: this repo's ephemeris checkout ships no such file
 * (external/swisseph/ephe has sepl/semo/seas_{12,18}.se1 and sefstars.txt only), so ifno 3 rows
 * here only ever exercise the empty-fnam reject branch, the same branch ifno 0/2/4 rows exercise
 * before any preceding call populates them.
 */
static void process_get_current_file_data(FILE *out, const char *case_id, char *fields[], int ifno_idx, int ipl_idx, int tjd_idx, int iflag_idx, int star_idx)
{
#ifdef SWISSEPH_HAS_GET_CURRENT_FILE_DATA
    int ifno = (int)parse_int(fields[ifno_idx], case_id, "ifno");
    double tfstart = 0.0, tfend = 0.0;
    int denum = 0;
    const char *fnam;
    int retc;

    if (has_value(fields[ipl_idx]) && has_value(fields[tjd_idx])) {
        int pre_ipl = (int)parse_int(fields[ipl_idx], case_id, "ipl");
        double pre_tjd = parse_double(fields[tjd_idx], case_id, "tjd");
        int32 pre_iflag = has_value(fields[iflag_idx]) ? (int32)parse_int(fields[iflag_idx], case_id, "iflag") : 0;
        double pre_xx[6];
        char pre_serr[AS_MAXCH];
        pre_serr[0] = '\0';
        /* discarded -- only the file-open side effect on swed.fidat matters to this row */
        (void)swe_calc(pre_tjd, pre_ipl, pre_iflag, pre_xx, pre_serr);
    } else if (has_value(fields[star_idx]) && has_value(fields[tjd_idx])) {
        char pre_star[STAR_BUF_LEN];
        double pre_tjd = parse_double(fields[tjd_idx], case_id, "tjd");
        int32 pre_iflag = has_value(fields[iflag_idx]) ? (int32)parse_int(fields[iflag_idx], case_id, "iflag") : 0;
        double pre_xx[6];
        char pre_serr[AS_MAXCH];
        strncpy(pre_star, fields[star_idx], sizeof pre_star - 1);
        pre_star[sizeof pre_star - 1] = '\0';
        pre_serr[0] = '\0';
        /* discarded -- see above */
        (void)swe_fixstar2(pre_star, pre_tjd, pre_iflag, pre_xx, pre_serr);
    }

    fnam = swe_get_current_file_data(ifno, &tfstart, &tfend, &denum);
    retc = (fnam != NULL) ? OK : ERR;

    fprintf(out, "%s\t%d\t", case_id, retc);
    emit_escaped(out, fnam != NULL ? fnam : "");
    emit_value(out, tfstart);
    emit_value(out, tfend);
    emit_value(out, (double)denum);
    fputc('\n', out);
#else
    /* swe_get_current_file_data does not exist in 2.08 -- see this file's own top-of-file comment
     * on SWISSEPH_HAS_GET_CURRENT_FILE_DATA. */
    char not_in_208_msg[AS_MAXCH];
    sprintf(not_in_208_msg, "swe_get_current_file_data does not exist in Swiss Ephemeris 2.08");
    (void)fields; (void)ifno_idx; (void)ipl_idx; (void)tjd_idx; (void)iflag_idx; (void)star_idx;
    fprintf(out, "%s\t%d\t", case_id, NOT_IN_208_RETC);
    emit_escaped(out, not_in_208_msg);
    emit_value(out, 0.0);
    emit_value(out, 0.0);
    emit_value(out, 0.0);
    fputc('\n', out);
#endif
}

int main(int argc, char **argv)
{
    FILE *in, *out;
    const char *ephe_dir = NULL;
    const char *jpl_file = NULL;
    char line[MAX_LINE];
    char buf[MAX_LINE];
    int header_seen = 0;
    enum grid_mode mode = MODE_ANALYTIC;
    int expected_columns = ANALYTIC_COLUMNS;
    long row_count = 0;

    /* Clears THIS process's own SE_EPHE_PATH before anything else runs, including before argument
     * parsing needs it -- see CLEAR_INHERITED_SE_EPHE_PATH in the header comment above for the
     * measurement (2,223 of 3,251 grid-files.tsv rows) that makes this more than defensive. Either
     * call below leaves a later getenv("SE_EPHE_PATH") in this same process returning NULL:
     * _putenv_s(name, "") -- an empty value string -- is documented by Microsoft to remove the
     * variable from the environment, the same outcome unsetenv gives on POSIX, so this is not
     * relying on sweph.c:1327-1330's own strlen(sp) != 0 half of its guard to treat an empty-but-
     * present value as absent; the variable is actually gone from this process's environment
     * block either way. _putenv_s is MSVC, the only compiler this repo's own build scripts use on
     * Windows; unsetenv is POSIX, for the gcc/clang builds this same source file is also compiled
     * with -- see this file's own top-of-file comment on why one source serves both. Neither call
     * touches the parent shell's or CI runner's environment, only this process's own copy of it. */
#ifdef _WIN32
    _putenv_s("SE_EPHE_PATH", "");
#else
    unsetenv("SE_EPHE_PATH");
#endif

    if (argc < 3 || argc > 5) {
        fprintf(stderr, "Usage: sedump <grid.tsv> <output.tsv> [ephe-dir [jpl-file]]\n");
        return 1;
    }
    if (argc >= 4) ephe_dir = argv[3];
    if (argc == 5) jpl_file = argv[4];
    /* swe_set_jpl_file resolves its argument against swed.ephepath (sweph.c:1500), so a jpl-file
     * with no ephe-dir would resolve against whatever SE_EPHE_PATH or the compiled-in default
     * happens to be -- almost certainly not finding the file, and then silently falling back
     * through SEFLG_SWIEPH to Moshier on every row. Rejected here rather than left to produce a
     * run that looks fine and measures nothing. The argc parsing above cannot express it anyway
     * (argv[4] implies argv[3]); this guard is for an explicitly empty ephe-dir. */
    if (jpl_file != NULL && (ephe_dir == NULL || ephe_dir[0] == '\0')) {
        fprintf(stderr, "sedump: jpl-file was given but ephe-dir is empty; swe_set_jpl_file resolves against the ephemeris path, so both are required together.\n");
        return 1;
    }

    in = fopen(argv[1], "rb");
    if (!in) die("cannot open grid file %s", argv[1]);
    out = fopen(argv[2], "wb");
    if (!out) die("cannot open output file %s", argv[2]);

    while (fgets(line, sizeof line, in)) {
        char *fields[FILES_COLUMNS > ANALYTIC_COLUMNS ? FILES_COLUMNS : ANALYTIC_COLUMNS];
        int n;
        const char *case_id, *func;

        rtrim(line);
        if (line[0] == '\0') continue;
        if (line[0] == '#') continue;

        if (!header_seen) {
            if (strcmp(line, EXPECTED_HEADER_ANALYTIC) == 0) {
                mode = MODE_ANALYTIC;
                expected_columns = ANALYTIC_COLUMNS;
            } else if (strcmp(line, EXPECTED_HEADER_FILES) == 0) {
                mode = MODE_FILES;
                expected_columns = FILES_COLUMNS;
            } else {
                die("grid header does not match either header this driver expects.\n"
                    "analytic: %s\nfiles:    %s\ngot:      %s",
                    EXPECTED_HEADER_ANALYTIC, EXPECTED_HEADER_FILES, line);
            }
            header_seen = 1;
            continue;
        }

        strcpy(buf, line);
        n = split_fields(buf, fields, expected_columns);
        if (n != expected_columns) {
            die("row has %d column(s), expected %d: %s", n, expected_columns, line);
        }

        case_id = fields[0];
        func = fields[1];

        swe_close(); /* fresh library state before every row -- see header comment */
        /* Unconditional, not `if (ephe_dir != NULL)`: SENTINEL_EPHE_DIR when the grid gave no
         * ephe-dir argument (today: only grid-analytic.tsv's two-argument invocation), the real
         * ephe-dir otherwise -- see SENTINEL_EPHE_DIR's own comment and THE SENTINEL EPHEMERIS
         * PATH in this file's own top-of-file comment for why leaving this call unmade at all was
         * the actual defect: it let the first non-MOSEPH row's lazy internal
         * swe_set_ephe_path(NULL) (sweph.c:639-640) decide swed.ephepath for the rest of the
         * process, from either SE_EPHE_PATH or the compiled-in default, depending on iteration
         * order and the machine this ran on. */
        swe_set_ephe_path(ephe_dir != NULL ? ephe_dir : SENTINEL_EPHE_DIR);
        /* Strictly after swe_set_ephe_path, never before it -- see INVOCATION in the header
         * comment for what swapping the two would silently turn every SEFLG_JPLEPH row into. */
        if (jpl_file != NULL) swe_set_jpl_file(jpl_file);

        if (mode == MODE_ANALYTIC) {
            if (strcmp(func, "CALC") == 0 || strcmp(func, "CALC_UT") == 0) {
                process_calc(out, case_id, func, fields, 11);
            } else if (strcmp(func, "HOUSES") == 0) {
                process_houses(out, case_id, fields, 11);
            } else if (strcmp(func, "HOUSES_ARMC") == 0) {
                process_houses_armc(out, case_id, fields, 11);
            } else if (strcmp(func, "SOLCROSS") == 0 || strcmp(func, "SOLCROSS_UT") == 0
                       || strcmp(func, "MOONCROSS") == 0 || strcmp(func, "MOONCROSS_UT") == 0) {
                process_crossing_deg(out, case_id, func, fields, 11, 12);
            } else if (strcmp(func, "MOONCROSS_NODE") == 0 || strcmp(func, "MOONCROSS_NODE_UT") == 0) {
                process_mooncross_node(out, case_id, func, fields, 11);
            } else if (strcmp(func, "HELIO_CROSS") == 0 || strcmp(func, "HELIO_CROSS_UT") == 0) {
                process_helio_cross(out, case_id, func, fields, 11, 12, 13);
            } else if (strcmp(func, "AYANAMSA") == 0) {
                process_ayanamsa(out, case_id, fields);
            } else if (strcmp(func, "AYANAMSA_EX") == 0 || strcmp(func, "AYANAMSA_EX_UT") == 0) {
                process_ayanamsa_ex(out, case_id, func, fields);
            } else if (strcmp(func, "HOUSES_EX") == 0) {
                process_houses_ex(out, case_id, fields, 11, 5);
            } else if (strcmp(func, "HOUSES_EX2") == 0) {
                process_houses_ex2(out, case_id, func, fields, 11, 5);
            } else if (strcmp(func, "HOUSES_ARMC_EX2") == 0) {
                process_houses_armc_ex2(out, case_id, func, fields, 11, 5, 9, 10);
            } else if (strcmp(func, "AYANAMSA_UT") == 0) {
                process_ayanamsa_ut(out, case_id, fields);
            } else if (strcmp(func, "SIDTIME") == 0) {
                process_sidtime(out, case_id, fields);
            } else if (strcmp(func, "AZALT") == 0) {
                process_azalt(out, case_id, fields);
            } else if (strcmp(func, "HOUSE_NAME") == 0) {
                process_house_name(out, case_id, fields);
            } else if (strcmp(func, "NOD_APS_UT") == 0) {
                process_nod_aps_ut(out, case_id, fields, 11, 16);
            } else {
                die("unknown func '%s' at case %s", func, case_id);
            }
        } else {
            if (strcmp(func, "CALC") == 0 || strcmp(func, "CALC_UT") == 0) {
                process_calc(out, case_id, func, fields, 9);
            } else if (strcmp(func, "FIXSTAR") == 0 || strcmp(func, "FIXSTAR_UT") == 0
                       || strcmp(func, "FIXSTAR2") == 0 || strcmp(func, "FIXSTAR2_UT") == 0) {
                process_fixstar(out, case_id, func, fields, 9);
            } else if (strcmp(func, "FIXSTAR_MAG") == 0 || strcmp(func, "FIXSTAR2_MAG") == 0) {
                process_fixstar_mag(out, case_id, func, fields);
            } else if (strcmp(func, "GET_PLANET_NAME") == 0) {
                process_name(out, case_id, fields);
            } else if (strcmp(func, "SOLCROSS") == 0 || strcmp(func, "SOLCROSS_UT") == 0
                       || strcmp(func, "MOONCROSS") == 0 || strcmp(func, "MOONCROSS_UT") == 0) {
                process_crossing_deg(out, case_id, func, fields, 9, 10);
            } else if (strcmp(func, "MOONCROSS_NODE") == 0 || strcmp(func, "MOONCROSS_NODE_UT") == 0) {
                process_mooncross_node(out, case_id, func, fields, 9);
            } else if (strcmp(func, "HELIO_CROSS") == 0 || strcmp(func, "HELIO_CROSS_UT") == 0) {
                process_helio_cross(out, case_id, func, fields, 9, 10, 11);
            } else if (strcmp(func, "HOUSES_EX") == 0) {
                process_houses_ex(out, case_id, fields, 9, 15);
            } else if (strcmp(func, "HOUSES_EX2") == 0) {
                process_houses_ex2(out, case_id, func, fields, 9, 15);
            } else if (strcmp(func, "HOUSES_ARMC_EX2") == 0) {
                process_houses_armc_ex2(out, case_id, func, fields, 9, 15, 16, 17);
            } else if (strcmp(func, "NOD_APS_UT") == 0) {
                process_nod_aps_ut(out, case_id, fields, 9, 14);
            } else if (strcmp(func, "PCTR") == 0) {
                process_pctr(out, case_id, fields, 9, 18);
            } else if (strcmp(func, "GET_CURRENT_FILE_DATA") == 0) {
                process_get_current_file_data(out, case_id, fields, 19, 2, 3, 4, 5);
            } else {
                die("unknown func '%s' at case %s", func, case_id);
            }
        }

        row_count++;
    }

    if (!header_seen) die("grid file %s had no header row", argv[1]);
    if (row_count == 0) die("grid file %s produced zero rows -- a run that processed nothing is not a pass", argv[1]);

    swe_close();
    fclose(in);
    fclose(out);
    fprintf(stderr, "sedump: wrote %ld row(s) to %s\n", row_count, argv[2]);
    return 0;
}
