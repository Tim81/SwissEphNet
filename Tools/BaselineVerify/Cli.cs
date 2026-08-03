namespace BaselineVerify;

/// <summary>
/// Pure argv parsing for BaselineVerify's two modes (plain verify/report and --diff-scope),
/// pulled out of Program.cs so it is unit-testable without spinning up a process or touching
/// disk: no file I/O, no Console access, just <c>string[]</c> in and a <see cref="ParseResult"/>
/// out. Program.cs still owns turning that result into actual directory resolution
/// (<c>Path.GetFullPath</c>, <c>Directory.Exists</c>) and everything printed -- see Verdict.cs's
/// own doc comment for why Program.cs should contain orchestration and nothing that itself
/// needs a test, a rule this class exists to restore for the argv-parsing path.
/// </summary>
internal static class Cli
{
    internal sealed record DiffScopeRequest(string OldDir, string NewDir, string[] Globs);

    internal sealed record VerifyRequest(bool ReportOnly, string? DumpFailuresPath, string? BaselineDir);

    internal sealed class ParseResult
    {
        public bool IsDiffScope { get; private init; }
        public bool IsListPrefixes { get; private init; }
        public DiffScopeRequest? DiffScope { get; private init; }
        public VerifyRequest? Verify { get; private init; }
        public string? Error { get; private init; }
        public bool IsError => Error is not null;

        public static ParseResult ForDiffScope(DiffScopeRequest request) => new() { IsDiffScope = true, DiffScope = request };
        public static ParseResult ForListPrefixes() => new() { IsListPrefixes = true };
        public static ParseResult ForVerify(VerifyRequest request) => new() { IsDiffScope = false, Verify = request };
        public static ParseResult ForError(string message) => new() { Error = message };
    }

    public static ParseResult Parse(string[] args)
    {
        if (Array.IndexOf(args, "--list-prefixes") >= 0)
        {
            return ParseResult.ForListPrefixes();
        }

        var diffScopeFlagIndex = Array.IndexOf(args, "--diff-scope");
        return diffScopeFlagIndex >= 0 ? ParseDiffScope(args, diffScopeFlagIndex) : ParseVerify(args);
    }

    private static ParseResult ParseDiffScope(string[] args, int diffScopeFlagIndex)
    {
        if (diffScopeFlagIndex + 2 >= args.Length)
        {
            return ParseResult.ForError("--diff-scope requires two directory arguments: <old-baseline-dir> <new-baseline-dir>.");
        }
        var oldDir = args[diffScopeFlagIndex + 1];
        var newDir = args[diffScopeFlagIndex + 2];

        var scopeFlagIndex = Array.IndexOf(args, "--expected-scope");
        if (scopeFlagIndex < 0)
        {
            return ParseResult.ForError("--diff-scope requires --expected-scope <glob> [<glob> ...].");
        }

        var globs = args[(scopeFlagIndex + 1)..];
        if (globs.Length == 0)
        {
            return ParseResult.ForError("--expected-scope requires at least one glob.");
        }

        // args[(scopeFlagIndex + 1)..] takes everything to the end of argv, so
        // --expected-scope must be the last flag on the command line, or a flag that was
        // meant to come after it (there are none today, but a future one) would silently be
        // compiled as a glob instead of being recognized and rejected as unconsumed. This
        // fails closed even without the check below -- Waivers.CompileGlob rejects a glob
        // with a wildcard before its first '|', and a flag like "--foo" has no '|' at all, so
        // the whole string becomes the "area prefix" and is rejected as invalid -- but for the
        // wrong reason, with a confusing message. Reject anything flag-shaped explicitly.
        var strayFlag = Array.Find(globs, static g => g.StartsWith('-'));
        if (strayFlag is not null)
        {
            return ParseResult.ForError(
                $"--expected-scope must be the last argument on the command line; found \"{strayFlag}\" after it, " +
                "which looks like a flag, not a glob. Put --diff-scope <old-dir> <new-dir> --expected-scope " +
                "<glob> [<glob> ...] in exactly that order.");
        }

        return ParseResult.ForDiffScope(new DiffScopeRequest(oldDir, newDir, globs));
    }

    private static ParseResult ParseVerify(string[] args)
    {
        var reportOnly = args.Any(static a => a is "--report-only" or "-ReportOnly");

        var dumpFailuresFlagIndex = Array.IndexOf(args, "--dump-failures");
        string? dumpFailuresPath = null;
        if (dumpFailuresFlagIndex >= 0)
        {
            if (dumpFailuresFlagIndex + 1 >= args.Length)
            {
                return ParseResult.ForError("--dump-failures requires a file path argument.");
            }
            dumpFailuresPath = args[dumpFailuresFlagIndex + 1];
        }

        // dumpFailuresFlagIndex is -1 when --dump-failures is absent. The index math that
        // skips the path argument following --dump-failures must be conditioned on the flag
        // actually being present: comparing `i != dumpFailuresFlagIndex + 1` unconditionally
        // means that when the flag is absent (index -1, +1 == 0), the predicate becomes
        // `i != 0` and silently drops args[0] on every invocation -- which is exactly the
        // positional baseline-directory argument. See CliTests for the regression case.
        var positionalArgs = args
            .Where((a, i) => a is not ("--report-only" or "-ReportOnly" or "--dump-failures") &&
                              (dumpFailuresFlagIndex < 0 || i != dumpFailuresFlagIndex + 1))
            .ToArray();

        var baselineDir = positionalArgs.Length > 0 ? positionalArgs[0] : null;
        return ParseResult.ForVerify(new VerifyRequest(reportOnly, dumpFailuresPath, baselineDir));
    }
}
