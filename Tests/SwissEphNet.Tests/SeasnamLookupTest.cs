using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// swe_get_planet_name's seasnam.txt lookup for a numbered asteroid with no ephemeris file
    /// (sweph.c:7001 onwards at upstream 6c989727, be4aa5dd). Pass 0 starts from a binary search
    /// over the file (seasnam_seek, sweph.c:6968) and stops at the first catalog number past the one
    /// wanted; pass 1, only if pass 0 found nothing, scans the whole file for an unsorted
    /// seasnam.txt. The fixtures are generated here, about 49 kB each so that the binary search
    /// iterates (it stops once the window is 4096 bytes or less). Every expected value below was
    /// measured against the C library at 6c989727 with byte-identical fixtures; the "misplaced"
    /// case is the one where the two algorithms differ, and the C at 94621aba (linear scan only)
    /// returns "Wrong" for it.
    /// </summary>
    public class SeasnamLookupTest
    {
        private static List<string> Body()
        {
            var lines = new List<string>();
            for (int n = 1; n <= 3000; n++)
            {
                if (n == 1500)
                    continue;
                lines.Add(n % 7 == 0 ? $"({n}) Name{n}" : $"{n:D6}  Name{n}");
                if (n % 500 == 0)
                {
                    lines.Add($"# comment after {n}");
                    lines.Add("");
                }
            }
            return lines;
        }

        private static byte[] Fixture(IEnumerable<string> lines) =>
            Encoding.ASCII.GetBytes(string.Concat(lines.Select(l => l + "\n")));

        private static byte[] Sorted() => Fixture(new[] { "# seasnam.txt test fixture" }.Concat(Body()));

        private static byte[] Unsorted()
        {
            var body = Body();
            body.Reverse();
            return Fixture(new[] { "# seasnam.txt test fixture" }.Concat(body));
        }

        private static byte[] Misplaced() => Fixture(new[] { "002900  Wrong" }.Concat(Body()));

        private static string Name(byte[] seasnam, int number)
        {
            using var swe = new SwissEph();
            swe.FileProvider = new DelegateFileProvider(path =>
                ResourceFileHelpers.GetPortableFileName(path).Equals(SwissEph.SE_ASTNAMFILE, StringComparison.OrdinalIgnoreCase)
                    ? new MemoryStream(seasnam, writable: false)
                    : null);
            return swe.swe_get_planet_name(SwissEph.SE_AST_OFFSET + number);
        }

        [Fact]
        public void FixtureIsLargeEnoughForTheBinarySearchToIterate()
        {
            Assert.Equal(49427, Sorted().Length);
        }

        [Theory]
        [InlineData(10, "Name10")]
        [InlineData(14, "Name14")] // bracketed catalog number, "(14) Name14"
        [InlineData(1499, "Name1499")]
        [InlineData(1501, "Name1501")]
        [InlineData(2900, "Name2900")]
        [InlineData(2999, "Name2999")]
        [InlineData(3000, "Name3000")]
        [InlineData(1500, "1500: not found (asteroid)")] // gap: pass 0 breaks at 1501, pass 1 finds nothing
        [InlineData(3001, "3001: not found (asteroid)")] // past the last line
        public void SortedFile(int number, string expected)
        {
            Assert.Equal(expected, Name(Sorted(), number));
        }

        [Theory]
        [InlineData(10, "Name10")]
        [InlineData(14, "Name14")]
        [InlineData(1499, "Name1499")]
        [InlineData(1501, "Name1501")]
        [InlineData(2900, "Name2900")]
        [InlineData(3000, "Name3000")]
        [InlineData(1500, "1500: not found (asteroid)")]
        [InlineData(3001, "3001: not found (asteroid)")]
        public void UnsortedFileFallsBackToTheFullScan(int number, string expected)
        {
            Assert.Equal(expected, Name(Unsorted(), number));
        }

        [Fact]
        public void BinarySearchFindsTheSortedEntryNotAnEarlierMisplacedOne()
        {
            // A linear scan from the top (the only algorithm before be4aa5dd) returns "Wrong".
            Assert.Equal("Name2900", Name(Misplaced(), 2900));
            Assert.Equal("Name10", Name(Misplaced(), 10));
        }
    }
}
