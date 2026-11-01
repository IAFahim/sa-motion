using System.Runtime.InteropServices;
using SA.Animation;
using SA.Motion.Numerics;
using Xunit;
using NumericsMatrix = System.Numerics.Matrix4x4;
using NumericsQuaternion = System.Numerics.Quaternion;
using NumericsVector = System.Numerics.Vector3;

namespace SA.Animation.Tests;

public sealed record PoseClip(Pose[] Samples, Pose[] Reference, int FrameCount);

public sealed record PoseScene(Pose[] Rest, PoseClip[] Clips, float[][] Masks)
{
    public int Bones => Rest.Length;

    public bool Contributes(in ClipState state) =>
        state.Clip.Value < Clips.Length && state.Mask.Value < Masks.Length && state.Weight * state.Fade > 0f;

    public Pose[] Blend(ReadOnlySpan<ClipState> states)
    {
        var pose = (Pose[])Rest.Clone();
        var sums = new PoseSum[Bones];
        for (var layer = PoseBlend.NextLayer(states, -1); layer >= 0; layer = PoseBlend.NextLayer(states, layer))
        {
            for (var i = 0; i < states.Length; i++)
            {
                var state = states[i];
                if (state.Layer != layer || state.Mode != ClipMode.Override || !Contributes(state))
                    continue;
                var clip = Clips[state.Clip.Value];
                var frame = Phase.Frame(state.Phase, state.Wrap, clip.FrameCount);
                PoseBlend.Override(sums, clip.Samples, clip.FrameCount, frame, Masks[state.Mask.Value], state.Weight * state.Fade);
            }
            PoseBlend.Resolve(pose, sums);
            for (var i = 0; i < states.Length; i++)
            {
                var state = states[i];
                if (state.Layer != layer || state.Mode != ClipMode.Additive || !Contributes(state))
                    continue;
                var clip = Clips[state.Clip.Value];
                var frame = Phase.Frame(state.Phase, state.Wrap, clip.FrameCount);
                PoseBlend.Additive(pose, clip.Samples, clip.Reference, clip.FrameCount, frame, Masks[state.Mask.Value], state.Weight * state.Fade);
            }
        }
        return pose;
    }

    public Pose PerBone(ReadOnlySpan<ClipState> states, int bone)
    {
        var pose = Rest[bone];
        var layer = -1;
        while (NextLayerAbove(states, layer, out layer))
        {
            var sum = default(PoseSum);
            for (var i = 0; i < states.Length; i++)
            {
                var state = states[i];
                if (state.Layer != layer || state.Mode != ClipMode.Override)
                    continue;
                var weight = WeightAt(state, bone);
                if (weight > 0f)
                    sum = PoseMath.Add(sum, SampleAt(state, bone), weight);
            }
            if (sum.Weight > 0f)
                pose = PoseMath.Lerp(pose, PoseMath.Mean(sum), Math.Min(1f, sum.Weight));
            for (var i = 0; i < states.Length; i++)
            {
                var state = states[i];
                if (state.Layer != layer || state.Mode != ClipMode.Additive)
                    continue;
                var weight = WeightAt(state, bone);
                if (weight > 0f)
                    pose = PoseMath.Accumulate(pose, PoseMath.Delta(SampleAt(state, bone), Clips[state.Clip.Value].Reference[bone]), weight);
            }
        }
        return pose;
    }

    static bool NextLayerAbove(ReadOnlySpan<ClipState> states, int after, out int layer)
    {
        layer = int.MaxValue;
        for (var i = 0; i < states.Length; i++)
            if (states[i].Layer > after && states[i].Layer < layer)
                layer = states[i].Layer;
        return layer != int.MaxValue;
    }

    float WeightAt(in ClipState state, int bone) =>
        Contributes(state) ? state.Weight * state.Fade * Masks[state.Mask.Value][bone] : 0f;

    Pose SampleAt(in ClipState state, int bone)
    {
        var clip = Clips[state.Clip.Value];
        return ClipSampling.At(clip.Samples, Bones, bone, Phase.Frame(state.Phase, state.Wrap, clip.FrameCount));
    }
}

public static class PoseFixtures
{
    public static readonly Quaternion RestTilt = Quaternion.Normalize(
        Quaternion.Multiply(RotateZ(1.1f), Quaternion.Multiply(RotateX(0.4f), RotateY(-0.7f))));

    public static Quaternion RotateX(float angle) => Quaternion.AxisAngle(new Float3(1f, 0f, 0f), angle);

    public static Quaternion RotateY(float angle) => Quaternion.AxisAngle(new Float3(0f, 1f, 0f), angle);

    public static Quaternion RotateZ(float angle) => Quaternion.AxisAngle(new Float3(0f, 0f, 1f), angle);

    public static float Between(Random random, float low, float high) => low + (float)random.NextDouble() * (high - low);

    public static Float3 RandomVector(Random random, float low, float high) =>
        new Float3(Between(random, low, high), Between(random, low, high), Between(random, low, high));

    public static Quaternion RandomRotation(Random random)
    {
        var u = (float)random.NextDouble();
        var v = (float)(random.NextDouble() * 2.0 * Math.PI);
        var w = (float)(random.NextDouble() * 2.0 * Math.PI);
        var outer = MathF.Sqrt(1f - u);
        var inner = MathF.Sqrt(u);
        return new Quaternion(outer * MathF.Sin(v), outer * MathF.Cos(v), inner * MathF.Sin(w), inner * MathF.Cos(w));
    }

    public static Pose RandomPose(Random random) => new Pose
    {
        Position = RandomVector(random, -2f, 2f),
        Rotation = RandomRotation(random),
        Scale = RandomVector(random, 0.5f, 2f)
    };

    public static Pose Tilted(float x, Quaternion rotation) => new Pose
    {
        Position = new Float3(x, 1f, 0f),
        Rotation = rotation,
        Scale = Float3.One
    };

