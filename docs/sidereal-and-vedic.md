# Jyotish and sidereal astrology with SwissEphNet

Swiss Ephemeris was written for tropical Western astrology, with sidereal calculation added as an
option layered on top of that core. This file covers what changes when that option is turned on:
the sidereal calculation workflow, the ayanamsha catalogue and its pitfalls, sidereal house
behaviour (including a documented, unpatched defect this port has not fixed because the fix belongs
upstream; it was released there in `6c989727` and is not yet synced into the port), nakshatras, rāśi ingresses, and which pieces of a full Jyotish stack this library does
and does not provide. It is written for callers building Vedic or other sidereal astrology software
on top of this port who already know `swe_calc_ut` and `swe_houses_ex` from the tropical case and
need to know what is different, not the same ground `README.md` already covers.

Every ayanamsha value quoted below is measured, not computed here — see "The 47 predefined
ayanamshas" below for the full 47-mode reference table and its sourcing.

## The workflow

Four calls, always in this order: pick an ayanamsha with `swe_set_sid_mode`, pass
`SEFLG_SIDEREAL` to `swe_calc_ut` for planetary positions, pass the same flag to `swe_houses_ex`
for houses, then use `swe_split_deg` to turn a longitude into sign/degree/nakshatra form for
display. `swe_set_sid_mode` sets calculation state on the `SwissEph` instance that every later
sidereal call reads, so it only needs to run once per instance, before the first sidereal call.

This computes the Moon's sidereal longitude (Lahiri) on 1 January 2020 and prints its rāśi. It
needs no data files: `SEFLG_MOSEPH` selects the built-in analytic ephemeris, the same choice
`README.md`'s first example makes, for the same reason — it works immediately, with a margin of
error this file does not re-derive.

```csharp
using SwissEphNet;

using var swe = new SwissEph();
swe.swe_set_sid_mode(SwissEph.SE_SIDM_LAHIRI, 0, 0);

// Julian day for 2020-01-01 00:00 UT, Gregorian calendar.
double jd = swe.swe_julday(2020, 1, 1, 0.0, SwissEph.SE_GREG_CAL);

var xx = new double[6];
string serr = "";
int ret = swe.swe_calc_ut(jd, SwissEph.SE_MOON,
    SwissEph.SEFLG_MOSEPH | SwissEph.SEFLG_SIDEREAL, xx, ref serr);

if (ret < 0)
{
    Console.WriteLine($"error: {serr}");
    return;
}

swe.swe_split_deg(xx[0], SwissEph.SE_SPLIT_DEG_ZODIACAL,
    out int ideg, out int imin, out int isec, out double dsecfr, out int isign);

string[] rasi = {
    "Mesha", "Vrishabha", "Mithuna", "Karka", "Simha", "Kanya",
    "Tula", "Vrischika", "Dhanu", "Makara", "Kumbha", "Meena"
};
Console.WriteLine($"Moon: {rasi[isign]} {ideg}°{imin}'{isec}\"");
```

Output:

```
Moon: Kumbha 22°0'22"
```

`swe_houses_ex` takes the same `SEFLG_SIDEREAL` flag and returns cusps already reduced to the
sidereal zodiac. If you also subtract an ayanamsha from its output you will double-count; the next
section covers when hand-subtraction is safe and when it is not.

## Subtracting an ayanamsha by hand: when it works and when it does not

It is tempting to compute a sidereal longitude yourself: call `swe_calc_ut` tropically, call
`swe_get_ayanamsa_ex` for the same instant, subtract. That does reproduce
`swe_calc_ut(SEFLG_SIDEREAL)` exactly — **provided both halves use the same nutation convention.**
Measured, Mars under Lahiri with `SEFLG_MOSEPH`, residual in arcseconds against `swe_calc`'s own
sidereal answer:

| pairing | JD 2451545.0 | JD 2460000.5 | JD 1721057.5 |
|---|---|---|---|
| tropical default − ayanamsha default (both true) | 0.0000 | 0.0000 | 0.0000 |
| tropical `NONUT` − ayanamsha `NONUT` (both mean) | 0.0000 | 0.0000 | 0.0000 |
| tropical default − ayanamsha `NONUT` (mismatched) | **−13.9315** | **−9.2745** | **+16.4375** |

`SEFLG_SIDEREAL` does force `SEFLG_NONUT` inside `swe_calc` (`sweph.c:6094-6096`), and
`swe_get_ayanamsa_ex` does add nutation back unless you pass `SEFLG_NONUT` (`sweph.c:2941`). But
nutation then enters both sides of your subtraction identically and cancels, so the mechanism does
not produce an error on its own. What produces an error is pairing a *true* tropical position with a
*mean* ayanamsha, or the reverse: that is off by nutation in longitude, up to about 17 arcseconds.

So the rule is not "never subtract" — it is **match the two calls**. Either leave both at their
defaults or pass `SEFLG_NONUT` to both. Astrodienst's own advice (`swephprg` §12.2) is still to
prefer `swe_calc(SEFLG_SIDEREAL)` and treat the ayanamsha functions as display, which avoids the
question; if you do display a value, include nutation.

**Where hand-subtraction genuinely cannot work is under the projection bits.** `SE_SIDBIT_ECL_T0`
and `SE_SIDBIT_SSY_PLANE` re-project coordinates onto another plane rather than sliding the origin
along the ecliptic, so no scalar subtraction reproduces them. Same instant and body:

| sid_mode | residual | sidereal latitude | tropical latitude |
|---|---|---|---|
| Lahiri | 0.0000″ | −1.067792 | −1.067792 |
| Lahiri \| `ECL_T0` | 0.3413″ | −1.065195 | −1.067792 |
| Lahiri \| `SSY_PLANE` | −63.9132″ | −0.044779 | −1.067792 |

The latitude column is the tell: under the traditional modes it is untouched, and under the other
two it moves, because the frame moved. If your code subtracts an ayanamsha and someone selects one
of the four modes that force `ECL_T0` (18, 19, 20, 34), your longitudes quietly stop matching the
library's.

## Choosing an ayanamsha

The 47 predefined modes spread over roughly 357° at J2000 (mode 40 reads
356°50'31.83", the same direction as −3°9'28.17"; "Mode 40 wraps past 360°", inside the catalogue below,
explains the wraparound), but the ones actually in circulation as competing astrological
standards cluster far more tightly. At 1 January 2000, 12:00 UT, true (nutated) ayanamsha:

| Mode | Value | Role |
|---|---|---|
| Lahiri (1) | 23°51'11.6009" | Indian civil/governmental standard |
| Lahiri ICRC (46) | 23°51'10.5089" | The original 1956 decree, before a later revision |
| Lahiri 1940 (43) | 23°50'18.4322" | Lahiri's own earlier value, superseded |
| Krishnamurti (5) | 23°45'22.9326" | KP practice |
| Raman (3) | 22°24'24.9162" | B.V. Raman's school |
| Fagan/Bradley (0) | 24°44'11.1485" | Western sidereal standard |

That is roughly 2.3° of spread among just these six widely-used modes, and the full 47-mode table
spans a much wider range still. A chart run through Lahiri and the same chart run through Raman can
put a planet in a different sign entirely near a sign boundary — this is not a rounding-level
difference.

**Lahiri (mode 1)** is the ayanamsha specified by the Indian government's Calendar Reform Committee
of 1956 for the *Rashtriya Panchang*, the Indian national civil calendar. It is India's civil
standard, adopted by government decree — that is a statement about its official status, not a
claim that it is astronomically or astrologically correct, and not a claim that it is the consensus
choice of practising astrologers, many of whom use other ayanamshas by tradition or school.

**Krishnamurti (mode 5)** is required, not merely preferred, for anyone implementing Krishnamurti
Paddhati (KP) astrology: the sub-lord tables that KP's technique depends on were built against this
specific ayanamsha, so substituting a different one breaks the technique's own internal
consistency, independent of any question about which ayanamsha is "more correct." Note the naming
trap set out in "A naming trap: mode 45 is not \"the KP New Ayanamsa\"" below — a different mode,
`SE_SIDM_KRISHNAMURTI_VP291`, carries "Krishnamurti" in its display name too but is not what most
KP software means by "the KP ayanamsha."

**Raman (mode 3)** identifies B.V. Raman's astrological school specifically. Astrodienst's own
catalogue entry for it goes further than neutral description: it calls the mode "apparently not
based on a valid precession theory" (see "The 47 predefined ayanamshas" below, row 3) — an editorial judgment
attributable to Astrodienst, not adjudicated here.

