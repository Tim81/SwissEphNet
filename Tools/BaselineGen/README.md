# BaselineGen / BaselineMatrix / BaselineVerify

Characterization ("golden master") harness for SwissEphNet. It runs a fixed matrix
of Swiss Ephemeris calls that need no ephemeris data files (Moshier and analytic
paths only) and freezes their output, so a later PR can prove it did not change
numerical behavior.

Four projects, one shared matrix:

- **`BaselineMatrix`** -- the actual matrix code (Houses.cs, Calc.cs, Ayanamsa.cs,
  etc.) and the `UseReferencePackage` switch that decides how it resolves
  `SwissEphNet`. Nothing in here is run directly.
- **`BaselineGen`** -- a console app that runs the matrix and writes one TSV file
  per area, plus an environment sidecar, to a directory you give it.
- **`BaselineVerify`** -- a console app that runs the matrix in local mode and
  compares it against the files committed under `Tests/baseline/`. It refuses to
  build at all with `UseReferencePackage=true` (see `BaselineVerify.csproj`) --
  without that guard, verification could compare the reference package against a
  baseline generated from that same package and pass regardless of what changed.
- **`BaselineVerify.Tests`** -- xUnit tests for `Comparer`, `Waivers`, and `Verdict`
  (the PASS/FAIL policy itself, pulled out of `Program.cs`'s top-level statements
  specifically so it is reachable from tests): exact/tolerance/beyond-tolerance
  comparisons at and near the threshold, the relative-vs-absolute crossover, arity
  changes, missing rows, glob anchoring, rejection of catch-all waivers, the waived
  and matched-breadth fraction caps on both sides of 5%, both stale-waiver
  conditions, and all four assembly-identity-check outcomes. The gate's own logic
  needs coverage independent of whatever the matrix happens to produce. Run with
  `dotnet test Tools/BaselineVerify.Tests -c Release`; CI runs this before every
  verify.

These four, plus `SwissEphNet.csproj` itself, live in `Tools/BaselineTools.slnx`.
They are **not** part of `SwissEphNet.sln` -- see "Why a separate solution" below.

## The two modes

`BaselineMatrix.csproj` resolves `SwissEphNet` one of two ways, chosen by the
`UseReferencePackage` MSBuild property:

| Mode | Command | Resolves SwissEphNet from |
|---|---|---|
| Reference | `-p:UseReferencePackage=true` | NuGet package `SwissEphNet` 2.8.0.2 |
| Local (default) | *(property omitted)* | `ProjectReference` to `SwissEphNet/SwissEphNet.csproj` |

`BaselineGen` can be built in either mode -- that is how the golden files were
produced and how they would be regenerated if the reference version ever changes.
`BaselineVerify` is always local mode; verification means "does the code in this
repo right now match the frozen reference", so there would be no reason to ever
pass `UseReferencePackage=true` to it.

**Comparisons must always run `-c Release`.** The committed baseline was generated
in Release, and Debug/Release can produce different floating-point results for the
same source (different JIT optimization, inlining, and codegen choices affect how
Math.Sin/Cos/etc. round intermediate values). Comparing a Debug run against a
Release-generated baseline risks spurious tolerance failures that have nothing to
do with an actual behavior change.

**`<InvariantGlobalization>true</InvariantGlobalization>` in `BaselineMatrix.csproj`
is load-bearing, not incidental.** `%f`-style formatting inside CPort's own
`C.printf`/`sprintf` (which builds the text embedded in `serr` messages) goes
through `String.Format(numberFormat, value)` with no explicit `IFormatProvider`,
so it uses `CultureInfo.CurrentCulture` by default (`SwissEphNet/Tools/C.printf.cs`,
`FormatNumber`/`FormatHex`). Confirmed on this machine directly: `CurrentCulture` is
`nl-NL`, whose decimal separator is a comma (`3.14` formats as `3,14`). Every
double the harness itself records already goes through
`.ToString("R", CultureInfo.InvariantCulture)` regardless of this setting (see
`Format.D`), but a number embedded inside a `serr` string by CPort's own formatting
does not -- without `InvariantGlobalization`, that text would render with commas
on a machine like this one, and the baseline would be silently machine-specific in
a way none of the other checks would catch: a `serr` comparison would pass or fail
based on whoever's regional settings generated the file, not on any actual
behavior change. Do not remove this property later without re-checking that.

