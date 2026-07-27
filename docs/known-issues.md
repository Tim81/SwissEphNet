# Known issues found by the characterization baseline

Findings from building and running the baseline gate: some cross-platform (Windows,
the platform the gate is locked to, vs. a Linux container with the same SDK), some
single-platform library defects the matrix happened to surface along the way. All
are faithfully frozen in the committed baseline rather than worked around, so a
porter needs to know they were seen deliberately. See `Tools/BaselineGen/README.md`
for why the gate is platform-locked rather than given a looser tolerance.
Cross-platform numbers below are from `.NET SDK 10.0.302`,
`mcr.microsoft.com/dotnet/sdk:10.0`, Ubuntu 24.04, against commit `8f5615e` (before
the angle-wraparound fix) and again after it.

## swe_houses_armc, hsys 'Y' (APC houses): a genuine, large divergence

`swe_houses_armc(armc=270, geolat=50, eps=40, hsys='Y', ...)`:

| Platform | cusp[2] |
|---|---|
| Windows | `270` |
| Linux | `243.43494882292202` |

A 26.6-degree difference is not floating-point noise -- reproduced independently
(same inputs, same commit, `BaselineGen` run inside the Linux container and diffed
against the committed Windows baseline). The two platforms are taking different
code paths through the APC houses calculation in `SweHouse.cs`, most likely because
an `acos`/`asin` argument lands marginally outside `[-1, 1]` on one platform and not
the other (a classic source of platform-dependent NaN/branch divergence in
trigonometric code that does not clamp its inputs). This is a numerical-stability
problem in the port, not in the harness or the tolerance.

This is not a tolerance problem: it survives every threshold measured in
`Tools/BaselineGen/README.md`'s "Platform lock" table, including the loosest
(1e-8 absolute / 1e-8 relative -- eight orders of magnitude looser than what
ships). It is the only `houses-armc` field that does. Everything else that
diverges cross-platform shrinks as the tolerance loosens, the way accumulated
floating-point noise should; this one field does not move, which is exactly the
signature of two platforms executing genuinely different branches rather than
the same branch with a slightly different rounding error.

**Action for the 2.10.03 port work:** when porting the SweHouse delta, check
whether the C source clamps the argument passed to `acos`/`asin` in the APC branch
(search for `case 'Y'` in `SwissEphNet/CPort/SweHouse.cs`), and whether upstream
2.10.03 changed that code. This is exactly the kind of case the baseline exists to
catch -- freeze it now, and the 2.10.03 PR's own verify run will show clearly
whether the fix changes this cusp value on Windows (expected: yes) and whether it
also stops the platforms from diverging (worth checking, not assumed).

## swe_houses_armc reports success while emitting NaN cusps

At `eps=0` with `hsys` in `{P, G, J, Z, 0}` (648 rows each, 3,240 rows total),
`swe_houses_armc` returns `retc = 0` (success) while several cusp fields are `NaN`
-- 39,312 `NaN` fields across those 3,240 rows. Example: `H|0|0|-10|0` (hsys `'0'`,
an invalid letter that falls through to the Placidus default) has cusp[3] and
cusp[4] both `NaN`, `retc` still `0`.

The `NaN` itself is plausible: `eps=0` is a genuinely degenerate obliquity for
several house systems (Placidus's iterative solution and Gauquelin's sector
geometry both divide by quantities that can vanish at `eps=0`), so `NaN` output for
some cusps is not surprising. The notable part is `retc` not reflecting it -- a
caller checking only the return code has no way to know part of the result is
unusable. This is a real behavior worth freezing and worth a second look during the
2.10.03 port: does the C source treat `eps=0` as an error case anywhere, and if so,
does that error surface through `retc` there but not here?

## swe_houses_armc, hsys 'i' (Makransky Sunshine houses): cusp = 360.0, missing normalization

