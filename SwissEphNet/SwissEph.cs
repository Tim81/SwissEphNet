using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SwissEphNet
{
    /// <summary>
    /// Swiss Ephemeris C conversion
    /// </summary>
    public partial class SwissEph : IDisposable
    {

        #region Ctors & Dest

        /// <summary>
        /// Static constructor
        /// </summary>
        static SwissEph()
        {
            // See CFile's constructor for why UTF-8 is the correct, deliberate
            // default here rather than an attempted Windows-1252 fallback:
            // every large Swiss Ephemeris data file is pure ASCII, and the
            // files that do carry non-ASCII text (2.10.03's seorbel.txt,
            // astlistn.md) are valid UTF-8, not Windows-1252.
            DefaultEncoding = Encoding.UTF8;
        }

        /// <summary>
        /// Create a new context
        /// </summary>
        public SwissEph() {
            FileProvider = DefaultFileProvider;
            Sweph = new CPort.Sweph(this);
            SweJPL = new CPort.SweJPL(this);
            SwephLib = new CPort.SwephLib(this);
            SwemMoon = new CPort.SwemMoon(this);
            SwemPlan = new CPort.SwemPlan(this);
            SweDate = new CPort.SweDate(this);
            SweHouse = new CPort.SweHouse(this);
            SweCL = new CPort.SweCL(this);
            SweHel = new CPort.SweHel(this);
        }

        private bool _disposed;

        /// <summary>
        /// Throws <see cref="ObjectDisposedException"/> if this instance has already been
        /// disposed. Every public member that reaches into the ported library does so
        /// through one of the nine internal component properties below, plus
        /// <see cref="LoadFile"/> and <see cref="Trace"/> -- calling this from those spots
        /// covers the whole surface without having to guard all 455 public members
        /// individually. A handful of pure-formatting helpers (e.g. swe_dotnet_version(),
        /// which only reads this assembly's own reflection metadata) never touch a
        /// component property and so are unaffected by disposal, which is harmless: they
        /// carry no library state to be stale.
        /// </summary>
        private void ThrowIfDisposed() {
            if (_disposed)
                throw new ObjectDisposedException(GetType().FullName);
        }

        /// <summary>
        /// Internal release resources
        /// </summary>
        protected virtual void Dispose(bool disposing) {
            if (_disposed) return;
            if (disposing) {
                // swe_close() routes through the Sweph property below; call it before
                // _disposed is set so that property's own ThrowIfDisposed() does not fire.
                swe_close();
                OnTrace = null;
            }
            _disposed = true;
        }

        /// <summary>
        /// Release resources
        /// </summary>
        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Trace

        /// <summary>
        /// Trace information
        /// </summary>
        public void Trace(String format, params object[] args) {
            ThrowIfDisposed();
            var h = OnTrace;
            if (h != null) {
                String message = args != null ? C.sprintf(format, args) : format;
                h(this, new TraceEventArgs(message));
            }
        }

        #endregion

        #region File management

        /// <summary>
        /// Default encoding
        /// </summary>
        public static Encoding DefaultEncoding = null;

        /// <summary>
        /// Default <see cref="FileProvider"/> for every newly-constructed <see cref="SwissEph"/>
        /// instance -- read once, in the constructor, into that instance's own
        /// <see cref="FileProvider"/>. Left <c>null</c> (the default), new instances start with
        /// <see cref="FileProvider"/> null too, i.e. the real filesystem (see
        /// <see cref="FileProvider"/> for what null means there).
        /// </summary>
        /// <remarks>
        /// This exists for harnesses that construct many instances and must guarantee none of
        /// them can ever read a real file -- the characterization baseline
        /// (<c>Tools/BaselineGen</c>, <c>Tools/BaselineMatrix</c>) is Moshier-only by
        /// construction (see <c>docs/known-issues.md</c>), and relying on every one of its
        /// several hundred <c>new SwissEph()</c> call sites to remember to set
        /// <see cref="FileProvider"/> individually would be exactly the kind of silent trap this
        /// property is meant to close off: set it once, before constructing anything, and every
        /// instance created afterwards inherits it.
        /// </remarks>
        public static IEphemerisFileProvider DefaultFileProvider = null;

        /// <summary>
        /// Resolves ephemeris file content that does not come from the real filesystem, e.g. an
        /// embedded test resource. Prefer <see cref="swe_set_ephe_path"/> pointed at a real
        /// directory instead of setting this, the way the C's own test suite does -- most
        /// callers never need it.
        /// </summary>
        public interface IEphemerisFileProvider {
            /// <summary>
            /// Open <paramref name="path"/> for reading, or return <c>null</c> if it does not
            /// exist. The returned stream must be readable and seekable: <see cref="CFile"/>
            /// seeks during parsing (e.g. rewinding sefstars.txt between a swe_fixstar and a
            /// swe_fixstar2 call on the same file). The library takes ownership of the stream it
            /// is handed and disposes it once it is done with it.
            /// </summary>
            Stream Open(string path);
        }

        /// <summary>
        /// Single-valued resolver for ephemeris file content, replacing the multicast
        /// <c>OnLoadFile</c> event this property supersedes (see <c>docs/known-issues.md</c>,
        /// "OnLoadFile: multicast leaks a stream..."). Initialized from
        /// <see cref="DefaultFileProvider"/> when this instance is constructed; set it directly
        /// afterwards to override that default for one instance.
        /// </summary>
        /// <remarks>
        /// <c>null</c> (the default, unless <see cref="DefaultFileProvider"/> says otherwise)
        /// means "use the real filesystem": <see cref="OpenBinary"/> opens the path with
        /// <see cref="File.OpenRead(string)"/> directly. That is a deliberate change from the
        /// event this replaces, which returned "not found" for every caller that never attached
        /// a handler -- silently downgrading every calculation to Moshier even when a real
        /// ephemeris directory was configured and present on disk. Now that every target
        /// framework this library ships (netstandard2.0, net8.0, net10.0) has full filesystem
        /// access, "no provider configured" reading real files is the more useful default; a
        /// caller that genuinely wants no file ever found (as the characterization baseline
        /// does) must say so, either through this property directly or through
        /// <see cref="DefaultFileProvider"/>.
        /// </remarks>
        public IEphemerisFileProvider FileProvider { get; set; }

        /// <summary>
        /// Opens a file for reading, honouring <see cref="FileProvider"/> if one is set, or the
        /// real filesystem otherwise. This is the sole "fopen()" substitution point in
        /// <c>swi_fopen</c>'s transliteration (<c>CPort/Sweph.cs</c>, matching
        /// <c>sweph.c:2370-2405</c>) -- everything else in that function's path-search loop
        /// (splitting <c>ephepath</c>, the "." current-directory case, joining with
        /// <see cref="DIR_GLUE"/>, the <see cref="AS_MAXCH"/> bounds check) is transliterated
        /// faithfully, line by line, ahead of this call.
        /// </summary>
        /// <param name="path">The full candidate path <c>swi_fopen</c> built for this attempt.</param>
        /// <returns>An open <see cref="CFile"/>, or <c>null</c> if <paramref name="path"/> does
        /// not resolve to a file.</returns>
        internal protected CFile OpenBinary(String path) {
            ThrowIfDisposed();
            Stream stream;
            if (FileProvider != null) {
                stream = FileProvider.Open(path);
            } else {
                try {
                    stream = File.Exists(path) ? File.OpenRead(path) : null;
                } catch (IOException) {
                    stream = null;
                } catch (UnauthorizedAccessException) {
                    stream = null;
                }
            }
            if (stream == null) return null;
            return new CFile(stream, DefaultEncoding);
        }

        #endregion

        #region Internals

        private CPort.Sweph _sweph;
        private CPort.SweJPL _sweJPL;
        private CPort.SwephLib _swephLib;
        private CPort.SwemMoon _swemMoon;
        private CPort.SwemPlan _swemPlan;
        private CPort.SweDate _sweDate;
        private CPort.SweHouse _sweHouse;
        private CPort.SweCL _sweCL;
        private CPort.SweHel _sweHel;

        /// <summary>
        /// Sweph
        /// </summary>
        internal CPort.Sweph Sweph { get { ThrowIfDisposed(); return _sweph; } private set { _sweph = value; } }

        /// <summary>
        /// SweJPL
        /// </summary>
        internal CPort.SweJPL SweJPL { get { ThrowIfDisposed(); return _sweJPL; } private set { _sweJPL = value; } }

        /// <summary>
        /// SwephLib
        /// </summary>
        internal CPort.SwephLib SwephLib { get { ThrowIfDisposed(); return _swephLib; } private set { _swephLib = value; } }

        /// <summary>
        /// SwemMoon
        /// </summary>
        internal CPort.SwemMoon SwemMoon { get { ThrowIfDisposed(); return _swemMoon; } private set { _swemMoon = value; } }

        /// <summary>
        /// SwemPlan
        /// </summary>
        internal CPort.SwemPlan SwemPlan { get { ThrowIfDisposed(); return _swemPlan; } private set { _swemPlan = value; } }

        /// <summary>
        /// SweDate
        /// </summary>
        internal CPort.SweDate SweDate { get { ThrowIfDisposed(); return _sweDate; } private set { _sweDate = value; } }

        /// <summary>
        /// SweHouse
        /// </summary>
        internal CPort.SweHouse SweHouse { get { ThrowIfDisposed(); return _sweHouse; } private set { _sweHouse = value; } }

        /// <summary>
        /// SweCL
        /// </summary>
        internal CPort.SweCL SweCL { get { ThrowIfDisposed(); return _sweCL; } private set { _sweCL = value; } }

        /// <summary>
        /// SweHel
        /// </summary>
        internal CPort.SweHel SweHel { get { ThrowIfDisposed(); return _sweHel; } private set { _sweHel = value; } }

        #endregion

        #region Events

        /// <summary>
        /// Event raised when a new trace message is invoked
        /// </summary>
        public event EventHandler<TraceEventArgs> OnTrace;

        #endregion

    }

}
