using System.Globalization;
using System.Text;
using SA.Motion.Legs;
using SA.Motion.Tuning;
using Xunit;

namespace SA.Motion.Tests;

public class LegProfileTextTests
{
    static readonly string[] KnownLines =
    {
        "Weight = 0",
        "WeightHalfLife = 0.125",
        "PlantHeight = 0.25",
        "ReleaseHeight = 0.375",
        "PlantRise = 0.5",
        "ReleaseRise = 0.625",
        "PlantSpeed = 0.75",
        "PinRadius = 0.875",
        "ReleaseDistance = 1",
        "BlendSeconds = 1.125",
        "TurnPivot = 1.25",
        "HeelRoll = 1.375",
        "BallRoll = 1.5",
        "RollBand = 1.625",
        "SlopeAlign = 1.75",
        "MaxSlopeAlign = 1.875",
        "MaxWalkableSlope = 2",
        "TerrainFollow = 2.125",
        "TerrainHalfLife = 2.25",
        "MaxStepHeight = 2.375",
        "Softness = 2.5",
        "MaxStretch = 2.625",
        "KneeFollow = 2.75",
        "PelvisWeight = 2.875",
        "PelvisDrop = 3",
        "PelvisRaise = 3.125",
        "PelvisHalfLife = 3.25"
    };

    static readonly string[] GarbagePieces =
    {
        "Weight", "PelvisHalfLife", "MaxStretch", " = ", "=", "==", " ", "\t", "\n", "\r\n", "\r", "\0",
        "0", "1", "0.5", "-1", "1e9", "1e-9", "1e999", "NaN", "Infinity", "-", "+", ".", "e", "x", ",",
        "\U000000e9", "\ud83d", "\U0000feff", "\U00002028"
    };

    public static IEnumerable<object[]> Presets() => Enum.GetValues<LegPreset>().Select(preset => new object[] { preset });

    static int Bits(float value) => BitConverter.SingleToInt32Bits(value);

    static string Invariant(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    static string Name(LegParam field) => LegParamsSpec.Text(field).Name;

    static LegParams Filled(Func<ParamBounds, float> choose)
    {
        var p = default(LegParams);
        for (var i = 0; i < LegParamsSpec.Count; i++)
        {
            var field = (LegParam)i;
            p = LegParamsSpec.With(p, field, choose(LegParamsSpec.Bounds(field)));
        }
        return p;
    }

    static LegParams Known()
    {
        var p = default(LegParams);
        for (var i = 0; i < LegParamsSpec.Count; i++)
            p = LegParamsSpec.With(p, (LegParam)i, i / 8f);
        return p;
    }

    static LegParams Midway() => Filled(bounds => bounds.Minimum + (bounds.Maximum - bounds.Minimum) * 0.4f);

    static LegParams Poisoned() => Filled(_ => float.NaN);

    static LegParams Sampled(Random random) => Filled(bounds =>
    {
        var scale = random.Next(2) == 0 ? 1.0 : Math.Pow(10.0, -random.Next(1, 10));
        var value = bounds.Minimum + (float)(random.NextDouble() * scale) * (bounds.Maximum - bounds.Minimum);
        return Math.Min(Math.Max(value, bounds.Minimum), bounds.Maximum);
    });

    static LegParams Parse(string? text, LegParams fallback, out List<string> problems)
    {
        problems = new List<string>();
        return LegProfileText.Read(text, fallback, problems);
    }

    static void SameBits(LegParams expected, LegParams actual)
    {
        for (var i = 0; i < LegParamsSpec.Count; i++)
        {
            var field = (LegParam)i;
            var want = LegParamsSpec.Get(expected, field);
            var got = LegParamsSpec.Get(actual, field);
            Assert.True(Bits(want) == Bits(got), Name(field) + " expected " + Invariant(want) + " but was " + Invariant(got));
        }
    }

    static void EveryFieldIsFiniteAndInRange(LegParams p)
    {
        for (var i = 0; i < LegParamsSpec.Count; i++)
        {
            var field = (LegParam)i;
            var bounds = LegParamsSpec.Bounds(field);
            Assert.InRange(LegParamsSpec.Get(p, field), bounds.Minimum, bounds.Maximum);
        }
    }

    static string LineNumberPrefix(int line) => "line " + line.ToString(CultureInfo.InvariantCulture) + ": ";

    static void AssertRejectedAlone(string text, int line, string reason, string? mention = null)
    {
        var fallback = Midway();
        var result = Parse(text, fallback, out var problems);
        var problem = Assert.Single(problems);
        Assert.StartsWith(LineNumberPrefix(line), problem, StringComparison.Ordinal);
        Assert.Contains(reason, problem);
        if (mention != null)
            Assert.Contains(mention, problem);
        SameBits(fallback, result);
    }

    static string Shuffled(string text, Random random)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (var i = lines.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (lines[i], lines[j]) = (lines[j], lines[i]);
        }
        return string.Join("\n", lines) + "\n";
    }

