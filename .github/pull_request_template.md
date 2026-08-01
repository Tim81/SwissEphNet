<!--
This template exists because a reviewer of a porting PR cannot check the C# against the C it
claims to implement unless the PR says exactly which C it is. See CONTRIBUTING.md, "Porting
upstream changes".
-->

## C hunk range

<!--
Required for any change to SwissEphNet/CPort/, Programs/SweTest/Program.cs or
Programs/SweMini/Program.cs. Cite the exact upstream hunk(s) this PR implements, e.g.:

  sweph.c:2310-2358 (v2.10.3final, external/swisseph)

Generate the candidate diff with:

  pwsh scripts/gen-delta.ps1 -File sweph.c

If this PR does not touch a frozen/transliterated file, write "N/A" and say why below instead.
-->

## What this PR does

<!-- One or two sentences. -->

## Gates

- [ ] `pwsh scripts/verify-baseline.ps1` passes on both net8.0 and net10.0
- [ ] `pwsh scripts/verify-freeze.ps1` passes
- [ ] If any file under `Tests/baseline/` changed: the deviation note is in this PR, in the
      same commit as the change, explaining why the new numbers are expected (see
      `scripts/regenerate-baseline.ps1` / CONTRIBUTING.md). If nothing there changed, delete
      this line.
- [ ] Every `(no PR yet -- fill in "PR #N" ...)` placeholder this PR's own regeneration left
      behind is now filled in with this PR's real number, in this PR, not a follow-up (see
      CONTRIBUTING.md, "Correctness oracle known-fail list"). A grep for `PR #` will not find
      every shape these placeholders take -- `Tests/baseline/baseline-2.8.0.2.env.txt` records
      one as `pheno-ast | local (no PR yet, log entry N; ...)`, not `PR #N` -- so check the
      regeneration log this PR actually touched by eye, not just by grep. If this PR did not run
      a regeneration script, delete this line.
