# SwissEphNet

This project is an Astrodienst Swiss Ephemeris (http://www.astro.com/swisseph/) .Net portage from
C to C#, targeting netstandard2.0, .NET 8 and .NET 10 for cross platform usage. `SE_VERSION` still
reports `"2.08"`, deliberately: the API surface reflects the whole 2.10.03 delta, but the version
string itself is a separate, deferred change. `swephlib.c`, the 2.10.03 ayanamsa and `pla_diam`
tables, the header and constants stage, the eight crossing functions, the ayanamsha machinery
(`get_aya_correction`, `prec_offset`), `sweph.c`, `swecl.c`, `swehouse.c`, and `swetest.c` are all
ported; see `docs/sweph-c-stages.md` for how `sweph.c`'s work divided across four slices.

## About this repository

This repository, https://github.com/Tim81/SwissEphNet, is a maintained fork of
[ygrenier/SwissEphNet](https://github.com/ygrenier/SwissEphNet). The original C-to-C# port is
Yan Grenier's work (2014-2019); this fork continues it. Since 2026 it has been maintained by
Timothy van der Ham, who has modernized the build and target frameworks (netstandard2.0, net8.0,
net10.0) and fixed a number of bugs in the port: the fixed-star search returning the wrong star,
multi-word star names being unfindable, the heliacal Moon branch never being taken,
`swe_set_astro_models` throwing, the `DIR_GLUE` path separator, culture-sensitive string
comparison, and a netstandard2.0 infinite recursion. See `NOTICE` and the package release notes
for details.

## License

Swiss Ephemeris, and therefore this library, is dual-licensed. You must choose one of:

- **AGPL-3.0** (GNU Affero General Public License) - free, but with a network clause: if you
  run a modified or unmodified version of this library as part of a service that users interact
  with over a network (a web app, an API, a SaaS product, etc.), the AGPL requires you to offer
  those users the complete corresponding source code of your whole service, not just this
  library. This reaches server-side and SaaS use even when you never distribute a binary to
  anyone - it is triggered by operating the service, not by shipping a copy. If that obligation
  does not work for your project, AGPL is not the option for you.
- **Swiss Ephemeris Professional License** - a commercial license purchased from
  [Astrodienst](http://www.astro.com/swisseph/) that does not carry the AGPL's source-disclosure
  obligation. Contact Astrodienst directly to obtain one.

See [`LICENSE`](LICENSE) for the full license conditions, [`agpl-3.0.txt`](agpl-3.0.txt) for the
AGPL text, and [`NOTICE`](NOTICE) for attribution.

The library targets 3 frameworks: `netstandard2.0`, `net8.0` and `net10.0`. It is not currently
published as a NuGet package (see the versioning note in `SwissEphNet.csproj`); build it from
source or reference the project directly.

The programs SweMini and SweTest target `net10.0`.

## Samples

A new repos was created https://github.com/ygrenier/SwissEphNet.Samples containing
lot of sample applications for using the library on different application types.

## Works with async

For working with the async context read the [this paragraph](https://github.com/ygrenier/SwissEphNet/wiki/Loading-files#works-in-an-async-context).

# Breaking changes

## Unreleased

`swe_houses`, `swe_houses_ex`, `swe_houses_armc`, `swe_house_pos`, and `swe_house_name`
each gained an `int hsys` overload alongside the existing `char hsys` overload, to match
upstream `swephexp.h`, which has always declared `hsys` as `int`. Binary compatibility is
preserved: existing compiled consumers (anything built against a prior version of this
library) keep working unchanged, since the original `char` overloads are still present
with the same signatures.

Source-level and reflection-based consumers can be affected:

- **Reflection by name** (`Type.GetMethod("swe_house_name")` with no parameter-type
  array, or any binder that resolves by name alone -- this affects Python.NET, some
  PowerShell cmdlet-binding paths, and dependency-injection or serializer conventions
  that enumerate methods by name) now throws `AmbiguousMatchException` for these five
  methods, because there are two overloads where there used to be one. Pass an explicit
  parameter-type array to `GetMethod` (or the equivalent for your binder) to select the
  overload you want.
- **`var f = swe.swe_house_name;`** (or any bare method-group assignment to `var` for one
  of the five widened methods) no longer compiles under C# 10+ natural-type inference:
  the compiler cannot pick between the two overloads and reports `CS8917`. Declare an
  explicit delegate type instead, e.g. `Func<char, string> f = swe.swe_house_name;`.
- **A `char` above `U+00FF`** passed to `swe_houses`, `swe_houses_ex` or
  `swe_houses_armc` now takes its low byte when resolving the house system, matching what
  a genuine 8-bit C `char` would resolve to, rather than being widened untruncated as
  before -- measured, `(char)331` (low byte `0x4B` = `'K'`) resolved to Placidus before
  and resolves to Koch now. `swe_house_pos` changes the same way in its internal cusp
  computation, but its own house-system dispatch compares the raw value and is unchanged,
  and `swe_house_name` never narrows at all -- its behaviour is identical before and
  after. This only affects callers passing a `char` outside the Latin-1 range, which was
  never a valid house-system letter either way; see `docs/known-issues.md` for the
  measured before/after.
- **`swe_house_pos` with `hsys = 'G'`** (Gauquelin sectors) no longer throws
  `IndexOutOfRangeException` -- an internal cusp array was undersized relative to
  upstream 2.10.03. If your code wraps that call in a guard specifically to catch this
  exception, that guard is now dead code and can be removed.
- **Eclipse magnitude and obscuration are a hundred times smaller.** `attr[0]` and
  `attr[2]` from `swe_sol_eclipse_how`, and from the `attr` array `swe_sol_eclipse_where`
  and `swe_lun_occult_where` fill, are now fractions rather than percentages: an eclipse
  that reported `100` reports `1`. This is upstream's change at `swecl.c:1067-1087`, not
  this port's, and it is silent -- the call still succeeds, the array is still the same
  length, and only the value moves. Any caller formatting these as a percentage needs to
  multiply by 100, and any threshold comparison against a number above 1 will now never
  fire. `attr[1]` and `attr[3]` onward are unaffected.
- **Planetary magnitudes changed.** `swe_pheno` and `swe_pheno_ut` return a different
  `attr[4]` for the Moon and for Mercury through Neptune. Upstream replaced the Hilton
  2005 model with Mallama 2018, and added a separate lunar model that switches formula
  past a phase angle of 147.1385465 degrees. Not an API change and not a defect fix on
  this side: the numbers differ because the underlying model does. Apparent diameter and
  phase angle for these bodies are unaffected by the magnitude-model swap.
  `Tests/SwissEphNet.Tests/PlaDiamCoverageTest.cs` is not evidence for that claim: it
  covers a different, unrelated change -- the updated `pla_diam[]` table moves `attr[3]`
  for six minor bodies this bullet is not about (Chiron, Pholus, Ceres, Pallas, Juno and
  Vesta) -- and it asserts only `attr[3]`, not phase angle or the rest of `attr`.
- **`swe_rise_trans_true_hor` gains a `horhgt == -100` sentinel.** Passing exactly `-100`
  now means "use the dip of the horizon", computed from `calc_dip`, instead of a literal
  horizon height of -100 degrees (`swecl.c:4415`; ported at `SweCL.cs:4502`). Absent from
  2.08, so a caller that happened to pass `-100` before now gets different rise/set times.
- **`swe_lun_eclipse_when`'s search-precision threshold moved from 2000000 to 2100000**
  (`swecl.c:3485`, ported at `SweCL.cs:3548`), changing which Julian day range gets the
  coarser 5-day search step versus the finer 0.1-day one. The sibling function
  `swe_sol_eclipse_when_glob` deliberately keeps its own threshold at 2000000 -- upstream
  did not move both.
- **House cusps move at every latitude for Placidus and Gauquelin.** `CalcH` (`swehouse.c`)
  now iterates to convergence (`niter_max = 100`) where 2.08 always called it with
  `iteration_count = 2` fixed. The two now agree only where two iterations happened to
  already converge.
- **`swe_rise_trans` routes fixed stars to the slow path.** `swecl.c:4362-4376` gates the
  fast algorithm on `!do_fixstar`; a fixed-star call now always falls through to
  `swe_rise_trans_true_hor` rather than the fast approximation, so star rise and set times
  change.
- **Every `SEFLG_SWIEPH` position changes.** `rot_back`'s J2000 obliquity was always zero in
  this port -- it read `swed.oec2000`, which nothing ever populated -- so every position
  rotated back through it used the wrong obliquity. Fixed alongside the rest of `sweph.c`'s
  file layer; the file-backed oracle grid went from 791 of 2,024 bit-identical to 1,975.
  Probably the largest numeric change in this release.
- **`swe_nod_aps`/`swe_nod_aps_ut` returned all-zero nodes and apsides for every standard
  ayanamsha.** A missing `swi_cartpol_sp` call on the sidereal branch (`swecl.c:5587`) left
  the ecliptic cartesian coordinates zeroed before the ayanamsha was applied. Fixed for
  every ayanamsha except `SE_SIDBIT_ECL_T0` and `SE_SIDBIT_SSY_PLANE`, which were already
  correct.
- **`serr` is now populated at roughly twenty sites where it was silently empty before**, a
  guard-inversion bug (`serr != NULL` transliterated from C, where it means "caller
  supplied a buffer", into a check for "a message is already present" against a C# `ref
  string` that always supplies one). Any caller using `String.IsNullOrEmpty(serr)` as a
  success signal will see previously-silent failures start reporting a message.
- **`swe_get_ayanamsa_ex` with no prior `swe_set_sid_mode` changes value**, from 92.525 to
  24.754 degrees: `swi_get_ayanamsa_ex` took its `sid_data` copy before the
  `SE_SIDM_FAGAN_BRADLEY` fallback ran, so it read pre-fallback state.
- **`swe_nod_aps` after `swe_close`, under a sidereal or geocentric mode, changes value** --
  344.63 degrees becomes 189.21 for the Moon's node at J2000. Two defects were cancelling
  each other out: `free_planets` replaced an object instead of zeroing it in place, and a
  separate `!= Sweph.B1950` mask (should be `!= 0`, `swecl.c:5414`) made the geocentric
  correction unreachable, so the stale object's coincidentally-zero values had been masking
  the first bug.
- **`swe_set_astro_models("")` or `(null)` changes value**, from `AMODELS_SE_1_00` to
  `AMODELS_SE_2_06`: the version-string parser did not match `strtod`'s "longest parseable
  prefix" behavior, so `"2.10.03"` failed to parse at all and fell through to the last
  branch.
- **`swe_refrac_extended` and `calc_dip` change value.** `swe_refrac_extended`'s
  visibility test flips from `trualt > dip` to `inalt >= dip` (upstream's own 4 Feb 2020 fix,
  `swecl.c:3070-3113`), and `calc_dip` corrects a constant from 273.16 to 273.15
  (`swecl.c:3159-3168`).
- **New additive API surface**, absent from 2.08 and now present: `swe_houses_ex2`,
  `swe_houses_armc_ex2`, `swe_calc_pctr`, `swe_get_current_file_data`, the
  `SEFLG_TROPICAL`, `SEFLG_CENTER_BODY` and `SEFLG_TEST_PLMOON` flags, `SE_ECL_HYBRID`, and
  three `SE_SIDBIT_*` constants (`SE_SIDBIT_ECL_DATE`, `SE_SIDBIT_NO_PREC_OFFSET`,
  `SE_SIDBIT_PREC_ORIG`). None of this replaces existing API; it is purely additive.
- **SweTest CLI**: options that previously threw (`-ay`, `-sidt0`, `-sidsp`, `-sid`, `-j`,
  `-helflag`, `-amod`, `-tidacc`) now parse instead of crashing on C pointer-arithmetic
  transliterated as string concatenation. `-house` and `-utc` no longer crash. `dms()` no
  longer throws `ArgumentOutOfRangeException` once a degree value reaches 100 or more.

## V:2.6.0.21

Since .NETStandard are supported, this library is not compiled on PCL version. Only
2 version are available: .NET 4.0 for old legacy application, .NETStandard 1.0
for the new framework.

.NETStandard 1.0 is supported by VS 2015.3 and VS 2017, so PCL are not usefull.

The new repos of https://github.com/ygrenier/SwissEphNet.Samples contains applications
using only this version of the library.

## V:2.5.1.16

Since 2.5.1.16 some libraries don't supports the "Windows-1252" code page. In this case, the default encoding become "UTF-8".

You can change the default encoding by assigning the static property ```SwissEphNet.SwissEph.DefaultEncoding```.

# Thread Local Storage (TLS) support

Since version 2.03.00 the Swiss Ephemeris library supports the 
[Thread-Local Storage (TLS)](https://en.wikipedia.org/wiki/Thread-local_storage), which
allows to run several calculations simultaneously with multiple threads.

As SwissEphNet is build an object ```SwissEphNet.SwissEph```, it always supports multiple
calculations. You just need create one ```SwissEphNet.SwissEph``` per thread. On other hand
it's still not thread-safe, so don't access the same ```SwissEphNet.SwissEph``` instance
from multiple threads.


# Projects splitted (2014-06-06)

From now the SweNet et SwephNet projects are moved to a new repos [SwephNet](https://github.com/ygrenier/SwephNet).

SwephNet is the next version of SwissEph, with a better .Net implementation. The two projects will 
continue to exist in parallel :
- SwissEphNet : is the direct C to C# portage of the Swiss Ephemeris.
- SwephNet : is the full .Net implementation of the Swiss Ephemeris.

# Usage

This fork is not currently published to NuGet (see the versioning note in `SwissEphNet.csproj`).
An older release of the upstream project is available as a
[Nuget package](https://www.nuget.org/packages/SwissEphNet), but it predates this fork's retarget
and bug fixes. Build from source or reference `SwissEphNet/SwissEphNet.csproj` directly.

SwissEphNet targets `netstandard2.0`, `net8.0` and `net10.0`.

## Create an instance

SwissEphNet.SwissEph is ```IDisposable``` so you can use it with an ```using``` statement.

```C#
using (var sweph = new SwissEphNet.SwissEph()) {
    // Use it
}
```

## Loading files

SwissEphNet does not access the file system directly.

As Swiss Ephemeris use some data files, an event exists for loading the files required.

```C#
using (var sweph = new SwissEphNet.SwissEph()) {
    sweph.OnLoadFile += (s, e) => {
        // Loading file
    };
    // Use it
}
```

For more information [read this page](https://github.com/ygrenier/SwissEphNet/wiki/Loading-files).

# Continuous Integration

This fork replaced the upstream project's AppVeyor CI with GitHub Actions; see
`.github/workflows/`.

# Contributing

Before touching `SwissEphNet/CPort/`, `Programs/SweTest/Program.cs` or `Programs/SweMini/Program.cs`,
read `CONTRIBUTING.md`. Those files are deliberate, line-by-line transliterations of the Swiss
Ephemeris C source and must never be reformatted or restructured; that correspondence is what
makes each upstream Swiss Ephemeris upgrade tractable.

# Characterization baseline

Before any change to the C-to-C# port, a frozen golden-master file records what the
library currently outputs for a large matrix of calls. See `Tools/BaselineGen/README.md`
for what it covers and `scripts/verify-baseline.ps1` to check current code against it.
The baseline is Windows-specific by design; see that file's "Platform lock" section.
Numerical-stability findings turned up while building it are in `docs/known-issues.md`.

The characterization baseline proves *self-consistency*: a change did not alter
anything it wasn't supposed to. It cannot prove *correctness*, because it is
generated from the port's own output. That is what the correctness oracle below
is for.

# Correctness oracle

`Tests/SwissEphNet.Conformance.Tests` checks the port's output against
Astrodienst's own reference values, not against the port's own prior output.
The reference corpus is Swiss Ephemeris **2.10.03**'s `setest` test suite
(12,757 iterations, ~321K asserted values across 10 functional areas). Even though the port has
now landed the whole 2.10.03 delta file by file, it is not at full parity: `known-fail.tsv` still
lists 1,435 failing iterations (11,322 passing, 88.8%), and the known-fail list remains the work
queue for the remainder. Each porting PR should remove entries from it; any entry that reappears
is a regression.

- `external/swisseph` -- a git submodule, sparse-checked-out, pinned to tag
  `v2.10.3final`. It serves two purposes:
  1. **The reference corpus** for the conformance oracle: `setest/t.exp`
     (expected values) and `setest/t.fix` (tolerances), plus the core `.se1`
     ephemeris files, `sefstars.txt`, `seorbel.txt`, and `seleapsec.txt` that
     the SWIEPH/analytic iterations need to run.
  2. **The C source to diff the port against**, file by file, as porting work
     from 2.08 to 2.10.03 proceeds (`*.c`, `*.h`, `Makefile`, `LICENSE`).

  Initialize it with the sparse-checkout recipe in `CONTRIBUTING.md` ("The upstream C is
  vendored at `external/swisseph`"), measured at ~19 MB. Sparse patterns have to be set up
  before the first checkout, so `git submodule update --init external/swisseph` on its own
  does not produce a sparse checkout -- it lands at the same commit but pulls the full,
  unfiltered tree, measured at ~423.9 MB.

- `Tests/conformance/known-fail.tsv` -- one row per iteration currently known
  to fail, with a category (`NOT-IMPLEMENTED`, `VALUE-MISMATCH`,
  `DATA-MISSING`, `ERROR`, or `UNREPRODUCIBLE`) and a short reason. The
  conformance run **fails** unless the port's actual behavior matches this
  file exactly: any iteration failing that isn't on the list (a regression),
  any listed row recorded under a category the port no longer matches
  (category drift -- still failing, but not the same failure), any listed row
  that now passes (progress left un-pruned), and any row for an iteration no
  longer in the corpus (stale) are all gate failures. There is no "reports
  without failing" case -- the file and the port's behavior must agree, in
  both directions, for the gate to pass.

  - `NOT-IMPLEMENTED` names the category for a 2.10-only API the port doesn't have; it is
    currently empty and its classifier unreachable, because every function the 2.10.03 API
    surface declares now exists on the port (the last three, `swe_calc_pctr`,
    `swe_houses_ex2` and `swe_houses_armc_ex2`, landed with `sweph.c`'s dispatch slice).
    `DATA-MISSING`: a required data file (a JPL DE ephemeris, `ephe/sat/`)
    isn't shipped by this repo. `ERROR`: the dispatch threw. `VALUE-MISMATCH`:
    the port ran and produced an answer that doesn't match the reference
    within `t.fix` tolerance -- this and `ERROR` are the actionable
    categories, the actual porting work queue. `UNREPRODUCIBLE`: a structural
    C-vs-C# representational gap makes the reference call impossible to
    construct at all, as opposed to constructible but wrong -- distinct from
    the other three, and excluded from the pass-rate denominator the same way
    they are (see `ConformanceReport.SuiteSummary.PassRate`'s doc comment).
    Currently 0 across the whole corpus (suite 6 testcase 6, the one place
    that used to carry all of it, became reproducible once the port's five
    house entry points gained faithful `int hsys` overloads -- see
    `Suite06Houses.Dispatch`'s remarks on testcase 6 for the mechanics).

  See "Reporting by testcase" in `CONTRIBUTING.md` for how to read a run
  (60 testcases, split into actionable vs. parked) instead of 12,757
  individual rows, and "The two gates disagree on purpose, not by accident"
  for why this gate failing constantly is expected and the characterization
  baseline above failing at all is not -- they are not the same kind of
  red.

- Two data sources this repo does not ship are skipped by default and
  reported as `DATA-MISSING`, not run: `SEFLG_JPLEPH` iterations need a
  multi-hundred-MB JPL DE file (opt in with `SWISSEPH_CONFORMANCE_INCLUDE_JPL=1`
  and `SWISSEPH_CONFORMANCE_JPL_FILE=<path>`), and planetary-moon bodies
  (`ipl` 9000-9999) need `ephe/sat/` at ~227 MB (opt in with
  `SWISSEPH_CONFORMANCE_INCLUDE_MOONS=1` and that directory populated).

- A separate workflow, not folded into `ci.yml`'s fast job:
  `.github/workflows/conformance.yml` runs on a schedule, on demand, and on every pull
  request, with no `paths` filter -- an earlier version restricted the `pull_request`
  trigger to `SwissEphNet/**` and the oracle's own paths, but that allowlist could never be
  complete (it missed `global.json`, `Directory.Build.props`, and a submodule gitlink bump
  to `external/swisseph` itself), so it was dropped to match `ci.yml` and `baseline.yml`,
  neither of which filters by paths either. It earns that spot on every PR on cost, not by
  default: dispatching all 12,757
  iterations is ~2s in-process (measured, Release build,
  `Tools/ConformanceKnownFailGen`), and `dotnet test
  Tests/SwissEphNet.Conformance.Tests` end-to-end (both TFMs, including test
  host startup) is ~8s. The submodule checkout is the only real cost and is
  sparse (~19 MB, not the ~423.9 MB a full, unfiltered checkout would pull)
  and cached on the pinned commit SHA.

- Regenerating `Tests/conformance/known-fail.tsv` is
  `scripts/regenerate-known-fail.ps1 -PruneOnly` to remove newly-passing rows
  (the common case after a porting PR; refuses to run if it would add or
  recategorize a row instead) or `-Reason "..." [-PR N]` for a full
  regenerate that can also add rows -- see "Correctness oracle known-fail
  list" in `CONTRIBUTING.md` for the invariant it enforces (rows may be
  removed freely; adding one needs a written reason and review).

**Licensing note:** vendoring Swiss Ephemeris 2.10.x source is consistent with this project's own
license, which is already the dual AGPL-3.0 / Swiss Ephemeris Professional text (see "License"
above and `LICENSE`). The submodule does not change that; both sides of the port have carried the
same license since before 2.10.03 work started.

# Firsts steps

Our first step is to convert the C source code to C#, and provide some conversions from C like string format.

## (x)printf 

We implements (x)printf base methods with the Richard Prinz project (http://www.codeproject.com/Articles/19274/A-printf-implementation-in-C) with somes updates.

## (x)scanf

We implements (x)scanf base methods with the Jonathan Wood project (http://www.blackbeltcoder.com/Articles/strings/a-sscanf-replacement-for-net) with some updates and unit tests.

## C conversion

All C files are included in the partial class 'SwissEph' each in a specific file.

All exported constants are defined as public in the class.

All exported methods are defined as public in the class.

The other elements are declared as private.

The compilation configuration use pre-processor constants. We remove lot of them in our case. The other are converted as constants, not pre-processor.

# Seconds steps

Now the portage is correct, so we create a new project (https://github.com/ygrenier/SwephNet) with
a new interface more adapted to the .Net guidelines.

# References

The Swiss Ephemeris Programming Interface documentation : http://www.astro.com/swisseph/swephprg.htm.

Last code source of Swiss Ephemeris from ftp://ftp.astro.ch/pub/swisseph/.

The NASA JPL resouces : http://www.jpl.nasa.gov/, http://ssd.jpl.nasa.gov/.
