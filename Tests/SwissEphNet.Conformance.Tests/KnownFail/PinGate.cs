using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using SwissEphNet.Conformance.Tests.Dispatch;

namespace SwissEphNet.Conformance.Tests.KnownFail;

/// <summary>One way a VALUE-MISMATCH row no longer matches its pins. <see cref="Message"/> names row, field, pinned and actual value.</summary>
public sealed record PinViolation(IterationKey Key, string Field, string Message);

/// <summary>
/// The value-level half of the known-fail gate. The category and magnitude_key comparison of
/// <see cref="ConformanceReport"/> only sees the worst field of a row, in decades; any other field
/// can move unseen, and a row whose worst field has key 0 or n/a cannot visibly change at all.
/// A VALUE-MISMATCH row is accepted only while, against known-fail-pins.tsv:
/// (1) the set of mismatching field names equals the pinned set (occurrences counted);
/// (2) every mismatching field's value equals its pinned port value: a double within setest's own
///     tolerance for that field (Tolerances.tolerance_for with the iteration's suite, testcase and
///     iflag, falling back to the t.fix precision exactly as CheckContext does), an int exactly, a
///     string exactly after the ephemeris directory is replaced by a placeholder;
/// (3) every field that matches t.exp keeps matching it, which is (1): a field that regressed
///     joins the mismatch set.
/// </summary>
public static class PinGate
{
    public const string EphePlaceholder = "<ephe>";

    /// <summary>The text pinned for a mismatch's actual value.</summary>
    public static string PinText(FieldMismatch m, string epheDir) => m.Kind switch
    {
        FieldKind.String => Encode(NormalizePaths(m.Actual, epheDir)),
        _ => m.Actual,
    };

