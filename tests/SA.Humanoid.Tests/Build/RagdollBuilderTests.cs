using SA.Humanoid.Build;
using SA.Humanoid.Mapping;
using SA.Humanoid.Profile;
using SA.Motion.Numerics;
using Xunit;

namespace SA.Humanoid.Tests.Build;

public class RagdollBuilderTests
{
    const float TotalMass = 80f;

    static RagdollLayout Kyle() => RagdollBuilder.Build(RagdollFixtures.Bones(), RagdollFixtures.Map(), TotalMass);

    static float MassSum(RagdollLayout layout)
    {
        float sum = 0f;
        for (int i = 0; i < layout.Bodies.Length; i++)
        {
            sum += layout.Bodies[i].Mass;
        }
        return sum;
    }

    [Fact]
    public void KyleBuildIsValidWithBodiesIndexedByRole()
    {
        RagdollLayout layout = Kyle();
        Assert.True(layout.IsValid);
        Assert.Empty(layout.Diagnostics);
        Assert.Equal(Roles.Count, layout.Bodies.Length);
        Assert.Equal(Roles.Count - 1, layout.Joints.Length);
        for (int i = 0; i < Roles.Count; i++)
        {
            Assert.Equal((Role)i, layout.Bodies[i].Role);
        }
        for (int i = 0; i < layout.Joints.Length; i++)
        {
            Assert.Equal((Role)(i + 1), layout.Joints[i].Child);
            Assert.Equal(Roles.Parent((Role)(i + 1)), layout.Joints[i].Parent);
        }
    }

    [Fact]
    public void RotatedGenericBuildIsValidWithMatchingSizes()
    {
        RagdollLayout kyle = Kyle();
        SkeletonBone[] generic = RagdollFixtures.Rotated(RagdollFixtures.Bones(), RagdollFixtures.TestRotation(), new Float3(5f, -3f, 2f));
        RagdollLayout layout = RagdollBuilder.Build(generic, RagdollFixtures.Map(), TotalMass);
        Assert.True(layout.IsValid);
        Assert.Empty(layout.Diagnostics);
        for (int i = 0; i < Roles.Count; i++)
        {
            RagdollFixtures.Near(kyle.Bodies[i].Radius, layout.Bodies[i].Radius, 1e-6f);
            RagdollFixtures.Near3(kyle.Bodies[i].BoxHalfExtents, layout.Bodies[i].BoxHalfExtents, 1e-6f);
            RagdollFixtures.Near3(kyle.Bodies[i].CenterOfMass, layout.Bodies[i].CenterOfMass, 1e-6f);
            Assert.Equal(kyle.Bodies[i].Mass, layout.Bodies[i].Mass);
        }
    }

    [Fact]
    public void MassesSumToTotalMass()
    {
        RagdollFixtures.Near(TotalMass, MassSum(Kyle()), 1e-4f);
        RagdollFixtures.Near(62.5f, MassSum(RagdollBuilder.Build(RagdollFixtures.Bones(), RagdollFixtures.Map(), 62.5f)), 1e-4f);
    }

    [Fact]
    public void AnchorsNameTheSameWorldPoint()
    {
        RagdollLayout layout = Kyle();
        for (int i = 0; i < layout.Joints.Length; i++)
        {
            JointLayout joint = layout.Joints[i];
            BodyLayout parent = layout.Bodies[(int)joint.Parent];
            BodyLayout child = layout.Bodies[(int)joint.Child];
            Float3 fromParent = RagdollFixtures.TransformPoint(parent, joint.AnchorInParent);
            Float3 fromChild = RagdollFixtures.TransformPoint(child, joint.AnchorInChild);
            RagdollFixtures.Near3(child.WorldFromBody.Position, fromParent, 1e-5f);
            RagdollFixtures.Near3(fromParent, fromChild, 1e-5f);
        }
    }

    [Fact]
    public void LimitsEqualTheProfileValues()
    {
        RagdollLayout layout = Kyle();
        for (int i = 0; i < layout.Joints.Length; i++)
        {
            JointLimit expected = HumanProfile.Of(layout.Joints[i].Child).Limit;
            Assert.Equal(expected.TwistMin, layout.Joints[i].Limit.TwistMin);
            Assert.Equal(expected.TwistMax, layout.Joints[i].Limit.TwistMax);
            Assert.Equal(expected.Swing1Max, layout.Joints[i].Limit.Swing1Max);
            Assert.Equal(expected.Swing2Max, layout.Joints[i].Limit.Swing2Max);
        }
    }

    [Fact]
    public void ValidBuildOutputsAreFinite()
    {
        Assert.Equal(0, RagdollFixtures.FiniteViolations(Kyle()));
    }

