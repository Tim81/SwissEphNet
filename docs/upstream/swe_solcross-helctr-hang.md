# `swe_solcross` and `swe_solcross_ut` never return when `SEFLG_HELCTR` is set

The refinement loop divides by a speed that is always zero under this flag, so the search step
becomes infinite and the loop condition can never be met. There is no iteration cap and the loop's
own error escape does not fire, so the call never returns and the thread spins at 100% CPU.

Measured at `v2.10.3bfinal`, the current release. `git ls-remote` puts both `refs/tags/v2.10.3bfinal`
and `refs/heads/master` at `f4dcd18e`, so there is no fix on `master` either, and `sweph.c` is
byte-identical between `v2.10.3final` (`af9823fe`) and `v2.10.3bfinal`. Anyone taking the patch
release still gets this. `swe_solcross` is at `sweph.c:8321`, `swe_solcross_ut` at `:8355`.

This comes from the SwissEphNet fork, a C# port of the library. We found it while building an oracle
grid that crosses every crossing function with every flag its own documentation lists.

## Is this a combination a caller would actually pass?

We want to put this first, because the last time we reported something we had not asked that
question carefully enough, and you were right to send it back. That was `dms()` with `-d` and `-fZ`,
where your answer was that a zodiacal position format is not a way to express an angular difference,
so the combination was an application error by the format's own definition. We checked and withdrew
it.

This one is different, and here is the evidence rather than our opinion.

**Your own comment on the function documents the flag and names the body it should use.** The block
immediately above `swe_solcross`, at `sweph.c:8313`, reads:

```
 * flag covers the following bits as used by swe_calc():
   SEFLG_HELCTR 		0 = geocentric, SUN, 1 = heliocentric, EARTH
```

That is not a generic note about what `swe_calc` accepts. `swe_calc` does not switch bodies for
anyone; the "SUN / EARTH" pairing is specific to this function and describes what this function is
meant to do with the bit. The same block appears above `swe_solcross_ut` at `:8347`. So the flag is
documented as supported here, and the intended behaviour is on record. Under `SEFLG_HELCTR` the
function should compute the Earth's crossing, which is the meaningful heliocentric counterpart of
the Sun's geocentric crossing, and which a heliocentric chart application would want in order to
find Earth ingresses. The body never switches `ipl` to `SE_EARTH`.

**Every sibling crossing function handles the flag sanely.** We checked all of them rather than
assuming:

All eight were run individually at `jd_et` 2451545.0 with `SEFLG_MOSEPH|SEFLG_HELCTR`; none of these
are inferred from a sibling.

| Function | With `SEFLG_HELCTR` | Result at target 90 |
|---|---|---|
| `swe_solcross` | never returns | killed at 25s |
| `swe_solcross_ut` | never returns | killed at 25s |
| `swe_mooncross` | returns a real crossing | 2451534.836376984 |
| `swe_mooncross_ut` | returns a real crossing | 2451534.835638321 |
| `swe_mooncross_node` | returns a real node crossing | 2451551.747581190, latitude -1.7e-08 |
| `swe_mooncross_node_ut` | returns a real node crossing | 2451551.746842388, latitude -1.7e-08 |
| `swe_helio_cross` | rejects `SE_SUN`, with a message | -1, "not possible for object 0 = Sun" |
| `swe_helio_cross_ut` | rejects `SE_SUN`, with a message | -1, "not possible for object 0 = Sun" |

The two Moon pairs differ from each other by about 64 seconds, which is delta T at that date, so the
UT variants are behaving exactly as they should.

**And you already guard this exact condition elsewhere.** `swe_helio_cross` is heliocentric by
construction, so a caller passing `SE_SUN` would ask for the origin in the same way. It refuses,
at `sweph.c:8538`:

```c
if (ipl == SE_SUN
  || ipl == SE_MOON
  || (ipl >= SE_MEAN_NODE && ipl <= SE_OSCU_APOG)
  || (ipl >= SE_INTP_APOG && ipl < SE_NPLANETS)
) {
  char snam[AS_MAXCH];
  swe_get_planet_name(ipl, snam);
  if (serr != NULL) sprintf(serr, "swe_helio_cross: not possible for object %d = %s", ipl, snam);
  return ERR;
}
```

