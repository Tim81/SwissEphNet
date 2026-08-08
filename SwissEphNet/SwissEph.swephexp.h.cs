/*
   This is a port of the Swiss Ephemeris Free Edition, Version 2.00.00
   of Astrodienst AG, Switzerland from the original C Code to .Net. For
   copyright see the original copyright notices below and additional
   copyright notes in the file named LICENSE, or - if this file is not
   available - the copyright notes at http://www.astro.ch/swisseph/ and
   following.
   
   For any questions or comments regarding this port, you should
   ONLY contact me and not Astrodienst, as the Astrodienst AG is not involved
   in this port in any way.

   Yanos : ygrenier@ygrenier.com
*/

/************************************************************
  $Header: /home/dieter/sweph/RCS/swephexp.h,v 1.75 2009/04/08 07:19:08 dieter Exp $
  SWISSEPH: exported definitions and constants 

  This file represents the standard application interface (API)
  to the Swiss Ephemeris.

  A C programmer needs only to include this file, and link his code
  with the SwissEph library.

  The function calls are documented in the Programmer's documentation,
  which is online in HTML format.

  Structure of this file:
    Public API definitions
    Internal developer's definitions
    Public API functions.

  Authors: Dieter Koch and Alois Treindl, Astrodienst Zurich

************************************************************/
/* Copyright (C) 1997 - 2021 Astrodienst AG, Switzerland.  All rights reserved.

  License conditions
  ------------------

  This file is part of Swiss Ephemeris.

  Swiss Ephemeris is distributed with NO WARRANTY OF ANY KIND.  No author
  or distributor accepts any responsibility for the consequences of using it,
  or for whether it serves any particular purpose or works at all, unless he
  or she says so in writing.  

  Swiss Ephemeris is made available by its authors under a dual licensing
  system. The software developer, who uses any part of Swiss Ephemeris
  in his or her software, must choose between one of the two license models,
  which are
  a) GNU Affero General Public License (AGPL)
  b) Swiss Ephemeris Professional License

  The choice must be made before the software developer distributes software
  containing parts of Swiss Ephemeris to others, and before any public
  service using the developed software is activated.

  If the developer choses the AGPL software license, he or she must fulfill
  the conditions of that license, which includes the obligation to place his
  or her whole software project under the AGPL or a compatible license.
  See https://www.gnu.org/licenses/agpl-3.0.html

  If the developer choses the Swiss Ephemeris Professional license,
  he must follow the instructions as found in http://www.astro.com/swisseph/ 
  and purchase the Swiss Ephemeris Professional Edition from Astrodienst
  and sign the corresponding license contract.

  The License grants you the right to use, copy, modify and redistribute
  Swiss Ephemeris, but only under certain conditions described in the License.
  Among other things, the License requires that the copyright notices and
  this notice be preserved on all copies.

  Authors of the Swiss Ephemeris: Dieter Koch and Alois Treindl

  The authors of Swiss Ephemeris have no control or influence over any of
  the derived works, i.e. over software or services created by other
  programmers which use Swiss Ephemeris functions.

  The names of the authors or of the copyright holder (Astrodienst) must not
  be used for promoting any software, product or service which uses or contains
  the Swiss Ephemeris. This copyright notice is the ONLY place where the
  names of the authors can legally appear, except in cases where they have
  given special permission in writing.

  The trademarks 'Swiss Ephemeris' and 'Swiss Ephemeris inside' may be used
  for promoting such software, products or services.
*/
namespace SwissEphNet
{
    using SwissEphNet.CPort;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;

    /// <summary>
    /// SwissEph export : Public part
    /// </summary>
    partial class SwissEph
    {

        /***********************************************************
         * definitions for use also by non-C programmers
         ***********************************************************/

        public const double SE_AUNIT_TO_KM = (149597870.700);
        public const double SE_AUNIT_TO_LIGHTYEAR = (1.0 / 63241.07708427);
        public const double SE_AUNIT_TO_PARSEC = (1.0 / 206264.8062471);

        /* values for gregflag in swe_julday() and swe_revjul() */
        /// <summary>Julian calendar, for the gregflag parameter of <see cref="swe_julday"/> and <see cref="swe_revjul"/>.</summary>
        public const int SE_JUL_CAL = 0;
        /// <summary>Gregorian calendar, for the gregflag parameter of <see cref="swe_julday"/> and <see cref="swe_revjul"/>.</summary>
        public const int SE_GREG_CAL = 1;

        /*
         * planet numbers for the ipl parameter in swe_calc()
         */
        /// <summary>Sentinel ipl value for <see cref="swe_calc"/>: requests obliquity and nutation instead of a body position.</summary>
        public const int SE_ECL_NUT = -1;

        /// <summary>Sun, ipl value for <see cref="swe_calc"/>.</summary>
        public const int SE_SUN = 0;
        /// <summary>Moon, ipl value for <see cref="swe_calc"/>.</summary>
        public const int SE_MOON = 1;
        /// <summary>Mercury, ipl value for <see cref="swe_calc"/>.</summary>
        public const int SE_MERCURY = 2;
        /// <summary>Venus, ipl value for <see cref="swe_calc"/>.</summary>
        public const int SE_VENUS = 3;
        /// <summary>Mars, ipl value for <see cref="swe_calc"/>.</summary>
        public const int SE_MARS = 4;
        /// <summary>Jupiter, ipl value for <see cref="swe_calc"/>.</summary>
        public const int SE_JUPITER = 5;
        /// <summary>Saturn, ipl value for <see cref="swe_calc"/>.</summary>
        public const int SE_SATURN = 6;
        /// <summary>Uranus, ipl value for <see cref="swe_calc"/>.</summary>
        public const int SE_URANUS = 7;
        /// <summary>Neptune, ipl value for <see cref="swe_calc"/>.</summary>
        public const int SE_NEPTUNE = 8;
        /// <summary>Pluto, ipl value for <see cref="swe_calc"/>.</summary>
        public const int SE_PLUTO = 9;
        /// <summary>Mean lunar node, ipl value for <see cref="swe_calc"/> -- the mean (smoothed) ascending node of the Moon's orbit.</summary>
        public const int SE_MEAN_NODE = 10;
        /// <summary>True (osculating) lunar node, ipl value for <see cref="swe_calc"/> -- the instantaneous, unsmoothed ascending node of the Moon's orbit.</summary>
        public const int SE_TRUE_NODE = 11;
        /// <summary>Mean lunar apogee ("Lilith"/Black Moon), ipl value for <see cref="swe_calc"/> -- computed from mean lunar orbital elements.</summary>
        public const int SE_MEAN_APOG = 12;
        /// <summary>Osculating lunar apogee, ipl value for <see cref="swe_calc"/> -- computed from the Moon's instantaneous (osculating) orbital ellipse.</summary>
        public const int SE_OSCU_APOG = 13;
        /// <summary>Earth, ipl value for <see cref="swe_calc"/> -- only meaningful for heliocentric or barycentric calculations, not geocentric ones.</summary>
        public const int SE_EARTH = 14;
        /// <summary>Chiron, ipl value for <see cref="swe_calc"/>.</summary>
        public const int SE_CHIRON = 15;
        /// <summary>Pholus, ipl value for <see cref="swe_calc"/>.</summary>
        public const int SE_PHOLUS = 16;
        /// <summary>Ceres, ipl value for <see cref="swe_calc"/>.</summary>
        public const int SE_CERES = 17;
        /// <summary>Pallas, ipl value for <see cref="swe_calc"/>.</summary>
        public const int SE_PALLAS = 18;
        /// <summary>Juno, ipl value for <see cref="swe_calc"/>.</summary>
        public const int SE_JUNO = 19;
        /// <summary>Vesta, ipl value for <see cref="swe_calc"/>.</summary>
        public const int SE_VESTA = 20;
        /// <summary>Interpolated lunar apogee, ipl value for <see cref="swe_calc"/> -- an apogee interpolated between mean and osculating elements for smoother motion.</summary>
        public const int SE_INTP_APOG = 21;
        /// <summary>Interpolated lunar perigee, ipl value for <see cref="swe_calc"/> -- the perigee counterpart of <see cref="SE_INTP_APOG"/>.</summary>
        public const int SE_INTP_PERG = 22;

        /// <summary>Number of natural body constants defined above (<see cref="SE_SUN"/> through <see cref="SE_INTP_PERG"/>); these occupy ipl values 0 through SE_NPLANETS - 1.</summary>
        public const int SE_NPLANETS = 23;

        /// <summary>Offset used to form the ipl value of a planetary moon or a planet's center of
        /// body (COB): ipl = SE_PLMOON_OFFSET + planet number * 100 + moon number, e.g. 9501 is
        /// Io/Jupiter (planet number 5, moon 1) and 9599 is Jupiter's COB (moon number 99). See
        /// https://www.astro.com/ftp/swisseph/ephe/sat/plmolist.txt for the moon numbers.</summary>
        public const int SE_PLMOON_OFFSET = 9000;
        /// <summary>Offset added to a minor planet's catalog number to form its ipl value, e.g. ipl = SE_AST_OFFSET + asteroid catalog number.</summary>
        public const int SE_AST_OFFSET = 10000;
        /// <summary>Asteroid 20000 Varuna, expressed as <see cref="SE_AST_OFFSET"/> plus its catalog number.</summary>
        public const int SE_VARUNA = (SE_AST_OFFSET + 20000);

        /// <summary>First ipl value used for the fictitious/Uranian bodies (<see cref="SE_CUPIDO"/> and following) and for user-defined fictitious bodies configured in seorbel.txt.</summary>
        public const int SE_FICT_OFFSET = 40;
        /// <summary>One less than <see cref="SE_FICT_OFFSET"/>; used where fictitious-body indexing is counted from this base instead.</summary>
        public const int SE_FICT_OFFSET_1 = 39;
        /// <summary>Highest ipl value reserved for fictitious bodies.</summary>
        public const int SE_FICT_MAX = 999;
        /// <summary>Number of orbital elements stored per fictitious-body entry.</summary>
        public const int SE_NFICT_ELEM = 15;

        /// <summary>Offset added to a comet's catalog number to form its ipl value, e.g. ipl = SE_COMET_OFFSET + comet catalog number.</summary>
        public const int SE_COMET_OFFSET = 1000;

        /// <summary>Total count of natural and fictitious body points: <see cref="SE_NPLANETS"/> plus <see cref="SE_NFICT_ELEM"/>.</summary>
        public const int SE_NALL_NAT_POINTS = (SE_NPLANETS + SE_NFICT_ELEM);

        /* Hamburger or Uranian "planets" */
        /// <summary>Uranian/Hamburger School hypothetical planet Cupido.</summary>
        public const int SE_CUPIDO = 40;
        /// <summary>Uranian/Hamburger School hypothetical planet Hades.</summary>
        public const int SE_HADES = 41;
        /// <summary>Uranian/Hamburger School hypothetical planet Zeus.</summary>
        public const int SE_ZEUS = 42;
        /// <summary>Uranian/Hamburger School hypothetical planet Kronos.</summary>
        public const int SE_KRONOS = 43;
        /// <summary>Uranian/Hamburger School hypothetical planet Apollon.</summary>
        public const int SE_APOLLON = 44;
        /// <summary>Uranian/Hamburger School hypothetical planet Admetos.</summary>
        public const int SE_ADMETOS = 45;
        /// <summary>Uranian/Hamburger School hypothetical planet Vulkanus.</summary>
        public const int SE_VULKANUS = 46;
        /// <summary>Uranian/Hamburger School hypothetical planet Poseidon.</summary>
        public const int SE_POSEIDON = 47;
        /* other fictitious bodies */
        /// <summary>Fictitious hypothetical body Isis.</summary>
        public const int SE_ISIS = 48;
        /// <summary>Fictitious hypothetical body Nibiru.</summary>
        public const int SE_NIBIRU = 49;
        /// <summary>Fictitious hypothetical trans-Neptunian planet per Harrington.</summary>
        public const int SE_HARRINGTON = 50;
        /// <summary>Neptune as predicted (pre-discovery) by Le Verrier, a fictitious body using his hypothetical orbital elements.</summary>
        public const int SE_NEPTUNE_LEVERRIER = 51;
        /// <summary>Neptune as predicted (pre-discovery) by Adams, a fictitious body using his hypothetical orbital elements.</summary>
        public const int SE_NEPTUNE_ADAMS = 52;
        /// <summary>Pluto as predicted by Lowell, a fictitious body using his hypothetical orbital elements.</summary>
        public const int SE_PLUTO_LOWELL = 53;
        /// <summary>Pluto as predicted by Pickering, a fictitious body using his hypothetical orbital elements.</summary>
        public const int SE_PLUTO_PICKERING = 54;
        /// <summary>Fictitious hypothetical intra-Mercurial planet Vulcan.</summary>
        public const int SE_VULCAN = 55;
        /// <summary>Fictitious hypothetical body "White Moon" (Selena).</summary>
        public const int SE_WHITE_MOON = 56;
        /// <summary>Fictitious hypothetical body Proserpina.</summary>
        public const int SE_PROSERPINA = 57;
        /// <summary>Fictitious hypothetical body per Waldemath (his "second" or "dark" Moon).</summary>
        public const int SE_WALDEMATH = 58;

        /// <summary>Sentinel ipl value meaning the call addresses a fixed star (by name or catalog entry) rather than a numbered body.</summary>
        public const int SE_FIXSTAR = -10;

        /// <summary>Index of the Ascendant in the ascmc[] array returned by <see cref="swe_houses(double, double, double, char, double[], double[])"/> and related functions.</summary>
        public const int SE_ASC = 0;
        /// <summary>Index of the Midheaven (MC) in the ascmc[] array returned by <see cref="swe_houses(double, double, double, char, double[], double[])"/> and related functions.</summary>
        public const int SE_MC = 1;
        /// <summary>Index of the ARMC (sidereal time expressed as right ascension of the MC) in the ascmc[] array returned by <see cref="swe_houses(double, double, double, char, double[], double[])"/> and related functions.</summary>
        public const int SE_ARMC = 2;
        /// <summary>Index of the Vertex in the ascmc[] array returned by <see cref="swe_houses(double, double, double, char, double[], double[])"/> and related functions.</summary>
        public const int SE_VERTEX = 3;
        /// <summary>Index of the "equatorial ascendant" in the ascmc[] array returned by <see cref="swe_houses(double, double, double, char, double[], double[])"/> and related functions.</summary>
        public const int SE_EQUASC = 4;	/* "equatorial ascendant" */
        /// <summary>Index of the "co-ascendant" (W. Koch method) in the ascmc[] array returned by <see cref="swe_houses(double, double, double, char, double[], double[])"/> and related functions.</summary>
        public const int SE_COASC1 = 5;	/* "co-ascendant" (W. Koch) */
        /// <summary>Index of the "co-ascendant" (M. Munkasey method) in the ascmc[] array returned by <see cref="swe_houses(double, double, double, char, double[], double[])"/> and related functions.</summary>
        public const int SE_COASC2 = 6;	/* "co-ascendant" (M. Munkasey) */
        /// <summary>Index of the "polar ascendant" (M. Munkasey method) in the ascmc[] array returned by <see cref="swe_houses(double, double, double, char, double[], double[])"/> and related functions.</summary>
        public const int SE_POLASC = 7;	/* "polar ascendant" (M. Munkasey) */
        /// <summary>Count of ascmc[] slots actually filled by most house systems, in the array returned by <see cref="swe_houses(double, double, double, char, double[], double[])"/> and related functions.</summary>
        public const int SE_NASCMC = 8;

        /*
         * flag bits for parameter iflag in function swe_calc()
         * The flag bits are defined in such a way that iflag = 0 delivers what one
         * usually wants:
         *    - the default ephemeris (SWISS EPHEMERIS) is used,
         *    - apparent geocentric positions referring to the true equinox of date
         *      are returned.
         * If not only coordinates, but also speed values are required, use 
         * flag = SEFLG_SPEED.
         *
         * The 'L' behind the number indicates that 32-bit integers (Long) are used.
         */
        /// <summary>Use the JPL ephemeris backend, for the iflag parameter of <see cref="swe_calc"/>.</summary>
        public const int SEFLG_JPLEPH = 1;       /* use JPL ephemeris */
        /// <summary>Use the Swiss Ephemeris (SWISSEPH) backend, for the iflag parameter of <see cref="swe_calc"/> -- the recommended high-precision backend.</summary>
        public const int SEFLG_SWIEPH = 2;       /* use SWISSEPH ephemeris */
        /// <summary>Use the Moshier analytical ephemeris, for the iflag parameter of <see cref="swe_calc"/> -- needs no data files but is lower precision and range-limited.</summary>
        public const int SEFLG_MOSEPH = 4;       /* use Moshier ephemeris */

        /// <summary>Mask combining the three ephemeris-backend selection bits: <see cref="SEFLG_JPLEPH"/>, <see cref="SEFLG_SWIEPH"/> and <see cref="SEFLG_MOSEPH"/>.</summary>
        public const int SEFLG_EPHMASK = (SEFLG_JPLEPH | SEFLG_SWIEPH | SEFLG_MOSEPH);
        /// <summary>Mask combining the coordinate-system bits: <see cref="SEFLG_EQUATORIAL"/>, <see cref="SEFLG_XYZ"/> and <see cref="SEFLG_RADIANS"/>.</summary>
        public const int SEFLG_COORDSYS = (SEFLG_EQUATORIAL | SEFLG_XYZ | SEFLG_RADIANS);

        /// <summary>Return a heliocentric position, for the iflag parameter of <see cref="swe_calc"/>.</summary>
        public const int SEFLG_HELCTR = 8;     /* heliocentric position */
        /// <summary>Return the true/geometric position rather than the apparent position, for the iflag parameter of <see cref="swe_calc"/>.</summary>
        public const int SEFLG_TRUEPOS = 16;     /* true/geometric position, not apparent position */
        /// <summary>Apply no precession, i.e. return the J2000 equinox instead of the equinox of date, for the iflag parameter of <see cref="swe_calc"/>.</summary>
        public const int SEFLG_J2000 = 32;     /* no precession, i.e. give J2000 equinox */
        /// <summary>Apply no nutation, i.e. return the mean equinox of date, for the iflag parameter of <see cref="swe_calc"/>.</summary>
        public const int SEFLG_NONUT = 64;     /* no nutation, i.e. mean equinox of date */
        /// <summary>Compute speed by differencing 3 positions, for the iflag parameter of <see cref="swe_calc"/>. Not recommended -- <see cref="SEFLG_SPEED"/> is faster and more precise.</summary>
        public const int SEFLG_SPEED3 = 128;    /* speed from 3 positions (do not use it,
                                                 * SEFLG_SPEED is faster and more precise.) */
        /// <summary>Compute high-precision speed values in the same call, for the iflag parameter of <see cref="swe_calc"/>.</summary>
        public const int SEFLG_SPEED = 256;    /* high precision speed  */
        /// <summary>Turn off gravitational light deflection, for the iflag parameter of <see cref="swe_calc"/>.</summary>
        public const int SEFLG_NOGDEFL = 512;    /* turn off gravitational deflection */
        /// <summary>Turn off annual aberration of light, for the iflag parameter of <see cref="swe_calc"/>.</summary>
        public const int SEFLG_NOABERR = 1024;   /* turn off 'annual' aberration of light */
        /// <summary>Astrometric position: combines <see cref="SEFLG_NOABERR"/> and <see cref="SEFLG_NOGDEFL"/> -- light-time corrected, but without aberration or light deflection.</summary>
        public const int SEFLG_ASTROMETRIC = (SEFLG_NOABERR | SEFLG_NOGDEFL); /* astrometric position,
                                        * i.e. with light-time, but without aberration and
			                            * light deflection */
        /// <summary>Return equatorial rather than ecliptic coordinates, for the iflag parameter of <see cref="swe_calc"/>.</summary>
        public const int SEFLG_EQUATORIAL = (2 * 1024);    /* equatorial positions are wanted */
        /// <summary>Return cartesian rather than polar coordinates, for the iflag parameter of <see cref="swe_calc"/>.</summary>
        public const int SEFLG_XYZ = (4 * 1024);     /* cartesian, not polar, coordinates */
        /// <summary>Return coordinates in radians rather than degrees, for the iflag parameter of <see cref="swe_calc"/>.</summary>
        public const int SEFLG_RADIANS = (8 * 1024);     /* coordinates in radians, not degrees */
        /// <summary>Return a barycentric position, for the iflag parameter of <see cref="swe_calc"/>.</summary>
        public const int SEFLG_BARYCTR = (16 * 1024);    /* barycentric position */
        /// <summary>Return a topocentric position, for the iflag parameter of <see cref="swe_calc"/> -- requires the observer location to be set via swe_set_topo.</summary>
        public const int SEFLG_TOPOCTR = (32 * 1024);    /* topocentric position */
        /// <summary>Alias for <see cref="SEFLG_TOPOCTR"/>, reused for Astronomical Almanac mode in the calculation of Kepler ellipses.</summary>
        public const int SEFLG_ORBEL_AA = SEFLG_TOPOCTR; /* used for Astronomical Almanac mode in
                                              * calculation of Kepler elipses */
        /// <summary>Tropical position, the default (value 0, no bit set), for the iflag parameter of <see cref="swe_calc"/>.</summary>
        public const int SEFLG_TROPICAL = (0);          /* tropical position (default) */
        /// <summary>Sidereal position, for the iflag parameter of <see cref="swe_calc"/> -- see <see cref="swe_set_sid_mode"/>.</summary>
        public const int SEFLG_SIDEREAL = (64 * 1024);    /* sidereal position */
        /// <summary>Use the ICRS reference frame (the DE406/DE431 reference frame), for the iflag parameter of <see cref="swe_calc"/>.</summary>
        public const int SEFLG_ICRS = (128 * 1024);   /* ICRS (DE406 reference frame) */
        /// <summary>Reproduce JPL Horizons results from 1962 to today to within 0.002 arcsec, for the iflag parameter of <see cref="swe_calc"/>.</summary>
        public const int SEFLG_DPSIDEPS_1980 = (256 * 1024); /* reproduce JPL Horizons
                                                                1962 - today to 0.002 arcsec. */
        /// <summary>Alias for <see cref="SEFLG_DPSIDEPS_1980"/>.</summary>
        public const int SEFLG_JPLHOR = SEFLG_DPSIDEPS_1980;
        /// <summary>Approximate JPL Horizons results from 1962 to today, for the iflag parameter of <see cref="swe_calc"/> -- a cheaper alternative to <see cref="SEFLG_JPLHOR"/>.</summary>
        public const int SEFLG_JPLHOR_APPROX = (512 * 1024);   /* approximate JPL Horizons 1962 - today */
        /// <summary>Calculate the position of the center of body (COB) of a planet, rather than the barycenter of its satellite system, for the iflag parameter of <see cref="swe_calc"/>.</summary>
        public const int SEFLG_CENTER_BODY = (1024 * 1024);  /* calculate position of center of body (COB)
                                                        of planet, not barycenter of its system */
        /// <summary>Internal Astrodienst test flag combination for validating raw data in the sepm9* planetary-moon files; not intended for general use.</summary>
        public const int SEFLG_TEST_PLMOON = (2 * 1024 * 1024 | SEFLG_J2000 | SEFLG_ICRS | SEFLG_HELCTR | SEFLG_TRUEPOS);  /* test raw data in files sepm9* */


        /// <summary>Threshold value for sid_mode in <see cref="swe_set_sid_mode"/>: values below this select an ayanamsha preset (SE_SIDM_*), values at or above this are the SE_SIDBIT_* modifier bits OR-ed in.</summary>
        public const int SE_SIDBITS = 256;
        /* for projection onto ecliptic of t0 */
        /// <summary>Modifier bit for sid_mode in <see cref="swe_set_sid_mode"/>: projects the ayanamsha onto the ecliptic of t0.</summary>
        public const int SE_SIDBIT_ECL_T0 = 256;
        /* for projection onto solar system plane */
        /// <summary>Modifier bit for sid_mode in <see cref="swe_set_sid_mode"/>: projects the ayanamsha onto the solar system plane.</summary>
        public const int SE_SIDBIT_SSY_PLANE = 512;
        /* with user-defined ayanamsha, t0 is UT */
        /// <summary>Modifier bit for sid_mode in <see cref="swe_set_sid_mode"/>: with a user-defined ayanamsha, t0 is UT.</summary>
        public const int SE_SIDBIT_USER_UT = 1024;
        /* ayanamsha measured on ecliptic of date;
         * see commentaries in sweph.c:swi_get_ayanamsa_ex(). */
        /// <summary>Modifier bit for sid_mode in <see cref="swe_set_sid_mode"/>: measures the ayanamsha on the ecliptic of date.</summary>
        public const int SE_SIDBIT_ECL_DATE = 2048;
        /* test feature: don't apply constant offset to ayanamsha
         * see commentary above sweph.c:get_aya_correction() */
        /// <summary>Modifier bit for sid_mode in <see cref="swe_set_sid_mode"/>: test feature that skips applying the constant offset to the ayanamsha.</summary>
        public const int SE_SIDBIT_NO_PREC_OFFSET = 4096;
        /* test feature: calculate ayanamsha using its original precession model */
        /// <summary>Modifier bit for sid_mode in <see cref="swe_set_sid_mode"/>: test feature that calculates the ayanamsha using its original precession model.</summary>
        public const int SE_SIDBIT_PREC_ORIG = 8192;

        /* sidereal modes (ayanamsas) */
        public const int SE_SIDM_FAGAN_BRADLEY = 0;
        public const int SE_SIDM_LAHIRI = 1;
        public const int SE_SIDM_DELUCE = 2;
        public const int SE_SIDM_RAMAN = 3;
        public const int SE_SIDM_USHASHASHI = 4;
        public const int SE_SIDM_KRISHNAMURTI = 5;
        public const int SE_SIDM_DJWHAL_KHUL = 6;
        public const int SE_SIDM_YUKTESHWAR = 7;
        public const int SE_SIDM_JN_BHASIN = 8;
        public const int SE_SIDM_BABYL_KUGLER1 = 9;
        public const int SE_SIDM_BABYL_KUGLER2 = 10;
        public const int SE_SIDM_BABYL_KUGLER3 = 11;
        public const int SE_SIDM_BABYL_HUBER = 12;
        public const int SE_SIDM_BABYL_ETPSC = 13;
        public const int SE_SIDM_ALDEBARAN_15TAU = 14;
        public const int SE_SIDM_HIPPARCHOS = 15;
        public const int SE_SIDM_SASSANIAN = 16;
        public const int SE_SIDM_GALCENT_0SAG = 17;
        public const int SE_SIDM_J2000 = 18;
        public const int SE_SIDM_J1900 = 19;
        public const int SE_SIDM_B1950 = 20;
        public const int SE_SIDM_SURYASIDDHANTA = 21;
        public const int SE_SIDM_SURYASIDDHANTA_MSUN = 22;
        public const int SE_SIDM_ARYABHATA = 23;
        public const int SE_SIDM_ARYABHATA_MSUN = 24;
        public const int SE_SIDM_SS_REVATI = 25;
        public const int SE_SIDM_SS_CITRA = 26;
        public const int SE_SIDM_TRUE_CITRA = 27;
        public const int SE_SIDM_TRUE_REVATI = 28;
        public const int SE_SIDM_TRUE_PUSHYA = 29;
        public const int SE_SIDM_GALCENT_RGILBRAND = 30;
        public const int SE_SIDM_GALEQU_IAU1958 = 31;
        public const int SE_SIDM_GALEQU_TRUE = 32;
        public const int SE_SIDM_GALEQU_MULA = 33;
        public const int SE_SIDM_GALALIGN_MARDYKS = 34;
        public const int SE_SIDM_TRUE_MULA = 35;
        public const int SE_SIDM_GALCENT_MULA_WILHELM = 36;
        public const int SE_SIDM_ARYABHATA_522 = 37;
        public const int SE_SIDM_BABYL_BRITTON = 38;
        public const int SE_SIDM_TRUE_SHEORAN = 39;
        public const int SE_SIDM_GALCENT_COCHRANE = 40;
        public const int SE_SIDM_GALEQU_FIORENZA = 41;
        public const int SE_SIDM_VALENS_MOON = 42;
        public const int SE_SIDM_LAHIRI_1940 = 43;
        public const int SE_SIDM_LAHIRI_VP285 = 44;
        public const int SE_SIDM_KRISHNAMURTI_VP291 = 45;
        public const int SE_SIDM_LAHIRI_ICRC = 46;
        ////#define SE_SIDM_MANJULA         43
        public const int SE_SIDM_USER = 255; /* user-defined ayanamsha, t0 is TT */

        public const int SE_NSIDM_PREDEF = 47;

        /* used for swe_nod_aps(): */
        /// <summary>Method bit for <see cref="swe_nod_aps"/>/<see cref="swe_nod_aps_ut"/>: compute mean nodes/apsides (the default).</summary>
        public const int SE_NODBIT_MEAN = 1;   /* mean nodes/apsides */
        /// <summary>Method bit for <see cref="swe_nod_aps"/>/<see cref="swe_nod_aps_ut"/>: compute osculating nodes/apsides.</summary>
        public const int SE_NODBIT_OSCU = 2;   /* osculating nodes/apsides */
        /// <summary>Method bit for <see cref="swe_nod_aps"/>/<see cref="swe_nod_aps_ut"/>: compute osculating nodes/apsides, but with motion about the solar system barycenter considered.</summary>
        public const int SE_NODBIT_OSCU_BAR = 4;   /* same, but motion about solar system barycenter is considered */
        /// <summary>Method bit for <see cref="swe_nod_aps"/>/<see cref="swe_nod_aps_ut"/>, OR-ed with one of the other <c>SE_NODBIT_*</c> bits: return the orbit's second focal point instead of the aphelion.</summary>
        public const int SE_NODBIT_FOPOINT = 256;   /* focal point of orbit instead of aphelion */

