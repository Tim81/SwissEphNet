#!/usr/bin/env python3
"""Replay the eleven local/mixed characterization-baseline areas through pyswisseph.

Why this exists: Tests/baseline/baseline-2.8.0.2.env.txt records eleven areas
(ayanamsa, datetime, house-pos [mixed], pheno-ast, eclipse, risetrans, atmo, orbit,
gauquelin, astromodels, calc-defaulteph) as "local" or "mixed" provenance -- their
content proves only "unchanged since the day it was written", not "correct". This
script replays each area's rows through pyswisseph 2.10.3.2, which bundles
Astrodienst's own libswe 2.10.03 (the same upstream version this port's submodule
is pinned to under external/swisseph), and classifies each row:

  1. AGREE    -- bit-exact or within tolerance. The row was right and stayed right.
  2. DISAGREE -- numerically or structurally different from what pyswisseph's real
                 C computed for the identical input.
  3. SKIP     -- not reconstructible from the case id, or pyswisseph's Python
                 binding cannot express the same call shape (a binding gap, not a
                 port defect).

`astromodels` is explicitly out of scope: pyswisseph exposes the MOD_*/MODEL_*
constants but binds none of set_astro_models, get_astro_models,
set_interpolate_nut, so there is no call to make on the Python side at all.

pyswisseph is 2.10.03; the port is 2.08. A DISAGREE therefore does not by itself
mean the port is wrong -- it may be a deliberate 2.08 behaviour 2.10.03 changed.
This script does not attempt that classification automatically (it would need to
cross-reference docs/known-issues.md and Tests/conformance/known-fail.tsv, which
are keyed to setest/t.exp testcases, not to these baseline case ids); it reports
AGREE/DISAGREE/SKIP with enough detail (magnitude, message text) for a human to
finish the classification, the same shape scripts/validate-pyswisseph.py already
uses for its own disagreements.

File-availability note: Tools/BaselineGen never calls swe_set_ephe_path (see its
own header comment and Tools/BaselineMatrix/*.cs's repeated "no OnLoadFile handler
is ever subscribed"), so every baseline row was generated against SwissEphNet's
unresolved default path, the literal constant "[ephe]" (SwissEph.SE_EPHE_PATH).
To compare like for like, this script points pyswisseph at a freshly created EMPTY
directory, not the real external/swisseph/ephe/ 8-file checkout -- using the real
files would answer a different question ("does real Swiss Ephemeris find files
that were never available to the C# baseline run") and would make file-lookup
areas (pheno-ast, and the file-dependent rows in calc-defaulteph/orbit/gauquelin)
incomparable to what the baseline actually recorded.

Usage:
    python scripts/validate-seeded-areas.py [--area NAME ...] [--tolerance FLOAT]
                                             [--baseline-dir PATH] [--max-detail N]

Requires: pyswisseph (pip install pyswisseph).
"""

from __future__ import annotations

import argparse
import math
import os
import re
import shutil
import sys
import tempfile
from dataclasses import dataclass, field


# ---------------------------------------------------------------------------
# Flag / constant values, copied from SwissEphNet/SwissEph.swephexp.h.cs so this
# script does not depend on pyswisseph's own attribute names matching 1:1 (it
# mostly does, but the case ids only ever carry resolved ints or the flag NAMES
# BaselineMatrix used -- so those names need mapping to numbers somewhere, and
# doing it from the same header both sides transliterate is more auditable than
# trusting two independent naming schemes to agree by construction).
# ---------------------------------------------------------------------------

SEFLG_JPLEPH = 1
SEFLG_SWIEPH = 2
SEFLG_MOSEPH = 4
SEFLG_HELCTR = 8
SEFLG_TRUEPOS = 16
SEFLG_J2000 = 32
SEFLG_NONUT = 64
SEFLG_SPEED = 256
SEFLG_NOGDEFL = 512
SEFLG_NOABERR = 1024
SEFLG_EQUATORIAL = 2 * 1024
SEFLG_XYZ = 4 * 1024
SEFLG_RADIANS = 8 * 1024
SEFLG_BARYCTR = 16 * 1024
SEFLG_TOPOCTR = 32 * 1024
SEFLG_SIDEREAL = 64 * 1024

SE_ECL_TOTAL = 4
SE_TRUE_TO_APP = 0
SE_APP_TO_TRUE = 1
SE_CALC_RISE = 1
SE_CALC_SET = 2
SE_CALC_MTRANSIT = 4
SE_CALC_ITRANSIT = 8
SE_BIT_DISC_CENTER = 256
SE_BIT_DISC_BOTTOM = 8192
SE_BIT_NO_REFRACTION = 512
SE_BIT_GEOCTR_NO_ECL_LAT = 128
SE_BIT_HINDU_RISING = SE_BIT_DISC_CENTER | SE_BIT_NO_REFRACTION | SE_BIT_GEOCTR_NO_ECL_LAT
SE_BIT_CIVIL_TWILIGHT = 1024
SE_BIT_NAUTIC_TWILIGHT = 2048
SE_BIT_ASTRO_TWILIGHT = 4096
SE_BIT_FIXED_DISC_SIZE = 16384

# Grids.CalcIflagCombos, Tools/BaselineMatrix/Grids.cs -- name -> flag value,
# copied verbatim (order does not matter here, only the mapping does).
CALC_IFLAG_COMBOS = {
    "0": 0,
    "SPEED": SEFLG_SPEED,
    "EQUATORIAL": SEFLG_EQUATORIAL,
    "XYZ": SEFLG_XYZ,
    "J2000": SEFLG_J2000,
    "HELCTR": SEFLG_HELCTR,
    "TRUEPOS": SEFLG_TRUEPOS,
    "RADIANS": SEFLG_RADIANS,
    "NONUT": SEFLG_NONUT,
    "NOABERR": SEFLG_NOABERR,
    "NOGDEFL": SEFLG_NOGDEFL,
    "BARYCTR": SEFLG_BARYCTR,
    "SIDEREAL": SEFLG_SIDEREAL,
    "SPEED_EQUATORIAL": SEFLG_SPEED | SEFLG_EQUATORIAL,
    "SPEED_XYZ": SEFLG_SPEED | SEFLG_XYZ,
    "J2000_EQUATORIAL": SEFLG_J2000 | SEFLG_EQUATORIAL,
    "HELCTR_SPEED": SEFLG_HELCTR | SEFLG_SPEED,
}

