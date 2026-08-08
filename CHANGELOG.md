# Changelog

Fork history from the Swiss Ephemeris 2.08 upgrade forward. Entries are prose bullets, not a
generated commit list — for anything earlier, see git history.

## 2.10.3.1

- Full XML documentation (`<summary>`, `<param>`, `<returns>`) added for all 116 public `swe_*`
  methods in `SwissEphNet/SwissEph.swephexp.h.cs`, plus the `SEFLG_*`/`SE_*`/`SEMOD_*` constants
  they reference. Purely additive: no signature or behavior changed, and `SwissEphNet/CPort/` is
  untouched. IntelliSense now shows the same documentation a C caller gets from `swephexp.h`
  instead of nothing (`CS1591` is suppressed repo-wide, so this gap was previously silent).
- `Programs/SweTest`: fixed `insert_gap_string_for_tabs`'s `LEN_SOUT` bound. It now counts bytes
  the way the C's `strlen` does, using native single-byte semantics on Windows (the narrow-argv
  code page) and UTF-8 elsewhere, instead of counting UTF-16 characters. Accented `-g` gap values
  no longer stop tab substitution one byte early. This affects the `SweTest` program's console
  output, not the `SwissEphSharp` library API.
- `swe_houses_ex2` now re-reads `sid_data` after its `SE_SIDM_FAGAN_BRADLEY` fallback, matching
  the C's pointer semantics (`swehouse.c:221`). No input reachable through the public API changes
  value from this today; it closes an audited fidelity gap rather than fixing an observed bug.
- Test coverage: `BaselineMatrix` now exercises 7 more of the library's 107 public entry points
  (`swe_houses_ex2`, `swe_houses_armc_ex2`, `swe_get_ayanamsa_name`, `swe_calc_pctr`,
  `swe_lat_to_lmt`, `swe_lmt_to_lat`, `swe_get_current_file_data`), all 100% EXACT on both TFMs.
  A new `NutationTableFidelityTest` value-diffs the nutation coefficient table against upstream
  source instead of only checking its length.
- CI gained macOS legs on the baseline and conformance gates (report-only, since the baseline
  stays locked to Windows; see `Tools/BaselineGen/README.md`).
- No breaking changes. See `docs/known-issues.md` and `docs/compliance-2.10.03.md` for the
  verification detail behind this release.

## 2.10.3 (the first release of this fork published to nuget.org, under the SwissEphSharp package ID; entries from 2.8.0.2 down are the original project's, published under SwissEphNet)

- LICENSE CHANGE, read this before upgrading. 2.8.0.2 was distributed under the GNU General
  Public License version 2 or later. This release is under the Swiss Ephemeris dual license:
  the GNU Affero General Public License, or a Swiss Ephemeris Professional License bought from
  Astrodienst. The practical difference is the AGPL's network clause. Under GPL-2.0 you could
  run this library inside a web service and owe nobody source, because nothing was distributed.
  Under the AGPL, operating the service is itself the trigger, and users who reach it over a
  network can require the complete corresponding source of your whole service, not just this
  library. If that does not work for your project, the Professional License is the other arm.
  This follows Astrodienst's own relicensing of Swiss Ephemeris and applies to the C library
  just as much as to this port. LICENSE, agpl-3.0.txt and NOTICE ship at the package root.
- SE_VERSION now reports "2.10.03", matching the C library this port tracks (upstream tag
  v2.10.3bfinal). Every stage of the 2.10.03 delta has landed: the header/constants stage,
  swephlib.c, the ayanamsha machinery, sweph.c, swecl.c, swehouse.c and swetest.c.
- swe_houses, swe_houses_ex, swe_houses_armc, swe_house_pos and swe_house_name each gained an
  int hsys overload alongside the existing char hsys overload, matching upstream swephexp.h.
- New API surface: swe_houses_ex2, swe_houses_armc_ex2, swe_calc_pctr, swe_get_current_file_data.
- swe_lun_occult_when_glob and swe_lun_occult_when_loc each gained an Int32 backward overload, so
  SE_ECL_ONE_TRY can be OR-ed into that bitfield the way swephexp.h declares. The existing bool
  overload is unchanged and still binds; it can only pass 0 or 1, so it can never request the
  flag. Reflection that resolves either method by name alone now throws AmbiguousMatchException.
- OnLoadFile is gone; ephemeris files are read from disk by default through the new
  SwissEph.FileProvider (IEphemerisFileProvider).
- The assembly is now named SwissEphSharp (was SwissEphNet), so this package can coexist in one
  dependency graph with the original SwissEphNet 2.8.0.2 instead of silently colliding with it at
  build time and crashing at runtime. The namespace stays SwissEphNet.