        /* default ephemeris used when no ephemeris flagbit is set */
        /// <summary>The ephemeris used by <see cref="swe_calc(double, Int32, Int32, double[], ref string)"/> when no <see cref="SEFLG_EPHMASK"/> bit is set; currently <see cref="SEFLG_SWIEPH"/>.</summary>
        public const int SEFLG_DEFAULTEPH = SEFLG_SWIEPH;

        /// <summary>Maximum size of a fixed-star name; the <c>star</c> parameter buffer in <see cref="swe_fixstar"/> must allow twice this space for the returned name.</summary>
        public const int SE_MAX_STNAME = 256;	/* maximum size of fixstar name;
                                                 * the parameter star in swe_fixstar
                                                 * must allow twice this space for
                                                 * the returned star name.
                                                 */

        /* defines for eclipse computations */

        /// <summary>Eclipse-type bit: the eclipse is central (the shadow axis touches the Earth), returned by/matched against by the eclipse and occultation functions.</summary>
        public const int SE_ECL_CENTRAL = 1;
        /// <summary>Eclipse-type bit: the eclipse is non-central (total/annular, but the shadow axis misses the Earth), returned by/matched against by the eclipse and occultation functions.</summary>
        public const int SE_ECL_NONCENTRAL = 2;
        /// <summary>Eclipse-type bit: the eclipse is total, returned by/matched against by the eclipse and occultation functions.</summary>
        public const int SE_ECL_TOTAL = 4;
        /// <summary>Eclipse-type bit: the eclipse is annular, returned by/matched against by the eclipse and occultation functions.</summary>
        public const int SE_ECL_ANNULAR = 8;
        /// <summary>Eclipse-type bit: the eclipse is partial, returned by/matched against by the eclipse and occultation functions.</summary>
        public const int SE_ECL_PARTIAL = 16;
        /// <summary>Eclipse-type bit: the eclipse is annular-total (hybrid), returned by/matched against by the eclipse and occultation functions.</summary>
        public const int SE_ECL_ANNULAR_TOTAL = 32;
        /// <summary>Alias for <see cref="SE_ECL_ANNULAR_TOTAL"/>: the eclipse is annular-total (hybrid).</summary>
        public const int SE_ECL_HYBRID = 32;  // = annular-total

        /// <summary>Eclipse-type bit: the (lunar) eclipse is penumbral, returned by/matched against by the eclipse functions.</summary>
        public const int SE_ECL_PENUMBRAL = 64;
        /// <summary>Mask of every eclipse-type bit a solar eclipse can have: <see cref="SE_ECL_CENTRAL"/>, <see cref="SE_ECL_NONCENTRAL"/>, <see cref="SE_ECL_TOTAL"/>, <see cref="SE_ECL_ANNULAR"/>, <see cref="SE_ECL_PARTIAL"/> and <see cref="SE_ECL_ANNULAR_TOTAL"/> combined.</summary>
        public const int SE_ECL_ALLTYPES_SOLAR = (SE_ECL_CENTRAL | SE_ECL_NONCENTRAL | SE_ECL_TOTAL | SE_ECL_ANNULAR | SE_ECL_PARTIAL | SE_ECL_ANNULAR_TOTAL);
        /// <summary>Mask of every eclipse-type bit a lunar eclipse can have: <see cref="SE_ECL_TOTAL"/>, <see cref="SE_ECL_PARTIAL"/> and <see cref="SE_ECL_PENUMBRAL"/> combined.</summary>
        public const int SE_ECL_ALLTYPES_LUNAR = (SE_ECL_TOTAL | SE_ECL_PARTIAL | SE_ECL_PENUMBRAL);
        /// <summary>Visibility bit: the eclipse is visible from the given location, returned by the local-attribute eclipse functions (e.g. <see cref="swe_sol_eclipse_how(double, Int32, double[], double[], ref string)"/>).</summary>
        public const int SE_ECL_VISIBLE = 128;
        /// <summary>Visibility bit: the eclipse's maximum phase is visible from the given location, returned by the local-attribute eclipse functions.</summary>
        public const int SE_ECL_MAX_VISIBLE = 256;
        /// <summary>Visibility bit: the beginning of the partial eclipse is visible from the given location. Same bit as <see cref="SE_ECL_PARTBEG_VISIBLE"/>.</summary>
        public const int SE_ECL_1ST_VISIBLE = 512;	/* begin of partial eclipse */
        /// <summary>Visibility bit: the beginning of the partial eclipse is visible from the given location. Same bit as <see cref="SE_ECL_1ST_VISIBLE"/>.</summary>
        public const int SE_ECL_PARTBEG_VISIBLE = 512;	/* begin of partial eclipse */
        /// <summary>Visibility bit: the beginning of the total eclipse is visible from the given location. Same bit as <see cref="SE_ECL_TOTBEG_VISIBLE"/>.</summary>
        public const int SE_ECL_2ND_VISIBLE = 1024;	/* begin of total eclipse */
        /// <summary>Visibility bit: the beginning of the total eclipse is visible from the given location. Same bit as <see cref="SE_ECL_2ND_VISIBLE"/>.</summary>
        public const int SE_ECL_TOTBEG_VISIBLE = 1024;	/* begin of total eclipse */
        /// <summary>Visibility bit: the end of the total eclipse is visible from the given location. Same bit as <see cref="SE_ECL_TOTEND_VISIBLE"/>.</summary>
        public const int SE_ECL_3RD_VISIBLE = 2048;    /* end of total eclipse */
        /// <summary>Visibility bit: the end of the total eclipse is visible from the given location. Same bit as <see cref="SE_ECL_3RD_VISIBLE"/>.</summary>
        public const int SE_ECL_TOTEND_VISIBLE = 2048;    /* end of total eclipse */
        /// <summary>Visibility bit: the end of the partial eclipse is visible from the given location. Same bit as <see cref="SE_ECL_PARTEND_VISIBLE"/>.</summary>
        public const int SE_ECL_4TH_VISIBLE = 4096;    /* end of partial eclipse */
        /// <summary>Visibility bit: the end of the partial eclipse is visible from the given location. Same bit as <see cref="SE_ECL_4TH_VISIBLE"/>.</summary>
        public const int SE_ECL_PARTEND_VISIBLE = 4096;    /* end of partial eclipse */
        /// <summary>Visibility bit: the beginning of the penumbral eclipse is visible from the given location.</summary>
        public const int SE_ECL_PENUMBBEG_VISIBLE = 8192;    /* begin of penumbral eclipse */
        /// <summary>Visibility bit: the end of the penumbral eclipse is visible from the given location.</summary>
        public const int SE_ECL_PENUMBEND_VISIBLE = 16384;   /* end of penumbral eclipse */
        /// <summary>Occultation visibility bit: the occultation begins during daylight at the given location. Reuses the same bit value as <see cref="SE_ECL_PENUMBBEG_VISIBLE"/>, but for occultations, not lunar eclipses.</summary>
        public const int SE_ECL_OCC_BEG_DAYLIGHT = 8192;    /* occultation begins during the day */
        /// <summary>Occultation visibility bit: the occultation ends during daylight at the given location. Reuses the same bit value as <see cref="SE_ECL_PENUMBEND_VISIBLE"/>, but for occultations, not lunar eclipses.</summary>
        public const int SE_ECL_OCC_END_DAYLIGHT = 16384;   /* occultation ends during the day */
        /// <summary>Search-control bit for the <c>backward</c>/search-mode parameter of the occultation and eclipse "when" functions: check only whether the next lunar conjunction with the body is itself an occultation/eclipse, without continuing to search further.</summary>
        public const int SE_ECL_ONE_TRY = (32 * 1024);
        /* check if the next conjunction of the moon with
         * a planet is an occultation; don't search further */

        /* for swe_rise_transit() */
        /// <summary><c>rsmi</c> value for <see cref="swe_rise_trans(double, Int32, string, Int32, Int32, double[], double, double, ref double, ref string)"/>: search for the next rising.</summary>
        public const int SE_CALC_RISE = 1;
        /// <summary><c>rsmi</c> value for <see cref="swe_rise_trans(double, Int32, string, Int32, Int32, double[], double, double, ref double, ref string)"/>: search for the next setting.</summary>
        public const int SE_CALC_SET = 2;
        /// <summary><c>rsmi</c> value for <see cref="swe_rise_trans(double, Int32, string, Int32, Int32, double[], double, double, ref double, ref string)"/>: search for the next upper (meridian) transit.</summary>
        public const int SE_CALC_MTRANSIT = 4;
        /// <summary><c>rsmi</c> value for <see cref="swe_rise_trans(double, Int32, string, Int32, Int32, double[], double, double, ref double, ref string)"/>: search for the next lower (antimeridian) transit.</summary>
        public const int SE_CALC_ITRANSIT = 8;
        /// <summary><c>rsmi</c> modifier bit, OR-ed with <see cref="SE_CALC_RISE"/>/<see cref="SE_CALC_SET"/>: use the rise/set of the disc center instead of the upper limb.</summary>
        public const int SE_BIT_DISC_CENTER = 256; /* to be or'ed to SE_CALC_RISE/SET,
                                                    * if rise or set of disc center is
                                                    * required */
        /// <summary><c>rsmi</c> modifier bit, OR-ed with <see cref="SE_CALC_RISE"/>/<see cref="SE_CALC_SET"/>: use the rise/set of the disc's lower limb instead of the upper limb.</summary>
        public const int SE_BIT_DISC_BOTTOM = 8192; /* to be or'ed to SE_CALC_RISE/SET,
                                                     * if rise or set of lower limb of
                                                     * disc is requried */
        /// <summary><c>rsmi</c> modifier bit for <see cref="swe_rise_trans(double, Int32, string, Int32, Int32, double[], double, double, ref double, ref string)"/>: use the geocentric rather than topocentric position of the object and ignore its ecliptic latitude.</summary>
        public const int SE_BIT_GEOCTR_NO_ECL_LAT = 128; /* use geocentric rather than topocentric
                                                            position of object and
                                                            ignore its ecliptic latitude */
        /// <summary><c>rsmi</c> modifier bit, OR-ed with <see cref="SE_CALC_RISE"/>/<see cref="SE_CALC_SET"/>: ignore atmospheric refraction.</summary>
        public const int SE_BIT_NO_REFRACTION = 512; /* to be or'ed to SE_CALC_RISE/SET,
                                                      * if refraction is to be ignored */
        /// <summary><c>rsmi</c> modifier bit, OR-ed with <see cref="SE_CALC_RISE"/>/<see cref="SE_CALC_SET"/>: search for the beginning/end of civil twilight instead of an ordinary rise/set.</summary>
        public const int SE_BIT_CIVIL_TWILIGHT = 1024; /* to be or'ed to SE_CALC_RISE/SET */
        /// <summary><c>rsmi</c> modifier bit, OR-ed with <see cref="SE_CALC_RISE"/>/<see cref="SE_CALC_SET"/>: search for the beginning/end of nautical twilight instead of an ordinary rise/set.</summary>
        public const int SE_BIT_NAUTIC_TWILIGHT = 2048; /* to be or'ed to SE_CALC_RISE/SET */
        /// <summary><c>rsmi</c> modifier bit, OR-ed with <see cref="SE_CALC_RISE"/>/<see cref="SE_CALC_SET"/>: search for the beginning/end of astronomical twilight instead of an ordinary rise/set.</summary>
        public const int SE_BIT_ASTRO_TWILIGHT = 4096; /* to be or'ed to SE_CALC_RISE/SET */
        /// <summary><c>rsmi</c> modifier bit, OR-ed with <see cref="SE_CALC_RISE"/>/<see cref="SE_CALC_SET"/>: neglect the effect of distance on the apparent disc size.</summary>
        public const int SE_BIT_FIXED_DISC_SIZE = 16384; /* or'ed to SE_CALC_RISE/SET:
                                                                * neglect the effect of distance on
                                                                * disc size */
        /// <summary>Astrodienst-internal test flag: forces <see cref="swe_rise_trans(double, Int32, string, Int32, Int32, double[], double, double, ref double, ref string)"/> to use the old, slow calculation method for risings and settings.</summary>
        public const int SE_BIT_FORCE_SLOW_METHOD = 32768;  /* This is only an Astrodienst in-house
                                                             * test flag.It forces the usage
                                                             * of the old, slow calculation of
                                                             * risings and settings. */
        /// <summary>Combination of <see cref="SE_BIT_DISC_CENTER"/>, <see cref="SE_BIT_NO_REFRACTION"/> and <see cref="SE_BIT_GEOCTR_NO_ECL_LAT"/>, matching the definition of rising/setting used in Hindu astrology.</summary>
        public const int SE_BIT_HINDU_RISING = (SE_BIT_DISC_CENTER | SE_BIT_NO_REFRACTION | SE_BIT_GEOCTR_NO_ECL_LAT);


        /* for swe_azalt() and swe_azalt_rev() */
        /// <summary><c>calc_flag</c> value for <see cref="swe_azalt(double, Int32, double[], double, double, double[], double[])"/>: the input coordinates are ecliptic longitude/latitude.</summary>
        public const int SE_ECL2HOR = 0;
        /// <summary><c>calc_flag</c> value for <see cref="swe_azalt(double, Int32, double[], double, double, double[], double[])"/>: the input coordinates are equatorial right ascension/declination.</summary>
        public const int SE_EQU2HOR = 1;
        /// <summary><c>calc_flag</c> value for <see cref="swe_azalt_rev(double, Int32, double[], double[], double[])"/>: return ecliptic coordinates.</summary>
        public const int SE_HOR2ECL = 0;
        /// <summary><c>calc_flag</c> value for <see cref="swe_azalt_rev(double, Int32, double[], double[], double[])"/>: return equatorial coordinates.</summary>
        public const int SE_HOR2EQU = 1;

        /* for swe_refrac() */
        /// <summary><c>calc_flag</c> value for <see cref="swe_refrac(double, double, double, Int32)"/>/<see cref="swe_refrac_extended(double, double, double, double, double, Int32, double[])"/>: the input altitude is true (geometric) and the apparent altitude is returned.</summary>
        public const int SE_TRUE_TO_APP = 0;
        /// <summary><c>calc_flag</c> value for <see cref="swe_refrac(double, double, double, Int32)"/>/<see cref="swe_refrac_extended(double, double, double, double, double, Int32, double[])"/>: the input altitude is apparent and the true (geometric) altitude is returned.</summary>
        public const int SE_APP_TO_TRUE = 1;

        /*
         * only used for experimenting with various JPL ephemeris files
         * which are available at Astrodienst's internal network
         */
        /// <summary>JPL DE ephemeris number matching <see cref="SE_FNAME_DFT"/>, the current default JPL file.</summary>
        public const int SE_DE_NUMBER = 431;
        /// <summary>JPL ephemeris file name for DE200.</summary>
        public const string SE_FNAME_DE200 = "de200.eph";
        /// <summary>JPL ephemeris file name for DE403.</summary>
        public const string SE_FNAME_DE403 = "de403.eph";
        /// <summary>JPL ephemeris file name for DE404.</summary>
        public const string SE_FNAME_DE404 = "de404.eph";
        /// <summary>JPL ephemeris file name for DE405.</summary>
        public const string SE_FNAME_DE405 = "de405.eph";
        /// <summary>JPL ephemeris file name for DE406.</summary>
        public const string SE_FNAME_DE406 = "de406.eph";
        /// <summary>JPL ephemeris file name for DE431.</summary>
        public const string SE_FNAME_DE431 = "de431.eph";
        /// <summary>Default JPL ephemeris file name; alias for <see cref="SE_FNAME_DE431"/>.</summary>
        public const string SE_FNAME_DFT = SE_FNAME_DE431;
        /// <summary>Secondary/fallback default JPL ephemeris file name; alias for <see cref="SE_FNAME_DE406"/>.</summary>
        public const string SE_FNAME_DFT2 = SE_FNAME_DE406;
        /// <summary>Legacy fixed-star data file name, superseded by <see cref="SE_STARFILE"/> (see <see cref="swe_set_ephe_path"/>).</summary>
        public const string SE_STARFILE_OLD = "fixstars.cat";
        /// <summary>Fixed-star data file name (see <see cref="swe_set_ephe_path"/>).</summary>
        public const string SE_STARFILE = "sefstars.txt";
        /// <summary>Asteroid-name lookup data file name (see <see cref="swe_set_ephe_path"/>).</summary>
        public const string SE_ASTNAMFILE = "seasnam.txt";
        /// <summary>Fictitious-bodies/orbital-elements data file name (see <see cref="swe_set_ephe_path"/>).</summary>
        public const string SE_FICTFILE = "seorbel.txt";

        /*
         * ephemeris path
         * this defines where ephemeris files are expected if the function
         * swe_set_ephe_path() is not called by the application.
         * Normally, every application should make this call to define its
         * own place for the ephemeris files.
         */
        /// <summary>
        /// SweNet : We create a pseudo constant for detect ephemeris path when loading
        /// </summary>
        public const String SE_EPHE_PATH = "[ephe]";

        // SE_EPHE_PATH above is public API and a const, so its VALUE cannot change here:
        // const fields are inlined into every caller at THAT caller's compile time, not
        // looked up at run time, so changing the literal would silently desync anything
        // already compiled against "[ephe]" from what this library actually does --
        // binary-breaking in a way a version bump alone does not fix. The real default
        // swe_set_ephe_path (and swi_init_swed_if_start) fall back to when no path is
        // configured lives here instead, as a member the constant does not gate: the
        // constant stays public API, the behaviour is not.
        //
        // swephexp.h:399-408 picks that default with a compile-time #if MSDOS, and MSDOS
        // there is also defined true for any _WIN32/WIN32 build (sweodef.h:96-98), so
        // upstream's real split is "Windows" vs. "everything else", not literally
        // MS-DOS. This port ships one assembly for Windows, Linux and macOS rather than
        // compiling per platform, so matching the C's #if by its letter is not available;
        // resolving the same split at run time instead of compile time is a deliberate
        // divergence from a literal transliteration, made because it matches the C's
        // intent on every platform this port targets, rather than matching its letter on
        // only one.
        //
        // Windows keeps upstream's own literal, backslash-terminated string. Combined
        // with swe_set_ephe_path's own trailing-separator check (Sweph.cs, "sweph.c:1339
        // compares..."), which tests only against DIR_GLUE and DIR_GLUE is '/' on every
        // platform this port targets (see the DIR_GLUE comment above), a Windows caller
        // who never configures a path gets "\sweph\ephe\/" -- a redundant trailing '/'
        // after the literal backslash -- where the C itself produces exactly
        // "\sweph\ephe\" (its own DIR_GLUE is '\\' on Windows, already matching the
        // string's own trailing character, so its check never appends). Windows accepts
        // both separators interchangeably in a path, so this is a cosmetic mismatch in
        // the exact bytes of an error message, not a functional one; left as-is rather
        // than special-casing the trailing-separator check for one platform's string,
        // consistent with DIR_GLUE's own existing "one value for every platform" choice.
        //
        // The non-Windows string carries upstream's three components in upstream's order,
        // joined with ':' exactly as swephexp.h:403 writes it -- not a typo and not a
        // second guess at the C. This is deliberately the literal, not a port-specific
        // rewrite: PATH_SEPARATOR (SwissEph.sweodef.h.cs) is per-platform, matching the
        // C's own two cut-lists rather than using one value everywhere -- { ';', ':' } off
        // Windows (sweodef.h:307, "semicolon or colon may be used" under #if UNIX_FS) and
        // { ';' } on Windows (sweodef.h:313, the #else branch, where a bare ':' would split
        // a drive letter). swi_fopen splits swed.ephepath on exactly that cut-list (Sweph.cs,
        // sweph.c:2377), so this colon-joined literal correctly splits into its three
        // components off Windows, including the "." component -- the current directory,
        // which sweph.c:2381 maps to the empty prefix and which is the only one of the
        // three that exists on a machine that is not Astrodienst's.
        //
        // This was briefly wrong. An earlier revision kept PATH_SEPARATOR at { ';' } on
        // every platform and rewrote this default to a ';'-joined string to match, reasoning
        // that a bare ':' was unsafe to add to a cross-platform cut-list. That reasoning was
        // sound for Windows drive letters and wrong to apply off Windows, where the C accepts
        // ':' natively and a caller-supplied ';'-joined path is not what any other Swiss
        // Ephemeris binding expects. It also moved the port away from the C on exactly the
        // platforms the exactness gates measure: 192 of 15,819 analytic rows differed from
        // gcc's C reference, 196 from clang's. Restored per-platform (this default and
        // PATH_SEPARATOR together, since the two have to agree) once that regression was
        // found; see commit c334d15's own message for the fuller account.
        // Split from the property so both branches are reachable from any platform. The
        // property alone cannot be tested for this: the separator mistake it guards against
        // is confined to the non-Windows literal, so a test reading DefaultEphePath on a
        // Windows runner exercises the other branch and passes no matter what the non-Windows
        // string says -- measured, by reintroducing the ':' form and watching the test stay
        // green. Tests/SwissEphNet.Tests/DefaultEphePathSplitTest.cs calls this directly with
        // both values instead.
        internal static string DefaultEphePathFor(bool isWindows)
        {
            return isWindows
                ? "\\sweph\\ephe\\"
                : ".:/users/ephe2/:/users/ephe/";
        }

        internal static string DefaultEphePath
        {
            get
            {
                return DefaultEphePathFor(RuntimeInformation.IsOSPlatform(OSPlatform.Windows));
            }
        }


        /* defines for function swe_split_deg() (in swephlib.c) */
        /// <summary>Round to the nearest arc-second, for the <c>roundflag</c> parameter of
        /// <see cref="swe_split_deg"/>.</summary>
        public const int SE_SPLIT_DEG_ROUND_SEC = 1;
        /// <summary>Round to the nearest arc-minute, for the <c>roundflag</c> parameter of
        /// <see cref="swe_split_deg"/>.</summary>
        public const int SE_SPLIT_DEG_ROUND_MIN = 2;
        /// <summary>Round to the nearest degree, for the <c>roundflag</c> parameter of
        /// <see cref="swe_split_deg"/>.</summary>
        public const int SE_SPLIT_DEG_ROUND_DEG = 4;
        /// <summary>Split into zodiac sign plus degree within the sign, instead of a plain angle,
        /// for the <c>roundflag</c> parameter of <see cref="swe_split_deg"/>.</summary>
        public const int SE_SPLIT_DEG_ZODIACAL = 8;
        /// <summary>Split into nakshatra (lunar mansion) instead of zodiac sign, for the
        /// <c>roundflag</c> parameter of <see cref="swe_split_deg"/>.</summary>
        public const int SE_SPLIT_DEG_NAKSHATRA = 1024;
        /// <summary>Don't round up into the next zodiac sign, for the <c>roundflag</c> parameter of
        /// <see cref="swe_split_deg"/>.</summary>
        public const int SE_SPLIT_DEG_KEEP_SIGN = 16;	/* don't round to next sign,
                                                         * e.g. 29.9999999 will be rounded
                                                         * to 29d59'59" (or 29d59' or 29d) */
        /// <summary>Don't round up into the next whole degree, for the <c>roundflag</c> parameter of
        /// <see cref="swe_split_deg"/>.</summary>
        public const int SE_SPLIT_DEG_KEEP_DEG = 32;	/* don't round to next degree
                                                         * e.g. 13.9999999 will be rounded
                                                         * to 13d59'59" (or 13d59' or 13d) */

        /* for heliacal functions */
        /// <summary>Event type for <see cref="swe_heliacal_ut"/>: heliacal rising. Same value as
        /// <see cref="SE_MORNING_FIRST"/>.</summary>
        public const int SE_HELIACAL_RISING = 1;
        /// <summary>Event type for <see cref="swe_heliacal_ut"/>: heliacal setting. Same value as
        /// <see cref="SE_EVENING_LAST"/>.</summary>
        public const int SE_HELIACAL_SETTING = 2;
        /// <summary>Event type for <see cref="swe_heliacal_ut"/>: morning first visibility. Alias for
        /// <see cref="SE_HELIACAL_RISING"/>.</summary>
        public const int SE_MORNING_FIRST = SE_HELIACAL_RISING;
        /// <summary>Event type for <see cref="swe_heliacal_ut"/>: evening last visibility. Alias for
        /// <see cref="SE_HELIACAL_SETTING"/>.</summary>
        public const int SE_EVENING_LAST = SE_HELIACAL_SETTING;
        /// <summary>Event type for <see cref="swe_heliacal_ut"/>: evening first visibility.</summary>
        public const int SE_EVENING_FIRST = 3;
        /// <summary>Event type for <see cref="swe_heliacal_ut"/>: morning last visibility.</summary>
        public const int SE_MORNING_LAST = 4;
        /// <summary>Event type for <see cref="swe_heliacal_ut"/>: acronychal rising. Still not
        /// implemented.</summary>
        public const int SE_ACRONYCHAL_RISING = 5;  /* still not implemented */
        /// <summary>Event type for <see cref="swe_heliacal_ut"/>: acronychal setting. Still not
        /// implemented. Same value as <see cref="SE_COSMICAL_SETTING"/>.</summary>
        public const int SE_ACRONYCHAL_SETTING = 6;  /* still not implemented */
        /// <summary>Event type for <see cref="swe_heliacal_ut"/>: cosmical setting. Alias for
        /// <see cref="SE_ACRONYCHAL_SETTING"/> (still not implemented).</summary>
        public const int SE_COSMICAL_SETTING = SE_ACRONYCHAL_SETTING;

        /// <summary><c>helflag</c> bit for <see cref="swe_heliacal_ut"/>/
        /// <see cref="swe_heliacal_pheno_ut"/>: search up to a year ahead instead of the default
        /// shorter window.</summary>
        public const int SE_HELFLAG_LONG_SEARCH = 128;
        /// <summary><c>helflag</c> bit for <see cref="swe_heliacal_ut"/>/
        /// <see cref="swe_heliacal_pheno_ut"/>: use high-precision calculation.</summary>
        public const int SE_HELFLAG_HIGH_PRECISION = 256;
        /// <summary><c>helflag</c> bit for <see cref="swe_heliacal_ut"/>/
        /// <see cref="swe_heliacal_pheno_ut"/>: use the optical-aid parameters supplied in
        /// <c>dobs</c> (magnification, aperture, transmission).</summary>
        public const int SE_HELFLAG_OPTICAL_PARAMS = 512;
        /// <summary><c>helflag</c> bit for <see cref="swe_heliacal_ut"/>/
        /// <see cref="swe_heliacal_pheno_ut"/>: suppress detail output.</summary>
        public const int SE_HELFLAG_NO_DETAILS = 1024;
        /// <summary><c>helflag</c> bit for <see cref="swe_heliacal_ut"/>: limit the search to a
        /// single synodic period.</summary>
        public const int SE_HELFLAG_SEARCH_1_PERIOD = (1 << 11);  /*  2048 */
        /// <summary><c>helflag</c> bit for <see cref="swe_heliacal_ut"/>/
        /// <see cref="swe_heliacal_pheno_ut"/>: use the visibility limit for a dark (moonless)
        /// sky.</summary>
        public const int SE_HELFLAG_VISLIM_DARK = (1 << 12);  /*  4096 */
        /// <summary><c>helflag</c> bit for <see cref="swe_heliacal_ut"/>/
        /// <see cref="swe_heliacal_pheno_ut"/>: compute the visibility limit ignoring the
        /// Moon.</summary>
        public const int SE_HELFLAG_VISLIM_NOMOON = (1 << 13);  /*  8192 */
        /* the following undocumented defines are for test reasons only */
        /// <summary><c>helflag</c> bit, for test purposes only (undocumented by Astrodienst): force
        /// photopic (daylight) vision mode. See <see cref="SE_PHOTOPIC_FLAG"/>.</summary>
        public const int SE_HELFLAG_VISLIM_PHOTOPIC = (1 << 14);  /* 16384 */
        /// <summary><c>helflag</c> bit, for test purposes only (undocumented by Astrodienst): force
        /// scotopic (night) vision mode. See <see cref="SE_SCOTOPIC_FLAG"/>.</summary>
        public const int SE_HELFLAG_VISLIM_SCOTOPIC = (1 << 15);  /* 32768 */
        /// <summary><c>helflag</c> bit, for test purposes only (undocumented by Astrodienst): request
        /// arcus visionis output. Same value as <see cref="SE_HELFLAG_AVKIND_VR"/>.</summary>
        public const int SE_HELFLAG_AV = (1 << 16);  /* 65536 */
        /// <summary><c>helflag</c> bit, for test purposes only (undocumented by Astrodienst): arcus
        /// visionis kind "VR" (Visual/Reijs). Same value as <see cref="SE_HELFLAG_AV"/>.</summary>
        public const int SE_HELFLAG_AVKIND_VR = (1 << 16);  /* 65536 */
        /// <summary><c>helflag</c> bit, for test purposes only (undocumented by Astrodienst): arcus
        /// visionis kind "PTO".</summary>
        public const int SE_HELFLAG_AVKIND_PTO = (1 << 17);
        /// <summary><c>helflag</c> bit, for test purposes only (undocumented by Astrodienst): arcus
        /// visionis kind "MIN7".</summary>
        public const int SE_HELFLAG_AVKIND_MIN7 = (1 << 18);
        /// <summary><c>helflag</c> bit, for test purposes only (undocumented by Astrodienst): arcus
        /// visionis kind "MIN9".</summary>
        public const int SE_HELFLAG_AVKIND_MIN9 = (1 << 19);
        /// <summary>Combination of all arcus visionis kind bits: <see cref="SE_HELFLAG_AVKIND_VR"/>
        /// | <see cref="SE_HELFLAG_AVKIND_PTO"/> | <see cref="SE_HELFLAG_AVKIND_MIN7"/> |
        /// <see cref="SE_HELFLAG_AVKIND_MIN9"/>.</summary>
        public const int SE_HELFLAG_AVKIND = (SE_HELFLAG_AVKIND_VR | SE_HELFLAG_AVKIND_PTO | SE_HELFLAG_AVKIND_MIN7 | SE_HELFLAG_AVKIND_MIN9);
        /// <summary>Sentinel Julian day value used to signal an invalid/undefined date.</summary>
        public const double TJD_INVALID = 99999999.0;
        // Not a C# language constant: swephexp.h:451 is `#define SIMULATE_VICTORVB 1`, so C's
        // `#ifndef SIMULATE_VICTORVB` guards (SweHel.cs's negated sites) are always compiled out.
        // A `const bool` here would make the negated `if (!SIMULATE_VICTORVB)` at those sites a
        // compile-time-false condition and trip CS0162 ("unreachable code"), the same problem
        // Sweph.cs:105's SID_TNODE_FROM_ECL_T0 avoids by not being a language constant either.
        public static readonly bool SIMULATE_VICTORVB = true;

#if FALSE  // unused and redundant
        public const int SE_HELIACAL_LONG_SEARCH = 128;
        public const int SE_HELIACAL_HIGH_PRECISION = 256;
        public const int SE_HELIACAL_OPTICAL_PARAMS = 512;
        public const int SE_HELIACAL_NO_DETAILS = 1024;
        public const int SE_HELIACAL_SEARCH_1_PERIOD = (1 << 11);  /*  2048 */
        public const int SE_HELIACAL_VISLIM_DARK = (1 << 12);  /*  4096 */
        public const int SE_HELIACAL_VISLIM_NOMOON = (1 << 13);  /*  8192 */
        public const int SE_HELIACAL_VISLIM_PHOTOPIC = (1 << 14);  /* 16384 */
        public const int SE_HELIACAL_AVKIND_VR = (1 << 15);  /* 32768 */
        public const int SE_HELIACAL_AVKIND_PTO = (1 << 16);
        public const int SE_HELIACAL_AVKIND_MIN7 = (1 << 17);
        public const int SE_HELIACAL_AVKIND_MIN9 = (1 << 18);
        public const int SE_HELIACAL_AVKIND = (SE_HELFLAG_AVKIND_VR | SE_HELFLAG_AVKIND_PTO | SE_HELFLAG_AVKIND_MIN7 | SE_HELFLAG_AVKIND_MIN9);
#endif

