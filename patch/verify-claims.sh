#!/bin/bash
#
# Re-derive every swetest claim these patch headers make, from a clean build.
#
#   ./verify-claims.sh --record   [-s <pristine tree>]   write claims.tsv from measurement
#   ./verify-claims.sh            [-s <pristine tree>]   check the headers against reality
#
# Why this exists. Nearly every wrong number that reached a draft of this patch
# set came from one of five things, and all five are mechanical:
#
#   1. A number measured against the wrong tree. Build artifacts outlive the
#      source that made them, and nothing in a stale binary says so. This script
#      always builds from scratch into a fresh directory and never reuses one.
#   2. A number taken at one sampling stride and printed in a table headed with
#      another. Here the command is the record, so there is no second sample to
#      confuse it with.
#   3. A claim that was true when written and false after the code was
#      regenerated. That is what this catches on every later run.
#   4. A scope claim -- "cusps 11 and 12", "positions are untouched" -- that was
#      never re-derived after mirroring or a folded-in fix widened it. Recording
#      whole house blocks rather than the one line the prose quotes is what
#      turns that from prose into a test.
#   5. The applier lying. `patch -p1` accepted this set and silently dropped
#      three hunks, reporting success and leaving a tree fifteen lines short;
#      the work built on it looked fine until the result failed to reproduce its
#      own source. Every build below goes through `git apply`, which is strict
#      and refuses rather than guesses. Do not substitute `patch`.
#
# What this is and is not: seventeen point probes. A behaviour change that
# vanishes at exactly these inputs passes here; for the five patches the
# harness's full-sample dump/compare closes that gap, and for the standalone
# small patches a second probe at a different latitude narrows it. It cannot
# be closed by any finite probe list, which is why the harness exists.
#
# It needs no ephemeris files: every command below uses -p0 and house systems
# that do not read one.
#
set -u
here=$(cd "$(dirname "$0")" && pwd)
src="$here/../external/swisseph"
mode=verify
while [ $# -gt 0 ]; do
  case "$1" in
    --record) mode=record; shift ;;
    -s) src=$2; shift 2 ;;
    *) echo "usage: $0 [--record] [-s <pristine swisseph tree>]" >&2; exit 2 ;;
  esac
done
[ -f "$src/swehouse.c" ] || { echo "no swehouse.c under $src" >&2; exit 2; }

work=$(mktemp -d) || exit 2
trap 'rm -rf "$work"' EXIT
CFLAGS="-O2 -ffp-contract=off"
UNITS="swedate.c swehouse.c swejpl.c swemmoon.c swemplan.c sweph.c swephlib.c swecl.c swehel.c"

# build <name> [patch]  -- always from the pinned source, never incremental
build() {
  local d="$work/$1"; shift
  rm -rf "$d"; mkdir -p "$d"
  cp "$src"/*.c "$src"/*.h "$d"/ 2>/dev/null
  if [ $# -gt 0 ]; then
    ( cd "$d" && git apply "$1" ) || { echo "APPLY FAILED: $1" >&2; return 1; }
  fi
  ( cd "$d" && gcc $CFLAGS -c $UNITS 2>/dev/null \
      && ar rcs libswe.a *.o \
      && gcc $CFLAGS -I. -o swetest swetest.c libswe.a -lm 2>/dev/null \
      && gcc $CFLAGS -I. -o claim-samples "$here/claim-samples.c" libswe.a -lm 2>/dev/null ) \
    || { echo "BUILD FAILED: $d" >&2; return 1; }
}

# the claims: key|patch|house-numbers|swetest args. The key names the tsv
# row, so one patch may carry several claims.
CLAIMS="
krusinski|swehouse-krusinski-zero-speeds|2 3 5 6 8 9 11 12|-b1.1.2000 -ut12:00 -house13.4,52.5,U -speed -p0
porphyry|swehouse-porphyry-cusp-speeds|2 3 5 6 11 12|-b1.1.2000 -ut12:00 -house13.4,52.5,O -speed -p0
koch|swehouse-koch-mc-derivative|2 3 5 6 8 9 11 12|-b1.1.2000 -ut12:00 -house13.4,60,K -speed -p0
whole-sign|swehouse-whole-sign-zero-speeds|1 2 3 4 5 6 7 8 9 10 11 12|-b1.1.2000 -ut12:00 -house13.4,52.5,W -speed -sid1 -p0
apc|swehouse-apc-equator-fix|1 4 7 10|-b1.1.2000 -ut12:00 -house13.4,0.00000000005,Y -p0
sidereal|swehouse-sidereal-ayanamsa-rate|1|-b1.1.2000 -ut12:00 -house13.4,52.5,P -speed -sid1 -p0
ana-porphyry|swehouse-analytic-cusp-speeds|2 3 5 6 11 12|-b1.1.2000 -ut12:00 -house13.4,52.5,O -speed -p0
ana-thread|swehouse-analytic-cusp-speeds|1 7 10|-b1.1.2000 -ut12:00 -house-10.46061837,66.5607089,Y -speed -p0
koch-30|swehouse-koch-mc-derivative|2 3 11 12|-b1.1.2000 -ut12:00 -house13.4,30,K -speed -p0
krusinski-30|swehouse-krusinski-zero-speeds|2 3 5 6|-b1.1.2000 -ut12:00 -house13.4,30,U -speed -p0
porphyry-30|swehouse-porphyry-cusp-speeds|5 6 11 12|-b1.1.2000 -ut12:00 -house13.4,30,O -speed -p0
whole-sign-trop|swehouse-whole-sign-zero-speeds|1 4 7 10|-b1.1.2000 -ut12:00 -house13.4,52.5,W -speed -p0
"

# emit "<house n> <rest of line>" for the requested house numbers
lines() { # $1 build dir, $2 space-separated house numbers, rest: args
  local d=$1 nums=$2; shift 2
  local out; out=$("$work/$d/swetest" "$@" 2>/dev/null)
  local n
  for n in $nums; do
    printf '%s\n' "$out" | awk -v n="$n" '$1=="house" && $2==n {
      $1=""; $2=""; sub(/^  */,""); printf("house %s %s\n", n, $0) }' n="$n"
  done
}