ORBIT_IFLAG_COMBOS = {
    "MOSEPH": SEFLG_MOSEPH,
    "MOSEPH_HELCTR": SEFLG_MOSEPH | SEFLG_HELCTR,
    "MOSEPH_BARYCTR": SEFLG_MOSEPH | SEFLG_BARYCTR,
}

RISETRANS_RSMI = {"RISE": SE_CALC_RISE, "SET": SE_CALC_SET, "MTRANSIT": SE_CALC_MTRANSIT, "ITRANSIT": SE_CALC_ITRANSIT}

RISETRANS_BITS = {
    "DISC_CENTER": SE_BIT_DISC_CENTER,
    "DISC_BOTTOM": SE_BIT_DISC_BOTTOM,
    "NO_REFRACTION": SE_BIT_NO_REFRACTION,
    "HINDU_RISING": SE_BIT_HINDU_RISING,
    "CIVIL_TWILIGHT": SE_BIT_CIVIL_TWILIGHT,
    "NAUTIC_TWILIGHT": SE_BIT_NAUTIC_TWILIGHT,
    "ASTRO_TWILIGHT": SE_BIT_ASTRO_TWILIGHT,
    "FIXED_DISC_SIZE": SE_BIT_FIXED_DISC_SIZE,
    "GEOCTR_NO_ECL_LAT": SE_BIT_GEOCTR_NO_ECL_LAT,
}

REFRAC_FLAGS = {"TRUE_TO_APP": SE_TRUE_TO_APP, "APP_TO_TRUE": SE_APP_TO_TRUE}

AYANAMSA_EX_FLAGS = {"0": 0, "NONUT": SEFLG_NONUT}

DELTAT_EX_FLAGS = {"MINUS1": -1, "SWIEPH": SEFLG_SWIEPH, "MOSEPH": SEFLG_MOSEPH}


# ---------------------------------------------------------------------------
# Row parsing. Mirrors Tools/BaselineMatrix/Format.cs: tab-separated, case id
# first field, then value fields. S()-escaped fields (\\, \t, \r, \n) are
# unescaped when the value is used as literal text (e.g. a serr comparison).
# ---------------------------------------------------------------------------


def unescape(value: str) -> str:
    # Reverse of Format.S(): order matters, \\ must be restored last.
    return (
        value.replace("\\t", "\t")
        .replace("\\r", "\r")
        .replace("\\n", "\n")
        .replace("\\\\", "\\")
    )


def read_rows(path: str) -> list[tuple[list[str], list[str]]]:
    """Returns a list of (case_id_parts, value_fields) per row."""
    rows = []
    with open(path, "r", encoding="utf-8") as fh:
        for line in fh:
            line = line.rstrip("\n")
            if not line:
                continue
            parts = line.split("\t")
            case_id = parts[0]
            fields = parts[1:]
            rows.append((case_id.split("|"), fields))
    return rows


def to_float(value: str) -> float:
    if value == "NaN":
        return math.nan
    return float(value)


# ---------------------------------------------------------------------------
# Comparison plumbing.
# ---------------------------------------------------------------------------


@dataclass
class RowResult:
    case_id: str
    status: str  # "AGREE" | "DISAGREE" | "SKIP"
    reason: str = ""
    detail: str = ""
    max_abs_diff: float | None = None


def close(expected: float, actual: float, tol: float) -> bool:
    if math.isnan(expected) and math.isnan(actual):
        return True
    if math.isnan(expected) or math.isnan(actual):
        return False
    return abs(expected - actual) <= tol


_PATH_TOKEN_RE = re.compile(r"PATH '[^']*'")
_BINDING_PREFIX_RE = re.compile(r"^swisseph\.\w+: ")


def normalize_message(msg: str) -> str:
    """Strips the ephemeris-path token and pyswisseph's own 'swisseph.func: '
    exception-message prefix, so a message can be compared across the two
    bindings without the placeholder-vs-real-scratch-dir noise or pyswisseph's
    extra prefix drowning out a real text difference."""
    msg = _BINDING_PREFIX_RE.sub("", msg)
    msg = _PATH_TOKEN_RE.sub("PATH '<ephe>'", msg)
    return msg.strip()


def compare_message(expected: str, actual: str) -> bool:
    return normalize_message(expected) == normalize_message(actual)


# ---------------------------------------------------------------------------
# Per-area replay functions. Each takes (rows, swe, tol) and yields RowResult.
# Every pyswisseph call is wrapped individually so one bad row cannot abort a
# whole area's run.
# ---------------------------------------------------------------------------


def replay_ayanamsa(rows, swe, tol):
    for parts, fields in rows:
        prefix = parts[0]
        case_id = "|".join(parts)
        try:
            if prefix in ("AY", "AYUT"):
                sid_mode = int(parts[1])
                jd = to_float(parts[2])
                swe.set_sid_mode(sid_mode, 0, 0)
                value = swe.get_ayanamsa_ut(jd) if prefix == "AYUT" else swe.get_ayanamsa(jd)
                expected = to_float(fields[0])
                if close(expected, value, tol):
                    yield RowResult(case_id, "AGREE")
                else:
                    yield RowResult(case_id, "DISAGREE", detail=f"exp={expected} got={value}", max_abs_diff=abs(expected - value))
            elif prefix in ("AYEX", "AYEXUT"):
                sid_mode = int(parts[1])
                jd = to_float(parts[2])
                flag_name = parts[3]
                flag = AYANAMSA_EX_FLAGS[flag_name]
                swe.set_sid_mode(sid_mode, 0, 0)
                # pyswisseph's get_ayanamsa_ex[_ut] returns (retflags, daya) with
                # no serr -- the baseline's third field (serr) is not comparable.
                if prefix == "AYEXUT":
                    retflags, daya = swe.get_ayanamsa_ex_ut(jd, flag)
                else:
                    retflags, daya = swe.get_ayanamsa_ex(jd, flag)
                exp_retc = int(fields[0])
                exp_daya = to_float(fields[1])
                ok = (retflags == exp_retc) and close(exp_daya, daya, tol)
                if ok:
                    yield RowResult(case_id, "AGREE")
                else:
                    yield RowResult(
                        case_id, "DISAGREE",
                        detail=f"retc: exp={exp_retc} got={retflags}; daya: exp={exp_daya} got={daya} (serr not comparable via pyswisseph)",
                        max_abs_diff=abs(exp_daya - daya),
                    )
            elif prefix == "AYUSER":
                t0 = to_float(parts[1])
                ayan_t0 = to_float(parts[2])
                jd = to_float(parts[3])
                use_ut = parts[4] == "True"
                swe.set_sid_mode(255, t0, ayan_t0)  # SE_SIDM_USER = 255
                value = swe.get_ayanamsa_ut(jd) if use_ut else swe.get_ayanamsa(jd)
                expected = to_float(fields[0])
                if close(expected, value, tol):
                    yield RowResult(case_id, "AGREE")
                else:
                    yield RowResult(case_id, "DISAGREE", detail=f"exp={expected} got={value}", max_abs_diff=abs(expected - value))
            elif prefix == "AYBIT":
                sid_mode = int(parts[1])
                bit_name = parts[2]
                jd = to_float(parts[3])
                use_ut = parts[4] == "True"
                bit = {"ECL_T0": 256, "SSY_PLANE": 512}[bit_name]
                swe.set_sid_mode(sid_mode | bit, 0, 0)
                value = swe.get_ayanamsa_ut(jd) if use_ut else swe.get_ayanamsa(jd)
                expected = to_float(fields[0])
                if close(expected, value, tol):
                    yield RowResult(case_id, "AGREE")
                else:
                    yield RowResult(case_id, "DISAGREE", detail=f"exp={expected} got={value}", max_abs_diff=abs(expected - value))
            else:
                yield RowResult(case_id, "SKIP", reason=f"unrecognized prefix {prefix}")
        except Exception as ex:  # noqa: BLE001
            yield RowResult(case_id, "DISAGREE", detail=f"pyswisseph raised where the baseline has a numeric row: {type(ex).__name__}: {ex}")


