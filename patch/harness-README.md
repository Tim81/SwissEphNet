swehouse.c cusp-speed harness

A regression harness for the swehouse.c cusp-speed patches. Build it
twice, once against an unpatched Swiss Ephemeris and once against a
patched one, and compare. The unpatched library is the baseline:
positions must not move except where a patch says they will and by how
much, return codes and error strings must not change, and every speed
that does change has to be accounted for.

It needs no ephemeris files. Only the Sunshine systems ask for the Sun,
and swe_houses_ex2() requests it with a hardcoded flag set
(swehouse.c:146-148 and :260-262), so the library picks its own source
and falls back to Moshier when no file is present. A dump nevertheless
records a fingerprint of the data that answered, and compare refuses a
pair whose fingerprints differ - see "Reproducibility" below.


Build and run

  ./build.sh /path/to/swisseph-unpatched  ./b-base
  ./build.sh /path/to/swisseph-patched    ./b-patched

  ./b-base/house-speed-harness    dump base.bin    1 trop
  ./b-patched/house-speed-harness dump patched.bin 1 trop
  ./b-base/compare base.bin patched.bin 20 --expect-apc-equator

  ./b-patched/house-speed-harness deriv
  ./b-patched/house-speed-harness claims

On Windows, build.ps1 -Source <dir> -Out <dir> does the same with MSVC.

Both build scripts turn floating-point contraction off -
-ffp-contract=off for gcc and clang, /fp:precise for MSVC - and that is
not decoration. gcc and clang default to -ffp-contract=fast at -O2, so
on any build that has FMA available (-mfma, or an -march that implies
it) an a*b + c is fused, one rounding step disappears, and the whole
comparison moves.

Measured, by building the shipping stack twice - once with the flags
above and once with -O2 -mfma -ffp-contract=fast, which puts 111 fused
multiply-adds into swehouse.o alone - and dumping both at stride 1, flag
set trop: 3,062,802 of the 8,707,776 cusp positions differ (35.2%), by
up to 1.545e-07 degrees over the random block. On the degenerate block
only 826 of 133,056 move, but 48 of those slots move by exactly 180.0
degrees - six rows, every one of them Sunshine 'I', each sitting on a
singularity where one ULP of input decides which branch the formula
takes. Cusp speeds move by up to 2.3e-03 deg/day on the random block; on
the degenerate block they reach 4.096e+03 deg/day. None of that is a
tolerance the harness could absorb - its positional claim is
bit-identity - and FMA also perturbs the sampled coordinates themselves,
so the two builds would not even be asked the same question. harness.h
defends the sampler a second time with a volatile, so a caller who
compiles the harness by hand still visits the same points.

The two dumps above cannot be fed to compare: the ephemeris fingerprint
itself moves under FMA (DD11D7830BBBE321 against 737FE9E3BF1D39E4) and
compare exits 2 rather than attribute the difference to a patch. That is
the guard doing its job, and it is why the figures in this paragraph
were taken with a private one-off reader instead of by loosening
anything that ships.

On the x86-64 baseline ISA there is no FMA, so -ffp-contract=off changes
nothing and every number below is identical with and without it (the
dumps are byte-for-byte equal). The flag is there so that the result
does not depend on that accident.

Every mode takes an optional trailing stride. It subsamples the random
dates only - 1 is all 25,520 of them, 40 is every fortieth - and never
the 396 degenerate dates, which exist precisely to be visited. A stride
that is not a whole number in range is rejected with exit 2 rather than
silently treated as 1, which is what atoi("ten") used to do. Do not
compare figures taken at different strides: the counts scale with the
sample and the extremes do not. Every gate whose statement is a share is
computed separately over the random block and over the degenerate block,
so that the same threshold means the same thing at every stride.

All three modes are gates and all three report through their exit
status: 0 pass, 1 a check failed, 2 the harness could not run at all
(bad arguments, unwritable or unreadable dump, a dump that does not
describe the same sample set, flag set or ephemeris, or the same dump
twice).


What it samples

25,520 random dates drawn uniformly in Julian day between 1 January 1000
BC and 31 December 4100, a span of 5,100 years and about 1.86 million
days. Each carries a random geographic latitude in (-89.9, 89.9) and
longitude in [-180, 180), and runs through all 26 house letters.

The generator is splitmix64, written into harness.h in pure 64-bit
integer arithmetic with no library call in it. That is deliberate:
rand() differs between MSVC and glibc, so a harness that used it would
sample different points on Windows and Linux and the cross-platform
comparison would mean nothing.

396 degenerate dates follow, and they are the part a random sample can
never reach. The random block's closest approach to the equator is
8.58e-03 degrees and to a quadrant of armc 4.03e-04 degrees, so every
configuration these patches demonstrate - the equator to 5e-11 in demo
D2, armc at 270 in D1 - sat outside anything the harness proved. The
degenerate block is the cross product of:

  - latitude: 0, +-1e-11, the polar circle +-(90 - eps) and +-1e-9
    either side of it, and +-90;
  - armc: exactly 0, 90, 180, 270 and +-1e-9 around each;
  - obliquity: 23.4392911, 0 and 90.

Those rows carry their own armc and obliquity rather than deriving them
from a date, because no date produces armc exactly 270. dump reaches
them through swe_houses_armc_ex2() for the tropical flag set, which is
the only entry point that takes an armc; the other flag sets have no
such entry point, so there the degenerate rows contribute their latitude
alone. Both dumps in a comparison are held to the same flag set, so both
follow the same rule.

That has a cost worth stating. On rad, sid, sidt0 and sidssy the 396
degenerate rows all carry the same date and the same longitude and
differ only in latitude, so they collapse to 11 distinct configurations,
each computed 36 times. Measured, by counting distinct results for one
house letter over the 396 rows: 69 on trop, 11 on each of the other
four. The armc and obliquity degeneracies - armc exactly 270, an
obliquity of exactly 0 - are reached on the tropical flag set and on no
other, and every count those rows contribute to a non-tropical run is 36
copies of eleven answers rather than 396 independent ones.

Total: 25,916 dates x 26 letters = 673,816 calls per build per flag set,
about 8.6 million cusp values.

Beyond about 3000 AD the Sun is out of range. frame_of() then supplies
its own declination (0) rather than the 99 sentinel: 99 makes
swe_houses_armc_ex2() reuse a static double saved_sundec from whatever
call ran before it, so an 'I' result depended on the stride and on the
order the letters happened to run in, on 21.7% of the dates. The sun_out
count is still reported, and it is still the only reason the two
Sunshine letters are skipped in the entry-point comparison.


Five flag sets, not one

dump and claims cover all five:

  trop     no flags
    only way in: the ordinary path

  rad      SEFLG_RADIANS
    only way in: the radians conversion block, including both speed
      arrays

  sid      SEFLG_SIDEREAL, Fagan/Bradley
    only way in: sidereal_houses_trad()

  sidt0    + SE_SIDBIT_ECL_T0
    only way in: sidereal_houses_ecl_t0()

  sidssy   + SE_SIDBIT_SSY_PLANE
    only way in: sidereal_houses_ssypl()

Before this, the harness ran trop only, and claims exercised the
sidereal path for 'P' alone. Three whole functions and 25 of 26 letters
were never entered by any mode, which meant
swehouse-radians-speed-units.patch had zero coverage,
swehouse-whole-sign-zero-speeds.patch's sidereal_houses_trad() hunk had
zero coverage, and the two sidereal_houses_* functions that
swehouse-gauquelin-porphyry-fallback.patch modifies were never called.


Modes

dump and compare

dump writes a self-describing header - magic, format version, seed,
random and degenerate date counts, stride, system list, JD span, flag
set, sidereal mode, an ephemeris fingerprint and the final row count -
then one binary record per (date, house system): return code, cusp
count, a hash of the error string, then the cusps, the cusp speeds,
ascmc and ascmc_speed. Every write is checked, including the close.

