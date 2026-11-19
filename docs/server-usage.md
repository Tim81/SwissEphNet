# Using SwissEph in a server

This page is for developers who call SwissEphNet from a web service or API, for example an
ASP.NET application, where the arguments to a calculation come from a request. The library is a
translation of Astrodienst's C code and was written for desktop and batch use. It assumes that the
caller passes sensible values. A server cannot assume that, so a few precautions belong in the
calling code.

The library is managed code with no `unsafe`, `stackalloc` or `Marshal`, so there is no
memory-safety class of problem. No star name, asteroid number, moon number, fictitious body number
or astro-model string becomes part of a file path; file names are built from constants and numbers.
The risks are the ones listed below: calls that do not return, long searches, exceptions, shared
instances and what error text discloses.

## Guidance

1. **Validate input.** Some input classes make a call run forever or throw, so keep them out.
   - Reject non-finite numbers. A NaN date or latitude can make a call spin, and ASP.NET query and
     form binding accepts `NaN` and `Infinity` (`System.Text.Json` rejects NaN by default).
   - Clamp dates to the range of the ephemeris files you ship. A search for an event that never
     happens runs until the ephemeris range ends.
   - Use allow-lists for bodies, flags, house systems and sidereal modes, not deny-lists. In
     particular, do not pass `SEFLG_TOPOCTR` or `SEFLG_HELCTR` to the crossing functions, do not
     pass `SE_MOON` or `SE_EARTH` to the occultation functions, and never pass negative body or
     mode numbers.
   - Cap string lengths, star names above all. The legacy `swe_fixstar` echoes an overlong name
     into `serr` in full (a 10 MB name peaked near 108 MB); `swe_fixstar2` truncates it.
2. **Bound the work.** .NET cannot abort a running thread, so a `CancellationToken` or a `Task`
   timeout leaves a spinning call holding a core. Run long searches (eclipse and occultation
   location, heliacal events, crossings) in a worker process you can kill, or use
   `SE_ECL_ONE_TRY`, which the Swiss Ephemeris manual documents for eclipse and occultation
   searches, and drive the loop yourself. Limit concurrency.
3. **Instances.** Use one `SwissEph` instance per request and never share one between concurrent
   requests. Two threads on one instance returned wrong results silently, with no exception, in
   1,986 of 20,000 calls; separate instances returned none. In an async server a request can move
   between threads, so "one instance per thread" is the wrong rule. An instance costs about 1.3 ms
   and 118 KB to create. Do not pool instances and rely on `swe_close()` to reset them: sidereal
   mode, topocentric position, `delta_t_userdef`, astro models, the ephemeris path and the JPL file
   persist, and `swe_close()` does not clear `delta_t_userdef`.
4. **Errors.** Wrap calls in `try`/`catch` and map exceptions to a 4xx response. Check return
   codes. Never return `serr` verbatim: it can contain server paths, such as
   `SwissEph file 'se00433s.se1' not found in PATH '...'`.
5. **Configuration.** Set the ephemeris path or a `FileProvider` explicitly. With no provider the
   library reads the real file system, and the default path is `\sweph\ephe\` on Windows and
   `.:/home/ephe/` on Linux (the working directory first); the `SE_EPHE_PATH` environment variable
   overrides it. Never take the ephemeris path, the JPL file name or data files from users.
   `swe_set_ephe_path` accepts UNC paths (on Windows that starts an outbound SMB attempt), and
   `swe_set_jpl_file` strips only `/`, so `..\` stays on Windows. Reject UNC paths. Do not accept
   user-supplied ephemeris or text data files: the line reader has no length limit and a few
   file-derived sizes are trusted.
6. **Keep the library up to date.** Later releases fix, among other things, the net10 NaN hangs in
   eclipse and occultation searches, the 180 MB first fixed-star load, and an exception on a
   non-seekable `FileProvider` stream. Check `CHANGELOG.md` for the release that carries each.

## Example

A per-request instance, an allow-list, finite-number checks, `try`/`catch`, and a fixed error
message instead of `serr`. `double.IsFinite` does not exist on `netstandard2.0`, so the check is
written out.

```csharp
using System;
using System.Collections.Generic;
using SwissEphNet;

