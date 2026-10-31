using SA.Humanoid;
using SA.Humanoid.Profile;
using Xunit;

namespace SA.Humanoid.Tests.Profile;

public class ProfileTests
{
    static void Near(float expected, float actual, float tolerance = 1e-6f) => Assert.InRange(actual, expected - tolerance, expected + tolerance);

    static IEnumerable<Role> All() => Enum.GetValues<Role>();

    static readonly (Role Left, Role Right)[] MirroredPairs =
    {
        (Role.UpperArmLeft, Role.UpperArmRight),
        (Role.ForearmLeft, Role.ForearmRight),
        (Role.HandLeft, Role.HandRight),
        (Role.ThighLeft, Role.ThighRight),
        (Role.ShinLeft, Role.ShinRight),
        (Role.FootLeft, Role.FootRight),
    };

    static readonly Role[] Hinges =
    {
        Role.ForearmLeft,
        Role.ForearmRight,
        Role.ShinLeft,
        Role.ShinRight,
    };

    const float HingeSwingBound = 0.05f;

    [Fact]
    public void MassFractionsArePositiveAndSumToOne()
    {
        var sum = 0f;
        foreach (var role in All())
        {
            var spec = HumanProfile.Of(role);
            Assert.True(spec.MassFraction > 0f);
            Assert.True(float.IsFinite(spec.DensityWeight) && spec.DensityWeight > 0f);
            sum += spec.MassFraction;
        }
        Near(1f, sum);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(62.5f)]
    [InlineData(80f)]
    public void MassOverAllRolesSumsToTotal(float total)
    {
        var sum = 0f;
        foreach (var role in All())
            sum += HumanProfile.Mass(role, total);
        Near(total, sum, 1e-4f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-80f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void MassWithBadTotalGivesZero(float total)
    {
        foreach (var role in All())
            Assert.Equal(0f, HumanProfile.Mass(role, total));
    }

    [Fact]
    public void EveryLimitIsFiniteWithOrderedTwistAndSwingsInRange()
    {
        foreach (var role in All())
        {
            var limit = HumanProfile.Of(role).Limit;
            Assert.True(float.IsFinite(limit.TwistMin));
            Assert.True(float.IsFinite(limit.TwistMax));
            Assert.True(float.IsFinite(limit.Swing1Max));
            Assert.True(float.IsFinite(limit.Swing2Max));
            Assert.True(limit.TwistMin <= limit.TwistMax);
            Assert.InRange(limit.Swing1Max, 0f, MathF.PI);
            Assert.InRange(limit.Swing2Max, 0f, MathF.PI);
        }
    }

    [Fact]
    public void PelvisLimitIsDefault()
    {
        Assert.Equal(default(JointLimit), HumanProfile.Of(Role.Pelvis).Limit);
    }

    [Fact]
    public void MirroredPairsShareSwingsAndMirrorTwist()
    {
        foreach (var pair in MirroredPairs)
        {
            var left = HumanProfile.Of(pair.Left).Limit;
            var right = HumanProfile.Of(pair.Right).Limit;
            Assert.Equal(left.Swing1Max, right.Swing1Max);
            Assert.Equal(left.Swing2Max, right.Swing2Max);
            Near(-right.TwistMax, left.TwistMin, 1e-6f);
            Near(-right.TwistMin, left.TwistMax, 1e-6f);
        }
    }

    [Fact]
    public void ElbowsAndKneesAreHingesWithFlexionOnTwist()
    {
        foreach (var role in Hinges)
        {
            var limit = HumanProfile.Of(role).Limit;
            Assert.True(limit.Swing1Max <= HingeSwingBound);
            Assert.True(limit.Swing2Max <= HingeSwingBound);
            Assert.True(limit.TwistMax - limit.TwistMin >= 2f);
        }
    }

    [Fact]
    public void UndefinedRolesGiveDefaultSpecAndZeroMass()
    {
        Assert.Equal(default(RoleSpec), HumanProfile.Of((Role)200));
        Assert.Equal(default(RoleSpec), HumanProfile.Of((Role)255));
        Assert.Equal(0f, HumanProfile.Mass((Role)200, 80f));
    }
}
