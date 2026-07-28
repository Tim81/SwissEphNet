# Contributing

## Transliterated files must never be reformatted

**`SwissEphNet/CPort/`, `Programs/SweTest/Program.cs`, and
`Programs/SweMini/Program.cs` are deliberate, line-by-line transliterations of
the Swiss Ephemeris C source** (`sweph.c`/`swephlib.c`/etc., `swetest.c`, and
`swemini.c` respectively -- each file carries the same port header identifying
it as such). Every one of these files corresponds, statement for statement, to
a file in the upstream C release. That correspondence is what makes each
upstream Swiss Ephemeris upgrade tractable: a porter diffs the new C release
against the old one, then applies the same diff, in the same shape, to the
matching C# file. If the C# no longer reads like the C it came from, that
process stops working and every future upgrade gets harder, not easier. This
is not a theoretical concern for the `Programs/` files either: `swetest.c`
alone changes by +484/-244 lines in the 2.10.03 delta.

**Do not run `dotnet format`, an IDE "clean up code" command, a `var`-for-explicit-type
rewrite, expression-bodied member conversion, or brace reflowing against
any of these files.** Any of these will destroy the line-by-line
correspondence permanently, even though each one individually looks like a
harmless style fix.

This applies to humans and to automation equally. If a tool you're running
offers to "fix" or "clean up" `CPort/`, `Programs/SweTest/Program.cs`, or
`Programs/SweMini/Program.cs`, decline, or scope the tool away from those
first.

### Running `dotnet format` anywhere else

`SwissEphNet/CPort/.editorconfig`, `Programs/SweTest/.editorconfig`, and
`Programs/SweMini/.editorconfig` silence every analyzer diagnostic in their
folder except a small set that has actually caught real transliteration bugs
(CA1304/CA1305/CA1307/CA1309/CA1310 for culture-sensitive string/number
operations, CA2242 for NaN comparison, and CS0162/CS0164/CS0219 for
unreachable/unused code that usually means a mis-landed `goto`) -- they are
a deliberate carve-out, not a blanket noise suppression, and that carve-out
is exactly why `Programs/SweTest/Program.cs` alone still emits over 200
warning sites (see "Net warning count" below): this document forbids fixing
them, since fixing them means editing frozen, transliterated code. Do not
try to clean those up. Separately, and regardless of any of the above,
**`.editorconfig` severities do not stop `dotnet format whitespace` or
`dotnet format style`** -- those look at what the formatting rules would
change, not at whether a diagnostic is enabled. The only thing that reliably
keeps `dotnet format` out of these files is excluding them on the command
line:

```powershell
dotnet format --exclude SwissEphNet/CPort/ --exclude Programs/SweTest/Program.cs --exclude Programs/SweMini/Program.cs
```

Any CI job or pre-commit hook that runs `dotnet format` must use this
exclusion. A format check without it will eventually "fix" these files and
quietly break the correspondence with upstream.

The nested `.editorconfig` files under those paths pin `trim_trailing_whitespace`
and `insert_final_newline`, and that part works. They deliberately do **not** pin
`csharp_new_line_before_open_brace`, because `.editorconfig` has no value meaning
"preserve": `none` is as strong an instruction as `all`, merely pointing the other
way. These files mix Allman and K&R exactly as the C does, so pinning `none`
reverses the rewrite instead of preventing it, and measurably widens it -- with
`none` set, an unexcluded `dotnet format whitespace` run took
`Programs/SweTest/Program.cs` from 8 lines touched to 1,518.

So the real guard is `scripts/verify-freeze.ps1`, which runs in CI on every push
and pull request. It records a structural fingerprint of each frozen path (file
count, total lines, K&R `) {` count, trailing-whitespace lines) in
`scripts/freeze-manifest.tsv` and fails when any of them moves, regardless of what
caused it. It is not a fidelity check -- whether a hunk matches the C it cites is a
review judgement -- it answers only "did anyone reformat".

When you legitimately change a frozen file, a fidelity fix correcting a divergence
from the C or the 2.10.03 re-transliteration itself, these counts will move. That
is expected. Regenerate the manifest and commit it **in the same commit** as the
change:

```powershell
pwsh scripts/verify-freeze.ps1 -Update
```

Committing it separately, or in a commit that touches nothing else, defeats the
point: the new counts should be reviewed alongside the change that caused them.

## Porting upstream changes

When updating to a newer Swiss Ephemeris release, diff the new upstream C
source against the version this port is currently tracking, and apply the same
diff to the matching file under `CPort/` (or to `Programs/SweTest/Program.cs`
/ `Programs/SweMini/Program.cs` for `swetest.c` / `swemini.c`), preserving its
existing structure, naming, and formatting. Do not take the opportunity to
also modernize the C# style of the lines you're touching -- keep that change
isolated to what the upstream diff actually changed.

