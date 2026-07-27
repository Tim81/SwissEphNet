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
  $Header: /home/dieter/sweph/RCS/swemini.c,v 1.74 2008/06/16 10:07:20 dieter Exp $

  swemini.c	A minimal program to test the Swiss Ephemeris.

  Input: a date (in gregorian calendar, sequence day.month.year)
  Output: Planet positions at midnight Universal time, ecliptic coordinates,
          geocentric apparent positions relative to true equinox of date, as 
          usual in western astrology.
        
   
  Authors: Dieter Koch and Alois Treindl, Astrodienst Zurich

**************************************************************/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SwissEphNet;
using System.IO;
using System.Text.RegularExpressions;

namespace SweMini
{
    class Program
    {
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
            int jday = 1, jmon = 1, jyear = 2000;
            double jut = 0.0;
            double[] x2 = new double[6];
            Int32 iflag, iflgret;
            //int p;
            using (var swe = new SwissEph())
            {
                swe.swe_set_ephe_path(null);
                iflag = SwissEph.SEFLG_SPEED;
                swe.OnLoadFile += swe_OnLoadFile;
                while (true)
                {
                    Console.Write("\nDate (d.m.y) ? ");
                    sdate = Console.ReadLine();
                    if (String.IsNullOrWhiteSpace(sdate)) break;
                    /*
                     * stop if a period . is entered
                     */
                    if (sdate == ".")
                        return SwissEph.OK;
                    var match = Regex.Match(sdate, @"(\d+)\.(\d+)\.(\d+)");
                    if (!match.Success) continue;
                    jday = int.Parse(match.Groups[1].Value);
                    jmon = int.Parse(match.Groups[2].Value);
                    jyear = int.Parse(match.Groups[3].Value);
                    /*
                     * we have day, month and year and convert to Julian day number
                     */
                    var jd = swe.swe_julday(jyear, jmon, jday, jut, SwissEph.SE_GREG_CAL);
                    /*
                     * compute Ephemeris time from Universal time by adding delta_t
                     */
                    var te = jd + swe.swe_deltat(jd);
                    Console.WriteLine("date: {0:00}.{1:00}.{2:D4} at 0:00 Universal time", jday, jmon, jyear);
                    Console.WriteLine("planet     \tlongitude\tlatitude\tdistance\tspeed long.");
                    /*
                     * a loop over all planets
                     */
                    for (var p = SwissEph.SE_SUN; p <= SwissEph.SE_CHIRON; p++)
                    {
                        if (p == SwissEph.SE_EARTH) continue;
                        /*
                         * do the coordinate calculation for this planet p
                         */
                        iflgret = swe.swe_calc(te, p, iflag, x2, ref serr);
                        /*
                         * if there is a problem, a negative value is returned and an 
                         * errpr message is in serr.
                         */
                        if (iflgret < 0)
                            printf("error: %s\n", serr);
                        else if (iflgret != iflag)
                            printf("warning: iflgret != iflag. %s\n", serr);
                        /*
                         * get the name of the planet p
                         */
                        snam = swe.swe_get_planet_name(p);
                        /*
                         * print the coordinates
                         */
                        printf("%10s\t%11.7f\t%10.7f\t%10.7f\t%10.7f\n",
                           snam, x2[0], x2[1], x2[2], x2[3]);
                    }
                }
            }

#if DEBUG
            Console.Write("\nPress a key to quit...");
            Console.ReadKey();
#endif
            return 0;
        }

        static Stream SearchFile(String fileName)
        {
            fileName = fileName.Trim('/', '\\');
            var folders = new string[] {
                System.IO.Path.Combine(Directory.GetCurrentDirectory(), "Datas"),
                @"C:\Temp\swisseph\swisseph\ephe"
            };
            foreach (var folder in folders)
            {
                var f = Path.Combine(folder, fileName);
                if (File.Exists(f))
                    return new System.IO.FileStream(f, System.IO.FileMode.Open, System.IO.FileAccess.Read);
            }
            return null;
        }

        static void swe_OnLoadFile(object sender, LoadFileEventArgs e)
        {
            if (e.FileName.StartsWith("[ephe]"))
            {
                e.File = SearchFile(e.FileName.Replace("[ephe]", string.Empty));
            }
            else
            {
                var f = e.FileName;
                if (System.IO.File.Exists(f))
                    e.File = new System.IO.FileStream(f, System.IO.FileMode.Open, System.IO.FileAccess.Read);
            }
        }

        public static void printf(string Format, params object[] Parameters)
        {
            Console.Write(C.sprintf(Format, Parameters));
        }

    }
}
