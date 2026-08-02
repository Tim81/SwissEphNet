# Compliance record: Swiss Ephemeris 2.10.03

This records what has actually been measured about this port's agreement with Astrodienst's
own Swiss Ephemeris C library, version 2.10.03 (upstream tag `v2.10.3final`), rather than
asserting it. Every number below comes from a gate in this repository or from a measurement run
during the version-bump work that closed the 2.10.03 port (see `docs/known-issues.md`'s
`SE_VERSION` entry). Where something has not been measured, that is stated rather than estimated.

## Four instruments, four different claims

This repository runs four separate checks against the C library, and they prove four different
things. Confusing them is easy, because each one reports PASS/FAIL against a reference, so a
one-line summary of "the tests pass" would hide which claim is actually being made.

- **The characterization baseline** (`Tests/baseline/`, `scripts/verify-baseline.ps1`) proves
  self-consistency: a change did not alter anything it was not supposed to. It compares the
  port's current output against its own frozen prior output, so it can never prove correctness --
  a bug present when the baseline was recorded stays invisible to it forever.
- **The correctness oracle** (`Tests/SwissEphNet.Conformance.Tests`, upstream `setest/t.exp`)
  proves agreement with Astrodienst's own published reference values, within the tolerances
  Astrodienst itself ships (`setest/t.fix`).
- **The bit-exact oracle** (`Tools/OracleGrid`, `Tools/CReference/sedump.c` and `Tools/OracleDump`
  as the paired drivers, `Tools/OracleVerify` as the comparer, `scripts/verify-oracle.ps1` as the
  gate) proves the strongest and narrowest of the four claims: for every input in its two grids,
  the port and Astrodienst's own C, compiled from the same upstream source and run against the
  same ephemeris files, compute the identical bits. Not "close within tolerance" -- identical.
- **The SweTest text-output comparison** (`Tools/SwetestDiff/args-grid.tsv`,
  `scripts/verify-swetest-diff.ps1`) proves agreement on what a user actually sees: the printed
  output of `Programs/SweTest` against Astrodienst's own compiled `swetest.exe`, argument string
  for argument string, not just the numeric values the other three instruments compare
  underneath. It is the one instrument below that is not currently clean; see "4. SweTest
  text-output comparison".

None of the four substitutes for any of the others. A porter who only reads the bit-exact
table below and skips the correctness oracle's known-fail count would conclude the port is at
full parity with 2.10.03; it is not, and the next section says by how much.

## 1. Bit-exact oracle