    public static float VectorGap(Float3 a, Float3 b) =>
        Math.Max(Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)), Math.Abs(a.Z - b.Z));

    public static float RotationGap(Quaternion a, Quaternion b)
    {
        var sign = Quaternion.Dot(a, b) < 0f ? -1f : 1f;
        return Math.Max(
            Math.Max(Math.Abs(a.X - sign * b.X), Math.Abs(a.Y - sign * b.Y)),
            Math.Max(Math.Abs(a.Z - sign * b.Z), Math.Abs(a.W - sign * b.W)));
    }

    public static float Gap(in Pose a, in Pose b) =>
        Math.Max(Math.Max(VectorGap(a.Position, b.Position), VectorGap(a.Scale, b.Scale)), RotationGap(a.Rotation, b.Rotation));

    public static float ComponentGap(in Pose a, in Pose b)
    {
        var rotation = Math.Max(
            Math.Max(Math.Abs(a.Rotation.X - b.Rotation.X), Math.Abs(a.Rotation.Y - b.Rotation.Y)),
            Math.Max(Math.Abs(a.Rotation.Z - b.Rotation.Z), Math.Abs(a.Rotation.W - b.Rotation.W)));
        return Math.Max(Math.Max(VectorGap(a.Position, b.Position), VectorGap(a.Scale, b.Scale)), rotation);
    }

    public static float AffineGap(in Affine a, in Affine b) =>
        Math.Max(Math.Max(VectorGap(a.C0, b.C0), VectorGap(a.C1, b.C1)), Math.Max(VectorGap(a.C2, b.C2), VectorGap(a.C3, b.C3)));

    public static NumericsMatrix Matrix(in Pose pose) =>
        NumericsMatrix.CreateScale(new NumericsVector(pose.Scale.X, pose.Scale.Y, pose.Scale.Z))
        * NumericsMatrix.CreateFromQuaternion(new NumericsQuaternion(pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W))
        * NumericsMatrix.CreateTranslation(new NumericsVector(pose.Position.X, pose.Position.Y, pose.Position.Z));

    public static float RelativeGap(in Affine actual, NumericsMatrix expected)
    {
        var worst = 0f;
        var columns = new[] { actual.C0, actual.C1, actual.C2, actual.C3 };
        var rows = new[]
        {
            new[] { expected.M11, expected.M12, expected.M13 },
            new[] { expected.M21, expected.M22, expected.M23 },
            new[] { expected.M31, expected.M32, expected.M33 },
            new[] { expected.M41, expected.M42, expected.M43 }
        };
        for (var k = 0; k < 4; k++)
        {
            var column = new[] { columns[k].X, columns[k].Y, columns[k].Z };
            for (var c = 0; c < 3; c++)
                worst = Math.Max(worst, Math.Abs(column[c] - rows[k][c]) / (1f + Math.Max(Math.Abs(column[c]), Math.Abs(rows[k][c]))));
        }
        return worst;
    }

    public static ClipState State(int clip, int layer, ClipMode mode, int mask, Wrap wrap, float phase, float weight, float fade = 1f) => new ClipState
    {
        Clip = new ClipIndex((ushort)clip),
        Layer = (byte)layer,
        Mode = mode,
        Mask = new MaskIndex((ushort)mask),
        Wrap = wrap,
        Synced = 0,
        Phase = phase,
        Speed = 1f,
        Weight = weight,
        Fade = fade,
        FadeRate = 0f
    };

    public static PoseScene Rig()
    {
        var rest = new[] { Pose.Identity, Tilted(0f, RestTilt) };
        var clips = new PoseClip[2];
        for (var c = 0; c < 2; c++)
        {
            var samples = new Pose[6];
            for (var f = 0; f < 3; f++)
            {
                var turn = Quaternion.Multiply(RestTilt, RotateY(0.3f * f * (c + 1)));
                samples[f * 2] = new Pose { Position = new Float3(0f, 0f, f), Rotation = RotateX(0.1f * f), Scale = Float3.One };
                samples[f * 2 + 1] = Tilted(2f * f * (c + 1), turn);
            }
            clips[c] = new PoseClip(samples, new[] { samples[0], samples[1] }, 3);
        }
        return new PoseScene(rest, clips, new[] { new[] { 1f, 1f }, new[] { 0f, 1f } });
    }

    public static PoseScene RandomScene(Random random, int bones, int frames, int clipCount, int maskCount)
    {
        var rest = new Pose[bones];
        for (var b = 0; b < bones; b++)
            rest[b] = RandomPose(random);
        var clips = new PoseClip[clipCount];
        for (var c = 0; c < clipCount; c++)
        {
            var samples = new Pose[frames * bones];
            for (var s = 0; s < samples.Length; s++)
                samples[s] = RandomPose(random);
            var reference = new Pose[bones];
            for (var b = 0; b < bones; b++)
                reference[b] = RandomPose(random);
            clips[c] = new PoseClip(samples, reference, frames);
        }
        var masks = new float[maskCount][];
        for (var m = 0; m < maskCount; m++)
        {
            masks[m] = new float[bones];
            for (var b = 0; b < bones; b++)
            {
                var pick = random.Next(10);
                masks[m][b] = pick < 2 ? 0f : pick < 5 ? 1f : Between(random, 0f, 1f);
            }
        }
        return new PoseScene(rest, clips, masks);
    }

    public static ClipState[] RandomStates(Random random, PoseScene scene)
    {
        var states = new ClipState[random.Next(2, 7)];
        for (var i = 0; i < states.Length; i++)
        {
            states[i] = State(
                random.Next(scene.Clips.Length),
                random.Next(3),
                random.Next(2) == 0 ? ClipMode.Override : ClipMode.Additive,
                random.Next(scene.Masks.Length),
                (Wrap)random.Next(3),
                Between(random, -0.5f, 2.5f),
                random.Next(6) == 0 ? 0f : Between(random, 0.05f, 1.2f),
                random.Next(6) == 0 ? 0f : Between(random, 0.1f, 1f));
        }
        return states;
    }
}

public class PoseMathTests
{
    [Fact]
    public void LerpEndpointsAreItsInputs()
    {
        var random = new Random(7);
        for (var i = 0; i < 100; i++)
        {
            var a = PoseFixtures.RandomPose(random);
            var b = PoseFixtures.RandomPose(random);
            Assert.InRange(PoseFixtures.Gap(PoseMath.Lerp(a, b, 0f), a), 0f, 1e-5f);
            Assert.InRange(PoseFixtures.Gap(PoseMath.Lerp(a, b, 1f), b), 0f, 1e-5f);
        }
    }

    [Fact]
    public void LerpBlendsEachChannelAndDoesNotClampTheFactor()
    {
        var a = new Pose { Position = new Float3(0f, 2f, 4f), Rotation = Quaternion.Identity, Scale = new Float3(1f, 1f, 1f) };
        var b = new Pose { Position = new Float3(2f, 2f, 0f), Rotation = PoseFixtures.RotateY(Scalar.Pi * 0.5f), Scale = new Float3(3f, 1f, 5f) };
        var middle = PoseMath.Lerp(a, b, 0.5f);
        Assert.InRange(PoseFixtures.VectorGap(middle.Position, new Float3(1f, 2f, 2f)), 0f, 1e-6f);
        Assert.InRange(PoseFixtures.VectorGap(middle.Scale, new Float3(2f, 1f, 3f)), 0f, 1e-6f);
        Assert.InRange(PoseFixtures.RotationGap(middle.Rotation, PoseFixtures.RotateY(Scalar.Pi * 0.25f)), 0f, 1e-6f);
        Assert.InRange(PoseFixtures.VectorGap(PoseMath.Lerp(a, b, 2f).Position, new Float3(4f, 2f, -4f)), 0f, 1e-5f);
    }

    [Fact]
    public void AccumulatingADeltaRecoversThePose()
    {
        var random = new Random(11);
        for (var i = 0; i < 100; i++)
        {
            var reference = PoseFixtures.RandomPose(random);
            var pose = PoseFixtures.RandomPose(random);
            Assert.InRange(PoseFixtures.Gap(PoseMath.Accumulate(reference, PoseMath.Delta(pose, reference), 1f), pose), 0f, 1e-4f);
            Assert.InRange(PoseFixtures.Gap(PoseMath.Accumulate(pose, PoseMath.Delta(reference, reference), 0.6f), pose), 0f, 1e-5f);
        }
    }

    [Fact]
    public void ADeltaAgainstItselfChangesNothingAndHalfWeightHalvesTheTurn()
    {
        var random = new Random(13);
        var pose = PoseFixtures.RandomPose(random);
        var neutral = PoseMath.Delta(pose, pose);
        Assert.Equal(Float3.Zero, neutral.Position);
        Assert.Equal(Float3.Zero, neutral.Scale);
        Assert.InRange(PoseFixtures.RotationGap(neutral.Rotation, Quaternion.Identity), 0f, 1e-6f);
        Assert.Equal(0f, PoseFixtures.ComponentGap(PoseMath.Accumulate(pose, neutral, 1f), pose), 5);

        var start = new Pose { Position = new Float3(1f, 0f, 0f), Rotation = Quaternion.Identity, Scale = Float3.One };
        var end = new Pose { Position = new Float3(3f, 0f, 0f), Rotation = PoseFixtures.RotateY(Scalar.Pi * 0.5f), Scale = new Float3(2f, 2f, 2f) };
        var half = PoseMath.Accumulate(start, PoseMath.Delta(end, start), 0.5f);
        Assert.InRange(PoseFixtures.VectorGap(half.Position, new Float3(2f, 0f, 0f)), 0f, 1e-6f);
        Assert.InRange(PoseFixtures.VectorGap(half.Scale, new Float3(1.5f, 1.5f, 1.5f)), 0f, 1e-6f);
        Assert.InRange(PoseFixtures.RotationGap(half.Rotation, PoseFixtures.RotateY(Scalar.Pi * 0.25f)), 0f, 1e-6f);
    }

    [Fact]
    public void MeanOfOneIsItselfAndIgnoresQuaternionSign()
    {
        var random = new Random(3);
        var pose = PoseFixtures.RandomPose(random);
        var flipped = pose;
        flipped.Rotation = -pose.Rotation;
        var sum = PoseMath.Add(PoseMath.Add(default, pose, 0.3f), flipped, 0.7f);
        Assert.InRange(PoseFixtures.Gap(PoseMath.Mean(sum), pose), 0f, 1e-5f);
    }

