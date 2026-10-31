using System;
using SA.Harness;
using SA.Humanoid;
using SA.Motion.Numerics;
using SA.ReactionControl;
using SA.ReactionControl.Balance;
using SA.ReactionControl.BalanceTick;
using Xunit;
using Xunit.Abstractions;
using TickDecision = SA.ReactionControl.BalanceTick.BalanceDecision;
using TickInput = SA.ReactionControl.BalanceTick.BalanceInput;

namespace SA.ReactionControl.Tests.BalanceTick;

public class BalanceTickTests
{
    readonly ITestOutputHelper output;

    public BalanceTickTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    static void TickOnce(HarnessWorld world, ref BalanceControllerState state, in BalanceParams p, in TickInput input, JointDrive[] drives, SupportProbe[] probes, out BalanceReport report)
    {
        probes[0] = world.ProbeGround(Role.FootLeft);
        probes[1] = world.ProbeGround(Role.FootRight);
        report = BalanceController.Tick(p, ref state, world.Bodies, probes, input, drives);
        world.ApplyDrives(drives);
        world.Step(1f / 60f);
    }

    static float TorsoTilt(HarnessWorld world)
    {
        var torso = world.Bodies[(int)Role.Torso];
        var axis = Quaternion.Rotate(torso.Rotation, new Float3(0f, 1f, 0f));
        var dot = Float3.Dot(axis, new Float3(0f, 1f, 0f));
        if (dot > 1f)
            dot = 1f;
        if (dot < -1f)
            dot = -1f;
        return (float)Math.Acos(dot) * 57.29578f;
    }

    [Fact]
    public void StandTenSeconds_HoldsHeightAndUpright()
    {
        var world = HarnessWorld.Create();
        var p = BalanceParams.Standard;
        var input = TickInput.Standard;
        var state = BalanceControllerState.Initial(p.MaxStepDelay);
        var drives = new JointDrive[Roles.Count];
        var probes = new SupportProbe[2];
        var startY = world.Bodies[(int)Role.Pelvis].Position.Y;
        var maxDrift = 0f;
        var maxTilt = 0f;
        var supported = 0;
        var report = default(BalanceReport);
        var firstFall = -1;
        for (var tick = 0; tick < 600; tick++)
        {
            TickOnce(world, ref state, p, input, drives, probes, out report);
            if (report.Decision == TickDecision.Falling && firstFall < 0)
            {
                firstFall = tick;
                break;
            }
            var drift = Math.Abs(world.Bodies[(int)Role.Pelvis].Position.Y - startY);
            if (drift > maxDrift)
                maxDrift = drift;
            var tilt = TorsoTilt(world);
            if (tilt > maxTilt)
                maxTilt = tilt;
            if (report.Supported)
                supported++;
        }
        var endY = world.Bodies[(int)Role.Pelvis].Position.Y;
        output.WriteLine($"stand pelvis start {startY:F4} end {endY:F4} maxDrift {maxDrift:F4} maxTilt {maxTilt:F2} supported {supported}/600 decision {report.Decision} firstFall {firstFall}");
        Assert.True(firstFall < 0);
        Assert.True(Math.Abs(endY - startY) <= 0.12f);
        Assert.True(maxTilt <= 15f);
        Assert.True(report.Supported);
    }