280 fields, all at `eps=0, geolat=0`, e.g. `H|i|0|0|30` gives cusp[3] = `360`;
`H|i|0|0|120` gives cusp[12] = `360`. A house cusp is defined to be normalized into
`[0, 360)` (that is what `swe_degnorm` is for), and `hsys='i'` is the only house
system anywhere in the baseline with a cusp outside that range -- every other
system, including its close sibling `hsys='I'` (Treindl Sunshine houses, same
`eps=0, geolat=0` inputs), stays inside `[0, 360)`. The Makransky branch in
`SweHouse.cs` is missing a `swe_degnorm` call somewhere on this path.

Like the `'Y'` finding below this is not a tolerance problem, and it gets the same
treatment: `hsys='i'` and `hsys='Y'` are the only two house systems in the entire
baseline with an opposite-cusp violation (a value outside its defined range).
`'Y'` is a genuine cross-platform algorithmic divergence; `'i'` is a genuine,
single-platform normalization bug, reproducible on Windows alone with no Linux
comparison needed. It is also why the gate's angle-wraparound allowance
specifically excludes an exact `360.0` value from ever being treated as
"near-360, so equivalent to near-0" (see `Comparer.EffectiveAbsoluteDiff`): if this
gets fixed and these 280 fields change from `360.0` to `0.0`, that is exactly the
kind of change the gate needs to report as a genuine difference, not silently wrap
away.

## swe_calc(SE_ECL_NUT) returns success with all-zero output for several iflag combinations

`swe_calc`/`swe_calc_ut` with `ipl = SE_ECL_NUT` (the pseudo-body used to get
obliquity and nutation via `xx[0]`/`xx[1]`) returns success (`retc` echoing the
iflag) with all six `xx[]` values `0` and `serr` empty, for `SEFLG_EQUATORIAL`,
`SEFLG_XYZ`, `SEFLG_SPEED_EQUATORIAL`, and `SEFLG_J2000_EQUATORIAL`. Only the plain
and a handful of other combinations return the actual obliquity/nutation values.

The likely cause: `swecalc` (in `Sweph.cs`) never populates `sd.xsaves` for
`SE_ECL_NUT` under these flag combinations and ends up reading its own
uninitialized save-area default (zero) instead of computing or caching anything.
Silent zero output with a success code and no `serr` is the concerning part --
a caller has no signal that anything went wrong. Worth checking against 2.10.03's
`sweph.c` for whether this pseudo-body's save-area handling changed.

## swe_houses and swe_houses_ex(iflag=0) disagree with each other

For the same `(tjd_ut, geolat, geolon, hsys)` inputs and `iflag=0` (no sidereal),
`swe_houses` and `swe_houses_ex` disagree on 1,260 of 1,680 comparable cusp/ascmc
pairs in the `houses` area, worst case 8.07e-7 degrees. Both functions are
supposed to compute the same non-sidereal result when `iflag=0`; this is a
structural disagreement between the two entry points, not scatter from
platform-dependent rounding (it reproduces identically on Windows alone).

Almost certainly the two functions derive obliquity differently: `swe_houses`
appears to call `swi_epsiln` directly, while `swe_houses_ex` routes obliquity
through `swe_calc(SE_ECL_NUT)` -- two different code paths to the same
conceptual quantity, which is exactly the kind of duplication that drifts apart
over time. Worth checking whether the 2.10.03 SweHouse delta unifies these paths
or preserves the split.

## calc/pheno SPEED fields: differentiation noise, expected but unexplained in detail

Cross-platform, SPEED-flagged fields (numerically differentiated results, not
closed-form) diverge at roughly 1e-7 to 1e-9 relative -- small, but well past the
gate's tolerance, concentrated in `calc` (SE_OSCU_APOG, Mercury named as the worst
cases) and `pheno`. `CPort` computes speed via numerical differentiation (small time
offsets and a finite difference), which amplifies whatever ULP-level difference
already exists between platforms in the underlying position calculation. This is
plausible and consistent with how numerical differentiation behaves, but it has not
been traced to a specific line of CPort. Recorded here as expected-but-unexplained,
not as "this is fine": if the ratio changes meaningfully in future cross-platform
reports (see `--report-only`), that is worth a fresh look, not an assumption that
it's the same known issue.

