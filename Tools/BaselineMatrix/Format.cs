using System.Globalization;
using System.Text;

namespace BaselineMatrix;

/// <summary>
/// Formatting helpers shared by every matrix generator. Every row must be
/// reproducible byte-for-byte across machines and runs: fixed culture, full
/// round-trip precision for doubles, and escaping for anything that could
/// contain a tab or newline.
/// </summary>
internal static class Format
{
    public static string D(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    public static string I(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string C(char value) => value.ToString(CultureInfo.InvariantCulture);

    public static string B(bool value) => value.ToString(CultureInfo.InvariantCulture);

    public static string S(string? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\")
            .Replace("\t", "\\t")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }

    /// <summary>
    /// Builds one TSV row: case id, then every value field, tab separated.
    /// </summary>
    public static string Row(string caseId, params string[] fields)
    {
        var sb = new StringBuilder(caseId.Length + fields.Length * 12);
        sb.Append(caseId);
        foreach (var field in fields)
        {
            sb.Append('\t').Append(field);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Runs a case body, catching any exception so one unexpected throw does not
    /// abort the whole generation run. Both reference and local mode run the same
    /// code, so an exception here is itself a piece of frozen behavior -- but only
    /// its type is frozen. The message is included only when the exception TYPE is
    /// itself defined in the SwissEphNet assembly (a type SwissEphNet's own source
    /// throws, with a message SwissEphNet's own source wrote). Standard runtime
    /// exception types such as IndexOutOfRangeException are defined in the base
    /// class library regardless of which assembly's code triggered them, and their
    /// .Message is a framework resource string that a future .NET runtime is free
    /// to reword -- recording it would make the baseline fail for reasons that have
    /// nothing to do with SwissEphNet. (Checking the throw-site stack frame instead
    /// of the exception's declaring assembly was tried and rejected: JIT inlining in
    /// Release builds can attribute the frame to the caller, making that check
    /// unreliable.) OutOfMemoryException is never caught here: it means the process
    /// is in trouble, not that a case produced an interesting result.
    /// </summary>
    public static string SafeRow(string caseId, Func<string[]> body)
    {
        try
        {
            return Row(caseId, body());
        }
        catch (OutOfMemoryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var typeName = ex.GetType().Name;
            var message = ex.GetType().Assembly.GetName().Name == "SwissEphNet" ? ex.Message : string.Empty;
            return Row(caseId, "EXCEPTION", typeName, S(message));
        }
    }
}
