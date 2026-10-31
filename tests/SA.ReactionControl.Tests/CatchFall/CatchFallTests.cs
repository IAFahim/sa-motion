using System;
using SA.Humanoid;
using SA.Motion.Numerics;
using SA.ReactionControl.CatchFall;
using CF = SA.ReactionControl.CatchFall.CatchFall;
using Xunit;

namespace SA.ReactionControl.Tests;
public class CatchFallTests
{
    static RoleBody[] MakeBodies(Float3 torsoVelocity, Float3 torsoAngularVelocity)
    {
        var bodies = new RoleBody[Roles.Count];
        bodies[(int)Role.Pelvis] = Body(new Float3(0.0f, 0.9f, 0.0f), Float3.Zero, Float3.Zero);
        bodies[(int)Role.Torso] = Body(new Float3(0.0f, 1.0f, 0.0f), torsoVelocity, torsoAngularVelocity);
        bodies[(int)Role.Head] = Body(new Float3(0.0f, 1.7f, 0.0f), torsoVelocity, Float3.Zero);
        bodies[(int)Role.UpperArmLeft] = Body(new Float3(-0.25f, 1.4f, 0.0f), Float3.Zero, Float3.Zero);
        bodies[(int)Role.UpperArmRight] = Body(new Float3(0.25f, 1.4f, 0.0f), Float3.Zero, Float3.Zero);
        bodies[(int)Role.ForearmLeft] = Body(new Float3(-0.25f, 1.1f, 0.1f), Float3.Zero, Float3.Zero);
        bodies[(int)Role.ForearmRight] = Body(new Float3(0.25f, 1.1f, 0.1f), Float3.Zero, Float3.Zero);
        bodies[(int)Role.HandLeft] = Body(new Float3(-0.25f, 0.85f, 0.2f), Float3.Zero, Float3.Zero);
        bodies[(int)Role.HandRight] = Body(new Float3(0.25f, 0.85f, 0.2f), Float3.Zero, Float3.Zero);
        bodies[(int)Role.ThighLeft] = Body(new Float3(-0.1f, 0.5f, 0.0f), Float3.Zero, Float3.Zero);
        bodies[(int)Role.ThighRight] = Body(new Float3(0.1f, 0.5f, 0.0f), Float3.Zero, Float3.Zero);
        bodies[(int)Role.ShinLeft] = Body(new Float3(-0.1f, 0.3f, 0.0f), Float3.Zero, Float3.Zero);
        bodies[(int)Role.ShinRight] = Body(new Float3(0.1f, 0.3f, 0.0f), Float3.Zero, Float3.Zero);
        bodies[(int)Role.FootLeft] = Body(new Float3(-0.1f, 0.05f, 0.0f), Float3.Zero, Float3.Zero);
        bodies[(int)Role.FootRight] = Body(new Float3(0.1f, 0.05f, 0.0f), Float3.Zero, Float3.Zero);
        return bodies;
    }

    static RoleBody Body(Float3 position, Float3 linearVelocity, Float3 angularVelocity)
    {
        return new RoleBody
        {
            Position = position,
            Rotation = Quaternion.Identity,
            LinearVelocity = linearVelocity,
            AngularVelocity = angularVelocity,
            InverseMass = 1.0f / 70.0f,
            InverseInertiaWorld = default
        };
    }

    static SupportProbe Hit(Float3 point)
    {
        return new SupportProbe
        {
            Hit = 1,
            Distance = 1.0f,
            Point = point,
            Normal = new Float3(0.0f, 1.0f, 0.0f),
            PointVelocity = Float3.Zero
        };
    }

    static SupportProbe Miss()
    {
        return new SupportProbe { Hit = 0, Distance = 10.0f };
    }

    [Fact]
    public void ForwardFallInReach_ExtendsBothArmsTowardContact()
    {
        var p = CatchFallParams.Standard;
        var state = CF.Start(p);
        var bodies = MakeBodies(new Float3(0.0f, -2.0f, 2.0f), Float3.Zero);
        var probes = new CatchFallProbes(
            new CatchFallArmProbe(Hit(new Float3(-0.3f, 0.9f, 0.35f)), 0),
            new CatchFallArmProbe(Hit(new Float3(0.3f, 0.9f, 0.35f)), 0));
        var drives = new JointDrive[Roles.Count];
        var result = CF.Tick(ref state, p, bodies, probes, new Float3(0.0f, 1.0f, 0.0f), 1.0f / 60.0f, drives);

        Assert.Equal(1, state.Active);
        Assert.Equal(new Float3(-0.3f, 0.9f, 0.35f), result.HandTargetLeft);
        Assert.Equal(new Float3(0.3f, 0.9f, 0.35f), result.HandTargetRight);
        Assert.True(drives[(int)Role.UpperArmLeft].Stiffness > 0.0f);
        Assert.True(drives[(int)Role.UpperArmRight].Stiffness > 0.0f);
        Assert.True(drives[(int)Role.HandLeft].Stiffness > p.SafeMissStiffness);
        Assert.True(drives[(int)Role.HandRight].Stiffness > p.SafeMissStiffness);
        Assert.True(Math.Abs(Quaternion.Dot(drives[(int)Role.UpperArmLeft].Target, Quaternion.Identity)) < 1.0f - 1e-4f);
        Assert.Equal(0, result.Touched);
    }

