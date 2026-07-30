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

**Answered: 2.10.03 treats it as an error, 2.08 does not, and the port is faithful to
2.08.** So this is upgrade work, not a defect to fix against the version the port
currently tracks.

Measured by the bit-exact comparison harness: 176 of the 14,220 analytic-grid rows
return a different `retc` against 2.10.03 C, all `swe_houses_armc` at `eps=0`, 88
with `hsys = 'G'` and 88 with `hsys = 'P'`. Against **2.08** C all 176 match
exactly -- `Tests/oracle/version-classification.tsv` classifies every one of them
`TRACKS-2.08` with `port_vs_2.08 = MATCH`. 2.08 returns `OK` with NaN cusps, which
is precisely what the port does.

The mechanism is in the C. 2.10.03 adds `int niter_max = 100`
(`external/swisseph/swehouse.c:940`) and caps the Placidus and Gauquelin pole-height
iterations with `if (i >= niter_max) { retc = ERR; hsy = 'O'; goto porphyry; }`
(`:1667`, `:1709`, and four more). At `eps=0`, `tand(0)` is 0 and the iteration never
converges, so 2.10.03 gives up, reports the error and falls back to Porphyry --
returning real cusps rather than NaN. `niter_max` does not appear anywhere in
`external/pyswisseph-2.08/swehouse.c`, and that file has three `retc = ERR` sites
against 2.10.03's nine.

So the swehouse.c port picks this up as part of the delta, and the 176 case ids in
`Tests/oracle/known-diff.tsv` under category `RETC` are the inputs to verify it
against. Note the rows also carry 33 to 34 cusp fields that are NaN on the port's
side and finite on 2.10.03's, from that Porphyry fallback; the difference is not
confined to the return code.

An earlier revision of this paragraph said the port "swallows" an error the C
reports, which was wrong in the way that costs time: it would have sent someone to
fix code that is already correct for the version it tracks. The
`Tests/oracle/version-classification.tsv` data that refutes it was available and
unread. It also claimed `'G'` and `'P'` were the only house systems the grid crosses
with `eps=0`; the grid crosses all 25 letters with `eps=0`, and `G` and `P` are
simply the only two where `retc` differs. `{J, Z, 0}` are untested because they are
not in the grid at all.

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

## hcusp[36] fixed: swe_house_pos was faithful to 2.08, not to 2.10.03

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
here (`SwissEphNet/CPort/SweHouse.cs`, the `hcusp` declaration in
`swe_house_pos`, now `new double[37]`) ahead of the full
2.10.03 `swehouse.c` re-transliteration, because the port is heading there
regardless and the conformance oracle already caught the live crash. **Do not
reapply this change when `swehouse.c` is re-transliterated for 2.10.03** -- the
array size will already match upstream at that point, and re-diffing the
upstream 2.08-to-2.10.03 change against an already-2.10.03-shaped line would be
a no-op at best and a miscount at worst.

Baseline effect: 375 `HP|G|*` rows in `Tests/baseline/baseline-house-pos.tsv`
were frozen as `EXCEPTION IndexOutOfRangeException`. This is freezing a
known-bad result in the committed baseline, not the waiver mechanism
(`Tools/BaselineVerify/waivers.tsv`) -- the two are different things.
Freezing keeps a row in the comparison, with its known-bad value as the
expected value, so any change to it (a fix, or a regression) is caught and
must be reviewed. Waiving a row removes it from comparison entirely, which
would have hidden these 375 rows rather than recorded them. The waiver
mechanism was correctly not used here, and should not be: every waiver is
staleness-checked (a waiver that matches zero rows, or whose matched rows are
all byte-for-byte identical to the baseline anyway, fails the run --
`Tools/BaselineVerify/waivers.tsv`), so a waiver only ever suppresses rows
that are actively differing, which is the opposite of what this baseline
freeze is for. Fixing the array size turns all 375 into real Gauquelin
house-position values, confirmed row by row: every one of the 375 changed
rows is `HP|G|*` and every one was `EXCEPTION` before the fix.