Confirmed by running it: `swe_helio_cross(SE_SUN, 90.0, 2451545.0, SEFLG_MOSEPH, 1, &jd, serr)`
returns `-1` with `serr` set to `swe_helio_cross: not possible for object 0 = Sun`. So the
"this body is the origin under this flag" case is one you have already recognised and handled
deliberately in one function. `swe_solcross` is the one place it is neither implemented nor
rejected.

So we do not think this is the same shape as the `-fZ` report. But the decision is yours, and if
you conclude a caller has no business passing `SEFLG_HELCTR` to a Sun-crossing function, then one
finding still stands on its own: **an unbounded `for(;;)` in a library should not be reachable from
a flag bit.** That part does not depend on which way you rule.

## Reproducing

```python
import swisseph as swe          # pyswisseph 2.10.03 wraps this C unmodified
swe.set_ephe_path(None)
swe.solcross(90.0, 2451545.0, swe.FLG_MOSEPH)                    # 2451716.575531276
swe.solcross( 0.0, 2451545.0, swe.FLG_MOSEPH | swe.FLG_HELCTR)   # nan
swe.solcross(90.0, 2451545.0, swe.FLG_MOSEPH | swe.FLG_HELCTR)   # never returns
```

Run the third under a timeout. Julian day 2451545.0 is J2000.

Target longitude 0 is the only input that terminates, and it does so by accident. It returns `nan`
with `serr` empty. Since the documented error convention is a return less than `jd_et`, and
`nan < jd_et` is false, a caller checking for the error cannot detect that one either.

**The hang is not specific to the Moshier backend.** We first reproduced it with `SEFLG_MOSEPH`
because it needs no data files, then repeated it with `SEFLG_SWIEPH` against the shipped `.se1`
files. It hangs identically. `swe_calc(2451545.0, SE_SUN, SEFLG_SWIEPH|SEFLG_HELCTR|SEFLG_SPEED)`
returns longitude 0 and longitude speed 0, the same as the Moshier path, because the answer comes
from the coordinate definition rather than from an ephemeris.

## Mechanism

`swe_solcross` hardcodes the body at `sweph.c:8325`:

```c
int ipl = SE_SUN;
```

and never changes it. With `SEFLG_HELCTR` the caller is therefore asking for the heliocentric
position of the Sun, which is the origin of that coordinate system:

```c
swe_calc(2451545.0, SE_SUN, SEFLG_MOSEPH|SEFLG_HELCTR|SEFLG_SPEED, x, serr);
/* retc = 1804, x = {0, 0, 0, 0, 0, 0}, serr empty */
```

So `x[0]`, the longitude, is 0 and `x[3]`, the longitude speed, is 0 at every date. The refinement
loop at `sweph.c:8335-8341` then reads:

```c
for(;;) {
  if (swe_calc(jd, ipl, flag, x, serr) < 0)
    return jd_et - 1;
  dist = swe_difdeg2n(x2cross, x[0]);
  jd += dist / x[3];                        /* x[3] is always 0 here */
  if (fabs(dist) < CROSS_PRECISION) break;
}
```

Two things go wrong together. `dist` is computed from `x[0]`, which is 0 no matter what `jd` is, so
`dist` equals `x2cross` on every iteration and never converges. And `jd += dist / 0` makes `jd`
infinite on the first pass.

The loop's own error escape should catch that, and does not, because `swe_calc` at an infinite date
returns success under this flag combination:

```c
swe_calc(INFINITY, SE_SUN, SEFLG_MOSEPH|SEFLG_HELCTR|SEFLG_SPEED, x, serr);
  /* retc = 1804, x[0] = 0, x[3] = 0, serr empty */
swe_calc(INFINITY, SE_SUN, SEFLG_MOSEPH|SEFLG_SPEED, x, serr);
  /* retc = -1, serr = "jd inf outside Moshier planet range 625000.50 .. 2818000.50" */
```

