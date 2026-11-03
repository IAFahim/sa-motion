using SA.Animation;
using SA.Motion.Mechanics;
using SA.Motion.Numerics;
using Xunit;

namespace SA.Shape.Tests;

public class ShapeInertiaTests
{
    [Fact]
    public void IdentityAffineReturnsIdenticalMassCenterAndInertia()
    {
        var inertia = Box(2f, 1f, 2f, 3f);
        var center = new Float3(0.5f, -1f, 2f);
        foreach (var policy in new[] { ShapeMassPolicy.FixedMass, ShapeMassPolicy.FixedDensity })
        {
            Assert.Equal(AnimationError.None, ShapeInertia.Transform(2f, center, inertia, Affine.Identity, policy, out var mass, out var next, out var turned));
            Assert.Equal(2f, mass);
            Assert.Equal(center.X, next.X);
            Assert.Equal(center.Y, next.Y);
            Assert.Equal(center.Z, next.Z);
            Near(inertia.C0, turned.C0, 1e-5f);
            Near(inertia.C1, turned.C1, 1e-5f);
            Near(inertia.C2, turned.C2, 1e-5f);
        }
    }

    [Fact]
    public void TranslationAloneMovesCenterAndKeepsInertia()
    {
        var inertia = Box(2f, 1f, 2f, 3f);
        var center = new Float3(0.5f, -1f, 2f);
        var shift = new Affine(Affine.Identity.C0, Affine.Identity.C1, Affine.Identity.C2, new Float3(3f, -2f, 0.5f));
        Assert.Equal(AnimationError.None, ShapeInertia.Transform(2f, center, inertia, shift, ShapeMassPolicy.FixedMass, out var mass, out var next, out var turned));
        Assert.Equal(2f, mass);
        Near(center + shift.C3, next, 1e-6f);
        Near(inertia.C0, turned.C0, 1e-5f);
        Near(inertia.C1, turned.C1, 1e-5f);
        Near(inertia.C2, turned.C2, 1e-5f);
    }

    [Fact]
    public void UniformScaleMatchesAnalyticSphere()
    {
        var sphere = Sphere(4f, 1.5f);
        var center = new Float3(1f, 2f, 3f);
        var scale = Scale(new Float3(2.5f, 2.5f, 2.5f), new Float3(-1f, 0.5f, 2f));
        Assert.Equal(AnimationError.None, ShapeInertia.Transform(4f, center, sphere, scale, ShapeMassPolicy.FixedMass, out var mass, out var next, out var turned));
        Assert.Equal(4f, mass);
        var analytic = Sphere(4f, 1.5f * 2.5f);
        Near(analytic.C0, turned.C0, 2e-4f);
        Near(analytic.C1, turned.C1, 2e-4f);
        Near(analytic.C2, turned.C2, 2e-4f);
        Near(Point(scale, center), next, 1e-5f);
        Assert.Equal(AnimationError.None, ShapeInertia.Transform(4f, center, sphere, scale, ShapeMassPolicy.FixedDensity, out var denseMass, out _, out var dense));
        Assert.Equal(4f * 2.5f * 2.5f * 2.5f, denseMass);
        Near(turned.C0 * (2.5f * 2.5f * 2.5f), dense.C0, 2e-3f);
        Near(turned.C1 * (2.5f * 2.5f * 2.5f), dense.C1, 2e-3f);
        Near(turned.C2 * (2.5f * 2.5f * 2.5f), dense.C2, 2e-3f);
    }

    [Fact]
    public void RotationConjugatesInertiaAndPreservesPrincipalMoments()
    {
        var box = Box(3f, 1f, 2f, 4f);
        var center = new Float3(0.5f, 1f, -0.5f);
        var spin = Quaternion.Canonical(Quaternion.AxisAngle(new Float3(1f, 1f, 1f) / (float)Math.Sqrt(3.0), 0.9f));
        var turn = Rotation(spin, new Float3(1f, -2f, 3f));
        Assert.Equal(AnimationError.None, ShapeInertia.Transform(3f, center, box, turn, ShapeMassPolicy.FixedMass, out var mass, out var next, out var turned));
        Assert.Equal(AnimationError.None, ShapeInertia.Transform(3f, center, box, turn, ShapeMassPolicy.FixedDensity, out var denseMass, out var denseNext, out var dense));
        Assert.True(Math.Abs(mass - denseMass) <= 1e-5f * mass, mass + " vs " + denseMass);
        Near(next, denseNext, 1e-6f);
        Near(dense.C0, turned.C0, 1e-5f);
        var basis = new Float3x3(turn.C0, turn.C1, turn.C2);
        var expected = Conjugate(basis, box);
        Near(expected.C0, turned.C0, 1e-4f);
        Near(expected.C1, turned.C1, 1e-4f);
        Near(expected.C2, turned.C2, 1e-4f);
        Near(Point(turn, center), next, 1e-5f);
        Assert.Equal(AnimationError.None, ShapeInertia.PrincipalAxes(box, out var before, out _));
        Assert.Equal(AnimationError.None, ShapeInertia.PrincipalAxes(turned, out var after, out _));
        Near(before, after, 1e-4f);
    }