        /// <summary>Vision mode for the heliacal/visibility functions: photopic (daylight)
        /// vision.</summary>
        public const int SE_PHOTOPIC_FLAG = 0;
        /// <summary>Vision mode for the heliacal/visibility functions: scotopic (night)
        /// vision.</summary>
        public const int SE_SCOTOPIC_FLAG = 1;
        /// <summary>Vision mode for the heliacal/visibility functions: mixed photopic/scotopic
        /// (twilight) vision.</summary>
        public const int SE_MIXEDOPIC_FLAG = 2;

        /* for swe_set_tid_acc() and ephemeris-dependent delta t:
         * intrinsic tidal acceleration in the mean motion of the moon,
         * not given in the parameters list of the ephemeris files but computed
         * by Chapront/Chapront-TouzÃ©/Francou A&A 387 (2002), p. 705.
         */
        /// <summary>Tidal acceleration preset for <see cref="swe_set_tid_acc"/>: the value implicit
        /// in JPL ephemeris DE200.</summary>
        public const double SE_TIDAL_DE200 = (-23.8946);
        /// <summary>Tidal acceleration preset for <see cref="swe_set_tid_acc"/>: the value implicit
        /// in JPL ephemeris DE403 (was -25.8 until v. 1.76.2).</summary>
        public const double SE_TIDAL_DE403 = (-25.580);  /* was (-25.8) until V. 1.76.2 */
        /// <summary>Tidal acceleration preset for <see cref="swe_set_tid_acc"/>: the value implicit
        /// in JPL ephemeris DE404 (was -25.8 until v. 1.76.2).</summary>
        public const double SE_TIDAL_DE404 = (-25.580);  /* was (-25.8) until V. 1.76.2 */
        /// <summary>Tidal acceleration preset for <see cref="swe_set_tid_acc"/>: the value implicit
        /// in JPL ephemeris DE405 (was -25.7376 until v. 1.76.2).</summary>
        public const double SE_TIDAL_DE405 = (-25.826);  /* was (-25.7376) until V. 1.76.2 */
        /// <summary>Tidal acceleration preset for <see cref="swe_set_tid_acc"/>: the value implicit
        /// in JPL ephemeris DE406 (was -25.7376 until v. 1.76.2).</summary>
        public const double SE_TIDAL_DE406 = (-25.826);  /* was (-25.7376) until V. 1.76.2 */
        /// <summary>Tidal acceleration preset for <see cref="swe_set_tid_acc"/>: the value implicit
        /// in JPL ephemeris DE421, per JPL Interoffice Memorandum 14-mar-2008 on DE421 Lunar
        /// Orbit.</summary>
        public const double SE_TIDAL_DE421 = (-25.85);   /* JPL Interoffice Memorandum 14-mar-2008 on DE421 Lunar Orbit */
        /// <summary>Tidal acceleration preset for <see cref="swe_set_tid_acc"/>: the value implicit
        /// in JPL ephemeris DE422, per JPL Interoffice Memorandum 14-mar-2008 on DE421 (sic) Lunar
        /// Orbit.</summary>
        public const double SE_TIDAL_DE422 = (-25.85);   /* JPL Interoffice Memorandum 14-mar-2008 on DE421 (sic!) Lunar Orbit */
        /// <summary>Tidal acceleration preset for <see cref="swe_set_tid_acc"/>: the value implicit
        /// in JPL ephemeris DE430, per JPL Interoffice Memorandum 9-jul-2013 on DE430 Lunar
        /// Orbit.</summary>
        public const double SE_TIDAL_DE430 = (-25.82);   /* JPL Interoffice Memorandum 9-jul-2013 on DE430 Lunar Orbit */
        /// <summary>Tidal acceleration preset for <see cref="swe_set_tid_acc"/>: the value implicit
        /// in JPL ephemeris DE431, per IPN Progress Report 42-196, 15-feb-2014, p. 15 (was -25.82 in
        /// v. 2.00.00). This is <see cref="SE_TIDAL_DEFAULT"/>.</summary>
        public const double SE_TIDAL_DE431 = (-25.80);   /* IPN Progress Report 42-196 • February 15, 2014, p. 15; was (-25.82) in V. 2.00.00 */
        /// <summary>Tidal acceleration preset for <see cref="swe_set_tid_acc"/>: the value implicit
        /// in JPL ephemeris DE441 (unpublished value, from email by Jon Giorgini to Dieter Koch, 11
        /// Apr 2021).</summary>
        public const double SE_TIDAL_DE441 = (-25.936);   /* unpublished value, from email by Jon Giorgini to DK on 11 Apr 2021 */
        /// <summary>Tidal acceleration preset for <see cref="swe_set_tid_acc"/>: a round -26.0
        /// arcsec/century^2 value.</summary>
        public const double SE_TIDAL_26 = (-26.0);
        /// <summary>Tidal acceleration preset for <see cref="swe_set_tid_acc"/>: the Stephenson et
        /// al. 2016 value.</summary>
        public const double SE_TIDAL_STEPHENSON_2016 = (-25.85);
        /// <summary>The current default tidal acceleration, currently
        /// <see cref="SE_TIDAL_DE431"/>.</summary>
        public const double SE_TIDAL_DEFAULT = SE_TIDAL_DE431;
        /// <summary>Special value for <see cref="swe_set_tid_acc"/> that picks the tidal
        /// acceleration automatically from whichever ephemeris file is in use, instead of a fixed
        /// preset.</summary>
        public const double SE_TIDAL_AUTOMATIC = 999999;
        /// <summary>Tidal acceleration used with the Moshier ephemeris. Alias for
        /// <see cref="SE_TIDAL_DE404"/>.</summary>
        public const double SE_TIDAL_MOSEPH = SE_TIDAL_DE404;
        /// <summary>Tidal acceleration used with the Swiss Ephemeris (SWIEPH) files. Alias for
        /// <see cref="SE_TIDAL_DEFAULT"/>.</summary>
        public const double SE_TIDAL_SWIEPH = SE_TIDAL_DEFAULT;
        /// <summary>Tidal acceleration used with the JPL ephemeris. Alias for
        /// <see cref="SE_TIDAL_DEFAULT"/>.</summary>
        public const double SE_TIDAL_JPLEPH = SE_TIDAL_DEFAULT;

        /* for function swe_set_delta_t_userdef() */
        /// <summary>Sentinel value for <see cref="swe_set_delta_t_userdef"/>'s <c>dt</c> parameter:
        /// switches Delta T back to being computed automatically instead of using a fixed
        /// override.</summary>
        public const double SE_DELTAT_AUTOMATIC = (-1E-10);

        /// <summary><c>model_number</c> selector for <see cref="swe_set_astro_models"/>/
        /// <see cref="swe_get_astro_models"/>: Delta T model. See the <c>SEMOD_DELTAT_*</c>
        /// constants.</summary>
        public const int SE_MODEL_DELTAT = 0;
        /// <summary><c>model_number</c> selector for <see cref="swe_set_astro_models"/>/
        /// <see cref="swe_get_astro_models"/>: long-term precession model. See the
        /// <c>SEMOD_PREC_*</c> constants.</summary>
        public const int SE_MODEL_PREC_LONGTERM = 1;
        /// <summary><c>model_number</c> selector for <see cref="swe_set_astro_models"/>/
        /// <see cref="swe_get_astro_models"/>: short-term precession model. See the
        /// <c>SEMOD_PREC_*</c> constants.</summary>
        public const int SE_MODEL_PREC_SHORTTERM = 2;
        /// <summary><c>model_number</c> selector for <see cref="swe_set_astro_models"/>/
        /// <see cref="swe_get_astro_models"/>: nutation model. See the <c>SEMOD_NUT_*</c>
        /// constants.</summary>
        public const int SE_MODEL_NUT = 3;
        /// <summary><c>model_number</c> selector for <see cref="swe_set_astro_models"/>/
        /// <see cref="swe_get_astro_models"/>: frame bias model. See the <c>SEMOD_BIAS_*</c>
        /// constants.</summary>
        public const int SE_MODEL_BIAS = 4;
        /// <summary><c>model_number</c> selector for <see cref="swe_set_astro_models"/>/
        /// <see cref="swe_get_astro_models"/>: JPL Horizons mode. See the <c>SEMOD_JPLHOR_*</c>
        /// constants.</summary>
        public const int SE_MODEL_JPLHOR_MODE = 5;
        /// <summary><c>model_number</c> selector for <see cref="swe_set_astro_models"/>/
        /// <see cref="swe_get_astro_models"/>: JPL Horizons approximation mode. See the
        /// <c>SEMOD_JPLHORA_*</c> constants.</summary>
        public const int SE_MODEL_JPLHORA_MODE = 6;
        /// <summary><c>model_number</c> selector for <see cref="swe_set_astro_models"/>/
        /// <see cref="swe_get_astro_models"/>: sidereal time model. See the <c>SEMOD_SIDT_*</c>
        /// constants.</summary>
        public const int SE_MODEL_SIDT = 7;
        /// <summary>Count of <c>SE_MODEL_*</c> selectors.</summary>
        public const int NSE_MODELS = 8;

        /* precession models: SEMOD_NPREC is the count of choices below; each SEMOD_PREC_* is a
         * precession model choice for SE_MODEL_PREC_LONGTERM/SE_MODEL_PREC_SHORTTERM (see
         * <see cref="swe_set_astro_models"/>); SEMOD_PREC_DEFAULT/_DEFAULT_SHORT alias the current
         * defaults. */
        /// <summary>Count of <c>SEMOD_PREC_*</c> precession model choices.</summary>
        public const int SEMOD_NPREC = 11;
        /// <summary>Precession model: IAU 1976.</summary>
        public const int SEMOD_PREC_IAU_1976 = 1;
        /// <summary>Precession model: Laskar 1986.</summary>
        public const int SEMOD_PREC_LASKAR_1986 = 2;
        /// <summary>Precession model: Williams 1994 obliquity combined with Laskar 1986
        /// precession.</summary>
        public const int SEMOD_PREC_WILL_EPS_LASK = 3;
        /// <summary>Precession model: Williams et al. 1994.</summary>
        public const int SEMOD_PREC_WILLIAMS_1994 = 4;
        /// <summary>Precession model: Simon et al. 1994.</summary>
        public const int SEMOD_PREC_SIMON_1994 = 5;
        /// <summary>Precession model: IAU 2000.</summary>
        public const int SEMOD_PREC_IAU_2000 = 6;
        /// <summary>Precession model: Bretagnon 2003.</summary>
        public const int SEMOD_PREC_BRETAGNON_2003 = 7;
        /// <summary>Precession model: IAU 2006.</summary>
        public const int SEMOD_PREC_IAU_2006 = 8;
        /// <summary>Precession model: Vondrak et al. 2011.</summary>
        public const int SEMOD_PREC_VONDRAK_2011 = 9;
        /// <summary>Precession model: Owen 1990.</summary>
        public const int SEMOD_PREC_OWEN_1990 = 10;
        /// <summary>Precession model: Newcomb.</summary>
        public const int SEMOD_PREC_NEWCOMB = 11;
        /// <summary>The current default long-term precession model, currently
        /// <see cref="SEMOD_PREC_VONDRAK_2011"/>.</summary>
        public const int SEMOD_PREC_DEFAULT = SEMOD_PREC_VONDRAK_2011;
        /* SE versions before 1.70 used IAU 1976 precession for
         * a limited time range of 2 centuries in combination with
         * the long-term precession Simon 1994.
         */
        /// <summary>The current default short-term precession model, currently
        /// <see cref="SEMOD_PREC_VONDRAK_2011"/>. Versions before 1.70 used IAU 1976 precession
        /// for a limited 2-century range combined with the long-term Simon 1994 model.</summary>
        public const int SEMOD_PREC_DEFAULT_SHORT = SEMOD_PREC_VONDRAK_2011;

        /* nutation models: SEMOD_NNUT is the count of choices below; each SEMOD_NUT_* is a nutation
         * model choice for SE_MODEL_NUT (see <see cref="swe_set_astro_models"/>); SEMOD_NUT_DEFAULT
         * aliases the current default. */
        /// <summary>Count of <c>SEMOD_NUT_*</c> nutation model choices.</summary>
        public const int SEMOD_NNUT = 5;
        /// <summary>Nutation model: IAU 1980.</summary>
        public const int SEMOD_NUT_IAU_1980 = 1;
        /// <summary>Nutation model: IAU 1980 with Herring's (1987) corrections to the nutation
        /// series. AA (1996) neglects them.</summary>
        public const int SEMOD_NUT_IAU_CORR_1987 = 2; /* Herring's (1987) corrections to IAU 1980
                            * nutation series. AA (1996) neglects them.*/
        /// <summary>Nutation model: IAU 2000A. Very time consuming.</summary>
        public const int SEMOD_NUT_IAU_2000A = 3; /* very time consuming ! */
        /// <summary>Nutation model: IAU 2000B. Fast, but precision of only milli-arcsec.</summary>
        public const int SEMOD_NUT_IAU_2000B = 4; /* fast, but precision of milli-arcsec */
        /// <summary>Nutation model: Woolard.</summary>
        public const int SEMOD_NUT_WOOLARD = 5;
        /// <summary>The current default nutation model, currently
        /// <see cref="SEMOD_NUT_IAU_2000B"/> (fast, but precision of milli-arcsec).</summary>
        public const int SEMOD_NUT_DEFAULT = SEMOD_NUT_IAU_2000B;  /* fast, but precision of milli-arcsec */

        /* methods for sidereal time: SEMOD_NSIDT is the count of choices below; each SEMOD_SIDT_* is
         * a sidereal time model choice for SE_MODEL_SIDT (see <see cref="swe_set_astro_models"/>);
         * SEMOD_SIDT_DEFAULT aliases the current default. */
        /// <summary>Count of <c>SEMOD_SIDT_*</c> sidereal time model choices.</summary>
        public const int SEMOD_NSIDT = 4;
        /// <summary>Sidereal time model: IAU 1976.</summary>
        public const int SEMOD_SIDT_IAU_1976 = 1;
        /// <summary>Sidereal time model: IAU 2006.</summary>
        public const int SEMOD_SIDT_IAU_2006 = 2;
        /// <summary>Sidereal time model: IERS Conventions 2010.</summary>
        public const int SEMOD_SIDT_IERS_CONV_2010 = 3;
        /// <summary>Sidereal time model: long-term.</summary>
        public const int SEMOD_SIDT_LONGTERM = 4;
        /// <summary>The current default sidereal time model, currently
        /// <see cref="SEMOD_SIDT_LONGTERM"/>.</summary>
        public const int SEMOD_SIDT_DEFAULT = SEMOD_SIDT_LONGTERM;
        //#define SEMOD_SIDT_DEFAULT          SEMOD_SIDT_IERS_CONV_2010

        /* frame bias methods: SEMOD_NBIAS is the count of choices below; each SEMOD_BIAS_* is a
         * frame bias model choice for SE_MODEL_BIAS (see <see cref="swe_set_astro_models"/>);
         * SEMOD_BIAS_DEFAULT aliases the current default. */
        /// <summary>Count of <c>SEMOD_BIAS_*</c> frame bias model choices.</summary>
        public const int SEMOD_NBIAS = 3;
        /// <summary>Frame bias model: ignore frame bias.</summary>
        public const int SEMOD_BIAS_NONE = 1;  /* ignore frame bias */
        /// <summary>Frame bias model: use the IAU 2000 frame bias matrix.</summary>
        public const int SEMOD_BIAS_IAU2000 = 2;  /* use frame bias matrix IAU 2000 */
        /// <summary>Frame bias model: use the IAU 2006 frame bias matrix.</summary>
        public const int SEMOD_BIAS_IAU2006 = 3;  /* use frame bias matrix IAU 2006 */
        /// <summary>The current default frame bias model, currently
        /// <see cref="SEMOD_BIAS_IAU2006"/>.</summary>
        public const int SEMOD_BIAS_DEFAULT = SEMOD_BIAS_IAU2006;

        /* methods of JPL Horizons (iflag & SEFLG_JPLHOR),
         * using daily dpsi, deps;  see explanations below */
        /// <summary>Count of <c>SEMOD_JPLHOR_*</c> model choices for
        /// <see cref="SEFLG_JPLHOR"/>.</summary>
        public const int SEMOD_NJPLHOR = 2;
        /// <summary>JPL Horizons agreement model using daily dpsi/deps: dpsi and deps from file are
        /// limited to 1962-today; JPL uses the first/last value for dates beyond this range.
        /// Currently the only option for <see cref="SEMOD_NJPLHOR"/>.</summary>
        public const int SEMOD_JPLHOR_LONG_AGREEMENT = 1;  /* daily dpsi and deps from file are
                                             * limited to 1962 - today. JPL uses the
                             * first and last value for all  dates
                             * beyond this time range. */
        /// <summary>The current default JPL Horizons agreement model, currently
        /// <see cref="SEMOD_JPLHOR_LONG_AGREEMENT"/>.</summary>
        public const int SEMOD_JPLHOR_DEFAULT = SEMOD_JPLHOR_LONG_AGREEMENT;
        /* Note, currently this is the only option for SEMOD_JPLHOR..*/
        /* SEMOD_JPLHOR_LONG_AGREEMENT, if combined with SEFLG_JPLHOR provides good 
         * agreement with JPL Horizons for 9998 BC (-9997) until 9999 CE. 
         * - After 20-jan-1962 until today, Horizons uses correct dpsi and deps. 
         * - For dates before that, it uses dpsi and deps of 20-jan-1962, which 
         *   provides a continuous ephemeris, but does not make sense otherwise.
         * - Before 1.1.1799 and after 1.1.2202, the precession model Owen 1990
         *   is used, as in Horizons. 
         * An agreement with Horizons to a couple of milli arc seconds is achieved 
         * for the whole time range of Horizons. (BC 9998-Mar-20 to AD 9999-Dec-31 TT.)
         */

        /* methods of approximation of JPL Horizons (iflag & SEFLG_JPLHORA),
         * without dpsi, deps; see explanations below */
        /// <summary>Count of <c>SEMOD_JPLHORA_*</c> model choices for
        /// <see cref="SEFLG_JPLHOR_APPROX"/>.</summary>
        public const int SEMOD_NJPLHORA = 3;
        /// <summary>JPL Horizons approximation model 1: always uses a recent precession/nutation
        /// model, with the frame bias matrix applied via a correction to RA and to epsilon; a very
        /// good approximation of JPL Horizons positions.</summary>
        public const int SEMOD_JPLHORA_1 = 1;
        /// <summary>JPL Horizons approximation model 2: frame bias per IERS Conventions 2003/2010 is
        /// not applied; instead dpsi_bias/deps_bias are added to nutation (an older approach).
        /// Equatorial positions are close to JPL Horizons between 1962 and current years; ecliptic
        /// longitude is good, latitude is not.</summary>
        public const int SEMOD_JPLHORA_2 = 2;
        /// <summary>JPL Horizons approximation model 3: works like model 1 after 1962, and like
        /// <see cref="SEFLG_JPLHOR"/> before that, giving extremely good agreement with JPL Horizons
        /// over its whole time range.</summary>
        public const int SEMOD_JPLHORA_3 = 3;
        /// <summary>The current default JPL Horizons approximation model, currently
        /// <see cref="SEMOD_JPLHORA_3"/>.</summary>
        public const int SEMOD_JPLHORA_DEFAULT = SEMOD_JPLHORA_3;

        /* With SEMOD_JPLHORA_1, planetary positions are always calculated 
         * using a recent precession/nutation model. Frame bias matrix is applied 
         * with some correction to RA and another correction added to epsilon.
         * This provides a very good approximation of JPL Horizons positions. 
         *
         * With SEMOD_JPLHORA_2, frame bias as recommended by IERS Conventions 2003 
         * and 2010 is *not* applied. Instead, dpsi_bias and deps_bias are added to 
         * nutation. This procedure is found in some older astronomical software.
         * Equatorial apparent positions will be close to JPL Horizons 
         * (within a few mas) between 1962 and current years. Ecl. longitude 
         * will be good, latitude bad. 
         *
         * With SEMOD_JPLHORA_3 works like SEMOD_JPLHORA_3 after 1962, but like
         * SEFLG_JPLHOR before that. This allows EXTREMELY good agreement with JPL 
         * Horizons over its whole time range.
         */

        /* Delta T models: SEMOD_NDELTAT is the count of choices below; each SEMOD_DELTAT_* is a
         * Delta T model choice for SE_MODEL_DELTAT (see <see cref="swe_set_astro_models"/> and
         * <see cref="swe_deltat(double)"/>); SEMOD_DELTAT_DEFAULT aliases the current default. */
        /// <summary>Count of <c>SEMOD_DELTAT_*</c> Delta T model choices.</summary>
        public const int SEMOD_NDELTAT = 5;
        /// <summary>Delta T model: Stephenson and Morrison 1984.</summary>
        public const int SEMOD_DELTAT_STEPHENSON_MORRISON_1984 = 1;
        /// <summary>Delta T model: Stephenson 1997.</summary>
        public const int SEMOD_DELTAT_STEPHENSON_1997 = 2;
        /// <summary>Delta T model: Stephenson and Morrison 2004.</summary>
        public const int SEMOD_DELTAT_STEPHENSON_MORRISON_2004 = 3;
        /// <summary>Delta T model: Espenak and Meeus 2006.</summary>
        public const int SEMOD_DELTAT_ESPENAK_MEEUS_2006 = 4;
        /// <summary>Delta T model: Stephenson et al. 2016.</summary>
        public const int SEMOD_DELTAT_STEPHENSON_ETC_2016 = 5;
        //#define SEMOD_DELTAT_DEFAULT   SEMOD_DELTAT_ESPENAK_MEEUS_2006
        /// <summary>The current default Delta T model, currently
        /// <see cref="SEMOD_DELTAT_STEPHENSON_ETC_2016"/>.</summary>
        public const int SEMOD_DELTAT_DEFAULT = SEMOD_DELTAT_STEPHENSON_ETC_2016;

        /// <summary>
        /// 2000 January 1.5
        /// </summary>
        public const double J2000 = 2451545.0;

        /***********************************************************
         * exported functions
         ***********************************************************/

        /// <summary>
        /// Searches forward from <paramref name="tjdstart_ut"/> for the heliacal rising or setting
        /// (or evening/morning first/last visibility) of a planet, the Moon, or a fixed star, given
        /// atmospheric and observer conditions.
        /// </summary>
        /// <param name="tjdstart_ut">Julian day (UT) to start the forward search from.</param>
        /// <param name="geopos">Observer's geographic position: <c>[0]</c> longitude (deg, east
        /// positive), <c>[1]</c> latitude (deg, north positive), <c>[2]</c> height above sea level
        /// (m).</param>
        /// <param name="datm">Atmospheric conditions: <c>[0]</c> pressure (hPa/mbar), <c>[1]</c>
        /// temperature (deg C), <c>[2]</c> relative humidity (%), <c>[3]</c> meteorological
        /// (horizontal) visibility (km).</param>
        /// <param name="dobs">Observer conditions: <c>[0]</c> age (years), <c>[1]</c> visual acuity
        /// relative to normal (1.0 = normal, or a Snellen fraction); remaining elements are
        /// telescope parameters (magnification, aperture in mm, optical transmission), used only if
        /// the corresponding <c>SE_HELFLAG_*</c> bit selects an optical aid.</param>
        /// <param name="ObjectName">Planet or fixed-star name, or a numeric string identifying a
        /// planet number.</param>
        /// <param name="TypeEvent">Event type to search for: 1 = morning first
        /// (<see cref="SE_MORNING_FIRST"/>), 2 = evening last (<see cref="SE_EVENING_LAST"/>),
        /// 3 = evening first (<see cref="SE_EVENING_FIRST"/>), 4 = morning last
        /// (<see cref="SE_MORNING_LAST"/>).</param>
        /// <param name="iflag">Ephemeris/computation flags as in <see cref="swe_calc"/>
        /// (e.g. <see cref="SEFLG_SWIEPH"/>).</param>
        /// <param name="dret">Receives event data; <c>dret[0]</c> is the found Julian day (UT) of
        /// the event.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise
        /// unchanged.</param>
        /// <returns><see cref="SwissEph.OK"/> on success; a negative value
        /// (<see cref="SwissEph.ERR"/>) if no event is found or on error, with a message in
        /// <paramref name="serr"/>.</returns>
        public Int32 swe_heliacal_ut(double tjdstart_ut, double[] geopos, double[] datm, double[] dobs, string ObjectName,
            Int32 TypeEvent, Int32 iflag, double[] dret, ref string serr)
        {
            return SweHel.swe_heliacal_ut(tjdstart_ut, geopos, datm, dobs, ObjectName, TypeEvent, iflag, dret, ref serr);
        }

