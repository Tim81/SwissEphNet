# Porting sweph.c: the remaining stages

`sweph.c` is the largest single file in the delta: **125 filtered hunks**. This records how
they divide, because the division is not obvious from the diff and re-deriving it costs a
submodule checkout plus a run of `gen-delta.ps1`.

Regenerate the map with `scripts/gen-delta.ps1 -File sweph.c`, then attribute each `# sweph.c:N`
citation to its enclosing C function.

## Landed

**Ayanamsha (10 hunks).** `get_aya_correction` and its three call sites,
`swi_get_ayanamsa_ex`, `swe_get_ayanamsa_ex`, `swe_set_sid_mode`. This is what made
`prec_offset` load-bearing.

**Crossing functions.** Already present from earlier work -- `swe_solcross`,
`swe_solcross_ut`, `swe_mooncross`, `swe_mooncross_ut`, `swe_mooncross_node`,
`swe_mooncross_node_ut`, `swe_helio_cross`, `swe_helio_cross_ut`, with tests in
`Tests/SwissEphNet.Tests/SwissEphTest.swe_crossing.cs`. Not part of the remaining work,
contrary to the original plan's sequencing.

## Remaining: 112 hunks across 43 functions

Too large for one change. Four slices, ordered so each compiles and keeps the gates green,
and so a bisect after a numeric regression stays cheap.

### A. Ephemeris file layer (~23 hunks)

`read_const` (12), `swe_set_ephe_path` (3), `swi_get_denum` (2), `do_fread`,
`get_new_segment`, `rot_back`, `load_dpsi_deps`, `swe_close`, `free_planets`.

Self-contained: file header parsing and the open/close lifecycle. `read_const` is the single
largest function in the delta. Note the baseline cannot see most of this -- it never
subscribes to `OnLoadFile` -- so the conformance corpus is the only gate with real coverage
here, and `swi_strnlen` loses its last caller in this slice.

### B. Position pipeline (~40 hunks)

`app_pos_etc_plan` (10), `lunar_osc_elem` (9), `main_planet` (3), `swi_deflect_light` (3),
`main_planet_bary` (2), `app_pos_etc_sun` (2), `app_pos_etc_mean` (2), plus single hunks in
`jplplan`, `swemoon`, `sweplan`, `app_pos_etc_plan_osc`, `swi_nutate`, `intp_apsides`,
`swi_plan_for_osc_elem`, `meff`, `calc_epsilon`, `rot_back`.

The numeric core. Every planetary position moves, so expect wide baseline movement and
regenerate per area with an explicit scope.

### C. Dispatch and new API (~30 hunks)

`swecalc` (8), `sweph` (8), `swe_calc` (7), `swe_calc_ut`, `plaus_iflag`, `swe_set_topo`,
`swe_get_ayanamsa_ut`, `calc_center_body` (new), plus `SEFLG_CENTER_BODY` and
`SE_PLMOON_OFFSET` plumbing. Delete `swe_set_timeout`.

`calc_center_body`, `swe_calc_pctr` and `swe_get_current_file_data` are new: absent from the
port, so they appear inside hunks attributed to whichever function precedes them rather than
under their own names.

### D. Fixed stars (~15 hunks)

`swi_fixstar_calc_from_record` (4), `fixstar_cut_string` (3), `load_all_fixed_stars` (3),
`fixstar_calc_from_struct` (2), `swi_fixstar_load_record` (2), `swe_get_planet_name` (2),
`get_builtin_star`, `swe_fixstar2`, `swe_fixstar_mag`.

Touches the `sefstars.txt` path, where `CFile.Seek`'s sticky-EOF defect lived -- see
`docs/known-issues.md`. Keep the tests added there in view when changing this.
