# Known issues found by the characterization baseline

Findings from building and running the baseline gate: some cross-platform (Windows,
the platform the gate is locked to, vs. a Linux container with the same SDK), some
single-platform library defects the matrix happened to surface along the way. All
are faithfully frozen in the committed baseline rather than worked around, so a
porter needs to know they were seen deliberately. See `Tools/BaselineGen/README.md`
for why the gate is platform-locked rather than given a looser tolerance.

The file has since grown well past its original scope: it now records defects found by
the conformance oracle, the bit-exact oracle and the swetest text diff as well, plus
corrections to earlier entries in this same file. Every entry carries a **Status** line
directly under its heading, and the index below is those lines in one place. Statuses
mean:

- **closed** -- fixed, with the fix verified on the current tree
- **open** -- still reproduces, or still a gap; the status line says what would close it
- **open, narrowed** -- part of the entry has been answered by later measurement and part has
  not; the status line says which is which, and the body carries the measurement
- **open upstream** -- real, reproduces, and the fix belongs in Astrodienst's source, not here
- **won't fix** -- a deliberate, permanent divergence, with the reasoning in the entry. Includes
  upstream behaviour this port reproduces faithfully: "the C does this too" is a reason not to
  change it, not a reason to call it fixed
- **record** -- never a work item: a correction, a lesson, or expected behaviour

The bodies below are append-only in spirit. Where an entry's opening paragraph has been
overtaken by later measurement, the correction is added underneath rather than folded in,
because several of these exist specifically to stop a wrong diagnosis being re-derived.
Read the status line first and the body for how it got there.

### Status index

| Entry | Status |
|---|---|
| `swe_houses_armc`, hsys `'Y'` (APC houses) | won't fix |
| `swe_houses_armc` reports success while emitting NaN cusps | closed |
| `swe_houses_armc`, hsys `'i'` (Makransky): cusp = 360.0 | won't fix |
| `swe_calc(SE_ECL_NUT)` returns all-zero output | won't fix |
| `swe_houses` and `swe_houses_ex(iflag=0)` disagree | closed |
| calc/pheno SPEED fields: differentiation noise | record |
| DIR_GLUE mis-transliteration | closed |
| netstandard2.0-only infinite recursion | closed |
| Five transliteration-fidelity defects | closed |
| `swe_fixstar_ut` distance speed | record |
| Negative-zero fields under SIDEREAL | record |
| hsys `'I'` (Sunshine): divergences near tolerance | record |
| `swe_house_pos`: 27 cross-platform divergences | record |
| `hcusp[36]`: faithful to 2.08, not 2.10.03 | closed |
| hsys narrowed to `char` | closed |
| Cross-platform divergence, and the platform lock | record |
| `swi_strnlen` outlived its deletion | closed |
| `calc_nutation_woolard`: C# `long` is 64-bit | won't fix |
| `swe_nod_aps` after `swe_close` | closed |
| Inverted `serr != NULL` guards | closed |
| The 7.2.x diagnosis in regenerations.log | record |
| `SE_VERSION` | closed |
| Constants from the header delta | closed |
| `sid_data` is a struct, so the copy does not alias | closed |
| `swe_calc`'s serr for ipl 13 | closed |
| OnLoadFile superseded by IEphemerisFileProvider | closed |
| Pointer arithmetic as concatenation in SweTest | closed |
| The file-backed grid's divergence is Earth's position | closed |
| What the oracle grids do not cover in the house code | open, narrowed |
| The pyswisseph replay's `swe_house_name` limitation | record |
| Three wrong numbers in the local-regenerations log | record |
| What local-mode regenerations have no check on | open, narrowed |
| `eclipse_how`'s 100-to-1 change | record |
| Three file-layer divergences | closed / won't fix |
| swetest.c `spmoon` / `gethostname` / `do_printf` | open upstream |
| `swe_set_jpl_file`'s AS_MAXCH clamps | won't fix |
| swetest.c's zodiac field | closed |
| `swe_solcross(SEFLG_HELCTR)` upstream hang | won't fix |
| `insert_gap_string_for_tabs` drops LEN_SOUT | closed |
| The 5% waiver caps divide by the whole area | record |
| `DivergenceReport`'s field-compared count | closed |
| 31 of 107 public entry points have no matrix coverage | closed |
| SweJPL rejected a non-ASCII constant-name block | closed |
| The analytic grid's artefacts depend on SE_EPHE_PATH | closed, one residual |
| swetest `-D<n>`: `xobl[0]` aliasing | won't fix |
| `%-Ns` / `%.Ns` padded by characters, C by bytes | closed / won't fix |
| `C.atoi` saturation; `C.atof`'s remaining gaps | closed / won't fix |
| `SwissEph.DefaultFileProvider` widened to a property | record |
| Eight live buffer-vs-unbounded-string sites | won't fix |

## Eight live sites transliterate a C fixed-size buffer as an unbounded C# string

**Status: won't fix.** All eight triaged; none has a plausible trigger with real data.

`docs/compliance-2.10.03.md`'s "What this record does not cover" originally cited seven
call sites where a C `char buf[AS_MAXCH]` (256 bytes, `SwissEph.sweodef.h.cs:137`) fixed-size
stack buffer is transliterated as a live, unbounded C# `string` -- the C's truncation-on-
overflow behavior is consequently not reproduced. **That citation was itself stale**: six of
its seven line numbers pointed at unrelated code when checked directly against the current
tree (`SwephLib.cs:4682` is a `switch (biasmod)` lookup with no buffer at all), and its count
of three fixstar TLS-cache pairs missed a fourth, `swe_fixstar_mag`. The compliance doc's
citation list has been corrected to the real eight, found by grepping `SwissEphNet/CPort/`
for `//char \w+\[`/`AS_MAXCH` and reading each hit's enclosing function to confirm it is
live: `SweHel.cs:327` (`DeterObject`); `SwephLib.cs:4725` (`swe_get_astro_models`); `Sweph.cs:
7472` (`load_all_fixed_stars`); `Sweph.cs:8770-8776` (`swi_fixstar_load_record`, three
buffers in one function); and four fixstar-family TLS-static `slast_stardata`/
`slast_starname` pairs (`swe_fixstar2` at `:8087-8135`, `swe_fixstar2_mag` at `:8187-8216`,
`swe_fixstar` at `:9296-9354`, `swe_fixstar_mag` at `:9409-9462`).

**Triage: none is load-bearing enough to add truncation.** Two categories:

- `SweHel.cs:327` and `SwephLib.cs:4725` hold short, code-controlled strings (a celestial
  body name being matched against literal prefixes like `"sun"`/`"venus"`; a debug-only
  model-name string for `swe_get_astro_models`, itself commented "function for inhouse
  testing only"). Truncating a 256+ character input to either would not change the
  prefix-match or debug-formatting outcome for any input that could plausibly reach them.
- The other six (`load_all_fixed_stars`, `swi_fixstar_load_record`, and all four TLS cache
  pairs) hold star-catalog lines/records read from `sefstars.txt` or a user-supplied star
  file. Measured directly: the longest line in the shipped `external/swisseph/ephe/
  sefstars.txt` is 129 characters (`awk '{ if (length > max) max = length } END { print max
  }' sefstars.txt`), 127 bytes under the 256-byte bound -- no shipped data can trigger the
  C's truncation. A user-supplied file with an unusually long line remains a theoretical
  trigger, but reproducing C's truncation there would make the port a strict *regression*
  against such a file (silently losing catalog fields the C# could otherwise parse
  correctly) for zero observed fidelity benefit, since nothing currently depends on matching
  C's truncation behavior.

Not fixed, for the same reason other `won't fix` entries in this file give: none of the four
verification instruments (baseline, bit-exact oracle, correctness oracle, SweTest diff) has
produced an observed divergence from any of these eight, and adding truncation here would
trade a correctness improvement (parsing a legitimately longer input in full) for reproducing
a C limitation with no demonstrated trigger.

## swe_houses_armc, hsys 'Y' (APC houses): a genuine, large divergence

**Status: won't fix.** Mechanism found and confirmed directly: catastrophic
cancellation inside `apc_sector`, inherent to the formula and shared with the C.
Two things this entry previously said turned out to be wrong when checked directly
against the current tree and a live Linux run -- see "Corrections" below.

`swe_houses_armc(armc=270, geolat=50, eps=40, hsys='Y', ...)`:

| Platform | cusp[1] |
|---|---|
| Windows | `270` |
| Linux | `243.43494882292202` |

A 26.6-degree difference is not floating-point noise -- reproduced directly: a
standalone probe referencing this repo's `SwissEphNet.csproj` was run natively on
Windows and inside a Linux container (`mcr.microsoft.com/dotnet/sdk:10.0`, Ubuntu
24.04.4, .NET 10.0.10 -- the same environment `Tools/BaselineGen/README.md`'s
"Platform lock" measurement uses), calling `swe_houses_armc` with these exact
inputs on both. Both reproduced their respective committed-baseline value exactly.

**Mechanism, confirmed by instrumenting `apc_sector` (`SweHouse.cs:934-986`) directly:**
`geolat=50` and `eps=40` are complementary (`50 + 40 = 90`), so `Math.Tan(ph) *
Math.Tan(e)` evaluates to `0.9999999999999999` -- one ULP below the mathematical
value of exactly `1`. With `armc=270`, `Math.Sin(az)` is exactly `-1`, so `kv`'s
denominator, `1 + Math.Tan(ph) * Math.Tan(e) * Math.Sin(az)`, collapses to
`1.1102230246251565E-16`: a near-total cancellation between two values close to `1`
and `-1`, leaving only a residual at the scale of one ULP. Measured directly: this
denominator (and the numerator, and `kv` down to its `atan` call's *input*) is
bit-for-bit identical on both platforms. `Math.Atan` itself then returns results one
ULP apart (`kv` bits `...3AA4` on Windows vs `...3AA5` on Linux) for that identical
input -- ordinary cross-platform libm noise of exactly the kind this file's "calc/
pheno SPEED fields" entry already documents elsewhere in this matrix. `dasc` and the
normalized right-ascension `a` that feed into the function's final step both then
print bit-identical on both platforms despite that. The 26.6-degree divergence
appears only in `apc_sector`'s last line, `dret = Math.Atan2(y, x)` (`SweHouse.cs:
983-984): `y` and `x` are themselves each built from several further `Tan`/`Sin`/
`Cos` calls on `dasc`/`ph`/`az`/`a`/`e`, and at this exact geometric configuration
their combination lands `atan2` at a second near-singularity, where even a single
one-ULP difference anywhere upstream flips the returned angle by tens of degrees.
Hand-reproducing the full function (kv through dret) outside the library, on both
platforms, matches each platform's real `cusp[1]` output exactly (`270` on Windows,
`243.43494882292202` on Linux), confirming this account end to end rather than only
at the boundary.

Because `apc_sector` is byte-identical to `swehouse.c`'s C (already established
below) and no argument ever leaves an unclamped domain (also already established
below), this is not a mistransliteration and not a branch split -- it is inherent
numerical instability in the C formula itself at this specific `geolat`/`eps`/`armc`
combination, amplified by ordinary cross-platform transcendental-function ULP noise
that this repository's own measurements already establish happens elsewhere too.
The C would show the same sensitivity if built against a different platform's libm.
Nothing in `SweHouse.cs` can be changed here without deviating from the C's own
(byte-identical) formula, which the transliteration freeze does not permit absent a
cited divergence from the C -- and there is none to cite.

This is not a tolerance problem: it survives every threshold measured in
`Tools/BaselineGen/README.md`'s "Platform lock" table, including the loosest
(1e-8 absolute / 1e-8 relative -- eight orders of magnitude looser than what
ships). It is the only `houses-armc` field that does, which is exactly what
catastrophic cancellation at a near-singularity looks like: not bounded accumulated
rounding error that shrinks as tolerance loosens, but an unbounded amplification of
sub-ULP input noise.

**Corrections to this entry's own history, found while confirming the mechanism
above.** Both are corrected, not merely noted, everywhere above:

- **The diverging field is `cusp[1]`, not `cusp[2]`.** `cusp[2]` for this exact case
  is `90` on both platforms, bit-identical -- confirmed directly against the
  committed baseline file (`Tests/baseline/baseline-houses-armc.tsv`, row
  `H|Y|40|50|270`) and against a live Linux run. This entry cited `cusp[2]` from its
  first version through its most recent "re-measured, unchanged" pass; the actual
  row was never wrong, the array index attached to it was.