    [Fact]
    public void TheMeanDoesNotDependOnTheSignOfAnyRotationAfterTheFirst()
    {
        var random = new Random(59);
        for (var i = 0; i < 100; i++)
        {
            var plain = default(PoseSum);
            var flipped = default(PoseSum);
            for (var k = 0; k < 3; k++)
            {
                var pose = PoseFixtures.RandomPose(random);
                var weight = PoseFixtures.Between(random, 0.1f, 1f);
                var other = pose;
                if (k > 0 && random.Next(2) == 0)
                    other.Rotation = -other.Rotation;
                plain = PoseMath.Add(plain, pose, weight);
                flipped = PoseMath.Add(flipped, other, weight);
            }
            Assert.InRange(PoseFixtures.RotationGap(PoseMath.Mean(flipped).Rotation, PoseMath.Mean(plain).Rotation), 0f, 1e-5f);
        }
    }

    [Fact]
    public void NonFiniteFieldsFallBackToTheirNeutralValues()
    {
        var nan = float.NaN;
        var badVector = new Float3(nan, 1f, 2f);
        var badRotation = new Quaternion(nan, 0f, 0f, 1f);
        var bad = new Pose { Position = badVector, Rotation = badRotation, Scale = badVector };
        var good = new Pose { Position = new Float3(1f, 2f, 3f), Rotation = PoseFixtures.RestTilt, Scale = new Float3(2f, 2f, 2f) };

        var lerped = PoseMath.Lerp(bad, good, 0.5f);
        Assert.Equal(Float3.Zero, lerped.Position);
        Assert.Equal(Quaternion.Identity, lerped.Rotation);
        Assert.Equal(Float3.One, lerped.Scale);

        var delta = PoseMath.Delta(bad, good);
        Assert.Equal(Float3.Zero, delta.Position);
        Assert.Equal(Quaternion.Identity, delta.Rotation);
        Assert.Equal(Float3.Zero, delta.Scale);

        var accumulated = PoseMath.Accumulate(good, bad, 1f);
        Assert.Equal(Float3.Zero, accumulated.Position);
        Assert.Equal(Float3.One, accumulated.Scale);
        Assert.InRange(PoseFixtures.RotationGap(accumulated.Rotation, good.Rotation), 0f, 1e-6f);

        var spoiled = PoseMath.Add(PoseMath.Add(default, good, 1f), bad, 1f);
        Assert.Equal(Float3.Zero, spoiled.Position);
        Assert.Equal(Float3.Zero, spoiled.Scale);
        Assert.Equal(default(Quaternion), spoiled.Rotation);
        Assert.Equal(2f, spoiled.Weight);

        var mean = PoseMath.Mean(new PoseSum { Position = badVector, Rotation = badRotation, Scale = badVector, Weight = 2f });
        Assert.Equal(Float3.Zero, mean.Position);
        Assert.Equal(Quaternion.Identity, mean.Rotation);
        Assert.Equal(Float3.One, mean.Scale);

        Assert.Equal(0f, PoseFixtures.AffineGap(PoseMath.Affine(bad), Affine.Identity));
        var product = PoseMath.Mul(new Affine(badVector, Float3.Zero, Float3.Zero, Float3.Zero), Affine.Identity);
        Assert.Equal(0f, PoseFixtures.AffineGap(product, default));
        Assert.Equal(0f, PoseFixtures.ComponentGap(ClipSampling.At(new[] { bad }, 1, 0, 0f), Pose.Identity));
    }

    [Fact]
    public void MeanIsTheWeightedAverageOfTheSummedPoses()
    {
        var a = new Pose { Position = new Float3(4f, 0f, 0f), Rotation = Quaternion.Identity, Scale = new Float3(1f, 1f, 1f) };
        var b = new Pose { Position = new Float3(0f, 8f, 0f), Rotation = PoseFixtures.RotateX(Scalar.Pi * 0.5f), Scale = new Float3(5f, 5f, 5f) };
        var sum = PoseMath.Add(PoseMath.Add(default, a, 1f), b, 3f);
        Assert.Equal(4f, sum.Weight);
        var mean = PoseMath.Mean(sum);
        Assert.InRange(PoseFixtures.VectorGap(mean.Position, new Float3(1f, 6f, 0f)), 0f, 1e-6f);
        Assert.InRange(PoseFixtures.VectorGap(mean.Scale, new Float3(4f, 4f, 4f)), 0f, 1e-6f);
        Assert.InRange(Quaternion.Dot(mean.Rotation, mean.Rotation), 0.999999f, 1.000001f);
    }

    [Fact]
    public void AnEmptySumHasTheDocumentedMean()
    {
        var mean = PoseMath.Mean(default);
        Assert.Equal(Float3.Zero, mean.Position);
        Assert.Equal(Float3.Zero, mean.Scale);
        Assert.Equal(Quaternion.Identity, mean.Rotation);
    }

    [Fact]
    public void AddingWithoutAUsableWeightKeepsTheSum()
    {
        var random = new Random(17);
        var sum = PoseMath.Add(default, PoseFixtures.RandomPose(random), 0.4f);
        var pose = PoseFixtures.RandomPose(random);
        foreach (var weight in new[] { 0f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            var kept = PoseMath.Add(sum, pose, weight);
            Assert.Equal(sum.Weight, kept.Weight);
            Assert.Equal(sum.Position, kept.Position);
            Assert.Equal(sum.Rotation, kept.Rotation);
            Assert.Equal(sum.Scale, kept.Scale);
        }
    }

    [Fact]
    public void ANonFiniteFactorOrWeightActsAsZero()
    {
        var random = new Random(53);
        var a = PoseFixtures.RandomPose(random);
        var b = PoseFixtures.RandomPose(random);
        foreach (var factor in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Assert.Equal(0f, PoseFixtures.ComponentGap(PoseMath.Lerp(a, b, factor), PoseMath.Lerp(a, b, 0f)));
            Assert.Equal(0f, PoseFixtures.ComponentGap(PoseMath.Accumulate(a, b, factor), PoseMath.Accumulate(a, b, 0f)));
            Assert.Equal(0f, PoseFixtures.RotationGap(PoseMath.Nlerp(a.Rotation, b.Rotation, factor), PoseMath.Nlerp(a.Rotation, b.Rotation, 0f)));
        }
        Assert.InRange(PoseFixtures.Gap(PoseMath.Lerp(a, b, float.NaN), a), 0f, 1e-6f);
    }

    [Fact]
    public void AlignFlipsOnlyAcrossHemispheres()
    {
        var q = PoseFixtures.RestTilt;
        Assert.Equal(q, PoseMath.Align(q, q));
        Assert.Equal(q, PoseMath.Align(-q, q));
        Assert.Equal(-q, PoseMath.Align(q, -q));
        var sideways = new Quaternion(1f, 0f, 0f, 0f);
        Assert.Equal(sideways, PoseMath.Align(sideways, Quaternion.Identity));
    }

    [Fact]
    public void NlerpTakesTheShortArcAndNormalizes()
    {
        var q = PoseFixtures.RestTilt;
        Assert.InRange(PoseFixtures.RotationGap(PoseMath.Nlerp(q, -q, 0.5f), q), 0f, 1e-6f);
        var scaled = new Quaternion(0f, 0f, 0f, 4f);
        var turned = PoseFixtures.RotateY(Scalar.Pi * 0.5f);
        var middle = PoseMath.Nlerp(scaled, turned, 0.5f);
        Assert.InRange(Quaternion.Dot(middle, middle), 0.999999f, 1.000001f);
        Assert.InRange(PoseFixtures.RotationGap(PoseMath.Nlerp(Quaternion.Identity, turned, 0.5f), PoseFixtures.RotateY(Scalar.Pi * 0.25f)), 0f, 1e-6f);
    }

    [Fact]
    public void AffineMatchesMatrix4x4Composition()
    {
        var random = new Random(5);
        for (var i = 0; i < 50; i++)
        {
            var a = PoseFixtures.RandomPose(random);
            var b = PoseFixtures.RandomPose(random);
            Assert.InRange(PoseFixtures.RelativeGap(PoseMath.Affine(a), PoseFixtures.Matrix(a)), 0f, 1e-5f);
            var composed = PoseMath.Mul(PoseMath.Affine(a), PoseMath.Affine(b));
            Assert.InRange(PoseFixtures.RelativeGap(composed, PoseFixtures.Matrix(b) * PoseFixtures.Matrix(a)), 0f, 1e-5f);
        }
    }

    [Fact]
    public void AffineOfTheIdentityPoseIsTheIdentityMatrixAndMulIsAssociative()
    {
        Assert.Equal(0f, PoseFixtures.AffineGap(PoseMath.Affine(Pose.Identity), Affine.Identity));
        var random = new Random(19);
        for (var i = 0; i < 50; i++)
        {
            var a = PoseMath.Affine(PoseFixtures.RandomPose(random));
            var b = PoseMath.Affine(PoseFixtures.RandomPose(random));
            var c = PoseMath.Affine(PoseFixtures.RandomPose(random));
            Assert.Equal(0f, PoseFixtures.AffineGap(PoseMath.Mul(Affine.Identity, a), a));
            Assert.Equal(0f, PoseFixtures.AffineGap(PoseMath.Mul(a, Affine.Identity), a));
            var left = PoseMath.Mul(PoseMath.Mul(a, b), c);
            var right = PoseMath.Mul(a, PoseMath.Mul(b, c));
            Assert.InRange(PoseFixtures.AffineGap(left, right) / (1f + PoseFixtures.VectorGap(left.C3, Float3.Zero)), 0f, 1e-5f);
        }
    }

    [Fact]
    public void AffineHasTheUnityLayoutByteForByte()
    {
        Assert.Equal(48, Marshal.SizeOf<Affine>());
        Assert.Equal(0, (int)Marshal.OffsetOf<Affine>(nameof(Affine.C0)));
        Assert.Equal(12, (int)Marshal.OffsetOf<Affine>(nameof(Affine.C1)));
        Assert.Equal(24, (int)Marshal.OffsetOf<Affine>(nameof(Affine.C2)));
        Assert.Equal(36, (int)Marshal.OffsetOf<Affine>(nameof(Affine.C3)));
    }
}

public class PoseSamplingTests
{
    static Pose[] TwoBoneClip()
    {
        var samples = new Pose[6];
        for (var f = 0; f < 3; f++)
        {
            samples[f * 2] = new Pose { Position = new Float3(0f, 0f, f), Rotation = PoseFixtures.RotateX(0.1f * f), Scale = Float3.One };
            samples[f * 2 + 1] = PoseFixtures.Tilted(2f * f, Quaternion.Multiply(PoseFixtures.RestTilt, PoseFixtures.RotateY(0.3f * f)));
        }
        return samples;
    }

