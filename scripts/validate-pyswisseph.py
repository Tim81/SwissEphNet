#!/usr/bin/env python3
"""Validate pyswisseph against setest/t.exp -- a trust hop check, not a CI gate.

Why this exists: pyswisseph vendors the same Astrodienst libswe 2.10.03 this
port targets, and can answer "what does the real C do for THIS input" for any
input, which t.exp cannot (t.exp is a frozen recording of ~12,757 specific
inputs). That makes it useful for validating this port on inputs t.exp never
covered. But pyswisseph is C plus a third-party Python binding -- an untested
trust hop -- so before leaning on it for anything, this script checks it
against the one thing that can: does pyswisseph reproduce t.exp's own
published expected values, for a representative sample of the inputs t.exp
already covers?

This is a developer script, not part of any CI gate: it validates a
third-party dependency, not this port, so a disagreement here is a finding
about pyswisseph (or about this script's own reproduction of the reference
C's call semantics), not a regression in SwissEphNet.

Usage:
    python scripts/validate-pyswisseph.py [--ephe PATH] [--tolerance FLOAT]

Requires: pyswisseph (pip install pyswisseph) and external/swisseph checked
out with *exactly* the declared core ephemeris files
(Tests/conformance/required-ephemeris-files.tsv -- the same set
EphemerisManifest asserts for the conformance oracle itself), not a full,
non-sparse submodule checkout: extra era files change which iterations get
compared here the same way they change known-fail.tsv, since a "file not
found" exception this script treats as a skip does not happen if the file
happens to be present. SEFLG_JPLEPH iterations are always skipped and
reported separately, since this repo does not ship a JPL DE file either way.

Last verified against the declared 8-file core set: suite 6 testcase 1
(swe_houses) 3384/3384 agree (100%, unaffected by ephemeris data -- houses
need none); suite 1 testcase 1 (swe_calc) 124/228 agree (54.4%; 195
iterations skipped corpus-wide, 33 of those specifically for a supplementary
asteroid/moon file the core set does not ship); 3508/3612 combined (97.12%).
An earlier run of this same script against a contaminated, non-sparse
checkout (158 ephe files) reported different skip/compared counts (fewer
skipped, since more files happened to be on disk) but the same 54.4%
agreement rate among what was actually compared -- the disagreement finding
below held even before this was caught, only the skip accounting was wrong.
"""

from __future__ import annotations

import argparse
import os
import re
import sys
from dataclasses import dataclass, field


# ---------------------------------------------------------------------------
# A minimal t.exp reader. Ported from the same source the C# reader
# (Tests/SwissEphNet.Conformance.Tests/Corpus/ExpReader.cs) documents itself
# against (external/swisseph/setest/reader.c): a line is a section marker
# only if it equals "TESTSUITE"/"TESTCASE"/"ITERATION" after trimming;
# indentation is cosmetic. Only suites/testcases in TARGET_TESTCASES are kept
# in memory -- the file is ~334,000 value lines and this script only needs a
# handful of testcases' worth.
# ---------------------------------------------------------------------------


@dataclass
class Iteration:
    suite: int
    testcase: int
    fields: dict[str, str] = field(default_factory=dict)

    def get_double(self, name: str) -> float:
        return float(_strip_comment(self.fields[name]))

    def get_int(self, name: str) -> int:
        return int(_strip_comment(self.fields[name]))

    def get_str(self, name: str) -> str:
        # t.exp's "serr:" lines carry no trailing comment and may be empty.
        return self.fields.get(name, "")

    def get_double_array(self, prefix: str, count: int) -> list[float]:
        return [self.get_double(f"{prefix}[{i}]") for i in range(count)]


def _strip_comment(raw: str) -> str:
    hash_index = raw.find("#")
    return (raw[:hash_index] if hash_index >= 0 else raw).strip()


def read_iterations(path: str, wanted: set[tuple[int, int]]) -> list[Iteration]:
    result: list[Iteration] = []
    scope = "header"  # header -> suite -> testcase -> iteration
    suite_id = 0
    testcase_id = 0
    current: dict[str, str] | None = None
    testcase_wanted = False

    with open(path, "r", encoding="utf-8") as fh:
        for raw_line in fh:
            trimmed = raw_line.strip()
            if not trimmed or trimmed.startswith("#"):
                continue

            if trimmed == "TESTSUITE":
                scope = "suite"
                testcase_wanted = False
                current = None
                continue
            if trimmed == "TESTCASE":
                scope = "testcase"
                testcase_wanted = False
                current = None
                continue
            if trimmed == "ITERATION":
                scope = "iteration"
                if testcase_wanted:
                    current = {}
                    result.append(Iteration(suite_id, testcase_id, current))
                else:
                    current = None
                continue

            colon = raw_line.find(":")
            if colon < 0:
                continue
            name = raw_line[:colon].strip()
            value = raw_line[colon + 1 :].rstrip("\r\n").lstrip()

            if name == "section-id" and scope == "suite":
                suite_id = int(_strip_comment(value))
                continue
            if name == "section-id" and scope == "testcase":
                testcase_id = int(_strip_comment(value))
                testcase_wanted = (suite_id, testcase_id) in wanted
                continue

            if scope == "iteration" and current is not None:
                current[name] = value

    return result


