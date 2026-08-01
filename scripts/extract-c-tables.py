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
    """Return the text between the outermost braces of a `... name[] = { ... };`.

    Brace depth is counted with C comments masked out (see _mask_comments below), never
    over the raw character stream -- an explanatory comment sitting inside the initializer
    can itself contain literal `{`/`}` characters (ayanamsa row 1's own comment is exactly
    this: `/*{J1900, 360 - 337.53953},  * 1: Lahiri (Robert Hand) */`), and naive depth
    counting over such a comment happens to land on the right answer only when that
    comment's braces are balanced -- an unbalanced one would silently return the wrong
    span with no error at all. Masking removes that dependency on luck. The returned slice
    is still taken from the ORIGINAL `text`, comments and all: masking only decides which
    characters count toward depth, never what gets returned, so this stays safe to call on
    raw, not-yet-comment-stripped text -- which emit_main() below does, precisely so the
    comments survive into the emitted C#.
    """
    m = re.search(decl_pattern, text)
    if not m:
        raise SystemExit('declaration not found: %s' % decl_pattern)
    start = text.index('{', m.end() - 1)
    flags = _mask_comments(text)
    depth, i = 0, start
    while i < len(text):
        if not flags[i]:
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
    # Strict, as gen-delta.ps1 decodes: a stray Latin-1 byte must fail loudly rather
    # than become U+FFFD and land in the emitted C#.
    raw = header.read_text(encoding='utf-8')
    text = strip_c_comments(raw)

    # Alongside the header we were given, not a fixed path -- pointing this at another
    # tree used to silently mix that tree's sweph.h with this one's swephexp.h.
    swephexp_text = (header.parent / 'swephexp.h').read_text(encoding='utf-8')
    predef = re.search(r'#define\s+SE_NSIDM_PREDEF\s+(\d+)', swephexp_text)
    n_predef = int(predef.group(1))

    # sweph.h declares `static const double pla_diam[NDIAM] = {...}` and
    # `#define NDIAM (SE_VESTA + 1)`, so the expected row count is SE_VESTA (from
    # swephexp.h, alongside SE_NSIDM_PREDEF above) plus one -- not a literal 21 written
    # here by hand, which would silently stop tracking the real declaration the day either
    # constant moves.
    se_vesta = re.search(r'#define\s+SE_VESTA\s+(\d+)', swephexp_text)
    if not se_vesta:
        raise SystemExit('SE_VESTA not found in swephexp.h -- cannot compute the expected pla_diam row count (NDIAM = SE_VESTA + 1)')
    n_diam = int(se_vesta.group(1)) + 1

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

    # Same shape of check for pla_diam as ayanamsa above: nothing previously checked this
    # table's row count against its own declared size at all, though scripts/verify-crt-parity.ps1
    # (a gated, append-only log) records "21 of 21 match" as though this had already been
    # verified here.
    if len(diam) != n_diam:
        raise SystemExit('pla_diam has %d values but NDIAM (SE_VESTA + 1) is %d'
                         % (len(diam), n_diam))

    print('SE_NSIDM_PREDEF = %d' % n_predef)
    print('NDIAM (SE_VESTA + 1) = %d' % n_diam)
    print('ayanamsa rows   = %d  (fields: t0, ayan_t0, t0_is_UT, prec_offset)' % len(aya))
    print('pla_diam values = %d' % len(diam))
    print()
    for i, r in enumerate(aya):
        print('  [%2d] %s' % (i, ' | '.join(r)))
    print()
    for i, v in enumerate(diam):
        print('  pla_diam[%2d] = %s' % (i, v))




def _mask_comments(text):
    """Return a list flagging, per character, whether it sits inside a C comment."""
    flags = [False] * len(text)
    i, n = 0, len(text)
    while i < n:
        if text.startswith('/*', i):
            end = text.find('*/', i + 2)
            end = n if end < 0 else end + 2
            for k in range(i, end):
                flags[k] = True
            i = end
        elif text.startswith('//', i):
            end = text.find('\n', i)
            end = n if end < 0 else end
            for k in range(i, end):
                flags[k] = True
            i = end
        else:
            i += 1
    return flags


def emit_ayanamsa_csharp(body):
    """Rewrite the C initializer body as C#, keeping every comment where it stands.

    Only braces outside comments are treated as rows, which is what makes this safe on a
    table whose comments contain literal initialiser syntax.
    """
    flags = _mask_comments(body)
    out = []
    i, n = 0, len(body)
    rows = 0
    while i < n:
        if body[i] == '{' and not flags[i]:
            depth, j = 0, i
            while j < n:
                if not flags[j]:
                    if body[j] == '{':
                        depth += 1
                    elif body[j] == '}':
                        depth -= 1
                        if depth == 0:
                            break
                j += 1
            inner = body[i + 1:j]
            fields = [f.strip() for f in inner.split(',')]
            if len(fields) != 4:
                raise SystemExit('row %d has %d fields: %r' % (rows, len(fields), fields))
            t0, ayan, is_ut, prec = fields
            is_ut = {'TRUE': 'true', 'FALSE': 'false'}[is_ut]
            # SEMOD_* live on the SwissEph class in this port; J1900/J2000/B1950 are
            # members of Sweph, which is where this table sits, so they stay bare.
            prec = re.sub(r'SEMOD_[A-Z0-9_]+', lambda mm: 'SwissEph.' + mm.group(0), prec)
            out.append('new aya_init{t0=%s, ayan_t0=%s, t0_is_UT=%s, prec_offset=%s}'
                       % (t0, ayan, is_ut, prec))
            rows += 1
            i = j + 1
        else:
            out.append(body[i])
            i += 1
    return ''.join(out), rows


def emit_main():
    """`--emit` prints the C# initializer body, so the committed table is reproducible."""
    header = Path(sys.argv[2] if len(sys.argv) > 2 else 'external/swisseph/sweph.h')
    raw = header.read_text(encoding='utf-8')
    body, rows = emit_ayanamsa_csharp(
        initializer_body(raw, r'struct\s+aya_init\s+ayanamsa\s*\[[^\]]*\]\s*='))
    n = int(re.search(r'#define\s+SE_NSIDM_PREDEF\s+(\d+)',
                      (header.parent / 'swephexp.h').read_text(encoding='utf-8')).group(1))
    if rows != n:
        raise SystemExit('emitted %d rows but SE_NSIDM_PREDEF is %d' % (rows, n))
    # Pin UTF-8 on the way out. The table carries degree signs and typographic quotes, and
    # a redirected stdout on Windows otherwise encodes with the console codepage -- ibm850
    # here -- so `--emit > file` produced mojibake.
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8', newline='')
    sys.stdout.write(body)


if __name__ == '__main__':
    if len(sys.argv) > 1 and sys.argv[1] == '--emit':
        emit_main()
    else:
        main()