compare refuses (exit 2) any file that carries no header, was never
finalised, whose row count does not match its stride, whose sample set,
flag set or ephemeris fingerprint differs from the other file's - or a
pair that is byte for byte identical. Two identical dumps compare
perfectly and establish nothing, and dumping the same binary twice is
the likeliest operator error there is; it used to read as a flawless
pass.

The maxprint argument is two budgets, not one: allowed examples and
unexplained ones are counted separately. With a single counter the
allowed differences - hundreds of thousands of them, and they come first
in row order - consumed the whole budget before the first unexplained
line was reached, so a FAILED run could print twenty perfectly fine
[allowed] examples and not one of the differences it had failed for.

Three things compare counts that a plain "did it change" comparison
cannot:

- a difference that is not finite, or one larger than 360 degrees.
  Wrapping such a difference into [0,180] - d = fabs(a-b); if (d > 180)
  d = 360 - d; - turns a cusp 12,345 degrees out into a negative number,
  and fabs(NaN - x) is NaN; neither ever exceeds a running maximum, so
  both used to be reported as "max 0.000e+00".
- a slot that is non-finite in both builds. Comparing with != calls that
  a difference forever, because NaN never equals itself; the comparison
  is on the bit patterns instead.
- index 0. cusp[0] and cusp_speed[0] are outside the documented range
  and both builds write a literal zero into them, so any difference
  there is unexplained by construction. cusp_speed[0] used to be dumped
  and then excluded from the comparison by a c >= 1 guard, which made it
  write-only.

The allowlist, and why it is not a tolerance

compare's exit status now includes ascmc and ascmc_speed. It did not,
and that was the single worst hole in the harness: a five-degree error
in the Ascendant printed "positions differing: 0 (bit-identical)" and
exited 0.

Adding them made compare red on the shipping stack, because the shipping
stack does change positions and ascmc_speed on purpose. Widening a
tolerance until it went green would have absorbed the five-degree
Ascendant as well. Instead every permitted difference must match a class
- a house letter, a region of the sample, and a bound - and each class
has a cap. A difference that fits no class is unexplained and fails,
however small; a class over its cap fails, however well-shaped. The
table is printed every run, so the allowlist cannot grow quietly.

The classes are enabled by --expect-apc-equator. Without it nothing is
permitted at all, which is the mode for checking that a change was
supposed to be inert.

Each class carries a count cap and, where its shape implies one, a
magnitude bound; both are gated, and a class over either fails. Five of
the caps are per date used (K x harness_ndates_used(stride)) because
their populations scale with the sample; the other four are absolute
because their classes live entirely in the 396 degenerate rows, which no
stride subsamples. An absolute cap on a scaling class is not a cap at
all: gauquelin-fill's old 400,000 was 24x the stride-2000 count, so past
about stride 700 it could not have been reached however broken the build
was.

apc-rounding
  shape:     'Y' position, |d| <= 1e-10 deg, anywhere
  count:     5,958 (worst 4.007e-11) at stride 1
  cap:       4/date
  bound:     1e-10 deg

apc-degenerate
  shape:     'Y' position on a degenerate row, |d| <= half a turn
  count:     508 (worst 179.4) at stride 1
  cap:       2,000
  bound:     180 deg

degnorm-360
  shape:     position, raw gap exactly 360 deg
  count:     0 at stride 1
  cap:       500
  bound:     shape only

gauquelin-fill
  shape:     'G' 13..36 unwritten in the baseline; on an ERR row the
             patched value must be exactly 0, otherwise finite and
             inside [0,360)
  count:     335,856 at stride 1
  cap:       48/date
  bound:     shape only

gauquelin-clear
  shape:     'G' index 13..36 on an ERR row, patched writes exactly 0
  count:     0 trop, 166,512 sidt0 at stride 1
  cap:       24/date
  bound:     shape only

pole-nan-both
  shape:     'I' within 1e-10 deg of +-90, non-finite in BOTH builds
  count:     576 at stride 1
  cap:       2,000
  bound:     shape only

pole-rounding
  shape:     position at geolat exactly +-90, |d| <= 1e-12 deg
  count:     96 (worst 1.53e-14) at stride 1
  cap:       500
  bound:     1e-12 deg

radians-ascmc-speed
  shape:     SEFLG_RADIANS only: ascmc_speed[0..7] == baseline *
             DEGTORAD
  count:     5,390,528 (rad) at stride 1
  cap:       208/date
  bound:     1e-15 rel

sidereal-ascmc-speed
  shape:     sidereal only: ascmc_speed[!=2] moved by the ayanamsa's own
             rate
  count:     4,716,712 (sid) at stride 1
  cap:       182/date
  bound:     1.0 of the allowance (worst 5.313e-02)

apc-degenerate is gated by its count and by nothing else, and its bound
is decoration. sepdeg() wraps into [0,180] by construction, so "|d| <=
half a turn" excludes no value that can ever be produced: the bound
column exists for the classes where it bites, and this is not one of
them. What the class really permits is therefore its cap's headroom -
2,000 against a clean count of 508 on the tropical flag set and 720 on
the sidereal ones - which is an absorption window of roughly 1,300 to
1,500 slots for arbitrary position changes of any size up to 180
degrees, on any degenerate row, in 'Y'. Measured: adding 20 degrees to
every 'Y' cusp on 44 degenerate rows (the four exact armc quadrants x
eleven latitudes) takes the class from 508 to 956, still inside the cap,
and compare exits 0 on all five flag sets. The degenerate block is where
the APC patch legitimately moves cusps by up to 180 degrees, so there is
no magnitude test available here; a tighter count is the only lever, and
2,000 was chosen before the class was measured at 508. Anything relying
on 'Y' degenerate positions should read the count, not the pass.

The two positional bounds are stated in degrees and converted when the
dump holds radians, because an unconverted bound is 57.3x looser there -
measured: a 'Y' cusp moved by 5e-10 degrees is unexplained on four flag
sets and was absorbed on the fifth for exactly that reason.

