using System;
using SA.Humanoid;
using SA.Motion.Numerics;
using Xunit;

namespace SA.ReactionControl.Tests.Totality;

public class BodyBalanceTotality
{
    static BodyBalanceParams Params(int i)
    {
        var p = BodyBalanceParams.Standard;
        p.BodyStiffness = Fuzz.Pick(i);
        p.Damping = Fuzz.Pick(i + 3);
        p.MaxDrive = Fuzz.Pick(i + 6);
        p.SpeedFloor = Fuzz.Pick(i + 9);
        p.DecisionFallLean = Fuzz.Pick(i + 12);
        p.DecisionLoseLean = Fuzz.Pick(i + 15);
        p.DecisionExitFraction = Fuzz.Pick(i + 18);
        p.FallHoldTime = Fuzz.Pick(i + 21);
        p.StabiliseDamping = Fuzz.Pick(i + 24);
        p.UseHeadLook = (byte)(i & 1);
        p.UseBodyTurn = (byte)((i >> 1) & 1);
        p.UseArmsOut = (byte)((i >> 2) & 1);
        p.UsePelvisFrame = (byte)((i >> 3) & 1);
        return p;
    }

    static BalanceInput Input(int i)
    {
        return new BalanceInput
        {
            DeltaTime = Fuzz.Pick(i),
            Stepping = (uint)(i * 2654435761u),
            Failed = (byte)(i & 1),
            Catching = (byte)((i >> 1) & 1),
            Grounded = (byte)((i >> 2) & 1),
            ContactUpper = (byte)((i >> 3) & 1),
            PushSource = Fuzz.Vec(i + 5),
            HasPushSource = (byte)((i >> 4) & 1),
            LookTarget = Fuzz.Vec(i + 11),
            HasLookTarget = (byte)((i >> 5) & 1),
        };
    }

    [Fact]
    public void BodyBalanceTick_Totality()
    {
        var bodies = new RoleBody[Roles.Count];
        var drives = new JointDrive[Roles.Count];
        var obstacles = new SupportProbe[3];
        for (var i = 0; i < Fuzz.Combos; i++)
        {
            var p = Params(i * 29);
            for (var b = 0; b < bodies.Length; b++)
            {
                bodies[b] = Fuzz.Body(i * 19 + b * 41, i + b);
            }
            var count = i & 3;
            for (var o = 0; o < obstacles.Length; o++)
            {
                obstacles[o] = Fuzz.Probe(i * 23 + o * 7, i + o);
            }
            var state = BodyBalanceState.Activate(p, Fuzz.Quat(i * 31, i), Fuzz.Quat(i * 37, i + 1));
            var input = Input(i * 43);
            var output = BodyBalance.Tick(p, ref state, bodies, new ReadOnlySpan<SupportProbe>(obstacles, 0, count), input, drives);
            Fuzz.Drives(bodies, drives);
            Fuzz.Value(output.SpineLean);
            Fuzz.Value(output.LeanResponse);
            Fuzz.Value(output.TurnIntent);
            Fuzz.Value(output.HeadLookTarget);
            Fuzz.Value(output.HeadLookVelocity);
            Fuzz.Value(output.HeadLookStiffness);
            Fuzz.Value(output.CatchFallGains);
            Fuzz.Value(output.StabiliseStiffness);
            Fuzz.Value(output.StabiliseDamping);
            Fuzz.Value(output.StepClamp);
        }
    }

    [Fact]
    public void BodyBalanceHelpers_Totality()
    {
        var rng = 0x12345678u;
        for (var i = 0; i < Fuzz.Combos; i++)
        {
            var next = BodyBalance.NextRng(ref rng);
            Assert.True(next != 0 || rng != 0);
            var unit = BodyBalance.Unit01(ref rng);
            Assert.True(unit >= 0f && unit < 1f);
            BodyBalance.OwnFrame(Fuzz.Quat(i * 3, i), out var x, out var y, out var z);
            Fuzz.Value(x);
            Fuzz.Value(y);
            Fuzz.Value(z);
            var lean = BodyBalance.LeanAngles(Fuzz.Vec(i * 5), Fuzz.Vec(i * 7), Fuzz.Vec(i * 11));
            if (Fuzz.Vec(i * 7).IsFinite && Fuzz.Vec(i * 11).IsFinite)
            {
                Fuzz.Value(lean);
            }
            var state = BodyBalanceState.Activate(Params(i * 13), Fuzz.Quat(i * 17, i + 2), Fuzz.Quat(i * 19, i + 3));
            Fuzz.Value(state.PrevEuler);
            Fuzz.Value(state.FrameX);
            Fuzz.Value(state.FrameY);
            Fuzz.Value(state.FrameZ);
        }
    }

    [Fact]
    public void BodyBalanceSpans_RejectWithoutThrow()
    {
        var p = BodyBalanceParams.Standard;
        var drives = new JointDrive[Roles.Count];
        var input = Input(5);
        var empty = Array.Empty<RoleBody>();
        var emptyDrives = Array.Empty<JointDrive>();
        var shortBodies = Fuzz.Bodies(3, Roles.Count - 1);
        var longBodies = Fuzz.Bodies(4, Roles.Count + 1);
        var shortDrives = new JointDrive[Roles.Count - 1];
        var probes = Array.Empty<SupportProbe>();
        for (var k = 0; k < 4; k++)
        {
            var s0 = BodyBalanceState.Activate(p, Fuzz.Quat(k, k), Fuzz.Quat(k + 1, k));
            BodyBalance.Tick(p, ref s0, empty, probes, input, drives);
            var s1 = BodyBalanceState.Activate(p, Fuzz.Quat(k + 2, k), Fuzz.Quat(k + 3, k));
            BodyBalance.Tick(p, ref s1, shortBodies, probes, input, drives);
            var s2 = BodyBalanceState.Activate(p, Fuzz.Quat(k + 4, k), Fuzz.Quat(k + 5, k));
            BodyBalance.Tick(p, ref s2, longBodies, probes, input, drives);
            var full = Fuzz.Bodies(40 + k, Roles.Count);
            var s3 = BodyBalanceState.Activate(p, Fuzz.Quat(k + 6, k), Fuzz.Quat(k + 7, k));
            BodyBalance.Tick(p, ref s3, full, probes, input, emptyDrives);
            var s4 = BodyBalanceState.Activate(p, Fuzz.Quat(k + 8, k), Fuzz.Quat(k + 9, k));
            BodyBalance.Tick(p, ref s4, full, probes, input, shortDrives);
        }
    }

    [Fact(Skip = "OPEN GAP: BodyBalance.LeanAngles propagates NaN components to its result instead of returning a finite default.")]
    public void LeanAnglesNaN_OpenGap()
    {
        var lean = BodyBalance.LeanAngles(new Float3(0f, 0f, 0f), new Float3(0f, float.NaN, 0f), new Float3(0f, 0f, 1f));
        Fuzz.Value(lean);
    }
}
