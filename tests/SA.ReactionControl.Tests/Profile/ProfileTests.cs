using System;
using System.Collections.Generic;
using System.IO;
using SA.Motion.Numerics;
using SA.ReactionControl;
using SA.ReactionControl.Balance;
using SA.ReactionControl.Brace;
using SA.ReactionControl.CatchFall;
using SA.ReactionControl.Coordination;
using SA.ReactionControl.Drive;
using SA.ReactionControl.Profile;
using Xunit;

namespace SA.ReactionControl.Tests.Profile;

public class ProfileTests
{
    static string ReadTuningDoc()
    {
        string? current = AppContext.BaseDirectory;
        while (current != null)
        {
            string candidate = Path.Combine(current, "docs", "tuning.md");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
            current = Path.GetDirectoryName(current);
        }
        return string.Empty;
    }

    static HashSet<string> TableFields(string text, int cell)
    {
        var names = new HashSet<string>();
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("|", StringComparison.Ordinal))
            {
                continue;
            }
            string[] cells = trimmed.Split('|');
            if (cells.Length <= cell)
            {
                continue;
            }
            string entry = cells[cell].Trim();
            if (entry.Length == 0 || entry == "Area" || entry == "Field" || entry.Contains("---"))
            {
                continue;
            }
            foreach (string part in entry.Split(','))
            {
                string name = part.Trim();
                if (name.Length > 0)
                {
                    names.Add(name);
                }
            }
        }
        return names;
    }

    static void AssertParamsCovered<T>(HashSet<string> names)
    {
        foreach (var field in typeof(T).GetFields())
        {
            Assert.True(names.Contains(field.Name), "Missing field " + field.Name);
        }
    }

    [Fact]
    public void EveryParamsFieldAppearsInTuningTable()
    {
        string text = ReadTuningDoc();
        Assert.NotEqual(string.Empty, text);
        HashSet<string> names = TableFields(text, 2);
        AssertParamsCovered<BalanceParams>(names);
        AssertParamsCovered<BodyBalanceParams>(names);
        AssertParamsCovered<CatchFallParams>(names);
        AssertParamsCovered<BraceParams>(names);
        AssertParamsCovered<DriveParams>(names);
        AssertParamsCovered<CoordinationParams>(names);
        AssertParamsCovered<GetUpParams>(names);
    }

    [Fact]
    public void ProfileFieldsAndTuningLogExist()
    {
        string text = ReadTuningDoc();
        Assert.NotEqual(string.Empty, text);
        HashSet<string> areas = TableFields(text, 1);
        foreach (var field in typeof(ControllerProfile).GetFields())
        {
            if (field.Name == "Name")
            {
                Assert.Contains("Name", text, StringComparison.Ordinal);
            }
            else
            {
                Assert.True(areas.Contains(field.Name), "Missing area " + field.Name);
            }
        }
        Assert.Contains("Tuning log", text, StringComparison.Ordinal);
        Assert.Contains("Date | Scenario |", text, StringComparison.Ordinal);
    }

    [Fact]
    public void StandardValidatesClean()
    {
        ControllerProfile standard = ControllerProfiles.Standard;
        Assert.Empty(ControllerProfiles.Validate(in standard));
    }

    static BraceParams HostileBrace()
    {
        BraceParams s = ControllerProfiles.Standard.Brace;
        return new BraceParams(
            s.BodyStiffness,
            s.ArmDamping,
            s.WristStiffness,
            s.WristDamping,
            s.MaxTorque,
            s.BraceDistance,
            float.NaN,
            s.PredictionTime,
            s.ReachAbsorptionTime,
            s.TargetSpeedLimit,
            s.ApproachFloor,
            s.HysteresisBand,
            s.ReleaseMargin,
            s.ContactHoldTime,
            s.SideSpread,
            s.StaggerTime,
            s.CrouchAmount,
            s.LeanGain,
            s.GuardStiffness,
            s.GuardDamping);
    }

    static CatchFallParams HostileCatchFall()
    {
        CatchFallParams s = ControllerProfiles.Standard.CatchFall;
        return new CatchFallParams(
            s.LegsStiffness,
            s.TorsoStiffness,
            s.ArmsStiffness,
            s.ForwardMaxArmOffset,
            s.BackwardsMinArmOffset,
            s.SpinReductionGain,
            s.UseHeadLook,
            s.Reach,
            s.ProbeLength,
            s.MinExtension,
            s.StiffnessFloor,
            s.GainFloor,
            s.RelaxRate,
            s.RecoverRate,
            s.GainRate,
            s.ArmCapScale,
            s.FallBias,
            s.SpinTorqueScale,
            s.SpinTorqueMax,
            s.ClavFollowRatio,
            s.ElbowRest,
            -1f,
            s.SafeMissStiffness,
            s.SpineTwistLimit);
    }

    static ControllerProfile Hostile()
    {
        ControllerProfile hostile = ControllerProfiles.Standard;
        hostile.Balance.HipStrength = float.NaN;
        hostile.Balance.HipDamping = float.PositiveInfinity;
        hostile.Balance.PelvisHeight = -1f;
        hostile.Balance.MaxStepDelay = 0;
        hostile.Balance.Epsilon = 0f;
        hostile.Balance.StepClamp = 500f;
        hostile.Balance.Up = new Float3(0f, 0f, 0f);
        hostile.BodyBalance.MaxSteps = 0u;
        hostile.BodyBalance.UseHeadLook = 7;
        hostile.BodyBalance.BodyStiffness = -5f;
        hostile.Brace = HostileBrace();
        hostile.CatchFall = HostileCatchFall();
        hostile.Drive.Strength = 0f;
        hostile.Coordination.MinLegStiffness = float.PositiveInfinity;
        hostile.GetUp.PoseDot = 2f;
        return hostile;
    }

    [Fact]
    public void HostileValuesAreReported()
    {
        ControllerProfile hostile = Hostile();
        string[] problems = ControllerProfiles.Validate(in hostile);
        string[] expected =
        {
            "Balance.HipStrength:",
            "Balance.HipDamping:",
            "Balance.PelvisHeight:",
            "Balance.MaxStepDelay:",
            "Balance.Epsilon:",
            "Balance.StepClamp:",
            "Balance.Up:",
            "BodyBalance.MaxSteps:",
            "BodyBalance.UseHeadLook:",
            "BodyBalance.BodyStiffness:",
            "Brace.MaxReach:",
            "CatchFall.TorqueLimit:",
            "Drive.Strength:",
            "Coordination.MinLegStiffness:",
            "GetUp.PoseDot:",
        };
        foreach (string field in expected)
        {
            Assert.Contains(problems, p => p.StartsWith(field, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void SanitizeRepairsHostileProfile()
    {
        ControllerProfile hostile = Hostile();
        ControllerProfile repaired = ControllerProfiles.Sanitize(in hostile);
        Assert.Empty(ControllerProfiles.Validate(in repaired));
        ControllerProfile standard = ControllerProfiles.Standard;
        Assert.Equal(standard.Balance.HipStrength, repaired.Balance.HipStrength);
        Assert.Equal(standard.Balance.Epsilon, repaired.Balance.Epsilon);
        Assert.Equal(standard.Balance.Up.X, repaired.Balance.Up.X);
        Assert.Equal(standard.BodyBalance.BodyStiffness, repaired.BodyBalance.BodyStiffness);
        Assert.Equal(standard.Brace.MaxReach, repaired.Brace.MaxReach);
        Assert.Equal(standard.CatchFall.TorqueLimit, repaired.CatchFall.TorqueLimit);
        Assert.Equal(standard.Drive.Strength, repaired.Drive.Strength);
        Assert.Equal(standard.Coordination.MinLegStiffness, repaired.Coordination.MinLegStiffness);
        Assert.Equal(standard.GetUp.PoseDot, repaired.GetUp.PoseDot);
    }

    static void AssertFloatNear(float expected, float actual, float tolerance)
    {
        Assert.InRange(actual, expected - tolerance, expected + tolerance);
    }

    static void AssertParamsNear(object left, object right, float tolerance)
    {
        foreach (var field in left.GetType().GetFields())
        {
            object? a = field.GetValue(left);
            object? b = field.GetValue(right);
            if (field.FieldType == typeof(float))
            {
                AssertFloatNear((float)a!, (float)b!, tolerance);
            }
            else if (field.FieldType == typeof(Float3))
            {
                var va = (Float3)a!;
                var vb = (Float3)b!;
                AssertFloatNear(va.X, vb.X, tolerance);
                AssertFloatNear(va.Y, vb.Y, tolerance);
                AssertFloatNear(va.Z, vb.Z, tolerance);
            }
            else
            {
                Assert.Equal(a, b);
            }
        }
    }

    static void AssertProfilesNear(ControllerProfile left, ControllerProfile right, float tolerance)
    {
        Assert.Equal(left.Name, right.Name);
        AssertParamsNear(left.Balance, right.Balance, tolerance);
        AssertParamsNear(left.BodyBalance, right.BodyBalance, tolerance);
        AssertParamsNear(left.CatchFall, right.CatchFall, tolerance);
        AssertParamsNear(left.Brace, right.Brace, tolerance);
        AssertParamsNear(left.Drive, right.Drive, tolerance);
        AssertParamsNear(left.Coordination, right.Coordination, tolerance);
        AssertParamsNear(left.GetUp, right.GetUp, tolerance);
    }

    [Fact]
    public void ReferenceBodyMatchesStandard()
    {
        ControllerProfile reference = ControllerProfiles.ForBody(70f, 1.75f);
        ControllerProfile standard = ControllerProfiles.Standard;
        AssertProfilesNear(reference, standard, 1e-6f);
    }

    [Fact]
    public void DoubleMassDoublesTorqueCapsAndKeepsDimensionless()
    {
        ControllerProfile heavy = ControllerProfiles.ForBody(140f, 1.75f);
        ControllerProfile standard = ControllerProfiles.Standard;
        Assert.Equal(800f, heavy.Balance.MaxTorque);
        Assert.Equal(400f, heavy.Brace.MaxTorque);
        Assert.Equal(160f, heavy.CatchFall.TorqueLimit);
        Assert.Equal(120f, heavy.CatchFall.SpinTorqueMax);
        Assert.Equal(800f, heavy.BodyBalance.MaxDrive);
        Assert.Equal(400f, heavy.Drive.MaxTorque);
        Assert.Equal(100f, heavy.Coordination.MinLegStiffness);
        Assert.Equal(1200f, heavy.Coordination.MaxLegStiffness);
        Assert.Equal(standard.BodyBalance.Damping, heavy.BodyBalance.Damping);
        Assert.Equal(standard.BodyBalance.UseHeadLook, heavy.BodyBalance.UseHeadLook);
        Assert.Equal(standard.Balance.YawHalfSpan, heavy.Balance.YawHalfSpan);
        Assert.Equal(standard.Brace.LeanGain, heavy.Brace.LeanGain);
        Assert.Equal(standard.Drive.Strength, heavy.Drive.Strength);
        Assert.Equal(standard.Coordination.SupportedKneeResponse, heavy.Coordination.SupportedKneeResponse);
        Assert.Equal(standard.GetUp.PoseDot, heavy.GetUp.PoseDot);
        Assert.Equal(standard.Balance.PelvisHeight, heavy.Balance.PelvisHeight);
    }

    [Fact]
    public void HalfHeightHalvesLengths()
    {
        ControllerProfile small = ControllerProfiles.ForBody(70f, 0.875f);
        ControllerProfile standard = ControllerProfiles.Standard;
        AssertFloatNear(0.475f, small.Balance.PelvisHeight, 1e-6f);
        AssertFloatNear(0.5f, small.Balance.LegLength, 1e-6f);
        AssertFloatNear(0.45f, small.Brace.BraceDistance, 1e-6f);
        AssertFloatNear(0.375f, small.Brace.MaxReach, 1e-6f);
        AssertFloatNear(0.35f, small.CatchFall.Reach, 1e-6f);
        AssertFloatNear(0.75f, small.GetUp.ClearDistance, 1e-6f);
        AssertFloatNear(3f, small.BodyBalance.LookProbeRange, 1e-6f);
        Assert.Equal(standard.Balance.MaxTorque, small.Balance.MaxTorque);
    }

    [Fact]
    public void InvalidBodyFallsBackToStandard()
    {
        ControllerProfile standard = ControllerProfiles.Standard;
        AssertProfilesNear(ControllerProfiles.ForBody(0f, 1.75f), standard, 0f);
        AssertProfilesNear(ControllerProfiles.ForBody(-70f, 1.75f), standard, 0f);
        AssertProfilesNear(ControllerProfiles.ForBody(70f, 0f), standard, 0f);
        AssertProfilesNear(ControllerProfiles.ForBody(float.NaN, 1.75f), standard, 0f);
        AssertProfilesNear(ControllerProfiles.ForBody(70f, float.PositiveInfinity), standard, 0f);
    }

    [Fact]
    public void RepeatedCallsAreDeterministic()
    {
        AssertProfilesNear(ControllerProfiles.ForBody(140f, 0.875f), ControllerProfiles.ForBody(140f, 0.875f), 0f);
        ControllerProfile first = Hostile();
        ControllerProfile second = Hostile();
        AssertProfilesNear(ControllerProfiles.Sanitize(in first), ControllerProfiles.Sanitize(in second), 0f);
    }

    [Fact]
    public void SanitizeValidAndForBodyDoNotAllocate()
    {
        ControllerProfile standard = ControllerProfiles.Standard;
        ControllerProfiles.Sanitize(in standard);
        ControllerProfiles.ForBody(70f, 1.75f);
        long before = GC.GetAllocatedBytesForCurrentThread();
        ControllerProfile sanitized = ControllerProfiles.Sanitize(in standard);
        ControllerProfile scaled = ControllerProfiles.ForBody(70f, 1.75f);
        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(before, after);
        Assert.Equal("Standard", sanitized.Name);
        Assert.Equal("Standard", scaled.Name);
    }
}