        /// <summary>
        /// Like <see cref="swe_heliacal_ut"/>, but instead of searching for the event, reports the
        /// visibility phenomenon data (altitudes, magnitudes, angular distances) at the given
        /// <paramref name="tjd_ut"/>.
        /// </summary>
        /// <param name="tjd_ut">Julian day (UT) at which to evaluate the phenomenon.</param>
        /// <param name="geopos">Observer's geographic position: <c>[0]</c> longitude (deg, east
        /// positive), <c>[1]</c> latitude (deg, north positive), <c>[2]</c> height above sea level
        /// (m).</param>
        /// <param name="datm">Atmospheric conditions: <c>[0]</c> pressure (hPa/mbar), <c>[1]</c>
        /// temperature (deg C), <c>[2]</c> relative humidity (%), <c>[3]</c> meteorological
        /// (horizontal) visibility (km).</param>
        /// <param name="dobs">Observer conditions: <c>[0]</c> age (years), <c>[1]</c> visual acuity
        /// relative to normal (1.0 = normal, or a Snellen fraction); remaining elements are
        /// telescope parameters, used only if the corresponding <c>SE_HELFLAG_*</c> bit selects an
        /// optical aid.</param>
        /// <param name="ObjectName">Planet or fixed-star name, or a numeric string identifying a
        /// planet number.</param>
        /// <param name="TypeEvent">Event type, as in <see cref="swe_heliacal_ut"/>: 1 = morning
        /// first, 2 = evening last, 3 = evening first, 4 = morning last.</param>
        /// <param name="helflag">Heliacal calculation flags (<c>SE_HELFLAG_*</c>).</param>
        /// <param name="darr">Receives the phenomenon data.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise
        /// unchanged.</param>
        /// <returns><see cref="SwissEph.OK"/> on success; a negative value
        /// (<see cref="SwissEph.ERR"/>) on error, with a message in <paramref name="serr"/>.</returns>
        public Int32 swe_heliacal_pheno_ut(double tjd_ut, double[] geopos, double[] datm, double[] dobs, string ObjectName,
            Int32 TypeEvent, Int32 helflag, double[] darr, ref string serr)
        {
            return SweHel.swe_heliacal_pheno_ut(tjd_ut, geopos, datm, dobs, ObjectName, TypeEvent, helflag, darr, ref serr);
        }
        /// <summary>
        /// Computes the limiting (threshold) visual magnitude at which an object would just be
        /// visible under the given atmospheric/observer conditions, together with the object's
        /// actual magnitude, at <paramref name="tjdut"/>.
        /// </summary>
        /// <param name="tjdut">Julian day (UT) at which to evaluate visibility.</param>
        /// <param name="geopos">Observer's geographic position: <c>[0]</c> longitude (deg, east
        /// positive), <c>[1]</c> latitude (deg, north positive), <c>[2]</c> height above sea level
        /// (m).</param>
        /// <param name="datm">Atmospheric conditions: <c>[0]</c> pressure (hPa/mbar), <c>[1]</c>
        /// temperature (deg C), <c>[2]</c> relative humidity (%), <c>[3]</c> meteorological
        /// (horizontal) visibility (km).</param>
        /// <param name="dobs">Observer conditions: <c>[0]</c> age (years), <c>[1]</c> visual acuity
        /// relative to normal (1.0 = normal, or a Snellen fraction); remaining elements are
        /// telescope parameters, used only if the corresponding <c>SE_HELFLAG_*</c> bit selects an
        /// optical aid.</param>
        /// <param name="ObjectName">Planet or fixed-star name, or a numeric string identifying a
        /// planet number.</param>
        /// <param name="helflag">Heliacal calculation flags (<c>SE_HELFLAG_*</c>).</param>
        /// <param name="dret">Receives the result: <c>dret[0]</c> is the limiting magnitude; further
        /// elements carry the object's own magnitude/altitude/extinction details.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise
        /// unchanged.</param>
        /// <returns>A negative value if the object is below the horizon (or another error occurred),
        /// with a code identifying why; otherwise a non-negative visibility indicator.</returns>
        public Int32 swe_vis_limit_mag(double tjdut, double[] geopos, double[] datm, double[] dobs, string ObjectName,
            Int32 helflag, double[] dret, ref string serr)
        {
            return SweHel.swe_vis_limit_mag(tjdut, geopos, datm, dobs, ObjectName, helflag, dret, ref serr);
        }
        /* the following are secret, for Victor Reijs' */
        /// <summary>
        /// Low-level building block behind the heliacal-visibility functions above (upstream marks
        /// this and <see cref="swe_topo_arcus_visionis"/> "secret, for Victor Reijs'" -- not
        /// documented by Astrodienst for general use, and not intended for typical callers). Uses the
        /// same <paramref name="dgeo"/>/<paramref name="datm"/>/<paramref name="dobs"/>/
        /// <paramref name="helflag"/> conventions as <see cref="swe_heliacal_ut"/>.
        /// </summary>
        /// <param name="tjdut">Julian day (UT) at which to evaluate.</param>
        /// <param name="dgeo">Observer's geographic position (longitude, latitude, height), as in
        /// <see cref="swe_heliacal_ut"/>.</param>
        /// <param name="datm">Atmospheric conditions, as in <see cref="swe_heliacal_ut"/>.</param>
        /// <param name="dobs">Observer conditions, as in <see cref="swe_heliacal_ut"/>.</param>
        /// <param name="helflag">Heliacal calculation flags (<c>SE_HELFLAG_*</c>).</param>
        /// <param name="mag">Magnitude of the object.</param>
        /// <param name="azi_obj">Azimuth of the object (deg).</param>
        /// <param name="azi_sun">Azimuth of the Sun (deg).</param>
        /// <param name="azi_moon">Azimuth of the Moon (deg).</param>
        /// <param name="alt_moon">Altitude of the Moon (deg).</param>
        /// <param name="dret">Receives the computed result data.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise
        /// unchanged.</param>
        /// <returns><see cref="SwissEph.OK"/> on success; a negative value
        /// (<see cref="SwissEph.ERR"/>) on error, with a message in <paramref name="serr"/>.</returns>
        public Int32 swe_heliacal_angle(double tjdut, double[] dgeo, double[] datm, double[] dobs, Int32 helflag, double mag,
            double azi_obj, double azi_sun, double azi_moon, double alt_moon, double[] dret, ref string serr)
        {
            return SweHel.swe_heliacal_angle(tjdut, dgeo, datm, dobs, helflag, mag, azi_obj, azi_sun, azi_moon, alt_moon, dret, ref serr);
        }
        /// <summary>
        /// Low-level building block behind the heliacal-visibility functions above: computes the
        /// arcus visionis (the angular altitude difference threshold for first/last visibility) for
        /// the topocentric observer (upstream marks this and <see cref="swe_heliacal_angle"/>
        /// "secret, for Victor Reijs'" -- not documented by Astrodienst for general use, and not
        /// intended for typical callers).
        /// </summary>
        /// <param name="tjdut">Julian day (UT) at which to evaluate.</param>
        /// <param name="dgeo">Observer's geographic position (longitude, latitude, height), as in
        /// <see cref="swe_heliacal_ut"/>.</param>
        /// <param name="datm">Atmospheric conditions, as in <see cref="swe_heliacal_ut"/>.</param>
        /// <param name="dobs">Observer conditions, as in <see cref="swe_heliacal_ut"/>.</param>
        /// <param name="helflag">Heliacal calculation flags (<c>SE_HELFLAG_*</c>).</param>
        /// <param name="mag">Magnitude of the object.</param>
        /// <param name="azi_obj">Azimuth of the object (deg).</param>
        /// <param name="alt_obj">Altitude of the object (deg).</param>
        /// <param name="azi_sun">Azimuth of the Sun (deg).</param>
        /// <param name="azi_moon">Azimuth of the Moon (deg).</param>
        /// <param name="alt_moon">Altitude of the Moon (deg).</param>
        /// <param name="dret">Receives the computed arcus visionis result.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise
        /// unchanged.</param>
        /// <returns><see cref="SwissEph.OK"/> on success; a negative value
        /// (<see cref="SwissEph.ERR"/>) on error, with a message in <paramref name="serr"/>.</returns>
        public Int32 swe_topo_arcus_visionis(double tjdut, double[] dgeo, double[] datm, double[] dobs, Int32 helflag, double mag,
            double azi_obj, double alt_obj, double azi_sun, double azi_moon, double alt_moon, ref double dret, ref string serr)
        {
            return SweHel.swe_topo_arcus_visionis(tjdut, dgeo, datm, dobs, helflag, mag, azi_obj, alt_obj, azi_sun, azi_moon, alt_moon, ref dret, ref serr);
        }

        //*DllImport int32 FAR PASCAL HeliacalAngle(double Magn, double Age, int SN, double AziO, double AltM, double AziM, double JDNDaysUT, double AziS, double Lat, double HeightEye, double Temperature, double Pressure, double RH, double VR, double *dangret, char *serr);

        //DllImport int32 FAR PASCAL HeliacalJDut(double JDNDaysUTStart, double Age, int SN, double Lat, double Longitude, double HeightEye, double Temperature, double Pressure, double RH, double VR, char *ObjectName, int TypeEvent, char *AVkind, double *dret, char *serr);*/

        /// <summary>
        /// Selects which historical precession/nutation/sidereal-time/bias model set Swiss Ephemeris
        /// uses internally, for reproducing older library versions' output (upstream marks this and
        /// <see cref="swe_get_astro_models"/> "secret, for Dieter, allows to test old models of
        /// precession, nutation, etc." -- not documented by Astrodienst for general use; search for
        /// <c>SE_MODEL_...</c> in this file). Not meant for routine use.
        /// </summary>
        /// <param name="samod">Either a comma-separated list of model numbers, or a version string
        /// like <c>"SE2.05"</c> selecting that version's defaults; <c>""</c> or <c>null</c> selects
        /// the current library's defaults.</param>
        /// <param name="iflag">Ephemeris flag as in <see cref="swe_calc"/>, used to pick which model
        /// group (e.g. sidereal vs. tropical) is affected.</param>
        /* the following is secret, for Dieter, allows to test old models of
         * precession, nutation, etc. Search for SE_MODEL_... in this file */
        public void swe_set_astro_models(string samod, Int32 iflag)
        {
            SwephLib.swe_set_astro_models(samod, iflag);
        }
        /// <summary>
        /// Reports which historical precession/nutation/sidereal-time/bias model set Swiss Ephemeris
        /// currently uses internally (upstream marks this and <see cref="swe_set_astro_models"/>
        /// "secret, for Dieter" -- not documented by Astrodienst for general use). Not meant for
        /// routine use.
        /// </summary>
        /// <param name="samod">Either a comma-separated list of model numbers, or a version string
        /// like <c>"SE2.05"</c>, or <c>""</c>/<c>null</c> for the current library's defaults; same
        /// meaning as in <see cref="swe_set_astro_models"/>.</param>
        /// <param name="sdet">Receives a human-readable description of the currently active model
        /// set.</param>
        /// <param name="iflag">Ephemeris flag as in <see cref="swe_calc"/>, used to pick which model
        /// group is reported.</param>
        public void swe_get_astro_models(string samod, out string sdet, Int32 iflag)
        {
            SwephLib.swe_get_astro_models(samod, out sdet, iflag);
        }

        /**************************** 
         * exports from sweph.c 
         ****************************/

        /// <summary>
        /// Returns the Swiss Ephemeris library version string this port implements
        /// (see the <c>SE_VERSION</c> constant).
        /// </summary>
        /// <returns>The version string, e.g. <c>"2.10.03"</c>.</returns>
        public string swe_version() { return Sweph.swe_version(); }
        /// <summary>
        /// Returns the file-system path of the currently loaded/running library assembly.
        /// Informational only -- this is not the ephemeris data path (see
        /// <see cref="swe_set_ephe_path(string)"/> for that).
        /// </summary>
        /// <returns>The path of the running library assembly.</returns>
        public string swe_get_library_path() { return Sweph.swe_get_library_path(); }

        /// <summary>
        /// Version for DotNet portage. This is .NET-specific and not part of the upstream C API.
        /// </summary>
        /// <remarks>
        /// DotNet version is the same than the SwissEph version. So we use only Revision part for our version.
        /// </remarks>
        /// <returns>A version string of the form <c>"{major}.{minor:D2}.{build:D2}-net-{revision:D4}"</c>,
        /// where the revision segment carries the fork's own revision.</returns>
        public string swe_dotnet_version()
        {
            var vrs = new System.Reflection.AssemblyName(typeof(SwissEph).GetAssembly().FullName).Version;
            return String.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}.{1:D2}.{2:D2}-net-{3:D4}", vrs.Major, vrs.Minor, vrs.Build, vrs.Revision);
        }

        /// <summary>
        /// Computes the position (and, if requested, speed) of a planet, the Moon, a lunar node or
        /// apogee, or an asteroid, for Ephemeris/Terrestrial Time <paramref name="tjd"/>.
        /// </summary>
        /// <param name="tjd">Julian day number, Ephemeris/Terrestrial Time (ET/TT).</param>
        /// <param name="ipl">Body number, e.g. one of the <c>SE_SUN</c>..<c>SE_PLUTO</c>,
        /// <c>SE_MEAN_NODE</c>, <c>SE_TRUE_NODE</c>, <c>SE_MEAN_APOG</c>, <c>SE_OSCU_APOG</c>,
        /// <c>SE_EARTH</c>, <c>SE_CHIRON</c> constants, or <see cref="SE_AST_OFFSET"/> + n for
        /// asteroid n.</param>
        /// <param name="iflag">Bitfield selecting the ephemeris backend
        /// (<see cref="SEFLG_JPLEPH"/>/<see cref="SEFLG_SWIEPH"/>/<see cref="SEFLG_MOSEPH"/>),
        /// coordinate system (<see cref="SEFLG_EQUATORIAL"/>, <see cref="SEFLG_XYZ"/>,
        /// <see cref="SEFLG_RADIANS"/>), reference frame (<see cref="SEFLG_HELCTR"/>,
        /// <see cref="SEFLG_BARYCTR"/>, <see cref="SEFLG_TOPOCTR"/>, <see cref="SEFLG_SIDEREAL"/>,
        /// <see cref="SEFLG_J2000"/>, <see cref="SEFLG_TRUEPOS"/>, <see cref="SEFLG_NONUT"/>), and
        /// <see cref="SEFLG_SPEED"/> to also compute speed.</param>
        /// <param name="xx">Output array of at least 6 doubles: <c>[0]</c> longitude in degrees (or
        /// right ascension if equatorial), <c>[1]</c> latitude (or declination), <c>[2]</c> distance
        /// (AU), <c>[3..5]</c> daily speed of each, valid only if <see cref="SEFLG_SPEED"/> was
        /// requested.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise
        /// unchanged.</param>
        /// <returns>The actually-used flag bits (non-negative) on success -- a partial/fallback
        /// condition can return a value with some requested flag bits missing (e.g. sidereal not
        /// honored), still non-negative -- or a negative value (<see cref="SwissEph.ERR"/>) on a
        /// fatal error, in which case <paramref name="xx"/> is all zero and the message is in
        /// <paramref name="serr"/>.</returns>
        public Int32 swe_calc(double tjd, int ipl, Int32 iflag, double[] xx, ref string serr)
        {
            return Sweph.swe_calc(tjd, ipl, iflag, xx, ref serr);
        }

        /// <summary>
        /// Identical to <see cref="swe_calc"/>, but <paramref name="tjd_ut"/> is Universal Time
        /// rather than ET/TT.
        /// </summary>
        /// <param name="tjd_ut">Julian day number, Universal Time (UT).</param>
        /// <param name="ipl">Body number, as in <see cref="swe_calc"/>.</param>
        /// <param name="iflag">Bitfield of computation flags, as in <see cref="swe_calc"/>.</param>
        /// <param name="xx">Output array, populated as in <see cref="swe_calc"/>.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise
        /// unchanged.</param>
        /// <returns>Same meaning as <see cref="swe_calc"/>'s return value.</returns>
        public Int32 swe_calc_ut(double tjd_ut, Int32 ipl, Int32 iflag, double[] xx, ref string serr)
        {
            return Sweph.swe_calc_ut(tjd_ut, ipl, iflag, xx, ref serr);
        }

        /// <summary>
        /// Like <see cref="swe_calc"/>, but the returned position of <paramref name="ipl"/> is
        /// relative to another body <paramref name="iplctr"/> (planetocentric), rather than the
        /// Sun/Earth/barycenter implied by <paramref name="iflag"/>.
        /// </summary>
        /// <param name="tjd">Julian day number, Ephemeris/Terrestrial Time (ET/TT).</param>
        /// <param name="ipl">Body number whose position is computed, as in <see cref="swe_calc"/>.</param>
        /// <param name="iplctr">Body number of the center body the position is computed relative
        /// to.</param>
        /// <param name="iflag">Bitfield of computation flags, as in <see cref="swe_calc"/>.</param>
        /// <param name="xxret">Output array, populated the same way <c>xx</c> is in
        /// <see cref="swe_calc"/>.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise
        /// unchanged.</param>
        /// <returns>Same meaning as <see cref="swe_calc"/>'s return value.</returns>
        public Int32 swe_calc_pctr(double tjd, Int32 ipl, Int32 iplctr, Int32 iflag, double[] xxret, ref string serr)
        {
            return Sweph.swe_calc_pctr(tjd, ipl, iplctr, iflag, xxret, ref serr);
        }

        // sweph.c:8310-8615
        /// <summary>
        /// Finds the next time (from <paramref name="jd_et"/> forward, forward search only) at which
        /// the Sun's ecliptic longitude crosses <paramref name="x2cross"/> degrees.
        /// </summary>
        /// <param name="x2cross">Ecliptic longitude to search for, in degrees.</param>
        /// <param name="jd_et">Julian day (Ephemeris/Terrestrial Time) to start the search from.</param>
        /// <param name="flag">Ephemeris/computation flags as in <see cref="swe_calc"/>; only a
        /// subset are meaningful here (e.g. <see cref="SEFLG_SWIEPH"/>, <see cref="SEFLG_NONUT"/>).</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise
        /// unchanged.</param>
        /// <returns>The crossing time as a Julian day (ET). A value less than
        /// <paramref name="jd_et"/> (or an error message in <paramref name="serr"/>) signals
        /// failure.</returns>
        public double swe_solcross(double x2cross, double jd_et, Int32 flag, ref string serr)
        {
            return Sweph.swe_solcross(x2cross, jd_et, flag, ref serr);
        }
        /// <summary>
        /// Same as <see cref="swe_solcross"/>, but <paramref name="jd_ut"/> and the return value are
        /// in Universal Time rather than ET.
        /// </summary>
        /// <param name="x2cross">Ecliptic longitude to search for, in degrees.</param>
        /// <param name="jd_ut">Julian day (Universal Time) to start the search from.</param>
        /// <param name="flag">Ephemeris/computation flags, as in <see cref="swe_solcross"/>.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise
        /// unchanged.</param>
        /// <returns>The crossing time as a Julian day (UT). A value less than
        /// <paramref name="jd_ut"/> (or an error message in <paramref name="serr"/>) signals
        /// failure.</returns>
        public double swe_solcross_ut(double x2cross, double jd_ut, Int32 flag, ref string serr)
        {
            return Sweph.swe_solcross_ut(x2cross, jd_ut, flag, ref serr);
        }
        /// <summary>
        /// Same idea as <see cref="swe_solcross"/>, but for the Moon's ecliptic longitude: finds the
        /// next time (from <paramref name="jd_et"/> forward) it crosses <paramref name="x2cross"/>
        /// degrees.
        /// </summary>
        /// <param name="x2cross">Ecliptic longitude to search for, in degrees.</param>
        /// <param name="jd_et">Julian day (Ephemeris/Terrestrial Time) to start the search from.</param>
        /// <param name="flag">Ephemeris/computation flags, as in <see cref="swe_solcross"/>.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise
        /// unchanged.</param>
        /// <returns>The crossing time as a Julian day (ET). A value less than
        /// <paramref name="jd_et"/> (or an error message in <paramref name="serr"/>) signals
        /// failure.</returns>
        public double swe_mooncross(double x2cross, double jd_et, Int32 flag, ref string serr)
        {
            return Sweph.swe_mooncross(x2cross, jd_et, flag, ref serr);
        }
        /// <summary>
        /// Same as <see cref="swe_mooncross"/>, but <paramref name="jd_ut"/> and the return value are
        /// in Universal Time rather than ET.
        /// </summary>
        /// <param name="x2cross">Ecliptic longitude to search for, in degrees.</param>
        /// <param name="jd_ut">Julian day (Universal Time) to start the search from.</param>
        /// <param name="flag">Ephemeris/computation flags, as in <see cref="swe_solcross"/>.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise
        /// unchanged.</param>
        /// <returns>The crossing time as a Julian day (UT). A value less than
        /// <paramref name="jd_ut"/> (or an error message in <paramref name="serr"/>) signals
        /// failure.</returns>
        public double swe_mooncross_ut(double x2cross, double jd_ut, Int32 flag, ref string serr)
        {
            return Sweph.swe_mooncross_ut(x2cross, jd_ut, flag, ref serr);
        }
        /// <summary>
        /// Finds the next time (from <paramref name="jd_et"/> forward) the Moon crosses its own
        /// (true) orbital node -- i.e. crosses the ecliptic plane.
        /// </summary>
        /// <param name="jd_et">Julian day (Ephemeris/Terrestrial Time) to start the search from.</param>
        /// <param name="flag">Ephemeris/computation flags, as in <see cref="swe_solcross"/>.</param>
        /// <param name="xlon">Receives the Moon's ecliptic longitude (deg) at the crossing.</param>
        /// <param name="xlat">Receives the Moon's ecliptic latitude (deg) at the crossing (close to
        /// 0, by definition of the crossing).</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise
        /// unchanged.</param>
        /// <returns>The crossing time as a Julian day (ET). A value less than
        /// <paramref name="jd_et"/> signals failure.</returns>
        public double swe_mooncross_node(double jd_et, Int32 flag, ref double xlon, ref double xlat, ref string serr)
        {
            return Sweph.swe_mooncross_node(jd_et, flag, ref xlon, ref xlat, ref serr);
        }
        /// <summary>
        /// Same as <see cref="swe_mooncross_node"/>, but <paramref name="jd_ut"/> and the return
        /// value are in Universal Time rather than ET.
        /// </summary>
        /// <param name="jd_ut">Julian day (Universal Time) to start the search from.</param>
        /// <param name="flag">Ephemeris/computation flags, as in <see cref="swe_solcross"/>.</param>
        /// <param name="xlon">Receives the Moon's ecliptic longitude (deg) at the crossing.</param>
        /// <param name="xlat">Receives the Moon's ecliptic latitude (deg) at the crossing (close to
        /// 0).</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise
        /// unchanged.</param>
        /// <returns>The crossing time as a Julian day (UT). A value less than
        /// <paramref name="jd_ut"/> signals failure.</returns>
        public double swe_mooncross_node_ut(double jd_ut, Int32 flag, ref double xlon, ref double xlat, ref string serr)
        {
            return Sweph.swe_mooncross_node_ut(jd_ut, flag, ref xlon, ref xlat, ref serr);
        }
        /// <summary>
        /// Finds the next (or previous) time a planet's heliocentric ecliptic longitude crosses
        /// <paramref name="x2cross"/> degrees, searching from <paramref name="jd_et"/>. Despite the
        /// non-<c>_ut</c> name, upstream declares this taking the same Universal Time semantics as
        /// <see cref="swe_helio_cross_ut"/> -- the two are identical in Astrodienst's reference
        /// documentation.
        /// </summary>
        /// <param name="ipl">Planet number whose heliocentric longitude is tracked.</param>
        /// <param name="x2cross">Heliocentric ecliptic longitude to search for, in degrees.</param>
        /// <param name="jd_et">Julian day to start the search from.</param>
        /// <param name="iflag">Ephemeris/computation flags, as in <see cref="swe_calc"/>.</param>
        /// <param name="dir">Search direction: &gt;= 0 searches forward in time, &lt; 0 backward.</param>
        /// <param name="jd_cross">Receives the crossing time (Julian day).</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise
        /// unchanged.</param>
        /// <returns><see cref="SwissEph.OK"/> (non-negative) on success; a negative value
        /// (<see cref="SwissEph.ERR"/>) on failure, with a message in <paramref name="serr"/>.</returns>
        public Int32 swe_helio_cross(Int32 ipl, double x2cross, double jd_et, Int32 iflag, Int32 dir, ref double jd_cross, ref string serr)
        {
            return Sweph.swe_helio_cross(ipl, x2cross, jd_et, iflag, dir, ref jd_cross, ref serr);
        }
        /// <summary>
        /// Finds the next (or previous) time a planet's heliocentric ecliptic longitude crosses
        /// <paramref name="x2cross"/> degrees, searching from <paramref name="jd_ut"/> (Universal
        /// Time).
        /// </summary>
        /// <param name="ipl">Planet number whose heliocentric longitude is tracked.</param>
        /// <param name="x2cross">Heliocentric ecliptic longitude to search for, in degrees.</param>
        /// <param name="jd_ut">Julian day (UT) to start the search from.</param>
        /// <param name="iflag">Ephemeris/computation flags, as in <see cref="swe_calc"/>.</param>
        /// <param name="dir">Search direction: &gt;= 0 searches forward in time, &lt; 0 backward.</param>
        /// <param name="jd_cross">Receives the crossing time (Julian day, UT).</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise
        /// unchanged.</param>
        /// <returns><see cref="SwissEph.OK"/> (non-negative) on success; a negative value
        /// (<see cref="SwissEph.ERR"/>) on failure, with a message in <paramref name="serr"/>.</returns>
        public Int32 swe_helio_cross_ut(Int32 ipl, double x2cross, double jd_ut, Int32 iflag, Int32 dir, ref double jd_cross, ref string serr)
        {
            return Sweph.swe_helio_cross_ut(ipl, x2cross, jd_ut, iflag, dir, ref jd_cross, ref serr);
        }

        /// <summary>
        /// Computes the position of a fixed star for Ephemeris/Terrestrial Time <paramref name="tjd"/>.
        /// </summary>
        /// <param name="star">Star name (or a Bayer/Flamsteed designation, or
        /// <c>"name,designation"</c>, or a 1-based sequential record number as a string) to look up;
        /// on return, rewritten to the full matched <c>"name,designation"</c> form found in
        /// <c>sefstars.txt</c>.</param>
        /// <param name="tjd">Julian day (Ephemeris/Terrestrial Time) to compute the position for.</param>
        /// <param name="iflag">Ephemeris/coordinate flags, as in <see cref="swe_calc"/>.</param>
        /// <param name="xx">Receives the star's position in the same 6-element layout as
        /// <see cref="swe_calc"/> (longitude, latitude, distance in AU -- effectively infinite/
        /// parallax-free for stars -- and their speeds).</param>
        /// <param name="serr">Receives an error description if the star isn't found or
        /// <c>sefstars.txt</c> is missing; otherwise unchanged.</param>
        /// <returns>A negative value (<see cref="SwissEph.ERR"/>) on failure; otherwise the flags
        /// actually used, as in <see cref="swe_calc"/>. Requires <c>sefstars.txt</c> in the
        /// ephemeris path.</returns>
        public Int32 swe_fixstar(ref string star, double tjd, Int32 iflag, double[] xx, ref string serr)
        {
            return Sweph.swe_fixstar(ref star, tjd, iflag, xx, ref serr);
        }
        /// <summary>
        /// Same as <see cref="swe_fixstar"/>, but <paramref name="tjd_ut"/> is Universal Time
        /// rather than Ephemeris/Terrestrial Time.
        /// </summary>
        /// <param name="star">Star name (or designation, or record number) to look up; rewritten
        /// on return to the full matched <c>"name,designation"</c> form, as in
        /// <see cref="swe_fixstar"/>.</param>
        /// <param name="tjd_ut">Julian day (Universal Time) to compute the position for.</param>
        /// <param name="iflag">Ephemeris/coordinate flags, as in <see cref="swe_calc"/>.</param>
        /// <param name="xx">Receives the star's position, as in <see cref="swe_fixstar"/>.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise
        /// unchanged.</param>
        /// <returns>A negative value (<see cref="SwissEph.ERR"/>) on failure; otherwise the flags
        /// actually used, as in <see cref="swe_calc"/>.</returns>
        public Int32 swe_fixstar_ut(ref string star, double tjd_ut, Int32 iflag, double[] xx, ref string serr)
        {
            return Sweph.swe_fixstar_ut(ref star, tjd_ut, iflag, xx, ref serr);
        }
        /// <summary>
        /// Looks up just a fixed star's visual magnitude (valid for epoch 2000.0), without
        /// computing a position.
        /// </summary>
        /// <param name="star">Star name (or designation, or record number) to look up; rewritten
        /// on return to the full matched <c>"name,designation"</c> form, as in
        /// <see cref="swe_fixstar"/>.</param>
        /// <param name="mag">Receives the star's visual magnitude.</param>
        /// <param name="serr">Receives an error description if the star isn't found; otherwise
        /// unchanged.</param>
        /// <returns>A negative value (<see cref="SwissEph.ERR"/>) on failure; otherwise
        /// <see cref="SwissEph.OK"/>.</returns>
        public Int32 swe_fixstar_mag(ref string star, ref double mag, ref string serr)
        {
            return Sweph.swe_fixstar_mag(ref star, ref mag, ref serr);
        }

        /// <summary>
        /// Functionally identical to <see cref="swe_fixstar"/>, but uses a faster/newer
        /// star-search implementation added in a later Swiss Ephemeris version. Prefer this
        /// overload in new code; <see cref="swe_fixstar"/> remains for compatibility.
        /// </summary>
        /// <param name="star">Star name (or designation, or record number) to look up; rewritten
        /// on return to the full matched <c>"name,designation"</c> form, as in
        /// <see cref="swe_fixstar"/>.</param>
        /// <param name="tjd">Julian day (Ephemeris/Terrestrial Time) to compute the position for.</param>
        /// <param name="iflag">Ephemeris/coordinate flags, as in <see cref="swe_calc"/>.</param>
        /// <param name="xx">Receives the star's position, as in <see cref="swe_fixstar"/>.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise
        /// unchanged.</param>
        /// <returns>A negative value (<see cref="SwissEph.ERR"/>) on failure; otherwise the flags
        /// actually used, as in <see cref="swe_calc"/>.</returns>
        public Int32 swe_fixstar2(ref string star, double tjd, Int32 iflag, double[] xx, ref string serr)
        {
            return Sweph.swe_fixstar2(ref star, tjd, iflag, xx, ref serr);
        }

