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
an invalid letter that falls through to the Placidus default) has cusp[2],
cusp[3], cusp[5], cusp[6], cusp[8], cusp[9], cusp[11], and cusp[12] all `NaN`,
`retc` still `0`.

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

## DIR_GLUE fixed: CPort/Sweph.cs:2634 was a mis-transliteration

`SwissEph.DIR_GLUE` (`SwissEphNet/SwissEph.sweodef.h.cs`) used to be
hard-coded to `'\\'`, where the upstream C source defines it per-platform.
`swi_gen_filename` (`SwissEphNet/CPort/SwephLib.cs`) uses it to build
numbered asteroid file names, e.g. `"ast4" + DIR_GLUE + "se04179.se1"` =
`"ast4\se04179.se1"` with the old value. A backslash is not a path separator
on Linux, macOS, Android, iOS, or WASM, so any `OnLoadFile` handler that does
`Path.Combine` or a resource-name lookup on that generated name could never
find the file except on Windows.

The first attempt to fix this by changing `DIR_GLUE` to `'/'` alone regressed
`Issue18Test.LoadAsteroidData` on Windows: `CPort/Sweph.cs`'s "correct file
name?" check (around line 4922, run against every successfully-opened
ephemeris file) strips a directory prefix off the file's recorded path by
searching for `DIR_GLUE`, but `swi_fopen`'s ephepath+filename join (around
line 2634) had been hard-coded to a literal `'\\'` instead of using
`DIR_GLUE`:

```csharp
fnamp = s.TrimEnd('\\', '/') + "\\" + fname;
```

That looked at first like a deliberate platform choice CPort couldn't own,
and the CPort formatting freeze (`CONTRIBUTING.md`) reads, on a fast pass, as
forbidding any edit there. It is not: checking the actual upstream C source
(2.08 `sweph.c:2362-2363`) shows the equivalent site uses `DIR_GLUE`, not a
literal backslash:

```c
if (*s != '\0' && *(s + j - 1) != *DIR_GLUE)
  strcat(s, DIR_GLUE);
```

So `CPort/Sweph.cs:2634`'s hard-coded `"\\"` is a mis-transliteration, not a
platform-specific deviation from the source. The proof it is an error rather
than a convention: the parallel site in `swe_set_ephe_path`
(`Sweph.cs:1514-1515`, corresponding to `sweph.c:1356-1357`, an identical C
pattern) was transliterated correctly, using `DIR_GLUE`. One site right, one
site wrong -- CPort's own internal inconsistency is the evidence, independent
of the C source lookup. Fixing `2634` to use `DIR_GLUE` (keeping
`TrimEnd('\\', '/')` as-is, since tolerating either separator on input is
harmless) restores line-for-line fidelity with the C source rather than
deviating from it, which is exactly what the freeze in `CONTRIBUTING.md` is
for protecting -- it was never a rule against correcting a transliteration
error, only against reformatting or restructuring faithful code. See
`CONTRIBUTING.md`'s "Porting upstream changes" section for the general
principle this case established: a parallel site transliterated correctly
elsewhere in the same file is strong evidence that a divergence is an error,
not a deliberate choice.

With both `DIR_GLUE = '/'` and `Sweph.cs:2634` fixed together,
`Issue18Test.LoadAsteroidData` passes again (confirmed, not assumed --
re-run on Windows specifically because it is what caught the original
regression), and the full suite passes on Windows (net8.0/net10.0,
Debug/Release) and in a Linux container (net10.0).

**Behavior change for consumers:** asteroid file names passed to `OnLoadFile`
now use `/` instead of `\`, e.g. `"ast4/se04179.se1"` instead of
`"ast4\se04179.se1"`. Windows accepts both forward and backward slashes in
paths, so existing Windows-only `OnLoadFile` handlers that pass the name
straight to `File.Open`/`Path.Combine` continue to work unchanged; handlers
that parsed the name expecting a literal backslash (e.g. via
`Path.GetFileName`, which does not recognize `\` as a separator on
non-Windows) should split on both separators, as this port's own test harness
now does (`ResourceFileHelpers.GetPortableFileName`).

**Baseline gate: updated, deliberately, via local-mode regeneration.** The
same `swe_set_ephe_path` code path that appends `DIR_GLUE` to the configured
ephemeris path also feeds "file not found" diagnostic messages, e.g. `SwissEph
file 'sefstars.txt' not found in PATH '[ephe]/'` instead of `'[ephe]\'`. This
surfaced as 207 baseline rows per TFM (192 in `ayanamsa`, 15 in `datetime` --
both areas that exercise a missing-file/Moshier-fallback path) once `DIR_GLUE`
and `Sweph.cs:2634` were fixed together. Every one of the 207 rows, confirmed
by dumping the full (non-truncated) failure list rather than trusting the
console's `Take(50)` sample, was exactly this one string-content change in a
diagnostic message column; none was a numeric divergence.

