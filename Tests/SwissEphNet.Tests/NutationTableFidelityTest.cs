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
    /// Value-diffs the nutation coefficient tables in SweNut200a.h.cs against the
    /// pinned external/swisseph/swenut2000a.h they were transliterated from. Before
    /// this test, the tables were only ever length-checked (NLS=678, NLS_2000B=77,
    /// NPL=687 match); no commit or script compared the actual coefficient values.
    /// See docs/compliance-2.10.03.md, "What this record does not cover".
    /// </summary>
    public class NutationTableFidelityTest
    {
        private static readonly string SubmoduleRoot = ResolveSubmoduleRoot();
        private static readonly string HeaderPath = Path.Combine(SubmoduleRoot, "swenut2000a.h");

        private static string ResolveSubmoduleRoot()
        {
            var overridePath = Environment.GetEnvironmentVariable("SWISSEPH_CONFORMANCE_SUBMODULE");
            if (!string.IsNullOrEmpty(overridePath))
            {
                return overridePath;
            }

            var marker = Path.Combine("external", "swisseph", "swenut2000a.h");
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, marker);
                if (File.Exists(candidate))
                {
                    return Path.GetDirectoryName(candidate);
                }
                dir = dir.Parent;
            }

            throw new InvalidOperationException(
                "Could not locate external/swisseph/swenut2000a.h in any parent directory of " +
                AppContext.BaseDirectory + ". Run 'git submodule update --init external/swisseph', " +
                "or set SWISSEPH_CONFORMANCE_SUBMODULE to the submodule's root directory.");
        }

        /// <summary>Extracts the comma-separated integer literals of `static const &lt;type&gt; name[] = { ... };` from the C header.</summary>
        private static long[] ParseCArray(string headerText, string arrayName)
        {
            var match = Regex.Match(
                headerText,
                @"static\s+const\s+\w+\s+" + Regex.Escape(arrayName) + @"\s*\[\s*\]\s*=\s*\{(.*?)\};",
                RegexOptions.Singleline);
            Assert.True(match.Success, $"could not find array '{arrayName}' in {HeaderPath}");

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
            var headerText = File.ReadAllText(HeaderPath);
            var expected = ParseCArray(headerText, arrayName);
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