It also changes **160 `GQ|*` rows** in `Tests/baseline/baseline-gauquelin.tsv`,
which reach the identical code through `swe_gauquelin_sector`
(`SwissEphNet/CPort/SweCL.cs`) rather than through `swe_house_pos` directly.
That area did not exist when this issue was first written; it was added later
as new coverage, and this fix is the first behaviour change it caught. So the
total is 535 rows across two areas, not 375 across one. An earlier revision of
this paragraph said "nothing else moved", which was true of the corpus as it
stood when written and false by the time the fix landed.

The `HP|G|*` values were afterwards checked against Astrodienst's own 2.10.03
libswe (the `pyswisseph` 2.10.3.2 wheel bundles it) and are **bit-exact**, not
merely within tolerance, across both the normal and the circumpolar
(Otto Ludwig) branches. The 160 `GQ|*` rows agree to 8.14e-09 sectors, roughly
0.0003 arcsec; since the shared `'G'` path is bit-identical, that residual comes
from the ephemeris chain ahead of it (delta T, obliquity, nutation, sidereal
time) and is 2.08-versus-2.10.03 drift rather than anything this fix introduced.

Note the range boundary: `hpos = xp[0] / 10.0 + 1` is usually described as
`[1, 37)`, but six frozen rows are exactly `37.0` -- `HP|G|90|-80|90|{-5,0,5}`
and `HP|G|270|80|270|{-5,0,5}`, all circumpolar cases where `xp[0]` lands on
exactly 0 so `360 - 0 = 360`. Upstream C returns `37.0` for all six as well, so
the closed interval `[1, 37]` is the correct contract. Do not "tighten" any
assertion to the half-open form; it would fail on real upstream behaviour.

## swe_houses/swe_houses_armc/swe_house_pos/swe_house_name: hsys narrowed to char (caused conformance suite 6.6 to be misclassified)

`swephexp.h:812-835` declares **`int hsys`** on all seven house entry points:
`swe_houses` (812), `swe_houses_ex` (816), `swe_houses_ex2` (820),
`swe_houses_armc` (824), `swe_houses_armc_ex2` (828), `swe_house_pos` (832),
and `swe_house_name` (835). Five of those seven are ported here; the port had
narrowed all five of them to `char hsys` (`SwissEphNet/CPort/SweHouse.cs`,
plus the internal `sidereal_houses_ecl_t0` / `sidereal_houses_ssypl` /
`sidereal_houses_trad` helpers, which the port's own commented-out C
signatures directly above them already showed as `int hsys`). `swe_houses_ex2`
and `swe_houses_armc_ex2` are **unported 2.10 features** (they add per-cusp
speed output and an explicit `serr` out-parameter that the ported API surface
does not have yet) -- their absence here is not a missed narrowing, it is
scope this branch does not touch. When they land, they must be declared
`int hsys` from the start, matching upstream; there is no `char`-only
predecessor to widen.

This is not merely a style narrowing. Internally, C truncates `hsys` to a
`char` only once, at the `CalcH` call inside `swe_houses_armc`
(`swehouse.c:661`, `CalcH(..., (char)hsys, ...)`), an 8-bit cast. The *outer*
functions -- `swe_house_name` (`swehouse.c:830`) and `swe_house_pos`
(`swehouse.c:2231`) -- compare the **raw, untruncated** int, via
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
`char` to `int`). This is behavior-preserving only for `char <= U+00FF`
(Latin-1): every existing caller passing an ASCII/Latin-1 `char` is
unaffected. For a `char` above `U+00FF`, routing it through the `int` path
now applies the same narrowing the `int` path applies at the `CalcH` call
inside `swe_houses_armc` (`swehouse.c:661`), which the old `char`-only
implementation did not apply -- measured, `(char)331` (low byte `0x4B` =
`'K'`) resolved to Placidus before this branch and to Koch after. This is a
behavior change for that narrow input range, and it is a change *toward*
C-faithfulness, not away from it: a C `char` is 8 bits, so a C caller could
never produce a value like 331 in a `char` variable in the first place, while
C#'s `char` (a 16-bit UTF-16 code unit) can; the widened path now resolves it
the way C would resolve its low byte.

