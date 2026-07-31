# SwissEphNet

This project is an Astrodienst Swiss Ephemeris (http://www.astro.com/swisseph/) .Net portage from
C to C#, targeting netstandard2.0, .NET 8 and .NET 10 for cross platform usage. `SE_VERSION`
reports `"2.10.03"`: the whole 2.10.03 delta has landed, file by file -- `swephlib.c`, the
2.10.03 ayanamsa and `pla_diam` tables, the header and constants stage, the eight crossing
functions, the ayanamsha machinery (`get_aya_correction`, `prec_offset`), `sweph.c`, `swecl.c`,
`swehouse.c`, and `swetest.c` are all ported; see `docs/sweph-c-stages.md` for how `sweph.c`'s
work divided across four slices. The port is not yet at full parity with 2.10.03's own reference
values -- see "Correctness oracle" below and `docs/compliance-2.10.03.md` for how far along it is
and what has and has not been measured.

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

# Upgrading from 2.8.0.2

This section is for anyone with `SwissEphNet 2.8.0.2` in a project, deciding whether to move to
`SwissEphSharp 2.10.3`. Four questions, in the order they matter: will your numbers change, will
your code still compile, what do you gain, and can you trust the answers you get.

## Will your numbers change?

For most calls, yes. Two different things are mixed together in this list: places where
Astrodienst changed the reference model between 2.08 and 2.10.03, and places where this port's
own arithmetic was wrong and is fixed now. Both move your output. Only one of them is upstream's
doing, and it matters which.

The largest change in the release, and a port defect rather than an upstream one: every
`SEFLG_SWIEPH` position changes. `rot_back` reads a J2000 obliquity, `swed.oec2000`, that nothing
in the port ever populated, so it came out zero on every call, and every position rotated back
through it used the wrong obliquity. That covers any `swe_calc`/`swe_calc_ut` call reading the
Swiss Ephemeris files rather than falling back to Moshier's analytic approximation. Fixing it
moved the file-backed comparison against Astrodienst's own C from 791 of 2,024 bit-identical rows
to 1,975. 2.08 and 2.10.03 compute `rot_back` the same way, so this was never a version-tracking
gap; it was wrong the whole time.

Also a port defect, not an upstream change: `swe_nod_aps` and `swe_nod_aps_ut` returned all-zero
nodes and apsides under `SEFLG_SIDEREAL` for every standard ayanamsha. A missing call to
`swi_cartpol_sp` left the ecliptic cartesian coordinates zeroed before the ayanamsha was applied.
Fixed for every ayanamsha except `SE_SIDBIT_ECL_T0` and `SE_SIDBIT_SSY_PLANE`, which took a
different path and were already correct.

One more port defect, present since long before 2.10.03: `swe_cs2lonlatstr` transposed its
hemisphere letter and its degrees units digit on every call. `swe_cs2lonlatstr(1234567, 'p', 'm')`
returned `"p325'46"`; the correct string, which the C has always produced, is `"3p25'46"`.

Now the model changes, which are upstream's doing and not a bug being fixed. `eclipse_how`'s
`attr[0]` and `attr[2]` are fractions now, not percentages (`swecl.c:1067-1087`): an eclipse that
used to report `100` reports `1`. Multiply by 100 if your code expects a percentage, and recheck
any threshold comparison written against a number above 1. `swe_pheno` and `swe_pheno_ut` compute
Moon and Mercury-through-Neptune magnitudes with the Mallama 2018 model instead of Hilton 2005;
apparent diameter and phase angle are unaffected. House cusps move at every latitude for Placidus
and Gauquelin, because 2.10.03 iterates `CalcH`'s pole-height calculation to convergence
(`niter_max = 100`) where 2.08 always stopped after exactly two iterations; the old and new cusps
only agree where two iterations happened to already converge.

A further set of smaller numeric shifts, some upstream's and some this fork's, is itemized with
exact C line citations under "Breaking changes" below: the `swe_refrac_extended`/`calc_dip` predicate
and constant fix, the `swe_lun_eclipse_when` search-precision threshold, fixed stars being routed
onto `swe_rise_trans`'s slower path, and a handful of `serr` messages that now report a failure the
port used to swallow silently. That section is the reference; this one is the summary an upgrade
decision needs.

## Will your code still compile?

Mostly. A handful of things need a one-line fix.

`OnLoadFile` is gone. It is replaced by `SwissEph.FileProvider`, a settable
`IEphemerisFileProvider` with one method, `Stream Open(string path)`, where `null` means "not
found". If your handler just opened a real file by path, delete it: the library now reads
straight off the filesystem whenever `swe_set_ephe_path` points at a real, populated directory,
the same way the C reference does. That is new, and it closes a real defect: previously, having no
`OnLoadFile` subscriber meant every ephemeris file silently failed to load and every calculation
quietly fell back to Moshier, even with a correctly configured ephemeris path. Write an
`IEphemerisFileProvider` only when your source is not a file on disk at all, an embedded resource
being the usual case.

`swe_houses`, `swe_houses_ex`, `swe_houses_armc`, `swe_house_pos`, and `swe_house_name` each
gained an `int hsys` overload alongside the existing `char hsys` one, matching what `swephexp.h`
has always declared. Binary compatibility holds: anything already compiled against this library
keeps working unchanged. Two source-level cases do break. `Type.GetMethod("swe_house_name")` and
any other name-only reflection lookup now throws `AmbiguousMatchException`, because there are two
overloads where there used to be one; pass an explicit parameter-type array. `var f =
swe.swe_house_name;` no longer compiles under C# 10 and later: the compiler cannot pick an
overload and reports `CS8917`; declare an explicit delegate type instead.

`Dispose()` now actually disposes. Before, it called `swe_close()` and stopped there: nothing
marked the instance as disposed, so a call made after `Dispose()` silently reopened the ephemeris
files and returned a correct answer. It now throws `ObjectDisposedException` instead. If your code
used a `SwissEph` instance after disposing it, that was already a bug; it surfaces now rather than
staying hidden.

`PATH_SEPARATOR` widened from `char` to `char[]`. The value is unchanged (`{ ';' }`); code that
reads it as a single `char` needs to index `[0]` instead.

The NuGet package ID changed, to `SwissEphSharp`. The namespace did not. `SwissEphNet` on
nuget.org already belongs to the upstream author's own release, so this fork cannot publish under
that name. Everything under `SwissEphNet/CPort/` is a line-by-line transliteration of the C and
declares the `SwissEphNet` namespace; renaming it would touch every one of those files for no
functional reason and would break drop-in compatibility with code written against the original.
Migrating is the one line it sounds like: swap the `PackageReference` name and change nothing
else. `using SwissEphNet;` and every type name stay exactly as they are.

## What you gain

Beyond fewer wrong numbers, the 2.10.3 API surface adds functionality the 2.8.0.2-era port simply
did not have:

- `swe_houses_ex2` and `swe_houses_armc_ex2`: house calculations with per-cusp speed output and
  an explicit `serr` parameter.
- `swe_calc_pctr`: planetocentric position, one body as seen from another.
- `swe_get_current_file_data`: reports which ephemeris file is currently open and the time range
  it covers.
- Eight crossing functions: `swe_solcross`/`_ut`, `swe_mooncross`/`_ut`,
  `swe_mooncross_node`/`_ut`, and `swe_helio_cross`/`_ut`. None of these existed in the 2.08-based
  port at all.
- House system `'J'` (Savard-A).
- Planetary-moon and centre-of-body support (`SEFLG_CENTER_BODY`, `SEFLG_TEST_PLMOON`), reaching
  bodies numbered 9000 and up. This needs the `ephe/sat/` data set, which this repository does not
  ship.
- `SEFLG_TROPICAL`, `SE_ECL_HYBRID`, and three `SE_SIDBIT_*` constants (`SE_SIDBIT_ECL_DATE`,
  `SE_SIDBIT_NO_PREC_OFFSET`, `SE_SIDBIT_PREC_ORIG`).

None of this replaces existing API. It is purely additive.

## What was actually fixed, and how we know

Model changes aside, several defects in this list are bugs a caller could hit in ordinary use,
some dating back to the original 2014-2019 port.

The fixed-star cache mixed up which star it had cached. `swe_fixstar`, `swe_fixstar_mag`,
`swe_fixstar2`, and `swe_fixstar2_mag` each declare their own function-local cache in the C; the
port had collapsed all four into three shared fields, so calling one entry point for one star
could return a different, previously-cached star's position under another entry point. Each
function now keeps its own cache.

Star and heliacal lookups broke under a Turkish locale. `ToLower()` on `"JUPITER"` produces
`"jupıter"` (dotless i) under `tr-TR`, so a name match against `"jupiter"` silently failed, and
`swe_vis_limit_mag`'s Moon special case never matched a capitalized `"Moon"` either. Both now
lowercase ASCII-only, matching the C's own loop.

`Programs/SweTest` crashed on any longitude reaching 100 degrees or more. Its degree formatter was
one character narrower than the C's own field width, so the routine that splices in a minus sign
wrote one byte before its own buffer and threw. Fixed by restoring the leading space the C's
format string has.

Thirteen call sites threw where the C, using `atoi`/`atof`, returns zero for input it cannot
parse. One of them made `swe_heliacal_ut` throw a `FormatException` for any non-numeric object
name. Another turned a data file's own header line into an unhandled exception, because the C
uses `atoi`'s zero return as its own signal to skip that line.

`swe_house_pos` threw `IndexOutOfRangeException` on every Gauquelin-sector (`hsys = 'G'`) call:
its internal cusp buffer was one element short of what upstream's own 2.10.03 array-size fix
requires.

How this is checked, and what checking it does and does not prove. The port's output is compared
field by field against Astrodienst's own C, built from the same source and run against the same
ephemeris files. On Windows (MSVC) and Linux (gcc), all 17,064<!--doccount:grid-total-combined--> rows in that comparison (14,820<!--doccount:grid-analytic-total-->
calls that need no ephemeris file plus 2,244<!--doccount:grid-files-total--> that read the shipped `.se1` files) come back
bit-identical, not merely close; the tracked difference lists for both are empty. macOS (clang,
arm64) matches too, once clang is told not to substitute its own math builtins for individual libm
calls (`-fno-builtin`). None of that proves agreement between platforms: comparing the port's own
frozen output, generated on Windows and on Linux from the same commit, finds 66,342 of 3,547,367
compared fields differing at all, 5,394 of them beyond the shipped tolerance. That divergence is
each platform's own math library disagreeing with itself in the last few bits, the same thing two
independently built C programs would show; it is not evidence against the port.

Separately, the port's output is checked against Astrodienst's own 2.10.03 test suite (`setest`),
12,757 iterations across ten functional areas. 1,427<!--doccount:known-fail-total--> of those still fail: 714<!--doccount:known-fail-value-mismatch--> because the answer
is outside the tolerance Astrodienst's own suite allows, and 713<!--doccount:known-fail-data-missing--> because a required data file (a
JPL ephemeris, or an ephemeris era this repository does not ship, roughly years 1200 to 2399) is
not present, not because the answer is wrong. That is the honest state of it: strong on everything
it has been checked against, not yet at full parity with Astrodienst's own reference corpus.

