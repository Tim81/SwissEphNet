# The 2.10.03 delta's no-op hunks

`scripts/gen-delta-hunk-counts.tsv` pins 403 filtered hunks across 24 files for the 2.08-to-2.10.03
delta. Of those, 35 hunks across seven files carry no semantic content for the C# port: renamed
preprocessor symbols, `void` added to empty C parameter lists, dead local variables, comment and
license-header churn, and a Windows-only header with no C# counterpart. This records what each
hunk actually contains, checked against `pwsh scripts/gen-delta.ps1 -File <name>` and the upstream
C at the cited lines, so a future porter can retire them without re-deriving the analysis.

Regenerate any of these with `pwsh scripts/gen-delta.ps1 -File <name>` -- the citations below are
`file:line` ranges into `external/swisseph` at the pinned `v2.10.3final` tag.

## `swehel.c` -- 18 hunks, confirmed no-op

| hunks | change |
|---|---|
| `swehel.c:79-85`, `281-287`, `1426-1432`, `1509-1515` | `#define DEBUG 0` renamed to `SWEHEL_DEBUG`, with its three `#if DEBUG` / `#if SWEHEL_DEBUG` guards updated to match |
| `swehel.c:830-836`, `1286-1292`, `1389-1395`, `1917-1923`, `3227-3233` | `if (0) {` rewritten `if ((0)) {` -- same dead branch, extra parens |
| `swehel.c:1816-1822` | `isalnum(*sp)` becomes `isalnum((int) *sp)`, an explicit cast to silence a compiler warning on signed `char` promotion |
| `swehel.c:3164-3171`, `3173-3178`, `3251-3257`, `3260-3265`, `3384-3390` | unused locals removed (`epheflag`/`iflag` in `heliacal_ut_vis_lim` and `moon_event_vis_lim`, `itry`'s declaration in `swe_heliacal_ut`) -- each removed variable was assigned but never read |
| `swehel.c:3482-3489` | the `for` loop in `swe_heliacal_ut` drops its unused `itry` counter: `for (itry = 0; tjd < tjdmax && retval == -2; itry++, tjd += tadd)` becomes `for (tjd = tjd0; tjd < tjdmax && retval == -2; tjd += tadd)`. The loop condition and `tjd` increment are unchanged, so the two forms iterate identically |
| `swehel.c:51-63` | licence-header text (adds the Koch/Treindl author line and "(Astrodienst)" to the promotion clause) |

None of these touch a return value, a branch condition's outcome, or a computed quantity. The
`DEBUG`-to-`SWEHEL_DEBUG` rename in particular is a C-only naming fix (avoiding collision with a
build system that predefines `DEBUG`) with nothing for a C# port to mirror -- C# does not read this
macro at all.

One adjacent fact worth a future porter knowing, unrelated to this delta: `SwissEphNet/CPort/SweHel.cs`
already declares `const int DEBUG = 0` (line 105) and guards trace calls with `#if DEBUG` (lines 305,
1410, 1502). In C#, `#if DEBUG` checks the compiler's `DEBUG` build symbol, not this local constant, so
those blocks compile into Debug builds regardless of the `= 0`. The blocks only call `trace(...)`, which
prints; they never change a return value, so this has no effect on the characterization baseline or the
conformance oracle. It predates the 2.10.03 delta and is independent of the `SWEHEL_DEBUG` rename above --
noted here because the next hand to touch this file will be looking at the same lines.

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
  The crossing functions are already ported (see "Landed" below); their C# signatures live on
  `SwissEph`, not in a header.
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

Every count above matches `scripts/gen-delta-hunk-counts.tsv` and passes
`pwsh scripts/verify-gen-delta.ps1`; none of the pinned totals needed correcting. Each hunk listed
here was read individually against `external/swisseph` at `v2.10.3final`, not assumed from the
original summary this document verifies.
