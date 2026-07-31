using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// SwissEph.Dispose() used to call swe_close() and nothing else: no disposed flag, no
    /// GC.SuppressFinalize, no finalizer, events left subscribed. Measured: after Dispose(),
    /// swe_calc_ut succeeded, returned the correct value, and re-opened the ephemeris files --
    /// see docs/known-issues.md ("Dispose does not dispose"). These tests pin the fix: use
    /// after dispose throws, double dispose does not, and disposal genuinely releases the
    /// underlying file handle rather than leaving it open for the next call to reopen.
    /// </summary>
    public class DisposeTest
    {
        /// <summary>
        /// A MemoryStream that records whether it was actually disposed, so a test can tell
        /// "the library closed this file" apart from "the library never touched it again".
        /// </summary>
        private sealed class TrackingStream : MemoryStream
        {
            public bool Disposed { get; private set; }

            public TrackingStream(byte[] buffer) : base(buffer) { }

            protected override void Dispose(bool disposing) {
                if (disposing)
                    Disposed = true;
                base.Dispose(disposing);
            }
        }

        [Fact]
        public void UseAfterDispose_Throws() {
            var swe = new SwissEph();
            swe.Dispose();

            double[] xx = new double[6];
            string serr = null;
            Assert.Throws<ObjectDisposedException>(() =>
                swe.swe_calc_ut(2451545.0, SwissEph.SE_SUN, SwissEph.SEFLG_MOSEPH, xx, ref serr));
        }

        [Fact]
        public void UseAfterDispose_ThrowsFromEachInternalComponentProperty() {
            // swe_calc_ut only exercises the Sweph property. Every public member reaches the
            // library through one of nine internal component properties (SwissEph.cs); spot
            // one call routed through each of the other six that a public method reaches
            // directly, so a future change that adds a tenth component, or a member that
            // bypasses all of them, has a test to fail. SwemMoon and SwemPlan are the
            // remaining two -- no public method reaches either one first, since every path
            // to them runs through Sweph's or SweCL's own ThrowIfDisposed() check first --
            // and are covered separately below, through reflection.
            using (var swe = new SwissEph()) {
                swe.Dispose();

                string serr = null;
                double[] xx = new double[6];
                Assert.Throws<ObjectDisposedException>(() =>
                    swe.swe_calc_ut(2451545.0, SwissEph.SE_SUN, SwissEph.SEFLG_MOSEPH, xx, ref serr));    // Sweph
                Assert.Throws<ObjectDisposedException>(() =>
                    swe.swe_set_jpl_file("de431.eph"));                                                   // SweJPL
                Assert.Throws<ObjectDisposedException>(() =>
                    swe.swe_degnorm(400));                                                                // SwephLib
                Assert.Throws<ObjectDisposedException>(() =>
                    swe.swe_julday(2000, 1, 1, 0, SwissEph.SE_GREG_CAL));                                  // SweDate
                Assert.Throws<ObjectDisposedException>(() =>
                    swe.swe_houses(2451545.0, 0, 0, 'P', new double[13], new double[10]));                 // SweHouse
                Assert.Throws<ObjectDisposedException>(() =>
                    swe.swe_sol_eclipse_when_glob(2451545.0, SwissEph.SEFLG_MOSEPH, 0, new double[10], false, ref serr)); // SweCL
                Assert.Throws<ObjectDisposedException>(() =>
                    swe.swe_heliacal_ut(2451545.0, new double[3], new double[4], new double[6], "Venus", 0, 0, new double[50], ref serr)); // SweHel
            }
        }

        [Fact]
        public void UseAfterDispose_ThrowsFromSwemMoonAndSwemPlanProperties() {
            // The last two of the nine internal component properties (see the comment above).
            // No public method reaches SwemMoon or SwemPlan before Sweph or SweCL already has,
            // so there is no public call whose first throw can be pinned to either property
            // specifically -- reflection is the only way to exercise their own
            // ThrowIfDisposed() directly. No InternalsVisibleTo is declared for this assembly
            // (same reasoning as SwissEphTest.FileNaming.cs's GenFileName helper), so
            // PropertyInfo.GetValue is used instead of a direct property access, and the
            // ObjectDisposedException it throws arrives wrapped in a TargetInvocationException.
            var swe = new SwissEph();
            swe.Dispose();

            var swemMoon = typeof(SwissEph).GetProperty("SwemMoon", BindingFlags.NonPublic | BindingFlags.Instance);
            var swemPlan = typeof(SwissEph).GetProperty("SwemPlan", BindingFlags.NonPublic | BindingFlags.Instance);

            var moonEx = Assert.Throws<TargetInvocationException>(() => swemMoon.GetValue(swe));
            Assert.IsType<ObjectDisposedException>(moonEx.InnerException);

            var planEx = Assert.Throws<TargetInvocationException>(() => swemPlan.GetValue(swe));
            Assert.IsType<ObjectDisposedException>(planEx.InnerException);
        }

        [Fact]
        public void DoubleDispose_DoesNotThrow() {
            var swe = new SwissEph();
            swe.Dispose();
            var ex = Record.Exception(() => swe.Dispose());
            Assert.Null(ex);
        }

        [Fact]
        public void Dispose_ClearsSubscribedEvents() {
            var swe = new SwissEph();
            var traceRaised = false;
            swe.OnTrace += (s, e) => traceRaised = true;

            swe.Dispose();

            // Reflection is the only way to observe this: the backing field is only ever read
            // from Trace(), which now throws ObjectDisposedException before reaching it, same
            // as every other post-dispose call. OnTrace has explicit add/remove accessors (see
            // SwissEph.cs), so its compiler-generated backing field is "_onTrace", not "OnTrace".
            var traceField = typeof(SwissEph).GetField("_onTrace",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.Null(traceField.GetValue(swe));
            Assert.False(traceRaised);
        }

        [Fact]
        public void UseAfterDispose_ThrowsFromFileProviderProperty() {
            // FileProvider (SwissEph.cs) carries instance state despite sitting outside the
            // nine internal component properties, and used to be a plain auto-property with no
            // ThrowIfDisposed() guard on either accessor -- the exact escape DisposeTest exists
            // to catch. Cover both directions: reading it and writing it.
            var swe = new SwissEph();
            swe.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _ = swe.FileProvider);
            Assert.Throws<ObjectDisposedException>(() => swe.FileProvider = new DelegateFileProvider(_ => null));
        }

        [Fact]
        public void UseAfterDispose_ThrowsFromOnTraceAccessors() {
            // OnTrace used to be an auto-implemented event: add_OnTrace/remove_OnTrace had no
            // guard, so subscribing after Dispose() succeeded and the disposed instance took a
            // strong reference to a handler Trace() could never invoke again.
            var swe = new SwissEph();
            swe.Dispose();

            EventHandler<TraceEventArgs> handler = (s, e) => { };
            Assert.Throws<ObjectDisposedException>(() => swe.OnTrace += handler);
            Assert.Throws<ObjectDisposedException>(() => swe.OnTrace -= handler);
        }

        [Fact]
        public void Dispose_ReleasesFileHandle_RatherThanReopeningOnNextCall() {
            byte[] bytes;
            using (var s = ResourceFileHelpers.OpenResourceFile("se00005s.se1"))
            using (var ms = new MemoryStream()) {
                s.CopyTo(ms);
                bytes = ms.ToArray();
            }
            var tracking = new TrackingStream(bytes);

            var swe = new SwissEph();
            swe.FileProvider = new DelegateFileProvider(path => {
                var asm = this.GetType().GetAssembly();
                var name = ResourceFileHelpers.GetPortableFileName(path);
                if (name == "se00005s.se1")
                    return tracking;
                return asm.GetManifestResourceStream(
                    path.Replace("[ephe]", "SwissEphNet.Tests.files", StringComparison.Ordinal).Replace("/", ".", StringComparison.Ordinal).Replace("\\", ".", StringComparison.Ordinal));
            });

            double tjd = swe.swe_julday(1974, 8, 16, 0.5, SwissEph.SE_GREG_CAL);
            double[] xx = new double[6];
            string serr = null;
            var retc = swe.swe_calc_ut(tjd, SwissEph.SE_AST_OFFSET + 5, SwissEph.SEFLG_SWIEPH, xx, ref serr);
            Assert.True(retc >= 0, serr);
            Assert.False(tracking.Disposed);

            swe.Dispose();

            Assert.True(tracking.Disposed);
            Assert.Throws<ObjectDisposedException>(() =>
                swe.swe_calc_ut(tjd, SwissEph.SE_AST_OFFSET + 5, SwissEph.SEFLG_SWIEPH, xx, ref serr));
        }
    }
}