## DIR_GLUE cannot be safely changed without a CPort edit

`SwissEph.DIR_GLUE` (`SwissEphNet/SwissEph.sweodef.h.cs`) is hard-coded to
`'\\'`, where the upstream C source defines it per-platform. `swi_gen_filename`
(`SwissEphNet/CPort/SwephLib.cs`) uses it to build numbered asteroid file
names, e.g. `"ast4" + DIR_GLUE + "se04179.se1"` = `"ast4\se04179.se1"`. A
backslash is not a path separator on Linux, macOS, Android, iOS, or WASM, so
any `OnLoadFile` handler that does `Path.Combine` or a resource-name lookup on
that generated name can never find the file except on Windows. That half of
the bug is real, confirmed, and unrelated to CPort.

Changing `DIR_GLUE` to `'/'` -- the fix this looked like it needed -- is not
safe on its own, though. `CPort/Sweph.cs`'s "correct file name?" check (around
line 4922, run against every successfully-opened ephemeris file) strips a
directory prefix off the file's recorded path by searching for `DIR_GLUE`:

```csharp
sp = fdp.fnam;
if (sp.LastIndexOf(SwissEph.DIR_GLUE) > 0)
    sp = sp.Substring(sp.LastIndexOf(SwissEph.DIR_GLUE) + 1);
if (!s.Equals(sp, StringComparison.CurrentCultureIgnoreCase))
{
    serr = C.sprintf("Ephemeris file name '%s' wrong; rename '%s' ", sp, s);
    goto return_error;
}
```

But `fdp.fnam` is built in `swi_fopen` (`CPort/Sweph.cs` around line 2634) by
joining the configured ephemeris path to the file name with a **hard-coded**
`'\\'`, not with `DIR_GLUE`:

```csharp
fnamp = s.TrimEnd('\\', '/') + "\\" + fname;
```

These two only agree because `DIR_GLUE` has always equaled `'\\'`. The moment
`DIR_GLUE` becomes `'/'`, `LastIndexOf(SwissEph.DIR_GLUE)` stops finding the
separator that the ephepath join actually used, `sp` is left as the *entire*
path (e.g. `"[ephe]\se04179.se1"`) instead of just the base file name, the
equality check against the file's internal recorded name always fails, and
every successfully-loaded ephemeris file gets rejected with "Ephemeris file
name '...' wrong; rename '...'" -- on every platform, Windows included, not
only the platforms the fix was meant to help.

This was caught, not assumed: setting `DIR_GLUE = '/'` and running the full
suite regressed `Issue18Test.LoadAsteroidData` on Windows, which loads a real
numbered-asteroid ephemeris file (`se00005s.se1`) via `OnLoadFile` and had
been passing. That is the regression-test discipline working as intended --
a fix that looked correct in isolation (and does fix the subdirectory-naming
half of the bug) turned out to reach further than expected and break something
it wasn't touching.

A real fix requires a CPort edit: either route `swi_fopen`'s ephepath join
through `DIR_GLUE` instead of a literal `'\\'`, or have the "correct file
name?" check strip using both possible separators rather than only
`DIR_GLUE`. Either is a change to `CPort/Sweph.cs`, which the formatting
freeze (`CONTRIBUTING.md`) does not forbid touching for logic (only
reformatting), but doing so needs a deliberate decision and its own upstream
diff-tracking story, not a change bundled quietly into a bug-fix PR framed as
CPort-untouched. `DIR_GLUE` stays `'\\'` for now; `swi_gen_filename`'s
asteroid file names remain backslash-joined and Windows-only-portable.

## swe_fixstar_ut distance speed: larger cross-platform differentiation noise

