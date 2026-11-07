using SA.Animation;
using SA.Motion.Numerics;
using SA.Soma;

namespace SA.Soma.Tests;

internal static class SomaFixtures
{
    internal static readonly AssetId Family = new(11, 22, 33, 44);

    internal static SomaRetarget Retarget()
    {
        Assert.Equal(AnimationError.None, SomaRetarget.Create(Family, out var retarget));
        return retarget!;
    }

    internal static Pose[] BindModel()
    {
        var model = new Pose[SomaRig.ModelJointCount];
        for (var j = 0; j < model.Length; j++) model[j] = SomaRig.BindLocal(j);
        return model;
    }

    internal static Quaternion[] World(Pose[] model)
    {
        var world = new Quaternion[model.Length];
        for (var j = 0; j < model.Length; j++)
        {
            var parent = SomaRig.Parent(j);
            world[j] = parent < 0 ? model[j].Rotation : Quaternion.Normalize(Quaternion.Multiply(world[parent], model[j].Rotation));
        }
        return world;
    }

    internal static Pose[] FromWorld(Quaternion[] world, Float3 hips)
    {
        var model = new Pose[world.Length];
        for (var j = 0; j < world.Length; j++)
        {
            var parent = SomaRig.Parent(j);
            model[j] = new Pose
            {
                Position = parent < 0 ? (j == SomaRig.Hips ? hips : SomaRig.RestTranslation(j)) : SomaRig.RestTranslation(j),
                Rotation = parent < 0 ? world[j] : Quaternion.Normalize(Quaternion.Multiply(Quaternion.InverseUnit(world[parent]), world[j])),
                Scale = Float3.One
            };
        }
        return model;
    }

    internal static float[] Pack(params Pose[][] frames)
    {
        var data = new float[frames.Length * SomaRig.ModelJointCount * 7];
        for (var f = 0; f < frames.Length; f++)
            for (var j = 0; j < SomaRig.ModelJointCount; j++)
            {
                var p = frames[f][j];
                var at = (f * SomaRig.ModelJointCount + j) * 7;
                data[at] = p.Position.X; data[at + 1] = p.Position.Y; data[at + 2] = p.Position.Z;
                data[at + 3] = p.Rotation.X; data[at + 4] = p.Rotation.Y; data[at + 5] = p.Rotation.Z; data[at + 6] = p.Rotation.W;
            }
        return data;
    }

    internal static (Pose[] samples, RigidPose[] root) Convert(SomaRetarget retarget, float[] poses, int frames, SomaSpace space = SomaSpace.ModelYUp)
    {
        var samples = new Pose[frames * CanonicalHumanoid.BoneCount];
        var root = new RigidPose[frames];
        Assert.Equal(AnimationError.None, SomaClip.Convert(retarget, poses, frames, SomaRig.ModelJointCount, space, samples, root));
        return (samples, root);
    }

    internal static RigidPose[] Model(SomaRetarget retarget, ReadOnlySpan<Pose> local)
    {
        var model = new RigidPose[local.Length];
        Assert.Equal(AnimationError.None, RigidMath.Model(retarget.TargetParents, local, model));
        return model;
    }

    internal static float AngleDegrees(Quaternion a, Quaternion b)
    {
        var dot = Math.Min(1f, Math.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b))));
        return 2f * MathF.Acos(dot) * 180f / MathF.PI;
    }

    internal static float AngleDegrees(Float3 a, Float3 b)
    {
        var cos = Float3.Dot(a, b) / (a.Length * b.Length);
        return MathF.Acos(Math.Clamp(cos, -1f, 1f)) * 180f / MathF.PI;
    }

    internal static readonly (int target, int targetChild, int model, int modelChild)[] Bones =
    {
        (1, 2, 0, 1), (2, 3, 1, 2), (3, 4, 2, 3), (4, 5, 3, 4), (5, 6, 5, 6),
        (10, 11, 10, 11), (11, 12, 11, 12), (12, 13, 12, 13),
        (29, 30, 16, 17), (30, 31, 17, 18), (31, 32, 18, 19),
        (48, 49, 22, 23), (49, 50, 23, 24), (50, 51, 24, 25),
        (52, 53, 26, 27), (53, 54, 27, 28), (54, 55, 28, 29)
    };

    internal static Float3[] SourceModelPositions(SomaRetarget retarget, Pose[] model, SomaSpace space = SomaSpace.ModelYUp)
    {
        var source = new Pose[SomaRig.SourceCount];
        Assert.Equal(AnimationError.None, SomaFrame.ToSource(model, space, source, out _));
        var rigid = new RigidPose[source.Length];
        Assert.Equal(AnimationError.None, RigidMath.Model(retarget.SourceParents, source, rigid));
        var positions = new Float3[SomaRig.ModelJointCount];
        for (var j = 0; j < positions.Length; j++)
        {
            var s = SomaRig.Source(j);
            positions[j] = s < 0 ? Float3.Zero : rigid[s].Position;
        }
        return positions;
    }

    internal static float WorstBoneDirection(SomaRetarget retarget, Pose[] model, ReadOnlySpan<Pose> samples, SomaSpace space = SomaSpace.ModelYUp)
    {
        var target = Model(retarget, samples);
        var source = SourceModelPositions(retarget, model, space);
        var worst = 0f;
        foreach (var (t, tc, m, mc) in Bones)
            worst = Math.Max(worst, AngleDegrees(target[tc].Position - target[t].Position, source[mc] - source[m]));
        return worst;
    }
}