This is a real, intended behavior change -- the path separator genuinely is
`/` now, so `swe_set_ephe_path` echoing `'[ephe]/'` into its diagnostic is
accurate -- so the committed baseline needed to start reflecting it, not stay
frozen on the pre-fix text forever. `scripts/regenerate-baseline.ps1
-FromLocal` (see `Tools/BaselineGen/README.md`, "Local mode -- when it is
legitimate") regenerated it from local code; the resulting diff against the
previously committed baseline was confirmed, row by row, to be exactly those
207 rows, exactly that one string substitution, nothing else. `git diff
--stat Tests/baseline` at the time: `baseline-ayanamsa.tsv` (192 rows changed),
`baseline-datetime.tsv` (15 rows changed), `baseline-2.8.0.2.env.txt` (a new
append-only provenance entry, not a rewrite of its original reference
fields). `scripts/verify-baseline.ps1` passes again on both TFMs after this,
with the assembly-identity check still correctly reporting that the current
(local) build's `ModuleVersionId`/SHA-256 differ from the original reference
package's, unchanged, recorded in the sidecar.

The sidecar (`Tests/baseline/baseline-2.8.0.2.env.txt`) is not renamed despite
no longer describing every row in the directory: its name is derived from
`EnvInfo.ReferenceVersion` specifically so a future version bump cannot leave
a stale-named file behind, and nothing hard-codes that literal name (only a
`baseline-*.env.txt` pattern), so renaming would cost real coupling for a
purely cosmetic gain. Instead, the file itself now carries an explicit,
append-only "Local regenerations" log stating exactly this: the original
eight fields describe the reference-mode run and are kept verbatim (the
assembly-identity check depends on that), and this deviation -- 207 rows, the
`serr` path separator, this DIR_GLUE fix -- is recorded as entry 1.

## netstandard2.0-only infinite recursion in StringExtensions.Contains

`SwissEphNet/Extensions/StringExtensions.cs`'s `Contains(this string, char)`
and `Contains(this string, char[])` extension methods called `s.Contains(c)`
internally. On `net8.0`/`net10.0` that binds to the real BCL
`string.Contains(char)` instance method. `netstandard2.0`'s `System.String`
has no `Contains(char)` overload at all (only `Contains(string)`), so on that
target the call cannot bind to any instance method and falls back to binding
to the extension method itself -- unbounded recursion, an uncatchable
`StackOverflowException` that terminates the process. Reachable from
`SwemPlan.cs` (the `seorbel.txt` reader), `C.printf.cs`'s format-flag
parsing, `C.scanf.cs`'s scanset parsing, and `SwephLib.cs`
(`swe_get_astro_models`, a public API entry point). Nothing caught this
because `Tests/SwissEphNet.Tests` targets `net8.0;net10.0` only: a
`ProjectReference` always resolves the newest compatible asset from a
multi-targeted project, so the `netstandard2.0` build had been compiled and
shipped but never actually executed by anything.

Fixed by reverting both `Contains` overloads to `s.Contains(c.ToString())`
(the one `Contains` overload that exists on every target framework, already
ordinal by definition) and `C.printf.cs`'s flag parsing to
`flags.IndexOf(ch) >= 0` (also on every TFM, also already ordinal). Verified
end to end: temporarily reintroducing `s.Contains(c)` reproduces the
hang/crash under the added `Tests/NetStandard20Smoke.Tests` project (a
`net48` project, which is the one host that resolves a multi-targeted
`ProjectReference` down to the `netstandard2.0` asset); reverting to
`s.Contains(c.ToString())` makes all of that project's tests pass again in
under half a second. `Tests/NetStandard20Smoke.Tests` now runs on every
change (`dotnet test Tests/NetStandard20Smoke.Tests -c Release`, Windows
only, `net48` cannot build or run elsewhere), closing the gap.

## Five transliteration-fidelity defects found by a targeted string/array audit

An audit of every string operation and array allocation in
`SwissEphNet/CPort` against the C it was ported from found five further
defects, each with its own regression test in
`Tests/SwissEphNet.Tests/TransliterationFidelityTest.cs` (Defects 1, 2, 3, 3b
and 4 in that file's comments, which cite the exact C file/line each one
diverged from) plus a sixth, separately-numbered "Tier 2" test for an
unrelated culture-dispatch bug in `SweHouse.cs`'s house-system `'i'`
dispatch:

- **Defect 1** (`sweph.c:7386-7387`): `swi_fixstar_load_record` used
  `Trim(' ')` where the C strips every internal space from the candidate star
  name, leaving multi-word names (e.g. "Galactic Center") unable to match a
  search key that had already had its own spaces removed.
- **Defect 2** (`sweph.c:5996-5997`): `fixstar_format_search_name` lowercased
  `sstar.Substring(0, p - 1)` instead of `Substring(0, p)`, dropping the
  character immediately before the comma in "Name,Bayer"-form search
  strings -- since `swe_fixstar` rewrites its `ref` string to that form on
  return, a call-again-with-the-same-variable loop silently matched the wrong
  star on the second call.
- **Defect 3 and 3b** (`swehel.c:1443-1449`): `tolower_string_star` computed a
  lower-cased value but never assigned it back to its `ref string`
  parameter, so `swe_vis_limit_mag`'s Moon special-case
  (`ObjectName.StartsWith("moon")`) never matched a capitalized "Moon"; a
  related missing `p > 0` guard threw `ArgumentOutOfRangeException` instead
  of leaving a comma-first string untouched.
- **Defect 4** (`swephlib.c:4052,4058`): `swe_set_astro_models` used
  `Substring(0, 20)`, which throws on any input under 20 characters
  (including empty/null, which the C explicitly handles), and `"s + 2"`
  string concatenation where the C does pointer arithmetic (skip 2 bytes),
  which silently always returned 0 from `C.atof` and selected the current
  library version instead of the one actually requested.

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
PR #4's (`fix/known-library-bugs`) fixed-star bug fixes on Linux, not
something PR #4 introduced or is in scope to fix, since it is not related
to any of that PR's bugs (Windows-1252/UTF-8 decoding, culture-sensitive
string comparison, `atoi` sign handling, `CPointer<T>.operator !=`, `DIR_GLUE`,
or the fixed-star `bsearch` comparator).

