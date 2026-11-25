using SwissEphNet.Conformance.Tests.Dispatch;

namespace SwissEphNet.Conformance.Tests.KnownFail;

/// <summary>
/// One mismatching field of one VALUE-MISMATCH row, with the value the port produced when the row
/// was last regenerated. <paramref name="Value"/> is stored exactly as it appears in
/// known-fail-pins.tsv: a double in round-trip ("G17") form, an int in decimal, a string with the
/// ephemeris directory replaced by <see cref="PinGate.EphePlaceholder"/> and tab, CR and backslash
/// escaped (<see cref="PinGate.Encode"/>).
/// </summary>
public sealed record PinnedField(IterationKey Key, string Name, FieldKind Kind, string Value);