    [Fact]
    public void PushEightDirections_NoFallAndSteps()
    {
        var p = BalanceParams.Standard;
        var input = TickInput.Standard;
        var maxStep = 0f;
        var steppedDirs = 0;
        for (var dir = 0; dir < 8; dir++)
        {
            var angle = dir * (float)Math.PI / 4f;
            var push = new Float3((float)Math.Sin(angle), 0f, (float)Math.Cos(angle));
            var world = HarnessWorld.Create();
            var state = BalanceControllerState.Initial(p.MaxStepDelay);
            var drives = new JointDrive[Roles.Count];
            var probes = new SupportProbe[2];
            var report = default(BalanceReport);
            for (var tick = 0; tick < 60; tick++)
                TickOnce(world, ref state, p, input, drives, probes, out report);
            var leftStart = world.Bodies[(int)Role.FootLeft].Position;
            var rightStart = world.Bodies[(int)Role.FootRight].Position;
            var torsoPoint = world.Bodies[(int)Role.Torso].Position;
            world.ApplyImpulse(Role.Torso, push * 60f, torsoPoint);
            var fell = false;
            var best = 0f;
            var bestSide = 0f;
            for (var tick = 0; tick < 300; tick++)
            {
                TickOnce(world, ref state, p, input, drives, probes, out report);
                if (report.Decision == TickDecision.Falling)
                    fell = true;
                var pelvisY = world.Bodies[(int)Role.Pelvis].Position.Y;
                if (pelvisY < 0.5f)
                    fell = true;
                var leftMove = world.Bodies[(int)Role.FootLeft].Position - leftStart;
                var rightMove = world.Bodies[(int)Role.FootRight].Position - rightStart;
                leftMove.Y = 0f;
                rightMove.Y = 0f;
                var step = Math.Max(leftMove.Length, rightMove.Length);
                if (step > best)
                    best = step;
                var side = Math.Max(Float3.Dot(leftMove, push), Float3.Dot(rightMove, push));
                if (side > bestSide)
                    bestSide = side;
            }
            output.WriteLine($"push dir {dir} ({push.X:F2},{push.Z:F2}) fell {fell} decision {report.Decision} best {best:F3} pushSide {bestSide:F3}");
            Assert.False(fell);
            if (best > 0.1f)
                steppedDirs++;
            if (best > maxStep)
                maxStep = best;
        }
        output.WriteLine($"push maxStep {maxStep:F3} steppedDirs {steppedDirs}/8");
        Assert.True(maxStep > 0.1f);
    }

    [Fact]
    public void HugePush_LatchesFalling()
    {
        var world = HarnessWorld.Create();
        var p = BalanceParams.Standard;
        var input = TickInput.Standard;
        var state = BalanceControllerState.Initial(p.MaxStepDelay);
        var drives = new JointDrive[Roles.Count];
        var probes = new SupportProbe[2];
        var report = default(BalanceReport);
        for (var tick = 0; tick < 60; tick++)
            TickOnce(world, ref state, p, input, drives, probes, out report);
        var torsoPoint = world.Bodies[(int)Role.Torso].Position;
        world.ApplyImpulse(Role.Torso, new Float3(400f, 0f, 0f), torsoPoint);
        var firstFall = -1;
        for (var tick = 0; tick < 180; tick++)
        {
            TickOnce(world, ref state, p, input, drives, probes, out report);
            if (report.Decision == TickDecision.Falling && firstFall < 0)
                firstFall = tick;
            if (report.Decision == TickDecision.Falling)
            {
                Assert.Equal(0f, drives[(int)Role.ThighLeft].Stiffness);
                Assert.Equal(0f, drives[(int)Role.Torso].Stiffness);
            }
        }
        output.WriteLine($"huge firstFall {firstFall} final {report.Decision} comHeight {report.ComHeight:F3}");
        Assert.True(firstFall >= 0);
        Assert.True(firstFall <= 30);
        Assert.Equal(TickDecision.Falling, report.Decision);
    }

    [Fact]
    public void Rerun_BitIdentical()
    {
        var p = BalanceParams.Standard;
        var input = TickInput.Standard;
        var first = new float[120 * Roles.Count * 8];
        var second = new float[120 * Roles.Count * 8];
        for (var run = 0; run < 2; run++)
        {
            var world = HarnessWorld.Create();
            var state = BalanceControllerState.Initial(p.MaxStepDelay);
            var drives = new JointDrive[Roles.Count];
            var probes = new SupportProbe[2];
            var report = default(BalanceReport);
            var store = run == 0 ? first : second;
            for (var tick = 0; tick < 120; tick++)
            {
                TickOnce(world, ref state, p, input, drives, probes, out report);
                for (var i = 0; i < Roles.Count; i++)
                {
                    var offset = (tick * Roles.Count + i) * 8;
                    store[offset] = drives[i].Target.X;
                    store[offset + 1] = drives[i].Target.Y;
                    store[offset + 2] = drives[i].Target.Z;
                    store[offset + 3] = drives[i].Target.W;
                    store[offset + 4] = drives[i].Stiffness;
                    store[offset + 5] = drives[i].Damping;
                    store[offset + 6] = drives[i].MaxTorque;
                    store[offset + 7] = drives[i].TargetAngularVelocity.X + drives[i].TargetAngularVelocity.Y + drives[i].TargetAngularVelocity.Z;
                }
            }
        }
        Assert.Equal(first, second);
    }

