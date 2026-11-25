using System.Collections.Generic;
using System.IO;
using System.Linq;
using SwissEphNet.Conformance.Tests.Corpus;
using SwissEphNet.Conformance.Tests.Dispatch;
using Xunit;

namespace SwissEphNet.Conformance.Tests.KnownFail;

/// <summary>
/// The value-level known-fail gate: an accepted VALUE-MISMATCH row must keep the same set of
/// mismatching fields, each at its pinned port value within setest's own tolerance, ints exact,
/// strings exact modulo the ephemeris directory. See <see cref="PinGate"/>.
/// </summary>
public class PinGateTests
{
    private const string EpheA = @"C:\work\swisseph\ephe";
    private const string EpheB = "/home/runner/swisseph/ephe";
    private static readonly IterationKey Key = new(1, 1, 7);

    // xx[0] in suite 1 testcase 1 is an angle in degrees: tolerances.c gives it 3e-8. An unclassified
    // name ("zz") falls back to the t.fix precision, here 1e-9 overall and 1e-5 for xx*[2].
    private static readonly Precision Fix = new(1e-9, [1e-6, 1e-6, 1e-5, 1e-6, 1e-6, 1e-6]);

    private static FieldMismatch D(string name, string expected, string actual) =>
        new(name, expected, actual, double.Parse(expected, System.Globalization.CultureInfo.InvariantCulture) - double.Parse(actual, System.Globalization.CultureInfo.InvariantCulture));

    private static IterationResult Row(params FieldMismatch[] mismatches) =>
        new(Key, "suite", "case", OutcomeKind.ValueMismatch, null, mismatches, new PinContext(0, Fix));

    private static PinnedField Pin(string name, FieldKind kind, string value) => new(Key, name, kind, value);

    [Fact]
    public void RowThatStillReproducesItsPins_HasNoViolations()
    {
        var row = Row(D("xx[0]", "10", "10.001"), new FieldMismatch("rc", "0", "-1", null, FieldKind.Int));
        var pins = new[] { Pin("xx[0]", FieldKind.Double, "10.001"), Pin("rc", FieldKind.Int, "-1") };

        Assert.Empty(PinGate.Check(row, pins, EpheA));
    }

    [Fact]
    public void ADoubleMovingWithinSetestsTolerance_IsNotAViolation_ButBeyondItIs()
    {
        Assert.True(Tolerances.tolerance_for("xx[0]", 10.001, 1, 1, 0) is > 1e-8 and < 1e-6, "test premise: xx[0] is a 3e-8 degree angle in suite 1.1");
        var pins = new[] { Pin("xx[0]", FieldKind.Double, "10.001") };

        Assert.Empty(PinGate.Check(Row(D("xx[0]", "10", "10.00100001")), pins, EpheA)); // 1e-8 away

        var moved = PinGate.Check(Row(D("xx[0]", "10", "10.002")), pins, EpheA);
        var v = Assert.Single(moved);
        Assert.Equal("xx[0]", v.Field);
        Assert.Contains("1.1.7", v.Message);
        Assert.Contains("'xx[0]'", v.Message);
        Assert.Contains("pinned 10.001", v.Message);
        Assert.Contains("actual 10.002", v.Message);
    }

    [Fact]
    public void AnUnclassifiedName_FallsBackToTheT_FixPrecision_PerComponentForXxNames()
    {
        Assert.True(Tolerances.tolerance_for("zz", 1.0, 1, 1, 0) < 0, "test premise: 'zz' is not classified by tolerances.c");
        var row = Row(D("zz", "1", "2"));
        Assert.Equal(1e-9, PinGate.ToleranceFor(row, "zz", 2.0));
        Assert.Equal(1e-5, PinGate.ToleranceFor(row, "xxzz[2]", 2.0));
        Assert.Equal(1e-9, PinGate.ToleranceFor(row, "xxzz[7]", 2.0)); // index >= 6: overall precision

        // 1e-7 away: inside 1e-5 for the xx component, outside 1e-9 for the overall precision.
        var pins = new[] { Pin("xxzz[2]", FieldKind.Double, "2"), Pin("zz", FieldKind.Double, "2") };
        var violations = PinGate.Check(Row(D("xxzz[2]", "1", "2.0000001"), D("zz", "1", "2.0000001")), pins, EpheA);
        Assert.Equal(["zz"], violations.Select(v => v.Field));
    }