    static string Expected(LegParams p)
    {
        var text = new StringBuilder();
        for (var i = 0; i < LegParamsSpec.Count; i++)
        {
            var field = (LegParam)i;
            text.Append(Name(field)).Append(" = ").Append(Invariant(LegParamsSpec.Get(p, field))).Append('\n');
        }
        return text.ToString();
    }

    static int NonBlankLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Count(line => line.Trim().Length > 0);

    [Fact]
    public void FixtureFallbackDiffersFromEveryPresetInEveryField()
    {
        var midway = Midway();
        foreach (var preset in Enum.GetValues<LegPreset>())
            for (var i = 0; i < LegParamsSpec.Count; i++)
            {
                var field = (LegParam)i;
                Assert.NotEqual(Bits(LegParamsSpec.Get(LegParams.Preset(preset), field)), Bits(LegParamsSpec.Get(midway, field)));
            }
    }

    [Fact]
    public void WriteEmitsNameEqualsValueForEveryFieldInEnumOrderEachLineEndingInLineFeed()
    {
        Assert.Equal(LegParamsSpec.Count, KnownLines.Length);
        Assert.Equal(string.Join("\n", KnownLines) + "\n", LegProfileText.Write(Known()));
    }

    [Fact]
    public void WriteUsesTheShortestTextThatReadsBack()
    {
        var p = LegParamsSpec.With(LegParamsSpec.With(default(LegParams), LegParam.PlantHeight, 0.1f), LegParam.PelvisRaise, 0.035f);
        var text = LegProfileText.Write(p);
        Assert.Contains("PlantHeight = 0.1\n", text);
        Assert.Contains("PelvisRaise = 0.035\n", text);
    }

    [Fact]
    public void WriteFormatsEachValueWithRoundTripInvariantText()
    {
        var p = Sampled(new Random(11));
        var lines = LegProfileText.Write(p).Split('\n');
        Assert.Equal(LegParamsSpec.Count + 1, lines.Length);
        Assert.Equal(string.Empty, lines[LegParamsSpec.Count]);
        for (var i = 0; i < LegParamsSpec.Count; i++)
        {
            var field = (LegParam)i;
            Assert.Equal(Name(field) + " = " + LegParamsSpec.Get(p, field).ToString("R", CultureInfo.InvariantCulture), lines[i]);
        }
    }