    [Fact]
    public void SamplingHitsFramesAndInterpolates()
    {
        var samples = TwoBoneClip();
        Assert.Equal(2f, ClipSampling.At(samples, 2, 1, 1f).Position.X, 5);
        Assert.Equal(1f, ClipSampling.At(samples, 2, 1, 0.5f).Position.X, 5);
        Assert.Equal(3f, ClipSampling.At(samples, 2, 1, 1.5f).Position.X, 5);
        Assert.Equal(4f, ClipSampling.At(samples, 2, 1, 99f).Position.X, 5);
        Assert.Equal(0f, ClipSampling.At(samples, 2, 1, -5f).Position.X, 5);
        Assert.Equal(1.5f, ClipSampling.At(samples, 2, 0, 1.5f).Position.Z, 5);
        Assert.InRange(PoseFixtures.Gap(ClipSampling.At(samples, 2, 1, 2f), samples[5]), 0f, 1e-6f);
    }

    [Fact]
    public void SamplingBlendsRotationAlongTheShortArc()
    {
        var samples = new[]
        {
            Pose.Identity,
            new Pose { Position = Float3.Zero, Rotation = -PoseFixtures.RotateY(Scalar.Pi * 0.5f), Scale = Float3.One }
        };
        var middle = ClipSampling.At(samples, 1, 0, 0.5f);
        Assert.InRange(PoseFixtures.RotationGap(middle.Rotation, PoseFixtures.RotateY(Scalar.Pi * 0.25f)), 0f, 1e-6f);
    }

    [Fact]
    public void ASingleFrameClipIsConstant()
    {
        var samples = new[] { PoseFixtures.Tilted(5f, PoseFixtures.RestTilt) };
        foreach (var frame in new[] { -3f, 0f, 0.5f, 7f })
            Assert.InRange(PoseFixtures.Gap(ClipSampling.At(samples, 1, 0, frame), samples[0]), 0f, 1e-6f);
    }

    [Fact]
    public void BadSpansAndIndicesGiveTheIdentityPose()
    {
        var samples = TwoBoneClip();
        Assert.Equal(0f, PoseFixtures.ComponentGap(ClipSampling.At(ReadOnlySpan<Pose>.Empty, 2, 0, 0f), Pose.Identity));
        Assert.Equal(0f, PoseFixtures.ComponentGap(ClipSampling.At(samples, 0, 0, 0f), Pose.Identity));
        Assert.Equal(0f, PoseFixtures.ComponentGap(ClipSampling.At(samples, -2, 0, 0f), Pose.Identity));
        Assert.Equal(0f, PoseFixtures.ComponentGap(ClipSampling.At(samples, 2, -1, 0f), Pose.Identity));
        Assert.Equal(0f, PoseFixtures.ComponentGap(ClipSampling.At(samples, 2, 2, 0f), Pose.Identity));
        Assert.Equal(0f, PoseFixtures.ComponentGap(ClipSampling.At(samples, int.MaxValue, 0, 0f), Pose.Identity));
        Assert.Equal(0f, PoseFixtures.ComponentGap(ClipSampling.At(samples.AsSpan(0, 1), 2, 0, 0f), Pose.Identity));
    }

    [Fact]
    public void NonFiniteFramesFallToTheNearestValidFrame()
    {
        var samples = TwoBoneClip();
        Assert.Equal(0f, ClipSampling.At(samples, 2, 1, float.NaN).Position.X, 5);
        Assert.Equal(0f, ClipSampling.At(samples, 2, 1, float.NegativeInfinity).Position.X, 5);
        Assert.Equal(4f, ClipSampling.At(samples, 2, 1, float.PositiveInfinity).Position.X, 5);
    }

    [Fact]
    public void TrailingSamplesThatDoNotFillAFrameAreIgnored()
    {
        var samples = TwoBoneClip();
        var padded = new Pose[samples.Length + 1];
        samples.CopyTo(padded, 0);
        padded[^1] = PoseFixtures.Tilted(100f, Quaternion.Identity);
        Assert.Equal(4f, ClipSampling.At(padded, 2, 1, 99f).Position.X, 5);
        Assert.Equal(2f, ClipSampling.At(padded, 2, 1, 1f).Position.X, 5);
    }
}

public class PoseBlendTests
{
    [Fact]
    public void NextLayerWalksTheLayersInAscendingOrder()
    {
        var states = new[]
        {
            PoseFixtures.State(0, 2, ClipMode.Override, 0, Wrap.Loop, 0f, 1f),
            PoseFixtures.State(0, 0, ClipMode.Additive, 0, Wrap.Loop, 0f, 1f),
            PoseFixtures.State(0, 0, ClipMode.Override, 0, Wrap.Loop, 0f, 1f),
            PoseFixtures.State(0, 255, ClipMode.Override, 0, Wrap.Loop, 0f, 1f),
            PoseFixtures.State(0, 1, ClipMode.Override, 0, Wrap.Loop, 0f, 1f)
        };
        Assert.Equal(0, PoseBlend.NextLayer(states, -1));
        Assert.Equal(1, PoseBlend.NextLayer(states, 0));
        Assert.Equal(2, PoseBlend.NextLayer(states, 1));
        Assert.Equal(255, PoseBlend.NextLayer(states, 2));
        Assert.Equal(-1, PoseBlend.NextLayer(states, 255));
        Assert.Equal(0, PoseBlend.NextLayer(states, int.MinValue));
        Assert.Equal(-1, PoseBlend.NextLayer(states, int.MaxValue));
        Assert.Equal(-1, PoseBlend.NextLayer(ReadOnlySpan<ClipState>.Empty, -1));
    }

    [Fact]
    public void OverrideAddsWeightedSamplesAndSkipsZeroWeights()
    {
        var scene = PoseFixtures.Rig();
        var clip = scene.Clips[0];
        var sums = new PoseSum[2];
        PoseBlend.Override(sums, clip.Samples, clip.FrameCount, 1f, new[] { 0f, 0.5f }, 0.8f);
        Assert.Equal(default(PoseSum), sums[0]);
        Assert.Equal(0.4f, sums[1].Weight, 6);
        Assert.Equal(0.4f * 2f, sums[1].Position.X, 5);
        PoseBlend.Override(sums, scene.Clips[1].Samples, scene.Clips[1].FrameCount, 2f, new[] { 0f, 1f }, 0.2f);
        Assert.Equal(default(PoseSum), sums[0]);
        Assert.Equal(0.6f, sums[1].Weight, 6);
        Assert.Equal(0.4f * 2f + 0.2f * 8f, sums[1].Position.X, 5);
        PoseBlend.Override(sums, clip.Samples, clip.FrameCount, 1f, new[] { 1f, 1f }, 0f);
        PoseBlend.Override(sums, clip.Samples, clip.FrameCount, 1f, new[] { 1f, 1f }, -1f);
        Assert.Equal(default(PoseSum), sums[0]);
        Assert.Equal(0.6f, sums[1].Weight, 6);
    }

