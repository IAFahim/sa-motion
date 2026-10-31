using SA.Motion.Inertialization;
using SA.Motion.Numerics;
using Xunit;
using Quaternion = SA.Motion.Numerics.Quaternion;

namespace SA.Motion.Tests;

public class ReadmeSampleTests
{
    static void Near(float expected, float actual) => Assert.InRange(actual, expected - 2e-4f, expected + 2e-4f);
    static void Near(Float3 expected, Float3 actual)
    {
        Near(expected.X, actual.X);
        Near(expected.Y, actual.Y);
        Near(expected.Z, actual.Z);
    }
    static void Near(Quaternion expected, Quaternion actual) => Assert.InRange(Quaternion.Dot(Quaternion.Normalize(expected), Quaternion.Normalize(actual)), 1f - 2e-5f, 1f + 2e-5f);

    [Fact]
    public void ReadmeSampleStartsFromCorrectedOutputAndReachesDestination()
    {
        var source = Pose.Identity;
        source.Position = new Float3(2, 0, 0);
        var history = new PoseHistory();
        history.Reset(source);
        var transition = PoseTransition.Create(history, Pose.Identity, 0.5f);
        var start = PoseTransition.Apply(ref transition, Pose.Identity, 0f);
        var end = PoseTransition.Apply(ref transition, Pose.Identity, 1f);
        Near(source.Position, start.Position);
        Near(source.Rotation, start.Rotation);
        Near(Float3.One, start.Scale);
        Near(Pose.Identity.Position, end.Position);
        Near(Pose.Identity.Rotation, end.Rotation);
        Near(Float3.One, end.Scale);
    }
}
