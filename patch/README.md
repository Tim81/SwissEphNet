swehouse.c patches

Against Swiss Ephemeris v2.10.3bfinal (f4dcd18e). Everything here
applies to a pristine tree with a plain git apply, and each patch
carries its own reasoning and its own one-command demonstration in its
header.

Measured on MSVC 19.51, gcc 13.3.0 and clang 18.1.3. gcc and clang agree
to every digit printed, dump-for-dump; MSVC agrees everywhere except the
last printed digit of one speed at one near-degenerate input (the APC
equator demonstration's cusp-1 speed, 5e-11 degrees from the
singularity: 381 42'44.4604 against 44.4601). Building swetest itself
under MSVC needs the known upstream do_printf fix first (fp is
undeclared under _WINDOWS); the library builds as-is.


Ten small patches

Each stands alone against a pristine tree and composes with all the
others in any order. Six of them are also contained in the analytic
patch and cannot be combined with it; the conflict table below says
which.

  swehouse-apc-equator-fix.patch                 17/15
    apc_sector() is 0/0 within 1e-10 degrees of the equator and returns
    cusps that are not the Ascendant and Descendant.
    shows up in: -house13.4,0.00000000005,Y

  swehouse-makransky-w180.patch                  17/14
    a Sunshine (Makransky) cusp comes back 180 degrees wrong when the
    house circle stands exactly on w = 180.
    shows up in: 8 of 360 integer ARMC values at geolat 0

  swehouse-gauquelin-porphyry-fallback.patch     28/0
    Gauquelin sectors leave cusp[13..36] and cusp_speed[13..36]
    untouched when they fall back to Porphyry, so a caller reads its own
    stack.
    shows up in: any 'G' call inside the polar circle

  swehouse-sidereal-ayanamsa-rate.patch          51/1
    sidereal positions, tropical speeds: the ayanamsa is subtracted but
    its rate never is.
    shows up in: -house13.4,52.5,P -speed -sid1

  swehouse-krusinski-zero-speeds.patch           50/0
    eight of twelve Krusinski cusp speeds are exactly zero, never
    assigned.
    shows up in: -house13.4,52.5,U -speed

  swehouse-porphyry-cusp-speeds.patch            9/2
    Porphyry cusps 11 and 12 grow from the Ascendant, and are offsets
    from the MC.
    shows up in: -house13.4,52.5,O -speed

  swehouse-koch-mc-derivative.patch              38/5
    Koch holds the ascensional difference fixed; 39 per cent high at
    latitude 60.
    shows up in: -house13.4,60,K -speed

  swehouse-whole-sign-zero-speeds.patch          33/0
    whole sign and 0 Aries cusps stand still between ingresses but
    report a speed.
    shows up in: -house13.4,52.5,W -speed -sid1

  swehouse-radians-speed-units.patch             13/0
    SEFLG_RADIANS converts the positions and leaves both speed arrays in
    degrees.
    shows up in: C sample in the header; swetest has no radians flag

  swehouse-sunshine-error-path.patch             21/1
    the Sunshine declination check segfaults through the serr-less entry
    points and returns with no output written.
    shows up in: claim-samples.c sunshine; swe_houses(630070.0, ...,
      'I') crashes pristine

Three of these change values working code already receives, and their
headers say so in the first paragraph: radians-speed-units moves every
radians-mode speed by a factor of 57.3, whole-sign-zero-speeds turns a
nonzero speed into a zero that a caller may divide by, and
porphyry-cusp-speeds moves four cusp speeds by 34 to 38 per cent.


The cusp speed work, offered two ways

Take either swehouse-analytic-cusp-speeds.patch (1031/117), or the eight
steps in series/. Not both: applied in order the series produces a file
byte-identical to the one the single patch produces, which is checked
rather than claimed.

  01-infra                               114/0
    a #define, two comment blocks, nine forward declarations. Nothing
    runs.

  02-radians-speed-units                 13/0
    SEFLG_RADIANS converted the positions and left the speeds in
    degrees.

  03-whole-sign-zero-speeds              33/0
    whole sign cusps stand still; the worst speed reported for one is
    12,385 degrees per day.

  04-porphyry-cusp-speeds                9/2
    Porphyry cusps 11 and 12 grew from the Ascendant, not the MC.

  05-koch-mc-derivative                  38/5
    Koch held the ascensional difference fixed; 39 per cent high at
    latitude 60.

  06-krusinski-cusp-speeds               50/0
    eight of twelve Krusinski cusp speeds were exactly zero.

  07-placidus-gauquelin-pole-speed       255/18
    the Placidus pole height follows the cusp; the |tant| branch guard
    rides with it.

  08-analytic-cusp-speeds                519/92
    closed-form speeds for the nine systems that were differencing.

Steps 01 to 06 each apply to a pristine tree on their own, so a single
one of them can be taken without the rest. 07 needs 01 to 05 - not 06,
which it is independent of. 08 applies after 01 to 06 but calls two
helpers only 07 defines, so it needs all seven to link.

Every step compiles. Steps 01 to 07 warn while a forward declaration
waits for its definition: nine times after step 01, all of them
"declared static but never defined". After step 07 there are five, of
which three are that message and two are "defined but not used"
(pullen_sr_sectors, pullen_sr_slopes). After step 08 there are none.
Measured with gcc -Wall; clang agrees. MSVC does not produce this family
of diagnostic at all, so this is a gcc and clang story only. The
pristine file and the finished series are both warning-free, and every
top-level patch leaves the file warning-free too.

Two seams are not clean, and both are noted in the step headers rather
than hidden: 07 cannot be split further, because the pole-height fix and
the |tant| branch guard share the same twelve lines; and 07 carries two
helpers that 08 is the first to call, because they sit in the same added
block as the ones 07 needs and cutting that block leaves a piece git
apply will not place.


What conflicts with what

Measured by applying each and then testing every other against it, not
inferred.

                    apc mak gau sid kru por who koc rad sun ana
  apc-equator-fix     .  ok  ok  ok  ok  ok  ok  ok  ok  ok  ok
  makransky-w180     ok   .  ok  ok  ok  ok  ok  ok  ok  ok   X
  gauquelin-fallback ok  ok   .  ok  ok  ok  ok  ok  ok  ok  ok
  sidereal-ayanamsa  ok  ok  ok   .  ok  ok  ok  ok  ok  ok  ok
  krusinski          ok  ok  ok  ok   .  ok  ok  ok  ok  ok   X
  porphyry           ok  ok  ok  ok  ok   .  ok  ok  ok  ok   X
  whole-sign         ok  ok  ok  ok  ok  ok   .  ok  ok  ok   X
  koch               ok  ok  ok  ok  ok  ok  ok   .  ok  ok   X
  radians            ok  ok  ok  ok  ok  ok  ok  ok   .  ok   X
  sunshine           ok  ok  ok  ok  ok  ok  ok  ok  ok   .  ok
  analytic           ok   X  ok  ok   X   X   X   X   X  ok   .

The analytic patch contains six of the small ones outright, so it is
either it or those six. apc-equator-fix, gauquelin-porphyry-fallback,
sidereal-ayanamsa-rate and sunshine-error-path are independent of
everything, including the analytic patch.

makransky-w180 and the analytic patch overlap: the analytic patch moves
that quadrant chain into a function of its own and carries the same
correction there, so the standalone patch is for taking the 180 degree
fix without the speed work.


Verification

harness/ holds a regression harness. Build it against a pristine tree
and a patched one and compare: 25,520 random dates from 1000 BC to 4100
AD plus a 396-point block pinned to the degenerate places (equator,
poles, polar circle, exact armc quadrants), across all 26 house letters
- 673,816 calls per flag set, five flag sets (tropical, radians, and
three sidereal routes). It checks each reported speed against a
Richardson-extrapolated difference of the position and re-tests every
falsifiable claim these headers make. Its deriv and claims gates certify
the patch set as a whole; run against a subset at least one of the two
fails on the missing companions' defects, except the sunshine patch,
which changes only an error path the sample never takes and is
byte-identical to pristine on every dump. See harness-README.md.

harness/demos.sh runs every swetest demonstration above, before and
after, side by side.

The derivative gate judges small references on absolute rather than
relative error. That distinction is not cosmetic: judged relatively on
the random block, the whole sign and 0 Aries letters discarded 612,432
of their 612,480 comparisons as "reference too small" and reported a
clean zero, which is how a cusp reporting 12,385 degrees per day went
unnoticed through several runs.

verify-claims.sh beside the patches is the second, smaller net: it
rebuilds pristine and each patched tree from scratch and re-derives
seventeen recorded before/after claims - the swetest lines and the C
samples in claim-samples.c, the segfault included - against claims.tsv,
plus per-patch apply/diffstat/warning checks and the series
byte-identity, failing loudly if any drifts.
