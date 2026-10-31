using System;
using System.Runtime.InteropServices;
using SA.Humanoid;
using SA.Motion.Mechanics;
using SA.Motion.Numerics;
using SA.ReactionControl.Balance;
using Xunit;

namespace SA.ReactionControl.Tests.Balance;

public class BalanceTotalityTests
{
    [Fact]
    public void SnapshotBuilder_RejectsShortSpan()
    {
        var p = BalanceParams.Standard;
        var bodies = new RoleBody[Roles.Count - 1];
        BalanceFixture.Probes(out var leftProbe, out var rightProbe);
        var snapshot = default(BalanceSnapshot);
        Assert.False(SnapshotBuilder.TryBuild(p, bodies, leftProbe, rightProbe, ref snapshot));
    }

    [Fact]
    public void SnapshotBuilder_RejectsAllInvalidBodies()
    {
        var p = BalanceParams.Standard;
        var bodies = new RoleBody[Roles.Count];
        for (var i = 0; i < bodies.Length; i++)
        {
            bodies[i] = new RoleBody
            {
                Position = new Float3(float.NaN, float.NaN, float.NaN),
                Rotation = new SA.Motion.Numerics.Quaternion(float.NaN, 0f, 0f, 0f),
                InverseMass = 0f
            };
        }
        BalanceFixture.Probes(out var leftProbe, out var rightProbe);
        var snapshot = default(BalanceSnapshot);
        Assert.False(SnapshotBuilder.TryBuild(p, bodies, leftProbe, rightProbe, ref snapshot));
    }

    [Fact]
    public void HostileInputs_ProduceFiniteBoundedOutputs()
    {
        var hostileFloats = new float[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 1e30f, -1e30f, 0f };
        var count = 0;
        foreach (var hx in hostileFloats)
        {
            foreach (var hy in hostileFloats)
            {
                foreach (var hz in hostileFloats)
                {
                    RunHostile(new Float3(hx, hy, hz));
                    count++;
                }
            }
        }
        Assert.True(count == hostileFloats.Length * hostileFloats.Length * hostileFloats.Length);
    }

    static void RunHostile(Float3 hostile)
    {
        var p = BalanceParams.Standard;
        var bodies = BalanceFixture.Standing(hostile);
        bodies[(int)Role.Pelvis] = new RoleBody
        {
            Position = hostile,
            Rotation = new SA.Motion.Numerics.Quaternion(hostile.X, hostile.Y, hostile.Z, hostile.X),
            LinearVelocity = hostile,
            AngularVelocity = hostile,
            InverseMass = hostile.X
        };
        var badProbe = new SupportProbe
        {
            Hit = 1,
            Distance = hostile.X,
            Point = hostile,
            Normal = hostile,
            PointVelocity = hostile
        };
        var snapshot = default(BalanceSnapshot);
        var built = SnapshotBuilder.TryBuild(p, bodies, badProbe, badProbe, ref snapshot);
        var feet = BalanceFeet.Initial(p.MaxStepDelay);
        var pelvis = default(BalancePelvisState);
        pelvis.BlendVelocity = hostile;
        pelvis.Centre = hostile;
        var drives = new JointDrive[Roles.Count];
        for (var i = 0; i < drives.Length; i++)
            drives[i] = JointDrive.Passive;
        if (built)
        {
            FootPlacement.Update(p, snapshot, ref feet);
            PelvisControl.Update(p, snapshot, ref pelvis, feet.LeftPlanted, feet.RightPlanted);
            BalanceSolve.Solve(p, ref snapshot, ref feet, ref pelvis, drives);
        }
        Assert.True(float.IsFinite(feet.FootLeft.X + feet.FootLeft.Y + feet.FootLeft.Z));
        Assert.True(float.IsFinite(feet.Centre.X + feet.Centre.Y + feet.Centre.Z));
        Assert.True(float.IsFinite(snapshot.HeightTarget));
        Assert.True(Math.Abs(snapshot.HeightTarget) < 1e6f);
        Assert.True(snapshot.Left.Hip.Desired.IsFinite);
        Assert.True(snapshot.Right.Hip.Desired.IsFinite);
        for (var i = 0; i < Roles.Count; i++)
        {
            var drive = drives[i];
            Assert.True(drive.Target.IsValidRotation || drive.Target.W == 1f);
            Assert.True(float.IsFinite(drive.Stiffness + drive.Damping + drive.MaxTorque));
            Assert.True(drive.Stiffness >= 0f && drive.Damping >= 0f && drive.MaxTorque >= 0f);
            Assert.True(drive.Stiffness <= 1e6f && drive.MaxTorque <= 1e6f);
        }
    }

