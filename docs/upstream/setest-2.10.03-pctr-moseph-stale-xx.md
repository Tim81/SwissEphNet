# `setest` suite 1 testcase 5: `t.exp` records values `swe_calc_pctr` never wrote

This comes from the SwissEphNet fork, a C# port of the library. We found this comparing our
port's output against `setest`'s own expected-results file, `setest/t.exp`, while working through
the 2.10.03 conformance corpus.

## The defect

`setest/suite_01_calc.c:6` declares `double xx[6],jd;` once, immediately after
`TESTSUITE(1,"Various swe_calc calls in different modes")` and before any `TESTCASE`. We ran this
fork's own copy of the generator to see what that produces -- `m4 testsuite.m4 globals_suite.c
suite_01_calc.c testsuite_end.m4`, the exact command `setest/Makefile` uses to build
`generated_tests.c` -- and confirmed directly: `xx` becomes a variable of the generated
`testsuite_1` function, and `testcase_1_1` through `testcase_1_5` are nested functions closing
over it. All five testcases in suite 1 share that one buffer for the life of a single suite run,
and nothing clears it between them.

`TESTCASE(5)` passes that shared buffer into `swe_calc_pctr`:

```c
void testcase_1_5(test_context *ctx) {
  int rc = swe_calc_pctr(jd, get_i("ipl",ctx), get_i("iplctr",ctx), iflag | iephe, xx, serr);
  check_swecalc_results(rc,xx,serr,ctx);
  }
```

`swe_calc_pctr` forces `SEFLG_BARYCTR` into its internal `iflag2` (`sweph.c:8061`):

```c
iflag2 |= (SEFLG_BARYCTR|SEFLG_J2000|SEFLG_ICRS|SEFLG_TRUEPOS|SEFLG_EQUATORIAL|SEFLG_XYZ|SEFLG_SPEED);
```

and the inner `swe_calc` it calls at `sweph.c:8063` hits the `SEFLG_BARYCTR|SEFLG_MOSEPH` reject
inside `swe_calc` itself, `sweph.c:634-638`:

```c
/* no barycentric calculations with Moshier ephemeris */
if ((iflag & SEFLG_BARYCTR) && (iflag & SEFLG_MOSEPH)) {
  if (serr != NULL)
    strcpy(serr, "barycentric Moshier positions are not supported.");
  return ERR;
}
```

`swe_calc_pctr` propagates that `ERR` straight back at `sweph.c:8064-8065`, before its own
`xxret` output parameter is ever touched:

```c
retc = swe_calc(tjd, iplctr, iflag2, xxctr, serr);
if (retc == ERR)
  return ERR;