def replay_datetime(rows, swe, tol):
    # The C# baseline gives every row a brand-new SwissEph() instance (see
    # Tools/BaselineGen/Program.cs's header comment), so TIDACC/DTUSERDEF rows
    # can never affect any other row. pyswisseph has no per-call instance --
    # set_tid_acc/set_delta_t_userdef mutate global C state that persists for
    # the rest of the process. Reset both to "automatic" before every row, not
    # just after TIDACC/DTUSERDEF ones: skipping this reset was tried first and
    # silently corrupted every JD/JU/JUT1/RJ/ST/ST0/TE/UJ/UTZ row from
    # DTUSERDEF onward (case ids sort alphabetically before all of those), each
    # one running under whatever delta-T override the last DTUSERDEF row left
    # behind -- not a genuine port-vs-pyswisseph disagreement, a test-harness bug.
    swe.set_tid_acc(swe.TIDAL_AUTOMATIC)
    swe.set_delta_t_userdef(swe.DELTAT_AUTOMATIC)
    for parts, fields in rows:
        prefix = parts[0]
        case_id = "|".join(parts)
        if prefix != "TIDACC":
            swe.set_tid_acc(swe.TIDAL_AUTOMATIC)
        if prefix != "DTUSERDEF":
            swe.set_delta_t_userdef(swe.DELTAT_AUTOMATIC)
        try:
            if prefix == "JD":
                y, m, d = int(parts[1]), int(parts[2]), int(parts[3])
                h = to_float(parts[4])
                greg = int(parts[5])
                jd = swe.julday(y, m, d, h, greg)
                expected = to_float(fields[0])
                yield _num1(case_id, expected, jd, tol)
            elif prefix == "RJ":
                jd = to_float(parts[1])
                greg = int(parts[2])
                y, m, d, h = swe.revjul(jd, greg)
                exp = [int(fields[0]), int(fields[1]), int(fields[2]), to_float(fields[3])]
                got = [y, m, d, h]
                yield _numlist(case_id, exp, got, tol)
            elif prefix == "DC":
                y, m, d = int(parts[1]), int(parts[2]), int(parts[3])
                h = to_float(parts[4])
                cal = parts[5].encode("latin-1")
                isvalid, tjd, _dt = swe.date_conversion(y, m, d, h, cal=cal)
                exp_retc = int(fields[0])
                exp_tjd = to_float(fields[1])
                got_retc = 0 if isvalid else -1
                ok = (got_retc == exp_retc) and close(exp_tjd, tjd, tol)
                if ok:
                    yield RowResult(case_id, "AGREE")
                else:
                    yield RowResult(case_id, "DISAGREE", detail=f"retc exp={exp_retc} got={got_retc}; tjd exp={exp_tjd} got={tjd}", max_abs_diff=abs(exp_tjd - tjd))
            elif prefix == "DT":
                jd = to_float(parts[1])
                value = swe.deltat(jd)
                yield _num1(case_id, to_float(fields[0]), value, tol)
            elif prefix == "DTEX":
                jd = to_float(parts[1])
                flag = DELTAT_EX_FLAGS[parts[2]]
                value = swe.deltat_ex(jd, flag)
                # pyswisseph's deltat_ex returns only the double; the baseline's
                # serr field is not comparable (no serr output on this binding).
                yield _num1(case_id, to_float(fields[0]), value, tol, note="serr not comparable via pyswisseph")
            elif prefix == "TIDACC":
                tidacc = to_float(parts[1])
                jd = to_float(parts[2])
                swe.set_tid_acc(tidacc)
                got = swe.get_tid_acc()
                deltat = swe.deltat(jd)
                exp = [to_float(fields[0]), to_float(fields[1])]
                yield _numlist(case_id, exp, [got, deltat], tol)
            elif prefix == "DTUSERDEF":
                dt = to_float(parts[1])
                jd = to_float(parts[2])
                swe.set_delta_t_userdef(dt)
                value = swe.deltat(jd)
                yield _num1(case_id, to_float(fields[0]), value, tol)
            elif prefix == "ST":
                jd = to_float(parts[1])
                value = swe.sidtime(jd)
                yield _num1(case_id, to_float(fields[0]), value, tol)
            elif prefix == "ST0":
                jd, ecl, nut = to_float(parts[1]), to_float(parts[2]), to_float(parts[3])
                value = swe.sidtime0(jd, ecl, nut)
                yield _num1(case_id, to_float(fields[0]), value, tol)
            elif prefix == "TE":
                jd = to_float(parts[1])
                e = swe.time_equ(jd)
                # pyswisseph returns only e; baseline's retc/serr are not comparable.
                yield _num1(case_id, to_float(fields[1]), e, tol, note="retc/serr not comparable via pyswisseph")
            elif prefix == "JU":
                jd = to_float(parts[1])
                greg = int(parts[2])
                y, m, d, h, mi, s = swe.jdet_to_utc(jd, greg)
                exp = [int(fields[0]), int(fields[1]), int(fields[2]), int(fields[3]), int(fields[4]), to_float(fields[5])]
                yield _numlist(case_id, exp, [y, m, d, h, mi, s], tol)
            elif prefix == "UJ":
                y, m, d, h, mi = int(parts[1]), int(parts[2]), int(parts[3]), int(parts[4]), int(parts[5])
                s = to_float(parts[6])
                greg = int(parts[7])
                jdet, jdut = swe.utc_to_jd(y, m, d, h, mi, s, greg)
                exp = [to_float(fields[1]), to_float(fields[2])]
                yield _numlist(case_id, exp, [jdet, jdut], tol, note="retc/serr not comparable via pyswisseph")
            elif prefix == "JUT1":
                jd = to_float(parts[1])
                greg = int(parts[2])
                y, m, d, h, mi, s = swe.jdut1_to_utc(jd, greg)
                exp = [int(fields[0]), int(fields[1]), int(fields[2]), int(fields[3]), int(fields[4]), to_float(fields[5])]
                yield _numlist(case_id, exp, [y, m, d, h, mi, s], tol)
            elif prefix == "UTZ":
                y, m, d, h, mi = int(parts[1]), int(parts[2]), int(parts[3]), int(parts[4]), int(parts[5])
                s = to_float(parts[6])
                tz = to_float(parts[7])
                yo, mo, do, ho, mio, so = swe.utc_time_zone(y, m, d, h, mi, s, tz)
                exp = [int(fields[0]), int(fields[1]), int(fields[2]), int(fields[3]), int(fields[4]), to_float(fields[5])]
                yield _numlist(case_id, exp, [yo, mo, do, ho, mio, so], tol)
            else:
                yield RowResult(case_id, "SKIP", reason=f"unrecognized prefix {prefix}")
        except Exception as ex:  # noqa: BLE001
            yield RowResult(case_id, "DISAGREE", detail=f"pyswisseph raised where the baseline has a numeric row: {type(ex).__name__}: {ex}")