The heliocentric Sun is answered from the coordinate definition without consulting an ephemeris, so
the date range check never runs and no date is ever invalid. The geocentric path rejects the same
input properly. So `if (swe_calc(...) < 0)` never fires, the loop has no iteration counter, and it
runs until the process is killed. The hang is in `swe_solcross`'s own loop, not inside `swe_calc`.

Target longitude 0 escapes only because `dist` is then 0, so the division is `0.0 / 0.0` rather than
`90.0 / 0.0`. That yields `NaN` instead of infinity, and `fabs(0.0) < CROSS_PRECISION` is true, so
the loop breaks on its first pass and hands back the `NaN` it has accumulated in `jd`.

## What the Moon functions actually do, which corrects an earlier draft of this note

An earlier version of this report said `swe_mooncross` and `swe_mooncross_ut` "return the documented
error" under `SEFLG_HELCTR`. That was wrong, and we are glad we checked before sending it. They
return values, with `serr` empty:

```
swe_mooncross(  0.0, 2451545.0, SEFLG_MOSEPH|SEFLG_HELCTR)  ->  2451445.042408307
swe_mooncross( 90.0, 2451545.0, SEFLG_MOSEPH|SEFLG_HELCTR)  ->  2451534.836376984
swe_mooncross(180.0, 2451545.0, SEFLG_MOSEPH|SEFLG_HELCTR)  ->  2451623.813410182
swe_mooncross(270.0, 2451545.0, SEFLG_MOSEPH|SEFLG_HELCTR)  ->  2451716.465567217
```

These look correct. The heliocentric Moon sits close to the heliocentric Earth, so its crossing of
180 should land near the geocentric Sun's crossing of 0, and it does: 2451623.813410182 against
2451623.816894977, a difference of about five minutes, which is the Moon's own offset from the
Earth. Nothing is wrong with these functions. They do not hang for the reason the earlier draft
gave, which was right as far as it went: the heliocentric Moon is not the coordinate origin, so its
speed is non-zero and the division is well defined.

Worth noting for the first two rows only: they are less than `jd_et`, which by the documented
convention a caller reads as an error, even though they are real crossings. That is the ordinary
consequence of asking for a crossing the body most recently made rather than the next one, and we
are not reporting it as a defect.

`swe_mooncross_node` under the same flag returns 2451551.747581190 with a latitude of -1.7e-08,
which is a real node crossing. It does not hang either.

## An observation on `SEFLG_BARYCTR` that we are not pressing

`SEFLG_BARYCTR` is not in this function's documented flag list, so this is outside the contract and
we mention it only so it is on the record.

With `SEFLG_MOSEPH` it is refused cleanly: `swe_calc` returns -1 with "barycentric Moshier positions
are not supported.", and `swe_solcross` passes that straight out as `jd_et - 1`. Correct behaviour.

With `SEFLG_SWIEPH` it is supported, and the barycentric Sun has a real non-zero speed (0.066
degrees per day at J2000), so there is no division by zero and no hang. But `swe_solcross` seeds its
search with the mean *solar* speed, `xlp = 360.0 / 365.24`, about 0.986 degrees per day, which is
fifteen times too fast for the body it is actually tracking. The result overshoots:
`swe_solcross(90.0, 2451545.0, SEFLG_SWIEPH|SEFLG_BARYCTR)` returns 2449568.014466507, roughly two
years *before* `jd_et`, with `serr` empty, contradicting the function's own "returns juldate of the
next crossing, with jd > jd_et". A caller following the documented convention would read it as an
error, so it is at least detectable. Again: undocumented flag, so your call whether it matters.

## Suggested fix

There are two defensible fixes and we are not going to insist on either, because which one is right
depends on what you intended the flag to mean here.

**Option A, honour the comment.** Substitute the body the documentation already names. Two lines,
one in each function, attached as `swe_solcross-helctr.patch`:

