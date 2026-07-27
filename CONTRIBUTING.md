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
`Programs/SweMini/.editorconfig` set analyzer severities so these files do not
generate build-warning noise, but **`.editorconfig` severities do not stop
`dotnet format whitespace` or `dotnet format style`** -- those look at what the
formatting rules would change, not at whether a diagnostic is enabled. The
only thing that reliably keeps `dotnet format` out of these files is excluding
them on the command line:

```powershell
dotnet format --exclude SwissEphNet/CPort/ --exclude Programs/SweTest/Program.cs --exclude Programs/SweMini/Program.cs
```

Any CI job or pre-commit hook that runs `dotnet format` must use this
exclusion. A format check without it will eventually "fix" these files and
quietly break the correspondence with upstream.

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

## The analyzer carve-out (fixed in PR1)

`SwissEphNet/CPort/.editorconfig` re-enables five analyzer rules --
CA1304/CA1305/CA1307/CA1309/CA1310 (culture-sensitive string/number
operations) -- specifically because they catch real transliteration bugs, the
`C.strcmp` bug being the example already found this way. Declaring them only
in that nested file meant they fired *inside* `CPort/`, where policy forbids
fixing them, and nowhere else -- including `SwissEphNet/Tools/C.cs`, where the
same class of bug actually lived and was fixable. PR1 moved these five
severities to the root `.editorconfig` (they are also still declared, now
redundantly, in `CPort/.editorconfig`, which documents on its own which rules
it deliberately keeps enabled). Enabling them at the repo root surfaced ~50
additional warning sites outside `CPort/`; PR1 fixed all of them, either with
an explicit `StringComparison.Ordinal`/`CultureInfo.InvariantCulture` where
that changed a real (if narrow) culture-dependent bug, or with a scoped
`#pragma warning disable CA1307` plus a comment where the suggested
`StringComparison`-taking overload does not exist on `netstandard2.0` (one of
this project's three target frameworks) and the overload actually in use is
already ordinal by definition (`string.Contains(char)`,
`string.Replace(string, string)`). Net warning count outside `CPort/` after
PR1: zero. `SwissEphNet/CPort/.editorconfig`'s
`dotnet_analyzer_diagnostic.severity = none` stays in place regardless and
continues to silence everything else inside `CPort/`.

## Characterization baseline

`scripts/verify-baseline.ps1` compares the library's current numeric output
against a frozen baseline under `Tests/baseline/`. Any change that is supposed
to be behavior-preserving (toolchain upgrades, refactoring, non-CPort cleanup)
must leave this gate at PASS with zero FAIL rows. A change that is supposed to
alter behavior (a real bug fix, a Swiss Ephemeris version upgrade) will show up
here as a diff -- that is the gate doing its job, not a problem to work around.
Never regenerate the baseline or add a waiver to make an unexpected diff go
away without first understanding why the diff happened.
