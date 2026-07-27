using System.Runtime.InteropServices;
using SwissEphNet;

namespace BaselineMatrix;

/// <summary>
/// Diagnostic information about the environment a matrix run executed in. This is
/// never part of a baseline TSV -- Math.Sin/Cos/Tan/Pow/Asin/Acos/Atan/Atan2/Log/Exp
/// are not guaranteed bit-identical across OS, architecture or runtime version (only
/// Math.Sqrt is), so exact-match comparisons cannot depend on this matching, and
/// baking it into the data would make the file falsely look different across
/// machines. It exists purely so a human can eyeball which SwissEphNet assembly
/// actually ran -- the reference package's or the local build's.
/// </summary>
public static class EnvInfo
{
    public static string Describe()
    {
        return string.Join('\n',
        [
            $"FrameworkDescription={RuntimeInformation.FrameworkDescription}",
            $"OSDescription={RuntimeInformation.OSDescription}",
            $"ProcessArchitecture={RuntimeInformation.ProcessArchitecture}",
            $"SwissEphAssemblyLocation={typeof(SwissEph).Assembly.Location}",
        ]);
    }
}