sidereal-ascmc-speed used to read "moved by <= 1e-3 deg/day", which is
ten times the largest rate the ayanamsa ever has on this sample: half a
thousandth of a degree a day added to the Ascendant's speed, with
nothing to do with any ayanamsa, sat comfortably inside it. What is
required now is that the difference be the rate - compare evaluates
d(ayanamsa)/dt itself, the same way swehouse.c does - with two
allowances, one relative (the rate's own evaluation) and one absolute
(subtracting two speeds that can reach 1e12 deg/day cannot resolve 1e-4
better than the operands' own ULP). The clean stack uses at most 5.3% of
that allowance.

Three of the five per-date caps are the class's structural ceiling, not
a measured number with headroom, and that is worth saying plainly.
radians-ascmc-speed reaches exactly 208 per date (26 letters x 8 slots)
and sidereal-ascmc-speed exactly 182 (26 x 7), because every one of
those slots does change; gauquelin-fill reaches 39.90 of a possible 48
(24 sectors x 2 arrays) at stride 2000, and gauquelin-clear 17.13 of a
possible 24. For those four the count cap can only ever say "no more
than every eligible slot"; what actually gates them is the shape test
each carries, which is why the shapes were tightened at the same time.
apc-rounding is the one with a real count margin, 3.5x at its worst
stride.

Cusp speeds are never unexplained - changing them is what the patch set
is for - but cusp_speed[0], a speed the baseline filled and the patched
build does not, and a speed non-finite on one side only all still fail.

Two of those classes are findings rather than concessions.
gauquelin-clear under SEFLG_RADIANS catches the baseline handing back
-1.55e+87 for cusp[13..36]: it never wrote them, and then multiplied the
caller's memory by DEGTORAD on the way out. pole-nan-both is described
under "waivers" below.

deriv

deriv checks each reported cusp speed against a Richardson-extrapolated
central difference of the cusp position. It needs no second build,
because it checks the library against its own positions.

The reference differentiates with respect to armc at fixed obliquity and
fixed solar declination, through swe_houses_armc_ex2(). That is
deliberate: the analytic speeds are defined the same way, and the
date-based entry point would also move the obliquity and, for Sunshine,
the Sun, adding terms the speeds never claimed to carry.

Every comparison is either judged or counted into a column that is
itself gated - which is a weaker statement than "every comparison is
judged", and the weaker one is the true one. At stride 1, 8,603,976
comparisons are judged against a threshold; 78,842 sit outside the
domain of the formulae and are held only to the share of them that still
agrees; and 21,958 are given up as an unconverged reference and held
only to the share of the block they may consume. Those last two are real
gates, and until this revision the 21,958 were held to nothing whatever.
Do not read the sentence as "everything is checked to 1e-8".

There are three judged classes, chosen by the reference's magnitude, and
all three are gated:

abs(ref) < 1 deg/day
  judged on: absolute error
  gate:      worst <= 5e-4 deg/day

1 <= abs(ref) <= 1e4
  judged on: relative error
  gate:      worst <= 1e-3, mean per letter <= 1e-8

abs(ref) > 1e4
  judged on: the ratio got/ref
  gate:      worst abs(got/ref - 1) <= 1e-5

The steep class used to be an uncounted continue, and a build that
multiplied every speed above 10,000 deg/day by 1.5 walked through
untouched. It is judged now because the convergence test was moved ahead
of it: a steep reference that has proved itself convergent is a
reference, and one that has not is rejected and counted.

The steep class is empty at stride >= 100, and a strided run therefore
does not exercise that gate at all. Measured: 244 comparisons at stride
1, 20 at stride 5, 13 at 10, and 2 or 3 at every stride from 20 to 80;
at 100, 150, 200, 500 and 2000 there are none. The verdict prints "gate
not exercised at this stride (0 comparisons)" in that case rather than
an implicit pass, which is what it used to print - a gate reading "pass"
over zero comparisons is the most flattering possible way to report that
nothing was looked at. Anything relying on the steep gate must be run at
stride 1.

A reference still has to earn the right to judge. These classes are
declined, counted, and gated as a share, because a gate that quietly
skips the hard points reads as "all clear" when it is really "not looked
at" - and a whole letter used to be able to vanish into one of them
(73.9% and 100% were both observed) with the verdict still reading pass:

  the two step sizes still disagree after halving the step 4 times
    gate:      <= 1% of a letter's random rows, <= 25% of its degenerate
               rows

  the five calls disagree on a return code
    gate:      <= 2% random, <= 5% degenerate

  a reference cusp is not finite
    gate:      <= 2% random, <= 5% degenerate

  a reference cusp the library never wrote
    gate:      0, always

  a letter with nothing judged at all
    gate:      0, always

  a whole DATE dropped before any letter is walked - 'P' gave no usable
  armc, or SE_ECL_NUT failed
    gate:      0, always

That last row is a date-level drop rather than a comparison-level one,
and it was the last thing in the harness that was counted, printed and
gated by nothing. It matters more than its size suggests: poisoning the
frame probe for abs(geolat) > 60 drops 8,459 dates, 32.6% of the sample
and every polar row in it, and both deriv and claims exited 0 - because
a bucket that loses all its comparisons is not a small bucket, it is no
bucket, so the tiny-bucket share still read 0.0000%. Neither reason ever
fires on the shipping stack at any stride, so the gate is zero rather
than a share.

Every comparison lands in exactly one column, and an unaccounted column
proves it: judged plus every rejection must equal what was attempted,
and that column has to read zero.

deriv also gates the positions, which it never did: an uninitialised or
non-finite cusp[c] is now a failure in its own right, so a build
returning NaN cusps beside matching NaN speeds can no longer be "judged
0" and pass.

Why a mean is not enough, and what replaced it

The old gate was the mean relative error per house letter, over about
306,000 comparisons. An error confined to a latitude band is diluted by
that band's share of the sample. The real historical Placidus defect -
+9.167e-4 deg/day on cusps 11 and 12 between 60 and 70 degrees of
latitude - touches 1.85% of 'P''s comparisons, so the mean sees about a
fiftieth of it. Any mean-based gate dilutes a band-local error by the
reciprocal of that band's share, and this one is no exception. Two more
statistics were added rather than replacing it:

  - a worst, which catches anything large at a single point (a cusp
    pinned to zero at armc exactly 270, say) but is deaf to a systematic
    error smaller than the sample's own near-singular outliers;
  - a median per (letter, cusp, latitude band) bucket, which is the one
    that makes a small band-local error visible. It cannot be moved by
    the handful of points that set the worst, and it moves the moment
    most of a bucket shifts. Buckets are histogrammed in half-decade
    bins and the number gated is the bin's upper edge, so the reported
    median over-estimates and never flatters the library. Buckets under
    16 comparisons are not gated - a median of three points is one point
    - but the share of comparisons living in them is, so a defect cannot
    be hidden by scattering it into small buckets.
  - the 80th and 90th percentiles of the same buckets, because a median
    needs most of a bucket to move. A defect confined to part of one -
    an armc range, one branch of a conditional, one season - leaves the
    median exactly where it was and lifts the tail instead. Measured,
    over 92 strides from 1 to 25,520, the clean stack's worst bucket p80
    never exceeds 3.162e-08 and its worst p90 never exceeds 1.000e-07;
    the gates stand at 1e-7 and 1e-6. A uniform x(1+2e-7) on one cusp
    over a third of a bucket reads 3.162e-07 at both percentiles - over
    the p80 gate, under the p90 one, which is why both are here and not
    just the one the audit asked for. The p90 is the noisier statistic
    and buys reach (a defect on a tenth of a bucket); the p80 is quieter
    and buys margin.

The band width follows the sample: eighteen 10-degree bands at stride 1,
and fewer as the sample shrinks, so that every bucket still expects at
least 64 comparisons. Fixing it at eighteen made stride 200 fail on the
tiny-bucket share alone - 48% of its comparisons in ungated buckets -
which is a real stride dependence and not a reason to stop gating. A
coarser banding dilutes a band-local error more, so a strided run is a
weaker test than stride 1; every certified figure here is stride 1.

The metric all three use is abs(got - ref) / max(abs(ref), 1): the
relative error above one degree per day and the absolute error below it,
which is exactly the split the two judged classes already made.

The sensitivity floor, per house letter

The largest median of any one bucket, on the shipping stack at stride 1.
A systematic error confined to one cusp above one latitude has to exceed
its letter's number here to move a median at all; the gate stands at
1e-8, which is between 10 and 3e11 times higher than every one of them.
(It stood at 1e-7 and was tightened by one decade: both the old value
and the new one are half-decade bin edges and the comparison is <=, so
nothing about the reported numbers changed, only what they are held to.
A uniform x(1+2e-8) on one cusp reads 3.162e-08 and now fails; at 1e-7
it did not.)

  A 1.000e-09   B 1.000e-09   C 3.162e-10   D 1.000e-10
  E 1.000e-09   F 1.000e-09   G 1.000e-09   H 1.000e-09
  I 3.162e-10   i 3.162e-10   J 3.162e-10   K 3.162e-10
  L 3.162e-10   M 1.000e-10   N 3.162e-20   O 3.162e-10
  P 1.000e-09   Q 3.162e-10   R 3.162e-10   S 3.162e-10
  T 3.162e-10   U 1.000e-09   V 1.000e-09   W 3.162e-20
  X 1.000e-10   Y 3.162e-10

'N' and 'W' read 3.162e-20 because their cusps stand still and the
library reports exactly zero for them; the metric is an absolute error
there, and it is a literal 0.0 on all 307,392 'N' and all 306,672 'W'
comparisons that reach the small class. Those two letters are also the
reason the abs(ref) < 1 class exists: judged relatively, every one of
those comparisons would be discarded, both letters would read "0
judged", and the 12,385 deg/day the unpatched library reports for a cusp
that does not move would pass.

Read the floor as: a defect uniform over a (cusp, band) bucket is caught
if it exceeds 1e-8 relative - or 1e-8 deg/day where the cusp barely
moves. A defect over a third of such a bucket is caught at 1e-7 by the
80th percentile, and over a tenth of one at 1e-6 by the 90th. A defect
uniform over a whole letter is caught at 1e-8 relative (the mean gate
over the random rows). A defect at a single point is caught at 1e-3
relative or 5e-4 deg/day (the worst gates). A defect that only fires at
a degeneracy is caught at 1e-7 by the degenerate-block mean.

The blindness window between the mean and the percentiles

The two statistics do not meet, and the gap is written down here rather
than left to be found. A speed defect confined to 10-20% of one bucket
escapes everything when both of these hold:

  - its binned percentile reading stays at or under the p90 gate, 1e-6;
    and
  - share x magnitude / 12 stays under the per-letter mean gate, 1e-8 -
    the twelfth because a defect on one cusp is diluted by the twelve
    that make up a letter's mean.

At a 15% share the second condition permits any magnitude up to about
7e-7. Measured, on a probe of cusp_speed[5] x (1+2.5e-7) applied where
armc < 54 (15% of the range): mean 4.162e-09 against 1e-8, bucket median
3.162e-09 against 1e-8, p80 1.000e-07 against 1e-7 - equal to the gate,
and therefore passing on <= - and p90 3.162e-07 against 1e-6. deriv,
claims and all five compare pairs exit 0. The p90 sees it; its gate sits
one bin above the reading. Closing this needs either a finer bin than
half a decade or a percentile between the 50th and the 80th, and both
cost margin elsewhere, so it is documented rather than quietly narrowed.

Gate stability across strides. deriv and claims both exit 0 on the
shipping stack at strides 1, 4, 20, 40, 200, 2000 and 25,520 - the last
of which leaves 2 random dates beside the 396 degenerate ones. That is
the test that a threshold means the same thing at every sample size; two
gates failed it before the random and degenerate blocks were separated.
The two bucket-tail thresholds were set from a 92-stride sweep rather
than from stride 1, because they are the statistics that move most with
the sample: the worst bucket p80 is 1.000e-08 at stride 1 but reaches
3.162e-08 elsewhere, and the worst p90 is 3.162e-08 at stride 1 but
reaches 1.000e-07 at fourteen of the 92 strides tried. The gates, 1e-7
and 1e-6, are above the sweep's ceiling and not above stride 1's
reading.

claims

claims covers what needs two calls rather than two builds:

- the two entry points, swe_houses_ex2() against swe_houses_armc_ex2(),
  in the speeds as well as the positions - the previous version compared
  positions only.
- the Porphyry fallback, for all 26 letters and all five flag sets.
  Every system that fails substitutes Porphyry, so an ERR row's twelve
  cusps and speeds must equal what 'O' returns for the same call, and
  'G''s sectors 13..36 must be a literal zero. This is the only check
  that looks inside sidereal_houses_ecl_t0() and sidereal_houses_ssypl()
  at all. The previous version asked it of 'I' and 'i' only.
    When the check's own 'O' call fails there is no reference to compare
    against and the row is abandoned. That was an unlabelled return, so
    a change that made Porphyry fail everywhere would have emptied this
    backstop of every row it checks and still printed "0 of 0 failed".
    Those rows are counted, printed and gated at zero now - counting
    them was only half the fix, because nothing read the counter. 0 on
    the clean stack, on every flag set. claims also gates the date-level
    frame drop described under deriv, at zero, for the same reason: a
    date without a frame is walked by none of the 26 letters and none of
    the five flag sets, and every count in the mode simply gets smaller.

- SEFLG_RADIANS: every returned value, positions and both speed arrays,
  must equal the degree answer times DEGTORAD. Measured worst relative
  difference over 28,196,608 values: exactly 0.
- the sidereal identity, for all 26 letters:

      sidereal position = tropical position - ayanamsa(t)
      sidereal speed    = tropical speed   - d(ayanamsa)/dt

    first against the rate swehouse.c is defined to subtract - its own
    central difference at 0.1 day, matched to 1e-10 deg/day, which says
    the subtracted term is that derivative - and then against a
    Richardson extrapolation over 0.5 and 0.25 day, which does not
    restate the library's arithmetic and so cannot be fooled by a defect
    living in the 0.1-day expression itself. The ayanamsa is not smooth
    everywhere and the wider stencil straddles a break on a small share
    of dates, so that second side gates on the share of comparisons
    outside 1%, not on any single one.

    'W' and 'N' are held to the other half of the claim: their cusps are
    pinned to sidereal sign boundaries, so their sidereal speeds must be
    exactly zero. That is the entire content of the whole-sign hunk in
    sidereal_houses_trad(), and while only 'P' was tested nothing looked
    at it.

    sidereal_houses_trad() upper-cases the letter before it computes, so
    the sidereal answer for 'i' is the sidereal answer for 'I'. The
    identity holds sidereal 'i' against tropical 'I' for that reason;
    held against tropical 'i' it would fail on every row and say nothing
    about the ayanamsa.

- every ascmc_speed against a Richardson-extrapolated derivative of its
  ascmc, for indices 0 to 9. Three holes are closed here. A reference
  below one degree per day used to be skipped outright - the same blind
  spot the cusp gate had - and is judged on absolute error. Index 2,
  armc, used to be skipped as "armc itself" and is held against the
  exact constant ARMCS (360.9856473350), bit for bit. And the loop
  stopped at 8, so ascmc_speed[8] and [9], which swehouse.c sets to a
  literal zero, were never looked at; they are held at exactly zero now.

    The two ways this check can decline a comparison - a reference out
    of range and a reference that has not converged - were printed and
    gated by nothing, which made them the last ungated continue in the
    harness: a change that pushed every ascmc reference out of range
    would have emptied the check and still read pass. Both are now
    shares of the block they occurred in, random and degenerate held
    separately as everywhere else. Clean: 12 of 178,640 random and 384
    of 2,772 degenerate out of range (gates 1% and 40%), and 0 of each
    unconverged (gates 1% and 5%).

- uninitialised and non-finite values in all four arrays, on all five
  flag sets. Every output slot is filled with a sentinel before every
  call; if one comes back, the library left it untouched and the caller
  would have read uninitialised stack. Scanning only cusp_speed is why
  this mode once saw 24 of the 48 slots 'G' leaves unwritten.


Two documented waivers, and one class held to a different statement

The harness would be dishonest if it pretended the shipping stack is
clean where it is not. Three things are exempted from the ordinary
gates, all three by shape and never by magnitude alone, all three
printed every run:

1. Sunshine at a pole.
   'I' (not 'i') within about 1e-10 degrees of +-90 returns NaN for four
   cusps and their four speeds and still reports OK. Measured identical
   in the pristine v2.10.3bfinal tree and in the patched one, so nothing
   here causes it and nothing here fixes it. Waived by letter, latitude
   and NaN-in-both-builds; any other non-finite value, in any other
   letter or at any other latitude, still fails. Without the degenerate
   block the sample never reached a pole and this never appeared -
   refusing to add the block would have been the cheaper way to keep the
   table clean.

2. Positions at a pole that differ by 1e-14 degrees.
   'i' at geolat exactly +-90 returns 5.09e-15 in one build and a
   literal 0 in the other. 96 slots, worst 1.53e-14 degrees, capped at
   500.

3. Outside the domain of the formulae.
   At a geographic pole, at an obliquity of exactly 0 or exactly 90, and
   where the horizon is exactly tangent to the ecliptic (abs(geolat) +
   eps == 90 with armc at 90 or 270), a house cusp has no derivative:
   the positions are discontinuous there. Measured: at geolat = 90 - eps
   and armc = 270 the reported 'Y' speed and the numerical difference
   disagree by 78%, and one nanodegree of latitude away they agree to
   6e-11. Those rows are still sampled, still computed, and their slots,
   finiteness and return codes are gated exactly as any other row's.
   What is held to a different statement is the derivative agreement,
   and what is gated there is the share that still agrees - 77,980 of
   78,842 (98.91%) against a floor of 95%. A change that broke the
   formulae generally would drive that share down; a change that only
   fails at the singular point cannot hide in there, because the
   singular point is one row and the share is over all of them.

   The floor was 50%, which left 48 points of slack nothing could ever
   use: a build that multiplies every speed by 1.5 wherever the
   obliquity is exactly zero drives the share to 50.69% and cleared 50%
   by two thirds of a point. These rows are the whole degenerate block
   and are never subsampled, so 98.91% is the entire population rather
   than a sample - it reads identically at every stride, and 95% leaves
   3.91 points against a measured 1.09 of disagreement. claims asks the
   same question of its ascmc references, gets 96.27% rather than
   98.91%, and therefore has a floor of its own at 90% instead of
   borrowing one measured somewhere else.

   The denominator of that share is gated now as well. 21,958
   comparisons - 21.78% of the block - are given up as an unconverged
   reference, and nothing looked at that number at all: a build could
   have driven the whole block into that column, left three comparisons
   behind, and passed the agreement gate on those three. The ceiling is
   40%.

   What the floor cannot see. A share gate is a budget, and this one has
   3,080 comparisons of it unspent: the floor permits 5% of 78,842 =
   3,942 disagreements and the clean stack uses 862. Measured, by
   multiplying the cusp speeds of n outside-domain rows by 1.5, each
   corrupted row costs about 288 comparisons, so a defect confined to
   about ten of the 396 degenerate rows fits inside the slack and is not
   caught here. Eleven rows is where it bites: 74,812 of 78,842 agree,
   94.89% against the 95% floor - 4,030 disagreements against a
   permitted 3,942, over by 88 comparisons, a margin of 1.02x. That is
   the gate working at its designed resolution and not a hair more. A
   defect on fewer rows than that has to be caught by something else,
   and usually is: the same x1.5 applied to eleven rows inside the
   domain is caught by the degenerate-block mean at 3.646e-02 against
   1e-7 and by the worst relative at 5.000e-01 against 1e-3.


Reproducibility

- Ephemeris fingerprint. Every dump header carries a hash of
  swe_version() and of the Sun at four fixed epochs, taken through the
  same public call the harness uses. compare refuses (exit 2) a pair
  whose fingerprints differ. Measured drift with a SE_EPHE_PATH holding
  the Swiss files against a build that falls back to Moshier: 208 of
  1,664 rows. Comparing those two dumps attributes a data difference to
  the patch.
- Seed and fingerprint in the headers. deriv and claims print both at
  the top of their output, not only in the usage text, so a saved run
  says what produced it.
- Same dump twice. Refused, exit 2.
- Flag set. Recorded in the header; a mismatched pair is refused.
- Bad stride. Refused, exit 2. atoi("ten") is 0.
- demos.sh runs bash with set -o pipefail. Every demonstration is
  swetest ... | sed, so without it the exit status was sed's and a
  swetest that failed to run at all rendered as an empty "before" block
  beside a full "after" one - the most flattering possible way to
  display a crash.


Results, shipping stack against pristine v2.10.3bfinal

swehouse-gauquelin-porphyry-fallback.patch,
swehouse-sidereal-ayanamsa-rate.patch,
swehouse-analytic-cusp-speeds.patch and swehouse-apc-equator-fix.patch,
in that order. (The guard correction described under "What it has found"
below is in the shipped sidereal patch; nothing needs applying by hand.)
Stride 1, gcc 13.3.0; clang 18.1.3 produces byte-identical dumps.

The shipped set has a fifth patch, swehouse-sunshine-error-path.patch,
and everything below is four-patch figures. They are also the five-patch
figures: applying it on top and dumping again gives byte-identical files
on all five flag sets at strides 1 and 200 (compared with cmp - compare
refuses an identical pair on principle), and the five-patch tree passes
deriv and claims unchanged. It hardens an error return this sample never
reaches, which is a fact about the sample rather than a reason to leave
the patch out of a build.

  deriv:  passed        claims: passed        compare: passed
  (all five flag sets)

derivative, worst mean relative error, random rows
  reading:   1.081e-09 ('Y')
  gate:      1e-8
  margin:    9.3x

derivative, worst mean relative error, degenerate rows
  reading:   2.144e-08 ('G')
  gate:      1e-7
  margin:    4.7x

derivative, worst relative error
  reading:   1.682e-04 ('Y')
  gate:      1e-3
  margin:    5.9x

derivative, worst absolute error where abs(ref) < 1
  reading:   1.716e-04 deg/day ('Y')
  gate:      5e-4
  margin:    2.9x

derivative, worst ratio where abs(ref) > 1e4
  reading:   2.000e-09 ('Q'), over 244 comparisons
  gate:      1e-5
  margin:    5000x

derivative, worst bucket median
  reading:   1.000e-09
  gate:      1e-8
  margin:    10x

derivative, worst bucket 80th percentile
  reading:   1.000e-08
  gate:      1e-7
  margin:    10x

derivative, worst bucket 90th percentile
  reading:   3.162e-08
  gate:      1e-6
  margin:    32x

comparisons in ungated (under-16) buckets
  reading:   0 of 8,603,976
  gate:      2%
  margin:    -

outside-domain comparisons agreeing to 1e-6
  reading:   77,980 of 78,842 (98.91%)
  gate:      >= 95%
  margin:    4.6x on the disagreeing side

outside-domain comparisons unconverged
  reading:   21,958 of 100,800 (21.78%)
  gate:      <= 40%
  margin:    1.8x

claims ascmc outside the domain
  reading:   1,704 of 1,770 agree (96.27%)
  gate:      >= 90%
  margin:    2.7x on the disagreeing side

claims ascmc reference out of range
  reading:   12 of 178,640 random, 384 of 2,772 degenerate
  gate:      <= 1% / <= 40%
  margin:    150x / 2.9x

claims ascmc reference unconverged
  reading:   0 of 178,640 random, 0 of 2,772 degenerate
  gate:      <= 1% / <= 5%
  margin:    -

uninitialised speed / position slots
  reading:   0 / 0
  gate:      0
  margin:    -

non-finite speeds / positions
  reading:   0 / 0
  gate:      0
  margin:    -

letters with nothing judged
  reading:   0
  gate:      0
  margin:    -

unaccounted comparisons
  reading:   0
  gate:      0
  margin:    -

dates dropped by the frame probe, deriv / claims
  reading:   0 / 0 (0 no armc, 0 no obliquity)
  gate:      0
  margin:    -

fallback rows with no 'O' reference
  reading:   0 over all five flag sets
  gate:      0
  margin:    -

entry points, worst cusp difference
  reading:   6.704e-09 deg ('I'), 1 row of 652,466 over the 1e-9
             reporting line
  gate:      1e-7
  margin:    15x

entry points, worst speed difference
  reading:   7.388e-09 relative ('B')
  gate:      1e-5
  margin:    1354x

SEFLG_RADIANS = degrees x DEGTORAD
  reading:   worst 0.000e+00 over 28,196,608 values
  gate:      1e-15
  margin:    exact

Porphyry fallback carried, all 5 flag sets
  reading:   0 of 170,037 failed
  gate:      0
  margin:    -

sidereal identity, positions
  reading:   worst 0.000e+00 deg
  gate:      1e-9
  margin:    exact

sidereal identity, speeds vs swehouse.c's own d/dt
  reading:   worst 1.766e-11 deg/day, 0 over the line
  gate:      1e-10
  margin:    5.7x

sidereal identity, speeds vs an independent d/dt
  reading:   131,904 of 7,924,200 outside 1% (1.665%)
  gate:      5%
  margin:    3x

'W'/'N' sidereal speeds pinned to zero
  reading:   0 of 621,984 nonzero
  gate:      0
  margin:    -

ascmc_speed[2] against the exact ARMCS
  reading:   0 disagreed of 25,916
  gate:      0
  margin:    -

ascmc_speed[8], [9] held at exactly zero
  reading:   0 disagreed of 25,916
  gate:      0
  margin:    -

ascmc_speed worst relative
  reading:   4.288e-08
  gate:      1e-6
  margin:    23x

ascmc_speed worst absolute where abs(ref) < 1
  reading:   4.557e-08 deg/day
  gate:      1e-4
  margin:    2195x

rows compared per flag set
  reading:   673,816 (25,916 dates x 26 systems)
  gate:      -
  margin:    -

positions differing, trop
  reading:   6,562, all explained by a class
  gate:      -
  margin:    -

cusp speeds differing, trop
  reading:   4,570,863 fields, on 17 of 26 letters
  gate:      -
  margin:    -

return codes, error strings differing
  reading:   0
  gate:      0
  margin:    -

differences at index 0
  reading:   0 and 0
  gate:      0
  margin:    -



What it has found

- A real defect in a draft of swehouse-sidereal-ayanamsa-rate.patch,
  since fixed in the shipped patch. Its guard read "if (retc >= 0 &&
  dayan != 0)", so on every row where the house system fell back to
  Porphyry - 26.1% of dates for 'P' - the ayanamsa was subtracted from
  the positions by the loop above it but its rate was never subtracted
  from the speeds. That is the exact defect the patch exists to remove,
  surviving on a quarter of the sample. Two independent gates caught it:
  sidereal identity, rate(exact) at 242,388 of 7,924,200 comparisons,
  and Porphyry fallback carried at 20,199 of 170,037 rows, because the
  same row's 'O' call succeeds and therefore does get the rate
  subtracted. Correcting the guard to "if (dayan != 0)" turned both
  green; the shipped patch carries that correction, and additionally
  zeroes the speed arrays before the call so that the one early return
  that writes nothing - the Sunshine declination check - is read as
  defined zeros rather than caller memory.
- A sign flip in the analytic Makransky speeds, at w exactly 90, 180 and
  270. On the equator the ascensional difference vanishes, so the arcs
  are exactly 90 degrees and every armc that is a multiple of 30 lands
  three of the twelve cusps on one of those values. Fixed in the patch.
- A cusp 180 degrees wrong in the shipped library.
  sunshine_solution_makransky() tests "w > 180 && w < 270" with a strict
  bound, so w == 180 falls through to the fourth-quadrant branch and
  returns 360 where both neighbours return 180. Present in unpatched
  v2.10.3bfinal. Fixed by swehouse-makransky-w180.patch.
- 'G' leaves 24 positions and 24 speeds uninitialised when Gauquelin
  sectors fall back to Porphyry inside the polar circle, and - through
  sidereal_houses_ecl_t0() and sidereal_houses_ssypl(), which shift them
  by the ayanamsa on the way out - hands the caller its own stack
  dressed up as a plausible cusp. Under SEFLG_RADIANS it multiplies that
  stack by DEGTORAD first. Present in unpatched v2.10.3bfinal; fixed by
  swehouse-gauquelin-porphyry-fallback.patch.
- 'N' and 'W' reporting speeds of up to 12,385 degrees per day for cusps
  whose reference derivative is exactly zero. deriv could not see this
  for as long as the abs(ref) < 1 class was discarded instead of judged:
  both letters read "0 judged" and the gate passed.
- Sunshine returns NaN at a geographic pole and reports OK. Upstream's,
  waived by shape, described above.


The gates were checked against deliberately broken libraries

A gate that has never failed has not been shown to work. Two audits have
now been run against this harness, and the second one is the reason for
the gate changes described above, so read the first table as history and
the second as the current state.


First audit

60 faults seeded into the patched swehouse.c, 23 undetected. All 23 are
re-run below against the harness of the day. Every one is caught, by a
named gate, with its margin. deriv runs at stride 1, claims and the five
dump/compare pairs at stride 4. Two of the gates named in that table
have since been tightened - the bucket median from 1e-7 to 1e-8 and the
steep ratio from 0.25 to 1e-5 - so the gate and margin columns below are
stated against the current thresholds while the reading column is what
was measured at the time. Both changes move the margins the same way.

placidus_3as
  shape:     +9.167e-4 deg/day on 'P' 11,12, lat 60-70 - the real
             historical bug
  caught by: deriv bucket median
  reading:   1.000e-05 ('P' cusp 12, lat -70..-60)
  gate:      1e-8
  margin:    1000x

  caught by: deriv mean per letter, random rows
  reading:   6.391e-08 ('P')
  gate:      1e-8
  margin:    6.4x

  caught by: claims Porphyry fallback
  reading:   1,446 of 44,096 rows
  gate:      0
  margin:    -

placidus_33as
  caught by: deriv bucket median
  reading:   1.000e-04
  gate:      1e-8
  margin:    1e4x

  caught by: deriv worst relative
  reading:   3.308e-03
  gate:      1e-3
  margin:    3.3x

steep_only
  shape:     x1.5 on every speed above 1e4 deg/day
  caught by: deriv steep ratio
  reading:   5.000e-01 ('I'), 244 comparisons
  gate:      1e-5
  margin:    5e4x

refnan_K
  shape:     'K' reference cusps NaN
  caught by: deriv non-finite-reference share
  reading:   1 letter over 2% (100% of 'K')
  gate:      0 letters
  margin:    -

  caught by: deriv letters with nothing judged
  reading:   1
  gate:      0
  margin:    -

rcmix_K
  shape:     'K' reference calls return ERR
  caught by: deriv rc-mix share
  reading:   1 letter over 2%, 1 over 5%
  gate:      0 letters
  margin:    -

radians_break
  shape:     radians speed factor x2
  caught by: claims SEFLG_RADIANS conversion
  reading:   2,072,424 of 7,372,288
  gate:      0
  margin:    -

sid_W_revert
  shape:     whole-sign sidereal clamp removed
  caught by: claims 'W'/'N' pinned to zero
  reading:   81,312 nonzero
  gate:      0
  margin:    -

sid_notP
  shape:     ayanamsa rate x1000 for every letter but 'P'
  caught by: claims sidereal rate(exact)
  reading:   1,991,112 of 2,072,424
  gate:      0
  margin:    -

  caught by: claims Porphyry fallback
  reading:   1,737 of 44,096
  gate:      0
  margin:    -

ecl_t0_revert
  shape:     +5 deg on cusp[1] in sidereal_houses_ecl_t0
  caught by: claims Porphyry fallback
  reading:   6,767 of 44,096
  gate:      0
  margin:    -

  caught by: compare sidt0 unexplained
  reading:   6,767
  gate:      0
  margin:    -

ascmc_pos_5
  shape:     Ascendant +5.0
  caught by: compare, all 5 flag sets
  reading:   176,176 unexplained
  gate:      0
  margin:    -

ascmc_pos
  shape:     Ascendant +1e-6
  caught by: compare, all 5 flag sets
  reading:   176,176 unexplained
  gate:      0
  margin:    -

ascmc_armc
  shape:     ascmc_speed[2] x1.5
  caught by: claims ascmc_speed[2] vs ARMCS
  reading:   6,776 disagreed
  gate:      0
  margin:    -

  caught by: compare, all 5 flag sets
  reading:   176,176 unexplained
  gate:      0
  margin:    -

ascmc89
  shape:     ascmc_speed[8],[9] set to 7
  caught by: claims ascmc_speed[8],[9] zero
  reading:   13,552
  gate:      0
  margin:    -

  caught by: compare, all 5 flag sets
  reading:   352,352 unexplained
  gate:      0
  margin:    -

armc270
  shape:     cusp_speed[3] = 0 at armc == 270.0
  caught by: deriv worst relative
  reading:   1.000e+00 ('A')
  gate:      1e-3
  margin:    1000x

  caught by: deriv mean per letter, degenerate rows
  reading:   6.481e-03 ('H')
  gate:      1e-7
  margin:    6.5e4x

  caught by: compare trop unexplained
  reading:   3
  gate:      0
  margin:    -

armc270w
  shape:     same, within 1e-6 of 270
  caught by: deriv worst relative
  reading:   1.000e+00
  gate:      1e-3
  margin:    1000x

  caught by: deriv mean per letter, degenerate rows
  reading:   1.944e-02 ('H')
  gate:      1e-7
  margin:    1.9e5x

  caught by: compare trop unexplained
  reading:   9
  gate:      0
  margin:    -

equator
  shape:     cusp_speed[3] = 0 at abs(geolat) < 1e-6
  caught by: deriv worst relative
  reading:   1.000e+00
  gate:      1e-3
  margin:    1000x

  caught by: deriv mean per letter, degenerate rows
  reading:   3.571e-02 ('F')
  gate:      1e-7
  margin:    3.6e5x

polar
  shape:     same, at the polar circle
  caught by: deriv worst relative
  reading:   1.000e+00
  gate:      1e-3
  margin:    1000x

  caught by: deriv mean per letter, degenerate rows
  reading:   5.556e-02 ('H')
  gate:      1e-7
  margin:    5.6e5x

lat65_1em5
  shape:     cusp_speed[3] x(1+1e-5) above lat 65
  caught by: deriv bucket median
  reading:   3.162e-05 ('A' cusp 3, lat 70..80)
  gate:      1e-8
  margin:    3162x

  caught by: deriv mean per letter, random rows
  reading:   1.179e-07 ('P')
  gate:      1e-8
  margin:    12x

Four of the five faults the audit called legitimately below the floor
are now caught too, which was not the aim but is worth recording:

const_1em4
  shape:     +1e-4 deg/day on cusp_speed[5]
  outcome:   caught, deriv bucket median
  reading:   3.162e-04 ('N' cusp 5) vs 1e-8

const_1em5
  shape:     +1e-5 deg/day
  outcome:   caught, deriv bucket median
  reading:   3.162e-05 vs 1e-8 (the mean has only 1.019e-8 vs 1e-8)

ascmc_spd_1em7
  shape:     ascmc_speed[3] x(1+1e-7)
  outcome:   caught, compare
  reading:   175,864 unexplained on trop

rel_1em9
  shape:     every cusp speed x(1+1e-9)
  outcome:   escapes, as expected
  reading:   bucket medians 1e-9, floor is 1e-9 to 3.16e-10

rel_1em12
  shape:     x(1+1e-12)
  outcome:   escapes, as expected
  reading:   four decades under the floor

The two that escape are below the harness's own numerical floor and no
threshold could separate them from it. The floor is the per-letter table
above: the numerical difference's own error against the closed form is
1e-9 to 3.16e-10 relative on the letters that move, so a uniform
relative perturbation of 1e-9 is indistinguishable from the measuring
instrument and 1e-12 is three decades inside it. compare does not see
them either, because a cusp speed changing is what the patch set is for.
To catch a uniform 1e-9 relative shift the reference would have to be an
independent closed form rather than a difference of the library's own
positions, which is a different instrument, not a tighter threshold.

Second audit: 31 mutants, 9 escapes

Do not read the section above as closure. A second, independent audit
ran 31 live seeded mutants against the harness as it then stood: 22 were
caught and 9 escaped. The nine are listed below with what now catches
them. Every reading in the table is measured - deriv at stride 1, claims
and the five dump/compare pairs at stride 20.

The "before" column is measured as well, but on these reproductions
rather than on the audit's own seeded code: patch/ is git-excluded here,
so the pre-fix harness was reconstructed by reversing each threshold and
each shape test one at a time, and that reconstruction still passes the
clean stack, which is what makes the comparison fair. Seven of the nine
escape it exactly as described. M22 and M28, written into
swe_houses_armc_ex2() where every entry point sees them, escape it only
on the tropical flag set - which is the hole the audit named - and are
caught elsewhere by gates that already worked.

M1
  shape:     x1.2 on every speed above 1e4 deg/day
  before:    escaped all three modes
  caught by: deriv steep ratio
  reading:   2.000e-01 ('I'), 244 comparisons
  gate:      1e-5
  margin:    2e4x

M2
  shape:     cusp_speed[5] x(1+5e-8) everywhere
  before:    escaped all three modes
  caught by: deriv bucket median
  reading:   1.000e-07 ('A' cusp 5, lat -90..-80)
  gate:      1e-8
  margin:    10x

M4
  shape:     'Y' cusp[5] += 5e-10 deg where abs(lat) > 80
  before:    escaped all three modes
  caught by: compare, all 5 flag sets
  reading:   140 unexplained
  gate:      0
  margin:    -

M7
  shape:     every speed x1.5 where eps == 0.0 exactly
  before:    escaped all three modes
  caught by: deriv outside-domain agreement
  reading:   39,964 of 78,842 (50.69%)
  gate:      >= 95%
  margin:    cleared the old 50% floor by 0.69 points

M8
  shape:     'W'/'N' speeds += 5e-8 deg/day
  before:    escaped all three modes
  caught by: deriv bucket median
  reading:   1.000e-07 ('N' cusp 1, lat -90..-80)
  gate:      1e-8
  margin:    10x

M9
  shape:     cusp_speed[5] x(1+2e-7) where armc < 120 (a third of a
             bucket)
  before:    escaped all three modes
  caught by: deriv bucket 80th percentile
  reading:   3.162e-07 ('A' cusp 5, lat -90..-80)
  gate:      1e-7
  margin:    3.2x

M14
  shape:     cusp_speed[5] x(1+2e-8) everywhere
  before:    escaped all three modes
  caught by: deriv bucket median
  reading:   3.162e-08 ('A' cusp 5, lat -90..-80)
  gate:      1e-8
  margin:    3.2x

M22
  shape:     'G' 13..36 = 42.0 on ERR rows, positions and speeds
  before:    escaped compare on trop
  caught by: compare trop unexplained
  reading:   32,160 (19,200-29,856 on the other four)
  gate:      0
  margin:    -

  caught by: deriv bucket median / p80 / p90
  reading:   1.000e+02 ('G' cusp 13)
  gate:      1e-8 / 1e-7 / 1e-6
  margin:    1e10x

  caught by: deriv worst absolute, abs(ref) < 1
  reading:   4.200e+01 deg/day ('G')
  gate:      5e-4
  margin:    8.4e4x

  caught by: claims Porphyry fallback
  reading:   2,240 of 10,258 rows
  gate:      0
  margin:    -

M28
  shape:     same, positions only
  before:    escaped compare on trop
  caught by: compare trop unexplained
  reading:   16,080 (9,600-14,928 on the other four)
  gate:      0
  margin:    -

  caught by: claims Porphyry fallback
  reading:   2,240 of 10,258 rows
  gate:      0
  margin:    -

M31
  shape:     sidereal ascmc_speed[0] += 5e-4 deg/day
  before:    escaped all three modes
  caught by: compare sid/sidt0/sidssy unexplained
  reading:   43,472
  gate:      0
  margin:    5e-4 against an ayanamsa rate of 1.048e-4

M9 is the reason there are two bucket-tail gates rather than the one the
audit asked for: it reads 3.162e-07 at the 80th percentile and at the
90th, and the 90th's own clean ceiling over 92 strides is 1.000e-07, so
a 90th-percentile gate with any headroom at all would sit at 1e-6 and
let M9 through. The 80th percentile has a clean ceiling of 3.162e-08 and
therefore has room to be set at 1e-7, which catches M9 with a bin to
spare. Both gates are kept: the 90th reaches a defect on a tenth of a
bucket, the 80th has the margin.

M22 and M28 name the hole precisely: gauquelin-fill accepted any finite
value into the 24 sectors the patch exists to fill, so 42.0 degrees was
as acceptable as the zero the patch actually writes. With the shape
tightened - an ERR row's sectors must be exactly zero, and any other
row's positions must be finite and inside [0,360), or [0,2pi) under
SEFLG_RADIANS - they fail on all five flag sets.

Third audit: the last ungated discard

A third audit found the harness sound overall - four of its five novel
mutants were caught and every figure in this file reproduced - and left
one real finding, which is fixed above: frame_of()'s date-level drops,
and claims's "no 'O' reference" rows, were counted, printed, and gated
by nothing. Its probe, and the before/after:

without the new gates
  probe:     frame_of() returns the skip_armc condition for abs(geolat)
             > 60
  deriv:     exit 0, 8,459 dates dropped, tiny-bucket share 0.0000%
  claims:    exit 0, 417 dates dropped

with them
  probe:     the same probe
  deriv:     exit 1, "dates dropped by the frame : 8459 (8459 no armc, 0
             no obliquity) FAIL"
  claims:    exit 1, "dates dropped by the frame : 417 ... FAIL"

