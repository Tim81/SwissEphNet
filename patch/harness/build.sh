#!/bin/sh
# Build the harness against a Swiss Ephemeris source tree.
#
#   ./build.sh /path/to/swisseph-sources [outdir] [cc]
#
# The source tree is copied first, so applying a patch for one build never
# disturbs another and the shipped sources are never touched. Two binaries land
# in outdir: house-speed-harness and compare.
set -e
SRC="${1:?usage: build.sh /path/to/swisseph-sources [outdir] [cc]}"
OUT="${2:-$(pwd)/build}"
CC="${3:-cc}"
HERE=$(cd "$(dirname "$0")" && pwd)

mkdir -p "$OUT"
cp "$SRC"/*.c "$SRC"/*.h "$OUT"/ 2>/dev/null || true
cd "$OUT"
rm -f ./*.o libswe.a

# -ffp-contract=off is not optional. This harness's central claim is that
# positions are bit-identical between two builds, and gcc and clang both default
# to -ffp-contract=fast at -O2: given -mfma or any -march that implies it, a
# a*b+c is fused and one rounding step disappears. Measured against this build,
# at stride 1 on the trop flag set: 3,062,802 of 8,707,776 cusp positions move,
# by up to 1.545e-07 degrees away from a singularity and by exactly 180 degrees
# in 48 slots across six degenerate rows that sit on one, all Sunshine 'I'. It
# also perturbs the sampled coordinates
# themselves, so the two builds would not even be asked the same question --
# and it moves the ephemeris fingerprint, so compare refuses the pair outright.
# Without this flag the harness measures the optimiser. (harness.h defends the
# sampler separately, with a volatile, so that a caller who compiles it by hand
# still samples the same points.)
FPFLAGS="-ffp-contract=off"

# The library translation units, from swisseph's own Makefile. swetest.c is a
# program rather than part of the library and is deliberately not compiled here.
$CC -O2 $FPFLAGS -fPIC -c swedate.c swehouse.c swejpl.c swemmoon.c swemplan.c \
                sweph.c swephlib.c swecl.c swehel.c
ar rcs libswe.a ./*.o

$CC -O2 $FPFLAGS -I. -I"$HERE" -o house-speed-harness "$HERE/house-speed-harness.c" libswe.a -lm
$CC -O2 $FPFLAGS -I. -I"$HERE" -o compare              "$HERE/compare.c"              libswe.a -lm

echo "built in $OUT:"
echo "  $OUT/house-speed-harness"
echo "  $OUT/compare"