    [Fact]
    public void MassPoliciesDivergeByDeterminant()
    {
        var box = Box(2f, 1f, 2f, 0.5f);
        var center = new Float3(0f, 1f, 0f);
        var stretch = Scale(new Float3(2f, 2f, 0.5f), Float3.Zero);
        Assert.Equal(AnimationError.None, ShapeInertia.Transform(2f, center, box, stretch, ShapeMassPolicy.FixedMass, out var mass, out _, out var light));
        Assert.Equal(AnimationError.None, ShapeInertia.Transform(2f, center, box, stretch, ShapeMassPolicy.FixedDensity, out var denseMass, out _, out var dense));
        Assert.Equal(2f, mass);
        Assert.Equal(4f, denseMass);
        Near(light.C0 * 2f, dense.C0, 1e-4f);
        Near(light.C1 * 2f, dense.C1, 1e-4f);
        Near(light.C2 * 2f, dense.C2, 1e-4f);
    }

    [Fact]
    public void RoundTripThroughInverseAffineRestoresBody()
    {
        var box = Box(2f, 1f, 3f, 2f);
        var center = new Float3(0.5f, -1f, 1.5f);
        var forward = Scale(new Float3(2f, 3f, 4f), new Float3(5f, -6f, 7f));
        var back = new Affine(
            new Float3(0.5f, 0f, 0f),
            new Float3(0f, 1f / 3f, 0f),
            new Float3(0f, 0f, 0.25f),
            new Float3(-2.5f, 2f, -1.75f));
        Assert.Equal(AnimationError.None, ShapeInertia.Transform(2f, center, box, forward, ShapeMassPolicy.FixedMass, out var mid, out var midCenter, out var midBox));
        Assert.Equal(AnimationError.None, ShapeInertia.Transform(mid, midCenter, midBox, back, ShapeMassPolicy.FixedMass, out var mass, out var next, out var turned));
        Assert.Equal(2f, mass);
        Near(center, next, 1e-5f);
        Near(box.C0, turned.C0, 1e-4f);
        Near(box.C1, turned.C1, 1e-4f);
        Near(box.C2, turned.C2, 1e-4f);
    }

    [Fact]
    public void DiagonalBoxRecoversSortedMomentsAndReconstruction()
    {
        var box = Box(2f, 1f, 2f, 4f);
        Assert.Equal(AnimationError.None, ShapeInertia.PrincipalAxes(box, out var moments, out var rotation));
        Assert.True(Quaternion.Dot(rotation, rotation) > 0.999f && Quaternion.Dot(rotation, rotation) < 1.001f);
        Assert.True(moments.X >= moments.Y && moments.Y >= moments.Z);
        var basis = new Float3x3(
            Quaternion.Rotate(rotation, new Float3(1f, 0f, 0f)),
            Quaternion.Rotate(rotation, new Float3(0f, 1f, 0f)),
            Quaternion.Rotate(rotation, new Float3(0f, 0f, 1f)));
        var rebuilt = Conjugate(basis, new Float3x3(new Float3(moments.X, 0f, 0f), new Float3(0f, moments.Y, 0f), new Float3(0f, 0f, moments.Z)));
        Near(box.C0, rebuilt.C0, 1e-3f);
        Near(box.C1, rebuilt.C1, 1e-3f);
        Near(box.C2, rebuilt.C2, 1e-3f);
        Assert.Equal(2f / 12f * (4f + 16f), moments.X, 3);
        Assert.Equal(2f / 12f * (1f + 16f), moments.Y, 3);
        Assert.Equal(2f / 12f * (1f + 4f), moments.Z, 3);
    }

