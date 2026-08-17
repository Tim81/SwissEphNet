#!/bin/bash
# Every swetest demonstration in these patches, before and after, side by side.
#
#   ./demos.sh /path/to/unpatched-build /path/to/patched-build [ephe-dir]
#
# Each build directory must contain a swetest binary. Nothing here needs an
# ephemeris for the house calculation itself; the path is only passed through
# so that -eswe has somewhere to look.
#
# bash rather than sh, and pipefail, because every demonstration below is
# `swetest ... | sed`. Without pipefail the exit status is sed's, so a swetest
# that failed to run at all -- missing binary, missing ephemeris, a segfault --
# printed an empty "before" block and the script carried on to the next demo
# reporting success. An empty block beside a full one reads as "the patch added
# this output", which is the most flattering possible way to render a crash.
set -e
set -o pipefail
B="${1:?usage: demos.sh UNPATCHED_BUILD PATCHED_BUILD [ephe-dir]}"
A="${2:?usage: demos.sh UNPATCHED_BUILD PATCHED_BUILD [ephe-dir]}"
E="${3:-}"
EDIR=""
[ -n "$E" ] && EDIR="-edir$E"

run() { d="$1"; shift; (cd "$d" && ./swetest "$@" -eswe $EDIR); }

demo() {
  title="$1"; filter="$2"; shift 2
  echo "=================================================================="
  echo "$title"
  echo "  swetest $*"
  echo "------------------------------------------------------------------"
  echo "-- before:"; run "$B" "$@" | sed -n "$filter"
  echo "-- after:";  run "$A" "$@" | sed -n "$filter"
  echo
}

demo "D1  APC cusp speeds near ARMC 270, the case from the mailing list.
    Cusps 1 and 7 are the Ascendant and Descendant here, and the Ascendant's
    own speed is printed four lines below them. Before, the two disagree by 6
    per cent; after, they are the same number, because cusp 1 is no longer
    differenced across a region the sampling window cannot resolve. The other
    eight cusp speeds move by 3 to 18 per cent for the same reason. All twelve
    positions are unchanged. The 2.89 million is not an artefact and does not
    go away: the Ascendant really does move that fast 0.004 degrees from the
    horizon tangency, and swetest already printed that figure for it." \
     '/^house  1/,/^Ascendant/p' \
     -b1.1.2000 -ut12:00 -p -house-10.46061837,66.5607089,Y -speed

demo "D2  APC within 1e-10 degrees of the equator.
    Cusp 1 of the APC system is the Ascendant by definition and cusp 7 the
    Descendant. Before, cusp 1 is 28.7 degrees away from the Ascendant printed
    below it and cusps 1 and 7 are 124.2 degrees apart instead of 180. After,
    cusp 1 is the Ascendant to the last digit and cusp 7 exactly opposite." \
     '/^house  1 /p;/^house  7 /p;/^Ascendant/p' \
     -b1.1.2000 -ut12:00 -house13.4,0.00000000005,Y

demo "D3  Krusinski-Pisa-Goelzer cusp speeds.
    Before, eight of the twelve are exactly zero: only 1, 4, 7 and 10 were ever
    filled, by the generic code that runs before the house system switch. After,
    all twelve are the derivative of the rotation chain the system is built from." \
     '/^house  1/,/^house 12/p' \
     -b1.1.2000 -ut12:00 -house13.4,52.5,U -speed

demo "D4  Porphyry cusps 11 and 12.
    They are offsets from the MC, and grew from the Ascendant's speed instead.
    687 and 774 degrees per day become 426 and 513, the rates their own
    positions move at." \
     '/^house 11/,/^house 12/p' \
     -b1.1.2000 -ut12:00 -house13.4,52.5,O -speed

demo "D4b Porphyry again, reached through Koch inside the polar circle.
    Five house systems fall back to Porphyry there, so they inherit its cusp
    speeds; this is the same defect as D4 seen through one of them. Cusp 11
    changes sign, -367 to +163 degrees per day: the old value had the cusp
    moving the wrong way. Note that this is the Porphyry fix, not the Koch one
    below, which is why the latitude differs between the two." \
     '/^house 11/,/^house 12/p' \
     -b1.1.2000 -ut12:00 -house13.4,80,K -speed

demo "D5  Whole sign houses, sidereal.
    The cusps sit on sign boundaries and stand still between ingresses, but were
    reported as moving at 600 degrees per day. The sidereal path needs its own
    line for this: it computes 'W' as equal houses and imposes the boundaries
    afterwards, so the equal house speeds survived on the pinned cusps." \
     '/^house  1/,/^house  3/p' \
     -b1.1.2000 -ut12:00 -house13.4,52.5,W -speed -sid1

demo "D6  Koch at latitude 60.
    Koch trisects the semi-arc of the MC, so one ascensional difference serves
    all four of its own cusps and the derivative is a chain through the MC. It
    was held fixed before, and the error grows with latitude: cusp 11 reads
    1303 degrees per day against a true 940, 39 per cent high. The latitude has
    to be below the polar circle for this to be Koch's own code at all; above
    it the case bails to Porphyry and what changes is D4b instead." \
     '/^house 11/,/^house 12/p' \
     -b1.1.2000 -ut12:00 -house13.4,60,K -speed

demo "D7  Placidus at latitude 60.
    The pole height follows the cusp, so the total derivative needs the implicit
    function theorem; before, only the partial at fixed pole height was taken." \
     '/^house 11/,/^house 12/p' \
     -b1.1.2000 -ut12:00 -house13.4,60,P -speed

demo "D8  Sidereal cusp speeds, Fagan/Bradley.
    Before the sidereal patch the tropical and sidereal calls print the same
    speed beside positions an ayanamsa apart; after it the sidereal one is
    lower by the ayanamsa's rate, 0.1448 arc seconds a day here. The tropical
    line does not move." \
     '/^house  1/p' \
     -b1.1.2000 -ut12:00 -house13.4,52.5,P -speed -sid1
