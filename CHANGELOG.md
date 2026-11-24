# Changelog

Project history from the Swiss Ephemeris 2.08 upgrade forward. Entries are prose bullets, not a
generated commit list — for anything earlier, see git history.

## 2.10.4.0

The port now tracks upstream tag `v2.10.04` (`e308765d10b9463ab0ed53b102ad454820ee0b43`), the first
upstream tag since `v2.10.3bfinal` and 43 commits after the previous pin, `43c9fe54`. `SE_VERSION`
is `"2.10.04"`. NuGet shows the package as `2.10.4`, because it drops a trailing zero. The entries
that were under "Unreleased" are part of this release and appear under Fixed and Performance.

### Breaking

- `SE_FNAME_DFT2` is removed. Upstream deleted it together with the DE406 fallback in
  `open_jpl_file`, and the port follows (`swephexp.h`, `sweph.c`), so code that names the constant
  no longer compiles. The default JPL file name, `SE_FNAME_DFT`, is `de441.eph`, and `SE_DE_NUMBER` is
  441. A caller that relied on the DE406 fallback no longer gets it.
- Results move without a code change, from the default Delta T model and the tidal acceleration
  (next section). UT-input results before 1 January 1962 and after 2020 differ from 2.10.3.3.
- The changed values are C# `const`s (`SE_TIDAL_DEFAULT`, and with it `SE_TIDAL_SWIEPH` and
  `SE_TIDAL_JPLEPH`; `SEMOD_DELTAT_DEFAULT`, `SEMOD_NDELTAT`, `SE_DE_NUMBER`, `SE_FNAME_DFT`), so
  the compiler inlines them into the caller. Code compiled against 2.10.3.x keeps the old values
  until it is recompiled against this version.
- A caller that has only `de431.eph` must now call `swe_set_jpl_file("de431.eph")`: `SE_FNAME_DFT`
  is `de441.eph`, and the `SE_FNAME_DFT2` fallback is gone.

### Changed to follow upstream

Each of these is an adoption of an upstream change, not a correction of the port.

- Delta T. The default model is `SEMOD_DELTAT_STEPHENSON_ETC_2020` (new, model 6), the Morrison,
  Stephenson, Hohenkerk and Zawilski 2021 spline, used before 1 January 1962
  (`deltat_stephenson_etc_2020()`, `swephlib.c:2964-3091`); the former default was
  `SEMOD_DELTAT_STEPHENSON_ETC_2016` (model 5). `dt[]` runs to 2033 (`TABEND`), and
  `adjust_for_tidacc()` changed (it uses 1955.5 and 0.000091072). Measured from the MSVC-built C
  at `e308765d`, `swe_deltat_ex` at JD 2086302.5 (1 January 1000 proleptic Gregorian, 27 December
  999 Julian, the calendar Swiss Ephemeris uses before 1582) is 0.018871612655972776 days under
  model 6 and 0.016726901931961108 days under model 5: 1630.507 s and 1445.204 s, so 185.303 s
  apart (0.002144710724 days times 86400). `SEMOD_NDELTAT` is 6.
- Tidal acceleration. `SE_TIDAL_DEFAULT` is `SE_TIDAL_DE441` (-25.936), where it was
  `SE_TIDAL_DE431`, and the new constant `SE_TIDAL_STEPHENSON_2020` is -25.82.
- Heliocentric light-time. With `SEFLG_HELCTR` the second light-time pass uses the heliocentric
  distance (upstream `8178d0ac`, in `app_pos_etc_plan()` and `app_pos_etc_plan_osc()`). Upstream's
  figures for the change are 21 mas for Mars in 2016 and up to 0.84" for Mercury.
- File choice. `swe_calc` chooses the ephemeris file by the date of the call (`tjd_calc`), so a
  result at a file boundary no longer depends on call history (upstream `79d3553f`, `85eb3a5d`,
  `49fbf676`; `sweph.c:308-340` and `:2277-2500`). Moon and Jupiter on 21 dates around the 1800
  `sepl`/`semo` `_12`/`_18` boundary now give the same result in ascending order, descending
  order, a fresh instance per date and after a call on the other side of the boundary.
- Moon light-time. When the light-time point falls outside the JPL file, the Moon falls back to
  Moshier (`sweph.c:750-765`).
- Error messages. The Chiron, Pholus, interpolated-apsides and file-limit messages now give the JD
  and the calendar date, for example `Chiron not available at JD 1967600.5 = 31.12.0674j;
  restricted to JD 1967601.5 = 01.01.0675j - JD 3419437.5 = 01.01.4650`. `swetest -lim` prints the
  JD and `-fT` dates in the same way, and shows the errors of `SE_INTP_APOG` and `SE_INTP_PERG`.
- `swe_set_astro_models("")` selects the current version's models (`AMODELS_SE_2_1004`).
- `SEFLG_JPLHOR` without the JPL file now returns a warning in `serr` (`plaus_iflag`).
- `load_dpsi_deps` reads the EOP `dpsi` column at the corrected offsets (`sweph.c:1571`, `:1575`),
  and `swe_close` resets the EOP state.
- `swe_set_ephe_path` follows the C's active body: it makes a test call for the Moon, closes the
  files again and keeps the DE number for the tidal acceleration.