    [Fact]
    public void FallingWithNothingInReach_SafeMissWithoutOverextend()
    {
        var p = CatchFallParams.Standard;
        var state = CF.Start(p);
        var bodies = MakeBodies(new Float3(0.0f, -2.0f, 2.0f), Float3.Zero);
        var probes = new CatchFallProbes(
            new CatchFallArmProbe(Miss(), 0),
            new CatchFallArmProbe(Miss(), 0));
        var drives = new JointDrive[Roles.Count];
        var result = CF.Tick(ref state, p, bodies, probes, new Float3(0.0f, 1.0f, 0.0f), 1.0f / 60.0f, drives);

        Float3 shoulderLeft = bodies[(int)Role.UpperArmLeft].Position;
        Float3 shoulderRight = bodies[(int)Role.UpperArmRight].Position;
        Assert.True((result.HandTargetLeft - shoulderLeft).Length <= p.Reach + 1e-3f);
        Assert.True((result.HandTargetRight - shoulderRight).Length <= p.Reach + 1e-3f);
        Assert.Equal(p.SafeMissStiffness, drives[(int)Role.HandLeft].Stiffness);
        Assert.Equal(p.SafeMissStiffness, drives[(int)Role.HandRight].Stiffness);
        Assert.True(result.HandTargetLeft.IsFinite);
        Assert.True(result.HandTargetRight.IsFinite);
    }

    [Fact]
    public void PostContactArmDrive_RelaxesWithoutOscillationOverSixtyTicks()
    {
        var p = CatchFallParams.Standard;
        var state = CF.Start(p);
        var bodies = MakeBodies(new Float3(0.0f, -0.1f, 0.05f), Float3.Zero);
        var probes = new CatchFallProbes(
            new CatchFallArmProbe(Hit(new Float3(-0.3f, 0.9f, 0.35f)), 1),
            new CatchFallArmProbe(Hit(new Float3(0.3f, 0.9f, 0.35f)), 1));
        var drives = new JointDrive[Roles.Count];
        float previousLeft = float.MaxValue;
        float previousRight = float.MaxValue;
        float firstLeft = 0.0f;
        float firstRight = 0.0f;
        for (int tick = 0; tick < 60; tick++)
        {
            var result = CF.Tick(ref state, p, bodies, probes, new Float3(0.0f, 1.0f, 0.0f), 1.0f / 60.0f, drives);
            Assert.Equal(1, result.Touched);
            Assert.True(state.ArmLeft.StiffnessScale <= previousLeft + 1e-6f);
            Assert.True(state.ArmRight.StiffnessScale <= previousRight + 1e-6f);
            previousLeft = state.ArmLeft.StiffnessScale;
            previousRight = state.ArmRight.StiffnessScale;
            if (tick == 0)
            {
                firstLeft = previousLeft;
                firstRight = previousRight;
            }
            for (int i = 0; i < drives.Length; i++)
            {
                Assert.True(Scalar.IsFinite(drives[i].Stiffness));
                Assert.True(Scalar.IsFinite(drives[i].Damping));
                Assert.True(Scalar.IsFinite(drives[i].MaxTorque));
                Assert.True(drives[i].Target.IsValidRotation);
            }
        }
        Assert.True(previousLeft < firstLeft - 0.05f);
        Assert.True(previousRight < firstRight - 0.05f);
    }