The freeze on these files is about never reformatting and never restructuring
-- it is not a blanket ban on ever touching them. Where the C# genuinely
diverges from what its C source does (a mis-transliteration, not a deliberate
platform-specific choice), correcting it is in scope and makes the port more
faithful, not less; see "DIR_GLUE" in `docs/known-issues.md` for a worked
example, including the evidence standard used to tell "divergence from the
source" apart from "deliberate platform difference" (a parallel site
transliterated correctly elsewhere in the same file is strong evidence of the
former).

**Every porting PR must cite the C hunk range it implements** (e.g.
`sweph.c:2310-2358`), against the upstream C described below, so a reviewer
can check the C# against the exact hunk without having to reconstruct the diff
themselves. `.github/pull_request_template.md` has a required field for this.
A PR that changes a frozen file with no hunk citation should not be merged.

### The upstream C is vendored at `external/swisseph`

`external/swisseph` is a git submodule of `https://github.com/aloistr/swisseph`,
pinned to tag `v2.10.3final` -- the upstream C this port is being upgraded to.
It is sparse-checked-out to keep it small: only `*.c`, `*.h`, `Makefile`,
`LICENSE` and `setest/` (the reference test corpus `Tests/SwissEphNet.Conformance.Tests`
is built against) are pulled. `ephe/` (the ephemeris data files) is deliberately
excluded -- it is 378 MB across 259 files and nothing in this repo needs it.

To initialize it:

```powershell
git submodule update --init external/swisseph
```

If a from-scratch, disk-conscious checkout matters (CI, a fresh clone), do the
sparse setup before the first checkout rather than after, so the partial clone
never fetches blobs for the excluded paths in the first place:

```powershell
git submodule init
git clone --filter=blob:none --no-checkout --sparse `
    https://github.com/aloistr/swisseph.git external/swisseph