The two builds differ only in those three fail++ lines; the poison, the
stack and the sample are identical. 8,459 of 25,916 dates is 32.6% of
the sample and it is not a random 32.6% - it is every row above 60
degrees of latitude, which is where most of what this harness exists to
check actually lives.

The audit's other three findings were instrument limits rather than
defects, and each is now stated where it belongs: the mean/percentile
blindness window, the outside-domain floor's ten-row slack, and
apc-degenerate being gated by its count alone.

The unmutated stack

  MUT=clean deriv=0 claims=0 compare=0
  (trop=0 rad=0 sid=0 sidt0=0 sidssy=0)

Verified at strides 1, 20, 200 and 2000, all three modes, all five flag
sets for compare, on the harness as it now stands. Three faults from the
first audit were re-run as controls and are still caught: ascmc_pos
(Ascendant +1e-6) by compare on all five flag sets with 43,472
unexplained; armc270 (cusp_speed[3] = 0 at armc == 270.0) by deriv's
worst relative at 1.000e+00 against 1e-3 and its degenerate mean at
6.481e-03 against 1e-7, and by compare trop with 3 unexplained;
radians_break (radians speed factor x2) by claims's SEFLG_RADIANS
conversion at 511,920 of 1,819,136.

A gate that only works by failing everything is worthless; the clean run
is the other half of the evidence.