def decode_hsys(raw: int) -> str:
    """Mirrors Tests/SwissEphNet.Conformance.Tests/Dispatch/HouseSystemCodec.cs."""
    if 32 <= raw < 128:
        return chr(raw)
    return chr(raw & 0xFF)


@dataclass
class CheckResult:
    key: str
    agreed: bool
    skipped_reason: str | None = None
    detail: str = ""
    max_abs_diff: float | None = None


def check_close(expected: float, actual: float, tol: float) -> bool:
    if expected != expected or actual != actual:  # NaN
        return False
    return abs(expected - actual) <= tol


def run_suite1_testcase1(iterations: list[Iteration], swe, tol: float) -> list[CheckResult]:
    """swe_calc(jd, ipl, iflag|iephe, xx, serr) -- 1.1.*."""
    results = []
    for it in iterations:
        key = f"1.1.{it.fields.get('section-id', '?')}"
        iflag = it.get_int("iflag")
        iephe = it.get_int("iephe")
        SEFLG_JPLEPH = 1
        if (iflag | iephe) & SEFLG_JPLEPH:
            results.append(CheckResult(key, agreed=False, skipped_reason="SEFLG_JPLEPH (no DE file shipped)"))
            continue

        jd = it.get_double("jd")
        ipl = it.get_int("ipl")
        exp_xx = it.get_double_array("xx", 6)
        exp_rc = it.get_int("rc")

        try:
            xx, retflag = swe.calc(jd, ipl, iflag | iephe)
            actual_rc = retflag
            ok_rc = actual_rc == exp_rc
            ok_xx = all(check_close(e, a, tol) for e, a in zip(exp_xx, xx))
            agreed = ok_rc and ok_xx
            max_diff = max((abs(e - a) for e, a in zip(exp_xx, xx)), default=0.0)
            detail = "" if agreed else f"rc: exp={exp_rc} got={actual_rc}; xx: exp={exp_xx} got={list(xx)}"
            results.append(CheckResult(key, agreed=agreed, detail=detail, max_abs_diff=max_diff))
        except Exception as ex:  # noqa: BLE001 -- reporting, not handling
            msg = str(ex)
            if "not found in PATH" in msg or "lower limit" in msg or "upper limit" in msg:
                # A supplementary data file this repo's ~4.2 MB core ephemeris
                # set does not ship (an asteroid orbital-element file, a
                # planetary-moon file, a JPL DE file), or a request outside
                # the date range the shipped era file covers -- not a
                # pyswisseph/SwissEphNet disagreement, a missing-data gap in
                # this sample environment.
                results.append(CheckResult(key, agreed=False, skipped_reason=f"missing supplementary data file ({msg.split(':')[0]})"))
            else:
                results.append(CheckResult(key, agreed=False, detail=f"exception: {ex}"))

    return results


