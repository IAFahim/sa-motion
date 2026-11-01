using SA.Motion.Legs;
using Xunit;

namespace SA.Motion.Tests;

public class FootHeightTests
{
    const float Leg = 0.8f;

    [Fact]
    public void ARestingAnkleIsKeptExactly()
    {
        Assert.Equal(0.113f, FootHeight.Resolve(0.113f, Leg));
        Assert.True(FootHeight.Plausible(0.113f, Leg));
    }

    [Theory]
    [InlineData(0.03f)]
    [InlineData(0.35f)]
    public void TheLimitsThemselvesAreAccepted(float ratio)
    {
        Assert.True(FootHeight.Plausible(ratio * Leg, Leg));
    }

    [Theory]
    [InlineData(-0.95f)]
    [InlineData(0f)]
    [InlineData(0.01f)]
    [InlineData(0.4f)]
    [InlineData(1.06f)]
    public void AnAnkleAwayFromTheFloorIsReplacedByATypicalHeight(float height)
    {
        Assert.False(FootHeight.Plausible(height, Leg));
        Assert.Equal(FootHeight.TypicalRatio * Leg, FootHeight.Resolve(height, Leg));
    }

    [Fact]
    public void ATypicalHeightIsItselfPlausible()
    {
        Assert.True(FootHeight.Plausible(FootHeight.Resolve(float.NaN, Leg), Leg));
    }

    [Theory]
    [InlineData(float.NaN, 0.8f)]
    [InlineData(float.PositiveInfinity, 0.8f)]
    [InlineData(0.1f, float.NaN)]
    [InlineData(0.1f, 0f)]
    [InlineData(0.1f, -1f)]
    [InlineData(0.1f, float.PositiveInfinity)]
    public void CorruptNumbersNeverThrowAndNeverClaimPlausible(float height, float length)
    {
        Assert.False(FootHeight.Plausible(height, length));
        Assert.False(FootHeight.PlausibleReach(height, length));
        Assert.True(FootHeight.Resolve(height, length) >= 0f);
    }

    [Theory]
    [InlineData(0.12f, true)]
    [InlineData(0.158f, true)]
    [InlineData(0.3f, true)]
    [InlineData(0.34f, false)]
    [InlineData(0.95f, false)]
    [InlineData(-0.1f, false)]
    public void AHeelOrBallFarFromTheFootIsImplausible(float reach, bool expected)
    {
        Assert.Equal(expected, FootHeight.PlausibleReach(reach, Leg));
    }
}