        /// <summary>
        /// Same as <see cref="swe_fixstar2"/>, but <paramref name="tjd_ut"/> is Universal Time
        /// rather than Ephemeris/Terrestrial Time. Prefer this overload over
        /// <see cref="swe_fixstar_ut"/> in new code.
        /// </summary>
        /// <param name="star">Star name (or designation, or record number) to look up; rewritten
        /// on return to the full matched <c>"name,designation"</c> form, as in
        /// <see cref="swe_fixstar"/>.</param>
        /// <param name="tjd_ut">Julian day (Universal Time) to compute the position for.</param>
        /// <param name="iflag">Ephemeris/coordinate flags, as in <see cref="swe_calc"/>.</param>
        /// <param name="xx">Receives the star's position, as in <see cref="swe_fixstar"/>.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise
        /// unchanged.</param>
        /// <returns>A negative value (<see cref="SwissEph.ERR"/>) on failure; otherwise the flags
        /// actually used, as in <see cref="swe_calc"/>.</returns>
        public Int32 swe_fixstar2_ut(ref string star, double tjd_ut, Int32 iflag, double[] xx, ref string serr)
        {
            return Sweph.swe_fixstar2_ut(ref star, tjd_ut, iflag, xx, ref serr);
        }

        /// <summary>
        /// Same as <see cref="swe_fixstar_mag"/>, but uses the faster/newer star-search
        /// implementation. Prefer this overload in new code.
        /// </summary>
        /// <param name="star">Star name (or designation, or record number) to look up; rewritten
        /// on return to the full matched <c>"name,designation"</c> form, as in
        /// <see cref="swe_fixstar"/>.</param>
        /// <param name="mag">Receives the star's visual magnitude.</param>
        /// <param name="serr">Receives an error description if the star isn't found; otherwise
        /// unchanged.</param>
        /// <returns>A negative value (<see cref="SwissEph.ERR"/>) on failure; otherwise
        /// <see cref="SwissEph.OK"/>.</returns>
        public Int32 swe_fixstar2_mag(ref string star, ref double mag, ref string serr)
        {
            return Sweph.swe_fixstar2_mag(ref star, ref mag, ref serr);
        }

        /// <summary>
        /// Releases file handles and cached data held by the library. Call this only when done
        /// making calls; nothing else works correctly afterward without setting the ephemeris
        /// path again first.
        /// </summary>
        public void swe_close() { Sweph.swe_close(); }

        /// <summary>
        /// Sets the directory (or <c>;</c>/<c>:</c>-separated list of directories, platform
        /// path-separator convention) Swiss Ephemeris searches for <c>.se1</c> ephemeris files,
        /// <c>sefstars.txt</c>, etc. Not required for the Moshier analytic ephemeris
        /// (<see cref="SEFLG_MOSEPH"/>), which needs no data files.
        /// </summary>
        /// <param name="path"><c>null</c>/omitted uses a built-in default search path.</param>
        public void swe_set_ephe_path(String path) { Sweph.swe_set_ephe_path(path); }

        /// <summary>
        /// Sets which JPL DE ephemeris file to use when <see cref="SEFLG_JPLEPH"/> is requested;
        /// the file must be located in the path set by <see cref="swe_set_ephe_path"/>.
        /// </summary>
        /// <param name="fname">JPL ephemeris file name, e.g. <c>"de431.eph"</c>.</param>
        public void swe_set_jpl_file(string fname) { Sweph.swe_set_jpl_file(fname); }

        /// <summary>
        /// Returns the display name of a body (planet, node/apogee, or asteroid -- for asteroids
        /// this can look up an updated name from <c>seasnam.txt</c>).
        /// </summary>
        /// <param name="ipl">Body number, one of the <c>SE_*</c> planet/point constants or an
        /// asteroid number.</param>
        /// <returns>The body's display name.</returns>
        public string swe_get_planet_name(int ipl) { string sdummy = null; return Sweph.swe_get_planet_name(ipl, ref sdummy); }

        /// <summary>
        /// Sets the observer's geographic location used for all subsequent calls made with the
        /// <see cref="SEFLG_TOPOCTR"/> flag.
        /// </summary>
        /// <param name="geolon">Geographic longitude in degrees, east positive.</param>
        /// <param name="geolat">Geographic latitude in degrees, north positive.</param>
        /// <param name="height">Height above sea level, in meters.</param>
        public void swe_set_topo(double geolon, double geolat, double height) { Sweph.swe_set_topo(geolon, geolat, height); }

        /// <summary>
        /// Selects the sidereal ayanamsha (tropical-to-sidereal offset) method used whenever
        /// <see cref="SEFLG_SIDEREAL"/> is set, for this and later calls.
        /// </summary>
        /// <param name="sid_mode">One of the <c>SE_SIDM_*</c> constants (e.g.
        /// <c>SE_SIDM_LAHIRI</c>, <c>SE_SIDM_FAGAN_BRADLEY</c>), or <c>SE_SIDM_USER</c> together
        /// with <paramref name="t0"/> and <paramref name="ayan_t0"/> to define a custom
        /// ayanamsha.</param>
        /// <param name="t0">Reference Julian day for a custom (<c>SE_SIDM_USER</c>) ayanamsha;
        /// ignored otherwise.</param>
        /// <param name="ayan_t0">Ayanamsha value in degrees at the reference date
        /// <paramref name="t0"/>, for a custom (<c>SE_SIDM_USER</c>) ayanamsha; ignored
        /// otherwise.</param>
        public void swe_set_sid_mode(Int32 sid_mode, double t0, double ayan_t0) { Sweph.swe_set_sid_mode(sid_mode, t0, ayan_t0); }

        /// <summary>
        /// Returns the ayanamsha value for the currently selected sidereal mode
        /// (<see cref="swe_set_sid_mode"/>) at Ephemeris/Terrestrial Time <paramref name="tjd_et"/>.
        /// </summary>
        /// <param name="tjd_et">Julian day (Ephemeris/Terrestrial Time) to compute the ayanamsha for.</param>
        /// <param name="iflag">Ephemeris-backend flags, as in <see cref="swe_calc"/> (e.g.
        /// <see cref="SEFLG_SWIEPH"/>).</param>
        /// <param name="daya">Receives the ayanamsha value, in degrees.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise
        /// unchanged.</param>
        /// <returns><see cref="SwissEph.OK"/> (non-negative) on success; a negative value
        /// (<see cref="SwissEph.ERR"/>) on error, with a message in <paramref name="serr"/>.</returns>
        public Int32 swe_get_ayanamsa_ex(double tjd_et, Int32 iflag, out double daya, ref string serr) { return Sweph.swe_get_ayanamsa_ex(tjd_et, iflag, out daya, ref serr); }
        /// <summary>
        /// Same as <see cref="swe_get_ayanamsa_ex"/>, but <paramref name="tjd_ut"/> is Universal
        /// Time rather than Ephemeris/Terrestrial Time.
        /// </summary>
        /// <param name="tjd_ut">Julian day (Universal Time) to compute the ayanamsha for.</param>
        /// <param name="iflag">Ephemeris-backend flags, as in <see cref="swe_calc"/>.</param>
        /// <param name="daya">Receives the ayanamsha value, in degrees.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise
        /// unchanged.</param>
        /// <returns><see cref="SwissEph.OK"/> (non-negative) on success; a negative value
        /// (<see cref="SwissEph.ERR"/>) on error, with a message in <paramref name="serr"/>.</returns>
        public Int32 swe_get_ayanamsa_ex_ut(double tjd_ut, Int32 iflag, out double daya, ref string serr) { return Sweph.swe_get_ayanamsa_ex_ut(tjd_ut, iflag, out daya, ref serr); }
        /// <summary>
        /// Simplified legacy form of <see cref="swe_get_ayanamsa_ex"/>: returns the ayanamsha
        /// directly, with no error reporting and (per upstream) marginally lower precision than
        /// the <c>_ex</c> form, which should be preferred in new code.
        /// </summary>
        /// <param name="tjd_et">Julian day (Ephemeris/Terrestrial Time) to compute the ayanamsha for.</param>
        /// <returns>The ayanamsha value, in degrees.</returns>
        public double swe_get_ayanamsa(double tjd_et) { return Sweph.swe_get_ayanamsa(tjd_et); }

        /// <summary>
        /// Simplified legacy form of <see cref="swe_get_ayanamsa_ex_ut"/>: returns the ayanamsha
        /// directly, with no error reporting and (per upstream) marginally lower precision than
        /// the <c>_ex</c> form, which should be preferred in new code.
        /// </summary>
        /// <param name="tjd_ut">Julian day (Universal Time) to compute the ayanamsha for.</param>
        /// <returns>The ayanamsha value, in degrees.</returns>
        public double swe_get_ayanamsa_ut(double tjd_ut) { return Sweph.swe_get_ayanamsa_ut(tjd_ut); }

        /// <summary>
        /// Returns the display name of a sidereal mode.
        /// </summary>
        /// <param name="isidmode">One of the <c>SE_SIDM_*</c> constants.</param>
        /// <returns>The sidereal mode's display name.</returns>
        public string swe_get_ayanamsa_name(Int32 isidmode) { return Sweph.swe_get_ayanamsa_name(isidmode); }

        /// <summary>
        /// Returns metadata about the ephemeris file used for the most recent calculation of the
        /// given kind.
        /// </summary>
        /// <param name="ifno">File-slot index (0 = main planets, 1 = Moon, 2 = main asteroids,
        /// 3 = other asteroids, 4 = star file).</param>
        /// <param name="tfstart">Receives the start of the file's valid Julian-day date range.</param>
        /// <param name="tfend">Receives the end of the file's valid Julian-day date range.</param>
        /// <param name="denum">Receives the JPL DE ephemeris number the file is based on.</param>
        /// <returns>The file's full path, or an empty/null result if no such file has been used
        /// yet.</returns>
        public string swe_get_current_file_data(int ifno, ref double tfstart, ref double tfend, ref int denum) { return Sweph.swe_get_current_file_data(ifno, ref tfstart, ref tfend, ref denum); }

        /*ext_def(void) swe_set_timeout(int32 tsec);*/

        /**************************** 
         * exports from swedate.c 
         ****************************/

        /// <summary>
        /// Converts a calendar date/time to a Julian day while validating that the date is a
        /// legal calendar date.
        /// </summary>
        /// <param name="y">Year.</param>
        /// <param name="m">Month.</param>
        /// <param name="d">Day.</param>
        /// <param name="utime">Universal time, as a decimal fraction of hours (0-24).</param>
        /// <param name="c">Calendar to use: <c>'g'</c> for Gregorian, <c>'j'</c> for Julian.</param>
        /// <param name="tjd">Receives the Julian day (Universal Time).</param>
        /// <returns><see cref="SwissEph.OK"/> if the date is legal; <see cref="SwissEph.ERR"/> if
        /// not (e.g. day 30 of February).</returns>
        public int swe_date_conversion(
                int y, int m, int d,         /* year, month, day */
                double utime,   /* universal time in hours (decimal) */
                char c,         /* calendar g[regorian]|j[ulian] */
                ref double tjd)
        {
            return SweDate.swe_date_conversion(y, m, d, utime, c, ref tjd);
        }

        /// <summary>
        /// Converts a calendar date/time to a Julian day without validation -- an illegal date
        /// is still converted arithmetically.
        /// </summary>
        /// <param name="year">Year.</param>
        /// <param name="mon">Month.</param>
        /// <param name="mday">Day of month.</param>
        /// <param name="hour">Hour, as a decimal fraction (0-24).</param>
        /// <param name="gregflag"><c>SE_GREG_CAL</c> for the Gregorian calendar, <c>SE_JUL_CAL</c>
        /// for the Julian calendar.</param>
        /// <returns>The Julian day number (Universal Time).</returns>
        public double swe_julday(int year, int mon, int mday, double hour, int gregflag)
        {
            return SweDate.swe_julday(year, mon, mday, hour, gregflag);
        }

        /// <summary>
        /// The inverse of <see cref="swe_julday"/>: converts a Julian day back to calendar
        /// year/month/day/hour.
        /// </summary>
        /// <param name="jd">Julian day to convert.</param>
        /// <param name="gregflag"><c>SE_GREG_CAL</c> for the Gregorian calendar, <c>SE_JUL_CAL</c>
        /// for the Julian calendar.</param>
        /// <param name="year">Receives the calendar year.</param>
        /// <param name="mon">Receives the calendar month.</param>
        /// <param name="mday">Receives the calendar day of month.</param>
        /// <param name="hour">Receives the hour, as a decimal fraction (0-24).</param>
        public void swe_revjul(double jd, int gregflag, ref int year, ref int mon, ref int mday, ref double hour)
        {
            SweDate.swe_revjul(jd, gregflag, ref year, ref mon, ref mday, ref hour);
        }

        /// <summary>
        /// Converts a civil UTC date/time (correctly handling leap seconds) to Julian day
        /// numbers in both time scales.
        /// </summary>
        /// <param name="iyear">Year.</param>
        /// <param name="imonth">Month.</param>
        /// <param name="iday">Day.</param>
        /// <param name="ihour">Hour.</param>
        /// <param name="imin">Minute.</param>
        /// <param name="dsec">Seconds, as a decimal fraction.</param>
        /// <param name="gregflag"><c>SE_GREG_CAL</c> for the Gregorian calendar, <c>SE_JUL_CAL</c>
        /// for the Julian calendar.</param>
        /// <param name="dret">Receives the Julian day in both time scales: <c>dret[0]</c> is
        /// Ephemeris/Terrestrial Time (TT), <c>dret[1]</c> is UT1.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise
        /// unchanged.</param>
        /// <returns><see cref="SwissEph.OK"/> (non-negative) on success; <see cref="SwissEph.ERR"/>
        /// on failure, with a message in <paramref name="serr"/>.</returns>
        public Int32 swe_utc_to_jd(Int32 iyear, Int32 imonth, Int32 iday,
                Int32 ihour, Int32 imin, double dsec,
                Int32 gregflag, double[] dret, ref string serr)
        {
            return SweDate.swe_utc_to_jd(iyear, imonth, iday, ihour, imin, dsec, gregflag, dret, ref serr);
        }

        /// <summary>
        /// Converts a Julian day in Ephemeris/Terrestrial Time (TT) to civil UTC date/time
        /// components, accounting for leap seconds.
        /// </summary>
        /// <param name="tjd_et">Julian day (Ephemeris/Terrestrial Time) to convert.</param>
        /// <param name="gregflag"><c>SE_GREG_CAL</c> for the Gregorian calendar, <c>SE_JUL_CAL</c>
        /// for the Julian calendar.</param>
        /// <param name="iyear">Receives the UTC year.</param>
        /// <param name="imonth">Receives the UTC month.</param>
        /// <param name="iday">Receives the UTC day.</param>
        /// <param name="ihour">Receives the UTC hour.</param>
        /// <param name="imin">Receives the UTC minute.</param>
        /// <param name="dsec">Receives the UTC seconds, as a decimal fraction.</param>
        public void swe_jdet_to_utc(
                double tjd_et, Int32 gregflag,
                ref Int32 iyear, ref Int32 imonth, ref Int32 iday,
                ref Int32 ihour, ref Int32 imin, ref double dsec)
        {
            SweDate.swe_jdet_to_utc(tjd_et, gregflag, ref iyear, ref imonth, ref iday, ref ihour, ref imin, ref dsec);
        }

        /// <summary>
        /// Same as <see cref="swe_jdet_to_utc"/>, but starting from a Julian day in UT1 rather
        /// than Ephemeris/Terrestrial Time.
        /// </summary>
        /// <param name="tjd_ut">Julian day (UT1) to convert.</param>
        /// <param name="gregflag"><c>SE_GREG_CAL</c> for the Gregorian calendar, <c>SE_JUL_CAL</c>
        /// for the Julian calendar.</param>
        /// <param name="iyear">Receives the UTC year.</param>
        /// <param name="imonth">Receives the UTC month.</param>
        /// <param name="iday">Receives the UTC day.</param>
        /// <param name="ihour">Receives the UTC hour.</param>
        /// <param name="imin">Receives the UTC minute.</param>
        /// <param name="dsec">Receives the UTC seconds, as a decimal fraction.</param>
        public void swe_jdut1_to_utc(
                double tjd_ut, Int32 gregflag,
                ref Int32 iyear, ref Int32 imonth, ref Int32 iday,
                ref Int32 ihour, ref Int32 imin, ref double dsec)
        {
            SweDate.swe_jdut1_to_utc(tjd_ut, gregflag, ref iyear, ref imonth, ref iday, ref ihour, ref imin, ref dsec);
        }

        /// <summary>
        /// Converts a civil date/time from local time to UTC (or the reverse), given a timezone
        /// offset. Correctly rolls the date over at midnight and across a leap second where
        /// applicable.
        /// </summary>
        /// <param name="iyear">Input year.</param>
        /// <param name="imonth">Input month.</param>
        /// <param name="iday">Input day.</param>
        /// <param name="ihour">Input hour.</param>
        /// <param name="imin">Input minute.</param>
        /// <param name="dsec">Input seconds, as a decimal fraction.</param>
        /// <param name="d_timezone">Timezone offset in hours, east of Greenwich positive.</param>
        /// <param name="iyear_out">Receives the converted year.</param>
        /// <param name="imonth_out">Receives the converted month.</param>
        /// <param name="iday_out">Receives the converted day.</param>
        /// <param name="ihour_out">Receives the converted hour.</param>
        /// <param name="imin_out">Receives the converted minute.</param>
        /// <param name="dsec_out">Receives the converted seconds, as a decimal fraction.</param>
        public void swe_utc_time_zone(
                Int32 iyear, Int32 imonth, Int32 iday,
                Int32 ihour, Int32 imin, double dsec,
                double d_timezone,
                ref Int32 iyear_out, ref Int32 imonth_out, ref Int32 iday_out,
                ref Int32 ihour_out, ref Int32 imin_out, ref double dsec_out)
        {
            SweDate.swe_utc_time_zone(
                iyear, imonth, iday,
                ihour, imin, dsec,
                d_timezone,
                ref iyear_out, ref imonth_out, ref iday_out,
                ref ihour_out, ref imin_out, ref dsec_out);
        }

        /**************************** 
         * exports from swehouse.c 
         ****************************/

        /// <summary>
        /// Computes house cusps and the related points (Ascendant, MC, ...) for a date and
        /// geographic position. Compatibility overload: widens to the <c>int</c> overload,
        /// which is the signature upstream declares. A <c>char</c> above U+00FF resolves by
        /// its low byte, matching an 8-bit C <c>char</c>.
        /// </summary>
        /// <param name="tjd_ut">Julian day number, Universal Time (UT).</param>
        /// <param name="geolat">Geographic latitude, in degrees (northern latitudes positive).</param>
        /// <param name="geolon">Geographic longitude, in degrees (eastern latitudes positive).</param>
        /// <param name="hsys">
        /// House system code:
        /// <list type="bullet">
        /// <item><description><c>'A'</c> equal (cusp 1 = Ascendant)</description></item>
        /// <item><description><c>'B'</c> Alcabitius</description></item>
        /// <item><description><c>'C'</c> Campanus</description></item>
        /// <item><description><c>'D'</c> equal (MC) -- cusp 10 = Midheaven</description></item>
        /// <item><description><c>'E'</c> equal (same as A)</description></item>
        /// <item><description><c>'F'</c> Carter poli-equatorial</description></item>
        /// <item><description><c>'G'</c> Gauquelin sectors (36 sectors; <c>cusps</c> needs 37 elements, not 13)</description></item>
        /// <item><description><c>'H'</c> horizon / azimuth system</description></item>
        /// <item><description><c>'I'</c> Sunshine (Treindl solution)</description></item>
        /// <item><description><c>'i'</c> Sunshine (Makransky/alternate solution)</description></item>
        /// <item><description><c>'J'</c> Savard-A</description></item>
        /// <item><description><c>'K'</c> Koch</description></item>
        /// <item><description><c>'L'</c> Pullen SD ("sinusoidal delta", ex-Neo-Porphyry)</description></item>
        /// <item><description><c>'M'</c> Morinus</description></item>
        /// <item><description><c>'N'</c> equal, 0 Aries on the 1st cusp</description></item>
        /// <item><description><c>'O'</c> Porphyry</description></item>
        /// <item><description><c>'Q'</c> Pullen SR ("sinusoidal ratio")</description></item>
        /// <item><description><c>'R'</c> Regiomontanus</description></item>
        /// <item><description><c>'S'</c> Sripati</description></item>
        /// <item><description><c>'T'</c> Polich/Page ("topocentric")</description></item>
        /// <item><description><c>'U'</c> Krusinski-Pisa-Goelzer</description></item>
        /// <item><description><c>'V'</c> equal, Vehlow variant (Ascendant at the middle of house 1, not the cusp)</description></item>
        /// <item><description><c>'W'</c> equal, whole-sign houses</description></item>
        /// <item><description><c>'X'</c> axial rotation / Meridian houses</description></item>
        /// <item><description><c>'Y'</c> APC houses</description></item>
        /// </list>
        /// Any other value falls back to Placidus.
        /// </param>
        /// <param name="cusps">
        /// Receives the house cusps, as ecliptic longitude in degrees. Must have at least 13
        /// elements (<c>cusps[0]</c> is unused/reserved; <c>cusps[1]</c>-<c>cusps[12]</c> are
        /// cusps 1-12), or at least 37 elements for Gauquelin sectors (<c>hsys</c> = <c>'G'</c>,
        /// <c>cusps[1]</c>-<c>cusps[36]</c>).
        /// </param>
        /// <param name="ascmc">
        /// Receives the additional points, and must have at least 10 elements: <c>ascmc[0]</c>
        /// Ascendant, <c>ascmc[1]</c> MC, <c>ascmc[2]</c> ARMC, <c>ascmc[3]</c> Vertex,
        /// <c>ascmc[4]</c> equatorial ascendant, <c>ascmc[5]</c> co-ascendant (W. Koch method),
        /// <c>ascmc[6]</c> co-ascendant (M. Munkasey method), <c>ascmc[7]</c> polar ascendant
        /// (M. Munkasey method); <c>ascmc[8]</c>/<c>ascmc[9]</c> are unused by most house
        /// systems.
        /// </param>
        /// <returns>
        /// <c>SwissEph.OK</c> on success, <c>SwissEph.ERR</c> on failure (e.g. a house system
        /// this ephemeris/latitude combination cannot compute, or an invalid <c>hsys</c>); this
        /// overload has no <c>serr</c> parameter to receive an error message.
        /// </returns>
        public int swe_houses(double tjd_ut, double geolat, double geolon, char hsys, double[] cusps, double[] ascmc)
        {
            return SweHouse.swe_houses(tjd_ut, geolat, geolon, hsys, cusps, ascmc);
        }

        /// <summary>
        /// Computes house cusps and the related points (Ascendant, MC, ...) for a date and
        /// geographic position. Matches upstream <c>swephexp.h:812</c>, which declares
        /// <c>int hsys</c>; prefer this overload in new code.
        /// </summary>
        /// <param name="tjd_ut">Julian day number, Universal Time (UT).</param>
        /// <param name="geolat">Geographic latitude, in degrees (northern latitudes positive).</param>
        /// <param name="geolon">Geographic longitude, in degrees (eastern latitudes positive).</param>
        /// <param name="hsys">
        /// House system code, the character code point of one of the letters documented on the
        /// <c>char</c> overload (e.g. <c>'K'</c> for Koch); any value that matches none of them
        /// falls back to Placidus.
        /// </param>
        /// <param name="cusps">
        /// Receives the house cusps, as ecliptic longitude in degrees. Must have at least 13
        /// elements (<c>cusps[0]</c> is unused/reserved; <c>cusps[1]</c>-<c>cusps[12]</c> are
        /// cusps 1-12), or at least 37 elements for Gauquelin sectors (<c>hsys</c> = <c>'G'</c>,
        /// <c>cusps[1]</c>-<c>cusps[36]</c>).
        /// </param>
        /// <param name="ascmc">
        /// Receives the additional points, and must have at least 10 elements: <c>ascmc[0]</c>
        /// Ascendant, <c>ascmc[1]</c> MC, <c>ascmc[2]</c> ARMC, <c>ascmc[3]</c> Vertex,
        /// <c>ascmc[4]</c> equatorial ascendant, <c>ascmc[5]</c> co-ascendant (W. Koch method),
        /// <c>ascmc[6]</c> co-ascendant (M. Munkasey method), <c>ascmc[7]</c> polar ascendant
        /// (M. Munkasey method); <c>ascmc[8]</c>/<c>ascmc[9]</c> are unused by most house
        /// systems.
        /// </param>
        /// <returns>
        /// <c>SwissEph.OK</c> on success, <c>SwissEph.ERR</c> on failure (e.g. a house system
        /// this ephemeris/latitude combination cannot compute, or an invalid <c>hsys</c>); this
        /// overload has no <c>serr</c> parameter to receive an error message.
        /// </returns>
        public int swe_houses(double tjd_ut, double geolat, double geolon, int hsys, double[] cusps, double[] ascmc)
        {
            return SweHouse.swe_houses(tjd_ut, geolat, geolon, hsys, cusps, ascmc);
        }

        /// <summary>
        /// Computes house cusps with additional flags (sidereal modes, radians, ...).
        /// Compatibility overload: widens to the <c>int</c> overload, which is the signature
        /// upstream declares. A <c>char</c> above U+00FF resolves by its low byte.
        /// </summary>
        /// <param name="tjd_ut">Julian day number, Universal Time (UT).</param>
        /// <param name="iflag">
        /// Calculation flags, e.g. <see cref="SEFLG_SIDEREAL"/> for sidereal house cusps, or
        /// <see cref="SEFLG_RADIANS"/> to return angles in radians instead of degrees.
        /// </param>
        /// <param name="geolat">Geographic latitude, in degrees (northern latitudes positive).</param>
        /// <param name="geolon">Geographic longitude, in degrees (eastern latitudes positive).</param>
        /// <param name="hsys">
        /// House system code, one of the letters documented on <see cref="swe_houses(double, double, double, char, double[], double[])"/>;
        /// any value that matches none of them falls back to Placidus.
        /// </param>
        /// <param name="hcusps">
        /// Receives the house cusps, as ecliptic longitude in degrees (radians if
        /// <see cref="SEFLG_RADIANS"/> is set). Must have at least 13 elements (index 0
        /// unused/reserved; indices 1-12 are cusps 1-12), or at least 37 elements for Gauquelin
        /// sectors (<c>hsys</c> = <c>'G'</c>, indices 1-36).
        /// </param>
        /// <param name="ascmc">
        /// Receives the additional points, and must have at least 10 elements: index 0
        /// Ascendant, 1 MC, 2 ARMC, 3 Vertex, 4 equatorial ascendant, 5 co-ascendant (W. Koch
        /// method), 6 co-ascendant (M. Munkasey method), 7 polar ascendant (M. Munkasey
        /// method); indices 8/9 are unused by most house systems.
        /// </param>
        /// <returns>
        /// <c>SwissEph.OK</c> on success, <c>SwissEph.ERR</c> on failure; this overload has no
        /// <c>serr</c> parameter to receive an error message.
        /// </returns>
        public int swe_houses_ex(double tjd_ut, Int32 iflag, double geolat, double geolon, char hsys, CPointer<double> hcusps, CPointer<double> ascmc)
        {
            return SweHouse.swe_houses_ex(tjd_ut, iflag, geolat, geolon, hsys, hcusps, ascmc);
        }

        /// <summary>
        /// Computes house cusps with additional flags (sidereal modes, radians, ...).
        /// Matches upstream <c>swephexp.h:816</c>, which declares <c>int hsys</c>; prefer
        /// this overload in new code.
        /// </summary>
        /// <param name="tjd_ut">Julian day number, Universal Time (UT).</param>
        /// <param name="iflag">
        /// Calculation flags, e.g. <see cref="SEFLG_SIDEREAL"/> for sidereal house cusps, or
        /// <see cref="SEFLG_RADIANS"/> to return angles in radians instead of degrees.
        /// </param>
        /// <param name="geolat">Geographic latitude, in degrees (northern latitudes positive).</param>
        /// <param name="geolon">Geographic longitude, in degrees (eastern latitudes positive).</param>
        /// <param name="hsys">
        /// House system code, the character code point of one of the letters documented on
        /// <see cref="swe_houses(double, double, double, char, double[], double[])"/>; any
        /// value that matches none of them falls back to Placidus.
        /// </param>
        /// <param name="hcusps">
        /// Receives the house cusps, as ecliptic longitude in degrees (radians if
        /// <see cref="SEFLG_RADIANS"/> is set). Must have at least 13 elements (index 0
        /// unused/reserved; indices 1-12 are cusps 1-12), or at least 37 elements for Gauquelin
        /// sectors (<c>hsys</c> = <c>'G'</c>, indices 1-36).
        /// </param>
        /// <param name="ascmc">
        /// Receives the additional points, and must have at least 10 elements: index 0
        /// Ascendant, 1 MC, 2 ARMC, 3 Vertex, 4 equatorial ascendant, 5 co-ascendant (W. Koch
        /// method), 6 co-ascendant (M. Munkasey method), 7 polar ascendant (M. Munkasey
        /// method); indices 8/9 are unused by most house systems.
        /// </param>
        /// <returns>
        /// <c>SwissEph.OK</c> on success, <c>SwissEph.ERR</c> on failure; this overload has no
        /// <c>serr</c> parameter to receive an error message.
        /// </returns>
        public int swe_houses_ex(double tjd_ut, Int32 iflag, double geolat, double geolon, int hsys, CPointer<double> hcusps, CPointer<double> ascmc)
        {
            return SweHouse.swe_houses_ex(tjd_ut, iflag, geolat, geolon, hsys, hcusps, ascmc);
        }

