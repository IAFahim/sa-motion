using System;
using SA.Humanoid;
using SA.Motion.Mechanics;
using SA.Motion.Numerics;
using SA.ReactionControl.Balance;
using Xunit;

namespace SA.ReactionControl.Tests.Balance;

public class BalancePipelineTests
{
    static void RunStep(
        in BalanceParams p,
        RoleBody[] bodies,
        SupportProbe leftProbe,
        SupportProbe rightProbe,
        ref BalanceSnapshot snapshot,
        ref BalanceFeet feet,
        ref BalancePelvisState pelvis,
        JointDrive[] drives)
    {
        var built = SnapshotBuilder.TryBuild(p, bodies, leftProbe, rightProbe, ref snapshot);
        Assert.True(built);
        FootPlacement.Update(p, snapshot, ref feet);
        PelvisControl.Update(p, snapshot, ref pelvis, feet.LeftPlanted, feet.RightPlanted);
        BalanceSolve.Solve(p, ref snapshot, ref feet, ref pelvis, drives);
    }

    static void Fresh(
        out BalanceParams p,
        out BalanceSnapshot snapshot,
        out BalanceFeet feet,
        out BalancePelvisState pelvis,
        out JointDrive[] drives)
    {
        p = BalanceParams.Standard;
        snapshot = default;
        feet = BalanceFeet.Initial(p.MaxStepDelay);
        pelvis = default;
        drives = new JointDrive[Roles.Count];
    }

    [Fact]
    public void StandingStill_RequestsNoStepAndNearZeroDrive()
    {
        Fresh(out var p, out var snapshot, out var feet, out var pelvis, out var drives);
        BalanceFixture.Probes(out var leftProbe, out var rightProbe);
        for (var tick = 0; tick < 10; tick++)
        {
            var bodies = BalanceFixture.Standing(Float3.Zero);
            RunStep(p, bodies, leftProbe, rightProbe, ref snapshot, ref feet, ref pelvis, drives);
            Assert.Equal(0, feet.SteppingLeg);
        }
        Assert.True(Math.Abs(snapshot.Left.Hip.Desired.X) < 0.05f);
        Assert.True(Math.Abs(snapshot.Left.Hip.Desired.Y) < 0.05f);
        Assert.True(Math.Abs(snapshot.Right.Hip.Desired.X) < 0.05f);
        Assert.True(Math.Abs(snapshot.Right.Hip.Desired.Y) < 0.05f);
        Assert.True(Math.Abs(snapshot.Left.Knee.Desired) < 0.05f);
        Assert.True(Math.Abs(snapshot.Left.Ankle.Desired.X) < 0.05f);
        Assert.True(Math.Abs(snapshot.Spine.Desired.Y) < 0.05f);
        for (var i = 0; i < Roles.Count; i++)
        {
            Assert.True(drives[i].Target.IsValidRotation);
            Assert.True(drives[i].Stiffness >= 0f && drives[i].Damping >= 0f);
        }
        Assert.Equal(JointDrive.Passive.Target.W, drives[(int)Role.Pelvis].Target.W);
        Assert.Equal(0f, drives[(int)Role.Head].Stiffness);
    }

    [Theory]
    [InlineData(1f, 0f)]
    [InlineData(-1f, 0f)]
    [InlineData(0f, 1f)]
    [InlineData(0f, -1f)]
    [InlineData(0.7071f, 0.7071f)]
    [InlineData(-0.7071f, 0.7071f)]
    [InlineData(0.7071f, -0.7071f)]
    [InlineData(-0.7071f, -0.7071f)]
    public void PushInEightDirections_StepsAsideTowardPush(float dx, float dz)
    {
        Fresh(out var p, out var snapshot, out var feet, out var pelvis, out var drives);
        BalanceFixture.Probes(out var leftProbe, out var rightProbe);
        var direction = new Float3(dx, 0f, dz);
        var bodies = BalanceFixture.GuardStance(direction * 1.5f);
        var first = 0;
        for (var tick = 0; tick < 15; tick++)
        {
            RunStep(p, bodies, leftProbe, rightProbe, ref snapshot, ref feet, ref pelvis, drives);
            if (tick == 0)
                first = feet.SteppingLeg;
        }
        Assert.True(first == 1 || first == 2);
        Assert.Equal(first, feet.SteppingLeg);
        if (dx > 0.5f)
            Assert.Equal(2, first);
        if (dx < -0.5f)
            Assert.Equal(1, first);
        var stepped = feet.SteppingLeg == 1 ? feet.FootLeft : feet.FootRight;
        var ahead = Float3.Dot(stepped - snapshot.Com, direction);
        Assert.True(ahead > 0f);
    }