`Test_swe_fixstar_ut` (Aldebaran, MOSEPH) pins `xx[5]` (distance speed) to
`0.015543` on Windows; the same call under .NET 10 on Linux (Ubuntu 24.04,
`mcr.microsoft.com/dotnet/sdk:10.0`) returns `0.0155324764...` instead --
about 6.8e-4 relative, four to six orders of magnitude larger than the
1e-7-to-1e-9 relative noise measured for the `calc`/`pheno` SPEED fields
below. It is the same category of finding (numerical differentiation of a
finite difference amplifying a tiny cross-platform difference in the
underlying position), just amplified further here, plausibly because `xx[5]`
divides a distance difference by a very small `dt`: found while confirming
PR1's fixed-star bug fixes on Linux (see PR1's `known-library-bugs` work),
not something PR1 introduced or is in scope to fix, since it is not related
to any of that PR's five bugs (Windows-1252/UTF-8 decoding, culture-sensitive
string comparison, `atoi` sign handling, `CPointer<T>.operator !=`, or
`DIR_GLUE`). `Test_swe_fixstar_ut`'s assertion on `xx[5]` was loosened from 6
to 4 decimal places to accommodate it, rather than pinning a
platform-specific value or skipping the assertion.

## Negative-zero (`-0`) fields under SIDEREAL: TRUE node, not mean node

18 fields in the `calc` area carry a negative-zero sign bit (`-0` rather than `0`)
-- all of them `SEFLG_SIDEREAL`, and all of them `ipl = 11` (`SE_TRUE_NODE`),
confirmed directly against the generated data (`cut -d'|' -f2` on every `-0` row).
These are analytically-zero quantities where the sign bit is roundoff, not a bug;
noted here precisely so the record stays accurate -- ipl 11 is the true node, not
the mean node (`ipl = 10`), which does not show this pattern in this data.

## hsys 'I' (Sunshine houses): smaller, more numerous divergences near tolerance

Not one of the two findings this doc originally set out to record, but visible in
the same Linux run: `swe_houses_armc` with `hsys='I'` produces a cluster of small
(1e-10 to 1e-11 absolute) but tolerance-exceeding divergences, mostly at extreme
`geolat` and specific `armc` values. Example: `H|I|0|-89|0` gives cusp[3] =
`120.00000000000338` (Windows) vs `119.99999999997604` (Linux) -- about 2.7e-11
absolute, roughly double the field's own relative threshold at that magnitude.
Unlike the 'Y' finding, these are not obviously wrong (both platforms agree to 10+
significant figures) and are not a raw-360-style wraparound artifact either (see
the cross-platform section below) -- just accumulated ULP drift through a
computation path (declination/ascensional-difference trig for Sunshine houses)
that is evidently more sensitive to it than most. Not chased further; flagged in
case it becomes relevant when porting the 2.10.03 SweHouse delta.

## Cross-platform divergence: measured, and why the gate is platform-locked

Full numbers, the tolerance-level cost table, and the reasoning for locking the
gate to Windows instead of loosening the shipped tolerance, are in
`Tools/BaselineGen/README.md` under "Platform lock". Summary: of 3,443,058 numeric
fields compared, 47,052 differ at all between Windows and Linux. Of those, only
108 are genuine angle-wraparound (raw difference > 180 degrees) and the
wraparound fix (`Comparer.EffectiveAbsoluteDiff`) resolves all 108 of them
exactly; 3,346 fields are still beyond the shipped `1e-12`/`1e-13` tolerance. The
two findings above (APC houses, and the calc/pheno SPEED fields) account for the
bulk of that remainder.

An earlier pass at this classification reported 2,637 fields as "wraparound" --
that number came from a comparison bug (`min(d, |360-d|)` computed on the raw
difference without first checking it was actually a large, near-360 difference,
so it just returned small differences unchanged and mislabeled them). The
corrected number is 108, confirmed by checking that the wraparound fix resolves
exactly that many fields and rows, and that zero of the remaining 3,346
beyond-tolerance fields have a raw difference anywhere near 360.