build pristine || exit 1
tsv="$here/claims.tsv"
[ "$mode" = record ] && : > "$tsv"

status_file="$work/status"; : > "$status_file"
printf '%s\n' "$CLAIMS" | while IFS='|' read -r key patch nums args; do
  [ -z "${key:-}" ] && continue
  p="$here/$patch.patch"
  [ -f "$p" ] || { echo "MISSING PATCH $patch"; echo x >> "$status_file"; continue; }
  build "p_$patch" "$p" || { echo "FAIL(build) $patch"; echo x >> "$status_file"; continue; }
  before=$(lines pristine "$nums" $args)
  after=$(lines "p_$patch" "$nums" $args)
  if [ "$mode" = record ]; then
    printf '%s\tBEFORE\t%s\n' "$key" "$(printf '%s' "$before" | tr '\n' '~')" >> "$tsv"
    printf '%s\tAFTER\t%s\n'  "$key" "$(printf '%s' "$after"  | tr '\n' '~')" >> "$tsv"
    echo "recorded $key"
  else
    eb=$(awk -F'\t' -v k="$key" '$1==k && $2=="BEFORE"{print $3}' "$tsv" | tr '~' '\n')
    ea=$(awk -F'\t' -v k="$key" '$1==k && $2=="AFTER"{print $3}'  "$tsv" | tr '~' '\n')
    if [ -z "$eb$ea" ]; then echo "NO RECORD  $key"; echo x >> "$status_file"; continue; fi
    ok=yes
    [ "$before" = "$eb" ] || { ok=no; { echo "FAIL $key  before drifted:"; echo x >> "$status_file"; }; diff <(printf '%s\n' "$eb") <(printf '%s\n' "$before") | sed 's/^/     /'; }
    [ "$after"  = "$ea" ] || { ok=no; { echo "FAIL $key  after  drifted:"; echo x >> "$status_file"; }; diff <(printf '%s\n' "$ea") <(printf '%s\n' "$after")  | sed 's/^/     /'; }
    [ "$before" = "$after" ] && { ok=no; { echo "FAIL $key  patch changed nothing at this input"; echo x >> "$status_file"; }; }
    [ "$ok" = yes ] && echo "pass $key"
  fi
done

# the claims swetest cannot show, via claim-samples.c
CSAMPLES="
gauquelin-c|swehouse-gauquelin-porphyry-fallback|gauquelin
makransky-c|swehouse-makransky-w180|makransky
radians-c|swehouse-radians-speed-units|radians
sunshine-c|swehouse-sunshine-error-path|sunshine
apcx-c|swehouse-apc-equator-fix|apcx
"
printf '%s\n' "$CSAMPLES" | while IFS='|' read -r key patch what; do
  [ -z "${key:-}" ] && continue
  p="$here/$patch.patch"
  [ -f "$p" ] || { echo "MISSING PATCH $patch"; echo x >> "$status_file"; continue; }
  build "c_$patch" "$p" || { echo "FAIL(build) $patch"; echo x >> "$status_file"; continue; }
  before=$("$work/pristine/claim-samples" "$what" 2>&1); st=$?
  [ $st -gt 128 ] && before="$before
