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

**Checked during the 2.10.03 port, and the hypothesis above is wrong.** `apc_sector`
contains no `acos` or `asin` at all -- in either C version or in the port, which uses
only `Atan`, `Atan2` and `Tan`. So an argument straying outside `[-1, 1]` cannot be
the mechanism, and no amount of clamping there would change anything. 2.10.03 does
not touch `apc_sector`; the function is byte-identical between the two versions.

2.10.03 does add a clamp of exactly the suspected shape elsewhere, in Alcabitius
(`swehouse.c:1602-1606`, `if (r > 1) r = 1; if (r < -1) r = -1;` before `acosd`),
which is why the shape looked plausible here. The second Alcabitius site, in
`swe_house_pos`, is still unclamped in 2.10.03 and the port follows it: a latent NaN
whenever `|tanfi * tand(dek)| > 1`, inherited rather than introduced.

A better candidate for `'Y'`, untested: the failing input is `armc=270, geolat=50,
eps=40`, where `Math.Abs(fi) >= 90 - ekl` compares 50 against 50.0 exactly. A branch
taken on an exact equality, followed by the `acmc < 0` sign test, would split two
platforms without either being wrong. That is a comparison boundary rather than a
domain overrun, and it would need a targeted measurement rather than a code read.

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
iterations at six sites total, each `if (i >= niter_max) { retc = ERR; ...; goto
porphyry; }`. Only the two Gauquelin sites (`:1667`, `:1709`) additionally set
`hsy = (int) 'O'` before the jump; the four Placidus sites (`:1865`, `:1901`,
`:1937`, `:1973`) set `retc` alone. That asymmetry is load-bearing for Gauquelin,
where the post-switch `hsy != 'G'` block would otherwise skip cusps 4 to 9, and a
no-op for Placidus -- treating all six as setting `hsy` would send someone to "fix"
four sites that are already faithful. At `eps=0`, `tand(0)` is 0 and the iteration
never converges at any of the six, so 2.10.03 gives up, reports the error and falls
back to Porphyry, returning real cusps rather than NaN. `niter_max` does not appear
anywhere in `external/pyswisseph-2.08/swehouse.c`, and that file has three
`retc = ERR` sites against 2.10.03's nine.

**Closed.** The swehouse.c port landed `niter_max` and all 176 rows now match 2.10.03
C bit for bit; `Tests/oracle/known-diff.tsv` is empty. The rows had also carried 33
to 34 cusp fields that were NaN on the port's side and finite on 2.10.03's, from the
Porphyry fallback, and those agree too.

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
a caller has no signal that anything went wrong.

**Checked against 2.10.03's `sweph.c`, and this is still open.** The `sweph.c` port
has landed, and the `SE_ECL_NUT` branch in `CPort/Sweph.cs`'s `swecalc` writes only
the first four `x[]` slots before returning, the same shape that leaves the
equatorial/cartesian fill this entry describes unreached. `Tests/baseline/baseline-2.8.0.2.env.txt`'s
pyswisseph replay notes corroborate it independently: the `calc-defaulteph` divergence
for the `SE_ECL_NUT` pseudo-body under J2000/no-nutation/sidereal flag combinations is
recorded there as real 2.10.03 C returning non-zero values where the port still
returns zero. Not fixed here; recorded as confirmed rather than left as "worth
checking."

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
over time.

**The 2.10.03 SweHouse delta unifies these paths.** `swe_houses` and
`swe_houses_ex`/`swe_houses_ex2` (`CPort/SweHouse.cs`) both call `SwephLib.swi_epsiln`
directly now; there is no `swe_calc(SE_ECL_NUT)` call left anywhere in that file. The
structural disagreement this entry describes no longer has a mechanism to produce it.

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
and `swe_houses_armc_ex2` were unported 2.10 features when this entry was
written (they add per-cusp speed output and an explicit `serr` out-parameter
that the ported API surface did not have yet). **Both are now implemented**
(`SwissEphNet/CPort/SweHouse.cs`), each with a `char hsys` and an `int hsys`
overload from the start, matching upstream directly -- there was no
`char`-only predecessor to widen for either.

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

## swi_strnlen outlived its deletion in swephlib.c, deliberately, until this slice

2.10.03 removed `swi_strnlen` from `swephlib.c`, and the swephlib port kept it
(`CPort/SwephLib.cs`) for a while: `sweph.c` was still at 2.08 in this repo and
`CPort/Sweph.cs` still called it, and deleting it with the swephlib port would
not have compiled.

**Closed.** `swi_fixstar_load_record` (`CPort/Sweph.cs`) was its only remaining
caller, and the `sweph.c` port replaced that call with the same `strlen`-plus-clamp
the C now uses (`sweph.c:7540-7556`: `slen = strlen(s); if (slen > SE_MAX_STNAME)
slen = SE_MAX_STNAME;`, in place of `slen = swi_strnlen(s, SE_MAX_STNAME);`), so
`swi_strnlen` was removed from `SwephLib.cs` in the same change. Anyone diffing
an older revision of `SwephLib.cs` against 2.10.03 would have found one function
the C no longer has; that is no longer the case.

Its body was also not what the C's was -- it returned the whole length rather than
`min(strlen, n)`, ignoring `n` entirely. That predated the 2.10.03 work and became
moot once the function was removed, so it was recorded rather than fixed while it
still existed.

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

## SE_VERSION: was deferred until the port reached 2.10.03; closed

`sweph.h`'s `SE_VERSION` goes `"2.08"` -> `"2.10.03"` in the header delta, and the
constants stage deliberately did not take that line on its own. Everything else in that
delta is data or a declaration; this one is a claim the library makes about itself through
`swe_version()`, and it would have been false while `sweph.c`, `swecl.c`, `swehouse.c` and
`swetest.c` were still 2.08.

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

**Closed.** `SE_VERSION` now reports `"2.10.03"` (`Sweph.h.cs:89`), landed with the release
stage alongside the assembly version. The assertion that pins it, and moves with it, is
`SwissEphTest.cs:34` (`Assert.Equal("2.10.03", target.swe_version())`);
`TransliterationFidelityTest.cs:206` only comments on the current value.

## Constants from the header delta not yet carried

The constants stage takes everything in `sweph.h`/`swephexp.h`/`swehouse.h`/`swephlib.h`
that is data or a declaration, with two deliberate exceptions: `SE_VERSION` above, and
declarations belonging to functions later stages add.