| Platform | C reference | Result |
|---|---|---|
| Windows x64 | MSVC 19.51.36248, `/O2 /fp:precise /MD` | 25,540 of 25,540 oracle rows bit-identical (gated) |
| Linux x64, Ubuntu 24.04.4 | gcc 13.3.0, `-O2` | 25,540 of 25,540 oracle rows bit-identical (gated; also confirmed by a full local replay against this grid: 22,289 analytic rows, sha256 `41f84577d86e296f86ba06444002977c`, and 3,251 files rows, sha256 `900308e9acd05d48dd846cd778f7a90a`, both bit-identical) |
| macOS arm64 (Apple libSystem) | clang, `-O2 -ffp-contract=off -fno-builtin` | 20,532 of 20,532 oracle rows bit-identical at last CI run (gated by `macos-exactness`; not re-run locally against the current 25,540-row grid -- macOS has no local reproduction path here, so this row is CI's own last result, not a claim made outside it) |

**25,540 was measured directly on Windows and Linux** (Windows: this record's own oracle numbers, `scripts/run-oracle-dump.ps1` + `scripts/verify-oracle.ps1`, both grids' known-diff lists empty and both dump files SHA-256 identical to Astrodienst's own C: `dump-c-2.10.03.tsv`/`dump-net.tsv` at `4ac1a3c0…c7640`, `dump-c-2.10.03-files.tsv`/`dump-net-files.tsv` at `aef136bd…d72f2d0`; the JPL grid, opt-in and not part of either total above, also ran clean at 2,400 of 2,400 rows, `dump-c-2.10.03-jpl.tsv`/`dump-net-jpl.tsv` at `bc0ca597…d7067724`. Linux: gcc, a full local replay of both grids against the port, bit-identical at the sha256 pair in the table row above). macOS is re-verified by its own CI job (`macos-exactness`) against the identical committed grids and drivers on the next push or pull request, not re-run from this workstation or reproducible outside CI here; its prior run at the smaller row count (20,532) was bit-identical, and this addition (5,008 rows: 4,500 across `HOUSES_EX2`/`HOUSES_ARMC_EX2` in both grids plus 8 `FIXSTAR2_MAG` rows) has not yet been measured on that platform. Unlike the ayanamsa addition that grew the grid from 17,064 to 18,064, this addition is not a re-sweep of code paths this grid already reached: `swe_houses_ex2`/`swe_houses_armc_ex2` are new in 2.10.03 and `swe_fixstar2_mag` had no row anywhere before this addition, so Windows and Linux are the only platforms this specific claim has actually been checked on until macOS CI next runs.

Windows is gated by `oracle-dump`, the `.github/workflows/oracle.yml` job that replays this grid
end to end on every push and pull request; Linux is gated the same way by `linux-exactness`, and
macOS by `macos-exactness`. The Linux row was originally a single hand-run of the grid in a WSL2
Docker container, with nothing re-running it automatically; `linux-exactness` replaced that with a
CI job that rebuilds the C reference with gcc and replays both grids on every push and pull
request, the same way `oracle-dump` and `macos-exactness` already did for their platforms -- see
`README.md`'s "Numerical compatibility" section for what the other Windows jobs in that workflow
(`crt-parity`, `c-reference-validate`, `swetest-diff`) check instead, since none of them replay
this grid.

25,540<!--doccount:grid-total-combined--> is the sum of the two grids, and neither is a single homogeneous function. Recounted by
`func` column rather than trusted from an earlier draft of this document:
`Tools/OracleGrid/grid-analytic.tsv` is 22,289<!--doccount:grid-analytic-total--> rows -- 6,600<!--doccount:grid-analytic-func-houses-armc--> `HOUSES_ARMC`, 3,300<!--doccount:grid-analytic-func-houses--> `HOUSES`, 2,160<!--doccount:grid-analytic-func-calc-->
`CALC`, 2,160<!--doccount:grid-analytic-func-calc-ut--> `CALC_UT` (all `SEFLG_MOSEPH`, opening no ephemeris file), 600<!--doccount:grid-analytic-crossing-total--> crossing rows
(`HELIO_CROSS`/`HELIO_CROSS_UT` 192<!--doccount:grid-analytic-func-helio-cross--><!--doccount:grid-analytic-func-helio-cross-ut--> each, `SOLCROSS`/`SOLCROSS_UT` 48<!--doccount:grid-analytic-func-solcross--><!--doccount:grid-analytic-func-solcross-ut--> each,
`MOONCROSS`/`MOONCROSS_UT` 48<!--doccount:grid-analytic-func-mooncross--><!--doccount:grid-analytic-func-mooncross-ut--> each, `MOONCROSS_NODE`/`MOONCROSS_NODE_UT` 12<!--doccount:grid-analytic-func-mooncross-node--><!--doccount:grid-analytic-func-mooncross-node-ut--> each),
1,000 direct ayanamsa rows -- `AYANAMSA` (plain `swe_get_ayanamsa`) 200<!--doccount:grid-analytic-func-ayanamsa-->,
`AYANAMSA_EX` (`swe_get_ayanamsa_ex`) 400<!--doccount:grid-analytic-func-ayanamsa-ex-->, `AYANAMSA_EX_UT` (`swe_get_ayanamsa_ex_ut`) 400<!--doccount:grid-analytic-func-ayanamsa-ex-ut-->
-- covering every predefined `sid_mode` (0..46) crossed with four Julian days (`AYANAMSA_EX`/`_EX_UT`
also crossed with a plain/`SEFLG_NONUT` iflag pair, both now always OR-ing in `SEFLG_MOSEPH` too --
see "The sentinel ephemeris path and the AYANAMSA_EX/AYANAMSA_EX_UT environment leak" below), plus
`SE_SIDM_USER` (mode 255) with three `t0`/`ayan_t0` pairs, plus 1,969 rows across six more entry
points added in an earlier record (see "Six astrology-program entry points" below) and a further
4,500 rows across `HOUSES_EX2`/`HOUSES_ARMC_EX2` (see "Two 2.10.03-only entry points" below). Earlier
text here described the grid as just `swe_calc`/`swe_calc_ut` plus `swe_houses`/`swe_houses_armc`
and omitted all 600 crossing rows; before that it also had no direct ayanamsa coverage at all --
see "Direct vs. indirect ayanamsa coverage" below.
`Tools/OracleGrid/grid-files.tsv` is 3,251<!--doccount:grid-files-total--> rows -- 900<!--doccount:grid-files-func-calc--> `CALC`, 900<!--doccount:grid-files-func-calc-ut--> `CALC_UT` (`SEFLG_SWIEPH`,
reading the shipped `.se1` files), 208<!--doccount:grid-files-fixstar-family-total--> across the `swe_fixstar` family (`FIXSTAR`/`FIXSTAR_UT`/
`FIXSTAR2`/`FIXSTAR2_UT` 48<!--doccount:grid-files-func-fixstar--><!--doccount:grid-files-func-fixstar-ut--><!--doccount:grid-files-func-fixstar2--><!--doccount:grid-files-func-fixstar2-ut--> each, `FIXSTAR_MAG` 8<!--doccount:grid-files-func-fixstar-mag-->, `FIXSTAR2_MAG` 8<!--doccount:grid-files-func-fixstar2-mag-->, reading `sefstars.txt`), 24<!--doccount:grid-files-func-get-planet-name-->
`GET_PLANET_NAME`, 220<!--doccount:grid-files-crossing-total--> crossing rows (`HELIO_CROSS`/`HELIO_CROSS_UT` 72<!--doccount:grid-files-func-helio-cross--><!--doccount:grid-files-func-helio-cross-ut--> each,
`SOLCROSS`/`SOLCROSS_UT` 16<!--doccount:grid-files-func-solcross--><!--doccount:grid-files-func-solcross-ut--> each, `MOONCROSS`/`MOONCROSS_UT` 16<!--doccount:grid-files-func-mooncross--><!--doccount:grid-files-func-mooncross-ut--> each,
`MOONCROSS_NODE`/`MOONCROSS_NODE_UT` 6<!--doccount:grid-files-func-mooncross-node--><!--doccount:grid-files-func-mooncross-node-ut--> each) -- 820 crossing rows omitted from an earlier draft
across both grids combined -- plus 499 rows across two of the six entry points added in an earlier
record, plus a further 500 rows across `HOUSES_EX2`/`HOUSES_ARMC_EX2` (below).
`grid-files.tsv` carries no `AYANAMSA`/`AYANAMSA_EX`/`AYANAMSA_EX_UT`/`AYANAMSA_UT` rows of its own:
none of the four opens an ephemeris file, so all direct ayanamsa coverage lives in
`grid-analytic.tsv` -- see "The sentinel ephemeris path..." below for why that premise needed its
own fix rather than a new file-backed grid.
Both `Tests/oracle/known-diff.tsv` and `Tests/oracle/known-diff-files.tsv`
are empty (0<!--doccount:oracle-known-diff-analytic--> and 0<!--doccount:oracle-known-diff-files--> rows respectively): there is no recorded exception on either grid, on any platform.

**Two 2.10.03-only entry points, `HOUSES_EX2`/`HOUSES_ARMC_EX2`.** `swe_houses_ex2` and
`swe_houses_armc_ex2` are new in 2.10.03 (absent from `external/pyswisseph-2.08/swephexp.h`
entirely). The oracle already reached both on every `HOUSES`/`HOUSES_EX` row, because
`swe_houses`/`swe_houses_ex` delegate to them (`swehouse.c:173,186`), but always with
`cusp_speed`/`ascmc_speed`/`serr` hardcoded `NULL`, so `h.do_speed`/`h.do_hspeed`
(`swehouse.c:642-647`) stayed `FALSE` and the 2.10 speed feature was switched off in every row that
reached it that way. `HOUSES_EX2` (`swe_houses_ex2`, called with real `cusp_speed`/`ascmc_speed`
arrays) is 1,500<!--doccount:grid-analytic-func-houses-ex2--> analytic rows plus
300<!--doccount:grid-files-func-houses-ex2--> file-backed rows, mirroring `HOUSES_EX`'s own sweep
exactly. `HOUSES_ARMC_EX2` (`swe_houses_armc_ex2`) is 3,000<!--doccount:grid-analytic-func-houses-armc-ex2-->
analytic rows plus 200<!--doccount:grid-files-func-houses-armc-ex2--> file-backed rows; the
file-backed rows exist for dispatch/schema parity with `grid-analytic.tsv` even though
`swe_houses_armc_ex2` itself opens no file (pure geometry, like `swe_houses_armc`). Both guarded
behind `SWISSEPH_HAS_HOUSES_EX2` in `sedump.c`, the same compiled-in-2.10.03-only pattern
`SWISSEPH_HAS_CROSSING` already uses for the eight crossing functions -- `Tools/OracleDump/Program.cs`
has no `SWISSEPH_HAS_*` symbol at all, correctly, since the port is single-version and has nothing
to guard; only `sedump.c` is compiled against two library versions (2.10.03 with the macros
defined, 2.08 without). `swe_fixstar2_mag` needed no
such guard (it is declared and implemented in `external/pyswisseph-2.08/swephexp.h:708`); both
drivers previously called only `swe_fixstar_mag`, and `FIXSTAR2_MAG`'s 8<!--doccount:grid-files-func-fixstar2-mag-->
rows close that gap. All new rows compare bit-identical against Astrodienst's own C, zero
`known-diff.tsv` entries.

**The sentinel ephemeris path and the AYANAMSA_EX/AYANAMSA_EX_UT environment leak.**
`grid-analytic.tsv`'s own header claims every row "depends on no ephemeris data file and is
reproducible on any machine". Measured false in two ways, both closed in this record. First, the
grid's own two-argument invocation (no `-EpheDir`) left `swed.ephepath` at whatever
`swi_init_swed_if_start()` set at process start until the first row whose `epheflag` was not
`SEFLG_MOSEPH` ran far enough to trigger `sweph.c:639-640`'s lazy `swe_set_ephe_path(NULL)` --
and `swe_set_ephe_path` checks `getenv("SE_EPHE_PATH")` before its own path argument
(`sweph.c:1327-1330`), so from that row onward the whole run's `swed.ephepath` reflected whichever
of the environment variable or the compiled-in default happened to apply on that machine, that run.
Both drivers now call `swe_set_ephe_path` unconditionally before every row -- a guaranteed-nonexistent
sentinel path when the grid gave no `-EpheDir`, the real directory otherwise -- so every row sees a
deterministic path regardless of iteration order, CWD or whether the compiled-in default happens to
exist. Proven byte-identical: with `SE_EPHE_PATH` unset, the recorded dump is SHA-256 identical
before and after this fix (`4ac1a3c0…7640` both times). This does **not** close the environment's
influence when `SE_EPHE_PATH` genuinely is set -- the variable still overrides whatever path either
driver passes, exactly as it overrides a real path, which is `swe_set_ephe_path`'s own documented
priority, faithfully ported. Second, `AYANAMSA_EX`/`AYANAMSA_EX_UT` rows now OR `SEFLG_MOSEPH` into
their `iflag`, closing a *different* leak specific to twelve `sid_mode`s
`swi_get_ayanamsa_ex`'s own guard names (`sweph.c:3031-3045`): those modes call `swe_fixstar`
(`sweph.c:7896-7953`, not `swe_fixstar2` -- `swi_get_ayanamsa_ex`'s star/galactic branches call
`swe_fixstar` directly, e.g. `sweph.c:3051` for `SE_SIDM_TRUE_CITRA`), which resolves their star
position from a hardcoded built-in table (`get_builtin_star`, `sweph.c:6750-6803`) before it would
ever fall through to `sefstars.txt`. What still leaked: `swe_fixstar`'s own position calc,
`swi_fixstar_calc_from_record` (`sweph.c:7613`), calls `main_planet_bary` for Earth's barycentric
position (`sweph.c:7711-7714`), which -- under the default `SEFLG_SWIEPH`, i.e. without
`SEFLG_MOSEPH` forced -- opens a planet file via `get_new_segment`'s
`swi_fopen(..., swed.ephepath, serr)` (`sweph.c:2192`). `swed.ephepath` at that point is whatever
the driver's own per-row `swe_set_ephe_path` call already resolved it to, honoring
`SE_EPHE_PATH`'s priority (`sweph.c:1327-1330`) -- the same priority rule as above, reached
through a file-open path rather than a fresh lazy re-init. A column-level diff (value columns and retc, not just row count) found only err-column
movement on these rows before the fix, never a value or retc change, so this closes a
reproducibility gap without changing any value. `swe_get_ayanamsa`/`swe_get_ayanamsa_ut` have no
`iflag` parameter to carry `SEFLG_MOSEPH` on at all -- `swi_guess_ephe_flag()`
(`swephlib.c:3186-3195`) resolves them to `SEFLG_SWIEPH` on this grid, but the function itself is
not unconditional: it returns `SEFLG_JPLEPH` whenever `swed.jpl_file_is_open` (`swephlib.c:3190-3192`),
which is true of grid-analytic.tsv specifically -- it opens no JPL file -- not of
`swi_guess_ephe_flag()` in general -- and `HOUSES_EX`/`HOUSES_EX2`'s own
`SIDEREAL` rows carry no `SEFLG_MOSEPH` either; both remain genuinely environment-sensitive
(measured: pointing `SE_EPHE_PATH` at a real, populated ephemeris directory changes `AYANAMSA`/
`AYANAMSA_UT` *values*, and `HOUSES_EX`/`HOUSES_EX2`'s `SIDEREAL` rows for every house-system
letter, not merely the hsys `'I'`/`'i'` Sunshine sub-call). The sentinel-path fix above is the
mitigation for those two: it makes the grid deterministic when the variable is unset, which is the
normal case, without pretending to close what only clearing the variable (or forcing `SEFLG_MOSEPH`
where the API allows it) can close.

**Six astrology-program entry points, added in this record.** `HOUSES_EX`, `AYANAMSA_UT`,
`SIDTIME`, `AZALT`, `HOUSE_NAME` and `NOD_APS_UT` are calls a real astrology program makes (this
fork's own use case; Celestium is named only as one example consumer, not a source this record
references) that no grid measured before this addition. `HOUSES_EX` (`swe_houses_ex`, the
sidereal/radians-capable sibling of `swe_houses`) is the highest-value of the six: 1,500<!--doccount:grid-analytic-func-houses-ex-->
analytic rows plus 300<!--doccount:grid-files-func-houses-ex--> file-backed rows, the latter
covering `SIDEREAL` `sid_mode`s that can drive the ayanamsa through a file-backed `swe_calc`, which
the analytic grid's forced `SEFLG_MOSEPH` can never reach. `AYANAMSA_UT` (`swe_get_ayanamsa_ut`)
mirrors `AYANAMSA`'s own sweep, 200<!--doccount:grid-analytic-func-ayanamsa-ut--> rows. `SIDTIME`
(`swe_sidtime`) and `AZALT` (`swe_azalt`) had zero coverage anywhere in this repository before
this addition: 12<!--doccount:grid-analytic-func-sidtime--> and 48<!--doccount:grid-analytic-func-azalt-->
rows respectively; `AZALT`'s rows deliberately include `atpress = 0` paired with a non-zero
height, to exercise `swe_azalt`'s pressure-estimate branch rather than only assert it exists.
`HOUSE_NAME` (`swe_house_name`) is a trivial pure lookup, 26<!--doccount:grid-analytic-func-house-name-->
rows -- the one place either grid exercises house system `'J'` at all, but only as a string lookup,
not the cusp geometry `docs/known-issues.md`'s "What the oracle grids do not cover in the house
code" still documents as uncovered for `'J'`. `NOD_APS_UT` (`swe_nod_aps_ut`) is 183<!--doccount:grid-analytic-func-nod-aps-ut-->
analytic rows plus 199<!--doccount:grid-files-func-nod-aps-ut--> file-backed rows, the latter the
one place either grid exercises `SE_CHIRON` through `swe_nod_aps_ut` at all (it has no Moshier
model, so its mean-speed override needs a real `seas_12.se1`/`seas_18.se1` segment). All six
funcs' rows compare bit-identical against Astrodienst's own C with zero `known-diff.tsv` entries,
the same as every other func in both grids.

**Direct vs. indirect ayanamsa coverage.** Before this addition, every `sid_mode` either grid
carried was exercised only *indirectly*: a `SEFLG_SIDEREAL` `swe_calc`/`swe_calc_ut` or
solar/lunar-crossing row applies the ayanamsa correction internally, so a bit-identical result
proves the correction was applied to *something*, never what the ayanamsa value itself was --
`swe_get_ayanamsa`, `swe_get_ayanamsa_ex` and `swe_get_ayanamsa_ex_ut` were called by zero rows in
either grid. The `AYANAMSA`/`AYANAMSA_EX`/`AYANAMSA_EX_UT` rows above close that: they call the
ayanamsa functions directly and compare the returned value (and, for the `_EX`/`_EX_UT` forms, the
return code and `serr`) bit for bit against Astrodienst's own C. The `SEFLG_SIDEREAL` rows
`swe_calc`/`swe_calc_ut` and the solar/lunar crossing functions already carried are also no longer
pinned to one fixed mode: `Tools/OracleGrid/gen-grid-analytic.ps1` and `gen-grid-files.ps1`'s own
`Get-NextSidMode` now cycle every such row's `sid_mode` deterministically across all 47 predefined
modes (previously always `SE_SIDM_LAHIRI`), the same 47-mode space
`Tools/BaselineMatrix/Ayanamsa.cs` sweeps for the characterization baseline -- widening indirect
coverage without multiplying row count (one mode per existing row, not a new row per mode).
`SE_SIDM_USER` (mode 255, `swe_set_sid_mode`'s custom-epoch path) previously had no representation
in either grid at all: `sedump.c`'s driver hardcoded `swe_set_sid_mode(sid_mode, 0, 0)`, so there
was no column to carry a non-default `t0`/`ayan_t0` even if a row had wanted to. Both grids now
carry `t0` and `ayan_t0` columns (always empty, meaning 0.0, except on `SE_SIDM_USER` rows), and
both drivers pass them through; the `AYANAMSA` family's dedicated `SE_SIDM_USER` sub-sweep (three
`t0`/`ayan_t0` pairs) is where that mode is actually pinned. Runtime cost of all three changes
combined: the isolated C driver replays 18,064 rows (up from 17,064) in 0.78s, statistically
indistinguishable from 17,064 rows' own 0.78s on the same machine -- the added `AYANAMSA` rows and
the sid-mode cycling (same row count, different values) do not measurably change per-row cost.

**Linux is now gated too, by `linux-exactness` in `.github/workflows/oracle.yml`.** It reuses both
grids and both drivers unchanged; `sedump.c` needs `-DSWISSEPH_HAS_CROSSING=1`,
`-DSWISSEPH_HAS_HOUSES_EX2=1` and an explicit source list to build there, the same two macros the
Windows build sets (`scripts/run-oracle-dump.ps1`). That is two macros, not one, and the second
was added later: `sedump.c` is compiled against 2.10.03 in six places, and a macro added to the
Windows build alone leaves the four non-Windows compile lines taking the 2.08 sentinel branch.
Measured when exactly that happened -- gcc, both grids, this repository's own `oracle.yml` compile
line -- the C side emitted the sentinel for 4,500 analytic rows while the port computed real
values, and the job's `cmp` failed. Nothing catches it earlier, because the sentinel branch
compiles cleanly; a macro added here has to be added to all six. At last measurement from this
workstation (14,820 and 2,244 rows -- the grid's size at that time, two additions before the
current 25,540<!--doccount:grid-total-combined--> total this record's "Of the 25,540 oracle rows"
paragraph below cites; not re-quoted as a "current" figure here because it was not current even
when this sentence was first written, only "at last measurement"), both grids produced exactly
that many rows on Linux, matching Windows row for row, and every row matched Astrodienst's own C
bit for bit, on `ubuntu-latest` (gcc 13.3.0, glibc), matching the Windows and macOS results at that
same row count; `linux-exactness` re-verifies the current row count on every push and pull
request, not measured fresh from this workstation since. Unlike macOS, the gate
build needs neither `-ffp-contract=off` nor `-fno-builtin`: base x86-64 has no FMA3 encoding at
all (`linux-exactness`'s own `objdump` check, mirroring `macos-exactness`'s `otool` check, finds
zero fused multiply-add instructions at plain `-O2`), and although gcc does substitute glibc's
`sincos` for an adjacent `sin`/`cos` call even without `-fno-builtin` -- confirmed with `nm`,
the same class of substitution that breaks macOS's bit-exactness -- glibc's `sincos` returns
bit-identically to calling the two functions separately, unlike Apple's. A second build adding
both flags is kept alongside as a diagnostic, the same way `macos-exactness` keeps its
builtins-on build, and currently shows zero rows differing from the plain `-O2` gate build on
either grid, confirming the flags make no difference on this glibc. Before this job existed, the
Linux result came from a single hand-run of the grid in a WSL2 Docker container, with nothing
re-checking it; `linux-exactness` replays it on every push and pull request instead.

**macOS is now measured, gated on `-fno-builtin`.** `.github/workflows/oracle.yml`'s
`macos-exactness` job builds Astrodienst's C with clang on macOS arm64 (Apple's libSystem, a
third libm alongside `ucrtbase.dll` and glibc above) and replays both grids against it, the same
way the Windows and Linux jobs do. It builds the C reference twice: once with `-fno-builtin`,
which is the gate, and once, kept alongside as a diagnostic rather than gated, with clang's math
builtins left on. With `-fno-builtin`, both grids are bit-identical against the port. At last
measurement from this workstation -- two grid additions before the current
25,540<!--doccount:grid-total-combined--> total, so the figures below are what that specific,
older run found, not a current claim; `macos-exactness` re-verifies the current row count on every
push and pull request -- with the diagnostic build's builtins left on, 62 of 14,819 analytic rows
and 10 of 2,243 file-backed rows differed from the port -- clang's default behavior substitutes
its own builtins for libm calls (e.g. fusing an adjacent `sin`/`cos` call on the same argument into
a single `__sincos`), and Apple's `__sincos` does not return bit-identically to calling the two
functions separately the way the port does; `-fno-builtin` removes that substitution. That run's
own printed row counts (14,819 and 2,243) were one short of each grid's then-true 14,820/2,244
(the grid's size at that time, matching the Linux paragraph's own figures above): that step
computes its total by subtracting a header line from `sedump`'s output file, but unlike the input
grid `.tsv` files, the output dump (`Tools/CReference/sedump.c`) never writes one, so the
arithmetic drops one real row from the printed total. The gate itself compares the output files
for whole-file equality, not this printed count, so the off-by-one affects only the diagnostic
step's own summary line, not what is actually gated.

Of the 25,540<!--doccount:grid-total-combined--> oracle rows, 24,532<!--doccount:grid-total-default-nutation--> exercise
the default nutation path rather than opting out via `SEFLG_NONUT`: 21,281<!--doccount:grid-analytic-default-nutation-->
of the analytic grid's 22,289<!--doccount:grid-analytic-total--> rows (1,008<!--doccount:grid-analytic-nonut-optout-->
opt out) and all 3,251<!--doccount:grid-files-default-nutation--> files-grid rows
(0<!--doccount:grid-files-nonut-optout--> opt out). Recomputed directly from both grids' `iflag`
columns (bit 64, `swephexp.h:193`), not carried forward from an earlier row count. All 24,532 are
among the bit-identical rows above -- see "What this record does not cover" for what that does and
does not establish about the nutation coefficient tables themselves.