    [Fact]
    public void JointFramesFollowTheDocumentedConvention()
    {
        RagdollLayout layout = Kyle();
        for (int i = 0; i < layout.Joints.Length; i++)
        {
            JointLayout joint = layout.Joints[i];
            BodyLayout parent = layout.Bodies[(int)joint.Parent];
            BodyLayout child = layout.Bodies[(int)joint.Child];
            Quaternion expected = Quaternion.Normalize(Quaternion.Multiply(Quaternion.InverseUnit(parent.WorldFromBody.Rotation), child.WorldFromBody.Rotation));
            RagdollFixtures.Near4(expected, joint.RestRelative, 1e-6f);
            RagdollFixtures.Near4(expected, joint.JointFrameInParent, 1e-6f);
            RagdollFixtures.Near4(Quaternion.Identity, joint.JointFrameInChild, 1e-6f);
        }
    }

    [Fact]
    public void RadiiFollowStandardRatiosOfSegmentLength()
    {
        RagdollLayout layout = Kyle();
        BuildParams standard = BuildParams.Standard;
        for (int i = 0; i < Roles.Count; i++)
        {
            BodyLayout body = layout.Bodies[i];
            float extent = RagdollFixtures.KyleSegmentLength(i);
            RagdollFixtures.Near(extent * 0.5f, body.CenterOfMass.X, 1e-6f);
            RagdollFixtures.Near(0f, body.CenterOfMass.Y, 1e-6f);
            RagdollFixtures.Near(0f, body.CenterOfMass.Z, 1e-6f);
            if (BuildParams.IsBox((Role)i))
            {
                Assert.Equal(0f, body.Radius);
                RagdollFixtures.Near(body.CenterOfMass.X, body.BoxHalfExtents.X, 1e-6f);
                RagdollFixtures.Near(standard.HalfWidthFor((Role)i) * extent, body.BoxHalfExtents.Y, 1e-6f);
                RagdollFixtures.Near(standard.HalfDepthFor((Role)i) * extent, body.BoxHalfExtents.Z, 1e-6f);
            }
            else
            {
                RagdollFixtures.Near(standard.RadiusFor((Role)i) * extent, body.Radius, 1e-6f);
                RagdollFixtures.Near(0f, body.BoxHalfExtents.X, 1e-6f);
                RagdollFixtures.Near(0f, body.CapsuleStart.X, 1e-6f);
                RagdollFixtures.Near(extent, body.CapsuleEnd.X, 1e-6f);
                Assert.True(body.Radius > 0f);
            }
        }
    }

    [Fact]
    public void CapsuleEndpointsLieOnTheirBoneSegments()
    {
        RagdollLayout layout = Kyle();
        for (int i = 0; i < Roles.Count; i++)
        {
            if (BuildParams.IsBox((Role)i))
            {
                continue;
            }
            BodyLayout body = layout.Bodies[i];
            Float3 start = RagdollFixtures.TransformPoint(body, body.CapsuleStart);
            Float3 end = RagdollFixtures.TransformPoint(body, body.CapsuleEnd);
            int primary = RagdollFixtures.PrimaryRole(i);
            Float3 segmentStart = layout.Bodies[i].WorldFromBody.Position;
            Float3 segmentEnd;
            if (primary >= 0)
            {
                segmentEnd = layout.Bodies[primary].WorldFromBody.Position;
            }
            else
            {
                Role parent = Roles.Parent((Role)i);
                Float3 parentJoint = layout.Bodies[(int)parent].WorldFromBody.Position;
                Float3 direction = segmentStart - parentJoint;
                segmentEnd = segmentStart + direction / direction.Length * (body.CapsuleEnd.X - body.CapsuleStart.X);
            }
            Assert.True(RagdollFixtures.PointSegmentDistance(start, segmentStart, segmentEnd) <= 1e-5f);
            Assert.True(RagdollFixtures.PointSegmentDistance(end, segmentStart, segmentEnd) <= 1e-5f);
        }
    }

    [Fact]
    public void BoxCentersLieOnTheirBoneSegments()
    {
        RagdollLayout layout = Kyle();
        foreach (Role role in new Role[] { Role.Pelvis, Role.Torso })
        {
            BodyLayout body = layout.Bodies[(int)role];
            int primary = RagdollFixtures.PrimaryRole((int)role);
            Float3 start = body.WorldFromBody.Position;
            Float3 end = layout.Bodies[primary].WorldFromBody.Position;
            Float3 center = RagdollFixtures.TransformPoint(body, body.CenterOfMass);
            Assert.True(RagdollFixtures.PointSegmentDistance(center, start, end) <= 1e-5f);
        }
    }