- 2.10.3 is the only release that ships netstandard2.0, not the last of several. 2.8.0.2 shipped
  net40 and netstandard1.0 and never carried netstandard2.0, and net8.0 or later will be required
  from the next release on, so the window in which this library is reachable from .NET Framework
  is this one version. Consumers on .NET Framework 4.6.1+ can take 2.10.3 and should pin to it
  deliberately rather than expect it to persist. netstandard2.0 is a compatibility target,
  not a correctness one: measured on .NET Framework 4.8 and 4.6.2 against .NET 10, the same
  asset's swe_calc differs on 29 of 102 calls (34 bodies x 3 epochs; worst case SE_ADMETOS
  latitude speed, 2.28e-3 relative -- an absolute divergence of 1.4e-08 deg/day against a base
  value of about -6.1e-06), while net8.0 and net10.0 agree on all 102. The cause is .NET
  Framework's less accurate Math.Sin/Math.Cos/Math.Tan near quarter-turn boundaries, not this
  port; see the "V:2.10.3" section of README.md for the full measurement.
- Numerous port bug fixes surfaced by the 2.10.03 work, several of them upstream C bugs Astrodienst
  fixed between 2.08 and 2.10.03 that this port now also carries: swe_nod_aps/swe_nod_aps_ut
  returning all-zero nodes and apsides, an obliquity read that affected every SEFLG_SWIEPH
  position, eclipse magnitude and obscuration off by a factor of 100, and the swe_house_pos buffer
  that was one element short for Gauquelin houses. See docs/compliance-2.10.03.md for the
  verification numbers behind this release and docs/known-issues.md and the "V:2.10.3" section
  of README.md for the full list.
- You are also inheriting everything in the 2.8.1.0 entry below, even though no such package was
  ever published. On nuget.org the step is 2.8.0.2 straight to this release, so those changes
  arrive with it: DIR_GLUE went from '\' to '/' on every platform, which changes the asteroid
  file names a file provider is asked for; DefaultEncoding became UTF-8 explicitly rather than
  falling back to it when Windows-1252 could not be resolved; and C.strcmp, strncmp, strstr and
  strchr became ordinal instead of culture-sensitive, which affects fixed-star name search and
  sort under a non-invariant culture. Read that entry as part of this one.

## 2.8.1.0 (not published to nuget.org; source-only distribution)

- Package ID renamed from SwissEphNet to SwissEphSharp. SwissEphNet on nuget.org belongs to the
  upstream author's own release and this fork cannot publish under it. At this release the
  assembly name and namespace were unaffected: both stayed SwissEphNet, so the only change a
  consumer needed to make was the PackageReference itself. The assembly was renamed too, later,
  at 2.10.3 above -- see that entry and the "Package name" section of README.md.
- Retarget the library to netstandard2.0;net8.0;net10.0, dropping net40 and netstandard1.0.
- Relicense from GPL-2.0-or-later to the Swiss Ephemeris 2.10.3 dual license: AGPL-3.0
  or a Swiss Ephemeris Professional License from Astrodienst. See LICENSE and NOTICE.
- Fix SwissEph.DIR_GLUE: was hardcoded to '\\' on every platform, now '/' per the upstream C
  source. Asteroid file names passed to OnLoadFile change accordingly, e.g. "ast4/se04179.se1"
  instead of "ast4\se04179.se1".
- Fix SwissEph.DefaultEncoding: was falling back to UTF-8 silently whenever Windows-1252 could
  not be resolved (missing System.Text.Encoding.CodePages reference); UTF-8 is now the explicit,
  deliberate default, matching the actual encoding of the ephemeris data files.
  You can still override it via the static SwissEphNet.SwissEph.DefaultEncoding property.
- Fix C.strcmp/strncmp/strstr/strchr: were culture-sensitive (string.Compare/IndexOf), now
  ordinal, matching C's byte-wise semantics. Affects fixed-star name search/sort under
  non-invariant cultures.
- Several additional port bug fixes with no signature change: swe_fixstar multi-word star names
  and an off-by-one in search-name formatting, a missing ref-parameter mutation and missing guard
  in the heliacal Moon branch, swe_set_astro_models throwing on short input, C.atoi sign handling,
  `CPointer<T>.operator !=` always returning false, and a netstandard2.0-only infinite
  recursion in a string-extension method. See NOTICE and docs/known-issues.md for details.

## 2.8.0.2

- Fix the #41 issue

## 2.8.0.1

- Update to version 2.08 of SwissEphemeris
