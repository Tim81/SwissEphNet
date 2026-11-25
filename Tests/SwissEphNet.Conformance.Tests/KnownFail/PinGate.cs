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
/// One compared value of a mismatching field, as the pin gate sees it. A file-backed check
/// (CHECK_D, CHECK_DD, CHECK_I, CHECK_S) has one: the port's value (<see cref="FieldMismatch.Actual"/>),
/// the other side being t.exp. A runtime self-check (CHECK_EQUALS_D, CHECK_EQUALS_I) has two, the
/// macro's first and second operand under <see cref="FieldMismatch.Name"/> and
/// <see cref="FieldMismatch.Name"/> + <see cref="PinGate.SecondOperandSuffix"/>: it compares two
/// values the run produced, and which of them is the one the port computed depends on the call site
/// (suite 10's CHECK_EQUALS_D(xcross, xx[0]) passes the t.exp input first and the computed value
/// second, suite 11's CHECK_EQUALS_D(xback[0], xin[0]) the reverse), so both are pinned rather
/// than guessed.
/// </summary>
public sealed record PinOperand(string Name, FieldKind Kind, string Text, FieldMismatch Source, bool Second);

/// <summary>
/// The value-level half of the known-fail gate. The category and magnitude_key comparison of
/// <see cref="ConformanceReport"/> only sees the worst field of a row, in decades; any other field
/// can move unseen, and a row whose worst field has key 0 or n/a cannot visibly change at all.
/// A VALUE-MISMATCH row is accepted only while, against known-fail-pins.tsv:
/// (1) the set of mismatching field names equals the pinned set (occurrences counted);
/// (2) every mismatching field's value equals its pinned port value: a double within setest's own
///     tolerance for that field (Tolerances.tolerance_for with the iteration's suite, testcase and
///     iflag, falling back to the t.fix precision exactly as CheckContext does), an int exactly, a
///     string exactly after the ephemeris directory is replaced by a placeholder. For a runtime
///     self-check (CHECK_EQUALS_*, not file-backed) there is no t.exp side: both operands are pinned,
///     the second under "name#2", each compared the same way;
/// (3) every field that matches t.exp keeps matching it, which is (1): a field that regressed
///     joins the mismatch set.
/// </summary>
public static class PinGate
{
    public const string EphePlaceholder = "<ephe>";

    /// <summary>Appended to a self-check's name to name its second operand in known-fail-pins.tsv.</summary>
    public const string SecondOperandSuffix = "#2";

    /// <summary>The text pinned for a mismatch's actual value (a self-check's first operand).</summary>
    public static string PinText(FieldMismatch m, string epheDir) => m.Kind switch
    {
        FieldKind.String => Encode(NormalizePaths(m.Actual, epheDir)),
        _ => m.Actual,
    };

    /// <summary>
    /// Every value pinned for one mismatch, in pin-file order: the port's value for a file-backed
    /// check, both operands for a runtime self-check.
    /// </summary>
    public static IReadOnlyList<PinOperand> Operands(FieldMismatch m, string epheDir)
    {
        var first = new PinOperand(m.Name, m.Kind, PinText(m, epheDir), m, Second: false);
        return m.SelfCheck
            ? [first, new PinOperand(m.Name + SecondOperandSuffix, m.Kind, m.Expected, m, Second: true)]
            : [first];
    }

    /// <summary>
    /// Replaces the ephemeris directory (either slash direction, with or without a trailing
    /// separator) by <see cref="EphePlaceholder"/>, then any other absolute path by "&lt;path&gt;"
    /// as known-fail.tsv's reason column does, so a pin does not depend on the machine's checkout.
    /// The library appends its own "/" to the directory it was given, so a directory configured with
    /// a trailing separator reaches serr as "...ephe\\/": that is collapsed to the "&lt;ephe&gt;/" the
    /// separator-free spelling produces, so SWISSEPH_CONFORMANCE_EPHE gives the same pin either way.
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

                value = Regex.Replace(value, Regex.Escape(EphePlaceholder) + @"[\\/]{2,}", EphePlaceholder + "/");
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

