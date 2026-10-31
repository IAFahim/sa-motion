using SA.Humanoid.Build;
using SA.Humanoid.Mapping;
using SA.Motion.Numerics;
using Xunit;

namespace SA.Humanoid.Tests.Build;

public class RagdollBuilderFailureTests
{
    const float TotalMass = 80f;

    static RagdollLayout Build(SkeletonBone[]? skeleton, int[]? map, float mass) => RagdollBuilder.Build(skeleton, map, mass);

    [Fact]
    public void ZeroLengthSegmentGivesDiagnosticAndStaysFinite()
    {
        Float3[] worlds = RagdollFixtures.Worlds();
        worlds[7] = worlds[6];
        RagdollLayout layout = Build(RagdollFixtures.FromWorlds(RagdollFixtures.Names(), RagdollFixtures.Parents(), worlds), RagdollFixtures.Map(), TotalMass);
        Assert.False(layout.IsValid);
        Assert.True(RagdollFixtures.Contains(layout, BuildDiagnostics.ZeroLengthSegment, Role.ForearmLeft));
        Assert.True(RagdollFixtures.Contains(layout, BuildDiagnostics.ZeroLengthSegment, Role.HandLeft));
        Assert.Equal(0, RagdollFixtures.FiniteViolations(layout));
        Assert.Equal(Roles.Count, layout.Bodies.Length);
        Assert.Equal(Roles.Count - 1, layout.Joints.Length);
    }

    [Fact]
    public void JustAboveThresholdStaysValid()
    {
        Float3[] worlds = RagdollFixtures.Worlds();
        worlds[7] = worlds[6] + new Float3(0.002f, 0f, 0f);
        RagdollLayout layout = Build(RagdollFixtures.FromWorlds(RagdollFixtures.Names(), RagdollFixtures.Parents(), worlds), RagdollFixtures.Map(), TotalMass);
        Assert.True(layout.IsValid);
        Assert.Empty(layout.Diagnostics);
        RagdollFixtures.Near(0.002f, layout.Bodies[(int)Role.ForearmLeft].CapsuleEnd.X, 1e-6f);
    }

    [Fact]
    public void MissingRoleGivesDiagnosticWithoutZeroLengthNoise()
    {
        int[] map = RagdollFixtures.Map();
        map[(int)Role.HandLeft] = -1;
        RagdollLayout layout = Build(RagdollFixtures.Bones(), map, TotalMass);
        Assert.False(layout.IsValid);
        Assert.True(RagdollFixtures.Contains(layout, DiagnosticCode.RoleMissing, Role.HandLeft));
        Assert.Equal(0, RagdollFixtures.FiniteViolations(layout));
        for (int i = 0; i < layout.Diagnostics.Length; i++)
        {
            Assert.NotEqual(BuildDiagnostics.ZeroLengthSegment, layout.Diagnostics[i].Code);
        }
    }

    [Fact]
    public void NonUniformScaleGivesDiagnostic()
    {
        SkeletonBone[] bones = RagdollFixtures.Bones();
        Pose rest = bones[1].Rest;
        bones[1] = new SkeletonBone(bones[1].Name, bones[1].Parent, new Pose { Position = rest.Position, Rotation = rest.Rotation, Scale = new Float3(1f, 1.5f, 1f) });
        RagdollLayout layout = Build(bones, RagdollFixtures.Map(), TotalMass);
        Assert.False(layout.IsValid);
        Assert.True(RagdollFixtures.Contains(layout, BuildDiagnostics.BadBoneScale, Role.Torso));
        Assert.Equal(0, RagdollFixtures.FiniteViolations(layout));
    }

