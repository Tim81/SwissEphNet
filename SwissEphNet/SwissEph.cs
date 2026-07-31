using System;
using System.Collections.Generic;
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
                OnLoadFile = null;
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
        /// Load a file
        /// </summary>
        /// <param name="filename">File name</param>
        /// <returns>File loaded or null if file not found</returns>
        internal protected CFile LoadFile(String filename) {
            ThrowIfDisposed();
            var h = OnLoadFile;
            if (h != null) {
                var e = new LoadFileEventArgs(filename) { Encoding = DefaultEncoding };
                h(this, e);
                if (e.File == null) return null;
                return new CFile(e.File, e.Encoding ?? DefaultEncoding);
            }
            return null;
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

        /// <summary>
        /// Event raised when loading a file is required
        /// </summary>
        public event EventHandler<LoadFileEventArgs> OnLoadFile;

        #endregion

    }

}
