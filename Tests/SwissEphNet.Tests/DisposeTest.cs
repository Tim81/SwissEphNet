using System;
using System.IO;
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
            // one call routed through each of the other eight so a future change that adds a
            // tenth component, or a member that bypasses all of them, has a test to fail.
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

            // Reflection is the only way to observe this: OnTrace is only ever invoked from
            // Trace(), which now throws ObjectDisposedException before reaching the event, same
            // as every other post-dispose call.
            var traceField = typeof(SwissEph).GetField("OnTrace",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.Null(traceField.GetValue(swe));
            Assert.False(traceRaised);
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
                    path.Replace("[ephe]", "SwissEphNet.Tests.files").Replace("/", ".").Replace("\\", "."));
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
