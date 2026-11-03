using SA.Animation;
using SA.Motion.Numerics;
using Xunit;
using static SA.Shape.Tests.ShapeFixture;

namespace SA.Shape.Tests;

public class ShapeRigTests
{
    [Fact]
    public void NeutralIsExactAndEveryEvaluationRebuildsFromTheSameRestData()
    {
        var f = new ShapeFixture();
        var immutable = (RetargetRow[])f.Rows.Clone();
        for (var i = 0; i < f.Rows.Length; i++)
        {
            Assert.Equal(f.Base[i].Position, f.Offsets[i]);
            Assert.Equal(1f, f.Lengths[i]);
            Assert.Equal(Float3.One, f.SkinScales[i]);
            Assert.Equal(Affine.Identity, f.Deformations[i]);
        }
        Assert.Equal(1f, f.Geometry.Retarget.HeightRatio);
        f.Values[0] = 1f; f.Values[1] = 1f; f.Values[2] = 1f;
        Assert.Equal(AnimationError.None, f.Evaluate(2));
        var offsets = (Float3[])f.Offsets.Clone();
        var skins = (Affine[])f.Deformations.Clone();
        for (var pass = 0; pass < 100; pass++) Assert.Equal(AnimationError.None, f.Evaluate((ulong)pass + 3));
        Assert.Equal(offsets, f.Offsets);
        Assert.Equal(skins, f.Deformations);
        Array.Clear(f.Values);
        Assert.Equal(AnimationError.None, f.Evaluate(103));
        for (var i = 0; i < f.Rows.Length; i++) Assert.Equal(f.Base[i].Position, f.Offsets[i]);
        Assert.Equal(immutable, f.Rows);
    }

    [Fact]
    public void LongerLegsAndThickerSolesRaiseHipsAndKeepTheLowestSoleAtBaseGround()
    {
        var f = new ShapeFixture();
        f.Values[0] = 1f; f.Values[1] = 1f; f.Values[2] = 1f;
        Assert.Equal(AnimationError.None, f.Evaluate(2));
        Near(f.Geometry.BaseHipHeight, 1f);
        Near(f.Geometry.GroundY, 0f);
        Near(f.Geometry.GroundShift, .245f);
        Near(f.Geometry.HipHeight, 1.245f);
        Near(f.Geometry.Retarget.HeightRatio, 1.245f);
        Near(f.Offsets[11].Length, .525f);
        Near(f.Offsets[12].Length, .45f);
        Assert.Equal(f.Geometry.Retarget.HeightRatio, f.Scales[1]);
        Assert.Equal(AnimationError.None, f.Retarget());
        for (var i = 0; i < f.Target.Length; i++) Assert.Equal(Float3.One, f.Target[i].Scale);
        Assert.True(ShapeContacts.TryMarker(f.Models[6], f.Deformations[6], f.LeftSole.Offset, out var sole));
        Near(sole.Y, 0f);
    }

    [Fact]
    public void SkinThicknessDoesNotScaleTheJointHierarchy()
    {
        var f = new ShapeFixture();
        f.Values[1] = 1f;
        f.Grounding = ShapeGrounding.Free;
        Assert.Equal(AnimationError.None, f.Evaluate());
        for (var i = 0; i < f.Rows.Length; i++) Assert.Equal(f.Base[i].Position, f.Offsets[i]);
        Assert.Equal(1f, f.Lengths[3]);
        Assert.Equal(1.5f, f.SkinScales[3].X);
        Near(f.Models[16].Position, new Float3(.1f, 1.75f, 0));
        Near(f.Geometry.GroundY, -.02f);
        Near(f.Geometry.GroundShift, 0f);
    }

    [Fact]
    public void CombinedEffectsAreBoundedAndBadDefinitionsAreDiagnosed()
    {
        var f = new ShapeFixture();
        f.Values[0] = 100f;
        Assert.Equal(AnimationError.None, f.Evaluate());
        Near(f.Lengths[5], 1.25f);
        var profile = f.Definition.Profile;
        var definition = new ShapeDefinition(profile, f.Channels, [new OffsetEffect(0, 5, Float3.Zero, 100f)], [], []);
        Assert.Equal(AnimationError.InvalidFrame, ShapeRig.Evaluate(definition, f.Binding, f.Rows, f.Values, 1, f.Workspace, out _));
        definition = new ShapeDefinition(profile, f.Channels, [new OffsetEffect(0, 0, Float3.Zero)], [], []);
        Assert.Equal(AnimationError.InvalidTable, ShapeRig.Evaluate(definition, f.Binding, f.Rows, f.Values, 1, f.Workspace, out _));
        definition = new ShapeDefinition(profile, f.Channels, [], [], [SkinFrame.Identity]);
        Assert.Equal(AnimationError.InvalidTable, ShapeRig.Evaluate(definition, f.Binding, f.Rows, f.Values, 1, f.Workspace, out _));
        f.Values[0] = float.NaN;
        Assert.Equal(AnimationError.InvalidFrame, f.Evaluate());
    }

    [Fact]
    public void GeometryOverlayMatchesNeutralAndTheGeneralRotationSolver()
    {
        var f = new ShapeFixture();
        var legacy = new Pose[f.Base.Length];
        Assert.Equal(AnimationError.None, RetargetTable.Apply(FamilyId, f.Binding, f.Parents, f.Base, f.Rows, f.CanonicalModels, f.TargetModels, legacy));
        Assert.Equal(AnimationError.None, f.Retarget());
        Assert.Equal(legacy, f.Target);
        f.Values[0] = .7f; f.Values[2] = -.8f;
        Assert.Equal(AnimationError.None, f.Evaluate(2));
        var input = (Pose[])f.Base.Clone();
        for (var i = 1; i < input.Length; i++) input[i].Rotation = RotationVector.Exp(new Float3(i * .01f, i * .013f, i * -.02f));
        input[1].Position += new Float3(.1f, -.2f, .3f);
        Assert.Equal(AnimationError.None, f.Retarget(input));
        Assert.Equal(AnimationError.None, RetargetTable.ApplyReferenceGeometry(FamilyId, f.Binding, f.Geometry.Retarget, f.Parents, input,
            f.Rows, f.Offsets, f.CanonicalModels, f.TargetModels, legacy));
        for (var i = 0; i < input.Length; i++)
        {
            Near(f.Target[i].Position, legacy[i].Position);
            Near(f.Target[i].Rotation, legacy[i].Rotation);
        }
        var wrong = new RetargetGeometry(FamilyId, f.Geometry.Retarget.HipReference, f.Geometry.Retarget.HeightRatio);
        Assert.Equal(AnimationError.InvalidTable, RetargetTable.ApplyGeometry(FamilyId, f.Binding, wrong, f.Parents, input, f.Rows,
            f.Offsets, f.CanonicalModels, f.TargetModels, legacy));
    }
}
