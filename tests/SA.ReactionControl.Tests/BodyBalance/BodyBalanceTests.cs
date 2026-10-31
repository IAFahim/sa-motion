using System;
using SA.Humanoid;
using SA.Motion.Numerics;
using Xunit;

namespace SA.ReactionControl.Tests;

public sealed class BodyBalanceTests
{
    private const float Dt = 1f / 60f;

    private static RoleBody[] UprightBodies()
    {
        var bodies = new RoleBody[Roles.Count];
        for (int i = 0; i < bodies.Length; i++)
        {
            bodies[i] = new RoleBody
            {
                Position = new Float3(0f, 1f, 0f),
                Rotation = Quaternion.Identity,
                LinearVelocity = Float3.Zero,
                AngularVelocity = Float3.Zero,
                InverseMass = 1f,
                InverseInertiaWorld = default,
            };
        }
        bodies[(int)Role.Torso] = WithPose(bodies[(int)Role.Torso], new Float3(0f, 1.3f, 0f));
        bodies[(int)Role.Head] = WithPose(bodies[(int)Role.Head], new Float3(0f, 1.65f, 0f));
        bodies[(int)Role.UpperArmLeft] = WithPose(bodies[(int)Role.UpperArmLeft], new Float3(-0.25f, 1.45f, 0f));
        bodies[(int)Role.UpperArmRight] = WithPose(bodies[(int)Role.UpperArmRight], new Float3(0.25f, 1.45f, 0f));
        return bodies;
    }

    private static RoleBody WithPose(RoleBody body, Float3 position)
    {
        body.Position = position;
        return body;
    }

    private static RoleBody WithTilt(RoleBody body, float radiansAboutX)
    {
        body.Rotation = Quaternion.Normalize(Quaternion.AxisAngle(new Float3(1f, 0f, 0f), radiansAboutX));
        return body;
    }

    private static BalanceInput LiveInput()
    {
        return new BalanceInput { DeltaTime = Dt, Grounded = 1 };
    }

    private static JointDrive[] PassiveDrives()
    {
        var drives = new JointDrive[Roles.Count];
        for (int i = 0; i < drives.Length; i++)
        {
            drives[i] = JointDrive.Passive;
        }
        return drives;
    }

    private static void AssertOwnedDrivesUsable(JointDrive[] drives, float maxDrive)
    {
        int[] owned = new int[] { (int)Role.Torso, (int)Role.Head, (int)Role.UpperArmLeft, (int)Role.UpperArmRight, (int)Role.ForearmLeft, (int)Role.ForearmRight };
        foreach (int i in owned)
        {
            Assert.True(drives[i].Target.IsValidRotation);
            Assert.True(Scalar.IsFinite(drives[i].Stiffness));
            Assert.True(Scalar.IsFinite(drives[i].Damping));
            Assert.InRange(drives[i].Stiffness, 0f, maxDrive);
            Assert.InRange(drives[i].Damping, 0f, maxDrive);
        }
    }

    [Fact]
    public void SmallPush_StaysRecoveringWithBoundedResponse()
    {
        var p = BodyBalanceParams.Standard;
        var bodies = UprightBodies();
        var pelvis = bodies[(int)Role.Pelvis];
        pelvis.LinearVelocity = new Float3(1.5f, 0f, 0f);
        bodies[(int)Role.Pelvis] = pelvis;
        var torso = bodies[(int)Role.Torso];
        torso = WithTilt(torso, 0.1f);
        bodies[(int)Role.Torso] = torso;
        var s = BodyBalanceState.Activate(in p, bodies[(int)Role.Torso].Rotation, bodies[(int)Role.Pelvis].Rotation);
        var drives = PassiveDrives();
        var input = LiveInput();
        var output = BodyBalance.Tick(in p, ref s, bodies, Array.Empty<SupportProbe>(), in input, drives);
        Assert.Equal(BalanceDecision.Recovering, output.Decision);
        Assert.True(output.LeanResponse < p.DecisionLoseLean);
        AssertOwnedDrivesUsable(drives, p.MaxDrive);
        Assert.InRange(Math.Abs(output.SpineLean.X), 0f, 0.6f);
        Assert.InRange(Math.Abs(output.SpineLean.Y), 0f, 0.6f);
        Assert.InRange(Math.Abs(output.SpineLean.Z), 0f, 0.6f);
    }