    [Fact]
    public void MirrorNegatesXAndPreservesSizes()
    {
        RagdollLayout original = Kyle();
        RagdollLayout mirrored = RagdollBuilder.Build(RagdollFixtures.Mirrored(RagdollFixtures.Bones()), RagdollFixtures.Map(), TotalMass);
        Assert.True(mirrored.IsValid);
        for (int i = 0; i < Roles.Count; i++)
        {
            Float3 expected = new Float3(-original.Bodies[i].WorldFromBody.Position.X, original.Bodies[i].WorldFromBody.Position.Y, original.Bodies[i].WorldFromBody.Position.Z);
            RagdollFixtures.Near3(expected, mirrored.Bodies[i].WorldFromBody.Position, 1e-6f);
            Float3 axis = Quaternion.Rotate(original.Bodies[i].WorldFromBody.Rotation, new Float3(1f, 0f, 0f));
            Float3 mirroredAxis = Quaternion.Rotate(mirrored.Bodies[i].WorldFromBody.Rotation, new Float3(1f, 0f, 0f));
            RagdollFixtures.Near3(new Float3(-axis.X, axis.Y, axis.Z), mirroredAxis, 1e-6f);
            Assert.Equal(original.Bodies[i].Radius, mirrored.Bodies[i].Radius);
            Assert.Equal(original.Bodies[i].Mass, mirrored.Bodies[i].Mass);
            RagdollFixtures.Near3(original.Bodies[i].BoxHalfExtents, mirrored.Bodies[i].BoxHalfExtents, 0f);
            RagdollFixtures.Near3(original.Bodies[i].CenterOfMass, mirrored.Bodies[i].CenterOfMass, 0f);
            Float3 originalStart = RagdollFixtures.TransformPoint(original.Bodies[i], original.Bodies[i].CapsuleStart);
            Float3 mirroredStart = RagdollFixtures.TransformPoint(mirrored.Bodies[i], mirrored.Bodies[i].CapsuleStart);
            RagdollFixtures.Near3(new Float3(-originalStart.X, originalStart.Y, originalStart.Z), mirroredStart, 1e-5f);
        }
        for (int i = 0; i < mirrored.Joints.Length; i++)
        {
            JointLimit expected = HumanProfile.Of(mirrored.Joints[i].Child).Limit;
            Assert.Equal(expected.TwistMin, mirrored.Joints[i].Limit.TwistMin);
            Assert.Equal(expected.TwistMax, mirrored.Joints[i].Limit.TwistMax);
        }
    }

    [Fact]
    public void UniformScaleBuildsValidWithDoubledSegments()
    {
        SkeletonBone[] bones = RagdollFixtures.Bones();
        Pose root = bones[0].Rest;
        bones[0] = new SkeletonBone(bones[0].Name, bones[0].Parent, new Pose { Position = root.Position, Rotation = root.Rotation, Scale = new Float3(2f, 2f, 2f) });
        RagdollLayout layout = RagdollBuilder.Build(bones, RagdollFixtures.Map(), TotalMass);
        Assert.True(layout.IsValid);
        RagdollLayout kyle = Kyle();
        for (int i = 0; i < Roles.Count; i++)
        {
            RagdollFixtures.Near(kyle.Bodies[i].Radius * 2f, layout.Bodies[i].Radius, 1e-5f);
        }
    }

    [Fact]
    public void BuildIsDeterministic()
    {
        RagdollLayout first = Kyle();
        RagdollLayout second = Kyle();
        Assert.Equal(first.IsValid, second.IsValid);
        Assert.Equal(first.Diagnostics.Length, second.Diagnostics.Length);
        for (int i = 0; i < Roles.Count; i++)
        {
            RagdollFixtures.Near3(first.Bodies[i].WorldFromBody.Position, second.Bodies[i].WorldFromBody.Position, 0f);
            RagdollFixtures.Near4(first.Bodies[i].WorldFromBody.Rotation, second.Bodies[i].WorldFromBody.Rotation, 0f);
            Assert.Equal(first.Bodies[i].Radius, second.Bodies[i].Radius);
            Assert.Equal(first.Bodies[i].Mass, second.Bodies[i].Mass);
        }
        for (int i = 0; i < first.Joints.Length; i++)
        {
            RagdollFixtures.Near3(first.Joints[i].AnchorInParent, second.Joints[i].AnchorInParent, 0f);
            RagdollFixtures.Near4(first.Joints[i].RestRelative, second.Joints[i].RestRelative, 0f);
        }
    }
}