    [Fact]
    public void ResolveBlendsByTheSummedWeightAndClearsTheSums()
    {
        var rest = new[] { Pose.Identity, Pose.Identity, Pose.Identity };
        var target = new Pose { Position = new Float3(4f, 0f, 0f), Rotation = Quaternion.Identity, Scale = Float3.One };
        var pose = (Pose[])rest.Clone();
        var sums = new PoseSum[3];
        sums[0] = PoseMath.Add(default, target, 0.25f);
        sums[1] = PoseMath.Add(PoseMath.Add(default, target, 1f), target, 1f);
        PoseBlend.Resolve(pose, sums);
        Assert.Equal(1f, pose[0].Position.X, 5);
        Assert.Equal(4f, pose[1].Position.X, 5);
        Assert.Equal(0f, PoseFixtures.ComponentGap(pose[2], Pose.Identity));
        foreach (var sum in sums)
            Assert.Equal(default(PoseSum), sum);
    }

    [Fact]
    public void AdditiveAppliesTheDeltaFromItsReferenceScaledByTheMask()
    {
        var scene = PoseFixtures.Rig();
        var clip = scene.Clips[1];
        var pose = (Pose[])scene.Rest.Clone();
        PoseBlend.Additive(pose, clip.Samples, clip.Reference, clip.FrameCount, 2f, new[] { 0f, 0.5f }, 0.5f);
        Assert.Equal(0f, PoseFixtures.ComponentGap(pose[0], scene.Rest[0]));
        var expected = PoseMath.Accumulate(scene.Rest[1], PoseMath.Delta(clip.Samples[5], clip.Reference[1]), 0.25f);
        Assert.InRange(PoseFixtures.Gap(pose[1], expected), 0f, 1e-6f);
    }

    [Fact]
    public void SpansOfDifferentLengthsUseTheShortest()
    {
        var scene = PoseFixtures.Rig();
        var clip = scene.Clips[0];
        var sums = new PoseSum[3];
        PoseBlend.Override(sums, clip.Samples, clip.FrameCount, 1f, new[] { 1f, 1f, 1f }, 1f);
        Assert.Equal(1f, sums[0].Weight);
        Assert.Equal(1f, sums[1].Weight);
        Assert.Equal(default(PoseSum), sums[2]);
        var shortMask = new PoseSum[2];
        PoseBlend.Override(shortMask, clip.Samples, clip.FrameCount, 1f, new[] { 1f }, 1f);
        Assert.Equal(1f, shortMask[0].Weight);
        Assert.Equal(default(PoseSum), shortMask[1]);
        var pose = (Pose[])scene.Rest.Clone();
        PoseBlend.Additive(pose, clip.Samples, clip.Reference.AsSpan(0, 1), clip.FrameCount, 2f, new[] { 1f, 1f }, 1f);
        Assert.Equal(0f, PoseFixtures.ComponentGap(pose[1], scene.Rest[1]));
        Assert.NotEqual(0f, PoseFixtures.ComponentGap(pose[0], scene.Rest[0]));
        var one = new[] { Pose.Identity };
        PoseBlend.Resolve(one, shortMask);
        Assert.All(shortMask, sum => Assert.Equal(default(PoseSum), sum));
    }

    [Fact]
    public void EmptyInputsAndInconsistentCountsChangeNothing()
    {
        var scene = PoseFixtures.Rig();
        var clip = scene.Clips[0];
        var sums = new PoseSum[2];
        var pose = (Pose[])scene.Rest.Clone();
        PoseBlend.Override(sums, ReadOnlySpan<Pose>.Empty, clip.FrameCount, 1f, new[] { 1f, 1f }, 1f);
        PoseBlend.Override(sums, clip.Samples, 0, 1f, new[] { 1f, 1f }, 1f);
        PoseBlend.Override(sums, clip.Samples, -3, 1f, new[] { 1f, 1f }, 1f);
        PoseBlend.Override(sums, clip.Samples, int.MaxValue, 1f, new[] { 1f, 1f }, 1f);
        PoseBlend.Override(Span<PoseSum>.Empty, clip.Samples, clip.FrameCount, 1f, new[] { 1f, 1f }, 1f);
        PoseBlend.Override(sums, clip.Samples, clip.FrameCount, 1f, ReadOnlySpan<float>.Empty, 1f);
        PoseBlend.Additive(pose, ReadOnlySpan<Pose>.Empty, clip.Reference, clip.FrameCount, 1f, new[] { 1f, 1f }, 1f);
        PoseBlend.Additive(pose, clip.Samples, clip.Reference, 0, 1f, new[] { 1f, 1f }, 1f);
        PoseBlend.Additive(pose, clip.Samples, ReadOnlySpan<Pose>.Empty, clip.FrameCount, 1f, new[] { 1f, 1f }, 1f);
        PoseBlend.Additive(Span<Pose>.Empty, clip.Samples, clip.Reference, clip.FrameCount, 1f, new[] { 1f, 1f }, 1f);
        PoseBlend.Resolve(Span<Pose>.Empty, Span<PoseSum>.Empty);
        Assert.All(sums, sum => Assert.Equal(default(PoseSum), sum));
        for (var b = 0; b < 2; b++)
            Assert.Equal(0f, PoseFixtures.ComponentGap(pose[b], scene.Rest[b]));
    }

    [Fact]
    public void EmptyStatesGiveTheRestPose()
    {
        var scene = PoseFixtures.Rig();
        var pose = scene.Blend(ReadOnlySpan<ClipState>.Empty);
        Assert.Equal(0f, PoseFixtures.ComponentGap(pose[0], scene.Rest[0]));
        Assert.Equal(0f, PoseFixtures.ComponentGap(pose[1], scene.Rest[1]));
    }

    [Fact]
    public void SingleClipIsItsSampleAndHalfWeightIsHalfway()
    {
        var scene = PoseFixtures.Rig();
        var full = new[] { PoseFixtures.State(0, 0, ClipMode.Override, 0, Wrap.Clamp, 0.5f, 1f) };
        Assert.Equal(2f, scene.Blend(full)[1].Position.X, 5);
        var half = new[] { PoseFixtures.State(0, 0, ClipMode.Override, 0, Wrap.Clamp, 0.5f, 0.5f) };
        Assert.Equal(1f, scene.Blend(half)[1].Position.X, 5);
    }

    [Fact]
    public void OverrideLayerStacksOnTheLayerBelow()
    {
        var scene = PoseFixtures.Rig();
        var states = new[]
        {
            PoseFixtures.State(0, 0, ClipMode.Override, 0, Wrap.Loop, 0f, 1f),
            PoseFixtures.State(1, 1, ClipMode.Override, 0, Wrap.Clamp, 1f, 0.8f)
        };
        Assert.Equal(0.8f * 8f, scene.Blend(states)[1].Position.X, 4);
    }

    [Fact]
    public void MaskZeroLeavesTheLayerBelow()
    {
        var scene = PoseFixtures.Rig();
        var states = new[] { PoseFixtures.State(1, 1, ClipMode.Override, 1, Wrap.Clamp, 1f, 1f) };
        var pose = scene.Blend(states);
        Assert.Equal(0f, PoseFixtures.ComponentGap(pose[0], scene.Rest[0]));
        Assert.Equal(8f, pose[1].Position.X, 4);
    }

    [Fact]
    public void AdditiveAppliesDeltaFromItsReferenceOnNonIdentityRest()
    {
        var scene = PoseFixtures.Rig();
        var states = new[] { PoseFixtures.State(0, 1, ClipMode.Additive, 0, Wrap.Clamp, 1f, 1f) };
        var clip = scene.Clips[0];
        var expected = PoseMath.Accumulate(scene.Rest[1], PoseMath.Delta(clip.Samples[2 * 2 + 1], clip.Reference[1]), 1f);
        var actual = scene.Blend(states)[1];
        Assert.InRange(PoseFixtures.Gap(actual, expected), 0f, 1e-5f);
        var turned = Quaternion.Multiply(scene.Rest[1].Rotation, PoseFixtures.RotateY(0.6f));
        Assert.InRange(PoseFixtures.RotationGap(expected.Rotation, turned), 0f, 1e-5f);
    }