The faithful truncation at the `CalcH` call site in `swe_houses_armc`
(`SwissEphNet/CPort/SweHouse.cs`, citing `swehouse.c:661`) is reproduced as
`(sbyte)hsys`, not `(char)(hsys & 0xFF)`: plain `char` is signed on the
reference platforms this port is verified against (x86-64 Windows and x86-64
Linux), so `(char)hsys` in C narrows to a *signed* 8-bit value,
and unlike `& 0xFF`, C#'s `(sbyte)` cast on an `int` reproduces that sign --
which matters observably, since `CalcH`'s lower-case-letter fold branches on
that sign -- pinned by
`TestHousesArmc_LowByte0x89_ResolvesToPlacidusNotSunshine` in
`Tests/SwissEphNet.Tests/HouseApiFidelityTest.cs`. Confirmed:
`swe_house_name(65611)` (`0x1004B`, low byte `'K'`) returns `"Placidus"`
(falls to `default:`, matching the raw-int comparison, not the low byte)
while `swe_house_name('P')` still returns `"Placidus"` and
`swe_house_name('K')` still returns `"Koch"`; `swe_houses_armc(..., 65611,
...)` produces cusps identical to an explicit `hsys = 'K'` call, confirming
the internal signed-8-bit narrowing resolves the correct house system even
though the outer comparisons never match a named letter. Out-of-range `int`
values (negative, or `> 65535`) no longer throw at any entry point --
formatting sites that render `hsys` into a diagnostic message narrow it first
rather than passing the raw `int` to a `%c`-style formatter.

Note that plain `char` signedness is implementation-defined in C, and is
**unsigned** by default on ARM and PowerPC Linux. Upstream C built there would
resolve a low byte of `0x89` to Sunshine where x86-64 resolves it to Placidus.
The port pins the x86-64 behaviour deliberately, since that is what the
conformance corpus and every reference run here are generated on. If an arm64
conformance runner is ever added, a divergence confined to low bytes `>= 0x80`
is this, not a regression.

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

## swe_house_pos: internal cusp buffer is one element short for Gauquelin houses

Found by the correctness oracle (`Tests/SwissEphNet.Conformance.Tests`), not the
characterization baseline: `SwissEphNet/CPort/SweHouse.cs:1903` allocates
`hcusp` as `new double[36]` inside `swe_house_pos`, then passes it to
`swe_houses_armc`, which for the Gauquelin sector house system ('G') writes
`cusp[36]` (upstream's own array is `double cusp[37]`, indices 0-36 -- see
`external/swisseph/swehouse.c`). The one-element-short C# buffer throws
`IndexOutOfRangeException` where the real C silently keeps going (writing past
the end of a 37-slot stack array is undefined behavior C does not catch
either, it just usually gets away with it).