# Breaking changes

## V:2.10.3

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
- **`OnLoadFile` is gone; ephemeris files are read from disk by default.** The event
  (and `LoadFileEventArgs`) is replaced by `SwissEph.FileProvider`, a settable
  `IEphemerisFileProvider` (`Stream Open(string path)`, null meaning "not found"). A
  caller that never subscribed to `OnLoadFile` used to get every ephemeris file reported
  as missing and every calculation silently downgraded to Moshier, even with a real,
  populated ephemeris directory configured via `swe_set_ephe_path`; now that every
  target framework this library ships (`netstandard2.0`, `net8.0`, `net10.0`) has full
  filesystem access, no `FileProvider` set means the real filesystem is used, the same
  way the C reference itself behaves. Most existing `OnLoadFile` handlers that just
  opened a real file by path can be deleted outright -- `swe_set_ephe_path` alone is
  now sufficient. A handler whose source genuinely is not a file (an embedded resource,
  for instance) should be rewritten against the new interface. `SwissEph.PATH_SEPARATOR`
  also widens from `char` to `char[]` (still `{ ';' }`) to support this; see
  `docs/known-issues.md`'s OnLoadFile entry for the full detail and the DefaultFileProvider
  static escape hatch for harnesses that construct many instances.

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

## Numerical compatibility