        /// <summary>
        /// Computes house cusps with additional flags (sidereal modes, radians, ...), together
        /// with the daily-motion speeds of the cusps and additional points. Compatibility
        /// overload: widens to the <c>int</c> overload, which is the signature upstream
        /// declares. A <c>char</c> above U+00FF resolves by its low byte.
        /// </summary>
        /// <param name="tjd_ut">Julian day number, Universal Time (UT).</param>
        /// <param name="iflag">
        /// Calculation flags, e.g. <see cref="SEFLG_SIDEREAL"/> for sidereal house cusps, or
        /// <see cref="SEFLG_RADIANS"/> to return angles in radians instead of degrees.
        /// </param>
        /// <param name="geolat">Geographic latitude, in degrees (northern latitudes positive).</param>
        /// <param name="geolon">Geographic longitude, in degrees (eastern latitudes positive).</param>
        /// <param name="hsys">
        /// House system code, one of the letters documented on <see cref="swe_houses(double, double, double, char, double[], double[])"/>;
        /// any value that matches none of them falls back to Placidus.
        /// </param>
        /// <param name="hcusps">
        /// Receives the house cusps, as ecliptic longitude in degrees (radians if
        /// <see cref="SEFLG_RADIANS"/> is set). Must have at least 13 elements (index 0
        /// unused/reserved; indices 1-12 are cusps 1-12), or at least 37 elements for Gauquelin
        /// sectors (<c>hsys</c> = <c>'G'</c>, indices 1-36).
        /// </param>
        /// <param name="ascmc">
        /// Receives the additional points, and must have at least 10 elements: index 0
        /// Ascendant, 1 MC, 2 ARMC, 3 Vertex, 4 equatorial ascendant, 5 co-ascendant (W. Koch
        /// method), 6 co-ascendant (M. Munkasey method), 7 polar ascendant (M. Munkasey
        /// method); indices 8/9 are unused by most house systems.
        /// </param>
        /// <param name="cuspSpeed">
        /// Receives the daily-motion speed of each house cusp, in degrees/day, same indexing
        /// as <c>hcusps</c>. Only filled if the caller supplies a non-null, appropriately sized
        /// array.
        /// </param>
        /// <param name="ascmcSpeed">
        /// Receives the daily-motion speed of each <c>ascmc</c> point, in degrees/day, same
        /// indexing as <c>ascmc</c>. Only filled if the caller supplies a non-null,
        /// appropriately sized array.
        /// </param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns><c>SwissEph.OK</c> on success, <c>SwissEph.ERR</c> on failure (message in <c>serr</c>).</returns>
        public int swe_houses_ex2(double tjd_ut, Int32 iflag, double geolat, double geolon, char hsys, CPointer<double> hcusps, CPointer<double> ascmc, CPointer<double> cuspSpeed, CPointer<double> ascmcSpeed, ref string serr)
        {
            return SweHouse.swe_houses_ex2(tjd_ut, iflag, geolat, geolon, hsys, hcusps, ascmc, cuspSpeed, ascmcSpeed, ref serr);
        }

        /// <summary>
        /// Computes house cusps with additional flags (sidereal modes, radians, ...), together
        /// with the daily-motion speeds of the cusps and additional points. Matches upstream
        /// <c>swephexp.h:820</c>, which declares <c>int hsys</c>; prefer this overload in new
        /// code.
        /// </summary>
        /// <param name="tjd_ut">Julian day number, Universal Time (UT).</param>
        /// <param name="iflag">
        /// Calculation flags, e.g. <see cref="SEFLG_SIDEREAL"/> for sidereal house cusps, or
        /// <see cref="SEFLG_RADIANS"/> to return angles in radians instead of degrees.
        /// </param>
        /// <param name="geolat">Geographic latitude, in degrees (northern latitudes positive).</param>
        /// <param name="geolon">Geographic longitude, in degrees (eastern latitudes positive).</param>
        /// <param name="hsys">
        /// House system code, the character code point of one of the letters documented on
        /// <see cref="swe_houses(double, double, double, char, double[], double[])"/>; any
        /// value that matches none of them falls back to Placidus.
        /// </param>
        /// <param name="hcusps">
        /// Receives the house cusps, as ecliptic longitude in degrees (radians if
        /// <see cref="SEFLG_RADIANS"/> is set). Must have at least 13 elements (index 0
        /// unused/reserved; indices 1-12 are cusps 1-12), or at least 37 elements for Gauquelin
        /// sectors (<c>hsys</c> = <c>'G'</c>, indices 1-36).
        /// </param>
        /// <param name="ascmc">
        /// Receives the additional points, and must have at least 10 elements: index 0
        /// Ascendant, 1 MC, 2 ARMC, 3 Vertex, 4 equatorial ascendant, 5 co-ascendant (W. Koch
        /// method), 6 co-ascendant (M. Munkasey method), 7 polar ascendant (M. Munkasey
        /// method); indices 8/9 are unused by most house systems.
        /// </param>
        /// <param name="cuspSpeed">
        /// Receives the daily-motion speed of each house cusp, in degrees/day, same indexing
        /// as <c>hcusps</c>. Only filled if the caller supplies a non-null, appropriately sized
        /// array.
        /// </param>
        /// <param name="ascmcSpeed">
        /// Receives the daily-motion speed of each <c>ascmc</c> point, in degrees/day, same
        /// indexing as <c>ascmc</c>. Only filled if the caller supplies a non-null,
        /// appropriately sized array.
        /// </param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns><c>SwissEph.OK</c> on success, <c>SwissEph.ERR</c> on failure (message in <c>serr</c>).</returns>
        public int swe_houses_ex2(double tjd_ut, Int32 iflag, double geolat, double geolon, int hsys, CPointer<double> hcusps, CPointer<double> ascmc, CPointer<double> cuspSpeed, CPointer<double> ascmcSpeed, ref string serr)
        {
            return SweHouse.swe_houses_ex2(tjd_ut, iflag, geolat, geolon, hsys, hcusps, ascmc, cuspSpeed, ascmcSpeed, ref serr);
        }

        /// <summary>
        /// Computes house cusps directly from ARMC, geographic latitude and the obliquity of
        /// the ecliptic, requiring no ephemeris data. Compatibility overload: widens to the
        /// <c>int</c> overload. A <c>char</c> above U+00FF resolves by its low byte.
        /// </summary>
        /// <param name="armc">ARMC (right ascension of the MC), in degrees.</param>
        /// <param name="geolat">Geographic latitude, in degrees (northern latitudes positive).</param>
        /// <param name="eps">Obliquity of the ecliptic, in degrees.</param>
        /// <param name="hsys">
        /// House system code, one of the letters documented on <see cref="swe_houses(double, double, double, char, double[], double[])"/>;
        /// any value that matches none of them falls back to Placidus.
        /// </param>
        /// <param name="cusps">
        /// Receives the house cusps, as ecliptic longitude in degrees. Must have at least 13
        /// elements (<c>cusps[0]</c> is unused/reserved; <c>cusps[1]</c>-<c>cusps[12]</c> are
        /// cusps 1-12), or at least 37 elements for Gauquelin sectors (<c>hsys</c> = <c>'G'</c>,
        /// <c>cusps[1]</c>-<c>cusps[36]</c>).
        /// </param>
        /// <param name="ascmc">
        /// Receives the additional points, and must have at least 10 elements: <c>ascmc[0]</c>
        /// Ascendant, <c>ascmc[1]</c> MC, <c>ascmc[2]</c> ARMC, <c>ascmc[3]</c> Vertex,
        /// <c>ascmc[4]</c> equatorial ascendant, <c>ascmc[5]</c> co-ascendant (W. Koch method),
        /// <c>ascmc[6]</c> co-ascendant (M. Munkasey method), <c>ascmc[7]</c> polar ascendant
        /// (M. Munkasey method); <c>ascmc[8]</c>/<c>ascmc[9]</c> are unused by most house
        /// systems, except that for <c>hsys</c> = <c>'I'</c>/<c>'i'</c> (Sunshine) the caller
        /// must supply the Sun's declination, in degrees and in [-24, 24], in <c>ascmc[9]</c>
        /// as an INPUT before the call -- unlike every other house system, where <c>ascmc</c>
        /// is pure output.
        /// </param>
        /// <returns>
        /// <c>SwissEph.OK</c> on success, <c>SwissEph.ERR</c> on failure; this overload has no
        /// <c>serr</c> parameter to receive an error message.
        /// </returns>
        public int swe_houses_armc(double armc, double geolat, double eps, char hsys, double[] cusps, double[] ascmc)
        {
            return SweHouse.swe_houses_armc(armc, geolat, eps, hsys, cusps, ascmc);
        }

        /// <summary>
        /// Computes house cusps directly from ARMC, geographic latitude and the obliquity of
        /// the ecliptic, requiring no ephemeris data. Matches upstream <c>swephexp.h:824</c>,
        /// which declares <c>int hsys</c>; prefer this overload in new code.
        /// </summary>
        /// <param name="armc">ARMC (right ascension of the MC), in degrees.</param>
        /// <param name="geolat">Geographic latitude, in degrees (northern latitudes positive).</param>
        /// <param name="eps">Obliquity of the ecliptic, in degrees.</param>
        /// <param name="hsys">
        /// House system code, the character code point of one of the letters documented on
        /// <see cref="swe_houses(double, double, double, char, double[], double[])"/>; any
        /// value that matches none of them falls back to Placidus.
        /// </param>
        /// <param name="cusps">
        /// Receives the house cusps, as ecliptic longitude in degrees. Must have at least 13
        /// elements (<c>cusps[0]</c> is unused/reserved; <c>cusps[1]</c>-<c>cusps[12]</c> are
        /// cusps 1-12), or at least 37 elements for Gauquelin sectors (<c>hsys</c> = <c>'G'</c>,
        /// <c>cusps[1]</c>-<c>cusps[36]</c>).
        /// </param>
        /// <param name="ascmc">
        /// Receives the additional points, and must have at least 10 elements: <c>ascmc[0]</c>
        /// Ascendant, <c>ascmc[1]</c> MC, <c>ascmc[2]</c> ARMC, <c>ascmc[3]</c> Vertex,
        /// <c>ascmc[4]</c> equatorial ascendant, <c>ascmc[5]</c> co-ascendant (W. Koch method),
        /// <c>ascmc[6]</c> co-ascendant (M. Munkasey method), <c>ascmc[7]</c> polar ascendant
        /// (M. Munkasey method); <c>ascmc[8]</c>/<c>ascmc[9]</c> are unused by most house
        /// systems, except that for <c>hsys</c> = <c>'I'</c>/<c>'i'</c> (Sunshine) the caller
        /// must supply the Sun's declination, in degrees and in [-24, 24], in <c>ascmc[9]</c>
        /// as an INPUT before the call -- unlike every other house system, where <c>ascmc</c>
        /// is pure output.
        /// </param>
        /// <returns>
        /// <c>SwissEph.OK</c> on success, <c>SwissEph.ERR</c> on failure; this overload has no
        /// <c>serr</c> parameter to receive an error message.
        /// </returns>
        public int swe_houses_armc(double armc, double geolat, double eps, int hsys, double[] cusps, double[] ascmc)
        {
            return SweHouse.swe_houses_armc(armc, geolat, eps, hsys, cusps, ascmc);
        }

        /// <summary>
        /// Computes house cusps directly from ARMC, geographic latitude and the obliquity of
        /// the ecliptic, requiring no ephemeris data, together with the daily-motion speeds of
        /// the cusps and additional points. Compatibility overload: widens to the <c>int</c>
        /// overload. A <c>char</c> above U+00FF resolves by its low byte.
        /// </summary>
        /// <param name="armc">ARMC (right ascension of the MC), in degrees.</param>
        /// <param name="geolat">Geographic latitude, in degrees (northern latitudes positive).</param>
        /// <param name="eps">Obliquity of the ecliptic, in degrees.</param>
        /// <param name="hsys">
        /// House system code, one of the letters documented on <see cref="swe_houses(double, double, double, char, double[], double[])"/>;
        /// any value that matches none of them falls back to Placidus.
        /// </param>
        /// <param name="cusps">
        /// Receives the house cusps, as ecliptic longitude in degrees. Must have at least 13
        /// elements (index 0 unused/reserved; indices 1-12 are cusps 1-12), or at least 37
        /// elements for Gauquelin sectors (<c>hsys</c> = <c>'G'</c>, indices 1-36).
        /// </param>
        /// <param name="ascmc">
        /// Receives the additional points, and must have at least 10 elements: index 0
        /// Ascendant, 1 MC, 2 ARMC, 3 Vertex, 4 equatorial ascendant, 5 co-ascendant (W. Koch
        /// method), 6 co-ascendant (M. Munkasey method), 7 polar ascendant (M. Munkasey
        /// method); indices 8/9 are unused by most house systems, except that for <c>hsys</c> =
        /// <c>'I'</c>/<c>'i'</c> (Sunshine) the caller must supply the Sun's declination, in
        /// degrees and in [-24, 24], in index 9 as an INPUT before the call -- unlike every
        /// other house system, where <c>ascmc</c> is pure output.
        /// </param>
        /// <param name="cuspSpeed">
        /// Receives the daily-motion speed of each house cusp, in degrees/day, same indexing
        /// as <c>cusps</c>. Only filled if the caller supplies a non-null, appropriately sized
        /// array.
        /// </param>
        /// <param name="ascmcSpeed">
        /// Receives the daily-motion speed of each <c>ascmc</c> point, in degrees/day, same
        /// indexing as <c>ascmc</c>. Only filled if the caller supplies a non-null,
        /// appropriately sized array.
        /// </param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns><c>SwissEph.OK</c> on success, <c>SwissEph.ERR</c> on failure (message in <c>serr</c>).</returns>
        public int swe_houses_armc_ex2(double armc, double geolat, double eps, char hsys, CPointer<double> cusps, CPointer<double> ascmc, CPointer<double> cuspSpeed, CPointer<double> ascmcSpeed, ref string serr)
        {
            return SweHouse.swe_houses_armc_ex2(armc, geolat, eps, hsys, cusps, ascmc, cuspSpeed, ascmcSpeed, ref serr);
        }

        /// <summary>
        /// Computes house cusps directly from ARMC, geographic latitude and the obliquity of
        /// the ecliptic, requiring no ephemeris data, together with the daily-motion speeds of
        /// the cusps and additional points. Matches upstream <c>swephexp.h:828</c>, which
        /// declares <c>int hsys</c>; prefer this overload in new code.
        /// </summary>
        /// <param name="armc">ARMC (right ascension of the MC), in degrees.</param>
        /// <param name="geolat">Geographic latitude, in degrees (northern latitudes positive).</param>
        /// <param name="eps">Obliquity of the ecliptic, in degrees.</param>
        /// <param name="hsys">
        /// House system code, the character code point of one of the letters documented on
        /// <see cref="swe_houses(double, double, double, char, double[], double[])"/>; any
        /// value that matches none of them falls back to Placidus.
        /// </param>
        /// <param name="cusps">
        /// Receives the house cusps, as ecliptic longitude in degrees. Must have at least 13
        /// elements (index 0 unused/reserved; indices 1-12 are cusps 1-12), or at least 37
        /// elements for Gauquelin sectors (<c>hsys</c> = <c>'G'</c>, indices 1-36).
        /// </param>
        /// <param name="ascmc">
        /// Receives the additional points, and must have at least 10 elements: index 0
        /// Ascendant, 1 MC, 2 ARMC, 3 Vertex, 4 equatorial ascendant, 5 co-ascendant (W. Koch
        /// method), 6 co-ascendant (M. Munkasey method), 7 polar ascendant (M. Munkasey
        /// method); indices 8/9 are unused by most house systems, except that for <c>hsys</c> =
        /// <c>'I'</c>/<c>'i'</c> (Sunshine) the caller must supply the Sun's declination, in
        /// degrees and in [-24, 24], in index 9 as an INPUT before the call -- unlike every
        /// other house system, where <c>ascmc</c> is pure output.
        /// </param>
        /// <param name="cuspSpeed">
        /// Receives the daily-motion speed of each house cusp, in degrees/day, same indexing
        /// as <c>cusps</c>. Only filled if the caller supplies a non-null, appropriately sized
        /// array.
        /// </param>
        /// <param name="ascmcSpeed">
        /// Receives the daily-motion speed of each <c>ascmc</c> point, in degrees/day, same
        /// indexing as <c>ascmc</c>. Only filled if the caller supplies a non-null,
        /// appropriately sized array.
        /// </param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns><c>SwissEph.OK</c> on success, <c>SwissEph.ERR</c> on failure (message in <c>serr</c>).</returns>
        public int swe_houses_armc_ex2(double armc, double geolat, double eps, int hsys, CPointer<double> cusps, CPointer<double> ascmc, CPointer<double> cuspSpeed, CPointer<double> ascmcSpeed, ref string serr)
        {
            return SweHouse.swe_houses_armc_ex2(armc, geolat, eps, hsys, cusps, ascmc, cuspSpeed, ascmcSpeed, ref serr);
        }

        /// <summary>
        /// Returns the house position of a given body position. Compatibility overload:
        /// widens to the <c>int</c> overload, which is the signature upstream declares.
        /// </summary>
        /// <param name="armc">ARMC (right ascension of the MC), in degrees.</param>
        /// <param name="geolat">Geographic latitude, in degrees (northern latitudes positive).</param>
        /// <param name="eps">Obliquity of the ecliptic, in degrees.</param>
        /// <param name="hsys">
        /// House system code, one of the letters documented on <see cref="swe_houses(double, double, double, char, double[], double[])"/>;
        /// any value that matches none of them falls back to the simplified Placidus-family
        /// interpolation algorithm, as in C.
        /// </param>
        /// <param name="xpin">
        /// 2-element array giving the ecliptic position to place: <c>xpin[0]</c> ecliptic
        /// longitude, <c>xpin[1]</c> ecliptic latitude, both in degrees.
        /// </param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns>
        /// The house position, in [1, 13) -- the integer part is the house number, the
        /// fractional part the position within that house. A negative value signals an error
        /// (message in <c>serr</c>).
        /// </returns>
        public double swe_house_pos(double armc, double geolat, double eps, char hsys, double[] xpin, ref string serr)
        {
            return SweHouse.swe_house_pos(armc, geolat, eps, hsys, xpin, ref serr);
        }

        /// <summary>
        /// Returns the house position of a given body position. Matches upstream
        /// <c>swephexp.h:832</c>, which declares <c>int hsys</c>; prefer this overload in new
        /// code. An <c>hsys</c> that matches no house-system letter falls through to the
        /// simplified interpolation algorithm, as in C.
        /// </summary>
        /// <param name="armc">ARMC (right ascension of the MC), in degrees.</param>
        /// <param name="geolat">Geographic latitude, in degrees (northern latitudes positive).</param>
        /// <param name="eps">Obliquity of the ecliptic, in degrees.</param>
        /// <param name="hsys">
        /// House system code, the character code point of one of the letters documented on
        /// <see cref="swe_houses(double, double, double, char, double[], double[])"/>; any
        /// value that matches none of them falls back to the simplified Placidus-family
        /// interpolation algorithm.
        /// </param>
        /// <param name="xpin">
        /// 2-element array giving the ecliptic position to place: <c>xpin[0]</c> ecliptic
        /// longitude, <c>xpin[1]</c> ecliptic latitude, both in degrees.
        /// </param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns>
        /// The house position, in [1, 13) -- the integer part is the house number, the
        /// fractional part the position within that house. A negative value signals an error
        /// (message in <c>serr</c>).
        /// </returns>
        public double swe_house_pos(double armc, double geolat, double eps, int hsys, double[] xpin, ref string serr)
        {
            return SweHouse.swe_house_pos(armc, geolat, eps, hsys, xpin, ref serr);
        }

        /// <summary>
        /// Returns the name of a house system. Compatibility overload: widens to the
        /// <c>int</c> overload. Both compare the raw value, so neither narrows.
        /// </summary>
        /// <param name="hsys">
        /// House system code, one of the letters documented on <see cref="swe_houses(double, double, double, char, double[], double[])"/>;
        /// any value that matches none of them is reported as <c>"Placidus"</c>.
        /// </param>
        /// <returns>The display name of the house system, e.g. <c>"Koch"</c> for <c>'K'</c>.</returns>
        public string swe_house_name(char hsys) { return SweHouse.swe_house_name(hsys); }

        /// <summary>
        /// Returns the name of a house system, or <c>"Placidus"</c> for any value that
        /// matches no house-system letter. Matches upstream <c>swephexp.h:835</c>, which
        /// declares <c>int hsys</c>; prefer this overload in new code.
        /// </summary>
        /// <param name="hsys">
        /// House system code, the character code point of one of the letters documented on
        /// <see cref="swe_houses(double, double, double, char, double[], double[])"/>.
        /// </param>
        /// <returns>The display name of the house system, e.g. <c>"Koch"</c> for <c>'K'</c>, or <c>"Placidus"</c> if <c>hsys</c> matches no house-system letter.</returns>
        public string swe_house_name(int hsys) { return SweHouse.swe_house_name(hsys); }

        /**************************** 
         * exports from swecl.c 
         ****************************/

        /// <summary>
        /// Computes the Gauquelin sector (a 36-sector division of the diurnal circle, sectors
        /// numbered 1-36) occupied by a planet or fixed star at a given time and place.
        /// </summary>
        /// <param name="t_ut">Time of the computation, Universal Time (Julian day, UT).</param>
        /// <param name="ipl">Body number, used when <paramref name="starname"/> is <c>null</c> or empty.</param>
        /// <param name="starname">Fixed star name, or <c>null</c>/<c>""</c> to use <paramref name="ipl"/> instead.</param>
        /// <param name="iflag">
        /// Computation flags, e.g. <see cref="SEFLG_SWIEPH"/>, combined with the ephemeris/coordinate flags accepted by <see cref="swe_calc(double, Int32, Int32, double[], ref string)"/>.
        /// </param>
        /// <param name="imeth">Sector-computation method selector; <c>0</c> uses the body's ecliptic latitude, per upstream.</param>
        /// <param name="geopos">Observer's geographic position: <c>[0]</c> longitude, <c>[1]</c> latitude, <c>[2]</c> height above sea level in meters.</param>
        /// <param name="atpress">Atmospheric pressure in hPa/mbar, used for the refraction correction.</param>
        /// <param name="attemp">Atmospheric temperature in degrees Celsius, used for the refraction correction.</param>
        /// <param name="dgsect">Receives the Gauquelin sector number.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns>Non-negative (<see cref="SwissEph.OK"/>) on success; negative (<see cref="SwissEph.ERR"/>) on error, with a message in <paramref name="serr"/>.</returns>
        public Int32 swe_gauquelin_sector(double t_ut, Int32 ipl, String starname, Int32 iflag, Int32 imeth, double[] geopos,
            double atpress, double attemp, ref double dgsect, ref string serr)
        {
            return SweCL.swe_gauquelin_sector(t_ut, ipl, starname, iflag, imeth, geopos, atpress, attemp, ref dgsect, ref serr);
        }

        /// <summary>
        /// computes geographic location and attributes of solar
        /// eclipse at a given tjd
        /// </summary>
        /// <remarks>
        /// For a solar eclipse already known to occur at <paramref name="tjd"/>, finds where on
        /// Earth it is central (or, for a non-central eclipse, where it is greatest).
        /// </remarks>
        /// <param name="tjd">Time of the eclipse, Universal Time (Julian day, UT).</param>
        /// <param name="ifl">
        /// Computation flags, e.g. <see cref="SEFLG_SWIEPH"/>, combined with the ephemeris flags accepted by <see cref="swe_calc(double, Int32, Int32, double[], ref string)"/>.
        /// </param>
        /// <param name="geopos">Receives the geographic position of the eclipse: <c>[0]</c> longitude, <c>[1]</c> latitude, in degrees.</param>
        /// <param name="attr">
        /// Receives eclipse attributes: <c>[0]</c> fraction of the Sun's diameter covered by the Moon,
        /// <c>[1]</c> ratio of the Moon's to the Sun's apparent diameter, <c>[2]</c> fraction of the Sun's disc
        /// obscured (area), <c>[3]</c> diameter of the Moon's core shadow on Earth in km, <c>[4]</c> azimuth of the
        /// Sun in degrees, <c>[5]</c> true altitude of the Sun in degrees, <c>[6]</c> apparent altitude of the Sun
        /// in degrees, <c>[7]</c> elongation of the Moon from the Sun in degrees, <c>[8]</c> eclipse magnitude,
        /// <c>[9]</c>/<c>[10]</c> Saros series number and Saros member number. Must have at least 20 elements.
        /// </param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns>
        /// Negative (<see cref="SwissEph.ERR"/>) on a fatal error; <c>0</c> if no eclipse is found at
        /// <paramref name="tjd"/>; otherwise a positive bitfield of <see cref="SE_ECL_TOTAL"/>/<see cref="SE_ECL_ANNULAR"/>/
        /// <see cref="SE_ECL_PARTIAL"/>/<see cref="SE_ECL_ANNULAR_TOTAL"/>/<see cref="SE_ECL_CENTRAL"/>/<see cref="SE_ECL_NONCENTRAL"/>
        /// describing the eclipse geometry found.
        /// </returns>
        public Int32 swe_sol_eclipse_where(double tjd, Int32 ifl, double[] geopos, double[] attr, ref string serr)
        {
            return SweCL.swe_sol_eclipse_where(tjd, ifl, geopos, attr, ref serr);
        }

        /// <summary>
        /// Computes, for a lunar occultation of a planet or star already known to occur at
        /// <paramref name="tjd"/>, the geographic location where it is central (or, for a
        /// non-central occultation, where it is greatest).
        /// </summary>
        /// <param name="tjd">Time of the occultation, Universal Time (Julian day, UT).</param>
        /// <param name="ipl">Body number, used when <paramref name="starname"/> is <c>null</c> or empty.</param>
        /// <param name="starname">Fixed star name, or <c>null</c>/<c>""</c> to use <paramref name="ipl"/> instead.</param>
        /// <param name="ifl">
        /// Computation flags, e.g. <see cref="SEFLG_SWIEPH"/>, combined with the ephemeris flags accepted by <see cref="swe_calc(double, Int32, Int32, double[], ref string)"/>.
        /// </param>
        /// <param name="geopos">Receives the geographic position of the occultation: <c>[0]</c> longitude, <c>[1]</c> latitude, in degrees.</param>
        /// <param name="attr">
        /// Receives occultation attributes with the same layout as <see cref="swe_sol_eclipse_where(double, Int32, double[], double[], ref string)"/>'s <c>attr</c>. Must have at least 20 elements.
        /// </param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns>
        /// Negative (<see cref="SwissEph.ERR"/>) on a fatal error; <c>0</c> if no occultation is found at
        /// <paramref name="tjd"/>; otherwise a positive bitfield describing the occultation geometry found, using
        /// the same <see cref="SE_ECL_TOTAL"/>-family bits as <see cref="swe_sol_eclipse_where(double, Int32, double[], double[], ref string)"/>.
        /// </returns>
        public Int32 swe_lun_occult_where(double tjd, Int32 ipl, string starname, Int32 ifl, double[] geopos, double[] attr, ref string serr)
        {
            return SweCL.swe_lun_occult_where(tjd, ipl, starname, ifl, geopos, attr, ref serr);
        }

        /// <summary>
        /// computes attributes of a solar eclipse for given tjd, geolon, geolat
        /// </summary>
        /// <remarks>
        /// Computes eclipse attributes as seen from a specific geographic position, unlike
        /// <see cref="swe_sol_eclipse_where(double, Int32, double[], double[], ref string)"/> which finds where the eclipse is central/greatest.
        /// </remarks>
        /// <param name="tjd">Time of the eclipse, Universal Time (Julian day, UT).</param>
        /// <param name="ifl">
        /// Computation flags, e.g. <see cref="SEFLG_SWIEPH"/>, combined with the ephemeris flags accepted by <see cref="swe_calc(double, Int32, Int32, double[], ref string)"/>.
        /// </param>
        /// <param name="geopos">Observer's geographic position: <c>[0]</c> longitude, <c>[1]</c> latitude, <c>[2]</c> height above sea level in meters.</param>
        /// <param name="attr">
        /// Receives eclipse attributes, same layout as <see cref="swe_sol_eclipse_where(double, Int32, double[], double[], ref string)"/>'s <c>attr</c>. Must have at least 20 elements.
        /// </param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns>
        /// Negative (<see cref="SwissEph.ERR"/>) on a fatal error; <c>0</c> if no eclipse is visible from
        /// <paramref name="geopos"/> at <paramref name="tjd"/>; otherwise a positive bitfield of eclipse-type and
        /// visibility bits (<see cref="SE_ECL_TOTAL"/>-family combined with <see cref="SE_ECL_VISIBLE"/>/<see cref="SE_ECL_MAX_VISIBLE"/>).
        /// </returns>
        public Int32 swe_sol_eclipse_how(double tjd, Int32 ifl, double[] geopos, double[] attr, ref string serr)
        {
            return SweCL.swe_sol_eclipse_how(tjd, ifl, geopos, attr, ref serr);
        }