def _num1(case_id, expected, got, tol, note=""):
    if close(expected, got, tol):
        return RowResult(case_id, "AGREE", detail=note)
    return RowResult(case_id, "DISAGREE", detail=f"exp={expected} got={got}" + (f" ({note})" if note else ""), max_abs_diff=abs(expected - got) if not math.isnan(expected) and not math.isnan(got) else None)


def _numlist(case_id, expected, got, tol, note=""):
    diffs = [abs(e - g) for e, g in zip(expected, got) if not math.isnan(e) and not math.isnan(g)]
    ok = all(close(e, g, tol) for e, g in zip(expected, got))
    if ok:
        return RowResult(case_id, "AGREE", detail=note)
    return RowResult(case_id, "DISAGREE", detail=f"exp={expected} got={got}" + (f" ({note})" if note else ""), max_abs_diff=max(diffs) if diffs else None)


def replay_house_pos(rows, swe, tol):
    for parts, fields in rows:
        prefix = parts[0]
        case_id = "|".join(parts)
        if prefix == "HN":
            hsys = parts[1]
            try:
                name = swe.house_name(hsys.encode("latin-1"))
                expected = fields[0]
                if name == expected:
                    yield RowResult(case_id, "AGREE")
                else:
                    yield RowResult(case_id, "DISAGREE", detail=f"exp={expected!r} got={name!r}")
            except Exception as ex:  # noqa: BLE001
                yield RowResult(case_id, "DISAGREE", detail=f"raised: {type(ex).__name__}: {ex}")
            continue

        if prefix != "HP":
            yield RowResult(case_id, "SKIP", reason=f"unrecognized prefix {prefix}")
            continue

        default_eps = "23.4392911"
        if len(parts) == 6:
            hsys, armc_s, geolat_s, lon_s, lat_s = parts[1:6]
            eps_s = default_eps
        else:
            hsys, eps_s, armc_s, geolat_s, lon_s, lat_s = parts[1:7]

        armc, geolat, eps, lon, lat = (to_float(x) for x in (armc_s, geolat_s, eps_s, lon_s, lat_s))
        exp_pos = to_float(fields[0])
        try:
            pos = swe.house_pos(armc, geolat, eps, [lon, lat], hsys=hsys.encode("latin-1"))
            if close(exp_pos, pos, tol):
                yield RowResult(case_id, "AGREE")
            else:
                yield RowResult(
                    case_id, "DISAGREE",
                    detail=f"exp={exp_pos} got={pos}",
                    max_abs_diff=abs(exp_pos - pos) if not math.isnan(exp_pos) and not math.isnan(pos) else None,
                )
        except Exception as ex:  # noqa: BLE001
            yield RowResult(case_id, "DISAGREE", detail=f"exp={exp_pos} (no exception in baseline) but pyswisseph raised {type(ex).__name__}: {ex}")


def _pheno_like(prefix_ok, case_id, ipl, jd, fields, swe, tol):
    exp_retc = int(fields[0])
    exp_attr = [to_float(f) for f in fields[1:7]]
    exp_serr = unescape(fields[7]) if len(fields) > 7 else ""
    try:
        attr, retflags = swe.pheno(jd, ipl, SEFLG_MOSEPH)
        # pyswisseph succeeded; baseline expects ERR (-1) for every pheno-ast row.
        ok = exp_retc >= 0 and all(close(e, a, tol) for e, a in zip(exp_attr, attr[:6]))
        if ok:
            return RowResult(case_id, "AGREE")
        return RowResult(case_id, "DISAGREE", detail=f"exp retc={exp_retc} serr={exp_serr!r}; pyswisseph SUCCEEDED with attr={attr}")
    except Exception as ex:  # noqa: BLE001
        if exp_retc < 0 and compare_message(exp_serr, str(ex)):
            return RowResult(case_id, "AGREE")
        if exp_retc < 0:
            return RowResult(case_id, "DISAGREE", detail=f"both error, message differs: exp={exp_serr!r} got={normalize_message(str(ex))!r}")
        return RowResult(case_id, "DISAGREE", detail=f"exp retc={exp_retc} (success) but pyswisseph raised {type(ex).__name__}: {ex}")


def replay_pheno_ast(rows, swe, tol):
    for parts, fields in rows:
        prefix = parts[0]
        case_id = "|".join(parts)
        if prefix not in ("PHAFILE", "PHACHIRON"):
            yield RowResult(case_id, "SKIP", reason=f"unrecognized prefix {prefix}")
            continue
        ipl = int(parts[1])
        jd = to_float(parts[2])
        yield _pheno_like(prefix, case_id, ipl, jd, fields, swe, tol)


def _geopos(s: str) -> list[float]:
    return [to_float(x) for x in s.split(",")]