    [Fact]
    public void SpinReduction_OpposesUpAxisAndIsCapped()
    {
        Float3 up = new Float3(0.0f, 1.0f, 0.0f);
        Float3 torque = CF.SpinTorque(up, new Float3(0.3f, 2.0f, -0.4f), 70.0f, 0.5f, 2.0f, 60.0f);
        Assert.True(Float3.Dot(torque, up) < 0.0f);
        Assert.True(torque.Length <= 60.0f + 1e-3f);
        Float3 huge = CF.SpinTorque(up, new Float3(0.0f, 1000.0f, 0.0f), 70.0f, 1.0f, 2.0f, 60.0f);
        Assert.True(Math.Abs(huge.Length - 60.0f) <= 1e-3f);
        Float3 off = CF.SpinTorque(up, new Float3(0.0f, 3.0f, 0.0f), 70.0f, 0.0f, 2.0f, 60.0f);
        Assert.Equal(Float3.Zero, off);
    }

    [Fact]
    public void HostileInputs_ProduceFiniteBoundedOutput()
    {
        var p = CatchFallParams.Standard;
        var state = CF.Start(p);
        var bodies = MakeBodies(new Float3(float.NaN, float.PositiveInfinity, -1e30f), new Float3(float.NaN, 1e30f, float.NegativeInfinity));
        bodies[(int)Role.Torso] = new RoleBody
        {
            Position = new Float3(float.NaN, float.NaN, float.NaN),
            Rotation = new Quaternion(float.NaN, 0.0f, 0.0f, 0.0f),
            LinearVelocity = new Float3(float.PositiveInfinity, 0.0f, 0.0f),
            AngularVelocity = new Float3(0.0f, float.NegativeInfinity, 0.0f),
            InverseMass = float.NaN,
            InverseInertiaWorld = default
        };
        var badProbe = new SupportProbe
        {
            Hit = 1,
            Distance = float.NaN,
            Point = new Float3(float.PositiveInfinity, 0.0f, 0.0f),
            Normal = new Float3(0.0f, float.NaN, 0.0f),
            PointVelocity = new Float3(0.0f, 0.0f, float.NegativeInfinity)
        };
        var probes = new CatchFallProbes(
            new CatchFallArmProbe(badProbe, 1),
            new CatchFallArmProbe(badProbe, 255));
        var drives = new JointDrive[Roles.Count];
        var result = CF.Tick(ref state, p, bodies, probes, new Float3(0.0f, float.NaN, 0.0f), float.NaN, drives);
        for (int i = 0; i < drives.Length; i++)
        {
            Assert.True(Scalar.IsFinite(drives[i].Stiffness));
            Assert.True(Scalar.IsFinite(drives[i].Damping));
            Assert.True(Scalar.IsFinite(drives[i].MaxTorque));
            Assert.True(drives[i].Stiffness >= 0.0f && drives[i].Stiffness <= p.ArmsStiffness + 1e-3f);
            Assert.True(drives[i].Target.IsValidRotation);
        }
        Assert.True(result.HandTargetLeft.IsFinite);
        Assert.True(result.SpinTorque.IsFinite);

        var nasty = new CatchFallParams(float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, 1,
            float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN,
            float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN);
        var state2 = CF.Start(nasty);
        var bodies2 = MakeBodies(new Float3(0.0f, -1.0f, 1.0f), Float3.Zero);
        var result2 = CF.Tick(ref state2, nasty, bodies2, probes, new Float3(0.0f, 1.0f, 0.0f), 1.0f / 60.0f, drives);
        for (int i = 0; i < drives.Length; i++)
            Assert.True(drives[i].Target.IsValidRotation && Scalar.IsFinite(drives[i].Stiffness));
        Assert.True(result2.HandTargetLeft.IsFinite);
    }

    [Fact]
    public void Start_IsDeterministicAndTickRespectsDeactivation()
    {
        var p = CatchFallParams.Standard;
        var first = CF.Start(p);
        var second = CF.Start(p);
        Assert.Equal(first.ArmLeft.StiffnessScale, second.ArmLeft.StiffnessScale);
        Assert.Equal(first.LegLeft, second.LegLeft);
        Assert.Equal(first.Dir, second.Dir);
        Assert.Equal(1, first.Active);

        var bodies = MakeBodies(new Float3(0.0f, -2.0f, 2.0f), Float3.Zero);
        var probes = new CatchFallProbes(
            new CatchFallArmProbe(Hit(new Float3(-0.3f, 0.9f, 0.35f)), 0),
            new CatchFallArmProbe(Hit(new Float3(0.3f, 0.9f, 0.35f)), 0));
        var drives = new JointDrive[Roles.Count];
        CF.Deactivate(ref first);
        CF.Tick(ref first, p, bodies, probes, new Float3(0.0f, 1.0f, 0.0f), 1.0f / 60.0f, drives);
        for (int i = 0; i < drives.Length; i++)
            Assert.Equal(JointDrive.Passive.Target, drives[i].Target);
    }
}