        /// <summary>
        /// finds time of next occultation globally
        /// </summary>
        /// <remarks>
        /// swephexp.h declares this parameter <c>int32 backward</c>, a bitfield: bit 0 selects
        /// search direction, and OR-ing in <see cref="SE_ECL_ONE_TRY"/> (swecl.c:1539, :1593)
        /// limits the search to a single lunar cycle instead of continuing until an occultation
        /// is found. A prior <c>bool backward</c> signature here could only ever pass 0 or 1, so
        /// <c>backward &amp; SE_ECL_ONE_TRY</c> (32768) was provably always 0 and
        /// SE_ECL_ONE_TRY was unreachable through this API. Call this overload to request it; the
        /// <c>bool</c> overload below still exists for source compiled against the old signature,
        /// but cannot pass it.
        /// </remarks>
        /// <param name="tjd_start">Start time of the search, Universal Time (Julian day, UT).</param>
        /// <param name="ipl">Body number, used when <paramref name="starname"/> is <c>null</c> or empty.</param>
        /// <param name="starname">Fixed star name, or <c>null</c>/<c>""</c> to use <paramref name="ipl"/> instead.</param>
        /// <param name="ifl">
        /// Computation flags, e.g. <see cref="SEFLG_SWIEPH"/>, combined with the ephemeris flags accepted by <see cref="swe_calc(double, Int32, Int32, double[], ref string)"/>.
        /// </param>
        /// <param name="ifltype">Desired occultation-type bitmask (the <see cref="SE_ECL_TOTAL"/>-family bits), or <c>0</c> for any type.</param>
        /// <param name="tret">Receives timing details for the occultation phase found (at least <c>[0]</c> the time of greatest occultation). Must have at least 10 elements.</param>
        /// <param name="backward">
        /// Bitfield: bit 0 selects search direction (search backward in time if set, forward otherwise);
        /// OR-ing in <see cref="SE_ECL_ONE_TRY"/> limits the search to a single lunar cycle instead of
        /// continuing until an occultation is found.
        /// </param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns>
        /// Negative (<see cref="SwissEph.ERR"/>) on a fatal error; <c>0</c> if no occultation matching
        /// <paramref name="ifltype"/> was found (only possible with <see cref="SE_ECL_ONE_TRY"/>); otherwise a
        /// positive bitfield of <see cref="SE_ECL_TOTAL"/>-family bits describing the occultation geometry found.
        /// </returns>
        public Int32 swe_lun_occult_when_glob(double tjd_start, Int32 ipl, string starname, Int32 ifl, Int32 ifltype, double[] tret, Int32 backward, ref string serr)
        {
            return SweCL.swe_lun_occult_when_glob(tjd_start, ipl, starname, ifl, ifltype, tret, backward, ref serr);
        }

        /// <summary>
        /// finds time of next occultation globally
        /// </summary>
        /// <remarks>
        /// Source-compatibility overload for callers built against the pre-fix <c>bool
        /// backward</c> signature. It can only pass 0 or 1 and so can never request
        /// <see cref="SE_ECL_ONE_TRY"/> -- use the <c>Int32 backward</c> overload above for that.
        /// </remarks>
        /// <param name="tjd_start">Start time of the search, Universal Time (Julian day, UT).</param>
        /// <param name="ipl">Body number, used when <paramref name="starname"/> is <c>null</c> or empty.</param>
        /// <param name="starname">Fixed star name, or <c>null</c>/<c>""</c> to use <paramref name="ipl"/> instead.</param>
        /// <param name="ifl">
        /// Computation flags, e.g. <see cref="SEFLG_SWIEPH"/>, combined with the ephemeris flags accepted by <see cref="swe_calc(double, Int32, Int32, double[], ref string)"/>.
        /// </param>
        /// <param name="ifltype">Desired occultation-type bitmask (the <see cref="SE_ECL_TOTAL"/>-family bits), or <c>0</c> for any type.</param>
        /// <param name="tret">Receives timing details for the occultation phase found (at least <c>[0]</c> the time of greatest occultation). Must have at least 10 elements.</param>
        /// <param name="backward"><c>true</c> to search backward in time from <paramref name="tjd_start"/>; <c>false</c> to search forward.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns>
        /// Negative (<see cref="SwissEph.ERR"/>) on a fatal error; otherwise a positive bitfield of
        /// <see cref="SE_ECL_TOTAL"/>-family bits describing the occultation geometry found.
        /// </returns>
        public Int32 swe_lun_occult_when_glob(double tjd_start, Int32 ipl, string starname, Int32 ifl, Int32 ifltype, double[] tret, bool backward, ref string serr)
        {
            return swe_lun_occult_when_glob(tjd_start, ipl, starname, ifl, ifltype, tret, backward ? 1 : 0, ref serr);
        }

        /// <summary>
        /// finds time of next local eclipse
        /// </summary>
        /// <remarks>
        /// Searches forward or backward from <paramref name="tjd_start"/> for the next solar eclipse
        /// visible from <paramref name="geopos"/>.
        /// </remarks>
        /// <param name="tjd_start">Start time of the search, Universal Time (Julian day, UT).</param>
        /// <param name="ifl">
        /// Computation flags, e.g. <see cref="SEFLG_SWIEPH"/>, combined with the ephemeris flags accepted by <see cref="swe_calc(double, Int32, Int32, double[], ref string)"/>.
        /// </param>
        /// <param name="geopos">Observer's geographic position: <c>[0]</c> longitude, <c>[1]</c> latitude, <c>[2]</c> height above sea level in meters.</param>
        /// <param name="tret">
        /// Receives timing details for the eclipse found: <c>[0]</c> time of greatest eclipse, <c>[1]</c>-<c>[4]</c>
        /// contact times where applicable to the eclipse type found, remaining elements bracketing rise/set of the
        /// eclipsed body. Must have at least 10 elements.
        /// </param>
        /// <param name="attr">
        /// Receives eclipse attributes, same layout as <see cref="swe_sol_eclipse_where(double, Int32, double[], double[], ref string)"/>'s <c>attr</c>. Must have at least 20 elements.
        /// </param>
        /// <param name="backward"><c>true</c> to search backward in time from <paramref name="tjd_start"/>; <c>false</c> to search forward.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns>
        /// Negative (<see cref="SwissEph.ERR"/>) on a fatal error; otherwise a positive bitfield of
        /// <see cref="SE_ECL_TOTAL"/>-family bits (combined with visibility bits such as <see cref="SE_ECL_VISIBLE"/>)
        /// describing the eclipse found.
        /// </returns>
        public Int32 swe_sol_eclipse_when_loc(double tjd_start, Int32 ifl, double[] geopos, double[] tret, double[] attr, bool backward, ref string serr)
        {
            return SweCL.swe_sol_eclipse_when_loc(tjd_start, ifl, geopos, tret, attr, backward, ref serr);
        }

        /// <summary>
        /// finds time of next local occultation
        /// </summary>
        /// <remarks>
        /// Same <see cref="SE_ECL_ONE_TRY"/> bitfield as <see cref="swe_lun_occult_when_glob(double, Int32, string, Int32, Int32, double[], Int32, ref string)"/>
        /// (swephexp.h; occult_when_loc masks it at swecl.c:2436) -- see that overload's remarks.
        /// </remarks>
        /// <param name="tjd_start">Start time of the search, Universal Time (Julian day, UT).</param>
        /// <param name="ipl">Body number, used when <paramref name="starname"/> is <c>null</c> or empty.</param>
        /// <param name="starname">Fixed star name, or <c>null</c>/<c>""</c> to use <paramref name="ipl"/> instead.</param>
        /// <param name="ifl">
        /// Computation flags, e.g. <see cref="SEFLG_SWIEPH"/>, combined with the ephemeris flags accepted by <see cref="swe_calc(double, Int32, Int32, double[], ref string)"/>.
        /// </param>
        /// <param name="geopos">Observer's geographic position: <c>[0]</c> longitude, <c>[1]</c> latitude, <c>[2]</c> height above sea level in meters.</param>
        /// <param name="tret">Receives timing details for the occultation phase found (at least <c>[0]</c> the time of greatest occultation). Must have at least 10 elements.</param>
        /// <param name="attr">
        /// Receives occultation attributes, same layout as <see cref="swe_sol_eclipse_where(double, Int32, double[], double[], ref string)"/>'s <c>attr</c>. Must have at least 20 elements.
        /// </param>
        /// <param name="backward">
        /// Bitfield: bit 0 selects search direction (search backward in time if set, forward otherwise);
        /// OR-ing in <see cref="SE_ECL_ONE_TRY"/> limits the search to a single lunar cycle instead of
        /// continuing until an occultation is found.
        /// </param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns>
        /// Negative (<see cref="SwissEph.ERR"/>) on a fatal error; <c>0</c> if no occultation was found (only
        /// possible with <see cref="SE_ECL_ONE_TRY"/>); otherwise a positive bitfield of eclipse-type and
        /// visibility bits describing the occultation found.
        /// </returns>
        public Int32 swe_lun_occult_when_loc(double tjd_start, Int32 ipl, String starname, Int32 ifl, double[] geopos, double[] tret,
            double[] attr, Int32 backward, ref string serr)
        {
            return SweCL.swe_lun_occult_when_loc(tjd_start, ipl, starname, ifl, geopos, tret, attr, backward, ref serr);
        }

        /// <summary>
        /// finds time of next local occultation
        /// </summary>
        /// <remarks>
        /// Source-compatibility overload for callers built against the pre-fix <c>bool
        /// backward</c> signature; cannot request <see cref="SE_ECL_ONE_TRY"/> -- use the
        /// <c>Int32 backward</c> overload above for that.
        /// </remarks>
        /// <param name="tjd_start">Start time of the search, Universal Time (Julian day, UT).</param>
        /// <param name="ipl">Body number, used when <paramref name="starname"/> is <c>null</c> or empty.</param>
        /// <param name="starname">Fixed star name, or <c>null</c>/<c>""</c> to use <paramref name="ipl"/> instead.</param>
        /// <param name="ifl">
        /// Computation flags, e.g. <see cref="SEFLG_SWIEPH"/>, combined with the ephemeris flags accepted by <see cref="swe_calc(double, Int32, Int32, double[], ref string)"/>.
        /// </param>
        /// <param name="geopos">Observer's geographic position: <c>[0]</c> longitude, <c>[1]</c> latitude, <c>[2]</c> height above sea level in meters.</param>
        /// <param name="tret">Receives timing details for the occultation phase found (at least <c>[0]</c> the time of greatest occultation). Must have at least 10 elements.</param>
        /// <param name="attr">
        /// Receives occultation attributes, same layout as <see cref="swe_sol_eclipse_where(double, Int32, double[], double[], ref string)"/>'s <c>attr</c>. Must have at least 20 elements.
        /// </param>
        /// <param name="backward"><c>true</c> to search backward in time from <paramref name="tjd_start"/>; <c>false</c> to search forward.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns>
        /// Negative (<see cref="SwissEph.ERR"/>) on a fatal error; otherwise a positive bitfield of eclipse-type
        /// and visibility bits describing the occultation found.
        /// </returns>
        public Int32 swe_lun_occult_when_loc(double tjd_start, Int32 ipl, String starname, Int32 ifl, double[] geopos, double[] tret,
            double[] attr, bool backward, ref string serr)
        {
            return swe_lun_occult_when_loc(tjd_start, ipl, starname, ifl, geopos, tret, attr, backward ? 1 : 0, ref serr);
        }

        /// <summary>
        /// finds time of next eclipse globally
        /// </summary>
        /// <remarks>
        /// Searches globally (anywhere on Earth) for the next solar eclipse matching <paramref name="ifltype"/>.
        /// </remarks>
        /// <param name="tjd_start">Start time of the search, Universal Time (Julian day, UT).</param>
        /// <param name="ifl">
        /// Computation flags, e.g. <see cref="SEFLG_SWIEPH"/>, combined with the ephemeris flags accepted by <see cref="swe_calc(double, Int32, Int32, double[], ref string)"/>.
        /// </param>
        /// <param name="ifltype">Desired eclipse-type bitmask (the <see cref="SE_ECL_TOTAL"/>-family bits), or <c>0</c> for any type.</param>
        /// <param name="tret">
        /// Receives timing details for the eclipse found: <c>[0]</c> time of greatest eclipse, <c>[1]</c>-<c>[4]</c>
        /// contact times where applicable to the eclipse type found, remaining elements per the eclipse's
        /// center-line/annular-total transition where applicable. Must have at least 10 elements.
        /// </param>
        /// <param name="backward"><c>true</c> to search backward in time from <paramref name="tjd_start"/>; <c>false</c> to search forward.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns>
        /// Negative (<see cref="SwissEph.ERR"/>) on a fatal error; otherwise a positive bitfield of
        /// <see cref="SE_ECL_TOTAL"/>-family bits describing the eclipse found.
        /// </returns>
        public Int32 swe_sol_eclipse_when_glob(double tjd_start, Int32 ifl, Int32 ifltype, double[] tret, bool backward, ref string serr)
        {
            return SweCL.swe_sol_eclipse_when_glob(tjd_start, ifl, ifltype, tret, backward, ref serr);
        }

        /// <summary>
        /// computes attributes of a lunar eclipse for given tjd
        /// </summary>
        /// <remarks>
        /// Lunar eclipses are visible from an entire hemisphere, so this is mostly about local
        /// circumstances (e.g. local altitude) rather than visibility itself.
        /// </remarks>
        /// <param name="tjd_ut">Time of the eclipse, Universal Time (Julian day, UT).</param>
        /// <param name="ifl">
        /// Computation flags, e.g. <see cref="SEFLG_SWIEPH"/>, combined with the ephemeris flags accepted by <see cref="swe_calc(double, Int32, Int32, double[], ref string)"/>.
        /// </param>
        /// <param name="geopos">Observer's geographic position: <c>[0]</c> longitude, <c>[1]</c> latitude, <c>[2]</c> height above sea level in meters.</param>
        /// <param name="attr">
        /// Receives eclipse attributes for Earth's shadow, using the same index layout as
        /// <see cref="swe_sol_eclipse_where(double, Int32, double[], double[], ref string)"/>'s <c>attr</c> (adapted to Earth's shadow instead of
        /// the Moon's). Must have at least 20 elements.
        /// </param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns>
        /// Negative (<see cref="SwissEph.ERR"/>) on a fatal error; <c>0</c> if no lunar eclipse is occurring at
        /// <paramref name="tjd_ut"/>; otherwise a positive bitfield of <see cref="SE_ECL_TOTAL"/>/<see cref="SE_ECL_PARTIAL"/>/
        /// <see cref="SE_ECL_PENUMBRAL"/> describing the eclipse geometry found.
        /// </returns>
        public Int32 swe_lun_eclipse_how(double tjd_ut, Int32 ifl, double[] geopos, double[] attr, ref string serr)
        {
            return SweCL.swe_lun_eclipse_how(tjd_ut, ifl, geopos, attr, ref serr);
        }

        /// <summary>
        /// Searches forward or backward from <paramref name="tjd_start"/> for the next lunar eclipse
        /// matching <paramref name="ifltype"/>.
        /// </summary>
        /// <param name="tjd_start">Start time of the search, Universal Time (Julian day, UT).</param>
        /// <param name="ifl">
        /// Computation flags, e.g. <see cref="SEFLG_SWIEPH"/>, combined with the ephemeris flags accepted by <see cref="swe_calc(double, Int32, Int32, double[], ref string)"/>.
        /// </param>
        /// <param name="ifltype">Desired eclipse-type bitmask (<see cref="SE_ECL_PENUMBRAL"/>/<see cref="SE_ECL_PARTIAL"/>/<see cref="SE_ECL_TOTAL"/>), or <c>0</c> for any type.</param>
        /// <param name="tret">
        /// Receives timing details for the eclipse found: <c>[0]</c> time of greatest eclipse, <c>[1]</c>-<c>[4]</c>
        /// penumbral/umbral contact times where applicable to the eclipse type found. Must have at least 10 elements.
        /// </param>
        /// <param name="backward"><c>true</c> to search backward in time from <paramref name="tjd_start"/>; <c>false</c> to search forward.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns>
        /// Negative (<see cref="SwissEph.ERR"/>) on a fatal error; otherwise a positive bitfield of
        /// <see cref="SE_ECL_TOTAL"/>/<see cref="SE_ECL_PARTIAL"/>/<see cref="SE_ECL_PENUMBRAL"/> describing the eclipse found.
        /// </returns>
        public Int32 swe_lun_eclipse_when(double tjd_start, Int32 ifl, Int32 ifltype, double[] tret, bool backward, ref string serr)
        {
            return SweCL.swe_lun_eclipse_when(tjd_start, ifl, ifltype, tret, backward, ref serr);
        }

        /// <summary>
        /// Like <see cref="swe_lun_eclipse_when(double, Int32, Int32, double[], bool, ref string)"/>, but
        /// restricted to eclipses visible (Moon above the horizon) from <paramref name="geopos"/>, and also
        /// returns eclipse attributes for the found eclipse.
        /// </summary>
        /// <param name="tjd_start">Start time of the search, Universal Time (Julian day, UT).</param>
        /// <param name="ifl">
        /// Computation flags, e.g. <see cref="SEFLG_SWIEPH"/>, combined with the ephemeris flags accepted by <see cref="swe_calc(double, Int32, Int32, double[], ref string)"/>.
        /// </param>
        /// <param name="geopos">Observer's geographic position: <c>[0]</c> longitude, <c>[1]</c> latitude, <c>[2]</c> height above sea level in meters.</param>
        /// <param name="tret">
        /// Receives timing details for the eclipse found: <c>[0]</c> time of greatest eclipse, <c>[1]</c>-<c>[4]</c>
        /// penumbral/umbral contact times where applicable, remaining elements bracketing rise/set of the Moon.
        /// Must have at least 10 elements.
        /// </param>
        /// <param name="attr">
        /// Receives eclipse attributes for Earth's shadow, same layout as
        /// <see cref="swe_lun_eclipse_how(double, Int32, double[], double[], ref string)"/>'s <c>attr</c>. Must have at least 20 elements.
        /// </param>
        /// <param name="backward"><c>true</c> to search backward in time from <paramref name="tjd_start"/>; <c>false</c> to search forward.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns>
        /// Negative (<see cref="SwissEph.ERR"/>) on a fatal error; otherwise a positive bitfield of
        /// <see cref="SE_ECL_TOTAL"/>-family bits (combined with visibility bits) describing the eclipse found.
        /// </returns>
        public Int32 swe_lun_eclipse_when_loc(double tjd_start, Int32 ifl, double[] geopos, double[] tret, double[] attr, bool backward, ref string serr)
        {
            return SweCL.swe_lun_eclipse_when_loc(tjd_start, ifl, geopos, tret, attr, backward, ref serr);
        }

        /// <summary>
        /// planetary phenomena
        /// </summary>
        /// <remarks>
        /// Computes visual phenomena of a body (not an eclipse), such as phase and apparent magnitude.
        /// </remarks>
        /// <param name="tjd">Time of the computation, Ephemeris/Terrestrial Time (Julian day, ET/TT).</param>
        /// <param name="ipl">Body number.</param>
        /// <param name="iflag">
        /// Computation flags, e.g. <see cref="SEFLG_SWIEPH"/>, combined with the ephemeris flags accepted by <see cref="swe_calc(double, Int32, Int32, double[], ref string)"/>.
        /// </param>
        /// <param name="attr">
        /// Receives the computed phenomena: <c>[0]</c> phase angle (Sun-body-Earth angle, degrees),
        /// <c>[1]</c> illuminated fraction of the disc (phase, 0-1), <c>[2]</c> elongation from the Sun
        /// (degrees), <c>[3]</c> apparent diameter of the disc (degrees), <c>[4]</c> apparent visual
        /// magnitude. Must have at least 20 elements.
        /// </param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns><see cref="SwissEph.OK"/> (non-negative) on success; a negative value (<see cref="SwissEph.ERR"/>) on error, with a message in <paramref name="serr"/>.</returns>
        public Int32 swe_pheno(double tjd, Int32 ipl, Int32 iflag, double[] attr, ref string serr)
        {
            return SweCL.swe_pheno(tjd, ipl, iflag, attr, ref serr);
        }

        /// <summary>
        /// Like <see cref="swe_pheno(double, Int32, Int32, double[], ref string)"/>, but takes
        /// <paramref name="tjd_ut"/> in Universal Time instead of Ephemeris Time.
        /// </summary>
        /// <param name="tjd_ut">Time of the computation, Universal Time (Julian day, UT).</param>
        /// <param name="ipl">Body number.</param>
        /// <param name="iflag">
        /// Computation flags, e.g. <see cref="SEFLG_SWIEPH"/>, combined with the ephemeris flags accepted by <see cref="swe_calc(double, Int32, Int32, double[], ref string)"/>.
        /// </param>
        /// <param name="attr">
        /// Receives the computed phenomena, same layout as <see cref="swe_pheno(double, Int32, Int32, double[], ref string)"/>'s <c>attr</c>. Must have at least 20 elements.
        /// </param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns><see cref="SwissEph.OK"/> (non-negative) on success; a negative value (<see cref="SwissEph.ERR"/>) on error, with a message in <paramref name="serr"/>.</returns>
        public Int32 swe_pheno_ut(double tjd_ut, Int32 ipl, Int32 iflag, double[] attr, ref string serr)
        {
            return SweCL.swe_pheno_ut(tjd_ut, ipl, iflag, attr, ref serr);
        }

        /// <summary>
        /// Computes atmospheric refraction for a given altitude.
        /// </summary>
        /// <param name="inalt">Input altitude, degrees; apparent or true, depending on <paramref name="calc_flag"/>.</param>
        /// <param name="atpress">Atmospheric pressure in hPa/mbar.</param>
        /// <param name="attemp">Atmospheric temperature in degrees Celsius.</param>
        /// <param name="calc_flag">
        /// <see cref="SE_APP_TO_TRUE"/> if <paramref name="inalt"/> is the apparent altitude and the true
        /// (geometric) altitude should be returned, or <see cref="SE_TRUE_TO_APP"/> for the reverse.
        /// </param>
        /// <returns>The refraction-adjusted altitude in degrees.</returns>
        public double swe_refrac(double inalt, double atpress, double attemp, Int32 calc_flag)
        {
            return SweCL.swe_refrac(inalt, atpress, attemp, calc_flag);
        }

        /// <summary>
        /// Like <see cref="swe_refrac(double, double, double, Int32)"/>, but also accounts for the
        /// observer's own altitude above sea level and a custom temperature lapse rate, and returns
        /// more detail via <paramref name="dret"/>.
        /// </summary>
        /// <param name="inalt">Input altitude, degrees; apparent or true, depending on <paramref name="calc_flag"/>.</param>
        /// <param name="geoalt">Observer's own height above sea level, in meters.</param>
        /// <param name="atpress">Atmospheric pressure in hPa/mbar.</param>
        /// <param name="attemp">Atmospheric temperature in degrees Celsius.</param>
        /// <param name="lapse_rate">Temperature lapse rate, degrees Celsius per meter of altitude change.</param>
        /// <param name="calc_flag">
        /// <see cref="SE_APP_TO_TRUE"/> if <paramref name="inalt"/> is the apparent altitude and the true
        /// (geometric) altitude should be returned, or <see cref="SE_TRUE_TO_APP"/> for the reverse.
        /// </param>
        /// <param name="dret">
        /// Receives further detail: <c>[0]</c> refraction applied (degrees), <c>[1]</c> true altitude,
        /// <c>[2]</c> apparent altitude, with further elements per upstream. Must have at least 4 elements.
        /// </param>
        /// <returns>The refraction-adjusted altitude in degrees.</returns>
        public double swe_refrac_extended(double inalt, double geoalt, double atpress, double attemp, double lapse_rate, Int32 calc_flag, double[] dret)
        {
            return SweCL.swe_refrac_extended(inalt, geoalt, atpress, attemp, lapse_rate, calc_flag, dret);
        }

        /// <summary>
        /// Sets the default temperature lapse rate (degrees Celsius per meter) used by refraction
        /// calculations that don't take one explicitly.
        /// </summary>
        /// <param name="lapse_rate">Temperature lapse rate, degrees Celsius per meter of altitude change.</param>
        public void swe_set_lapse_rate(double lapse_rate)
        {
            SweCL.swe_set_lapse_rate(lapse_rate);
        }

        /// <summary>
        /// Converts ecliptic or equatorial coordinates to horizontal (azimuth/altitude) coordinates
        /// as seen from a given geographic position at a given time.
        /// </summary>
        /// <param name="tjd_ut">Time of the computation, Universal Time (Julian day, UT).</param>
        /// <param name="calc_flag"><see cref="SE_ECL2HOR"/> if <paramref name="xin"/> is ecliptic longitude/latitude, or <see cref="SE_EQU2HOR"/> if it is right ascension/declination.</param>
        /// <param name="geopos">Observer's geographic position: <c>[0]</c> longitude, <c>[1]</c> latitude, <c>[2]</c> height above sea level in meters.</param>
        /// <param name="atpress">Atmospheric pressure in hPa/mbar, used for the refraction correction.</param>
        /// <param name="attemp">Atmospheric temperature in degrees Celsius, used for the refraction correction.</param>
        /// <param name="xin">Input coordinates: <c>[0]</c> ecliptic longitude or right ascension, <c>[1]</c> ecliptic latitude or declination, both in degrees.</param>
        /// <param name="xaz">
        /// Receives the horizontal coordinates: <c>[0]</c> azimuth in degrees (measured from north),
        /// <c>[1]</c> true altitude in degrees, <c>[2]</c> apparent (refraction-corrected) altitude in degrees.
        /// </param>
        public void swe_azalt(double tjd_ut, Int32 calc_flag, double[] geopos, double atpress, double attemp, double[] xin, double[] xaz)
        {
            SweCL.swe_azalt(tjd_ut, calc_flag, geopos, atpress, attemp, xin, xaz);
        }

        /// <summary>
        /// The inverse of <see cref="swe_azalt(double, Int32, double[], double, double, double[], double[])"/>:
        /// converts azimuth/altitude back to ecliptic or equatorial coordinates.
        /// </summary>
        /// <param name="tjd_ut">Time of the computation, Universal Time (Julian day, UT).</param>
        /// <param name="calc_flag"><see cref="SE_HOR2ECL"/> to return ecliptic coordinates, or <see cref="SE_HOR2EQU"/> to return equatorial coordinates.</param>
        /// <param name="geopos">Observer's geographic position: <c>[0]</c> longitude, <c>[1]</c> latitude, <c>[2]</c> height above sea level in meters.</param>
        /// <param name="xin">Input horizontal coordinates: <c>[0]</c> azimuth in degrees, <c>[1]</c> altitude (true or apparent) in degrees.</param>
        /// <param name="xout">Receives the output coordinates: <c>[0]</c> ecliptic longitude or right ascension, <c>[1]</c> ecliptic latitude or declination, both in degrees.</param>
        public void swe_azalt_rev(double tjd_ut, Int32 calc_flag, double[] geopos, double[] xin, double[] xout)
        {
            SweCL.swe_azalt_rev(tjd_ut, calc_flag, geopos, xin, xout);
        }

        /// <summary>
        /// Like <see cref="swe_rise_trans(double, Int32, string, Int32, Int32, double[], double, double, ref double, ref string)"/>,
        /// with an added local-horizon-altitude parameter for observers with an obstructed or elevated horizon (e.g. a mountain skyline).
        /// </summary>
        /// <param name="tjd_ut">Start time of the search, Universal Time (Julian day, UT).</param>
        /// <param name="ipl">Body number, used when <paramref name="starname"/> is <c>null</c> or empty.</param>
        /// <param name="starname">Fixed star name, or <c>null</c>/<c>""</c> to use <paramref name="ipl"/> instead.</param>
        /// <param name="epheflag">
        /// Ephemeris flags, e.g. <see cref="SEFLG_SWIEPH"/>, as accepted by <see cref="swe_calc(double, Int32, Int32, double[], ref string)"/>.
        /// </param>
        /// <param name="rsmi">
        /// Which event to search for: <see cref="SE_CALC_RISE"/>, <see cref="SE_CALC_SET"/>, <see cref="SE_CALC_MTRANSIT"/>
        /// (upper transit), or <see cref="SE_CALC_ITRANSIT"/> (lower transit), optionally OR-ed with refraction/disc-edge
        /// bits such as <see cref="SE_BIT_DISC_CENTER"/> or <see cref="SE_BIT_NO_REFRACTION"/>.
        /// </param>
        /// <param name="geopos">Observer's geographic position: <c>[0]</c> longitude, <c>[1]</c> latitude, <c>[2]</c> height above sea level in meters.</param>
        /// <param name="atpress">Atmospheric pressure in hPa/mbar, used for the refraction correction.</param>
        /// <param name="attemp">Atmospheric temperature in degrees Celsius, used for the refraction correction.</param>
        /// <param name="horhgt">Altitude, in degrees, of the local true horizon above (positive) or below (negative) the astronomical horizon, used instead of 0.</param>
        /// <param name="tret">Receives the event's time, Universal Time (Julian day, UT).</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns><see cref="SwissEph.OK"/> on success; <c>0</c> if the event does not occur (e.g. a circumpolar body); a negative value (<see cref="SwissEph.ERR"/>) on error, with a message in <paramref name="serr"/>.</returns>
        public Int32 swe_rise_trans_true_hor(double tjd_ut, Int32 ipl, string starname,
                   Int32 epheflag, Int32 rsmi, double[] geopos, double atpress, double attemp,
                   double horhgt, ref double tret, ref string serr)
        {
            return SweCL.swe_rise_trans_true_hor(tjd_ut, ipl, starname,
                   epheflag, rsmi, geopos, atpress, attemp,
                   horhgt, ref tret, ref serr);
        }