def replay_eclipse(rows, swe, tol):
    for parts, fields in rows:
        prefix = parts[0]
        case_id = "|".join(parts)
        try:
            if prefix == "SEW":
                jd = to_float(parts[1])
                retflags, geopos, attr = swe.sol_eclipse_where(jd, SEFLG_MOSEPH)
                exp = [int(fields[0]), to_float(fields[1]), to_float(fields[2]), to_float(fields[3]), to_float(fields[4]), to_float(fields[5]), to_float(fields[6])]
                got = [retflags, geopos[0], geopos[1], attr[0], attr[1], attr[2], attr[3]]
                yield _numlist(case_id, exp, got, tol)
            elif prefix == "SEH":
                jd = to_float(parts[1])
                geopos = _geopos(parts[2])
                retflags, attr = swe.sol_eclipse_how(jd, geopos, SEFLG_MOSEPH)
                exp = [int(fields[0])] + [to_float(f) for f in fields[1:8]]
                got = [retflags] + list(attr[0:7])
                yield _numlist(case_id, exp, got, tol)
            elif prefix == "LEH":
                jd = to_float(parts[1])
                geopos_label = parts[2]
                if geopos_label == "NULL":
                    yield RowResult(case_id, "SKIP", reason="pyswisseph's lun_eclipse_how requires a real geopos sequence and raises TypeError for None/empty; the baseline's NULL-geopos call cannot be reconstructed via this binding")
                    continue
                geopos = _geopos(geopos_label)
                retflags, attr = swe.lun_eclipse_how(jd, geopos, SEFLG_MOSEPH)
                exp = [int(fields[0])] + [to_float(f) for f in fields[1:8]]
                got = [retflags] + list(attr[0:7])
                yield _numlist(case_id, exp, got, tol)
            elif prefix == "LOW":
                ipl = int(parts[1])
                jd = to_float(parts[2])
                retflags, geopos, attr = swe.lun_occult_where(jd, ipl, SEFLG_MOSEPH)
                exp = [int(fields[0]), to_float(fields[1]), to_float(fields[2]), to_float(fields[3]), to_float(fields[4]), to_float(fields[5]), to_float(fields[6])]
                got = [retflags, geopos[0], geopos[1], attr[0], attr[1], attr[2], attr[3]]
                yield _numlist(case_id, exp, got, tol)
            elif prefix == "SWG":
                start_jd = to_float(parts[1])
                iflType = 0 if parts[2] == "ANY" else SE_ECL_TOTAL
                backward = parts[3] == "True"
                retc, tret = swe.sol_eclipse_when_glob(start_jd, SEFLG_MOSEPH, iflType, backward)
                exp = [int(fields[0]), to_float(fields[1]), to_float(fields[2]), to_float(fields[3])]
                got = [retc, tret[0], tret[2], tret[3]]
                yield _numlist(case_id, exp, got, tol)
            elif prefix == "SWL":
                start_jd = to_float(parts[1])
                geopos = _geopos(parts[2])
                backward = parts[3] == "True"
                retflags, tret, attr = swe.sol_eclipse_when_loc(start_jd, geopos, SEFLG_MOSEPH, backward)
                exp = [int(fields[0]), to_float(fields[1]), to_float(fields[2]), to_float(fields[3]), to_float(fields[4])]
                got = [retflags, tret[0], tret[2], tret[3], attr[0]]
                yield _numlist(case_id, exp, got, tol)
            elif prefix == "LEW":
                start_jd = to_float(parts[1])
                iflType = 0 if parts[2] == "ANY" else SE_ECL_TOTAL
                backward = parts[3] == "True"
                retflag, tret = swe.lun_eclipse_when(start_jd, SEFLG_MOSEPH, iflType, backward)
                exp = [int(fields[0]), to_float(fields[1]), to_float(fields[2]), to_float(fields[3])]
                got = [retflag, tret[0], tret[2], tret[3]]
                yield _numlist(case_id, exp, got, tol)
            elif prefix == "LWL":
                start_jd = to_float(parts[1])
                geopos = _geopos(parts[2])
                backward = parts[3] == "True"
                retflag, tret, attr = swe.lun_eclipse_when_loc(start_jd, geopos, SEFLG_MOSEPH, backward)
                exp = [int(fields[0]), to_float(fields[1]), to_float(fields[2]), to_float(fields[3]), to_float(fields[4])]
                got = [retflag, tret[0], tret[2], tret[3], attr[0]]
                yield _numlist(case_id, exp, got, tol)
            elif prefix == "LOG":
                ipl = int(parts[1])
                start_jd = to_float(parts[2])
                backward = parts[3] == "True"
                retflags, tret = swe.lun_occult_when_glob(start_jd, ipl, SEFLG_MOSEPH, 0, backward)
                exp = [int(fields[0]), to_float(fields[1]), to_float(fields[2]), to_float(fields[3])]
                got = [retflags, tret[0], tret[2], tret[3]]
                yield _numlist(case_id, exp, got, tol)
            elif prefix == "LOL":
                ipl = int(parts[1])
                start_jd = to_float(parts[2])
                backward = parts[3] == "True"
                geopos = [0.0, 51.5, 0.0]
                retflags, tret, attr = swe.lun_occult_when_loc(start_jd, ipl, geopos, SEFLG_MOSEPH, backward)
                exp = [int(fields[0]), to_float(fields[1]), to_float(fields[2]), to_float(fields[3]), to_float(fields[4])]
                got = [retflags, tret[0], tret[2], tret[3], attr[0]]
                yield _numlist(case_id, exp, got, tol)
            else:
                yield RowResult(case_id, "SKIP", reason=f"unrecognized prefix {prefix}")
        except Exception as ex:  # noqa: BLE001
            exp_retc = int(fields[0]) if fields and fields[0].lstrip("-").isdigit() else None
            exp_serr = unescape(fields[-1]) if fields else ""
            if exp_retc is not None and exp_retc < 0 and compare_message(exp_serr, str(ex)):
                yield RowResult(case_id, "AGREE")
            elif exp_retc is not None and exp_retc < 0:
                yield RowResult(case_id, "DISAGREE", detail=f"both error, message differs: exp={exp_serr!r} got={normalize_message(str(ex))!r}")
            else:
                yield RowResult(case_id, "DISAGREE", detail=f"pyswisseph raised: {type(ex).__name__}: {ex}")