    [Fact]
    public void StepLength_GrowsWithPushThenClamps()
    {
        var small = MaxStepTravel(0.6f);
        var medium = MaxStepTravel(2.5f);
        var huge = MaxStepTravel(10f);
        Assert.True(small < medium);
        Assert.True(medium <= huge + 0.05f);
        Assert.True(Math.Abs(huge - medium) < 0.15f);
        Assert.True(huge < 1.5f);
    }

    static float MaxStepTravel(float speed)
    {
        Fresh(out var p, out var snapshot, out var feet, out var pelvis, out var drives);
        BalanceFixture.Probes(out var leftProbe, out var rightProbe);
        var direction = new Float3(1f, 0f, 0f);
        var start = new Float3(-0.13f, 0f, 0.02f);
        var farthest = 0f;
        for (var tick = 0; tick < 200; tick++)
        {
            var bodies = BalanceFixture.Standing(direction * speed);
            var built = SnapshotBuilder.TryBuild(p, bodies, leftProbe, rightProbe, ref snapshot);
            Assert.True(built);
            FootPlacement.Update(p, snapshot, ref feet);
            var stepped = feet.SteppingLeg == 2 ? feet.FootRight : feet.FootLeft;
            var travel = (stepped - start).Length;
            if (travel > farthest)
                farthest = travel;
            PelvisControl.Update(p, snapshot, ref pelvis, feet.LeftPlanted, feet.RightPlanted);
            BalanceSolve.Solve(p, ref snapshot, ref feet, ref pelvis, drives);
        }
        return farthest;
    }

    [Fact]
    public void Stepping_HasHysteresisAgainstNoiseAndReversal()
    {
        Fresh(out var p, out var snapshot, out var feet, out var pelvis, out var drives);
        BalanceFixture.Probes(out var leftProbe, out var rightProbe);
        var bodies = BalanceFixture.Standing(new Float3(1.5f, 0f, 0f));
        RunStep(p, bodies, leftProbe, rightProbe, ref snapshot, ref feet, ref pelvis, drives);
        Assert.Equal(2, feet.SteppingLeg);
        for (var tick = 0; tick < 20; tick++)
        {
            bodies = BalanceFixture.Standing(new Float3(0.05f, 0f, 0.02f));
            RunStep(p, bodies, leftProbe, rightProbe, ref snapshot, ref feet, ref pelvis, drives);
            Assert.Equal(2, feet.SteppingLeg);
        }
        for (var tick = 0; tick < 10; tick++)
        {
            bodies = BalanceFixture.Standing(new Float3(-1.5f, 0f, 0f));
            RunStep(p, bodies, leftProbe, rightProbe, ref snapshot, ref feet, ref pelvis, drives);
            Assert.Equal(2, feet.SteppingLeg);
        }
        for (var tick = 0; tick < 12; tick++)
        {
            bodies = BalanceFixture.Standing(new Float3(1.5f, 0f, 0f));
            RunStep(p, bodies, leftProbe, rightProbe, ref snapshot, ref feet, ref pelvis, drives);
            Assert.Equal(2, feet.SteppingLeg);
        }
        BalanceFixture.Probes(out leftProbe, out rightProbe);
        leftProbe.Hit = 0;
        bodies = BalanceFixture.Standing(new Float3(1.5f, 0f, 0f));
        RunStep(p, bodies, leftProbe, rightProbe, ref snapshot, ref feet, ref pelvis, drives);
        Assert.Equal(1, feet.SteppingLeg);
    }

