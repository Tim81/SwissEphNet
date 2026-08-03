# The 2.10.03 delta's no-op hunks

`scripts/gen-delta-hunk-counts.tsv` pins 405 filtered hunks across 24 files for the 2.08-to-2.10.03
delta. Of those, 33 hunks across eight files carry no semantic content for the C# port: `void`
added to empty C parameter lists, dead local variables, comment and license-header churn,
formatting-only reindentation, and a Windows-only header with no C# counterpart. Four further
hunks in `swehel.c` rename a preprocessor symbol and are recorded separately below, because they
turn out not to be no-op for this port. This records what each
hunk actually contains, checked against `pwsh scripts/gen-delta.ps1 -File <name>` and the upstream
C at the cited lines, so a future porter can retire them without re-deriving the analysis.

Regenerate any of these with `pwsh scripts/gen-delta.ps1 -File <name>` -- the citations below are
`file:line` ranges into `external/swisseph` at the pinned `v2.10.3bfinal` tag (unchanged from
`v2.10.3final` for every file cited here: the two tags differ only in `sweodef.h`, `swetest.c` and
`ephe/`, none of which any hunk below touches).

## `swehel.c` -- 22 raw hunks (18 filtered, 4 license-noise); 14 confirmed no-op, 4 not

`gen-delta.ps1 -File swehel.c` reports `raw=22 filtered=18 license-noise=4`. Of the 18 filtered
hunks, 14 are confirmed no-op; the remaining 4 (the `DEBUG`-to-`SWEHEL_DEBUG` rename) are not,
for a reason specific to this port -- see below the table.