    [Fact]
    public void LargePush_FallsAndHoldsWithoutFlicker()
    {
        var p = BodyBalanceParams.Standard;
        var bodies = UprightBodies();
        var pelvis = bodies[(int)Role.Pelvis];
        pelvis.LinearVelocity = new Float3(6f, 0f, 0f);
        bodies[(int)Role.Pelvis] = pelvis;
        bodies[(int)Role.Torso] = WithTilt(bodies[(int)Role.Torso], 0.9f);
        var s = BodyBalanceState.Activate(in p, Quaternion.Identity, Quaternion.Identity);
        var drives = PassiveDrives();
        var input = LiveInput();
        input.Failed = 1;
        for (int i = 0; i < 5; i++)
        {
            var output = BodyBalance.Tick(in p, ref s, bodies, Array.Empty<SupportProbe>(), in input, drives);
            Assert.Equal(BalanceDecision.Falling, output.Decision);
            Assert.Equal((byte)1, output.HasCatchFall);
        }
        input.Failed = 0;
        var calm = UprightBodies();
        for (int i = 0; i < 30; i++)
        {
            var output = BodyBalance.Tick(in p, ref s, calm, Array.Empty<SupportProbe>(), in input, drives);
            Assert.Equal(BalanceDecision.Falling, output.Decision);
        }
        BalanceOutput late = default;
        for (int i = 0; i < 40; i++)
        {
            late = BodyBalance.Tick(in p, ref s, calm, Array.Empty<SupportProbe>(), in input, drives);
        }
        Assert.Equal(BalanceDecision.Recovering, late.Decision);
    }

    [Fact]
    public void MetricDrivenLargeTilt_FallsWithoutFailedFlag()
    {
        var p = BodyBalanceParams.Standard;
        var bodies = UprightBodies();
        var pelvis = bodies[(int)Role.Pelvis];
        pelvis.LinearVelocity = new Float3(6f, 0f, 0f);
        bodies[(int)Role.Pelvis] = pelvis;
        bodies[(int)Role.Torso] = WithTilt(bodies[(int)Role.Torso], 1.2f);
        var s = BodyBalanceState.Activate(in p, Quaternion.Identity, Quaternion.Identity);
        var drives = PassiveDrives();
        var input = LiveInput();
        var output = BodyBalance.Tick(in p, ref s, bodies, Array.Empty<SupportProbe>(), in input, drives);
        Assert.Equal(BalanceDecision.Falling, output.Decision);
    }

    [Fact]
    public void HeadLook_PointsAtPushSourceWithinTwoDegrees()
    {
        var p = BodyBalanceParams.Standard;
        var bodies = UprightBodies();
        var s = BodyBalanceState.Activate(in p, Quaternion.Identity, Quaternion.Identity);
        var drives = PassiveDrives();
        var input = LiveInput();
        input.PushSource = new Float3(5f, 1f, 0f);
        input.HasPushSource = 1;
        var output = BodyBalance.Tick(in p, ref s, bodies, Array.Empty<SupportProbe>(), in input, drives);
        Assert.Equal((byte)1, output.HasHeadLook);
        Float3 headPos = bodies[(int)Role.Head].Position;
        Float3 toTarget = output.HeadLookTarget - headPos;
        Float3 toPush = input.PushSource - headPos;
        float cosine = Float3.Dot(toTarget, toPush) / (toTarget.Length * toPush.Length);
        Assert.True(cosine > 0.99939f);
    }

