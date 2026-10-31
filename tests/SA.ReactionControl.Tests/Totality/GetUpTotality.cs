using SA.Humanoid;
using SA.Motion.Numerics;
using Xunit;

namespace SA.ReactionControl.Tests.Totality;

public class GetUpTotality
{
    static GetUpParams Params(int i)
    {
        var standard = GetUpParams.Standard;
        if ((i & 1) == 0)
        {
            return standard;
        }
        return new GetUpParams
        {
            LyingHeadDrop = Fuzz.Pick(i),
            UprightHeadRise = Fuzz.Pick(i + 1),
            PoseDot = Fuzz.Pick(i + 2),
            MaxAlignOffset = Fuzz.Pick(i + 3),
            ClearDistance = Fuzz.Pick(i + 4),
        };
    }

    [Fact]
    public void GetUp_Totality()
    {
        var bodies = new RoleBody[Roles.Count];
        var clips = new GetUpClipEntry[5];
        var probes = new SupportProbe[4];
        for (var i = 0; i < Fuzz.Combos; i++)
        {
            var p = Params(i * 3);
            for (var b = 0; b < bodies.Length; b++)
            {
                bodies[b] = Fuzz.Body(i * 17 + b * 23, i + b);
            }
            var pose = GetUp.Classify(bodies, p);
            Assert.True(pose == RestingPose.Unknown || pose == RestingPose.Supine || pose == RestingPose.Prone || pose == RestingPose.Side || pose == RestingPose.Upright);
            for (var c = 0; c < clips.Length; c++)
            {
                clips[c] = new GetUpClipEntry
                {
                    ClipId = i * 31 + c,
                    StartPose = (RestingPose)(byte)(i * 7 + c * 13),
                    RootOffset = Fuzz.Vec(i * 11 + c * 17),
                };
            }
            var count = i % 6;
            var choice = GetUp.ChooseClip((RestingPose)(byte)(i * 19), new System.ReadOnlySpan<GetUpClipEntry>(clips, 0, count > 5 ? 5 : count));
            Assert.True(choice.HasClip == 0 || choice.HasClip == 1);
            var alignment = GetUp.ComputeAlignment(bodies, p);
            Assert.True(alignment.Valid == 0 || alignment.Valid == 1);
            Fuzz.Value(alignment.Position);
            Fuzz.Value(alignment.Yaw);
            for (var s = 0; s < probes.Length; s++)
            {
                probes[s] = Fuzz.Probe(i * 29 + s * 7, i + s);
            }
            var probeCount = i % 5;
            var clearance = GetUp.CheckClearance(new System.ReadOnlySpan<SupportProbe>(probes, 0, probeCount > 4 ? 4 : probeCount), p);
            Assert.True(clearance == GetUpClearance.Go || clearance == GetUpClearance.Blocked);
        }
    }

    [Fact]
    public void GetUpSpans_RejectWithoutThrow()
    {
        var p = GetUpParams.Standard;
        var empty = new RoleBody[0];
        var shortBodies = Fuzz.Bodies(3, Roles.Count - 1);
        var longBodies = Fuzz.Bodies(4, Roles.Count + 1);
        var full = Fuzz.Bodies(5, Roles.Count);
        Assert.Equal(RestingPose.Unknown, GetUp.Classify(empty, p));
        Assert.Equal(RestingPose.Unknown, GetUp.Classify(shortBodies, p));
        Assert.Equal(RestingPose.Unknown, GetUp.Classify(longBodies, p));
        var alignment = GetUp.ComputeAlignment(empty, p);
        Assert.Equal(0, alignment.Valid);
        var noClips = new GetUpClipEntry[0];
        var choice = GetUp.ChooseClip(RestingPose.Supine, noClips);
        Assert.Equal(0, choice.HasClip);
        var noProbes = new SupportProbe[0];
        Assert.Equal(GetUpClearance.Go, GetUp.CheckClearance(noProbes, p));
        GetUp.Classify(full, p);
        GetUp.ComputeAlignment(full, p);
    }
}
