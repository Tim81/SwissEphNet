// The .NET side of scripts/verify-crt-parity.ps1's comparison. See
// Tools/CReference/crt-parity.c's header comment for why this program exists and how
// its value tables were chosen -- this file's tables must stay identical to that one's,
// value for value and in the same call order, or the two output streams stop lining up
// and the gate reports a false CRT difference instead of a real one. Nothing besides a
// human keeps the two files in step; verify-crt-parity.ps1 can only tell that the line
// counts match, not that the values behind them do.
//
// Bits come from BitConverter.DoubleToUInt64Bits, the managed equivalent of the C side's
// memcpy-into-uint64_t: both read the IEEE-754 representation directly rather than going
// through a cast that could round or reinterpret.

using System.Globalization;

namespace CrtParity;

internal static class Program
{
    private static readonly double[] Values =
    [
        0.0, -0.0,
        1e-10, -1e-10,
        0.5, -0.5,
        0.9999999999999999, 1.0000000000000002,
        1.0, -1.0,
        1.5707963267948966, -1.5707963267948966,
        3.141592653589793, 2.718281828459045,
        1e10, -1e10,
        1e15, -1e15,
        23.4392911, -23.4392911,
        2451545.0, -2451545.0,
        100.0, -100.0,
        0.1, 10.0
    ];

    private static readonly double[] UnitDomain =
    [
        0.0, -0.0,
        1e-10, -1e-10,
        0.1, -0.1,
        0.5, -0.5,
        0.7071067811865476, -0.7071067811865476,
        0.9999999999999999, -0.9999999999999999,
        1.0, -1.0
    ];

    private static readonly double[] NonNegative =
    [
        0.0,
        1e-10,
        0.1,
        0.5,
        0.9999999999999999, 1.0000000000000002,
        1.0,
        1.5707963267948966,
        3.141592653589793, 2.718281828459045,
        1e10, 1e15,
        23.4392911,
        2451545.0,
        100.0,
        10.0
    ];

    private static readonly (double A, double B)[] Atan2Pairs =
    [
        (0.0, 1.0), (1.0, 0.0), (0.0, -1.0), (-0.0, 1.0),
        (1.0, 1.0), (-1.0, -1.0),
        (23.4392911, 2451545.0),
        (1e15, 1e10), (-1e10, 1e15),
        (0.5, -0.5),
        (3.141592653589793, 2.718281828459045),
        (-1.0, 0.0),
        (1e-10, 1e-10)
    ];

    private static readonly (double A, double B)[] PowPairs =
    [
        (2.0, 10.0), (2.0, 0.5), (10.0, -1.0), (0.5, 0.5),
        (1.0000000000000002, 1e15),
        (23.4392911, 2.0), (2451545.0, 0.5),
        (0.0, 0.0), (0.0, 2.0), (2.0, 0.0),
        (-2.0, 3.0), (-2.0, 2.0),
        (1e10, 0.1)
    ];

    private static readonly (double A, double B)[] FmodPairs =
    [
        (5.5, 2.0), (-5.5, 2.0), (5.5, -2.0),
        (2451545.0, 365.25),
        (1e15, 1e10),
        (23.4392911, 1.0),
        (0.1, 0.03),
        (7.0, 7.0),
        (1e-10, 3.0),
        (100.0, 0.1), (-100.0, 3.0),
        (3.141592653589793, 1.0)
    ];

    private static void Main()
    {
        using var stdout = Console.OpenStandardOutput();
        using var writer = new StreamWriter(stdout, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            NewLine = "\n"
        };

        foreach (var v in Values) Emit(writer, "sin", Math.Sin(v));
        foreach (var v in Values) Emit(writer, "cos", Math.Cos(v));
        foreach (var v in Values) Emit(writer, "tan", Math.Tan(v));
        foreach (var v in Values) Emit(writer, "atan", Math.Atan(v));
        foreach (var v in Values) Emit(writer, "exp", Math.Exp(v));
        foreach (var v in Values) Emit(writer, "floor", Math.Floor(v));
        foreach (var v in Values) Emit(writer, "ceil", Math.Ceiling(v));

        foreach (var v in UnitDomain) Emit(writer, "asin", Math.Asin(v));
        foreach (var v in UnitDomain) Emit(writer, "acos", Math.Acos(v));

        foreach (var v in NonNegative) Emit(writer, "log", Math.Log(v));
        foreach (var v in NonNegative) Emit(writer, "log10", Math.Log10(v));
        foreach (var v in NonNegative) Emit(writer, "sqrt", Math.Sqrt(v));

        foreach (var (a, b) in Atan2Pairs) Emit(writer, "atan2", Math.Atan2(a, b));
        foreach (var (a, b) in PowPairs) Emit(writer, "pow", Math.Pow(a, b));
        // C#'s % on doubles is C's fmod: both truncate toward zero and give a result with
        // the same sign as the dividend, unlike Math.IEEERemainder, which rounds to nearest.
        foreach (var (a, b) in FmodPairs) Emit(writer, "fmod", a % b);
    }

    private static void Emit(TextWriter writer, string name, double result)
    {
        var bits = BitConverter.DoubleToUInt64Bits(result);
        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name}\t{bits:x16}"));
    }
}