    [Fact]
    public void HeadLook_PrefersNearestProbeOverPushSource()
    {
        var p = BodyBalanceParams.Standard;
        var bodies = UprightBodies();
        var s = BodyBalanceState.Activate(in p, Quaternion.Identity, Quaternion.Identity);
        var drives = PassiveDrives();
        var input = LiveInput();
        input.PushSource = new Float3(5f, 1f, 0f);
        input.HasPushSource = 1;
        var probes = new SupportProbe[1];
        probes[0] = new SupportProbe { Hit = 1, Distance = 2f, Point = new Float3(2f, 1f, 0f), Normal = new Float3(0f, 1f, 0f), PointVelocity = new Float3(0f, 0f, 1f) };
        var output = BodyBalance.Tick(in p, ref s, bodies, probes, in input, drives);
        Assert.Equal((byte)1, output.HasHeadLook);
        Assert.Equal(2f, output.HeadLookTarget.X);
        Assert.Equal(1f, output.HeadLookVelocity.Z);
    }

    [Fact]
    public void Decision_IsDeterministic()
    {
        var p = BodyBalanceParams.Standard;
        var bodies = UprightBodies();
        bodies[(int)Role.Torso] = WithTilt(bodies[(int)Role.Torso], 0.4f);
        var input = LiveInput();
        input.Stepping = 2;
        var a = BodyBalanceState.Activate(in p, Quaternion.Identity, Quaternion.Identity);
        var b = BodyBalanceState.Activate(in p, Quaternion.Identity, Quaternion.Identity);
        var drivesA = PassiveDrives();
        var drivesB = PassiveDrives();
        for (int i = 0; i < 10; i++)
        {
            var outA = BodyBalance.Tick(in p, ref a, bodies, Array.Empty<SupportProbe>(), in input, drivesA);
            var outB = BodyBalance.Tick(in p, ref b, bodies, Array.Empty<SupportProbe>(), in input, drivesB);
            Assert.Equal(outA.Decision, outB.Decision);
            Assert.Equal(outA.LeanResponse, outB.LeanResponse);
            Assert.Equal(outA.SpineLean.X, outB.SpineLean.X);
            Assert.Equal(drivesA[(int)Role.Torso].Stiffness, drivesB[(int)Role.Torso].Stiffness);
            Assert.Equal(drivesA[(int)Role.ForearmLeft].Target.X, drivesB[(int)Role.ForearmLeft].Target.X);
        }
    }

    [Fact]
    public void LeanThreshold_HasHysteresis()
    {
        var p = BodyBalanceParams.Standard;
        var input = LiveInput();
        var drives = PassiveDrives();
        var s = BodyBalanceState.Activate(in p, Quaternion.Identity, Quaternion.Identity);
        var bodies = UprightBodies();
        BalanceOutput output = default;
        for (int i = 1; i <= 50; i++)
        {
            bodies[(int)Role.Torso] = WithTilt(UprightBodies()[(int)Role.Torso], i * 0.01f);
            output = BodyBalance.Tick(in p, ref s, bodies, Array.Empty<SupportProbe>(), in input, drives);
        }
        Assert.Equal(BalanceDecision.LosingBalance, output.Decision);
        for (int i = 49; i >= 40; i--)
        {
            bodies[(int)Role.Torso] = WithTilt(UprightBodies()[(int)Role.Torso], i * 0.01f);
            output = BodyBalance.Tick(in p, ref s, bodies, Array.Empty<SupportProbe>(), in input, drives);
        }
        Assert.Equal(BalanceDecision.LosingBalance, output.Decision);
        for (int i = 39; i >= 20; i--)
        {
            bodies[(int)Role.Torso] = WithTilt(UprightBodies()[(int)Role.Torso], i * 0.01f);
            output = BodyBalance.Tick(in p, ref s, bodies, Array.Empty<SupportProbe>(), in input, drives);
        }
        output = BodyBalance.Tick(in p, ref s, bodies, Array.Empty<SupportProbe>(), in input, drives);
        Assert.Equal(BalanceDecision.Recovering, output.Decision);
    }

