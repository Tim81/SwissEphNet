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
  needs coverage independent of whatever the matrix happens to produce. Every one
  of those is a decision function, unit tested directly; `Program.cs` itself --
  the composition of those functions into an exit code -- is untested by every one
  of them, since none actually runs the process. `ProgramEndToEndTests` is the one
  exception: it copies the committed baseline, corrupts one area's expected row
  count in the copy, runs the real, compiled `BaselineVerify.dll` against it as a
  subprocess, and asserts the process exits nonzero -- proof the wiring itself
  (`Program.cs`'s own `&&`s and `overallExitCode = 1` assignments) is load-bearing,
  not just the functions it calls. Run with
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

## Why net8.0 and net10.0

`BaselineMatrix` and `BaselineVerify` both multi-target `net8.0;net10.0` (not
just `net10.0`). SwissEphNet itself ships three assets --
`netstandard2.0`, `net8.0`, `net10.0` -- and there is no guarantee they behave
identically. This is not theoretical: `C.ToUnsigned` (called from the
library's own printf path, `SwissEphNet/Tools/C.printf.cs`, not just from
tests) converts an out-of-range negative float/double to an unsigned integral
type, which the C# language spec leaves unspecified, and its concrete result
changed between the .NET 8 and .NET 10 JIT (wrapping vs. saturating to zero --
confirmed by running the same test binary against both runtimes). A gate that
only ever built `BaselineMatrix`/`BaselineVerify` as `net10.0` would only ever
exercise one of SwissEphNet's three shipped assets and could stay green while
the `net8.0` asset silently diverged.

`scripts/verify-baseline.ps1` builds `BaselineVerify` once, then runs it once
per TFM (`dotnet run -f net8.0`, then `-f net10.0`), reporting each as its own
section. Both must pass for the script to exit 0.

`netstandard2.0` is deliberately not exercised this way: `BaselineVerify` is a
`dotnet run` console app, and any modern host resolves `net8.0` or `net10.0`
in preference to a `netstandard2.0` reference, so there is no way to make it
actually execute the `netstandard2.0` asset without a separate, older host
leg (e.g. .NET Framework) to run it under. That would cost more to build and
maintain right now than the coverage is worth, so it is left as a known,
noted gap rather than solved here.

## Regenerating the golden files

### Reference mode (default)

Only needed when the reference package version changes (i.e., essentially never,
until the harness itself is retargeted at a newer frozen release). Run:

```powershell
./scripts/regenerate-baseline.ps1 -ExpectedScope '<glob>','<glob>...'
```

This builds `BaselineGen` in reference mode, generates twice into separate temp
directories, diffs them to confirm reproducibility, checks the result against
`-ExpectedScope` (see below), and then copies the result (both the per-area
TSVs and the environment sidecar, replacing it wholesale) into
`Tests/baseline/` for you to review and commit. See that script for the exact
`dotnet build`/`dotnet run` commands if you want to run them by hand.

### Local mode -- when it is legitimate

```powershell
./scripts/regenerate-baseline.ps1 -FromLocal -DeviationNote "<what changed and why>" -ExpectedScope '<glob>','<glob>...'
```

This builds `BaselineGen` against the in-repo `SwissEphNet` project instead of
the reference package, so the committed baseline can track a real change in
local code.

**The gate failing is the mechanism working, not a problem to make go away.**
`scripts/verify-baseline.ps1` failing means the matrix's frozen output no
longer matches what the current code produces. There are exactly two honest
responses to that:

1. The code has a bug that made it diverge unintentionally -- fix the code,
   not the baseline. This is the common case, and it needs no special
   regeneration mode at all: once the code is fixed, `verify-baseline.ps1`
   passes again against the *existing* baseline, because the code now matches
   what was always expected.
2. The code changed on purpose, in a way that is supposed to alter observable
   behavior, and the baseline needs to start reflecting the new, intended
   behavior from here on. This is what `-FromLocal` is for, and only this.

A red gate is never, by itself, a reason to run `-FromLocal` -- that would
turn the gate into a formality that always passes, which defeats the entire
point of freezing behavior in the first place. Before using it, you should
already be able to explain, precisely, which rows will change and why, the
same way you would explain any other reviewed code change. If you cannot
explain a FAIL row before regenerating, you are not ready to regenerate it --
go find out why it FAILs first.

PR #4 (`fix/known-library-bugs`, the `DIR_GLUE` mis-transliteration fix; see
`docs/known-issues.md`) is the worked example: fixing `CPort/Sweph.cs:2634` and
`SwissEph.DIR_GLUE` changed a `serr` diagnostic string's path separator from
`[ephe]\` to `[ephe]/` in exactly 207 rows (192 `ayanamsa`, 15 `datetime`), and
nothing else -- confirmed at the time by dumping the *full*, non-truncated
failure list by hand (the console output truncates at 50 per area and shows
only the first differing field) and checking that every one of the 207 rows
differs from the committed baseline in only that one substring, with zero
numeric fields touched. Only once that was established did `-FromLocal` get
used, with a `-DeviationNote` describing exactly this. That manual dump does
not scale past a few hundred rows; `BaselineVerify --dump-failures <path>`
(see "Verifying current code against the baseline" below) now does the same
thing directly -- every non-exact row, every differing field (not just the
first), unlimited -- so the next review does not have to improvise it again.

### `-ExpectedScope`: proving the diff, not just describing it

`scripts/regenerate-baseline.ps1` requires `-ExpectedScope <glob> [<glob> ...]` in
both modes (one or more case-id globs, same syntax as
`Tests/baseline/waivers.tsv` -- `*` is field-local, `**` crosses fields,
e.g. `-ExpectedScope 'H|**'` to scope an entire area, or `-ExpectedScope 'H|J|**'`
to scope only house system J within it). Before anything under
`Tests/baseline/` is touched, the script diffs the currently committed
baseline against the freshly generated run, by case id, across every area
(`BaselineVerify --diff-scope`), and refuses -- prints every offending case id,
writes nothing -- if any added, removed, or changed case id in any area is not
covered by at least one glob.

This exists because "diff it yourself and confirm every changed row is
explained by `-DeviationNote`" cannot actually be followed by someone using
this script's own console output as their guide: a single corrupted constant
(one character trimmed off `EPS0`) failed only one area in
`scripts/verify-baseline.ps1` while silently moving roughly 12,300 rows across
seven areas by less than the comparison tolerance -- `-FromLocal` accepted and
committed all of it with no gate signal, before or after.

**What this guarantees is per case id, not per magnitude.** `-ExpectedScope`
proves that every added, removed, or changed case id in the run matches at
least one glob -- it does not bound how many rows a single glob is allowed to
cover. `H|**` is satisfied identically whether it matches one row or every row
under that prefix: `houses-armc`'s `H` prefix alone is 54,432 of the area's
55,512 rows (its other two prefixes, `HSTATE` and `HSUN`, need their own
globs -- see "Case id prefixes by area" above). So `-ExpectedScope` turns "read
the full diff yourself" into "read the one line the tool already proved true
about *which* case ids moved" -- e.g. the `calc-defaulteph` area's own
introduction, `calc-defaulteph: 0 changed / 2,720 added / 0 removed (100.0% of
the area's 2,720 case ids)`, not 2,720 rows to eyeball one by one -- and 100%
here is exactly right, since the whole area was new. It is still the
reviewer's job to look at that percentage and judge whether a change this
wide was the one they intended.
The percentage is printed for exactly that reason (see
`Tools/BaselineVerify/ScopeDiff.cs`'s `AreaResult.TouchedFraction`); nothing
refuses or warns based on it, deliberately -- see the note below.

A hard cap on `TouchedFraction`, mirroring the 5% waiver caps in
`Tools/BaselineVerify/Verdict.cs`, was considered and rejected. A waiver is a
narrow, targeted exception; matching more than 5% of an area is already
suspicious on its own. `-ExpectedScope` exists to confirm the opposite kind of
change -- a deliberate, often area-wide porting update (a new sidereal mode
touching every case id that uses it, a corrected constant touching everything
downstream of it) -- so a high `TouchedFraction` is the *expected* shape of
plenty of legitimate porting PRs, not a warning sign by itself. A cap here
would need routine per-regeneration overriding, which is exactly "a knob
nobody will tune" correctly: it would either sit so high it never fires, or
fire constantly for good reason until someone reflexively raises it. The
percentage is reported precisely so a human makes that judgment call with the
number in front of them, rather than a fixed threshold making it for them.

`-ExpectedScope` also gates the row-count manifest and hashes this script
writes: on success it rewrites `Tests/baseline/row-counts.tsv` (see
`Tools/BaselineVerify/RowCounts.cs`) from the new run's per-area counts, and
for `-FromLocal` appends the per-area changed/added/removed summary plus every
new TSV's SHA-256 into the same numbered log entry `-DeviationNote` writes --
so a reviewer sees the tool's own proof of scope right next to the human
explanation of intent, not just the latter.

### Case id prefixes by area

Writing an `-ExpectedScope` glob means knowing the area's case-id prefix (the
literal text before the first `|`, e.g. `H` in `H|0|-45|...`) -- and until now
nothing recorded that mapping. The only way to find a prefix was to write a
glob, get it wrong, and read the `OFFENDER` lines a `SCOPE-VIOLATION` prints.

`BaselineVerify --list-prefixes` prints the mapping directly, one line per
area, computed from the matrix's current output rather than any committed
file -- it runs `Areas.Generate` for every area in `Areas.All` and reports the
distinct prefixes actually present (see `Tools/BaselineVerify/PrefixMap.cs`).
It needs no baseline directory and no `-ExpectedScope` globs, so it is safe to
run standalone before writing one:

```powershell
dotnet run --project Tools/BaselineVerify -f net10.0 -- --list-prefixes
```

`BaselineVerify --diff-scope` also prints the same information (`PREFIX <area>:
...` lines) after `SCOPE-OK`, computed from the rows just regenerated, so a
scope that already ran can be double-checked without a separate invocation.

Several areas use more than one prefix -- an area is a file, not a namespace,
and some areas' generators cover more than one case-id family. `house-pos`
mixes `HouseName` rows (`HN|...`) with `HousePos` rows (`HP|...`); `houses-armc`
mixes `H|...`, `HSTATE|...`, and `HSUN|...`. A glob scoped to only one of an
area's prefixes does not cover the others -- see the note on scope magnitude
below.

The table below is a snapshot, generated by running the command above against
the code as of this writing, not hand-typed -- regenerate it (and diff against
what is here) any time an area is added, removed, or gains a new prefix,
rather than editing it directly:

| Area | Prefixes |
| --- | --- |
| astromodels | `AMLISTALL`, `AMNUT`, `AMSAYA`, `AMSCALC`, `AMSDET`, `AMSDT`, `AMVCALC`, `AMVDET` |
| atmo | `LAPSEDIRECT`, `LAPSERISE`, `REFR`, `REFX` |
| ayanamsa | `AY`, `AYBIT`, `AYEX`, `AYEXUT`, `AYUSER`, `AYUT` |
| calc | `C`, `CTOPO`, `CTOPO_SPEED`, `CU`, `CUTOPO`, `CUTOPO_SPEED` |
| calc-defaulteph | `CDEF`, `CUDEF` |
| coord | `AZ`, `AZR`, `CT`, `CTS` |
| datetime | `DC`, `DT`, `DTEX`, `DTUSERDEF`, `JD`, `JU`, `JUT1`, `RJ`, `ST`, `ST0`, `TE`, `TIDACC`, `UJ`, `UTZ` |
| eclipse | `LEH`, `LEW`, `LOG`, `LOL`, `LOW`, `LWL`, `SEH`, `SEW`, `SWG`, `SWL` |
| format | `CD`, `CLL`, `CN`, `CRS`, `CTM`, `D2L`, `D2N`, `DC2N`, `DCN`, `DGN`, `DM`, `DN`, `DOW`, `R2N`, `RM`, `RN`, `SD` |
| gauquelin | `GQ` |
| house-pos | `HN`, `HP` |
| houses-armc | `H`, `HSTATE`, `HSUN` |
| houses | `HS`, `HX` |
| misc | `PN`, `VER` |
| nodaps | `NA`, `NAUT` |
| orbit | `OE`, `OMM` |
| pheno-ast | `PHACHIRON`, `PHAFILE` |
| pheno | `PH`, `PHTOPO`, `PHTOPO_SPEED`, `PHUT`, `PHUTTOPO`, `PHUTTOPO_SPEED` |
| risetrans | `RT`, `RTATM`, `RTBIT`, `RTH` |

### Why `waivers.tsv` lives at `Tests/baseline/`, not `Tools/BaselineVerify/`

`Tests/baseline/waivers.tsv` (formerly `Tools/BaselineVerify/waivers.tsv`) excuses a
real, committed difference from the frozen reference, area by area, exactly the way
a `-FromLocal` regeneration does -- but unlike a regeneration, adding or editing a
waiver went through no check at all: `scripts/verify-baseline-log.ps1` only ever
looked at `Tests/baseline/*.tsv`, so a waiver change needed no accompanying log
entry and left no append-only record, even though each line already carries its own
PR number and reason (see the file's own header). Moving the file under
`Tests/baseline/` puts it inside that same glob with no code change to the log
script itself -- it is swept up automatically, the same way any other
`baseline-*.tsv` file is. There is no `regenerate-baseline.ps1` mode that writes a
waiver, so (same as a reference-mode version bump already requires) add the log
entry by hand, describing which waiver changed and why.
`Tools/BaselineVerify/BaselineVerify.csproj` links the file into the build output
under its bare name, so `Program.cs`'s runtime lookup did not need to change.

**Local mode does not touch the sidecar's original reference identity.** The
committed `SwissEphModuleVersionId`/`SwissEphAssemblySha256` fields describe
the reference package build and stay exactly as they were (see "Provenance
sidecar" below); `-FromLocal` instead appends a dated, commit-stamped entry
to that file's append-only "Local regenerations" log, using `-DeviationNote`
as the description. `-DeviationNote` is required with `-FromLocal` specifically
so that log entry cannot be an empty placeholder -- writing the description is
part of using the switch, not an optional afterthought.

## Provenance sidecar: what it means once local rows exist

`Tests/baseline/baseline-<version>.env.txt` started as a pure description of
one reference-mode run (`SwissEphNet <version>` NuGet package: framework, OS,
architecture, the assembly's `ModuleVersionId` and SHA-256). That description
is exactly what `BaselineVerify`'s assembly-identity check needs, and it never
changes once local rows start landing -- see `CheckAssemblyIdentity` in
`Tools/BaselineVerify/Program.cs`, which fails the run if the *current*
build's identity ever matches what is recorded there (local mode should never
accidentally compile to the same bytes as the reference package).

Once any row in `Tests/baseline/*.tsv` has been regenerated from local code
(`-FromLocal`), the sidecar's original eight fields no longer describe *every*
row in the directory, and saying so honestly matters more than a filename.
Rather than rename `baseline-<version>.env.txt` (which is derived from
`EnvInfo.ReferenceVersion` specifically so a future version bump cannot leave
a stale-named file behind -- see `EnvInfo.SidecarFileName` -- and which no
script or doc hard-codes as a literal string, only as a `baseline-*.env.txt`
pattern), the file grows an append-only "Local regenerations" log recording
every deliberate deviation, most recent last. Open the file: the ambiguity is
resolved in its content, not its name. This was a deliberate choice, not an
oversight -- renaming would decouple the filename from the
version-bump-safety property `SidecarFileName` exists to guarantee, for a
cosmetic gain the file's own content already delivers.

**Two different kinds of reference now live in this one file, area by area.**
`Tests/baseline/baseline-2.8.0.2.env.txt` has a "## Area provenance" table
naming, for every area, one of two kinds:

- `package-2.8.0.2`: this area's committed content is confirmed
  byte-identical to the original 2.8.0.2 reference-mode run. It proves
  **"unchanged since 2.08"** -- a real oracle, since the reference package
  predates any local code this repo has since patched and cannot have been
  produced by whatever is under test.
- `local-<short-sha>`: this area's committed content was last produced by
  `-FromLocal` at the given commit. It proves only **"unchanged since the day
  it was written"** -- a regression net against future drift, not an oracle
  against 2.08 behavior, because local code authored the content in the
  first place. Any bug already present in local code on that day is baked
  into the "golden" file right along with everything else, undetectably.

`pheno-ast`, `eclipse`, `risetrans`, `atmo`, `orbit`, `gauquelin` and
`astromodels` are `local-<sha>` for a structural reason, not a convenience
one: none of the functions they cover (`swe_pheno` for the six asteroids,
the eclipse/occultation `_where`/`_how`/`_when_*` family, `swe_rise_trans[_true_hor]`,
`swe_refrac[_extended]`/`swe_set_lapse_rate`, `swe_get_orbital_elements`/
`swe_orbit_max_min_true_distance`, `swe_gauquelin_sector`, or
`swe_set_astro_models`/`swe_get_astro_models`/`swe_set_interpolate_nut`) were
exercised by the matrix when it only targeted the 2.8.0.2 package, so there
is no reference-mode run of them to be byte-identical *to*. Regenerating
these seven in reference mode is not an option later, either, the way it
would be for a genuinely untouched area -- they simply did not exist as
areas at 2.8.0.2. Local mode was the only way to seed them at all.

## Verifying current code against the baseline

```powershell
./scripts/verify-baseline.ps1
```

This builds `BaselineVerify` in Release (both TFMs it targets), then runs it once
per TFM -- see "Why net8.0 and net10.0" above -- which builds `BaselineMatrix`
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
  `Tests/baseline/waivers.tsv`, which also records the PR it belongs to and
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
- Every area's row count (case id count) is checked against
  `Tests/baseline/row-counts.tsv` (`Tools/BaselineVerify/RowCounts.cs`). A missing
  entry, or a count that does not match, fails the area outright -- independent of
  and in addition to FAIL/ONLY-LOCAL/ONLY-REFERENCE. This exists because those
  three, plus the waiver fractions, all read zero (and so report PASS) for an area
  whose committed TSV has been silently reduced to zero rows and whose generator
  has been narrowed to match; `scripts/regenerate-baseline.ps1` is the only thing
  that rewrites this manifest, in the same pass as the TSVs, gated behind
  `-ExpectedScope`.

Pass `--dump-failures <path>` to write every non-exact row's *every* differing
field (not just the first, which is all the console table shows) to a file,
unlimited (the console output truncates at 50 FAIL rows per area). Use this
instead of the manual dump the DIR_GLUE review above had to improvise.

Exit code is 0 only if every area has zero FAIL, zero ONLY-LOCAL and zero
ONLY-REFERENCE rows, no stale waivers, no area over either 5% cap, and the
assembly-identity check does not detect a suspicious match.

## Platform lock

**The committed baseline is Windows-specific, and this gate is deliberately locked
to Windows.** It is not portable to Linux/macOS by construction, and that is a
choice, not an oversight -- see the measurements below for why.

A full Windows-vs-Linux comparison (same source, same commit, `.NET SDK 10.0.302`
both sides, Linux via `mcr.microsoft.com/dotnet/sdk:10.0-noble` on Ubuntu 24.04.4,
which is what `ubuntu-latest` currently resolves to) was run against the matrix as
it stood at commit `5148573` (2026-07-31, "Re-measure the Windows-vs-Linux
divergence against the current matrix"):

- **3,547,367** numeric fields compared; **66,342** (1.87%) differ at all between
  platforms; **5,394** are still beyond the shipped tolerance after the
  angle-wraparound allowance.
- **net8.0 and net10.0 produce identical numbers**, to the field: .NET 8.0.29 and
  .NET 10.0.10 each report 3,547,367 / 66,342 / 5,394. The divergence is
  ucrtbase-versus-glibc, not a difference between .NET versions, which is why the
  bit-exactness claim is scoped by platform rather than by runtime version.
- Five areas are bit-identical across platforms outright: `format`, `misc`,
  `pheno-ast`, `risetrans` and `atmo`. They are formatting, date arithmetic and
  table lookups, so no transcendental is involved.
- `houses` differs in 2.71% of its fields and yet **zero** rows fail, which is the
  tolerance doing what it was sized for against real libm divergence rather than
  synthetic boundary tests. `calc` (11.27% of fields, 3,442 beyond tolerance) and
  `nodaps` (14.96%) are the heaviest contributors.

**This triple is itself superseded.** `Tests/baseline/` has changed since commit
`5148573`: `row-counts.tsv` moved `astromodels` 300 -> 329 and `coord` 294 -> 394,
and eight more area files (`baseline-ayanamsa.tsv`, `baseline-calc-defaulteph.tsv`,
`baseline-datetime.tsv`, `baseline-eclipse.tsv`, `baseline-gauquelin.tsv`,
`baseline-misc.tsv`, `baseline-orbit.tsv`, `baseline-pheno-ast.tsv`) carry value
churn on top of that. The 3,547,367 / 66,342 / 5,394 figures above no longer
describe the current matrix; a Windows-vs-Linux re-measure against the current
baseline is outstanding. Do not quote this triple as current without re-running it.

The earlier pass, against a smaller matrix (3,443,058 numeric fields), found 47,052
differing and 3,346 beyond tolerance. The per-field findings below (the `'Y'` and
`'i'` house-system bugs, the SPEED differentiation noise) come from that pass and
still describe fields present in the current matrix; the sub-counts in the bullets
that follow have not been re-derived against the current run, so read them as
characterising the classes rather than as current totals. See "Verifying current
code against the baseline" above for how to run a report-only pass yourself.

- **3,443,058** numeric fields compared (at the time of this comparison); **47,052**
  (1.37%) differ at all between platforms.
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
'dotnet run --project Tools/BaselineVerify/BaselineVerify.csproj -c Release -f net10.0 --
/src/Tests/baseline --report-only'`; add a second run with `-f net8.0` to check that
asset too -- BaselineVerify targets both, see "Why net8.0 and net10.0" above).

## Why a separate solution

`BaselineGen`/`BaselineVerify.Tests` target `net10.0`; `BaselineMatrix` and
`BaselineVerify` target `net8.0;net10.0` (see "Why net8.0 and net10.0" above).
None of that overlaps with what `SwissEphNet.sln` itself used to build under
the old AppVeyor CI (`appveyor.yml`, since deleted -- see
`.github/workflows/ci.yml`), which ran on a `Visual Studio 2017` image whose
bundled SDK knew neither `net8.0`/`net10.0` nor central package management.
Adding these projects to `SwissEphNet.sln` back then would have broken
`dotnet restore .\SwissEphNet.sln` in that CI before the build step even
started. `Tools/BaselineTools.slnx` predates AppVeyor's removal and has
stayed separate since; it still keeps these projects buildable and
discoverable locally without going anywhere near the library's own target
frameworks. `global.json` at the repo root pins the SDK to the `10.0` major.minor
line for everything in the repo, including these four projects. It used to live
at `Tools/global.json` instead, on the theory that SDK resolution walks up from
a project's own directory -- it does not: `dotnet` walks up from the *invoking
process's current working directory*, and every build in this repo (CI, the
verify/regenerate scripts) is invoked from the repo root, so a `Tools/global.json`
was never actually being found or honored by any of them. Moving it to the root
is what makes the pin real.

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
| pheno-ast | `PhenoAst.cs` | `swe_pheno` (never `_ut`) for two bodies, Chiron and Pholus, that `pheno.cs`'s `Grids.CalcPlanets` sweep never reaches -- not all six originally swept; Ceres/Pallas/Juno/Vesta and every iflag/topocentric/ET-UT axis were collapsed out as duplicate rows, since the underlying `swe_calc` call always fails at file-open before any of them is ever consulted (see the file's doc comment). Every row's `retc` is `-1` with an all-zero `attr[]` payload, so the only thing a row actually pins is its `serr` string; this area cannot distinguish two library versions that fail at the same lookup but would disagree on a real `attr[]` value (e.g. the `pla_diam[]` change this area was originally meant to catch, which never executes here -- see the doc comment's "CORRECTION" paragraph) |
| eclipse | `Eclipse.cs` | `swe_sol_eclipse_where/how/when_glob/when_loc`, `swe_lun_eclipse_how/when/when_loc`, `swe_lun_occult_where/when_glob/when_loc` |
| risetrans | `RiseTrans.cs` | `swe_rise_trans`, `swe_rise_trans_true_hor` across `SE_CALC_RISE/SET/MTRANSIT/ITRANSIT` and the `SE_BIT_*` rise/set bit flags |
| atmo | `Atmo.cs` | `swe_refrac`, `swe_refrac_extended` (both directions), `swe_set_lapse_rate` |
| orbit | `Orbit.cs` | `swe_get_orbital_elements`, `swe_orbit_max_min_true_distance` |
| gauquelin | `Gauquelin.cs` | `swe_gauquelin_sector` (imeth 0/1 hit a known undersized-array bug in `swe_house_pos` for hsys `'G'` -- see the file's doc comment) |
| astromodels | `AstroModels.cs` | `swe_set_astro_models`/`swe_get_astro_models` across the `SEMOD_PREC_*`/`SEMOD_NUT_*`/`SEMOD_DELTAT_*`/`SEMOD_JPLHOR*` families and the `"SE<version>"` bundle form, plus `swe_set_interpolate_nut` |
| calc-defaulteph | `CalcDefaultEph.cs` | `swe_calc`/`swe_calc_ut` with iflag combinations that do NOT force `SEFLG_MOSEPH`, exercising `plaus_iflag`'s ephemeris-defaulting branch (`CPort/Sweph.cs:6698-6699`) that the `calc` area's own sweep never reaches (`Calc.cs` ORs `SEFLG_MOSEPH` into every combination, including the nominal `"0"` one) |

Every row uses a brand new `SwissEph` instance, with one deliberate, explicitly-named
exception: `Houses.AddStatefulPairRows` shares a single instance across two ordered
calls, specifically to exercise `swe_houses_armc`'s hidden `saved_sundec` field (a
C `static` emulated as a C# instance field) in the one state it actually matters --
a real declination stored by one call, consumed by a sentinel call right after it on
the same instance. Every other row uses a fresh instance, since reusing one there
would make hsys `'I'`/`'i'` depend on call order and the baseline would not be
reproducible.
