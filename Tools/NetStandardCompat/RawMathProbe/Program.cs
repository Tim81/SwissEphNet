// Standalone probe for whether System.Math.Sin/Cos/Tan/Atan2 themselves differ between net48 and
// net10.0 -- see RawMathProbe.csproj's own header comment for why this exists as a separate,
// SwissEphNet-free project. Writes one row per swept argument: arg_hex, sin_hex, cos_hex, tan_hex,
// atan2_hex (atan2(arg, 1.0)), tab-separated, hex-only (BitConverter.DoubleToInt64Bits, cast
// unchecked to ulong -- not DoubleToUInt64Bits, which does not exist on net462/net48; see
// Tools/NetStandardCompat/NetStandardCompatDump/Program.cs's identical EmitValue comment for why).
//
// SWEEP
//
// Two parts, both fixed and deterministic:
//   1. 4001 evenly spaced points across [-2*pi, 2*pi] (step (4*pi)/4000), covering two full
//      periods of both functions.
//   2. A dense cluster of 2001 points spanning [-1e-6, 1e-6] around each of 0, pi/2, pi, 3*pi/2
//      and 2*pi (10005 rows total) -- landmarks the causal claim names ("near pi") plus the other
//      three quarter-turn boundaries, so a divergence that clusters specifically at pi and not at
//      the others would be visible as such, rather than folded into one "near pi" bucket by
//      construction.
//
// INVOCATION
//
//   RawMathProbe.exe <output.tsv>

using System.Globalization;

namespace RawMathProbe;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: RawMathProbe <output.tsv>");
            return 1;
        }

        var outputPath = args[0];

        var arguments = new List<double>();

        const int wideCount = 4001;
        var wideLo = -2.0 * Math.PI;
        var wideHi = 2.0 * Math.PI;
        var wideStep = (wideHi - wideLo) / (wideCount - 1);
        for (var i = 0; i < wideCount; i++)
        {
            arguments.Add(wideLo + (i * wideStep));
        }

        double[] landmarks = [0.0, Math.PI / 2.0, Math.PI, 3.0 * Math.PI / 2.0, 2.0 * Math.PI];
        const int clusterCount = 2001;
        const double clusterHalfWidth = 1e-6;
        var clusterStep = (2.0 * clusterHalfWidth) / (clusterCount - 1);
        foreach (var landmark in landmarks)
        {
            for (var i = 0; i < clusterCount; i++)
            {
                arguments.Add(landmark - clusterHalfWidth + (i * clusterStep));
            }
        }

        using var writer = new StreamWriter(outputPath, append: false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            NewLine = "\n"
        };
        writer.Write("arg_hex");
        writer.Write('\t');
        writer.Write("sin_hex");
        writer.Write('\t');
        writer.Write("cos_hex");
        writer.Write('\t');
        writer.Write("tan_hex");
        writer.Write('\t');
        writer.Write("atan2_hex");
        writer.Write('\n');

        foreach (var arg in arguments)
        {
            writer.Write(Hex(arg));
            writer.Write('\t');
            writer.Write(Hex(Math.Sin(arg)));
            writer.Write('\t');
            writer.Write(Hex(Math.Cos(arg)));
            writer.Write('\t');
            writer.Write(Hex(Math.Tan(arg)));
            writer.Write('\t');
            writer.Write(Hex(Math.Atan2(arg, 1.0)));
            writer.Write('\n');
        }

        Console.Error.WriteLine($"RawMathProbe: wrote {arguments.Count} row(s) to {outputPath}");
        return 0;
    }

    private static string Hex(double value)
    {
        var bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
        return bits.ToString("x16", CultureInfo.InvariantCulture);
    }
}
