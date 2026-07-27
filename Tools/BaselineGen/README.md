# BaselineGen / BaselineMatrix / BaselineVerify

Characterization ("golden master") harness for SwissEphNet. It runs a fixed matrix
of Swiss Ephemeris calls that need no ephemeris data files (Moshier and analytic
paths only) and freezes their output, so a later PR can prove it did not change
numerical behavior.

Three projects, one shared matrix:

- **`BaselineMatrix`** -- the actual matrix code (Houses.cs, Calc.cs, Ayanamsa.cs,
  etc.) and the `UseReferencePackage` switch that decides how it resolves
  `SwissEphNet`. Nothing in here is run directly.
- **`BaselineGen`** -- a console app that runs the matrix and writes one TSV file
  per area, plus an environment sidecar, to a directory you give it.
- **`BaselineVerify`** -- a console app that runs the matrix in local mode (always;
  it never builds against the reference package) and compares it against the
  files committed under `Tests/baseline/`.

These three, plus `SwissEphNet.csproj` itself, live in `Tools/BaselineTools.slnx`.
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

- Numeric fields are compared with a relative epsilon of 1e-13 (CPort calls
  `Math.Sin`/`Cos`/`Tan`/`Pow`/`Asin`/`Acos`/`Atan`/`Atan2`/`Log`/`Exp` several
  hundred times, and .NET does not guarantee bit-identical transcendental results
  across OS, architecture, or runtime version -- only `Math.Sqrt` is exempt from
  that).
- String and integer fields must match exactly.
- A row is reported as PASS (either exact or within tolerance), FAIL (exact-match
  mismatch on a non-numeric field, or a numeric field beyond tolerance), or WAIVED
  (its case id matches a glob in `Tools/BaselineVerify/waivers.tsv`, which also
  records the reason -- see that file for the current entries).

Exit code is 0 only if every area has zero FAIL, zero ONLY-LOCAL and zero
ONLY-REFERENCE rows (after waivers).

## Why a separate solution

`BaselineMatrix`/`BaselineGen`/`BaselineVerify` target `net10.0`. AppVeyor CI
(`appveyor.yml`) builds `SwissEphNet.sln` on the `Visual Studio 2017` image, whose
bundled SDK does not know `net10.0` -- adding these projects to `SwissEphNet.sln`
would break `dotnet restore .\SwissEphNet.sln` in CI before the build step even
starts. `Tools/BaselineTools.slnx` keeps them buildable and discoverable locally
without going anywhere near the CI image or the library's own target frameworks.

## Matrix coverage

See the doc comments at the top of each file under `Tools/BaselineMatrix/` for what
each area covers. Areas and their files:

| Area | File | Covers |
|---|---|---|
| houses-armc | `Houses.cs` | `swe_houses_armc` (dense sweep), the saved_sundec hazard |
| houses | `HousesEx.cs` | `swe_houses`, `swe_houses_ex` (including `SEFLG_SIDEREAL`) |
| house-pos | `HousePos.cs` | `swe_house_pos`, `swe_house_name` |
| calc | `Calc.cs` | `swe_calc`, `swe_calc_ut` |
| pheno | `Pheno.cs` | `swe_pheno`, `swe_pheno_ut` |
| ayanamsa | `Ayanamsa.cs` | `swe_get_ayanamsa[_ut]`, `_ex[_ut]`, `SE_SIDM_USER`, SIDBIT flags |
| datetime | `DateTime_.cs` | date/time conversions, Delta-T, tidal acceleration |
| coord | `CoordHelpers.cs` | `swe_cotrans[_sp]`, `swe_azalt[_rev]` |
| format | `FormatHelpers.cs` | `swe_split_deg`, `swe_cs2*str`, norm/midpoint helpers |
| misc | `Misc.cs` | `swe_get_planet_name`, `swe_version` |

Every row uses a brand new `SwissEph` instance. `swe_houses_armc` keeps a hidden
field (`saved_sundec`) that emulates a C `static`, so reusing an instance across
rows would make hsys `'I'`/`'i'` depend on call order and the baseline would not
be reproducible.