Thresholds that are looser than one would like

- deriv worst relative, 1e-3. The audit suggested 1e-4. The shipping
  stack reaches 1.682e-4 on 'Y' cusp 3 at geolat -68.83, where the
  reference is 1.108 deg/day - just over the abs(ref) < 1 cutoff, so the
  relative error is amplified by a small denominator. 1e-4 would fail
  the clean stack. At 1e-3 the margin is 5.9x and the worst gate still
  catches everything in the table above that it is the named gate for,
  all of which read 1.0 or 3.3e-3.
- deriv worst absolute where abs(ref) < 1, 5e-4. The shipping stack
  reaches 1.716e-4 on the same 'Y' geometry; 2.9x margin. This is the
  tightest margin among the numeric deriv gates - it used to be
  described as the tightest in the harness, which was wrong: four of
  compare's count caps are tighter, three of them at 1.0x because the
  classes are saturated by construction, and the apc-degenerate bound is
  179.4 against a structural 180. The full ordering, tightest first:

radians-ascmc-speed count cap
  measured:  208.0/date
  threshold: 208/date
  margin:    1.00x (ceiling)

sidereal-ascmc-speed count cap
  measured:  182.0/date
  threshold: 182/date
  margin:    1.00x (ceiling)

apc-degenerate bound
  measured:  179.4 deg
  threshold: 180 deg
  margin:    1.003x (ceiling)