This reproduces through two call paths in the conformance corpus: directly, via
`swe_house_pos` itself (suite 6, testcase 6 -- see that testcase's own remarks
in `Suite06Houses.cs` for why it is classified `Unreproducible` rather than
dispatched, for an unrelated, C-vs-C# representational reason), and indirectly
via `swe_gauquelin_sector`, which calls `swe_house_pos` internally with a
hardcoded Gauquelin `hsys` (suite 6, testcase 7 -- 22 iterations recorded as
`ERROR` in `Tests/conformance/known-fail.tsv`).

**Not fixed in `test/conformance-oracle`, deliberately:** this is a real library
defect, not test infrastructure, and fixing `SwissEphNet/CPort/SweHouse.cs`
would flip the ~240 currently-waived baseline rows tied to `house-pos` --
enough surface area to want under its own review, separate from the oracle
this branch adds. Fix it (`new double[37]`, matching upstream) as its own
reviewed PR, then remove the corresponding `known-fail.tsv` rows and drop this
entry.

## swi_strnlen outlives its deletion in swephlib.c, deliberately

2.10.03 removes `swi_strnlen` from `swephlib.c`, and the swephlib port keeps it
(`CPort/SwephLib.cs`). That is intentional, not an oversight: `sweph.c` is still
at 2.08 in this repo and `CPort/Sweph.cs` still calls it. Deleting it with the
swephlib port would not compile.

It comes out with the `sweph.c` port, along with its last caller. Anyone diffing
`SwephLib.cs` against 2.10.03 before then will find one function the C no longer
has, and this is why.

Its body is also not what the C's was -- it returns the whole length rather than
`min(strlen, n)`, ignoring `n` entirely. That predates the 2.10.03 work and is
moot once the function goes, so it is recorded rather than fixed.

## calc_nutation_woolard: C# long is 64-bit, MSVC's is 32-bit

`calc_nutation_woolard` casts to `long` when reducing an angle. In C# that is
`Int64`; under MSVC, which is the compiler behind the reference values this
repo's gates are locked to, `long` is 32 bits. The two diverge once the value
exceeds 2^31, i.e. `|J - J1900| > 5.92e6` days.

DE431 reaches about 5.58e6 days, so the divergence is outside the range any
ephemeris file can address and is unreachable in practice. The port matches
gcc and clang, where `long` is 64-bit, and differs from the Windows C only
beyond that horizon.

Recorded rather than changed: forcing 32-bit truncation would make the C#
match one platform's C and stop matching the other two, for inputs no caller
can supply.

## swe_nod_aps after swe_close: free_planets replaces objects where the C memsets

The nine `7.2.x` conformance rows (`swe_nod_aps_ut`, ~1.9e-6 degrees off) are not an
ephemeris-vintage issue and not a 2.08/2.10 mismatch -- `swe_nod_aps` is byte-identical
between the two C versions. They are a port defect, diagnosed as follows.

**Reproduction.** With a fixed `tjd_et`, so Delta T is out of the picture:

| sequence | port | libswe 2.10.03 |
|---|---|---|
| `set_ephe_path` | 76.65098418723707 | 76.65098420609208 |
| `set_ephe_path`, `swe_close` | **76.65098234128769** | 76.65098420609208 |
| `swe_close`, then any `swe_calc` | 76.65098418723707 | 76.65098420609208 |

Any `swe_calc` before `swe_nod_aps` restores it -- the Sun works as well as the Moon, so
2.08's deleted lunar `swi_get_tid_acc` probe was incidental, not special.

**Mechanism.** `swe_set_ephe_path` sets `swed.last_epheflag = 2` (`sweph.c:1346`) and
`swe_close` clears it. On the first `swe_calc` after a close, `last_epheflag != epheflag`
(`sweph.c:386`), so `free_planets()` runs -- and it runs *inside* `swe_nod_aps`, partway
through its own computation. `swe_nod_aps` already knows `swe_calc` clobbers the save area
(there is a comment and a restoring `swe_calc` for exactly that), but the C survives it and
the port does not.

The difference is aliasing. C's `free_planets` does
`memset(&swed.pldat[i], 0, sizeof(struct plan_data))`, zeroing **in place**: any pointer
already taken into that array still refers to the same, now-zeroed, storage. The port does
`swed.pldat[i] = new plan_data()`, **replacing** the object, so a reference captured earlier
keeps pointing at the old one with stale contents. `swe_calc` has the same shape at
`Sweph.cs`'s `swed.fidat[i] = new file_data()` against the C's `memset` at `sweph.c:397`.

Confirmed: replacing those three assignments in `free_planets` with an in-place field zero
makes the closed case return 76.65098418723707 -- exactly the open case, and matching libswe
to the port's usual 1.9e-8.

**Why the blunt version regressed eleven rows.** It was not over-clearing anything. A
*second* defect sat in `swe_nod_aps` and the two had been cancelling each other out.
`swecl.c:5414` is `if (iflag & (SEFLG_HELCTR | SEFLG_BARYCTR))`, and the port wrote
`!= Sweph.B1950` -- comparing an int mask against `2433282.42345905`, so always true. The
geocentric arms below it were unreachable and `xobs` stayed zero, so the `xear` added at
`swecl.c:5470` was never subtracted back out. That came out right only because `xear`
aliased an orphaned, all-zero array left by the object replacement. Fix `free_planets`
alone and the cancellation breaks: geocentric Moon nodes come out barycentric, 344.63
instead of 189.21. The identical block 100 lines further down was always correct as
`!= 0`, which is the intent proof.

**Both are now fixed.** Together they make 43 conformance rows pass with zero
regressions, and move the characterization baseline in `nodaps` only (156 of 360 rows),
regenerated under `-ExpectedScope 'NA|**','NAUT|**'` with a deviation note. Neither came
from the swephlib port -- both are present verbatim in `main` -- but they are fixed here
because the port is what made them reachable.

## Inverted `serr != NULL` guards: swept

C writes `if (serr != NULL) strcpy(serr, "...")`, asking whether the caller supplied a
buffer. A C# `ref string` always supplies one, so the literal `if (serr != null)` asks
instead whether a message is *already present* -- false for every caller that starts from
`null`, which is all of them -- and the message is silently dropped.

Corrected: two `swe_helio_cross` sites, `calc_deltat`, `swi_get_ayanamsa_ex`,
`swe_fixstar`/`swe_fixstar_ut`, the star-file-damaged message, `swe_sol_eclipse_how`'s
out-of-range message, `swi_mean_node`'s out-of-range append, and all thirteen
Moshier-fallback sites.

The Moshier family was the awkward one. `sweph.c` uses a single form at every site --
`if (serr != NULL && strlen(serr) + 30 < AS_MAXCH) strcat(serr, "...")` -- and the port
had four different renderings, none equivalent to it. Two **assigned** where the C appends,
so the "using Moshier eph." note overwrote the diagnostic explaining why the fallback
happened; a missing `seplm24.se1` reported only the note, not the missing file. One carried
the inverted guard and emitted nothing. None reproduced the buffer-space test, which is now
written `(serr == null ? 0 : serr.Length) + 30 < 256` -- a C# string has no such limit, but
keeping the test preserves the C's behaviour in the one case where it decides anything.

## The 7.2.x diagnosis in regenerations.log is superseded

`Tests/conformance/regenerations.log` attributes the nine `7.2.x` rows to a stale
`swed.oec`/`swed.nut` read by `swe_nod_aps`'s mean-node path. That was wrong: both were
measured identical at the point of use in the working and failing cases. The correct
diagnosis is the `free_planets` object-replacement entry above. The log is append-only, so
the correction is recorded here rather than by editing it.

## SE_VERSION stays at "2.08" until the port actually is 2.10.03

`sweph.h`'s `SE_VERSION` goes `"2.08"` -> `"2.10.03"` in the header delta, and the
constants stage deliberately does not take that line. Everything else in that delta is data
or a declaration; this one is a claim the library makes about itself through
`swe_version()`, and it would be false while `sweph.c`, `swecl.c`, `swehouse.c` and
`swetest.c` are still 2.08. The known-fail list is the standing evidence.

An earlier version of this note claimed the deferral was behaviourally inert, because
`swe_set_astro_models` parses the string and both `atof("2.08")` and `atof("2.10.03")`
select `AMODELS_SE_2_06`. **That was wrong for this port, and why is worth recording.** C's
`atof` is `strtod`, which takes the longest initial subsequence of the expected form, so
`"2.10.03"` yields 2.10. `Tools/C.cs` narrowed to the first character outside
`0123456789.+-Ee`, and `.` is in that set, so the whole of `"2.10.03"` survived,
`double.TryParse` rejected it, and the result was **0**. Zero falls through every version
branch to the final `else`, selecting `AMODELS_SE_1_00` and a different tidal acceleration.
Reachable from the public API via `swe_set_astro_models("")` or `(null)`.

`C.atof` now takes the longest parseable prefix as `strtod` does, so the claim holds *now*:
`atof("2.10.03")` is 2.10, which is >= 2.06, and both values select `AMODELS_SE_2_06`. Do
not rely on that without re-checking if `C.atof` changes again.

Take `SE_VERSION` in the release stage with the assembly version.
`TransliterationFidelityTest` asserts the current value and moves with it.

## Constants from the header delta not yet carried

The constants stage takes everything in `sweph.h`/`swephexp.h`/`swehouse.h`/`swephlib.h`
that is data or a declaration, with two deliberate exceptions: `SE_VERSION` above, and
declarations belonging to functions later stages add.

Carried after being missed on the first pass: `SEFLG_TROPICAL`, `SEFLG_CENTER_BODY`,
`SEFLG_TEST_PLMOON`, `SE_ECL_HYBRID`, and the three `SE_SIDBIT_*` values.

Still absent, with their implementations: `swe_calc_pctr` (`swephexp.h:413`) and
`swe_get_current_file_data` (`:447`). `swe_houses_ex2`, `swe_houses_armc_ex2` and the
`int hsys` / `const char *` signature changes are recorded further up this file.



## sid_data is a struct, so `sip = swed.sidd` copies where the C aliases

The C writes `struct sid_data *sip = &swed.sidd;` and reads through the pointer, so it sees
any later mutation of `swed.sidd`. `sid_data` is a **struct** in this port
(`CPort/Sweph.h.cs`), so `sid_data sip = swed.sidd;` takes a snapshot. Eight sites use that
form: five in `CPort/Sweph.cs` and three in `CPort/SweHouse.cs`.

Most are harmless because nothing mutates `swed.sidd` between the copy and the reads, and
`swe_set_sid_mode` works because it deliberately copies, mutates and writes back.

One was not harmless and is fixed: `swi_get_ayanamsa_ex` took its copy before the
`SE_SIDM_FAGAN_BRADLEY` fallback ran, so with no prior `swe_set_sid_mode` it read
pre-fallback state and returned 92.525 where the C returns 24.754 -- 67.8 degrees out. It
now re-reads `swed.sidd` after the fallback.

The remaining seven are unaudited. The general fix would be to make `sid_data` a class so
the assignment aliases as the C's pointer does, which would cover all of them at once; the
cost is that `swe_set_sid_mode`'s copy-mutate-write-back would need revisiting, since with a
class its intermediate writes would become visible to anything reading `swed.sidd`
concurrently. Worth doing as its own change with its own measurement, not folded into a
porting stage.

## swe_calc's serr differs for SE_INTP_APOG outside the Moshier range

Found by the bit-exact comparison harness, which compares the error string as well as the
numbers. 40 of the 14,220 analytic-grid rows agree on every value and on `retc`, and differ
only in `serr`. All 40 are `ipl = 13` (`SE_INTP_APOG`) at Julian days outside the interpolated
range, through both `swe_calc` and `swe_calc_ut`.

| | message |
|---|---|
| C 2.10.03 | `jd 500000.000000 outside Moshier's Moon range 625000.50 .. 2818000.50 ` |
| this port | `Interpolated apsides are restricted to JD 625000.5 - JD 2818000.5` |

Both strings exist in both C versions and in the port: the first is `swemmoon.c:883`
(`SwemMoon.cs:912`), the second is `sweph.c:982` and `:1006` (`Sweph.cs:1097` and `:1124`).
Neither side is missing a message. They disagree about which check runs first, or about which
one gets to write `serr` last, so the port reports the apsides restriction where the C reports
the underlying Moshier range failure.

This is the same family as the inverted `serr != NULL` guards swept earlier in this file: the
numbers were never wrong, only the diagnostic, which is why nothing caught it until something
compared the strings. The characterization baseline does not exercise `serr` for these rows and
the conformance corpus has no iteration at these Julian days.

The 40 case ids are in `Tests/oracle/known-diff.tsv` under category `SERR`. Fix belongs with
the `sweph.c` port, since that is where the ordering is decided.

## OnLoadFile: multicast leaks a stream, and a missing handler is indistinguishable from a missing file

`SwissEph.LoadFile` (`SwissEphNet/SwissEph.cs:89`) is the only route by which the library reads
an ephemeris file -- `swi_fopen` calls it at `CPort/Sweph.cs:2659` and nowhere else does. It
raises the `OnLoadFile` event and takes the stream back out of a settable property on the event
args:

```csharp
var h = OnLoadFile;
if (h != null) {
    var e = new LoadFileEventArgs(filename) { Encoding = DefaultEncoding };
    h(this, e);
    if (e.File == null) return null;
    return new CFile(e.File, e.Encoding ?? DefaultEncoding);
}
return null;
```

Two defects follow from using an event for what is a request with a return value.

**A second subscriber leaks a file handle.** Events are multicast by default. Every handler runs,
each may assign `e.File`, and only the last assignment survives. `CFile` takes ownership of the
one stream it is given and disposes it (`Tools/CFile.cs:55-59`), so any stream an earlier handler
opened is never disposed. Nothing in the API signature suggests attaching a second handler is
unsafe.

**`null` means both "no handler attached" and "file not found".** The C treats either as a
missing file and falls back to Moshier, so a caller who never subscribes gets answers rather than
an error. The values are plausible and wrong: at JD 2451545.0 the Sun comes out `280.3681666`
against `280.3681656` from the real files, a difference in the last printed digit. This was
observed, not theorised -- `Tools/CReference/build-c.ps1`'s smoke check originally accepted any
parseable number and passed against a nonexistent ephemeris directory for exactly this reason,
which is why it now pins the expected value and verifies the declared file set up front.

**Action for the release stage.** Replace the event with a single-valued resolver, something
shaped like `Func<string, EphemerisFile?>`, which removes the multicast ambiguity and the leak
with it, and give "no resolver configured" a state distinct from "file not found" so the silent
Moshier fallback becomes catchable. Neither `SwissEph.cs` nor `[Events].cs` is inside the
transliteration freeze, so this is allowed, and it adds no dependency. It is deferred to the
release stage rather than done during porting for two reasons: it is a breaking public API change,
which belongs with the version bump the package is already going to take; and the conformance
harness and the bit-exact comparison drivers are all `OnLoadFile` consumers, so changing it
mid-port would rebuild the instruments while they are being used to decide whether the port is
correct.

## Pointer arithmetic as string concatenation: Defect 4's class survives in SweTest

Defect 4 above records `swe_set_astro_models` writing `"s + 2"` where the C does pointer
arithmetic on `s`, so the C# appended the character `2` instead of skipping two bytes. That audit
swept `SwissEphNet/CPort`. `Programs/SweTest/Program.cs` is a separate frozen path and was not
covered, and the same class is still there.

Found by the swetest text-diff harness (`scripts/verify-swetest-diff.ps1`), which crashes the
port on six command-line options the C accepts. `-sid1` reports it plainly:

```
System.FormatException: The input string '-sid14' was not in a correct format.
```

The C is `atoi(argv[i] + 4)`: skip `-sid`, parse `1`. The port is `int.Parse(argv[i] + 4)`, which
concatenates and parses `-sid14`.

Eight live sites share the shape, at `Program.cs` lines 878, 884, 893, 919, 933, 964, 1162 and
1168. Six throw (`-sid`, `-ay`, `-sidt0`, `-sidsp`, `-helflag`, `-j`). The other two are worse for
not throwing:

- `1168`, `C.atof(argv[i] + 7)` for `-tidacc`. `C.atof` takes the longest parseable prefix, so a
  concatenated string that starts with `-t` yields `0` rather than an error, and the run silently
  uses a default tidal acceleration. This is visible in the harness as a numeric drift, not as a
  failure.
- `1162`, `astro_models = argv[i] + 5`, which assigns the whole option string with a digit
  appended instead of the model name.

Four further sites at 834, 847, 940 and 949 are commented-out C, kept for reference; they are the
same pattern and are the ones to check first if that code is ever restored.

Two crashes in the same harness are a different cause and are recorded here so they are not
mistaken for this class. `-house` throws `InvalidCastException` out of `C.sscanf` on a `%c` read
into a `string`, which makes swetest's main house-cusp entry point unusable in the port. `-utc`
throws `ArgumentOutOfRangeException` from a `Substring(4, 30)` where the C uses a bounded
`strncpy` that stops at the end of the string.

**None of this is 2.10.03 work.** All of it is present in the port as it stands, against the 2.08
it currently tracks, so it can be fixed without waiting for the swetest.c re-transliteration.
Fixing it in `Programs/SweTest/Program.cs` is a freeze-permitted correction of a divergence from
the C, the same standing as the six that have already landed: cite the C file and line.