This library has been validated against the official Swiss Ephemeris C library.

On modern .NET (.NET 8 and later), it produces bit-identical results for the validated test suite
on every platform tested, each against a C reference built on that same platform:

| Platform | C reference | Result |
|---|---|---|
| Windows x64 | MSVC 19.51, `/O2 /fp:precise /MD` | 17,064 of 17,064 rows bit-identical |
| Linux x64 (Ubuntu 24.04) | gcc 13.3.0, `-O2` | 17,064 of 17,064 rows bit-identical |
| macOS arm64 | clang, `-O2 -ffp-contract=off -fno-builtin` | 17,064 of 17,064 rows bit-identical (gated) |

The agreement is exact rather than close because the port and the C reference call the same libm
on a given platform: `ucrtbase.dll` on Windows, glibc on Linux, Apple's libSystem on macOS. The
macOS build needs `-fno-builtin`: without it, clang substitutes its own math builtins for some
libm calls (fusing an adjacent `sin`/`cos` pair into one `__sincos`, for instance), and Apple's
`__sincos` does not return bit-identically to calling the two functions separately the way the
port does. With `-fno-builtin`, both grids are bit-identical there too.

Windows and Linux do not produce the same numbers as each other, and cannot be made to. `Math.Sin`
and its siblings bind to whatever libm the platform provides, at run time, so this is the same
divergence two identically-built C programs would show. Comparing the Windows-generated
characterization baseline against Linux gives 3,547,367 numeric fields with 66,342 (1.87%)
differing and 5,394 beyond the shipped tolerance. That is why the baseline is locked to the
platform that generated it and the cross-platform CI job reports drift without gating on it, and
it is a statement about libm rather than about this port. The baseline has not been generated on
macOS, so this same field-by-field comparison has not been run there.