- Data files. Upstream's `sepl`, `semo` and `seas` files were regenerated on DE441. Callers who
  rely on the ephemeris data should take the `v2.10.04` files.
- The upstream pin moves from `43c9fe54` to the tag `v2.10.04`. In `swetest.c` the `do_printf` and
  `USE_DLL` hunks (`0219d926`, `280e4ce1`) have no counterpart in the port, and
  `Tools/CReference/build-c.ps1` no longer patches `do_printf`, because upstream fixed it in
  `0219d926`; the `-astpos` variable-length-array patch stays.

### Fixed

- `swed` is cleared in place on first initialisation, as `sweph.c:1257` does
  (`memset(&swed, 0, sizeof(struct swe_data))`, and `:1285` for `astro_models`). The port replaced
  the object, so a reference taken before the first initialisation pointed at the discarded copy.
  This predates the sync. Two symptoms on a fresh `SwissEph`, measured against the MSVC-built C at
  `e308765d`: `swe_get_astro_models("SE2.06")` listed the default models (`-amod0,...`) where the C
  gives `-amod5,...`, and `swe_nod_aps` returned wrong apsides for some bodies and dates, for
  example Sun `MEAN` at JD 1000000 gave perihelion 215.29900167326124 where the C gives
  302.33849105597147. All 360 `nodaps` calls of the characterization baseline are now bit-identical
  to the C.
- `read_const` no longer drops leading digits from an asteroid's name beyond the C's prefix scan
  (`sweph.c:4614-4617`: spaces, then digits, then exactly one character). A name such as
  `1999 AB1` lost `1999 ` as well, so `ast_H`, `ast_G` and the diameter were read five columns
  too far; the magnitude for `se00005s.se1` renamed to that was 4.329m against the C's 11.707m,
  and is now identical.
- Under `SEFLG_SIDEREAL`, the `SwissEph file ... not found ... using Moshier eph.` warning in
  `serr` is no longer erased by `plaus_iflag` (`sweph.c:6078`). `swetest -b1.1.2000 -p0 -sid1
  -fPl -head -edirnowhere` prints the warning, as the C does.
- A planet file opened as an asteroid file (`swe_calc(2451545, SE_MARS, 0x32003E)`) now returns an
  error, `...sepl_18.se1 is damaged (0d)`, as the C does, instead of throwing
  `ArgumentOutOfRangeException`. The copyright-line check in `read_const` is still more lenient
  than the C's.
- A `seorbel.txt` elements line with a trailing comma is now accepted as the C accepts it
  (`swi_cutstr`, `swemplan.c:755`); it gave `nine elements required`.
- Digit and alphanumeric tests that the C makes with `isdigit` and `isalnum` on bytes are now
  ASCII-only. .NET had accepted any Unicode digit or letter. `read_const`'s version-line regex
  (`sweph.c:4554`) still uses `\d`.