| hunks | change |
|---|---|
| `swehel.c:830-836`, `1286-1292`, `1389-1395`, `1917-1923`, `3227-3233` | `if (0) {` rewritten `if ((0)) {` -- same dead branch, extra parens |
| `swehel.c:1816-1822` | `isalnum(*sp)` becomes `isalnum((int) *sp)`, an explicit cast to silence a compiler warning on signed `char` promotion |
| `swehel.c:3164-3171`, `3173-3178`, `3251-3257`, `3260-3265`, `3384-3390` | unused locals removed (`epheflag`/`iflag` in `heliacal_ut_vis_lim` and `moon_event_vis_lim`, `itry`'s declaration in `swe_heliacal_ut`) -- each removed variable was assigned but never read |
| `swehel.c:3482-3489` | the `for` loop in `swe_heliacal_ut` drops its unused `itry` counter: `for (itry = 0; tjd < tjdmax && retval == -2; itry++, tjd += tadd)` becomes `for (tjd = tjd0; tjd < tjdmax && retval == -2; tjd += tadd)`. The loop condition and `tjd` increment are unchanged, so the two forms iterate identically |
| `swehel.c:476-485` | four `if`/`else` lines in the rise/set estimate reindented from column 0 to match the enclosing block; no token changes |
| `swehel.c:51-63` | licence-header text (adds the Koch/Treindl author line and "(Astrodienst)" to the promotion clause) |

None of these fourteen touch a return value, a branch condition's outcome, or a computed
quantity.

**The `DEBUG`-to-`SWEHEL_DEBUG` rename is not no-op for this port**, unlike the other thirteen.
`swehel.c:79-85`, `281-287`, `1426-1432` and `1509-1515` rename `#define DEBUG 0` to
`SWEHEL_DEBUG`, with its three `#if DEBUG` / `#if SWEHEL_DEBUG` guards updated to match, so a
build system that predefines `DEBUG` does not collide with it. `SwissEphNet/CPort/SweHel.cs`
has the exact C# counterpart of that same collision, live today: it declares `const int DEBUG =
0` (line 105) and guards trace calls with `#if DEBUG` (lines 305, 1410, 1502). In C#, `#if
DEBUG` checks the compiler's `DEBUG` build symbol, not this local constant, so those blocks
compile into every Debug build regardless of the `= 0` -- the same shape of bug the C rename
exists to avoid, just tripped by the C# compiler's own predefined symbol instead of a build
system's. The blocks only call `trace(...)`, which prints; they never change a return value, so
this has no effect on the characterization baseline or the conformance oracle, and it predates
the 2.10.03 delta. But a same-shape port of the rename -- renaming `SweHel.cs`'s local `DEBUG`
constant to `SWEHEL_DEBUG` -- would fix it, which is what makes these four hunks actionable
rather than no-op.

## `swemmoon.c` -- 6 hunks, confirmed no-op

Six functions gain an explicit `void` parameter list, with no other change in each hunk:

- `swemmoon.c:1179-1185` -- `moon1()` to `moon1(void)`
- `swemmoon.c:1364-1370` -- `moon2()` to `moon2(void)`
- `swemmoon.c:1441-1447` -- `moon3()` to `moon3(void)`
- `swemmoon.c:1455-1461` -- `moon4()` to `moon4(void)`
- `swemmoon.c:1760-1766` -- `mean_elements()` to `mean_elements(void)`
- `swemmoon.c:1817-1823` -- `mean_elements_pl()` to `mean_elements_pl(void)`

An empty parameter list means different things in C (an unspecified, un-checked argument count) and
C++ (zero arguments); `(void)` makes "takes no arguments" explicit under both. C# has no equivalent
ambiguity -- a method declared with no parameters always takes none -- so there is nothing to port.

## `swedll.h` -- 5 hunks, four confirmed no-op, one mislabeled reason (same verdict)

Four of the five hunks are genuinely `DllImport`-decorated C prototypes for the Windows DLL build,
which has no C# counterpart (the managed library exposes its public surface as ordinary C# methods,
not `extern` exports):

- `swedll.h:105-137` -- adds prototypes for `swe_calc_pctr` and the eight crossing functions
  (`swe_solcross[_ut]`, `swe_mooncross[_ut]`, `swe_mooncross_node[_ut]`, `swe_helio_cross[_ut]`).
  The crossing functions are already ported (see "Landed" in `docs/sweph-c-stages.md`); their
  C# signatures live on `SwissEph`, not in a header.
- `swedll.h:172-193` -- adds `swe_houses_ex2` and `swe_houses_armc_ex2` prototypes, and widens
  `swe_house_name`'s return type from `char *` to `const char *`.
- `swedll.h:202-208` -- adds the `swe_get_current_file_data` prototype.
- `swedll.h:250-257` -- widens `swe_set_ephe_path` and `swe_set_jpl_file`'s parameter from
  `char *` to `const char *`.

The fifth hunk (`swedll.h:58-63`) does not match the pinned table's stated reason: it removes an RCS
`$Id:` comment line. That is the same kind of version-stamp comment covered under `swedate.c`/`swedate.h`
below, not a DLL prototype. The verdict is unchanged (a removed comment carries nothing to port), but
the reason column in the original summary was wrong for this one hunk; the count of 5 is correct.

## `swejpl.c` -- 2 hunks, confirmed no-op

- `swejpl.c:81-92` adds a five-line block, entirely commented out with `//`, guarding
  `FSEEK`/`FTELL` redefinitions behind `__ANDROID_API__`. Since every added line is a comment, this
  changes nothing that compiles on any platform, C or C#.
- `swejpl.c:951-957` -- `swi_get_jpl_denum()` to `swi_get_jpl_denum(void)`, the same explicit-`void`
  fix as `swemmoon.c` above.

## `Makefile` -- 2 hunks, confirmed no-op

A full rewrite of the Linux-only `gcc` build (50 lines) into a cross-platform Linux/macOS Makefile
(122 lines) with `uname`-based OS detection, new targets (`swetests`, `swevents`, `sweventss`,
`obama`, `test`, `test.exp`), and updated dependency rules. None of it applies: this project builds
with the .NET SDK and `SwissEphNet.csproj`/`SwissEphNet.CrossPlatform.slnf`, not `make`.

## `swedate.c` / `swedate.h` -- 1 hunk each, confirmed no-op

Both hunks are comment-only: each removes an RCS `$Header:`/version-stamp line (`swedate.h` replaces
it with a plain `version 21-apr-2021` line instead of deleting it outright) and bumps the copyright
year from `1997 - 2008` to `1997 - 2021`, with matching blank-line churn. No declaration, prototype,
or executable line changes in either file. This is the same class of change as the mislabeled
`swedll.h:58-63` hunk above: a version-stamp comment that `gen-delta.ps1`'s licence-noise filter
(which matches only the standard copyright/GPL-to-AGPL rewrite) left in the filtered output instead
of dropping, because it does not match that filter's licence-text pattern.

## Verification record

Every per-file hunk count above matches `scripts/gen-delta-hunk-counts.tsv` and passes
`pwsh scripts/verify-gen-delta.ps1`; none of the pinned totals needed correcting. A later
re-audit did correct this document's own accounting for `swehel.c`, though: its filtered count
of 18 was right, but the itemized table was missing the `476-485` reindentation hunk, and the
four `DEBUG`-to-`SWEHEL_DEBUG` hunks were wrongly classified as no-op (see that section above).
Neither correction moves any `scripts/gen-delta-hunk-counts.tsv` number. Each hunk listed here
was read individually against `external/swisseph` at `v2.10.3final` (unchanged in `swehel.c`
after the `v2.10.3bfinal` bump), not assumed from the original summary this document verifies.

## swecl.c: two of its 29 hunks are formatting

The rest of `swecl.c` was ported in phase 3. Two hunks carry no C# counterpart, recorded here so
the file's hunk accounting adds up without re-deriving it:

- **`if( (inalt + refr < dip) )` becomes `if (inalt + refr < dip)`** -- a space after `if` and a
  redundant inner paren. The port already reads `if ((inalt + refr < dip))` at `SweCL.cs:3142`
  with the same redundant paren; same-shape porting would drop it, and it changes nothing either
  way.
- **`nazalt++;` reindented.**

One further shape divergence in the same file is deliberate and is not a no-op, so it is recorded
here rather than left to be rediscovered: 2.10.03 promotes two commented-out `fprintf(stderr, ...)`
calls to live `if (0) fprintf(...)` statements. The port keeps them as comments. Both are no-ops in
any build, and this port has no `fprintf(stderr)` equivalent to call, so writing `if (false)` would
add unreachable code for no gain -- and `CPort/.editorconfig` keeps CS0162 enabled. The parallel
case at hunk 22, `if (0)` becoming `if ((0))`, genuinely was a no-op because the port already had
`if (false)` there.