public static class Positions
{
    // Allow-lists: only values the service has decided to offer.
    static readonly HashSet<int> Bodies = new HashSet<int>
    {
        SwissEph.SE_SUN, SwissEph.SE_MOON, SwissEph.SE_MARS
    };
    const int AllowedFlags = SwissEph.SEFLG_SWIEPH | SwissEph.SEFLG_SPEED;

    static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);

    // Returns false with a message that is safe to show to a client.
    public static bool TryCalc(double jdUt, int body, int flags, string ephePath,
        out double[] xx, out string clientError)
    {
        xx = new double[6];
        clientError = null;

        // Validate before the library sees anything. Also clamp jdUt to the range
        // of the ephemeris files you ship.
        if (!Finite(jdUt) || !Bodies.Contains(body) || (flags & ~AllowedFlags) != 0)
        {
            clientError = "Invalid request.";
            return false;
        }

        try
        {
            using (var swe = new SwissEph())            // one instance per request
            {
                swe.swe_set_ephe_path(ephePath);        // from configuration, never from users
                string serr = null;
                int rc = swe.swe_calc_ut(jdUt, body, flags, xx, ref serr);
                if (rc < 0)
                {
                    // Log serr on the server; it can contain file system paths.
                    Console.Error.WriteLine("swe_calc_ut: " + serr);
                    clientError = "Calculation failed.";
                    return false;
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            clientError = "Calculation failed.";
            return false;
        }
    }
}
```

## Known limits

- **Calls that do not return.** Some finite input with documented flags, and NaN, make some calls
  spin: certain flag and body combinations on the crossing functions (`SEFLG_HELCTR` on
  `swe_solcross`, `SEFLG_TOPOCTR` on `swe_mooncross_node`, `swe_helio_cross` for `SE_EARTH` with
  `SEFLG_TOPOCTR`), `swe_lun_occult_when_glob` with `ipl` of `SE_MOON` or `SE_EARTH`, and any of
  these with a NaN date or latitude. They are shared with the Astrodienst C library, and the port
  follows the C until upstream changes it; see [known-issues.md](known-issues.md), "Do not fix the
  port".
- **Long but finite searches.** Searches for events that never occur (an occultation of a star with
  an impossible eclipse-type filter, an osculating or interpolated apogee or true node as `ipl`,
  `when_loc` for a star that never rises at that latitude, heliacal events at high latitude) ran 25
  to 148 s forward and up to 414 s backward, allocating about 32 GB of short-lived memory while the
  peak working set stayed near 53 MB. The C library is two to three times faster but behaves the
  same. A larger ephemeris file set makes the search longer.
- **Exceptions.** Catchable `IndexOutOfRangeException`, `NullReferenceException` and
  `OverflowException` can escape the API for a negative ayanamsa or body number in some functions
  (`swe_get_ayanamsa_name`, `swe_pheno`, `swe_rise_trans` and `swe_gauquelin_sector` with `ipl` of
  -1), for some sidereal plus JPLHOR flag combinations in `swe_houses_ex2`, for
  `swe_cs2timestr(int.MinValue)`, and for arrays that are shorter than documented, or null.
- **Thread safety.** An instance is not safe for concurrent use, and the failure is silent wrong
  results. The static `SwissEph.DefaultFileProvider` is unsynchronized as well; set it once at
  startup.
- **State.** A reused instance keeps its previous settings, as listed under "Instances".
- **`serr` paths.** Error text can contain server paths, as listed under "Errors".

These measurements are from probes run against the current branch; they describe classes of input,
not a complete list.