    [Fact]
    public void AFieldJoiningTheMismatchSet_IsAViolation_EvenWhenTheWorstFieldIsUnchanged()
    {
        var row = Row(D("xx[0]", "10", "10.001"), D("xx[1]", "5", "5.5"));
        var pins = new[] { Pin("xx[0]", FieldKind.Double, "10.001") };

        var v = Assert.Single(PinGate.Check(row, pins, EpheA));
        Assert.Equal("xx[1]", v.Field);
        Assert.Contains("not in the pinned mismatch set", v.Message);
        Assert.Contains("5.5", v.Message);
    }

    [Fact]
    public void AFieldLeavingTheMismatchSet_IsAViolation()
    {
        var row = Row(D("xx[0]", "10", "10.001"));
        var pins = new[] { Pin("xx[0]", FieldKind.Double, "10.001"), Pin("xx[1]", FieldKind.Double, "5.5") };

        var v = Assert.Single(PinGate.Check(row, pins, EpheA));
        Assert.Equal("xx[1]", v.Field);
        Assert.Contains("no longer mismatches", v.Message);
        Assert.Contains("5.5", v.Message);
    }

    [Fact]
    public void AnIntField_MustBeExact()
    {
        var pins = new[] { Pin("rc", FieldKind.Int, "-1") };

        Assert.Empty(PinGate.Check(Row(new FieldMismatch("rc", "0", "-1", null, FieldKind.Int)), pins, EpheA));
        var v = Assert.Single(PinGate.Check(Row(new FieldMismatch("rc", "0", "-2", null, FieldKind.Int)), pins, EpheA));
        Assert.Contains("pinned -1, actual -2", v.Message);
    }

    [Fact]
    public void ASerrPath_IsNormalisedToAPlaceholder_SoTheMachineDoesNotMatter()
    {
        const string onWindows = @"SwissEph file 'sepl_30.se1' not found in PATH 'C:\work\swisseph\ephe/' \nusing Moshier eph.; ";
        const string onLinux = @"SwissEph file 'sepl_30.se1' not found in PATH '/home/runner/swisseph/ephe/' \nusing Moshier eph.; ";
        var pinText = PinGate.PinText(new FieldMismatch("serr", "", onWindows, null, FieldKind.String), EpheA);
        Assert.Contains("PATH '<ephe>/'", pinText);
        Assert.DoesNotContain("work", pinText);

        var pins = new[] { Pin("serr", FieldKind.String, pinText) };
        Assert.Empty(PinGate.Check(Row(new FieldMismatch("serr", "", onWindows, null, FieldKind.String)), pins, EpheA));

        // The same message produced where the ephemeris directory is spelled differently: the pin
        // made on one machine holds on another once each normalises its own directory.
        var onOther = PinGate.PinText(new FieldMismatch("serr", "", onLinux, null, FieldKind.String), EpheB);
        Assert.Equal(pinText, onOther);
    }