    [Fact]
    public void Hostile_PassiveAndFinite()
    {
        var p = BalanceParams.Standard;
        var input = TickInput.Standard;
        var state = BalanceControllerState.Initial(p.MaxStepDelay);
        var drives = new JointDrive[Roles.Count];
        var world = HarnessWorld.Create();
        var bodies = world.Bodies.ToArray();
        bodies[(int)Role.Torso].Position = new Float3(float.NaN, 0f, 0f);
        var probes = new SupportProbe[2];
        probes[0] = world.ProbeGround(Role.FootLeft);
        probes[1] = world.ProbeGround(Role.FootRight);
        var report = BalanceController.Tick(p, ref state, bodies, probes, input, drives);
        for (var i = 0; i < Roles.Count; i++)
            Assert.Equal(0f, drives[i].Stiffness);
        Assert.True(Scalar.IsFinite(report.ComHeight));
        bodies[(int)Role.Torso].Position = new Float3(float.PositiveInfinity, 0f, 0f);
        report = BalanceController.Tick(p, ref state, bodies, probes, input, drives);
        for (var i = 0; i < Roles.Count; i++)
            Assert.Equal(0f, drives[i].Stiffness);
        Assert.True(Scalar.IsFinite(report.ComHeight));
        var huge = world.Bodies.ToArray();
        huge[(int)Role.Torso].Position = new Float3(100000f, 0f, 0f);
        report = BalanceController.Tick(p, ref state, huge, probes, input, drives);
        Assert.True(Scalar.IsFinite(report.ComHeight));
        Assert.True(Scalar.IsFinite(report.StepDistance));
        for (var i = 0; i < Roles.Count; i++)
        {
            Assert.True(Scalar.IsFinite(drives[i].Stiffness));
            Assert.True(Scalar.IsFinite(drives[i].Damping));
            Assert.True(Scalar.IsFinite(drives[i].MaxTorque));
            Assert.True(drives[i].Target.IsValidRotation);
        }
    }

    [Fact]
    public void ShortSpans_Safe()
    {
        var p = BalanceParams.Standard;
        var input = TickInput.Standard;
        var state = BalanceControllerState.Initial(p.MaxStepDelay);
        var drives = new JointDrive[5];
        var bodies = new RoleBody[3];
        var probes = new SupportProbe[1];
        var report = BalanceController.Tick(p, ref state, bodies, probes, input, drives);
        Assert.Equal(TickDecision.Falling, report.Decision);
        Assert.True(Scalar.IsFinite(report.ComHeight));
        for (var i = 0; i < drives.Length; i++)
            Assert.Equal(0f, drives[i].Stiffness);
    }

    [Fact]
    public void CountdownExpired_LatchesFalling()
    {
        var world = HarnessWorld.Create();
        var p = BalanceParams.Standard;
        var input = TickInput.Standard;
        var state = BalanceControllerState.Initial(p.MaxStepDelay);
        var drives = new JointDrive[Roles.Count];
        var probes = new SupportProbe[2];
        state.Feet.Countdown = -1;
        probes[0] = world.ProbeGround(Role.FootLeft);
        probes[1] = world.ProbeGround(Role.FootRight);
        var report = BalanceController.Tick(p, ref state, world.Bodies, probes, input, drives);
        Assert.Equal(TickDecision.Falling, report.Decision);
        Assert.True(state.FallingLatched);
        var fresh = HarnessWorld.Create();
        probes[0] = fresh.ProbeGround(Role.FootLeft);
        probes[1] = fresh.ProbeGround(Role.FootRight);
        report = BalanceController.Tick(p, ref state, fresh.Bodies, probes, input, drives);
        Assert.Equal(TickDecision.Falling, report.Decision);
    }