    [Fact]
    public void FallThreshold_HasHysteresisAndHold()
    {
        var p = BodyBalanceParams.Standard;
        var input = LiveInput();
        var drives = PassiveDrives();
        var s = BodyBalanceState.Activate(in p, Quaternion.Identity, Quaternion.Identity);
        var calm = UprightBodies();
        input.Failed = 1;
        var output = BodyBalance.Tick(in p, ref s, calm, Array.Empty<SupportProbe>(), in input, drives);
        Assert.Equal(BalanceDecision.Falling, output.Decision);
        input.Failed = 0;
        int holdTicks = (int)(p.FallHoldTime / Dt) - 2;
        for (int i = 0; i < holdTicks; i++)
        {
            output = BodyBalance.Tick(in p, ref s, calm, Array.Empty<SupportProbe>(), in input, drives);
            Assert.Equal(BalanceDecision.Falling, output.Decision);
        }
        for (int i = 0; i < 10; i++)
        {
            output = BodyBalance.Tick(in p, ref s, calm, Array.Empty<SupportProbe>(), in input, drives);
        }
        Assert.Equal(BalanceDecision.Recovering, output.Decision);
    }

    [Fact]
    public void HostileInputs_ProduceFiniteBoundedPassiveOutput()
    {
        var p = BodyBalanceParams.Standard;
        var s = BodyBalanceState.Activate(in p, Quaternion.Identity, Quaternion.Identity);
        var drives = PassiveDrives();
        var bodies = UprightBodies();
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            b.Position = new Float3(float.NaN, float.PositiveInfinity, 1e30f);
            b.LinearVelocity = new Float3(float.NegativeInfinity, float.NaN, 1e30f);
            b.Rotation = new Quaternion(float.NaN, 0f, 0f, 0f);
            bodies[i] = b;
        }
        var input = new BalanceInput { DeltaTime = float.PositiveInfinity, Grounded = 1, PushSource = new Float3(float.NaN, 0f, 0f), HasPushSource = 1 };
        var output = BodyBalance.Tick(in p, ref s, bodies, Array.Empty<SupportProbe>(), in input, drives);
        Assert.Equal(BalanceDecision.Falling, output.Decision);
        foreach (var d in drives)
        {
            Assert.True(Scalar.IsFinite(d.Stiffness));
            Assert.True(Scalar.IsFinite(d.Damping));
            Assert.InRange(d.Stiffness, 0f, p.MaxDrive);
            Assert.Equal(0f, d.Stiffness);
        }
        Assert.True(Scalar.IsFinite(output.LeanResponse));
        var s2 = BodyBalanceState.Activate(in p, Quaternion.Identity, Quaternion.Identity);
        var big = UprightBodies();
        for (int i = 0; i < big.Length; i++)
        {
            var b = big[i];
            b.Position = new Float3(1e30f, -1e30f, 1e30f);
            b.LinearVelocity = new Float3(1e20f, 0f, 0f);
            big[i] = b;
        }
        var badDt = new BalanceInput { DeltaTime = -5f, Grounded = 1 };
        var drives2 = PassiveDrives();
        var output2 = BodyBalance.Tick(in p, ref s2, big, Array.Empty<SupportProbe>(), in badDt, drives2);
        AssertOwnedDrivesUsable(drives2, p.MaxDrive);
        Assert.True(Scalar.IsFinite(output2.LeanResponse));
        Assert.True(output2.SpineLean.IsFinite);
    }

    [Fact]
    public void WrongSpanLengths_FallWithoutThrow()
    {
        var p = BodyBalanceParams.Standard;
        var s = BodyBalanceState.Activate(in p, Quaternion.Identity, Quaternion.Identity);
        var input = LiveInput();
        var shortBodies = new RoleBody[3];
        var drives = PassiveDrives();
        var output = BodyBalance.Tick(in p, ref s, shortBodies, Array.Empty<SupportProbe>(), in input, drives);
        Assert.Equal(BalanceDecision.Falling, output.Decision);
        var bodies = UprightBodies();
        var shortDrives = new JointDrive[5];
        for (int i = 0; i < shortDrives.Length; i++)
        {
            shortDrives[i] = new JointDrive { Stiffness = 7f };
        }
        output = BodyBalance.Tick(in p, ref s, bodies, Array.Empty<SupportProbe>(), in input, shortDrives);
        Assert.Equal(BalanceDecision.Falling, output.Decision);
        Assert.Equal(7f, shortDrives[0].Stiffness);
    }
}
