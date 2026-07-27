# SwissEphNet

This project is an Astrodienst Swiss Ephemeris (http://www.astro.com/swisseph/) .Net portage from
C (currently tracking version 2.08; the 2.10.03 port has not started) to C#, targeting
netstandard2.0, .NET 8 and .NET 10 for cross platform usage.

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
(12,757 iterations, ~321K asserted values across 10 functional areas); the
port is currently at **2.08**, so most iterations fail today, and that is the
expected starting state -- the known-fail list is the work queue for the
port. Each porting PR should remove entries from it; any entry that reappears
is a regression.

- `external/swisseph` -- a git submodule, sparse-checked-out, pinned to tag
  `v2.10.3final`. It serves two purposes:
  1. **The reference corpus** for the conformance oracle: `setest/t.exp`
     (expected values) and `setest/t.fix` (tolerances), plus the core `.se1`
     ephemeris files, `sefstars.txt`, `seorbel.txt`, and `seleapsec.txt` that
     the SWIEPH/analytic iterations need to run.
  2. **The C source to diff the port against**, file by file, as porting work
     from 2.08 to 2.10.03 proceeds (`*.c`, `*.h`, `Makefile`, `LICENSE`).

  Initialize it with `git submodule update --init external/swisseph` (~25 MB;
  it is sparse-checked-out, not the ~444 MB full checkout).

- `Tests/conformance/known-fail.tsv` -- one row per iteration currently known
  to fail, with a category (`NOT-IMPLEMENTED`, `VALUE-MISMATCH`,
  `DATA-MISSING`, or `ERROR`) and a short reason. The conformance run passes
  if the set of failures is a subset of this list, fails if any iteration
  fails that isn't on it (a regression), and reports -- without failing -- any
  listed iteration that now passes (progress; remove that row).

- Two data sources this repo does not ship are skipped by default and
  reported as `DATA-MISSING`, not run: `SEFLG_JPLEPH` iterations need a
  multi-hundred-MB JPL DE file (opt in with `SWISSEPH_CONFORMANCE_INCLUDE_JPL=1`
  and `SWISSEPH_CONFORMANCE_JPL_FILE=<path>`), and planetary-moon bodies
  (`ipl` 9000-9999) need `ephe/sat/` at ~227 MB (opt in with
  `SWISSEPH_CONFORMANCE_INCLUDE_MOONS=1` and that directory populated).

- A separate workflow, not folded into `ci.yml`'s fast job:
  `.github/workflows/conformance.yml` runs on a schedule, on demand, and on
  pull requests that touch `SwissEphNet/**` or the oracle itself -- gated by
  `paths`, so it does not run on every PR the way `ci.yml` does. It earns that
  spot on the PR path on cost, not by default: dispatching all 12,757
  iterations is ~2s in-process (measured, Release build,
  `Tools/ConformanceKnownFailGen`), and `dotnet test
  Tests/SwissEphNet.Conformance.Tests` end-to-end (both TFMs, including test
  host startup) is ~8s. The submodule checkout is the only real cost and is
  sparse (~25 MB, not the full ~444 MB `git submodule update --init` would
  pull) and cached on the pinned commit SHA.

- Regenerating `Tests/conformance/known-fail.tsv` is
  `scripts/regenerate-known-fail.ps1 -Reason "..."` -- see "Correctness oracle
  known-fail list" in `CONTRIBUTING.md` for the invariant it enforces (rows
  may be removed freely; adding one needs a written reason and review).

**Licensing note:** vendoring Swiss Ephemeris 2.10.x source is consistent with
the AGPL-3.0 relicensing Astrodienst has planned for that line, but that
relicensing has **not** happened in this repo yet -- `LICENSE` here is still
GPL-2. The license itself is unchanged by the submodule; a separate PR should
land that relicensing before or alongside any further 2.10.03 porting work.

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
