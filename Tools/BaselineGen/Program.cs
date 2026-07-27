// Characterization ("golden master") generator for SwissEphNet.
//
// Runs a fixed matrix of Swiss Ephemeris calls that need no ephemeris data files
// (Moshier / analytic paths only, since no OnLoadFile handler is ever subscribed)
// and writes one tab-separated row per case to the path given as argv[0].
//
// This file is shared, unmodified, between two build modes selected by the
// UseReferencePackage MSBuild property:
//   - reference mode: SwissEphNet resolved from NuGet package 2.8.0.2
//   - local mode:     SwissEphNet resolved via ProjectReference to the in-repo library
// Both modes must produce byte-identical output when the local code matches 2.8.0.2.
//
// swe_houses_armc carries a hidden static-like field (saved_sundec) that affects
// hsys 'I' when ascmc[9] == 99. To keep output reproducible regardless of call
// order, every single row in this file uses a brand new SwissEph instance.

using System.Globalization;
using SwissEphNet;

namespace BaselineGen;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: BaselineGen <output-file>");
            return 1;
        }

        var rows = new List<string>(150_000);

        Houses.AddRows(rows);
        Houses.AddSunshineStateRows(rows);
        HousePos.AddRows(rows);
        HouseName.AddRows(rows);
        Calc.AddRows(rows);
        Ayanamsa.AddRows(rows);
        DateTime_.AddRows(rows);
        CoordHelpers.AddRows(rows);
        FormatHelpers.AddRows(rows);
        Misc.AddRows(rows);

        rows.Sort(StringComparer.Ordinal);

        var outputPath = args[0];
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using (var writer = new StreamWriter(outputPath, append: false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.NewLine = "\n";
            foreach (var row in rows)
            {
                writer.WriteLine(row);
            }
        }

        Console.WriteLine($"Wrote {rows.Count} rows to {outputPath}");
        return 0;
    }
}