def replay_risetrans(rows, swe, tol):
    for parts, fields in rows:
        prefix = parts[0]
        case_id = "|".join(parts)
        try:
            if prefix == "RT":
                ipl = int(parts[1])
                jd = to_float(parts[2])
                rsmi = RISETRANS_RSMI[parts[3]]
                geopos = _geopos(parts[4])
                retc, tret = swe.rise_trans(jd, ipl, rsmi, geopos, 0.0, 0.0, SEFLG_MOSEPH)
                exp = [int(fields[0]), to_float(fields[1])]
                got = [retc, tret[0]]
                yield _numlist(case_id, exp, got, tol, note="serr not comparable via pyswisseph")
            elif prefix == "RTBIT":
                ipl = int(parts[1])
                jd = to_float(parts[2])
                base_rsmi = RISETRANS_RSMI[parts[3]]
                bit = RISETRANS_BITS[parts[4]]
                geopos = [0.0, 51.5, 0.0]
                retc, tret = swe.rise_trans(jd, ipl, base_rsmi | bit, geopos, 0.0, 0.0, SEFLG_MOSEPH)
                exp = [int(fields[0]), to_float(fields[1])]
                got = [retc, tret[0]]
                yield _numlist(case_id, exp, got, tol, note="serr not comparable via pyswisseph")
            elif prefix == "RTATM":
                ipl = int(parts[1])
                jd = to_float(parts[2])
                atpress, attemp = (to_float(x) for x in parts[3].split(","))
                geopos = [0.0, 51.5, 0.0]
                retc, tret = swe.rise_trans(jd, ipl, SE_CALC_RISE, geopos, atpress, attemp, SEFLG_MOSEPH)
                exp = [int(fields[0]), to_float(fields[1])]
                got = [retc, tret[0]]
                yield _numlist(case_id, exp, got, tol, note="serr not comparable via pyswisseph")
            elif prefix == "RTH":
                ipl = int(parts[1])
                jd = to_float(parts[2])
                rsmi = RISETRANS_RSMI[parts[3]]
                horhgt = to_float(parts[4])
                geopos = [0.0, 51.5, 0.0]
                retc, tret = swe.rise_trans_true_hor(jd, ipl, rsmi, geopos, 0.0, 0.0, horhgt, SEFLG_MOSEPH)
                exp = [int(fields[0]), to_float(fields[1])]
                got = [retc, tret[0]]
                yield _numlist(case_id, exp, got, tol, note="serr not comparable via pyswisseph")
            else:
                yield RowResult(case_id, "SKIP", reason=f"unrecognized prefix {prefix}")
        except Exception as ex:  # noqa: BLE001
            exp_retc = int(fields[0])
            if exp_retc < 0:
                yield RowResult(case_id, "AGREE", detail="both error (message not comparable)")
            else:
                yield RowResult(case_id, "DISAGREE", detail=f"exp retc={exp_retc} but pyswisseph raised {type(ex).__name__}: {ex}")


def replay_atmo(rows, swe, tol):
    for parts, fields in rows:
        prefix = parts[0]
        case_id = "|".join(parts)
        try:
            if prefix == "REFR":
                inalt, atpress, attemp = (to_float(x) for x in parts[1:4])
                flag = REFRAC_FLAGS[parts[4]]
                result = swe.refrac(inalt, atpress, attemp, flag)
                yield _num1(case_id, to_float(fields[0]), result, tol)
            elif prefix == "REFX":
                inalt, geoalt, atpress, attemp, lapse_rate = (to_float(x) for x in parts[1:6])
                flag = REFRAC_FLAGS[parts[6]]
                result, dret = swe.refrac_extended(inalt, geoalt, atpress, attemp, lapse_rate, flag)
                exp = [to_float(f) for f in fields[0:5]]
                got = [result, dret[0], dret[1], dret[2], dret[3]]
                yield _numlist(case_id, exp, got, tol)
            elif prefix == "LAPSEDIRECT":
                lapse_rate = to_float(parts[1])
                swe.set_lapse_rate(lapse_rate)
                result, dret = swe.refrac_extended(1.0, 0.0, 1013.25, 15.0, lapse_rate, SE_TRUE_TO_APP)
                exp = [to_float(f) for f in fields[0:5]]
                got = [result, dret[0], dret[1], dret[2], dret[3]]
                yield _numlist(case_id, exp, got, tol)
            elif prefix == "LAPSERISE":
                jd = to_float(parts[1])
                lapse_rate = to_float(parts[2])
                swe.set_lapse_rate(lapse_rate)
                geopos = [0.0, 51.5, 0.0]
                retc, tret = swe.rise_trans_true_hor(jd, 0, SE_CALC_RISE, geopos, 0.0, 0.0, 0.0, SEFLG_MOSEPH)
                exp = [int(fields[0]), to_float(fields[1])]
                got = [retc, tret[0]]
                yield _numlist(case_id, exp, got, tol, note="serr not comparable via pyswisseph")
            else:
                yield RowResult(case_id, "SKIP", reason=f"unrecognized prefix {prefix}")
        except Exception as ex:  # noqa: BLE001
            yield RowResult(case_id, "DISAGREE", detail=f"pyswisseph raised: {type(ex).__name__}: {ex}")


def replay_orbit(rows, swe, tol):
    for parts, fields in rows:
        prefix = parts[0]
        case_id = "|".join(parts)
        ipl = int(parts[1])
        jd = to_float(parts[2])
        flag = ORBIT_IFLAG_COMBOS[parts[3]]
        exp_retc = int(fields[0])
        try:
            if prefix == "OE":
                dret = swe.get_orbital_elements(jd, ipl, flag)
                exp = [to_float(f) for f in fields[1:18]]
                got = list(dret[0:17])
                if exp_retc < 0:
                    yield RowResult(case_id, "DISAGREE", detail=f"exp retc={exp_retc} (ERR) but pyswisseph SUCCEEDED with dret[0:3]={dret[0:3]}")
                else:
                    yield _numlist(case_id, exp, got, tol)
            elif prefix == "OMM":
                dmax, dmin, dtrue = swe.orbit_max_min_true_distance(jd, ipl, flag)
                exp = [to_float(f) for f in fields[1:4]]
                if exp_retc < 0:
                    yield RowResult(case_id, "DISAGREE", detail=f"exp retc={exp_retc} (ERR) but pyswisseph SUCCEEDED with {dmax},{dmin},{dtrue}")
                else:
                    yield _numlist(case_id, exp, [dmax, dmin, dtrue], tol)
            else:
                yield RowResult(case_id, "SKIP", reason=f"unrecognized prefix {prefix}")
        except Exception as ex:  # noqa: BLE001
            exp_serr = unescape(fields[-1])
            if exp_retc < 0 and compare_message(exp_serr, str(ex)):
                yield RowResult(case_id, "AGREE")
            elif exp_retc < 0:
                yield RowResult(case_id, "DISAGREE", detail=f"both error, message differs: exp={exp_serr!r} got={normalize_message(str(ex))!r}")
            else:
                yield RowResult(case_id, "DISAGREE", detail=f"exp retc={exp_retc} (success) but pyswisseph raised {type(ex).__name__}: {ex}")