When the `netstandard2.0` build is executed on .NET Framework 4.8, small floating-point
differences may occur, because that runtime's implementations of transcendental functions (for
example `Math.Sin` and `Math.Tan`) are not bit-identical to those of modern .NET. In the current
validation suite this results in a maximum observed difference of 2 ULP, for example on
`FICT_CUPIDO`.

The table above is the bit-exact oracle's own result; see "Bit-exact oracle" below for the tooling
behind it and what it proves that the other two verification instruments in this README cannot.

## Package name

This project carries three names, and meeting them separately looks like something is broken.
It is not:

- The **repository** is `Tim81/SwissEphNet`, a fork of `ygrenier/SwissEphNet`, and keeps that
  name.
- The **NuGet package ID** is `SwissEphSharp`. The `SwissEphNet` ID on nuget.org belongs to the
  upstream author's own release, and this fork cannot publish under it.
- The **namespace and assembly name** stay `SwissEphNet`. Every file under `SwissEphNet/CPort/`
  is a line-by-line transliteration of the Swiss Ephemeris C source and declares that namespace;
  renaming it would touch every one of those frozen files for a cosmetic reason. Keeping it also
  means the library stays a drop-in replacement for code written against the original namespace.

Migrating from the old package is the one line it sounds like: replace the `PackageReference` for
`SwissEphNet` with one for `SwissEphSharp` and change nothing else. `using SwissEphNet;` and every
type name are unaffected.

This fork is not published or endorsed by Yan Grenier or Astrodienst. See "About this repository"
above and `NOTICE` for the credit both are owed.

