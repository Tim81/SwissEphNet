using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace SwissEphNet.Conformance.Tests.Corpus;

/// <summary>
/// Reads setest/t.exp: an indentation-decorated (but not indentation-driven --
/// see below) nested TESTSUITE / TESTCASE / ITERATION format with "key: value
/// # comment" lines.
/// </summary>
/// <remarks>
/// Ported from the reference reader (external/swisseph/setest/reader.c). The
/// upstream reader does not actually use indentation to determine nesting: a
/// line is a section marker only if, after trimming, it equals "TESTSUITE",
/// "TESTCASE", or "ITERATION" exactly; every other non-blank, non-comment line
/// is a "name: value" pair attached to whichever section is currently
/// innermost. Indentation in the file is purely cosmetic. This reader
/// reproduces that: it does not track column position at all.
///
/// Strictness: this reader throws (does not skip) on a data line under an open
/// section that isn't blank, isn't a comment, and doesn't parse as
/// "name: value" -- a corpus of this size should never silently lose rows.
/// </remarks>
public static class ExpReader
{
    public static ExpDocument Read(string path)
    {
        using var reader = new StreamReader(path);
        return Read(reader, path);
    }

    public static ExpDocument Read(TextReader textReader, string sourceName)
    {
        var header = new Dictionary<string, string>(StringComparer.Ordinal);
        var suites = new List<ExpTestSuite>();

        ExpFields? suiteFields = null;
        List<ExpTestCase>? suiteTestCases = null;

        ExpFields? testCaseFields = null;
        List<ExpIteration>? testCaseIterations = null;

        ExpFields? iterationFields = null;

        var lineNumber = 0;
        string? rawLine;

        void FlushIteration()
        {
            if (iterationFields is null)
            {
                return;
            }

            var id = RequireId(iterationFields, "ITERATION", sourceName);
            testCaseIterations!.Add(new ExpIteration { Id = id, Fields = iterationFields });
            iterationFields = null;
        }

        void FlushTestCase()
        {
            FlushIteration();
            if (testCaseFields is null)
            {
                return;
            }

            var id = RequireId(testCaseFields, "TESTCASE", sourceName);
            var descr = testCaseFields.TryGetRawString("section-descr");
            suiteTestCases!.Add(new ExpTestCase
            {
                Id = id,
                Description = descr,
                Fields = testCaseFields,
                Iterations = testCaseIterations!,
            });
            testCaseFields = null;
            testCaseIterations = null;
        }

        void FlushSuite()
        {
            FlushTestCase();
            if (suiteFields is null)
            {
                return;
            }

            var id = RequireId(suiteFields, "TESTSUITE", sourceName);
            var descr = suiteFields.TryGetRawString("section-descr");
            suites.Add(new ExpTestSuite
            {
                Id = id,
                Description = descr,
                Fields = suiteFields,
                TestCases = suiteTestCases!,
            });
            suiteFields = null;
            suiteTestCases = null;
        }

        while ((rawLine = textReader.ReadLine()) is not null)
        {
            lineNumber++;
            var trimmed = rawLine.Trim();

            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed[0] == '#')
            {
                continue;
            }

            if (trimmed == "TESTSUITE")
            {
                FlushSuite();
                suiteFields = new ExpFields { LineNumber = lineNumber };
                suiteTestCases = [];
                continue;
            }

            if (trimmed == "TESTCASE")
            {
                if (suiteFields is null)
                {
                    throw new FormatException(
                        $"{sourceName}:{lineNumber}: TESTCASE found before any TESTSUITE was opened.");
                }

                FlushTestCase();
                testCaseFields = new ExpFields { LineNumber = lineNumber };
                testCaseIterations = [];
                continue;
            }

            if (trimmed == "ITERATION")
            {
                if (testCaseFields is null)
                {
                    throw new FormatException(
                        $"{sourceName}:{lineNumber}: ITERATION found before any TESTCASE was opened.");
                }

                FlushIteration();
                iterationFields = new ExpFields { LineNumber = lineNumber };
                continue;
            }

            // Ordinary "name: value [# comment]" line, attached to the
            // innermost currently-open section (iteration > testcase > suite >
            // file header).
            var colonIndex = rawLine.IndexOf(':');
            if (colonIndex < 0)
            {
                throw new FormatException(
                    $"{sourceName}:{lineNumber}: unparseable line (no ':' found and not blank/comment/section marker): '{rawLine}'");
            }

            var name = rawLine[..colonIndex].Trim();
            if (name.Length == 0)
            {
                throw new FormatException($"{sourceName}:{lineNumber}: empty field name in line: '{rawLine}'");
            }

            var value = rawLine[(colonIndex + 1)..].TrimStart();
            value = value.TrimEnd('\r');

            var target = iterationFields ?? testCaseFields ?? suiteFields;
            if (target is not null)
            {
                target.Set(name, value);
            }
            else
            {
                header[name] = value;
            }
        }

        FlushSuite();

        return new ExpDocument { Header = header, TestSuites = suites };
    }

    private static int RequireId(ExpFields fields, string sectionKind, string sourceName)
    {
        var raw = fields.TryGetRawString("section-id");
        if (raw is null)
        {
            throw new FormatException(
                $"{sourceName}:{fields.LineNumber}: {sectionKind} block has no 'section-id' field.");
        }

        // Iteration section-id lines carry a trailing "#1.1.1"-style comment;
        // truncate it the same way GetInt would (this must not call
        // fields.Set again -- that would double-count the line in RawLineCount).
        if (!int.TryParse(TruncateForId(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            throw new FormatException(
                $"{sourceName}:{fields.LineNumber}: {sectionKind} 'section-id' value '{raw}' is not an integer.");
        }

        return id;
    }

    private static string TruncateForId(string raw)
    {
        var hashIndex = raw.IndexOf('#');
        return (hashIndex >= 0 ? raw[..hashIndex] : raw).Trim();
    }
}
