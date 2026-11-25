using System;
using System.Linq;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// swe_calc results no longer depend on call history at an ephemeris file boundary (upstream
    /// 79d3553f and 85eb3a5d, both in v2.10.04). Before them, Jupiter..Pluto kept the last segment of the
    /// previous file (read_const freed it only for bodies with a reference ellipse) and the file in use
    /// depended on which file the previous call had opened; the C's own boundary test went from 350 of 400
    /// and 343 of 392 history-dependent points to none.
    ///
    /// The boundary of the sepl_12/sepl_18 and semo_12/semo_18 files is 1 Jan 1800, JD 2378496.5
    /// (gen_filename_range; the first day of a file belongs to its predecessor, sweph.c, rule of
    /// 24 April 2026). This test takes 21 dates from 2 days before to 3 days after it in steps of 0.25 d
    /// and computes the Moon and Jupiter with SEFLG_SWIEPH | SEFLG_SPEED
    ///   (a) in increasing date order in one instance,
    ///   (b) in decreasing order in one fresh instance,
    ///   (c) one date per fresh instance,
    ///   (d) after a first call on the other side of the boundary.
    /// All of them must equal what the C returns in a fresh process per date.
    ///
    /// Reference values: C probe linked against external/.c-reference/build-2.10.03/libswe-2.10.03.lib
    /// (built from e308765d = v2.10.04 despite its name; MSVC /O2 /fp:precise /MD), %.17g:
    ///   swe_set_ephe_path("&lt;external/swisseph/ephe&gt;");
    ///   for each date t:  swe_calc(t, ipl, SEFLG_SWIEPH | SEFLG_SPEED, x, serr);   // rc = 258, serr empty
    /// with ipl = SE_MOON (1) and SE_JUPITER (5). A fresh process per date, the same process with the
    /// dates ascending, and the same process with them descending, printed identical bytes in the C.
    /// Each row: tjd, lon, lat, dist, dlon, dlat, ddist.
    /// </summary>
    public class FileBoundaryHistoryTest
    {
        private const double Boundary = 2378496.5;
        private const int Flags = SwissEph.SEFLG_SWIEPH | SwissEph.SEFLG_SPEED;

        private static readonly double[][] Moon =
        {
            new double[] { 2378494.5, 322.54568374365601, -4.8162490587430256, 0.0025502659263350155, 13.393666827546157, 0.36745101142570968, 3.9042412204138468e-05 },
            new double[] { 2378494.75, 325.88004398400864, -4.7165972297930692, 0.0025600348573542727, 13.281213806160961, 0.42924133135342696, 3.9076927528337735e-05 },
            new double[] { 2378495, 329.18640262250619, -4.6018707454472496, 0.0025697880299347746, 13.169767379525487, 0.48803566433068701, 3.8916450860980086e-05 },
            new double[] { 2378495.25, 332.46508910528729, -4.4728283781936424, 0.0025794772836079021, 13.059944420495567, 0.54376111889808387, 3.8565940983812192e-05 },
            new double[] { 2378495.5, 335.71658219605405, -4.3302434588872973, 0.0025890558522570595, 12.952321100179018, 0.59637336731901835, 3.8031528521740047e-05 },
            new double[] { 2378495.75, 338.94149927239073, -4.1748972016925636, 0.0025984786290234856, 12.847430530848229, 0.64585308493001803, 3.7320392464921193e-05 },
            new double[] { 2378496, 342.14058546734481, -4.0075728916548865, 0.0026077024196299206, 12.745761250078239, 0.69220236259453372, 3.644064010682434e-05 },
            new double[] { 2378496.25, 345.31470249824071, -3.8290509638783359, 0.0026166861669257506, 12.647756538097527, 0.73544117973599143, 3.5401188498273843e-05 },
            new double[] { 2378496.5, 348.4648171793059, -3.6401050439798222, 0.0026253911350468833, 12.553814504216966, 0.77560403075230888, 3.4211645531524761e-05 },
            new double[] { 2378496.75, 351.59199044315972, -3.4414984407278233, 0.0026337810481746001, 12.464288858929045, 0.81273667388714277, 3.2882204459417693e-05 },
            new double[] { 2378497, 354.69736605101264, -3.2339821283777477, 0.0026418222570672302, 12.379489861130221, 0.84689323959894713, 3.1423530010207571e-05 },
            new double[] { 2378497.25, 357.78216010623225, -3.0182927206536521, 0.0026494838167339077, 12.299685999074072, 0.87813349823182341, 2.9846663497460782e-05 },
            new double[] { 2378497.5, 0.84765084051332484, -2.795151426652613, 0.0026567375875528724, 12.225105665296205, 0.9065204389417002, 2.8162929473137463e-05 },
            new double[] { 2378497.75, 3.8951687613084403, -2.5652635377038924, 0.0026635582793907023, 12.155939184178882, 0.93211813388579601, 2.6383848210230512e-05 },
            new double[] { 2378498, 6.926087451708252, -2.329318372993511, 0.0026699235145207331, 12.092340752260316, 0.95498987144080361, 2.4521056675761721e-05 },
            new double[] { 2378498.25, 9.9418147274796187, -2.087989709559873, 0.0026758138152594324, 12.034430669639676, 0.97519658103778473, 2.2586233606724121e-05 },
            new double[] { 2378498.5, 12.943784465615643, -1.8419365223034434, 0.0026812126300561684, 11.982297289731976, 0.9927955011945705, 2.0591032520337324e-05 },
            new double[] { 2378498.75, 15.933448851082462, -1.5918040543236696, 0.0026861063112320152, 11.935999050248, 1.0078391061437357, 1.8547019195775385e-05 },
            new double[] { 2378499, 18.912271160725442, -1.3382251224196251, 0.0026904840777017274, 11.895566406063262, 1.0203742651372643, 1.6465614608314525e-05 },
            new double[] { 2378499.25, 21.88171892834858, -1.0818215901671386, 0.0026943379964367957, 11.861003494013737, 1.0304416060115895, 1.4358042083011643e-05 },
            new double[] { 2378499.5, 24.84325766832276, -0.82320600043465963, 0.0026976628945792015, 11.832289914111811, 1.038075101134319, 1.2235279349802593e-05 },
        };

        private static readonly double[][] Jupiter =
        {
            new double[] { 2378494.5, 84.781539016926047, -0.30865694522084247, 4.1708642851277302, -0.12852900180464816, 0.0026680558343418477, 0.0041293745691341963 },
            new double[] { 2378494.75, 84.749445721316633, -0.30799367992860771, 4.1719080492958875, -0.12821620935543068, 0.002672520405218157, 0.0042063490398558719 },
            new double[] { 2378495, 84.71743142487918, -0.30732916983969955, 4.1729710374608686, -0.12789701000857967, 0.0026767535233263653, 0.0042831847262880373 },
            new double[] { 2378495.25, 84.685497728717962, -0.30666343146926356, 4.1740532145377065, -0.12757141121843774, 0.0026807622913397763, 0.0043598788665331484 },
            new double[] { 2378495.5, 84.653646230539323, -0.30599648167472421, 4.1751545448499918, -0.12723943073806523, 0.0026845714346822296, 0.004436429313130744 },
            new double[] { 2378495.75, 84.621878526281108, -0.30532833595372255, 4.176274992049116, -0.12690108373357908, 0.0026882012151843129, 0.004512833733699503 },
            new double[] { 2378496, 84.590196197735864, -0.30465901471279072, 4.1774145195678862, -0.12655641210583105, 0.0026916651246304784, 0.0045890911784500603 },
            new double[] { 2378496.25, 84.558600824682188, -0.30398853404833837, 4.1785730900205014, -0.12620544734773131, 0.0026950015073205899, 0.0046651969753923528 },
            new double[] { 2378496.5, 84.527093975795196, -0.3033169118388791, 4.1797506655796486, -0.12584822893829015, 0.0026982022802933213, 0.0047411512525430992 },
            new double[] { 2378496.75, 84.495677209160391, -0.30264416615314166, 4.1809472079304184, -0.12548479885940589, 0.002701289021379802, 0.0048169512704773036 },
            new double[] { 2378497, 84.464352071471026, -0.30197031522923889, 4.1821626782836097, -0.12511520736225257, 0.0027042931622765247, 0.0048925946517076975 },
            new double[] { 2378497.25, 84.433120097596913, -0.30129537744514245, 4.1833970373879197, -0.12473949883039029, 0.0027072138242736341, 0.0049680810532324758 },
            new double[] { 2378497.5, 84.401982838015812, -0.30061943021033272, 4.1846502498940321, -0.12435772631914199, 0.0027100682880183548, 0.005043408142424181 },
            new double[] { 2378497.75, 84.370941746048828, -0.29994237457006062, 4.1859222670110929, -0.12396994605820943, 0.0027128604564743363, 0.0051185742534784612 },
            new double[] { 2378498, 84.339998345152992, -0.29926428789526127, 4.1872130524513409, -0.123576208261819, 0.0027156272499432098, 0.0051935768374765517 },
            new double[] { 2378498.25, 84.309154117923782, -0.29858518896227154, 4.1885225652134901, -0.12317656782003771, 0.0027183624837439826, 0.0052684136935190707 },
            new double[] { 2378498.5, 84.278410532996816, -0.29790509661487657, 4.1898507638789502, -0.12277107976363999, 0.0027210625059163299, 0.0053430866114800414 },
            new double[] { 2378498.75, 84.247769045350125, -0.29722402974090401, 4.1911976066180978, -0.12235980004333487, 0.0027237549732771196, 0.005417589677922087 },
            new double[] { 2378499, 84.217231095637032, -0.29654200727185703, 4.1925630511971423, -0.12194278581950534, 0.002726466887997814, 0.0054919238134449209 },
            new double[] { 2378499.25, 84.186798110484631, -0.29585904816127795, 4.1939470549837257, -0.12152009619502732, 0.0027291657777883081, 0.0055660842279872226 },
            new double[] { 2378499.5, 84.156471501805655, -0.29517517138598331, 4.1953495749533882, -0.12109178628983669, 0.0027318742855702836, 0.0056400746586970467 },
        };

        private static double[][] Rows(int ipl) => ipl == SwissEph.SE_MOON ? Moon : Jupiter;

        private static double[] Calc(SwissEph swe, double tjd, int ipl)
        {
            var x = new double[6];
            string serr = null;
            int rc = swe.swe_calc(tjd, ipl, Flags, x, ref serr);
            Assert.True(rc == Flags, $"ipl {ipl} tjd {tjd:R}: rc {rc}, serr '{serr}'");
            return x;
        }

        private static void AssertRow(double[] row, double[] x, int ipl)
        {
            for (int i = 0; i < 6; i++)
                CReference.Equal(row[i + 1], x[i], 1e-8, $"ipl {ipl} tjd {row[0]:R} x[{i}]");
        }

        [Fact]
        public void TheTableSpansTheBoundary()
        {
            Assert.Equal(21, Moon.Length);
            Assert.Equal(21, Jupiter.Length);
            Assert.Equal(Boundary - 2, Moon[0][0]);
            Assert.Equal(Boundary + 3, Moon[20][0]);
            Assert.True(Moon.Zip(Jupiter, (m, j) => m[0] == j[0]).All(b => b));
        }

        [EpheTheory]
        [InlineData(SwissEph.SE_MOON)]
        [InlineData(SwissEph.SE_JUPITER)]
        public void IncreasingOrderInOneInstance_MatchesTheC(int ipl)
        {
            using var swe = RepoEphe.Open();
            foreach (var row in Rows(ipl))
                AssertRow(row, Calc(swe, row[0], ipl), ipl);
        }

        [EpheTheory]
        [InlineData(SwissEph.SE_MOON)]
        [InlineData(SwissEph.SE_JUPITER)]
        public void DecreasingOrderInOneInstance_MatchesTheC(int ipl)
        {
            using var swe = RepoEphe.Open();
            foreach (var row in Enumerable.Reverse(Rows(ipl)))
                AssertRow(row, Calc(swe, row[0], ipl), ipl);
        }

        [EpheTheory]
        [InlineData(SwissEph.SE_MOON)]
        [InlineData(SwissEph.SE_JUPITER)]
        public void EachDateInAFreshInstance_MatchesTheC(int ipl)
        {
            foreach (var row in Rows(ipl))
            {
                using var swe = RepoEphe.Open();
                AssertRow(row, Calc(swe, row[0], ipl), ipl);
            }
        }

        [EpheTheory]
        [InlineData(SwissEph.SE_MOON)]
        [InlineData(SwissEph.SE_JUPITER)]
        public void PreviousCallOnTheOtherSideOfTheBoundary_ChangesNothing(int ipl)
        {
            foreach (var row in Rows(ipl))
            {
                // Prime the instance with the file on the other side (Moon and Jupiter both, so that
                // each body's segment and each file have been used there), then ask for the date.
                double other = row[0] < Boundary ? Boundary + 3 : Boundary - 2;
                using var swe = RepoEphe.Open();
                Calc(swe, other, SwissEph.SE_MOON);
                Calc(swe, other, SwissEph.SE_JUPITER);
                AssertRow(row, Calc(swe, row[0], ipl), ipl);
            }
        }

        [EpheFact]
        public void AllFourOrdersAgreeBitForBit()
        {
            // Independent of the C values: the port must agree with itself on every platform.
            foreach (int ipl in new[] { SwissEph.SE_MOON, SwissEph.SE_JUPITER })
            {
                var dates = Rows(ipl).Select(r => r[0]).ToArray();
                double[][] Run(double[] order)
                {
                    using var swe = RepoEphe.Open();
                    return order.Select(t => Calc(swe, t, ipl)).ToArray();
                }
                var inc = Run(dates);
                var dec = Enumerable.Reverse(Run(Enumerable.Reverse(dates).ToArray())).ToArray();
                for (int i = 0; i < dates.Length; i++)
                {
                    double[] fresh;
                    using (var swe = RepoEphe.Open())
                        fresh = Calc(swe, dates[i], ipl);
                    Assert.Equal(fresh, inc[i]);
                    Assert.Equal(fresh, dec[i]);
                }
            }
        }
    }
}
