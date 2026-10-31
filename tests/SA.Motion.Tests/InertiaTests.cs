using SA.Motion.Mechanics;
using SA.Motion.Numerics;
using Xunit;
using Quaternion = SA.Motion.Numerics.Quaternion;

namespace SA.Motion.Tests;

public class InertiaTests
{
    static void Near(float expected, float actual, float tolerance = 1e-4f) => Assert.InRange(actual, expected - tolerance, expected + tolerance);
    static void Near(Float3 expected, Float3 actual, float tolerance = 1e-4f)
    {
        Near(expected.X, actual.X, tolerance);
        Near(expected.Y, actual.Y, tolerance);
        Near(expected.Z, actual.Z, tolerance);
    }
    static void NearMatrix(Float3x3 expected, Float3x3 actual, float tolerance = 1e-4f)
    {
        Near(expected.C0, actual.C0, tolerance);
        Near(expected.C1, actual.C1, tolerance);
        Near(expected.C2, actual.C2, tolerance);
    }

    static bool AllFinite(Float3x3 value) =>
        value.C0.IsFinite && value.C1.IsFinite && value.C2.IsFinite;

    static Float3x3 Product(Float3x3 a, Float3x3 b) =>
        new Float3x3(a.Multiply(b.C0), a.Multiply(b.C1), a.Multiply(b.C2));

    static Quaternion RandomRotation(Random random)
    {
        while (true)
        {
            var candidate = new Quaternion(
                (float)(random.NextDouble() * 2.0 - 1.0),
                (float)(random.NextDouble() * 2.0 - 1.0),
                (float)(random.NextDouble() * 2.0 - 1.0),
                (float)(random.NextDouble() * 2.0 - 1.0));
            if (candidate.IsValidRotation)
                return Quaternion.Normalize(candidate);
        }
    }

    static Float3 RandomPrincipal(Random random) => new Float3(
        0.01f + (float)random.NextDouble() * 10f,
        0.01f + (float)random.NextDouble() * 10f,
        0.01f + (float)random.NextDouble() * 10f);

    [Fact]
    public void ToWorldIsSymmetricAndPreservesTrace()
    {
        var random = new Random(2201);
        for (var i = 0; i < 200; i++)
        {
            var principal = RandomPrincipal(random);
            var world = Inertia.ToWorld(principal, RandomRotation(random));
            Near(world.C0.Y, world.C1.X);
            Near(world.C0.Z, world.C2.X);
            Near(world.C1.Z, world.C2.Y);
            Near(principal.X + principal.Y + principal.Z, world.C0.X + world.C1.Y + world.C2.Z);
        }
    }

    [Fact]
    public void ToWorldTimesInverseToWorldIsIdentityForPositivePrincipals()
    {
        var random = new Random(2202);
        for (var i = 0; i < 200; i++)
        {
            var principal = RandomPrincipal(random);
            var orientation = RandomRotation(random);
            NearMatrix(Float3x3.Identity, Product(Inertia.ToWorld(principal, orientation), Inertia.InverseToWorld(principal, orientation)));
        }
    }

    [Fact]
    public void AxisAlignedInverseMatchesReciprocalDiagonal()
    {
        var inverse = Inertia.InverseToWorld(new Float3(2f, 4f, 8f), Quaternion.Identity);
        NearMatrix(new Float3x3(
            new Float3(0.5f, 0f, 0f),
            new Float3(0f, 0.25f, 0f),
            new Float3(0f, 0f, 0.125f)), inverse, 1e-6f);
    }

    [Theory]
    [InlineData(0f, 2f, 3f)]
    [InlineData(2f, 0f, 3f)]
    [InlineData(2f, 3f, 0f)]
    [InlineData(-1f, 2f, 3f)]
    [InlineData(1e-7f, 2f, 3f)]
    public void LockedPrincipalGivesZeroInverseOnThatAxis(float px, float py, float pz)
    {
        var principal = new Float3(px, py, pz);
        var aligned = Inertia.InverseToWorld(principal, Quaternion.Identity);
        if (px <= 1e-6f)
            Near(Float3.Zero, aligned.C0, 0f);
        if (py <= 1e-6f)
            Near(Float3.Zero, aligned.C1, 0f);
        if (pz <= 1e-6f)
            Near(Float3.Zero, aligned.C2, 0f);
        var random = new Random(2203);
        for (var i = 0; i < 50; i++)
        {
            var orientation = RandomRotation(random);
            var inverse = Inertia.InverseToWorld(principal, orientation);
            var rotated = Quaternion.Normalize(orientation);
            if (px <= 1e-6f)
                Near(Float3.Zero, inverse.Multiply(Quaternion.Rotate(rotated, new Float3(1f, 0f, 0f))), 1e-4f);
            if (py <= 1e-6f)
                Near(Float3.Zero, inverse.Multiply(Quaternion.Rotate(rotated, new Float3(0f, 1f, 0f))), 1e-4f);
            if (pz <= 1e-6f)
                Near(Float3.Zero, inverse.Multiply(Quaternion.Rotate(rotated, new Float3(0f, 0f, 1f))), 1e-4f);
        }
    }

    [Fact]
    public void DegenerateInertiaReturnsDefinedValues()
    {
        var orientations = new[]
        {
            Quaternion.Identity,
            default,
            new Quaternion(float.NaN, 0f, 0f, 1f),
            new Quaternion(float.PositiveInfinity, 0f, 0f, 0f),
        };
        var principals = new[]
        {
            new Float3(float.NaN, 1f, 2f),
            new Float3(float.PositiveInfinity, 1f, 2f),
            new Float3(-2f, -3f, -4f),
            new Float3(0f, 0f, 0f),
            new Float3(float.NaN, float.NaN, float.NaN),
        };
        foreach (var orientation in orientations)
        {
            foreach (var principal in principals)
            {
                Assert.True(AllFinite(Inertia.ToWorld(principal, orientation)));
                Assert.True(AllFinite(Inertia.InverseToWorld(principal, orientation)));
            }
        }
    }

    [Fact]
    public void TransposeRoundTripsAndMatchesToWorldSymmetry()
    {
        var random = new Random(2204);
        for (var i = 0; i < 50; i++)
        {
            var world = Inertia.ToWorld(RandomPrincipal(random), RandomRotation(random));
            NearMatrix(world, world.Transpose());
            NearMatrix(world, world.Transpose().Transpose());
        }
        var general = new Float3x3(new Float3(1f, 2f, 3f), new Float3(4f, 5f, 6f), new Float3(7f, 8f, 9f));
        NearMatrix(general, general.Transpose().Transpose(), 0f);
        Near(new Float3(1f, 4f, 7f), general.Transpose().C0, 0f);
        Near(new Float3(1f, 2f, 3f), general.Multiply(new Float3(1f, 0f, 0f)), 0f);
        Near(new Float3(30f, 36f, 42f), general.Multiply(new Float3(1f, 2f, 3f)), 0f);
    }
}