    [Fact]
    public void EvaluationIgnoresStateOrder()
    {
        var scene = PoseFixtures.Rig();
        var forward = new[]
        {
            PoseFixtures.State(0, 2, ClipMode.Additive, 0, Wrap.Loop, 0.37f, 1f),
            PoseFixtures.State(0, 0, ClipMode.Override, 0, Wrap.Loop, 0.37f, 0.3f),
            PoseFixtures.State(1, 0, ClipMode.Override, 0, Wrap.Loop, 0.37f, 0.7f)
        };
        var backward = forward.Reverse().ToArray();
        var a = scene.Blend(forward);
        var b = scene.Blend(backward);
        for (var bone = 0; bone < 2; bone++)
            Assert.InRange(PoseFixtures.Gap(a[bone], b[bone]), 0f, 1e-5f);
    }

    [Fact]
    public void LayerByLayerBlendMatchesThePerBoneReferenceOn200RandomStateLists()
    {
        var random = new Random(7);
        var withOverride = 0;
        var withAdditive = 0;
        var withBoth = 0;
        var stackedOverrides = 0;
        var moved = 0;
        for (var run = 0; run < 200; run++)
        {
            var scene = PoseFixtures.RandomScene(random, 3, 5, 3, 3);
            var states = PoseFixtures.RandomStates(random, scene);
            var blended = scene.Blend(states);
            var moving = false;
            for (var bone = 0; bone < scene.Bones; bone++)
            {
                var expected = scene.PerBone(states, bone);
                var gap = PoseFixtures.ComponentGap(expected, blended[bone]);
                Assert.True(gap <= 1e-5f, "run " + run + " bone " + bone + " component gap " + gap);
                moving |= PoseFixtures.ComponentGap(expected, scene.Rest[bone]) > 1e-3f;
            }
            var hasOverride = states.Any(s => s.Mode == ClipMode.Override && scene.Contributes(s));
            var hasAdditive = states.Any(s => s.Mode == ClipMode.Additive && scene.Contributes(s));
            var stacked = states.Where(s => s.Mode == ClipMode.Override && scene.Contributes(s)).GroupBy(s => s.Layer).Any(g => g.Count() > 1);
            withOverride += hasOverride ? 1 : 0;
            withAdditive += hasAdditive ? 1 : 0;
            withBoth += hasOverride && hasAdditive ? 1 : 0;
            stackedOverrides += stacked ? 1 : 0;
            moved += moving ? 1 : 0;
        }
        Assert.True(withOverride >= 120, "override coverage " + withOverride);
        Assert.True(withAdditive >= 100, "additive coverage " + withAdditive);
        Assert.True(withBoth >= 50, "mixed coverage " + withBoth);
        Assert.True(stackedOverrides >= 20, "stacked coverage " + stackedOverrides);
        Assert.True(moved >= 150, "moved coverage " + moved);
    }

    [Fact]
    public void DenselyStackedLayersMatchThePerBoneReference()
    {
        var random = new Random(8);
        for (var run = 0; run < 200; run++)
        {
            var scene = PoseFixtures.RandomScene(random, 3, 5, 3, 3);
            var states = new ClipState[random.Next(4, 9)];
            for (var i = 0; i < states.Length; i++)
            {
                states[i] = PoseFixtures.State(
                    random.Next(3),
                    random.Next(2),
                    random.Next(2) == 0 ? ClipMode.Override : ClipMode.Additive,
                    random.Next(3),
                    (Wrap)random.Next(3),
                    PoseFixtures.Between(random, -0.5f, 2.5f),
                    PoseFixtures.Between(random, 0.1f, 1f),
                    PoseFixtures.Between(random, 0.5f, 1f));
            }
            var blended = scene.Blend(states);
            for (var bone = 0; bone < scene.Bones; bone++)
            {
                var gap = PoseFixtures.ComponentGap(scene.PerBone(states, bone), blended[bone]);
                Assert.True(gap <= 1e-5f, "run " + run + " bone " + bone + " component gap " + gap);
            }
        }
    }

    [Fact]
    public void ANegativeWeightNeverContributesEvenThroughANegativeMask()
    {
        var scene = PoseFixtures.Rig();
        var clip = scene.Clips[0];
        var sums = new PoseSum[2];
        var pose = (Pose[])scene.Rest.Clone();
        PoseBlend.Override(sums, clip.Samples, clip.FrameCount, 1f, new[] { -1f, -1f }, -1f);
        PoseBlend.Additive(pose, clip.Samples, clip.Reference, clip.FrameCount, 1f, new[] { -1f, -1f }, -1f);
        Assert.All(sums, sum => Assert.Equal(default(PoseSum), sum));
        for (var bone = 0; bone < 2; bone++)
            Assert.Equal(0f, PoseFixtures.ComponentGap(pose[bone], scene.Rest[bone]));
    }

    [Fact]
    public void BlendingTheSameStatesTwiceIsBitIdentical()
    {
        var random = new Random(23);
        for (var run = 0; run < 50; run++)
        {
            var scene = PoseFixtures.RandomScene(random, 3, 5, 3, 3);
            var states = PoseFixtures.RandomStates(random, scene);
            var first = scene.Blend(states);
            var second = scene.Blend(states);
            for (var bone = 0; bone < scene.Bones; bone++)
                Assert.Equal(0f, PoseFixtures.ComponentGap(first[bone], second[bone]));
        }
    }

    [Fact]
    public void SampledFramesMatchThePerBoneSamplerForEveryFraction()
    {
        var random = new Random(29);
        var scene = PoseFixtures.RandomScene(random, 3, 5, 1, 1);
        var clip = scene.Clips[0];
        var full = new[] { 1f, 1f, 1f };
        foreach (var frame in new[] { -2f, 0f, 0.25f, 1f, 2.5f, 3.999f, 4f, 4.5f, 40f })
        {
            var sums = new PoseSum[3];
            PoseBlend.Override(sums, clip.Samples, clip.FrameCount, frame, full, 1f);
            for (var bone = 0; bone < 3; bone++)
            {
                var sampled = ClipSampling.At(clip.Samples, 3, bone, frame);
                Assert.Equal(0f, PoseFixtures.ComponentGap(PoseMath.Mean(sums[bone]), PoseMath.Mean(PoseMath.Add(default, sampled, 1f))));
            }
        }
    }

    [Fact]
    public void KernelsDoNotAllocate()
    {
        var random = new Random(41);
        var scene = PoseFixtures.RandomScene(random, 4, 5, 2, 2);
        var states = PoseFixtures.RandomStates(random, scene);
        var clip = scene.Clips[0];
        var pose = (Pose[])scene.Rest.Clone();
        var sums = new PoseSum[4];
        var parents = new[] { -1, 0, 1, 1 };
        var model = new Affine[4];
        var mask = scene.Masks[0];
        var sink = 0f;

        void Run()
        {
            PoseBlend.Override(sums, clip.Samples, clip.FrameCount, 1.5f, mask, 0.7f);
            PoseBlend.Resolve(pose, sums);
            PoseBlend.Additive(pose, clip.Samples, clip.Reference, clip.FrameCount, 2.5f, mask, 0.4f);
            Hierarchy.Model(parents, pose, model);
            sink += PoseBlend.NextLayer(states, 0) + ClipSampling.At(clip.Samples, 4, 2, 1.25f).Position.X;
            sink += PoseMath.Mul(model[1], model[2]).C3.X;
        }

        Run();
        Run();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
            Run();
        var after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0L, after - before);
        Assert.False(float.IsNaN(sink));
    }
}