## 2. Characterization baseline

The committed baseline (`Tests/baseline/`) is a Windows-generated golden master; the gate that
checks current code against it (`scripts/verify-baseline.ps1`) runs on both `net8.0` and
`net10.0` and requires zero FAIL rows on either. It currently passes clean on both.

That baseline is also, by design, not portable to another platform. Comparing the same source at
the same commit, built and run on both Windows and a Linux container
(`mcr.microsoft.com/dotnet/sdk:10.0-noble`, Ubuntu 24.04.4), each against its own compiled C
reference:

- 3,547,367 numeric fields compared; 66,342 (1.87%) differ at all between platforms; 5,394 are
  still beyond the shipped tolerance (`max(1e-12 absolute, 1e-13 relative)`) after the
  angle-wraparound allowance.
- `net8.0` and `net10.0` report identical numbers to the field (3,547,367 / 66,342 / 5,394 on
  both), so the divergence is `ucrtbase.dll` versus glibc, not a difference between .NET runtime
  versions.
- Five of the baseline's areas are bit-identical across platforms outright: `format`, `misc`,
  `pheno-ast`, `risetrans`, and `atmo`. None of the five involves a transcendental function on the
  divergent code path.

Full area-by-area numbers, the tolerance-level cost table, and the reasoning behind locking the
gate to Windows rather than loosening the shipped tolerance live in `Tools/BaselineGen/README.md`
under "Platform lock" -- this is not restated here in full. The short version: the gate is locked
to the platform that generated it (Windows, `verify-baseline` in CI); a separate Linux job reports
this same drift on every run without gating on it, since a purely numeric comparison cannot tell
libm noise apart from a real regression of similar magnitude in the same fields.