Carried after being missed on the first pass: `SEFLG_TROPICAL`, `SEFLG_CENTER_BODY`,
`SEFLG_TEST_PLMOON`, `SE_ECL_HYBRID`, and the three `SE_SIDBIT_*` values.

**Closed.** `swe_calc_pctr` (`swephexp.h:705`) and `swe_get_current_file_data` (`swephexp.h:763`)
are both implemented as full transliterations -- `CPort/Sweph.cs` (citing `sweph.c:8042-8283` and
`:8285-8306` respectively) -- with the usual public facade in `SwissEph.swephexp.h.cs` that every
ported function gets, not a stub. `swe_houses_ex2` and `swe_houses_armc_ex2` are implemented too,
each with both `char hsys` and `int hsys` overloads in `CPort/SweHouse.cs`; the `int hsys` /
`const char *` signature changes are recorded further up this file.



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

## swe_calc's serr for ipl 13: a range gate the C does not have there

Fixed. Recorded because the first diagnosis was wrong in a way worth not repeating.

Found by the bit-exact comparison harness, which compares the error string as well as the
numbers: 40 of the 14,220 analytic-grid rows agreed on every value and on `retc` and differed
only in `serr`, all at `ipl = 13` through both `swe_calc` and `swe_calc_ut`.

| | message |
|---|---|
| C | `jd 500000.000000 outside Moshier's Moon range 625000.50 .. 2818000.50 ` |
| the port | `Interpolated apsides are restricted to JD 625000.5 - JD 2818000.5` |

The first reading was that both messages are legitimate and the two sides disagree about which
check runs first. That was wrong. `ipl = 13` is `SE_OSCU_APOG`, the osculating apogee;
`SE_INTP_APOG` is 21. The C's `SE_OSCU_APOG` case (`sweph.c` 2.08:945-957, 2.10.03:955-966) has
no Julian-day gate at all. It calls `lunar_osc_elem`, which reaches `swi_moshmoon`, and
`swemmoon.c:883` is what emits the range message. The gate carrying "Interpolated apsides are
restricted" belongs only to the `SE_INTP_APOG` and `SE_INTP_PERG` cases further down.

The port had that gate copied into its `SE_OSCU_APOG` branch as well: eight lines with no
counterpart in the C, at either version. Deleting them lets `swi_moshmoon` own the message, as
the C does. Both C versions emit identical text here, so this was never upgrade work.

Baseline effect: 56 rows per TFM in the `calc` area, all `ipl = 13`, all the `serr` column, no
numeric field and no other area. Regenerated under `-ExpectedScope 'C|13|**;CU|13|**'` as
deviation 15.

The lesson is the diagnosis, not the fix. Two plausible messages in two places invited an
ordering explanation, and the constant that names which branch runs settles it in one line. Read
the C before proposing a mechanism for what the C does.

## OnLoadFile superseded: single-valued IEphemerisFileProvider, real filesystem by default

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

**Closed.** `OnLoadFile` and `LoadFileEventArgs` are gone. `SwissEph.cs` now exposes:

```csharp
public interface IEphemerisFileProvider { Stream Open(string path); }  // null means not found
public IEphemerisFileProvider FileProvider { get; set; }               // per-instance, null default
public static IEphemerisFileProvider DefaultFileProvider = null;       // read into FileProvider by the ctor
internal protected CFile OpenBinary(string path) { ... }               // the fopen() substitution
```

Single-valued by construction, so the multicast leak cannot recur: `FileProvider` holds at most
one provider, and `OpenBinary` (`SwissEph.cs`) calls it directly rather than raising anything.
Ownership and the readable/seekable requirement, both load-bearing before but undocumented, are
now stated on `IEphemerisFileProvider.Open`'s own doc comment: the library disposes whatever
stream it is handed, and `CFile` seeks during parsing (e.g. rewinding `sefstars.txt` between a
`swe_fixstar` and a `swe_fixstar2` call), so a provider's stream must support both.

**The null-provider decision, made deliberately.** `FileProvider == null` now means "use the real
filesystem" -- `OpenBinary` opens the path with `File.OpenRead` directly -- rather than "not
found". This is the opposite default from the event it replaces, and is the better one now:
`SwissEph.csproj:12` records that `net40`/`netstandard1.0`, the targets `OnLoadFile` originally
existed to work around (no `System.IO.FileSystem`), were dropped; the three targets this library
ships today (`netstandard2.0`, `net8.0`, `net10.0`) all have full filesystem access, and the
library uses `System.IO.File` zero times before this change. A caller who calls
`swe_set_ephe_path` pointed at a real, populated directory and never touches `FileProvider` now
gets real ephemeris data instead of a silent Moshier downgrade -- closing the exact defect this
entry opened with, by construction rather than by a caller remembering to attach a handler. A
provider is still the right tool when the source genuinely is not a file (an embedded test
resource); `Tests/SwissEphNet.Tests` keeps one (`ResourceFileHelpers.DelegateFileProvider`) for
exactly that case.

**`swi_fopen` (`CPort/Sweph.cs`) is now a faithful transliteration of `sweph.c:2370-2405`**, not a
single `SE.LoadFile(fnamp)` call standing in for 18 commented-out lines. The path-search loop --
splitting `ephepath` with `swi_cutstr` against the cut-list `PATH_SEPARATOR`, the `"."`
current-directory case, joining with `DIR_GLUE`, the `AS_MAXCH` bounds check with its own "file
path and name must be shorter than" error -- is transliterated line by line; `SE.OpenBinary(fnamp)`
is the only substitution for the C's `fopen()` call. This closes the three gaps this document
elsewhere recorded as unfixed, under "Three file-layer divergences, recorded and not fixed here":
`AS_MAXCH` is now checked, and the `"."` case is now handled. `PATH_SEPARATOR` (below) closes the
third.

