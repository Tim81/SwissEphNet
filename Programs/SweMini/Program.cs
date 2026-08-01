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
  swemini.c	A minimal program to test the Swiss Ephemeris.

  Input: a date (in gregorian calendar, sequence day.month.year)
  	if no date is entered, 1 Jan 2022 is used. Next time, the date
	advances by one day.
  Output: Planet positions at midnight Universal time, ecliptic coordinates,
          geocentric apparent positions relative to true equinox of date, as
          usual in western astrology.


  Authors: Dieter Koch and Alois Treindl.

  The code of sample program swemini.c is in the public domain.
  (But not the code of the library functions called by it.)

**************************************************************/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SwissEphNet;
using System.IO;

namespace SweMini
{
    class Program
    {
        static string[] smon = { null, "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };

        static int Main(string[] args)
        {
            return Main_Mini(args);
            //return Main_TestValues(args);
        }

        //static int Main_TestValues(string[] args) {
        //    int jyear = 2014,
        //        jmon = 4,
        //        jday = 21,
        //        jhour = 20,
        //        jmin = 41,
        //        jsec = 23;
        //    using (Sweph sweph = new Sweph()) {
        //        double jut = jhour + (jmin / 60.0) + (jsec / 3600.0);
        //        double tjd = sweph.swe_julday(jyear, jmon, jday, jut, SwissEph.SE_GREG_CAL);
        //        double deltat = sweph.swe_deltat(tjd);
        //        double te = tjd + deltat;
        //        printf("date: %02d.%02d.%d at %02d:%02d:%02d\nDeltat : %f\nJulian Day : %f\nEphemeris Time : %f\n", jday, jmon, jyear, jhour, jmin, jsec, deltat, tjd, te);

        //        var date = new DateUT(jyear, jmon, jday, jhour, jmin, jsec);
        //        var jd = sweph.JulianDay(date, DateCalendar.Gregorian);
        //        var et = sweph.EphemerisTime(jd);
        //        printf("date: %02d.%02d.%d at %02d:%02d:%02d\nDeltat : %f\nJulian Day : %f\nEphemeris Time : %f\n", 
        //            date.Day, date.Month, date.Year, date.Hours, date.Minutes, date.Seconds, et.DeltaT, jd.Value, et.Value);

        //    }

        //    Console.ReadKey();
        //    return 0;
        //}

        static int Main_Mini(string[] args)
        {
            string sdate = String.Empty, snam = String.Empty, serr = String.Empty;
            int jday = 1, jmon = 1, jyear = 2022;
            double jut = 0.0;
            double[] x2 = new double[6];
            Int32 iflag, iflgret;
            //int p;
            using (var swe = new SwissEph())
            {
                // swe.OnLoadFile used to be wired to swe_OnLoadFile/SearchFile below,
                // duplicating the path search the library now performs itself
                // (SwissEph.OpenBinary): pointing swe_set_ephe_path at the same "Datas"
                // folder SearchFile used to search is sufficient on its own.
                swe.swe_set_ephe_path(Path.Combine(Directory.GetCurrentDirectory(), "Datas"));
                iflag = SwissEph.SEFLG_SPEED;
                while (true)
                {
                    Console.Write("\nDate (d.m.y) ?");
                    sdate = Console.ReadLine();
                    // stop if a period . is entered
                    // swemini.c:40 tests *sdate == '.', a single-byte comparison; StartsWith
                    // without StringComparison is culture-sensitive, so make it ordinal.
                    if (sdate != null && sdate.StartsWith(".", StringComparison.Ordinal))
                        return SwissEph.OK;
                    // swemini.c:42 is sscanf(sdate, "%d%*c%d%*c%d", &jday,&jmon,&jyear):
                    // three decimal fields separated by any single byte each (%*c,
                    // assignment suppressed), with each field left at its prior value
                    // (the loop's previous iteration, or the 1/1/2022 default) when its
                    // own scan fails -- sscanf's normal partial-assignment behavior.
                    // Regex.Match(@"(\d+)\.(\d+)\.(\d+)") diverged from that on inputs
                    // like "5/3/2024", "5-3-2024" or "5 3 2024" (fell back to the
                    // defaults instead of parsing), "5" or "5.3" alone (no partial
                    // assignment), "-5.3.2024" (lost its sign) and "v1.2.3 rc" (matched
                    // mid-string where C parses nothing). C.sscanf already implements
                    // sscanf's field-width and assignment-suppression semantics and
                    // reproduces the C parser's results exactly.
                    C.sscanf(sdate ?? String.Empty, "%d%*c%d%*c%d", ref jday, ref jmon, ref jyear);
                    if (jmon < 1 || jmon > 12)
                    {
                        printf("illegal month %d\n", jmon);
                        continue;
                    }
                    var jd = swe.swe_julday(jyear, jmon, jday, jut, SwissEph.SE_GREG_CAL);
                    // compute Ephemeris time from Universal time by adding delta_t
                    var te = jd + swe.swe_deltat(jd);
                    printf("date: %02d %s %04d at 0:00 Universal time, jd=%.1lf\n", jday, smon[jmon], jyear, jd);
                    Console.WriteLine("planet     \tlongitude\tlatitude\tdistance\tspeed long.");
                    for (var p = SwissEph.SE_SUN; p <= SwissEph.SE_CHIRON; p++) // a loop over all planets
                    {
                        if (p == SwissEph.SE_EARTH) continue;
                        snam = swe.swe_get_planet_name(p); //  get the name of the planet p
                        // do the coordinate calculation for this planet p
                        iflgret = swe.swe_calc(te, p, iflag, x2, ref serr);
                        // if there is a problem, a negative value is returned and an error message is in serr.
                        if (iflgret < 0)
                        {
                            printf("%10s\terror: %s\n", snam, serr);
                            continue;
                        }
                        if (iflgret != iflag)
                            printf("warning: iflgret != iflag. %s\n", serr);
                        // print the coordinates
                        printf("%10s\t%11.7f\t%10.7f\t%10.7f\t%10.7f\n",
                           snam, x2[0], x2[1], x2[2], x2[3]);
                    }
                    jd++;   // if date entry is empty, take next day
                    swe.swe_revjul(jd, SwissEph.SE_GREG_CAL, ref jyear, ref jmon, ref jday, ref jut);
                }
            }

#if DEBUG
            Console.Write("\nPress a key to quit...");
            Console.ReadKey();
#endif
            // swemini.c:72 has this same trailing `return OK;` after its own
            // `while (TRUE)` loop, whose only exit is the `return OK;` at swemini.c:41 --
            // unreachable in the C too, not a mis-transliterated goto. Left to match.
            return 0;
        }

        public static void printf(string Format, params object[] Parameters)
        {
            Console.Write(C.sprintf(Format, Parameters));
        }

    }
}