## Regenerating the golden files

Only needed when the reference package version changes (i.e., essentially never,
until the harness itself is retargeted at a newer frozen release). Run:

```powershell
./scripts/regenerate-baseline.ps1
```

This builds `BaselineGen` in reference mode, generates twice into separate temp
directories, diffs them to confirm reproducibility, and then copies the result
into `Tests/baseline/` for you to review and commit. See that script for the
exact `dotnet build`/`dotnet run` commands if you want to run them by hand.

## Verifying current code against the baseline

```powershell
./scripts/verify-baseline.ps1
```

This builds `BaselineVerify` in Release and runs it, which builds `BaselineMatrix`
in local mode, runs every area, and compares each against
`Tests/baseline/baseline-<area>.tsv`:

- Numeric fields are compared with `max(1e-12 absolute, 1e-13 relative)` (CPort
  calls `Math.Sin`/`Cos`/`Tan`/`Pow`/`Asin`/`Acos`/`Atan`/`Atan2`/`Log`/`Exp` several
  hundred times, and .NET does not guarantee bit-identical transcendental results
  across OS, architecture, or runtime version -- only `Math.Sqrt` is exempt from
  that). The absolute floor matters because a large share of the matrix's numeric
  fields are exactly zero, where a purely relative tolerance is meaningless.
- On top of that, a pair of values is also treated as within tolerance if their
  *angular* distance (`min(|a-b|, 360-|a-b|)`) is within the same threshold, for
  values that plausibly represent a degree in `[0, 360]` and land within `1e-9` of
  the `0`/`360` wrap point (`Comparer.EffectiveAbsoluteDiff`). This exists because
  one platform can normalize the same angle to `0` and another to
  `359.99999999999994` -- a raw difference of ~360 with a true angular difference
  of 5.68e-14 degrees. It is not applied to every field: a Julian Day or a distance
  is never near a 0/360 boundary, so the check never activates for those, and for
  two values that are not straddling the wrap point it is a no-op (see the doc
  comment on `EffectiveAbsoluteDiff` for why it cannot loosen an unrelated
  comparison). See "Platform lock" below for the measured impact.
- Any field that parses as a number -- including plain integers like return codes
  and iflag values -- goes through that same numeric comparison; a field that does
  not parse as a number (strings, the `EXCEPTION` marker, `serr` text) must match
  exactly. This makes no practical difference for integers (a real integer
  difference is always many orders of magnitude past the tolerance), but it is the
  numeric path, not a separate exact-integer path, that actually runs for them.
- Existence -- a case id present on only one side -- is checked before any waiver
  and can never be waived; a waiver only ever excuses a value difference on a row
  both sides agree exists.