DIED: signal $((st-128))"
  after=$("$work/c_$patch/claim-samples" "$what" 2>&1); st=$?
  [ $st -gt 128 ] && after="$after
DIED: signal $((st-128))"
  if [ "$mode" = record ]; then
    printf '%s\tBEFORE\t%s\n' "$key" "$(printf '%s' "$before" | tr '\n' '~')" >> "$tsv"
    printf '%s\tAFTER\t%s\n'  "$key" "$(printf '%s' "$after"  | tr '\n' '~')" >> "$tsv"
    echo "recorded $key (C sample)"
  else
    eb=$(awk -F'\t' -v k="$key" '$1==k && $2=="BEFORE"{print $3}' "$tsv" | tr '~' '\n')
    ea=$(awk -F'\t' -v k="$key" '$1==k && $2=="AFTER"{print $3}'  "$tsv" | tr '~' '\n')
    if [ -z "$eb$ea" ]; then echo "NO RECORD  $key"; echo x >> "$status_file"; continue; fi
    ok=yes
    [ "$before" = "$eb" ] || { ok=no; { echo "FAIL $key  before drifted:"; echo x >> "$status_file"; }; diff <(printf '%s\n' "$eb") <(printf '%s\n' "$before") | sed 's/^/     /'; }
    [ "$after"  = "$ea" ] || { ok=no; { echo "FAIL $key  after  drifted:"; echo x >> "$status_file"; }; diff <(printf '%s\n' "$ea") <(printf '%s\n' "$after")  | sed 's/^/     /'; }
    [ "$before" = "$after" ] && { ok=no; { echo "FAIL $key  patch changed nothing at this input"; echo x >> "$status_file"; }; }
    [ "$ok" = yes ] && echo "pass $key (C sample)"
  fi
done

echo
echo "structural checks"
# each patch applies to a pristine tree on its own
for p in "$here"/swehouse-*.patch; do
  build chk "$p" >/dev/null 2>&1 && echo "  applies   $(basename "$p")" || { echo "  FAILS     $(basename "$p")"; echo x >> "$status_file"; }
done
# the stated diffstat matches the body
for p in "$here"/swehouse-*.patch; do
  rm -rf "$work/ns"; mkdir -p "$work/ns"; cp "$src"/*.c "$src"/*.h "$work/ns"/ 2>/dev/null
  n=$( cd "$work/ns" && git apply --numstat "$p" 2>/dev/null | awk '{print $1"/"$2}' )
  s=$( grep -m1 -oE '[0-9]+ insertions?\(\+\)(, [0-9]+ deletions?\(-\))?' "$p" \
       | grep -oE '[0-9]+' | paste -sd/ )
  case "$s" in *"/"*) ;; *) s="$s/0" ;; esac   # no deletions clause means zero
  [ "$n" = "$s" ] && echo "  diffstat  $(basename "$p") $n" \
                  || { echo "  DIFFSTAT MISMATCH $(basename "$p"): body $n, header $s"; echo x >> "$status_file"; }
done
# warning delta against pristine must be zero
w0=$( cd "$work/pristine" && gcc -O2 -Wall -c swehouse.c -o /dev/null 2>&1 | grep -c warning: )
for p in "$here"/swehouse-*.patch; do
  build wchk "$p" >/dev/null 2>&1 || continue
  w=$( cd "$work/wchk" && gcc -O2 -Wall -c swehouse.c -o /dev/null 2>&1 | grep -c warning: )
  [ "$w" = "$w0" ] && echo "  warnings  $(basename "$p") $w (pristine $w0)" \
                   || { echo "  NEW WARNINGS $(basename "$p"): $w vs pristine $w0"; echo x >> "$status_file"; }
done
# the series must reproduce the single patch byte for byte
build s_series >/dev/null 2>&1
for f in "$here"/series/*.patch; do ( cd "$work/s_series" && git apply "$f" ) || { echo "  SERIES FAILED at $(basename "$f")"; echo x >> "$status_file"; }; done
build s_single "$here/swehouse-analytic-cusp-speeds.patch" >/dev/null 2>&1
if cmp -s "$work/s_series/swehouse.c" "$work/s_single/swehouse.c"; then
  echo "  series    byte-identical to the single analytic patch"
else
  { echo "  SERIES DIFFERS from the single analytic patch"; echo x >> "$status_file"; }
fi

if [ -s "$status_file" ]; then
  echo
  echo "verify-claims: FAILED ($(wc -l < "$status_file" | tr -d ' ') problem(s) above)"
  exit 1
fi
if [ "$mode" = verify ]; then
  echo
  echo "verify-claims: all claims and structural checks passed"
fi
exit 0
