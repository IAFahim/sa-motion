using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using SA.Animation;
using SA.Animation.Content;
using SA.Motion.Inertialization;
using Xunit;

namespace SA.Shape.Tests;

public class ShapeContractTests
{
    [Fact]
    public void OptionalAddonNeverBecomesAnAnimationCoreDependency()
    {
        Assert.DoesNotContain(typeof(TimelinePose).Assembly.GetReferencedAssemblies(), a => a.Name == "SA.Shape");
        Assert.DoesNotContain(typeof(ShapeRig).Assembly.GetReferencedAssemblies(), a => a.Name == "SA.Animation.Content" || a.Name!.StartsWith("Unity"));
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<ShapeParameter>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<ShapeProfile>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<ShapeGeometry>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<ShapeFrameState>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<ShapeContactTarget>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<ShapeReachResult>());
    }

    [Fact]
    public void TestsActuallyLoadTheSelectedLibraryTarget()
    {
#if SA_LIBRARY_NETSTANDARD
        const string expected = ".NETStandard,Version=v2.1";
#else
        const string expected = ".NETCoreApp,Version=v10.0";
#endif
        foreach (var type in new[] { typeof(ShapeRig), typeof(TimelinePose), typeof(PoseDecay), typeof(SharedClipEncoder) })
        {
            var actual = type.Assembly.GetCustomAttributes(typeof(TargetFrameworkAttribute), false).Cast<TargetFrameworkAttribute>().Single().FrameworkName;
            Assert.True(expected == actual, $"{type.FullName} loaded {actual}, expected {expected}");
        }
    }
}