cd external/swisseph
git sparse-checkout set --no-cone /*.c /*.h /Makefile /LICENSE /setest/*
git checkout v2.10.3final
cd ../..
git submodule absorbgitdirs external/swisseph
```

(`git submodule update --init` alone still works and lands at the same commit;
it is simpler to type but checks out every file at HEAD once before any sparse
pattern narrows the working tree, which briefly touches all of `ephe/` if you
care about that during the clone.)

CI jobs that do not read `external/swisseph` should not pay for it: `actions/checkout@v4`
does not fetch submodules by default, and none of the jobs in `.github/workflows/ci.yml`
or `.github/workflows/baseline.yml` need to -- they build, test and freeze-check the
.NET solution, none of which reads the vendored C. Only add `submodules: true` (or
`recursive`) to a checkout step if a future job actually invokes `scripts/gen-delta.ps1`
or otherwise reads `external/swisseph` in CI.

### The 2.08 baseline trap

Porting to 2.10.03 means diffing it against 2.08, the C version this port
currently tracks. **Do not diff against the `v2.08.00a` tag in `aloistr/swisseph`.**
That tag is an incomplete snapshot of the 2.08 release: it is missing `swecl.c`,
`swehouse.c` and `swehel.c` entirely, and its `swephexp.h` is truncated (about
14 KB against the real 38,410 bytes). Diffing against it silently produces a
wrong work queue for three of the five files the 2.10.03 port touches -- those
three files simply do not appear in the diff at all, and nothing about a
missing file from a git tag looks like an error.

The correct 2.08 baseline is the **PyPI `pyswisseph 2.08.00-1` sdist**, which
vendors the complete, unmodified 2.08 tree under `libswe/`. Verified byte
sizes: `swecl.c` 221,667 B, `swehouse.c` 94,080 B, `swehel.c` 123,155 B,
`swephexp.h` 38,410 B, 24 `.c`/`.h` files present (31 files total, including
the data files and `Makefile`).

`scripts/fetch-2.08-baseline.ps1` downloads that sdist, verifies its own
sha256, extracts `libswe/`, and checks every file it contains against
`scripts/pyswisseph-2.08.manifest.tsv` (sha256, byte size, line count per
file). It fails loudly on any mismatch rather than proceeding with an
unverified baseline. Nothing it downloads is committed -- the output directory
(`external/pyswisseph-2.08/`) is gitignored; only the script and the manifest
are tracked. Run it with:

```powershell
pwsh scripts/fetch-2.08-baseline.ps1
```

This is a structural guard, not a convention: the script has exactly one 2.08
input (the PyPI URL above) and no parameter or code path that can reach the
`v2.08.00a` git tag instead.

### Generating a per-file delta

`scripts/gen-delta.ps1` diffs a file between the 2.08 baseline (fetched above)
and the pinned 2.10.03 submodule:

```powershell
pwsh scripts/gen-delta.ps1 -File sweph.c
pwsh scripts/gen-delta.ps1               # every file present on both sides
```

Like the fetch script, it has no parameter that accepts a different 2.08
source -- the 2.08 side is always `external/pyswisseph-2.08/`, the 2.10.3 side
is always `external/swisseph/`.

Two filters make the output usable instead of just noisy:

* **License-noise filter**, on by default. Astrodienst's GPL-2 -> AGPL-3
  relicensing touches every file with the same header rewrite (copyright
  year, "GNU public license" -> "GNU Affero General Public License", the
  license URL, and the surrounding blank-line churn). A hunk is dropped from
  the report, and counted separately, only when every one of its changed
  lines matches that known rewrite -- a hunk that mixes a license-text change
  with a real code change is left in. Pass `-IncludeLicenseHunks` to see it
  anyway.
* **Comments-stripped variant for headers.** A raw diff of a `.h` file
  over-counts: header files are mostly doc comments, and unlike the `.c`
  files (where the license block is usually its own isolated hunk), a
  header's license-comment edits often sit close enough to a real
  `#define`/prototype change to land in the same hunk. Stripping `/* ... */`
  comments from both sides before diffing isolates the actual code delta.
  `sweph.h` is +277/-100 raw but about 70 lines of real code change once
  comments are stripped -- reported alongside the raw count, not instead of
  it.

## The analyzer carve-out (fixed in PR #4)

`SwissEphNet/CPort/.editorconfig` re-enables five analyzer rules --
CA1304/CA1305/CA1307/CA1309/CA1310 (culture-sensitive string/number
operations) -- specifically because they catch real transliteration bugs, the
`C.strcmp` bug being the example already found this way. Declaring them only
in that nested file meant they fired *inside* `CPort/`, where policy forbids
fixing them, and nowhere else -- including `SwissEphNet/Tools/C.cs`, where the
same class of bug actually lived and was fixable. PR #4 (`fix/known-library-bugs`)
moved these five severities to the root `.editorconfig` (they are also still
declared, now redundantly, in `CPort/.editorconfig`, which documents on its
own which rules it deliberately keeps enabled). Enabling them at the repo root
surfaced ~50 additional warning sites outside `CPort/`; PR #4 fixed all of
them, either with an explicit `StringComparison.Ordinal`/`CultureInfo.InvariantCulture`
where that changed a real (if narrow) culture-dependent bug, or with a scoped
`#pragma warning disable CA1307` plus a comment where the suggested
`StringComparison`-taking overload does not exist on `netstandard2.0` (one of
this project's three target frameworks) and the overload actually in use is
already ordinal by definition (`string.Contains(char)`,
`string.Replace(string, string)`). Net warning count outside `CPort/`
immediately after PR #4 was zero, but that was never a standing invariant:
`Programs/SweTest/Program.cs` and `Programs/SweMini/Program.cs` are frozen,
transliterated files this document forbids editing (see above), and the test
project and `SweWin` accumulate their own warnings independently as they
grow. `SwissEphNet/CPort/.editorconfig`'s `dotnet_analyzer_diagnostic.severity
= none` stays in place regardless and continues to silence everything else
inside `CPort/`.

### Net warning count (measured, not a target of zero)

Measured with `dotnet build <target> -c Release --no-incremental` (an
up-to-date build recompiles nothing and silently reports zero, so always
force a clean rebuild when counting):

- `SwissEphNet.sln`: 831 warnings, 546 distinct sites, 464 of them outside
  `CPort/` (206 in `Programs/SweTest/Program.cs`, 185 in
  `Tests/SwissEphNet.Tests`, 64 in `Programs/SweWin`, the remaining 9 split
  between `Programs/SweMini/Program.cs` and `SwissEphNet/Tools/`).
- `SwissEphNet.CrossPlatform.slnf` (excludes `SweWin`, which is Windows-only):
  767 warnings.

Most of the outside-`CPort/` total sits in the two other frozen,
transliterated files (`SweTest/Program.cs`, `SweMini/Program.cs`), which this
document forbids cleaning up for the same reason it forbids touching
`CPort/`. A contributor should not treat this count as a backlog to clear;
it is a snapshot, re-measure it rather than trusting a stale number here.

## Characterization baseline

`scripts/verify-baseline.ps1` compares the library's current numeric output
against a frozen baseline under `Tests/baseline/`. Any change that is supposed
to be behavior-preserving (toolchain upgrades, refactoring, non-CPort cleanup)
must leave this gate at PASS with zero FAIL rows. A change that is supposed to
alter behavior (a real bug fix, a Swiss Ephemeris version upgrade) will show up
here as a diff -- that is the gate doing its job, not a problem to work around.
Never regenerate the baseline or add a waiver to make an unexpected diff go
away without first understanding why the diff happened.

## Licensing of contributions

`README.md` and `NOTICE` state that this project, and therefore this library,
is dual-licensed: the GNU Affero General Public License (AGPL-3.0) or a Swiss
Ephemeris Professional License from Astrodienst. By submitting a contribution
(a pull request, a patch, or any other change proposed for inclusion), you
agree to license your contribution under that same dual license, on the same
terms as the rest of the project. This is a statement of the terms
contributions are offered under, not a contributor license agreement; there
is no separate CLA to sign. Contributions must be your own work, or work you
have the right to submit under these terms. Do not remove or reduce the
per-file attribution to Yan Grenier (the original C-to-C# port, 2014-2019)
that many files under `SwissEphNet/CPort/` carry in their header comment.