    [Fact]
    public void Solve_HandlesShortDriveSpan()
    {
        var p = BalanceParams.Standard;
        var bodies = BalanceFixture.Standing(Float3.Zero);
        BalanceFixture.Probes(out var leftProbe, out var rightProbe);
        var snapshot = default(BalanceSnapshot);
        Assert.True(SnapshotBuilder.TryBuild(p, bodies, leftProbe, rightProbe, ref snapshot));
        var feet = BalanceFeet.Initial(p.MaxStepDelay);
        var pelvis = default(BalancePelvisState);
        var drives = new JointDrive[Roles.Count - 1];
        BalanceSolve.Solve(p, ref snapshot, ref feet, ref pelvis, drives);
    }

    [Fact]
    public void Helpers_HandleNonFiniteInputs()
    {
        var bad = new Float3(float.NaN, float.PositiveInfinity, 0f);
        var frame = new Frame(bad, new SA.Motion.Numerics.Quaternion(float.NaN, 0f, 0f, 0f));
        var p = BalanceParams.Standard;
        var closest = Support.Measure(bad, frame, frame, float.NaN, float.NaN, bad, out var point);
        Assert.True(float.IsFinite(closest));
        Assert.True(point.IsFinite);
        var reached = FootPlacement.Reach(bad, bad, frame, float.NaN, float.NaN, p);
        Assert.True(reached.Position.IsFinite);
        FootPlacement.HipLeans(new BalanceJoint3(), bad, float.NaN, out var lean1, out var lean2);
        Assert.True(float.IsFinite(lean1 + lean2));
        var room = FootPlacement.HipRoom(new BalanceJoint3(), bad.X, bad.Y, bad.X, bad.Y, bad.X);
        Assert.True(float.IsFinite(room));
        var yaw = PelvisControl.Yaw(float.NaN, frame, bad, true, true, bad, p);
        Assert.True(float.IsFinite(yaw));
    }

    [Fact]
    public void Support_MeasuresInsideNegativeOutside()
    {
        var left = new Frame(new Float3(0.13f, 0f, 0.02f), SA.Motion.Numerics.Quaternion.Identity);
        var right = new Frame(new Float3(-0.13f, 0f, 0.02f), SA.Motion.Numerics.Quaternion.Identity);
        var up = new Float3(0f, 1f, 0f);
        var inside = Support.Measure(new Float3(0f, 0f, 0.02f), left, right, 0.09f, 0.16f, up, out _);
        Assert.True(inside < 0f);
        Assert.True(Math.Abs(inside + 0.16f) < 1e-4f);
        var outside = Support.Measure(new Float3(2f, 0f, 0.02f), left, right, 0.09f, 0.16f, up, out var near);
        Assert.True(outside < inside);
        Assert.True(near.IsFinite);
        Assert.True(Math.Abs(near.X + 0.04f) < 1e-4f);
    }

    [Fact]
    public void NewTypes_AreSequential()
    {
        Assert.Equal(LayoutKind.Sequential, typeof(BalanceParams).StructLayoutAttribute!.Value);
        Assert.Equal(LayoutKind.Sequential, typeof(BalanceSnapshot).StructLayoutAttribute!.Value);
        Assert.Equal(LayoutKind.Sequential, typeof(BalanceFeet).StructLayoutAttribute!.Value);
        Assert.Equal(LayoutKind.Sequential, typeof(BalancePelvisState).StructLayoutAttribute!.Value);
        Assert.Equal(LayoutKind.Sequential, typeof(BalanceJoint3).StructLayoutAttribute!.Value);
        Assert.Equal(LayoutKind.Sequential, typeof(BalanceJoint1).StructLayoutAttribute!.Value);
        Assert.Equal(LayoutKind.Sequential, typeof(BalancePart).StructLayoutAttribute!.Value);
        Assert.Equal(LayoutKind.Sequential, typeof(BalanceLeg).StructLayoutAttribute!.Value);
        Assert.Equal(LayoutKind.Sequential, typeof(BalanceSupport).StructLayoutAttribute!.Value);
    }

    [Fact]
    public void StandardParams_AreSane()
    {
        var p = BalanceParams.Standard;
        Assert.Equal(1f, p.Up.Y);
        Assert.True(p.DeltaTime > 0f && p.DeltaTime < 0.1f);
        Assert.True(p.PelvisHeight > 0f && p.PelvisHeight < 2f);
        Assert.True(p.StepClamp > 0f);
        Assert.True(p.MaxTorque > 0f);
        Assert.True(p.MaxStepDelay > 0);
        Assert.True(p.HipStrength > 0f && p.KneeStrength > 0f && p.AnkleStrength > 0f);
    }
}