Astrodienst's own verdict, quoted from its documentation of the ayanamsha family groupings, is the
right note to end this section on: the definitions "all look arbitrary in their definitions, and
there is no striking evidence – from a mere astronomical point of view – for anyone of them." This
library does not, and should not, call any ayanamsha correct. It ships forty-seven of them precisely
because the choice is a matter of astrological tradition, not physics. There is a default, and it
is not one you want silently: if `SEFLG_SIDEREAL` is set and `swe_set_sid_mode` was never called,
`swi_get_ayanamsa_ex` selects **Fagan/Bradley** for you (`sweph.c:3047-3048`, and the same fallback
appears at `sweph.c:641`, `:6444`, `:7652` and `swehouse.c:229`). That is 53 arcminutes from Lahiri
at J2000 — enough to move a body across a nakshatra boundary and change a Vimshottari dasha lord.
Call `swe_set_sid_mode` explicitly, before the first sidereal call, with a mode your users or
tradition specify.

## The 47 predefined ayanamshas

Reference catalogue of the 47 predefined ayanamsha modes (`SE_SIDM_*`, indices 0–46,
`SE_NSIDM_PREDEF = 47`). Ground truth for epochs and offsets is the `ayanamsa[]` init table at
`external/swisseph/sweph.h:351-596`, carrying Dieter Koch's own commentary. Name strings are the
`ayanamsa_name[]` array at `external/swisseph/sweph.c:130-179`, exactly what
`swe_get_ayanamsa_name()` returns. Constant identifiers are `external/swisseph/swephexp.h:238-284`.
Measured values are the true (nutated) ayanamsha at 1 Jan 2000, 12:00 UT, from a fresh MSVC build
of the pinned C (`v2.10.3bfinal`); nutation in longitude at that instant is −0°0'13.9315.

Astrodienst's published programmer's documentation misprints mode 30's constant as
`SE_SIDM_GALCENT_RGBRAND`; the real identifier, confirmed in `swephexp.h:268`, is
`SE_SIDM_GALCENT_RGILBRAND`. Code copied verbatim from that documentation page does not compile.

