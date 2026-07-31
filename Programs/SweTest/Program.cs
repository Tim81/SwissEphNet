#define MSDOS
//#define NO_SWE_GLP

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

/*
  swetest.c	A test program

  Authors: Dieter Koch and Alois Treindl, Astrodienst Zuerich

**************************************************************/

#region Licence
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
#endregion

using SwissEphNet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SweTest
{
    class Program
    {
        #region C

        #region Strings
        /* attention: Microsoft Compiler does not accept strings > 2048 char */

        static string infocmd0 = @"
  Swetest computes a complete set of geocentric planetary positions,
  for a given date or a sequence of dates.
  Input can either be a date or an absolute julian day number.
  0:00 (midnight).
  With the proper options, swetest can be used to output a printed
  ephemeris and transfer the data into other programs like spreadsheets
  for graphical display.
  Version:                                                                                   " + "\n";
        static string infocmd1 = @"
  Command line options:
     help commands:
        -?, -h  display whole info
        -hcmd   display commands
        -hplan  display planet numbers
        -hform  display format characters
        -hdate  display input date format
        -hexamp  display examples
        -glp  report file location of library
     input time formats:
        -bDATE  begin date; e.g. -b1.1.1992 if
                Note: the date format is day month year (European style).
        -bj...  begin date as an absolute Julian day number; e.g. -bj2415020.5
        -j...   same as -bj
        -tHH[:MM[:SS]]  input time (as Ephemeris Time)
        -ut     input date is Universal Time (UT1)
    -utHH[:MM[:SS]] input time (as Universal Time)
    -utcHH[:MM[:SS]] input time (as Universal Time Coordinated UTC)
        H,M,S can have one or two digits. Their limits are unchecked.
     output time for eclipses, occultations, risings/settings is UT by default
        -lmt    output date/time is LMT (with -geopos)
        -lat    output date/time is LAT (with -geopos)
     object, number of steps, step with
        -pSEQ   planet sequence to be computed.
                See the letter coding below.
        -dX     differential ephemeris: print differential ephemeris between
                body X and each body in list given by -p
                example: -p2 -d0 -fJl -n366 -b1.1.1992 prints the longitude
                distance between SUN (planet 0) and MERCURY (planet 2)
                for a full year starting at 1 Jan 1992.
        -dhX    differential ephemeris: print differential ephemeris between
                heliocentric body X and each body in list given by -p
                example: -p8 -dh8 -ftl -n36600 -b1.1.1500 -s5 prints the longitude
                distance between geocentric and heliocentric Neptune (planet 8)
                for 500 year starting at 1 Jan 1500.
        Using this option mostly makes sense for a single planet
        to find out how much its geocentric and heliocentric positions can differ
        over extended periods of time
    -DX	midpoint ephemeris, works the same way as the differential
        mode -d described above, but outputs the midpoint position.
        -nN     output data for N consecutive timesteps; if no -n option
                is given, the default is 1. If the option -n without a
                number is given, the default is 20.
        -sN     timestep N days, default 1. This option is only meaningful
                when combined with option -n.
                If an 'y' is appended, the time step is in years instead of days, 
                for example -s10y for a time step of 10 years.
                If an 'mo' is appended, the time step is in months instead of days, 
                for example -s3mo for a time step of 3 months.
                If an 'm' is appended, the time step is in minutes instead of days, 
                for example -s15m for a time step of 15 minutes.
                If an 's' is appended, the time step is in seconds instead of days, 
                for example -s1s for a time step of 1 second.
";
        static string infocmd2 = @"\
     output format:
        -fSEQ   use SEQ as format sequence for the output columns;
                default is PLBRS.
        -head   don\'t print the header before the planet data. This option
                is useful when you want to paste the output into a
                spreadsheet for displaying graphical ephemeris.
        +head   header before every step (with -s..) 
        -gPPP   use PPP as gap between output columns; default is a single
                blank.  -g followed by white space sets the
                gap to the TAB character; which is useful for data entry
                into spreadsheets.
        -hor	list data for multiple planets 'horizontally' in same line.
		all columns of -fSEQ are repeated except time colums tTJyY.
     astrological house system:
        -house[long,lat,hsys]	
        include house cusps. The longitude, latitude (degrees with
        DECIMAL fraction) and house system letter can be given, with
        commas separated, + for east and north. If none are given,
        Greenwich UK and Placidus is used: 0.00,51.50,p.
		The output lists 12 house cusps, Asc, MC, ARMC, Vertex,
		Equatorial Ascendant, co-Ascendant as defined by Walter Koch, 
		co-Ascendant as defined by Michael Munkasey, and Polar Ascendant. 
        Houses can only be computed if option -ut is given.
                   A  equal
                   B  Alcabitius
                   C  Campanus
                   D  equal / MC
                   E  equal = A
                   F  Carter poli-equatorial
                   G  36 Gauquelin sectors
                   H  horizon / azimuth
                   I  Sunshine
                   i  Sunshine alternative
                   K  Koch
                   L  Pullen S-delta
                   M  Morinus
                   N  Whole sign, Aries = 1st house
                   O  Porphyry
                   P  Placidus
                   Q  Pullen S-ratio
                   R  Regiomontanus
                   S  Sripati
                   T  Polich/Page (""topocentric"")
                   U  Krusinski-Pisa-Goelzer
                   V  equal Vehlow
                   W  equal, whole sign
                   X  axial rotation system/ Meridian houses
                   Y  APC houses
		 The use of lower case letters is deprecated. They will have a
		 different meaning in future releases of Swiss Ephemeris.
        -hsy[hsys]	
		 house system to be used (for house positions of planets)
		 for long, lat, hsys, see -house
		 The use of lower case letters is deprecated. They will have a
		 different meaning in future releases of Swiss Ephemeris.
";
        static string infocmd3 = @"
        -geopos[long,lat,elev]	
        Geographic position. Can be used for azimuth and altitude
                or house cusps calculations.
                The longitude, latitude (degrees with DECIMAL fraction)
        and elevation (meters) can be given, with
        commas separated, + for east and north. If none are given,
		Greenwich is used: 0,51.5,0.
		For topocentric planet positions please user the parameter -topo
     sidereal astrology:
    -ay..   ayanamsha, with number of method, e.g. ay0 for Fagan/Bradley
	-sid..    sidereal, with number of method (see below)
	-sidt0..  dito, but planets are projected on the ecliptic plane of the
	          reference date of the ayanamsha (more info in general documentation
		  www.astro.com/swisseph/swisseph.htm)
	-sidsp..  dito, but planets are projected on the solar system plane.
		  (see www.astro.com/swisseph/swisseph.htm)
        -sidudef[jd,ay0,...]  sidereal, with user defined ayanamsha; 
	          jd=julian day number in TT/ET
	          ay0=initial value of ayanamsha, 
		  ...=optional parameters, comma-sparated:
		  'jdisut': ayanamsha reference date is UT
		  'eclt0':  project on ecliptic of reference date (like -sidt0..)
		  'ssyplane':  project on solar system plane (like -sidsp..)
		  e.g. '-sidudef2452163.8333333,25.0,jdisut': ayanamsha is 25.0° on JD 2452163.8333333 UT
           number of ayanamsha method:
       0 for Fagan/Bradley
       1 for Lahiri
       2 for De Luce
       3 for Raman
       4 for Usha/Shashi
       5 for Krishnamurti
       6 for Djwhal Khul
       7 for Yukteshwar
       8 for J.N. Bhasin
       9 for Babylonian/Kugler 1
       10 for Babylonian/Kugler 2
       11 for Babylonian/Kugler 3
       12 for Babylonian/Huber
       13 for Babylonian/Eta Piscium
       14 for Babylonian/Aldebaran = 15 Tau
       15 for Hipparchos
       16 for Sassanian
       17 for Galact. Center = 0 Sag
       18 for J2000
       19 for J1900
       20 for B1950
       21 for Suryasiddhanta
       22 for Suryasiddhanta, mean Sun
       23 for Aryabhata
       24 for Aryabhata, mean Sun
       25 for SS Revati
       26 for SS Citra
       27 for True Citra
       28 for True Revati
       29 for True Pushya (PVRN Rao)
	   30 for Galactic (Gil Brand)
	   31 for Galactic Equator (IAU1958)
	   32 for Galactic Equator
	   33 for Galactic Equator mid-Mula
	   34 for Skydram (Mardyks)
	   35 for True Mula (Chandra Hari)
	   36 Dhruva/Gal.Center/Mula (Wilhelm)
	   37 Aryabhata 522
	   38 Babylonian/Britton
   	   39 Vedic/Sheoran
	   40 Cochrane (Gal.Center = 0 Cap)
	   41 Galactic Equator (Fiorenza)
	   42 Vettius Valens
	   43 Lahiri 1940
	   44 Lahiri VP285 (1980)
	   45 Krishnamurti VP291
	   46 Lahiri ICRC
     ephemeris specifications:
        -edirPATH change the directory of the ephemeris files 
        -eswe   swiss ephemeris
        -ejpl   jpl ephemeris (DE431), or with ephemeris file name
                -ejplde200.eph 
        -emos   moshier ephemeris
        -true             true positions
        -noaberr          no aberration
        -nodefl           no gravitational light deflection
    -noaberr -nodefl  astrometric positions
        -j2000            no precession (i.e. J2000 positions)
        -icrs             ICRS (use Internat. Celestial Reference System)
        -nonut            no nutation 
";
        static string infocmd4 = @"
        -speed            calculate high precision speed 
        -speed3           'low' precision speed from 3 positions 
                          do not use this option. -speed parameter
              is faster and more precise 
    -iXX	          force iflag to value XX
        -testaa96         test example in AA 96, B37,
                          i.e. venus, j2450442.5, DE200.
                          attention: use precession IAU1976
                          and nutation 1980 (s. swephlib.h)
        -testaa95
        -testaa97

     special purpose options:
        -roundsec         round to seconds
        -roundmin         round to minutes
	-ep		  use extra precision in output for some data
	-dms              use dms instead of fractions, at some places
	-lim		  print ephemeris file range
     observer position:
        -hel    compute heliocentric positions
        -bary   compute barycentric positions (bar. earth instead of node) 
        -topo[long,lat,elev]	
        topocentric positions. The longitude, latitude (degrees with
        DECIMAL fraction) and elevation (meters) can be given, with
        commas separated, + for east and north. If none are given,
		Greenwich is used 0.00,51.50,0
        -pc...  compute planetocentric positions
                to specify the central body, use the internal object number
		of Swiss Ephemeris, e.g. 3 for Venus, 4 for Mars,
        -pc3 	Venus-centric
        -pc4 	Mars-centric
        -pc5 	Jupiter-centric (barycenter)
	-pc9599 Jupiter-centric (center of body)
	-pc9699 Saturn-centric (center of body)
		For asteroids use MPC number + 10000, e.g.
	-pc10433 Eros-centric (Eros = 433 + 10000)
     orbital elements:
        -orbel  compute osculating orbital elements relative to the
	        mean ecliptic J2000. (Note, all values, including time of
		pericenter vary considerably depending on the date for which the
		osculating ellipse is calculated

     special events:
        -solecl solar eclipse
                output 1st line:
                  eclipse date,
                  time of maximum (UT):
		    geocentric angle between centre of Sun and Moon reaches minimum.
                  core shadow width (negative with total eclipses),
		  eclipse magnitudes:
		    1. NASA method (= 2. with partial ecl. and
		       ratio lunar/solar diameter with total and annular ecl.)
		    2. fraction of solar diameter covered by moon;
		       if the value is > 1, it means that Moon covers more than
		       just the solar disk
		    3. fraction of solar disc covered by moon (obscuration)
		       with total and annular eclipses it is the ratio of
		       the sizes of the solar disk and the lunar disk.
		  Saros series and eclipse number
		  Julian day number (6-digit fraction) of maximum
                output 2nd line:
                  start and end times for partial and total phases
		  delta t in sec
                output 3rd line:
                  geographical longitude and latitude of maximum eclipse,
                  totality duration at that geographical position,
                output with -local, see below.
        -occult occultation of planet or star by the moon. Use -p to
                specify planet (-pf -xfAldebaran for stars)
                output format same as with -solecl, with the following differences:
		  Magnitude is defined like no. 2. with solar eclipses.
		  There are no saros series.
";
        static string infocmd5 = @"
        -lunecl lunar eclipse
                output 1st line:
                  eclipse date,
                  time of maximum (UT),
                  eclipse magnitudes: umbral and penumbral
		    method as method 2 with solar eclipses
		  Saros series and eclipse number
          Julian day number (6-digit fraction) of maximum
                output 2nd line:
                  6 contacts for start and end of penumbral, partial, and
                  total phase
		  delta t in sec
                output 3rd line:
                  geographic position where the Moon is in zenith at maximum eclipse
        -local  only with -solecl or -occult, if the next event of this
                kind is wanted for a given geogr. position.
                Use -geopos[long,lat,elev] to specify that position.
                If -local is not set, the program 
                searches for the next event anywhere on earth.
                output 1st line:
                  eclipse date,
                  time of maximum,
                  eclipse magnitudes, as with global solar eclipse function
		    (with occultations: only diameter method, see solar eclipses, method 2)
		  Saros series and eclipse number (with solar eclipses only)
		  Julian day number (6-digit fraction) of maximum
                output 2nd line:
                  local eclipse duration for totality (zero with partial occultations)
                  local four contacts,
		  delta t in sec
		Occultations with the remark ""(daytime)"" cannot be observed because
		they are taking place by daylight. Occultations with the remark
		""(sunrise)"" or ""(sunset)"" can be observed only partly because part
		of them takes place in daylight.
        -hev[type] heliacal events,
        type 1 = heliacal rising
        type 2 = heliacal setting
        type 3 = evening first
        type 4 = morning last
            type 0 or missing = all four events are listed.
        -rise   rising and setting of a planet or star.
                Use -geopos[long,lat,elev] to specify geographical position.
        -metr   southern and northern meridian transit of a planet of star
                Use -geopos[long,lat,elev] to specify geographical position.
     specifications for eclipses:
        -total  total eclipse (only with -solecl, -lunecl)
        -partial partial eclipse (only with -solecl, -lunecl)
        -annular annular eclipse (only with -solecl)
        -anntot annular-total (hybrid) eclipse (only with -solecl)
        -penumbral penumbral lunar eclipse (only with -lunecl)
        -central central eclipse (only with -solecl, nonlocal)
        -noncentral non-central eclipse (only with -solecl, nonlocal)
";
        static string infocmd6 = @"
     specifications for risings and settings:
        -norefrac   neglect refraction (with option -rise)
        -disccenter find rise of disc center (with option -rise)
        -discbottom find rise of disc bottom (with option -rise)
    -hindu      hindu version of sunrise (with option -rise)
     specifications for heliacal events:
        -at[press,temp,rhum,visr]:
                pressure in hPa
            temperature in degrees Celsius
            relative humidity in %
            visual range, interpreted as follows:
              > 1 : meteorological range in km
              1>visr>0 : total atmospheric coefficient (ktot)
              = 0 : calculated from press, temp, rhum
            Default values are -at1013.25,15,40,0
         -obs[age,SN] age of observer and Snellen ratio
                Default values are -obs36,1
         -opt[age,SN,binocular,magn,diam,transm]
                age and SN as with -obs
            0 monocular or 1 binocular
            telescope magnification
            optical aperture in mm
            optical transmission
            Default values: -opt36,1,1,1,0,0 (naked eye)
     backward search:
        -bwd";
        /* characters still available:
          ijklruv
         */
        static string infoplan = @"
  Planet selection letters:
     planetary lists:
        d (default) main factors 0123456789mtABCcg
        p main factors as above, plus main asteroids DEFGHI
        h ficticious factors J..X
        a all factors
        (the letters above can only appear as a single letter)n\
     single body numbers/letters:
        0 Sun (character zero)
        1 Moon (character 1)
        2 Mercury
        3 Venus
        4 Mars
        5 Jupiter
        6 Saturn
        7 Uranus
        8 Neptune
        9 Pluto
        m mean lunar node
        t true lunar node
        n nutation
        o obliquity of ecliptic
    q delta t
    y time equation
	b ayanamsha
        A mean lunar apogee (Lilith, Black Moon) 
        B osculating lunar apogee 
        c intp. lunar apogee 
        g intp. lunar perigee 
        C Earth (in heliocentric or barycentric calculation)
        For planets Jupiter to Pluto the center of body (COB) can be
        calculated using the additional parameter -cob
     dwarf planets, plutoids
        F Ceres
    9 Pluto
    s -xs136199   Eris
    s -xs136472   Makemake
    s -xs136108   Haumea
     some minor planets:
        D Chiron
        E Pholus
        G Pallas 
        H Juno 
        I Vesta 
        s minor planet, with MPC number given in -xs
     some planetary moons and center of body of a planet:
        v with moon number given in -xv:
        v -xv9501 Io/Jupiter:
        v -xv9599 Jupiter, center of body (COB):
        v -xv94.. Mars moons:
        v -xv95.. Jupiter moons and COB:
        v -xv96.. Saturn moons and COB:
        v -xv97.. Uranus moons and COB:
        v -xv98.. Neptune moons and COB:
        v -xv99.. Pluto moons and COB:
          The numbers of the moons are given here:
	  https://www.astro.com/ftp/swisseph/ephe/sat/plmolist.txt
     fixed stars:
        f fixed star, with name or number given in -xf option
    f -xfSirius   Sirius
     fictitious objects:
        J Cupido 
        K Hades 
        L Zeus 
        M Kronos 
        N Apollon 
        O Admetos 
        P Vulkanus 
        Q Poseidon 
        R Isis (Sevin) 
        S Nibiru (Sitchin) 
        T Harrington 
        U Leverrier's Neptune
        V Adams' Neptune
        W Lowell's Pluto
        X Pickering's Pluto
        Y Vulcan
        Z White Moon
    w Waldemath's dark Moon
        z hypothetical body, with number given in -xz
     sidereal time:
        x sidereal time
        e print a line of labels
          ";
        /* characters still available 
           CcEeMmOoqWwz
        */
        static string infoform = @"
  Output format SEQ letters:
  In the standard setting five columns of coordinates are printed with
  the default format PLBRS. You can change the default by providing an
  option like -fCCCC where CCCC is your sequence of columns.
  The coding of the sequence is like this:
        y year
        Y year.fraction_of_year
        p planet index
        P planet name
        J absolute juldate
        T date formatted like 23.02.1992 
        t date formatted like 920223 for 1992 february 23
        L longitude in degree ddd mm'ss\""
        l longitude decimal
        Z longitude ddsignmm'ss\""
        S speed in longitude in degree ddd:mm:ss per day
        SS speed for all values specified in fmt
        s speed longitude decimal (degrees/day)
        ss speed for all values specified in fmt
        B latitude degree
        b latitude decimal
        R distance decimal in AU
        r distance decimal in AU, Moon in seconds parallax
        W distance decimal in light years
        w distance decimal in km
        q relative distance (1000=nearest, 0=furthest)
        A right ascension in hh:mm:ss
        a right ascension hours decimal
	m Meridian distance
	z Zenith distance
        D declination degree
        d declination decimal
        I azimuth degree
        i azimuth decimal
        H altitude degree
        h altitude decimal
        K altitude (with refraction) degree
        k altitude (with refraction) decimal
        G house position in degrees
        g house position in degrees decimal
        j house number 1.0 - 12.99999
        X x-, y-, and z-coordinates ecliptical
        x x-, y-, and z-coordinates equatorial
        U unit vector ecliptical
        u unit vector equatorial
        Q l, b, r, dl, db, dr, a, d, da, dd
    n nodes (mean): ascending/descending (Me - Ne); longitude decimal
    N nodes (osculating): ascending/descending, longitude; decimal
    f apsides (mean): perihelion, aphelion, second focal point; longitude dec.
    F apsides (osc.): perihelion, aphelion, second focal point; longitude dec.
    + phase angle
    - phase
    * elongation
    / apparent diameter of disc (without refraction)
    = magnitude";
        static string infoform2 = @"
        v (reserved)
        V (reserved)
    ";
        static string infodate = @"
  Date entry:
  In the interactive mode, when you are asked for a start date,
  you can enter data in one of the following formats:

        1.2.1991        three integers separated by a nondigit character for
                        day month year. Dates are interpreted as Gregorian
                        after 4.10.1582 and as Julian Calendar before.
                        Time is always set to midnight (0 h).
                        If the three letters jul are appended to the date,
                        the Julian calendar is used even after 1582.
                        If the four letters greg are appended to the date,
                        the Gregorian calendar is used even before 1582.

        j2400123.67     the letter j followed by a real number, for
                        the absolute Julian daynumber of the start date.
                        Fraction .5 indicates midnight, fraction .0
                        indicates noon, other times of the day can be
                        chosen accordingly.

        <RETURN>        repeat the last entry
        
        .               stop the program

        +20             advance the date by 20 days

        -10             go back in time 10 days";
        static string infoexamp = @"

  Examples:

    swetest -p2 -b1.12.1900 -n15 -s2
    ephemeris of Mercury (-p2) starting on 1 Dec 1900,
    15 positions (-n15) in two-day steps (-s2)

    swetest -p2 -b1.12.1900 -n15 -s2 -fTZ -roundsec -g, -head
    same, but output format =  date and zodiacal position (-fTZ),
    separated by comma (-g,) and rounded to seconds (-roundsec),
    without header (-head).

    swetest -ps -xs433 -b1.12.1900
    position of asteroid 433 Eros (-ps -xs433)

    swetest -pf -xfAldebaran -b1.1.2000
    position of fixed star Aldebaran 

    swetest -p1 -d0 -b1.12.1900 -n10 -fPTl -head
    angular distance of moon (-p1) from sun (-d0) for 10
    consecutive days (-n10).

    swetest -p6 -DD -b1.12.1900 -n100 -s5 -fPTZ -head -roundmin
      Midpoints between Saturn (-p6) and Chiron (-DD) for 100
      consecutive steps (-n100) with 5-day steps (-s5) with
      longitude in degree-sign format (-f..Z) rounded to minutes (-roundmin)

    swetest -b5.1.2002 -p -house12.05,49.50,K -ut12:30
	Koch houses for a location in Germany at a given date and time

    swetest -b1.1.2016  -g -fTlbR -p0123456789Dmte -hor -n366 -roundsec
	tabular ephemeris (all planets Sun - Pluto, Chiron, mean node, true node)
	in one horizontal row, tab-separated, for 366 days. For each planet
	list longitude, latitude and geocentric distance.";
        #endregion

        /**************************************************************/

        //#include "swephexp.h" 	/* this includes  "sweodef.h" */
        //#include "swephlib.h"
        //#include "sweph.h"
        //#include <math.h>

        /*
         * programmers warning: It looks much worse than it is!
         * Originally swetest.c was a small and simple test program to test
         * the main functions of the Swiss Ephemeris and to demonstrate
         * its precision.
         * It compiles on Unix, on MSDOS and as a non-GUI utility on 16-bit
         * and 32-bit windows.
         * This portability has forced us into some clumsy constructs, which
         * end to hide the actual simplicity of the use of Swiss Ephemeris.
         * For example, the mechanism implemented here in swetest.c to find
         * the binary ephemeris files overrides the much simpler mechanism
         * inside the SwissEph library. This was necessary because we wanted
         * swetest.exe to run directly off the CDROM and search with some
         * intelligence for ephemeris files already installed on a system.
         */

        //#if MSDOS
        //#  include <direct.h>
        //#  include <dos.h>
        //#  ifdef _MSC_VER
        //#    include <sys\types.h>
        //#  endif
        //#if __MINGW32__
        //#  include <sys/stat.h>
        //#else
        //#  include <sys\stat.h>
        //#endif
        //#  include <float.h>
        //#else
        //# ifdef MACOS
        //#  include <console.h>
        //# else
        //#  include <sys/stat.h>
        //# endif
        //#endif

        const double J2000 = 2451545.0;  /* 2000 January 1.5 */
        static double square_sum(CPointer<double> x) { return (x[0] * x[0] + x[1] * x[1] + x[2] * x[2]); }
        const int SEFLG_EPHMASK = SwissEph.SEFLG_EPHMASK;//(SEFLG_JPLEPH | SEFLG_SWIEPH | SEFLG_MOSEPH);

        const int BIT_ROUND_SEC = 1;
        const int BIT_ROUND_MIN = 2;
        const int BIT_ZODIAC = 4;
        const int BIT_LZEROES = 8;

        const int BIT_TIME_LZEROES = 8;
        const int BIT_TIME_LMT = 16;
        const int BIT_TIME_LAT = 32;
        const int BIT_ALLOW_361 = 64;

        const string PLSEL_D = "0123456789mtA";
        const string PLSEL_P = "0123456789mtABCcgDEFGHI";
        const string PLSEL_H = "JKLMNOPQRSTUVWXYZw";
        const string PLSEL_A = "0123456789mtABCcgDEFGHIJKLMNOPQRSTUVWXYZw";

        const char DIFF_DIFF = 'd';
        const char DIFF_GEOHEL = 'h';
        const char DIFF_MIDP = 'D';
        const int MODE_HOUSE = 1;
        const int MODE_LABEL = 2;
        const int MODE_AYANAMSA = 4;

        const int SEARCH_RANGE_LUNAR_CYCLES = 20000;

        // swetest.c:712 sizes fixed char buffers (sout[LEN_SOUT], etc.) with this, and it is
        // not merely a size: swetest.c:2809's insert_gap_string_for_tabs reads it live,
        // bounding its tab-replacement loop (`while ((sp = strchr(sout, '\t')) != NULL &&
        // strlen(sout) + strlen(gap) < LEN_SOUT)`). This port's own
        // insert_gap_string_for_tabs (below) replaces that bounded loop with an
        // unconditional string.Replace (see the commented-out C original near line 3416),
        // dropping the 1000-byte bound rather than reproducing it -- a real, pre-existing
        // divergence from the C, not a faithful match. See docs/known-issues.md,
        // "insert_gap_string_for_tabs drops swetest.c's LEN_SOUT bound". LEN_SOUT itself
        // stays genuinely unread in this file; only the claim that its being unread matches
        // the C was wrong.
        static int LEN_SOUT = 1000; // length of output string variable
        static double SIND(double x) { return Math.Sin(x * SwissEph.DEGTORAD); }
        static double COSD(double x) { return Math.Cos(x * SwissEph.DEGTORAD); }
        static double ACOSD(double x) { return Math.Acos(x) * SwissEph.RADTODEG; }

        static string se_pname = String.Empty;
        static string[] zod_nam = new String[]{"ar", "ta", "ge", "cn", "le", "vi",
                                  "li", "sc", "sa", "cp", "aq", "pi"};

        static string star = "algol", star2 = String.Empty;
        static string sastno = "433";
        static string shyp = "1";
        static string spmoon = "9501"; // not declared in the pinned v2.10.3final tag (swetest.c:1139); added to hold -xv, mirroring sastno/shyp -- "9501" (Jupiter's moon Io) matches upstream master's own later fix for this same defect (swetest.c on master, "static char spmoon[AS_MAXCH] = \"9501\";  // Jupiter Moon Io", between sastno and shyp) rather than being this repo's invention; matched to Tools/CReference/build-c.ps1's spmoon patch (build-c.ps1:293) so the C reference oracle and this port fail the same way instead of a blank default's atoi("") == 0 == SE_SUN silently printing the Sun under a planetary-moon heading; keep both defaults in sync if either changes
        //static char *dms(double x, int32 iflag);
        //static int make_ephemeris_path(char *argv0, char *ephepath);
        //static int letter_to_ipl(int letter);
        //static int print_line(int mode, AS_BOOL is_first, int sid_mode);
        //static int do_special_event(double tjd, int32 ipl, char* star, int32 special_event, int32 special_mode, double* geopos, double* datm, double* dobs, char* serr);
        //static int32 orbital_elements(double tjd_et, int32 ipl, int32 iflag, char* serr);
        //static char *hms_from_tjd(double x);
        //static void do_printf(char *info);
        //static char *hms(double x, int32 iflag);
        //static void remove_whitespace(char *s);
        //#if MSDOS
        //static int cut_str_any(char *s, char *cutlist, char *cpos[], int nmax);
        //#endif
        //static int32 call_swe_fixstar(char *star, double te, int32 iflag, double *x, char *serr);
        //static void jd_to_time_string(double jut, char* stimeout);
        //static char* our_strcpy(char* to, char* from);

        /* globals shared between main() and print_line() */
        static string fmt = "PLBRS";
        static string gap = " ";
        static double t, te, tut, jut = 0, tstep = 1;
        static int jmon, jday, jyear;
        static int ipl = SwissEph.SE_SUN, ipldiff = SwissEph.SE_SUN, nhouses = 12;
        static int iplctr = SwissEph.SE_SUN;
        static string spnam = string.Empty, spnam2 = string.Empty, serr = string.Empty;
        static string serr_save = string.Empty, serr_warn = string.Empty;
        static int gregflag = SwissEph.SE_GREG_CAL;
        static bool gregflag_auto = true;
        static int diff_mode = 0;
        static bool use_dms = false;
        // swetest.c:754/:1157 set this the same way, and its only read (swetest.c:1280) is
        // commented out there too -- a dead variable in the upstream C, not a porting gap.
        // Left assigned, unread, to match.
        static bool has_n = false;
        static bool universal_time = false;
        static bool universal_time_utc = false;
        static Int32 round_flag = 0;
        static Int32 time_flag = 0;
        static bool short_output = false;
        static bool list_hor = false;
        static Int32 special_event = 0;
        static Int32 special_mode = 0;
        static bool do_orbital_elements = false;
        static bool hel_using_AV = false;
        static bool with_header = true;
        static bool with_chart_link = false;
        static int lcount = 0; // static local in call_lunar_eclipse (swetest.c:3247)
        static int scount = 0; // static local in call_solar_eclipse (swetest.c:3476)
        static double[] x = new double[6], x2 = new double[6], xequ = new double[6], xcart = new double[6],
            xcartq = new double[6], xobl = new double[6], xaz = new double[6], xt = new double[6], xsv = new double[6];
        static double hpos, hpos2, hposj, armc;
        static int hpos_meth = 0;
        static double[] geopos = new double[10];
        static double[] attr = new double[20], tret = new double[20], datm = new double[4], dobs = new double[6];
        static Int32 iflag = 0, iflag2;              /* external flag: helio, geo... */
        static string[] hs_nam = new string[]
        { "undef", "Ascendant", "MC", "ARMC", "Vertex", "equat. Asc.", "co-Asc. W.Koch", "co-Asc Munkasey", "Polar Asc." };
        static int direction = 1;
        static bool direction_flag = false;
        static bool step_in_minutes = false;
        static bool step_in_seconds = false;
        static bool step_in_years = false;
        static bool step_in_months = false;
        static Int32 helflag = 0;
        static double tjd = 2415020.5;
        static Int32 nstep = 1, istep;
        static Int32 search_flag = 0;
        //static char sout[LEN_SOUT];
        static string sout = String.Empty;
        static Int32 whicheph = SwissEph.SEFLG_SWIEPH;
        static char psp;
        static Int32 norefrac = 0;
        static Int32 disccenter = 0;
        static string ephepath = String.Empty;
        static Int32 discbottom = 0;
        static Int32 hindu = 0;
        /* for test of old models only */
        static string astro_models;
        static bool do_set_astro_models = false;
        //static char smod[2000];
        static string smod;
        static bool inut = false; /* for Astrodienst internal feature */
        static bool have_gap_parameter = false;
        static bool use_swe_fixstar2 = false;
        static bool output_extra_prec = false;
        static bool show_file_limit = false;

        const int SP_LUNAR_ECLIPSE = 1;
        const int SP_SOLAR_ECLIPSE = 2;
        const int SP_OCCULTATION = 3;
        const int SP_RISE_SET = 4;
        const int SP_MERIDIAN_TRANSIT = 5;
        const int SP_HELIACAL = 6;

        const int SP_MODE_HOW = 2;       /* an option for Lunar */
        const int SP_MODE_LOCAL = 8;       /* an option for Solar */
        const int SP_MODE_HOCAL = 4096;

        const int ECL_LUN_PENUMBRAL = 1;       /* eclipse types for hocal list */
        const int ECL_LUN_PARTIAL = 2;
        const int ECL_LUN_TOTAL = 3;
        const int ECL_SOL_PARTIAL = 4;
        const int ECL_SOL_ANNULAR = 5;
        const int ECL_SOL_TOTAL = 6;

        static SwissEph sweph = null;

        static int main_test(int argc, string[] argv)
        {
            string sdate_save = String.Empty;
            string s1 = String.Empty, s2 = String.Empty;
            string sp; int spi, sp2i, sp2;
            char spno;
            string plsel = PLSEL_D;
            //#if HPUNIX
            //  char hostname[80];
            //#endif
            int i, j, n, iflag_f = -1, iflgt;
            int line_count, line_limit = 36525; // days in a century
            double daya;
            double top_long = 0.0;	/* Greenwich UK */
            double top_lat = 51.5;
            double top_elev = 0;
            bool have_geopos = false;
            char ihsy = 'P';
            int year_start = 0, mon_start = 1, day_start = 1;
            bool do_houses = false;
            string fname = String.Empty;
            string sdate = String.Empty;
            string begindate = null;
            string stimein = string.Empty;
            string stimeout = string.Empty;
            Int32 iflgret;
            bool is_first = true;
            bool with_glp = false;
            bool with_header_always = false;
            bool do_ayanamsa = false;
            bool do_planeto_centric = false;
            double aya_t0 = 0, aya_val0 = 0;
            bool no_speed = false;
            Int32 sid_mode = SwissEph.SE_SIDM_FAGAN_BRADLEY;
            double t2, thour = 0;
            double delt;
            double tid_acc = 0;
            datm[0] = 1013.25; datm[1] = 15; datm[2] = 40; datm[3] = 0;
            dobs[0] = 0; dobs[1] = 0;
            dobs[2] = 0; dobs[3] = 0; dobs[4] = 0; dobs[5] = 0;
            serr = serr_save = serr_warn = sdate_save = String.Empty;
            stimein = string.Empty;
            using (sweph = new SwissEph())
            {
                // sweph.OnLoadFile used to be wired to sweph_OnLoadFile here, duplicating the
                // path search swi_fopen already performs against swe_set_ephe_path(ephepath)
                // below. That duplication existed only because the library had no filesystem
                // access of its own; now that it does (SwissEph.OpenBinary), swe_set_ephe_path
                // alone is sufficient, matching how swetest.c's own C reference resolves files.
                ephepath = @".;C:\\sweph\ephe";
                fname = SwissEph.SE_FNAME_DFT;
                for (i = 1; i < argc; i++)
                {
                    if (argv[i].StartsWith("-utc", StringComparison.Ordinal))
                    {
                        universal_time = true;
                        universal_time_utc = true;
                        if ((argv[i].Length) > 4)
                        {
                            //strncpy(stimein, argv[i] + 4, 30);
                            //stimein[30] = '\0';
                            C.strncpy(out stimein, argv[i].Substring(4), 30);
                        }
                    }
                    else if (argv[i].StartsWith("-ut", StringComparison.Ordinal))
                    {
                        universal_time = true;
                        if (argv[i].Length > 3)
                        {
                            stimein = argv[i].Substring(3);
                            if (stimein.Length > 30)
                                stimein = stimein.Substring(0, 30);
                            //strncpy(stimein, argv[i] + 3, 30);
                            //stimein[30] = '\0';
                        }
                    }
                    else if (argv[i].StartsWith("-glp", StringComparison.Ordinal))
                    {
                        with_glp = true;
                    }
                    else if (argv[i].StartsWith("-hor", StringComparison.Ordinal))
                    {
                        list_hor = true;
                    }
                    else if (argv[i].StartsWith("-head", StringComparison.Ordinal))
                    {
                        with_header = false;
                    }
                    else if (argv[i].StartsWith("+head", StringComparison.Ordinal))
                    {
                        with_header_always = true;
                    }
                    else if (String.CompareOrdinal(argv[i], "-j2000") == 0)
                    {
                        iflag |= SwissEph.SEFLG_J2000;
                    }
                    else if (String.CompareOrdinal(argv[i], "-icrs") == 0)
                    {
                        iflag |= SwissEph.SEFLG_ICRS;
                    }
                    else if (String.CompareOrdinal(argv[i], "-cob") == 0)
                    {
                        iflag |= SwissEph.SEFLG_CENTER_BODY;
                    }
                    else if (argv[i].StartsWith("-ay", StringComparison.Ordinal))
                    {
                        do_ayanamsa = true;
                        sid_mode = C.atoi(argv[i].Substring(3));
                        //sweph.swe_set_sid_mode(sid_mode, 0, 0);
                    }
                    else if (argv[i].StartsWith("-sidt0", StringComparison.Ordinal))
                    {
                        iflag |= SwissEph.SEFLG_SIDEREAL;
                        sid_mode = C.atoi(argv[i].Substring(6));
                        if (sid_mode == 0)
                            sid_mode = SwissEph.SE_SIDM_FAGAN_BRADLEY;
                        sid_mode |= SwissEph.SE_SIDBIT_ECL_T0;
                        //sweph.swe_set_sid_mode(sid_mode, 0, 0);
                    }
                    else if (argv[i].StartsWith("-sidsp", StringComparison.Ordinal))
                    {
                        iflag |= SwissEph.SEFLG_SIDEREAL;
                        sid_mode = C.atoi(argv[i].Substring(6));
                        if (sid_mode == 0)
                            sid_mode = SwissEph.SE_SIDM_FAGAN_BRADLEY;
                        sid_mode |= SwissEph.SE_SIDBIT_SSY_PLANE;
                    }
                    else if (argv[i].StartsWith("-sidudef", StringComparison.Ordinal))
                    {
                        iflag |= SwissEph.SEFLG_SIDEREAL;
                        sid_mode = SwissEph.SE_SIDM_USER;
                        s1 = argv[i].Substring(8);
                        aya_t0 = C.atof(s1);
                        sp = string.Empty;
                        if ((spi = C.strchr(s1, ',')) >= 0)
                        {
                            sp = s1.Substring(spi + 1);
                            aya_val0 = C.atof(sp);
                        }
                        if ((spi = C.strstr(sp, "jdisut")) >= 0)
                        {
                            sid_mode |= SwissEph.SE_SIDBIT_USER_UT;
                        }
                        //sweph.swe_set_sid_mode(sid_mode, 0, 0);
                    }
                    else if (argv[i].StartsWith("-sidbit", StringComparison.Ordinal))
                    {
                        sid_mode |= C.atoi(argv[i].Substring(7));
                    }
                    else if (argv[i].StartsWith("-sid", StringComparison.Ordinal))
                    {
                        iflag |= SwissEph.SEFLG_SIDEREAL;
                        sid_mode = C.atoi(argv[i].Substring(4));
                        //if (sid_mode > 0)
                        //    sweph.swe_set_sid_mode(sid_mode, 0, 0);
                    }
                    else if (String.CompareOrdinal(argv[i], "-jplhora") == 0)
                    {
                        iflag |= SwissEph.SEFLG_JPLHOR_APPROX;
                    }
                    else if (String.CompareOrdinal(argv[i], "-tpm") == 0)
                    {
                        iflag |= SwissEph.SEFLG_TEST_PLMOON;
                    }
                    else if (String.CompareOrdinal(argv[i], "-jplhor") == 0)
                    {
                        iflag |= SwissEph.SEFLG_JPLHOR;
                    }
                    else if (argv[i].StartsWith("-j", StringComparison.Ordinal))
                    {
                        begindate = argv[i].Substring(1);
                    }
                    else if (argv[i].StartsWith("-ejpl", StringComparison.Ordinal))
                    {
                        whicheph = SwissEph.SEFLG_JPLEPH;
                        if (argv[i].Length > 5)
                        {
                            //strncpy(fname, argv[i] + 5, AS_MAXCH - 1);
                            //fname[AS_MAXCH - 1] = '\0';
                            fname = argv[i].Substring(5);
                        }
                    }
                    else if (argv[i].StartsWith("-edir", StringComparison.Ordinal))
                    {
                        if (argv[i].Length > 5)
                        {
                            //strncpy(ephepath, argv[i] + 5, AS_MAXCH - 1);
                            //ephepath[AS_MAXCH - 1] = '\0';
                            ephepath = argv[i].Substring(5);
                        }
                    }
                    else if (String.CompareOrdinal(argv[i], "-eswe") == 0)
                    {
                        whicheph = SwissEph.SEFLG_SWIEPH;
                    }
                    else if (String.CompareOrdinal(argv[i], "-emos") == 0)
                    {
                        whicheph = SwissEph.SEFLG_MOSEPH;
                    }
                    else if (argv[i].StartsWith("-helflag", StringComparison.Ordinal))
                    {
                        helflag = C.atoi(argv[i].Substring(8));
                        if (helflag >= SwissEph.SE_HELFLAG_AV)
                            hel_using_AV = true;
                    }
                    else if (String.CompareOrdinal(argv[i], "-hel") == 0)
                    {
                        iflag |= SwissEph.SEFLG_HELCTR;
                    }
                    else if (String.CompareOrdinal(argv[i], "-bary") == 0)
                    {
                        iflag |= SwissEph.SEFLG_BARYCTR;
                    }
                    else if (argv[i].StartsWith("-house", StringComparison.Ordinal))
                    {
                        sout = String.Empty;
                        sp = argv[i].Substring(6);
                        if (sp.StartsWith("[", StringComparison.Ordinal)) sp = sp.Substring(1);
                        C.sscanf(sp, "%lf,%lf,%c", ref top_long, ref top_lat, ref ihsy);
                        top_elev = 0;
                        do_houses = true;
                        have_geopos = true;
                    }
                    else if (argv[i].StartsWith("-hsy", StringComparison.Ordinal))
                    {
                        ihsy = argv[i].Length > 4 ? argv[i][4] : '\0';
                        if (ihsy == '\0') ihsy = 'P';
                        if (argv[i].Length > 5)
                            hpos_meth = C.atoi(argv[i].Substring(5));
                        have_geopos = true;
                    }
                    else if (argv[i].StartsWith("-topo", StringComparison.Ordinal))
                    {
                        iflag |= SwissEph.SEFLG_TOPOCTR;
                        sp = argv[i].Substr(5);
                        if (sp.StartsWith("[", StringComparison.Ordinal)) sp = sp.Substring(1);
                        C.sscanf(sp, "%lf,%lf,%lf", ref top_long, ref top_lat, ref top_elev);
                        have_geopos = true;
                    }
                    else if (argv[i].StartsWith("-geopos", StringComparison.Ordinal))
                    {
                        sp = argv[i].Substring(7);
                        if (sp.StartsWith("[", StringComparison.Ordinal)) sp = sp.Substring(1);
                        C.sscanf(sp, "%lf,%lf,%lf", ref top_long, ref top_lat, ref top_elev);
                        have_geopos = true;
                    }
                    else if (String.CompareOrdinal(argv[i], "-true") == 0)
                    {
                        iflag |= SwissEph.SEFLG_TRUEPOS;
                    }
                    else if (String.CompareOrdinal(argv[i], "-noaberr") == 0)
                    {
                        iflag |= SwissEph.SEFLG_NOABERR;
                    }
                    else if (String.CompareOrdinal(argv[i], "-nodefl") == 0)
                    {
                        iflag |= SwissEph.SEFLG_NOGDEFL;
                    }
                    else if (String.CompareOrdinal(argv[i], "-nonut") == 0)
                    {
                        iflag |= SwissEph.SEFLG_NONUT;
                    }
                    else if (String.CompareOrdinal(argv[i], "-speed3") == 0)
                    {
                        iflag |= SwissEph.SEFLG_SPEED3;
                    }
                    else if (String.CompareOrdinal(argv[i], "-speed") == 0)
                    {
                        iflag |= SwissEph.SEFLG_SPEED;
                    }
                    else if (String.CompareOrdinal(argv[i], "-nospeed") == 0)
                    {
                        no_speed = true;
                    }
                    else if (argv[i].StartsWith("-testaa", StringComparison.Ordinal))
                    {
                        whicheph = SwissEph.SEFLG_JPLEPH;
                        fname = SwissEph.SE_FNAME_DE200;
                        if (String.CompareOrdinal(argv[i].Substring(7), "95") == 0)
                            begindate = "j2449975.5";
                        if (String.CompareOrdinal(argv[i].Substring(7), "96") == 0)
                            begindate = "j2450442.5";
                        if (String.CompareOrdinal(argv[i].Substring(7), "97") == 0)
                            begindate = "j2450482.5";
                        fmt = "PADRu";
                        universal_time = false;
                        plsel = "3";
                    }
                    else if (argv[i].StartsWith("-lmt", StringComparison.Ordinal))
                    {
                        universal_time = true;
                        time_flag |= BIT_TIME_LMT;
                        if (argv[i].Length > 4)
                        {
                            C.strncpy(out stimein, argv[i].Substring(4), 30);
                        }
                    }
                    else if (String.CompareOrdinal(argv[i], "-lat") == 0)
                    {
                        universal_time = true;
                        time_flag |= BIT_TIME_LAT;
                    }
                    else if (String.CompareOrdinal(argv[i], "-lim") == 0)
                    {
                        show_file_limit = true;
                    }
                    else if (C.strcmp(argv[i], "-clink") == 0)
                    {
                        with_chart_link = true;
                    }
                    else if (String.CompareOrdinal(argv[i], "-lunecl") == 0)
                    {
                        special_event = SP_LUNAR_ECLIPSE;
                    }
                    else if (String.CompareOrdinal(argv[i], "-solecl") == 0)
                    {
                        special_event = SP_SOLAR_ECLIPSE;
                        have_geopos = true;
                    }
                    else if (String.CompareOrdinal(argv[i], "-short") == 0)
                    {
                        short_output = true;
                    }
                    else if (String.CompareOrdinal(argv[i], "-occult") == 0)
                    {
                        special_event = SP_OCCULTATION;
                        have_geopos = true;
                    }
                    else if (String.CompareOrdinal(argv[i], "-ep") == 0)
                    {
                        output_extra_prec = true;
                    }
                    else if (String.CompareOrdinal(argv[i], "-hocal") == 0)
                    {
                        /* used to create a listing for inclusion in hocal.c source code */
                        special_mode |= SP_MODE_HOCAL;
                    }
                    else if (String.CompareOrdinal(argv[i], "-how") == 0)
                    {
                        special_mode |= SP_MODE_HOW;
                    }
                    else if (String.CompareOrdinal(argv[i], "-total") == 0)
                    {
                        search_flag |= SwissEph.SE_ECL_TOTAL;
                    }
                    else if (String.CompareOrdinal(argv[i], "-annular") == 0)
                    {
                        search_flag |= SwissEph.SE_ECL_ANNULAR;
                    }
                    else if (String.CompareOrdinal(argv[i], "-anntot") == 0)
                    {
                        search_flag |= SwissEph.SE_ECL_ANNULAR_TOTAL;
                    }
                    else if (String.CompareOrdinal(argv[i], "-partial") == 0)
                    {
                        search_flag |= SwissEph.SE_ECL_PARTIAL;
                    }
                    else if (String.CompareOrdinal(argv[i], "-penumbral") == 0)
                    {
                        search_flag |= SwissEph.SE_ECL_PENUMBRAL;
                    }
                    else if (String.CompareOrdinal(argv[i], "-noncentral") == 0)
                    {
                        search_flag &= ~SwissEph.SE_ECL_CENTRAL;
                        search_flag |= SwissEph.SE_ECL_NONCENTRAL;
                    }
                    else if (String.CompareOrdinal(argv[i], "-central") == 0)
                    {
                        search_flag &= ~SwissEph.SE_ECL_NONCENTRAL;
                        search_flag |= SwissEph.SE_ECL_CENTRAL;
                    }
                    else if (String.CompareOrdinal(argv[i], "-local") == 0)
                    {
                        special_mode |= SP_MODE_LOCAL;
                    }
                    else if (String.CompareOrdinal(argv[i], "-rise") == 0)
                    {
                        special_event = SP_RISE_SET;
                        have_geopos = true;
                    }
                    else if (String.CompareOrdinal(argv[i], "-norefrac") == 0)
                    {
                        norefrac = 1;
                    }
                    else if (String.CompareOrdinal(argv[i], "-disccenter") == 0)
                    {
                        disccenter = 1;
                    }
                    else if (String.CompareOrdinal(argv[i], "-hindu") == 0)
                    {
                        hindu = 1;
                        norefrac = 1;
                        disccenter = 1;
                    }
                    else if (string.CompareOrdinal(argv[i], "-discbottom") == 0)
                    {
                        discbottom = 1;
                    }
                    else if (string.CompareOrdinal(argv[i], "-metr") == 0)
                    {
                        special_event = SP_MERIDIAN_TRANSIT;
                        have_geopos = true;
                        /* undocumented test feature */
                    }
                    else if (argv[i].StartsWith("-amod", StringComparison.Ordinal))
                    {
                        astro_models = argv[i].Substring(5);
                        do_set_astro_models = true;
                        /* undocumented test feature */
                    }
                    else if (argv[i].StartsWith("-tidacc", StringComparison.Ordinal))
                    {
                        tid_acc = C.atof(argv[i].Substring(7));
                    }
                    else if (argv[i].StartsWith("-hev", StringComparison.Ordinal))
                    {
                        special_event = SP_HELIACAL;
                        search_flag = 0;
                        //if (argv[i].Length > 4)
                        //    search_flag = int.Parse(argv[i].Substring(4));
                        sp = argv[i].Substring(4);
                        if (sp.StartsWith("[", StringComparison.Ordinal)) sp = sp.Substring(1);
                        if (C.strlen(sp) > 0)
                            search_flag = C.atoi(sp);
                        have_geopos = true;
                        if (argv[i].Contains("AV", StringComparison.Ordinal)) hel_using_AV = true;
                    }
                    else if (argv[i].StartsWith("-at", StringComparison.Ordinal))
                    {
                        C.sscanf(argv[i].Substring(3), "%lf,%lf,%lf,%lf", ref datm[0], ref datm[1], ref datm[2], ref datm[3]);
                        sp = argv[i].Substring(3);
                        if (sp.StartsWith("[", StringComparison.Ordinal)) sp = sp.Substring(1);
                        j = 0;
                        var parts = sp.Split(',');
                        while (j < 4 && j < parts.Length)
                        {
                            datm[j] = C.atof(parts[j]);
                            //sp = strchr(sp, ',');
                            //if (sp != NULL) sp += 1;
                            j++;
                        }
                    }
                    else if (argv[i].StartsWith("-obs", StringComparison.Ordinal))
                    {
                        sp = argv[i].Substring(4);
                        if (sp.StartsWith("[", StringComparison.Ordinal)) sp = sp.Substring(1);
                        C.sscanf(sp, "%lf,%lf", ref (dobs[0]), ref (dobs[1]));
                    }
                    else if (argv[i].StartsWith("-opt", StringComparison.Ordinal))
                    {
                        sp = argv[i].Substring(4);
                        if (sp.StartsWith("[", StringComparison.Ordinal)) sp = sp.Substring(1);
                        C.sscanf(sp, "%lf,%lf,%lf,%lf,%lf,%lf", ref (dobs[0]), ref (dobs[1]), ref (dobs[2]), ref (dobs[3]), ref (dobs[4]), ref (dobs[5]));
                    }
                    else if (argv[i].StartsWith("-orbel", StringComparison.Ordinal))
                    {
                        do_orbital_elements = true;
                    }
                    else if (String.CompareOrdinal(argv[i], "-bwd") == 0)
                    {
                        direction = -1;
                        direction_flag = true;
                    }
                    else if (argv[i].StartsWith("-pc", StringComparison.Ordinal))
                    {
                        iplctr = C.atoi(argv[i].Substring(3));
                        do_planeto_centric = true;
                    }
                    else if (argv[i].StartsWith("-p", StringComparison.Ordinal))
                    {
                        spno = argv[i][2];
                        switch (spno)
                        {
                            case 'd':
                                /*
                                case '\0':
                                case ' ':  
                                */
                                plsel = PLSEL_D; break;
                            case 'p': plsel = PLSEL_P; break;
                            case 'h': plsel = PLSEL_H; break;
                            case 'a': plsel = PLSEL_A; break;
                            default: plsel = spno.ToString(); break;
                        }
                    }
                    else if (argv[i].StartsWith("-xs", StringComparison.Ordinal))
                    {
                        /* number of asteroid */
                        sastno = argv[i].Substring(3);
                    }
                    else if (argv[i].StartsWith("-xv", StringComparison.Ordinal))
                    {
                        /* number of planetary moon */
                        spmoon = argv[i].Substring(3);
                    }
                    else if (argv[i].StartsWith("-xf", StringComparison.Ordinal))
                    {
                        /* name or number of fixed star */
                        star = argv[i].Substring(3);
                    }
                    else if (argv[i].StartsWith("-xz", StringComparison.Ordinal))
                    {
                        /* number of hypothetical body */
                        shyp = argv[i].Substring(3);
                    }
                    else if (argv[i].StartsWith("-x", StringComparison.Ordinal))
                    {
                        /* name or number of fixed star */
                        star = argv[i].Substring(2);
                    }
                    else if (argv[i].StartsWith("-nut", StringComparison.Ordinal))
                    {
                        inut = true;
                    }
                    else if (argv[i].StartsWith("-n", StringComparison.Ordinal))
                    {
                        nstep = C.atoi(argv[i].Substring(2));
                        has_n = true;
                        if (nstep == 0)
                            nstep = 20;
                    }
                    else if (argv[i].StartsWith("-i", StringComparison.Ordinal))
                    {
                        iflag_f = C.atoi(argv[i].Substring(2));
                        if ((iflag_f & SwissEph.SEFLG_XYZ) != 0)
                            fmt = "PX";
                    }
                    else if (argv[i].StartsWith("-swefixstar2", StringComparison.Ordinal))
                    {
                        use_swe_fixstar2 = true;
                    }
                    else if (argv[i].StartsWith("-s", StringComparison.Ordinal))
                    {
                        tstep = C.atof(argv[i].Substring(2));
                        //if (*(argv[i] + strlen(argv[i]) - 1) == 'm')
                        //    step_in_minutes = TRUE;
                        //if (*(argv[i] + strlen(argv[i]) - 1) == 's')
                        //    step_in_seconds = TRUE;
                        if (argv[i].EndsWith("m", StringComparison.Ordinal))
                            step_in_minutes = true;
                        if (argv[i].EndsWith("s", StringComparison.Ordinal))
                            step_in_seconds = true;
                        if (argv[i].EndsWith("y", StringComparison.Ordinal))
                            step_in_years = true;
                        if (argv[i].EndsWith("o", StringComparison.Ordinal))
                        {
                            step_in_minutes = false;
                            step_in_months = true;
                        }
                    }
                    else if (argv[i].StartsWith("-b", StringComparison.Ordinal))
                    {
                        begindate = argv[i].Substring(2);
                    }
                    else if (argv[i].StartsWith("-f", StringComparison.Ordinal))
                    {
                        fmt = argv[i].Substring(2);
                    }
                    else if (argv[i].StartsWith("-g", StringComparison.Ordinal))
                    {
                        gap = argv[i].Substring(2);
                        have_gap_parameter = true;
                        if (String.IsNullOrEmpty(gap)) gap = "\t";
                    }
                    else if (C.strcmp(argv[i], "-dms") == 0)
                    {
                        use_dms = true;
                    }
                    else if (argv[i].StartsWith("-d", StringComparison.Ordinal) || argv[i].StartsWith("-D", StringComparison.Ordinal))
                    {
                        diff_mode = argv[i][1];	/* 'd' or 'D' */
                        sp = argv[i].Substring(2);
                        if (!String.IsNullOrEmpty(sp) && sp[0] == 'h')
                        {
                            sp = sp.Substring(1);
                            diff_mode = 'h';   // diff helio to geo
                        }
                        ipldiff = letter_to_ipl(String.IsNullOrEmpty(sp) ? '\0' : sp[0]);
                        if (ipldiff < 0) ipldiff = SwissEph.SE_SUN;
                        spnam2 = sweph.swe_get_planet_name(ipldiff);
                    }
                    else if (String.CompareOrdinal(argv[i], "-roundsec") == 0)
                    {
                        round_flag |= BIT_ROUND_SEC;
                    }
                    else if (String.CompareOrdinal(argv[i], "-roundmin") == 0)
                    {
                        round_flag |= BIT_ROUND_MIN;
                        /*} else if (strncmp(argv[i], "-timeout", 8) == 0) {
                              swe_set_timeout(atoi(argv[i]) + 8);*/
                    }
                    else if (argv[i].StartsWith("-t", StringComparison.Ordinal))
                    {
                        if (C.strlen(argv[i]) > 2)
                        {
                            C.strncat(ref stimein, argv[i].Substring(2), 30);
                        }

                    }
                    else if (argv[i].StartsWith("-h", StringComparison.Ordinal) || argv[i].StartsWith("-?", StringComparison.Ordinal))
                    {
                        sp = argv[i].Length > 2 ? argv[i].Substring(2, 1) : String.Empty;
                        if (sp == "c" || sp == String.Empty)
                        {
                            string si0 = infocmd0;
                            sout = sweph.swe_version();
                            //strcpy(si0, infocmd0);
                            sp2 = C.strstr(si0, "Version:");
                            if (sp2 >= 0 && si0.Length - sp2 > 10 + C.strlen(sout))
                                si0 = si0.Substring(0, sp2 + 9) + sout + si0.Substring(sp2 + 9 + sout.Length);
                            Console.Write(si0);
                            Console.Write(infocmd1);
                            Console.Write(infocmd2);
                            Console.Write(infocmd3);
                            Console.Write(infocmd4);
                            Console.Write(infocmd5);
                            Console.Write(infocmd6);
                        }
                        if (sp == "p" || sp == String.Empty)
                            Console.Write(infoplan);
                        if (sp == "f" || sp == String.Empty)
                        {
                            Console.Write(infoform);
                            Console.Write(infoform2);
                        }
                        if (sp == "d" || sp == String.Empty)
                            Console.Write(infodate);
                        if (sp == "e" || sp == String.Empty)
                            Console.Write(infoexamp);
                        goto end_main;
                    }
                    else
                    {
                        sout = "illegal option ";
                        C.strncat(ref sout, argv[i], 100);
                        sout += "\n";
                        Console.Write(sout);
                        return 1;
                    }
                }
                if (special_event == SP_OCCULTATION ||
                    special_event == SP_RISE_SET ||
                    special_event == SP_MERIDIAN_TRANSIT ||
                    special_event == SP_HELIACAL
                    )
                {
                    ipl = letter_to_ipl(string.IsNullOrEmpty(plsel) ? '\0' : plsel[0]);
                    if (plsel == "f")
                    {
                        ipl = SwissEph.SE_FIXSTAR;
                    }
                    else
                    {
                        if (plsel == "s")
                            ipl = C.atoi(sastno) + SwissEph.SE_AST_OFFSET;
                        star = String.Empty;
                    }
                    if (special_event == SP_OCCULTATION && ipl == 1)
                        ipl = 2; /* no occultation of moon by moon */
                }
                if (!string.IsNullOrEmpty(stimein))
                {
                    t = 0;
                    if ((spi = stimein.IndexOf(':', StringComparison.Ordinal)) >= 0)
                    {
                        if ((sp2i = stimein.IndexOf(':', spi + 1)) >= 0)
                        {
                            t += C.atof(stimein.Substring(sp2i + 1)) / 60.0;
                        }
                        t += C.atof(stimein.Substring(spi + 1));
                        t /= 60.0;
                    }
                    if (C.atoi(stimein) < 0)
                        t = -t;
                    t += C.atoi(stimein);
                    //t += 0.0000000001;
                    thour = t;
                }
                // if (! with_header && ! has_n)
                //  with_header = TRUE;
                //gethostname (hostname, 80);
                //if (strstr(hostname, "as80") != NULL)
                //  line_limit = 2 * 36525;
#if MSDOS
                Console.OutputEncoding = Encoding.UTF8;
                //SetConsoleOutputCP(65001);	// set console to utf-8,
                // works only from Windows Vista upwards, not on XP.
#endif
                if (with_header)
                {
                    for (i = 0; i < argc; i++)
                    {
                        Console.Write(argv[i]);
                        Console.Write(" ");
                    }
                }
                iflag = (iflag & ~SEFLG_EPHMASK) | whicheph;
                if (fmt.IndexOfAny("SsQ".ToCharArray()) >= 0 && (iflag & SwissEph.SEFLG_SPEED3) == 0 && !no_speed)
                    iflag |= SwissEph.SEFLG_SPEED;
                if (String.IsNullOrEmpty(ephepath))
                {
                    if (make_ephemeris_path(argv[0], ref ephepath) == SwissEph.ERR)
                    {
                        iflag = (iflag & ~SwissEph.SEFLG_EPHMASK) | SwissEph.SEFLG_MOSEPH;
                        whicheph = SwissEph.SEFLG_MOSEPH;
                    }
                }
                if (whicheph != SwissEph.SEFLG_MOSEPH)
                    sweph.swe_set_ephe_path(ephepath);
                if ((whicheph & SwissEph.SEFLG_JPLEPH) != 0)
                    sweph.swe_set_jpl_file(fname);
                /* the following is only a test feature */
                if (do_set_astro_models)
                {
                    sweph.swe_set_astro_models(astro_models, iflag); /* secret test feature for dieter */
                    sweph.swe_get_astro_models(astro_models, out smod, iflag);
                }
                //#if 1
                if (inut) /* Astrodienst internal feature */
                    sweph.swe_set_interpolate_nut(true);
                //#endif
                if ((iflag & SwissEph.SEFLG_SIDEREAL) != 0 || do_ayanamsa)
                {
                    if ((sid_mode & SwissEph.SE_SIDM_USER) != 0)
                        sweph.swe_set_sid_mode(sid_mode, aya_t0, aya_val0);
                    else
                        sweph.swe_set_sid_mode(sid_mode, 0, 0);
                }
                geopos[0] = top_long;
                geopos[1] = top_lat;
                geopos[2] = top_elev;
                sweph.swe_set_topo(top_long, top_lat, top_elev);
                if (tid_acc != 0)
                    sweph.swe_set_tid_acc(tid_acc);
                serr = serr_save = serr_warn = String.Empty;
                while (true)
                {
                    if (begindate == null)
                    {
                        Console.Write("\nDate ?");
                        sdate = String.Empty;
                        sdate = Console.ReadLine();
                        if (sdate == null) goto end_main;
                    }
                    else
                    {
                        sdate = begindate;
                        begindate = ".";  /* to exit afterwards */
                    }
                    if (String.CompareOrdinal(sdate, "-bary") == 0)
                    {
                        iflag = iflag & ~SwissEph.SEFLG_HELCTR;
                        iflag |= SwissEph.SEFLG_BARYCTR;
                        sdate = String.Empty;
                    }
                    else if (String.CompareOrdinal(sdate, "-hel") == 0)
                    {
                        iflag = iflag & ~SwissEph.SEFLG_BARYCTR;
                        iflag |= SwissEph.SEFLG_HELCTR;
                        sdate = String.Empty;
                    }
                    else if (String.CompareOrdinal(sdate, "-geo") == 0)
                    {
                        iflag = iflag & ~SwissEph.SEFLG_BARYCTR;
                        iflag = iflag & ~SwissEph.SEFLG_HELCTR;
                        sdate = String.Empty;
                    }
                    else if (String.CompareOrdinal(sdate, "-ejpl") == 0)
                    {
                        iflag &= ~SwissEph.SEFLG_EPHMASK;
                        iflag |= SwissEph.SEFLG_JPLEPH;
                        sdate = String.Empty;
                    }
                    else if (String.CompareOrdinal(sdate, "-eswe") == 0)
                    {
                        iflag &= ~SwissEph.SEFLG_EPHMASK;
                        iflag |= SwissEph.SEFLG_SWIEPH;
                        sdate = String.Empty;
                    }
                    else if (String.CompareOrdinal(sdate, "-emos") == 0)
                    {
                        iflag &= ~SwissEph.SEFLG_EPHMASK;
                        iflag |= SwissEph.SEFLG_MOSEPH;
                        sdate = String.Empty;
                    }
                    else if (sdate.StartsWith("-xs", StringComparison.Ordinal))
                    {
                        /* number of asteroid */
                        sastno = sdate.Substring(3);
                        sdate = String.Empty;
                    }
                    sp = sdate;
                    if (sp.StartsWith(".", StringComparison.Ordinal))
                    {
                        goto end_main;
                    }
                    else if (String.IsNullOrEmpty(sp))
                    {
                        sdate = sdate_save;
                    }
                    else
                    {
                        sdate_save = sdate;
                    }
                    if (String.IsNullOrEmpty(sdate))
                    {
                        sdate = C.sprintf("j%f", tjd);
                    }
                    if (sp.StartsWith("j", StringComparison.Ordinal))
                    {   /* it's a day number */
                        if ((sp2i = sp.IndexOf(',', StringComparison.Ordinal)) >= 0)
                            //*sp2 = '.';
                            sp = String.Concat(sp.Substring(0, sp2i), '.', sp.Substring(sp2i + 1));
                        sdate = sp;
                        C.sscanf(sp.Substring(1), "%lf", ref tjd);
                        if (tjd < 2299160.5)
                            gregflag = SwissEph.SE_JUL_CAL;
                        else
                            gregflag = SwissEph.SE_GREG_CAL;
                        if (sp.Contains("jul", StringComparison.Ordinal))
                        {
                            gregflag = SwissEph.SE_JUL_CAL;
                            gregflag_auto = false;
                        }
                        else if (sp.Contains("greg", StringComparison.Ordinal))
                        {
                            gregflag = SwissEph.SE_GREG_CAL;
                            gregflag_auto = false;
                        }
                        sweph.swe_revjul(tjd, gregflag, ref jyear, ref jmon, ref jday, ref jut);
                        year_start = jyear;
                        mon_start = jmon;
                        day_start = jday;
                    }
                    else if (sp.StartsWith("+", StringComparison.Ordinal))
                    {
                        n = C.atoi(sp);
                        if (n == 0) n = 1;
                        tjd += n;
                        sweph.swe_revjul(tjd, gregflag, ref jyear, ref jmon, ref jday, ref jut);
                    }
                    else if (sp.StartsWith("-", StringComparison.Ordinal))
                    {
                        n = C.atoi(sp);
                        if (n == 0) n = -1;
                        tjd += n;
                        sweph.swe_revjul(tjd, gregflag, ref jyear, ref jmon, ref jday, ref jut);
                    }
                    else
                    {
                        if (C.sscanf(sp, "%d%*c%d%*c%d", ref jday, ref jmon, ref jyear) < 1) return 1;
                        year_start = jyear;
                        mon_start = jmon;
                        day_start = jday;
                        if ((Int32)jyear * 10000L + (Int32)jmon * 100L + (Int32)jday < 15821015L)
                            gregflag = SwissEph.SE_JUL_CAL;
                        else
                            gregflag = SwissEph.SE_GREG_CAL;
                        if (sp.Contains("jul", StringComparison.Ordinal))
                        {
                            gregflag = SwissEph.SE_JUL_CAL;
                            gregflag_auto = false;
                        }
                        else if (sp.Contains("greg", StringComparison.Ordinal))
                        {
                            gregflag = SwissEph.SE_GREG_CAL;
                            gregflag_auto = false;
                        }
                        jut = 0;
                        if (universal_time_utc)
                        {
                            int ih = 0, im = 0;
                            double ds = 0.0;
                            if (!string.IsNullOrEmpty(stimein))
                            {
                                C.sscanf(stimein, "%d:%d:%lf", ref ih, ref im, ref ds);
                            }
                            if (sweph.swe_utc_to_jd(jyear, jmon, jday, ih, im, ds, gregflag, tret, ref serr) == SwissEph.ERR)
                            {
                                printf(" error in swe_utc_to_jd(): %s\n", serr);
                                //exit(-1);
                                return -1;
                            }
                            tjd = tret[1];
                        }
                        else
                        {
                            tjd = sweph.swe_julday(jyear, jmon, jday, jut, gregflag);
                            tjd += thour / 24.0;
                            jut = thour;
                        }
                    }
                    if (special_event > 0)
                    {
                        do_special_event(tjd, ipl, star, special_event, special_mode, geopos, datm, dobs, ref serr);
                        //swe_close();
                        return SwissEph.OK;
                    }
                    line_count = 0;
                    for (t = tjd, istep = 1; istep <= nstep; t += tstep, istep++)
                    {
                        if (step_in_minutes)
                            t = tjd + (istep - 1) * tstep / 1440;
                        if (step_in_seconds)
                            t = tjd + (istep - 1) * tstep / 86400;
                        if (step_in_years)
                        {
                            t = sweph.swe_julday(year_start + (istep - 1) * (int)tstep, mon_start, day_start, jut, gregflag);
                        }
                        if (step_in_months)
                        {
                            jmon = mon_start + (istep - 1) * (int)tstep;
                            jyear = year_start + (int)((jmon - 1) / 12);
                            jmon = ((jmon - 1) % 12) + 1;
                            t = sweph.swe_julday(jyear, jmon, day_start, jut, gregflag);
                        }
                        if (gregflag_auto)
                        {
                            if (t < 2299160.5)
                                gregflag = SwissEph.SE_JUL_CAL;
                            else
                                gregflag = SwissEph.SE_GREG_CAL;
                        }
                        // must repeat because gregflag may have changed
                        if (step_in_years)
                        {
                            t = sweph.swe_julday(year_start + (istep - 1) * (int)tstep, mon_start, day_start, jut, gregflag);
                        }
                        if (step_in_months)
                        {
                            jmon = mon_start + (istep - 1) * (int)tstep;
                            jyear = year_start + (int)((jmon - 1) / 12);
                            jmon = ((jmon - 1) % 12) + 1;
                            t = sweph.swe_julday(jyear, jmon, day_start, jut, gregflag);
                        }
                        delt = sweph.swe_deltat_ex(t, iflag, ref serr);
                        if (!universal_time)
                        {
                            delt = sweph.swe_deltat_ex(t - delt, iflag, ref serr);
                        }
                        t2 = t;
                        // output line: 
                        // "date (dmy) 4.6.2017 greg.   2:07:00 TT		version 2.07.02"
                        sweph.swe_revjul(t2, gregflag, ref jyear, ref jmon, ref jday, ref jut);
                        if (with_header)
                        {
#if !NO_SWE_GLP             // -DNO_SWE_GLP to suppress this function, on C# uncomment the define at top of the file
                            if (with_glp)
                            {
                                sout = sweph.swe_get_library_path();
                                printf("\npath: %s", sout);
                            }
#endif
                            printf("\ndate (dmy) %d.%d.%04d", jday, jmon, jyear);
                            if (gregflag != 0)
                                Console.Write(" greg.");
                            else
                                Console.Write(" jul.");
                            jd_to_time_string(jut, out stimeout);
                            printf("%s", stimeout);
                            if (universal_time)
                            {
                                if ((time_flag & BIT_TIME_LMT) != 0)
                                    printf(" LMT");
                                else
                                    printf(" UT");
                            }
                            else
                            {
                                printf(" TT");
                            }
                            printf("\t\tversion %s", sweph.swe_version());
                        }
                        if (universal_time)
                        {
                            // "LMT: 2457908.588194444"
                            if ((time_flag & BIT_TIME_LMT) != 0)
                            {
                                if (with_header)
                                {
                                    printf("\nLMT: %.9f", t);
                                    t -= geopos[0] / 15.0 / 24.0;
                                }
                            }
                            // "UT:  2457908.565972222     delta t: 68.761612 sec"
                            if (with_header)
                            {
                                printf("\nUT:  %.9f", t);
                                printf("     delta t: %f sec", delt * 86400.0);
                            }
                            te = t + delt;
                            tut = t;
                        }
                        else
                        {
                            te = t;
                            tut = t - delt;
                            // "UT:  2457908.565972222     delta t: 68.761612 sec"
                            if (with_header)
                            {
                                printf("\nUT:  %.9f", tut);
                                printf("     delta t: %f sec", delt * 86400.0);
                            }
                        }
                        iflgret = sweph.swe_calc(te, SwissEph.SE_ECL_NUT, iflag, xobl, ref serr);
                        if (with_header)
                        {
                            // "TT:  2457908.566768074
                            printf("\nTT:  %.9f", te);
                            // "ayanamsa =   24° 5'51.6509 (Lahiri)"
                            if ((iflag & SwissEph.SEFLG_SIDEREAL) != 0)
                            {
                                if (sweph.swe_get_ayanamsa_ex(te, iflag, out daya, ref serr) == SwissEph.ERR)
                                {
                                    printf("   error in swe_get_ayanamsa_ex(): %s\n", serr);
                                    return 1;
                                }
                                printf("   ayanamsa = %s (%s)", dms(daya, round_flag), sweph.swe_get_ayanamsa_name(sid_mode));
                            }
                            // "geo. long 8.000000, lat 47.000000, alt 0.000000"
                            if (have_geopos)
                            {
                                printf("\ngeo. long %f, lat %f, alt %f", geopos[0], geopos[1], geopos[2]);
                            }
                            if (iflag_f >= 0)
                                iflag = iflag_f;
                            if (plsel.IndexOf('o', StringComparison.Ordinal) < 0)
                            {
                                if ((iflag & (SwissEph.SEFLG_NONUT | SwissEph.SEFLG_SIDEREAL)) != 0)
                                {
                                    printf("\n%-15s %s", "Epsilon (m)", dms(xobl[0], round_flag));
                                }
                                else
                                {
                                    printf("\n%-15s %s%s", "Epsilon (t/m)", dms(xobl[0], round_flag), gap);
                                    printf("%s", dms(xobl[1], round_flag));
                                }
                            }
                            if (C.strchr(plsel, 'n') < 0 && 0 == (iflag & (SwissEph.SEFLG_NONUT | SwissEph.SEFLG_SIDEREAL)))
                            {
                                Console.Write("\nNutation        ");
                                Console.Write(dms(xobl[2], round_flag));
                                Console.Write(gap);
                                Console.Write(dms(xobl[3], round_flag));
                            }
                            printf("\n");
                            if (do_houses)
                            {
                                var shsy = sweph.swe_house_name(ihsy);
                                if (!universal_time)
                                {
                                    do_houses = false;
                                    printf("option -house requires option -ut for Universal Time\n");
                                }
                                else
                                {
                                    s1 = dms(top_long, round_flag);
                                    s2 = dms(top_lat, round_flag);
                                    printf("Houses system %c (%s) for long=%s, lat=%s\n", ihsy, shsy, s1, s2);
                                }
                            }
                        }
                        if (with_header && !with_header_always)
                            with_header = false;
                        if (do_ayanamsa)
                        {
                            if (sweph.swe_get_ayanamsa_ex(te, iflag, out daya, ref serr) == SwissEph.ERR)
                            {
                                printf("   error in swe_get_ayanamsa_ex(): %s\n", serr);
                                return 1;
                            }
                            x[0] = daya;
                            print_line(MODE_AYANAMSA, true, sid_mode);
                            continue;
                        }
                        if (t == tjd && plsel.IndexOf('e', StringComparison.Ordinal) >= 0)
                        {
                            if (list_hor)
                            {
                                is_first = true;
                                //for (psp = plsel; *psp != '\0'; psp++)
                                for (var pspi = 0; pspi < plsel.Length; pspi++)
                                {
                                    psp = plsel[pspi];
                                    if (psp == 'e') continue;
                                    ipl = letter_to_ipl(psp);
                                    spnam = string.Empty;
                                    if (ipl >= SwissEph.SE_SUN && ipl <= SwissEph.SE_VESTA)
                                        spnam = sweph.swe_get_planet_name(ipl);
                                    print_line(MODE_LABEL, is_first, 0);
                                    is_first = false;
                                }
                                printf("\n");
                            }
                            else
                            {
                                print_line(MODE_LABEL, true, 0);
                            }
                        }
                        is_first = true;
                        for (var pspi = 0; pspi < plsel.Length; pspi++)
                        {
                            psp = plsel[pspi];
                            if (psp == 'e') continue;
                            ipl = letter_to_ipl(psp);
                            if (ipl == -2)
                            {
                                printf("illegal parameter -p%s\n", plsel);
                                return 1;
                            }
                            if (psp == 'f')      // fixed star
                                ipl = SwissEph.SE_FIXSTAR;
                            else if (psp == 's') // asteroid
                                ipl = C.atoi(sastno) + 10000;
                            else if (psp == 'v') // planetary moon
                                ipl = C.atoi(spmoon);
                            else if (psp == 'z') // fictitious object
                                ipl = C.atoi(shyp) + SwissEph.SE_FICT_OFFSET_1;
                            if ((iflag & SwissEph.SEFLG_HELCTR) != 0)
                            {
                                if (ipl == SwissEph.SE_SUN
                                      || ipl == SwissEph.SE_MEAN_NODE || ipl == SwissEph.SE_TRUE_NODE
                                      || ipl == SwissEph.SE_MEAN_APOG || ipl == SwissEph.SE_OSCU_APOG)
                                    continue;
                            }
                            else if ((iflag & SwissEph.SEFLG_BARYCTR) != 0)
                            {
                                if (ipl == SwissEph.SE_MEAN_NODE || ipl == SwissEph.SE_TRUE_NODE
                                      || ipl == SwissEph.SE_MEAN_APOG || ipl == SwissEph.SE_OSCU_APOG)
                                    continue;
                            }
                            else
                            {         /* geocentric */
                                if (ipl == SwissEph.SE_EARTH && !do_orbital_elements)
                                    continue;
                            }
                            /* ecliptic position */
                            if (iflag_f >= 0)
                                iflag = iflag_f;
                            if (ipl == SwissEph.SE_FIXSTAR)
                            {
                                iflgret = call_swe_fixstar(ref star, te, iflag, x, ref serr);
                                /* magnitude, etc. */
                                if (iflgret != SwissEph.ERR && fmt.IndexOf('=', StringComparison.Ordinal) >= 0)
                                {
                                    double mag = 0;
                                    iflgret = sweph.swe_fixstar_mag(ref star, ref mag, ref serr);
                                    attr[4] = mag;
                                }
                                se_pname = star;
                            }
                            else if (do_planeto_centric)
                            {
                                iflgret = sweph.swe_calc_pctr(te, ipl, iplctr, iflag, x, ref serr);
                                se_pname = sweph.swe_get_planet_name(ipl);
                            }
                            else
                            {
                                iflgret = sweph.swe_calc(te, ipl, iflag, x, ref serr);
                                /* phase, magnitude, etc. */
                                if (iflgret != SwissEph.ERR && fmt.IndexOfAny("+-*/=".ToCharArray()) >= 0)
                                    iflgret = sweph.swe_pheno(te, ipl, iflag, attr, ref serr);
                                se_pname = sweph.swe_get_planet_name(ipl);
                                if (show_file_limit && ipl > SwissEph.SE_AST_OFFSET)
                                {
                                    string sbeg, send;
                                    double tfstart = 0, tfend = 0;
                                    int denum = 0;
                                    var fnam = sweph.swe_get_current_file_data(3, ref tfstart, ref tfend, ref denum);
                                    if (fnam != null)
                                    {
                                        sweph.swe_revjul(tfstart, gregflag, ref jyear, ref jmon, ref jday, ref jut);
                                        sbeg = C.sprintf("%d.%02d.%04d", jday, jmon, jyear);
                                        sweph.swe_revjul(tfend, gregflag, ref jyear, ref jmon, ref jday, ref jut);
                                        send = C.sprintf("%d.%02d.%04d", jday, jmon, jyear);
                                        printf("range %s: %.1lf = %s to %.1lf = %s de=%d\n", fnam, tfstart, sbeg, tfend, send, denum);
                                        show_file_limit = false;
                                    }
                                }
                            }
                            if (psp == 'q')
                            {/* delta t */
                                x[0] = sweph.swe_deltat_ex(tut, iflag, ref serr) * 86400;
                                x[1] = x[2] = x[3] = 0;
                                x[1] = x[0] / 3600.0; // to hours
                                se_pname = "Delta T";
                            }
                            if (psp == 'x')
                            {/* sidereal time */
                                x[0] = sweph.swe_degnorm(sweph.swe_sidtime(tut) * 15 + geopos[0]);
                                x[1] = x[2] = x[3] = 0;
                                se_pname = "Sidereal Time";
                            }
                            if (psp == 'o')
                            {/* ecliptic is wanted, remove nutation */
                                x[2] = x[3] = 0;
                                se_pname = "Ecl. Obl.";
                            }
                            if (psp == 'n')
                            {/* nutation is wanted, remove ecliptic */
                                x[0] = x[2];
                                x[1] = x[3];
                                x[2] = x[3] = 0;
                                se_pname = "Nutation";
                            }
                            if (psp == 'y')
                            {/* time equation */
                                iflgret = sweph.swe_time_equ(tut, out (x[0]), ref serr);
                                x[0] *= 86400; /* in seconds */;
                                x[1] = x[2] = x[3] = 0;
                                se_pname = "Time Equ.";
                            }
                            if (psp == 'b')
                            {/* ayanamsha */
                                if (sweph.swe_get_ayanamsa_ex(te, iflag, out (x[0]), ref serr) == SwissEph.ERR)
                                {
                                    printf("   error in swe_get_ayanamsa_ex(): %s\n", serr);
                                    iflgret = -1;
                                }
                                x[1] = 0;
                                se_pname = "Ayanamsha";
                            }
                            if (iflgret < 0)
                            {
                                if (String.CompareOrdinal(serr, serr_save) != 0
                                  && (ipl == SwissEph.SE_SUN || ipl == SwissEph.SE_MOON || ipl <= SwissEph.SE_PLUTO
                                      || ipl == SwissEph.SE_MEAN_NODE || ipl == SwissEph.SE_TRUE_NODE
                                      || ipl == SwissEph.SE_CERES || ipl == SwissEph.SE_PALLAS || ipl == SwissEph.SE_JUNO || ipl == SwissEph.SE_VESTA
                                      || ipl == SwissEph.SE_CHIRON || ipl == SwissEph.SE_PHOLUS || ipl == SwissEph.SE_CUPIDO
                                      || ipl >= SwissEph.SE_PLMOON_OFFSET
                                      || ipl >= SwissEph.SE_AST_OFFSET || ipl == SwissEph.SE_FIXSTAR
                                      || psp == 'y'))
                                {
                                    Console.Write("error: ");
                                    Console.Write(serr);
                                    Console.Write("\n");
                                }
                                serr_save = serr;
                            }
                            else if (!String.IsNullOrEmpty(serr) && String.IsNullOrEmpty(serr_warn))
                            {
                                if (!serr.Contains("'seorbel.txt' not found", StringComparison.Ordinal))
                                    serr_warn = serr;
                            }
                            if (diff_mode != 0)
                            {
                                iflgret = sweph.swe_calc(te, ipldiff, iflag, x2, ref serr);
                                if (diff_mode == DIFF_GEOHEL)
                                    iflgret = sweph.swe_calc(te, ipldiff, iflag | SwissEph.SEFLG_HELCTR, x2, ref serr);
                                if (iflgret < 0)
                                {
                                    Console.Write("error: ");
                                    Console.Write(serr);
                                    Console.Write("\n");
                                }
                                if (diff_mode == DIFF_DIFF || diff_mode == DIFF_GEOHEL)
                                {
                                    for (i = 1; i < 6; i++)
                                        x[i] -= x2[i];
                                    if ((iflag & SwissEph.SEFLG_RADIANS) == 0)
                                        x[0] = sweph.swe_difdeg2n(x[0], x2[0]);
                                    else
                                        x[0] = sweph.swe_difrad2n(x[0], x2[0]);
                                }
                                else
                                {	/* DIFF_MIDP */
                                    for (i = 1; i < 6; i++)
                                        x[i] = (x[i] + x2[i]) / 2;
                                    if ((iflag & SwissEph.SEFLG_RADIANS) == 0)
                                        x[0] = sweph.swe_deg_midp(x[0], x2[0]);
                                    else
                                        x[0] = sweph.swe_rad_midp(x[0], x2[0]);
                                }
                            }
                            /* equator position */
                            if (fmt.IndexOfAny("aADdQmzx".ToCharArray()) >= 0)
                            {
                                iflag2 = iflag | SwissEph.SEFLG_EQUATORIAL;
                                if (ipl == SwissEph.SE_FIXSTAR)
                                    iflgret = call_swe_fixstar(ref star, te, iflag2, xequ, ref serr);
                                else if (do_planeto_centric)
                                    iflgret = sweph.swe_calc_pctr(te, ipl, iplctr, iflag2, xequ, ref serr);
                                else
                                    iflgret = sweph.swe_calc(te, ipl, iflag2, xequ, ref serr);
                                if (diff_mode != 0)
                                {
                                    iflgret = sweph.swe_calc(te, ipldiff, iflag2, x2, ref serr);
                                    if (diff_mode == DIFF_DIFF || diff_mode == DIFF_GEOHEL)
                                    {
                                        if (diff_mode == DIFF_GEOHEL)
                                            iflgret = sweph.swe_calc(te, ipldiff, iflag2 | SwissEph.SEFLG_HELCTR, x2, ref serr);
                                        for (i = 1; i < 6; i++)
                                            xequ[i] -= x2[i];
                                        if ((iflag & SwissEph.SEFLG_RADIANS) == 0)
                                            xequ[0] = sweph.swe_difdeg2n(xequ[0], x2[0]);
                                        else
                                            xequ[0] = sweph.swe_difrad2n(xequ[0], x2[0]);
                                    }
                                    else
                                    {	/* DIFF_MIDP */
                                        for (i = 1; i < 6; i++)
                                            xequ[i] = (xequ[i] + x2[i]) / 2;
                                        if ((iflag & SwissEph.SEFLG_RADIANS) == 0)
                                            xequ[0] = sweph.swe_deg_midp(xequ[0], x2[0]);
                                        else
                                            xequ[0] = sweph.swe_rad_midp(xequ[0], x2[0]);
                                    }
                                }
                            }
                            /* azimuth and height */
                            if (fmt.IndexOfAny("IiHhKk".ToCharArray()) >= 0)
                            {
                                /* first, get topocentric equatorial positions */
                                iflgt = whicheph | SwissEph.SEFLG_EQUATORIAL | SwissEph.SEFLG_TOPOCTR;
                                if (ipl == SwissEph.SE_FIXSTAR)
                                    iflgret = call_swe_fixstar(ref star, te, iflgt, xt, ref serr);
                                else
                                    iflgret = sweph.swe_calc(te, ipl, iflgt, xt, ref serr);
                                /* to azimuth/height */
                                /* atmospheric pressure "0" has the effect that a value
                                 * of 1013.25 mbar is assumed at 0 m above sea level.
                                 * If the altitude of the observer is given (in geopos[2])
                                 * pressure is estimated according to that */
                                sweph.swe_azalt(tut, SwissEph.SE_EQU2HOR, geopos, datm[0], datm[1], xt, xaz);
                                if (diff_mode != 0)
                                {
                                    iflgret = sweph.swe_calc(te, ipldiff, iflgt, xt, ref serr);
                                    sweph.swe_azalt(tut, SwissEph.SE_EQU2HOR, geopos, datm[0], datm[1], xt, x2);
                                    if (diff_mode == DIFF_DIFF || diff_mode == DIFF_GEOHEL)
                                    {
                                        if (diff_mode == DIFF_GEOHEL)
                                        {   // makes little sense for a heliocentric
                                            iflgret = sweph.swe_calc(te, ipldiff, iflgt | SwissEph.SEFLG_HELCTR, xt, ref serr);
                                            sweph.swe_azalt(tut, SwissEph.SE_EQU2HOR, geopos, datm[0], datm[1], xt, x2);
                                        }
                                        for (i = 1; i < 3; i++)
                                            xaz[i] -= x2[i];
                                        if ((iflag & SwissEph.SEFLG_RADIANS) == 0)
                                            xaz[0] = sweph.swe_difdeg2n(xaz[0], x2[0]);
                                        else
                                            xaz[0] = sweph.swe_difrad2n(xaz[0], x2[0]);
                                    }
                                    else
                                    {	/* DIFF_MIDP */
                                        for (i = 1; i < 3; i++)
                                            xaz[i] = (xaz[i] + x2[i]) / 2;
                                        if ((iflag & SwissEph.SEFLG_RADIANS) == 0)
                                            xaz[0] = sweph.swe_deg_midp(xaz[0], x2[0]);
                                        else
                                            xaz[0] = sweph.swe_rad_midp(xaz[0], x2[0]);
                                    }
                                }
                            }
                            /* ecliptic cartesian position */
                            if (fmt.IndexOfAny("XU".ToCharArray()) >= 0)
                            {
                                iflag2 = iflag | SwissEph.SEFLG_XYZ;
                                if (ipl == SwissEph.SE_FIXSTAR)
                                    iflgret = call_swe_fixstar(ref star, te, iflag2, xcart, ref serr);
                                else if (do_planeto_centric)
                                    iflgret = sweph.swe_calc_pctr(te, ipl, iplctr, iflag2, xcart, ref serr);
                                else
                                    iflgret = sweph.swe_calc(te, ipl, iflag2, xcart, ref serr);
                                if (diff_mode != 0)
                                {
                                    iflgret = sweph.swe_calc(te, ipldiff, iflag2, x2, ref serr);
                                    if (diff_mode == DIFF_DIFF || diff_mode == DIFF_GEOHEL)
                                    {
                                        if (diff_mode == DIFF_GEOHEL)
                                            iflgret = sweph.swe_calc(te, ipldiff, iflag2 | SwissEph.SEFLG_HELCTR, x2, ref serr);
                                        for (i = 0; i < 6; i++)
                                            xcart[i] -= x2[i];
                                    }
                                    else
                                    {
                                        xcart[i] = (xcart[i] + x2[i]) / 2;
                                    }
                                }
                            }
                            /* equator cartesian position */
                            if (fmt.IndexOfAny("xu".ToCharArray()) >= 0)
                            {
                                iflag2 = iflag | SwissEph.SEFLG_XYZ | SwissEph.SEFLG_EQUATORIAL;
                                if (ipl == SwissEph.SE_FIXSTAR)
                                    iflgret = call_swe_fixstar(ref star, te, iflag2, xcartq, ref serr);
                                else if (do_planeto_centric)
                                    iflgret = sweph.swe_calc_pctr(te, ipl, iplctr, iflag2, xcartq, ref serr);
                                else
                                    iflgret = sweph.swe_calc(te, ipl, iflag2, xcartq, ref serr);
                                if (diff_mode != 0)
                                {
                                    iflgret = sweph.swe_calc(te, ipldiff, iflag2, x2, ref serr);
                                    if (diff_mode == DIFF_DIFF || diff_mode == DIFF_GEOHEL)
                                    {
                                        if (diff_mode == DIFF_GEOHEL)
                                            iflgret = sweph.swe_calc(te, ipldiff, iflag2 | SwissEph.SEFLG_HELCTR, x2, ref serr);
                                        for (i = 0; i < 6; i++)
                                            xcartq[i] -= x2[i];
                                    }
                                    else
                                    {
                                        xcartq[i] = (xcart[i] + x2[i]) / 2;
                                    }
                                }
                            }
                            /* house position */
                            if (fmt.IndexOfAny("gGjzm".ToCharArray()) >= 0)
                            {
                                armc = sweph.swe_degnorm(sweph.swe_sidtime(tut) * 15 + geopos[0]);
                                for (i = 0; i < 6; i++)
                                    xsv[i] = x[i];
                                if (hpos_meth == 1)
                                    xsv[1] = 0;
                                if (ipl == SwissEph.SE_FIXSTAR)
                                    star2 = star;
                                else
                                    star2 = String.Empty;
                                // swetest.c:1879 tests toupper(ihsy) == 'G', an ASCII-only
                                // comparison; char.ToUpper is culture-sensitive, so compare
                                // both cases directly instead.
                                if (hpos_meth >= 2 && (ihsy == 'G' || ihsy == 'g'))
                                {
                                    sweph.swe_gauquelin_sector(tut, ipl, star2, iflag, hpos_meth, geopos, 0, 0, ref hposj, ref serr);
                                }
                                else
                                {
                                    CPointer<double> cusp = new double[100];
                                    if (ihsy == 'i' || ihsy == 'I')
                                        iflgret = sweph.swe_houses_ex(t, iflag, top_lat, top_long, ihsy, cusp, cusp + 13);
                                    hposj = sweph.swe_house_pos(armc, geopos[1], xobl[0], ihsy, xsv, ref serr);
                                }
                                // swetest.c:1888/:1898 test toupper(ihsy) == 'G', an ASCII-only
                                // comparison; char.ToUpper is culture-sensitive, so compare
                                // both cases directly instead.
                                if (ihsy == 'G' || ihsy == 'g')
                                    hpos = (hposj - 1) * 10;
                                else
                                    hpos = (hposj - 1) * 30;
                                if (diff_mode != 0)
                                {
                                    for (i = 0; i < 6; i++)
                                        xsv[i] = x2[i];
                                    if (hpos_meth == 1)
                                        xsv[1] = 0;
                                    hpos2 = sweph.swe_house_pos(armc, geopos[1], xobl[0], ihsy, xsv, ref serr);
                                    if (ihsy == 'G' || ihsy == 'g')
                                        hpos2 = (hpos2 - 1) * 10;
                                    else
                                        hpos2 = (hpos2 - 1) * 30;
                                    if (diff_mode == DIFF_DIFF || diff_mode == DIFF_GEOHEL)
                                    {
                                        if ((iflag & SwissEph.SEFLG_RADIANS) == 0)
                                            hpos = sweph.swe_difdeg2n(hpos, hpos2);
                                        else
                                            hpos = sweph.swe_difrad2n(hpos, hpos2);
                                    }
                                    else
                                    {	/* DIFF_MIDP */
                                        if ((iflag & SwissEph.SEFLG_RADIANS) == 0)
                                            hpos = sweph.swe_deg_midp(hpos, hpos2);
                                        else
                                            hpos = sweph.swe_rad_midp(hpos, hpos2);
                                    }
                                }
                            }
                            spnam = se_pname;
                            print_line(0, is_first, 0);
                            is_first = false;
                            if (!list_hor) line_count++;
                            if (do_orbital_elements)
                            {
                                orbital_elements(te, ipl, iflag, ref serr);
                                continue;
                            }
                            if (line_count >= line_limit)
                            {
                                printf("****** line count %d was exceeded\n", line_limit);
                                break;
                            }
                        }         /* for psp */
                        if (list_hor)
                        {
                            printf("\n");
                            line_count++;
                        }
                        if (do_houses)
                        {
                            double[] cusp = new double[37];
                            double[] cusp_speed = new double[37];
                            double[] ascmc = new double[10];
                            double[] ascmc_speed = new double[10];
                            int iofs;
                            // swetest.c:1938 tests toupper(ihsy) == 'G', an ASCII-only
                            // comparison; char.ToUpper is culture-sensitive, so compare both
                            // cases directly instead.
                            if (ihsy == 'G' || ihsy == 'g') // Gauquelin has 36 cusps
                                nhouses = 36;
                            iofs = nhouses + 1;
                            iflgret = sweph.swe_houses_ex2(t, iflag, top_lat, top_long, ihsy, cusp, ascmc, cusp_speed, ascmc_speed, ref serr);
                            // when swe_houses_ex() fails (e.g. with Placidus, Gauquelin, Makranski),
                            // it always returns Porphyry cusps instead
                            if (iflgret < 0)
                            {
                                var shsy = sweph.swe_house_name(ihsy);
                                serr = C.sprintf("House method %s failed, Porphyry calculated instead", shsy);
                                if (String.CompareOrdinal(serr, serr_save) != 0)
                                {
                                    Console.Write("error: ");
                                    Console.Write(serr);
                                    Console.Write("\n");
                                }
                                serr_save = serr;
                                ihsy = 'O';
                                nhouses = 12; // instead of 36 with 'G'
                                iofs = nhouses + 1;
                            }
                            is_first = true;
                            for (ipl = 1; ipl < iofs + 8; ipl++)
                            {
                                x[0] = cusp[ipl];
                                if (ipl >= iofs)
                                {
                                    x[0] = ascmc[ipl - iofs];
                                    x[3] = ascmc_speed[ipl - iofs];
                                }
                                else
                                {
                                    x[3] = cusp_speed[ipl];
                                }
                                x[1] = 0;	/* latitude */
                                x[2] = 1.0;	/* pseudo radius vector */
                                if (ipl == iofs + 2)
                                { /* armc is already equatorial! */
                                    xequ[0] = x[0];
                                    xequ[1] = x[1];
                                    xequ[2] = x[2];
                                }
                                else if (fmt.IndexOfAny("aADdQ".ToCharArray()) >= 0)
                                {
                                    sweph.swe_cotrans(x, xequ, -xobl[0]);
                                }
                                if (fmt.IndexOfAny("IiHhKk".ToCharArray()) >= 0)
                                {
                                    double[] gpos = new double[3];
                                    gpos[0] = top_long;
                                    gpos[1] = top_lat;
                                    gpos[2] = 0;
                                    sweph.swe_azalt(t, SwissEph.SE_ECL2HOR, gpos, datm[0], datm[1], x, xaz);
                                }
                                if (fmt.IndexOfAny("gGj".ToCharArray()) >= 0)
                                {
                                    hposj = sweph.swe_house_pos(armc, geopos[1], xobl[0], ihsy, x, ref serr);
                                    // swetest.c:1984 tests toupper(ihsy) == 'G', an ASCII-only
                                    // comparison; char.ToUpper is culture-sensitive, so compare
                                    // both cases directly instead.
                                    if (ihsy == 'G' || ihsy == 'g')
                                        hpos = (hposj - 1) * 10;
                                    else
                                        hpos = (hposj - 1) * 30;
                                }
                                print_line(MODE_HOUSE, is_first, 0);
                                is_first = false;
                                if (!list_hor) line_count++;
                            }
                            if (list_hor)
                            {
                                printf("\n");
                                line_count++;
                            }
                        }
                        if (line_count >= line_limit)
                        {
                            printf("****** line count %d was exceeded\n", line_limit);
                            break;
                        }
                    }           /* for tjd */
                    if (!String.IsNullOrEmpty(serr_warn))
                    {
                        printf("\nwarning: ");
                        Console.Write(serr_warn);
                        printf("\n");
                    }
                }             /* while 1 */
                              /* close open files and free allocated space */
            end_main:
                if (do_set_astro_models)
                {
                    printf("%s", smod);
                }
                //swe_close();
                return SwissEph.OK;
            }
        }

        static Int32 call_swe_fixstar(ref string star, double te, Int32 iflag, double[] x, ref string serr)
        {
            if (use_swe_fixstar2)
                return sweph.swe_fixstar2(ref star, te, iflag, x, ref serr);
            else
                return sweph.swe_fixstar(ref star, te, iflag, x, ref serr);
        }

        /* This function calculates the geocentric relative distance of a planet,
         * where the closest position has value 1000, and remotest position has 
         * value 0.
         * The value is returned as an integer. The algorithm does not allow 
         * much higher accuracy.
         *
         * With the Moon we measure the distance relative to the maximum and minimum
         * found between 12000 BCE and 16000 CE.
         * If the distance value were given relative to the momentary osculating 
         * ellipse, then the apogee would always have the value 1000 and the perigee
         * the value 0. It is certainly more interesting to know how much it is
         * relative to a greater time range.
         */
        static Int32 get_geocentric_relative_distance(double tjd_et, Int32 ipl, Int32 iflag, ref string serr)
        {
            Int32 iflagi = (iflag & (SEFLG_EPHMASK | SwissEph.SEFLG_HELCTR | SwissEph.SEFLG_BARYCTR));
            Int32 retval;
            double ar = 0;
            double[] xx = new double[6];
            double dmax = 0, dmin = 0, dtrue = 0;
            if (false && ipl == SwissEph.SE_MOON)
            {
                dmax = 0.002718774; // jd = 283030.8
                dmin = 0.002381834; // jd = -1006731.3
                if ((retval = sweph.swe_calc(tjd_et, SwissEph.SE_MOON, iflagi | SwissEph.SEFLG_J2000 | SwissEph.SEFLG_TRUEPOS, xx, ref serr)) == SwissEph.ERR)
                    return 0;
                dtrue = xx[2];
            }
            else
            {
                if (sweph.swe_orbit_max_min_true_distance(tjd_et, ipl, iflagi, ref dmax, ref dmin, ref dtrue, ref serr) == SwissEph.ERR)
                    return 0;
            }
            if (dmax - dmin == 0)
            {
                ar = 0;
            }
            else
            {
                ar = (1 - (dtrue - dmin) / (dmax - dmin)) * 1000.0;
                ar += 0.5; // rounding
            }
            return (Int32)ar;
        }

        /*
         * The string fmt contains a sequence of format specifiers;
         * each character in fmt creates a column, the columns are
         * sparated by the gap string.
         * Time columns tTJyY are only printed, if is_first is TRUE,
         * so that they are not repeated in list_hor (horizontal list) mode.
         * In list_hor mode, no newline is printed.
         */
        static int print_line(int mode, bool is_first, int sid_mode)
        {
            //string sp, sp2;
            int spi = 0; char sp;
            int sp2i = 0; char sp2;
            double t2, ju2 = 0;
            double y_frac;
            double ar/*, sinp*/;
            double[] dret = new double[20];
            string slon = string.Empty;
            string pnam = string.Empty;
            bool is_house = ((mode & MODE_HOUSE) != 0);
            bool is_label = ((mode & MODE_LABEL) != 0);
            bool is_ayana = ((mode & MODE_AYANAMSA) != 0);
            Int32 iflgret, dar;
            // build planet name column, just in case
            if (is_house)
            {
                if (ipl <= nhouses)
                {
                    pnam = C.sprintf("house %2d       ", ipl);
                }
                else
                {
                    pnam = C.sprintf("%-15s", hs_nam[ipl - nhouses]);
                }
            }
            else if (diff_mode == DIFF_DIFF)
            {
                pnam = C.sprintf("%.3s-%.3s", spnam, spnam2);
            }
            else if (diff_mode == DIFF_GEOHEL)
            {
                pnam = C.sprintf("%.3s-%.3sHel", spnam, spnam2);
            }
            else if (diff_mode == DIFF_MIDP)
            {
                pnam = C.sprintf("%.3s/%.3s", spnam, spnam2);
            }
            else
            {
                pnam = C.sprintf("%-15.15s", spnam);
            }
            if (list_hor && fmt.IndexOf('P', StringComparison.Ordinal) >= 0)
            {
                slon = C.sprintf("%.8s %s", pnam, "long.");
            }
            else
            {
                slon = C.sprintf("%-14s", "long.");
            }
            for (spi = 0; spi < fmt.Length; spi++)
            {
                sp = fmt[spi];
                // if (is_house && ipl <= nhouses && "bBsSrRxXuUQnNfFj+-*/=".IndexOf(sp, StringComparison.Ordinal) >= 0) continue;
                if (is_house && "bBrRxXuUQnNfFj+-*/=".IndexOf(sp, StringComparison.Ordinal) >= 0) continue;
                if (is_ayana && "bBsSrRxXuUQnNfFj+-*/=".IndexOf(sp, StringComparison.Ordinal) >= 0) continue;
                //if (sp != fmt)
                if (spi > 0)
                    Console.Write(gap);
                //if (sp == fmt && list_hor && !is_first && strchr("yYJtT", *sp) == NULL)
                // swetest.c:1931 (2.08): emit the gap when the first format char is
                // NOT in "yYJtT"; ">= 0" tested the opposite condition.
                if (spi == 0 && list_hor && !is_first && "yYJtT".IndexOf(sp, StringComparison.Ordinal) < 0)
                    //fputs(gap, stdout);
                    Console.Write(gap);
                switch (sp)
                {
                    case 'y':
                        if (list_hor && !is_first)
                        {
                            break;
                        }
                        if (is_label) { printf("year"); break; }
                        printf("%d", jyear);
                        break;
                    case 'Y':
                        if (list_hor && !is_first)
                        {
                            break;
                        }
                        if (is_label) { printf("year"); break; }
                        t2 = sweph.swe_julday(jyear, 1, 1, ju2, gregflag);
                        y_frac = (t - t2) / 365.0;
                        printf("%.2f", jyear + y_frac);
                        break;
                    case 'p':
                        if (is_label) { printf("obj.nr"); break; }
                        if (!is_house && diff_mode == DIFF_DIFF)
                        {
                            printf("%d-%d", ipl, ipldiff);
                        }
                        else if (!is_house && diff_mode == DIFF_GEOHEL)
                        {
                            printf("%d-%dhel", ipl, ipldiff);
                        }
                        else if (!is_house && diff_mode == DIFF_MIDP)
                        {
                            printf("%d/%d", ipl, ipldiff);
                        }
                        else
                        {
                            printf("%d", ipl);
                        }
                        break;
                    case 'P':
                        if (is_label) { printf("%-15s", "name"); break; }
                        if (is_house)
                        {
                            if (ipl <= nhouses)
                            {
                                printf("house %2d       ", ipl);
                            }
                            else
                            {
                                printf("%-15s", hs_nam[ipl - nhouses]);
                            }
                        }
                        else if (is_ayana)
                        {
                            // printf("Ayanamsha       ");
                            printf("Ayanamsha %s ", sweph.swe_get_ayanamsa_name(sid_mode));
                        }
                        else if (diff_mode == DIFF_DIFF || diff_mode == DIFF_GEOHEL)
                        {
                            printf("%.3s-%.3s", spnam, spnam2);
                        }
                        else if (diff_mode == DIFF_MIDP)
                        {
                            printf("%.3s/%.3s", spnam, spnam2);
                        }
                        else
                        {
                            printf("%-15s", spnam);
                        }
                        break;
                    case 'J':
                        if (list_hor && !is_first)
                        {
                            break;
                        }
                        if (is_label) { printf("julday"); break; }
                        y_frac = (t - Math.Floor(t)) * 100;
                        if (Math.Floor(y_frac) != y_frac)
                        {
                            printf("%.5f", t);
                        }
                        else
                        {
                            printf("%.2f", t);
                        }
                        break;
                    case 'T':
                        if (list_hor && !is_first)
                        {
                            break;
                        }
                        if (is_label) { printf("date    "); break; }
                        printf("%02d.%02d.%04d", jday, jmon, jyear);
                        if (gregflag == SwissEph.SE_JUL_CAL) printf("j");
                        if (jut != 0 || step_in_minutes || step_in_seconds)
                        {
                            int h, m, s, isgn;
                            double dsecfr;
                            int roundflag = SwissEph.SE_SPLIT_DEG_ROUND_SEC;
                            if ((tstep < 1 && tstep > -1) && step_in_seconds)
                            {
                                roundflag = 0;
                                sweph.swe_split_deg(jut, roundflag, out h, out m, out s, out dsecfr, out isgn);
                                printf(" %d:%02d:%02.2lf", h, m, s + dsecfr);
                            }
                            else
                            {
                                sweph.swe_split_deg(jut, roundflag, out h, out m, out s, out dsecfr, out isgn);
                                printf(" %d:%02d:%02d", h, m, s);
                            }
                            if (universal_time)
                                printf(" UT");
                            else
                                printf(" TT");
                        }
                        break;
                    case 't':
                        if (list_hor && !is_first)
                        {
                            break;
                        }
                        if (is_label) { printf("date"); break; }
                        printf("%02d%02d%02d", jyear % 100, jmon, jday);
                        break;
                    case 'L':
                        if (is_label) { printf("%s", slon); break; }
                        if (/*!string.IsNullOrEmpty(psp) &&*/ (psp == 'q' || psp == 'y'))
                        { /* delta t or time equation */
                            //if (psp == 'q' || psp == 'y') { /* delta t or time equation */
                            printf("%# 11.7f", x[0]);
                            printf("s");
                            break;
                        }
                        Console.Write(dms(x[0], round_flag));
                        break;
                    case 'l':
                        if (is_label) { printf("%s", slon); break; }
                        if ((round_flag & BIT_ROUND_MIN) != 0)
                        {
                            printf("%# 6.2f", x[0]);
                        }
                        else
                        {
                            if (output_extra_prec)
                                printf("%# 11.11f", x[0]);
                            else
                                printf("%# 11.7f", x[0]);
                        }
                        break;
                    case 'G':
                        if (is_label) { printf("housPos"); break; }
                        Console.Write(dms(hpos, round_flag));
                        break;
                    case 'g':
                        if (is_label) { printf("housPos"); break; }
                        printf("%# 11.7f", hpos);
                        break;
                    case 'j':
                        if (is_label) { printf("houseNr"); break; }
                        printf("%# 11.7f", hposj);
                        break;
                    case 'Z':
                        if (is_label) { printf("%s", slon); break; }
                        Console.Write(dms(x[0], round_flag | BIT_ZODIAC));
                        break;
                    case 'S':
                    case 's':
                        if (fmt.Length > spi + 1 && (fmt[spi + 1] == 'S' || fmt[spi + 1] == 's' || fmt.IndexOfAny("XUxu".ToCharArray()) >= 0))
                        {
                            for (sp2i = 0; sp2i < fmt.Length; sp2i++)
                            {
                                sp2 = fmt[sp2i];
                                if (sp2i > 0)
                                    Console.Write(gap);
                                switch (sp2)
                                {
                                    case 'L':   /* speed! */
                                    case 'Z':   /* speed! */
                                        if (is_label) { printf("lon/day"); break; }
                                        Console.Write(dms(x[3], round_flag));
                                        break;
                                    case 'l':   /* speed! */
                                        if (is_label) { printf("lon/day"); break; }
                                        if (output_extra_prec)
                                            printf("%# 11.9f", x[3]);
                                        else
                                            printf("%# 11.7f", x[3]);
                                        break;
                                    case 'B':   /* speed! */
                                        if (is_label) { printf("lat/day"); break; }
                                        Console.Write(dms(x[4], round_flag));
                                        break;
                                    case 'b':   /* speed! */
                                        if (is_label) { printf("lat/day"); break; }
                                        if (output_extra_prec)
                                            printf("%# 11.9f", x[4]);
                                        else
                                            printf("%# 11.7f", x[4]);
                                        break;
                                    case 'A':   /* speed! */
                                        if (is_label) { printf("RA/day"); break; }
                                        Console.Write(dms(xequ[3] / 15, round_flag | SwissEph.SEFLG_EQUATORIAL));
                                        break;
                                    case 'a':   /* speed! */
                                        if (is_label) { printf("RA/day"); break; }
                                        if (output_extra_prec)
                                            printf("%# 11.9f", xequ[3]);
                                        else
                                            printf("%# 11.7f", xequ[3]);
                                        break;
                                    case 'D':   /* speed! */
                                        if (is_label) { printf("dcl/day"); break; }
                                        Console.Write(dms(xequ[4], round_flag));
                                        break;
                                    case 'd':   /* speed! */
                                        if (is_label) { printf("dcl/day"); break; }
                                        if (output_extra_prec)
                                            printf("%# 11.9f", xequ[4]);
                                        else
                                            printf("%# 11.7f", xequ[4]);
                                        break;
                                    case 'R':   /* speed! */
                                    case 'r':   /* speed! */
                                        if (is_label) { printf("AU/day"); break; }
                                        if (output_extra_prec)
                                            printf("%# 18.16f", x[5]);
                                        else
                                            printf("%# 14.9f", x[5]);
                                        break;
                                    case 'U':   /* speed! */
                                    case 'X':   /* speed! */
                                        if (is_label)
                                        {
                                            Console.Write("speed_0");
                                            Console.Write(gap);
                                            Console.Write("speed_1");
                                            Console.Write(gap);
                                            Console.Write("speed_2");
                                            break;
                                        }
                                        if (sp == 'U')
                                            ar = Math.Sqrt(square_sum(xcart));
                                        else
                                            ar = 1;
                                        printf("%# 14.9f", xcart[3] / ar);
                                        Console.Write(gap);
                                        printf("%# 14.9f", xcart[4] / ar);
                                        Console.Write(gap);
                                        printf("%# 14.9f", xcart[5] / ar);
                                        break;
                                    case 'u':   /* speed! */
                                    case 'x':   /* speed! */
                                        if (is_label)
                                        {
                                            Console.Write("speed_0");
                                            Console.Write(gap);
                                            Console.Write("speed_1");
                                            Console.Write(gap);
                                            Console.Write("speed_2");
                                            break;
                                        }
                                        if (sp == 'u')
                                            ar = Math.Sqrt(square_sum(xcartq));
                                        else
                                            ar = 1;
                                        printf("%# 14.9f", xcartq[3] / ar);
                                        Console.Write(gap);
                                        printf("%# 14.9f", xcartq[4] / ar);
                                        Console.Write(gap);
                                        printf("%# 14.9f", xcartq[5] / ar);
                                        break;
                                    default:
                                        break;
                                }
                            }
                            if (fmt[spi + 1] == 'S' || fmt[spi + 1] == 's')
                            {
                                spi++;
                                sp = fmt[spi];
                            }
                        }
                        else if (sp == 'S')
                        {
                            int flag = round_flag;
                            if (is_house) flag |= BIT_ALLOW_361;   // speed of houses can be > 360
                            if (is_label) { printf("deg/day"); break; }
                            Console.Write(dms(x[3], flag));
                        }
                        else
                        {
                            if (is_label) { printf("deg/day"); break; }
                            if (output_extra_prec)
                                printf("%# 11.17f", x[3]);
                            else
                                printf("%# 11.7f", x[3]);
                        }
                        break;
                    case 'B':
                        if (is_label) { printf("lat.    "); break; }
                        if (psp == 'q')
                        { /* delta t */
                            printf("%# 11.7f", x[1]);
                            printf("h");
                            break;
                        }
                        Console.Write(dms(x[1], round_flag));
                        break;
                    case 'b':
                        if (is_label) { printf("lat.    "); break; }
                        if (output_extra_prec)
                            printf("%# 11.11f", x[1]);
                        else
                            printf("%# 11.7f", x[1]);
                        break;
                    case 'A':     /* right ascension */
                        if (is_label) { printf("RA      "); break; }
                        Console.Write(dms(xequ[0] / 15, round_flag | SwissEph.SEFLG_EQUATORIAL));
                        break;
                    case 'a':     /* right ascension */
                        if (is_label) { printf("RA      "); break; }
                        if (output_extra_prec)
                            printf("%# 11.11f", xequ[0]);
                        else
                            printf("%# 11.7f", xequ[0]);
                        break;
                    case 'D':     /* declination */
                        if (is_label) { printf("decl      "); break; }
                        Console.Write(dms(xequ[1], round_flag));
                        break;
                    case 'd':     /* declination */
                        if (is_label) { printf("decl      "); break; }
                        if (output_extra_prec)
                            printf("%# 11.11f", xequ[1]);
                        else
                            printf("%# 11.7f", xequ[1]);
                        break;
                    case 'I':     /* azimuth */
                        if (is_label) { printf("azimuth"); break; }
                        Console.Write(dms(xaz[0], round_flag));
                        break;
                    case 'i':     /* azimuth */
                        if (is_label) { printf("azimuth"); break; }
                        printf("%# 11.7f", xaz[0]);
                        break;
                    case 'H':     /* height */
                        if (is_label) { printf("height"); break; }
                        Console.Write(dms(xaz[1], round_flag));
                        break;
                    case 'h':     /* height */
                        if (is_label) { printf("height"); break; }
                        printf("%# 11.7f", xaz[1]);
                        break;
                    case 'K':     /* height (apparent) */
                        if (is_label) { printf("hgtApp"); break; }
                        Console.Write(dms(xaz[2], round_flag));
                        break;
                    case 'k':     /* height (apparent) */
                        if (is_label) { printf("hgtApp"); break; }
                        printf("%# 11.7f", xaz[2]);
                        break;
                    case 'R':
                        if (is_label) { printf("distAU   "); break; }
                        if (output_extra_prec)
                            printf("%# 18.16f", x[2]);
                        else
                            printf("%# 14.9f", x[2]);
                        break;
                    case 'W':
                        if (is_label) { printf("distLY   "); break; }
                        printf("%# 14.9f", x[2] * SwissEph.SE_AUNIT_TO_LIGHTYEAR);
                        break;
                    case 'w':
                        if (is_label) { printf("distkm   "); break; }
                        printf("%# 14.9f", x[2] * SwissEph.SE_AUNIT_TO_KM);
                        break;
                    case 'r':
                        if (is_label) { printf("dist"); break; }
                        if (ipl == SwissEph.SE_MOON)
                        { /* for moon print parallax */
                            /* geocentric horizontal parallax: */
                            //if (false) {
                            //    sinp = 8.794 / x[2];    /* in seconds of arc */
                            //    ar = sinp * (1 + sinp * sinp * 3.917402e-12);
                            //    /* the factor is 1 / (3600^2 * (180/pi)^2 * 6) */
                            //    printf("%# 13.5f\" %# 13.5f'", ar, ar / 60.0);
                            //}
                            sweph.swe_pheno(te, ipl, iflag, dret, ref serr);
                            printf("%# 13.5f\"", dret[5] * 3600);
                        }
                        else
                        {
                            printf("%# 14.9f", x[2]);
                        }
                        break;
                    case 'q':
                        if (is_label) { printf("reldist"); break; }
                        dar = get_geocentric_relative_distance(te, ipl, iflag, ref serr);
                        printf("% 5d", dar);
                        break;
                    case 'U':
                    case 'X':
                        if (sp == 'U')
                            ar = Math.Sqrt(square_sum(xcart));
                        else
                            ar = 1;
                        printf("%# 14.9f", xcart[0] / ar);
                        Console.Write(gap);
                        printf("%# 14.9f", xcart[1] / ar);
                        Console.Write(gap);
                        printf("%# 14.9f", xcart[2] / ar);
                        break;
                    case 'u':
                    case 'x':
                        if (is_label)
                        {
                            Console.Write("x0");
                            Console.Write(gap);
                            Console.Write("x1");
                            Console.Write(gap);
                            Console.Write("x2");
                            break;
                        }
                        if (sp == 'u')
                            ar = Math.Sqrt(square_sum(xcartq));
                        else
                            ar = 1;
                        if (output_extra_prec)
                        {
                            printf("%# .17f", xcartq[0] / ar);
                            Console.Write(gap);
                            printf("%# .17f", xcartq[1] / ar);
                            Console.Write(gap);
                            printf("%# .17f", xcartq[2] / ar);
                        }
                        else
                        {
                            printf("%# 14.9f", xcartq[0] / ar);
                            Console.Write(gap);
                            printf("%# 14.9f", xcartq[1] / ar);
                            Console.Write(gap);
                            printf("%# 14.9f", xcartq[2] / ar);
                        }
                        break;
                    case 'Q':
                        if (is_label) { printf("Q"); break; }
                        printf("%-15s", spnam);
                        Console.Write(dms(x[0], round_flag));
                        Console.Write(dms(x[1], round_flag));
                        printf("  %# 14.9f", x[2]);
                        Console.Write(dms(x[3], round_flag));
                        Console.Write(dms(x[4], round_flag));
                        printf("  %# 14.9f\n", x[5]);
                        printf("               %s", dms(xequ[0], round_flag));
                        Console.Write(dms(xequ[1], round_flag));
                        printf("                %s", dms(xequ[3], round_flag));
                        Console.Write(dms(xequ[4], round_flag));
                        break;
                    case 'N':
                    case 'n':
                        {
                            double[] xasc = new double[6], xdsc = new double[6];
                            // swetest.c:2517 tests *sp == tolower(*sp), an ASCII-only
                            // comparison; char.ToLower is culture-sensitive. tolower only
                            // touches 'A'-'Z', so "already lowercase" is exactly "not an
                            // uppercase ASCII letter".
                            int imeth = !(sp >= 'A' && sp <= 'Z') ? SwissEph.SE_NODBIT_MEAN : SwissEph.SE_NODBIT_OSCU;
                            iflgret = sweph.swe_nod_aps(te, ipl, iflag, imeth, xasc, xdsc, null, null, ref serr);
                            if (iflgret >= 0 && (ipl <= SwissEph.SE_NEPTUNE || sp == 'N'))
                            {
                                if (is_label)
                                {
                                    Console.Write("nodAsc");
                                    Console.Write(gap);
                                    Console.Write("nodDesc");
                                    break;
                                }
                                if (use_dms)
                                    Console.Write(dms(xasc[0], round_flag | BIT_ZODIAC));
                                else
                                    printf("%# 11.7f", xasc[0]);
                                Console.Write(gap);
                                if (use_dms)
                                    Console.Write(dms(xdsc[0], round_flag | BIT_ZODIAC));
                                else
                                    printf("%# 11.7f", xdsc[0]);
                            }
                        };
                        break;
                    case 'F':
                    case 'f':
                        if (!is_house)
                        {
                            double[] xfoc = new double[6], xaph = new double[6], xper = new double[6];
                            // swetest.c:2542 tests *sp == tolower(*sp), an ASCII-only
                            // comparison; char.ToLower is culture-sensitive. tolower only
                            // touches 'A'-'Z', so "already lowercase" is exactly "not an
                            // uppercase ASCII letter".
                            int imeth = !(sp >= 'A' && sp <= 'Z') ? SwissEph.SE_NODBIT_MEAN : SwissEph.SE_NODBIT_OSCU;
                            //	fprintf(stderr, "c=%c\n", *sp);
                            iflgret = sweph.swe_nod_aps(te, ipl, iflag, imeth, null, null, xper, xaph, ref serr);
                            if (iflgret >= 0 && (ipl <= SwissEph.SE_NEPTUNE || sp == 'F'))
                            {
                                if (is_label)
                                {
                                    Console.Write("peri");
                                    Console.Write(gap);
                                    Console.Write("apo");
                                    Console.Write(gap);
                                    Console.Write("focus");
                                    break;
                                }
                                printf("%# 11.7f", xper[0]);
                                Console.Write(gap);
                                printf("%# 11.7f", xaph[0]);
                            }
                            imeth |= SwissEph.SE_NODBIT_FOPOINT;
                            iflgret = sweph.swe_nod_aps(te, ipl, iflag, imeth, null, null, xper, xfoc, ref serr);
                            if (iflgret >= 0 && (ipl <= SwissEph.SE_NEPTUNE || sp == 'F'))
                            {
                                Console.Write(gap);
                                printf("%# 11.7f", xfoc[0]);
                            }
                        };
                        break;
                    case '+':
                        if (is_house) break;
                        if (is_label) { printf("phase"); break; }
                        if (fmt.IndexOf('l', StringComparison.Ordinal) >= 0)  // if decimal longitude is present, do phae angle also decimal
                        {
                            printf("%# 11.7f", attr[0]);
                        }
                        else
                        {
                            Console.Write(dms(attr[0], round_flag));
                        }
                        break;
                    case '-':
                        if (is_label) { printf("phase"); break; }
                        if (is_house) break;
                        printf("  %# 14.9f", attr[1]);
                        break;
                    case '*':
                        if (is_label) { printf("elong"); break; }
                        if (is_house) break;
                        if (fmt.IndexOf('l', StringComparison.Ordinal) >= 0)  // if decimal longitude is present, do elongation also decimal
                        {
                            printf("%# 11.7f", attr[2]);
                        }
                        else
                        {
                            Console.Write(dms(attr[2], round_flag));
                        }
                        break;
                    case '/':
                        if (is_label) { printf("diamet"); break; }
                        if (is_house) break;
                        Console.Write(dms(attr[3], round_flag));
                        break;
                    case '=':
                        if (is_label) { printf("magn"); break; }
                        if (is_house) break;
                        printf("  %# 6.3fm", attr[4]);
                        break;
                    case 'V': /* human design gates */
                    case 'v':
                        {
                            double xhds;
                            int igate, iline, ihex;
                            int[] hexa = new int[64] { 1, 43, 14, 34, 9, 5, 26, 11, 10, 58, 38, 54, 61, 60, 41, 19, 13, 49, 30, 55, 37, 63, 22, 36, 25, 17, 21, 51, 42, 3, 27, 24, 2, 23, 8, 20, 16, 35, 45, 12, 15, 52, 39, 53, 62, 56, 31, 33, 7, 4, 29, 59, 40, 64, 47, 6, 46, 18, 48, 57, 32, 50, 28, 44 };
                            if (is_label) { printf("hds"); break; }
                            if (is_house) break;
                            xhds = sweph.swe_degnorm(x[0] - 223.25);
                            ihex = (int)Math.Floor(xhds / 5.625);
                            iline = ((int)(Math.Floor(xhds / 0.9375))) % 6 + 1;
                            igate = hexa[ihex];
                            printf("%2d.%d", igate, iline);
                            if (sp == 'V')
                                printf(" %2d%%", SwissEph.swe_d2l(100 * ((xhds / 0.9375) % 1.0)));
                            break;
                        }
                    case 'm':
                        {   // Meridian distance
                            if (is_label) { printf("MD      "); break; }
                            double md = sweph.swe_difdeg2n(xequ[0], armc);
                            if (md < 0) md = -md;
                            if (output_extra_prec)
                                printf("%# 11.11f", md);
                            else
                                printf("%# 11.7f", md);
                            break;
                        }
                    case 'z':
                        {   // Zenith distance
                            if (is_label) { printf("ZD      "); break; }
                            sweph.swe_azalt(tut, SwissEph.SE_EQU2HOR, geopos, datm[0], datm[1], xequ, xaz);
                            double zd = 90 - xaz[1];
                            if (output_extra_prec)
                                printf("%# 11.11f", zd);
                            else
                                printf("%# 11.7f", zd);
                            break;
                        }
                }     /* switch */
            }       /* for sp */
            if (!list_hor)
                printf("\n");
            return SwissEph.OK;
        }

        static string dms(double xv, Int32 iflg)
        {
            int izod;
            Int32 k, kdeg, kmin, ksec;
            string c = SwissEph.ODEGREE_STRING;
            string /*sp,*/ s1 = string.Empty;
            string s;
            int sgn;
            if (double.IsNaN(xv))
                return "nan";
            if (xv >= 360 && (iflg & BIT_ALLOW_361) == 0)
                xv = 0;
            s = string.Empty;
            if ((iflg & SwissEph.SEFLG_EQUATORIAL) != 0)
                c = "h";
            if (xv < 0)
            {
                xv = -xv;
                sgn = -1;
            }
            else
            {
                sgn = 1;
            }
            if ((iflg & BIT_ROUND_MIN) != 0)
            {
                if ((iflg & BIT_ALLOW_361) == 0)
                    xv = sweph.swe_degnorm(xv + 0.5 / 60);
            }
            else if ((iflg & BIT_ROUND_SEC) != 0)
            {
                if ((iflg & BIT_ALLOW_361) == 0)
                    xv = sweph.swe_degnorm(xv + 0.5 / 3600);
            }
            else
            {
                /* rounding 0.9999999999 to 1 */
                if (output_extra_prec)
                    xv += (xv < 0 ? -1 : 1) * 0.000000005 / 3600.0;
                else
                    xv += (xv < 0 ? -1 : 1) * 0.00005 / 3600.0;
            }
            if ((iflg & BIT_ZODIAC) != 0)
            {
                izod = (int)(xv / 30);
                if (izod == 12) izod = 0;
                xv = (xv % 30.0);
                kdeg = (Int32)xv;
                // swetest.c:2686: sprintf(s, "%2d %s ", kdeg, zod_nam[izod]); -- no leading space.
                // A prior fix here kept a leading space to dodge a sign-loss bug in the C (see the
                // sign-insertion guard below, at return_dms), but that made every zodiac field diverge
                // from the C, not just the case the C gets wrong. Matching the C's own format and
                // guarding the sign insertion instead keeps this byte-exact with the C for every
                // non-negative value, and confines the divergence to the one input where the C itself
                // has undefined behavior. See "swetest.c's zodiac field: a sign the C itself can
                // lose, reproduced instead of dodged" in docs/known-issues.md.
                s = C.sprintf("%2d %s ", kdeg, zod_nam[izod]);
            }
            else
            {
                // swetest.c:2688: sprintf(s, " %3d%s", kdeg, c). The leading space was missing here,
                // which made every degree-bearing field a column narrow and, once kdeg reached 100,
                // put a digit at index 0 so the sign path below called Substring(0, -1) and threw.
                // swetest -p0 -d1 -b1.1.2020 -fPL -n12 -emos crashed where the C prints -121 degrees.
                kdeg = (Int32)xv;
                s = C.sprintf(" %3d%s", kdeg, c);
            }
            xv -= kdeg;
            xv *= 60;
            kmin = (Int32)xv;
            if ((iflg & BIT_ZODIAC) != 0 && (iflg & BIT_ROUND_MIN) != 0)
            {
                s1 = C.sprintf("%2d", kmin);
            }
            else
            {
                s1 = C.sprintf("%2d'", kmin);
            }
            s += s1;
            if ((iflg & BIT_ROUND_MIN) != 0)
                goto return_dms;
            xv -= kmin;
            xv *= 60;
            ksec = (Int32)xv;
            if ((iflg & BIT_ROUND_SEC) != 0)
            {
                s1 = C.sprintf("%2d\"", ksec);
            }
            else
            {
                s1 = C.sprintf("%2d", ksec);
            }
            s += s1;
            if ((iflg & BIT_ROUND_SEC) != 0)
                goto return_dms;
            xv -= ksec;
            if (output_extra_prec)
            {
                k = (Int32)(xv * 100000000);
                s1 = C.sprintf(".%08d", k);
            }
            else
            {
                k = (Int32)(xv * 10000);
                s1 = C.sprintf(".%04d", k);
            }
            s += s1;
        return_dms:;
            int spi;
            if (sgn < 0)
            {
                spi = s.IndexOfAny("0123456789".ToCharArray());
                // swetest.c:2723-2725 (return_dms): sp = strpbrk(s, "0123456789"); *(sp - 1) = '-';
                // overwrites the character immediately before the first digit. Under BIT_ZODIAC,
                // once kdeg reaches double digits "%2d" fills the field and the first digit lands
                // at index 0, so the C writes *(sp - 1) one byte before its own buffer -- undefined
                // behavior that loses the minus (swetest -p0 -d1 -b3.1.2020 -fPZ prints
                // "27 ge 50' 3.9344" for a value of -27, not "-27 ge..."). Reproducing that would
                // print a positive number for a negative one, so prepend the sign here instead of
                // splicing at index -1 when there is no character before the digit to overwrite.
                if (spi == 0)
                    s = "-" + s;
                else
                    s = String.Concat(s.Substring(0, spi - 1), '-', s.Substring(spi));
            }
            if ((iflg & BIT_LZEROES) != 0)
            {
                //while ((sp = strchr(s + 2, ' ')) != NULL) *sp = '0';
                s = s.Substring(0, 2) + s.Substring(2).Replace(' ', '0');
            }
            return (s);
        }

        static int letter_to_ipl(char letter)
        {
            if (letter >= '0' && letter <= '9')
                return letter - '0' + SwissEph.SE_SUN;
            if (letter >= 'A' && letter <= 'I')
                return letter - 'A' + SwissEph.SE_MEAN_APOG;
            if (letter >= 'J' && letter <= 'Z')
                return letter - 'J' + SwissEph.SE_CUPIDO;
            switch (letter)
            {
                case 'm': return SwissEph.SE_MEAN_NODE;
                case 'c': return SwissEph.SE_INTP_APOG;
                case 'g': return SwissEph.SE_INTP_PERG;
                case 'n':
                case 'o': return SwissEph.SE_ECL_NUT;
                case 't': return SwissEph.SE_TRUE_NODE;
                case 'f': return SwissEph.SE_FIXSTAR;
                case 'w': return SwissEph.SE_WALDEMATH;
                case 'e': /* swetest: a line of labels */
                case 'q': /* swetest: delta t */
                case 'y': /* swetest: time equation */
                case 'x': /* swetest: sidereal time */
                case 'b': /* swetest: ayanamsha */
                case 's': /* swetest: an asteroid, with number given in -xs[number] */
                case 'v': /* swetest: a planetary moon, with number given in -xv[number] */
                case 'z': /* swetest: a fictitious body, number given in -xz[number] */
                case 'd': /* swetest: default (main) factors 0123456789mtABC */
                case 'p': /* swetest: main factors ('d') plus main asteroids DEFGHI */
                case 'h': /* swetest: fictitious factors JKLMNOPQRSTUVWXYZw */
                case 'a': /* swetest: all factors, like 'p'+'h' */
                    return -1;
            }
            return -2;
        }

        static Int32 ut_to_lmt_lat(double t_ut, double[] geopos, out double t_ret, ref string serr)
        {
            Int32 iflgret = SwissEph.OK;
            if ((time_flag & (BIT_TIME_LMT | BIT_TIME_LAT)) != 0)
            {
                t_ut += geopos[0] / 360.0;
                if ((time_flag & BIT_TIME_LAT) != 0)
                {
                    iflgret = sweph.swe_lmt_to_lat(t_ut, geopos[0], out t_ut, ref serr);
                }
            }
            t_ret = t_ut;
            return iflgret;
        }

        static Int32 orbital_elements(double tjd_et, Int32 ipl, Int32 iflag, ref string serr)
        {
            Int32 retval;
            double[] dret = new double[20]; double jut = 0;
            Int32 jyear = 0, jmon = 0, jday = 0;
            string sdateperi = string.Empty;
            retval = sweph.swe_get_orbital_elements(tjd_et, ipl, iflag, dret, ref serr);
            if (retval == SwissEph.ERR)
            {
                printf("%s\n", serr);
                return SwissEph.ERR;
            }
            else
            {
                sweph.swe_revjul(dret[14], gregflag, ref jyear, ref jmon, ref jday, ref jut);
                sdateperi = C.sprintf("%2d.%02d.%04d,%s", jday, jmon, jyear, hms(jut, BIT_LZEROES));
                printf("semiaxis         \t%f\neccentricity     \t%f\ninclination      \t%f\nasc. node       \t%f\narg. pericenter  \t%f\npericenter       \t%f\n", dret[0], dret[1], dret[2], dret[3], dret[4], dret[5]);
                printf("mean longitude   \t%f\nmean anomaly     \t%f\necc. anomaly     \t%f\ntrue anomaly     \t%f\n", dret[9], dret[6], dret[8], dret[7]);
                printf("time pericenter  \t%f %s\ndist. pericenter \t%f\ndist. apocenter  \t%f\n", dret[14], sdateperi, dret[15], dret[16]);
                printf("mean daily motion\t%f\nsid. period (y)  \t%f\ntrop. period (y) \t%f\nsynodic cycle (d)\t%f\n", dret[11], dret[10], dret[12], dret[13]);
            }
            return SwissEph.OK;
        }

        static void insert_gap_string_for_tabs(ref string sout, string gap)
        {
            //char* sp;
            //char s[LEN_SOUT];
            if (!have_gap_parameter)
                return;
            if (gap.StartsWith("\t", StringComparison.Ordinal))
                return;
            //while ((sp = strchr(sout, '\t')) != NULL && strlen(sout) + strlen(gap) < LEN_SOUT) {
            //    strcpy(s, sp + 1);
            //    strcpy(sp, gap);
            //    strcat(sp, s);
            //}
            sout = sout?.Replace("\t", gap, StringComparison.Ordinal);
        }

        static int print_rise_set_line(double trise, double tset, double[] geopos, ref string serr)
        {
            double t0;
            int retc = SwissEph.OK;
            sout = string.Empty;
            if (trise != 0) retc = ut_to_lmt_lat(trise, geopos, out trise, ref serr);
            if (tset != 0) retc = ut_to_lmt_lat(tset, geopos, out tset, ref serr);
            sout = "rise     ";
            if (have_gap_parameter) sout += "\t";  // C.sprintf(sout + strlen(sout), "\t");
            if (trise == 0)
            {
                sout += "         -\t           -    ";
            }
            else
            {
                sweph.swe_revjul(trise, gregflag, ref jyear, ref jmon, ref jday, ref jut);
                sout += C.sprintf("%2d.%02d.%04d\t%s    ", jday, jmon, jyear, hms(jut, BIT_LZEROES));
            }
            if (have_gap_parameter) sout += "\t";   // sprintf(sout + strlen(sout), "\t");
            sout += "set      ";
            if (have_gap_parameter) sout += "\t";  // C.sprintf(sout + strlen(sout), "\t");
            if (tset == 0)
            {
                sout += "         -\t           -    ";
            }
            else
            {
                sweph.swe_revjul(tset, gregflag, ref jyear, ref jmon, ref jday, ref jut);
                sout += C.sprintf("%2d.%02d.%04d\t%s    ", jday, jmon, jyear, hms(jut, BIT_LZEROES));
            }
            if (trise != 0 && tset != 0)
            {
                if (have_gap_parameter) sout += "\t";  // C.sprintf(sout + strlen(sout), "\t");
                sout += "dt =";
                if (have_gap_parameter) sout += "\t";  // C.sprintf(sout + strlen(sout), "\t");
                t0 = (tset - trise) * 24;
                sout += C.sprintf("%s", hms(t0, BIT_LZEROES));
            }
            sout += "\n";
            if (have_gap_parameter) insert_gap_string_for_tabs(ref sout, gap);
            do_printf(sout);
            return retc;
        }

        static Int32 call_rise_set(double t_ut, Int32 ipl, string star, Int32 whicheph, double[] geopos, ref string serr)
        {
            int ii, rval, loop_count;
            Int32 rsmi = 0;
            double dayfrac = 0.0001;
            double[] tret = new double[10]; double trise = 0, tset = 0, tnext = 0, tret1sv = 0;
            bool do_rise, do_set;
            bool last_was_empty = false;
            Int32 retc = SwissEph.OK;
            int rsmior = 0;
            if (norefrac != 0) rsmior |= SwissEph.SE_BIT_NO_REFRACTION;
            if (disccenter != 0) rsmior |= SwissEph.SE_BIT_DISC_CENTER;
            if (discbottom != 0) rsmior |= SwissEph.SE_BIT_DISC_BOTTOM;
            if (hindu != 0) rsmior |= SwissEph.SE_BIT_HINDU_RISING;
            if (Math.Abs(geopos[1]) < 60 && ipl >= SwissEph.SE_SUN && ipl <= SwissEph.SE_PLUTO)
                dayfrac = 0.01;
            sweph.swe_set_topo(geopos[0], geopos[1], geopos[2]);
            // "geo. long 8.000000, lat 47.000000, alt 0.000000"
            if (with_header)
                printf("\ngeo. long %f, lat %f, alt %f", geopos[0], geopos[1], geopos[2]);
            do_printf("\n");
            tnext = t_ut;
            // the code is designed for looping with -nxxx over many days, during which
            // the object might become circumpolar, or never rise at all.
            while (special_event == SP_RISE_SET && tnext < t_ut + nstep)
            {
                // the following 'if' avoids unnecessary calculations for circumpolar 
                // objects. even without it, the output would be correct, but 
                // could be considerably slower.
                if (last_was_empty && string.IsNullOrEmpty(star))
                {
                    rval = sweph.swe_calc_ut(tnext, ipl, whicheph | SwissEph.SEFLG_EQUATORIAL, tret, ref serr);
                    if (rval >= 0)
                    {
                        double edist = geopos[1] + tret[1];
                        double edist2 = geopos[1] - tret[1];
                        if ((edist - 2 > 90 || edist + 2 < -90)
                        || (edist2 - 2 > 90 || edist2 + 2 < -90))
                        {
                            tnext += 1;
                            continue;
                        }
                    }
                }
                /* rising */
                rsmi = SwissEph.SE_CALC_RISE | rsmior;
                rval = sweph.swe_rise_trans(tnext, ipl, star, whicheph, rsmi, geopos, datm[0], datm[1], ref trise, ref serr);
                if (rval == SwissEph.ERR)
                {
                    do_printf(serr);
                    Environment.Exit(0);
                }
                do_rise = (rval == SwissEph.OK);
                /* setting */
                rsmi = SwissEph.SE_CALC_SET | rsmior;
                do_set = false;
                loop_count = 0;
                //tnext = trise; // dieter 14-feb-17
                while (!do_set && loop_count < 2)
                {
                    rval = sweph.swe_rise_trans(tnext, ipl, star, whicheph, rsmi, geopos, datm[0], datm[1], ref tset, ref serr);
                    if (rval == SwissEph.ERR)
                    {
                        do_printf(serr);
                        Environment.Exit(0);
                    }
                    do_set = (rval == SwissEph.OK);
                    if (!do_set && do_rise)
                    {
                        tnext = trise;
                    }
                    loop_count++;
                }
                if (do_rise && do_set && trise > tset)
                {
                    do_rise = false;    // ignore rises happening before setting
                    trise = 0;  // we hope that exact time 0 never happens, is highly unlikely.
                }
                if (do_rise && do_set)
                {
                    rval = print_rise_set_line(trise, tset, geopos, ref serr);
                    last_was_empty = false;
                    tnext = tset + dayfrac;
                }
                else if (do_rise && !do_set)
                {
                    rval = print_rise_set_line(trise, 0, geopos, ref serr);
                    last_was_empty = false;
                    tnext = trise + dayfrac;
                }
                else if (do_set && !do_rise)
                {
                    tnext = tset + dayfrac;
                    rval = print_rise_set_line(0, tset, geopos, ref serr);
                    last_was_empty = false;
                }
                else
                { // neither rise nor set 
                  // for sequences of days without rise or set, the line '-   -' is printed only once.
                    if (!last_was_empty) rval = print_rise_set_line(0, 0, geopos, ref serr);
                    tnext += 1;
                    last_was_empty = true;
                }
                if (rval == SwissEph.ERR)
                {
                    do_printf(serr);
                    Environment.Exit(0);
                }
                if (nstep == 1) break;
            }
            /* swetest -metr
             * calculate and print transits over meridian (midheaven and lower
             * midheaven */
            if (special_event == SP_MERIDIAN_TRANSIT)
            {
                /* loop over days */
                for (ii = 0; ii < nstep; ii++, t_ut = tret1sv + 0.001)
                {
                    /* transit over midheaven */
                    if (sweph.swe_rise_trans(t_ut, ipl, star, whicheph, SwissEph.SE_CALC_MTRANSIT, geopos, datm[0], datm[1], ref (tret[0]), ref serr) != SwissEph.OK)
                    {
                        do_printf(serr);
                        return SwissEph.ERR;
                    }
                    /* transit over lower midheaven */
                    if (sweph.swe_rise_trans(t_ut, ipl, star, whicheph, SwissEph.SE_CALC_ITRANSIT, geopos, datm[0], datm[1], ref (tret[1]), ref serr) != SwissEph.OK)
                    {
                        do_printf(serr);
                        return SwissEph.ERR;
                    }
                    tret1sv = tret[1];
                    if ((time_flag & (BIT_TIME_LMT | BIT_TIME_LAT)) != 0)
                    {
                        retc = ut_to_lmt_lat(tret[0], geopos, out (tret[0]), ref serr);
                        retc = ut_to_lmt_lat(tret[1], geopos, out (tret[1]), ref serr);
                    }
                    sout = "mtransit ";
                    if (have_gap_parameter) sout += "\t"; ;
                    if (tret[0] == 0 || tret[0] > tret[1]) sout += "         -\t           -    ";
                    else
                    {
                        sweph.swe_revjul(tret[0], gregflag, ref jyear, ref jmon, ref jday, ref jut);
                        sout += C.sprintf("%2d.%02d.%04d\t%s    ", jday, jmon, jyear, hms(jut, BIT_LZEROES));
                    }
                    if (have_gap_parameter) sout += "\t"; ;
                    sout += "itransit ";
                    if (have_gap_parameter) sout += "\t"; ;
                    if (tret[1] == 0) sout += "         -\t           -    \n";
                    else
                    {
                        sweph.swe_revjul(tret[1], gregflag, ref jyear, ref jmon, ref jday, ref jut);
                        sout += C.sprintf("%2d.%02d.%04d\t%s\n", jday, jmon, jyear, hms(jut, BIT_LZEROES));
                    }
                    if (have_gap_parameter) insert_gap_string_for_tabs(ref sout, gap);
                    do_printf(sout);
                }
            }
            return retc;
        }

        static string get_gregjul(int gregflag, int year)
        {
            if (gregflag == SwissEph.SE_JUL_CAL) return "jul";
            if (year < 1700) return "greg";
            return string.Empty;
        }

        // print lon and lat string in minute precision
        static void format_lon_lat(out string slon, out string slat, double lon, double lat)
        {
            int roundflag, ideg, imin, isec, isgn;
            double dsecfr;
            char c;
            roundflag = SwissEph.SE_SPLIT_DEG_ROUND_SEC;
            sweph.swe_split_deg(lon, roundflag, out ideg, out imin, out isec, out dsecfr, out isgn);
            c = (lon < 0) ? 'w' : 'e';
            slon = C.sprintf("%d%c%02d%02d", Math.Abs(ideg), c, imin, isec);
            sweph.swe_split_deg(lat, roundflag, out ideg, out imin, out isec, out dsecfr, out isgn);
            c = (lat < 0) ? 's' : 'n';
            slat = C.sprintf("%d%c%02d%02d", Math.Abs(ideg), c, imin, isec);
        }

        static Int32 call_lunar_eclipse(double t_ut, Int32 whicheph, Int32 special_mode, double[] geopos, ref string serr)
        {
            int i, ii, retc = SwissEph.OK, eclflag, ecl_type = 0;
            int rval, ihou, imin, isec, isgn;
            double dfrc; double[] attr = new double[30]; double dt; double[] xx = new double[6], geopos_max = new double[3];
            string s1 = string.Empty, s2 = string.Empty, sout_short = string.Empty, sfmt = string.Empty, styp = "none", sgj;
            string slon = string.Empty, slat = string.Empty, saros = string.Empty;
            geopos_max[0] = 0; geopos_max[1] = 0;
            /* no selective eclipse type set, set all */
            if (with_chart_link) do_printf("<pre>");
            if ((search_flag & SwissEph.SE_ECL_ALLTYPES_LUNAR) == 0)
                search_flag |= SwissEph.SE_ECL_ALLTYPES_LUNAR;
            // "geo. long 8.000000, lat 47.000000, alt 0.000000"
            if ((special_mode & SP_MODE_LOCAL) != 0)
            {
                if (with_header)
                    printf("\ngeo. long %f, lat %f, alt %f", geopos[0], geopos[1], geopos[2]);
            }
            do_printf("\n");
            for (ii = 0; ii < nstep; ii++, t_ut += direction)
            {
                sout = String.Empty;
                /* swetest -lunecl -how 
                 * type of lunar eclipse and percentage for a given time: */
                if ((special_mode & SP_MODE_HOW) != 0)
                {
                    if ((eclflag = sweph.swe_lun_eclipse_how(t_ut, whicheph, geopos, attr, ref serr)) == SwissEph.ERR)
                    {
                        do_printf(serr);
                        return SwissEph.ERR;
                    }
                    else
                    {
                        if ((eclflag & SwissEph.SE_ECL_TOTAL) != 0)
                        {
                            ecl_type = ECL_LUN_TOTAL;
                            sfmt = "total lunar eclipse: %f o/o \n";
                        }
                        else if ((eclflag & SwissEph.SE_ECL_PARTIAL) != 0)
                        {
                            ecl_type = ECL_LUN_PARTIAL;
                            sfmt = "partial lunar eclipse: %f o/o \n";
                        }
                        else if ((eclflag & SwissEph.SE_ECL_PENUMBRAL) != 0)
                        {
                            ecl_type = ECL_LUN_PENUMBRAL;
                            sfmt = "penumbral lunar eclipse: %f o/o \n";
                        }
                        else
                        {
                            sfmt = "no lunar eclipse \n";
                        }
                        sout = sfmt;
                        if (sfmt.IndexOf('%', StringComparison.Ordinal) >= 0)
                        {
                            sout = C.sprintf(sfmt, attr[0]);
                        }
                        do_printf(sout);
                    }
                    continue;
                }
                /* swetest -lunecl 
                 * find next lunar eclipse: */
                /* locally visible lunar eclipse */
                if ((special_mode & SP_MODE_LOCAL) != 0)
                {
                    if ((eclflag = sweph.swe_lun_eclipse_when_loc(t_ut, whicheph, geopos, tret, attr, direction_flag, ref serr)) == SwissEph.ERR)
                    {
                        do_printf(serr);
                        return SwissEph.ERR;
                    }
                    if ((time_flag & (BIT_TIME_LMT | BIT_TIME_LAT)) != 0)
                    {
                        for (i = 0; i < 10; i++)
                        {
                            if (tret[i] != 0)
                            {
                                retc = ut_to_lmt_lat(tret[i], geopos, out tret[i], ref serr);
                                if (retc == SwissEph.ERR)
                                {
                                    do_printf(serr);
                                    return SwissEph.ERR;
                                }
                            }
                        }
                    }
                    t_ut = tret[0];
                    if ((eclflag & SwissEph.SE_ECL_TOTAL) != 0)
                    {
                        sout = "total   ";
                        ecl_type = ECL_LUN_TOTAL;
                    }
                    if ((eclflag & SwissEph.SE_ECL_PENUMBRAL) != 0)
                    {
                        sout = "penumb. ";
                        ecl_type = ECL_LUN_PENUMBRAL;
                    }
                    if ((eclflag & SwissEph.SE_ECL_PARTIAL) != 0)
                    {
                        sout = "partial ";
                        ecl_type = ECL_LUN_PARTIAL;
                    }
                    sout = "lunar eclipse\t";
                    sweph.swe_revjul(t_ut, gregflag, ref jyear, ref jmon, ref jday, ref jut);
                    sgj = get_gregjul(gregflag, jyear);
                    /*if ((eclflag = swe_lun_eclipse_how(t_ut, whicheph, geopos, attr, serr)) == ERR) {
                      do_printf(serr);
                      return ERR;
                    }*/
                    dt = (tret[3] - tret[2]) * 24 * 60;
                    s1 = C.sprintf("%d min %4.2f sec", (int)dt, (dt % 1.0) * 60);
                    /* short output: 
                     * date, time of day, umbral magnitude, umbral duration, saros series, member number */
                    saros = C.sprintf("%d/%d", (int)attr[9], (int)attr[10]);
                    sout_short = C.sprintf("%s\t%2d.%2d.%4d%s\t%s\t%.3f\t%s\t%s\n", sout, jday, jmon, jyear, sgj, hms(jut, 0), attr[8], s1, saros);
                    sout += C.sprintf("%2d.%02d.%04d%s\t%s\t%.4f/%.4f\tsaros %s\t%.6f\n", jday, jmon, jyear, sgj, hms(jut, BIT_LZEROES), attr[0], attr[1], saros, t_ut);
                    /* second line:
                     * eclipse times, penumbral, partial, total begin and end */
                    if (have_gap_parameter) sout += "\t";
                    if ((eclflag & SwissEph.SE_ECL_PENUMBBEG_VISIBLE) != 0)
                        sout += C.sprintf("  %s ", hms_from_tjd(tret[6]));
                    else
                        sout += ("      -         ");
                    if (have_gap_parameter) sout += "\t";
                    if ((eclflag & SwissEph.SE_ECL_PARTBEG_VISIBLE) != 0)
                        sout += C.sprintf("%s ", hms_from_tjd(tret[2]));
                    else
                        sout += ("    -         ");
                    if (have_gap_parameter) sout += "\t";
                    if ((eclflag & SwissEph.SE_ECL_TOTBEG_VISIBLE) != 0)
                        sout += C.sprintf("%s ", hms_from_tjd(tret[4]));
                    else
                        sout += ("    -         ");
                    if (have_gap_parameter) sout += "\t";
                    if ((eclflag & SwissEph.SE_ECL_TOTEND_VISIBLE) != 0)
                        sout += C.sprintf("%s ", hms_from_tjd(tret[5]));
                    else
                        sout += ("    -         ");
                    if (have_gap_parameter) sout += "\t";
                    if ((eclflag & SwissEph.SE_ECL_PARTEND_VISIBLE) != 0)
                        sout += C.sprintf("%s ", hms_from_tjd(tret[3]));
                    else
                        sout += ("    -         ");
                    if (have_gap_parameter) sout += "\t";
                    if ((eclflag & SwissEph.SE_ECL_PENUMBEND_VISIBLE) != 0)
                        sout += C.sprintf("%s ", hms_from_tjd(tret[7]));
                    else
                        sout += ("    -         ");
                    if (have_gap_parameter) sout += "\t";
                    sout += C.sprintf("dt=%.1f", sweph.swe_deltat_ex(tret[0], whicheph, ref serr) * 86400.0);
                    sout += ("\n");
                    /* global lunar eclipse */
                }
                else
                {
                    if ((eclflag = sweph.swe_lun_eclipse_when(t_ut, whicheph, search_flag, tret, direction_flag, ref serr)) == SwissEph.ERR)
                    {
                        do_printf(serr);
                        return SwissEph.ERR;
                    }
                    t_ut = tret[0];
                    if ((eclflag & SwissEph.SE_ECL_TOTAL) != 0)
                    {
                        styp = "Total";
                        sout = "total ";
                        ecl_type = ECL_LUN_TOTAL;
                    }
                    if ((eclflag & SwissEph.SE_ECL_PENUMBRAL) != 0)
                    {
                        styp = "Penumbral";
                        sout = ("penumb. ");
                        ecl_type = ECL_LUN_PENUMBRAL;
                    }
                    if ((eclflag & SwissEph.SE_ECL_PARTIAL) != 0)
                    {
                        styp = "Partial";
                        sout = ("partial ");
                        ecl_type = ECL_LUN_PARTIAL;
                    }
                    sout += ("lunar eclipse\t");
                    if ((eclflag = sweph.swe_lun_eclipse_how(t_ut, whicheph, geopos, attr, ref serr)) == SwissEph.ERR)
                    {
                        do_printf(serr);
                        return SwissEph.ERR;
                    }
                    if ((time_flag & (BIT_TIME_LMT | BIT_TIME_LAT)) != 0)
                    {
                        for (i = 0; i < 10; i++)
                        {
                            if (tret[i] != 0)
                            {
                                retc = ut_to_lmt_lat(tret[i], geopos, out tret[i], ref serr);
                                if (retc == SwissEph.ERR)
                                {
                                    do_printf(serr);
                                    return SwissEph.ERR;
                                }
                            }
                        }
                    }
                    t_ut = tret[0];
                    rval = sweph.swe_calc_ut(t_ut, SwissEph.SE_MOON, whicheph | SwissEph.SEFLG_EQUATORIAL, xx, ref s1);
                    if (rval < 0)
                        C.strcat(ref s1, "\n");
                    do_printf(s1);
                    sweph.swe_revjul(t_ut, gregflag, ref jyear, ref jmon, ref jday, ref jut);
                    geopos_max[0] = sweph.swe_degnorm(xx[0] - sweph.swe_sidtime(t_ut) * 15);
                    if (geopos_max[0] > 180) geopos_max[0] -= 360;
                    geopos_max[1] = xx[1];
                    sgj = get_gregjul(gregflag, jyear);
                    dt = (tret[3] - tret[2]) * 24 * 60;
                    s1 = C.sprintf("%d min %4.2f sec", (int)dt, (dt % 1.0) * 60);
                    /* short output: 
                     * date, time of day, umbral magnitude, umbral duration, saros series, member number */
                    saros = C.sprintf("%d/%d", (int)attr[9], (int)attr[10]);
                    sout_short = C.sprintf("%s\t%2d.%2d.%4d%s\t%s\t%.3f\t%s\t%s\n", sout, jday, jmon, jyear, sgj, hms(jut, 0), attr[8], s1, saros);
                    //sout += C.sprintf("%2d.%02d.%04d%s\t%s\t%.4f/%.4f\tsaros %s\t%.6f\tdt=%.2f\n", jday, jmon, jyear, sgj, hms(jut, BIT_LZEROES), attr[0], attr[1], saros, t_ut, sweph.swe_deltat_ex(t_ut, whicheph, ref serr) * 86400);
                    sout += C.sprintf("%2d.%02d.%04d%s\t%s\t%.4f/%.4f\tsaros %s\t%.6f\n", jday, jmon, jyear, sgj, hms(jut, BIT_LZEROES), attr[0], attr[1], saros, t_ut);
                    /* second line:
                     * eclipse times, penumbral, partial, total begin and end */
                    if (have_gap_parameter) sout += "\t";
                    sout += C.sprintf("  %s ", hms_from_tjd(tret[6]));
                    if (have_gap_parameter) sout += "\t";
                    if (tret[2] != 0)
                        // swetest.c:3204: sprintf(sout + strlen(sout), ...) appends;
                        // the plain assignment here dropped the eclipse label, the
                        // date/magnitude/saros line and the penumbral time above it.
                        sout += C.sprintf("%s ", hms_from_tjd(tret[2]));
                    else
                        sout += ("   -         ");
                    if (have_gap_parameter) sout += "\t";
                    if (tret[4] != 0)
                        sout += C.sprintf("%s ", hms_from_tjd(tret[4]));
                    else
                        sout += ("   -         ");
                    if (have_gap_parameter) sout += "\t";
                    if (tret[5] != 0)
                        sout += C.sprintf("%s ", hms_from_tjd(tret[5]));
                    else
                        sout += ("   -         ");
                    if (have_gap_parameter) sout += "\t";
                    if (tret[3] != 0)
                        sout += C.sprintf("%s ", hms_from_tjd(tret[3]));
                    else
                        sout += ("   -         ");
                    if (have_gap_parameter) sout += "\t";
                    sout += C.sprintf("%s", hms_from_tjd(tret[7]));
                    if (have_gap_parameter) sout += "\t";
                    sout += C.sprintf("dt=%.1f", sweph.swe_deltat_ex(tret[0], whicheph, ref serr) * 86400.0);
                    sout += "\n";
                    if ((special_mode & SP_MODE_HOCAL) != 0)
                    {
                        sweph.swe_split_deg(jut, SwissEph.SE_SPLIT_DEG_ROUND_MIN, out ihou, out imin, out isec, out dfrc, out isgn);
                        sout = C.sprintf("\"%04d%s %02d %02d %02d.%02d %d\",\n", jyear, sgj, jmon, jday, ihou, imin, ecl_type);
                    }
                    sout += C.sprintf("\t%s\t%s\n", C.strcpy(out s1, dms(geopos_max[0], BIT_ROUND_SEC)), C.strcpy(out s2, dms(geopos_max[1], BIT_ROUND_SEC)));
                }
                //dt = (tret[7] - tret[6]) * 24 * 60;
                //sout += C.sprintf("\t%d min %4.2f sec\n", (int) dt, (dt % 1.0) * 60);
                if (have_gap_parameter) insert_gap_string_for_tabs(ref sout, gap);
                if (short_output)
                {
                    do_printf(sout_short);
                }
                else
                {
                    do_printf(sout);
                }
                if (with_chart_link)
                {
                    string snat;
                    string stim;
                    int iflg = 0;
                    char cal = gregflag != 0 ? 'g' : 'j';
                    lcount++;
                    C.strcpy(out stim, hms(jut, BIT_LZEROES));
                    format_lon_lat(out slon, out slat, geopos_max[0], geopos_max[1]);
                    //while (*stim == ' ') our_strcpy(stim, stim + 1);
                    stim = stim.TrimStart();
                    if (stim.StartsWith("0", StringComparison.Ordinal)) our_strcpy(out stim, stim.Substring(1));
                    snat = C.sprintf("Lunar Eclipse %s,%s,e,%d,%d,%d,%s,h0e,%cnu,%d,Moon Zenith location,,%s,%s,u,0,0,0", saros, styp, jday, jmon, jyear, stim, cal, iflg, slon, slat);
                    sout = C.sprintf("<a id='swepop%dl' href='/cgi/chart.cgi?muasp=1;nhor=1;act=chmnat;nd1=%s;rs=1;iseclipse=1' target='eclipse'>chart popup</a>", lcount, snat);
                    do_printf(sout);
                    sout = C.sprintf(" <a href='/cgi/chart.cgi?muasp=1;nhor=1;act=chmnat;nd1=%s;rs=1;iseclipse=1' target='eclipse'>chart link</a>\n\n", snat);
                    do_printf(sout);
                }
            }
            if (with_chart_link) do_printf("</pre>\n");
            return SwissEph.OK;
        }

        static Int32 call_solar_eclipse(double t_ut, Int32 whicheph, Int32 special_mode, double[] geopos, ref string serr)
        {
            int i, ii, retc = SwissEph.OK, eclflag, ecl_type = 0;
            double dt; double[] tret = new double[30], attr = new double[30], geopos_max = new double[3];
            string slon = string.Empty, slat = string.Empty, saros = string.Empty;
            string s1 = string.Empty, s2 = string.Empty, sout_short = string.Empty, styp = "none", sgj;
            bool has_found = false;
            /* no selective eclipse type set, set all */
            if (with_chart_link) do_printf("<pre>");
            if ((search_flag & SwissEph.SE_ECL_ALLTYPES_SOLAR) == 0)
                search_flag |= SwissEph.SE_ECL_ALLTYPES_SOLAR;
            /* for local eclipses: set geographic position of observer */
            if ((special_mode & SP_MODE_LOCAL) != 0)
            {
                sweph.swe_set_topo(geopos[0], geopos[1], geopos[2]);
                // "geo. long 8.000000, lat 47.000000, alt 0.000000"
                if (with_header)
                    printf("\ngeo. long %f, lat %f, alt %f", geopos[0], geopos[1], geopos[2]);
            }
            do_printf("\n");
            for (ii = 0; ii < nstep; ii++, t_ut += direction)
            {
                sout = String.Empty;
                /* swetest -solecl -local -geopos...
                 * find next solar eclipse observable from a given geographic position */
                if ((special_mode & SP_MODE_LOCAL) != 0)
                {
                    if ((eclflag = sweph.swe_sol_eclipse_when_loc(t_ut, whicheph, geopos, tret, attr, direction_flag, ref serr)) == SwissEph.ERR)
                    {
                        do_printf(serr);
                        return SwissEph.ERR;
                    }
                    else
                    {
                        has_found = false;
                        t_ut = tret[0];
                        if ((search_flag & SwissEph.SE_ECL_TOTAL) != 0 && (eclflag & SwissEph.SE_ECL_TOTAL) != 0)
                        {
                            sout = ("total   ");
                            has_found = true;
                            ecl_type = ECL_SOL_TOTAL;
                        }
                        if ((search_flag & SwissEph.SE_ECL_ANNULAR) != 0 && (eclflag & SwissEph.SE_ECL_ANNULAR) != 0)
                        {
                            sout = ("annular ");
                            has_found = true;
                            ecl_type = ECL_SOL_ANNULAR;
                        }
                        if ((search_flag & SwissEph.SE_ECL_PARTIAL) != 0 && (eclflag & SwissEph.SE_ECL_PARTIAL) != 0)
                        {
                            sout = ("partial ");
                            has_found = true;
                            ecl_type = ECL_SOL_PARTIAL;
                        }
                        if (have_gap_parameter) sout += "\t";
                        if (!has_found)
                        {
                            ii--;
                        }
                        else
                        {
                            sweph.swe_calc(t_ut + sweph.swe_deltat_ex(t_ut, whicheph, ref serr), SwissEph.SE_ECL_NUT, 0, x, ref serr);
                            if ((time_flag & (BIT_TIME_LMT | BIT_TIME_LAT)) != 0)
                            {
                                for (i = 0; i < 10; i++)
                                {
                                    if (tret[i] != 0)
                                    {
                                        retc = ut_to_lmt_lat(tret[i], geopos, out tret[i], ref serr);
                                        if (retc == SwissEph.ERR)
                                        {
                                            do_printf(serr);
                                            return SwissEph.ERR;
                                        }
                                    }
                                }
                            }
                            t_ut = tret[0];
                            sweph.swe_revjul(t_ut, gregflag, ref jyear, ref jmon, ref jday, ref jut);
                            dt = (tret[3] - tret[2]) * 24 * 60;
                            sgj = get_gregjul(gregflag, jyear);
                            saros = C.sprintf("%d/%d", (int)attr[9], (int)attr[10]);
                            sout += C.sprintf("%2d.%02d.%04d%s\t%s\t%.4f/%.4f/%.4f\tsaros %s\t%.6f\n", jday, jmon, jyear, sgj, hms(jut, BIT_LZEROES), attr[8], attr[0], attr[2], saros, t_ut);
                            sout += C.sprintf("\t%d min %4.2f sec\t", (int)dt, C.fmod(dt, 1) * 60);
                            if ((eclflag & SwissEph.SE_ECL_1ST_VISIBLE) != 0)
                            {
                                sout += C.sprintf("%s ", hms_from_tjd(tret[1]));
                            }
                            else
                            {
                                sout += "   -         ";
                            }
                            if (have_gap_parameter) sout += "\t";
                            if ((eclflag & SwissEph.SE_ECL_2ND_VISIBLE) != 0)
                            {
                                sout += C.sprintf("%s ", hms_from_tjd(tret[2]));
                            }
                            else
                            {
                                sout += "   -         ";
                            }
                            if (have_gap_parameter) sout += "\t";
                            if ((eclflag & SwissEph.SE_ECL_3RD_VISIBLE) != 0)
                            {
                                sout += C.sprintf("%s ", hms_from_tjd(tret[3]));
                            }
                            else
                            {
                                sout += "   -         ";
                            }
                            if (have_gap_parameter) sout += "\t";
                            if ((eclflag & SwissEph.SE_ECL_4TH_VISIBLE) != 0)
                            {
                                sout += C.sprintf("%s ", hms_from_tjd(tret[4]));
                            }
                            else
                            {
                                sout += "   -         ";
                            }
                            if (have_gap_parameter) sout += "\t";
                            //#if 0
                            //      sprintf(sout + strlen(sout), "\t%d min %4.2f sec   %s %s %s %s", 
                            //                (int) dt, fmod(dt, 1) * 60, 
                            //                strcpy(s1, hms(fmod(tret[1] + 0.5, 1) * 24, BIT_LZEROES)), 
                            //                strcpy(s3, hms(fmod(tret[2] + 0.5, 1) * 24, BIT_LZEROES)), 
                            //                strcpy(s4, hms(fmod(tret[3] + 0.5, 1) * 24, BIT_LZEROES)),
                            //                strcpy(s2, hms(fmod(tret[4] + 0.5, 1) * 24, BIT_LZEROES)));
                            //#endif
                            sout += C.sprintf("dt=%.1f", sweph.swe_deltat_ex(tret[0], whicheph, ref serr) * 86400.0);
                            sout += ("\n");
                            if (have_gap_parameter) insert_gap_string_for_tabs(ref sout, gap);
                            do_printf(sout);
                        }
                    }
                }   /* endif search_local */
                /* swetest -solecl
                 * find next solar eclipse observable from anywhere on earth */
                if (0 == (special_mode & SP_MODE_LOCAL))
                {
                    if ((eclflag = sweph.swe_sol_eclipse_when_glob(t_ut, whicheph, search_flag, tret, direction_flag, ref serr)) == SwissEph.ERR)
                    {
                        do_printf(serr);
                        return SwissEph.ERR;
                    }
                    t_ut = tret[0];
                    if ((eclflag & SwissEph.SE_ECL_TOTAL) != 0)
                    {
                        styp = "Total";
                        sout = ("total");
                        ecl_type = ECL_SOL_TOTAL;
                    }
                    if ((eclflag & SwissEph.SE_ECL_ANNULAR) != 0)
                    {
                        styp = "Annular";
                        sout = ("annular");
                        ecl_type = ECL_SOL_ANNULAR;
                    }
                    if ((eclflag & SwissEph.SE_ECL_ANNULAR_TOTAL) != 0)
                    {
                        styp = "Annular-Total";
                        sout = ("ann-tot");
                        ecl_type = ECL_SOL_ANNULAR;	/* by Alois: what is this ? */
                    }
                    if ((eclflag & SwissEph.SE_ECL_PARTIAL) != 0)
                    {
                        styp = "Partial";
                        sout = ("partial");
                        ecl_type = ECL_SOL_PARTIAL;
                    }
                    if ((eclflag & SwissEph.SE_ECL_NONCENTRAL) != 0 && 0 == (eclflag & SwissEph.SE_ECL_PARTIAL))
                        sout = " non-central";
                    sout += C.sprintf(" solar\t");
                    sweph.swe_sol_eclipse_where(t_ut, whicheph, geopos_max, attr, ref serr);
                    if ((time_flag & (BIT_TIME_LMT | BIT_TIME_LAT)) != 0)
                    {
                        for (i = 0; i < 10; i++)
                        {
                            if (tret[i] != 0)
                            {
                                retc = ut_to_lmt_lat(tret[i], geopos, out tret[i], ref serr);
                                if (retc == SwissEph.ERR)
                                {
                                    do_printf(serr);
                                    return SwissEph.ERR;
                                }
                            }
                        }
                    }
                    sweph.swe_revjul(tret[0], gregflag, ref jyear, ref jmon, ref jday, ref jut);
                    sgj = get_gregjul(gregflag, jyear);
                    saros = C.sprintf("%d/%d", (int)attr[9], (int)attr[10]);
                    sout_short = C.sprintf("%s\t%2d.%2d.%4d%s\t%s\t%.3f", sout, jday, jmon, jyear, sgj, hms(jut, 0), attr[8]);
                    sout += C.sprintf("%2d.%02d.%04d%s\t%s\t%f km\t%.4f/%.4f/%.4f\tsaros %s\t%.6f\n", jday, jmon, jyear, sgj, hms(jut, 0), attr[3], attr[8], attr[0], attr[2], saros, tret[0]);
                    sout += C.sprintf("\t%s ", hms_from_tjd(tret[2]));
                    if (have_gap_parameter) sout += "\t";
                    if (tret[4] != 0)
                    {
                        sout += C.sprintf("%s ", hms_from_tjd(tret[4]));
                    }
                    else
                    {
                        sout += ("   -         ");
                    }
                    if (have_gap_parameter) sout += "\t";
                    if (tret[5] != 0)
                    {
                        sout += C.sprintf("%s ", hms_from_tjd(tret[5]));
                    }
                    else
                    {
                        sout += ("   -         ");
                    }
                    if (have_gap_parameter) sout += "\t";
                    sout += C.sprintf("%s", hms_from_tjd(tret[3]));
                    if (have_gap_parameter) sout += "\t";
                    sout += C.sprintf("dt=%.1f", sweph.swe_deltat_ex(tret[0], whicheph, ref serr) * 86400.0);
                    sout += "\n";
                    sout += C.sprintf("\t%s\t%s", C.strcpy(out s1, dms(geopos_max[0], BIT_ROUND_SEC)), C.strcpy(out s2, dms(geopos_max[1], BIT_ROUND_SEC)));
                    sout += ("\t");
                    sout_short += ("\t");
                    if (0 == (eclflag & SwissEph.SE_ECL_PARTIAL) && 0 == (eclflag & SwissEph.SE_ECL_NONCENTRAL))
                    {
                        if ((eclflag = sweph.swe_sol_eclipse_when_loc(t_ut - 10, whicheph, geopos_max, tret, attr, false, ref serr)) == SwissEph.ERR)
                        {
                            do_printf(serr);
                            return SwissEph.ERR;
                        }
                        if (Math.Abs(tret[0] - t_ut) > 2)
                        {
                            do_printf("when_loc returns wrong date\n");
                        }
                        dt = (tret[3] - tret[2]) * 24 * 60;
                        s1 = C.sprintf("%d min %4.2f sec", (int)dt, (dt % 1.0) * 60);
                        sout += (s1);
                        sout_short += (s1);
                    }
                    sout_short += C.sprintf("\t%d\t%d", (int)attr[9], (int)attr[10]);
                    sout += ("\n");
                    sout_short += ("\n");
                    if ((special_mode & SP_MODE_HOCAL) != 0)
                    {
                        int ihou, imin, isec, isgn;
                        double dfrc;
                        sweph.swe_split_deg(jut, SwissEph.SE_SPLIT_DEG_ROUND_MIN, out ihou, out imin, out isec, out dfrc, out isgn);
                        sout = C.sprintf("\"%04d%s %02d %02d %02d.%02d %d\",\n", jyear, sgj, jmon, jday, ihou, imin, ecl_type);
                    }
                    /*printf("len=%ld\n", strlen(sout));*/
                    if (short_output)
                    {
                        do_printf(sout_short);
                    }
                    else
                    {
                        if (have_gap_parameter) insert_gap_string_for_tabs(ref sout, gap);
                        do_printf(sout);
                    }
                    if (with_chart_link)
                    {
                        string snat;
                        string stim;
                        int iflg = 0; // NAT_IFLG_UNKNOWN_TIME;
                        char cal = gregflag != 0 ? 'g' : 'j';
                        scount++;
                        format_lon_lat(out slon, out slat, geopos_max[0], geopos_max[1]);
                        C.strcpy(out stim, hms(jut, BIT_LZEROES));
                        //while (*stim == ' ') our_strcpy(stim, stim + 1);
                        stim = stim.TrimStart();
                        if (stim.StartsWith("0", StringComparison.Ordinal)) our_strcpy(out stim, stim.Substring(1));
                        snat = C.sprintf("Solar Eclipse %s,%s,e,%d,%d,%d,%s,h0e,%cnu,%d,Location of Maximum,,%s,%s,u,0,0,0", saros, styp, jday, jmon, jyear, stim, cal, iflg, slon, slat);
                        sout = C.sprintf("<a id='swepop%ds' href='/cgi/chart.cgi?muasp=1;nhor=1;act=chmnat;nd1=%s;rs=1;iseclipse=1;topo=1' target='eclipse'>chart popup</a>", scount, snat);
                        do_printf(sout);
                        sout = C.sprintf(" <a href='/cgi/chart.cgi?muasp=1;nhor=1;act=chmnat;nd1=%s;rs=1;iseclipse=1;topo=1' target='eclipse'>chart link</a>\n\n", snat);
                        do_printf(sout);
                    }
                }
            }
            if (with_chart_link) do_printf("</pre>\n");
            return SwissEph.OK;
        }

        static Int32 call_lunar_occultation(double t_ut, Int32 ipl, string star, Int32 whicheph, Int32 special_mode, double[] geopos, ref string serr)
        {
            int i, ii, ecl_type = 0, eclflag, retc = SwissEph.OK;
            double dt; double[] tret = new double[30], attr = new double[30], geopos_max = new double[3];
            string s1 = String.Empty, s2 = String.Empty;
            bool has_found = false;
            int nloops = 0;
            /* no selective eclipse type set, set all */
            if ((search_flag & SwissEph.SE_ECL_ALLTYPES_SOLAR) == 0)
                search_flag |= SwissEph.SE_ECL_ALLTYPES_SOLAR;
            /* for local occultations: set geographic position of observer */
            if ((special_mode & SP_MODE_LOCAL) != 0)
            {
                sweph.swe_set_topo(geopos[0], geopos[1], geopos[2]);
                if (with_header)
                    printf("\ngeo. long %f, lat %f, alt %f", geopos[0], geopos[1], geopos[2]);
            }
            do_printf("\n");
            for (ii = 0; ii < nstep; ii++)
            {
                sout = String.Empty;
                nloops++;
                if (nloops > SEARCH_RANGE_LUNAR_CYCLES)
                {
                    serr = C.sprintf("event search ended after %d lunar cycles at jd=%f\n", SEARCH_RANGE_LUNAR_CYCLES, t_ut);
                    do_printf(serr);
                    return SwissEph.ERR;
                }
                if ((special_mode & SP_MODE_LOCAL) != 0)
                {
                    /* * local search for occultation, test one lunar cycle only (SE_ECL_ONE_TRY) */
                    if (ipl != SwissEph.SE_SUN)
                    {
                        search_flag &= ~(SwissEph.SE_ECL_ANNULAR | SwissEph.SE_ECL_ANNULAR_TOTAL);
                        if (search_flag == 0)
                            search_flag = SwissEph.SE_ECL_ALLTYPES_SOLAR;
                    }
                    if ((eclflag = sweph.swe_lun_occult_when_loc(t_ut, ipl, star, whicheph, geopos, tret, attr, direction_flag/*|SwissEph.SE_ECL_ONE_TRY*/, ref serr)) == SwissEph.ERR)
                    {
                        do_printf(serr);
                        return SwissEph.ERR;
                    }
                    else if (eclflag == 0)
                    {  /* event not found, try next conjunction */
                        t_ut = tret[0] + direction * 10;  /* try again with start date increased by 10 */
                        ii--;
                    }
                    else
                    {
                        t_ut = tret[0];
                        if ((time_flag & (BIT_TIME_LMT | BIT_TIME_LAT)) != 0)
                        {
                            for (i = 0; i < 10; i++)
                            {
                                if (tret[i] != 0)
                                {
                                    retc = ut_to_lmt_lat(tret[i], geopos, out tret[i], ref serr);
                                    if (retc == SwissEph.ERR)
                                    {
                                        do_printf(serr);
                                        return SwissEph.ERR;
                                    }
                                }
                            }
                        }
                        has_found = false;
                        sout = String.Empty;
                        if ((search_flag & SwissEph.SE_ECL_TOTAL) != 0 && (eclflag & SwissEph.SE_ECL_TOTAL) != 0)
                        {
                            sout += ("total");
                            has_found = true;
                            ecl_type = ECL_SOL_TOTAL;
                        }
                        if ((search_flag & SwissEph.SE_ECL_ANNULAR) != 0 && (eclflag & SwissEph.SE_ECL_ANNULAR) != 0)
                        {
                            sout += ("annular");
                            has_found = true;
                            ecl_type = ECL_SOL_ANNULAR;
                        }
                        if ((search_flag & SwissEph.SE_ECL_PARTIAL) != 0 && (eclflag & SwissEph.SE_ECL_PARTIAL) != 0)
                        {
                            sout += ("partial");
                            has_found = true;
                            ecl_type = ECL_SOL_PARTIAL;
                        }
                        if (ipl != SwissEph.SE_SUN)
                        {
                            if ((eclflag & SwissEph.SE_ECL_OCC_BEG_DAYLIGHT) != 0 && (eclflag & SwissEph.SE_ECL_OCC_END_DAYLIGHT) != 0)
                                sout += ("(daytime)"); /* occultation occurs during the day */
                            else if ((eclflag & SwissEph.SE_ECL_OCC_BEG_DAYLIGHT) != 0)
                                sout += ("(sunset) "); /* occultation occurs during the day */
                            else if ((eclflag & SwissEph.SE_ECL_OCC_END_DAYLIGHT) != 0)
                                sout += ("(sunrise)"); /* occultation occurs during the day */
                        }
                        if (have_gap_parameter) sout += "\t";
                        while (sout.Length < 17)
                            sout += (" ");
                        if (!has_found)
                        {
                            ii--;
                        }
                        else
                        {
                            sweph.swe_calc_ut(t_ut, SwissEph.SE_ECL_NUT, 0, x, ref serr);
                            sweph.swe_revjul(tret[0], gregflag, ref jyear, ref jmon, ref jday, ref jut);
                            dt = (tret[3] - tret[2]) * 24 * 60;
                            sout += C.sprintf("%2d.%02d.%04d\t%s\t%f\t%.6f\n", jday, jmon, jyear, hms(jut, BIT_LZEROES), attr[0], tret[0]);
                            sout += C.sprintf("\t%d min %4.2f sec\t", (int)dt, (dt % 1.0) * 60);
                            if ((eclflag & SwissEph.SE_ECL_1ST_VISIBLE) != 0)
                                sout += C.sprintf("%s ", hms_from_tjd(tret[1]));
                            else
                                sout += ("   -         ");
                            if (have_gap_parameter) sout += "\t";
                            if ((eclflag & SwissEph.SE_ECL_2ND_VISIBLE) != 0)
                                sout += C.sprintf("%s ", hms_from_tjd(tret[2]));
                            else
                                sout += ("   -         ");
                            if (have_gap_parameter) sout += "\t";
                            if ((eclflag & SwissEph.SE_ECL_3RD_VISIBLE) != 0)
                                sout += C.sprintf("%s ", hms_from_tjd(tret[3]));
                            else
                                sout += ("   -         ");
                            if (have_gap_parameter) sout += "\t";
                            if ((eclflag & SwissEph.SE_ECL_4TH_VISIBLE) != 0)
                                sout += C.sprintf("%s ", hms_from_tjd(tret[4]));
                            else
                                sout += ("   -         ");
                            if (have_gap_parameter) sout += "\t";
                            //#if 0
                            //      sprintf(sout + strlen(sout), "\t%d min %4.2f sec   %s %s %s %s", 
                            //                (int) dt, fmod(dt, 1) * 60, 
                            //                strcpy(s1, hms(fmod(tret[1] + 0.5, 1) * 24, BIT_LZEROES)), 
                            //                strcpy(s3, hms(fmod(tret[2] + 0.5, 1) * 24, BIT_LZEROES)), 
                            //                strcpy(s4, hms(fmod(tret[3] + 0.5, 1) * 24, BIT_LZEROES)),
                            //                strcpy(s2, hms(fmod(tret[4] + 0.5, 1) * 24, BIT_LZEROES)));
                            //#endif
                            sout += C.sprintf("dt=%.1f", sweph.swe_deltat_ex(tret[0], whicheph, ref serr) * 86400.0);
                            sout += ("\n");
                            if (have_gap_parameter) insert_gap_string_for_tabs(ref sout, gap);
                            do_printf(sout);
                        }
                    }
                }   /* endif search_local */
                if (0 == (special_mode & SP_MODE_LOCAL))
                {
                    /* * global search for occultations, test one lunar cycle only (SE_ECL_ONE_TRY) */
                    if ((eclflag = sweph.swe_lun_occult_when_glob(t_ut, ipl, star, whicheph, search_flag, tret, direction_flag/*|SE_ECL_ONE_TRY*/, ref serr)) == SwissEph.ERR)
                    {
                        do_printf(serr);
                        return SwissEph.ERR;
                    }
                    if (eclflag == 0)
                    { /* no occltation was found at next conjunction, try next conjunction */
                        t_ut = tret[0] + direction;
                        ii--;
                        continue;
                    }
                    if ((eclflag & SwissEph.SE_ECL_TOTAL) != 0)
                    {
                        sout = ("total   ");
                        ecl_type = ECL_SOL_TOTAL;
                    }
                    if ((eclflag & SwissEph.SE_ECL_ANNULAR) != 0)
                    {
                        sout = ("annular ");
                        ecl_type = ECL_SOL_ANNULAR;
                    }
                    if ((eclflag & SwissEph.SE_ECL_ANNULAR_TOTAL) != 0)
                    {
                        sout = ("ann-tot ");
                        ecl_type = ECL_SOL_ANNULAR;	/* by Alois: what is this ? */
                    }
                    if ((eclflag & SwissEph.SE_ECL_PARTIAL) != 0)
                    {
                        sout = ("partial ");
                        ecl_type = ECL_SOL_PARTIAL;
                    }
                    if ((eclflag & SwissEph.SE_ECL_NONCENTRAL) != 0 && 0 == (eclflag & SwissEph.SE_ECL_PARTIAL))
                        sout += ("non-central ");
                    t_ut = tret[0];
                    sweph.swe_lun_occult_where(t_ut, ipl, star, whicheph, geopos_max, attr, ref serr);
                    /* for (i = 0; i < 8; i++) {
                      printf("attr[%d]=%.17f\n", i, attr[i]);
                    } */
                    if ((time_flag & (BIT_TIME_LMT | BIT_TIME_LAT)) != 0)
                    {
                        for (i = 0; i < 10; i++)
                        {
                            if (tret[i] != 0)
                            {
                                retc = ut_to_lmt_lat(tret[i], geopos, out tret[i], ref serr);
                                if (retc == SwissEph.ERR)
                                {
                                    do_printf(serr);
                                    return SwissEph.ERR;
                                }
                            }
                        }
                    }
                    sweph.swe_revjul(tret[0], gregflag, ref jyear, ref jmon, ref jday, ref jut);
                    sout += C.sprintf("%2d.%02d.%04d\t%s\t%f km\t%f\t%.6f\n", jday, jmon, jyear, hms(jut, BIT_LZEROES), attr[3], attr[0], tret[0]);
                    sout += C.sprintf("\t%s ", hms_from_tjd(tret[2]));
                    if (have_gap_parameter) sout += "\t";
                    if (tret[4] != 0)
                        sout += C.sprintf("%s ", hms_from_tjd(tret[4]));
                    else
                        sout += ("   -         ");
                    if (have_gap_parameter) sout += "\t";
                    if (tret[5] != 0)
                        sout += C.sprintf("%s ", hms_from_tjd(tret[5]));
                    else
                        sout += ("   -         ");
                    if (have_gap_parameter) sout += "\t";
                    sout += C.sprintf("%s", hms_from_tjd(tret[3]));
                    if (have_gap_parameter) sout += "\t";
                    sout += C.sprintf("dt=%.1f", sweph.swe_deltat_ex(tret[0], whicheph, ref serr) * 86400.0);
                    sout += "\n";
                    s1 = (dms(geopos_max[0], BIT_ROUND_MIN));
                    s2 = (dms(geopos_max[1], BIT_ROUND_MIN));
                    sout += C.sprintf("\t%s\t%s", s1, s2);
                    if (0 == (eclflag & SwissEph.SE_ECL_PARTIAL) && 0 == (eclflag & SwissEph.SE_ECL_NONCENTRAL))
                    {
                        if ((eclflag = sweph.swe_lun_occult_when_loc(t_ut - 10, ipl, star, whicheph, geopos_max, tret, attr, false, ref serr)) == SwissEph.ERR)
                        {
                            do_printf(serr);
                            return SwissEph.ERR;
                        }
                        if (Math.Abs(tret[0] - t_ut) > 2)
                            do_printf("when_loc returns wrong date\n");
                        dt = (tret[3] - tret[2]) * 24 * 60;
                        sout += C.sprintf("\t%d min %4.2f sec", (int)dt, C.fmod(dt, 1) * 60);
                    }
                    sout += ("\n");
                    if (have_gap_parameter) insert_gap_string_for_tabs(ref sout, gap);
                    if ((special_mode & SP_MODE_HOCAL) != 0)
                    {
                        int ihou, imin, isec, isgn;
                        double dfrc;
                        sweph.swe_split_deg(jut, SwissEph.SE_SPLIT_DEG_ROUND_MIN, out ihou, out imin, out isec, out dfrc, out isgn);
                        sout = C.sprintf("\"%04d %02d %02d %02d.%02d %d\",\n", jyear, jmon, jday, ihou, imin, ecl_type);
                    }
                    do_printf(sout);
                }
                t_ut += direction;
            }
            return SwissEph.OK;
        }

        static void do_print_heliacal(double[] dret, Int32 event_type, string obj_name)
        {
            string[] sevtname = new string[]{"",
            "heliacal rising ",
            "heliacal setting",
            "evening first   ",
            "morning last    ",
            "evening rising  ",
            "morning setting ",};
            string stz = "UT";
            string stim0 = String.Empty, stim1 = String.Empty, stim2 = String.Empty;
            if ((time_flag & BIT_TIME_LMT) != 0)
                stz = "LMT";
            if ((time_flag & BIT_TIME_LAT) != 0)
                stz = "LAT";
            sout = String.Empty;
            sweph.swe_revjul(dret[0], gregflag, ref jyear, ref jmon, ref jday, ref jut);
            if (event_type <= 4)
            {
                if (hel_using_AV)
                {
                    stim0 = (hms_from_tjd(dret[0]));
                    remove_whitespace(ref stim0);
                    /* The following line displays only the beginning of visibility. */
                    sout += C.sprintf("%s %s: %d/%02d/%02d %s %s (%.5f)\n", obj_name, sevtname[event_type], jyear, jmon, jday, stim0, stz, dret[0]);
                }
                else
                {
                    /* display the moment of beginning and optimum visibility */
                    stim0 = (hms_from_tjd(dret[0]));
                    stim1 = (hms_from_tjd(dret[1]));
                    stim2 = (hms_from_tjd(dret[2]));
                    remove_whitespace(ref stim0);
                    remove_whitespace(ref stim1);
                    remove_whitespace(ref stim2);
                    sout += C.sprintf("%s %s: %d/%02d/%02d %s %s (%.5f), opt %s, end %s, dur %.1f min\n", obj_name, sevtname[event_type], jyear, jmon, jday, stim0, stz, dret[0], stim1, stim2, (dret[2] - dret[0]) * 1440);
                }
            }
            else
            {
                stim0 = (hms_from_tjd(dret[0]));
                remove_whitespace(ref stim0);
                sout += C.sprintf("%s %s: %d/%02d/%02d %s %s (%f)\n", obj_name, sevtname[event_type], jyear, jmon, jday, stim0, stz, dret[0]);
            }
            do_printf(sout);
        }

        static Int32 call_heliacal_event(double t_ut, Int32 ipl, string star, Int32 whicheph, double[] geopos, double[] datm, double[] dobs, ref string serr)
        {
            int ii, retc = SwissEph.OK, event_type = 0, retflag;
            double[] dret = new double[40]; double tsave1, tsave2 = 0;
            string obj_name;
            helflag |= whicheph;
            /* if invalid heliacal event type was required, set 0 for any type */
            if (search_flag < 0 || search_flag > 6)
                search_flag = 0;
            /* optical instruments used: */
            if (dobs[3] > 0)
                helflag |= SwissEph.SE_HELFLAG_OPTICAL_PARAMS;
            if (hel_using_AV)
                helflag |= SwissEph.SE_HELFLAG_AV;
            if (ipl == SwissEph.SE_FIXSTAR)
                obj_name = star;
            else
                obj_name = sweph.swe_get_planet_name(ipl);
            if (with_header)
            {
                printf("\ngeo. long %f, lat %f, alt %f", geopos[0], geopos[1], geopos[2]);
                do_printf("\n");
            }
            for (ii = 0; ii < nstep; ii++, t_ut = dret[0] + 1)
            {
                sout = String.Empty;
                if (search_flag > 0)
                    event_type = search_flag;
                else if (ipl == SwissEph.SE_MOON)
                    event_type = SwissEph.SE_EVENING_FIRST;
                else
                    event_type = SwissEph.SE_HELIACAL_RISING;
                retflag = sweph.swe_heliacal_ut(t_ut, geopos, datm, dobs, obj_name, event_type, helflag, dret, ref serr);
                if (retflag == SwissEph.ERR)
                {
                    do_printf(serr);
                    return SwissEph.ERR;
                }
                if ((time_flag & (BIT_TIME_LMT | BIT_TIME_LAT)) != 0)
                {
                    retc = ut_to_lmt_lat(dret[0], geopos, out (dret[0]), ref serr);
                    if (retc != SwissEph.ERR) retc = ut_to_lmt_lat(dret[1], geopos, out (dret[1]), ref serr);
                    if (retc != SwissEph.ERR) retc = ut_to_lmt_lat(dret[2], geopos, out (dret[2]), ref serr);
                    if (retc == SwissEph.ERR)
                    {
                        do_printf(serr);
                        return SwissEph.ERR;
                    }
                }
                do_print_heliacal(dret, event_type, obj_name);
                /* list all events within synodic cycle */
                if (search_flag == 0)
                {
                    if (ipl == SwissEph.SE_VENUS || ipl == SwissEph.SE_MERCURY)
                    {
                        /* we have heliacal rising (morning first), now find morning last */
                        event_type = SwissEph.SE_MORNING_LAST;
                        retflag = sweph.swe_heliacal_ut(dret[0], geopos, datm, dobs, obj_name, event_type, helflag, dret, ref serr);
                        if (retflag == SwissEph.ERR)
                        {
                            do_printf(serr);
                            return SwissEph.ERR;
                        }
                        if ((time_flag & (BIT_TIME_LMT | BIT_TIME_LAT)) != 0)
                        {
                            retc = ut_to_lmt_lat(dret[0], geopos, out (dret[0]), ref serr);
                            if (retc != SwissEph.ERR) retc = ut_to_lmt_lat(dret[1], geopos, out (dret[1]), ref serr);
                            if (retc != SwissEph.ERR) retc = ut_to_lmt_lat(dret[2], geopos, out (dret[2]), ref serr);
                            if (retc == SwissEph.ERR)
                            {
                                do_printf(serr);
                                return SwissEph.ERR;
                            }
                        }
                        do_print_heliacal(dret, event_type, obj_name);
                        tsave1 = dret[0];
                        /* mercury can have several evening appearances without any morning
                         * appearances in betweeen. We have to find out when the next 
                         * morning appearance is and then find all evening appearances 
                         * that take place before that */
                        if (ipl == SwissEph.SE_MERCURY)
                        {
                            event_type = SwissEph.SE_HELIACAL_RISING;
                            retflag = sweph.swe_heliacal_ut(dret[0], geopos, datm, dobs, obj_name, event_type, helflag, dret, ref serr);
                            if (retflag == SwissEph.ERR)
                            {
                                do_printf(serr);
                                return SwissEph.ERR;
                            }
                            tsave2 = dret[0];
                        }
                        //repeat_mercury:
                        /* evening first */
                        event_type = SwissEph.SE_EVENING_FIRST;
                        retflag = sweph.swe_heliacal_ut(tsave1, geopos, datm, dobs, obj_name, event_type, helflag, dret, ref serr);
                        if (retflag == SwissEph.ERR)
                        {
                            do_printf(serr);
                            return SwissEph.ERR;
                        }
                        if (ipl == SwissEph.SE_MERCURY && dret[0] > tsave2)
                            continue;
                        if ((time_flag & (BIT_TIME_LMT | BIT_TIME_LAT)) != 0)
                        {
                            retc = ut_to_lmt_lat(dret[0], geopos, out (dret[0]), ref serr);
                            if (retc != SwissEph.ERR) retc = ut_to_lmt_lat(dret[1], geopos, out (dret[1]), ref serr);
                            if (retc != SwissEph.ERR) retc = ut_to_lmt_lat(dret[2], geopos, out (dret[2]), ref serr);
                            if (retc == SwissEph.ERR)
                            {
                                do_printf(serr);
                                return SwissEph.ERR;
                            }
                        }
                        do_print_heliacal(dret, event_type, obj_name);
                    }
                    if (ipl == SwissEph.SE_MOON)
                    {
                        /* morning last */
                        event_type = SwissEph.SE_MORNING_LAST;
                        retflag = sweph.swe_heliacal_ut(dret[0], geopos, datm, dobs, obj_name, event_type, helflag, dret, ref serr);
                        if (retflag == SwissEph.ERR)
                        {
                            do_printf(serr);
                            return SwissEph.ERR;
                        }
                        if ((time_flag & (BIT_TIME_LMT | BIT_TIME_LAT)) != 0)
                        {
                            retc = ut_to_lmt_lat(dret[0], geopos, out (dret[0]), ref serr);
                            if (retc != SwissEph.ERR) retc = ut_to_lmt_lat(dret[1], geopos, out (dret[1]), ref serr);
                            if (retc != SwissEph.ERR) retc = ut_to_lmt_lat(dret[2], geopos, out (dret[2]), ref serr);
                            if (retc == SwissEph.ERR)
                            {
                                do_printf(serr);
                                return SwissEph.ERR;
                            }
                        }
                        do_print_heliacal(dret, event_type, obj_name);
                    }
                    else
                    {
                        /* heliacal setting (evening last) */
                        event_type = SwissEph.SE_HELIACAL_SETTING;
                        retflag = sweph.swe_heliacal_ut(dret[0], geopos, datm, dobs, obj_name, event_type, helflag, dret, ref serr);
                        if (retflag == SwissEph.ERR)
                        {
                            do_printf(serr);
                            return SwissEph.ERR;
                        }
                        if ((time_flag & (BIT_TIME_LMT | BIT_TIME_LAT)) != 0)
                        {
                            retc = ut_to_lmt_lat(dret[0], geopos, out (dret[0]), ref serr);
                            if (retc != SwissEph.ERR) retc = ut_to_lmt_lat(dret[1], geopos, out (dret[1]), ref serr);
                            if (retc != SwissEph.ERR) retc = ut_to_lmt_lat(dret[2], geopos, out (dret[2]), ref serr);
                            if (retc == SwissEph.ERR)
                            {
                                do_printf(serr);
                                return SwissEph.ERR;
                            }
                        }
                        do_print_heliacal(dret, event_type, obj_name);
                        //if (false && ipl == SwissEph.SE_MERCURY) {
                        //    tsave1 = dret[0];
                        //    goto repeat_mercury;
                        //}
                    }
                }
            }
            return SwissEph.OK;
        }

        static int do_special_event(double tjd, Int32 ipl, string star, Int32 special_event, Int32 special_mode, double[] geopos, double[] datm, double[] dobs, ref string serr)
        {
            int retc = 0;
            /* risings, settings, meridian transits */
            if (special_event == SP_RISE_SET ||
                special_event == SP_MERIDIAN_TRANSIT)
                retc = call_rise_set(tjd, ipl, star, whicheph, geopos, ref serr);
            /* lunar eclipses */
            if (special_event == SP_LUNAR_ECLIPSE)
                retc = call_lunar_eclipse(tjd, whicheph, special_mode, geopos, ref serr);
            /* solar eclipses */
            if (special_event == SP_SOLAR_ECLIPSE)
                retc = call_solar_eclipse(tjd, whicheph, special_mode, geopos, ref serr);
            /* occultations by the moon */
            if (special_event == SP_OCCULTATION)
                retc = call_lunar_occultation(tjd, ipl, star, whicheph, special_mode, geopos, ref serr);
            /* heliacal event */
            if (special_event == SP_HELIACAL)
                retc = call_heliacal_event(tjd, ipl, star, whicheph, geopos, datm, dobs, ref serr);
            return retc;
        }

        static string hms_from_tjd(double tjd)
        {
            double x;
            /* tjd may be negative, 0h corresponds to day number 9999999.5 */
            x = (tjd % 1.0);  /* may be negative ! */
            x = ((x + 1.5) % 1.0); /* is positive day fraction */
            return C.sprintf("%s ", hms(x * 24, BIT_LZEROES));
        }

        static string hms(double x, Int32 iflag)
        {
            //static char s[AS_MAXCH], s2[AS_MAXCH], *sp;
            var c = SwissEph.ODEGREE_STRING;
            x += 0.5 / 36000.0; /* round to 0.1 sec */
            var s = dms(x, iflag);
            // C uses strstr (byte-exact); default IndexOf(string) is culture-sensitive
            // and the result below feeds Substring arithmetic that assumes an exact
            // char-for-char match. Same fix as SwissEph.Format.cs:110.
            var spi = s.IndexOf(c, StringComparison.Ordinal);
            if (spi >= 0)
            {
                s = String.Concat(s.Substring(0, spi), ":", s.Substring(spi + 1));
                var s2 = s.Substring(spi + SwissEph.ODEGREE_STRING.Length);
                s = String.Concat(s.Substring(0, spi + 1), s2);
                // swetest.c:3936: *(sp + 3) = ':'; writes a single byte into the static
                // AS_MAXCH buffer regardless of length. Substring(spi + 4) throws where
                // C's single-byte write would not, on a BIT_ROUND_MIN result ending at
                // spi + 2 (s.Length == spi + 3); the guard below is the sibling of the
                // spi + 8 one just after it.
                if (s.Length > spi + 4)
                    s = String.Concat(s.Substring(0, spi + 3), ":", s.Substring(spi + 4));
                else
                    s = String.Concat(s.Substring(0, spi + 3), ":");
                // swetest.c:3937: *(sp + 8) = '\0'; truncates the buffer after the
                // seconds field. The length guard is needed because the C writes into
                // a static AS_MAXCH buffer regardless of length, while Substring would
                // throw here if s were ever shorter than spi + 8.
                if (s.Length > spi + 8) s = s.Substring(0, spi + 8);
            }
            return s;
        }

        static void do_printf(string info)
        {
            Console.Write(info);
        }

        /* make_ephemeris_path().
         * ephemeris path includes
         *   current working directory
         *   + program directory
         *   + default path from swephexp.h on current drive
         *   +                              on program drive
         *   +                              on drive C:
         */
        static int make_ephemeris_path(string argv0, ref string path)
        {
            int spi;
            char dirglue = SwissEph.DIR_GLUE;
            int pathlen = 0;
            /* current working directory */
            // swetest.c:3965: sprintf(path, ".%c", *PATH_SEPARATOR); -- *PATH_SEPARATOR
            // dereferences the cut-list string down to its first character. PATH_SEPARATOR
            // widened to char[] alongside swi_fopen's swi_cutstr restoration
            // (SwissEph.sweodef.h.cs); [0] is the equivalent dereference here.
            path = C.sprintf(".%c", SwissEph.PATH_SEPARATOR[0]);
            /* program directory */
            spi = argv0.LastIndexOf(dirglue);
            if (spi >= 0)
            {
                pathlen = spi;
                path += argv0.Substring(0, pathlen);
                // swetest.c:3972: sprintf(path + strlen(path), "%c", *PATH_SEPARATOR);
                path += C.sprintf("%c", SwissEph.PATH_SEPARATOR[0]);
            }
#if MSDOS
            {
                string[] cpos = new string[20];
                string s = string.Empty, s1 = String.Empty;
                string[] sp = new string[3];
                int i, j, np;
                s1 = SwissEph.SE_EPHE_PATH;
                //s1 = ".;sweph";
                // swetest.c:3982: np = cut_str_any(s1, PATH_SEPARATOR, cpos, 20); -- the full
                // cut-list, not a dereferenced single char, matching Split's own multi-char
                // separator array now that PATH_SEPARATOR is one.
                cpos = s1.Split(SwissEph.PATH_SEPARATOR, StringSplitOptions.RemoveEmptyEntries);
                np = cpos.Length;
                /* 
                 * default path from swephexp.h
                 * - current drive
                 * - program drive
                 * - drive C
                 */
                s = String.Empty;
                /* current working drive */
                sp[0] = Directory.GetCurrentDirectory();
                if (String.IsNullOrWhiteSpace(sp[0]))
                {
                    printf("error in getcwd()\n");
                    Environment.Exit(1);
                }
                if (sp[0][0] == 'C')
                    sp[0] = null;
                /* program drive */
                if (argv0[0] != 'C' && (sp[0] == null || sp[0][0] != argv0[0]))
                    sp[1] = argv0;
                else
                    sp[1] = null;
                /* drive C */
                sp[2] = "C";
                for (i = 0; i < np; i++)
                {
                    s = cpos[i];
                    if (s[0] == '.')	/* current directory */
                        continue;
                    if (s[1] == ':')  /* drive already there */
                        continue;
                    for (j = 0; j < 3; j++)
                    {
                        if (sp[j] != null)
                            // swetest.c:4013: sprintf(path + strlen(path), "%c:%s%c", *sp[j], s, *PATH_SEPARATOR);
                            path += C.sprintf("%c:%s%c", sp[j][0], s, SwissEph.PATH_SEPARATOR[0]);
                    }
                }
            }
#else
            if (strlen(path) + strlen(SE_EPHE_PATH) < AS_MAXCH - 1)
                strcat(path, SE_EPHE_PATH);
#endif
            return SwissEph.OK;
        }

        static void remove_whitespace(ref string s)
        {
            //char *sp, s1[AS_MAXCH];
            //if (s == NULL) return;
            //for (sp = s; *sp == ' '; sp++)
            //  ;
            //strcpy(s1, sp);
            //while (*(sp = s1 + strlen(s1) - 1) == ' ')
            //  *sp = '\0';
            //strcpy(s, s1);
            if (s == null) { s = null; return; }
            s = s.Trim(' ');
        }

        static void jd_to_time_string(double jut, out string stimeout)
        {
            double t2;
            //t2 = jut + 0.5 / 3600000.0; // rounding to millisec
            t2 = jut + 0.5 / 3600000.0; // rounding to millisec
            stimeout = C.sprintf("  % 2d:", (int)t2); // hour
            t2 = (t2 - (Int32)t2) * 60;
            stimeout += C.sprintf("%02d:", (int)t2);  // min
            t2 = (t2 - (Int32)t2) * 60;
            stimeout += C.sprintf("%02d", (int)t2); // sec
            t2 = (t2 - (Int32)t2) * 1000;
            if ((Int32)t2 > 0)
            {
                stimeout += C.sprintf(".%03d", (int)t2); // millisec, if > 0
            }
        }

        //#if MSDOS
        ///**************************************************************
        //cut the string s at any char in cutlist; put pointers to partial strings
        //into cpos[0..n-1], return number of partial strings;
        //if less than nmax fields are found, the first empty pointer is
        //set to NULL.
        //More than one character of cutlist in direct sequence count as one
        //separator only! cut_str_any("word,,,word2",","..) cuts only two parts,
        //cpos[0] = "word" and cpos[1] = "word2".
        //If more than nmax fields are found, nmax is returned and the
        //last field nmax-1 rmains un-cut.
        //**************************************************************/
        //static int cut_str_any(char *s, char *cutlist, char *cpos[], int nmax)
        //{
        //  int n = 1;
        //  cpos [0] = s;
        //  while (*s != '\0') {
        //    if ((strchr(cutlist, (int) *s) != NULL) && n < nmax) {
        //      *s = '\0';
        //      while (*(s + 1) != '\0' && strchr (cutlist, (int) *(s + 1)) != NULL) s++;
        //      cpos[n++] = s + 1;
        //    }
        //    if (*s == '\n' || *s == '\r') {	/* treat nl or cr like end of string */
        //      *s = '\0';
        //      break;
        //    }
        //    s++;
        //  }
        //  if (n < nmax) cpos[n] = NULL;
        //  return (n);
        //}	/* cutstr */
        //#endif

        static string our_strcpy(out string to, string from)
        {
            if (string.IsNullOrEmpty(from))
            {
                to = string.Empty;
                return to;
            }
            to = from;
            //if (strlen(from) < AS_MAXCH)
            //{
            //    strcpy(s, from);
            //    strcpy(to, s);
            //}
            //else
            //{
            //    sp = strdup(from);
            //    if (sp == NULL)
            //    {
            //        strcpy(to, from);
            //    }
            //    else
            //    {
            //        strcpy(to, sp);
            //        free(sp);
            //    }
            //}
            return to;
        }

        static void printf(String format, params object[] args)
        {
            Console.Write(C.sprintf(format, args));
        }

        #endregion

        static int Main(string[] args)
        {
            var s = Environment.GetCommandLineArgs();
            //s = new String[] { s[0], "-b16.08.1974", "-n1", "-s1", "-fPLBRS", "-pp", "-ejpl" };
            //s = new String[] { s[0], "-b16.08.1974", "-n1", "-s1", "-fPLBRS", "-pp", "-emos" };
            //s = new String[] { s[0], "-b16.08.1974", "-n1", "-s1", "-fPLBRS", "-pah", "-ejplde431.eph" };
            var result = main_test(s.Length, s);
#if DEBUG
            Console.WriteLine("Press a key to exit...");
            Console.ReadKey();
#endif
            return result;
        }
    }

}