def replay_gauquelin(rows, swe, tol):
    for parts, fields in rows:
        prefix = parts[0]
        case_id = "|".join(parts)
        if prefix != "GQ":
            yield RowResult(case_id, "SKIP", reason=f"unrecognized prefix {prefix}")
            continue
        star_label = parts[1]
        jd = to_float(parts[2])
        imeth = int(parts[3])
        lon, lat, height = (to_float(x) for x in parts[4].split(","))
        atpress, attemp = (to_float(x) for x in parts[5].split(","))
        exp_retc = int(fields[0])
        exp_dgsect = to_float(fields[1])
        exp_serr = unescape(fields[2]) if len(fields) > 2 else ""
        body = star_label if not star_label.lstrip("-").isdigit() else int(star_label)
        try:
            dgsect = swe.gauquelin_sector(jd, body, imeth, [lon, lat, height], atpress, attemp, flags=SEFLG_MOSEPH)
            if exp_retc < 0:
                yield RowResult(case_id, "DISAGREE", detail=f"exp retc={exp_retc} serr={exp_serr!r} but pyswisseph SUCCEEDED with dgsect={dgsect}")
            else:
                yield _num1(case_id, exp_dgsect, dgsect, tol)
        except Exception as ex:  # noqa: BLE001
            if exp_retc < 0 and compare_message(exp_serr, str(ex)):
                yield RowResult(case_id, "AGREE")
            elif exp_retc < 0:
                yield RowResult(case_id, "DISAGREE", detail=f"both error, message differs: exp={exp_serr!r} got={normalize_message(str(ex))!r}")
            else:
                yield RowResult(case_id, "DISAGREE", detail=f"exp retc={exp_retc} (success) but pyswisseph raised {type(ex).__name__}: {ex}")


def replay_calc_defaulteph(rows, swe, tol):
    for parts, fields in rows:
        prefix = parts[0]
        case_id = "|".join(parts)
        if prefix not in ("CDEF", "CUDEF"):
            yield RowResult(case_id, "SKIP", reason=f"unrecognized prefix {prefix}")
            continue
        ipl = int(parts[1])
        jd = to_float(parts[2])
        flag = CALC_IFLAG_COMBOS[parts[3]]
        exp_retc = int(fields[0])
        exp_xx = [to_float(f) for f in fields[1:7]]
        exp_serr = unescape(fields[7]) if len(fields) > 7 else ""
        try:
            if prefix == "CUDEF":
                xx, retflags = swe.calc_ut(jd, ipl, flag)
            else:
                xx, retflags = swe.calc(jd, ipl, flag)
            if exp_retc < 0:
                yield RowResult(case_id, "DISAGREE", detail=f"exp retc={exp_retc} serr={exp_serr!r} but pyswisseph SUCCEEDED with xx={xx}")
                continue
            ok = (retflags == exp_retc) and all(close(e, a, tol) for e, a in zip(exp_xx, xx))
            if ok:
                yield RowResult(case_id, "AGREE", detail="serr not comparable via pyswisseph" if exp_serr else "")
            else:
                yield RowResult(
                    case_id, "DISAGREE",
                    detail=f"retc exp={exp_retc} got={retflags}; xx exp={exp_xx} got={list(xx)} (serr not comparable via pyswisseph)",
                    max_abs_diff=max((abs(e - a) for e, a in zip(exp_xx, xx)), default=None),
                )
        except Exception as ex:  # noqa: BLE001
            if exp_retc < 0 and compare_message(exp_serr, str(ex)):
                yield RowResult(case_id, "AGREE")
            elif exp_retc < 0:
                yield RowResult(case_id, "DISAGREE", detail=f"both error, message differs: exp={exp_serr!r} got={normalize_message(str(ex))!r}")
            else:
                yield RowResult(case_id, "DISAGREE", detail=f"exp retc={exp_retc} (success) but pyswisseph raised {type(ex).__name__}: {ex}")


AREA_REPLAYERS = {
    "ayanamsa": replay_ayanamsa,
    "datetime": replay_datetime,
    "house-pos": replay_house_pos,
    "pheno-ast": replay_pheno_ast,
    "eclipse": replay_eclipse,
    "risetrans": replay_risetrans,
    "atmo": replay_atmo,
    "orbit": replay_orbit,
    "gauquelin": replay_gauquelin,
    "calc-defaulteph": replay_calc_defaulteph,
}


def run_one_area(area: str, baseline_dir: str, tol: float, max_detail: int, swe, empty_ephe: str) -> tuple[int, int, int]:
    """Replays one area and prints its block. Returns (agree, disagree, skip) counts."""
    path = os.path.join(baseline_dir, f"baseline-{area}.tsv")
    if not os.path.isfile(path):
        print(f"[{area}] MISSING: {path}")
        return 0, 0, 0
    rows = read_rows(path)
    results = list(AREA_REPLAYERS[area](rows, swe, tol))

    agree = [r for r in results if r.status == "AGREE"]
    disagree = [r for r in results if r.status == "DISAGREE"]
    skip = [r for r in results if r.status == "SKIP"]
    total = len(results)
    replayable = total - len(skip)

    print(f"=== {area} ===")
    print(f"Rows: {total}  Replayable: {replayable} ({100.0 * replayable / total:.1f}%)  Skipped: {len(skip)}")
    if replayable:
        print(f"Agree: {len(agree)} ({100.0 * len(agree) / replayable:.2f}%)  Disagree: {len(disagree)} ({100.0 * len(disagree) / replayable:.2f}%)")

    if skip:
        by_reason: dict[str, int] = {}
        for r in skip:
            by_reason[r.reason] = by_reason.get(r.reason, 0) + 1
        print("Skip reasons:")
        for reason, count in sorted(by_reason.items(), key=lambda kv: -kv[1]):
            print(f"  {count}x {reason}")

    if disagree:
        with_mag = [r for r in disagree if r.max_abs_diff is not None]
        if with_mag:
            worst = max(r.max_abs_diff for r in with_mag)
            within_1e4 = sum(1 for r in with_mag if r.max_abs_diff <= 1e-4)
            print(f"Disagreements with measurable magnitude: {len(with_mag)}; {within_1e4} within 1e-4; worst={worst:.6g}")
        print(f"Disagreement detail (first {max_detail} of {len(disagree)}):")
        for r in disagree[:max_detail]:
            print(f"  {r.case_id}: {r.detail}")
        if len(disagree) > max_detail:
            print(f"  ... and {len(disagree) - max_detail} more")
    print()
    sys.stdout.flush()
    return len(agree), len(disagree), len(skip)


