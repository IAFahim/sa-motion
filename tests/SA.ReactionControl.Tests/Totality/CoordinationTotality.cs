using SA.Humanoid;
using SA.Motion.Numerics;
using SA.ReactionControl.Coordination;
using Xunit;

namespace SA.ReactionControl.Tests.Totality;

public class CoordinationTotality
{
    static CoordinationParams Params(int i)
    {
        var standard = CoordinationParams.Standard;
        if ((i & 1) == 0)
        {
            return standard;
        }
        return new CoordinationParams
        {
            HysteresisMargin = Fuzz.Pick(i),
            FadeTime = Fuzz.Pick(i + 1),
            MinLegStiffness = Fuzz.Pick(i + 2),
            MaxLegStiffness = Fuzz.Pick(i + 3),
            SupportedAnkleResponse = Fuzz.Pick(i + 4),
            SupportedKneeResponse = Fuzz.Pick(i + 5),
            SwingAnkleResponse = Fuzz.Pick(i + 6),
            SwingKneeResponse = Fuzz.Pick(i + 7),
        };
    }

    [Fact]
    public void BlendWeights_Totality()
    {
        for (var i = 0; i < Fuzz.Combos; i++)
        {
            var role = (Role)(byte)(i * 37);
            var mode = (ReactionMode)(byte)(i * 41);
            var authority = BlendWeights.Authority(role, mode);
            Assert.True(authority >= 0f && authority <= 1f);
            var fade = BlendWeights.Fade(Fuzz.Pick(i * 3), Fuzz.Pick(i * 5));
            Assert.True(fade >= 0f && fade <= 1f);
            BlendWeights.FadePair(Fuzz.Pick(i * 7), Fuzz.Pick(i * 11), out var staying, out var incoming);
            Assert.True(staying >= 0f && staying <= 1f);
            Assert.True(incoming >= 0f && incoming <= 1f);
            var blended = BlendWeights.BlendAuthority(Fuzz.Pick(i * 13), Fuzz.Pick(i * 17), Fuzz.Pick(i * 19), Fuzz.Pick(i * 23));
            Assert.True(blended >= 0f && blended <= 1f);
            var from = new JointDrive
            {
                Target = Fuzz.Quat(i * 29, i),
                TargetAngularVelocity = Fuzz.Vec(i * 31),
                Stiffness = Fuzz.Pick(i * 37),
                Damping = Fuzz.Pick(i * 41),
                MaxTorque = Fuzz.Pick(i * 43),
            };
            var to = new JointDrive
            {
                Target = Fuzz.Quat(i * 47, i + 1),
                TargetAngularVelocity = Fuzz.Vec(i * 53),
                Stiffness = Fuzz.Pick(i * 59),
                Damping = Fuzz.Pick(i * 61),
                MaxTorque = Fuzz.Pick(i * 67),
            };
            var drive = BlendWeights.BlendDrive(from, to, Fuzz.Pick(i * 71), Fuzz.Pick(i * 73));
            Fuzz.Drive(drive);
        }
    }

    [Fact]
    public void Coordinator_Totality()
    {
        var requests = new ModeRequest[9];
        for (var i = 0; i < Fuzz.Combos; i++)
        {
            var count = i % 10;
            for (var r = 0; r < requests.Length; r++)
            {
                requests[r] = new ModeRequest
                {
                    Mode = (ReactionMode)(byte)(i * 3 + r * 11),
                    Active = ((i >> r) & 1) == 1,
                    Priority = Fuzz.Pick(i * 7 + r * 13),
                };
            }
            var current = (ReactionMode)(byte)(i * 17);
            var result = Coordinator.Arbitrate(new System.ReadOnlySpan<ModeRequest>(requests, 0, count > 9 ? 9 : count), current, Fuzz.Pick(i * 19));
            Assert.True((byte)result.Owner <= (byte)ReactionMode.Recovering);
            Fuzz.Value(result.Priority);
        }
    }

    [Fact]
    public void StanceDrive_Totality()
    {
        var roles = new Role[] { Role.Pelvis, Role.Torso, Role.Head, Role.ThighLeft, Role.ShinLeft, Role.FootLeft, Role.FootRight, Role.HandLeft, (Role)200 };
        for (var i = 0; i < Fuzz.Combos; i++)
        {
            var p = Params(i);
            var intent = StanceDrive.Resolve((i & 1) == 1, (i & 2) == 2, (i & 4) == 4, (i & 8) == 8, Fuzz.Pick(i * 3), Fuzz.Pick(i * 5), Fuzz.Pick(i * 7), Fuzz.Pick(i * 11), p);
            Assert.True(intent.Weight >= 0f && intent.Weight <= 1f);
            Assert.True(intent.GainWeight >= 0f && intent.GainWeight <= intent.Weight);
            var response = StanceDrive.ResponseFor(roles[i % roles.Length], (i & 16) == 16, p);
            Assert.True(response >= 0f);
            var calibrated = StanceDrive.Calibrate(Fuzz.Quat(i * 13, i), Fuzz.Pick(i * 17), Fuzz.Pick(i * 19), Fuzz.Pick(i * 23));
            Fuzz.Drive(calibrated);
            var relaxed = StanceDrive.RelaxAuthority(Fuzz.Pick(i * 29));
            Assert.True(relaxed >= 0f && relaxed <= 1f);
        }
    }
}