## Create an instance

SwissEphNet.SwissEph is ```IDisposable``` so you can use it with an ```using``` statement.

```C#
using (var sweph = new SwissEphNet.SwissEph()) {
    // Use it
}
```

## Loading files

By default, SwissEphNet reads ephemeris files straight from the real filesystem: point
`swe_set_ephe_path` at a directory the way the C reference does, and nothing else needs
configuring.

For a source that is not a real file on disk, e.g. an embedded resource, set
`SwissEph.FileProvider` to an `IEphemerisFileProvider`:

```C#
using (var sweph = new SwissEphNet.SwissEph()) {
    sweph.FileProvider = new MyEmbeddedResourceProvider();
    // Use it
}
```

`IEphemerisFileProvider` has a single method, `Stream Open(string path)`, returning
`null` for "not found". `SwissEph.DefaultFileProvider` sets the provider every
subsequently-constructed instance starts with, for a harness that creates many instances
and needs every one of them configured the same way without setting `FileProvider`
individually on each. See the "V:2.10.3" entry above for what this replaces
(`OnLoadFile`) and why.

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
The baseline is Windows-specific by design; see that file's "Platform lock" section. This is not
in tension with "Numerical compatibility" above showing both Windows and Linux bit-identical
against their own C: the baseline is a Windows-generated golden master, so comparing Linux output
against it measures libm divergence between platforms (glibc versus `ucrtbase.dll`), not a defect
in the port. Comparing each platform against its own C reference, as "Numerical compatibility" and
"Bit-exact oracle" below do, is what actually tests the port. Numerical-stability findings turned
up while building the baseline are in `docs/known-issues.md`.

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
lists 1,427<!--doccount:known-fail-total--> failing iterations (11,330 passing, 88.8%), and the known-fail list remains the work
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

# Bit-exact oracle

The characterization baseline above proves self-consistency, and the correctness oracle proves
agreement with Astrodienst's own published reference values within the tolerances Astrodienst
itself ships. Neither can prove the strongest claim this project makes: that for a given input,
the port and Astrodienst's own C compute the identical bits. That is what this third instrument
is for, and it is the source of the "Numerical compatibility" table above.

- `Tools/OracleGrid` holds the two input grids: `grid-analytic.tsv` (14,820<!--doccount:grid-analytic-total--> rows, `SEFLG_MOSEPH
  swe_calc`/`swe_calc_ut` plus `swe_houses`/`swe_houses_armc`, opening no ephemeris file) and
  `grid-files.tsv` (2,244<!--doccount:grid-files-total--> rows, `SEFLG_SWIEPH swe_calc`/`swe_calc_ut`, the `swe_fixstar` family,
  and `swe_get_planet_name`, reading the shipped `.se1`/`sefstars.txt` files).
- Each grid is replayed by a pair of drivers built from the same inputs: `Tools/CReference/sedump.c`,
  compiled against Astrodienst's own vendored 2.10.03 C, and `Tools/OracleDump`, built against this
  port. Both write every hex-encoded field, the return code, and the `serr` text to a TSV.
- `Tools/OracleVerify` compares the two dumps field by field. A row that is not an outright match
  has to be listed in `Tests/oracle/known-diff.tsv` or `known-diff-files.tsv`, under a category
  that still fits and at a magnitude no worse than the last time that entry was regenerated -- both
  lists are currently empty.
- `scripts/verify-oracle.ps1` is the gate: it also checks that the committed dumps still reflect
  what is on disk (the two grids, the port's own source, and the C reference binaries), and, when a
  grid's known-diff list is empty, that the two dump files are byte-for-byte identical at the
  file level, not merely equal per `OracleVerify`'s own field comparator.

Run `scripts/run-oracle-dump.ps1` to regenerate the dumps, then `scripts/verify-oracle.ps1` to
check them. See `docs/compliance-2.10.03.md` for the current numbers on both Windows and Linux,
what this instrument does and does not cover, and the same record for the other two instruments
above.

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
