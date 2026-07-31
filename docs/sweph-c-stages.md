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

## The other 112 hunks: landed in four slices

Too large for one change. Landed as four slices, in the order below so each compiled and
kept the gates green, and so a bisect after a numeric regression stayed cheap. `sweph.c` is
now complete; this section is kept as a record of how the work divided; each subsection
cites the commit that landed it.

### A. Ephemeris file layer (~23 hunks) -- `276fc5b`

`read_const` (12), `swe_set_ephe_path` (3), `swi_get_denum` (2), `do_fread`,
`get_new_segment`, `rot_back`, `free_planets`.

Self-contained: file header parsing and the open/close lifecycle. `read_const` is the single
largest function in the delta. The baseline cannot see most of this -- it never subscribes to
`OnLoadFile` -- so the conformance corpus was the gate with real coverage here.

Two expectations from the original plan did not hold, both mapping artifacts rather than
defects. `swi_strnlen` does not lose its last caller in this slice: its only call is in
`swi_fixstar_load_record`, which belongs to slice D. And `swe_close` and `load_dpsi_deps` are
byte-identical between 2.08 and 2.10.03; the hunks this section originally attributed to them
actually belong to `swe_set_ephe_path` and `swe_set_jpl_file` (`swe_set_jpl_file` itself
remains unported, outside every slice's function list), because `gen-delta.ps1` labels a hunk
with the nearest preceding function signature rather than the location of the change.

`rot_back` is the hunk that mattered most: the port read `swed.oec2000.seps`/`.ceps`, which
nothing in this port ever populated, so every position rotated back through it used a J2000
obliquity of zero. 2.10.03 replaces the reads with literal constants, which is what made this
visible. The file-backed oracle grid went from 791 of 2,024 bit-identical to 1,975.

### B. Position pipeline (~40 hunks) -- `83c0363`

`app_pos_etc_plan` (10), `lunar_osc_elem` (9), `main_planet` (3), `swi_deflect_light` (3),
`main_planet_bary` (2), `app_pos_etc_sun` (2), `app_pos_etc_mean` (2), plus single hunks in
`jplplan`, `swemoon`, `sweplan`, `app_pos_etc_plan_osc`, `swi_nutate`, `intp_apsides`,
`swi_plan_for_osc_elem`, `meff`, `calc_epsilon`, `rot_back`.

Billed in the original plan as the numeric core, with every planetary position expected to
move. That did not happen: nothing moved, not one row, in any gate, on either framework. The
substance of this slice's delta turned out to be brace placement, dead-code removal, and
plumbing (an `iplmoon` parameter, `SEFLG_CENTER_BODY` blocks) for a dispatch path that stayed
unreachable until slice C.

### C. Dispatch and new API (~30 hunks) -- `b288652`

`swecalc` (8), `sweph` (8), `swe_calc` (7), `swe_calc_ut`, `plaus_iflag`, `swi_force_app_pos_etc`,
`swe_get_ayanamsa_ut`, `calc_center_body` (new). `swe_set_timeout` needed no deletion --
confirmed to have been commented out in this port all along, so the original plan's claim
that it needed removing was itself wrong.

`calc_center_body`, `swe_calc_pctr` and `swe_get_current_file_data` are new: absent from the
port, so they appeared inside hunks attributed to whichever function precedes them rather than
under their own names. All three now exist. `NOT-IMPLEMENTED` became empty here: every
function the 2.10.03 API surface declares is implemented.

### D. Fixed stars (~15 hunks) -- `63cd024`

`swi_fixstar_calc_from_record` (4), `fixstar_cut_string` (3), `load_all_fixed_stars` (3),
`fixstar_calc_from_struct` (2), `swi_fixstar_load_record` (2), `swe_get_planet_name` (2),
`get_builtin_star`, `swe_fixstar2`, `swe_fixstar_mag`.

Touches the `sefstars.txt` path, where `CFile.Seek`'s sticky-EOF defect lived -- see
`docs/known-issues.md`. This slice completed both `sweph.c` and the file-backed oracle grid.
