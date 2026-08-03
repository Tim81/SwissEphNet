// Characterization ("golden master") generator for SwissEphNet.
//
// Runs the fixed matrix defined in Tools/BaselineMatrix (Swiss Ephemeris calls that
// need no ephemeris data files -- Moshier / analytic paths only) and writes one
// tab-separated file per area into the directory given as argv[0], plus an
// EnvInfo.SidecarFileName sidecar recording the environment the run executed in.
//
// SwissEph.OpenBinary defaults to reading real files off the filesystem (see
// docs/known-issues.md, "OnLoadFile: multicast leaks a stream..." and its superseding
// entry), which this generator must never do: the committed baseline is Moshier-only
// by construction, and a real ephemeris directory happening to be present on whatever
// machine regenerates it would silently change the areas that exercise a missing-file
// fallback. Areas.Generate (Tools/BaselineMatrix/Areas.cs) sets
// SwissEph.DefaultFileProvider to a no-op provider before running any area's
// generator -- every one of the several hundred `new SwissEph()` call sites across
// Tools/BaselineMatrix inherits it structurally, from the one choke point all of them
// go through, rather than this Main needing to remember it too.
//
// The matrix code lives in BaselineMatrix.csproj, which is built in one of two
// modes selected by the UseReferencePackage MSBuild property:
//   - reference mode: SwissEphNet resolved from NuGet package 2.8.0.2
//   - local mode:     SwissEphNet resolved via ProjectReference to the in-repo library
// Both modes must produce byte-identical output when the local code matches 2.8.0.2.
// See Tools/BaselineGen/README.md for the exact commands.
//
// swe_houses_armc carries a hidden static-like field (saved_sundec) that affects
// hsys 'I' when ascmc[9] == 99. To keep output reproducible regardless of call
// order, every single row in the matrix uses a brand new SwissEph instance.

using System.Text;
using BaselineMatrix;

namespace BaselineGen;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: BaselineGen <output-directory>");
            return 1;
        }

        var outputDir = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(outputDir);

        var env = EnvInfo.Describe();
        Console.WriteLine(env);

        var totalRows = 0;
        foreach (var (name, populate) in Areas.All)
        {
            var rows = Areas.Generate(populate);
            var path = Path.Combine(outputDir, $"baseline-{name}.tsv");
            WriteFile(path, rows);
            var size = new FileInfo(path).Length;
            Console.WriteLine($"{name,-14} {rows.Count,7} rows  {size,10} bytes  {path}");
            totalRows += rows.Count;
        }

        var envPath = Path.Combine(outputDir, EnvInfo.SidecarFileName);
        File.WriteAllText(envPath, env + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Console.WriteLine($"Total: {totalRows} rows across {Areas.All.Length} areas.");
        return 0;
    }

    private static void WriteFile(string path, List<string> rows)
    {
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.NewLine = "\n";
        foreach (var row in rows)
        {
            writer.WriteLine(row);
        }
    }
}