```diff
   double x[6], xlp, dist;
   double jd;
-  int ipl = SE_SUN;
+  int ipl = (flag & SEFLG_HELCTR) ? SE_EARTH : SE_SUN;
```

**Option B, refuse the flag,** the way `swe_helio_cross` already refuses `SE_SUN`, with a message in
`serr` and the documented error return. This is a smaller change and it makes the two functions
consistent with each other. It does mean the comment at `:8313` and `:8347` should lose the
", EARTH" clause, since the behaviour it promises would then not exist anywhere.

Option A restores the loop's error escape as a side effect, which is worth knowing: with `SE_EARTH`
the ephemeris range check applies normally, so even an unforeseen route to an out-of-range `jd`
would leave through the documented error return rather than spin.

**Either way, we would suggest an iteration cap on the loop.** The `for(;;)` has no bound, and one
flag bit should not be able to make a library spin forever even once this particular route is
closed. Returning `jd_et - 1` after some generous number of passes would match the error convention
the function already documents, and would give `serr` somewhere to explain itself.

## What Option A does when you build it

We built it rather than only proposing it. MSVC 19.51.36248 x64, `/O2 /fp:precise /MD`, the tagged
sources with only the two lines above changed.

Every target returns, and the values are right. The check is that the Earth's heliocentric longitude
is the Sun's geocentric longitude plus 180 degrees, so a heliocentric crossing of `t` must fall at
the same instant as a geocentric crossing of `t + 180`. The last column is that independent
calculation, run through the unmodified geocentric path:

| target | geocentric Sun | heliocentric Earth | geo(t+180) cross-check | difference |
|---|---|---|---|---|
| 0.0 | 2451623.816894977 | 2451810.228230200 | 2451810.228230188 | 1.2e-08 |
| 0.5 | 2451624.320476105 | 2451810.738953894 | 2451810.738953881 | 1.3e-08 |
| 90.0 | 2451716.575531276 | 2451900.068406662 | 2451900.068406653 | 9.0e-09 |
| 180.0 | 2451810.228230188 | 2451623.816894982 | 2451623.816894977 | 5.0e-09 |
| 359.5 | 2451623.313456060 | 2451809.717340656 | 2451809.717340643 | 1.3e-08 |

The residuals are around 1e-8 days, roughly a millisecond, which is well inside `CROSS_PRECISION`:
one milliarcsecond at the Earth's roughly one degree per day works out to about 2.8e-7 days. The two
paths agree as closely as the convergence criterion allows.

`swe_solcross_ut` checks out separately. With the same fix it returns 2451900.067664941 for target
90, against 2451900.068406662 from `swe_solcross`. The difference is 64.08 seconds, and `swe_deltat`
at that date is also 64.08 seconds, so the UT variant is offset by exactly delta T as it should be.

## Scope of what we tested

Everything above is measured, not inferred, and every number in it was produced by a run we made
rather than carried over from an earlier draft.

Two independent routes agree. The first is our own MSVC build of the tagged sources linked into a
test program. The second is pyswisseph 2.10.03, which wraps this C unmodified, and which we ran
rather than merely citing: it gives 2451716.575531276 for the geocentric call, `nan` for
`SEFLG_HELCTR` at target 0, all zeros for `swe_calc` on the heliocentric Sun, and it hangs at target
90, matching our build on all four.

The hang was reproduced at four separate target longitudes and under both `SEFLG_MOSEPH` and
`SEFLG_SWIEPH`, so it does not depend on the ephemeris backend. All eight crossing functions were
exercised under `SEFLG_HELCTR` individually, not just the two that fail. The patched build was
checked against an independent geocentric calculation at five targets and against `swe_deltat`.

One limit: we tested on Windows with MSVC 19.51.36248. The mechanism is plain IEEE-754 arithmetic
and a loop with no bound, so we would expect the same from gcc and clang, but we have not run it
there.

---

Reported from the SwissEphNet fork (`https://github.com/Tim81/SwissEphNet`), a C# port of the Swiss
Ephemeris tracking `v2.10.3bfinal`. Original port by Yan Grenier; fork work by Timothy van der Ham.
