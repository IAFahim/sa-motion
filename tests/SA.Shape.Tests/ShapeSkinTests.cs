using SA.Animation;
using SA.Motion.Mechanics;
using SA.Motion.Numerics;
using Xunit;
using static SA.Shape.Tests.ShapeFixture;

namespace SA.Shape.Tests;

public class ShapeSkinTests
{
    [Fact]
    public void NeutralPalettePreservesTheOriginalInverseBindsExactly()
    {
        var f = new ShapeFixture();
        Assert.Equal(AnimationError.None, f.Retarget());
        var model = new Affine[f.Rows.Length];
        Hierarchy.Model(f.Parents, f.Target, model);
        var map = new[] { 6, 0, 11, 3 };
        var binds = new Affine[4];
        for (var i = 0; i < binds.Length; i++)
        {
            Assert.True(RigidMath.TryInverse(f.Models[map[i]], out var inverse));
            binds[i] = PoseMath.Affine(inverse.ToPose());
        }
        var immutableBinds = (Affine[])binds.Clone();
        var legacy = new Affine[4];
        var shaped = new Affine[4];
        var normal = new Float3x3[4];
        var meshFromModel = PoseMath.Affine(new Pose { Position = new Float3(2, -.2f, 7), Rotation = RotationVector.Exp(new Float3(.1f, -.3f, .2f)), Scale = new Float3(2, 2, 2) });
        Assert.Equal(AnimationError.None, SkinPalette.Build(model, map, binds, meshFromModel, legacy));
        Assert.Equal(AnimationError.None, ShapeSkinPalette.Build(model, f.Deformations, map, binds, meshFromModel, shaped, normal));
        Assert.Equal(legacy, shaped);
        Assert.Equal(immutableBinds, binds);
    }

    [Fact]
    public void BoneBasisAndPivotDefineTheStretchWithoutAssumingUnityBoneAxes()
    {
        var f = new ShapeFixture();
        f.Values[0] = 1f;
        var frames = Enumerable.Repeat(SkinFrame.Identity, f.Rows.Length).ToArray();
        var basis = Quaternion.AxisAngle(new Float3(0, 0, 1), Scalar.Pi / 2);
        var pivot = new Float3(.2f, -.1f, .3f);
        frames[3] = new SkinFrame(basis, pivot);
        var definition = new ShapeDefinition(f.Definition.Profile, f.Channels, [], [new SkinEffect(0, 3, new Float3(MathF.Log(2), 0, 0))], frames);
        Assert.Equal(AnimationError.None, ShapeRig.Evaluate(definition, f.Binding, f.Rows, f.Values, 1, f.Workspace, out _));
        var d = f.Deformations[3];
        Near(Point(d, pivot), pivot);
        Near(Point(d, pivot + new Float3(0, .5f, 0)), pivot + new Float3(0, 1, 0));
        Near(Point(d, pivot + new Float3(.5f, 0, 0)), pivot + new Float3(.5f, 0, 0));
        Near(f.Models[16].Position, new Float3(.1f, 1.75f, 0));
    }

    [Fact]
    public void NormalMatrixStaysPerpendicularToNonUniformlyDeformedTangents()
    {
        var f = new ShapeFixture();
        f.Values[1] = 1f;
        Assert.Equal(AnimationError.None, f.Evaluate());
        Assert.Equal(AnimationError.None, f.Retarget());
        f.Target[3].Rotation = RotationVector.Exp(new Float3(.3f, -.7f, .1f));
        var model = new Affine[f.Rows.Length];
        Hierarchy.Model(f.Parents, f.Target, model);
        var position = new Affine[1];
        var normal = new Float3x3[1];
        Assert.Equal(AnimationError.None, ShapeSkinPalette.Build(model, f.Deformations, [3], [Affine.Identity], Affine.Identity, position, normal));
        var tangentA = new Float3(1, 1, 0);
        var tangentB = new Float3(0, 1, 1);
        var n = normal[0].Multiply(Float3.Cross(tangentA, tangentB));
        Near(Float3.Dot(n, Vector(position[0], tangentA)), 0f);
        Near(Float3.Dot(n, Vector(position[0], tangentB)), 0f);
    }

    [Fact]
    public void ExactLbsNormalsUseTheInverseTransposeAfterBlending()
    {
        var p = new Affine(new Float3(2, 0, 0), new Float3(0, 1, 0), new Float3(0, 0, 1), new Float3(1, 0, 0));
        Assert.Equal(AnimationError.None, ShapeSkinPalette.Blend([p, Affine.Identity], [0, 1], [2, 2], out var position, out var normal));
        Near(position.C0, new Float3(1.5f, 0, 0));
        Near(position.C3, new Float3(.5f, 0, 0));
        Near(normal.C0.X, 2f / 3f);
        var tangent = new Float3(1, 1, 0);
        var n = normal.Multiply(new Float3(1, -1, 0));
        Near(Float3.Dot(n, Vector(position, tangent)), 0f);
    }

    [Fact]
    public void SingularBlendsAndInvalidBindingsReturnErrors()
    {
        var reflected = new Affine(new Float3(-1, 0, 0), new Float3(0, 1, 0), new Float3(0, 0, 1), Float3.Zero);
        Assert.Equal(AnimationError.UnsupportedScale, ShapeSkinPalette.Blend([reflected, Affine.Identity], [0, 1], [.5f, .5f], out _, out _));
        Assert.Equal(AnimationError.InvalidTable, ShapeSkinPalette.Blend([Affine.Identity], [0], [0], out _, out _));
        Assert.Equal(AnimationError.InvalidTable, ShapeSkinPalette.Build([Affine.Identity], [Affine.Identity], [1], [Affine.Identity], Affine.Identity, new Affine[1], new Float3x3[1]));
        var alias = new[] { Affine.Identity };
        Assert.Equal(AnimationError.AliasedBuffers, ShapeSkinPalette.Build(alias, alias, [0], [Affine.Identity], Affine.Identity, alias, new Float3x3[1]));
    }

    internal static Float3 Vector(in Affine matrix, Float3 vector) => matrix.C0 * vector.X + matrix.C1 * vector.Y + matrix.C2 * vector.Z;
    internal static Float3 Point(in Affine matrix, Float3 point) => Vector(matrix, point) + matrix.C3;
}
