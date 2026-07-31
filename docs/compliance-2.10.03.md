# Compliance record: Swiss Ephemeris 2.10.03

This records what has actually been measured about this port's agreement with Astrodienst's
own Swiss Ephemeris C library, version 2.10.03 (upstream tag `v2.10.3final`), rather than
asserting it. Every number below comes from a gate in this repository or from a measurement run
during the version-bump work that closed the 2.10.03 port (see `docs/known-issues.md`'s
`SE_VERSION` entry). Where something has not been measured, that is stated rather than estimated.

## Three instruments, three different claims

This repository runs three separate checks against the C library, and they prove three different
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
  gate) proves the strongest and narrowest of the three claims: for every input in its two grids,
  the port and Astrodienst's own C, compiled from the same upstream source and run against the
  same ephemeris files, compute the identical bits. Not "close within tolerance" -- identical.

None of the three substitutes for either of the others. A porter who only reads the bit-exact
table below and skips the correctness oracle's known-fail count would conclude the port is at
full parity with 2.10.03; it is not, and the next section says by how much.

## 1. Bit-exact oracle

| Platform | C reference | Result |
|---|---|---|
| Windows x64 | MSVC 19.51.36248, `/O2 /fp:precise /MD` | 17,064 of 17,064 oracle rows bit-identical |
| Linux x64, Ubuntu 24.04.4 | gcc 13.3.0, `-O2` | 17,064 of 17,064 oracle rows bit-identical |

17,064 is the sum of the two grids: `Tools/OracleGrid/grid-analytic.tsv` (14,820 rows,
`SEFLG_MOSEPH swe_calc`/`swe_calc_ut` plus `swe_houses`/`swe_houses_armc`, opening no ephemeris
file) and `Tools/OracleGrid/grid-files.tsv` (2,244 rows, `SEFLG_SWIEPH swe_calc`/`swe_calc_ut`,
the `swe_fixstar` family, and `swe_get_planet_name`, reading the shipped `.se1`/`sefstars.txt`
files). Both `Tests/oracle/known-diff.tsv` and `Tests/oracle/known-diff-files.tsv` are empty:
there is no recorded exception on either grid, on either platform.

The Linux run reused both grids and both drivers unchanged; `sedump.c` needed only
`-DSWISSEPH_HAS_CROSSING=1` and an explicit source list to build there, the same macro the
Windows build already sets (`scripts/run-oracle-dump.ps1`). Both grids produced exactly 14,820
and 2,244 rows on Linux, matching Windows row for row, and every row matched Astrodienst's own C
bit for bit, matching the Windows result. **macOS has not been measured.**

Of the 17,064 oracle rows, 16,456 exercise the default nutation path rather than opting out via
`SEFLG_NONUT`: 14,212 of the analytic grid's 14,820 rows (608 opt out) and all 2,244 files-grid
rows (0 opt out). All 16,456 are among the bit-identical rows above -- see "What this record does
not cover" for what that does and does not establish about the nutation coefficient tables
themselves.

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

As of this record, `known-fail.tsv` carries 1,427 rows, so 11,330 of 12,757 iterations pass
(88.8%). That is down from 4,382 rows when the oracle was first wired up (commit `835a6c6`) --
the 2.10.03 port has closed 2,955 of the iterations it started 4,382 behind on.

The 1,427 remaining rows split into two categories, with none in `ERROR`, `UNREPRODUCIBLE`, or
`NOT-IMPLEMENTED` at present:

| Category | Rows | What it means |
|---|---|---|
| `VALUE-MISMATCH` | 714 | The port ran and produced an answer outside `t.fix` tolerance -- the actual porting work queue. |
| `DATA-MISSING` | 713 | A required data file (a JPL DE ephemeris, a pre-1200/post-2399 era `.se1` file, `ephe/sat/`) is not shipped by this repo, so the iteration was not run at all. |