**`PATH_SEPARATOR` widens from `char` to `char[]`** (`SwissEph.sweodef.h.cs`), matching the C's own
cut-list shape (`sweodef.h:305`/`:311`: `";:"` on Unix, `";"` on MSDOS/Windows) so `swi_cutstr` can
be called at all. The value itself stays `{ ';' }` rather than adopting the Unix `";:"` list: unlike
`DIR_GLUE` (which safely picked `'/'` as the one separator both Windows and everything else accept),
a bare `':'` is not safe to add on this cross-platform port, because it collides with a Windows
drive letter (`"C:\ephe;D:\ephe2"` would split at the drive letter, not at the `;`). `";"` alone is
the value that is correct on every platform this library targets. `Programs/SweTest/Program.cs`'s
`make_ephemeris_path` (frozen, transliterated) needed a companion fix at the four sites that use
`*PATH_SEPARATOR` in the C (`swetest.c:3965`, `:3972`, `:4013`) to dereference the first element,
`PATH_SEPARATOR[0]`, now that the port's own field is an array; the one site using the bare
cut-list (`swetest.c:3982`) drops its `new char[] { ... }` wrapper since `PATH_SEPARATOR` already
is one. `Programs/SweWin/FormData.cs` (not frozen) needed the same fix for the same reason.

**A real bug found while restoring the transliteration, not by inspection: an inverted
`serr != NULL` guard, the same class already sept from a dozen other sites in this document.**
`sweph.c:2391` and `:2404` both guard their `sprintf`/`strcpy` into `serr` with `if (serr != NULL)`
-- in C, "did the caller supply a buffer at all". A first-draft transliteration of both sites as
`if (serr != null) serr = ...;` compiles and looks faithful, but a C# `ref string` always supplies
a buffer, so the guard instead asks "does `serr` already hold a message" -- false for every caller
starting from `null`, which is all of them. Caught immediately, not eventually: with a real
filesystem default, `Tools/BaselineVerify`'s `calc-defaulteph` area (which pins the exact "file not
found ... using Moshier eph." diagnostic) started rendering the message with the "file not found"
half silently missing, `scripts/verify-baseline.ps1` showed 1,610 rows in that area alone, and the
`gauquelin` area showed a matching 32-row loss. Both guards were dropped (unconditional assignment,
matching the fix already applied at the dozen sites in "Inverted `serr != NULL` guards: swept"
above); `verify-baseline.ps1` is byte-identical, both TFMs, after the fix.

**Verified byte-identical / bit-identical, not merely "still green".** The characterization
baseline (`Tests/baseline/`) is unchanged to the byte across all 19 areas on both `net8.0` and
`net10.0` -- `Tools/BaselineMatrix/Areas.cs`'s `Generate` now sets
`SwissEph.DefaultFileProvider` to a no-op provider before running any area's generator (the one
choke point every `new SwissEph()` in the several-hundred-call-site matrix goes through), so the
matrix stays Moshier-only exactly as it was when nothing subscribed to `OnLoadFile`, rather than
starting to find whatever ephemeris files happen to be present on the machine that runs it. The
bit-exact oracle (`Tests/SwissEphNet.Conformance.Tests`, `scripts/verify-oracle.ps1`) stays at
14,820 + 2,244 rows, all bit-identical against MSVC-built 2.10.03 C, both `known-diff.tsv` lists
empty -- including the files grid, which exercises real path resolution through
`SwissEph.OpenBinary`'s filesystem branch for the first time.

**Migration.** 37 files referenced `OnLoadFile`. Most became simpler: a handler that just opened a
real file by path (`Programs/SweTest/Program.cs`, `Programs/SweMini/Program.cs`,
`Programs/SweWin/FormData.cs`, `Tools/OracleDump/Program.cs`,
`Tests/SwissEphNet.Conformance.Tests/Dispatch/EphemerisFileResolver.cs`, several
`Tests/SwissEphNet.Tests` cases) was deleted outright, since `swe_set_ephe_path` alone now reaches
the same files through the restored `swi_fopen`. `EphemerisFileResolver`'s JPL-file redirect
(matching a custom DE-file path by filename regardless of directory) is now a second
`PATH_SEPARATOR`-joined `swe_set_ephe_path` entry instead of a provider. A provider survives only
where the source genuinely is not a file: `Tests/SwissEphNet.Tests`'s embedded-resource cases
(`ResourceFileHelpers.DelegateFileProvider`, a small adapter from a `Func<string, Stream>` to
`IEphemerisFileProvider`, replacing the per-test `OnLoadFile` lambda).

One capability did not survive the interface's fixed shape (`Stream Open(string path)`, no
encoding channel): `LoadFileEventArgs.Encoding` used to let a handler override the decode encoding
per file. `IEphemerisFileProvider` cannot express that -- the static `SwissEph.DefaultEncoding` is
the only lever left, applying to every file for the life of the process rather than per file.
`Tests/SwissEphNet.Tests/SwissEphTest.cs`'s `TestOnLoadFileHandlerCanOverrideEncodingPerFile` is
now `TestDefaultEncodingAppliesToProviderSuppliedStreams`, pinning the new, coarser mechanism
rather than the one that is gone.

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

**Closed, all ten sites.** Commit `2b7e896` fixed the eight pointer-arithmetic sites (`-ay`,
`-sidt0`, `-sidsp`, `-sid`, `-j`, `-helflag`, `-amod`, `-tidacc`, citing the 2.08 `swetest.c` line
each one corresponds to) and the two unrelated crashes recorded above (`-house`, `-utc`) in the
same change. The 150-row `Tests/swetest/known-diff.tsv` grid moved from 70 to 80 identical rows,
ten CRASH cases becoming byte-identical against the C reference. The four commented-out sites at
834, 847, 940 and 949 are unaffected, since there is no live code there to fix.

## The file-backed grid's divergence is Earth's position

`Tools/OracleGrid/grid-files.tsv` (2,024 rows) is the only grid that opens an ephemeris
file at all -- `grid-analytic.tsv` OR-s in `SEFLG_MOSEPH` throughout, so it never reads one.
Comparing `external/.c-reference/dump-net-files.tsv` against `dump-c-2.10.03-files.tsv` field
by field gives, for `swe_calc`/`swe_calc_ut` (900 rows each, 1,800 total, crossing bodies 0-14
with six iflag combinations and ten dates):

| Body | PLAIN / SPEED / TOPOCTR / SIDEREAL (geocentric) | HELCTR / BARYCTR |
|---|---|---|
| Sun (0) | 0 / 80 | 40 / 40 |
| Mercury..Pluto (2-9) | 0 / 640 | 320 / 320 |
| Mean node (10) | 80 / 80 | 40 / 40 |
| True node (11) | 0 / 80 | 40 / 40 |
| Mean apogee (12) | 80 / 80 | 40 / 40 |
| Osculating apogee (13) | 0 / 80 | 40 / 40 |
| Earth (14) | 80 / 80 | 0 / 40 |
| Moon (1) | 0 / 80 | 0 / 40 |