    [Fact]
    public void DriveOwnership_OnlyLegsTorsoPelvis()
    {
        var world = HarnessWorld.Create();
        var p = BalanceParams.Standard;
        var input = TickInput.Standard;
        var state = BalanceControllerState.Initial(p.MaxStepDelay);
        var drives = new JointDrive[Roles.Count];
        var probes = new SupportProbe[2];
        probes[0] = world.ProbeGround(Role.FootLeft);
        probes[1] = world.ProbeGround(Role.FootRight);
        var report = BalanceController.Tick(p, ref state, world.Bodies, probes, input, drives);
        Assert.True(report.Decision != TickDecision.Falling);
        var owned = new[] { Role.Pelvis, Role.Torso, Role.ThighLeft, Role.ThighRight, Role.ShinLeft, Role.ShinRight, Role.FootLeft, Role.FootRight };
        for (var i = 0; i < Roles.Count; i++)
        {
            var isOwned = Array.IndexOf(owned, (Role)i) >= 0;
            if (!isOwned)
                Assert.Equal(0f, drives[i].Stiffness);
        }
    }

    [Fact]
    public void Taper_ScalesBentKnee()
    {
        var world = HarnessWorld.Create();
        var bodies = world.Bodies.ToArray();
        for (var i = 0; i < Roles.Count; i++)
        {
            var role = (Role)i;
            if (role != Role.FootLeft && role != Role.FootRight && role != Role.ShinLeft && role != Role.ShinRight && role != Role.ThighLeft && role != Role.ThighRight)
            {
                var body = bodies[i];
                body.Position = body.Position - new Float3(0f, 0.3f, 0f);
                bodies[i] = body;
            }
        }
        var input = TickInput.Standard;
        var probes = new SupportProbe[2];
        probes[0] = new SupportProbe { Hit = 1, Point = bodies[(int)Role.FootLeft].Position, Normal = new Float3(0f, 1f, 0f) };
        probes[1] = new SupportProbe { Hit = 1, Point = bodies[(int)Role.FootRight].Position, Normal = new Float3(0f, 1f, 0f) };
        var plain = BalanceParams.Standard;
        var tapered = BalanceParams.Standard;
        tapered.Taper = true;
        var plainState = BalanceControllerState.Initial(plain.MaxStepDelay);
        var taperState = BalanceControllerState.Initial(tapered.MaxStepDelay);
        var plainDrives = new JointDrive[Roles.Count];
        var taperDrives = new JointDrive[Roles.Count];
        var plainReport = BalanceController.Tick(plain, ref plainState, bodies, probes, input, plainDrives);
        var taperReport = BalanceController.Tick(tapered, ref taperState, bodies, probes, input, taperDrives);
        Assert.True(plainReport.Decision != TickDecision.Falling);
        Assert.True(taperReport.Decision != TickDecision.Falling);
        output.WriteLine($"taper plain {plainDrives[(int)Role.ShinLeft].Stiffness:F2} tapered {taperDrives[(int)Role.ShinLeft].Stiffness:F2}");
        Assert.True(taperDrives[(int)Role.ShinLeft].Stiffness <= plainDrives[(int)Role.ShinLeft].Stiffness);
    }

    [Fact]
    public void WideStep_RunsFinite()
    {
        var world = HarnessWorld.Create();
        var p = BalanceParams.Standard;
        var narrow = TickInput.Standard;
        var wide = new TickInput { Timestep = 1f / 60f, WideStep = true };
        var narrowState = BalanceControllerState.Initial(p.MaxStepDelay);
        var wideState = BalanceControllerState.Initial(p.MaxStepDelay);
        var narrowDrives = new JointDrive[Roles.Count];
        var wideDrives = new JointDrive[Roles.Count];
        var probes = new SupportProbe[2];
        probes[0] = world.ProbeGround(Role.FootLeft);
        probes[1] = world.ProbeGround(Role.FootRight);
        var narrowReport = BalanceController.Tick(p, ref narrowState, world.Bodies, probes, narrow, narrowDrives);
        var wideReport = BalanceController.Tick(p, ref wideState, world.Bodies, probes, wide, wideDrives);
        Assert.True(Scalar.IsFinite(narrowReport.ComHeight));
        Assert.True(Scalar.IsFinite(wideReport.ComHeight));
    }
}