## 3. Correctness oracle

The reference corpus is Swiss Ephemeris 2.10.03's own `setest` test suite: 12,757 iterations
across 10 functional areas, checked against `external/swisseph/setest/t.exp` within the
tolerances `setest/t.fix` itself ships. `Tests/conformance/known-fail.tsv` is the work queue: one
row per iteration the port does not currently match.

As of this record, `known-fail.tsv` carries 1,423<!--doccount:known-fail-total--> rows, so 11,334 of 12,757 iterations pass
(88.8%). That is down from 3,291 rows when the oracle landed (commit `7013ed7`) -- the 2.10.03
port has closed 1,868 of the iterations it started behind on. An earlier revision of this
paragraph cited a development commit that never reached this branch's history, and its 4,382-row
figure with it; both are unreachable for anyone reading from a clone.

The 1,423<!--doccount:known-fail-total--> remaining rows split into two categories, with 0<!--doccount:known-fail-error--> in
`ERROR`, 0<!--doccount:known-fail-unreproducible--> in `UNREPRODUCIBLE`, and 0<!--doccount:known-fail-not-implemented--> in
`NOT-IMPLEMENTED` at present:

| Category | Rows | What it means |
|---|---|---|
| `VALUE-MISMATCH` | 664<!--doccount:known-fail-value-mismatch--> | The port ran and produced an answer outside `t.fix` tolerance -- the actual porting work queue. |
| `DATA-MISSING` | 759<!--doccount:known-fail-data-missing--> | A required data file (a JPL DE ephemeris, a pre-1200/post-2399 era `.se1` file, `ephe/sat/`, or a per-asteroid file) is not shipped by this repo, so the iteration was not run at all. |