**157 of the 713 `DATA-MISSING` rows are `SEFLG_SWIEPH` calls for a date outside the era this
repo's shipped core ephemeris files cover** (the `sepl`/`semo`/`seas_N.se1` and their BCE `_N.se1`
counterparts, roughly years 1200-2399). These stay `DATA-MISSING` by design: shipping the full
era file set would add well over 100 MB to a repository whose vendored C source is already kept
sparse for the same reason (`CONTRIBUTING.md`, "The upstream C is vendored at
`external/swisseph`"). This is a data-availability decision, not a gap in what has been checked --
the next paragraph gives the actual number for what these 157 rows would show if the data were
present.

**A one-time probe (`Tests/conformance/regenerations.log`, Phase 6) widened the ephemeris checkout
to the full era file set, `ephe/sat/`, and a JPL DE431 file, and re-ran the full 12,757-iteration
corpus with all three opt-in flags set, without changing `known-fail.tsv`.** Of the 713
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

**The `reason` column in `known-fail.tsv` is documentation, not part of what the gate checks.**
`scripts/regenerate-known-fail.ps1` and the gate itself compare only `category`; the free-text
`reason` can drift from the exact current failure without the gate noticing (`CONTRIBUTING.md`,
"Correctness oracle known-fail list"). Treat the numbers above, sourced from the category column
and from the Phase 6 probe's direct run, as load-bearing; treat individual `reason` strings as
best-effort commentary.

## What this record does not cover

**macOS is entirely unmeasured**, on all three instruments. Every number above is Windows or
Linux.

**The nutation coefficient tables in `SwissEphNet/CPort/SweNut200a.h.cs` have never been
independently diffed, value by value, against `external/swisseph/swenut2000a.h`.** The array
lengths match exactly on both sides (`NLS` 678, `NLS_2000B` 77, `NPL` 687), which rules out gross
truncation, but that is a length check, not a value check, and no commit or script in this
repository's history performs the latter. The indirect evidence is real but is not the same
claim: 16,456 of the 17,064 bit-exact oracle rows exercise the default (non-`SEFLG_NONUT`)
nutation path and match Astrodienst's own C bit for bit, which a wrong coefficient of any
consequence would be very unlikely to survive. That is strong corroboration through the oracle,
not a direct audit of the table itself.

**Eleven call sites in `SwissEphNet/CPort` transliterate a C fixed-size stack buffer
(`char buf[N]`) as an unbounded C# `string`.** The C's buffer-boundary behavior (truncation,
overflow) is consequently not reproduced at these sites: `SwephLib.cs:4682`, `:4893`, `:4926`;
`SweHel.cs:327`, `:2493`; `Sweph.cs:420`, `:7408`, `:8312`, `:8904`, `:8030-8031`, `:8128-8129`,
`:9240-9241` (the last three are TLS-static pairs, `slast_stardata`/`slast_starname` sharing one
site each). None of these has produced an observed divergence in the baseline, the bit-exact
oracle, or the correctness oracle to date -- the C# `string`'s lack of a bound has simply never
been exercised past the C's own buffer size in any input any of the three instruments generates.
That is an absence of a triggering input, not a proof the sites are safe against one.

**The `reason` column caveat above** applies to every number in this document that is sourced
from `known-fail.tsv`'s category counts rather than a fresh run: category is gated and reliable,
free text is not.

**The bit-exact oracle's house-code coverage is narrower than "14,820 of 14,820 match" suggests.**
Both drivers call only the six-argument `swe_houses`/`swe_houses_armc` forms, so `swe_houses_ex2`,
`swe_houses_armc_ex2`, `swe_house_pos`, `swe_house_name`, every house-code `serr` path, and every
speed derivative (`AscDash` and the other nine speed fields) have no bit-exact coverage at all --
some of that is partly covered by the correctness oracle instead (house systems `'P'`/`'W'`/`'K'`
only, for speeds), and house system `'J'` (Savard-A) has no external validation on either
instrument, anywhere. See `docs/known-issues.md`, "What the oracle grids do not cover in the house
code," for the full accounting.

## Sources

`docs/known-issues.md` (`SE_VERSION` entry and the DIR_GLUE/`niter_max`/`free_planets` entries
this compliance work built on), `CONTRIBUTING.md` ("Correctness oracle known-fail list",
"The two gates disagree on purpose, not by accident"), `Tools/BaselineGen/README.md` ("Platform
lock", "Matrix coverage"), `Tests/conformance/regenerations.log` (the Phase 6 DATA-MISSING probe
entry and its correction), `Tests/conformance/known-fail.tsv`, `Tools/OracleGrid/grid-analytic.tsv`
and `grid-files.tsv`, and the `scripts/verify-oracle.ps1` / `scripts/run-oracle-dump.ps1` runs
this record's oracle numbers were taken from directly.
