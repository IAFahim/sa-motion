using SA.Humanoid;
using SA.Motion.Numerics;
using SA.ReactionControl.Brace;
using Xunit;
using BraceApi = SA.ReactionControl.Brace.Brace;

namespace SA.ReactionControl.Tests.Totality;

public class BraceTotality
{
    static BraceParams Params(int i)
    {
        return new BraceParams(
            Fuzz.Pick(i), Fuzz.Pick(i + 1), Fuzz.Pick(i + 2), Fuzz.Pick(i + 3),
            Fuzz.Pick(i + 4), Fuzz.Pick(i + 5), Fuzz.Pick(i + 6), Fuzz.Pick(i + 7),
            Fuzz.Pick(i + 8), Fuzz.Pick(i + 9), Fuzz.Pick(i + 10), Fuzz.Pick(i + 11),
            Fuzz.Pick(i + 12), Fuzz.Pick(i + 13), Fuzz.Pick(i + 14), Fuzz.Pick(i + 15),
            Fuzz.Pick(i + 16), Fuzz.Pick(i + 17), Fuzz.Pick(i + 18), Fuzz.Pick(i + 19));
    }

    [Fact]
    public void BraceTick_Totality()
    {
        var bodies = new RoleBody[Roles.Count];
        var drives = new JointDrive[Roles.Count];
        for (var i = 0; i < Fuzz.Combos; i++)
        {
            var p = Params(i * 7);
            for (var b = 0; b < bodies.Length; b++)
            {
                bodies[b] = Fuzz.Body(i * 19 + b * 31, i + b);
            }
            var state = default(BraceState);
            if ((i & 3) == 0)
            {
                BraceApi.Activate(ref state);
            }
            else
            {
                state.Phase = (BracePhase)(byte)(i * 13);
                state.ArmMask = (byte)(i * 17);
                state.SmoothedDistance = Fuzz.Pick(i * 23);
                state.HoldTimer = Fuzz.Pick(i * 29);
                state.StaggerLeft = Fuzz.Pick(i * 31);
                state.StaggerRight = Fuzz.Pick(i * 37);
                state.Lean = Fuzz.Pick(i * 41);
                state.ReachLeft = Fuzz.Vec(i * 43);
                state.ReachRight = Fuzz.Vec(i * 47);
                state.ContactLeft = Fuzz.Vec(i * 53);
                state.ContactRight = Fuzz.Vec(i * 59);
                state.NormalLeft = Fuzz.Vec(i * 61);
                state.NormalRight = Fuzz.Vec(i * 67);
            }
            var wall = Fuzz.Probe(i * 71, i);
            var touchLeft = Fuzz.Probe(i * 73, i + 1);
            var touchRight = Fuzz.Probe(i * 79, i + 2);
            var hint = new BalanceHint(Fuzz.Pick(i * 83), Fuzz.Pick(i * 89));
            var result = BraceApi.Tick(p, ref state, bodies, wall, touchLeft, touchRight, hint, Fuzz.Pick(i * 97), drives);
            Fuzz.Drives(bodies, drives);
            Fuzz.Value(result.SmoothedDistance);
            Fuzz.Value(result.LeanIntent);
            Fuzz.Value(result.CrouchIntent);
            Fuzz.Value(result.ReachLeft);
            Fuzz.Value(result.ReachRight);
        }
    }

    [Fact]
    public void BraceSpans_RejectWithoutThrow()
    {
        var p = BraceParams.Standard;
        var hint = BalanceHint.Supported;
        var wall = Fuzz.Probe(1, 0);
        for (var k = 0; k < 8; k++)
        {
            var shortBodies = Fuzz.Bodies(k * 101, Roles.Count - 1);
            var shortDrives = new JointDrive[Roles.Count - 1];
            var state = default(BraceState);
            BraceApi.Tick(p, ref state, shortBodies, wall, wall, wall, hint, 0.0167f, shortDrives);
            var fullBodies = Fuzz.Bodies(k * 103, Roles.Count);
            var emptyDrives = new JointDrive[0];
            var state2 = default(BraceState);
            BraceApi.Tick(p, ref state2, fullBodies, wall, wall, wall, hint, 0.0167f, emptyDrives);
        }
        var activate = default(BraceState);
        for (var i = 0; i < Fuzz.Combos; i++)
        {
            activate.Phase = (BracePhase)(byte)i;
            activate.SmoothedDistance = Fuzz.Pick(i);
            BraceApi.Activate(ref activate);
            Assert.Equal(BracePhase.Idle, activate.Phase);
        }
    }
}