| # | Constant | Name string | Defining principle | Value at J2000 | Notes |
|---|----------|--------------|---------------------|-----------------|-------|
| 0 | `SE_SIDM_FAGAN_BRADLEY` | `Fagan/Bradley` | Epoch B1950.0 (JD 2433282.42346, TT); Bradley's empirical "synetic vernal point", 24.042044444° | 24°44'11.1485 | `prec_offset` = Newcomb |
| 1 | `SE_SIDM_LAHIRI` | `Lahiri` | Epoch 21 Mar 1956, 00:00 TT; Indian Calendar Reform Committee decree, 23.245524743° (Wahr-1980 nutation subtracted) | 23°51'11.6009 | `prec_offset` = IAU 1976 |
| 2 | `SE_SIDM_DELUCE` | `De Luce` | Epoch 1 Jan 1 BCE (year 0, Julian calendar), UT; offset 0 | 27°48'42.7786 | `prec_offset` = 0; rewritten in SE 2.09 |
| 3 | `SE_SIDM_RAMAN` | `Raman` | Epoch J1900 (1900 Jan 0.5, TT); offset 21.01444° | 22°24'24.9162 | `prec_offset` = Newcomb; Astrodienst: "apparently not based on a valid precession theory" |
| 4 | `SE_SIDM_USHASHASHI` | `Usha/Shashi` | Epoch J1900 (TT); offset 18.66096° | 20°3'13.2162 | `prec_offset` = −1; defining principle **not established** (see prose) |
| 5 | `SE_SIDM_KRISHNAMURTI` | `Krishnamurti` | Epoch J1900 (TT); offset 22.363889° | 23°45'22.9326 | `prec_offset` = Newcomb; Astrodienst: does not know the method used |
| 6 | `SE_SIDM_DJWHAL_KHUL` | `Djwhal Khul` | Epoch J1900 (TT); offset 26.9630976°, "channeled" Aquarius-ingress date 1 Jul 2117 | 28°21'20.9115 | `prec_offset` = 0; theosophical origin |
| 7 | `SE_SIDM_YUKTESHWAR` | `Yukteshwar` | Epoch J1900 (TT); offset 21.082222° | 22°28'29.7594 | `prec_offset` = −1; Astrodienst: "This ayanamsha seems to be wrong" |
| 8 | `SE_SIDM_JN_BHASIN` | `J.N. Bhasin` | Epoch J1900 (TT); offset 21.365556° | 22°45'29.7618 | `prec_offset` = −1; defining principle **not established** (see prose) |
| 9 | `SE_SIDM_BABYL_KUGLER1` | `Babylonian/Kugler 1` | Epoch year −100, UT (Kugler cluster 1); offset −5.66667° | 23°31'47.1721 | `prec_offset` = −1; wrong in SE until 14 Sept 2018; Kugler's bibliographic source **not established** |
| 10 | `SE_SIDM_BABYL_KUGLER2` | `Babylonian/Kugler 2` | Epoch year −100, UT (Kugler cluster 2); offset −4.26667° | 24°55'47.1721 | `prec_offset` = −1; wrong in SE until 14 Sept 2018; Kugler's bibliographic source **not established** |
| 11 | `SE_SIDM_BABYL_KUGLER3` | `Babylonian/Kugler 3` | Epoch year −100, UT (Kugler cluster 3); offset −3.41667° | 25°46'47.1721 | `prec_offset` = −1; wrong in SE until 14 Sept 2018; Kugler's bibliographic source **not established** |
| 12 | `SE_SIDM_BABYL_HUBER` | `Babylonian/Huber` | Epoch year −100, UT; Peter Huber, *Centaurus* 5 (1958) 192–208; offset −4.46667° | 24°43'47.1721 | `prec_offset` = −1; had a wrong initial value until 14 Sept 2018 |
| 13 | `SE_SIDM_BABYL_ETPSC` | `Babylonian/Eta Piscium` | Epoch JD 1673941, UT; eta Piscium culminating with the zero point (Mercier); offset −5.079167° | 24°31'7.1691 | `prec_offset` = −1 |
| 14 | `SE_SIDM_ALDEBARAN_15TAU` | `Babylonian/Aldebaran = 15 Tau` | Epoch year −100, UT; Aldebaran fixed at 15 Tau; offset −4.44138598° | 24°45'18.1946 | `prec_offset` = 0; Astrodienst's own construction from a Fagan remark |
| 15 | `SE_SIDM_HIPPARCHOS` | `Hipparchos` | Epoch JD 1674484.0 (Mercier's date for Hipparchus/Ptolemy), UT; offset −9.33333° | 20°14'38.1057 | `prec_offset` = −1 |
| 16 | `SE_SIDM_SASSANIAN` | `Sassanian` | Epoch JD 1927135.8747793, UT (Astrodienst associates this with the Zij al-Shah revision under Khusrau Anushirwan); offset 0 | 19°59'20.7224 | `prec_offset` = −1; causal framing is Astrodienst's own inference, not a cited Mercier statement |
| 17 | `SE_SIDM_GALCENT_0SAG` | `Galact. Center = 0 Sag` | Runtime: Galactic Center fixed at 0° Sagittarius (240°); Astrodienst's own, D. Koch 1999 (SE 1.50), "without any astrological background" | 26°50'31.8335 | Runtime star-lookup mode (star is built into the library; no `sefstars.txt` needed); all sid bits stripped |
| 18 | `SE_SIDM_J2000` | `J2000` | Reference frame: epoch J2000 (2000 Jan 1.5, TT), offset 0 | −0°0'13.9314 | `prec_offset` = 0; `swe_set_sid_mode` forces `SE_SIDBIT_ECL_T0`; near-zero because this is a frame, not an astrological ayanamsha |
| 19 | `SE_SIDM_J1900` | `J1900` | Reference frame: epoch J1900 (1900 Jan 0.5, TT), offset 0 | 1°23'33.7602 | `prec_offset` = 0; `swe_set_sid_mode` forces `SE_SIDBIT_ECL_T0`; near-zero, a frame not an ayanamsha |
| 20 | `SE_SIDM_B1950` | `B1950` | Reference frame: epoch B1950 (1950 Jan 0.923, TT), offset 0 | 0°41'40.2010 | `prec_offset` = 0; `swe_set_sid_mode` forces `SE_SIDBIT_ECL_T0`; near-zero, a frame not an ayanamsha |
| 21 | `SE_SIDM_SURYASIDDHANTA` | `Suryasiddhanta` | Epoch 21 Mar 499 CE, 07:30:31.57 UT, Ujjain (75.7684565° E) — mean-Sun Aries ingress at mean equinox of date; offset 0 | 20°53'28.2804 | `prec_offset` = 0; Astrodienst's own derivation from the text via Simon et al. (1994) |
| 22 | `SE_SIDM_SURYASIDDHANTA_MSUN` | `Suryasiddhanta, mean Sun` | Same epoch as mode 21, but ingress taken at the true position of the mean Sun; offset −0.21463395° | 20°40'35.5982 | `prec_offset` = 0 |
| 23 | `SE_SIDM_ARYABHATA` | `Aryabhata` | Epoch 21 Mar 499 CE, 06:56:55.57 UT, Ujjain — analogous to mode 21; offset 0 | 20°53'28.2836 | `prec_offset` = 0 |
| 24 | `SE_SIDM_ARYABHATA_MSUN` | `Aryabhata, mean Sun` | Same epoch as mode 23, mean-Sun variant analogous to mode 22; offset −0.23763238° | 20°39'12.8070 | `prec_offset` = 0 |
| 25 | `SE_SIDM_SS_REVATI` | `SS Revati` | Same epoch as mode 21; Suryasiddhanta's stated Revati/zeta Piscium polar longitude, 359°50'; offset −0.79167046° | 20°5'58.2667 | `prec_offset` = 0; frozen at the 499 CE epoch, not runtime star lookup |
| 26 | `SE_SIDM_SS_CITRA` | `SS Citra` | Same epoch as mode 21; Suryasiddhanta's stated Citra/Spica polar longitude, 180°; offset 2.11070444° | 23°0'6.8164 | `prec_offset` = 0; frozen at the 499 CE epoch, not runtime star lookup |
| 27 | `SE_SIDM_TRUE_CITRA` | `True Citra` | Runtime: Spica held exactly at 0° Libra (180° true ecliptic longitude) | 23°50'10.1227 | Runtime star-lookup mode (star is built into the library; no `sefstars.txt` needed); all sid bits stripped |
| 28 | `SE_SIDM_TRUE_REVATI` | `True Revati` | Runtime: zeta Piscium held exactly at 29°50' Pisces (359°50') | 20°2'28.6611 | Runtime star-lookup mode (star is built into the library; no `sefstars.txt` needed); all sid bits stripped; broken in SE 2.00–2.04 |
| 29 | `SE_SIDM_TRUE_PUSHYA` | `True Pushya (PVRN Rao)` | Runtime: delta Cancri held exactly at 16° Cancer (106°); P.V.R. Narasimha Rao (author of Jagannatha Hora) | 22°43'23.5983 | Runtime star-lookup mode (star is built into the library; no `sefstars.txt` needed); all sid bits stripped |
| 30 | `SE_SIDM_GALCENT_RGILBRAND` | `Galactic Center (Gil Brand)` | Runtime: Galactic Center at the golden section between 0° Scorpio and 0° Aquarius (244.377°); Rafael Gil Brand | 22°27'54.8459 | Runtime star-lookup mode (star is built into the library; no `sefstars.txt` needed); all sid bits stripped |
| 31 | `SE_SIDM_GALEQU_IAU1958` | `Galactic Equator (IAU1958)` | Runtime: galactic/ecliptic intersection node using the IAU 1958 galactic pole | 30°1'9.4895 | Runtime star-lookup mode (star is built into the library; no `sefstars.txt` needed); all sid bits stripped; Astrodienst calls the IAU 1958 pole outdated |
| 32 | `SE_SIDM_GALEQU_TRUE` | `Galactic Equator` | Runtime: galactic/ecliptic intersection node using the Liu/Zhu/Zhang (2010) galactic pole, *A&A* Oct. 2010 | 30°4'20.0004 | Runtime star-lookup mode (star is built into the library; no `sefstars.txt` needed); all sid bits stripped |
| 33 | `SE_SIDM_GALEQU_MULA` | `Galactic Equator mid-Mula` | Runtime: galactic/ecliptic intersection node placed at the middle of lunar mansion Mula; Ernst Wilhelm, 2004 | 23°24'20.0004 | Runtime star-lookup mode (star is built into the library; no `sefstars.txt` needed); all sid bits stripped; originally built on a Koch node computation with a 2" error, now on the 2010 pole |
| 34 | `SE_SIDM_GALALIGN_MARDYKS` | `Skydram (Mardyks)` | Epoch autumn equinox 1998 (JD 2451079.734892, TT); Raymond Mardyks — autumn equinox aligned with the Galactic Equator/Pole, exactly 30° | 30°0'50.1265 | `prec_offset` = 0; `swe_set_sid_mode` forces `SE_SIDBIT_ECL_T0` — despite the galactic name, **not** one of the twelve runtime modes; fixed epoch+offset |
| 35 | `SE_SIDM_TRUE_MULA` | `True Mula (Chandra Hari)` | Runtime: lambda Scorpii held exactly at 0° Sagittarius (240°); K. Chandra Hari | 24°34'33.9710 | Runtime star-lookup mode (star is built into the library; no `sefstars.txt` needed); all sid bits stripped; Astrodienst rebuts Chandra Hari's "no proper motion" claim |
| 36 | `SE_SIDM_GALCENT_MULA_WILHELM` | `Dhruva/Gal.Center/Mula (Wilhelm)` | Runtime: Galactic Center projected through the celestial north pole (*dhruva*) to the middle of Mula (246°40'); Ernst Wilhelm, 2004 | 20°2'7.3079 | Runtime star-lookup mode (star is built into the library; no `sefstars.txt` needed); all sid bits stripped |
| 37 | `SE_SIDM_ARYABHATA_522` | `Aryabhata 522` | Epoch Kali 3623 = 522 CE, UT, Ujjain (75.7684565° E) — Kali midnight, Suryasiddhanta year length; offset 0 | 20°34'19.1177 | `prec_offset` = 0; `sweph.h:546`'s trailing comment mislabels this row `// 36:` (upstream typo, do not propagate) |
| 38 | `SE_SIDM_BABYL_BRITTON` | `Babylonian/Britton` | Epoch 1 Jan 1 BCE (year 0, Julian calendar), UT; John P. Britton, *Arch. Hist. Exact Sci.* 64 (2010) 617–663, p. 630; offset −3.2° | 24°36'42.7786 | `prec_offset` = −1 |
| 39 | `SE_SIDM_TRUE_SHEORAN` | `"Vedic"/Sheoran` | Runtime; Sunil Sheoran, *The Science of Time and Timeline of World History* (2017) | 25°13'50.0863 | Runtime star-lookup mode (star is built into the library; no `sefstars.txt` needed); all sid bits stripped; Astrodienst publishes a Sanskrit-philological rebuttal in the same documentation section |
| 40 | `SE_SIDM_GALCENT_COCHRANE` | `Cochrane (Gal.Center = 0 Cap)` | Runtime: Galactic Center fixed at 0° Capricorn (270°); David Cochrane, 2017 | 356°50'31.8335 | Runtime star-lookup mode (star is built into the library; no `sefstars.txt` needed); all sid bits stripped; **negative ayanamsha** — see prose |
| 41 | `SE_SIDM_GALEQU_FIORENZA` | `Galactic Equator (Fiorenza)` | Epoch 1 Jan 2000, 00:00 UT (JD 2451544.5); Nick Anthony Fiorenza, *The Star Chart* (2001) — exactly 25° at that epoch | 24°59'46.1373 | `prec_offset` = 0; not a runtime mode despite the "Galactic Equator" name; Astrodienst notes it sits over 5° from Mardyks (mode 34) |
| 42 | `SE_SIDM_VALENS_MOON` | `Vettius Valens` | Epoch 1 Jan 150 CE (Julian calendar), UT; James H. Holden, *AFA Journal of Research* 7:2 (1995) p. 12, derived from Valens' recorded lunar positions; offset −2.9422° | 22°47'30.2621 | `prec_offset` = −1; "MOON" in the constant name reflects the lunar-position derivation |
| 43 | `SE_SIDM_LAHIRI_1940` | `Lahiri 1940` | Epoch J1900 (TT); Lahiri's own earlier value from *Panchanga Darpan* (1940): 22°26'45".50 + 50".25748T + 0".00011115T²; offset 22.44597222° | 23°50'18.4322 | `prec_offset` = Newcomb |
| 44 | `SE_SIDM_LAHIRI_VP285` | `Lahiri VP285` | Epoch ~285 CE, TT (JD 1825235.2458513028) — mean Sun at 0° in 285 CE per Simon et al. (1994), corrected for Vondrák precession; Lahiri, *Tables of the Sun* (1972) p. IX; offset 0 | 23°51'34.6009 | `prec_offset` = 0; "VP285" = vernal point of the year 285, not a page number; Lahiri later abandoned this for the ICRC value (mode 46) |
| 45 | `SE_SIDM_KRISHNAMURTI_VP291` | `Krishnamurti-Senthilathiban` | Epoch mean equinox of 291 CE, TT (JD 1827424.752255678); D. Senthilathiban, *Study of KP Ayanamsa with Modern Precession Theories* (2019); offset 0 | 23°46'35.3824 | `prec_offset` = 0 (the source's Newcomb-offset variant is present but commented out at `sweph.h:581`); "VP291" = vernal point of 291 CE — see naming-trap warning in prose |
| 46 | `SE_SIDM_LAHIRI_ICRC` | `Lahiri ICRC` | Epoch 21 Mar 1956, 00:00 TT (same epoch as mode 1); the original 1956 Calendar Reform Committee decree, 23.24535793°, before the 0.658" IAE-1985 revision, with Woolard-1953 nutation subtracted | 23°51'10.5089 | `prec_offset` = Newcomb; `sweph.h:593`'s comment refers to this mode as "(45)" (upstream typo — the correct index is 46; do not propagate) |

