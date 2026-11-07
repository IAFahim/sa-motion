using SA.Animation;
using SA.Animation.Content;
using SA.Motion.Kinematics;
using SA.Motion.Numerics;
using SA.Soma;
using static SA.Soma.Tests.SomaFixtures;

namespace SA.Soma.Tests;

public sealed class SomaRetargetTests
{
    [Fact]
    public void MirrorIsConjugationByTheXReflection()
    {
        var random = new Random(7);
        for (var i = 0; i < 200; i++)
        {
            var q = Quaternion.Normalize(new Quaternion(R(random), R(random), R(random), R(random)));
            var r = Quaternion.Normalize(new Quaternion(R(random), R(random), R(random), R(random)));
            var v = new Float3(R(random), R(random), R(random));
            var mirroredRotated = SomaMirror.Position(Quaternion.Rotate(q, v));
            var rotatedMirrored = Quaternion.Rotate(SomaMirror.Rotation(q), SomaMirror.Position(v));
            Assert.True((mirroredRotated - rotatedMirrored).Length < 1e-5f);
            var composed = SomaMirror.Rotation(Quaternion.Multiply(q, r));
            var separately = Quaternion.Multiply(SomaMirror.Rotation(q), SomaMirror.Rotation(r));
            Assert.True(Math.Abs(Quaternion.Dot(composed, separately)) > 1f - 1e-5f);
        }
        Assert.Equal(-0.3f, SomaMirror.Position(new Float3(0.3f, 1f, 2f)).X);
    }