```

So on every `SEFLG_MOSEPH` iteration of testcase 5, the shared `xx` still holds whatever the
*previous* testcase wrote into it -- and `t.exp` recorded those stale values as the expected
result.

## What `t.exp` actually records

It is systematic, not a one-off. All four `SEFLG_MOSEPH` iterations in suite 1 testcase 5, read
straight out of `t.exp`:

| iter | iephe | rc | serr | `xx[0]` |
|---|---|---|---|---|
| 1 | 2 (`SEFLG_SWIEPH`) | 2 | (none) | `169.36685325561396098237` |
| 2 | 4 (`SEFLG_MOSEPH`)  | -1 | `barycentric Moshier positions are not supported.` | `169.36685325561396098237` -- identical to iter 1 |
| 4 | 2 (`SEFLG_SWIEPH`) | 2 | (none) | `278.21313013380341772063` |
| 5 | 4 (`SEFLG_MOSEPH`)  | -1 | `barycentric Moshier positions are not supported.` | `278.21313013380341772063` -- identical to iter 4 |
| 7 | 2 (`SEFLG_SWIEPH`) | 2 | (none) | `169.36684402396099358157` |
| 8 | 4 (`SEFLG_MOSEPH`)  | -1 | `barycentric Moshier positions are not supported.` | `169.36684402396099358157` -- identical to iter 7 |
| 10 | 2 (`SEFLG_SWIEPH`) | 2 | (none) | `278.21688685190588330443` |
| 11 | 4 (`SEFLG_MOSEPH`)  | -1 | `barycentric Moshier positions are not supported.` | `278.21688685190588330443` -- identical to iter 10 |

Each pair matches across all six `xx` components, not only `xx[0]`. The `SEFLG_JPLEPH` iterations
(3, 6, 9, 12) carry genuinely different values from their `SEFLG_SWIEPH` neighbours, which is what
rules out these being copy-pasted rows rather than a stale buffer: the JPL leg actually computed,
the Moshier leg did not.

What the harness got right matters to the diagnosis: `rc` is recorded correctly as `-1` on all
four, and `serr` is recorded correctly as `"barycentric Moshier positions are not supported."` on
all four. Only `xx` is stale. The capture logic is fine; what is missing is clearing, or not
recording, an out-parameter the call never wrote.

## Independently reproduced

We built your own C at the pinned `v2.10.3bfinal` (`f4dcd18e`) with MSVC 19.51
(`/O2 /fp:precise /D_CRT_SECURE_NO_WARNINGS /MD`), disassembly-checked to confirm no FMA
contraction, and linked a small driver against it calling `swe_calc_pctr` directly with real
ephemeris data (Jupiter and Mars from `sepl_18.se1`, no Moshier fallback).

A call to `swe_calc_pctr(2455334.0, /* Mars */ 4, /* Jupiter */ 5, SEFLG_MOSEPH, xx, serr)` on a
freshly zeroed buffer, with nothing written to `xx` beforehand, returns:

```
rc=-1 serr="barycentric Moshier positions are not supported." xx=[0,0,0,0,0,0]
```

That is what a correct implementation's output parameter looks like on this path: unwritten. It
does not match what `t.exp` iteration 2 expects.

Replaying testcase 5's own sequence -- `SEFLG_SWIEPH` into the buffer, then `SEFLG_MOSEPH` right
after without clearing it, exactly as `testcase_1_1`..`testcase_1_5` do when they share one `xx` --
reproduces `t.exp`'s shape exactly. The `SEFLG_SWIEPH` call fills `xx[0]` with
`169.36685249095711...`; the following `SEFLG_MOSEPH` call returns `rc=-1` and the same error
string, with `xx[0]` still `169.36685249095711...` from the call before it.

## Consequence

Any implementation that behaves correctly here -- returning `ERR` and leaving the caller's buffer
alone -- fails those four iterations against `t.exp` as it stands. An implementation that wrongly
wrote stale or garbage values into `xxret` on this path might accidentally pass. The corpus rewards
the wrong behaviour on those four rows.

## Suggested fix

Ours to flag, yours to choose: clear `xx` in the testcase or in a `SETUP`, or declare it inside the
`TESTCASE(5)` block instead of sharing it with the rest of the suite, then regenerate those four
rows. The fix and the regeneration have to go together -- a regenerated expected value for a call
that writes nothing is only whatever the harness happens to zero it to.

## A separate observation: the `SEFLG_SWIEPH` rows have drifted too

Not a complaint about the defect above, a data point alongside it. The `SEFLG_SWIEPH` iterations
in the same testcase are ordinary numeric drift, not a scoping bug. Your own C at the pinned tag
computes `169.36685249095711` for iteration 1's `xx[0]`, where `t.exp` records
`169.36685325561396` -- matching to the eighth significant digit and diverging at the ninth. This
fork carries roughly ninety further suite 1 rows of the same shape (small, sub-part-per-million
relative error, not a category mismatch), consistent with the corpus and the current C having
drifted apart in the time since `t.exp` was generated. `t.exp`'s own header records
`localtime: 14.12.2023 16:48:13` and `user: alois`.

## A note on `swe_calc_pctr` itself

`sweph.c:634-638`'s guard is what makes this behaviour correct in the first place -- rejecting
`SEFLG_BARYCTR|SEFLG_MOSEPH` before any geometry runs, with a clear `serr`, is the right response
to a combination the Moshier ephemeris cannot support. The defect here is in the test, not in the
library.

---

Reported from the SwissEphNet fork (`https://github.com/Tim81/SwissEphNet`), a C# port of the
Swiss Ephemeris tracking `v2.10.3bfinal`. Original port by Yan Grenier; fork work by
Timothy van der Ham.
