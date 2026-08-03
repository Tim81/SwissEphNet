using SwissEphNet.ApiApproval;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// Pins the library's externally visible API surface against a committed approved list,
    /// one per target framework, so that nothing becomes public by accident.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This project targets net8.0 and net10.0 and resolves the matching library asset for
    /// each, so this test covers those two. The netstandard2.0 asset is covered by the same
    /// check in Tests/NetStandard20Smoke.Tests, which is the only project that resolves it;
    /// both share PublicApiSurface.cs, so the two lists are produced by identical code and
    /// can be compared with each other directly.
    /// </para>
    /// <para>
    /// See PublicApiSurface.cs for what this catches and why: three extension classes shipped
    /// public in namespace SwissEphNet, injecting their methods into the scope of every
    /// consumer that wrote <c>using SwissEphNet;</c>, with per-framework null semantics and a
    /// CS0121 compile break for anyone who already had the same helper. No behavioural test
    /// could see that, because the defect was in who could see the members, not in what they
    /// did.
    /// </para>
    /// </remarks>
    public class PublicApiApprovalTest
    {
        [Fact]
        public void PublicApiSurface_MatchesApprovedList()
        {
            PublicApiSurface.Verify("Tests/SwissEphNet.Tests/PublicApi");
        }

        /// <summary>
        /// Guards the guard. If the surface renderer ever returned nothing -- a reflection
        /// failure swallowed, a filter inverted -- the comparison above would still pass the
        /// day someone regenerated an empty approved list, and would then never fail again.
        /// SwissEph itself must always be in the listing.
        /// </summary>
        [Fact]
        public void PublicApiSurface_IsNotVacuous()
        {
            var surface = PublicApiSurface.Render(typeof(SwissEph).Assembly);

            Assert.Contains(surface, line => line.StartsWith("TYPE ", System.StringComparison.Ordinal)
                                          && line.IndexOf(" SwissEphNet.SwissEph ",
                                                          System.StringComparison.Ordinal) >= 0);
            Assert.Contains(surface, line => line.IndexOf("SwissEphNet.SwissEph.swe_calc(",
                                                          System.StringComparison.Ordinal) >= 0);
        }

        /// <summary>
        /// The two extension classes whose public visibility is the reason this file exists.
        /// The approved list already pins them by omission, but omission is a weak assertion:
        /// it holds only as long as nobody regenerates the list carelessly. Naming them makes
        /// a regression fail on a test whose name says what went wrong.
        /// </summary>
        /// <remarks>
        /// ArrayExtensions is deliberately absent from this list: it is still public, because
        /// GetPointer&lt;T&gt; is how a consumer builds the CPointer&lt;T&gt; that swe_houses_ex,
        /// swe_houses_ex2 and swe_cotrans take, so removing it would remove the only supported
        /// way to call those APIs. It is a public extension method in namespace SwissEphNet by
        /// design, and the approved list carries it on its own line where a reviewer can see it.
        /// </remarks>
        [Theory]
        [InlineData("SwissEphNet.StringExtensions")]
        [InlineData("SwissEphNet.TypeExtensions")]
        public void ExtensionHelpers_AreNotExternallyVisible(string typeName)
        {
            var type = typeof(SwissEph).Assembly.GetType(typeName, throwOnError: true);

            Assert.False(type.IsPublic,
                typeName + " is public again. In namespace SwissEphNet, that puts its extension " +
                "methods into the scope of every consumer that writes 'using SwissEphNet;'.");

            var surface = PublicApiSurface.Render(typeof(SwissEph).Assembly);
            Assert.DoesNotContain(surface, line => line.IndexOf(typeName + ".",
                                                                System.StringComparison.Ordinal) >= 0);
        }
    }
}