### The five families

Astrodienst's §2.8.10 concludes that "there are basically five definitions, not counting the
manifold variations", and names specific exemplars for the first three. Reproduced with the modes
Astrodienst itself names — **this is not an exhaustive assignment of all 47 modes, and it should
not be turned into one.** Most modes are the "manifold variations" the passage sets aside.

1. **The Babylonian zodiac**, with Spica at 29 Virgo or Aldebaran at 15 Taurus — named:
   Fagan/Bradley (0), "refined with Aldebaran at 15 Tau" (14), P. Huber (12), J.P. Britton (38).
2. **The Greek-Hindu-Arabic zodiac**, zero point 10′–20′ east of zeta Piscium — named:
   Hipparchus (15), Ushâshashî (4), Sassanian (16), True Revati (28).
3. **The Hindu astrological zodiac**, Spica at 0 Libra — named: Lahiri (1).
4. **Ayanamshas based on the Kaliyuga year 3600, or on the 23rd year of Aryabhata's life** — no
   exemplars named; the Suryasiddhanta/Aryabhata epoch modes (21–26, 37) belong here.
5. **Galactic ayanamshas**, based on the galactic centre or the galactic nodes (the intersections
   of the galactic equator with the ecliptic) — no exemplars named; modes 17, 30–33, 36 and 40 are
   computed at call time from a galactic anchor. Modes 34 and 41 derive their constants from a
   galactic argument but are stored as plain epoch-plus-offset entries, so they are not runtime
   galactic modes.

Astrodienst adds that family 1 "is historically the oldest one, but we are not sure about its
precise astronomical definition. It could have been Aldebaran at 15 Taurus and Antares at 15
Scorpio."

Two groupings worth noting because they cut across the above: Fagan/Bradley (0) is **Babylonian**,
not Greek-Hindu-Arabic, and True Revati (28) is **Greek-Hindu-Arabic**, not Hindu astrological —
both easy to misfile from the names alone.

Plus a standing exception: the technical reference frames J2000/J1900/B1950 (modes 18–20), which
are not astrological ayanamshas at all but plain precession epochs with a zero offset.

### The twelve runtime star-lookup modes

Modes **17, 27, 28, 29, 30, 31, 32, 33, 35, 36, 39, 40** have all-zero `{t0, ayan_t0, t0_is_UT,
prec_offset}` rows in `sweph.h` and are computed at call time from `swe_fixstar()` (or, for the
galactic-equator modes, from pseudo-star records `,GP1958` and `,GPol` in the same built-in table). `swe_set_sid_mode()` strips all sid
bits for these twelve.

**They do not require `sefstars.txt`.** This contradicts a widely repeated claim and is worth
stating plainly. `get_builtin_star()` (`sweph.c:6750`) carries hardcoded records for exactly the
objects these ayanamshas need — Spica, zePsc (Revati), deCnc (Pushya), laSco (Mula), SgrA\*
(galactic centre), GP1958 and GPol (galactic poles) — under the comment *"some stars are built-in,
because they are required for Hindu sidereal ephemerides"*. Measured: with the ephemeris path set
to a directory that does not exist and no star file anywhere on the search path, all twelve modes
return values **bit-identical** to a run with the real `ephe/` directory containing `sefstars.txt`
(delta 0.00e+00 for every one, at J2000 under `SEFLG_MOSEPH`).

Two branches inside `get_builtin_star` are unreachable: the `,deCnc` test annotated
`SE_SIDM_TRUE_SHEORAN` is preceded by an identical `,deCnc` test for `TRUE_PUSHYA`, and the `,GPol`
test annotated `SE_SIDM_GALEQU_MULA` is preceded by an identical one for `GALEQU_TRUE`. Both are
harmless — the shadowed record is byte-identical to the one that matches — but they are dead code.

What these modes do still want is an **ephemeris** for the Earth's position, since the star's
apparent place includes aberration. Moshier (`SEFLG_MOSEPH`) satisfies that with no files at all.
If `swe_set_ephe_path()`/`swe_set_jplfile()` was never called, `swi_get_ayanamsa_ex`
(`sweph.c:3029-3046`) writes this into `serr`:

> "Please call swe_set_ephe_path() or swe_set_jplfile() before calling swe_get_ayanamsa_ex()"

Three properties of that guard matter, and the third makes the other two academic. It is a
**warning, not a failure** — there is no early return, so a value is still produced. It fires only
when `serr` is non-`NULL`, so the legacy `swe_get_ayanamsa`/`_ut`, which pass `NULL`, could never
surface it. And it is gated on `swi_init_swed_if_start() == 1`, true only on the very first
initialisation of `swed` — which in this port means once per `SwissEph` instance, since
`CPort/Sweph.cs:161` holds `swed` as an instance field where the C has one global. But
`swe_set_sid_mode` itself calls that function (`sweph.c:2864`), and you cannot select any of the
twelve modes without calling it, so by the time the guard runs `swed` is always already initialised.
**The branch is unreachable and this string cannot be emitted through the documented path.**
`swe_close()` does not reset the flag either. What you actually get if the ephemeris path is wrong
is `swe_fixstar`'s own message, along the lines of "SwissEph file 'sepl_18.se1' not found in
PATH ... using Moshier eph.".

### The twelve `prec_offset = -1` modes

Modes **4, 7, 8, 9, 10, 11, 12, 13, 15, 16, 38, 42** carry `prec_offset = -1` in `sweph.h`. Per the
header's own commentary (`sweph.h:341-345`), `-1` means the correction "is unclear or has not been
investigated and therefore is not applied." At runtime this is clamped to 0 — the same as an
explicit `prec_offset = 0` — so there is no runtime *difference* in behaviour between a `-1` mode
and a `0` mode. The semantic point is not a computational one: it is that Astrodienst regards the
precession-model correction for these twelve as an open question, so their absolute ayanamsha
values carry an unquantified systematic error that the software does not attempt to correct.

### Mode 40 wraps past 360°

**Mode 40 (Cochrane, Galactic Center = 0° Capricorn) measures 356°50'31.83 at J2000**, the same
direction as −3°9'28.17". It is not the only mode that breaks a naive range check. Measured at
J2000, five of the 47 fall outside `0 < ayanamsha < 30`:

| mode | value at J2000 | why it surprises |
|---|---|---|
| 18 J2000 | −0°0'13.93 | negative |
| 31 Galactic Equator (IAU1958) | 30°01'09.49 | just over 30 |
| 32 Galactic Equator | 30°04'20.00 | just over 30 |
| 34 Skydram (Mardyks) | 30°00'50.13 | just over 30 |
| 40 Cochrane | 356°50'31.83 | wraps past 360 |

Modes 31, 32 and 34 are the awkward ones, because they look like ordinary values and slip past any
guard written around mode 40's wraparound. Modes 18–20 are near zero for a different reason again:
they are precession reference frames with `ayan_t0 = 0`, not astrological ayanamshas.

### A naming trap: mode 45 is not "the KP New Ayanamsa"

