using SA.Humanoid;
using Xunit;

namespace SA.Humanoid.Tests;

public class RolesTests
{
    static IEnumerable<Role> All() => Enum.GetValues<Role>();

    [Fact]
    public void CountMatchesTheEnum() => Assert.Equal(Roles.Count, All().Count());

    [Fact]
    public void EveryRoleReachesThePelvisWithoutACycle()
    {
        foreach (var role in All())
        {
            var current = role;
            for (var step = 0; step < Roles.Count && !Roles.IsRoot(current); step++)
                current = Roles.Parent(current);
            Assert.Equal(Role.Pelvis, current);
        }
    }

    [Fact]
    public void OnlyThePelvisIsARootAndItsParentIsItself()
    {
        Assert.Equal(new[] { Role.Pelvis }, All().Where(Roles.IsRoot).ToArray());
        Assert.Equal(Role.Pelvis, Roles.Parent(Role.Pelvis));
    }

    [Fact]
    public void UndefinedValuesAreInvalidAndHaveADefinedParent()
    {
        Assert.False(Roles.IsValid((Role)200));
        Assert.Equal(Role.Pelvis, Roles.Parent((Role)200));
        Assert.All(All(), role => Assert.True(Roles.IsValid(role)));
    }
}
