# SwissEphSharp

A C# port of the Astrodienst [Swiss Ephemeris](http://www.astro.com/swisseph/), the astronomical
library used to compute planetary positions, house cusps, eclipses and related quantities. It is a
line-by-line translation of Astrodienst's C source rather than a reimplementation, so the function
names, arguments and return values are the ones in Astrodienst's own
[programming documentation](http://www.astro.com/swisseph/swephprg.htm).

`swe_version()` reports `2.10.03`. Targets `netstandard2.0`, `net8.0` and `net10.0`. No
dependencies.

## Read the license first

Swiss Ephemeris, and therefore this library, is dual-licensed, and one of the two options has a
condition that catches people out. You must choose one:

- **AGPL-3.0.** Free, but with a network clause. If you run this library as part of a service that
  users reach over a network, a web app, an API, a SaaS product, the AGPL requires you to offer
  those users the complete corresponding source of your **whole service**, not just this library.
  Operating the service is the trigger; you do not have to distribute a binary to anyone. If that
  does not work for your project, AGPL is not your option.
- **Swiss Ephemeris Professional License.** A commercial license bought from
  [Astrodienst](http://www.astro.com/swisseph/), without the source-disclosure obligation.

This is not a choice this package makes for you, and it follows Astrodienst's own relicensing of
Swiss Ephemeris. `LICENSE`, `agpl-3.0.txt` and `NOTICE` ship at the root of this package.

## Install

```
dotnet add package SwissEphSharp
```

The package ID is `SwissEphSharp`; the namespace and every type name stay `SwissEphNet`, so
`using SwissEphNet;` is what you write. The `SwissEphNet` package ID on nuget.org belongs to the
upstream project's own release, which is why this fork cannot publish under it.

## Your first calculation

This computes the Sun's position on 1 January 2020. It needs no data files: `SEFLG_MOSEPH` selects
the built-in analytic ephemeris, which is computed rather than read from disk.

```csharp
using System.Globalization;
using SwissEphNet;

using var swe = new SwissEph();

// Julian day for 2020-01-01 00:00 UT, Gregorian calendar.
double jd = swe.swe_julday(2020, 1, 1, 0.0, SwissEph.SE_GREG_CAL);

var xx = new double[6];
string serr = "";
int ret = swe.swe_calc_ut(jd, SwissEph.SE_SUN, SwissEph.SEFLG_MOSEPH, xx, ref serr);

if (ret < 0)
    Console.WriteLine($"error: {serr}");
else
    Console.WriteLine("Sun longitude: "
        + xx[0].ToString("F6", CultureInfo.InvariantCulture) + " degrees");
```

Output:

```
Sun longitude: 280.009518 degrees
```

`xx` comes back as longitude, latitude, distance, then the three matching speeds. `swe_calc_ut`
takes Universal Time; `swe_calc` takes Ephemeris Time. A negative return means failure and `serr`
says why. `SwissEph` is `IDisposable`, hence the `using`.

## Which API to call: prefer the `2` variants

Several functions have a newer sibling with a `2` in the name, and for new code that is generally
what you want.

For fixed stars this is Astrodienst's own published advice: "For new projects, we recommend using
the new functions `swe_fixstar2_ut()` and `swe_fixstar2()`. Performance will be a lot better if a
great number of fixed star calculations are done." The same goes for `swe_fixstar2_mag` over
`swe_fixstar_mag`. If an existing project is slow on star lookups, replacing the old calls is the
fix. All six are here, old and new.

For houses, `swe_houses_ex2` and `swe_houses_armc_ex2` are new in 2.10.03 and give you two things
the older calls cannot: per-cusp and per-`ascmc` speeds, and an explicit `serr` out-parameter
instead of a bare return code. Astrodienst does not publish a "prefer these" recommendation for
them the way it does for fixed stars, so treat them as extra capability rather than a replacement:
reach for them when you want speeds or a diagnostic message, and stay on `swe_houses`/
`swe_houses_ex` otherwise.

There is no `2` variant of `swe_calc`, which is why the example above uses `swe_calc_ut`.

## Using real ephemeris files

For better accuracy, or for bodies the analytic ephemeris does not cover, point
`swe_set_ephe_path` at a directory of Astrodienst's `.se1` files and ask for `SEFLG_SWIEPH`:

```csharp
using var swe = new SwissEph();
swe.swe_set_ephe_path("/path/to/ephe");

var xx = new double[6];
string serr = "";
int ret = swe.swe_calc_ut(jd, SwissEph.SE_SUN, SwissEph.SEFLG_SWIEPH, xx, ref serr);
```

Files are read straight from the filesystem. Astrodienst publishes them in the Swiss Ephemeris
repository at [`aloistr/swisseph/ephe`](https://github.com/aloistr/swisseph/tree/master/ephe),
mirrored at [`ephe.scryr.io/ephe`](https://ephe.scryr.io/ephe). `sepl_18.se1`, `semo_18.se1` and
`seas_18.se1` cover 1800 to 2399 and are enough for most work; each file holds six centuries
starting at the century in its name.

**Watch for the silent fallback.** When a file is missing the library falls back to the analytic
ephemeris, notes it in `serr`, and returns a number that looks perfectly reasonable. Compare `ret`
against the flag you asked for: ask for `SEFLG_SWIEPH` and get `SEFLG_MOSEPH` back and your path is
wrong. If you read `serr` instead, null-check it, because it comes back `null` from some successful
calls and `""` from others.

When your data is not a file on disk, an embedded resource being the usual case, implement
`SwissEph.IEphemerisFileProvider` (one method, `Stream Open(string path)`, returning `null` for
"not found") and assign it to `SwissEph.FileProvider`.

## Threads

Create one `SwissEph` instance per thread. A single instance is not safe to share across threads.
Separate instances are independent and can run concurrently. There is no async API; the calls are
synchronous, so wrap a long sweep in `Task.Run` to keep it off a UI thread.

## Upgrading from SwissEphNet 2.8.0.2

Four things to check, in the order that matters:

1. **The license changed.** 2.8.0.2 was GPL-2.0-or-later; this is AGPL-3.0 or the Professional
   License. See above. If you run it server-side and cannot publish your source, this is a
   licensing decision before it is a technical one.
2. **The package ID changed** from `SwissEphNet` to `SwissEphSharp`, and the assembly is now
   `SwissEphSharp.dll`. Source that only calls the public API needs no change beyond the
   `PackageReference`. Anything that hardcodes the assembly name needs updating.
3. **Your numbers will change.** Some of that is Astrodienst's own model changes between 2.08 and
   2.10.03; some is port defects that are now fixed, including one that gave every
   `SEFLG_SWIEPH` position the wrong obliquity. Eclipse magnitude and obscuration are now
   fractions rather than percentages, so a value that read `100` reads `1`.
4. **`OnLoadFile` is gone**, replaced by `SwissEph.FileProvider`. If your handler just opened a
   real file by path, delete it: `swe_set_ephe_path` alone now reaches those files.

Target frameworks moved too. 2.8.0.2 shipped `net40` and `netstandard1.0`; neither is supported
here. .NET Framework 4.6.1 and later resolve `netstandard2.0`.

The [full README](https://github.com/Tim81/SwissEphNet/blob/release/2.10.03/README.md) documents
every breaking change with the C source line each one corresponds to.

## Credits

The original C-to-C# port is [Yan Grenier](https://github.com/ygrenier/SwissEphNet)'s work
(2014-2019). This fork, maintained by Timothy van der Ham, continues it: modernised target
frameworks, the 2.10.03 upgrade, and a number of bug fixes in the port. The Swiss Ephemeris itself
is by Astrodienst. See `NOTICE` in this package for the full attribution.

This package is not published or endorsed by Yan Grenier or Astrodienst.

## More

- [Repository and full documentation](https://github.com/Tim81/SwissEphNet)
- [Report an issue](https://github.com/Tim81/SwissEphNet/issues)
- [How the numbers are verified](https://github.com/Tim81/SwissEphNet/blob/release/2.10.03/docs/compliance-2.10.03.md)
- [Known issues](https://github.com/Tim81/SwissEphNet/blob/release/2.10.03/docs/known-issues.md)