        var actualByName = result.Mismatches.SelectMany(m => Operands(m, epheDir)).GroupBy(o => o.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var pinnedByName = pins.GroupBy(p => p.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var name in actualByName.Keys.Union(pinnedByName.Keys, StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal))
        {
            actualByName.TryGetValue(name, out var actual);
            pinnedByName.TryGetValue(name, out var pinned);
            var nActual = actual?.Count ?? 0;
            var nPinned = pinned?.Count ?? 0;

            if (nPinned == 0)
            {
                violations.Add(new PinViolation(key, name, DescribeNewMismatch(key, actual![0])));
                continue;
            }

            if (nActual == 0)
            {
                violations.Add(new PinViolation(key, name,
                    $"{key} field '{name}' no longer mismatches t.exp (or, for a runtime self-check, no longer fails) but is pinned (pinned {pinned![0].Value}): the row improved, regenerate to record it."));
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
                var violation = CompareOne(result, actual![i], pinned![i], pins);
                if (violation is not null)
                {
                    violations.Add(violation);
                }
            }
        }

        return violations;
    }

    private static string DescribeNewMismatch(IterationKey key, PinOperand operand)
    {
        var m = operand.Source;
        return m.SelfCheck
            ? $"{key} runtime self-check '{m.Name}' now fails (CHECK_EQUALS operands: first {PinText(m, "")}, second {m.Expected}; not file-backed, no t.exp side) but is not in the pinned mismatch set: a check that passed regressed."
            : $"{key} field '{operand.Name}' now mismatches t.exp (actual {operand.Text}, t.exp {m.Expected}) but is not in the pinned mismatch set: a field that matched t.exp regressed.";
    }

    private static PinViolation? CompareOne(IterationResult result, PinOperand operand, PinnedField pin, IReadOnlyList<PinnedField> rowPins)
    {
        var key = result.Key;
        var name = operand.Name;
        if (operand.Kind != pin.Kind)
        {
            return new PinViolation(key, name, $"{key} field '{name}' is now compared as {operand.Kind}, pinned as {pin.Kind}.");
        }

        var actualText = operand.Text;
        if (operand.Kind != FieldKind.Double)
        {
            return string.Equals(actualText, pin.Value, StringComparison.Ordinal)
                ? null
                : new PinViolation(key, name, $"{key} field '{name}': pinned {pin.Value}, actual {actualText}.");
        }

        if (!double.TryParse(pin.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pinnedValue) ||
            !double.TryParse(actualText, NumberStyles.Float, CultureInfo.InvariantCulture, out var actualValue))
        {
            return new PinViolation(key, name, $"{key} field '{name}': cannot parse pinned '{pin.Value}' or actual '{actualText}' as a double.");
        }

        if (double.IsNaN(pinnedValue) && double.IsNaN(actualValue))
        {
            return null;
        }

        var tolerance = ToleranceFor(result, ToleranceName(name), ToleranceReference(operand, pinnedValue, rowPins));
        if (pinnedValue == actualValue || Math.Abs(pinnedValue - actualValue) <= tolerance)
        {
            return null;
        }

        return new PinViolation(key, name,
            $"{key} field '{name}': pinned {pin.Value}, actual {actualText}, difference {Math.Abs(pinnedValue - actualValue):G3} exceeds setest's tolerance {tolerance:G3}.");
    }

    /// <summary>A self-check's second operand is classified by tolerances.c under the check's own name.</summary>
    private static string ToleranceName(string name) =>
        name.EndsWith(SecondOperandSuffix, StringComparison.Ordinal) ? name[..^SecondOperandSuffix.Length] : name;

    /// <summary>
    /// The value tolerance_for sees. check_equals_d_internal passes the second operand ("exp"), so
    /// for a self-check's first operand that is the pinned second operand, not its own value.
    /// </summary>
    private static double ToleranceReference(PinOperand operand, double ownPinnedValue, IReadOnlyList<PinnedField> rowPins)
    {
        if (!operand.Source.SelfCheck || operand.Second)
        {
            return ownPinnedValue;
        }

        var second = rowPins.FirstOrDefault(p => string.Equals(p.Name, operand.Name + SecondOperandSuffix, StringComparison.Ordinal));
        return second is not null && double.TryParse(second.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : ownPinnedValue;
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
