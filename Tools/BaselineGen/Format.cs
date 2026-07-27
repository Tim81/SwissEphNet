using System.Globalization;
using System.Text;

namespace BaselineGen;

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
    /// code, so an exception here is itself a piece of frozen behavior.
    /// </summary>
    public static string SafeRow(string caseId, Func<string[]> body)
    {
        try
        {
            return Row(caseId, body());
        }
        catch (Exception ex)
        {
            return Row(caseId, "EXCEPTION", S($"{ex.GetType().Name}: {ex.Message}"));
        }
    }
}