- `SweHel`'s trace blocks are guarded by `SWEHEL_DEBUG`, as `swehel.c` does, instead of `#if
  DEBUG`, so a Debug build no longer compiles trace code the C never builds. Their loop
  variables now match the C's (`swehel.c:1515-1523`).
- Two `strcat` slips in `swetest`: `-lunecl -local` printed `lunar eclipse` without the `total`,
  `penumb.` or `partial` prefix, and global `-solecl` printed ` non-central solar` instead of
  `total non-central solar` or `annular non-central solar` (`swetest.c:3149`, `:3458-3459`).
- `FormatToDegreeMinuteSecond` rounds seconds once and carries into minutes, degrees and zodiac
  sign. It printed `10°20'13.9960` for 10 + 20/60 + 12.996/3600 (now `  10°20'12.9960`),
  `10°59'60.0000` for 10°59'59.99996" (now `  11° 0' 0.0000`) and `29 ar 59'60"` for 29.99999999
  in `Z2` (now ` 0 ta  0' 0"`), and dropped the sign of -0.5 (now `  -0°30' 0.0000`).
- `C.sprintf` takes a float's sign from the IEEE sign bit (`%+.2f` of -0.0 gave `+-0.00`; `%08.3f`
  gave `0000.000`, now `-000.000`), prints non-finite values as the C does (`nan`, `inf`, `-inf`,
  and `NAN`, `INF` for `E` and `G`, space-padded, honouring `+` and space; they were `NaN` and
  `Infinity`), and formats a non-string `%s` argument with the invariant culture (`1.5` printed
  `1,5` under nl-NL). The interpolated-apogee NaN fields in `swetest` output now read `nan`.
- A `FileProvider` that returns a non-seekable stream (a `GZipStream`, say) no longer makes
  `swe_calc_ut` with `SEFLG_SWIEPH` or `swe_fixstar2_ut` throw `NotSupportedException`; such
  streams are copied into a `MemoryStream` first.
- Double-to-int conversions match the C on every runtime. The C truncates, and on MSVC x64 NaN,
  infinities and out-of-range values give `INT_MIN`; .NET 8 did the same, but .NET 9 made the
  cast saturating, so on net10.0 `swe_lun_occult_when_glob(NaN, ...)` hung,
  `swe_sol_eclipse_when_glob(NaN)` and `swe_lun_eclipse_when(NaN)` returned an eclipse instead of
  an error, and `swe_revjul(1e12)` gave year 2147483647. The new public `C.ToInt32` and
  `C.ToInt64` apply the MSVC x64 behaviour, and every floating-point cast in the transliterated
  files uses them. Valid inputs are unaffected: the result is identical to a plain cast whenever
  the value is in range.

### Performance

- The first `swe_fixstar2` call of an instance no longer copies the fixed-star array once per
  record: 180,016,736 B allocated and 50.7-63 ms before, 4,860,851 B and 11.0-17.7 ms now. The
  array grows geometrically in `save_star_in_struct`, where the C calls `realloc`. Results are
  bit-identical.
- `C.bsearch` searches in place with the same probe sequence, where it copied the array and boxed
  every comparison. `swe_fixstar2_ut` with 12 rotating names went from 173,206 to 6,018 B and
  from 61-67 to 24 µs per call; `swe_fixstar2_mag` from 182,000 to 2,193 B and from 39-47 to
  1.5 µs. Results are bit-identical.

### Verification

- Oracle grids. Against the MSVC-built C at `e308765d` the port is bit-identical on 27,437
  analytic rows and 3,376 file rows, the dumps are SHA-256 identical, and both known-diff lists are
  empty.
- `swetest`. 251 of 268 cases are byte-identical to the MSVC-built `swetest`. The other 17 differ
  only by the directory separator and the CRT's `-nan(ind)`. `DATE_FORMATS|FAR_FUTURE` is newly
  listed, because it now prints a `sepl_24.se1` not-found warning in both programs.
- Characterization baseline, regenerated twice, each with a deviation note. For the Delta T and tidal
  defaults: with only those two changes reverted, exactly 82 case ids change, all Chiron `serr`
  rows, plus `VER`. For the `swed` fix: 156 rows, scope `NA`/`NAUT` only, and all 360 `nodaps` calls
  bit-identical to the C.
- Conformance. The corpus is now 11 suites, 68 testcases and 18,919 iterations (it was 10, 60 and
  12,757); suite 11 (`swe_pheno`, `swe_azalt`, `swe_refrac`, `swe_cotrans`) is new, and 8.9 calls
  `swe_lun_eclipse_when_loc`. `known-fail.tsv` has 1,635 rows, 1,258 `DATA-MISSING` and 377
  `VALUE-MISMATCH`, where it had 2,395 (759 and 1,636). The number measures divergence from
  `t.exp`, a static Astrodienst snapshot, not port defects: all 377 rows are triaged against the C
  in `Tests/conformance/value-mismatch-triage.tsv` as drift, 260 `DATA-ENV`, 91 `SERR-PATH` (an era
  ephemeris file outside the 8 declared) and 26 `LIBM-PLATFORM`. The harness uses `setest`'s v2.10.04
  comparison rules (per-quantity tolerances from `tolerances.c`, precision reset per testcase) and
  mirrors the file-scope statics of its suite files. The 972 house-speed rows of the `6c989727` sync pass
  against the regenerated `t.exp`, so their drift ledger and its test are retired (in git history).
- New tests pin the v2.10.04 behaviour with values measured from the C: `FileBoundaryHistoryTest`,
  `HeliocentricLightTimeTest`, `DeltaTModelsTest`, `CalcMessagesTest`, `AstroModelsStringTest` and
  `SweDataClearInPlaceTest`. File-backed tests skip when `external/swisseph/ephe` lacks the files.

### Known issues

- Upstream data. `dt[]` in `v2.10.04` has 69.2093 for 2023 where USNO's `deltat.data` has 69.2039,
  which looks like a transposition, and its 2026.0 entry is the prediction 69.05 where the measured
  value is 69.1099. The port keeps the C's values, as the transliteration freeze requires. A
  correction is being prepared for upstream.
- Upstream test harness, not the library, and the port's harness is unaffected: `suite_09_rise.c`
  declares `double dobs[5]` where the heliacal functions take `dobs[6]`, and `xxtret[10]`, which
  `swe_heliacal_pheno_ut` overruns. With gcc's stack protector, `setest` aborts at 9.2.9.
- See `docs/known-issues.md` for both, and for the closed `swed` entry.

## 2.10.3.3

- House cusp speeds are now analytic for every house system. `swe_houses_ex2` and
  `swe_houses_armc_ex2` (and the older entry points that forward to them) previously used a
  finite-difference interpolation that returned values in the millions of degrees per day near the
  Ascendant's horizon tangency (ARMC 90 and 270). That interpolation now runs only as a fallback
  when an analytic result is not finite. This is the eleven-patch house cusp-speed set that Timothy
  van der Ham reported to the Swiss Ephemeris mailing list in August 2026 and that Astrodienst
  applied upstream in commit `6c989727`; the port adopts it by re-transliterating every hunk of
  `swehouse.c`. Behaviour change for callers that read cusp or `ascmc` speeds. By system: `P` and
  `G` use the implicit function theorem; `K` uses the chain rule through the MC (the old value was
  up to 39% high at latitude 60); `O` takes cusps 11 and 12 from the MC speed instead of the
  Ascendant speed (a 34-38% shift); `L`, `S` and `Q` use the chain rule through the quadrant; `X`
  and `M` use a closed form, as do `F`, `B`, `U` and `Y`; `I` and `i` use a closed form with the
  Sun's declination held fixed (the omitted declination term reaches about 0.14 degrees per day at
  latitude 35 and 0.6 at latitude 52, and up to 18 degrees per day near the polar circle on a cusp
  moving about 6000 degrees per day); `W` and `N` speeds are 0, since those cusps stand still
  between sign ingresses.
- Sidereal house speeds now account for the ayanamsa's rate. In the traditional sidereal mode the
  ayanamsa rate is subtracted from cusp and `ascmc` speeds (not from the ARMC, `ascmc[2]`, and not
  from `W` and `N` cusps), where before the speeds were the tropical ones. Measured with
  `swetest -b1.1.2000 -ut12:00 -house13.4,52.5,P -speed -sid1 -p0`: the Ascendant is at
  27°43'54.4557 and moves at 600°43'39.3962 per day, against 600°43'39.5410 tropical, 0.1448"
  less; house 2 moves at 375°05'39.2577 per day. With whole-sign houses (`-house13.4,52.5,W`) every cusp
  speed is 0°0'0.0000 and the Ascendant still moves at 600°43'39.3962. The `SE_SIDBIT_ECL_T0` and
  `SE_SIDBIT_SSY_PLANE` modes subtract their auxiliary frame's rates instead (about 4e-5 degrees
  per day). The port's output equals the MSVC-built `swetest` on every house, Ascendant and MC
  line of these commands.
- `SEFLG_RADIANS`: house speeds are now converted to radians per day as well. They were left in
  degrees per day beside positions in radians.
- Three house position fixes, each with a narrow scope. `Y` (APC) now uses `sin(kv)` instead of
  the `dasc` round trip: within 1e-10 degrees of the equator, cusps 1 and 7 are now the Ascendant
  and Descendant (for example latitude 5e-11, ARMC 242: cusp 1 was 303.139 and is now 329.906);
  elsewhere only last digits change. `i` (Makransky Sunshine): `w == 180` exactly, and southern
  `w == 90` or `270`, are fixed, and no cusp of exactly 360 is returned. `G` (Gauquelin) in the
  polar Porphyry fallback: cusps 13 to 36 and their speeds are now 0 (the C left them
  uninitialised, and in sidereal mode the ayanamsa was subtracted from them). `swe_house_pos` is
  unchanged.
- `I` (Sunshine) declination error path: it now returns `ERR` with all output arrays zeroed. It
  left them untouched before, and through `swe_houses` at ancient dates the C dereferenced a NULL
  `serr`.
- ECL_T0 sidereal modes near the mode's epoch: the frame rate added above is taken on `tjde`'s side
  of the epoch `t0` when a central difference would straddle it (upstream `0c3eb56e`). In the
  `ECL_T0` modes (`SE_SIDBIT_ECL_T0`, and `SE_SIDM_J2000`, `SE_SIDM_J1900`, `SE_SIDM_B1950` and
  `SE_SIDM_GALALIGN_MARDYKS`, which force it) the cusps jump at `t0` by twice the nutation in
  longitude, as in every earlier release, and the first version of the frame-rate code
  (`6c989727`) differenced across that jump, putting every cusp and `ascmc` speed within 0.1 day
  of `t0` off by up to 0.04 degrees per day. That version was never part of a SwissEphNet release.
  Measured, for `-b1.1.2000 -ut12:00 -house13.4,52.5,P -speed -sid18` the Ascendant's speed is
  600°43'39.6898 against 600°43'39.5410 tropical (it would have been 600°41'20.2289 at
  `6c989727`): the 0.149" difference is the frame term and nothing else. The position jump at
  `t0` itself is unchanged. `SE_SIDBIT_SSY_PLANE` is not affected. The port equals the
  MSVC-built `swetest` at `43c9fe54` on this command.
- `swe_calc` on an asteroid (or other) ephemeris file at exactly the file's last date returned
  garbage. At `tjd == tfend`, which the range check allows, `get_new_segment()` took the index
  entry after the last segment, so the coefficients were wrong. It now uses the last segment
  (upstream `43c9fe54`). The port had the identical bug: Astraea (`se00005s.se1`, file range
  JD 2268932.5 to 2488932.5) at exactly JD 2488932.5 came out at longitude 126.0072799, latitude
  -22.1761351 and distance 3.648274082 au instead of 66.9437422, -3.5490403 and 3.288141871 (0.0001
  day earlier: 66.9436936). It shows only when `tfend` is the first date evaluated, because an
  earlier date in the last segment caches that segment. The new `AsteroidFileUpperLimitTest` (3
  cases) matches the MSVC-built C at `43c9fe54` bit for bit and fails on the previous `Sweph.cs`.
- `swe_houses_armc_ex2` no longer raises `SwissEph.OnTrace` messages. Upstream removed the three
  `#ifdef TRACE` blocks from `swehouse.c` (`6318ad4c`); the C emits them only when built with
  `-DTRACE`, but `TRACE` is defined by default in the .NET build, so the port's
  `swe_houses_armc_ex2` raised them. Other `OnTrace` sources are unchanged.