    [Fact]
    public void RuntimeZUpRootReturnsToModelYUp()
    {
        var runtime = new Pose { Position = new Float3(-0.131f, -0.943f, 0.94f), Rotation = Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.5f * Scalar.Pi), Scale = Float3.One };
        var model = SomaAxes.RootToModel(runtime, SomaSpace.RuntimeZUp);
        Assert.True((model.Position - new Float3(-0.131f, 0.94f, 0.943f)).Length < 1e-5f);
        Assert.True(AngleDegrees(model.Rotation, Quaternion.Identity) < 0.05f);
    }

    [Fact]
    public void EmbeddedBindRotationsCarryRestTranslationsOntoBindPositions()
    {
        for (var j = 0; j < SomaRig.ModelJointCount; j++)
        {
            var parent = SomaRig.Parent(j);
            if (parent < 0) continue;
            var predicted = SomaRig.BindPosition(parent) + Quaternion.Rotate(SomaRig.BindRotation(parent), SomaRig.RestTranslation(j));
            Assert.True((predicted - SomaRig.BindPosition(j)).Length < 1e-4f, SomaRig.Name(j));
        }
    }

    [Fact]
    public void FixedTableMapsEveryRequiredBoneAndFoldsNeck2()
    {
        var retarget = Retarget();
        foreach (var bone in new[] { 1, 2, 6, 11, 12, 13, 30, 31, 32, 48, 49, 50, 52, 53, 54 })
            Assert.True(retarget.Rows[bone].Source > 0, bone.ToString());
        Assert.Equal(SomaRig.Source(SomaRig.Neck2), retarget.Rows[5].Source);
        Assert.Equal(SomaRig.Source(3), retarget.Rows[4].Source);
        for (var bone = 14; bone <= 28; bone++) Assert.Equal(RetargetRowMode.Static, retarget.Rows[bone].Mode);
        for (var bone = 33; bone <= 47; bone++) Assert.Equal(RetargetRowMode.Static, retarget.Rows[bone].Mode);
        Assert.InRange(retarget.HeightRatio, 0.95f, 1f);
    }

    [Fact]
    public void CorrectionRemapsTheSomaLocalBoneAxisOntoTheCanonicalBone()
    {
        var retarget = Retarget();
        var reference = Model(retarget, retarget.TargetReference);
        foreach (var (t, tc, m, mc) in Bones)
        {
            var canonical = reference[tc].Position - reference[t].Position;
            var somaAxis = SomaMirror.Position(SomaRig.RestTranslation(mc));
            Assert.True(AngleDegrees(Quaternion.Rotate(retarget.Rows[t].Correction, canonical), somaAxis) < 0.05f, SomaRig.Name(m));
        }
        Assert.True(SomaRig.RestTranslation(SomaRig.LeftForeArm).X > 0.2f);
        Assert.True(SomaRig.RestTranslation(SomaRig.RightForeArm).X < -0.2f);
    }

    [Fact]
    public void TPoseMapsToIdentity()
    {
        var retarget = Retarget();
        var world = World(BindModel());
        for (var j = 0; j < SomaRig.ModelJointCount; j++)
        {
            var source = SomaRig.Source(j);
            if (source < 0) continue;
            for (var bone = 0; bone < retarget.Rows.Length; bone++)
                if (retarget.Rows[bone].Source == source)
                    world[j] = SomaMirror.Rotation(Quaternion.InverseUnit(retarget.Rows[bone].Correction));
        }
        world[SomaRig.Neck1] = world[SomaRig.Neck2];
        var (samples, root) = Convert(retarget, Pack(FromWorld(world, SomaRig.BindPosition(SomaRig.Hips))), 1);
        Assert.True(AngleDegrees(root[0].Rotation, Quaternion.Identity) < 0.05f);
        for (var bone = 1; bone < CanonicalHumanoid.BoneCount; bone++)
            Assert.True(AngleDegrees(samples[bone].Rotation, Quaternion.Identity) < 0.02f, $"bone {bone}: {AngleDegrees(samples[bone].Rotation, Quaternion.Identity)}");
        Assert.True((samples[1].Position - retarget.Binding.TargetHipReference).Length < 1e-4f);
    }

    [Fact]
    public void BindPoseMapsToTheCalibratedAPoseWithMatchingBoneDirections()
    {
        var retarget = Retarget();
        var model = BindModel();
        var (samples, _) = Convert(retarget, Pack(model), 1);
        var target = Model(retarget, samples);
        for (var bone = 1; bone < CanonicalHumanoid.BoneCount; bone++)
            Assert.True(AngleDegrees(target[bone].Rotation, retarget.APose[bone]) < 0.05f, bone.ToString());
        Assert.True(WorstBoneDirection(retarget, model, samples) < 0.05f);
        Assert.True(target[13].Position.X < -0.3f && target[13].Position.Y < 1.2f);
        Assert.True(target[32].Position.X > 0.3f && target[32].Position.Y < 1.2f);
    }

    [Fact]
    public void RaisedSomaLeftArmStaysLeftAfterMirror()
    {
        var retarget = Retarget();
        var world = World(BindModel());
        var upperArm = SomaRig.BindPosition(SomaRig.LeftForeArm) - SomaRig.BindPosition(SomaRig.LeftArm);
        var raise = TwoBoneIK.FromTo(upperArm, new Float3(0.3f, 1f, 0f));
        for (var j = SomaRig.LeftArm; j <= SomaRig.LeftHand; j++) world[j] = Quaternion.Normalize(Quaternion.Multiply(raise, world[j]));
        var model = FromWorld(world, SomaRig.BindPosition(SomaRig.Hips));
        var mirroredSomaHand = SourceModelPositions(retarget, model)[SomaRig.LeftHand];
        var (samples, _) = Convert(retarget, Pack(model), 1);
        var target = Model(retarget, samples);
        Assert.True(target[13].Position.Y > target[11].Position.Y + 0.3f);
        Assert.True(target[13].Position.X < -0.1f);
        Assert.True(target[32].Position.Y < target[30].Position.Y - 0.2f);
        Assert.True(mirroredSomaHand.X < 0f);
        Assert.True(WorstBoneDirection(retarget, model, samples) < 0.05f);
    }

    [Fact]
    public void ContactsMergeHeelAndToePerFootAndZeroHands()
    {
        var sa = new float[8];
        Assert.Equal(AnimationError.None, SomaContacts.Merge(new[] { 0.2f, 0.9f, 0.6f, 0.1f, 2f, float.NaN, -1f, 0.3f }, sa));
        Assert.Equal(new[] { 0.9f, 0.6f, 0f, 0f, 1f, 0.3f, 0f, 0f }, sa);
        Assert.Equal(AnimationError.InvalidFrame, SomaContacts.Merge(new float[3], new float[4]));
    }

    [Fact]
    public void SyntheticWalkEncodesWithinLimitsAndKeepsRootAndContacts()
    {
        var retarget = Retarget();
        const int frames = 40;
        var clip = new Pose[frames][];
        var soma = new float[frames * 4];
        for (var f = 0; f < frames; f++)
        {
            var phase = f / 10f * Scalar.Pi;
            var world = World(BindModel());
            var swing = Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.4f * MathF.Sin(phase));
            for (var j = SomaRig.LeftLeg; j <= 25; j++) world[j] = Quaternion.Multiply(swing, world[j]);
            for (var j = SomaRig.RightLeg; j <= 29; j++) world[j] = Quaternion.Multiply(Quaternion.InverseUnit(swing), world[j]);
            var hips = SomaRig.BindPosition(SomaRig.Hips) + new Float3(0f, 0f, 0.04f * f);
            clip[f] = FromWorld(world, hips);
            soma[f * 4] = MathF.Sin(phase) > 0 ? 1f : 0f;
            soma[f * 4 + 3] = MathF.Sin(phase) > 0 ? 0f : 0.7f;
        }
        var (samples, root) = Convert(retarget, Pack(clip), frames);
        Assert.True(root[frames - 1].Position.Z > 1.4f && Math.Abs(root[frames - 1].Position.X) < 1e-4f);
        var contacts = new float[frames * 4];
        Assert.Equal(AnimationError.None, SomaContacts.Merge(soma, contacts));
        Assert.Equal(AnimationError.None, SomaClip.Encode(retarget, samples, root, contacts, frames, 30f, out var bytes, out var report));
        Assert.True(report.Error.AngleRadians <= CompressionLimits.Default.AngleRadians && report.Error.PositionMetres <= CompressionLimits.Default.PositionMetres);
        Assert.Equal(AnimationError.None, SharedClipReader.Load(bytes, Family, retarget.TargetReference, out var loaded));
        Assert.Equal(contacts, loaded!.ContactWeights);
        Assert.Equal(RootPolicy.Planar, loaded.RootPolicy);
        Assert.True((loaded.Root[frames - 1].Position - root[frames - 1].Position).Length < 1e-5f);
    }

    [Fact]
    public void ForearmRollBecomesTwistAboutTheCanonicalBoneAxis()
    {
        var retarget = Retarget();
        var hips = SomaRig.BindPosition(SomaRig.Hips);
        var baseline = Convert(retarget, Pack(BindModel()), 1).samples;
        RollBecomesTwist(retarget, baseline, hips, true);
        RollBecomesTwist(retarget, baseline, hips, false);
    }

    static void RollBecomesTwist(SomaRetarget retarget, Pose[] baseline, Float3 hips, bool left)
    {
        var fore = left ? SomaRig.LeftForeArm : SomaRig.RightForeArm;
        var hand = left ? SomaRig.LeftHand : SomaRig.RightHand;
        var lower = left ? 12 : 31;
        var upper = left ? 11 : 30;
        var ownAxis = left ? new Float3(-1f, 0f, 0f) : new Float3(1f, 0f, 0f);
        var axis = SomaRig.BindPosition(hand) - SomaRig.BindPosition(fore);
        var roll = Quaternion.AxisAngle(axis / axis.Length, -0.5f * Scalar.Pi);
        var world = World(BindModel());
        world[fore] = Quaternion.Normalize(Quaternion.Multiply(roll, world[fore]));
        world[hand] = Quaternion.Normalize(Quaternion.Multiply(roll, world[hand]));
        var samples = Convert(retarget, Pack(FromWorld(world, hips)), 1).samples;
        var twist = Quaternion.Normalize(Quaternion.Multiply(Quaternion.InverseUnit(baseline[lower].Rotation), samples[lower].Rotation));
        Assert.True(AngleDegrees(twist, Quaternion.AxisAngle(ownAxis, 0.5f * Scalar.Pi)) < 0.1f);
        Assert.True(AngleDegrees(baseline[upper].Rotation, samples[upper].Rotation) < 0.01f);
    }

    static float R(Random random) => (float)(random.NextDouble() * 2 - 1);
}