    [Fact]
    public void NonPositiveScaleGivesDiagnostic()
    {
        SkeletonBone[] bones = RagdollFixtures.Bones();
        Pose rest = bones[12].Rest;
        bones[12] = new SkeletonBone(bones[12].Name, bones[12].Parent, new Pose { Position = rest.Position, Rotation = rest.Rotation, Scale = new Float3(1f, 0f, 1f) });
        RagdollLayout layout = Build(bones, RagdollFixtures.Map(), TotalMass);
        Assert.False(layout.IsValid);
        Assert.True(RagdollFixtures.Contains(layout, BuildDiagnostics.BadBoneScale, Role.ThighLeft));
        Assert.Equal(0, RagdollFixtures.FiniteViolations(layout));
    }

    [Fact]
    public void NonFiniteRestGivesDiagnostic()
    {
        SkeletonBone[] bones = RagdollFixtures.Bones();
        Pose rest = bones[13].Rest;
        bones[13] = new SkeletonBone(bones[13].Name, bones[13].Parent, new Pose { Position = new Float3(float.NaN, 0f, 0f), Rotation = rest.Rotation, Scale = rest.Scale });
        RagdollLayout layout = Build(bones, RagdollFixtures.Map(), TotalMass);
        Assert.False(layout.IsValid);
        Assert.True(RagdollFixtures.Contains(layout, BuildDiagnostics.BadRestValue, Role.ShinLeft));
        Assert.Equal(0, RagdollFixtures.FiniteViolations(layout));
    }

    [Theory]
    [InlineData(-5f)]
    [InlineData(0f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void BadTotalMassGivesDiagnosticWithZeroMasses(float mass)
    {
        RagdollLayout layout = Build(RagdollFixtures.Bones(), RagdollFixtures.Map(), mass);
        Assert.False(layout.IsValid);
        Assert.True(RagdollFixtures.Contains(layout, BuildDiagnostics.BadTotalMass, Role.Pelvis));
        for (int i = 0; i < layout.Bodies.Length; i++)
        {
            Assert.Equal(0f, layout.Bodies[i].Mass);
        }
        Assert.Equal(0, RagdollFixtures.FiniteViolations(layout));
    }

    [Fact]
    public void NullInputsStayTotal()
    {
        RagdollLayout layout = Build(null, null, TotalMass);
        Assert.False(layout.IsValid);
        Assert.True(RagdollFixtures.Contains(layout, DiagnosticCode.WrongMapLength, Role.Pelvis));
        Assert.Equal(Roles.Count, layout.Bodies.Length);
        Assert.Equal(Roles.Count - 1, layout.Joints.Length);
        Assert.Equal(0, RagdollFixtures.FiniteViolations(layout));
    }

    [Fact]
    public void AllUnmappedStaysTotal()
    {
        int[] map = new int[Roles.Count];
        for (int i = 0; i < map.Length; i++)
        {
            map[i] = -1;
        }
        RagdollLayout layout = Build(RagdollFixtures.Bones(), map, TotalMass);
        Assert.False(layout.IsValid);
        Assert.Equal(Roles.Count, layout.Diagnostics.Length);
        for (int i = 0; i < layout.Diagnostics.Length; i++)
        {
            Assert.Equal(DiagnosticCode.RoleMissing, layout.Diagnostics[i].Code);
        }
        Assert.Equal(0, RagdollFixtures.FiniteViolations(layout));
    }

    [Fact]
    public void DiagnosticsAreOrderedByRoleThenCode()
    {
        Float3[] worlds = RagdollFixtures.Worlds();
        worlds[7] = worlds[6];
        int[] map = RagdollFixtures.Map();
        map[(int)Role.FootRight] = -1;
        RagdollLayout layout = Build(RagdollFixtures.FromWorlds(RagdollFixtures.Names(), RagdollFixtures.Parents(), worlds), map, TotalMass);
        Assert.False(layout.IsValid);
        for (int i = 1; i < layout.Diagnostics.Length; i++)
        {
            Diagnostic before = layout.Diagnostics[i - 1];
            Diagnostic after = layout.Diagnostics[i];
            Assert.True((int)before.Role < (int)after.Role || ((int)before.Role == (int)after.Role && (int)before.Code <= (int)after.Code));
        }
    }
}
