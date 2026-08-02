using SwissEphNet.CPort;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// <c>swi_cutstr</c> against the C it transliterates, <c>swephlib.c</c>'s <c>swi_cutstr</c>,
    /// which sits commented out directly above the port in <c>SwephLib.cs</c>.
    /// </summary>
    /// <remarks>
    /// Every expectation here is derived from that C by hand, not from the port's own prior
    /// output. The C seeds <c>n = 1</c> with <c>cpos[0] = s</c> before its loop, so a field
    /// always exists and its <c>n &lt; nmax</c> test is on a count that already includes the
    /// first field. The port had neither property.
    /// </remarks>
    public class CutStrTest
    {
        private static readonly char[] Comma = { ',' };
        private static readonly char[] Semi = { ';' };

        [Fact]
        public void EmptyInputYieldsOneEmptyField()
        {
            // C: n = 1 and cpos[0] = s before the loop body runs at all, so an empty string is
            // one empty field. The port returned an empty array, and a caller indexing cpos[0]
            // the way the C's callers do would have read past the end.
            var n = SwephLib.swi_cutstr(string.Empty, Comma, out var cpos, 20);
            Assert.Equal(1, n);
            Assert.Equal(new[] { string.Empty }, cpos);
        }

        [Fact]
        public void TrailingSeparatorYieldsATrailingEmptyField()
        {
            // C: the cut at ',' starts a new field at s + 1, which is the terminator, so
            // cpos[1] is "". The port gated its trailing add on ps < pe and dropped it.
            var n = SwephLib.swi_cutstr("a,", Comma, out var cpos, 20);
            Assert.Equal(2, n);
            Assert.Equal(new[] { "a", string.Empty }, cpos);
        }

        [Fact]
        public void LastFieldIsLeftUnCutWhenNmaxIsReached()
        {
            // The behaviour this function's own doc comment promises: "If more than nmax fields
            // are found, nmax is returned and the last field nmax-1 remains un-cut." With n
            // seeded at 1, the C stops cutting once n reaches nmax, so "a;b;c" with nmax 2 is
            // {"a", "b;c"}. The port's count lagged by one and returned {"a", "b", "c"}.
            var n = SwephLib.swi_cutstr("a;b;c", Semi, out var cpos, 2);
            Assert.Equal(2, n);
            Assert.Equal(new[] { "a", "b;c" }, cpos);
        }

        [Fact]
        public void NmaxOfOneCutsNothing()
        {
            // n starts at 1, so n < 1 is false immediately and no cut ever happens.
            var n = SwephLib.swi_cutstr("a;b;c", Semi, out var cpos, 1);
            Assert.Equal(1, n);
            Assert.Equal(new[] { "a;b;c" }, cpos);
        }

        [Fact]
        public void RunsOfSeparatorsCountAsOne()
        {
            // The doc comment's own worked example: cut_str_any("word,,,word2", ",") gives two
            // parts. The C skips forward while the NEXT character is also a separator.
            var n = SwephLib.swi_cutstr("word,,,word2", Comma, out var cpos, 20);
            Assert.Equal(2, n);
            Assert.Equal(new[] { "word", "word2" }, cpos);
        }

        [Fact]
        public void LeadingSeparatorYieldsALeadingEmptyField()
        {
            // cpos[0] is the text before the first cut, which here is nothing.
            var n = SwephLib.swi_cutstr(",a", Comma, out var cpos, 20);
            Assert.Equal(2, n);
            Assert.Equal(new[] { string.Empty, "a" }, cpos);
        }

        [Fact]
        public void NewlineEndsTheStringLikeATerminator()
        {
            // C: "treat nl or cr like end of string" -- *s = '\0' and break, so everything from
            // the newline on is dropped, including further separators.
            var n = SwephLib.swi_cutstr("a,b\nc,d", Comma, out var cpos, 20);
            Assert.Equal(2, n);
            Assert.Equal(new[] { "a", "b" }, cpos);
        }

        [Fact]
        public void CarriageReturnBehavesLikeNewline()
        {
            var n = SwephLib.swi_cutstr("a,b\r\nc", Comma, out var cpos, 20);
            Assert.Equal(2, n);
            Assert.Equal(new[] { "a", "b" }, cpos);
        }

        [Fact]
        public void OrdinarySplitIsUnchanged()
        {
            // The shape both live callers actually pass: well under nmax, no empty fields. This
            // is the case that must not move, since swi_fopen and fixstar_cut_string depend on
            // it and the characterization baseline cannot see either.
            var n = SwephLib.swi_cutstr("Aldebaran,alTau,4,35,55.2", Comma, out var cpos, 20);
            Assert.Equal(5, n);
            Assert.Equal(new[] { "Aldebaran", "alTau", "4", "35", "55.2" }, cpos);
        }
    }
}