gauquelin-fill count cap
  measured:  39.90/date
  threshold: 48/date
  margin:    1.20x (ceiling)

gauquelin-clear count cap
  measured:  17.13/date
  threshold: 24/date
  margin:    1.40x (ceiling)

outside-domain unconverged share
  measured:  21.78%
  threshold: <= 40%
  margin:    1.8x

apc-rounding bound
  measured:  4.007e-11 deg
  threshold: 1e-10 deg
  margin:    2.5x

claims ascmc outside-domain agreement
  measured:  3.73% disagree
  threshold: <= 10%
  margin:    2.7x

deriv worst absolute, abs(ref) < 1
  measured:  1.716e-04
  threshold: 5e-4
  margin:    2.9x

The four marked "ceiling" cannot be given more headroom without
exceeding what the class can structurally contain, and saying "1.0x" is
the honest way to record that: for those the count cap is a statement of
the ceiling and the real gate is the shape test.

- deriv steep ratio, 1e-5. It was 0.25, chosen so a x1.5 fails with 2x
  margin. The shipping stack reads 2.000e-09, so 0.25 was eight decades
  of unused slack and a x(1+1e-3) on every steep speed sailed through.
  1e-5 keeps four decades of headroom over the measured worst and
  catches a x1.2 with 2e4x. The class is empty at stride >= 100 and the
  verdict now says so rather than printing pass over nothing.