**Confirmed against the base branch, not assumed:** re-ran this exact test on
the unmodified `release/2.10.03` branch (a `git worktree` checkout, with the
non-Windows fixed-star skip that was in place on that base branch at the
time -- the custom `WindowsOnlyFactAttribute`, added to skip known
Windows-1252/culture-sensitivity failures on Linux, since removed for good
once the UTF-8 encoding and ordinal-comparison fixes landed in PR #4 --
lifted only in that throwaway copy, no other change), in the same Linux
container. It fails identically: `Expected: 0.015543 ... Actual:
0.015532000000000001 (rounded from 0.015532476471018478)`, byte-for-byte the
same numbers PR #4's branch produces. This rules out any of PR #4's own
changes as the cause -- the divergence predates all of them.
`Test_swe_fixstar_ut`'s assertion on `xx[5]` was loosened from 6 to 4 decimal
places to accommodate it, rather than pinning a platform-specific value or
skipping the assertion.

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

## hcusp[36] fixed: CPort/SweHouse.cs:1983 was faithful to 2.08, not to 2.10.03

`swe_house_pos` (`SwissEphNet/CPort/SweHouse.cs`) declared `double[] hcusp = new
double[36]`. `swe_houses_armc` writes `cusp[36]` when `hsys` is `'G'`
(Gauquelin, `ito = 36`), which needs an array of length 37 -- indices `0..36`
inclusive -- so every `swe_house_pos` call with `hsys = 'G'` threw
`IndexOutOfRangeException`, and so did every caller that reaches the same code
path indirectly, including `swe_gauquelin_sector` (`SwissEphNet/CPort/SweCL.cs`).

This was not a mis-transliteration against the C version this port was tracking:
upstream C **2.08** also declares `double hcusp[36]` at the equivalent site in
`swehouse.c`, so the port was faithful to its source at the time. Upstream
**2.10.03** `swehouse.c:2224` changed the declaration to `double hcusp[37]` --
a real bug fix on Astrodienst's side, not a porting error on this side. Fixed
here (`SwissEphNet/CPort/SweHouse.cs:1983`, `new double[37]`) ahead of the full
2.10.03 `swehouse.c` re-transliteration, because the port is heading there
regardless and the conformance oracle already caught the live crash. **Do not
reapply this change when `swehouse.c` is re-transliterated for 2.10.03** -- the
array size will already match upstream at that point, and re-diffing the
upstream 2.08-to-2.10.03 change against an already-2.10.03-shaped line would be
a no-op at best and a miscount at worst.

