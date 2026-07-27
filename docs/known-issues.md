# Known issues found by the characterization baseline

Findings from running the baseline gate cross-platform (Windows, the platform it is
locked to, vs. a Linux container with the same SDK). See
`Tools/BaselineGen/README.md` for why the gate is platform-locked rather than given
a looser tolerance. Numbers below are from `.NET SDK 10.0.302`,
`mcr.microsoft.com/dotnet/sdk:10.0`, Ubuntu 24.04, against commit `8f5615e` (before
the angle-wraparound fix) and again after it.

## swe_houses_armc, hsys 'Y' (APC houses): a genuine, large divergence

`swe_houses_armc(armc=270, geolat=50, eps=40, hsys='Y', ...)`:

| Platform | cusp[2] |
|---|---|
| Windows | `270` |
| Linux | `243.43494882292202` |

A 26.6-degree difference is not floating-point noise -- reproduced independently
(same inputs, same commit, `BaselineGen` run inside the Linux container and diffed
against the committed Windows baseline). The two platforms are taking different
code paths through the APC houses calculation in `SweHouse.cs`, most likely because
an `acos`/`asin` argument lands marginally outside `[-1, 1]` on one platform and not
the other (a classic source of platform-dependent NaN/branch divergence in
trigonometric code that does not clamp its inputs). This is a numerical-stability
problem in the port, not in the harness or the tolerance.

This is not a tolerance problem: it survives every threshold measured in
`Tools/BaselineGen/README.md`'s "Platform lock" table, including the loosest
(1e-8 absolute / 1e-8 relative -- eight orders of magnitude looser than what
ships). It is the only `houses-armc` field that does. Everything else that
diverges cross-platform shrinks as the tolerance loosens, the way accumulated
floating-point noise should; this one field does not move, which is exactly the
signature of two platforms executing genuinely different branches rather than
the same branch with a slightly different rounding error.

**Action for the 2.10.03 port work:** when porting the SweHouse delta, check
whether the C source clamps the argument passed to `acos`/`asin` in the APC branch
(search for `case 'Y'` in `SwissEphNet/CPort/SweHouse.cs`), and whether upstream
2.10.03 changed that code. This is exactly the kind of case the baseline exists to
catch -- freeze it now, and the 2.10.03 PR's own verify run will show clearly
whether the fix changes this cusp value on Windows (expected: yes) and whether it
also stops the platforms from diverging (worth checking, not assumed).

## calc/pheno SPEED fields: differentiation noise, expected but unexplained in detail

Cross-platform, SPEED-flagged fields (numerically differentiated results, not
closed-form) diverge at roughly 1e-7 to 1e-9 relative -- small, but well past the
gate's tolerance, concentrated in `calc` (SE_OSCU_APOG, Mercury named as the worst
cases) and `pheno`. `CPort` computes speed via numerical differentiation (small time
offsets and a finite difference), which amplifies whatever ULP-level difference
already exists between platforms in the underlying position calculation. This is
plausible and consistent with how numerical differentiation behaves, but it has not
been traced to a specific line of CPort. Recorded here as expected-but-unexplained,
not as "this is fine": if the ratio changes meaningfully in future cross-platform
reports (see `--report-only`), that is worth a fresh look, not an assumption that
it's the same known issue.

## hsys 'I' (Sunshine houses): smaller, more numerous divergences near tolerance

Not one of the two findings this doc originally set out to record, but visible in
the same Linux run: `swe_houses_armc` with `hsys='I'` produces a cluster of small
(1e-10 to 1e-11 absolute) but tolerance-exceeding divergences, mostly at extreme
`geolat` and specific `armc` values. Example: `H|I|0|-89|0` gives cusp[3] =
`120.00000000000338` (Windows) vs `119.99999999997604` (Linux) -- about 2.7e-11
absolute, roughly double the field's own relative threshold at that magnitude.
Unlike the 'Y' finding, these are not obviously wrong (both platforms agree to 10+
significant figures) and are not a raw-360-style wraparound artifact either (see
the cross-platform section below) -- just accumulated ULP drift through a
computation path (declination/ascensional-difference trig for Sunshine houses)
that is evidently more sensitive to it than most. Not chased further; flagged in
case it becomes relevant when porting the 2.10.03 SweHouse delta.

## Cross-platform divergence: measured, and why the gate is platform-locked

Full numbers, the tolerance-level cost table, and the reasoning for locking the
gate to Windows instead of loosening the shipped tolerance, are in
`Tools/BaselineGen/README.md` under "Platform lock". Summary: of 3,443,058 numeric
fields compared, 47,052 differ at all between Windows and Linux. Of those, only
108 are genuine angle-wraparound (raw difference > 180 degrees) and the
wraparound fix (`Comparer.EffectiveAbsoluteDiff`) resolves all 108 of them
exactly; 3,346 fields are still beyond the shipped `1e-12`/`1e-13` tolerance. The
two findings above (APC houses, and the calc/pheno SPEED fields) account for the
bulk of that remainder.

An earlier pass at this classification reported 2,637 fields as "wraparound" --
that number came from a comparison bug (`min(d, |360-d|)` computed on the raw
difference without first checking it was actually a large, near-360 difference,
so it just returned small differences unchanged and mislabeled them). The
corrected number is 108, confirmed by checking that the wraparound fix resolves
exactly that many fields and rows, and that zero of the remaining 3,346
beyond-tolerance fields have a raw difference anywhere near 360.