        /// <summary>
        /// Finds the next rising, setting, or meridian transit of a body or star after a given time.
        /// </summary>
        /// <param name="tjd_ut">Start time of the search, Universal Time (Julian day, UT).</param>
        /// <param name="ipl">Body number, used when <paramref name="starname"/> is <c>null</c> or empty.</param>
        /// <param name="starname">Fixed star name, or <c>null</c>/<c>""</c> to use <paramref name="ipl"/> instead.</param>
        /// <param name="epheflag">
        /// Ephemeris flags, e.g. <see cref="SEFLG_SWIEPH"/>, as accepted by <see cref="swe_calc(double, Int32, Int32, double[], ref string)"/>.
        /// </param>
        /// <param name="rsmi">
        /// Which event to search for: <see cref="SE_CALC_RISE"/>, <see cref="SE_CALC_SET"/>, <see cref="SE_CALC_MTRANSIT"/>
        /// (upper transit), or <see cref="SE_CALC_ITRANSIT"/> (lower transit), optionally OR-ed with refraction/disc-edge
        /// bits such as <see cref="SE_BIT_DISC_CENTER"/> or <see cref="SE_BIT_NO_REFRACTION"/>.
        /// </param>
        /// <param name="geopos">Observer's geographic position: <c>[0]</c> longitude, <c>[1]</c> latitude, <c>[2]</c> height above sea level in meters.</param>
        /// <param name="atpress">Atmospheric pressure in hPa/mbar, used for the refraction correction.</param>
        /// <param name="attemp">Atmospheric temperature in degrees Celsius, used for the refraction correction.</param>
        /// <param name="tret">Receives the event's time, Universal Time (Julian day, UT).</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns><see cref="SwissEph.OK"/> on success; <c>0</c> if the event does not occur (e.g. a circumpolar body); a negative value (<see cref="SwissEph.ERR"/>) on error, with a message in <paramref name="serr"/>.</returns>
        public Int32 swe_rise_trans(double tjd_ut, Int32 ipl, string starname, Int32 epheflag, Int32 rsmi,
            double[] geopos, double atpress, double attemp, ref double tret, ref string serr)
        {
            return SweCL.swe_rise_trans(tjd_ut, ipl, starname, epheflag, rsmi, geopos, atpress, attemp, ref tret, ref serr);
        }

        /// <summary>
        /// Computes a planet's orbital nodes and apsides.
        /// </summary>
        /// <param name="tjd_et">Time of the computation, Ephemeris/Terrestrial Time (Julian day, ET/TT).</param>
        /// <param name="ipl">Body number.</param>
        /// <param name="iflag">
        /// Computation flags, e.g. <see cref="SEFLG_SWIEPH"/>, combined with the ephemeris/coordinate flags accepted by <see cref="swe_calc(double, Int32, Int32, double[], ref string)"/>.
        /// </param>
        /// <param name="method">
        /// <see cref="SE_NODBIT_MEAN"/> (mean elements, default), <see cref="SE_NODBIT_OSCU"/> (osculating), or
        /// <see cref="SE_NODBIT_OSCU_BAR"/> (osculating from barycentric elements), optionally OR-ed with
        /// <see cref="SE_NODBIT_FOPOINT"/> to return the orbit's second focal point in <paramref name="xaphe"/> instead of the aphelion.
        /// </param>
        /// <param name="xnasc">Receives the ascending node, in the same longitude/latitude/distance/speed layout as <see cref="swe_calc(double, Int32, Int32, double[], ref string)"/>'s <c>xx</c>. Must have at least 6 elements.</param>
        /// <param name="xndsc">Receives the descending node, same layout as <paramref name="xnasc"/>. Must have at least 6 elements.</param>
        /// <param name="xperi">Receives the perihelion, same layout as <paramref name="xnasc"/>. Must have at least 6 elements.</param>
        /// <param name="xaphe">Receives the aphelion (or, with <see cref="SE_NODBIT_FOPOINT"/>, the orbit's second focal point), same layout as <paramref name="xnasc"/>. Must have at least 6 elements.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns><see cref="SwissEph.OK"/> (non-negative) on success; a negative value (<see cref="SwissEph.ERR"/>) on error, with a message in <paramref name="serr"/>.</returns>
        public Int32 swe_nod_aps(double tjd_et, Int32 ipl, Int32 iflag,
                              Int32 method,
                              double[] xnasc, double[] xndsc,
                              double[] xperi, double[] xaphe,
                              ref string serr)
        {
            return SweCL.swe_nod_aps(tjd_et, ipl, iflag, method, xnasc, xndsc, xperi, xaphe, ref serr);
        }

        /// <summary>
        /// Like <see cref="swe_nod_aps(double, Int32, Int32, Int32, double[], double[], double[], double[], ref string)"/>,
        /// but takes <paramref name="tjd_ut"/> in Universal Time instead of Ephemeris Time.
        /// </summary>
        /// <param name="tjd_ut">Time of the computation, Universal Time (Julian day, UT).</param>
        /// <param name="ipl">Body number.</param>
        /// <param name="iflag">
        /// Computation flags, e.g. <see cref="SEFLG_SWIEPH"/>, combined with the ephemeris/coordinate flags accepted by <see cref="swe_calc(double, Int32, Int32, double[], ref string)"/>.
        /// </param>
        /// <param name="method">
        /// <see cref="SE_NODBIT_MEAN"/> (mean elements, default), <see cref="SE_NODBIT_OSCU"/> (osculating), or
        /// <see cref="SE_NODBIT_OSCU_BAR"/> (osculating from barycentric elements), optionally OR-ed with
        /// <see cref="SE_NODBIT_FOPOINT"/> to return the orbit's second focal point in <paramref name="xaphe"/> instead of the aphelion.
        /// </param>
        /// <param name="xnasc">Receives the ascending node, in the same longitude/latitude/distance/speed layout as <see cref="swe_calc(double, Int32, Int32, double[], ref string)"/>'s <c>xx</c>. Must have at least 6 elements.</param>
        /// <param name="xndsc">Receives the descending node, same layout as <paramref name="xnasc"/>. Must have at least 6 elements.</param>
        /// <param name="xperi">Receives the perihelion, same layout as <paramref name="xnasc"/>. Must have at least 6 elements.</param>
        /// <param name="xaphe">Receives the aphelion (or, with <see cref="SE_NODBIT_FOPOINT"/>, the orbit's second focal point), same layout as <paramref name="xnasc"/>. Must have at least 6 elements.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns><see cref="SwissEph.OK"/> (non-negative) on success; a negative value (<see cref="SwissEph.ERR"/>) on error, with a message in <paramref name="serr"/>.</returns>
        public Int32 swe_nod_aps_ut(double tjd_ut, Int32 ipl, Int32 iflag,
                              Int32 method,
                              double[] xnasc, double[] xndsc,
                              double[] xperi, double[] xaphe,
                              ref string serr)
        {
            return SweCL.swe_nod_aps_ut(tjd_ut, ipl, iflag, method, xnasc, xndsc, xperi, xaphe, ref serr);
        }

        /// <summary>
        /// Computes a body's osculating Kepler orbital elements at a given time.
        /// </summary>
        /// <param name="tjd_et">Time of the computation, Ephemeris/Terrestrial Time (Julian day, ET/TT).</param>
        /// <param name="ipl">Body number.</param>
        /// <param name="iflag">
        /// Computation flags, e.g. <see cref="SEFLG_SWIEPH"/>, combined with the ephemeris/coordinate flags accepted by <see cref="swe_calc(double, Int32, Int32, double[], ref string)"/>.
        /// </param>
        /// <param name="dret">
        /// Receives the orbital elements: <c>[0]</c> semi-major axis (AU), <c>[1]</c> eccentricity,
        /// <c>[2]</c> inclination (degrees), <c>[3]</c> longitude of ascending node (degrees), <c>[4]</c>
        /// argument of perihelion (degrees), <c>[5]</c> longitude of perihelion (degrees), <c>[6]</c> mean
        /// anomaly at <paramref name="tjd_et"/> (degrees), <c>[10]</c> sidereal orbital period (years),
        /// <c>[11]</c> mean daily motion (degrees/day), <c>[13]</c> synodic period (days), <c>[14]</c> time
        /// of perihelion passage (Julian day), <c>[15]</c> perihelion distance (AU), <c>[16]</c> aphelion
        /// distance (AU). Must have at least 50 elements.
        /// </param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns><see cref="SwissEph.OK"/> (non-negative) on success; a negative value (<see cref="SwissEph.ERR"/>) on error, with a message in <paramref name="serr"/>.</returns>
        public Int32 swe_get_orbital_elements(
          double tjd_et, Int32 ipl, Int32 iflag, double[] dret, ref string serr)
        {
            return SweCL.swe_get_orbital_elements(tjd_et, ipl, iflag, dret, ref serr);
        }

        /// <summary>
        /// Computes a body's maximum possible distance (aphelion), minimum possible distance
        /// (perihelion), and its actual/true distance at a given time.
        /// </summary>
        /// <param name="tjd_et">Time of the computation, Ephemeris/Terrestrial Time (Julian day, ET/TT).</param>
        /// <param name="ipl">Body number.</param>
        /// <param name="iflag">
        /// Computation flags, combined with the ephemeris/coordinate flags accepted by <see cref="swe_calc(double, Int32, Int32, double[], ref string)"/>.
        /// Include <see cref="SEFLG_HELCTR"/> to request heliocentric (Sun-centered) distances; without
        /// it, distances are relative to whichever center the orbital-elements computation otherwise
        /// uses (see <see cref="swe_get_orbital_elements"/>).
        /// </param>
        /// <param name="dmax">Receives the maximum possible distance (aphelion), in AU.</param>
        /// <param name="dmin">Receives the minimum possible distance (perihelion), in AU.</param>
        /// <param name="dtrue">Receives the actual/true distance at <paramref name="tjd_et"/>, in AU.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns><see cref="SwissEph.OK"/> (non-negative) on success; a negative value (<see cref="SwissEph.ERR"/>) on error, with a message in <paramref name="serr"/>.</returns>
        public Int32 swe_orbit_max_min_true_distance(double tjd_et, Int32 ipl, Int32 iflag, ref double dmax, ref double dmin, ref double dtrue, ref string serr)
        {
            return SweCL.swe_orbit_max_min_true_distance(tjd_et, ipl, iflag, ref dmax, ref dmin, ref dtrue, ref serr);
        }

        /**************************** 
         * exports from swephlib.c 
         ****************************/

        /// <summary>
        /// delta t. Returns Delta T (ET/TT minus UT1, in days) for Julian day <paramref name="tjd"/>
        /// (UT), using whichever Delta T model is currently selected (see
        /// <see cref="swe_set_astro_models"/>).
        /// </summary>
        /// <param name="tjd">Julian day number, Universal Time.</param>
        /// <returns>Delta T (ET/TT - UT1), in days.</returns>
        public double swe_deltat(double tjd) { return SwephLib.swe_deltat(tjd); }
        /// <summary>
        /// Same as <see cref="swe_deltat(double)"/>, but lets <paramref name="iflag"/> select which
        /// ephemeris (and therefore Delta T table/model) to use, and reports an error via
        /// <paramref name="serr"/> if that ephemeris' data isn't available at <paramref name="tjd"/>,
        /// instead of silently falling back to a different model.
        /// </summary>
        /// <param name="tjd">Julian day number, Universal Time.</param>
        /// <param name="iflag">Ephemeris-selection flags, e.g. <see cref="SEFLG_SWIEPH"/>,
        /// <see cref="SEFLG_JPLEPH"/>, <see cref="SEFLG_MOSEPH"/>.</param>
        /// <param name="serr">Receives an error description if the requested ephemeris' data is
        /// unavailable at <paramref name="tjd"/>; otherwise unchanged.</param>
        /// <returns>Delta T (ET/TT - UT1), in days.</returns>
        public double swe_deltat_ex(double tjd, Int32 iflag, ref string serr) { return SwephLib.swe_deltat_ex(tjd, iflag, ref serr); }

        /// <summary>
        /// equation of time. Computes the difference between mean solar time and apparent (true)
        /// solar time at Julian day <paramref name="tjd"/> (UT).
        /// </summary>
        /// <param name="tjd">Julian day number, Universal Time.</param>
        /// <param name="e">Receives the equation of time, in days (a fraction of a day; multiply by
        /// 1440 for minutes).</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns><see cref="SwissEph.OK"/> on success, <see cref="SwissEph.ERR"/> on error.</returns>
        public int swe_time_equ(double tjd, out double e, ref string serr) { return Sweph.swe_time_equ(tjd, out e, ref serr); }
        /// <summary>
        /// Converts Local Mean Time (a Julian day already adjusted for a location's mean-time offset
        /// from UT) to Local Apparent (true solar) Time, by applying the equation of time (see
        /// <see cref="swe_time_equ"/>).
        /// </summary>
        /// <param name="tjd_lmt">Local Mean Time, expressed as a Julian day number.</param>
        /// <param name="geolon">Geographic longitude, in degrees (east positive).</param>
        /// <param name="tjd_lat">Receives the corresponding Local Apparent Time, as a Julian day
        /// number.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns><see cref="SwissEph.OK"/> on success, <see cref="SwissEph.ERR"/> on error.</returns>
        public int swe_lmt_to_lat(double tjd_lmt, double geolon, out double tjd_lat, ref string serr)
        {
            return Sweph.swe_lmt_to_lat(tjd_lmt, geolon, out tjd_lat, ref serr);
        }
        /// <summary>
        /// The inverse of <see cref="swe_lmt_to_lat"/>: converts Local Apparent (true solar) Time to
        /// Local Mean Time.
        /// </summary>
        /// <param name="tjd_lat">Local Apparent Time, expressed as a Julian day number.</param>
        /// <param name="geolon">Geographic longitude, in degrees (east positive).</param>
        /// <param name="tjd_lmt">Receives the corresponding Local Mean Time, as a Julian day number.</param>
        /// <param name="serr">Receives an error description if the call fails; otherwise unchanged.</param>
        /// <returns><see cref="SwissEph.OK"/> on success, <see cref="SwissEph.ERR"/> on error.</returns>
        public int swe_lat_to_lmt(double tjd_lat, double geolon, out double tjd_lmt, ref string serr)
        {
            return Sweph.swe_lat_to_lmt(tjd_lat, geolon, out tjd_lmt, ref serr);
        }


        /// <summary>
        /// sidereal time. Computes Greenwich apparent sidereal time for Julian day
        /// <paramref name="tjd_ut"/> (UT), given the obliquity of the ecliptic and the nutation in
        /// longitude explicitly, rather than computing them internally.
        /// </summary>
        /// <param name="tjd_ut">Julian day number, Universal Time.</param>
        /// <param name="ecl">Obliquity of the ecliptic (mean or true, as appropriate to the caller),
        /// in degrees.</param>
        /// <param name="nut">Nutation in longitude, in degrees.</param>
        /// <returns>Greenwich apparent sidereal time, in hours (0..24).</returns>
        public double swe_sidtime0(double tjd_ut, double ecl, double nut) { return SwephLib.swe_sidtime0(tjd_ut, ecl, nut); }
        /// <summary>
        /// Same as <see cref="swe_sidtime0"/>, computing the obliquity of the ecliptic and the
        /// nutation internally instead of taking them as parameters.
        /// </summary>
        /// <param name="tjd_ut">Julian day number, Universal Time.</param>
        /// <returns>Greenwich apparent sidereal time, in hours (0..24).</returns>
        public double swe_sidtime(double tjd_ut) { return SwephLib.swe_sidtime(tjd_ut); }
        /// <summary>
        /// Controls whether the nutation used internally by <see cref="swe_sidtime"/> and other
        /// calculations is interpolated from a coarser step table for speed, at a small accuracy
        /// cost, or computed exactly on every call.
        /// </summary>
        /// <param name="do_interpolate"><c>true</c> to interpolate nutation (faster, slightly less
        /// accurate); <c>false</c> to always compute it exactly (the default).</param>
        public void swe_set_interpolate_nut(bool do_interpolate) { SwephLib.swe_set_interpolate_nut(do_interpolate); }

        /// <summary>
        /// coordinate transformation polar -&gt; polar. Rotates a position between the ecliptic and
        /// equatorial coordinate systems (works in either direction, since the same rotation matrix
        /// applies about the x-axis by <paramref name="eps"/>).
        /// </summary>
        /// <param name="xpo">Input 3-element position: longitude/right ascension, latitude/
        /// declination (degrees), and distance.</param>
        /// <param name="xpn">Receives the transformed 3-element position, in the same layout as
        /// <paramref name="xpo"/>.</param>
        /// <param name="eps">Obliquity of the ecliptic, in degrees.</param>
        public void swe_cotrans(CPointer<double> xpo, CPointer<double> xpn, double eps) { SwephLib.swe_cotrans(xpo, xpn, eps); }
        /// <summary>
        /// Same as <see cref="swe_cotrans"/>, but on a 6-element array that also carries the
        /// position's speed (elements 3-5), transforming position and speed together.
        /// </summary>
        /// <param name="xpo">Input 6-element position and speed.</param>
        /// <param name="xpn">Receives the transformed 6-element position and speed.</param>
        /// <param name="eps">Obliquity of the ecliptic, in degrees.</param>
        public void swe_cotrans_sp(CPointer<double> xpo, CPointer<double> xpn, double eps) { SwephLib.swe_cotrans_sp(xpo, xpn, eps); }

        /// <summary>
        /// tidal acceleration to be used in swe_deltat(). Returns the tidal acceleration value
        /// (arcsec/century^2, the Moon's secular acceleration term) currently used by
        /// <see cref="swe_deltat(double)"/>/<see cref="swe_deltat_ex"/> and the lunar ephemeris.
        /// </summary>
        /// <returns>The current tidal acceleration value, in arcsec/century^2.</returns>
        public double swe_get_tid_acc() { return SwephLib.swe_get_tid_acc(); }
        /// <summary>
        /// Sets the tidal acceleration value used by <see cref="swe_deltat(double)"/>/
        /// <see cref="swe_deltat_ex"/> and the lunar ephemeris explicitly. See the <c>SE_TIDAL_*</c>
        /// constants in this file for named presets, e.g. <see cref="SE_TIDAL_DE431"/>,
        /// <see cref="SE_TIDAL_AUTOMATIC"/> to pick it from the ephemeris file in use.
        /// </summary>
        /// <param name="tidacc">Tidal acceleration, in arcsec/century^2, or one of the
        /// <c>SE_TIDAL_*</c> preset constants.</param>
        public void swe_set_tid_acc(double tidacc) { SwephLib.swe_set_tid_acc(tidacc); }

        /// <summary>
        /// set a user defined delta t to be returned by functions swe_deltat() and swe_deltat_ex().
        /// Overrides Delta T with a fixed, caller-supplied value used by all subsequent
        /// <see cref="swe_deltat(double)"/>/<see cref="swe_deltat_ex"/> calls instead of computing it.
        /// </summary>
        /// <param name="dt">Delta T value, in days, to use for all subsequent calls; pass
        /// <see cref="SE_DELTAT_AUTOMATIC"/> to go back to computing it normally.</param>
        /* set a user defined delta t to be returned by functions
         * swe_deltat() and swe_deltat_ex() */
        public void swe_set_delta_t_userdef(double dt) { SwephLib.swe_set_delta_t_userdef(dt); }

        /// <summary>
        /// Normalizes an angle in degrees to the half-open range [0, 360).
        /// </summary>
        /// <param name="x">Angle, in degrees.</param>
        /// <returns>The normalized angle, in degrees, in [0, 360).</returns>
        public double swe_degnorm(double x) { return SwephLib.swe_degnorm(x); }

        /// <summary>
        /// Normalizes an angle in radians to the half-open range [0, 2*pi).
        /// </summary>
        /// <param name="x">Angle, in radians.</param>
        /// <returns>The normalized angle, in radians, in [0, 2*pi).</returns>
        public double swe_radnorm(double x) { return SwephLib.swe_radnorm(x); }
        /// <summary>
        /// Returns the midpoint, in radians, of the short arc between two angles.
        /// </summary>
        /// <param name="x1">First angle, in radians.</param>
        /// <param name="x0">Second angle, in radians.</param>
        /// <returns>The midpoint of the short arc from <paramref name="x0"/> to
        /// <paramref name="x1"/>, in radians.</returns>
        public double swe_rad_midp(double x1, double x0) { return SwephLib.swe_rad_midp(x1, x0); }
        /// <summary>
        /// Returns the midpoint, in degrees, of the short arc between two angles (handles wraparound
        /// through 0/360 correctly, unlike a plain average).
        /// </summary>
        /// <param name="x1">First angle, in degrees.</param>
        /// <param name="x0">Second angle, in degrees.</param>
        /// <returns>The midpoint of the short arc from <paramref name="x0"/> to
        /// <paramref name="x1"/>, in degrees.</returns>
        public double swe_deg_midp(double x1, double x0) { return SwephLib.swe_deg_midp(x1, x0); }

        /// <summary>
        /// Splits a decimal-degree value into sexagesimal degrees/minutes/seconds (plus a sign or
        /// zodiac sign index).
        /// </summary>
        /// <param name="ddeg">Value to split, in decimal degrees.</param>
        /// <param name="roundflag">Bitfield controlling rounding and output layout, e.g.
        /// <see cref="SE_SPLIT_DEG_ROUND_SEC"/>, <see cref="SE_SPLIT_DEG_ROUND_MIN"/>,
        /// <see cref="SE_SPLIT_DEG_ROUND_DEG"/>, <see cref="SE_SPLIT_DEG_ZODIACAL"/> (split into
        /// zodiac sign + degree instead of a plain angle), <see cref="SE_SPLIT_DEG_NAKSHATRA"/>,
        /// <see cref="SE_SPLIT_DEG_KEEP_SIGN"/>, <see cref="SE_SPLIT_DEG_KEEP_DEG"/>.</param>
        /// <param name="ideg">Receives the whole degrees (or degrees within sign, if
        /// <see cref="SE_SPLIT_DEG_ZODIACAL"/> is set).</param>
        /// <param name="imin">Receives the whole arc-minutes.</param>
        /// <param name="isec">Receives the whole arc-seconds.</param>
        /// <param name="dsecfr">Receives the fractional part of the arc-seconds.</param>
        /// <param name="isgn">Receives +1/-1 for the sign of <paramref name="ddeg"/>, or the zodiac
        /// sign index (0-11) when <see cref="SE_SPLIT_DEG_ZODIACAL"/> is set.</param>
        public void swe_split_deg(double ddeg, Int32 roundflag, out Int32 ideg, out Int32 imin, out Int32 isec, out double dsecfr, out Int32 isgn)
        {
            SwephLib.swe_split_deg(ddeg, roundflag, out ideg, out imin, out isec, out dsecfr, out isgn);
        }

        /******************************************************* 
         * other functions from swephlib.c;
         * they are not needed for Swiss Ephemeris,
         * but may be useful to former Placalc users.
         ********************************************************/

        /// <summary>
        /// normalize argument into interval [0..DEG360]. <c>p</c> is expressed in centiseconds of
        /// arc (<see cref="DEG"/> = 360000 units per degree, so <see cref="DEG360"/> = one full
        /// circle), not centiseconds of time despite the "cs" abbreviation.
        /// </summary>
        /// <param name="p">Angle, in centiseconds of arc.</param>
        /// <returns>The normalized angle, in centiseconds of arc, in [0, <see cref="DEG360"/>).</returns>
        public Int32 swe_csnorm(Int32 p) { return SwephLib.swe_csnorm(p); }

        /// <summary>
        /// distance in centisecs p1 - p2 normalized to [0..360[. Always non-negative.
        /// </summary>
        /// <param name="p1">First angle, in centiseconds of arc.</param>
        /// <param name="p2">Second angle, in centiseconds of arc.</param>
        /// <returns><paramref name="p1"/> - <paramref name="p2"/>, normalized to
        /// [0, <see cref="DEG360"/>) centiseconds of arc.</returns>
        public Int32 swe_difcsn(Int32 p1, Int32 p2) { return SwephLib.swe_difcsn(p1, p2); }

        /// <summary>
        /// Same as <see cref="swe_difcsn"/>, in plain degrees rather than centiseconds of arc.
        /// </summary>
        /// <param name="p1">First angle, in degrees.</param>
        /// <param name="p2">Second angle, in degrees.</param>
        /// <returns><paramref name="p1"/> - <paramref name="p2"/>, normalized to [0, 360) degrees.</returns>
        public double swe_difdegn(double p1, double p2) { return SwephLib.swe_difdegn(p1, p2); }

        /// <summary>
        /// distance in centisecs p1 - p2 normalized to [-180..180[. Signed, shortest direction.
        /// </summary>
        /// <param name="p1">First angle, in centiseconds of arc.</param>
        /// <param name="p2">Second angle, in centiseconds of arc.</param>
        /// <returns><paramref name="p1"/> - <paramref name="p2"/>, normalized to
        /// [-<see cref="DEG180"/>, <see cref="DEG180"/>) centiseconds of arc.</returns>
        public Int32 swe_difcs2n(Int32 p1, Int32 p2) { return SwephLib.swe_difcs2n(p1, p2); }

        /// <summary>
        /// Same as <see cref="swe_difcs2n"/>, in plain degrees rather than centiseconds of arc.
        /// </summary>
        /// <param name="p1">First angle, in degrees.</param>
        /// <param name="p2">Second angle, in degrees.</param>
        /// <returns><paramref name="p1"/> - <paramref name="p2"/>, normalized to [-180, 180) degrees.</returns>
        public double swe_difdeg2n(double p1, double p2) { return SwephLib.swe_difdeg2n(p1, p2); }

        /// <summary>
        /// Same as <see cref="swe_difdeg2n"/>, in radians rather than degrees.
        /// </summary>
        /// <param name="p1">First angle, in radians.</param>
        /// <param name="p2">Second angle, in radians.</param>
        /// <returns><paramref name="p1"/> - <paramref name="p2"/>, normalized to [-pi, pi) radians.</returns>
        public double swe_difrad2n(double p1, double p2) { return SwephLib.swe_difrad2n(p1, p2); }

        /// <summary>
        /// round second, but at 29.5959 always down. Rounds a centiseconds-of-arc value to the
        /// nearest whole arc-second, except that a value at exactly 29.5959... seconds (just under
        /// the next full unit) always rounds down rather than up.
        /// </summary>
        /// <param name="x">Value to round, in centiseconds of arc.</param>
        /// <returns>The value rounded to the nearest whole arc-second, in centiseconds of arc.</returns>
        public Int32 swe_csroundsec(Int32 x) { return SwephLib.swe_csroundsec(x); }

        /// <summary>
        /// double to int32 with rounding, no overflow check.
        /// </summary>
        /// <param name="x">Value to round.</param>
        /// <returns><paramref name="x"/> rounded to the nearest <see cref="Int32"/>.</returns>
        public static Int32 swe_d2l(double x) { return SwephLib.swe_d2l(x); }

        /// <summary>
        /// monday = 0, ... sunday = 6
        /// </summary>
        /// <param name="jd">Julian day number.</param>
        /// <returns>The day of the week for <paramref name="jd"/>: 0 = Monday .. 6 = Sunday.</returns>
        public int swe_day_of_week(double jd) { return SwephLib.swe_day_of_week(jd); }

        /// <summary>
        /// Formats a centiseconds-of-arc value as a time string ("HH:MM:SS"-style; hours and minutes
        /// are never suppressed).
        /// </summary>
        /// <param name="t">Value to format, in centiseconds of arc.</param>
        /// <param name="sep">Separator character placed between the hours, minutes and seconds
        /// fields.</param>
        /// <param name="suppressZero">When <c>true</c>, omits the seconds field if it is zero.</param>
        /// <returns>The formatted time string.</returns>
        public string swe_cs2timestr(Int32 t, char sep, bool suppressZero) { return SwephLib.swe_cs2timestr(t, sep, suppressZero); }

        /// <summary>
        /// Formats a centiseconds-of-arc value as a longitude/latitude string (degrees, minutes,
        /// seconds, with a hemisphere letter).
        /// </summary>
        /// <param name="t">Value to format, in centiseconds of arc.</param>
        /// <param name="pchar">Hemisphere letter used when <paramref name="t"/> is non-negative
        /// (e.g. <c>'n'</c>/<c>'e'</c>).</param>
        /// <param name="mchar">Hemisphere letter used when <paramref name="t"/> is negative
        /// (e.g. <c>'s'</c>/<c>'w'</c>).</param>
        /// <returns>The formatted longitude/latitude string.</returns>
        public string swe_cs2lonlatstr(Int32 t, char pchar, char mchar) { return SwephLib.swe_cs2lonlatstr(t, pchar, mchar); }

        /// <summary>
        /// Formats a centiseconds-of-arc value as a plain degrees/minutes/seconds string, with no
        /// hemisphere letter.
        /// </summary>
        /// <param name="t">Value to format, in centiseconds of arc.</param>
        /// <returns>The formatted degrees/minutes/seconds string.</returns>
        public string swe_cs2degstr(Int32 t) { return SwephLib.swe_cs2degstr(t); }

    }

}