def run_suite6_testcase1(iterations: list[Iteration], swe, tol: float) -> list[CheckResult]:
    """swe_houses(jd_ut, geolat, geolon, ihsy, cusps, ascmc) -- 6.1.*."""
    results = []
    for it in iterations:
        key = f"6.1.{it.fields.get('section-id', '?')}"
        jd_ut = it.get_double("jd") + it.get_double("ut") / 24.0
        raw_ihsy = it.get_int("ihsy")
        hsys = decode_hsys(raw_ihsy)
        geolat = it.get_double("geolat")
        geolon = it.get_double("geolon")
        exp_cusps = it.get_double_array("cusps", 13)  # cusps[0..12] -- see Suite06Houses.cs's cuspCount remarks
        exp_rc = it.get_int("rc")

        try:
            cusps, ascmc = swe.houses(jd_ut, geolat, geolon, hsys.encode("latin-1"))
            # pyswisseph's cusps tuple is 0-indexed at C's cusps[1]; cusps[0] in
            # t.exp (and SwissEphNet's cusps[0]) is always 0 (C never writes
            # index 0), so compare cusps[1..12] against pyswisseph's [0..11].
            ok = check_close(exp_cusps[0], 0.0, tol)
            for i in range(1, 13):
                ok = ok and check_close(exp_cusps[i], cusps[i - 1], tol)
            detail = "" if ok else f"cusps: exp={exp_cusps} got=(0.0,)+{cusps}"
            results.append(CheckResult(key, agreed=ok, detail=detail))
        except Exception as ex:  # noqa: BLE001
            agreed = exp_rc == -1
            results.append(CheckResult(key, agreed=agreed, detail=f"exception: {ex}" if not agreed else ""))

    return results


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ephe", default=None, help="Path to the ephemeris directory (default: external/swisseph/ephe next to this script's repo root)")
    parser.add_argument("--tolerance", type=float, default=1e-6, help="Absolute tolerance for numeric comparisons (default 1e-6)")
    args = parser.parse_args()

    try:
        import swisseph as swe
    except ImportError:
        print("pyswisseph is not installed. Run: pip install pyswisseph", file=sys.stderr)
        return 2

    repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    t_exp_path = os.path.join(repo_root, "external", "swisseph", "setest", "t.exp")
    if not os.path.isfile(t_exp_path):
        print(f"Could not find {t_exp_path}. Run 'git submodule update --init external/swisseph' first.", file=sys.stderr)
        return 2

    ephe_dir = args.ephe or os.path.join(repo_root, "external", "swisseph", "ephe")
    swe.set_ephe_path(ephe_dir)

    wanted = {(1, 1), (6, 1)}
    iterations = read_iterations(t_exp_path, wanted)
    suite1 = [it for it in iterations if it.suite == 1 and it.testcase == 1]
    suite6 = [it for it in iterations if it.suite == 6 and it.testcase == 1]

    print(f"pyswisseph version: {swe.version}")
    print(f"Sample: suite 1 testcase 1 (swe_calc, ET) = {len(suite1)} iterations; "
          f"suite 6 testcase 1 (swe_houses) = {len(suite6)} iterations")
    print(f"Tolerance: {args.tolerance}")
    print()

    all_results: list[CheckResult] = []
    all_results += run_suite1_testcase1(suite1, swe, args.tolerance)
    all_results += run_suite6_testcase1(suite6, swe, args.tolerance)

    skipped = [r for r in all_results if r.skipped_reason]
    compared = [r for r in all_results if not r.skipped_reason]
    agreed = [r for r in compared if r.agreed]
    disagreed = [r for r in compared if not r.agreed]

    print(f"Compared: {len(compared)}  Agreed: {len(agreed)}  Disagreed: {len(disagreed)}  Skipped: {len(skipped)}")
    if compared:
        rate = 100.0 * len(agreed) / len(compared)
        print(f"Agreement rate: {rate:.2f}%")

    if skipped:
        print()
        print(f"Skipped ({len(skipped)}):")
        by_reason: dict[str, int] = {}
        for r in skipped:
            by_reason[r.skipped_reason] = by_reason.get(r.skipped_reason, 0) + 1
        for reason, count in by_reason.items():
            print(f"  {count}x {reason}")

    if disagreed:
        print()
        print(f"Disagreements ({len(disagreed)}) -- a finding about pyswisseph or this script, not about SwissEphNet:")
        with_magnitude = [r for r in disagreed if r.max_abs_diff is not None]
        if with_magnitude:
            within_1e4 = sum(1 for r in with_magnitude if r.max_abs_diff <= 1e-4)
            within_1e5 = sum(1 for r in with_magnitude if r.max_abs_diff <= 1e-5)
            worst = max(r.max_abs_diff for r in with_magnitude)
            print(f"  Of {len(with_magnitude)} with a measurable magnitude: {within_1e5} within 1e-5, "
                  f"{within_1e4} within 1e-4, worst={worst:.3e}. All this small and this consistent points at a "
                  f"systematic source (a different default ephemeris file/era selection, or the pyswisseph build's "
                  f"own libm/toolchain), not a random defect -- see docs/known-issues.md's \"Cross-platform "
                  f"divergence\" section for a much larger sample (3.4M fields, Windows vs Linux) showing the same "
                  f"shape: most differences are small and tolerance absorbs the large majority of them.")
        for r in disagreed[:30]:
            print(f"  {r.key}: {r.detail}")
        if len(disagreed) > 30:
            print(f"  ... and {len(disagreed) - 30} more")

    # Vacuity floor: "Compared: 0" was previously indistinguishable from a genuine, fully-agreeing
    # run -- both print no disagreements and, before this check existed, both returned 0. A wrong
    # --ephe path produces exactly this: every iteration's swe.calc/swe.houses call raises the
    # "missing supplementary data file" exception this script already treats as a skip (see
    # run_suite1_testcase1's except clause), so `compared` silently empties out instead of failing
    # loudly. This script exists to say something about pyswisseph's agreement with t.exp; zero
    # comparisons is not that, and must not report the same exit code as 3,508 agreeing ones.
    if not compared:
        print(
            "FAIL: zero iterations were actually compared (Compared: 0). This usually means --ephe "
            "pointed at the wrong directory (every iteration skipped as a missing supplementary "
            "data file), external/swisseph/setest/t.exp does not contain the expected suite "
            "1/testcase 1 or suite 6/testcase 1 sample, or pyswisseph itself is misconfigured. "
            "Fix the underlying cause and re-run -- do not treat a passing exit code here as a real "
            "agreement result.",
            file=sys.stderr,
        )
        return 2

    return 0 if not disagreed else 1


if __name__ == "__main__":
    raise SystemExit(main())
