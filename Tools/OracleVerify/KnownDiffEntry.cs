namespace OracleVerify;

/// <summary>
/// One row of Tests/oracle/known-diff.tsv.
/// </summary>
/// <param name="CaseId">The dump row's case_id, matching a row in both dump-c-2.10.03.tsv and dump-net.tsv.</param>
/// <param name="Category">Why this row is allowed to differ -- see <see cref="DiffCategory"/>.</param>
/// <param name="MaxUlp">
/// The largest non-categorical field distance recorded for this case_id, or <see langword="null"/>
/// when at least one field differs categorically (a NaN on one side, a finite value on the
/// other -- see <see cref="UlpMath.CategoricalDistance"/>). <see langword="null"/> is written to
/// the TSV as the literal text "categorical", never as a number: the previous encoding used
/// <see cref="ulong.MaxValue"/> as an in-band sentinel, which meant nothing could ever compare
/// greater than it, so the drift check in <see cref="OracleVerifyReport"/> could never fail on a
/// row recorded that way. Representing "categorical" as a distinct state instead of a number
/// means the check that matters for these rows is "is it still categorical", not "did a
/// meaningless magnitude get bigger" -- see <see cref="OracleVerifyReport"/>'s remarks.
/// </param>
/// <param name="Reason">A short, deterministic summary of what differs -- see <c>Program.BuildReason</c>.</param>
internal sealed record KnownDiffEntry(string CaseId, DiffCategory Category, ulong? MaxUlp, string Reason);