760 of the 1,800 rows match bit for bit; 1,040 do not, and which side a body lands on is not
random. Every heliocentric/barycentric row for Mercury through Pluto, true node and osculating
apogee matches -- 400 rows, none of them needing anything but that body's own `sepl_*.se1`
segment. Heliocentric and barycentric Sun match too (40 more), and mean node and mean apogee
match under all six flags (240 more, and unsurprising: `SwephLib.cs`'s mean-node/mean-apogee
path is a closed-form secular formula that opens no file regardless of `iflag`). Earth's own
geocentric position matches under all four geocentric flags (80 rows) because it is the zero
vector by definition -- `xx[0..2]` read `0000000000000000` on both sides, confirmed by reading
the hex columns directly.

Every row that needs Earth's position anywhere in the computation differs: Earth's own
heliocentric and barycentric position (40 rows, 0 match), which needs `semo_*.se1` to split the
Earth-Moon barycentre; the Sun's geocentric position (80 rows, 0 match), which is Earth's
heliocentric position negated; the Moon, under every flag (120 rows, 0 match); and the
geocentric position of every other body (800 rows across Mercury..Pluto, true node and
osculating apogee, 0 match), because geocentric position is heliocentric position minus Earth's,
and Earth's heliocentric position is the one thing on this list the port gets wrong. It is a
single defect that appears once per body, because every geocentric calculation subtracts the
same wrong vector.

