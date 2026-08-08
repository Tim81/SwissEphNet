# Baseline: state of the code as inherited (historical record)

**This is a historical record, not an open task list.** It documents the state of the code as
inherited before this project made any change, and the plan that followed from that state at the
time. The two Release-only test failures and the "PR0" work item described below were resolved by
PR #4 (`fix/known-library-bugs`) -- see `docs/known-issues.md` and the commit history. Nothing in
this file describes current work remaining to be done.

Recorded at commit `8118f32` (tag `v2.8.0.2-import`), the last upstream commit from 2019-12-15,
before any change in this project. Environment: Windows 11, .NET SDK 10.0.302 as the only installed
SDK, runtimes 8.0.29 and 10.0.10, VS 2026 Community.

Every later change was measured against this. Anything green here had to stay green.

## Build

`dotnet build SwissEphNet.sln -c Release` gives 1 error and 33 warnings.

The error is confined to one project:

```
error MSB3644: The reference assemblies for .NETFramework,Version=v4.0 were not found.
  [Programs\SweWin\SweWin.csproj]
```

`SweWin.csproj` is a legacy non-SDK-style project (`ToolsVersion="12.0"`), so it resolves targeting
packs from disk. The `v4.0` reference-assembly folder on this machine is an empty stub (0 DLLs;
only `v4.7.2` and `v4.8` hold real assemblies).

Everything else builds, including the `net40` leg of the library. SDK-style projects pick up
`Microsoft.NETFramework.ReferenceAssemblies` implicitly and never touch the on-disk pack. This
corrects an earlier assumption that `net40` could not build here at all. It can. Only the legacy
project cannot.

Other warnings:

- `NETSDK1215`: netstandard1.0 is no longer recommended.
- `NETSDK1138`: `netcoreapp1.0` (SweTest, SweMini) is out of support.
- `NU1902`/`NU1903`: known vulnerabilities in `Microsoft.NETCore.App` 1.0.4 and 2.1.14.
- `CS0649`: `Sweph.fixed_star.starno` is never assigned (`CPort/Sweph.h.cs:615`).
- `CS0414`: `Program.LEN_SOUT` is assigned but never used (`Programs/SweTest/Program.cs:672`).

## Tests

| Configuration | Result |
|---|---|
| `net46`, Debug | 203 passed, 0 failed |
| `net46`, Release | 201 passed, 2 failed |
| `netcoreapp2.1` | Cannot run. Runtime 2.1.14 is not installed and is EOL |

The test project does run on the `net46` leg, contrary to an earlier assumption. Only the
`netcoreapp2.1` leg is dead.

### The two Release-only failures

`SwissEphTest.Test_swe_fixstar` and `SwissEphTest.Test_swe_fixstar_ut`, both in
`Tests/SwissEphNet.Tests/SwissEphTest.swe_fixstar.cs`.

```
Test_swe_fixstar     expected 0.00014887   actual 0.00014896
Test_swe_fixstar_ut  expected 0.01536      actual 0.015532
```

These are stale assertions, not a numerical defect. Both tests carry `#if DEBUG` / `#else` branches
holding different expected values for the same computation:

```csharp
#if DEBUG
    Assert.Equal(0.00014896, xx[3], 8);   // what the code actually produces
#else
    Assert.Equal(0.00014887, xx[3], 8);   // stale
#endif
```

The computed values are the same in both configurations: `0.00014896` and `0.0155324764710185`
either way. Only the assertions differ, and the `#else` branch is wrong. `Test_swe_fixstar_ut` also
has a dead `#if NET_STANDARD` sub-branch; the test project never defines `NET_STANDARD`, so that
arm is unreachable.

So this is not JIT or libm divergence between configurations. The numbers do not move. The
expectation was written once and never updated.

Fix belongs in the bug-fix PR: delete the conditionals and assert the single value the code
produces, so the suite stops depending on build configuration.

## What this meant for the plan (resolved)

The regression baseline was "203 pass in Debug, 201 pass plus 2 stale in Release", not "all green".
PR #4 (`fix/known-library-bugs`) reached 203/203 in both configurations, with the two fixstar
expectations corrected and their conditionals removed.
