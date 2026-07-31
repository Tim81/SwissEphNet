using System;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// LoadFileEventArgs (and the OnLoadFile event it served) no longer exist -- replaced by
    /// SwissEph.IEphemerisFileProvider, a single-valued resolver. See docs/known-issues.md's
    /// OnLoadFile entry for why. These tests cover DelegateFileProvider, the adapter this test
    /// project uses in place of the per-test lambda handlers OnLoadFile used to take.
    /// </summary>
    public class DelegateFileProviderTest
    {
        [Fact]
        public void OpenInvokesTheDelegateWithThePath()
        {
            string seen = null;
            var target = new DelegateFileProvider(path => { seen = path; return null; });

            var result = target.Open("some/path");

            Assert.Equal("some/path", seen);
            Assert.Null(result);
        }

        [Fact]
        public void OpenReturnsWhateverTheDelegateReturns()
        {
            var stream = new System.IO.MemoryStream();
            var target = new DelegateFileProvider(path => stream);

            Assert.Same(stream, target.Open("irrelevant"));
        }
    }
}
