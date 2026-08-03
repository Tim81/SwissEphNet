using SwissEphNet.ApiApproval;
using Xunit;

namespace SwissEphNet.NetStandard20Smoke.Tests
{
    /// <summary>
    /// The netstandard2.0 half of the public-API approval check. Tests/SwissEphNet.Tests runs
    /// the same comparison for net8.0 and net10.0; this project is the only one that resolves
    /// the netstandard2.0 asset (see the remarks in NetStandard20Smoke.Tests.csproj), and
    /// netstandard2.0 is the framework where an accidentally public extension method actually
    /// bites: the BCL there has no instance <c>string.Contains(char)</c>, so a consumer's own
    /// call binds to the library's method instead of to the BCL, silently changing its null
    /// behaviour. On net8.0/net10.0 the instance method wins and the injection is invisible.
    /// </summary>
    /// <remarks>
    /// This project exists because netstandard2.0 once shipped with nothing exercising it and
    /// put a StackOverflowException into a released package. Leaving its public surface
    /// unpinned while pinning the other two would repeat that pattern exactly: the least
    /// tested shipped asset being the one nothing checks.
    /// </remarks>
    public class PublicApiApprovalTest
    {
        [Fact]
        public void PublicApiSurface_MatchesApprovedList()
        {
            PublicApiSurface.Verify("Tests/NetStandard20Smoke.Tests/PublicApi");
        }

        /// <summary>
        /// Confirms this project really is comparing against the netstandard2.0 list. Both of
        /// this project's own target frameworks are .NET Framework, so a moniker taken from
        /// the test assembly instead of from the library assembly would silently look for a
        /// net462/net48 list that does not exist -- or, worse, find the wrong one.
        /// </summary>
        [Fact]
        public void ApprovedList_IsTheNetStandard20One()
        {
            Assert.Equal("netstandard2.0", PublicApiSurface.TargetFrameworkMoniker(typeof(SwissEph).Assembly));
        }

        /// <summary>
        /// The specific regression this whole check was built around, asserted on the
        /// framework that manifests it: StringExtensions must not be reachable from outside
        /// the assembly on the netstandard2.0 asset.
        /// </summary>
        [Fact]
        public void StringExtensions_IsNotExternallyVisible()
        {
            var type = typeof(SwissEph).Assembly.GetType("SwissEphNet.StringExtensions", throwOnError: true);

            Assert.False(type.IsPublic,
                "SwissEphNet.StringExtensions is public again. On this framework the BCL has no " +
                "instance string.Contains(char), so every consumer that writes 'using SwissEphNet;' " +
                "would bind their own calls to this library's extension method.");
        }
    }
}