    /// <summary>
    /// Replaces the ephemeris directory (either slash direction, with or without a trailing
    /// separator) by <see cref="EphePlaceholder"/>, then any other absolute path by "&lt;path&gt;"
    /// as known-fail.tsv's reason column does, so a pin does not depend on the machine's checkout.
    /// </summary>
    public static string NormalizePaths(string value, string epheDir)
    {
        if (!string.IsNullOrEmpty(epheDir))
        {
            var trimmed = epheDir.TrimEnd('\\', '/');
            if (trimmed.Length > 0)
            {
                foreach (var variant in new[] { trimmed, trimmed.Replace('\\', '/'), trimmed.Replace('/', '\\') }.Distinct())
                {
                    value = value.Replace(variant, EphePlaceholder, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        value = Regex.Replace(value, @"[A-Za-z]:[\\/](?:[^\s'""]+[\\/])*[^\s'""]*", "<path>");
        value = Regex.Replace(value, @"(?<![\w.])/(?:[^\s'""/]+/)+[^\s'""/]*", "<path>");
        return value;
    }

    /// <summary>Keeps a pinned string on one TSV line: backslash, tab and CR are escaped.</summary>
    public static string Encode(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal);

    /// <summary>
    /// Checks one VALUE-MISMATCH <paramref name="result"/> against <paramref name="pins"/> (null or
    /// empty means the row has none, itself a violation). Returns no violations when the row still
    /// reproduces its pinned failure.
    /// </summary>
    public static IReadOnlyList<PinViolation> Check(IterationResult result, IReadOnlyList<PinnedField>? pins, string epheDir)
    {
        var violations = new List<PinViolation>();
        var key = result.Key;
        pins ??= [];
        if (pins.Count == 0)
        {
            violations.Add(new PinViolation(key, "*", $"{key}: VALUE-MISMATCH row has no pinned fields in known-fail-pins.tsv. Regenerate (scripts/regenerate-known-fail.ps1 -Reason \"...\")."));
            return violations;
        }

        var actualByName = result.Mismatches.GroupBy(m => m.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var pinnedByName = pins.GroupBy(p => p.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var name in actualByName.Keys.Union(pinnedByName.Keys, StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal))
        {
            actualByName.TryGetValue(name, out var actual);
            pinnedByName.TryGetValue(name, out var pinned);
            var nActual = actual?.Count ?? 0;
            var nPinned = pinned?.Count ?? 0;

            if (nPinned == 0)
            {
                violations.Add(new PinViolation(key, name,
                    $"{key} field '{name}' now mismatches t.exp (actual {PinText(actual![0], epheDir)}, t.exp {actual[0].Expected}) but is not in the pinned mismatch set: a field that matched t.exp regressed."));
                continue;
            }

            if (nActual == 0)
            {
                violations.Add(new PinViolation(key, name,
                    $"{key} field '{name}' no longer mismatches t.exp but is pinned (pinned {pinned![0].Value}): the row improved, regenerate to record it."));
                continue;
            }

            if (nActual != nPinned)
            {
                violations.Add(new PinViolation(key, name,
                    $"{key} field '{name}' mismatches {nActual} time(s) now, {nPinned} time(s) pinned."));
                continue;
            }

            for (var i = 0; i < nActual; i++)
            {
                var violation = CompareOne(result, actual![i], pinned![i], epheDir);
                if (violation is not null)
                {
                    violations.Add(violation);
                }
            }
        }

        return violations;
    }

    private static PinViolation? CompareOne(IterationResult result, FieldMismatch actual, PinnedField pin, string epheDir)
    {
        var key = result.Key;
        if (actual.Kind != pin.Kind)
        {
            return new PinViolation(key, actual.Name, $"{key} field '{actual.Name}' is now compared as {actual.Kind}, pinned as {pin.Kind}.");
        }

        var actualText = PinText(actual, epheDir);
        if (actual.Kind != FieldKind.Double)
        {
            return string.Equals(actualText, pin.Value, StringComparison.Ordinal)
                ? null
                : new PinViolation(key, actual.Name, $"{key} field '{actual.Name}': pinned {pin.Value}, actual {actualText}.");
        }

        if (!double.TryParse(pin.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pinnedValue) ||
            !double.TryParse(actual.Actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var actualValue))
        {
            return new PinViolation(key, actual.Name, $"{key} field '{actual.Name}': cannot parse pinned '{pin.Value}' or actual '{actual.Actual}' as a double.");
        }

        if (double.IsNaN(pinnedValue) && double.IsNaN(actualValue))
        {
            return null;
        }

        var tolerance = ToleranceFor(result, actual.Name, pinnedValue);
        if (pinnedValue == actualValue || Math.Abs(pinnedValue - actualValue) <= tolerance)
        {
            return null;
        }

        return new PinViolation(key, actual.Name,
            $"{key} field '{actual.Name}': pinned {pin.Value}, actual {actual.Actual}, difference {Math.Abs(pinnedValue - actualValue):G3} exceeds setest's tolerance {tolerance:G3}.");
    }

    /// <summary>
    /// The tolerance setest would apply to this field (checkpoints.c check_equals_d_internal):
    /// tolerances.c's per-quantity value when it classifies the name, else the t.fix precision
    /// (xx* names with index &lt; 6 use their per-component precision, everything else the overall one).
    /// </summary>
    public static double ToleranceFor(IterationResult result, string fieldName, double pinnedValue)
    {
        var pin = result.Pin;
        var fallback = pin is null ? Corpus.Precision.Default.All : FallbackPrecision(pin.Precision, fieldName);
        var t = Tolerances.tolerance_for(fieldName, pinnedValue, result.Key.Suite, result.Key.TestCase, pin?.Iflag ?? 0);
        return t >= 0 ? t : fallback;
    }

    private static double FallbackPrecision(Corpus.Precision precision, string fieldName)
    {
        // CheckContext.CheckDD: an "xx" name prefix and index < 6 selects the per-component precision.
        var open = fieldName.LastIndexOf('[');
        if (fieldName.StartsWith("xx", StringComparison.Ordinal) && open > 0 && fieldName.EndsWith(']') &&
            int.TryParse(fieldName.AsSpan(open + 1, fieldName.Length - open - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var index) &&
            index < 6)
        {
            return precision.Xx[index];
        }

        return precision.All;
    }
}
