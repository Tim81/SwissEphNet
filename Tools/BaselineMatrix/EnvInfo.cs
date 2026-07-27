using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SwissEphNet;

namespace BaselineMatrix;

/// <summary>
/// Diagnostic information about the environment a matrix run executed in. This is
/// never part of a baseline TSV -- Math.Sin/Cos/Tan/Pow/Asin/Acos/Atan/Atan2/Log/Exp
/// are not guaranteed bit-identical across OS, architecture or runtime version (only
/// Math.Sqrt is), so exact-match comparisons cannot depend on this matching, and
/// baking it into the data would make the file falsely look different across
/// machines. It exists so a human -- or BaselineVerify itself -- can tell which
/// SwissEphNet assembly actually ran.
///
/// Assembly.Location alone cannot do that: with a ProjectReference or a copied
/// PackageReference DLL, Location is just wherever the host project's output
/// directory put it, so reference mode and local mode print the exact same path.
/// ModuleVersionId (a GUID baked in at compile time, distinct per build) and a
/// SHA-256 of the DLL bytes are what actually distinguish "the reference package"
/// from "whatever CPort currently compiles to".
/// </summary>
public static class EnvInfo
{
    public const string ReferenceVersion = "2.8.0.2";

    /// <summary>Name of the committed sidecar file, derived from ReferenceVersion so a version bump cannot leave a stale-named file behind.</summary>
    public static string SidecarFileName => $"baseline-{ReferenceVersion}.env.txt";

    public static string Describe()
    {
        var assembly = typeof(SwissEph).Assembly;
        var location = assembly.Location;
        var name = assembly.GetName();
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

        return string.Join('\n',
        [
            $"FrameworkDescription={RuntimeInformation.FrameworkDescription}",
            $"OSDescription={RuntimeInformation.OSDescription}",
            $"ProcessArchitecture={RuntimeInformation.ProcessArchitecture}",
            $"SwissEphAssemblyLocation={location}",
            $"SwissEphAssemblyVersion={name.Version}",
            $"SwissEphInformationalVersion={informationalVersion}",
            $"SwissEphModuleVersionId={CurrentModuleVersionId():D}",
            $"SwissEphAssemblySha256={ComputeSha256(location)}",
        ]);
    }

    /// <summary>The GUID baked into the currently loaded SwissEphNet assembly at compile time.</summary>
    public static Guid CurrentModuleVersionId() => typeof(SwissEph).Assembly.ManifestModule.ModuleVersionId;

    /// <summary>Extracts the SwissEphModuleVersionId= line from a previously captured <see cref="Describe"/> block, or null if absent/unparseable.</summary>
    public static Guid? ParseModuleVersionId(string envFileContent)
    {
        const string prefix = "SwissEphModuleVersionId=";
        foreach (var line in envFileContent.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal) &&
                Guid.TryParse(trimmed[prefix.Length..], out var guid))
            {
                return guid;
            }
        }
        return null;
    }

    private static string ComputeSha256(string assemblyLocation)
    {
        if (string.IsNullOrEmpty(assemblyLocation) || !File.Exists(assemblyLocation))
        {
            return "(unavailable)";
        }
        using var stream = File.OpenRead(assemblyLocation);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
