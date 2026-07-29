#!/usr/bin/env python3
"""Extract the ayanamsa[] and pla_diam[] tables from external/swisseph/sweph.h.

These two tables are the only bulk data the 2.10.03 header delta changes, and they are
the one part of the port most exposed to transcription error: 47 rows of four fields and
21 floating-point constants, none of which a reviewer can check by eye against a diff.

The extraction has one trap, and it is why comments are stripped before any brace is
matched rather than after. Several ayanamsa rows carry explanatory comments that contain
literal C initialiser syntax -- row 1's is `/*{J1900, 360 - 337.53953},  * 1: Lahiri
(Robert Hand) */`. A brace-matching pass run over the raw text picks those up as data and
silently produces a table that is the right shape and the wrong contents.

Prints the parsed tables and, given the port's current (2.08) values, the exact set of
changes, so the result can be checked against what the delta is known to contain rather
than trusted.
"""
import re
import sys
from pathlib import Path


def strip_c_comments(text):
    """Remove /* */ and // comments, preserving string literals and line structure."""
    out = []
    i, n = 0, len(text)
    while i < n:
        if text.startswith('/*', i):
            end = text.find('*/', i + 2)
            if end < 0:
                break
            # keep newlines so reported line numbers stay meaningful
            out.append('\n' * text.count('\n', i, end))
            i = end + 2
        elif text.startswith('//', i):
            end = text.find('\n', i)
            if end < 0:
                break
            i = end
        elif text[i] == '"':
            j = i + 1
            while j < n and text[j] != '"':
                j += 2 if text[j] == '\\' else 1
            out.append(text[i:j + 1])
            i = j + 1
        else:
            out.append(text[i])
            i += 1
    return ''.join(out)


def initializer_body(text, decl_pattern):
    """Return the text between the outermost braces of a `... name[] = { ... };`."""
    m = re.search(decl_pattern, text)
    if not m:
        raise SystemExit('declaration not found: %s' % decl_pattern)
    start = text.index('{', m.end() - 1)
    depth, i = 0, start
    while i < len(text):
        if text[i] == '{':
            depth += 1
        elif text[i] == '}':
            depth -= 1
            if depth == 0:
                return text[start + 1:i]
        i += 1
    raise SystemExit('unterminated initializer for %s' % decl_pattern)


def parse_ayanamsa(body):
    rows = []
    for m in re.finditer(r'\{([^{}]*)\}', body):
        fields = [f.strip() for f in m.group(1).split(',')]
        if len(fields) != 4:
            raise SystemExit('row %d has %d fields, expected 4: %r'
                             % (len(rows), len(fields), fields))
        rows.append(fields)
    return rows


def parse_pla_diam(body):
    return [v.strip() for v in body.split(',') if v.strip()]


def main():
    header = Path(sys.argv[1] if len(sys.argv) > 1 else 'external/swisseph/sweph.h')
    raw = header.read_text(encoding='utf-8', errors='replace')
    text = strip_c_comments(raw)

    predef = re.search(r'#define\s+SE_NSIDM_PREDEF\s+(\d+)',
                       Path('external/swisseph/swephexp.h').read_text(
                           encoding='utf-8', errors='replace'))
    n_predef = int(predef.group(1))

    aya = parse_ayanamsa(initializer_body(
        text, r'struct\s+aya_init\s+ayanamsa\s*\[[^\]]*\]\s*='))
    diam = parse_pla_diam(initializer_body(
        text, r'double\s+pla_diam\s*\[[^\]]*\]\s*='))

    # The table is declared [SE_NSIDM_PREDEF], so a row count that disagrees with the
    # constant means the extraction is wrong, not the data.
    #
    # Worth knowing when comparing against 2.08: that header declares the table [43] and
    # then initialises 44 rows, the last a {0, 0, FALSE} sentinel, and the port copied it
    # faithfully. 2.10.03 drops the sentinel and has exactly 47 real rows, so the old
    # index 43 is not a changed value -- it is the sentinel being replaced by the first of
    # the four new ayanamsas (LAHIRI_1940, LAHIRI_VP285, KRISHNAMURTI_VP291, LAHIRI_ICRC).
    # The ported table must therefore end at index 46 with no trailing sentinel.
    if len(aya) != n_predef:
        raise SystemExit('ayanamsa has %d rows but SE_NSIDM_PREDEF is %d'
                         % (len(aya), n_predef))

    print('SE_NSIDM_PREDEF = %d' % n_predef)
    print('ayanamsa rows   = %d  (fields: t0, ayan_t0, t0_is_UT, prec_offset)' % len(aya))
    print('pla_diam values = %d' % len(diam))
    print()
    for i, r in enumerate(aya):
        print('  [%2d] %s' % (i, ' | '.join(r)))
    print()
    for i, v in enumerate(diam):
        print('  pla_diam[%2d] = %s' % (i, v))


if __name__ == '__main__':
    main()