public class PoseHierarchyTests
{
    [Fact]
    public void AChainMatchesHandComposedMatrices()
    {
        var local = new[]
        {
            new Pose { Position = new Float3(1f, 0f, 0f), Rotation = Quaternion.Identity, Scale = Float3.One },
            new Pose { Position = new Float3(0f, 2f, 0f), Rotation = PoseFixtures.RotateZ(Scalar.Pi * 0.5f), Scale = new Float3(2f, 1f, 1f) },
            new Pose { Position = new Float3(3f, 0f, 0f), Rotation = Quaternion.Identity, Scale = Float3.One }
        };
        var model = new Affine[3];
        Hierarchy.Model(new[] { -1, 0, 1 }, local, model);

        Assert.InRange(PoseFixtures.VectorGap(model[0].C3, new Float3(1f, 0f, 0f)), 0f, 1e-6f);
        Assert.InRange(PoseFixtures.VectorGap(model[1].C0, new Float3(0f, 2f, 0f)), 0f, 1e-6f);
        Assert.InRange(PoseFixtures.VectorGap(model[1].C1, new Float3(-1f, 0f, 0f)), 0f, 1e-6f);
        Assert.InRange(PoseFixtures.VectorGap(model[1].C2, new Float3(0f, 0f, 1f)), 0f, 1e-6f);
        Assert.InRange(PoseFixtures.VectorGap(model[1].C3, new Float3(1f, 2f, 0f)), 0f, 1e-6f);
        Assert.InRange(PoseFixtures.VectorGap(model[2].C3, new Float3(1f, 8f, 0f)), 0f, 1e-5f);
        Assert.InRange(PoseFixtures.VectorGap(model[2].C0, new Float3(0f, 2f, 0f)), 0f, 1e-6f);
    }

    [Fact]
    public void ATreeMatchesMatrix4x4WorldTransforms()
    {
        var random = new Random(31);
        for (var run = 0; run < 20; run++)
        {
            var count = 12;
            var parents = new int[count];
            var local = new Pose[count];
            var expected = new NumericsMatrix[count];
            for (var bone = 0; bone < count; bone++)
            {
                parents[bone] = bone == 0 ? -1 : random.Next(-1, bone);
                local[bone] = PoseFixtures.RandomPose(random);
                var own = PoseFixtures.Matrix(local[bone]);
                expected[bone] = parents[bone] >= 0 ? own * expected[parents[bone]] : own;
            }
            var model = new Affine[count];
            Hierarchy.Model(parents, local, model);
            for (var bone = 0; bone < count; bone++)
                Assert.InRange(PoseFixtures.RelativeGap(model[bone], expected[bone]), 0f, 1e-5f);
        }
    }

    [Fact]
    public void AParentThatIsNotSmallerOrIsOutOfRangeMakesTheBoneARoot()
    {
        var random = new Random(37);
        var local = new Pose[5];
        for (var bone = 0; bone < local.Length; bone++)
            local[bone] = PoseFixtures.RandomPose(random);
        var model = new Affine[5];
        Hierarchy.Model(new[] { -1, 1, 4, int.MaxValue, int.MinValue }, local, model);
        for (var bone = 0; bone < 5; bone++)
            Assert.Equal(0f, PoseFixtures.AffineGap(model[bone], PoseMath.Affine(local[bone])));

        Hierarchy.Model(new[] { -1, 0, 3, 1, 9 }, local, model);
        Assert.Equal(0f, PoseFixtures.AffineGap(model[0], PoseMath.Affine(local[0])));
        Assert.Equal(0f, PoseFixtures.AffineGap(model[1], PoseMath.Mul(model[0], PoseMath.Affine(local[1]))));
        Assert.Equal(0f, PoseFixtures.AffineGap(model[2], PoseMath.Affine(local[2])));
        Assert.Equal(0f, PoseFixtures.AffineGap(model[3], PoseMath.Mul(model[1], PoseMath.Affine(local[3]))));
        Assert.Equal(0f, PoseFixtures.AffineGap(model[4], PoseMath.Affine(local[4])));
    }

    [Fact]
    public void TheShortestSpanDecidesHowManyBonesAreWritten()
    {
        var random = new Random(43);
        var local = new[] { PoseFixtures.RandomPose(random), PoseFixtures.RandomPose(random), PoseFixtures.RandomPose(random) };
        var sentinel = new Affine(new Float3(7f, 7f, 7f), new Float3(7f, 7f, 7f), new Float3(7f, 7f, 7f), new Float3(7f, 7f, 7f));

        var wide = new[] { sentinel, sentinel, sentinel, sentinel };
        Hierarchy.Model(new[] { -1, 0 }, local, wide);
        Assert.NotEqual(0f, PoseFixtures.AffineGap(wide[0], sentinel));
        Assert.NotEqual(0f, PoseFixtures.AffineGap(wide[1], sentinel));
        Assert.Equal(0f, PoseFixtures.AffineGap(wide[2], sentinel));
        Assert.Equal(0f, PoseFixtures.AffineGap(wide[3], sentinel));

        var narrow = new[] { sentinel, sentinel };
        Hierarchy.Model(new[] { -1, 0, 1 }, local, narrow);
        Assert.NotEqual(0f, PoseFixtures.AffineGap(narrow[1], sentinel));

        Hierarchy.Model(ReadOnlySpan<int>.Empty, local, wide);
        Hierarchy.Model(new[] { -1 }, ReadOnlySpan<Pose>.Empty, wide);
        Hierarchy.Model(new[] { -1 }, local, Span<Affine>.Empty);
    }
}

public class PoseTotalityTests
{
    static readonly float[] Hostile =
    {
        float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, -0f, float.Epsilon, float.MaxValue, -float.MaxValue,
        1e-30f, -1e-30f, 1e30f, -1e30f, 1f, -1f, 0.5f, 2f, 3.7f, -2.4f
    };

    static readonly float[] Extreme =
    {
        0f, -0f, float.Epsilon, float.MaxValue, -float.MaxValue, 1e-30f, -1e-30f, 1e30f, -1e30f, 1e19f, -1e19f, 1f, -1f, 0.5f, 2f, 3.7f
    };

    static readonly int[] FrameCounts = { int.MinValue, -3, 0, 1, 2, 3, 5, 15, int.MaxValue };

    static float Pick(Random random, float[] palette) => palette[random.Next(palette.Length)];

    static Float3 PickVector(Random random, float[] palette) =>
        new Float3(Pick(random, palette), Pick(random, palette), Pick(random, palette));

    static Quaternion PickRotation(Random random, float[] palette) =>
        new Quaternion(Pick(random, palette), Pick(random, palette), Pick(random, palette), Pick(random, palette));

    static Pose PickPose(Random random, float[] palette) => new Pose
    {
        Position = PickVector(random, palette),
        Rotation = PickRotation(random, palette),
        Scale = PickVector(random, palette)
    };

    static PoseSum PickSum(Random random, float[] palette) => new PoseSum
    {
        Position = PickVector(random, palette),
        Rotation = PickRotation(random, palette),
        Scale = PickVector(random, palette),
        Weight = Pick(random, palette)
    };

    static Affine PickAffine(Random random, float[] palette) =>
        new Affine(PickVector(random, palette), PickVector(random, palette), PickVector(random, palette), PickVector(random, palette));

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    static bool Finite(Float3 value) => Finite(value.X) && Finite(value.Y) && Finite(value.Z);

    static bool Finite(Quaternion value) => Finite(value.X) && Finite(value.Y) && Finite(value.Z) && Finite(value.W);

    static bool Finite(in Pose value) => Finite(value.Position) && Finite(value.Rotation) && Finite(value.Scale);

    static bool Finite(in PoseSum value) =>
        Finite(value.Position) && Finite(value.Rotation) && Finite(value.Scale) && Finite(value.Weight);

    static bool Finite(in Affine value) => Finite(value.C0) && Finite(value.C1) && Finite(value.C2) && Finite(value.C3);

    static bool Finite(Pose[] values)
    {
        foreach (var value in values)
            if (!Finite(value))
                return false;
        return true;
    }

    static bool Finite(PoseSum[] values)
    {
        foreach (var value in values)
            if (!Finite(value))
                return false;
        return true;
    }

    static bool Finite(Affine[] values)
    {
        foreach (var value in values)
            if (!Finite(value))
                return false;
        return true;
    }