- A row is reported as PASS (exact or within tolerance), FAIL (exact-match mismatch
  on a non-numeric field, a numeric field beyond tolerance, an arity change, or a
  row missing from one side), or WAIVED (its case id matches a glob in
  `Tools/BaselineVerify/waivers.tsv`, which also records the PR it belongs to and
  the reason -- see that file's format, glob syntax, and current entries). A case id
  can match more than one waiver; every matching waiver gets credit for the row, not
  just the first one found.
- Every waiver is checked for staleness at the end of the run: it fails if it
  matched zero rows, or if every row it matched passed on its own (exact or within
  tolerance) and the waiver never actually excused a failure.
- An area fails outright if more than 5% of its rows had a failure excused by a
  waiver (`WaivedFraction`), **or** if more than 5% of its rows were touched by a
  waiver at all regardless of outcome (`MatchedFraction`) -- the second cap exists
  because a broad glob is a risk the moment it matches a lot of rows, not only once
  one of those rows starts failing.
- The committed environment sidecar (`Tests/baseline/baseline-<version>.env.txt`,
  written by whichever run produced the reference file) is compared against the
  currently running build's `ModuleVersionId` and DLL SHA-256. If either matches,
  the run fails: local mode should never be compiling to the same bytes as the
  reference package.

Exit code is 0 only if every area has zero FAIL, zero ONLY-LOCAL and zero
ONLY-REFERENCE rows, no stale waivers, no area over either 5% cap, and the
assembly-identity check does not detect a suspicious match.

## Platform lock

**The committed baseline is Windows-specific, and this gate is deliberately locked
to Windows.** It is not portable to Linux/macOS by construction, and that is a
choice, not an oversight -- see the measurements below for why.

A full Windows-vs-Linux comparison (same source, same commit, `.NET SDK 10.0.302`
both sides, Linux via `mcr.microsoft.com/dotnet/sdk:10.0` on Ubuntu 24.04) found:

- **3,443,058** numeric fields compared; **47,052** (1.37%) differ at all between
  platforms.
- Of those 47,052, only **108** are genuine angle-wraparound (raw difference > 180
  degrees, i.e. one side normalized to 0 and the other to something just under 360)
  -- and the wraparound allowance resolves all 108 of them, exactly. (An earlier
  pass at this classification mislabeled 2,529 additional fields as "wraparound"
  by computing `min(d, |360-d|)`, which is a no-op and just returns `d` back for
  any already-small difference; those 2,529 are small (1e-12 to 1e-9) numeric
  divergences with nothing to do with angle wraparound, already counted below.)
  Row-level, resolving those 108 fields cleared **108** rows, all in `houses-armc`
  (192 -> 84 FAIL rows); `calc` (1,368), `pheno` (140) and `houses` (1) were
  unaffected, since none of their divergences are wraparound-shaped. Whole-run
  Linux FAIL count: 1,701 -> 1,593 rows.
- **3,346** fields are still beyond the shipped tolerance
  (`max(1e-12 abs, 1e-13 rel)`) after the wraparound fix. The rest (43,598,
  92.66% of the 47,052 that differ at all) are ULP-level noise the tolerance
  already absorbs -- this validates the tolerance design against real data, not
  just synthetic boundary tests.
- The remaining 3,346 beyond-tolerance fields split into differentiation-noise
  fields in `calc`/`pheno` (SPEED values, expected but not fully explained) and
  one confirmed numerical-stability bug in `swe_houses_armc` hsys `'Y'`. See
  `docs/known-issues.md` for both, in detail.

**What a looser, cross-platform-passing tolerance would actually cost** (measured
directly, angle-awareness applied at every level, so these are not estimates):

| Absolute floor | Relative | Fields still beyond tolerance | Worst areas |
|---|---|---|---|
| 1e-12 (shipped) | 1e-13 (shipped) | 3,346 | calc 2,973 / houses-armc 210 / pheno 162 |
| 1e-11 | 1e-13 | 2,328 | calc 2,078 / houses-armc 162 / pheno 88 |
| 1e-10 | 1e-13 | 1,215 | calc 1,152 / pheno 62 / houses-armc 1 |
| 1e-09 | 1e-13 | 817 | calc 777 / pheno 39 / houses-armc 1 |
| 1e-09 | 1e-09 | 407 | calc 406 / houses-armc 1 |
| 1e-08 | 1e-08 | 97 | calc 96 / houses-armc 1 |

Two things stand out. First, raising just the absolute floor to 1e-9 degrees
(3.6e-6 arcsec, still far below anything meaningful for an ephemeris) would cut
failures from 3,346 to 817 on its own -- most of the remaining divergence is
absolute-scale noise near zero, not something that needs a looser *relative*
tolerance at all. Second, exactly one `houses-armc` field survives every level in
this table, including the loosest (1e-8 absolute / 1e-8 relative): the hsys `'Y'`
case at 26.6 degrees (see `docs/known-issues.md`). Surviving every threshold up to
1e-8 -- eight orders of magnitude looser than what ships -- is itself evidence
this is a real algorithmic divergence, not accumulated floating-point noise.

**Why the fix was a platform lock, not a looser shipped tolerance:** even 1e-9
absolute (which would clear most of the noise) still leaves 817 fields beyond
tolerance, concentrated in `calc`'s SPEED fields -- a real regression of similar
magnitude in those fields would look identical to this drift under a purely
numeric comparison. The only honest fix is to not ask the comparison to do a job
it cannot do, which is why `Tests/baseline/` is Windows-only and CI's Windows job
(`verify-baseline`) is the one that gates merges. The shipped tolerance stays at
`1e-12`/`1e-13`; 1e-9 absolute is recorded here as the value a future *opt-in*
cross-platform profile would use if one is ever built, not as something wired up
today.

The Linux job (`verify-baseline-linux`) exists purely to keep tracking this drift
over time -- see the next section. Run it yourself with
`./scripts/verify-baseline.ps1 -ReportOnly` for current numbers instead of the
ones above (from a Linux shell, or via Docker:
`docker run --rm -v "<repo>:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash -c
'dotnet run --project Tools/BaselineVerify/BaselineVerify.csproj -c Release --
/src/Tests/baseline --report-only'`).

## Why a separate solution

`BaselineMatrix`/`BaselineGen`/`BaselineVerify`/`BaselineVerify.Tests` target
`net10.0`. AppVeyor CI (`appveyor.yml`) builds `SwissEphNet.sln` on the
`Visual Studio 2017` image, whose bundled SDK does not know `net10.0` -- adding
these projects to `SwissEphNet.sln` would break `dotnet restore .\SwissEphNet.sln`
in CI before the build step even starts. `Tools/BaselineTools.slnx` keeps them
buildable and discoverable locally without going anywhere near the CI image or the
library's own target frameworks. `Tools/global.json` pins the SDK to the `10.0`
major.minor line for anything built from under `Tools/`; SDK resolution walks up
from a project's own directory, not from the invoking shell's working directory, so
this has no effect on `SwissEphNet.sln` at the repo root.

`.github/workflows/baseline.yml` is what actually runs the gate: `dotnet test
Tools/BaselineVerify.Tests` followed by `scripts/verify-baseline.ps1`, on
`windows-latest`, on every push and PR -- a separate CI system from AppVeyor, so
neither one's SDK requirements constrain the other. A second, `continue-on-error`
job runs the unit tests plus `scripts/verify-baseline.ps1 -ReportOnly` on
`ubuntu-latest`: this never asserts PASS/FAIL (see "Platform lock" above for why),
it only prints the divergence distribution, so continue-on-error is there purely as
a backstop against the test suite itself breaking on Linux, not because the report
step can fail.

## Matrix coverage

See the doc comments at the top of each file under `Tools/BaselineMatrix/` for what
each area covers. Areas and their files:

| Area | File | Covers |
|---|---|---|
| houses-armc | `Houses.cs` | `swe_houses_armc` (dense sweep), the saved_sundec hazard, and a small deliberate exception to the fresh-instance rule (see below) |
| houses | `HousesEx.cs` | `swe_houses`, `swe_houses_ex` (including `SEFLG_SIDEREAL` with `swe_set_sid_mode`, plain and with `SE_SIDBIT_ECL_T0`/`SE_SIDBIT_SSY_PLANE`) |
| house-pos | `HousePos.cs` | `swe_house_pos`, `swe_house_name` |
| calc | `Calc.cs` | `swe_calc`, `swe_calc_ut`, including a topocentric pass with and without `SEFLG_SPEED` |
| pheno | `Pheno.cs` | `swe_pheno`, `swe_pheno_ut`, including a topocentric pass (`attr[5]`) with and without `SEFLG_SPEED` |
| nodaps | `NodAps.cs` | `swe_nod_aps`, `swe_nod_aps_ut` |
| ayanamsa | `Ayanamsa.cs` | `swe_get_ayanamsa[_ut]`, `_ex[_ut]`, `SE_SIDM_USER`, SIDBIT flags |
| datetime | `DateTime_.cs` | date/time conversions, Delta-T, tidal acceleration, `swe_jdut1_to_utc`, `swe_utc_time_zone` |
| coord | `CoordHelpers.cs` | `swe_cotrans[_sp]`, `swe_azalt[_rev]` |
| format | `FormatHelpers.cs` | `swe_split_deg`, `swe_cs2*str`, norm/midpoint helpers, and their radian/centisecond siblings (`swe_radnorm`, `swe_difrad2n`, `swe_difcsn`, `swe_difcs2n`, `swe_csroundsec`, `swe_d2l`, `swe_day_of_week`) |
| misc | `Misc.cs` | `swe_get_planet_name`, `swe_version` |

Every row uses a brand new `SwissEph` instance, with one deliberate, explicitly-named
exception: `Houses.AddStatefulPairRows` shares a single instance across two ordered
calls, specifically to exercise `swe_houses_armc`'s hidden `saved_sundec` field (a
C `static` emulated as a C# instance field) in the one state it actually matters --
a real declination stored by one call, consumed by a sentinel call right after it on
the same instance. Every other row uses a fresh instance, since reusing one there
would make hsys `'I'`/`'i'` depend on call order and the baseline would not be
reproducible.
