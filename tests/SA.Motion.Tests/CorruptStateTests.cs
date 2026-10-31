using SA.Motion.Inertialization;
using SA.Motion.Numerics;
using Xunit;

namespace SA.Motion.Tests;

public class CorruptStateTests
{
    [Fact]
    public void CorruptVectorOffsetFallsBackToTheDestination()
    {
        var offset = new VectorOffset
        {
            Direction = new Float3(-0f, float.NaN, float.PositiveInfinity),
            Decay = new ScalarDecay { Duration = float.NegativeInfinity, C0 = 1e-45f, C1 = float.MaxValue, C2 = -float.MaxValue, C3 = 1e-30f, C4 = -1e-30f, C5 = 1e30f }
        };
        var destination = new Float3(float.MaxValue, -float.MaxValue, 1e-30f);
        Assert.Equal(destination, offset.Apply(destination, -1e30f));
    }

    [Fact]
    public void OverflowingCorrectionFallsBackToTheDestination()
    {
        var offset = new VectorOffset
        {
            Direction = new Float3(1f, 0f, 0f),
            Decay = new ScalarDecay { Duration = 1f, C0 = float.MaxValue }
        };
        var destination = new Float3(float.MaxValue, 0f, 0f);
        Assert.Equal(destination, offset.Apply(destination, 0f));
    }

    [Fact]
    public void CorruptScaleCorrectionKeepsTheDestinationScale()
    {
        var transition = new PoseTransition
        {
            LogScale = new VectorOffset
            {
                Direction = new Float3(1f, 1f, 1f),
                Decay = new ScalarDecay { Duration = 1f, C0 = 1000f }
            }
        };
        var destination = Pose.Identity;
        destination.Scale = new Float3(2f, 2f, 2f);
        var pose = PoseTransition.Apply(ref transition, destination, 0f);
        Assert.True(pose.Scale.IsPositive);
        Assert.Equal(destination.Scale, pose.Scale);
    }
}