    static string? PoseMathFailure(float[] palette)
    {
        var random = new Random(31);
        for (var i = 0; i < 20000; i++)
        {
            var a = PickPose(random, palette);
            var b = PickPose(random, palette);
            var delta = PickPose(random, palette);
            var q = PickRotation(random, palette);
            var r = PickRotation(random, palette);
            var x = Pick(random, palette);
            var sum = PickSum(random, palette);
            var m = PickAffine(random, palette);
            var n = PickAffine(random, palette);
            if (!Finite(PoseMath.Align(q, r)))
                return "Align at " + i;
            if (!Finite(PoseMath.Nlerp(q, r, x)))
                return "Nlerp at " + i;
            if (!Finite(PoseMath.Lerp(a, b, x)))
                return "Lerp at " + i;
            if (!Finite(PoseMath.Delta(a, b)))
                return "Delta at " + i;
            if (!Finite(PoseMath.Accumulate(a, delta, x)))
                return "Accumulate at " + i;
            if (!Finite(PoseMath.Add(sum, a, x)))
                return "Add at " + i;
            if (!Finite(PoseMath.Mean(sum)))
                return "Mean at " + i;
            if (!Finite(PoseMath.Affine(a)))
                return "Affine at " + i;
            if (!Finite(PoseMath.Mul(m, n)))
                return "Mul at " + i;
        }
        return null;
    }

    static string? SamplingFailure(float[] palette)
    {
        var random = new Random(33);
        for (var i = 0; i < 20000; i++)
        {
            var samples = new Pose[random.Next(0, 13)];
            for (var s = 0; s < samples.Length; s++)
                samples[s] = PickPose(random, palette);
            var pose = ClipSampling.At(samples, random.Next(-2, 6), random.Next(-2, 6), Pick(random, palette));
            if (!Finite(pose))
                return "At at " + i;
        }
        return null;
    }

    static string? BlendFailure(float[] palette)
    {
        var random = new Random(37);
        for (var i = 0; i < 20000; i++)
        {
            var pose = new Pose[random.Next(0, 5)];
            Array.Fill(pose, Pose.Identity);
            var sums = new PoseSum[random.Next(0, 5)];
            var samples = new Pose[random.Next(0, 16)];
            for (var s = 0; s < samples.Length; s++)
                samples[s] = PickPose(random, palette);
            var reference = new Pose[random.Next(0, 5)];
            for (var s = 0; s < reference.Length; s++)
                reference[s] = PickPose(random, palette);
            var mask = new float[random.Next(0, 5)];
            for (var s = 0; s < mask.Length; s++)
                mask[s] = Pick(random, palette);
            var frameCount = FrameCounts[random.Next(FrameCounts.Length)];
            var frame = Pick(random, palette);
            var weight = Pick(random, palette);
            PoseBlend.Override(sums, samples, frameCount, frame, mask, weight);
            if (!Finite(sums))
                return "Override at " + i;
            PoseBlend.Override(sums, samples, frameCount, frame, mask, weight);
            PoseBlend.Resolve(pose, sums);
            if (!Finite(pose) || !Finite(sums))
                return "Resolve at " + i;
            PoseBlend.Additive(pose, samples, reference, frameCount, frame, mask, weight);
            if (!Finite(pose))
                return "Additive at " + i;
        }
        return null;
    }

    static string? HostileSumsFailure(float[] palette)
    {
        var random = new Random(39);
        for (var i = 0; i < 20000; i++)
        {
            var count = random.Next(1, 5);
            var sums = new PoseSum[count];
            for (var s = 0; s < count; s++)
                sums[s] = PickSum(random, palette);
            var samples = new Pose[count * 3];
            for (var s = 0; s < samples.Length; s++)
                samples[s] = PickPose(random, palette);
            var ones = new float[count];
            Array.Fill(ones, 1f);
            PoseBlend.Override(sums, samples, 3, Pick(random, palette), ones, 1f);
            if (!Finite(sums))
                return "Override over hostile sums at " + i;
            for (var s = 0; s < count; s++)
                sums[s] = PickSum(random, palette);
            var pose = new Pose[count];
            Array.Fill(pose, Pose.Identity);
            PoseBlend.Resolve(pose, sums);
            if (!Finite(pose))
                return "Resolve over hostile sums at " + i;
            foreach (var sum in sums)
                if (!sum.Equals(default(PoseSum)))
                    return "Resolve left a sum at " + i;
        }
        return null;
    }

    static string? HierarchyFailure(float[] palette)
    {
        var random = new Random(43);
        for (var i = 0; i < 20000; i++)
        {
            var count = random.Next(0, 7);
            var parents = new int[count];
            var local = new Pose[count];
            for (var b = 0; b < count; b++)
            {
                parents[b] = random.Next(-3, 9);
                local[b] = PickPose(random, palette);
            }
            var model = new Affine[random.Next(0, 8)];
            Hierarchy.Model(parents, local, model);
            if (!Finite(model))
                return "Model at " + i;
        }
        return null;
    }

    [Fact]
    public void PoseMathNeverThrowsAndAlwaysReturnsFiniteValues()
    {
        Assert.Null(PoseMathFailure(Hostile));
        Assert.Null(PoseMathFailure(Extreme));
    }

    [Fact]
    public void SamplingNeverThrowsAndAlwaysReturnsAFinitePose()
    {
        Assert.Null(SamplingFailure(Hostile));
        Assert.Null(SamplingFailure(Extreme));
    }

    [Fact]
    public void BlendingNeverThrowsAndNeverLeavesNonFiniteValues()
    {
        Assert.Null(BlendFailure(Hostile));
        Assert.Null(BlendFailure(Extreme));
    }

    [Fact]
    public void BlendingRepairsHostileSumsItTouches()
    {
        Assert.Null(HostileSumsFailure(Hostile));
        Assert.Null(HostileSumsFailure(Extreme));
    }

    [Fact]
    public void HierarchyNeverThrowsAndAlwaysReturnsFiniteMatrices()
    {
        Assert.Null(HierarchyFailure(Hostile));
        Assert.Null(HierarchyFailure(Extreme));
    }

    [Fact]
    public void NonFiniteWeightsFramesAndPosesLeaveTheRestPoseAlone()
    {
        var scene = PoseFixtures.Rig();
        var nan = float.NaN;
        var poisoned = new PoseClip(
            Enumerable.Repeat(new Pose { Position = new Float3(nan, nan, nan), Rotation = new Quaternion(nan, nan, nan, nan), Scale = new Float3(nan, nan, nan) }, 6).ToArray(),
            Enumerable.Repeat(new Pose { Position = new Float3(nan, nan, nan), Rotation = new Quaternion(nan, nan, nan, nan), Scale = new Float3(nan, nan, nan) }, 2).ToArray(),
            3);
        var infected = scene with { Clips = new[] { poisoned, poisoned } };
        foreach (var weight in new[] { nan, float.PositiveInfinity, float.NegativeInfinity, 0f })
        {
            var states = new[]
            {
                PoseFixtures.State(0, 0, ClipMode.Override, 0, Wrap.Loop, 0.5f, weight),
                PoseFixtures.State(1, 1, ClipMode.Additive, 0, Wrap.Loop, 0.5f, weight)
            };
            var pose = infected.Blend(states);
            for (var bone = 0; bone < 2; bone++)
                Assert.Equal(0f, PoseFixtures.ComponentGap(pose[bone], scene.Rest[bone]));
        }
        var usable = new[]
        {
            PoseFixtures.State(0, 0, ClipMode.Override, 0, Wrap.Loop, nan, 1f),
            PoseFixtures.State(1, 1, ClipMode.Additive, 0, Wrap.PingPong, float.PositiveInfinity, 0.5f)
        };
        foreach (var pose in infected.Blend(usable))
            Assert.True(Finite(pose));
        foreach (var pose in scene.Blend(usable))
            Assert.True(Finite(pose));
    }

    [Fact]
    public void NextLayerAcceptsEveryStartingLayer()
    {
        var random = new Random(47);
        for (var i = 0; i < 2000; i++)
        {
            var states = new ClipState[random.Next(0, 8)];
            for (var s = 0; s < states.Length; s++)
                states[s].Layer = (byte)random.Next(256);
            var after = random.Next(4) switch { 0 => int.MinValue, 1 => int.MaxValue, 2 => random.Next(-5, 300), _ => random.Next() };
            var next = PoseBlend.NextLayer(states, after);
            Assert.InRange(next, -1, 255);
            if (next >= 0)
            {
                Assert.True(next > after);
                Assert.Contains(states, state => state.Layer == next);
                Assert.DoesNotContain(states, state => state.Layer > after && state.Layer < next);
            }
            else
            {
                Assert.DoesNotContain(states, state => state.Layer > after);
            }
        }
    }
}
