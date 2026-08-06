using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// Value-diffs the nutation coefficient tables in SweNut200a.h.cs against
    /// external/swisseph/swenut2000a.h, the C header they were transliterated from. Before
    /// this test, the tables were only ever length-checked (NLS=678, NLS_2000B=77,
    /// NPL=687 match); no commit or script compared the actual coefficient values.
    /// See docs/compliance-2.10.03.md, "What this record does not cover".
    ///
    /// Fixture: Tests/SwissEphNet.Tests/files/swenut2000a.h, a byte-identical copy of
    /// external/swisseph/swenut2000a.h at the pinned submodule commit, embedded as a
    /// resource the same way seas_18.se1 already is for PlaDiamCoverageTest. A fixture
    /// copy, not a submodule read, for the same reason PlaDiamCoverageTest's own doc
    /// comment gives: .github/workflows/ci.yml's build-and-test job (which builds and
    /// runs this project) and .github/workflows/release.yml both checkout without
    /// fetching submodules -- only conformance.yml/oracle.yml/baseline.yml do that, for
    /// the projects that actually need external/swisseph directly. A test here that read
    /// the submodule would fail for every contributor who has not run the submodule-init
    /// recipe in CONTRIBUTING.md, and did fail in CI for exactly that reason.
    /// </summary>
    public class NutationTableFidelityTest
    {
        private static readonly string HeaderText = ReadHeaderFixture();

        private static string ReadHeaderFixture()
        {
            using var stream = ResourceFileHelpers.OpenResourceFile("swenut2000a.h");
            Assert.NotNull(stream);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        /// <summary>Extracts the comma-separated integer literals of `static const &lt;type&gt; name[] = { ... };` from the C header.</summary>
        private static long[] ParseCArray(string headerText, string arrayName)
        {
            var match = Regex.Match(
                headerText,
                @"static\s+const\s+\w+\s+" + Regex.Escape(arrayName) + @"\s*\[\s*\]\s*=\s*\{(.*?)\};",
                RegexOptions.Singleline);
            Assert.True(match.Success, $"could not find array '{arrayName}' in the embedded swenut2000a.h fixture");

            var body = match.Groups[1].Value;
            var tokens = body.Split(new[] { ',', '\r', '\n', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var values = new long[tokens.Length];
            for (var i = 0; i < tokens.Length; i++)
            {
                values[i] = long.Parse(tokens[i], CultureInfo.InvariantCulture);
            }
            return values;
        }

        private static long[] GetPortArray(string fieldName)
        {
            var type = typeof(SwissEph).Assembly.GetType("SwissEphNet.CPort.SweNut200a");
            Assert.NotNull(type);
            var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(field);
            var raw = field.GetValue(null);

            return raw switch
            {
                short[] shorts => Array.ConvertAll(shorts, x => (long)x),
                int[] ints => Array.ConvertAll(ints, x => (long)x),
                _ => throw new InvalidOperationException($"unexpected array element type for '{fieldName}': {raw?.GetType()}"),
            };
        }

        public static IEnumerable<object[]> Arrays()
        {
            // (C array name, CPort field name) -- names match exactly on both sides.
            yield return new object[] { "nls" };
            yield return new object[] { "cls" };
            yield return new object[] { "npl" };
            yield return new object[] { "icpl" };
        }

        [Theory]
        [MemberData(nameof(Arrays))]
        public void TestNutationArray_MatchesCHeaderValueForValue(string arrayName)
        {
            var expected = ParseCArray(HeaderText, arrayName);
            var actual = GetPortArray(arrayName);

            Assert.Equal(expected.Length, actual.Length);
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.True(expected[i] == actual[i],
                    $"{arrayName}[{i}]: C={expected[i]}, port={actual[i]}");
            }
        }
    }
}
