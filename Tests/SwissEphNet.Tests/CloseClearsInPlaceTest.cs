using System.Reflection;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// swe_close clears swed.oec, oec2000, nut, nut2000, nutv (sweph.c:1315-1319 at e308765d), topd and sidd
    /// (sweph.c:1336-1337) with memset in place, so a pointer taken earlier stays valid. The port replaced the
    /// objects (swed.Reset(false), new topo_data()). No C probe: the expectation is the memset itself (same
    /// storage, all fields zero). Compare CloseKeepTopoClearsInPlaceTest, which covers swi_close_keep_topo_etc.
    /// </summary>
    public class CloseClearsInPlaceTest
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static object Get(object o, string name)
        {
            var t = o.GetType();
            var f = t.GetField(name, All);
            if (f != null) return f.GetValue(o);
            return t.GetProperty(name, All).GetValue(o);
        }

        private static void AssertZero(object v, string what)
        {
            if (v is double d) Assert.True(d == 0.0, what);
            else if (v is int i) Assert.True(i == 0, what);
            else if (v is bool b) Assert.False(b, what);
            else if (v is double[] da) Assert.All(da, e => Assert.Equal(0.0, e));
            else if (v is double[,] dm) foreach (double e in dm) Assert.Equal(0.0, e);
        }

        private static void AssertAllZero(object o, string what)
        {
            foreach (var f in o.GetType().GetFields(All))
                AssertZero(f.GetValue(o), what + "." + f.Name);
            foreach (var p in o.GetType().GetProperties(All))
                if (p.GetIndexParameters().Length == 0)
                    AssertZero(p.GetValue(o), what + "." + p.Name);
        }

        [Fact]
        public void ReferencesTakenBeforeTheCloseAreTheSameObjectsAndZeroAfter()
        {
            using var swe = new SwissEph();
            var x = new double[6];
            string serr = null;
            swe.swe_set_topo(8.55, 47.37, 400);
            swe.swe_calc(2451545.0, SwissEph.SE_SUN, SwissEph.SEFLG_MOSEPH | SwissEph.SEFLG_TOPOCTR, x, ref serr); // fills oec, nut, topd
            swe.swe_set_sid_mode(SwissEph.SE_SIDM_LAHIRI, 0, 0);                                                   // fills sidd

            object sweph = typeof(SwissEph).GetProperty("Sweph", All).GetValue(swe);
            object swed = Get(sweph, "swed");
            string[] names = { "oec", "oec2000", "nut", "nut2000", "nutv", "topd" };
            var before = new object[names.Length];
            for (int i = 0; i < names.Length; i++) before[i] = Get(swed, names[i]);
            Assert.NotEqual(0.0, (double)Get(before[0], "eps"));
            Assert.NotEqual(0.0, (double)Get(before[2], "tnut"));
            Assert.NotEqual(0.0, (double)Get(before[5], "geolon"));
            Assert.NotEqual(0, (int)Get(Get(swed, "sidd"), "sid_mode"));

            swe.swe_close();

            for (int i = 0; i < names.Length; i++)
            {
                object after = Get(swed, names[i]);
                Assert.True(ReferenceEquals(before[i], after), names[i] + " was replaced");
                AssertAllZero(after, names[i]);
            }
            AssertAllZero(Get(swed, "sidd"), "sidd");
        }
    }
}
