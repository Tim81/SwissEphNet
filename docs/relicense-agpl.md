# Relicense: GPL-2-or-later to AGPL (2.08 -> 2.10 license line)

## What changed

Swiss Ephemeris is dual-licensed by Astrodienst: a free-software option plus a commercial
"Professional" option. The free option's terms changed between the versions of Swiss Ephemeris
this fork has tracked:

- At Swiss Ephemeris 2.08 (what this fork was previously aligned with), the free option was
  "GNU public license version 2 or later", referencing
  `http://www.gnu.org/licenses/old-licenses/gpl-2.0.html`.
- Upstream (`aloistr/swisseph`) changed the free option to the GNU Affero General Public
  License (AGPL-3.0) at Swiss Ephemeris **2.10**, referencing
  `https://www.gnu.org/licenses/agpl-3.0.html`. The Professional license option is unchanged;
  this was never a move to "AGPL only".

This repository has now adopted the 2.10 license text ahead of the 2.10.03 port itself:

- `LICENSE` replaced with a verbatim copy of upstream's 2.10.3 `LICENSE`
  (`https://github.com/aloistr/swisseph/blob/v2.10.3bfinal/LICENSE` -- byte-identical to
  `v2.10.3final`'s copy).
- `agpl-3.0.txt` added, a verbatim copy of upstream's
  `https://github.com/aloistr/swisseph/blob/v2.10.3bfinal/agpl-3.0.txt` (likewise unchanged from
  `v2.10.3final`).
- `NOTICE` added, recording attribution for Astrodienst, Yan Grenier's original port, and this
  fork's maintenance.
- The per-file license header comment (the block starting `/* Copyright (C) 1997 - 2008
  Astrodienst AG...`) was updated to the 2.10 wording in every source file that carried it: the
  copyright line becomes `1997 - 2021`, item (a) becomes "GNU Affero General Public License
  (AGPL)", the GPL paragraphs become AGPL paragraphs, and the license URL changes. This matches
  upstream's own per-file headers exactly, including two file-specific quirks upstream itself
  has: `SweHel.cs` (ported from `swehel.c`) keeps its separate "Copyright (c) Victor Reijs, 2008"
  line and gains the Astrodienst copyright line alongside it, plus the "Authors of the Swiss
  Ephemeris" paragraph it was previously missing; `Programs/SweMini/Program.cs` (ported from
  `swemini.c`) had its entire Astrodienst license paragraph *removed*, because upstream's 2.10.3
  `swemini.c` no longer carries one (that sample file is public domain and upstream dropped the
  dual-license boilerplate from it). No other comment text, and no code, was touched.
- `SwissEphNet/SwissEphNet.csproj` keeps `PackageLicenseFile=LICENSE` (not
  `PackageLicenseExpression`, since NuGet only accepts OSI/FSF-approved SPDX identifiers there and
  the Professional license option has none), now packs `agpl-3.0.txt` and `NOTICE` alongside
  `LICENSE`, and credits Astrodienst, Yan Grenier, and the fork maintainer in `Copyright`.
- `README.md` gained a license section up top, stating the AGPL/Professional choice and, in
  particular, that the AGPL's network clause reaches server-side and SaaS use: operating a
  network service built on an AGPL-covered library obliges you to offer the complete
  corresponding source of that service, whether or not you ever distribute a binary.

The root `LICENSE` file (unlike the per-file source headers) also drops the closing paragraph
about the trademarks "Swiss Ephemeris" and "Swiss Ephemeris inside" that the 2.08 text carried -
that is upstream's own change at the 2.10 `LICENSE` file, not something invented here. The
per-file source headers still carry that trademark paragraph, because upstream's own per-file
headers still carry it too.

## Why this had to land before the 2.10.03 port

Once source code written against Swiss Ephemeris 2.10.03 lands in this repository's git history
under the old GPL-2-or-later header, that history is effectively permanent: this fork has 46
siblings in the same GitHub network, and rewriting shared history to fix a licensing mismatch
after the fact would be disruptive to anyone who has forked or pulled from this repository in the
meantime. Doing the relicense first, as a comment-only, no-behavior-change commit, means the
2.10.03 port's own diffs will already show the correct (AGPL) header wherever the C source
carries one, instead of introducing a second wave of "now fix the license header too" churn mixed
in with actual porting changes.

This also removes noise from the upcoming port diff directly: roughly 150 of the ~940 hunks in
the 2.08-to-2.10.03 upstream delta are exactly this same header rewrite, repeated file by file.
With the header rewrite already done, the 2.10.03 port's diff against upstream will consist
almost entirely of the substantive code changes, not comment churn.

## What did not change

- The dual-license model itself is unchanged: a free option plus a commercial Professional
  option purchased from Astrodienst. This fork did not switch to "AGPL only".
- Yan Grenier's original port attribution - the "This is a port of the Swiss Ephemeris Free
  Edition ... Yan Grenier" comment block at the top of each ported file - was left untouched.
  Only the separate Astrodienst copyright/license paragraph beneath it was rewritten.
- No executable code changed. See the verification steps in the corresponding pull/commit
  history: build warning count, test pass counts, and `scripts/verify-baseline.ps1` all matched
  their pre-relicense values, and every changed line in `SwissEphNet/CPort/*.cs` sits inside a
  comment.
