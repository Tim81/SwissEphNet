using System.Reflection;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// swi_close_keep_topo_etc clears swed.oec, oec2000, nut, nut2000 and nutv with memset in place
    /// (sweph.c:1280-1284 at e308765d), so a pointer taken earlier stays valid. SweCL.cs:687 (eclipse_where,
    /// swecl.c:662) takes oe = swed.oec, then calls swe_calc, which can reach swi_close_keep_topo_etc through
    /// swe_set_ephe_path(NULL), then reads oe->eps. The port replaced the objects, leaving the reference on a
    /// discarded copy. No C probe: the expectation is the memset itself (all fields zero, same storage).
    /// </summary>
    public class CloseKeepTopoClearsInPlaceTest
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
            else if (v is double[] da) Assert.All(da, e => Assert.Equal(0.0, e));
            else if (v is double[,] dm) foreach (double e in dm) Assert.Equal(0.0, e);
        }

        [Fact]
        public void ReferencesTakenBeforeTheCloseAreTheSameObjectsAndZeroAfter()
        {
            using var swe = new SwissEph();
            var x = new double[6];
            string serr = null;
            swe.swe_calc(2451545.0, SwissEph.SE_SUN, SwissEph.SEFLG_MOSEPH, x, ref serr); // fills oec and nut

            object sweph = typeof(SwissEph).GetProperty("Sweph", All).GetValue(swe);
            object swed = Get(sweph, "swed");
            string[] names = { "oec", "oec2000", "nut", "nut2000", "nutv" };
            var before = new object[names.Length];
            for (int i = 0; i < names.Length; i++) before[i] = Get(swed, names[i]);
            Assert.NotEqual(0.0, (double)Get(before[0], "eps"));
            Assert.NotEqual(0.0, (double)Get(before[2], "tnut"));

            sweph.GetType().GetMethod("swi_close_keep_topo_etc", All).Invoke(sweph, null);

            for (int i = 0; i < names.Length; i++)
            {
                object after = Get(swed, names[i]);
                Assert.True(ReferenceEquals(before[i], after), names[i] + " was replaced");
                foreach (var f in after.GetType().GetFields(All))
                    AssertZero(f.GetValue(after), names[i] + "." + f.Name);
                foreach (var p in after.GetType().GetProperties(All))
                    AssertZero(p.GetValue(after), names[i] + "." + p.Name);
            }
        }
    }
}
