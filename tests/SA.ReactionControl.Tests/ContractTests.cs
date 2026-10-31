using System.Runtime.InteropServices;
using SA.ReactionControl;
using Xunit;

namespace SA.ReactionControl.Tests;

public class ContractTests
{
    [Fact]
    public void PassiveDriveHasNoAuthorityAndAnIdentityTarget()
    {
        var drive = JointDrive.Passive;
        Assert.Equal(0f, drive.Stiffness);
        Assert.Equal(0f, drive.Damping);
        Assert.Equal(0f, drive.MaxTorque);
        Assert.Equal(1f, drive.Target.W);
    }

    [Fact]
    public void ContractStructsAreSequentialAndBlittable()
    {
        Assert.Equal(LayoutKind.Sequential, typeof(RoleBody).StructLayoutAttribute!.Value);
        Assert.Equal(LayoutKind.Sequential, typeof(SupportProbe).StructLayoutAttribute!.Value);
        Assert.Equal(LayoutKind.Sequential, typeof(JointDrive).StructLayoutAttribute!.Value);
        Assert.Equal(92, Marshal.SizeOf<RoleBody>());
        Assert.Equal(40, Marshal.SizeOf<JointDrive>());
    }
}