    [Fact]
    public void WriteIsTotalAndAlwaysEmitsOneLinePerField()
    {
        var profiles = new[]
        {
            default(LegParams),
            Filled(_ => float.NaN),
            Filled(_ => float.PositiveInfinity),
            Filled(_ => float.NegativeInfinity),
            Filled(_ => float.MaxValue),
            Filled(_ => -float.MaxValue),
            Filled(_ => float.Epsilon)
        };
        foreach (var profile in profiles)
        {
            var text = LegProfileText.Write(profile);
            Assert.EndsWith("\n", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\r", text);
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(LegParamsSpec.Count, lines.Length);
            for (var i = 0; i < LegParamsSpec.Count; i++)
                Assert.StartsWith(Name((LegParam)i) + " = ", lines[i], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TextIsTheSameUnderAnyCurrentCulture()
    {
        var odd = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        odd.NumberFormat.NumberDecimalSeparator = ",";
        odd.NumberFormat.NumberGroupSeparator = ".";
        var profile = Sampled(new Random(7));
        var expectedText = Expected(profile);
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = odd;
        try
        {
            Assert.Equal(expectedText, LegProfileText.Write(profile));
            var result = Parse(expectedText, Poisoned(), out var problems);
            Assert.Empty(problems);
            SameBits(profile, result);
            AssertRejectedAlone("Weight = 0,5\n", 1, "not a number", "Weight");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [MemberData(nameof(Presets))]
    public void EveryPresetReadsBackBitForBit(LegPreset preset)
    {
        var profile = LegParams.Preset(preset);
        var result = Parse(LegProfileText.Write(profile), Poisoned(), out var problems);
        Assert.Empty(problems);
        SameBits(profile, result);
    }

    [Fact]
    public void OneHundredSeededRandomInRangeProfilesReadBackBitForBit()
    {
        var random = new Random(20261101);
        for (var n = 0; n < 100; n++)
        {
            var profile = Sampled(random);
            EveryFieldIsFiniteAndInRange(profile);
            var result = Parse(LegProfileText.Write(profile), Poisoned(), out var problems);
            Assert.Empty(problems);
            SameBits(profile, result);
        }
    }

    [Fact]
    public void ValuesExactlyOnEveryBoundAreAccepted()
    {
        foreach (var profile in new[] { Filled(bounds => bounds.Minimum), Filled(bounds => bounds.Maximum) })
        {
            var result = Parse(LegProfileText.Write(profile), Poisoned(), out var problems);
            Assert.Empty(problems);
            SameBits(profile, result);
        }
    }

    [Fact]
    public void NegativeZeroKeepsItsSignBit()
    {
        var profile = Filled(bounds => bounds.Minimum == 0f ? -0f : bounds.Minimum);
        var result = Parse(LegProfileText.Write(profile), Poisoned(), out var problems);
        Assert.Empty(problems);
        SameBits(profile, result);
        Assert.Equal(int.MinValue, Bits(profile.Weight));
        Assert.Equal(int.MinValue, Bits(result.Weight));
    }

    [Fact]
    public void ReadDoesNotApplyTheCrossFieldOrderingOfSanitize()
    {
        var inverted = LegParams.Standard;
        inverted.PlantHeight = 0.2f;
        inverted.ReleaseHeight = 0.1f;
        inverted.PinRadius = 0.9f;
        inverted.ReleaseDistance = 0.2f;
        var result = Parse(LegProfileText.Write(inverted), Poisoned(), out var problems);
        Assert.Empty(problems);
        SameBits(inverted, result);
    }

    [Theory]
    [InlineData("Bogus = 1")]
    [InlineData("weight = 0.5")]
    [InlineData("WEIGHT = 0.5")]
    [InlineData("Weight2 = 0.5")]
    [InlineData("Weigh = 0.5")]
    [InlineData("Plant Height = 0.1")]
    [InlineData("Group.Weight = 0.5")]
    public void UnknownNameIsOneProblemAndKeepsTheFallback(string line)
    {
        var name = line.Substring(0, line.IndexOf('=')).Trim();
        AssertRejectedAlone(line + "\n", 1, "unknown name", name);
    }

    [Theory]
    [InlineData("Weight")]
    [InlineData("Weight 0.5")]
    [InlineData("Weight: 0.5")]
    [InlineData("Weight - 0.5")]
    [InlineData("= 0.5")]
    [InlineData("=")]
    [InlineData("0.5")]
    [InlineData("just words")]
    public void MalformedLineIsOneProblemAndKeepsTheFallback(string line)
    {
        AssertRejectedAlone(line + "\n", 1, "expected Name = value");
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0.5.5")]
    [InlineData("1e")]
    [InlineData("--1")]
    [InlineData("0x10")]
    [InlineData("0,5")]
    [InlineData("1,000")]
    [InlineData("0.5f")]
    [InlineData("1 2")]
    [InlineData(".")]
    [InlineData("0.5%")]
    [InlineData("= 0.5")]
    [InlineData("0.5 = 0.6")]
    [InlineData("\U00000665")]
    public void BadNumberIsOneProblemAndKeepsTheFallbackForEveryField(string value)
    {
        for (var i = 0; i < LegParamsSpec.Count; i++)
        {
            var name = Name((LegParam)i);
            AssertRejectedAlone(name + " = " + value + "\n", 1, "not a number", name);
        }
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("nan")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("+Infinity")]
    [InlineData("1e999")]
    [InlineData("-1e999")]
    public void NonFiniteValueIsOneProblemAndKeepsTheFallbackForEveryField(string value)
    {
        for (var i = 0; i < LegParamsSpec.Count; i++)
        {
            var name = Name((LegParam)i);
            AssertRejectedAlone(name + " = " + value + "\n", 1, "not finite", name);
        }
    }

    [Fact]
    public void ValueJustOutsideEitherBoundIsOneProblemAndKeepsTheFallbackForEveryField()
    {
        for (var i = 0; i < LegParamsSpec.Count; i++)
        {
            var field = (LegParam)i;
            var bounds = LegParamsSpec.Bounds(field);
            var outside = new[]
            {
                MathF.BitDecrement(bounds.Minimum),
                MathF.BitIncrement(bounds.Maximum),
                bounds.Minimum - 1f,
                bounds.Maximum + 1f,
                -float.MaxValue,
                float.MaxValue
            };
            foreach (var value in outside)
                AssertRejectedAlone(Name(field) + " = " + Invariant(value) + "\n", 1, "outside", Name(field));
        }
    }

    [Fact]
    public void DuplicateNameIsOneProblemAndTheFirstValueStands()
    {
        var fallback = Midway();
        var result = Parse("Weight = 0.25\nWeight = 0.75\n", fallback, out var problems);
        var problem = Assert.Single(problems);
        Assert.StartsWith(LineNumberPrefix(2), problem, StringComparison.Ordinal);
        Assert.Contains("duplicate", problem);
        Assert.Contains("Weight", problem);
        SameBits(LegParamsSpec.With(fallback, LegParam.Weight, 0.25f), result);
    }

    [Fact]
    public void DuplicateWithAnUnreadableValueIsStillOneProblem()
    {
        var fallback = Midway();
        var result = Parse("Weight = 0.25\nWeight = abc\n", fallback, out var problems);
        var problem = Assert.Single(problems);
        Assert.StartsWith(LineNumberPrefix(2), problem, StringComparison.Ordinal);
        Assert.Contains("duplicate", problem);
        SameBits(LegParamsSpec.With(fallback, LegParam.Weight, 0.25f), result);
    }

    [Fact]
    public void EveryRepeatOfAKeyIsItsOwnProblem()
    {
        var fallback = Midway();
        var result = Parse("Weight = 0.25\nWeight = 0.5\nWeight = 0.75\n", fallback, out var problems);
        Assert.Equal(2, problems.Count);
        Assert.StartsWith(LineNumberPrefix(2), problems[0], StringComparison.Ordinal);
        Assert.StartsWith(LineNumberPrefix(3), problems[1], StringComparison.Ordinal);
        Assert.Contains("duplicate", problems[0]);
        Assert.Contains("duplicate", problems[1]);
        SameBits(LegParamsSpec.With(fallback, LegParam.Weight, 0.25f), result);
    }

    [Fact]
    public void ExtraLineAfterAFullProfileIsOneDuplicateProblem()
    {
        var profile = Sampled(new Random(3));
        var result = Parse(LegProfileText.Write(profile) + "PelvisHalfLife = 0.5\n", Midway(), out var problems);
        var problem = Assert.Single(problems);
        Assert.StartsWith(LineNumberPrefix(LegParamsSpec.Count + 1), problem, StringComparison.Ordinal);
        Assert.Contains("duplicate", problem);
        SameBits(profile, result);
    }

    [Fact]
    public void RejectedValueDoesNotClaimTheNameForALaterValidLine()
    {
        var fallback = Midway();
        var result = Parse("Weight = abc\nWeight = 0.5\n", fallback, out var problems);
        var problem = Assert.Single(problems);
        Assert.StartsWith(LineNumberPrefix(1), problem, StringComparison.Ordinal);
        Assert.Contains("not a number", problem);
        SameBits(LegParamsSpec.With(fallback, LegParam.Weight, 0.5f), result);
    }

    [Fact]
    public void NullTextReturnsTheFallbackWithOneProblem()
    {
        var fallback = Midway();
        var result = Parse(null, fallback, out var problems);
        var problem = Assert.Single(problems);
        Assert.Contains("null", problem);
        SameBits(fallback, result);
    }

    [Fact]
    public void NullProblemsListIsAcceptedAndStillReadsEveryGoodLine()
    {
        var fallback = Midway();
        var result = LegProfileText.Read("Bogus = 1\nWeight = 0.25\nPlantHeight = nope\n", fallback, null);
        SameBits(LegParamsSpec.With(fallback, LegParam.Weight, 0.25f), result);
        SameBits(fallback, LegProfileText.Read(null, fallback, null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("\r\n\r\n")]
    [InlineData("\r")]
    [InlineData("   \t  \n  \n")]
    [InlineData("\U000000a0\n \U00002003 ")]
    public void EmptyOrBlankTextReadsAsTheFallbackWithoutProblems(string text)
    {
        var fallback = Midway();
        var result = Parse(text, fallback, out var problems);
        Assert.Empty(problems);
        SameBits(fallback, result);
    }

    [Fact]
    public void FieldsMissingFromTheTextKeepTheFallbackWithoutProblems()
    {
        var fallback = Midway();
        var expected = LegParamsSpec.With(LegParamsSpec.With(fallback, LegParam.PlantHeight, 0.1f), LegParam.PelvisDrop, 0.2f);
        var result = Parse("PlantHeight = 0.1\nPelvisDrop = 0.2\n", fallback, out var problems);
        Assert.Empty(problems);
        SameBits(expected, result);
    }

    [Theory]
    [MemberData(nameof(Presets))]
    public void ReversedLinesStillRead(LegPreset preset)
    {
        var profile = LegParams.Preset(preset);
        var lines = LegProfileText.Write(profile).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Array.Reverse(lines);
        var result = Parse(string.Join("\n", lines) + "\n", Poisoned(), out var problems);
        Assert.Empty(problems);
        SameBits(profile, result);
    }

    [Fact]
    public void ShuffledLinesStillRead()
    {
        var random = new Random(99);
        for (var n = 0; n < 50; n++)
        {
            var profile = Sampled(random);
            var result = Parse(Shuffled(LegProfileText.Write(profile), random), Poisoned(), out var problems);
            Assert.Empty(problems);
            SameBits(profile, result);
        }
    }

    [Fact]
    public void CarriageReturnLineFeedLineEndingsStillRead()
    {
        var random = new Random(5);
        for (var n = 0; n < 20; n++)
        {
            var profile = Sampled(random);
            var text = LegProfileText.Write(profile).Replace("\n", "\r\n");
            var result = Parse(text, Poisoned(), out var problems);
            Assert.Empty(problems);
            SameBits(profile, result);
        }
        foreach (var preset in Enum.GetValues<LegPreset>())
        {
            var result = Parse(LegProfileText.Write(LegParams.Preset(preset)).Replace("\n", "\r\n"), Poisoned(), out var problems);
            Assert.Empty(problems);
            SameBits(LegParams.Preset(preset), result);
        }
    }

    [Fact]
    public void LoneCarriageReturnAndMixedLineEndingsStillRead()
    {
        var profile = Sampled(new Random(8));
        var lines = LegProfileText.Write(profile).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var endings = new[] { "\n", "\r\n", "\r" };
        var text = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
            text.Append(lines[i]).Append(endings[i % endings.Length]);
        var result = Parse(text.ToString(), Poisoned(), out var problems);
        Assert.Empty(problems);
        SameBits(profile, result);
    }

    [Fact]
    public void MissingFinalLineFeedStillReads()
    {
        var profile = Sampled(new Random(13));
        var text = LegProfileText.Write(profile).TrimEnd('\n');
        var result = Parse(text, Poisoned(), out var problems);
        Assert.Empty(problems);
        SameBits(profile, result);
    }

    [Theory]
    [InlineData("x", 1)]
    [InlineData("Weight = 0.5\nx", 2)]
    [InlineData("Weight = 0.5\r\nx", 2)]
    [InlineData("Weight = 0.5\rx", 2)]
    public void FinalLineWithoutALineEndingIsStillChecked(string text, int line)
    {
        var fallback = Midway();
        var expected = text.StartsWith("Weight", StringComparison.Ordinal) ? LegParamsSpec.With(fallback, LegParam.Weight, 0.5f) : fallback;
        var result = Parse(text, fallback, out var problems);
        var problem = Assert.Single(problems);
        Assert.StartsWith(LineNumberPrefix(line), problem, StringComparison.Ordinal);
        SameBits(expected, result);
    }

    [Fact]
    public void SurroundingWhitespaceAndBlankLinesAreIgnored()
    {
        var profile = Sampled(new Random(21));
        var lines = LegProfileText.Write(profile).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var text = new StringBuilder("\n  \n");
        for (var i = 0; i < lines.Length; i++)
        {
            var parts = lines[i].Split(" = ");
            switch (i % 4)
            {
                case 0:
                    text.Append(lines[i]);
                    break;
                case 1:
                    text.Append(parts[0]).Append('=').Append(parts[1]);
                    break;
                case 2:
                    text.Append("\t  ").Append(parts[0]).Append("\t=\t").Append(parts[1]).Append("   \t");
                    break;
                default:
                    text.Append(parts[0]).Append("    =    ").Append(parts[1]);
                    break;
            }
            text.Append(i % 3 == 0 ? "\r\n \r\n" : "\n");
        }
        var result = Parse(text.ToString(), Poisoned(), out var problems);
        Assert.Empty(problems);
        SameBits(profile, result);
    }

    [Fact]
    public void ProblemsNameTheirLineAcrossBlankLinesAndEveryKindOfLineEnding()
    {
        var fallback = Midway();
        var text = "Weight = 0.5\r\n\r\nBogus = 1\r\nPlantHeight = 0.1\rWeight2 = 3\nnonsense\n\n  PelvisDrop = 7";
        var result = Parse(text, fallback, out var problems);
        Assert.Equal(4, problems.Count);
        Assert.StartsWith(LineNumberPrefix(3), problems[0], StringComparison.Ordinal);
        Assert.StartsWith(LineNumberPrefix(5), problems[1], StringComparison.Ordinal);
        Assert.StartsWith(LineNumberPrefix(6), problems[2], StringComparison.Ordinal);
        Assert.StartsWith(LineNumberPrefix(8), problems[3], StringComparison.Ordinal);
        Assert.Contains("Bogus", problems[0]);
        Assert.Contains("Weight2", problems[1]);
        Assert.Contains("PelvisDrop", problems[3]);
        SameBits(LegParamsSpec.With(LegParamsSpec.With(fallback, LegParam.Weight, 0.5f), LegParam.PlantHeight, 0.1f), result);
    }

    [Fact]
    public void ReadAppendsToAnExistingProblemsListWithoutClearingIt()
    {
        var problems = new List<string> { "earlier" };
        LegProfileText.Read("Bogus = 1\n", Midway(), problems);
        LegProfileText.Read("Weight = abc\n", Midway(), problems);
        Assert.Equal(3, problems.Count);
        Assert.Equal("earlier", problems[0]);
        Assert.StartsWith(LineNumberPrefix(1), problems[1], StringComparison.Ordinal);
        Assert.StartsWith(LineNumberPrefix(1), problems[2], StringComparison.Ordinal);
    }

    [Fact]
    public void WrittenNonFiniteValuesAreRejectedOneProblemPerField()
    {
        var fallback = Midway();
        var result = Parse(LegProfileText.Write(Filled(_ => float.NaN)), fallback, out var problems);
        Assert.Equal(LegParamsSpec.Count, problems.Count);
        for (var i = 0; i < problems.Count; i++)
        {
            Assert.StartsWith(LineNumberPrefix(i + 1), problems[i], StringComparison.Ordinal);
            Assert.Contains(Name((LegParam)i), problems[i]);
        }
        SameBits(fallback, result);
    }

    [Fact]
    public void GeneratedTextReadsAsGeneratedWithOneProblemPerRejectedLine()
    {
        var random = new Random(424242);
        var fallback = Midway();
        string[] breaks = { "\n", "\r\n", "\r" };
        string[] joints = { " = ", "=", "\t=\t", "  =  " };
        string[] unreadable = { "abc", string.Empty, "0.5.5", "0,5", "NaN", "Infinity", "1e999" };
        string[] unknown = { "Bogus", "weight", "Weight2", "Plant Height" };
        string[] malformed = { "Weight", "Weight 0.5", "= 0.5", "just words" };
        for (var round = 0; round < 500; round++)
        {
            var expected = fallback;
            var applied = new bool[LegParamsSpec.Count];
            var rejectedLines = new List<int>();
            var text = new StringBuilder();
            var lineCount = random.Next(1, 40);
            for (var number = 1; number <= lineCount; number++)
            {
                var index = random.Next(LegParamsSpec.Count);
                var field = (LegParam)index;
                var bounds = LegParamsSpec.Bounds(field);
                var joint = joints[random.Next(joints.Length)];
                var rejected = true;
                string content;
                switch (random.Next(8))
                {
                    case 0:
                        content = "  ";
                        rejected = false;
                        break;
                    case 1:
                        content = unknown[random.Next(unknown.Length)] + joint + "0.5";
                        break;
                    case 2:
                        content = malformed[random.Next(malformed.Length)];
                        break;
                    case 3:
                        content = Name(field) + joint + unreadable[random.Next(unreadable.Length)];
                        break;
                    case 4:
                        content = Name(field) + joint + Invariant(bounds.Maximum + 1f);
                        break;
                    default:
                        var value = Math.Min(Math.Max(bounds.Minimum + (bounds.Maximum - bounds.Minimum) * (float)random.NextDouble(), bounds.Minimum), bounds.Maximum);
                        content = Name(field) + joint + Invariant(value);
                        rejected = applied[index];
                        if (!rejected)
                        {
                            applied[index] = true;
                            expected = LegParamsSpec.With(expected, field, value);
                        }
                        break;
                }
                if (rejected)
                    rejectedLines.Add(number);
                text.Append(content);
                if (number < lineCount || random.Next(2) == 0)
                    text.Append(breaks[random.Next(breaks.Length)]);
            }
            var result = Parse(text.ToString(), fallback, out var problems);
            SameBits(expected, result);
            Assert.Equal(rejectedLines.Count, problems.Count);
            for (var k = 0; k < problems.Count; k++)
                Assert.StartsWith(LineNumberPrefix(rejectedLines[k]), problems[k], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ArbitraryTextNeverThrowsAndNeverLeavesAFieldOutOfRange()
    {
        var random = new Random(31337);
        var fallback = Midway();
        for (var n = 0; n < 3000; n++)
        {
            var builder = new StringBuilder();
            var pieces = random.Next(1, 60);
            for (var k = 0; k < pieces; k++)
                builder.Append(GarbagePieces[random.Next(GarbagePieces.Length)]);
            var text = builder.ToString();
            var result = Parse(text, fallback, out var problems);
            EveryFieldIsFiniteAndInRange(result);
            Assert.True(problems.Count <= NonBlankLines(text));
            foreach (var problem in problems)
                Assert.StartsWith("line ", problem, StringComparison.Ordinal);
            SameBits(result, LegProfileText.Read(text, fallback, null));
            SameBits(result, LegProfileText.Read(text, fallback, new List<string>()));
        }
    }

    [Fact]
    public void ALeadingByteOrderMarkIsSkipped()
    {
        var problems = new List<string>();
        var read = LegProfileText.Read(((char)0xFEFF).ToString() + "Weight = 0.5\n", LegParams.Standard, problems);
        Assert.Empty(problems);
        Assert.Equal(0.5f, read.Weight);
    }
}