That reduces `read_const`, `do_fread`, `get_new_segment` and the Chebyshev evaluation to
demonstrably sound code -- 440 rows read `sepl_*.se1` for a body that is not Earth and match
exactly, which cannot happen if any of those four were wrong. The remaining unexplained
divergence was narrowed to wherever `main_planet` derives Earth's own position from the Moon
(`SwissEphNet/CPort/Sweph.cs`'s `SEI_EARTH`/`SEI_MOON` handling), a far smaller place to look
than "the file layer" suggested -- but `main_planet` was not, in the end, where the bug was.

**Closed, and `rot_back` was the fifth function this paragraph cleared too soon.** The actual
defect was in `rot_back`, not `main_planet`: it read `swed.oec2000.seps`/`.ceps`, which nothing in
this port ever populates, so every position rotated back through it used a J2000 obliquity of
zero (commit `276fc5b`, part of the `sweph.c` file-layer slice). `main_planet` reads Earth's
position via `rot_back` on the way out, which is why the divergence looked like it belonged to
`main_planet` from this grid's evidence alone -- the wrong function was simply downstream of the
right one. Every `SEFLG_SWIEPH` position was affected, not only Earth's, since `rot_back` is on
the return path for every body; see "Every `SEFLG_SWIEPH` position changes" in `README.md`'s
breaking-changes list. The file-backed grid moved from 791 of 2,024 bit-identical rows to 1,975.
No closure note was added here when the fix landed; this is that note.

**The SEFLG_SPEED zero-fill claim, checked the same way.** Of the 1,500 non-SPEED
`swe_calc`/`swe_calc_ut` rows, 0 have the C leaving `xx[3..5]` at zero while the port fills them
with something else -- the claim that the port does this generally does not hold anywhere in
this grid. It does hold for fixed stars, but only two of the four entry points: of 96 non-SPEED
fixed-star rows (24 each for `FIXSTAR`, `FIXSTAR_UT`, `FIXSTAR2`, `FIXSTAR2_UT`), all 24
`FIXSTAR` rows and all 24 `FIXSTAR_UT` rows show the C at zero and the port nonzero; `FIXSTAR2`
and `FIXSTAR2_UT` show it on none of their 48.

An earlier pass at this grid read the 42%-match rate on `swe_calc` (380/900, same on
`swe_calc_ut`) against the 100%-match rate on the `SEFLG_MOSEPH`-only analytic grid (2,160/2,160)
and concluded the fault was "in the file layer", reasoning that the only variable between the
two grids was whether a file got read. That comparison was not valid: the two grids also differ
in which iflag combinations they cross (twelve against six) and which dates they use (JD
500000-3000000 against calendar years 1200-2399), so "the only variable is whether it reads a
file" was false on its face, and the 42% figure was an average across bodies and flags that
behave completely differently, which is exactly what the table above shows. The same pass also
claimed the port fills `xx[3..5]` with nonzero values on non-SPEED rows where the C leaves them
at zero; measured directly, that is 0 of 1,500 `calc`/`calc_ut` rows and is real only for two of
the four fixed-star entry points, as recorded above.

## What the oracle grids do not cover in the house code

The bit-exact comparison reports 14,220 of 14,220 analytic-grid rows matching MSVC-built
2.10.03 C. That is a real result and it is narrower than it sounds, so this records what it
does and does not establish, to stop it being cited for things it never touched.

Both replay drivers (`Tools/OracleDump/Program.cs`, `Tools/CReference/sedump.c`) call exactly
two house functions, `swe_houses` and `swe_houses_armc`, in their **six-argument** form. That
determines the coverage.

**Genuinely covered.** Twenty-five house letters crossed with latitudes to 89 degrees and
obliquities including 0. The `eps = 0` column is what earns the grid its keep: `tand(0)` is
zero, the pole-height iteration cannot converge, and 2.10.03's `niter_max` cap fires. So
`niter_max`, the Porphyry fallback, the Alcabitius clamp and the non-speed `CalcH`
restructuring are all verified against Astrodienst's own C.

**Not covered by the grid at all**, because the six-argument overloads have no such parameter
or entry point:

- every speed derivative, so `AscDash` and all nine speed fields
- `swe_houses_ex2` and `swe_houses_armc_ex2`
- `swe_house_pos` and `swe_house_name`
- `serr` on any house path, including the threading added through `sidereal_houses_*`
- house systems `'J'`, `'Z'` and `'0'`

**Partly covered elsewhere.** The speed fields are exercised by conformance suite 6 testcases
8 and 9, all 1,080 iterations of which pass against `t.exp`. But testcase 8 uses only hsys `'P'`
and `'W'`, and testcase 9 only `'K'` and `'P'`. So `AscDash` is verified against Astrodienst for
Placidus and Koch, and for nothing else -- not Campanus, Horizon, Regiomontanus, Topocentric,
Savard-A, the Gauquelin per-sector speeds, or the equal-house fill loops. The `do_interpol`
numerical-differentiation path, reached by `L Q S X M F B Y I`, has no coverage in either
oracle, because the two systems that do carry speeds through conformance both take the analytic
path.

**House system `'J'` (Savard-A) has no external validation whatsoever.** The analytic grid
excludes it deliberately (`gen-grid-analytic.ps1`'s house-letter list, with a comment saying
so), and `setest/t.exp` never uses it in any suite 6 testcase -- checked by enumerating every
`ihsy` in the corpus. Its geometry was transliterated from `swehouse.c:1176-1251` and `:2472-2535`
and read back against the C line by line, which is the only evidence there is. The 918 `HP|J`
and `HN|J` baseline rows froze the port's own output with no oracle behind them.

Closing that gap means either adding `'J'` to the analytic grid, which needs the C reference to
compute it too, or accepting transliteration review as the standard of proof for one house
system and saying so. It is recorded here rather than left implicit because "the analytic grid
is fully bit-exact" is otherwise easy to read as covering it.

House system `'J'` is also the largest single block of house-code baseline movement with no
oracle behind it: `Tests/baseline/baseline-2.8.0.2.env.txt` deviations 16 and 17 move 4,171 `'J'`
rows between them (1,944 `houses-armc` + 480 `houses` + 829 `house-pos` at deviation 16, plus 918
`house-pos` at deviation 17 -- summed per deviation the way each entry's own scope check reports
it, not deduplicated, since the same row can be touched by both). Every one of those rows is
frozen output checked only by re-reading the C, per the paragraph above.

## Three numbers in baseline-2.8.0.2.env.txt's local-regenerations log are wrong

The log is append-only, so these are corrected here rather than by editing the entries.

**Deviation 18** says "737 rows in the pheno area across its six case-id prefixes." The scope
check two lines below it, in the same entry, already gives the correct figure: "pheno: 736
changed." 736 is right -- exactly half of the area's 1,472 rows, which is also what the landing
commit's own message says. 737 is a transcription slip in the prose sentence, not a second
measurement.

**Deviation 17** says "919 rows in house-pos: 918 HP\|J cusp values and the single HN\|J name
row." 918 + 1 is 919, but the entry's own scope check reports "house-pos: 918 changed" -- 918
total, not 919. Diffing the area directly (commit `e31a9d6`, deviation 16's landing commit, against
`2c95529`, deviation 17's) confirms the scope check: 917 `HP\|J` rows changed plus the 1 `HN\|J`
row, 918 in total. "918 HP\|J" in the prose should read "917 HP\|J."

**Deviation 16** lists three mechanisms for why house cusps move -- `niter_max`'s Placidus/Gauquelin
fallback, the Alcabitius clamp, and house system `'J'` becoming real -- without saying that the
second of the three moved nothing. Checked directly: every hsys `'B'` (Alcabitius) row is
byte-identical between commit `7470527` (deviation 16's parent) and the current baseline, in
every area that carries house-system-keyed rows -- 0 of 1,944 in `houses-armc`, 0 of 480 in
`houses` (60 `HS\|B\|*` + 420 `HX\|B\|*`), 0 of 1,125 in `house-pos`. The clamp was ported
faithfully (`swehouse.c:1602-1606`, `if (r > 1) r = 1; if (r < -1) r = -1;` before `acosd`) and
changed no observable output in this baseline: every `r` the matrix's inputs produced already sat
inside `[-1, 1]`. The other two mechanisms account for all of deviation 16's actual movement.

## What the local-mode baseline regenerations have no independent check on

The two verification gates section of this project's contributing notes explains that a
`local-<sha>`-provenance baseline row proves "unchanged since the day it was written," not
correctness against any external reference. Most of the areas seeded that way have since picked
up at least partial corroboration -- `scripts/validate-seeded-areas.py`'s pyswisseph replay
above, or a conformance row that started passing. Three pieces of the 2.10.03 work landed in the
baseline with neither, and are worth naming rather than leaving to be inferred from the log.

**`swe_refrac_extended` and `calc_dip`.** Deviation 19 flips a predicate (`if (trualt > dip)` to
`if (inalt >= dip)`) and corrects a constant (`273.16` to `273.15`), moving 393 `REFX` rows in the
`atmo` area. `swe_refrac`, `swe_refrac_extended` and `swe_set_lapse_rate` appear zero times in
`external/swisseph/setest/t.exp` (checked directly: `grep -c` for all three names returns 0), so
none of these functions has a conformance testcase, ever, in the corpus this port is verified
against. `atmo` is `local-a30cb80` in the provenance table above, so it never had a package
reference either -- it was seeded from local code from the moment it existed. A predicate flip and
a constant change moved 393 rows with nothing in this repository that could contradict them if
they were wrong. This is the largest wholly unverified behavior change in the 2.10.03 work so far.

**`swe_rise_trans`'s `!do_fixstar` gate.** Also deviation 19: `swe_rise_trans` now routes fixed-star
calls off the fast path that never called `swe_fixstar`. The `risetrans` area has 760 rows across
its four case-id prefixes (`RT` 400, `RTATM` 18, `RTBIT` 162, `RTH` 180); every one of the 760 uses
a numeric `ipl` (0-9), confirmed by listing the distinct `ipl` values under each prefix -- none is
a star name. The only star rows anywhere in the baseline are six `GQ\|Aldebaran\|*` rows in
`gauquelin` (`imeth` 0 through 5, not four as an earlier pass at this count said), and every one of
those returns `SwissEph file 'sefstars.txt' not found in PATH '[ephe]'` -- the baseline harness
never subscribes to `OnLoadFile`, so a fixed-star lookup always fails before reaching the gate at
all. The gate has no row anywhere in the baseline that both names a star and produces a computed
(non-error) result, so nothing here could have caught a mistake in it either way. (Deviation 19's
four `GQ` rows that did move came from the *opposite* direction -- `swe_gauquelin_sector` reaching
`swe_rise_trans` through `imeth` 2-5 with a fixed-star name, routed off the old path -- not from
the gate itself computing a different fixed-star result.)

**House system `'J'`.** Covered above, under "What the oracle grids do not cover in the house
code" -- see that entry rather than duplicating it here.

**The Mallama magnitudes.** Deviation 18 replaces `swe_pheno`'s Hilton 2005 magnitude model with
Mallama 2018 (plus a Vreijsen term for the Moon), moving 736 `pheno` rows with no package
reference (`pheno` is `mixed`, not `local`, but the magnitude model itself was never part of the
2.8.0.2 package's own output for these flag combinations -- see the corrected provenance table
above) and no conformance row passing on it at the time. That is no longer the whole picture.
Checked against `Tests/conformance/known-fail.tsv`'s change at the deviation 18 and deviation 19
landing commits: suite 9 testcase 3 (`swe_heliacal_ut`, which depends on magnitude to judge
visibility) has five iterations, and all five improved. Iteration 7 (9.3.7) now passes outright,
pruned from `known-fail.tsv`. Iterations 5 and 6 had been off by roughly a full day before the
Mallama port (`xxtret[0]` differing by 0.9999 and 1.0034 days against Astrodienst's reference) and
are now off by 1.16e-5 and 1.27e-4 days respectively -- three to five orders of magnitude closer.
Iterations 3 and 4 each had one field resolve exactly and their remaining field's error shrink to
roughly a fifth (from ~5.8e-5 to ~1.16e-5 days). None of this is proof the Mallama coefficients are correct
-- a mistyped coefficient could easily still be wrong and simply less wrong than Hilton 2005 was
for this particular date range -- but it is real, independent corroboration from Astrodienst's own
reference values, not merely "the baseline moved."

## eclipse_how's 100-to-1 change: the counter-example worth reading carefully

Deviation 19 also changes `eclipse_how`'s `attr[0]`/`attr[2]` sentinel from `100` to `1`
(`swecl.c:1067-1087`), moving 380 rows in the `eclipse` area (320 `LOW`, 60 `SEW`). Read only as
"380 baseline rows changed," this looks like the same kind of evidence as the areas above. It
is not, and the difference is worth spelling out because it is easy to miss.

**Every one of the 380 changed rows is a non-eclipse case.** Checked directly: all 380 carry
`serr = "no solar eclipse at tjd = ..."` -- the port asked for an eclipse on a date with none, and
the changed field is exactly the sentinel value passed through in that failure path (confirmed
field by field: e.g. `SEW\|1000000` reads `100` before the fix and `1` after, with every other
field, including the `serr` text, byte-identical). **The 120 `SEH` rows -- the ones that do compute
a real eclipse, `retc = 0` with a populated `attr[]` -- did not move at all**, confirmed by diffing
all 120 across the same commit boundary. So the baseline's 380-row movement, on its own,
demonstrates nothing about whether `1` or `100` is the value a real eclipse magnitude computation
should carry. It only proves the constant embedded in one error path changed, which is true but
uninteresting -- a caller who checks `retc` before reading `attr[]` (as the API contract requires)
would never observe it.

**The real evidence is six conformance rows in suite 8**, not the 380 baseline rows. Astrodienst's
own reference values in `t.exp` expect `xxattr[0]`/`xxattr[2]` to be `1`; before this fix the port
returned `100` for genuine eclipse computations, not just the error path. Diffing
`Tests/conformance/known-fail.tsv` at the deviation 19 landing commit (`01cec05`): three
iterations -- 8.6.2, 8.7.5, 8.7.7 -- had their *entire* mismatch resolved by this one change and
were pruned, now fully passing. Three more -- 8.6.1, 8.7.1, 8.7.3 -- had the `xxattr[0]`/`xxattr[2]`
component of their mismatch resolved (their reason string no longer mentions `attr[0]`/`attr[2]`
at all) but remain in the file failing for an unrelated reason (position fields, `xxtret`/
`xxgeopos`, off by sub-second amounts traceable to ephemeris precision, not this fix). Six rows
show the fix taking effect against real reference values; three of them now fully pass.

The lesson: when a change touches both an error-message path and a real computation path with the
same constant, a baseline row count alone cannot tell you which one moved. Here the baseline's 380
rows are the uninteresting half and the conformance oracle's six rows are the ones that actually
say something about correctness. The next time a deviation entry reports "N rows moved" for a
change like this, check what those rows' `serr`/`retc` actually say before treating the count as
evidence of anything beyond "the constant is now embedded in the output."

## Three file-layer divergences: two closed, one remains

Found while porting `swetest.c`/`swemini.c` to 2.10.03. All three predate that work: they sit in
`sweph.c`'s file layer, carried in the port since 2.08, and 2.10.03 leaves these sites unchanged.
None was fixed at the time this was written; they were recorded so a future porter would not have
to rediscover them. **`PATH_SEPARATOR` and the `AS_MAXCH` check are now closed**, alongside
restoring `swi_fopen`'s actual transliteration -- see "OnLoadFile superseded" above, which is
where the fix landed and cites the exact C lines. `DIR_GLUE` remains open, deliberately: it is a
narrower case (see its own paragraph below for why it is safe to defer where the other two were
not) and is still deferred to the same release-stage breaking-change list `OnLoadFile` was.

**`PATH_SEPARATOR` was always `';'` as a single `char`. Closed:** it is now `char[]`, matching the
C's cut-list shape, and `swi_fopen` calls `swi_cutstr` against it the way `sweph.c:2377` does
instead of `string.Split`. The *value* deliberately stays `{ ';' }` rather than adopting Unix's
`";:"` -- see "OnLoadFile superseded" above for why a bare `':'` is not safe to add on a
cross-platform port (it collides with a Windows drive letter). A Unix caller passing a
colon-separated path still gets one unsplit entry, which is now a considered choice rather than a
`char`-width accident.

**`DIR_GLUE` is always `'/'`, so the "not found" message reads wrong on Windows.**
`SwissEph.sweodef.h.cs:153` sets `DIR_GLUE = '/'` unconditionally, for the reasons already recorded
above under "DIR_GLUE fixed" -- a single cross-platform value has to pick one separator, and `/` is
the one both Windows and everything else accept. The C instead compiles a different literal per
platform (`sweodef.h:304` gives `"/"`, `:319` gives `"\\"` under MSDOS), so on Windows the C joins
paths with `\` and the port joins with `/`. Both still open the file -- Windows accepts either
separator -- so there is no numeric effect. The `"SwissEph file '%s' not found in PATH '%s'"`
warning (`sweph.c:2400`, `Sweph.cs:2807`) embeds the joined path, though, so its *text* differs by
one character on Windows, and that text mismatch is already visible in 11 rows of
`Tests/swetest/known-diff.tsv`. Changing `DIR_GLUE` back to a per-platform value would be a
breaking change for any `IEphemerisFileProvider` consumer that matches on the separator in a file
name it receives -- the same consumers called out in "DIR_GLUE fixed" above -- so it belongs with that
entry's deferred release-stage work, not with this file-layer note. It has not been added there yet:
`README.md`'s `# Breaking changes` / `## Unreleased` section, which is where that deferred work
belongs, has no entry for this Windows-only diagnostic-text divergence.

**`swi_fopen` never checked `AS_MAXCH`. Closed:** it now does, at the same site the C does. What
follows is the state as originally found, kept for the record:

```c
if (strlen(s) + strlen(fname) < AS_MAXCH) {
  strcat(s, fname);
} else {
  if (serr != NULL)
    sprintf(serr, "error: file path and name must be shorter than %d.", AS_MAXCH);
  return NULL;
}
```

The port's `swi_fopen` (`Sweph.cs:2775-2781`) carries this block only as a comment (directly below
the live code, `Sweph.cs:2788-2799`) and instead builds the path unconditionally:

```csharp
fnamp = s.TrimEnd('\\', '/') + SwissEph.DIR_GLUE + fname;
```

An ephemeris path long enough to trip the C's guard never got the
`"error: file path and name must be shorter than %d."` message from the port at all; it was passed
through to `SE.LoadFile` regardless of length. (Historical: `SE.LoadFile` itself is also gone,
replaced by `SE.OpenBinary` -- see "OnLoadFile superseded" above.)

## swetest.c's missing `spmoon` declaration: fixed on upstream master, not on the pinned tag

`swetest.c` uses `spmoon` at `:1139`, `:1140` and `:1621` (reading `-xv`, then `atoi`-ing it for
the `v` planetary-moon selector) but never declares it in the pinned `v2.10.3final` tag.
`Tools/CReference/build-c.ps1` patches a declaration in ahead of `sastno`'s;
`Programs/SweTest/Program.cs:759` carries the equivalent field for the port; the two are kept at
the same default value so the C reference binary and the port fail the same way under
`swetest -pv` with no `-xv`.

Astrodienst has since fixed this on the `aloistr/swisseph` `master` branch, past the pinned tag:
`static char spmoon[AS_MAXCH] = "9501";  // Jupiter Moon Io`, sitting between `sastno` and `shyp`.
Both this repo's patches now use `"9501"`, matching that fix rather than inventing a value; an
earlier version of both used `"9001"`, which is not a moon of anything (the planetary-moon
numbering is `SE_PLMOON_OFFSET`, 9000, plus the host planet's number times 100, so 95xx is a
Jupiter moon and Io is the first one, `9501`, not `9001`).

A future submodule bump past the commit that adds this declaration must drop both patches instead
of applying them on top. `build-c.ps1` already asserts the declaration is absent before patching
and fails loudly, naming this exact scenario, if it finds one already there
(`'swetest.c: spmoon is already declared. The upstream compile defect this patch exists for may no
longer apply...'`) -- confirmed against the master-branch declaration text directly, which matches
the assert's pattern and would trip it.

## swe_set_jpl_file: the C's AS_MAXCH clamps are not reproduced, and the comments were 2.08's

`swe_set_jpl_file` changed in 2.10.03 (`sweph.c:1475-1529`, against `:1491-1538` in 2.08). The C
now copies its argument into a local `s[AS_MAXCH]`, truncating at `AS_MAXCH - 1` when the argument
reaches 256 characters, runs `strrchr` on that copy rather than on the caller's buffer, and fixes
`sp[AS_MAXCH] = '\0'` to `sp[AS_MAXCH - 1] = '\0'`. The 2.08 form wrote one past the end of a
256-byte array.

`Sweph.cs:1727` reproduces none of it, deliberately. Both clamps exist to keep a `strcpy` inside a
fixed C buffer, and `swed.jplfnam` is a C# `string` with no such bound, so there is nothing for the
truncation to protect. Every other `AS_MAXCH` occurrence in `Sweph.cs` is likewise commented-out C
rather than live code; adding a clamp here would make this the only exception in the file, and it
would import a C buffer limit as behaviour by truncating a filename that currently resolves.

The second clamp is unreachable in the C regardless: after the first one `s` is at most
`AS_MAXCH - 1` characters, so its suffix `sp` can never reach `AS_MAXCH`.

What was actually wrong here was the commented C, which still quoted the 2.08 body including the
off-by-one write, so the file misrepresented what upstream does. That is now the 2.10.03 text.

The residual behavioural difference is bounded and cannot reach a computed number. `swed.jplfnam`
feeds `open_jpl_file` and nothing else, so a caller passing a filename whose basename reaches 256
characters gets the untruncated name here where the C gets 255, which changes only whether the file
is found. A second difference in the same class: the C takes the basename of the clamped copy, so
for a path longer than `AS_MAXCH` it can compute a different basename than the port, which takes it
from the full string.

This was the one gap a function-by-function audit of all 87 shared `sweph.c` functions found still
at the 2.08 form, after Phase 4 reported the file complete. It survived because
`scripts/gen-delta.ps1` labels each hunk with the nearest *preceding* function signature, which is
often not the function the change lands in, and the slice work lists were built from those labels.

## swetest.c's zodiac field: a sign the C itself can lose, reproduced instead of dodged

`dms()` (`swetest.c:2642-2731`) formats a degree value with `sprintf`, then patches a minus sign
into the result by hand: `sp = strpbrk(s, "0123456789"); *(sp - 1) = '-';` (`:2723-2725`). That
overwrites the character immediately before the first digit -- it assumes there always is one.

Under `BIT_ZODIAC`, the degree field is `sprintf(s, "%2d %s ", kdeg, zod_nam[izod])` (`:2686`),
`kdeg` being 0-29 within the sign. `"%2d"` only pads to width 2 when `kdeg` is single-digit; once
it reaches 10, the field is exactly two characters and the first one is a digit at index 0. The
minus-sign write then lands at index -1: one byte before the C's own buffer. `swetest -p0 -d1
-b3.1.2020 -fPZ` shows it directly, printing `27 ge 50' 3.9344` for a value of -27 instead of
`-27 ge...` -- the sign is gone, not misplaced.

An earlier version of `Programs/SweTest/Program.cs`'s port of this function kept a leading space on
every `BIT_ZODIAC` field (`" %2d %s "` instead of the C's `"%2d %s "`) to dodge the crash a literal
translation of the sign-insertion would otherwise hit at index 0. That traded one problem for a
bigger one: it diverged from the C's column width on every zodiac field, in every rounding mode,
not just the one input where the C loses its sign. The port now matches `dms()`'s own format
exactly and instead guards the sign-insertion site: when the first digit sits at index 0, it
prepends the minus rather than splicing at index -1. That keeps the port byte-exact with the C for
every non-negative value and confines the divergence to the single case the C itself gets wrong --
verified against `external/.c-reference/swetest.exe` under `-fPLZ`, `-fPLZ -roundmin`,
`-fPLZ -roundsec`, and the `-fPZ` case above.

**Reframed: reaching this needs `-d`, and `-d` with `-fZ` is the wrong flag combination in the
first place.** Astrodienst reviewed this report and declined it, correctly. A zodiacal position
format is not a way to express an angular difference: `-fZ` formats a position in
sign/degree/minute/second form, and `-d` asks for a differential value between two positions.
`-fL`/`-fl` (plain longitude) is the format a differential value belongs in; combining `-d` with
`-fZ` is an application-level error, not a legitimate call this repro path exercises. Verified
independently against `external/swisseph/swetest.c`: it has exactly three `BIT_ZODIAC` sites, and
the two that format a node longitude both take a value `swe_nod_aps` already normalizes into
`[0, 360)`, so a negative value reaching `dms()` under `BIT_ZODIAC` at all is only reachable through
the differential path this section's repro uses.

The port keeps its guard anyway. `dms()`'s `*(sp - 1) = '-'` at an index-0 first digit writes one
byte before the start of a local C buffer -- undefined behavior, not a defined C result this port
could faithfully reproduce. Guarding the site instead is the only sound choice here, independent of
whether `-d -fZ` is a combination any caller should actually use.

This is recorded here, not filed upstream. Astrodienst's own reporting channel is outside this
repository's control, so "reported upstream" should never be written into a code comment as a
statement of fact without a tracked issue behind it.

## swe_solcross(SEFLG_HELCTR): an upstream libswe hang, not a grid problem

Found while building `Tools/OracleGrid/gen-grid-analytic.ps1`'s crossing-function coverage. Every
one of `swe_solcross`'s three documented flag bits (`external/swisseph/sweph.c:8312-8315`) was
meant to get its own grid row, `SEFLG_HELCTR` included, until a `SOLCROSS|90|1200000|HELCTR`-shaped
row made `sedump.exe` spin forever with zero output.

**Mechanism.** `swe_solcross` (`sweph.c:8321-8343`) hardcodes `int ipl = SE_SUN;` and never
substitutes `SE_EARTH`, despite its own doc comment reading "`SEFLG_HELCTR` ... 1 = heliocentric,
EARTH". So a caller passing `SEFLG_HELCTR` asks `swe_calc` for the heliocentric position of the Sun
itself -- the coordinate origin by definition, with an always-zero speed (`x[3]`). The refinement
loop is:

```c
for(;;) {
    if (swe_calc(jd, ipl, flag, x, serr) < 0)
      return jd_et - 1;
    dist = swe_difdeg2n(x2cross, x[0]);
    jd += dist / x[3];
    if (fabs(dist) < CROSS_PRECISION) break;
}
```

For `x2cross` values whose initial distance estimate does not already land within
`CROSS_PRECISION` on the very first pass (every value tried except `x2cross` at exactly 0.0/360.0,
where `dist` starts at 0 and the loop exits on its first iteration before the division), `dist /
x[3]` divides a nonzero `dist` by that zero speed. IEEE 754 gives `+Infinity`, not a fault, so `jd`
becomes `+Infinity` and the next `swe_calc(Infinity, SE_SUN, ...)` call inside `libswe` itself never
returns -- confirmed by isolating exactly that one row (`SOLCROSS|90|1200000|HELCTR`, x2cross=90,
via a purpose-built repro grid, not guessed from reading the loop) against the built `sedump.exe`
and observing unbounded CPU time (measured past 370 seconds and still climbing) with no output.
Killing the process and re-running the same row alone, with `x2cross` at exactly 0.0, completes
immediately and returns `NaN` -- the `0/0` form of the same division, not `Infinity`, and a `for(;;)`
that happens to exit on its first pass regardless (`fabs(NaN) < CROSS_PRECISION` is a false
comparison, but the loop's own body already ran once, so the corrupted `jd` propagates out rather
than looping). Both are the same defect; only the second one hangs, because it needs more than one
iteration to reach the division that produces `Infinity` instead of `NaN`.

This hangs Astrodienst's own C, built with the MSVC toolchain this repository's oracle is locked
to (`Tools/CReference/build-c.ps1`) -- it is an upstream `libswe` defect, not a mistranslation, and
not something a grid can work around by choosing different inputs; every `x2cross` value that is
not exactly 0.0/360.0 reaches it. `Tools/OracleGrid/gen-grid-analytic.ps1`'s `$SolCrossFlagCombos`
excludes `SEFLG_HELCTR` for this reason, with the mechanism summarized in that script's own
comment; this entry is the fuller record.

**The port almost certainly shares this hazard, and that is untested.** `SwissEphNet/CPort/Sweph.cs`'s
`swe_solcross` (citing `sweph.c:8310-8343`) is a line-by-line transliteration: it hardcodes `int ipl
= SwissEph.SE_SUN;` the same way, and its refinement loop divides by `x[3]` the same way. C#'s
`double` follows IEEE 754 exactly as C's does, so `dist / x[3]` at `x[3] == 0` produces the same
`+Infinity`, and nothing in `swe_calc`'s ported form is known to reject an infinite `jd_et` any
more than the C does. No one has actually called `swe_solcross(x2cross, jd_et, SEFLG_HELCTR, ref
serr)` on the port with a non-degenerate `x2cross` and confirmed a hang (or confirmed it does not
hang, for whatever reason) -- this paragraph is a structural argument from the shared source, not a
reproduced result. Confirming it either way on the C# side is future work.

**Do not fix the port.** `SwissEphNet/CPort/Sweph.cs` is a transliteration-frozen path
(`CONTRIBUTING.md`), and even setting that aside, this is a design decision (how the port should
guard against or recover from a hang its own upstream source has) separate from porting 2.10.03,
not a divergence-from-the-C correction the freeze's one exception covers -- the port is faithful to
the C here, which is exactly the problem. Recorded so a future porter (or anyone routing a caller-
supplied `x2cross` into `swe_solcross` with `SEFLG_HELCTR` set) knows this before hitting it in
production rather than during an oracle run.