    [Fact]
    public void RotatedBodyDiagonalizesToTheSameMoments()
    {
        var box = Box(3f, 1f, 2f, 4f);
        var spin = Quaternion.Canonical(Quaternion.AxisAngle(new Float3(0f, 0f, 1f), 0.7f));
        var basis = new Float3x3(
            Quaternion.Rotate(spin, new Float3(1f, 0f, 0f)),
            Quaternion.Rotate(spin, new Float3(0f, 1f, 0f)),
            Quaternion.Rotate(spin, new Float3(0f, 0f, 1f)));
        var turned = Conjugate(basis, box);
        Assert.Equal(AnimationError.None, ShapeInertia.Transform(3f, Float3.Zero, box, new Affine(basis.C0, basis.C1, basis.C2, Float3.Zero),
            ShapeMassPolicy.FixedMass, out _, out _, out var kernel));
        Near(turned.C0, kernel.C0, 1e-4f);
        Assert.Equal(AnimationError.None, ShapeInertia.PrincipalAxes(kernel, out var moments, out var rotation));
        Assert.Equal(AnimationError.None, ShapeInertia.PrincipalAxes(box, out var plain, out _));
        Near(plain, moments, 1e-4f);
        var axes = new Float3x3(
            Quaternion.Rotate(rotation, new Float3(1f, 0f, 0f)),
            Quaternion.Rotate(rotation, new Float3(0f, 1f, 0f)),
            Quaternion.Rotate(rotation, new Float3(0f, 0f, 1f)));
        var rebuilt = Conjugate(axes, new Float3x3(new Float3(moments.X, 0f, 0f), new Float3(0f, moments.Y, 0f), new Float3(0f, 0f, moments.Z)));
        Near(kernel.C0, rebuilt.C0, 1e-3f);
        Near(kernel.C1, rebuilt.C1, 1e-3f);
        Near(kernel.C2, rebuilt.C2, 1e-3f);
    }

    [Fact]
    public void SingularAndInvalidInputsAreRejected()
    {
        var box = Box(2f, 1f, 2f, 3f);
        var center = new Float3(0f, 1f, 0f);
        var flat = new Affine(new Float3(1f, 0f, 0f), new Float3(0f, 1f, 0f), Float3.Zero, Float3.Zero);
        Assert.Equal(AnimationError.UnsupportedScale, ShapeInertia.Transform(2f, center, box, flat, ShapeMassPolicy.FixedMass, out _, out _, out _));
        Assert.Equal(AnimationError.UnsupportedScale, ShapeInertia.Transform(2f, center, box, flat, ShapeMassPolicy.FixedDensity, out _, out _, out _));
        Assert.Equal(AnimationError.UnsupportedScale, ShapeInertia.Transform(2f, center, box, default, ShapeMassPolicy.FixedMass, out _, out _, out _));
        var mirror = Scale(new Float3(-1f, 1f, 1f), Float3.Zero);
        Assert.Equal(AnimationError.UnsupportedScale, ShapeInertia.Transform(2f, center, box, mirror, ShapeMassPolicy.FixedDensity, out _, out _, out _));
        Assert.Equal(AnimationError.None, ShapeInertia.Transform(2f, center, box, mirror, ShapeMassPolicy.FixedMass, out _, out _, out var mirrored));
        Near(box.C0, mirrored.C0, 1e-5f);
        Assert.Equal(AnimationError.InvalidTable, ShapeInertia.Transform(0f, center, box, Affine.Identity, ShapeMassPolicy.FixedMass, out _, out _, out _));
        Assert.Equal(AnimationError.InvalidTable, ShapeInertia.Transform(-2f, center, box, Affine.Identity, ShapeMassPolicy.FixedMass, out _, out _, out _));
        Assert.Equal(AnimationError.InvalidTable, ShapeInertia.Transform(float.NaN, center, box, Affine.Identity, ShapeMassPolicy.FixedMass, out _, out _, out _));
        Assert.Equal(AnimationError.InvalidTable, ShapeInertia.Transform(2f, new Float3(float.NaN, 0f, 0f), box, Affine.Identity, ShapeMassPolicy.FixedMass, out _, out _, out _));
        var lopsided = new Float3x3(new Float3(1f, 0.5f, 0f), new Float3(0f, 1f, 0f), new Float3(0f, 0f, 1f));
        Assert.Equal(AnimationError.InvalidTable, ShapeInertia.Transform(2f, center, lopsided, Affine.Identity, ShapeMassPolicy.FixedMass, out _, out _, out _));
        var wild = new Affine(new Float3(float.NaN, 0f, 0f), new Float3(0f, 1f, 0f), new Float3(0f, 0f, 1f), Float3.Zero);
        Assert.Equal(AnimationError.InvalidTable, ShapeInertia.Transform(2f, center, box, wild, ShapeMassPolicy.FixedMass, out _, out _, out _));
        Assert.Equal(AnimationError.InvalidTable, ShapeInertia.Transform(2f, center, box, Affine.Identity, (ShapeMassPolicy)255, out _, out _, out _));
        Assert.Equal(AnimationError.InvalidTable, ShapeInertia.PrincipalAxes(lopsided, out _, out _));
        var foul = new Float3x3(new Float3(float.NaN, 0f, 0f), new Float3(0f, 1f, 0f), new Float3(0f, 0f, 1f));
        Assert.Equal(AnimationError.InvalidTable, ShapeInertia.PrincipalAxes(foul, out _, out _));
        var hollow = new Float3x3(new Float3(-1f, 0f, 0f), new Float3(0f, -2f, 0f), new Float3(0f, 0f, -3f));
        Assert.Equal(AnimationError.InvalidTable, ShapeInertia.PrincipalAxes(hollow, out _, out _));
    }