    [Fact]
    public void ATrailingSeparatorOnTheEpheDirectory_GivesTheSamePin()
    {
        // SWISSEPH_CONFORMANCE_EPHE=...\ephe\ makes the library's own "/" follow a backslash, so serr
        // reads '...\ephe\/'. The pin is the same as for the separator-free spelling.
        const string plain = @"SwissEph file 'sepl_30.se1' not found in PATH 'C:\work\swisseph\ephe/' 
using Moshier eph.; ";
        const string trailing = @"SwissEph file 'sepl_30.se1' not found in PATH 'C:\work\swisseph\ephe\/' 
using Moshier eph.; ";
        var reference = PinGate.PinText(new FieldMismatch("serr", "", plain, null, FieldKind.String), EpheA);
        Assert.Contains("PATH '<ephe>/'", reference);

        Assert.Equal(reference, PinGate.PinText(new FieldMismatch("serr", "", trailing, null, FieldKind.String), EpheA + @"\"));
        Assert.Equal(reference, PinGate.PinText(new FieldMismatch("serr", "", trailing, null, FieldKind.String), EpheA + "/"));
        Assert.Equal(reference, PinGate.PinText(new FieldMismatch("serr", "", plain, null, FieldKind.String), EpheA + @"\"));

        var pins = new[] { Pin("serr", FieldKind.String, reference) };
        Assert.Empty(PinGate.Check(Row(new FieldMismatch("serr", "", trailing, null, FieldKind.String)), pins, EpheA + @"\"));
    }

    [Fact]
    public void ARuntimeSelfCheck_PinsBothOperands_NotJustTheFirst()
    {
        // suite_10_solcross.c:31 CHECK_EQUALS_D(xcross, xx[0]): the first operand is the t.exp input,
        // the second the value the port computed. Both are pinned, the second as "xcross#2".
        var selfCheck = new FieldMismatch("xcross", "36.7645140093774", "30", 6.7645140093774, FieldKind.Double, SelfCheck: true);
        var operands = PinGate.Operands(selfCheck, EpheA);
        Assert.Equal(["xcross", "xcross#2"], operands.Select(o => o.Name).ToArray());
        Assert.Equal(["30", "36.7645140093774"], operands.Select(o => o.Text).ToArray());

        var pins = new[] { Pin("xcross", FieldKind.Double, "30"), Pin("xcross#2", FieldKind.Double, "36.7645140093774") };
        Assert.Empty(PinGate.Check(Row(selfCheck), pins, EpheA));

        // The computed operand moving beyond setest's tolerance is a violation. Before both were
        // pinned, only the input (which cannot move) was.
        var moved = new FieldMismatch("xcross", "36.7745140093774", "30", 6.7745140093774, FieldKind.Double, SelfCheck: true);
        var v = Assert.Single(PinGate.Check(Row(moved), pins, EpheA));
        Assert.Equal("xcross#2", v.Field);
        Assert.Contains("pinned 36.7645140093774, actual 36.7745140093774", v.Message);
    }

    [Fact]
    public void ANewRuntimeSelfCheckFailure_IsLabelledAsOne_NotAsAT_ExpMismatch()
    {
        var selfCheck = new FieldMismatch("xx[1]", "0", "-5.05", 5.05, FieldKind.Double, SelfCheck: true);
        var v = Assert.Single(PinGate.Check(Row(selfCheck), [Pin("rc", FieldKind.Int, "4")], EpheA), x => x.Field == "xx[1]");
        Assert.Contains("runtime self-check", v.Message);
        Assert.DoesNotContain("t.exp 0", v.Message);
        Assert.Contains("first -5.05", v.Message);
        Assert.Contains("second 0", v.Message);
    }

    [Fact]
    public void AChangedSerrText_IsAViolation_AndNamesBothValues()
    {
        const string was = "SwissEph file 'sepl_30.se1' not found in PATH 'C:\\work\\swisseph\\ephe/'";
        const string now = "SwissEph file 'seplm36.se1' not found in PATH 'C:\\work\\swisseph\\ephe/'";
        var pins = new[] { Pin("serr", FieldKind.String, PinGate.PinText(new FieldMismatch("serr", "", was, null, FieldKind.String), EpheA)) };

        var v = Assert.Single(PinGate.Check(Row(new FieldMismatch("serr", "", now, null, FieldKind.String)), pins, EpheA));
        Assert.Contains("serr", v.Message);
        Assert.Contains("sepl_30.se1", v.Message);
        Assert.Contains("seplm36.se1", v.Message);
    }

    [Fact]
    public void ARowWithoutPins_IsAViolation()
    {
        var v = Assert.Single(PinGate.Check(Row(D("xx[0]", "10", "10.001")), null, EpheA));
        Assert.Contains("no pinned fields", v.Message);
    }

    [Fact]
    public void ARepeatedFieldName_IsComparedOccurrenceByOccurrence()
    {
        var row = Row(D("xx[0]", "10", "10.001"), D("xx[0]", "11", "11.001"));
        var pins = new[] { Pin("xx[0]", FieldKind.Double, "10.001"), Pin("xx[0]", FieldKind.Double, "11.5") };

        var v = Assert.Single(PinGate.Check(row, pins, EpheA));
        Assert.Contains("pinned 11.5, actual 11.001", v.Message);
        Assert.Contains("mismatches 1 time(s) now, 2 time(s) pinned", Assert.Single(PinGate.Check(Row(D("xx[0]", "10", "10.001")), pins, EpheA)).Message);
    }

    [Fact]
    public void Report_FlagsPinViolations_AndOrphanPins_AndPasses_WhenTheyMatch()
    {
        var knownFail = new Dictionary<IterationKey, KnownFailEntry>
        {
            [Key] = new(Key, FailureCategory.ValueMismatch, MagnitudeKey.Compute(Row(D("xx[0]", "10", "10.0015")).Mismatches), "r"),
        };
        var good = new Dictionary<IterationKey, IReadOnlyList<PinnedField>> { [Key] = [Pin("xx[0]", FieldKind.Double, "10.0015")] };

        var ok = ConformanceReport.Build([Row(D("xx[0]", "10", "10.0015"))], knownFail, good, EpheA);
        Assert.Empty(ok.PinViolations);
        Assert.True(ok.Passed);

        // Same magnitude key (the worst field is still decade -4) but a pinned value moved: the
        // category and magnitude_key comparison alone would have accepted this.
        var moved = ConformanceReport.Build([Row(D("xx[0]", "10", "10.0025"))], knownFail, good, EpheA);
        Assert.Empty(moved.Drifted);
        Assert.Single(moved.PinViolations);
        Assert.False(moved.Passed);
        Assert.Contains("pinned 10.0015", moved.FormatSummary());

        var orphanKey = new IterationKey(9, 9, 9);
        var withOrphan = new Dictionary<IterationKey, IReadOnlyList<PinnedField>>(good)
        {
            [orphanKey] = [new PinnedField(orphanKey, "xx[0]", FieldKind.Double, "1")],
        };
        var orphaned = ConformanceReport.Build([Row(D("xx[0]", "10", "10.0015"))], knownFail, withOrphan, EpheA);
        Assert.Contains(orphaned.PinViolations, v => v.Key == orphanKey);

        // No pins supplied (the generator's own summary pass): the value-level check is skipped.
        Assert.Empty(ConformanceReport.Build([Row(D("xx[0]", "10", "10.0025"))], knownFail).PinViolations);
    }

    [Fact]
    public void PinList_RoundTrips_IncludingEscapedStrings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pins-{System.Guid.NewGuid():N}.tsv");
        try
        {
            var text = PinGate.PinText(new FieldMismatch("serr", "", "tab\there back\\slash \\n", null, FieldKind.String), EpheA);
            Assert.DoesNotContain('\t', text);
            PinList.Save(path, [Pin("xx[0]", FieldKind.Double, "1.5"), Pin("serr", FieldKind.String, text)]);

            var loaded = PinList.Load(path);
            var pins = Assert.Single(loaded).Value;
            Assert.Equal(["xx[0]", "serr"], pins.Select(p => p.Name));
            Assert.Equal(text, pins[1].Value);
            Assert.Equal(FieldKind.String, pins[1].Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ThePinFile_CoversExactlyTheValueMismatchRowsOfKnownFail_AndEveryRowHasPins()
    {
        var knownFail = KnownFailList.Load(Path.Combine(RepoLocator.ConformanceDataDir, "known-fail.tsv"));
        var pins = PinList.Load(Path.Combine(RepoLocator.ConformanceDataDir, "known-fail-pins.tsv"));
        var valueMismatch = knownFail.Where(kv => kv.Value.Category == FailureCategory.ValueMismatch).Select(kv => kv.Key).ToHashSet();

        Assert.Equal(valueMismatch.Count, pins.Count);
        Assert.Empty(valueMismatch.Except(pins.Keys));
        Assert.Empty(pins.Keys.Except(valueMismatch));
    }
}