    [Fact]
    public void PelvisHeight_HoldsNearStandingHeight()
    {
        Fresh(out var p, out var snapshot, out var feet, out var pelvis, out var drives);
        BalanceFixture.Probes(out var leftProbe, out var rightProbe);
        var bodies = BalanceFixture.Standing(Float3.Zero);
        RunStep(p, bodies, leftProbe, rightProbe, ref snapshot, ref feet, ref pelvis, drives);
        Assert.True(Math.Abs(snapshot.HeightTarget - p.PelvisHeight) < 0.06f);
        var wide = BalanceFixture.Standing(Float3.Zero);
        wide[(int)Role.FootLeft] = WideFoot(wide[(int)Role.FootLeft], 0.5f);
        wide[(int)Role.FootRight] = WideFoot(wide[(int)Role.FootRight], -0.5f);
        wide[(int)Role.ThighLeft] = WideFoot(wide[(int)Role.ThighLeft], 0.45f);
        wide[(int)Role.ThighRight] = WideFoot(wide[(int)Role.ThighRight], -0.45f);
        wide[(int)Role.ShinLeft] = WideFoot(wide[(int)Role.ShinLeft], 0.48f);
        wide[(int)Role.ShinRight] = WideFoot(wide[(int)Role.ShinRight], -0.48f);
        RunStep(p, wide, leftProbe, rightProbe, ref snapshot, ref feet, ref pelvis, drives);
        Assert.True(snapshot.HeightTarget < p.PelvisHeight);
    }

    static RoleBody WideFoot(RoleBody body, float x)
    {
        body.Position = new Float3(x, body.Position.Y, body.Position.Z);
        return body;
    }

    [Fact]
    public void Mirror_MirrorsEveryOutput()
    {
        Fresh(out var p, out var snapshot, out var feet, out var pelvis, out var drives);
        BalanceFixture.Probes(out var leftProbe, out var rightProbe);
        var push = new Float3(1.2f, 0f, 0.4f);
        var bodies = BalanceFixture.Standing(push);
        RunStep(p, bodies, leftProbe, rightProbe, ref snapshot, ref feet, ref pelvis, drives);
        var mirroredBodies = BalanceFixture.Mirrored(bodies);
        var mirroredLeft = BalanceFixture.Mirrored(rightProbe);
        var mirroredRight = BalanceFixture.Mirrored(leftProbe);
        Fresh(out _, out var snapshotMirror, out var feetMirror, out var pelvisMirror, out var drivesMirror);
        RunStep(p, mirroredBodies, mirroredLeft, mirroredRight, ref snapshotMirror, ref feetMirror, ref pelvisMirror, drivesMirror);
        Assert.Equal(feet.SteppingLeg == 1 ? 2 : 1, (int)feetMirror.SteppingLeg);
        Assert.True(Distance(BalanceFixture.Mirror(feet.FootLeft), feetMirror.FootRight) < 1e-4f);
        Assert.True(Distance(BalanceFixture.Mirror(feet.FootRight), feetMirror.FootLeft) < 1e-4f);
        Assert.True(Math.Abs(snapshot.HeightTarget - snapshotMirror.HeightTarget) < 1e-4f);
        for (var i = 0; i < Roles.Count; i++)
        {
            var role = (Role)i;
            var other = BalanceFixture.MirrorRole(role);
            var a = drives[i];
            var b = drivesMirror[(int)other];
            Assert.True(QuatDistance(MirrorDrive(a.Target), b.Target) < 1e-4f);
            Assert.True(Math.Abs(a.Stiffness - b.Stiffness) < 1e-5f);
            Assert.True(Math.Abs(a.Damping - b.Damping) < 1e-5f);
        }
    }

    static float Distance(Float3 a, Float3 b)
    {
        return (a - b).Length;
    }

    static float QuatDistance(SA.Motion.Numerics.Quaternion a, SA.Motion.Numerics.Quaternion b)
    {
        var dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
        if (dot < 0f)
            dot = -dot;
        return 1f - dot;
    }

    static SA.Motion.Numerics.Quaternion MirrorDrive(SA.Motion.Numerics.Quaternion q)
    {
        return BalanceFixture.MirrorRotation(q);
    }
}