    static Float3x3 Box(float mass, float sx, float sy, float sz) => new Float3x3(
        new Float3(mass / 12f * (sy * sy + sz * sz), 0f, 0f),
        new Float3(0f, mass / 12f * (sx * sx + sz * sz), 0f),
        new Float3(0f, 0f, mass / 12f * (sx * sx + sy * sy)));

    static Float3x3 Sphere(float mass, float radius)
    {
        var diagonal = 0.4f * mass * radius * radius;
        return new Float3x3(new Float3(diagonal, 0f, 0f), new Float3(0f, diagonal, 0f), new Float3(0f, 0f, diagonal));
    }

    static Affine Scale(Float3 scale, Float3 shift) => new Affine(
        new Float3(scale.X, 0f, 0f),
        new Float3(0f, scale.Y, 0f),
        new Float3(0f, 0f, scale.Z),
        shift);

    static Affine Rotation(Quaternion rotation, Float3 shift) => new Affine(
        Quaternion.Rotate(rotation, new Float3(1f, 0f, 0f)),
        Quaternion.Rotate(rotation, new Float3(0f, 1f, 0f)),
        Quaternion.Rotate(rotation, new Float3(0f, 0f, 1f)),
        shift);

    static Float3 Point(in Affine matrix, Float3 value) => matrix.C0 * value.X + matrix.C1 * value.Y + matrix.C2 * value.Z + matrix.C3;

    static Float3 Vector(in Float3x3 matrix, Float3 value) => matrix.C0 * value.X + matrix.C1 * value.Y + matrix.C2 * value.Z;

    static Float3x3 Conjugate(in Float3x3 basis, in Float3x3 inertia)
    {
        var row0 = new Float3(
            Vector(basis, inertia.C0).X * basis.C0.X + Vector(basis, inertia.C1).X * basis.C1.X + Vector(basis, inertia.C2).X * basis.C2.X,
            Vector(basis, inertia.C0).X * basis.C0.Y + Vector(basis, inertia.C1).X * basis.C1.Y + Vector(basis, inertia.C2).X * basis.C2.Y,
            Vector(basis, inertia.C0).X * basis.C0.Z + Vector(basis, inertia.C1).X * basis.C1.Z + Vector(basis, inertia.C2).X * basis.C2.Z);
        var row1 = new Float3(
            Vector(basis, inertia.C0).Y * basis.C0.X + Vector(basis, inertia.C1).Y * basis.C1.X + Vector(basis, inertia.C2).Y * basis.C2.X,
            Vector(basis, inertia.C0).Y * basis.C0.Y + Vector(basis, inertia.C1).Y * basis.C1.Y + Vector(basis, inertia.C2).Y * basis.C2.Y,
            Vector(basis, inertia.C0).Y * basis.C0.Z + Vector(basis, inertia.C1).Y * basis.C1.Z + Vector(basis, inertia.C2).Y * basis.C2.Z);
        var row2 = new Float3(
            Vector(basis, inertia.C0).Z * basis.C0.X + Vector(basis, inertia.C1).Z * basis.C1.X + Vector(basis, inertia.C2).Z * basis.C2.X,
            Vector(basis, inertia.C0).Z * basis.C0.Y + Vector(basis, inertia.C1).Z * basis.C1.Y + Vector(basis, inertia.C2).Z * basis.C2.Y,
            Vector(basis, inertia.C0).Z * basis.C0.Z + Vector(basis, inertia.C1).Z * basis.C1.Z + Vector(basis, inertia.C2).Z * basis.C2.Z);
        return new Float3x3(
            new Float3(row0.X, row1.X, row2.X),
            new Float3(row0.Y, row1.Y, row2.Y),
            new Float3(row0.Z, row1.Z, row2.Z));
    }

    static void Near(Float3 expected, Float3 actual, float tolerance)
    {
        var width = 1f + Math.Abs(expected.X) + Math.Abs(expected.Y) + Math.Abs(expected.Z);
        Assert.True(Math.Abs(expected.X - actual.X) <= tolerance * width, expected.X + " vs " + actual.X);
        Assert.True(Math.Abs(expected.Y - actual.Y) <= tolerance * width, expected.Y + " vs " + actual.Y);
        Assert.True(Math.Abs(expected.Z - actual.Z) <= tolerance * width, expected.Z + " vs " + actual.Z);
    }
}