- deriv mean per letter, 1e-8 on the random rows and 1e-7 on the
  degenerate ones. The audit suggested a single 1e-8. One mean over both
  blocks is a function of the stride, not of the library: the degenerate
  block is 396 fixed dates, so it is 1.5% of the sample at stride 1 and
  97% at stride 2000, and its rows sit at genuinely harder geometry.
  Measured as one number it read 1.078e-9 at stride 1 and 1.852e-8 at
  stride 2000 - the same library, the same code, a gate that passed at
  one stride and failed at another. Split, the random mean is 1.081e-9
  (9.3x) and the degenerate mean is 2.144e-08 (4.7x), and both hold at
  every stride from 1 to 25,520. Every share-based gate in the harness
  is split the same way and for the same reason.
    The mean is retained beside the bucket median rather than replaced,
    because the two fail on different mutants: const_1em5 and lat65_1em5
    are caught by the median with 316x to spare while the mean has 1.7x
    and 12x, and armc270 is caught by the degenerate mean with 6.5e4x
    while the median does not move at all.

- degenerate-block unconverged share, 25%. 'W' loses 15.2% of its
  degenerate comparisons to an unconverged reference - its cusps are
  pinned, so the numerical difference straddles a sign ingress wherever
  one falls inside the step. The random block is held to 1%, where the
  measured worst is 0.


