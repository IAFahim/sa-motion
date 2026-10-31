using SA.Humanoid;
using SA.Motion.Numerics;
using SA.ReactionControl.CatchFall;
using Xunit;
using CatchFallApi = SA.ReactionControl.CatchFall.CatchFall;

namespace SA.ReactionControl.Tests.Totality;

public class CatchFallTotality
{
    static CatchFallParams Params(int i)
    {
        return new CatchFallParams(
            Fuzz.Pick(i), Fuzz.Pick(i + 1), Fuzz.Pick(i + 2), Fuzz.Pick(i + 3),
            Fuzz.Pick(i + 4), Fuzz.Pick(i + 5), (byte)(i & 1), Fuzz.Pick(i + 7),
            Fuzz.Pick(i + 8), Fuzz.Pick(i + 9), Fuzz.Pick(i + 10), Fuzz.Pick(i + 11),
            Fuzz.Pick(i + 12), Fuzz.Pick(i + 13), Fuzz.Pick(i + 14), Fuzz.Pick(i + 15),
            Fuzz.Pick(i + 16), Fuzz.Pick(i + 17), Fuzz.Pick(i + 18), Fuzz.Pick(i + 19),
            Fuzz.Pick(i + 20), Fuzz.Pick(i + 21), Fuzz.Pick(i + 22), Fuzz.Pick(i + 23));
    }

    static CatchFallProbes Probes(int i)
    {
        return new CatchFallProbes(
            new CatchFallArmProbe(Fuzz.Probe(i, i), (byte)(i & 1)),
            new CatchFallArmProbe(Fuzz.Probe(i + 11, i + 1), (byte)((i >> 1) & 1)));
    }

    [Fact]
    public void CatchFallTick_Totality()
    {
        var bodies = new RoleBody[Roles.Count];
        var drives = new JointDrive[Roles.Count];
        for (var i = 0; i < Fuzz.Combos; i++)
        {
            var p = Params(i * 5);
            for (var b = 0; b < bodies.Length; b++)
            {
                bodies[b] = Fuzz.Body(i * 23 + b * 29, i + b);
            }
            var state = CatchFallApi.Start(p);
            if ((i & 1) == 0)
            {
                CatchFallApi.Deactivate(ref state);
            }
            var probes = Probes(i * 31);
            var result = CatchFallApi.Tick(ref state, p, bodies, probes, Fuzz.Vec(i * 37), Fuzz.Pick(i * 41), drives);
            Fuzz.Drives(bodies, drives);
            Fuzz.Value(result.HandTargetLeft);
            Fuzz.Value(result.HandTargetRight);
            Fuzz.Value(result.HeadTarget);
            Fuzz.Value(result.SpineTwist);
            Fuzz.Value(result.SpinTorque);
        }
    }

    [Fact]
    public void SpinTorque_Totality()
    {
        for (var i = 0; i < Fuzz.Combos; i++)
        {
            var torque = CatchFallApi.SpinTorque(Fuzz.Vec(i * 3), Fuzz.Vec(i * 5), Fuzz.Pick(i * 7), Fuzz.Pick(i * 11), Fuzz.Pick(i * 13), Fuzz.Pick(i * 17));
            Fuzz.Value(torque);
        }
    }

    [Fact]
    public void CatchFallSpans_RejectWithoutThrow()
    {
        var p = CatchFallParams.Standard;
        var probes = Probes(9);
        for (var k = 0; k < 8; k++)
        {
            var shortBodies = Fuzz.Bodies(k * 107, Roles.Count - 1);
            var shortDrives = new JointDrive[Roles.Count - 1];
            var state = CatchFallApi.Start(p);
            CatchFallApi.Tick(ref state, p, shortBodies, probes, new Float3(0f, 1f, 0f), 0.0167f, shortDrives);
            var fullBodies = Fuzz.Bodies(k * 109, Roles.Count);
            var emptyDrives = new JointDrive[0];
            var state2 = CatchFallApi.Start(p);
            CatchFallApi.Tick(ref state2, p, fullBodies, probes, new Float3(0f, 1f, 0f), 0.0167f, emptyDrives);
        }
        for (var i = 0; i < Fuzz.Combos; i++)
        {
            var cycle = CatchFallApi.Start(Params(i));
            Assert.Equal((byte)1, cycle.Active);
            CatchFallApi.Deactivate(ref cycle);
            Assert.Equal((byte)0, cycle.Active);
        }
    }
}