- **The two acos/asin-clamp hypotheses (the original one, and the "exact-equality
  branch" one that replaced it) were both dead ends, and are now superseded rather
  than merely refuted.** `apc_sector` contains no `acos`/`asin` (confirmed, as
  before: only `Atan`, `Atan2`, `Tan`), so no clamp is relevant. The exact-equality
  branch this entry proposed next (`Math.Abs(fi) >= 90 - ekl`, comparing `50` against
  `90 - 40`) was measured directly and found to evaluate identically -- `True` on
  both platforms, with `90 - ekl` itself bit-identical on both, because `90 - 40` is
  an exact integer subtraction with no rounding possible on any IEEE-754 platform.
  That branch's outcome cannot differ between platforms for this input, full stop --
  and a global branch flip would in any case have shifted many or all of the 12
  house cusps together, not isolated `cusp[1]` alone while `cusp[2..12]` stay within
  tolerance. The real mechanism, above, was found by instrumenting the actual
  computation rather than reading the code for a second candidate.

## swe_houses_armc reports success while emitting NaN cusps

**Status: closed.** The `swehouse.c` port landed `niter_max`; all 176 rows match 2.10.03 C
bit for bit and `Tests/oracle/known-diff.tsv` is empty.

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

Measured by the bit-exact comparison harness against the 14,220-row analytic grid of
the time: 176 rows returned a different `retc` from 2.10.03 C, all `swe_houses_armc`
at `eps=0`, 88 with `hsys = 'G'` and 88 with `hsys = 'P'`. Against **2.08** C all 176
matched exactly, and `Tests/oracle/version-classification.tsv` classified every one of
them `TRACKS-2.08` with `port_vs_2.08 = MATCH`. 2.08 returns `OK` with NaN cusps,
which is what the port did at that point.

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

Read the paragraph above as a record of a past state, not a current one: the
`TRACKS-2.08` classification it cites no longer exists anywhere in the file. Measured on
the committed classification with

```powershell
Get-Content Tests/oracle/version-classification.tsv |
    Where-Object { $_ -notmatch '^#' } | Select-Object -Skip 1 |
    ForEach-Object { ($_ -split "`t")[1] } | Group-Object | Select-Object Count, Name
```

every data row is `AGREES-BOTH` or `TRACKS-2.10.03`, and zero are `TRACKS-2.08`; the
same holds for `version-classification-files.tsv`. That file is regenerated and diffed
against what is committed by `.github/workflows/oracle.yml`, so it tracks the port
rather than the moment someone last looked at it.

An earlier revision of this paragraph said the port "swallows" an error the C
reports, which was wrong in the way that costs time: it would have sent someone to
fix code that is already correct for the version it tracks. The
`Tests/oracle/version-classification.tsv` data that refutes it was available and
unread. It also claimed `'G'` and `'P'` were the only house systems the grid crosses
with `eps=0`; the grid crosses all 25 letters with `eps=0`, and `G` and `P` are
simply the only two where `retc` differs. `{J, Z, 0}` are untested because they are
not in the grid at all.

## swe_houses_armc, hsys 'i' (Makransky Sunshine houses): cusp = 360.0, missing normalization

**Status: won't fix.** The value still reproduces, but it is not a port defect: upstream C
emits the same unnormalized `360.0`. Adding a `swe_degnorm` here would be a deliberate
deviation from Astrodienst, not a fidelity fix. See the correction at the end of this entry.

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

**Checked against the C, and the paragraph above is wrong where it counts.** "The Makransky
branch in `SweHouse.cs` is missing a `swe_degnorm` call somewhere on this path" describes a
port defect. There is none: `CPort/SweHouse.cs:3340-3344` and `external/swisseph/swehouse.c:3040-3044`
are the same lines. Both apply `swe_degnorm` only on the `lat < 0` sub-branch, and both then
write `cusp[ih] = cu` unnormalized, so `cu = 360 - r` with `r == 0` stores an exact `360.0`
in either language.

The comparison with `'I'` that the paragraph leans on is also misleading. `'I'` (Treindl) has
no extra `swe_degnorm` either -- it stays inside `[0, 360)` only incidentally, because it
computes its cusps through `Asc1` (`SweHouse.cs:3443-3444`, `swehouse.c:3132-3133`), and
`Asc1` normalizes its own input at `swehouse.c:2062` and returns a quadrant-resolved value.
The Makransky path never calls `Asc1`; it builds `cu` directly from `atand` results. That
difference exists in the C, not in the port.

So this is an upstream defect faithfully transliterated, and the status is `won't fix` rather
than `open`: adding a `swe_degnorm` here would move 280 fields *away* from bit-exact agreement
with Astrodienst and would need a deviation note recording a deliberate divergence, not a
fidelity fix. **Do not "fix" it** without deciding, explicitly, to diverge from upstream.

The `Comparer.EffectiveAbsoluteDiff` note above stays correct and is now load-bearing for a
different reason: it must keep refusing to wrap an exact `360.0`, so that if upstream ever
normalizes this path and the port follows, the gate reports the change rather than absorbing it.

## swe_calc(SE_ECL_NUT) returns success with all-zero output for several iflag combinations

**Status: won't fix.** The behaviour still reproduces, and it is what upstream C does:
`sweph.c` writes only `x[0..3]` in both 2.08 and 2.10.3b. See the correction at the end of
this entry, which also detaches 30 rows this entry had wrongly attributed to it.

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

**Checked against the C on both sides, and the port is faithful.** The paragraph above says
the port "writes only the first four `x[]` slots before returning" as though that were the
divergence. It is the C's own shape. `external/swisseph/sweph.c:656-664` and
`external/pyswisseph-2.08/sweph.c:633-641` are character-for-character identical to each
other and to `CPort/Sweph.cs:776-789`: all three assign `x[0..3]`, scale them by `RADTODEG`,
and `return(iflag)`. This path did not change between 2.08 and 2.10.3b, so there is no
version delta to port here either.

The zeros come from `end_swe_calc`, and that is identical too (`sweph.c:507-522` against
`Sweph.cs:610-631`): it reads `sd->xsaves+12` for `SEFLG_EQUATORIAL` and steps a further
`+6` for `SEFLG_XYZ`, offsets that `swecalc` never wrote for this pseudo-body. On a fresh
save area those slots are zero in C exactly as they are here. The C is equally capable of
returning success with zeros; nothing about the port makes it worse.

So the status is `won't fix`. Filling those slots would be a deliberate divergence from
Astrodienst, not a fidelity fix. The user-facing complaint in the opening paragraph -- silent
zeros with a success code and empty `serr` -- is a fair criticism of the upstream API, and is
the right thing to report upstream rather than to patch here.

**The 30 `CDEF|-1|*` / `CUDEF|-1|*` rows do not belong to this entry.** The paragraph above
cites `Tests/baseline/baseline-2.8.0.2.env.txt`'s pyswisseph replay notes as independent
corroboration. They are not corroboration of *this* mechanism: those rows are J2000,
no-nutation and sidereal flag combinations, and none of them sets `SEFLG_EQUATORIAL` or
`SEFLG_XYZ`, which are the only two flags the branch shape above can strand. Whatever makes
real 2.10.03 C return non-zero where the port returns zero on those 30 rows is a different,
currently unattributed mechanism. It is recorded here as open and unexplained rather than
left attached to a `won't fix`, because conflating the two would retire a live question.

## swe_houses and swe_houses_ex(iflag=0) disagree with each other

**Status: closed.** The 2.10.03 `SweHouse` delta unified the two obliquity paths;
`SE_ECL_NUT` appears zero times in `CPort/SweHouse.cs`, which now calls `swi_epsiln`
directly.

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

**Status: record.** Expected behaviour of numerical differentiation, not a defect to fix.
Kept so a future change in the ratio gets a fresh look rather than an assumption.

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

**Status: closed.** The mis-transliteration is fixed and the baseline was regenerated for
it. One consequence is still open by choice: see "Three file-layer divergences" below.

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
(`Sweph.cs:1595-1596`, corresponding to `sweph.c:1339-1340`, an identical C
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

**Status: closed.** Fixed, and `Tests/NetStandard20Smoke.Tests` now covers the
`netstandard2.0` asset on every change; it passes on the current tree.

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

**Status: closed.** All five fixed, each with its own regression test in
`TransliterationFidelityTest.cs` citing the C file and line it diverged from.

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

**Status: record.** Cross-platform differentiation noise that predates the fixed-star
fixes it was found alongside. Accommodated by loosening the assertion, not by pinning a
platform-specific value.

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

**Status: record.** Roundoff on analytically-zero quantities, not a defect. Kept so the
attribution to `SE_TRUE_NODE` rather than the mean node stays on the record.

18 fields in the `calc` area carry a negative-zero sign bit (`-0` rather than `0`)
-- all of them `SEFLG_SIDEREAL`, and all of them `ipl = 11` (`SE_TRUE_NODE`),
confirmed directly against the generated data (`cut -d'|' -f2` on every `-0` row).
These are analytically-zero quantities where the sign bit is roundoff, not a bug;
noted here precisely so the record stays accurate -- ipl 11 is the true node, not
the mean node (`ipl = 10`), which does not show this pattern in this data.

## hsys 'I' (Sunshine houses): smaller, more numerous divergences near tolerance

**Status: record.** Mechanism confirmed: ordinary cross-platform ULP noise compounding
through a long, faithfully-transliterated trig chain, not a domain-clamp flip or a
mistransliteration. One more array-index citation error found and corrected, matching
the pattern found in the `'Y'` entry above.

`swe_houses_armc` with `hsys='I'` produces a cluster of small (1e-10 to 1e-11
absolute) but tolerance-exceeding divergences, mostly at extreme `geolat` and
specific `armc` values. Confirmed still present at 209 `'I'` fields beyond tolerance
in `houses-armc` (re-measured live via a Linux container report-only run against the
committed Windows baseline: `houses-armc` shows 210 fields beyond tolerance total,
209 attributable to `'I'` and 1 to `'Y'` -- matching this entry and the `'Y'` entry
above exactly).

**The array index in this entry's own example was wrong, the same mistake as the
`'Y'` entry above -- corrected here.** For `H|I|0|-89|0`, the value
`119.99999999997604` (Windows) / `120.00000000000338` (Linux) is **`cusp[2]`, not
`cusp[3]`** -- confirmed directly against the committed baseline
(`Tests/baseline/baseline-houses-armc.tsv`, row `H|I|0|-89|0`: field 4 after the case
id, i.e. `cusp[2]`, holds `119.99999999997604`) and against a live probe run natively
on Windows and inside a Linux container (`mcr.microsoft.com/dotnet/sdk:10.0`, Ubuntu
24.04.4, .NET 10.0.10), both reproducing their platform's committed value bit for
bit. `cusp[3]` for this case is `149.9999999999815` (Windows) / `150.00000000000557`
(Linux) -- also cross-platform-divergent, but within tolerance, not the field this
entry names. The direction (Windows lower, Linux higher) was previously corrected
from an earlier, inverted write-up and remains correct.

**Mechanism, confirmed by comparing the port against the C line by line and by
inspecting the full cusp vector, not just the one cited field.** `sunshine_solution_treindl`
(`SweHouse.cs:3358-3437`) matches `swehouse.c:3048-3131` operation for operation --
every `sind`/`cosd`/`tand`/`asind`/`acosd`/`atand` call and every intermediate
(`xhs`, `cosa`, `alph`, `alpha2`, `b`, `cosc`, `c`, `sinzd`, `zd`, `rax`, `pole`)
appears in the same order in both, so this is not a reordering defect. Unlike hsys
`'Y'`'s `apc_sector`, this function *does* call `acosd`/`asind` (`swehouse.c:3086`,
`:3091`, `:3109`, `:3116`) -- but the probe's full cusp vector for `H|I|0|-89|0`
shows small, graduated cross-platform differences at nearly every cusp (1..3, 5, 6,
8, 9, 11, 12 all differ at the 1e-10-to-1e-13 relative level; only 1, 4, 7, 10 --
exact 90-degree-multiple sector boundaries -- are bit-identical), with no `NaN` and
no discontinuity on either platform. That pattern is the signature of ordinary
accumulated rounding noise through a chain of roughly ten chained transcendental
calls per cusp, not a domain-boundary argument flipping in or out of `[-1, 1]`
(which would produce a `NaN` on one platform, not a smoothly-graduated difference
across many adjacent fields). Because the transliteration is confirmed faithful and
the pattern is inconsistent with a branch/domain defect, this is inherent numerical
sensitivity shared with the C, the same disposition as the `'Y'` entry above, not a
port defect to fix.

## swe_house_pos: 27 cross-platform divergences, twelve of them whole-house jumps

**Status: record.** The "same cusp, different rounding side" lead is refuted by the
values' own magnitudes; a different, evidenced mechanism is documented below.

The `house-pos` area has 27 fields beyond tolerance between Windows and Linux,
every one of them `xp[0]` (the returned house position itself, array index 0), and
they split into two unrelated groups.

**Twelve large ones, under `'C'`, `'H'` and `'J'`, four each.** These are not
floating-point noise: the two platforms differ by between 1.35 and 2.76 *sectors*,
i.e. they place the point in a different house. Case ids are
`HP|hsys|eps|armc|geolat|lon|lat`, and all twelve sit at `eps=40`, `armc` of 90 or
270, `geolat` of +45 or -45, `lon` of 90 or 270, and `lat` of +5 or -5. Re-verified
directly, bit for bit: a probe calling `swe_house_pos` with these exact inputs,
run natively on Windows and inside a Linux container
(`mcr.microsoft.com/dotnet/sdk:10.0`, Ubuntu 24.04.4, .NET 10.0.10), reproduces
every value below exactly on its respective platform:

| case | Windows | Linux |
|---|---|---|
| `HP\|C\|40\|270\|45\|270\|-5` | `7.000000009259259` | `8.989096080910333` |
| `HP\|H\|40\|270\|-45\|90\|5` | `3.2898205878702615` | `1.0000000092592594` |
| `HP\|J\|40\|270\|45\|90\|5` | `1` | `3.7548003003837302` |

**Fifteen small ones, all under `'Y'`**, about 2.8e-8 absolute, at `eps=40,
armc=37.5, geolat=0`. Same house system as the APC finding above but a different
area and a different order of magnitude, so treat them as a separate observation
rather than the same one seen twice.

**The "same cusp, disagreeing about which side" lead is refuted by the values
themselves, not merely left untested.** If the two platforms were landing near the
*same* boundary and disagreeing only about which side of it the fractional part
falls on, both values would sit close together, near the same integer, on either
side of it (like `6.999999991` vs `7.000000009`). They do not: `7.000000009259259`
vs `8.989096080910333` are nearly two whole houses apart, and `1` vs
`3.7548003003837302` are more than two apart. Landing near-exactly on a cusp on
*one* platform is real (`hpos = xp[0] / 30.0 + 1` with `xp[0]` near a multiple of
`30`), but the other platform is not near that same boundary at all -- it computed
a substantially different `xp[0]` upstream of the divide.

**A better-evidenced mechanism, by structural analogy with the fully-traced `'Y'`
finding above (not traced to the same atan2-argument level here, so this remains a
strong inference, not a full derivation).** Hsys `'C'` and `'H'` (`SweHouse.cs:
2723-2732`, `:2868-2874`) both compute `xeq[0] = swe_degnorm(mdd - 90)` then rotate
it with `SE.swe_cotrans(xeq, xp, ...)` before dividing by 30 -- `swe_cotrans`
(`SwephLib.cs:234-249`) converts to Cartesian, rotates, and converts back via
`swi_cartpol`, which resolves the result angle with `Math.Atan2` internally, the
same category of computation that produced hsys `'Y'`'s confirmed near-singularity
above. `'J'` (`SweHouse.cs:2734-2793`) uses the same `swe_cotrans` call plus its own
further `asind`/`swe_degnorm` chain. Both `'C'`/`'H'`'s own code comments note a
`MILLIARCSEC` nudge is deliberately added "to make sure that a call with a house
cusp position returns a value within the house" -- i.e. this formula is already
known, by its own authors, to land results essentially exactly on a 30-degree
cusp boundary for some inputs. All twelve failing cases sit at maximally symmetric,
axis-aligned inputs (`geolat=+-45`, `lon`/`armc` at `90`/`270`) -- exactly the
configuration most likely to drive `swe_cotrans`'s internal rotation to a
coordinate-system near-singularity, where (as directly measured for `'Y'` above)
sub-ULP cross-platform differences in the underlying `Tan`/`Sin`/`Cos`/`Atan2`
calls get amplified into a substantially different result. Given `swe_cotrans` is a
plain, unclamped, faithfully-ported coordinate rotation (no domain-restricted
`acos`/`asin` argument in the `'C'`/`'H'` path at all), this is far more consistent
with the evidence than a branch/rounding-side flip, and matches the disposition of
every other confirmed-inherent finding in this file: shared with the C, not a
port defect, not fixable without deviating from the C's own formula.

Worth noting for `'J'` (Savard-A) specifically: four of these twelve are `'J'`, and
`'J'` is the one house system with no external validation of its cusp computation
at all, per "What the oracle grids do not cover in the house code" below. A
cross-platform divergence in a system whose only evidence is transliteration review
is worth more attention than the same divergence elsewhere, not less.

## hcusp[36] fixed: swe_house_pos was faithful to 2.08, not to 2.10.03

**Status: closed.** `CPort/SweHouse.cs` declares `new double[37]`, and
`Tests/conformance/known-fail.tsv` carries no `ERROR`-category row at all.

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
(`Tests/baseline/waivers.tsv`) -- the two are different things.
Freezing keeps a row in the comparison, with its known-bad value as the
expected value, so any change to it (a fix, or a regression) is caught and
must be reviewed. Waiving a row removes it from comparison entirely, which
would have hidden these 375 rows rather than recorded them. The waiver
mechanism was correctly not used here, and should not be: every waiver is
staleness-checked (a waiver that matches zero rows, or whose matched rows are
all byte-for-byte identical to the baseline anyway, fails the run --
`Tests/baseline/waivers.tsv`), so a waiver only ever suppresses rows
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

The conformance oracle saw this defect through both call paths, and only one of
them shows up as a failing row. `swe_gauquelin_sector` reached it indirectly, as
suite 6 testcase 7's 22 `ERROR` iterations; those are gone from
`Tests/conformance/known-fail.tsv`, which now carries no `ERROR`-category row at
all. The direct path, `swe_house_pos` itself, is suite 6 testcase 6, and that
testcase is classified `Unreproducible` for an unrelated C-versus-C#
representational reason -- see its own remarks in `Suite06Houses.cs`. So testcase 6
never counted this defect and does not now count the fix.

Note the range boundary: `hpos = xp[0] / 10.0 + 1` is usually described as
`[1, 37)`, but six frozen rows are exactly `37.0` -- `HP|G|90|-80|90|{-5,0,5}`
and `HP|G|270|80|270|{-5,0,5}`, all circumpolar cases where `xp[0]` lands on
exactly 0 so `360 - 0 = 360`. Upstream C returns `37.0` for all six as well, so
the closed interval `[1, 37]` is the correct contract. Do not "tighten" any
assertion to the half-open form; it would fail on real upstream behaviour.

## swe_houses/swe_houses_armc/swe_house_pos/swe_house_name: hsys narrowed to char (caused conformance suite 6.6 to be misclassified)

**Status: closed.** `int hsys` overloads added on all five ported entry points, matching
`swephexp.h`, with the `char` ones kept as delegates.

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

**Status: record.** Not a defect: a libm property of the two platforms. Re-measured
against the current matrix below. `Tools/BaselineGen/README.md` is the source of record.

Full numbers, the tolerance-level cost table, and the reasoning for locking the
gate to Windows instead of loosening the shipped tolerance, are in
`Tools/BaselineGen/README.md` under "Platform lock". That file is the source of
record for this measurement; the summary below is a copy of it and goes stale the
moment the matrix grows, which has happened twice already.

Re-measured against the current matrix: **3,547,935 numeric fields compared,
66,390 differing (1.8712%), 5,394 still beyond the shipped `1e-12`/`1e-13`
tolerance** after the angle-wraparound allowance. Windows side: the committed
baseline. Linux side: .NET 10.0.10 on Ubuntu 24.04.4, `mcr.microsoft.com/dotnet/
sdk:10.0` at digest `sha256:ed034a8b`, run against a pristine clone of the same
commit.

Do not quote the two earlier triples. 3,443,058 / 47,052 / 3,346 was measured
against a smaller matrix and understates the limit in both directions;
3,547,367 / 66,342 / 5,394 was the intermediate one. Both are superseded by the
figures above.

Where the 5,394 actually sit, which is more useful than the total: `calc`
contributes 3,442, `orbit` 489, `nodaps` 347, `calc-defaulteph` 597, `pheno` 192,
`eclipse` 89. Five areas are bit-identical across platforms outright -- `format`,
`misc`, `pheno-ast`, `risetrans` and `atmo` -- because they are formatting, date
arithmetic and table lookups with no transcendental in the path.

The house areas are worth separating out, because their beyond-tolerance content
is not scattered noise. All 210 beyond-tolerance fields in `houses-armc` belong to
exactly two house systems: 209 to `'I'` and 1 to `'Y'`, the two entries above.
Nothing else in that 2.66-million-field area exceeds tolerance at all. `houses`
differs in 2.71% of its fields and yet zero of its rows fail, which is the
tolerance doing what it was sized for against real libm divergence.

An earlier pass at this classification reported 2,637 fields as "wraparound" --
that number came from a comparison bug (`min(d, |360-d|)` computed on the raw
difference without first checking it was actually a large, near-360 difference,
so it just returned small differences unchanged and mislabeled them). The
corrected number was 108 at the matrix size that measurement covered, confirmed by
checking that the wraparound fix resolved exactly that many fields and rows, and
that none of the then-remaining beyond-tolerance fields had a raw difference
anywhere near 360. The wraparound split has not been re-derived at the current
matrix size; only the three totals above have.

## swi_strnlen outlived its deletion in swephlib.c, deliberately, until this slice

**Status: closed.** Removed from `CPort/SwephLib.cs`; it appears nowhere under
`SwissEphNet/CPort/` now.

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

**Status: won't fix.** The divergence needs `|J - J1900| > 5.92e6` days, beyond what any
ephemeris file addresses. Forcing 32-bit truncation would match one platform's C and break
the other two.

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

**Status: closed.** Both defects fixed, 43 conformance rows now pass, and the `nodaps`
baseline movement landed with a deviation note.

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

**Status: closed.** Swept across every site listed below, including all thirteen
Moshier-fallback ones.

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

**Status: record.** A correction to an append-only log, which is why it lives here rather
than as an edit to that log.

`Tests/conformance/regenerations.log` attributes the nine `7.2.x` rows to a stale
`swed.oec`/`swed.nut` read by `swe_nod_aps`'s mean-node path. That was wrong: both were
measured identical at the point of use in the working and failing cases. The correct
diagnosis is the `free_planets` object-replacement entry above. The log is append-only, so
the correction is recorded here rather than by editing it.

## SE_VERSION: was deferred until the port reached 2.10.03; closed

**Status: closed.** `swe_version()` reports `"2.10.03"`, pinned by `SwissEphTest.cs:34`.

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

**Status: closed.** The four functions this entry was waiting on -- `swe_calc_pctr`,
`swe_get_current_file_data`, `swe_houses_ex2` and `swe_houses_armc_ex2` -- are all
implemented as full transliterations.

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

**Status: closed.** All ten copy sites are now individually accounted for (`Sweph.cs:3426`,
`:3540`, `:3583`, `:3883`, `:3920`, `:6098`, `:6785`; `SweHouse.cs:301`, `:429`, `:538`). One
was a demonstrated bug and is now fixed; a second got the same defensive re-read during this
audit but, checked carefully, has no demonstrated observable effect (see below -- an earlier
version of this entry overclaimed it); the remaining eight were audited and confirmed harmless
(no mutation of `swed.sidd` between copy and last read of `sip`), not just left alone.

The C writes `struct sid_data *sip = &swed.sidd;` and reads through the pointer, so it sees
any later mutation of `swed.sidd`. `sid_data` is a **struct** in this port
(`CPort/Sweph.h.cs`), so `sid_data sip = swed.sidd;` takes a snapshot instead. `swed.sidd` is
only ever mutated in two places: `Sweph.cs:1505` (`swe_close`'s full reset to a fresh
`sid_data()`, never called mid-computation by anything this audit covers) and `Sweph.cs:3483`
(`swe_set_sid_mode`'s own write-back). So the only way a `sip` snapshot can go stale is if the
function holding it calls `swe_set_sid_mode` (or something that does) between the copy and a
later read of `sip` -- and the only thing that ever does that internally is the
`SE_SIDM_FAGAN_BRADLEY` fallback (`if ((iflag & SEFLG_SIDEREAL) && !swed.ayana_is_set)
swe_set_sid_mode(...)`), which appears at four call sites (`Sweph.cs:762`, `:3627`,
`:7614`, `:9002`) plus one C# counterpart the C doesn't share
(`SweHouse.cs:310`, inside `swe_houses_ex2`).

**One site was not harmless: `swi_get_ayanamsa_ex` (`Sweph.cs:3583`, fallback at `:3627`) --
fixed previously.** With no prior `swe_set_sid_mode` call, it read pre-fallback state and
returned 92.525 where the C returns 24.754, 67.8 degrees out. Re-reads `swed.sidd` at `:3634`
after the fallback.

**A second site, `swe_houses_ex2` (`SweHouse.cs:301`, fallback at `:310`), got the identical
re-read fix during this audit, but -- checked carefully, and corrected here after an initial
overclaim -- it has no demonstrated observable effect for any currently reachable input,
unlike the site above.** `sip` is copied at line 301, and the fallback at line 310 always
installs `SE_SIDM_FAGAN_BRADLEY`, whose value is `0`
(`SwissEph.swephexp.h.cs:262`). The only read of `sip` inside `swe_houses_ex2`'s own body
afterward is `sip.sid_mode`, at lines `:366`/`:368`, choosing between
`sidereal_houses_ecl_t0`/`sidereal_houses_ssypl`/`sidereal_houses_trad`. The `!ayana_is_set`
guard that reaches this fallback at all is only ever true while `swed.sidd` still holds its
all-zero default (the only two places `swed.sidd` is ever mutated are `swe_close`'s reset to a
fresh, zero `sid_data()` and `swe_set_sid_mode`'s own write-back, and the latter always sets
`ayana_is_set = true` in the same call -- so "`ayana_is_set` false" and "`swed.sidd` all zero"
are the same condition). `sid_mode` `0` therefore already reads as `0` *before* the fallback,
and `swe_set_sid_mode(0, 0, 0)` sets `sid_mode` back to plain `0` too -- mode `0` matches
none of `swe_set_sid_mode`'s special-case checks (`Sweph.cs:3436-3461`) that would OR in
`SE_SIDBIT_ECL_T0`/`SE_SIDBIT_SSY_PLANE`. So `sip.sid_mode` is `0` on both sides of the
fallback, and the branch chosen at `:366`/`:368` (the `else`, `sidereal_houses_trad`) is
identical whether or not the re-read happens. `sip.t0`/`sip.ayan_t0` are never read directly
in `swe_houses_ex2`'s own body at all: `sidereal_houses_ecl_t0`/`sidereal_houses_ssypl` each
take their own fresh `sip = swed.sidd;` copy at their own entry, always after this fallback has
already run (see the harmless list below); `sidereal_houses_trad` does not read `sip` at
all -- it delegates ayanamsa entirely to `swe_get_ayanamsa_ex`
(`SweHouse.cs:655`) -> `swi_get_ayanamsa_ex`, which takes its own fresh, already-fixed copy at
its own call time, likewise always after the fallback. So every path this dispatch can reach
ends up reading post-fallback state regardless of this specific fix. The change is kept
anyway -- it costs nothing, matches the C's pointer semantics
(`swehouse.c:221`, `struct sid_data *sip = &swed.sidd;`) the same way the `swi_get_ayanamsa_ex`
fix does, and is defensive against a future change (a different fallback mode, or a future
edit that adds a direct `t0`/`ayan_t0` read to `swe_houses_ex2` itself) -- but it should be
described as a fidelity fix following an established precedent, not as a demonstrated bug
fix the way the entry above is. `scripts/verify-baseline.ps1` staying 100% EXACT on both TFMs
after this change is expected either way, for the reason just given, not evidence either for
or against an observable effect.

**The remaining eight copy sites are confirmed harmless, not merely unaudited:**

- `swe_set_sid_mode` itself (`Sweph.cs:3426`) -- deliberately copies, mutates, and writes
  back in a `finally` block (`:3483`); this is the mutator, not a stale-read site.
- `get_aya_correction` (`Sweph.cs:3540`) -- calls `swi_precess`/`swi_epsiln`/`swi_coortrf`/
  `swi_cartpol` only; none mutate `swed.sidd`.
- `swi_trop_ra2sid_lon` (`Sweph.cs:3883`) and `swi_trop_ra2sid_lon_sosy` (`Sweph.cs:3920`) --
  both call `get_aya_correction` (confirmed harmless above) and otherwise only coordinate-
  transform helpers; no path to `swe_set_sid_mode`.
- `lunar_osc_elem` (`Sweph.cs:6098` declares `sip`, the copy from `swed.sidd` is at `:6102`
  inside the `SID_TNODE_FROM_ECL_T0` guard) and `swi_plan_for_osc_elem` (`Sweph.cs:6785`
  declares `sip`, copy at `:6828`, same guard) -- neither calls `swe_set_sid_mode` or
  `swi_get_ayanamsa_ex` anywhere in their bodies (confirmed by grep: the only four
  `swe_set_sid_mode` call sites in `Sweph.cs` all fall outside both functions' line ranges).
- `sidereal_houses_ecl_t0` (`SweHouse.cs:429`) and `sidereal_houses_ssypl` (`SweHouse.cs:538`)
  -- both copy `sip` at their own entry, and both are called exclusively from
  `swe_houses_ex2` (confirmed by grep: no other call sites exist) at a point after that
  function's own fallback has already run -- so their copies always see the post-fallback
  state already.

The general fix -- making `sid_data` a class so the assignment aliases as the C's pointer
does, covering every present and future copy site at once -- remains undone. The cost is that
`swe_set_sid_mode`'s copy-mutate-write-back would need revisiting, since with a class its
intermediate writes would become visible to anything reading `swed.sidd` concurrently. Worth
doing as its own change with its own measurement, not folded into this audit: every currently
existing copy site is now individually confirmed correct (one demonstrated bug fixed, one
defensive re-read with no demonstrated effect, eight harmless), so the
class change would be a defensive/structural improvement against *future* copy sites, not a
fix for a live bug.

## swe_calc's serr for ipl 13: a range gate the C does not have there

**Status: closed.** The eight lines with no counterpart in either C version were deleted;
the 56 affected `calc` rows regenerated as deviation 15.

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

**Status: closed.** `OnLoadFile` and `LoadFileEventArgs` are gone, `swi_fopen` is a
faithful transliteration again, and the baseline is byte-identical across all 19 areas
either side of the change.

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
`SwissEphNet/SwissEphNet.csproj:15-16` records that `net40`/`netstandard1.0`, the targets `OnLoadFile` originally
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
cut-list shape (`sweodef.h:307`/`:313`: `";:"` on Unix, `";"` on MSDOS/Windows) so `swi_cutstr` can
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
bit-exact oracle (`Tools/OracleGrid`, `Tools/OracleDump`, `Tools/OracleVerify`,
`scripts/verify-oracle.ps1` -- not `Tests/SwissEphNet.Conformance.Tests`, which is the
correctness oracle, a different instrument) stays at 15,820 + 2,244 rows, all bit-identical
against MSVC-built 2.10.03 C, both `known-diff.tsv` lists
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

**Status: closed.** All ten sites fixed in commit `44d434c`; no concatenating `argv[i] +
N` site remains in `Programs/SweTest/Program.cs`.

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

**Closed, all ten sites.** Commit `44d434c` fixed the eight pointer-arithmetic sites (`-ay`,
`-sidt0`, `-sidsp`, `-sid`, `-j`, `-helflag`, `-amod`, `-tidacc`, citing the 2.08 `swetest.c` line
each one corresponds to) and the two unrelated crashes recorded above (`-house`, `-utc`) in the
same change. The 150-row `Tests/swetest/known-diff.tsv` grid moved from 70 to 80 identical rows,
ten CRASH cases becoming byte-identical against the C reference. The four commented-out sites at
834, 847, 940 and 949 are unaffected, since there is no live code there to fix.

## The file-backed grid's divergence is Earth's position

**Status: closed.** The defect was in `rot_back`, not `main_planet`. Both oracle grids are
SHA-256 identical to the C reference on the current tree.

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
zero (commit `b75bddd`, part of the `sweph.c` file-layer slice). `main_planet` reads Earth's
position via `rot_back` on the way out, which is why the divergence looked like it belonged to
`main_planet` from this grid's evidence alone -- the wrong function was simply downstream of the
right one. Every `SEFLG_SWIEPH` position was affected, not only Earth's, since `rot_back` is on
the return path for every body; see "Every `SEFLG_SWIEPH` position changes" in `README.md`'s
breaking-changes list. The file-backed grid moved from 791 of 2,024 bit-identical rows to 1,975,
as `grid-files.tsv` stood at the time -- before the crossing functions added 220 more rows to it;
see `README.md`'s "Bit-exact oracle" section for the grid's current, marked total. No closure note
was added here when the fix landed; this is that note.

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

**Status: open, narrowed.** House system `'J'`'s cusp computation and `swe_house_pos`
**have external validation** -- against Astrodienst's own libswe rather than the analytic grid,
see the correction later in this entry. **The speed-derivative gap this entry used to describe is
also closed**, found stale while working on it: `HOUSES_EX2`/`HOUSES_ARMC_EX2` rows, real
`cusp_speed`/`ascmc_speed` arrays and all, are already in the committed grid and already pass
bit-exact -- re-verified directly below. What remains genuinely uncovered is `swe_house_pos`
itself, `'Z'`/`'0'`, and `'J'`'s cusp computation on *this* instrument specifically (covered on a
different one, see below).

The bit-exact comparison reports 22,289 of 22,289 analytic-grid rows (and 3,280 of 3,280
files-grid rows) matching MSVC-built 2.10.03 C -- re-run directly (`scripts/run-oracle-dump.ps1`
then `scripts/verify-oracle.ps1`) rather than assumed from a prior number, since this entry had
already gone stale once before (see the correction below); both grids are SHA-256 identical
between the C and .NET dumps, not merely row-comparator-clean. This records what that result
does and does not establish, to stop it being cited for things it never touched.

Both replay drivers (`Tools/OracleDump/Program.cs`, `Tools/CReference/sedump.c`) call
`swe_houses` and `swe_houses_armc` in their **six-argument** form, plus `swe_houses_ex`/
`swe_houses_armc_ex2`-family calls and `swe_house_name`. That determines the coverage.

**Genuinely covered.** Twenty-five house letters crossed with latitudes to 89 degrees and
obliquities including 0. The `eps = 0` column is what earns the grid its keep: `tand(0)` is
zero, the pole-height iteration cannot converge, and 2.10.03's `niter_max` cap fires. So
`niter_max`, the Porphyry fallback, the Alcabitius clamp and the non-speed `CalcH`
restructuring are all verified against Astrodienst's own C. `swe_houses_ex`'s own iflag handling
(`SEFLG_SIDEREAL` -- including the ayanamsa applied through it, on `grid-files.tsv`'s rows via a
real file-backed `swe_calc` -- and `SEFLG_RADIANS`) is covered too, over the same 25 letters
(narrower geolat/geolon/date spread than plain `swe_houses`' own sweep, since iflag is a new
dimension on top of what that sweep already proves). `swe_house_name`'s own switch is covered in
full -- every case label it has, INCLUDING `'J'` (see below) and one letter (`'P'`) that
deliberately is not a case label, to exercise its default/Placidus branch.

**Also genuinely covered, found stale while investigating this entry for other work:** `swe_houses_ex2`
and `swe_houses_armc_ex2` are called directly, with real (non-null) `cusp_speed`/`ascmc_speed`
arrays -- `Tools/CReference/sedump.c`'s `process_houses_ex2`/`process_houses_armc_ex2`
(dispatched for every `HOUSES_EX2`/`HOUSES_ARMC_EX2` row) and `Tools/OracleDump/Program.cs`'s own
matching handlers both pass real arrays, not `NULL`. `Tools/OracleGrid/grid-analytic.tsv` already
carries 1,500 `HOUSES_EX2` rows and 3,000 `HOUSES_ARMC_EX2` rows, over the same 25-letter
`$HouseLetters` list every other house sweep in that script uses -- confirmed directly
(`grep -c` against the committed grid file), so every non-`'J'` `do_interpol` letter (`L Q S X M
F B Y`, plus `I`/`i`) is included, not just the two the correctness oracle's suite 6 covers. A
fresh `scripts/run-oracle-dump.ps1` + `scripts/verify-oracle.ps1` run (this session, against the
current tree) reports all of it bit-identical: 0 regressions, 0 differing rows, file-level SHA-256
match on both grids. `swe_houses_ex2` and `swe_houses_armc_ex2` also carry a **real** `serr` (not
hardcoded `NULL` the way `swe_houses`/`swe_houses_armc`/`swe_houses_ex` are), and that is
exercised and compared too, not just the numeric fields -- `sedump.c`'s own comment on
`process_houses_armc_ex2` cites the two branches that write it (`swehouse.c:667`/CalcH failure,
and the hsys `'I'` invalid-declination message).

This entry previously said all of the above was "not covered by the grid at all... because none
of the four entry points above has such a parameter or reaches it." That was true when written
and is no longer true; something else evidently added the `HOUSES_EX2`/`HOUSES_ARMC_EX2` coverage
without this entry being updated to match, the same class of drift already found and corrected in
several other entries in this file. Recorded here rather than silently deleted, since "this
entry was already wrong once" is itself useful context for whoever next revisits it.

**Still not covered by the grid at all**, because no entry point sedump.c/OracleDump.Program.cs
calls has such a parameter or reaches it:

- `swe_house_pos`
- house systems `'Z'` and `'0'`
- house system `'J'`'s cusp **computation** specifically -- see below; its *name* is covered, and
  its cusp geometry now has external validation too, but via a different instrument (the
  pyswisseph replay below), not this grid

**House system `'J'` (Savard-A) has no external validation of its cusp computation, though its
*name* now does.** `swe_house_name('J')` is bit-exact verified: both `sedump.c` and the port agree
on the string `"Savard-A"` for it, since `swe_house_name` is a pure lookup and both sides implement
the identical 25-case switch (`swehouse.c:827`, `SwissEphNet/CPort/SweHouse.cs:990`) -- see
`gen-grid-analytic.ps1`'s `$HouseNameLetters`. That is not the same claim as validating the house
*system*: `'J'`'s actual cusp geometry is still excluded, deliberately, from every hsys sweep that
computes cusps at all (`swe_houses`/`swe_houses_armc`'s own `$HouseLetters`, and `swe_houses_ex`'s
sweep reuses that same list, with the same comment explaining why), and `setest/t.exp` never uses
it in any suite 6 testcase -- checked by enumerating every `ihsy` in the corpus. Its geometry was
transliterated from `swehouse.c:1176-1251` and `:2472-2535` and read back against the C line by
line, which is the only evidence there is for the computation itself. The 918 `HP|J`
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

**`'J'` is no longer unvalidated, and the route was not the one this entry proposed.** The
paragraph above offers two ways to close the gap: add `'J'` to the analytic grid, which needs
the C reference to compute it, or accept transliteration review as the standard of proof. There
was a third. `scripts/validate-seeded-areas.py` replays a baseline area against pyswisseph, which
bundles Astrodienst's own 2.10.03 libswe, and a `houses-armc` replayer has now been added to it
(`replay_houses_armc`, registered in `AREA_REPLAYERS`). That reaches `'J'`'s cusp geometry
directly, with no MSVC C build involved:

```
python scripts/validate-seeded-areas.py --area houses-armc
=== houses-armc ===
Rows: 55512  Replayable: 48240 (86.9%)  Skipped: 7272
Agree: 48240 (100.00%)  Disagree: 0 (0.00%)
```

All 1,944 `H|J|*` rows are inside that 48,240 and all of them agree, so `'J'`'s cusps are now
externally validated and correct, not merely reviewed. The same applies to the 918 `HP|J` rows
this entry calls oracle-less: `--area house-pos` replays 31,528 rows with 31,526 agreeing, every
`HP|J` row among them (the 2 exceptions are `HN|0` and `HN|Z`, a binding artefact -- see "The
pyswisseph replay's swe_house_name limitation" below).

So `swe_house_pos`, and house systems `'Z'` and `'0'`, come off the "not covered at all" list
too; they are covered by the replay, just not by the analytic grid. The distinction worth keeping
is what kind of oracle each is. The analytic grid is bit-exact against a locally built MSVC C;
the replay is a 1e-6 numeric comparison against a prebuilt libswe. The replay is the weaker
statement and the wider net, and for `'J'` it is the difference between no external evidence and
48,240 rows of it.

What the replay does **not** reach, so this entry stays open: the speed derivatives. pyswisseph's
`houses_armc` returns cusps and `ascmc` only, so `AscDash`, the nine speed fields, and
`swe_houses_ex2`/`swe_houses_armc_ex2`'s `cusp_speed`/`ascmc_speed` are still verified for
Placidus and Koch alone, through conformance suite 6 testcases 8 and 9, exactly as described
above. The `do_interpol` path reached by `L Q S X M F B Y I` still has no coverage in any oracle.
That is now the whole of this entry's remaining scope.

## The pyswisseph replay's swe_house_name limitation: 2 expected disagreements

**Status: record.** Expected replay noise, not a defect. The port matches the C; the Python
binding is the outlier.

`python scripts/validate-seeded-areas.py --area house-pos` reports 2 disagreements out of 31,528
rows, and both are `swe_house_name`:

```
HN|0: exp='Placidus' got=''
HN|Z: exp='Placidus' got=''
```

The port is right. `swehouse.c`'s `swe_house_name` ends its 25-case switch with
`default: return "Placidus";`, so `'0'` and `'Z'` -- neither of which is a case label -- resolve
to `"Placidus"` in the C, which is exactly what the port returns and what the baseline froze.
pyswisseph returns an empty string for both instead, confirmed directly: `swe.house_name(b'Z')`
and `swe.house_name(b'0')` give `''` while `b'P'`, `b'J'`, `b'i'` and `b'Y'` give `'Placidus'`,
`'Savard-A'`, `'Sunshine/alt.'` and `'APC houses'`. The binding is not reproducing the C's
default branch.

Recorded so the next person to run the replay reads 2 disagreements as the known binding gap
rather than as a regression, and so nobody "fixes" the port to return an empty string and
thereby diverges from `swehouse.c`.

## Three numbers in baseline-2.8.0.2.env.txt's local-regenerations log are wrong

**Status: record.** Corrections to an append-only log, recorded here for the same reason
as the 7.2.x entry above.

The log is append-only, so these are corrected here rather than by editing the entries.

**Deviation 18** says "737 rows in the pheno area across its six case-id prefixes." The scope
check two lines below it, in the same entry, already gives the correct figure: "pheno: 736
changed." 736 is right -- exactly half of the area's 1,472 rows, which is also what the landing
commit's own message says. 737 is a transcription slip in the prose sentence, not a second
measurement.

**Deviation 17** says "919 rows in house-pos: 918 HP\|J cusp values and the single HN\|J name
row." 918 + 1 is 919, but the entry's own scope check reports "house-pos: 918 changed" -- 918
total, not 919. Diffing the area directly (commit `dcdf293`, deviation 16's landing commit, against
`7b6e1ca`, deviation 17's) confirms the scope check: 917 `HP\|J` rows changed plus the 1 `HN\|J`
row, 918 in total. "918 HP\|J" in the prose should read "917 HP\|J."

**Deviation 16** lists three mechanisms for why house cusps move -- `niter_max`'s Placidus/Gauquelin
fallback, the Alcabitius clamp, and house system `'J'` becoming real -- without saying that the
second of the three moved nothing. Checked directly: every hsys `'B'` (Alcabitius) row is
byte-identical between commit `ea07643` (deviation 16's parent) and the current baseline, in
every area that carries house-system-keyed rows -- 0 of 1,944 in `houses-armc`, 0 of 480 in
`houses` (60 `HS\|B\|*` + 420 `HX\|B\|*`), 0 of 1,125 in `house-pos`. The clamp was ported
faithfully (`swehouse.c:1602-1606`, `if (r > 1) r = 1; if (r < -1) r = -1;` before `acosd`) and
changed no observable output in this baseline: every `r` the matrix's inputs produced already sat
inside `[-1, 1]`. The other two mechanisms account for all of deviation 16's actual movement.

## What the local-mode baseline regenerations have no independent check on

**Status: open, narrowed.** A verification gap, not a defect. `swe_refrac_extended` and
`calc_dip` are **no longer unverified**: the pyswisseph replay does reach them, and all 1,676
`atmo` rows agree -- see the correction at the end of this entry. What remains genuinely
uncheckable in this repository is `swe_rise_trans`'s `!do_fixstar` gate.

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

**Measured, and the `swe_refrac_extended` bullet is answered.** "Nothing in this repository able
to contradict them" was true of `setest/t.exp`, which is what that bullet actually checked, and
false of the repository as a whole. `scripts/validate-seeded-areas.py` already carried a
`replay_atmo` that calls pyswisseph's `refrac_extended` on every `REFX` and `LAPSEDIRECT` row --
so the contradiction mechanism existed and had not been run:

```
python scripts/validate-seeded-areas.py --area atmo
=== atmo ===
Rows: 1676  Replayable: 1676 (100.0%)  Skipped: 0
Agree: 1676 (100.00%)  Disagree: 0 (0.00%)
```

1,676 of 1,676 agree with pyswisseph 2.10.03 at a 1e-6 tolerance, zero disagreements, zero
skips. That covers the 393 `REFX` rows deviation 19 moved. Both halves of the deviation are
therefore corroborated against Astrodienst's own compiled library: the `if (inalt >= dip)`
predicate flip and the `273.16` to `273.15` constant. "The largest wholly unverified behavior
change in the 2.10.03 work so far" is no longer an accurate description of it.

The distinction the original bullet was reaching for still stands and is worth keeping: no
*conformance* testcase exercises these functions, so `t.exp` cannot catch a regression in them.
The replay is not part of any gate either -- it is a script somebody has to run. What changed is
that the evidence now exists and has been taken, not that it is now automatic.

`swe_rise_trans`'s `!do_fixstar` gate is genuinely not reachable this way and keeps this entry
open: the replay compares baseline rows, and the baseline has no row that both names a star and
produces a computed result, so there is nothing for a replay to compare against either.

## eclipse_how's 100-to-1 change: the counter-example worth reading carefully

**Status: record.** A lesson about reading row counts as evidence, kept because the
mistake it describes is easy to repeat.

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
`Tests/conformance/known-fail.tsv` at the deviation 19 landing commit (`ec7cb75`): three
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

## Three file-layer divergences: two closed, one deliberate and now documented

**Status: closed / won't fix.** `PATH_SEPARATOR` and the `AS_MAXCH` check are closed, fixed
with the `swi_fopen` transliteration. `DIR_GLUE` is `won't fix`: keeping `/` on every platform
is a deliberate, permanent divergence from the C's per-platform literal, not deferred work.
The one action it still owed -- documenting the Windows-only diagnostic-text difference for
consumers -- has landed in `README.md`'s `# Breaking changes` / `## V:2.10.3` list, in the
bullet directly after `OnLoadFile`'s, so nothing here is outstanding.

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
`SwissEph.sweodef.h.cs:192` sets `DIR_GLUE = '/'` unconditionally, for the reasons already recorded
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
`README.md`'s `# Breaking changes` / `## V:2.10.3` section, which is where that deferred work
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

## swetest.c's missing `spmoon` declaration and unguarded `gethostname`: fixed in v2.10.3bfinal; a third defect surfaced by the same fix

**Status: open upstream.** Two of the three are fixed in `v2.10.3bfinal`, the tag this
port pins. The third, `do_printf`'s undeclared `fp`, is still present upstream and is
patched narrowly by `Tools/CReference/build-c.ps1`.

Resolved. `swetest.c` used `spmoon` at `:1139`, `:1140` and `:1621` (reading `-xv`, then `atoi`-ing
it for the `v` planetary-moon selector) without declaring it, and called `gethostname()`
unconditionally at `:1282` with the variable it writes into declared only under `#if HPUNIX`
(`:826-828`) -- both hard compile errors in `v2.10.3final`, the tag this port previously pinned.
`Tools/CReference/build-c.ps1` used to patch a copy of `swetest.c` at build time to work around
both; `Programs/SweTest/Program.cs:770` carries the equivalent `spmoon` default for the port.

Astrodienst fixed both upstream, released in `v2.10.3bfinal` (`f4dcd18e`), the tag this port now
pins: `static char spmoon[AS_MAXCH] = "9501";  // Jupiter Moon Io` (matching the value this fork's
own patch already used -- an earlier version of that patch used `"9001"`, which is not a moon of
anything; the planetary-moon numbering is `SE_PLMOON_OFFSET`, 9000, plus the host planet's number
times 100, so 95xx is a Jupiter moon and Io is the first one, `9501`, not `9001`) and
`#ifndef _WINDOWS` around the `gethostname` block, with `sweodef.h` now defining `_WINDOWS` under
`#ifdef _WIN32` to match. `build-c.ps1`'s two patches for these are removed, not adapted -- both
of its own assertions ("spmoon is already declared", "the gethostname call is already inside a
preprocessor conditional") fired against `f4dcd18e` before removal, exactly as designed.

The same `sweodef.h` change surfaces a third defect, still open as of `f4dcd18e`: `do_printf`
(`swetest.c:3956-3963`, called by every line `swetest` prints) reads `fprintf(fp, info)` under
`#ifdef _WINDOWS`, and `fp` is declared nowhere in `swetest.c`. Dead code under `v2.10.3final`
(`_WINDOWS` was never defined there, so the branch never compiled); a hard `C2065` on any MSVC
build now that it activates on every Windows build (`_WIN32` is always defined by MSVC). `master`
and `v2.10.3bfinal` are the same commit, so there is no newer upstream fix to pull.
`build-c.ps1` patches this one narrowly (`fputs(info, stdout)` substituted for both branches of
the `#ifdef`, matching the `#else` branch's existing behaviour exactly), and the defect was reported
to Astrodienst. The report itself is drafted outside the tracked tree and sent by hand, so there is
no in-repo path to follow here.

## swe_set_jpl_file: the C's AS_MAXCH clamps are not reproduced, and the comments were 2.08's

**Status: won't fix.** Both clamps exist to keep a `strcpy` inside a fixed C buffer, and
`swed.jplfnam` is a C# `string`. The residual difference cannot reach a computed number.

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

**Status: closed.** The port matches `dms()`'s format exactly and guards the
sign-insertion site. Astrodienst reviewed the report and declined it, correctly.

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

**Status: won't fix.** An upstream `libswe` defect that the port reproduces faithfully,
confirmed by measurement on both sides. Guarding it is a design decision separate from
porting 2.10.03.

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

**The port shares the hazard. Confirmed, no longer inferred.** `SwissEphNet/CPort/Sweph.cs`'s
`swe_solcross` (citing `sweph.c:8310-8343`) is a line-by-line transliteration: it hardcodes `int ipl
= SwissEph.SE_SUN;` at `Sweph.cs:9861` (and `swe_solcross_ut` at `:9898`) the same way, and its
refinement loop divides by `x[3]` the same way. This entry previously argued from that shared
source that the port must hang too, while noting no one had actually called it. It has now been
called. Against the net10.0 build of `SwissEphSharp.dll`, with `swe_set_ephe_path` pointed at a
sentinel directory that cannot resolve so `SEFLG_MOSEPH` needs no data files, `jd_et` at 2451545.0
and `SEFLG_MOSEPH | SEFLG_HELCTR`:

| `x2cross` | `swe_solcross` | `swe_solcross_ut` |
|---|---|---|
| 0 | returns `NaN` in about 1 second | returns `NaN` in about 1 second |
| 90 | never returns (killed at 25s) | never returns (killed at 20s) |
| 180 | never returns (killed at 20s) | not run |
| 359.5 | never returns (killed at 20s) | not run |

That reproduces the C's behaviour exactly, including the asymmetry: `x2cross` at exactly 0 exits on
the loop's first pass with the `0/0` `NaN` rather than reaching the `+Infinity` division, and every
other value tried spins. The structural argument was right, and it is now a measured result rather
than a prediction. The runs were one-off probes against the shipped assembly, deliberately not
added to any test project -- a test that hangs on failure is worse than no test, and pinning this
behaviour would pin a defect this port hopes upstream will fix.

**Do not fix the port.** `SwissEphNet/CPort/Sweph.cs` is a transliteration-frozen path
(`CONTRIBUTING.md`), and even setting that aside, this is a design decision (how the port should
guard against or recover from a hang its own upstream source has) separate from porting 2.10.03,
not a divergence-from-the-C correction the freeze's one exception covers -- the port is faithful to
the C here, which is exactly the problem. Recorded so a future porter (or anyone routing a caller-
supplied `x2cross` into `swe_solcross` with `SEFLG_HELCTR` set) knows this before hitting it in
production rather than during an oracle run.

## insert_gap_string_for_tabs drops swetest.c's LEN_SOUT bound

**Status: closed.** `insert_gap_string_for_tabs` (`Programs/SweTest/Program.cs:3485`) now
reproduces swetest.c:2815-2828's bounded tab-replacement loop --

```c
while((sp = strchr(sout, '\t')) != NULL && strlen(sout) + strlen(gap) < LEN_SOUT) {
    strcpy(s, sp + 1);
    strcpy(sp, gap);
    strcat(sp, s);
}
```

-- with a C# loop that repeatedly replaces the first remaining tab with `gap`, stopping once
`sout.Length + gap.Length` would reach `LEN_SOUT` (1000), the same bound the C checks live via
`strlen(sout) + strlen(gap) < LEN_SOUT`. Previously the method used an unconditional
`sout?.Replace("\t", gap, StringComparison.Ordinal)`, which had no such limit and kept
substituting regardless of the result's length -- this was the real, pre-existing divergence
this entry originally recorded, and the fix is a straightforward transliteration of the C loop
using `string.IndexOf('\t')`/`Substring` in place of `strchr`/`strcpy`/`strcat`, citing
swetest.c:2801-2814 under the transliteration freeze's one permitted exception
(`CONTRIBUTING.md`). `LEN_SOUT` (`Program.cs:753`) is now genuinely read by this method, matching
the C.

This was previously misdocumented rather than left unrecorded: a comment beside `LEN_SOUT`'s own
declaration (`Program.cs:747-751`, before this fix) claimed the port's dynamic strings made the
bound irrelevant -- but the C does read it, live, at exactly this site. That comment is now
corrected to reflect the fix.

## The 5% waiver caps divide by the whole area, not by the relevant sub-scope

**Status: record.** The caps do what their names say. Recorded because the fraction a
reviewer reads is of the area, not of the sub-scope a waiver names.

`Verdict.cs:59-60`'s `MaxWaivedFraction`/`MaxMatchedFraction` (both 5%) are checked against
`WaivedFraction`/`MatchedFraction`, and both of those divide by the area's total row count
(`CompareResult.Total`, see `Comparer.cs`) -- not by the row count of whatever narrower glob a
waiver actually targets. An area with a large, mostly-unrelated sweep can make a real, glob-wide
regression look tiny next to that denominator:

- `houses-armc` is 55,512 rows. Its `HSTATE|**` case ids (the stateful `saved_sundec` pair
  `Houses.AddStatefulPairRows` exists specifically to exercise -- see
  `Tools/BaselineGen/README.md`'s "Matrix coverage" table) number 72, so a waiver covering every
  one of them, regardless of outcome, would sit at 72 / 55,512 = 0.13% of the area -- nowhere near
  either 5% cap.
- The same area's `HSUN|**` case ids (the sunshine-state sweep, `Houses.AddSunshineStateRows`)
  number 1,008, i.e. 1.82% of the area.
- `house-pos` is 31,528 rows. Its `HP|G|**` case ids (`swe_house_pos` under hsys `'G'`, Gauquelin
  sectors -- see the hcusp[37] fix a few sections up) number 1,125 (375 case ids at each of the
  three `eps` values the sweep now covers, `Grids.Eps` = `{0, 23.4392911, 40}`), i.e. 3.57% of the
  area.

All three could be waived in full -- every row, regardless of whether it fails -- and still clear
both caps with room to spare, because the cap is measured against 55,512 or 31,528, not against 72,
1,008, or 1,125. This is not a bug in the arithmetic (the caps do exactly what their names say:
bound a waiver's share of the *area*), and it is not being fixed here -- recorded because a reviewer
skimming "5.0% cap, waiver passed at 1.8%" could reasonably assume the waiver's actual target is
narrow, when the fraction that matters (of the sub-scope the waiver names) could be 100%.

## DivergenceReport's field-compared count includes non-numeric fields

**Status: closed.** `DivergenceStats` now carries a second, correctly-labeled counter,
`NumericFieldsCompared`, alongside the original `FieldsCompared`. `DivergenceReport.Collect`
(`Tools/BaselineVerify/DivergenceReport.cs`) parses every field's numeric-ness unconditionally
(not only for fields that differ, so equal-and-numeric fields are counted too) and increments
`NumericFieldsCompared` only for fields that actually parse as finite, non-NaN doubles on both
sides -- excluding `serr` diagnostic strings and planet names the way `FieldsCompared` never
did. `Tools/BaselineVerify/Program.cs`'s `--report-only` output gained a `NUMERIC` column and
now prints "N fields compared (M of them numeric)" instead of the old, misleading "N numeric
fields compared".

`FieldsCompared` itself, and the DIFFER% figure computed from it, are deliberately unchanged --
`Tools/BaselineGen/README.md`'s "Platform lock" section already cites both the raw count
(3,547,935 for the current matrix) and the percentage derived from it (66,390 / 3,547,935 =
1.8712%), so the fix adds an accurate second number rather than changing the meaning of the
one already cited elsewhere. `FieldsDiffering`/`FieldsBeyondTolerance` were already correct
(both only ever incremented after a successful numeric parse) and are unaffected.

## 31 of 107 public `swe_*` entry points have no matrix coverage

**Status: closed.** The 7 gaps with no stated reason are now covered; the 24 deliberate
exclusions are unchanged.

`SwissEphNet`'s public API surface is 107 `swe_*` methods; cross-referencing that list against
every function name that appears anywhere under `Tools/BaselineMatrix/` found 31 with no
matrix coverage at all -- no area's generator called them, under any name. The fixed-star family
(`swe_fixstar[_ut]`, `_mag`, `swe_fixstar2[_ut]`, `_mag`) is out of reach under this harness's
no-real-files rule (`Tools/BaselineMatrix/Areas.cs`'s `NoEphemerisFilesProvider`) for the same
reason `sefstars.txt`-dependent ayanamsa modes are already noted as frozen-without-the-file
behavior in `Tools/BaselineMatrix/Ayanamsa.cs`'s own doc comment -- a real, load-bearing
exclusion, not an oversight.

**Re-measured directly while closing the 7 below** (`comm -23` between the port's public `swe_*`
names and every name called as `swe.swe_*(` anywhere under `Tools/BaselineMatrix/`): 24 remain
uncovered after this entry's fix, not just the fixed-star family -- also `swe_close`,
`swe_dotnet_version`, `swe_get_library_path`, `swe_heliacal_angle`, `swe_heliacal_pheno_ut`,
`swe_heliacal_ut`, `swe_helio_cross[_ut]`, `swe_mooncross[_ut]`, `swe_mooncross_node[_ut]`,
`swe_set_ephe_path`, `swe_set_jpl_file`, `swe_solcross[_ut]`, `swe_topo_arcus_visionis`, and
`swe_vis_limit_mag`. This entry's own earlier text described the pre-fix 31 as splitting into
"eight, oracle-covered" plus the fixed-star family; that framing is not re-derived or endorsed
here -- this list is what is actually absent from `Tools/BaselineMatrix/` today, confirmed by
direct measurement rather than carried forward from the earlier count. Whether any of these 24
belongs on a future matrix-coverage pass is a separate question this fix does not answer.

**The remaining 7 -- `swe_houses_ex2`, `swe_houses_armc_ex2`, `swe_get_ayanamsa_name`,
`swe_calc_pctr`, `swe_lat_to_lmt`, `swe_lmt_to_lat`, and `swe_get_current_file_data` -- now have
matrix coverage.** `swe_houses_ex2` (`HousesEx.cs`'s `BuildHousesEx2Row`) sweeps the same
tjd/armc-derivation and sidereal-branch grid as the existing `swe_houses_ex` coverage, plus the
`cusp_speed`/`ascmc_speed` out-parameters the `_ex2` form adds -- 11,760 new rows in the `houses`
area. `swe_houses_armc_ex2` (`Houses.cs`'s `AddArmcEx2Rows`) is the armc-based sibling, same
treatment -- 54,432 new rows in `houses-armc`. `swe_get_ayanamsa_name` (`Ayanamsa.cs`'s
`AddAyanamsaNameRows`) sweeps every predefined sid_mode plus the out-of-range and
`SE_SIDBITS`-wraparound edges -- 59 new rows in `ayanamsa`. `swe_calc_pctr` (`Calc.cs`'s
`BuildPctrRow`) sweeps `Grids.CalcPlanets` against three center bodies -- 4,080 new rows in
`calc`. `swe_lat_to_lmt`/`swe_lmt_to_lat` (`DateTime_.cs`'s `AddLatToLmt`/`AddLmtToLat`) sweep the
existing Julian-day grid against a spread of longitudes -- 180 new rows in `datetime`.
`swe_get_current_file_data` (`Misc.cs`) adds one row per `ifno` (including two out-of-range
values) -- 7 new rows in `misc`, all resolving through the "no file open" branch under this
harness's no-files rule, which is itself the behavior worth freezing here. Regenerated via
`regenerate-baseline.ps1 -FromLocal` (deviation log entry 25, `Tests/baseline/baseline-2.8.0.2.env.txt`):
SCOPE-OK, 0 changed/removed anywhere, only the new case ids listed above added; `scripts/verify-baseline.ps1`
is 100% EXACT on both TFMs afterward.

**107 here vs. 108 elsewhere in this repository is not a typo; the two count different
populations, measured and reconciled below.** `Tools/OracleGrid/gen-grid-files.ps1`'s own header
comment (and `docs/compliance-2.10.03.md`'s "The last two 2.10.03-only entry points" section) cite
108 as the count of distinct `swe_*` names declared in the current, 2.10.03
`external/swisseph/swephexp.h` (`grep -oE '\bswe_[A-Za-z0-9_]+\s*\(' external/swisseph/swephexp.h
| sed -E 's/\s*\($//' | sort -u | wc -l`; the 2.08 header, `external/pyswisseph-2.08/swephexp.h`,
gives 96 by the identical measure). The 107 immediately above is a different count entirely: the
port's own public `swe_*` API surface (every `public` method named `swe_*` across
`SwissEphNet/SwissEph*.cs`), which is what `Tools/BaselineMatrix`'s coverage question is actually
asked against -- a matrix gap has to be a method that exists to have no coverage.

The two are close but not the same population, and the 1-off gap is fully accounted for by three
names, not by rounding or an approximation: comparing the two name lists directly
(`comm -23`/`comm -13` between the header's 108 and the port's 107, both sorted) finds `swe_rise_transit`
and `swe_set_timeout` in the 2.10.03 header with no same-named public method anywhere in the port
at all (not merely missing from `Tools/BaselineMatrix` -- absent from the port's public surface
entirely, a porting gap wider than a matrix-coverage one), and `swe_dotnet_version` in the port's
public surface with no counterpart in Astrodienst's header at all (a port-only addition, this
fork's own informational sibling to `swe_version`). 108 header names minus those 2, plus that 1,
is 107 -- the port's own count, exactly. Whether `swe_rise_transit`/`swe_set_timeout` themselves
belong on some future work queue is a separate question this section does not answer; the point
here is only that "107" and "108" are two different, correctly-computed numbers, not one stale and
one current.

## SweJPL rejected a DE file whose constant-name block is not plain ASCII; fixed

**Status: closed.** Both sites read into a `byte[]` and count bytes, as `swejpl.c` does.
Re-measured over the JPL grid at 2,400 of 2,400 rows bit-identical.

Found by the third bit-exact oracle grid (`Tools/OracleGrid/grid-jpl.tsv`), the first
port-versus-C measurement of the `SEFLG_JPLEPH` backend at any level. Full numbers, the data
file's hashes and the environment are in `Tests/oracle/regenerations-jpl.log`; this section is
the defect itself.

`swejpl.c` reads the 400 six-byte JPL constant names as a raw *byte* field and checks the *byte*
count it got back:

```c
/* swejpl.c:210, and again at :682 in state() */
nrd = fread((void *) js->ch_cnam, 1, 6*400, js->jplfptr);
if (nrd != 6*400) return NOT_AVAILABLE;
```

The port reads the same 2400 bytes but decodes them as text and checks the resulting *character*
count against the same 2400 (`SweJPL.cs:245-247` in `fsizer`, `:743-745` in `state`):

```csharp
js.ch_cnam = js.jplfptr.ReadChars(6 * 400) ?? Array.Empty<char>();
nrd = js.ch_cnam.Length;
if (nrd != 6 * 400) return Sweph.NOT_AVAILABLE;
```

`CFile.ReadChars(count)` reads `count` bytes and then decodes them, so its result is `count`
characters only when every byte decodes to exactly one character. Under the port's UTF-8 default
(the deliberate one -- see the data-file encoding note in `CONTRIBUTING.md`) a byte above `0x7F`
either combines with its neighbours or becomes a single replacement character, and the count
comes back short.

That block is not guaranteed to be ASCII. It is 400 fixed six-byte slots and a DE file names far
fewer constants than that; whatever sits in the unused tail is not specified. Measured on the two
files to hand:

| File | Bytes above `0x7F` in the 2400-byte block | Decodes to | Port's guard |
|---|---|---|---|
| NASA JPL DE406 (`lnxm3000p3000.406`) | 176 | 2380 chars | fails |
| Astrodienst `de431.eph` | 0 | 2400 chars | passes |

So `fsizer` returns `NOT_AVAILABLE` for DE406, `open_jpl_file` fails, and `swe_calc` falls back
through `SEFLG_SWIEPH` to Moshier on every row that needs an ephemeris -- silently, because the
fallback is the documented behaviour for a JPL file that genuinely is not there. Measured over the
JPL grid: 1,985 of 2,400 rows differ from the C, 1,860 of them by returning `SEFLG_MOSEPH` where
the C returns `SEFLG_JPLEPH`.

**This is why nothing had caught it.** The defect is data-dependent, and the only DE file this
repo's tooling had ever been pointed at is DE431, which is clean ASCII in that block. The DE431
conformance run recorded in `Tests/conformance/regenerations.log` (2026-07-31, 500 of 538 JPL rows
passing) therefore exercised a working JPL path and proves nothing about DE406, DE405, DE200 or
any other file whose unused constant slots happen to hold a high byte.

One further consequence, and the sharpest evidence that the diagnosis is right: `load_dpsi_deps`
is called from exactly one place in the whole library -- `swe_set_jpl_file`, on the branch where
the file it just opened reports `jpldenum >= 403` (`sweph.c:1503-1504`). Because the port never
gets a successful open, it never reaches that branch. `plaus_iflag` (`sweph.c:6121-6141`) turns
that into a visible, byte-comparable difference in the `serr` column of every `SEFLG_JPLHOR` row:
the C writes `file eop_1962_today.txt not found; default to SEFLG_JPLHOR_APPROX`
(`swed.eop_dpsi_loaded == -1`, only ever written by `load_dpsi_deps`), the port writes `you did not
call swe_set_jpl_file(); default to SEFLG_JPLHOR_APPROX` (`== 0`, the untouched initial value).

**Fixed** in the commit that carries freeze-manifest log entry 14, one commit after the one that
recorded the measurement. `SwissEphNet/CPort/` is a frozen path, and this is the freeze's one
permitted exception: restoring the byte-count semantics makes the port *more* faithful to
`swejpl.c:210`/`:682`. Keeping it out of the measurement commit was deliberate, so that the
before and after numbers came from two separately reviewed states rather than one.

Both sites now read into a `byte[]` and count what `Read(byte[], int, int)` returns, which is the
byte count the C compares. `ch_cnam` stays `char[]` at one char per byte, matching the C's `char`
buffer and what its only reader -- the commented-out diagnostic `printf` at the end of the file --
assumes.

Re-measured over the same grid and the same DE406: 2,400 of 2,400 rows bit-identical, 0 differing,
down from 1,985. `Tests/oracle/known-diff-jpl.tsv` is still empty and
`scripts/verify-oracle.ps1 -Grid Jpl` is now green with nothing waived. The analytic and files
grids are untouched either side of the change, `dump-net.tsv` at `b36a007e...` and
`dump-net-files.tsv` at `f3fa03aa...` both times.

What the fix does *not* close is `load_dpsi_deps`'s parsing loop. The port now reaches the same
early return the C does, and the `serr` text agrees on all 110 `SEFLG_JPLHOR` rows, but neither
side gets past `swi_fopen` because neither `eop_1962_today.txt` nor `eop_finals.txt` is
retrievable -- see the entry above on what the oracle grids do not cover.

## The analytic grid's recorded artefacts depend on SE_EPHE_PATH; the port and the C do not disagree

**Status: closed, with one unexplained residual.** Both drivers now clear `SE_EPHE_PATH`
and the grid is no longer environment-sensitive. The 264-row `CALC`/`CALC_UT` pattern
described near the end of this entry is still unattributed.

Fourth update to this section. The first version claimed the ayanamsa values moved; they never
did. The second claimed a C-versus-port divergence; there is none. Both errors and how they were
reached are recorded further down, because the same mistake produced both. The third version's own
heading, "The residual, which is real and is not closed", was rewritten -- not kept alongside --
once the fix landed; "The residual that was open, and is now closed" below is what replaced it,
not an addition next to it. This, the fourth update, corrects the section immediately below:
"What is true, measured on the current tree" stopped being the current tree once the residual
closed, and had not been re-labeled to say so.

### What was true before the residual below closed

Replaying `grid-analytic.tsv` (22,289 rows) through each driver, in three configurations:

| | C vs port |
|---|---|
| `SE_EPHE_PATH` unset (the CI configuration) | **0 rows differ** |
| `SE_EPHE_PATH` set to a real ephemeris directory | **0 rows differ** |
| explicit `ephe-dir` argument to both drivers | **0 rows differ** |

The port and the C agree bit for bit in every configuration tested, including the ones where real
ephemeris files are found and used.

What does move is *both sides together*. Comparing either driver against itself, environment unset
versus set:

```
621 rows, every one a value change, no err changes
   AYANAMSA 73   AYANAMSA_UT 168   HOUSES_EX 190   HOUSES_EX2 190
```

Identical row sets, identical counts, on both drivers, at the time this was measured: the grid was
environment-sensitive; the port was not divergent. That gap is closed now -- see "The residual that
was open, and is now closed" below -- and re-measured directly on the current tree with the same
comparison this table used (`Tools/OracleDump`, `grid-analytic.tsv`, `SE_EPHE_PATH` unset versus
pointed at `external/swisseph/ephe`, a real populated ephemeris directory): the two dumps are
byte-identical, sha256 `4ac1a3c0…c7640` both times. 0 of 22,289 rows differ. The grid is no longer
environment-sensitive.

### Why the earlier measurement showed 210 rows differing

It did, on the tree at `8814b33`, and that measurement was correct for that tree. It was a
**harness artefact, not a port defect**. Neither driver pinned an ephemeris path for the analytic
grid, so each row started from whatever implicit state its own runtime produced -- the C from its
compiled-in default, the port from its own -- and the two implicit states were not the same. Once
both drivers call `swe_set_ephe_path` explicitly before every row, the disagreement is gone.

The proof that it was never in the port is that the change which closed it touched no port file at
all. `git diff --name-only` across the two commits lists only `Tools/`, `scripts/`, `docs/`,
`.github/` and the regenerated grid and classification artefacts. Nothing under `SwissEphNet/`.
A defect in `SwissEphNet/CPort/` cannot be fixed by editing a driver.

### The residual that was open, and is now closed: both drivers clear their own SE_EPHE_PATH

The 621 rows were still environment-sensitive, and the sentinel path alone could not close that.
`swe_set_ephe_path` gives the environment variable priority over the path it was passed
(`sweph.c:1327`), so an exported `SE_EPHE_PATH` overrode the sentinel on both sides. Worse, that
priority is not specific to the sentinel: it applies exactly as much when a real, explicit
`ephe-dir` is passed, which is grid-files.tsv's normal case, not grid-analytic.tsv's. Measured on
grid-files.tsv (3,280<!--doccount:grid-files-total--> rows, the grid CI actually gates, current
tree, re-measured after the grid grew from the 3,251 rows this fix was first measured against)
with the explicit ephe-dir still passed
and `SE_EPHE_PATH` pointed at an empty directory: **2,246 rows changed, 2,241 of them in value
columns.** That is not a reproducibility footnote, it is the gated grid silently reading from a
contributor's own directory instead of `external/swisseph/ephe` -- and since both `sedump.c` and
`Tools/OracleDump/Program.cs` were equally hijacked, they still agreed with each other, so
`verify-oracle` stayed green while measuring the wrong data.

Both drivers now clear `SE_EPHE_PATH` from their own process before any row runs --
`Tools/CReference/sedump.c`'s `main()` (`_putenv_s`/`unsetenv`, platform-guarded) and
`Tools/OracleDump/Program.cs`'s `Main` (`Environment.SetEnvironmentVariable("SE_EPHE_PATH", null)`)
-- so `getenv("SE_EPHE_PATH")` inside `swe_set_ephe_path` (`sweph.c:1327-1330`, ported faithfully
at `SwissEphNet/CPort/Sweph.cs:1569-1583`) returns nothing regardless of what either process
inherited, and the path argument each driver actually passes is what governs. This is a
driver-level change made from outside `swe_set_ephe_path`, not an edit to it: that function stays
a frozen, faithful transliteration, priority check included.

Measured, both directions:

- **Inert on a clean machine.** With `SE_EPHE_PATH` unset, `dump-c-2.10.03.tsv`, `dump-net.tsv`,
  `dump-c-2.10.03-files.tsv` and `dump-net-files.tsv` are byte-identical before and after this
  change (same SHA-256 for each of the four, both grids).
- **Closes the hijack.** With `SE_EPHE_PATH` pointed at an empty directory and the real
  `ephe-dir` still passed on the command line, the files grid now produces the same bytes
  (matching SHA-256) as the unset case, on both drivers -- the 2,246-row change above is gone.

Two of the four ayanamsa-adjacent funcs the 621-row figure covers could not have been fixed by an
`iflag` alone: `swe_get_ayanamsa(double tjd_et)` and `swe_get_ayanamsa_ut(double tjd_ut)` take no
`iflag` parameter at all (`swephexp.h:758-759`), so there was nothing to OR `SEFLG_MOSEPH` into;
the ephemeris is chosen internally. Clearing the variable in both drivers, rather than trying to
route around it per call, is what actually closes those two.

What this does not close: it clears the variable in each driver's own process only, using
`_putenv_s`/`unsetenv` (C) or `Environment.SetEnvironmentVariable(name, null)` (.NET) -- none of
which touch the parent shell's or CI runner's own environment, only the child process's copy of
it. It also does not reach `swetest.exe`, a separate binary built by `Tools/CReference/build-c.ps1`
and exercised only by `scripts/verify-swetest-diff.ps1`, not by either oracle driver -- a
`SE_EPHE_PATH` set on a machine running the swetest text diff still resolves against that variable
exactly as `sweph.c:1327-1330` always intended.

### DIR_GLUE's consequence under a hijacked configuration, newly measured

The other known, deliberate divergence in this file ("DIR_GLUE fixed: CPort/Sweph.cs:2634 was a
mis-transliteration") is *not* touched by the fix above and must not be. But the hijacked
configuration used to measure the fix also measured DIR_GLUE's consequence for the first time:
under `SE_EPHE_PATH` pointed at an empty directory (files grid, before the env-clearing fix), the
C and the port disagree on **1,754 of 3,280<!--doccount:grid-files-total--> rows -- all 1,754 in the `err` column, zero in any
value column, zero in `retc`** (re-measured directly on the current tree, up from 1,738 of 3,251
at the grid size this was first measured against).

Of those 1,754, character-diffing each pair of `err` strings splits them two ways:

- **1,490 rows are a pure separator swap**, and nothing else: every one of the 1,490 diffs to
  exactly one `difflib` replace operation, the C side's escaped `\\` (one literal backslash, from
  `emit_escaped`/`EscapeErr` doubling it for TSV safety) against the port's `/`, with the rest of
  the string -- including every other separator in the same path -- character-for-character
  identical on both sides. This is the backslash-versus-slash split the "DIR_GLUE fixed" section
  already documents (`external/swisseph/sweodef.h:304` defines `/` under `UNIX_FS`, `:319` defines
  `\\` otherwise; `SwissEphNet/SwissEph.sweodef.h.cs:192` hardcodes `/` for every platform, since
  one assembly ships to Linux and macOS too, where `\\` is not a separator at all). `sweph.c:2400`
  (`swi_fopen`'s "not found in PATH" message) embeds `ephepath` as stored by `swe_set_ephe_path`,
  which appends exactly one trailing `DIR_GLUE` character (`sweph.c:1338-1340`) -- that one
  appended character is the entire diff. Both sides fail to find the file identically; only the
  spelling of the path they looked in differs.
- **264 rows are a different pattern this measurement does not explain** (unchanged at the current
  3,280-row grid, re-measured alongside the 1,754/1,490 figures above): `CALC`/`CALC_UT` rows
  carrying the `TOPOCTR` or `SIDEREAL` iflag combination, where one side's `err` is empty and the
  other's carries the full "not found" message -- e.g. case `CALC|0|2195878|TOPOCTR`: C empty,
  port carries the message; case `CALC|0|2195878|SIDEREAL`: C carries the message, port empty.
  132 `CALC` and 132 `CALC_UT` rows, split the same way. This is not the DIR_GLUE mechanism (no
  separator is involved when one side is simply empty) and is not attributed to anything here --
  flagged as a real, currently-unexplained pattern rather than folded into the DIR_GLUE count it
  was found alongside.

Clearing `SE_EPHE_PATH` in both drivers, as the section above does, keeps both patterns out of the
gated `grid-files.tsv` comparison: with the variable cleared, that grid runs under the real,
existing `-EpheDir` its own invocation passes, not a hijacked or nonexistent one. This is not true
of `grid-analytic.tsv` the same way -- that grid runs under `SENTINEL_EPHE_DIR`, a deliberately
nonexistent path, on all 22,289 rows, by design (see `docs/compliance-2.10.03.md`'s "The sentinel
ephemeris path and the AYANAMSA_EX/AYANAMSA_EX_UT environment leak"), so
"never runs under a nonexistent path" does not hold for it. What keeps these two `serr` patterns out
of the analytic comparison is a different, narrower fact: no analytic row calls `swi_fopen` at all,
because every row forces `SEFLG_MOSEPH` -- confirmed directly, `grep -c "not found in PATH"` finds
0 occurrences in either analytic dump (`SE_EPHE_PATH` unset or pointed at a real ephemeris
directory). It does not and should not paper over that the underlying `serr`
differences are real and would reappear if `grid-files.tsv` were ever pointed at a directory that
does not exist, ephe-dir or (absent the clearing fix) `SE_EPHE_PATH` alike.

### What the two earlier versions got wrong, and how

The first version reported "192 rows change answer". The measurement was `diff` on whole lines; the
count of differing *lines* was written up as a count of differing *values*. Comparing field by
field afterwards: 0 value differences, 192 in the `serr` column alone. It then built a causal story
around the twelve sid modes matching the twelve names in `swi_get_ayanamsa_ex`'s guard
(`sweph.c:3031-3045`) exactly, one for one -- and on the strength of that coincidence rejected the
one-line fix that was in fact correct. `sweph.c:6755-6800` hardcodes those star records, so
`sefstars.txt` was never the dependency.

The second version isolated the columns correctly but attributed the result wrongly, calling a
harness asymmetry a disagreement between the port and the C. The measurement was sound; the
inference from it was not, and it was written as a finding rather than as a hypothesis.

The lesson both share: a number that lines up is a lead to test, not a mechanism found, and the
attribution of a difference needs its own evidence separate from the difference itself.

## swetest -D<n>: xobl[0] aliasing, deliberately not reproduced

**Status: won't fix.** Reproducing it would mean engineering C BSS layout aliasing into
managed arrays to copy a bug the C does not intend. A known, permanent divergence.

`Programs/SweTest/Program.cs`'s `x2`, `xcart` and `xcartq` are sized `[7]`, one slot larger than
the C's `double [6]`. That extra slot exists so a leftover loop index does not throw in C#; it
does not, and is not meant to, reproduce what that leftover index does in the C.

**The C.** `swetest.c:768` declares `x`, `x2`, `xequ`, `xcart`, `xcartq`, `xobl`, `xaz`, `xt`
(then the scalars `hpos`, `hpos2`, `hposj`, `armc`), then `xsv`, all as file-scope `static`
arrays/doubles in one declaration -- BSS, laid out contiguously in that order by the reference
MSVC toolchain. `swetest.c:1850-1854` and `:1872-1876` are `DIFF_MIDP`-style `else` arms inside a
`for (i = 1; i < 6; i++)` loop's enclosing block; when that block runs with no earlier
format-letter block having reset `i`, `i` is left at `6` -- one past the end of a `[6]` array:

```c
/* :1836, ecliptic cartesian ("XU") */               /* :1858, equatorial cartesian ("xu") */
if (strpbrk(fmt, "XU") != NULL) {                    if (strpbrk(fmt, "xu") != NULL) {
  ...                                                   ...
  if (diff_mode) {                                      if (diff_mode) {
    ...                                                    ...
    } else {                                              } else {
      xcart[i] = (xcart[i] + x2[i]) / 2;   /* :1853 */       xcartq[i] = (xcart[i] + x2[i]) / 2; /* :1875 */
    }                                                      }
  }                                                      }
}                                                      }
```

Given the declaration order, `xcart[6]` (`:1853`) aliases `xcartq[0]`, and `xcartq[6]` (`:1875`)
aliases `xobl[0]`; the same statement's right-hand side, `xcart[6]` and `x2[6]`, aliases
`xcartq[0]` and `xequ[0]`.

**Only one of the two writes is invisible.** `:1853`'s write to `xcartq[0]` (via `xcart[6]`) is
inert: if the lowercase `"xu"` block also runs, it recomputes `xcartq[0]` from scratch via
`swe_calc`/`call_swe_fixstar` before anything reads it; if it does not run, `xcartq[0]` is never
read at all -- the house-position block (`:1880-1901`) reads `xobl[0]` and a copy of `x[]`
(`xsv`), never `xcartq`. `:1875`'s write to `xobl[0]` (via `xcartq[6]`) is not inert: `xobl[0]` is
the `eps` argument `swe_house_pos` receives at `:1900`
(`hposj = swe_house_pos(armc, geopos[1], xobl[0], ihsy, xsv, serr);`), reached whenever `fmt`
contains a house-position letter (`strpbrk(fmt, "gGjzm")`, `:1880`). Corrupting the obliquity fed
into that call changes its result.

**Trigger.** `-D<n>` (any diff mode) AND `fmt` contains `x` or `u` (lowercase -- the equatorial
cartesian block, `:1858`) AND `fmt` contains a house-position letter (`g`, `G`, `j`, `z`, `m`) AND
`fmt` contains none of `I`, `i`, `H`, `h`, `K`, `k` (letters whose own blocks run first and leave
`i` at something other than the loop's exit value before the house-position block runs, per the
same leftover-index mechanism this document does not re-derive here).

**Measured**, an MSVC build of the pinned `v2.10.3bfinal` `swetest.exe` vs. this port, both run
with `-b1.1.2020 -p2 -house12,49,P -ut -D0 -emos -n1`:

```
-fPxG    C: Polar Asc. 143°13'52.4484    port: Polar Asc. 211°41'57.3754
-fPXj    C and port match
```

`-fPXj` (uppercase `X`, no lowercase `x`/`u`) never reaches the `:1858` block at all, so `xobl[0]`
is never corrupted and both sides agree; `-fPxG` (lowercase `x`) does, and they diverge by roughly
68 degrees -- not floating-point noise, a different obliquity value entirely.

**Deliberately not reproduced.** The fix that made `-D<n>` stop throwing
(`x2`/`xcart`/`xcartq` sized `[7]`) gives the leftover-index access a defined slot instead of
adding a bounds check the C does not have, matching this file's own precedent (the Gauquelin
`cusp[iofs+8]` fix, `freeze-manifest-log.txt` entry 7, and `SwissEphNet/CPort/SweHouse.cs`'s
`hcusp[37]` fix). It stops there. Reproducing the `xobl[0]` corruption above would require making
`x2`, `xcart` and `xcartq` genuinely alias `xequ`, `xcartq` and `xobl` the way contiguous C BSS
statics happen to under one specific toolchain's layout -- not a portable C guarantee, not
something the C standard specifies, and not a property any future .NET runtime or JIT owes this
code. Deliberately engineering that aliasing into a managed array layout, to reproduce a bug the
C itself does not intend, is the wrong trade for a library that is supposed to be memory-safe by
construction. So this is a known, permanent divergence: `-fPxG`-shaped invocations (and the wider
trigger condition above) will keep returning the arithmetically correct Polar Asc. this port
computes, not the C's corrupted one, and that is intentional.

## %-Ns/%.Ns padded and truncated by characters, C by bytes: width fixed, precision left as-is

**Status: closed for the width side; won't fix for the precision side.** `%s` padding now
counts UTF-8 bytes. `%.Ns` truncation stays character-based, because reproducing C's byte
truncation means emitting malformed output on purpose for a case with no known reproducer.

`SwissEphNet/Tools/C.printf.cs`'s `%s` handling used `string.PadLeft`/`PadRight`, which measure a
field width in UTF-16 code units. C's `printf` measures the same field width in bytes copied from
the caller's `char *`. The two agree for pure-ASCII content -- the overwhelming majority of what
this shim formats -- and disagree by one padding character per extra UTF-8 byte for anything else.

**Reproducer.** `external/swisseph/ephe/seorbel.txt:83` names fictitious body 24 "Korè" -- 4
characters, 5 UTF-8 bytes (`è` is U+00E8, 2 bytes in UTF-8). `Programs/SweTest/Program.cs:2616`
formats it with `%-15.15s`. A C build pads to 15 *bytes*: 5 bytes of name plus 10 spaces. The
port's pre-fix `PadRight(15, ' ')` padded to 15 *characters*: 4 characters of name plus 11 spaces
-- one space too many. Confirmed with `-b1.1.2000 -pz -xz24 -fPLBRS -n1 -edir. -head` against an
MSVC build of the pinned `v2.10.3bfinal` `swetest.exe`.

**Fixed: the width (padding) side.** `case 's'` now computes the pad count as
`fieldLength - Encoding.UTF8.GetByteCount(w)` instead of delegating to `PadLeft`/`PadRight`'s
character-based length, so a name like "Korè" gets exactly as many padding spaces as a C build
would emit. This is a no-op for every ASCII string already formatted correctly (byte count equals
character count there), so it cannot regress any existing byte-exact `swetest` comparison.

**Left as character-based, deliberately: the precision (truncation) side.** `%.Ns` in C truncates
the raw `char *` at the Nth *byte*, which can land inside a multi-byte UTF-8 sequence and produce
genuinely malformed output -- that is a real property of C's byte-oriented `printf`, not an
oversight in it. Reproducing that precisely in C# would mean truncating the UTF-8-encoded byte
sequence directly (not the decoded string) and accepting that the result may not round-trip back
to a valid .NET string at all, which is a materially larger change than the width fix above for a
case this port's own data files essentially never exercise: precision on `%s` in
`Programs/SweTest/Program.cs` is used for short, effectively-ASCII fields (see the `%.8s` fix
already on record in `freeze-manifest-log.txt`), and no known argument here is both non-ASCII and
long enough to be truncated by a `%.Ns` precision. Left character-based rather than chasing a
divergence with no known reproducer and a genuinely hazardous fix (malformed output on purpose).
If a future data file's non-ASCII, over-precision-length string surfaces this, revisit with a real
reproducer in hand rather than a hypothetical one.

## C.atoi saturates on overflow now; C.atof's hex-float and inf/nan gaps are left open

**Status: closed for `atoi`; won't fix for `atof`.** `atoi` now saturates the way C's
does. The hex-float and `inf`/`nan` gaps stay open: no data file this port reads has ever
been observed to contain either token.

Measured against MSVC UCRT (`net10.0` and `net48`), `SwissEphNet/Tools/C.cs`'s `atof`/`atoi` had
four remaining divergences from the C runtime they stand in for:

| input | C | port (before) |
|---|---|---|
| `atof("0x10")` | `16` | `0` |
| `atof("inf")` / `atof("nan")` | `inf` / `nan` | `0` |
| `atoi("2147483648")` | `2147483647` (saturates) | `0` |
| `atoi("-2147483649")` | `-2147483648` (saturates) | `0` |

All four need a hostile or exotic data file to reach: nothing this port ships or is tested
against writes a hex float, an `inf`/`nan` token, or an out-of-`Int32`-range integer into a text
field `atof`/`atoi` parses.

**Fixed: `atoi`'s overflow saturation.** `Int32.TryParse` returning `false` on overflow was being
treated the same as "no digits at all" and silently returned `0` -- the worst of the three
plausible answers (`0`, a wrapped/truncated value, or the C's actual saturated endpoint). C's
`atoi` does not return 0 on overflow: it saturates to `int.MaxValue`/`int.MinValue`, the same
clamp `strtol` performs internally. `atoi` already isolates a leading sign from the narrowed
digit string before calling `TryParse` (see the surrounding comment), so distinguishing genuine
overflow from "no digits" and picking the correctly-signed endpoint needed no new parsing, only a
fallback on `TryParse` failure. Low risk and directly testable: every currently-passing input is
unaffected (an in-range value still parses on the first `TryParse` call), and the only behavior
change is replacing a silently-wrong `0` with the C's actual saturated value for inputs that were
already wrong before this fix.

**Left open: `atof`'s hex-float and `inf`/`nan` literals.** Not fixed, for reasons specific to
each:

- **Hex floats.** MSVC UCRT's `strtod` accepts a bare hex integer like `"0x10"` with no C99
  binary-exponent (`p`) suffix, which is itself an MSVC-specific extension beyond strict C99 (the
  standard grammar requires the `p`-exponent on a hexadecimal-floating-constant); whether glibc's
  `strtod` parses `"0x10"` the same way was not checked here. Implementing this correctly means
  detecting a `0x`/`0X` prefix, parsing hex significand digits with an optional `.`, and handling
  an optional `p`-exponent when present -- a materially different code path from the
  decimal-and-backoff loop `atof` already has, for a token type no ephemeris or configuration file
  in this repository has ever been observed to contain -- the data-file encoding audit this fork
  ran across `sefstars.txt`, `seasnam.txt`, `seorbel.txt` and the rest found only plain ASCII and
  UTF-8 text. Adding it on spec, with no reproducer and an unverified
  cross-platform C behavior to match, risks introducing exactly the kind of untested
  platform-dependent divergence the characterization baseline's platform lock exists to catch,
  for a code path nothing currently exercises.
- **`inf`/`nan`.** C99 `strtod` recognizes `"INF"`/`"INFINITY"`/`"NAN"` (and `"NAN(...)"`)
  case-insensitively; `atof`'s `fchars` set (`"0123456789.+-Ee"`) does not include any of `i`,
  `n`, `f`, `a`, so a leading `"inf"`/`"nan"` token is stripped to nothing by the narrowing step
  before the numeric parser ever runs, and `atof` returns `0`. Recognizing these tokens is more
  contained than the hex-float case -- a prefix check ahead of the numeric path -- but still adds
  a second parsing branch for input this port's data files do not produce; the same "no known
  trigger, do not add untested surface" reasoning applies.

Both are recorded here rather than silently left as "presumably fine": if a future data file or a
2.10.03 upgrade introduces a text field that can plausibly carry either token shape, revisit with
that reproducer in hand instead of the four synthetic ones in the table above.

## SwissEph.DefaultFileProvider: field widened to a property (binary-breaking, source-compatible)

**Status: record.** A deliberate public-shape change, source-compatible and
binary-breaking. The property is still not thread-safe and its doc comment now says so.

`SwissEph.DefaultFileProvider` was a mutable public *field* (`public static IEphemerisFileProvider
DefaultFileProvider = null;`): no room to add validation, logging, or synchronization later
without another public-shape change, and every read/write compiled to a direct `ldsfld`/`stsfld`
against that field. Changed to an auto-implemented property
(`public static IEphemerisFileProvider DefaultFileProvider { get; set; } = null;`) with the same
name, type, nullability and default value.

**Source-compatible, binary-breaking.** Every existing call site --
`SwissEph.DefaultFileProvider = provider;`, reading `SwissEph.DefaultFileProvider` -- compiles
unchanged against the property; nothing in this repository needed an edit. A consumer's assembly
compiled against the old field-based surface, without recompiling against this change, will fail
to bind at load time: field access and property access are different IL shapes
(`ldsfld`/`stsfld` vs. a `call`/`callvirt` to a generated accessor), so this is a binary break for
anyone shipping a pre-built assembly against an older version of this library, even though no
source change is required on their side. Recompiling against the new version is sufficient; no
consumer code needs to change.

**Still not thread-safe, and now says so.** The property adds no locking; a concurrent write on
one thread and a `new SwissEph()` construction (which reads this into the new instance's own
`FileProvider`) on another still race exactly as they did with the field, with no ordering
guarantee on which value the new instance observes. The XML doc comment now states this
explicitly, matching the pattern this document has used to record other unsynchronized static
state, rather than leaving it implicit.