Mode 45, `SE_SIDM_KRISHNAMURTI_VP291` ("Krishnamurti-Senthilathiban"), is D. Senthilathiban's 2019
reconstruction using a mean equinox of 291 CE. It is **not** the "KP New Ayanamsa" (KPNA) of Prof.
K. Balachandran that Krishnamurti Paddhati practitioners commonly mean by that name — the two are
measured roughly 33″–62″ apart. Callers porting KP software that expects "the KP ayanamsha" should
not assume mode 45 is the one intended.

### Two upstream comment typos, not to propagate

- `sweph.h:546` — the trailing comment on mode 37's row reads `// 36:` instead of `// 37:`.
- `sweph.h:593` — the comment above mode 46's row calls it "(45)": "The difference between Lahiri (1)
  and Lahiri ICRC (45) amounts to 1.1\"". The row itself, at `:594`, is correctly labelled `// 46:`,
  so only the prose above it is misnumbered.

Both are comment-only defects in the upstream C — the array indices and `SE_SIDM_*` constant values
themselves are correct. They are noted here so a transliteration does not "fix" the numbering in a
way that diverges from the upstream comment text, and so anyone cross-referencing `sweph.h` by eye
is not misled.

## The three sidereal projection methods, and a fourth bit

`swe_set_sid_mode` takes modifier bits above 255 that change what "sidereal" means geometrically.
Two of them re-project coordinates: `SE_SIDBIT_ECL_T0` (256) measures positions against the ecliptic
of the ayanamsha's own reference date, and `SE_SIDBIT_SSY_PLANE` (512) against the solar system's
rotation plane. Both change ecliptic *latitude* as well as longitude, and both change what house
cusps mean, because the houses are then cut against a different plane. Astrodienst puts the
Ascendant difference at "well 1/2 degree" 2000 years from t0.