# Per-area subprocess timeout. Empirically, pyswisseph's global (process-wide) C
# state degrades after tens of thousands of calls in one process badly enough
# that a handful of search calls (observed: lun_occult_when_glob rows in
# "eclipse", reached only after ayanamsa+datetime+house-pos+pheno-ast already
# ran ~34,000 calls earlier in the same process) that complete in under a
# millisecond in a fresh process effectively never return in that process --
# swe.close() + swe.set_ephe_path() before the stuck call does not fix it, so
# whatever is accumulating is not the ephemeris-path/file-handle state close()
# releases. Rather than chase that further (this is a pyswisseph/host-library
# finding, not a SwissEphNet finding), each area gets its own subprocess -- a
# fresh interpreter and a fresh global C library state -- which reproduced
# every area at full speed (a fraction of a second to ~1.5s) with none of them
# anywhere near this timeout.
AREA_SUBPROCESS_TIMEOUT_SECONDS = 180


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--area", action="append", dest="areas", help="Restrict to this area (repeatable). Default: all ten replayable areas, each in its own subprocess -- see AREA_SUBPROCESS_TIMEOUT_SECONDS's comment for why.")
    parser.add_argument("--tolerance", type=float, default=1e-6, help="Absolute tolerance for numeric comparisons (default 1e-6).")
    parser.add_argument("--baseline-dir", default=None, help="Directory containing baseline-*.tsv (default: Tests/baseline next to this script's repo root).")
    parser.add_argument("--max-detail", type=int, default=15, help="Max disagreement rows to print per area (default 15).")
    parser.add_argument("--no-subprocess", action="store_true", help="Run all requested areas in this one process instead of one subprocess per area. Faster, but see the process-state-accumulation warning in this script's header before combining more than a couple of areas this way.")
    args = parser.parse_args()

    repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    baseline_dir = args.baseline_dir or os.path.join(repo_root, "Tests", "baseline")

    areas = args.areas or list(AREA_REPLAYERS.keys())
    unknown = [a for a in areas if a not in AREA_REPLAYERS]
    if unknown:
        print(f"Unknown area(s): {unknown}. Known: {list(AREA_REPLAYERS.keys())}", file=sys.stderr)
        return 2

    # Single-area (or explicitly requested single-process) mode: do the work
    # directly in this process.
    if len(areas) == 1 or args.no_subprocess:
        try:
            import swisseph as swe
        except ImportError:
            print("pyswisseph is not installed. Run: pip install pyswisseph", file=sys.stderr)
            return 2

        empty_ephe = tempfile.mkdtemp(prefix="validate-seeded-areas-no-ephe-")
        swe.set_ephe_path(empty_ephe)

        print(f"pyswisseph version: {swe.version}")
        print(f"Tolerance: {args.tolerance}")
        print(f"Ephemeris path (deliberately empty, matching Tools/BaselineGen's unresolved '[ephe]' default): {empty_ephe}")
        print()

        overall_agree = overall_disagree = overall_skip = 0
        try:
            for area in areas:
                a, d, s = run_one_area(area, baseline_dir, args.tolerance, args.max_detail, swe, empty_ephe)
                overall_agree += a
                overall_disagree += d
                overall_skip += s
        finally:
            shutil.rmtree(empty_ephe, ignore_errors=True)

        print("=== Overall (replayable rows only) ===")
        replayable_total = overall_agree + overall_disagree
        if replayable_total:
            print(f"Agree: {overall_agree} ({100.0 * overall_agree / replayable_total:.2f}%)  Disagree: {overall_disagree}  Skipped: {overall_skip}")
        return 0 if overall_disagree == 0 else 1

    # Multi-area default mode: one subprocess per area (see
    # AREA_SUBPROCESS_TIMEOUT_SECONDS's comment above for why).
    import subprocess

    overall_agree = overall_disagree = overall_skip = 0
    summary_re = re.compile(r"^Agree: (\d+) \([\d.]+%\)  Disagree: (\d+)  Skipped: (\d+)$")
    timed_out_areas = []
    failed_areas = []

    for area in areas:
        cmd = [
            sys.executable, os.path.abspath(__file__),
            "--area", area,
            "--tolerance", str(args.tolerance),
            "--max-detail", str(args.max_detail),
            "--baseline-dir", baseline_dir,
        ]
        try:
            proc = subprocess.run(cmd, capture_output=True, text=True, timeout=AREA_SUBPROCESS_TIMEOUT_SECONDS)
        except subprocess.TimeoutExpired:
            print(f"=== {area} ===")
            print(f"TIMEOUT: did not complete within {AREA_SUBPROCESS_TIMEOUT_SECONDS}s in its own subprocess. Not a per-row hang (see this script's AREA_SUBPROCESS_TIMEOUT_SECONDS comment) -- worth rerunning alone with --area {area} and a longer wall-clock budget.")
            print()
            timed_out_areas.append(area)
            continue

        sys.stdout.write(proc.stdout)
        if proc.returncode not in (0, 1):
            print(f"[{area}] subprocess exited {proc.returncode}; stderr:\n{proc.stderr}")
            failed_areas.append(area)
            continue

        found = False
        for line in proc.stdout.splitlines():
            m = summary_re.match(line)
            if m:
                overall_agree += int(m.group(1))
                overall_disagree += int(m.group(2))
                overall_skip += int(m.group(3))
                found = True
        if not found and "MISSING:" not in proc.stdout:
            print(f"[{area}] could not parse a per-area summary line from its subprocess output; stderr:\n{proc.stderr}")
            failed_areas.append(area)

    print("=== Overall (replayable rows only, across all non-timed-out areas) ===")
    replayable_total = overall_agree + overall_disagree
    if replayable_total:
        print(f"Agree: {overall_agree} ({100.0 * overall_agree / replayable_total:.2f}%)  Disagree: {overall_disagree}  Skipped: {overall_skip}")
    if timed_out_areas:
        print(f"Timed out (excluded from the totals above): {timed_out_areas}")
    if failed_areas:
        print(f"Failed to run cleanly (excluded from the totals above): {failed_areas}")

    return 0 if overall_disagree == 0 and not timed_out_areas and not failed_areas else 1


if __name__ == "__main__":
    raise SystemExit(main())