Baseline effect: 375 `HP|G|*` rows in `Tests/baseline/baseline-house-pos.tsv`
were frozen as `EXCEPTION IndexOutOfRangeException` (the waiver mechanism
`docs/known-issues.md`'s characterization baseline exists for -- freezing a
known-bad result rather than working around it). Fixing the array size turns
all 375 into real Gauquelin house-position values, confirmed row by row: every
one of the 375 changed rows is `HP|G|*` and every one was `EXCEPTION` before
the fix, nothing else moved.

## swe_houses/swe_houses_armc/swe_house_pos/swe_house_name: hsys narrowed to char (caused conformance suite 6.6 to be misclassified)

`external/swisseph/swephexp.h:812-835` declares **`int hsys`** on every house
entry point (`swe_houses`, `swe_houses_ex`, `swe_houses_armc`, `swe_house_pos`,
`swe_house_name`). The port had narrowed all of them to `char hsys`
(`SwissEphNet/CPort/SweHouse.cs`, plus the internal `sidereal_houses_ecl_t0` /
`sidereal_houses_ssypl` / `sidereal_houses_trad` helpers, which the port's own
commented-out C signatures directly above them already showed as `int hsys`).

This is not merely a style narrowing. Internally, C truncates `hsys` to a
`char` only once, at the `CalcH` call inside `swe_houses_armc`
(`swehouse.c:661`, `CalcH(..., (char)hsys, ...)`), an 8-bit cast. The *outer*
functions -- `swe_house_name` (`swehouse.c:829`) and `swe_house_pos`
(`swehouse.c:2233`/`:2835`) -- compare the **raw, untruncated** int, via
`toupper()`, and fall through to their `default:` branch when it does not
match a house-system letter. A `char`-typed parameter cannot express that
distinction: every caller effectively already truncated before the port ever
saw the value, so out-of-range `int` inputs -- reproducible only by calling
through a signature the port did not offer -- could never be exercised.

This is exactly why conformance suite 6.6 (house-name/house-pos behavior for
out-of-range `hsys` values) was misclassified as unreproducible: the test
cases in that suite call `swe_house_name`/`swe_house_pos` with `hsys` values
outside `char` range specifically to exercise the raw-int-vs-truncated-char
split, and there was no way to construct that call against a `char`-only
signature.

Fixed by adding `int`-taking overloads matching upstream's signatures
(`SwissEphNet/SwissEph.swephexp.h.cs` on the public surface,
`SwissEphNet/CPort/SweHouse.cs` for the transliterated implementation), while
keeping the existing `char`-taking overloads as thin delegates (widening
`char` to `int`, no truncation, so every existing caller is unaffected). The
faithful 8-bit truncation is reproduced explicitly as `(char)(hsys & 0xFF)` at
the `CalcH` call site in `swe_houses_armc` (`SwissEphNet/CPort/SweHouse.cs`,
citing `swehouse.c:661`), since C#'s `(char)` cast on an `int` does not
truncate to 8 bits the way C's does (C# `char` is a 16-bit UTF-16 code unit,
not an 8-bit C `char`). Confirmed: `swe_house_name(32592)` returns
`"Placidus"` (falls to `default:`, matching the raw-int comparison) while
`swe_house_name('P')` still returns `"Placidus"` and `swe_house_name('K')`
still returns `"Koch"`; `swe_houses_armc(..., 32592, ...)` produces cusps
identical to an explicit `hsys = 'P'` call (32592 & 0xFF == `'P'`), confirming
the internal 8-bit truncation resolves the correct house system even though
the outer comparisons never match a named letter.

Baseline effect: none. The characterization matrix only ever calls through the
pre-existing `char`-typed API (there is no way to construct an out-of-range
`int` call against a `char` parameter), and the new `char` overloads are
behavior-preserving delegates, so `scripts/verify-baseline.ps1` shows zero
change in the `houses`/`houses-armc` areas from this fix.

## Cross-platform divergence: measured, and why the gate is platform-locked

Full numbers, the tolerance-level cost table, and the reasoning for locking the
gate to Windows instead of loosening the shipped tolerance, are in
`Tools/BaselineGen/README.md` under "Platform lock". Summary, measured against
the matrix as it stood when this comparison was run (3,443,058 numeric fields;
the committed baseline has since widened to 3,453,972 total fields, 3,426,469
of which parse as numbers, about 10,900 more numeric fields than this
comparison covered -- see `Tools/BaselineGen/README.md` for the re-scoping
and why the two counts differ): of the 3,443,058 numeric fields compared,
47,052 differ at all between Windows and Linux. Of those, only
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