`SE_SIDBIT_ECL_DATE` (2048) is categorically different and easy to lump in with them. It changes
only the ayanamsha scalar — measuring it on the ecliptic of date rather than of t0 — and rotates no
coordinates. Latitude is untouched exactly (measured d = 0.000e+00" at J2000 and at JD 1721057.5),
and so is the plane the houses are cut against; the cusps themselves still move, by exactly the same
scalar as the planets, because `sidereal_houses_trad` subtracts the ayanamsha from every one
(`swehouse.c:555`, `:569`). At JD 1721057.5 the ayanamsha moves −1.6561" and cusp 1 moves +1.6561". The source comment calls it the "alternative
method, more consistent, programmed on 15 may 2020" (`sweph.c:3143`), against a default the same
comment admits "is not logical". Differences are sub-milliarcsecond for current decades and grow
into arcseconds for antiquity.

Two further bits exist and are not for production use. Both act only on the six modes whose
`prec_offset` names a real precession model — 0, 1, 3, 5, 43 and 46 — because
`sweph.c:2912` and `get_aya_correction` both gate on `prec_offset > 0`. For the other 41, including
every `prec_offset = -1` mode, they are no-ops.

`SE_SIDBIT_NO_PREC_OFFSET` (4096) suppresses the constant offset `get_aya_correction` would apply.
`SE_SIDBIT_PREC_ORIG` (8192) goes further and switches precession and nutation to the models the
ayanamsha's author used, by writing `swed.astro_models` (`sweph.c:2913-2925`). That write outlives
the call: every later calculation on the same `SwissEph` instance, tropical included, uses the
substituted models until `swe_close()` zeroes them again. Measured on a tropical Mars longitude at
JD 1721057.5 — Lahiri shifts it 0.86", Fagan/Bradley 22.09", Suryasiddhanta and True Citra not at
all, and after `swe_close()` the residual is 0.0000" in every case. Astrodienst's own documentation
says in bold not to use this bit in software.

## Sidereal houses

`swe_houses_ex` dispatches sidereal house computation through one of three algorithms, selected by
bits in the sidereal mode rather than by the mode number itself (`swehouse.c:269-275`):

- `SE_SIDBIT_ECL_T0` set → `sidereal_houses_ecl_t0` — houses on the ecliptic of the ayanamsha's own
  reference epoch (used automatically by modes 18-20 (J2000/J1900/B1950) and by mode 34 (Skydram/Mardyks): J2000, J1900, B1950, and
  Skydram/Mardyks, per "The 47 predefined ayanamshas" above, modes 18-20 and 34).
- `SE_SIDBIT_SSY_PLANE` set → `sidereal_houses_ssypl` — houses on the solar-system invariable
  plane.
- Neither bit set (the common case, including Lahiri, Krishnamurti, Raman, Fagan/Bradley, and every
  other mode without one of those two bits) → `sidereal_houses_trad`, the traditional path: houses
  computed on the ecliptic of date, with `armc` (the sidereal-time-derived right ascension of the
  midheaven) deliberately **not** shifted by the ayanamsha before the house geometry runs. The
  ayanamsha only enters afterward, to rotate the resulting cusps into the sidereal frame.

Whole-sign cusps are computed as the equal-house ('E'-style) ascendant and then floored to the
30° boundary at or below it — a truncation, not a round to nearest: `swehouse.c:1481` computes
`cusp[1] = ac − fmod(ac, 30)`, where `ac` is the same ascendant the equal-house case uses, and the
remaining eleven cusps step by exactly 30° from there (`swehouse.c:1482-1483`).

That is the tropical path. The sidereal one never reaches it: `sidereal_houses_trad` rewrites `'W'`
to `'E'` before computing and floors afterwards (`swehouse.c:562-563` and `:571-572`), which is what
gives sidereal whole sign its cusp speeds. "Cusp speeds are tropical" below has the measurements and
the difference between the two routes.

**The Jyotish→house-system mapping.** Four house-system letters correspond to distinct
Jyotish/Vedic conventions, and they are not interchangeable:

- **Rasi chart** (the sign-based chart most Vedic software calls simply "the chart"): hsys `'W'`,
  whole sign.
- **Sripati / bhava-madhya**: hsys `'S'`. The source comment at `swehouse.c:1410-1411` is explicit
  about the method — *"uses Porphyry sectors, but then takes middle of sectors as cusps"*
  (`swehouse.c:1410-1411`). Note what that does and does not mean: `cusp[1]` is the midpoint of the
  Porphyry *twelfth* sector, so the Ascendant falls **inside** house 1 rather than opening it, and it
  is the exact centre only when the two adjoining Porphyry sectors happen to be equal. Measured at
  J2000, 52.5N/13.4E: Ascendant 51.5850°, Sripati cusp 1 at 31.6684° and cusp 2 at 61.6684°, so the
  Ascendant sits 66.4% of the way through the house, not at its midpoint. Porphyry (`'O'`) puts
  cusp 1 on the Ascendant itself, at 51.5850°.

`'S'` and `'O'` in particular are easy to confuse because both trace back to Porphyry sector
construction, but `'S'` then re-centers the cusp at the sector's middle while `'O'` does not — they
are not interchangeable outputs for the same underlying chart, and picking the wrong one silently
produces a chart with every cusp off by roughly half a house-width from what the intended
convention expects.

## Cusp speeds are tropical (as of v2.10.3bfinal)

Astrodienst's newer speed-bearing entry points (`swe_houses_ex2`, `swe_houses_armc_ex2`) can return
a per-cusp rate of change alongside each cusp's position. Measured directly against the pinned C
build (`v2.10.3bfinal`, MSVC 19.51.36248, `/O2 /fp:precise`), **the sidereal cusp speed this library
currently returns is the tropical speed, unmodified** — not the true rate of change of the sidereal
cusp. This is upstream C behaviour, faithfully reproduced by the port; it is not something this
port introduced or can fix locally without diverging from the reference.

`swetest -b1.1.2000 -ut12:00 -house13.4,52.5,P -speed -sid1 -p0`:

| | cusp 1 position | cusp 1 speed |
|---|---|---|
| tropical | 51°35'06.0567" | 600°43'39.5410" |
| sidereal, unpatched | 27°43'54.4557" | 600°43'39.5410" — identical to tropical |

The position correctly differs from tropical by the ayanamsha (23°51'11.6" at this instant); the
speed does not differ at all. A locally built reference with `swehouse.c` recompiled against
`patch/swehouse-sidereal-ayanamsa-rate.patch` shows what a corrected speed looks like:
600°43'39.3962", a delta of 0.1448"/day (4.0e-5°/day) from the tropical value — the rate the
ayanamsha itself is changing at.

That delta is **not a constant, and not always negative.** The rate is the derivative of the
nutation-included ayanamsha, so nutation dominates its variation rather than precession: the patch's
own header tabulates 4.0212e-05°/day at J2000, 8.6572e-05 at JD 2415020, and **−1.6104e-06** at JD
2460013. On some dates a corrected sidereal cusp speed therefore comes out *higher* than the
tropical one. Do not read the single J2000 figure above as a fixed offset.

**This patch is not applied to this port's shipped build**, and neither is the whole-sign one named
further down; both are local measurements of what a fix would look like. The house patch set was sent to
Astrodienst on 17 August 2026 (groups.io topic 120714355, message #13529) and released upstream on
13 November 2026 as commit `6c989727`; this port has not synced to that commit and still returns the
unpatched behaviour. The tropical-speed behaviour above describes the port as of 2.10.3.2 (pinned
upstream `94621aba`, whose `swehouse.c` is identical to `v2.10.3bfinal`'s); upstream changed it in
`6c989727`, so it is not settled or permanent design.

**Whole sign is the prominent case**, because whole sign is the default house convention for most
Vedic charts. `swetest -b1.1.2000 -ut12:00 -house13.4,52.5,W -speed -sid1 -p0`:

| cusp | position | speed, unpatched | speed, with the whole-sign patch |
|---|---|---|---|
| 1 | 0°00'00" | 600°43'39.5410" | 0°00'00" |
| 2 | 30°00'00" | 600°43'39.5410" | 0°00'00" |
| 3 | 60°00'00" | 600°43'39.5410" | 0°00'00" |
| 4 | 90°00'00" | 600°43'39.5410" | 0°00'00" |

The zeros in that last column come from a **different** patch than the 600°43'39.3962" above:
`patch/swehouse-whole-sign-zero-speeds.patch`, not the ayanamsha-rate one. The rate patch adjusts a
moving cusp's speed by d(ayanamsha)/dt; it deliberately excludes `'W'` and `'N'`, whose cusps do not
move in the sidereal frame at all, so applying it alone leaves these twelve speeds at
600°43'39.5410". Two separate fixes, two separate results. Note also that the whole-sign patch is
mutually exclusive with `patch/swehouse-analytic-cusp-speeds.patch`, which contains the same change
among others.

Whole-sign cusps sit fixed on sidereal sign boundaries and do not move at all between one rāśi
ingress and the next, so the true derivative is exactly zero for every cusp, every day the Moon (or
whatever body defines the chart's ascendant sign) does not ingress. The unpatched build instead
reports a large, misleading nonzero number for a value that should read as static.

Where that number comes from is specific to the sidereal path, and worth stating precisely because
the tropical path behaves differently. `CalcH`'s `case 'W'` (`swehouse.c:1474-1484`) writes cusp
*positions* only and sets no speeds, so a tropical whole-sign call leaves most speeds at the
pre-switch zeros — measured, `-house13.4,52.5,W -speed` with no `-sid` gives 600°43'39.5410" on
cusps 1 and 7, 340°00'04.2645" on cusps 4 and 10, and exactly zero on the other eight. The sidereal
route never reaches that code: `sidereal_houses_trad` remaps `'W'` to `'E'` before computing
(`swehouse.c:562-563`, *"whole sign houses: treat as 'E' and fix later"*), so all twelve speeds are
filled with the equal-house rate, and the ayanamsha subtraction and 30° flooring that follow
(`:570-573`) touch positions only. That is why sidereal whole sign reports the same large number on
all twelve cusps while tropical whole sign reports a nonzero speed on only four:
600°43'39.5410" on cusps 1 and 7, 340°00'04.2645" on cusps 4 and 10.

The tropical state is itself internally inconsistent — four cusps with speeds and eight without —
which is its own argument that nothing is relying on these values today. A caller reading cusp speed
for a whole-sign sidereal chart and expecting "how fast is this cusp moving" should treat the current
answer as wrong until a fix lands upstream, not as a subtlety to work around locally.

## `swe_house_pos` — do not use it for sidereal work

`swe_house_pos` computes which house a given ecliptic longitude falls into for an already-known
`armc`/`eps`/`hsys`. The C source carries an explicit warning at `swehouse.c:2201-2213` that this
port reproduces verbatim in behaviour:

> IMPORTANT: This function should NOT be used for sidereal astrology.
> If you cannot avoid doing so, please note:
> - The input longitudes (xpin) MUST always be tropical, even if you are a siderealist.
> - Sidereal and tropical house positions are identical for most house systems, if a traditional
>   definition of the sidereal zodiac is used (sid = trop - ayanamsa).
> - The function does NOT provide correct positions for Whole Sign houses.
> - The function does NOT provide correct positions, if you use a non-traditional sidereal method
>   (where the sidereal plane is not identical to the ecliptic of date) with a house system whose
>   definition is dependent on the ecliptic, such as: equal, Porphyry, Alcabitius, Koch, Krusinski
>   (all others should work).

Two documents disagree here, and it is worth being exact about which says what, because the
published one is the more reassuring and the less correct.

**The source comment contradicts itself.** Six lines above the warning quoted here, ending at
`swehouse.c:2190-2195`, it lists the methods for which `swe_house_pos` "provides geometrically
correct house positions", and `W Whole Signs` is in that list. Then at `:2208` it says "The function
does NOT provide correct positions for Whole Sign houses."

**The published programmer's reference carries only the reassuring half.** It reproduces the
inclusive list (in a different order, and labelling `T` as Polich/Page where the source says
Topocentric), and it has neither the whole-sign exclusion nor the "should NOT be used for sidereal
astrology" block. What it says instead, of the traditional `sid = trop − ayanamsha` method, is that
"the function `swe_house_pos()` works for all house systems." A reader working from the published
documentation alone would conclude sidereal whole-sign house positions are fine.

The code settles it, and it agrees with the exclusion. `swe_house_pos`'s whole-sign branch (`case 'W'`
at `swehouse.c:2295`, adjusted at `:2303`) computes `xpin[0] - asc + fmod(asc, 30)`, which is
`xpin[0] - 30·floor(asc/30)`: it anchors to the sign boundary below the Ascendant. That `asc` is
derived internally from `armc`, `geolat` and `eps`, so it is the **tropical** Ascendant. The function
takes no ayanamsha and cannot know the sidereal one.

How often that matters is worth stating as a rate rather than a feeling, and there are two different
rates in play. The tropical and sidereal *Ascendants* fall in different signs for 24 degrees out of
every 30 at a present-day ~24° ayanamsha, about 80% of positions. But the house number depends on the
planet's sign as well, and the ayanamsha shifts both, so the two errors partly cancel: measured over
10⁶ random planet/Ascendant pairs at 23°51′, `swe_house_pos` returns a different whole-sign house
than the direct arithmetic below for **32.6%** of them. Roughly one placement in three, not four in
five.

A third list in the same comment block matters for the house-system mapping above and is easy to
miss. Between the two quoted here, at `swehouse.c:2197-2199`, the comment says a *simplified*
position — literally `distance_from_cusp / house_size` — is all that is provided for `Y`, `L`, `Q`,
`I` and **`S`**. So `swe_house_pos` never computes a true Sripati house position either, even
tropically. If you follow the Sripati mapping above, compute the house from the cusp array
`swe_houses_ex` returns rather than asking `swe_house_pos`.

For a rasi (whole-sign) chart specifically, skip `swe_house_pos` and compute the house directly
from the sidereal longitude and sidereal ascendant with integer arithmetic:

```
house = ((floor(lon / 30) - floor(asc / 30)) mod 12) + 1
```

where `lon` and `asc` are both already-sidereal ecliptic longitudes (e.g. from
`swe_calc_ut(SEFLG_SIDEREAL)` and `ascmc[0]` from a sidereal `swe_houses_ex` call), and `mod` is
taken non-negative (add 12 before the final `mod 12` if the subtraction can go negative in your
language's `%` operator).

## Nakshatras

The 27 nakshatras divide the ecliptic into equal 13°20' segments. `swe_split_deg` computes the
index directly when `SE_SPLIT_DEG_NAKSHATRA` is set in `roundflag`, returned through the same
`isgn` out-parameter (the sample above names its local `isign`) that carries the zodiac sign index under `SE_SPLIT_DEG_ZODIACAL` — the two
flags share one output slot with different meanings. Nothing enforces exclusivity: setting both
silently takes the nakshatra branch, because `swephlib.c:4024-4027` returns before the zodiacal
test at `:4043`. Nakshatra wins and no diagnostic is produced.

A few properties of that shared out-parameter matter and are easy to get wrong:

- **The nakshatra index is 0-based, 0 through 26** (Ashwini = 0, ... Revati = 26) — not the 1-based
  numbering (1 through 27) commonly used when nakshatras are listed in prose. The same `isgn`
  parameter under `SE_SPLIT_DEG_ZODIACAL` (rāśi splitting) is also 0-based, 0 through 11, so the
  indexing convention is at least internally consistent between the two modes — but a caller
  hard-coding "nakshatra 1 through 27" against this 0-based return value will be off by one.
- **A negative `ddeg` input silently bypasses the nakshatra path entirely**, and what you get back in
  that slot is a *sign flag*, not an index: `swe_split_deg` tests `if (ddeg < 0)` before the
  nakshatra branch, so a negative input takes the ordinary path and sets the out-parameter to `-1`
  (`swephlib.c:4021-4027`). Indexing a 27-element name array with it throws. Feed the function
  something already normalized to `[0, 360)` — the form `swe_calc_ut`'s longitude output and
  `ascmc[0]` are already in — rather than a raw difference that can go negative.
- **The split is only meaningful on an already-sidereal longitude.** `swe_split_deg` takes a plain
  decimal degree, not a body and not an `iflag`, so it cannot tell whether what you hand it is
  tropical or sidereal. Nakshatra boundaries are sidereal by definition: pass it a longitude that
  came from a `SEFLG_SIDEREAL` call under whichever ayanamsha you intend, or the index means
  nothing.
- **But it is not purely a formatter — it reads global sidereal state.** Despite having no sid
  parameter, `split_deg_nakshatra` consults `swed.sidd.sid_mode` and adds 3°20′ before splitting
  (`swephlib.c:3951-3953`), because Sheoran's ayanamsha (mode 39) places 0° Aries at 3°20′ Ashvini.
  So the same longitude splits into different nakshatras depending on a `swe_set_sid_mode` call made
  elsewhere in your process. Nothing in the function's own header block says so.
- **That guard is too broad, and it catches `SE_SIDM_USER`.** The test is bit inclusion rather than
  equality — `(sid_mode & 39) == 39` — and `SE_SIDM_USER` is 255, whose bit pattern is a superset of
  39's. So **a user-defined ayanamsha silently receives Sheoran's 3°20′ offset**: measured, mode 255
  returns the same shifted nakshatra as mode 39, as do 511 and 1279. If you use `SE_SIDM_USER` — the
  mode you reach for to define a custom ayanamsha — your nakshatra indices are shifted by exactly one
  pada (3°20', which is a quarter of a nakshatra) until this is fixed upstream. The workaround is to compute the index yourself:
  `nak = (int)(swe_degnorm(lon) / (40.0/3.0)); if (nak == 27) nak = 0;`.

The `if (nak == 27)` clamp mirrors the one the C carries at `swephlib.c:3970`, and that clamp exists
for two independent reasons — worth separating, because only one of them survives in the workaround
above.

The first is the divisor itself. The C uses the truncated literal `13.33333333333333`
(`swephlib.c:3944`), and 27 times that is 359.99999999999994 — which happens to be exactly the
largest double below 360. So that one value divides out to 27 with no rounding flag involved at all.
Measured through the library: `swe_split_deg` on the largest double below 360, no rounding flag,
returns nakshatra 0 — the clamp fired. Exactly one double is affected; with an exact `40.0/3.0` the
same value divides to 26.999999999999993 and none is.

The second is the rounding addend, which the C adds *before* dividing. Under
`SE_SPLIT_DEG_ROUND_SEC` a longitude of 359.9999° becomes 360.0000389° and divides out to 27 — that is
what `:3970`'s comment means by "with rounding up from 359.9999". Measured: 359.9999° returns 26 with
no rounding flag, 0 under `ROUND_SEC`, and 26 again under `ROUND_SEC | SE_SPLIT_DEG_KEEP_SIGN`, since
`KEEP_SIGN` suppresses the addend at a boundary.

In the workaround above neither applies: exact `40.0/3.0` removes the first, and there is no addend
to trigger the second. Keep the clamp anyway if anything upstream of you can hand in a value at or
above 360° — `swe_degnorm` already guards that, so treat it as belt and braces rather than
load-bearing. See `docs/known-issues.md`, "split_deg_nakshatra: SE_SIDM_USER collides with the
Sheoran ayanamsha offset, reproduced deliberately", for the collision measurement and upstream status.

**Abhijit is not the 28th nakshatra for this purpose.** Some traditions recognize Abhijit as an
additional nakshatra inserted between Uttara Ashadha and Shravana, but it functions as a *muhurta*
(auspicious-timing) overlay on the 27-fold system, not as a 28th equal division of the ecliptic —
inserting it into the 0-26 index above would break the equal 13°20' spacing `SE_SPLIT_DEG_NAKSHATRA`
actually computes. Do not add it to the index this function returns.

**Pada and navamsa share one formula.** A nakshatra's four padas (quarters) are each 3°20' wide,
which is exactly one-ninth of a zodiac sign (30° / 9 = 3°20') — the same width a navamsa division
uses. That is not a coincidence to memorize separately; it is one expression serving both purposes:

```
navamsa_sign = floor(lon / 3°20') mod 12
```

applied to a sidereal longitude gives the navamsa sign directly. The shared quantity underneath is
`floor(lon / 3°20')`, which runs 0 through 107 across the whole zodiac — it is the **global** pada
index, not the pada within its nakshatra. Take it apart deliberately:

```
p        = floor(lon / 3°20')      // 0..107, global pada index
nakshatra = p / 4                  // 0..26   (integer division)
pada      = p % 4 + 1              // 1..4    traditional numbering within the nakshatra
navamsa   = p % 12                 // 0..11   navamsa sign
```

Using `p` directly where `p % 4 + 1` is meant is an easy slip and yields nonsense like "pada 63".

The traditional hand-calculation mnemonic — count the navamsa from the sign itself for a movable
sign, from the 9th for a fixed sign, from the 5th for a dual sign — is a paper shortcut for exactly
that `p % 12`, not a separate rule. Note the resulting anchors are the four **cardinal** signs, one
per triplicity: working the formula, the first navamsa of each sign lands on Aries (Aries, Leo,
Sagittarius), Capricorn (Taurus, Virgo, Capricorn), Libra (Gemini, Libra, Aquarius) or Cancer
(Cancer, Scorpio, Pisces). There are four anchors, not three, and Leo is never one of them. If you
can evaluate `p % 12` there is no need to special-case modality at all.

## Rāśi ingresses in one call

Finding the moment the Sun crosses into a new sidereal sign (a rāśi sankranti) looks like it needs
a hand-rolled root-finder — search forward, evaluate the sidereal longitude repeatedly, bisect
toward the crossing. It does not: `swe_solcross_ut` accepts `SEFLG_SIDEREAL` and honours it,
searching for the next time the Sun's longitude in the *currently selected sidereal frame* crosses
the target value. This is not documented in Astrodienst's own `swephprg` reference, which lists only
`SEFLG_TRUEPOS`, `SEFLG_NONUT` and `SEFLG_EQUATORIAL` as the useful flags for the eight crossing
functions (chapter 4) and never mentions `SEFLG_SIDEREAL` among them — the flag reaches
`swe_calc_ut` unchanged (`swe_solcross_ut` at `sweph.c:8355` forwards it at `:8364` and `:8370`), and works, without being
called out as a feature.

Measured, searching forward from 1 January 2000 for the Sun crossing longitude 0 under
`SEFLG_MOSEPH`:

| Search | Result |
|---|---|
| Tropical (`SEFLG_MOSEPH`) | JD 2451623.816155 — 2000-03-20, 07.59 h UT decimal = 07:35 clock (the vernal equinox) |
| Sidereal, Lahiri | JD 2451647.994570 — 2000-04-13, 11.87h UT |
| Sidereal, Fagan/Bradley | JD 2451648.896381 — 2000-04-14, 09.51h UT |
| Sidereal, Raman | JD 2451646.518933 — 2000-04-12, 00.45h UT |

The Lahiri result reproduces Mesha Sankranti 2000 (13 April), while the three sidereal ayanamshas
spread across 12-14 April — matching their roughly 2° pairwise spread in ayanamsha value. Every rāśi
ingress a Jyotish calendar needs is therefore one `swe_solcross_ut` call with `SEFLG_SIDEREAL` set
(and `swe_set_sid_mode` already called with the intended ayanamsha) — no separate search loop
required.

## What is downstream arithmetic, not an ephemeris call

Most of what a Jyotish astrology program needs — vargas (divisional charts beyond the navamsa
identity above), dashas (planetary period systems), and panchanga (the five daily almanac
elements) — is arithmetic performed on top of a small number of ephemeris calls, not a separate
Swiss Ephemeris entry point. The whole stack rests on roughly five functions: `swe_set_sid_mode`,
`swe_calc_ut`, `swe_houses_ex`, `swe_rise_trans` (for sunrise/sunset-anchored timings), and
`swe_get_ayanamsa_ex_ut` (for display, per the trap section above).

Three of the five panchanga elements interact with the ayanamsha, and they do so differently:

- **Tithi and karana are both Moon − Sun angular *differences*.** Because the ayanamsha is a single
  offset subtracted equally from both bodies' tropical longitudes, it cancels exactly out of a
  difference: `(moon_sid − sun_sid) = (moon_trop − ayanamsha) − (sun_trop − ayanamsha) =
  moon_trop − sun_trop`. Tithi and karana can therefore be computed entirely from **tropical**
  longitudes — no sidereal call, and no ayanamsha choice, changes either value at all.
- **Yoga is a Moon + Sun *sum*.** A sum does not cancel the ayanamsha the way a difference does — it
  enters **doubled**: `(moon_sid + sun_sid) = (moon_trop + sun_trop) − 2×ayanamsha`. Computing yoga
  from tropical longitudes and forgetting this factor of two is a specific, easy arithmetic mistake
  to make once tithi's cancellation has established the (wrong, for this one case) expectation that
  ayanamsha does not matter for lunisolar angle arithmetic in general.

**There is no `SE_KETU` constant.** Rahu (the north lunar node, `SE_MEAN_NODE` or `SE_TRUE_NODE`
depending on which convention you choose — see "Contested conventions" below) is a real body this
library computes; Ketu (the south node) is always exactly opposite it and is derived, not looked
up: `ketu_longitude = swe_degnorm(rahu_longitude + 180)`.

**Do not assert the node's latitude is zero under `SEFLG_SIDEREAL`.** For both `SE_MEAN_NODE` and
`SE_TRUE_NODE`, the C source deliberately forces the returned ecliptic latitude (and several
matching speed/z-coordinate fields) to exactly `0.0` to suppress infinitesimal numerical noise from
coordinate conversions — but only when neither `SEFLG_SIDEREAL` nor `SEFLG_J2000` is set
(`sweph.c:885` for the mean node, `sweph.c:944` for the true node: both guards read
`if (!(iflag & SEFLG_SIDEREAL) && !(iflag & SEFLG_J2000))`). Under `SEFLG_SIDEREAL` that
zero-forcing is skipped.

What comes back is small rather than exactly zero, and sometimes negatively signed. Sampled over
200 dates 37 days apart from J2000 under `SEFLG_MOSEPH | SEFLG_SIDEREAL`, via `swe_calc_ut`:

| body | latitude not exactly 0 | sign bit set |
|---|---|---|
| `SE_MEAN_NODE` | 78 of 200 | 35 of 200 |
| `SE_TRUE_NODE` | 36 of 200 | 103 of 200 |

Both nodes do it, so this is not a true-node property. At J2000 itself the mean node happens to
return exactly `0` through `swe_calc_ut` while the true node returns `-0`, which makes a tidy
contrast; it is a coincidence of that instant, and sampling away from it dissolves the pattern.
Through `swe_calc`, in ET rather than UT, the mean node at J2000 returns
`2.4175470700678966e-15` rather than `0`.

Which operations notice is worth being exact about, because the two obvious defensive checks are
both blind to it. `lat == 0.0` is `true` for `-0.0`, so a guard written that way is unaffected. So is
`Math.Sign(lat)`, which returns `0` — it yields `-1` only when `value < 0`, and `-0.0 < 0` is
`false`.

What does notice: `double.IsNegative(lat)` returns `true`; `BitConverter.DoubleToInt64Bits(lat)` is
`0x8000000000000000`; `1.0 / lat` is negative infinity; `Math.CopySign` propagates the sign; and
formatting renders it — `(-0.0).ToString()` gives `"-0"` and `ToString("F6")` gives `"-0.000000"`.
So the realistic failure is a sidereal chart that serialises a node latitude as `-0`, or a
round-trip through text that produces a different string than the tropical run, rather than a
comparison taking the wrong branch.

One target-framework caveat, since this package ships three: the formatting behaviour above is
.NET Core 3.0 and later. On `net48` — the framework `Tests/NetStandard20Smoke.Tests` exercises —
`(-0.0).ToString()` returns `"0"`, so the serialisation hazard does not appear there.

This repository's characterization baseline sees a narrower slice of the same effect: see
`docs/known-issues.md`, "Negative-zero (`-0`) fields under SIDEREAL: TRUE node, not mean node",
which found 18 such fields, all under `SEFLG_SIDEREAL` and all `SE_TRUE_NODE`. That reflects the
dates and flag combinations that grid sweeps, not immunity in the mean node.

## What this library cannot do

Swiss Ephemeris computes positions from modern gravitational theory (analytic series or JPL/Swiss
ephemeris files), and the seven epoch-anchored modes named after classical
Indian astronomical texts describe an ayanamsha **origin**, not an alternative planetary theory.
Modes 21-26 (Suryasiddhanta and Aryabhata, and their mean-Sun/Revati/Citra variants) and mode 37
(Aryabhata 522) each fix a zero point derived from a stated epoch in that text, computed by
Astrodienst's own reconstruction of what the text specifies (see "The 47 predefined ayanamshas" above, the rows for
the derivation of each). None of them replace the Sun, Moon or planet positions themselves with the
text's own planetary model.

Concretely: this library cannot produce a genuine *vakya* panchanga (computed from the Vakya
tradition's own interpolation tables) or a genuine Surya-Siddhanta panchanga (computed from that
text's own planetary equations throughout, not just its stated ayanamsha epoch). Combining mode 21
with this library's modern Moon and Sun gives a chart that uses the Suryasiddhanta's zero point but
a twenty-first-century gravitational Moon — a hybrid that is neither a drik (modern-ephemeris)
panchanga nor a vakya/Surya-Siddhanta one, and should not be presented to a user as either. State
this plainly to anyone building on these modes rather than letting mode 21 read as "the
Suryasiddhanta option."

## Contested conventions

Two choices in the code are genuinely contested among practitioners, and this file takes no side on
either — the library exposes both options and the choice belongs to the caller or the caller's own
users.

**Mean node vs. true node** (`SE_MEAN_NODE` vs. `SE_TRUE_NODE`). The mean node is, by construction,
strictly retrograde — it never reverses direction — which matches the classical description of Rahu
and Ketu as always-retrograde points. The true node is the actual, physical point where the Moon's
orbital plane crosses the ecliptic at each instant, including its real short-term oscillation, and
is the point actually involved in eclipse geometry.

Measured on the pinned C reference with `swe_calc_ut` over 10,950 consecutive daily samples starting
at JD 2451545.0, `iflag = SEFLG_MOSEPH | SEFLG_SPEED`, the two differ by up to
**1.9315°** (115.9 arcminutes), and the true node moves **direct on 25.9% of days** — 2,834 of
10,950. Both numbers bear directly on the dispute. A pada is 3°20′ wide, so a spread approaching 2°
is easily enough to move a body across a pada boundary, and so to change a navamsa placement or
a nakshatra-based lookup that reads Rahu or Ketu. It does not touch the Vimshottari dasha lord,
which is fixed by the Moon's nakshatra and changes only at 13°20' boundaries.
The direct-motion figure goes to the substance of the classical objection: a point that reverses
to direct for a quarter of its days does not match the description of Rahu and Ketu as
permanently retrograde.
`SEFLG_SPEED` is not optional in that recipe: without it `xx[3]` is never populated, every sample
reads exactly zero, and the direct-motion figure comes out as 0%. The exact start JD and sample
count matter too — shifting the grid by half a day moves both the maximum and the direct-day count by
small amounts, so quote the recipe alongside the numbers if you re-derive them.

Neither convention is "the" correct Rahu/Ketu, and this file does not adjudicate it. Expose the
choice as a user-facing setting rather than hardcoding one, and record which was used alongside any
stored chart.

**Vimshottari dasha year length.** The Vimshottari dasha system's 120-year total cycle is divided
among the nine grahas by well-established ratios, but the *year* those ratios are multiplied by is
not universally the same across software and tradition — a mean solar/tropical year, a sidereal
year, and other fixed-length conventions are all in use, and they diverge by enough to shift a
dasha boundary by days over a multi-year period. As with the node convention, this is reported here
as a contested choice with no side taken; expose it rather than picking silently on a caller's
behalf.