- `swe_get_planet_name` now looks numbered asteroids up in `seasnam.txt` by binary search
  (`seasnam_line_number`, `sweph.c:6954`; `seasnam_seek`, `sweph.c:6968`; upstream `be4aa5dd`),
  which is faster than the previous scan. For a sorted file the results are the same; for an
  unsorted file it falls back to the full scan. The new `SeasnamLookupTest` (19 cases) includes one, a
  misplaced line ahead of the sorted entries, that fails on the previous `Sweph.cs`.
- `Programs/SweTest`: `-astpos` now passes the ephemeris time `te` to `print_asteroids` (`swetest.c:1613`,
  upstream `509f9335`, the fix of a defect reported by this project); the port had passed `tjd`
  since 2.10.3.2. `swe_get_planet_name` is now called only when the name is printed (formats `P`
  and `Q`, horizontal lists, difference modes; `swetest.c:1689` and `:1701`, upstream
  `7821165a`). This affects the `SweTest` program, not the library API.
- Swiss Ephemeris Professional License: the contract PDF was updated on 9 November 2026 and now
  reads "Edition September 2026 update 9 Nov 2026"
  (https://github.com/aloistr/swisseph/blob/6c9897279736b76e81f0b0039d05283a9ba59dc5/doc/secont_e_2609.pdf).
  The one substantive change is a new clause 13, "Use of language bindings and ports": every
  binding or port must honour the dual licensing (available under the AGPL and under Astrodienst's
  professional license), authors of bindings and ports may charge no extra fees, and Astrodienst
  lists the bindings it knows meet this (php-sweph, pysweph, pyswisseph, swe-jni-lib and the node
  `swisseph` package). SwissEphNet is not in that list. The former clauses 13 to 15 (payment,
  transfer, place of court) are renumbered 14 to 16. Nothing else changed: the price (CHF 700),
  the 6-year duration and the API-services clause 3 are as in the September edition. SwissEphNet is
  already offered under both the AGPL-3.0 and the Swiss Ephemeris Professional License, as the
  README says, and this repository's `LICENSE` is unchanged. If you hold or plan to buy a
  Professional License, check the current contract with Astrodienst.
- The upstream pin moves from `94621aba` to `43c9fe54` (upstream master, 2026-11-16), which is
  `v2.10.3bfinal` (`f4dcd18e`) plus 25 commits; no newer tag exists. `SE_VERSION` is still
  `"2.10.03"`. `94621aba..43c9fe54` touches `swehouse.c`, `sweph.c`, `swetest.c`,
  `ephe/seasnam.txt`, `ephe/seorbel.txt`, `ephe/astlistn.md`, the contract PDF and `bin/swetest`
  (a prebuilt binary, nothing to port). The first three are ported above; the rest need no port.
  `Tools/CReference/build-c.ps1` needed no change to build the C reference at the new pin.
- Verification. Against the MSVC-built C at `43c9fe54` the port is bit-exact: the oracle grids
  (27,437 analytic rows and 3,376 file rows) produce dumps with identical SHA-256, the known-diff
  files are empty, and the `swetest` diff gate still shows only its 16 known `DIR_GLUE` rows. The
  characterization baseline was regenerated deliberately (entry 26 in the sidecar log,
  attributed to the upstream patches): houses changed in 11,333 of 25,200 case ids and
  houses-armc in 33,674 of 109,944; every other area is unchanged, including house-pos (31,528
  rows). Each of the 2,004 position-changed rows is reproduced by exactly one upstream patch alone
  (APC 420, Makransky 1,296, Gauquelin Porphyry fallback 288). `known-fail.tsv` grows from 1,423
  to 2,395 rows (`VALUE-MISMATCH` 664 to 1,636; `DATA-MISSING` 759 unchanged). The 972 new rows
  are all `setest` 6.8 (`swe_houses_ex2`) and 6.9 (`swe_houses_armc_ex2`): positions are unchanged
  on all 1,080 iterations and only speed fields differ from `t.exp` beyond its 1e-3 tolerance
  (9,120 fields). Per-row evidence was in `Tests/conformance/house-speed-drift-6c989727.tsv`
  (retired at v2.10.04, in git history), produced by `Tools/CReference/house-speed-texp-check.c` against the MSVC-built library: 970 rows
  are a correction (the C speed is the numerical derivative of its own positions, and `t.exp`'s is
  not), 2 are a step (6.8.285 and 6.8.645, whole-sign cusps at a sign ingress), and 0 are
  unexplained; `HouseSpeedDriftTests` (retired with it) tied the file one to one to `known-fail.tsv`. These are
  `t.exp` drift from an intentional upstream change, not port defects. 10,362 of 12,757
  iterations now pass (11,334 before). The oracle version classification moves 37 analytic cases
  from `AGREES-BOTH` to `TRACKS-2.10.03`. The commits after `6c989727` (`0c3eb56e`, `6318ad4c`,
  `43c9fe54`) needed no further baseline regeneration and left `known-fail.tsv` and the evidence
  file unchanged; the evidence reproduces byte for byte against the C at `43c9fe54`. The unit
  tests pass 354 of 354. `swe_dotnet_version()` returns `"2.10.03-net-0003"`. No API change.
- Documentation and test data from #47, with no code change: a `DATA-MISSING` triage file, XML
  documentation for the `SE_SIDM_*` constants, and `docs/sidereal-and-vedic.md`.

## 2.10.3.2

- `C.sprintf` no longer constructs its format-parsing `Regex` on every call; the constant pattern
  is now built once. `fixstar_cut_string` calls `sprintf`, so every `swe_fixstar*` call and every
  sidereal calculation under a star-based ayanamsa (for example `SE_SIDM_TRUE_CITRA`) paid for a
  fresh `Regex` each time. Measured on net10.0, Windows x64, Moshier: `swe_get_ayanamsa_ex_ut`
  went from about 13.6 to 10.7 µs and 16.0 to 6.0 KB per call, and `swe_calc_ut` for the Moon with
  `SEFLG_SIDEREAL | SEFLG_SPEED` from about 37.8 to 32.5 µs and 36.0 to 15.8 KB per call. Output
  is unchanged. Reported by sudhiramidala in #42.
- `read_const` now takes an ephemeris file's byte order from the file, as `sweph.c:4650-4658`
  does, instead of from the host (`BitConverter.IsLittleEndian`). The two only differ for a file
  whose byte order is not the machine's, which no shipped `.se1` file is, so no existing user sees
  a change; such a file previously failed with `Ephemeris file ... is damaged (4)`. A big-endian
  regression test was checked against the C reference. Reported by wwlcom-hub in #41, whose own
  failing case is still under investigation.
- `swe_heliacal_ut` threw `IndexOutOfRangeException` from `swe_azalt` for any
  `SE_HELFLAG_AVKIND_VR`, `SE_HELFLAG_AVKIND_PTO`, `SE_HELFLAG_AVKIND_MIN7` or
  `SE_HELFLAG_AVKIND_MIN9` flag. `heliacal_ut_arc_vis` declared `xaz` with two elements and
  `swe_azalt` writes three. In C that is a silent stack overrun; the port had copied the
  declaration faithfully, so it surfaced as an exception. Upstream fixed it after `v2.10.3bfinal`
  (`swehel.c:2215`, `double xaz[3]`, commit `94621aba`), and the port now has the same one-token
  change (`SweHel.cs:2217`). The new `HeliacalArcVisTest` (Venus heliacal rising, Moshier) matches
  the C at `94621aba` bit for bit on Windows x64, net8.0 and net10.0. Nothing covered these flags
  before.
- New public `swe_get_denum()`, plus the constants `SE3_DE_NUMBER` (441), `SE_FNAME_DE441` and
  `SE_FNAME_DFT3`, all from upstream (`sweph.c:240`, `swephexp.h`). `swe_get_denum` returns the DE
  number of the open planet file (`sepl*.se1`), or `SE_DE_NUMBER` (431) when none is open; it does
  not consult a JPL file. Nothing in the port reads the three constants; upstream declares them for
  its future 3.0. `SE_EPHE_PATH` is unchanged. The public API grows by these four members on every
  target framework.
- Behaviour change for callers that never call `swe_set_ephe_path`: on Linux and macOS the default
  ephemeris path is now `".:/home/ephe/"`, following `swephexp.h:406`, where it was
  `".:/users/ephe2/:/users/ephe/"`. Such a caller now probes `.` and `/home/ephe/` and no longer
  probes `/users/ephe2/` or `/users/ephe/`. The Windows default is unchanged. The same default
  is also used when `swe_set_ephe_path` is given null, `""`, or a path longer than 242 characters
  (`AS_MAXCH - 1 - 13`); a non-empty `SE_EPHE_PATH` environment variable of 242 characters or
  fewer still takes precedence over both. On Linux and macOS, an `IEphemerisFileProvider` in those
  cases now receives paths built from the new default components (`.`, `/home/ephe/`). Callers
  that set a usable path are not affected.
- `Programs/SweTest`: ported the `swetest.c` changes made after `v2.10.3bfinal`. New option
  `-astpos<DDD>[,orb]` lists named asteroids within `orb` of longitude `DDD` and reads
  `seasnam.txt`. `-pd`, `-pp`, `-ph` and `-pa` now append `e` when the argument contains an `e` anywhere
  (the C uses `strchr(spno, 'e')`), instead of dropping it. A
  `-p` list of 256 characters or more is cut to 255. `-sidudef` without a comma no longer
  dereferences NULL in the C; it is a no-op here. The help text is copied verbatim, including
  upstream's typos. This affects the `SweTest` program, not the library API.
- Swiss Ephemeris Professional License: Astrodienst has replaced the contract PDF with the
  "Edition September 2026" (`doc/secont_e_2609.pdf` in the upstream repository,
  https://github.com/aloistr/swisseph/blob/94621aba9f3ff9c32cb721eac0380c30dbb153fe/doc/secont_e_2609.pdf). Compared with the June
  2026 edition, the duration changes from 99 years to 6 years with renewal, a clause on API
  services for third parties is added (a licensee who provides an API service must state in its
  documentation that each third-party user needs their own license, and must inform Astrodienst of
  the identity and contact of each API service user), the right to distribute in compiled form is
  no longer listed, and license transfer is allowed. The price is unchanged. This repository's `LICENSE` is
  unchanged. If you hold or plan to buy a Professional License, check the current contract with
  Astrodienst.
- The upstream pin moves from `v2.10.3bfinal` (`f4dcd18e`) to upstream master at `94621aba`, which
  is `v2.10.3bfinal` plus 12 commits; no newer tag exists. `SE_VERSION` is still `"2.10.03"`. Of
  the 12, three changes are ported above (heliacal, `swe_get_denum` with its constants, and
  `swetest.c`), and the rest need no port: `readme.md` edits, the contract PDF, `swevents.c` (not
  ported), and 51 `sepl*.se3` files for the future 3.0, which 2.10 cannot read.
  `Tools/CReference/build-c.ps1`
  needed two changes to build the C reference at the new pin: it substitutes a constant for the
  variable-length array in `swetest.c`'s new `print_asteroids`, which cl.exe rejects, and its `fp`
  guard now looks only at file scope.
- `C.printf`'s `ReplaceMetaChars` now implements `\xhh` hex escapes, capped at two hex digits
  (#40). This is a behaviour change of a public method (`SwissEphNet.C` is public API):
  `C.ReplaceMetaChars(@"\x41")` used to return `"x41"` and now returns `"A"`. Nothing inside the
  library calls it. Per-call allocations were also removed from `FormatToDegreeMinuteSecond`
  (`SwissEph.Format.cs`), `Extensions/StringExtensions.cs` and `CPointer.ToArray()`, which now
  uses `Array.Copy` with `BaseIndex` clamped at both ends. Output is unchanged for these
  allocation changes.

## 2.10.3.1

- Full XML documentation (`<summary>`, `<param>`, `<returns>`) added for all 116 public `swe_*`
  methods in `SwissEphNet/SwissEph.swephexp.h.cs`, plus the `SEFLG_*`/`SE_*`/`SEMOD_*` constants
  they reference. Purely additive: no signature or behavior changed, and `SwissEphNet/CPort/` is
  untouched. IntelliSense now shows the same documentation a C caller gets from `swephexp.h`
  instead of nothing (`CS1591` is suppressed repo-wide, so this gap was previously silent).
- `Programs/SweTest`: fixed `insert_gap_string_for_tabs`'s `LEN_SOUT` bound. It now counts bytes
  the way the C's `strlen` does, using native single-byte semantics on Windows (the narrow-argv
  code page) and UTF-8 elsewhere, instead of counting UTF-16 characters. Accented `-g` gap values
  no longer stop tab substitution one byte early. This affects the `SweTest` program's console
  output, not the `SwissEphSharp` library API.
- `swe_houses_ex2` now re-reads `sid_data` after its `SE_SIDM_FAGAN_BRADLEY` fallback, matching
  the C's pointer semantics (`swehouse.c:221`). No input reachable through the public API changes
  value from this today; it closes an audited fidelity gap rather than fixing an observed bug.
- Test coverage: `BaselineMatrix` now exercises 7 more of the library's 107 public entry points
  (`swe_houses_ex2`, `swe_houses_armc_ex2`, `swe_get_ayanamsa_name`, `swe_calc_pctr`,
  `swe_lat_to_lmt`, `swe_lmt_to_lat`, `swe_get_current_file_data`), all 100% EXACT on both TFMs.
  A new `NutationTableFidelityTest` value-diffs the nutation coefficient table against upstream
  source instead of only checking its length.
- CI gained macOS legs on the baseline and conformance gates (report-only, since the baseline
  stays locked to Windows; see `Tools/BaselineGen/README.md`).
- No breaking changes. See `docs/known-issues.md` and `docs/compliance-2.10.04.md` for the
  verification detail behind this release.

## 2.10.3 (the first release published to nuget.org, under the SwissEphSharp package ID; entries from 2.8.0.2 down are the original project's, published under SwissEphNet)

- LICENSE CHANGE, read this before upgrading. 2.8.0.2 was distributed under the GNU General
  Public License version 2 or later. This release is under the Swiss Ephemeris dual license:
  the GNU Affero General Public License, or a Swiss Ephemeris Professional License bought from
  Astrodienst. The practical difference is the AGPL's network clause. Under GPL-2.0 you could
  run this library inside a web service and owe nobody source, because nothing was distributed.
  Under the AGPL, operating the service is itself the trigger, and users who reach it over a
  network can require the complete corresponding source of your whole service, not just this
  library. If that does not work for your project, the Professional License is the other arm.
  This follows Astrodienst's own relicensing of Swiss Ephemeris and applies to the C library
  just as much as to this port. LICENSE, agpl-3.0.txt and NOTICE ship at the package root.
- SE_VERSION now reports "2.10.03", matching the C library this port tracks (upstream tag
  v2.10.3bfinal). Every stage of the 2.10.03 delta has landed: the header/constants stage,
  swephlib.c, the ayanamsha machinery, sweph.c, swecl.c, swehouse.c and swetest.c.
- swe_houses, swe_houses_ex, swe_houses_armc, swe_house_pos and swe_house_name each gained an
  int hsys overload alongside the existing char hsys overload, matching upstream swephexp.h.
- New API surface: swe_houses_ex2, swe_houses_armc_ex2, swe_calc_pctr, swe_get_current_file_data.
- swe_lun_occult_when_glob and swe_lun_occult_when_loc each gained an Int32 backward overload, so
  SE_ECL_ONE_TRY can be OR-ed into that bitfield the way swephexp.h declares. The existing bool
  overload is unchanged and still binds; it can only pass 0 or 1, so it can never request the
  flag. Reflection that resolves either method by name alone now throws AmbiguousMatchException.
- OnLoadFile is gone; ephemeris files are read from disk by default through the new
  SwissEph.FileProvider (IEphemerisFileProvider).
- The assembly is now named SwissEphSharp (was SwissEphNet), so this package can coexist in one
  dependency graph with the original SwissEphNet 2.8.0.2 instead of silently colliding with it at
  build time and crashing at runtime. The namespace stays SwissEphNet.
- 2.10.3 is the only release that ships netstandard2.0, not the last of several. 2.8.0.2 shipped
  net40 and netstandard1.0 and never carried netstandard2.0, and net8.0 or later will be required
  from the next release on, so the window in which this library is reachable from .NET Framework
  is this one version. Consumers on .NET Framework 4.6.1+ can take 2.10.3 and should pin to it
  deliberately rather than expect it to persist. netstandard2.0 is a compatibility target,
  not a correctness one: measured on .NET Framework 4.8 and 4.6.2 against .NET 10, the same
  asset's swe_calc differs on 29 of 102 calls (34 bodies x 3 epochs; worst case SE_ADMETOS
  latitude speed, 2.28e-3 relative -- an absolute divergence of 1.4e-08 deg/day against a base
  value of about -6.1e-06), while net8.0 and net10.0 agree on all 102. The cause is .NET
  Framework's less accurate Math.Sin/Math.Cos/Math.Tan near quarter-turn boundaries, not this
  port; see the "V:2.10.3" section of README.md for the full measurement.
- Numerous port bug fixes surfaced by the 2.10.03 work, several of them upstream C bugs Astrodienst
  fixed between 2.08 and 2.10.03 that this port now also carries: swe_nod_aps/swe_nod_aps_ut
  returning all-zero nodes and apsides, an obliquity read that affected every SEFLG_SWIEPH
  position, eclipse magnitude and obscuration off by a factor of 100, and the swe_house_pos buffer
  that was one element short for Gauquelin houses. See docs/compliance-2.10.04.md for the
  verification numbers behind this release and docs/known-issues.md and the "V:2.10.3" section
  of README.md for the full list.
- You are also inheriting everything in the 2.8.1.0 entry below, even though no such package was
  ever published. On nuget.org the step is 2.8.0.2 straight to this release, so those changes
  arrive with it: DIR_GLUE went from '\' to '/' on every platform, which changes the asteroid
  file names a file provider is asked for; DefaultEncoding became UTF-8 explicitly rather than
  falling back to it when Windows-1252 could not be resolved; and C.strcmp, strncmp, strstr and
  strchr became ordinal instead of culture-sensitive, which affects fixed-star name search and
  sort under a non-invariant culture. Read that entry as part of this one.

## 2.8.1.0 (not published to nuget.org; source-only distribution)

- Package ID renamed from SwissEphNet to SwissEphSharp. SwissEphNet on nuget.org belongs to the
  upstream author's own release and this project cannot publish under it. At this release the
  assembly name and namespace were unaffected: both stayed SwissEphNet, so the only change a
  consumer needed to make was the PackageReference itself. The assembly was renamed too, later,
  at 2.10.3 above -- see that entry and the "Package name" section of README.md.
- Retarget the library to netstandard2.0;net8.0;net10.0, dropping net40 and netstandard1.0.
- Relicense from GPL-2.0-or-later to the Swiss Ephemeris 2.10.3 dual license: AGPL-3.0
  or a Swiss Ephemeris Professional License from Astrodienst. See LICENSE and NOTICE.
- Fix SwissEph.DIR_GLUE: was hardcoded to '\\' on every platform, now '/' per the upstream C
  source. Asteroid file names passed to OnLoadFile change accordingly, e.g. "ast4/se04179.se1"
  instead of "ast4\se04179.se1".
- Fix SwissEph.DefaultEncoding: was falling back to UTF-8 silently whenever Windows-1252 could
  not be resolved (missing System.Text.Encoding.CodePages reference); UTF-8 is now the explicit,
  deliberate default, matching the actual encoding of the ephemeris data files.
  You can still override it via the static SwissEphNet.SwissEph.DefaultEncoding property.
- Fix C.strcmp/strncmp/strstr/strchr: were culture-sensitive (string.Compare/IndexOf), now
  ordinal, matching C's byte-wise semantics. Affects fixed-star name search/sort under
  non-invariant cultures.
- Several additional port bug fixes with no signature change: swe_fixstar multi-word star names
  and an off-by-one in search-name formatting, a missing ref-parameter mutation and missing guard
  in the heliacal Moon branch, swe_set_astro_models throwing on short input, C.atoi sign handling,
  `CPointer<T>.operator !=` always returning false, and a netstandard2.0-only infinite
  recursion in a string-extension method. See NOTICE and docs/known-issues.md for details.

## 2.8.0.2

- Fix the #41 issue

## 2.8.0.1

- Update to version 2.08 of SwissEphemeris
