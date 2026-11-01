using SA.Motion.Kinematics;
using SA.Motion.Numerics;
using Xunit;

namespace SA.Motion.Tests;

public class IKTests
{
    [Fact]
    public void AimHonorsConeWeightAndInvalidTarget()
    {
        var right = new Float3(1, 0, 0);
        var forward = new Float3(0, 0, 1);
        var rotation = Aim.Solve(Quaternion.Identity, forward, right, 1f, Scalar.Pi * 0.25f);
        var aimed = Quaternion.Rotate(rotation, forward);
        Assert.InRange(aimed.X, 0.706f, 0.708f);
        Assert.InRange(aimed.Z, 0.706f, 0.708f);
        Assert.Equal(Quaternion.Identity, Aim.Solve(Quaternion.Identity, forward, right, 0f, Scalar.Pi));
        Assert.Equal(Quaternion.Identity, Aim.Solve(Quaternion.Identity, forward, new Float3(float.NaN, 0, 0), 1f, Scalar.Pi));
    }

    [Theory]
    [InlineData(0.4f, -1.5f, 0f)]
    [InlineData(4f, -5f, 1f)]
    [InlineData(0f, 0f, 0f)]
    public void PreservesLengthsAndRotationDeltas(float x, float y, float z)
    {
        var middle = new Float3(0, -1, 0);
        var end = new Float3(0, -2, 0);
        var result = TwoBoneIK.Solve(default, middle, end, new Float3(x, y, z), new Float3(0, -1, 1));
        Assert.True(result.Valid);
        Assert.InRange(Math.Abs(result.Middle.Length - 1f), 0, 1e-4f);
        Assert.InRange(Math.Abs((result.End - result.Middle).Length - 1f), 0, 1e-4f);
        Assert.InRange((Quaternion.Rotate(result.UpperDelta, middle) - result.Middle).Length, 0, 1e-4f);
        Assert.InRange((Quaternion.Rotate(result.LowerDelta, end - middle) - (result.End - result.Middle)).Length, 0, 1e-4f);
    }

    [Fact]
    public void RejectsDegenerateOrNonfiniteChains()
    {
        Assert.False(TwoBoneIK.Solve(default, default, new Float3(0, -1, 0), default, default).Valid);
        Assert.False(TwoBoneIK.Solve(default, new Float3(0, -1, 0), new Float3(0, -2, 0), new Float3(float.NaN, 0, 0), default).Valid);
        Assert.True(TwoBoneIK.Solve(default, new Float3(0, -1, 0), new Float3(0, -2, 0), new Float3(0, -1, 0), default).Valid);
    }
}
