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

/*******************************************************
$Header: /home/dieter/sweph/RCS/swehouse.h,v 1.74 2008/06/16 10:07:20 dieter Exp $
module swehouse.h
house and (simple) aspect calculation 

*******************************************************/

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
namespace SwissEphNet.CPort
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    partial class SweHouse
    {
        public class houses
        {
            public houses() {
                cusp = new double[37];
                cusp_speed = new double[37];
                serr = string.Empty;
            }
            public double[] cusp;
            public double[] cusp_speed;
            public double ac;
            public double ac_speed;	// speed of ac
            public double mc;
            public double mc_speed;	// speed of mc
            public double armc_speed;	// speed of armc
            public double vertex;
            public double vertex_speed;	// speed of vertex
            public double equasc;
            public double equasc_speed;	// speed
            public double coasc1;
            public double coasc1_speed;	// speed
            public double coasc2;
            public double coasc2_speed;	// speed
            public double polasc;
            public double polasc_speed;	// speed
            public double sundec;   // declination of Sun for Sunshine houses
            public bool do_speed;
            public bool do_hspeed;
            public bool do_interpol;
            public string serr;
        }

        //#define HOUSES 	struct houses;
        public const double VERY_SMALL = 1E-10;

        public static double degtocs(double x) { return (SwissEph.swe_d2l((x) * SwissEph.DEG)); }
        public static double cstodeg(double x) { return (double)((x) * SwissEph.CS2DEG); }

        public static double sind(double x) { return Math.Sin((x) * SwissEph.DEGTORAD); }
        public static double cosd(double x) { return Math.Cos((x) * SwissEph.DEGTORAD); }
        public static double tand(double x) { return Math.Tan((x) * SwissEph.DEGTORAD); }
        public static double asind(double x) { return (Math.Asin(x) * SwissEph.RADTODEG); }
        public static double acosd(double x) { return (Math.Acos(x) * SwissEph.RADTODEG); }
        public static double atand(double x) { return (Math.Atan(x) * SwissEph.RADTODEG); }
        public static double atan2d(double y, double x) { return (Math.Atan2(y, x) * SwissEph.RADTODEG); }

    }
}