**157 of the 759 `DATA-MISSING` rows are `SEFLG_SWIEPH` calls for a date outside the era this
repo's shipped core ephemeris files cover** (the `sepl`/`semo`/`seas_N.se1` and their BCE `_N.se1`
counterparts, roughly years 1200-2399). These stay `DATA-MISSING` by design: shipping the full
era file set would add well over 100 MB to a repository whose vendored C source is already kept
sparse for the same reason (`CONTRIBUTING.md`, "The upstream C is vendored at
`external/swisseph`"). This is a data-availability decision, not a gap in what has been checked --
the next paragraph gives the actual number for what these 157 rows would show if the data were
present.

**A one-time probe (`Tests/conformance/regenerations.log`, Phase 6) widened the ephemeris checkout
to the full era file set, `ephe/sat/`, and a JPL DE431 file, and re-ran the full 12,757-iteration
corpus with all three opt-in flags set, without changing `known-fail.tsv`.** Of the (then-713)
`DATA-MISSING` rows, 504 pass outright once the data is present: 500 of 538 JPL rows and 4 of 18
`ephe/sat/` rows. **None of the 157 era rows pass** -- with the data present, they surface as
`VALUE-MISMATCH` instead, the same tracked 2.08-versus-2.10.03 gap the rest of the actionable
queue shows (magnitudes ranging from roughly 1.2e-11 relative for the bulk of them, about 2.5
seconds of crossing-time error, up to 0.42% relative for one delta-T iteration). So the 157 era
rows are correctly filed as `DATA-MISSING`: the data genuinely is not shipped, and having it would
not have made them pass regardless. The probe's own env-var opt-ins
(`SWISSEPH_CONFORMANCE_INCLUDE_JPL`, `SWISSEPH_CONFORMANCE_INCLUDE_MOONS`) are real, standing
features of `Tests/SwissEphNet.Conformance.Tests`; the era-file opt-in it used
(`SWISSEPH_CONFORMANCE_INCLUDE_ERA`) was a temporary, reverted probe, not a shipped feature.

**46 rows moved from `VALUE-MISMATCH` to `DATA-MISSING` in the triage described in section 3a
below, none of them the era class above.** `EphemerisFileResolver` gained two more static checks
(`NeedsAsteroidFileWeDoNotShip`, `NeedsCenterBodySatFileWeDoNotHave`), wired into
`ConformanceDispatcher`'s universal pre-check: 16 rows cite a numbered-asteroid file this repo
never ships at any tier (`se00433s.se1` for 433 Eros, `se00010s.se1` for 10 Hygiea), and 30 cite
`SEFLG_CENTER_BODY`'s own per-planet `ephe/sat/` record (`sepm9599.se1` through `sepm9999.se1`)
for a major-planet `ipl` that a plain planetary-moon-range check does not catch. Verified against
the full corpus before landing: zero of the 22 `ipl`/`iplctr` > `SE_AST_OFFSET+4` iterations and
zero of the 45 `SEFLG_CENTER_BODY` iterations anywhere in `t.exp` were passing beforehand, so
neither check turns a real pass into a false `DATA-MISSING` -- `scripts/regenerate-known-fail.ps1`
confirmed this directly (0 added, 0 removed, exactly 46 recategorized). A third, adjacent class
(5 suite 1 rows citing `seplm36.se1`/`sepl_30.se1`, the same era-file gap as the paragraph above
but never wired into suite 1's own dispatcher) was investigated and deliberately **not** fixed the
same way: suite 1's `swe_calc`/`swe_calc_ut`/`swe_calc_pctr` return a genuinely correct answer via
Moshier fallback for *some* out-of-era dates even without the file, unlike `swe_deltat_ex` and the
crossing functions that already carry this check -- wiring it into suite 1 unconditionally flipped
14 previously-passing iterations to `DATA-MISSING` in a first attempt (caught by
`regenerate-known-fail.ps1`'s own added/removed count, then reverted). Those 5 rows stay
`VALUE-MISMATCH`; section 3a's own triage classifies them directly instead (see
`Tests/conformance/value-mismatch-triage.tsv`).

**The `reason` column in `known-fail.tsv` is documentation, not part of what the gate checks.**
`scripts/regenerate-known-fail.ps1` and the gate itself compare only `category`; the free-text
`reason` can drift from the exact current failure without the gate noticing (`CONTRIBUTING.md`,
"Correctness oracle known-fail list"). Treat the numbers above, sourced from the category column
and from the Phase 6 probe's direct run, as load-bearing; treat individual `reason` strings as
best-effort commentary.

## 3a. VALUE-MISMATCH triage against Astrodienst's own C

This triage ran against 668 `VALUE-MISMATCH` rows, the count at the time. The table above says 664
because the four defects the triage found have since been fixed and pruned; the numbers below are
the triage's own, and are left as it measured them.

Those rows are the porting work queue in name, but a queue entry is only
actionable if the port is actually wrong. `Tests/conformance/value-mismatch-triage.tsv` checks
that directly, for every row: it drives Astrodienst's own MSVC-built 2.10.03 C
(`external/.c-reference/build-2.10.03/libswe-2.10.03.lib`, the same library the bit-exact oracle
above uses) through the identical suite/testcase/iteration sequence `ConformanceRunner.cs` and
every `Dispatch/Suite0*.cs` file replay against the port, using a scratch C driver that
transliterates that same dispatch logic (not `Tools/CReference/sedump.c`, which only covers
`CALC`/`CALC_UT`/`HOUSES`/`HOUSES_ARMC`/the fixed-star family/the crossing functions -- suites 5,
7, 8 and 9 call functions outside that set entirely). Each row gets three numbers: what the port
produced (already recorded in `known-fail.tsv`'s own `reason` column), what `t.exp` expects (same
source), and what this fresh, independently-built C produces for the identical input.

Of 668 rows, 664 are **drift**: the C reference reproduces the port's own output, not `t.exp`'s,
so `t.exp` and the current build disagree for a reason that has nothing to do with a porting
defect. 360 of the 664 are a single root cause (suite 6 testcase 3, sidereal `swe_houses_ex` with
house system `W`/Whole Sign and `isid` 0 or 27): both the port and this replay's C driver resolve
the requested ayanamsa to 0 and fall back to tropical sign boundaries, producing cusps at exact
30-degree multiples, where `t.exp`'s own values are offset by the correct ayanamsa -- reproduced
identically by fresh, independently-built C under the same replay sequence, so this is a
replay/environment artifact (this triage's own harness runs suites in one continuous process;
Astrodienst's original `t.exp`-generating run may not have), not a value the port computed wrong.
The rest split across every other suite at magnitudes from a few ULP up to roughly 0.1% relative,
consistent with the cross-toolchain floating-point drift already measured for suite 4's ayanamsa
rows (`docs/known-issues.md`) and the platform drift measured for the characterization baseline
(section 2 above) -- Windows/MSVC/UCRT against whatever produced `t.exp` (its own header: user
`alois`, 14.12.2023), not a defect surviving in either C or the port.

4 rows are a **real, confirmed port defect**: suite 1 testcase 1 iterations 377, 379, 383, 385,
all `ipl=22` (`SE_INTP_PERG`, interpolated lunar perigee) at a JD outside `[625000.5, 2818000.5]`.
`external/swisseph/sweph.c`'s `SE_INTP_PERG` branch (:994-1006) checks that range and returns
`ERR` with `serr` "Interpolated apsides are restricted to JD 625000.5 - JD 2818000.5" before ever
computing a position; `SwissEphNet/CPort/Sweph.cs`'s `SE_INTP_PERG` branch (:1179-1197) has no
such check -- its sibling `SE_INTP_APOG` branch immediately above (:1152-1178) does have it
(:1161-1168), so this reads as the same guard simply not copied down to the next `else if`. The C
reference (built independently from the same pinned `external/swisseph` commit) returns `ERR` and
the matching `serr`, exactly matching `t.exp`; the port returns a computed (wrong, out-of-range)
position instead.

Not fixed by the triage itself, which scoped to `Tests/SwissEphNet.Conformance.Tests/` rather than
`SwissEphNet/CPort/`. Fixed afterwards in `b5af491`, which mirrored the `SE_INTP_APOG` guard down
into the `SE_INTP_PERG` branch and pruned these four rows, taking `VALUE-MISMATCH` from 668 to 664
and the known-fail total from 1,427 to 1,423. So of the 668 rows this triage examined, none now
remain that anyone has shown the port gets wrong.

Suites reached: all nine that carry a `VALUE-MISMATCH` row (1, 2, 4, 5, 6, 7, 8, 9, 10) -- every
row was driven, none skipped for reach reasons. Suite 3 carries zero `VALUE-MISMATCH` rows and was
not replayed (`swe_close()` at suite 6's own start already firewalls it from anything downstream).

## 4. SweTest text-output comparison

`scripts/verify-swetest-diff.ps1` runs every row of `Tools/SwetestDiff/args-grid.tsv` (253 rows,
one CLI argument string each) through both `Programs/SweTest` and Astrodienst's own compiled
`swetest.exe`, and diffs their printed output line for line. `Tests/swetest/known-diff.tsv` is
the same shape as the correctness oracle's `known-fail.tsv`: one row per `case_id` whose output
does not match, checked by category so a listed row whose difference has changed shape still
fails the gate.

**This is the one instrument in this document that is not currently clean**, though it is closer
than it was. `Tests/swetest/known-diff.tsv` carries 12<!--doccount:swetest-known-diff--> rows, all category `OUTPUT-DIFFERS`, so 241
of 253 argument strings (95.3%) produce output that matches Astrodienst's C exactly. All 12 are
path-separator or placeholder cosmetics rather than computational divergence: most print an
ephemeris-file-not-found message that embeds the search path, where the C reports it with `\`
(`'<ephe-dir>\'`) and the port with `/` (`'<ephe-dir>/'`), or the C reports a literal directory
where the port reports the `[ephe]` placeholder token this comparison substitutes for the actual
(machine-specific) ephemeris directory. One further row (`FMT_MULTI|6`) differs only in how
not-a-number prints: C's `-nan(ind)` against the port's `NaN`.

The three `PLSEL_TRUNCATION` rows that used to sit here were the one real output-shape gap in
this list, and they are gone. `Programs/SweTest` read `-p<seq>`'s body as a single `char` where
`swetest.c:1120` takes a `char *` to the whole remainder, so `-p0123456789` computed one body
where the C computes ten. Fixing that made all three match outright, and a `HOUSES_CRASH` row was
added to the grid at the same time for the Gauquelin argument string that used to throw before it
could print anything.

None of the 12 rows is `Tests/swetest/known-diff.tsv`-invisible: every one is category
`OUTPUT-DIFFERS` and none is `ERROR` or unrecognized. `.github/workflows/oracle.yml`'s
`swetest-diff` job carries `continue-on-error` on the comparison step alone (not on the gitlink
assertion, submodule checkout, or C build before it) because `known-diff.tsv` records printed
output captured from one specific MSVC build, which a future toolchain bump could shift without
the port changing -- see that workflow's own header comment for the full reasoning and for why
this is the only one of the four instruments' gates still allowed to go red.

## What this record does not cover

**macOS is now measured on the bit-exact oracle** (`macos-exactness` in `.github/workflows/oracle.yml`;
see "1. Bit-exact oracle" above), **but still unmeasured on the other three instruments, and two
of those are Linux-unmeasured too.** The correctness oracle (`conformance.yml`) and the SweTest
text-output comparison (`swetest-diff` in `oracle.yml`) each run on `windows-latest` only, with no
Linux or macOS leg at all. The characterization baseline is the one exception: it gates on Windows
and additionally runs a report-only job on Linux (`verify-baseline-linux` in `baseline.yml`), which
is where section 2's Linux divergence numbers above come from -- but it, too, has no macOS leg.

**The nutation coefficient tables in `SwissEphNet/CPort/SweNut200a.h.cs` have never been
independently diffed, value by value, against `external/swisseph/swenut2000a.h`.** The array
lengths match exactly on both sides (`NLS` 678, `NLS_2000B` 77, `NPL` 687), which rules out gross
truncation, but that is a length check, not a value check, and no commit or script in this
repository's history performs the latter. The indirect evidence is real but is not the same
claim: 24,532<!--doccount:grid-total-default-nutation--> of the 25,540<!--doccount:grid-total-combined-->
bit-exact oracle rows exercise the default (non-`SEFLG_NONUT`)
nutation path and match Astrodienst's own C bit for bit, which a wrong coefficient of any
consequence would be very unlikely to survive. That is strong corroboration through the oracle,
not a direct audit of the table itself.

**Seven call sites in `SwissEphNet/CPort` transliterate a C fixed-size stack buffer
(`char buf[N]`) as a live, unbounded C# `string`.** The C's buffer-boundary behavior (truncation,
overflow) is consequently not reproduced at these sites: `SwephLib.cs:4682`; `SweHel.cs:327`;
`Sweph.cs:7408`, `:8312`, `:8030-8031`, `:8128-8129`, `:9240-9241` (the last three are TLS-static
pairs, `slast_stardata`/`slast_starname` sharing one site each, for `swe_fixstar2`,
`swe_fixstar2_mag` and `swe_fixstar` respectively). None of these has produced an observed
divergence in the baseline, the bit-exact oracle, or the correctness oracle to date -- the C#
`string`'s lack of a bound has simply never been exercised past the C's own buffer size in any
input any of the four instruments generates. That is an absence of a triggering input, not a
proof the sites are safe against one.

An earlier version of this document said "eleven call sites" and listed twelve; recounted, the
correct number is seven. Five of the twelve previously listed were not live call sites at all:
`SwephLib.cs:4893` and `:4926` sit inside `swi_open_trace`'s entirely hand-commented `#if TRACE`
body (the live implementation is the one-line stub three lines above,
`internal void swi_open_trace(out string serr) { serr = null; }`); `SweHel.cs:2493` sits inside
`get_asc_obl_old`, itself guarded out by `//#if 0` in the C and never transliterated as live code
at all; `Sweph.cs:420` sits inside `swe_calc`'s `#if TRACE` block, inside the FORCE_IFLAG debug
mechanism, which the port carries only as a comment; and `Sweph.cs:8904` sits in
`swi_fixstar_calc_from_record`, whose C buffer was dropped outright rather than carried forward as
a string -- no identifier named `s` appears anywhere else across that function's 323 lines. Each
of the five is a reference to commented-out or eliminated C, not to a live C# `string` standing in
for a live C buffer, so none belongs on this list.

**The `reason` column caveat above** applies to every number in this document that is sourced
from `known-fail.tsv`'s category counts rather than a fresh run: category is gated and reliable,
free text is not.

**The bit-exact oracle's house-code coverage is narrower than "17,789 of 17,789 match" suggests,
though this record closes part of the gap.** Before this record, both drivers called only the
six-argument `swe_houses`/`swe_houses_armc` forms; they now also call `swe_houses_ex` (the
iflag-taking, sidereal/radians-capable form) and `swe_house_name`. `swe_houses_ex2`/
`swe_houses_armc_ex2`'s own speed-derivative outputs (`cusp_speed`/`ascmc_speed`), `swe_house_pos`,
and every house-code `serr` path (`swe_houses_ex` has none either, matching `swe_houses`) still
have no bit-exact coverage at all -- some of the speed gap is partly covered by the correctness
oracle instead (house systems `'P'`/`'W'`/`'K'` only, for speeds). House system `'J'` (Savard-A)
still has no external validation of its cusp *computation* on either instrument: `HOUSE_NAME`'s new
coverage only confirms both sides agree on the string `swe_house_name` returns for `'J'` (a lookup
table entry), not the geometry `swe_houses_ex` would compute for it -- `'J'` stays deliberately
excluded from every hsys sweep that actually computes cusps, on both grids. See
`docs/known-issues.md`, "What the oracle grids do not cover in the house
code," for the full accounting.

## Sources

`docs/known-issues.md` (`SE_VERSION` entry and the DIR_GLUE/`niter_max`/`free_planets` entries
this compliance work built on), `CONTRIBUTING.md` ("Correctness oracle known-fail list",
"The two gates disagree on purpose, not by accident"), `Tools/BaselineGen/README.md` ("Platform
lock", "Matrix coverage"), `Tests/conformance/regenerations.log` (the Phase 6 DATA-MISSING probe
entry and its correction), `Tests/conformance/known-fail.tsv`, `Tools/OracleGrid/grid-analytic.tsv`
and `grid-files.tsv`, the `scripts/verify-oracle.ps1` / `scripts/run-oracle-dump.ps1` runs
this record's oracle numbers were taken from directly, `.github/workflows/oracle.yml` (the
`macos-exactness` and `swetest-diff` jobs), and `Tools/SwetestDiff/args-grid.tsv` /
`Tests/swetest/known-diff.tsv` for section 4's numbers.