Corrections to earlier versions of this document

- The results table used to be headed "positions must not move at all"
  while listing 5,757 positions that had moved. What the shipping stack
  actually guarantees is that positions move only in 'Y', only by
  rounding except at a singularity the APC patch exists to fix, and
  never anywhere else - which is what the allowlist now states and
  gates.
- "5,757, all hsys 'Y', at most 6.935e-12 degrees" was a trop-only
  figure taken before the degenerate block existed. With it, trop reads
  6,562, of which 508 are at a degeneracy and reach 179.4 degrees. That
  is the patch working, and the old sample simply never went there.
- "derivative gate, worst mean relative error, any letter: 6.665e-01 ->
  1.081e-09" is now 1.078e-09; the difference is the 396 degenerate
  dates entering the mean.
- The gate table used to say abs(ref) > 1e4 was "the subject of the
  report, not of this gate". It is a gate now.
- "letters with nothing judged: reported, not gated" is gated.
- "Every comparison is judged" was too strong, and in bold. 21,958 of
  them were judged by nothing at all and 78,842 by a share only. Both
  are gated now, and the sentence has been rewritten to say what is
  actually true.
- "This is the tightest margin in the harness", of the 2.9x on deriv's
  worst absolute error, was wrong: four of compare's count caps are
  tighter, three of them at the class's structural ceiling. The full
  ordering is tabulated above.
- The FMA drift figures disagreed with each other - 2.6e-05 degrees over
  4,386,719 values in this file and "about 6e-06 degrees" in build.sh,
  neither reproducible. Both now carry one measured pair: 3,062,802 of
  8,707,776 positions move, by up to 1.545e-07 degrees away from a
  singularity.
- The steep-ratio gate used to print pass at strides where it had
  nothing to judge. It says "gate not exercised at this stride" instead,
  and the class is empty at stride >= 100.
- compare's "baseline dump ended early" was printed when the patched
  dump had trailing rows, which is the one file that branch says nothing
  about.
- "one of them moves by 180 degrees: that row" understated the FMA
  measurement. It is 48 slots across 6 degenerate rows, all Sunshine
  'I', maximum exactly 180.0.
- "Cusp speeds move further, up to 2.3e-03 deg/day" was the random block
  only; the degenerate block reaches 4.096e+03 deg/day. The sentence is
  scoped now.
- house-speed-harness.c said the two bucket-tail gates were "one and
  three bins clear". They are one (p80 over its 3.162e-08 ceiling) and
  two (p90 over its 1.000e-07 ceiling).
- The Results heading named four patches without saying what the fifth
  does to the numbers. It does nothing to them: every dump is
  byte-identical with it applied.
- frame_of()'s date drops and claims's "no 'O' reference" rows were
  counted and printed and gated by nothing. All three are gated at zero
  now.