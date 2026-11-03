using SA.Animation;
using SA.Animation.Content;
using SA.Motion.Mechanics;
using SA.Motion.Numerics;
using Xunit;
using static SA.Shape.Tests.ShapeFixture;

namespace SA.Shape.Tests;

public class ShapePipelineTests
{
    static SharedClip Encode(ShapeFixture f, out byte[] bytes)
    {
        const int frames = 18;
        var samples = new Pose[frames * f.Rows.Length];
        var root = new RigidPose[frames];
        for (var frame = 0; frame < frames; frame++)
        {
            var time = frame / (float)(frames - 1);
            for (var bone = 0; bone < f.Rows.Length; bone++)
            {
                var pose = f.Base[bone];
                if (bone == 1) pose.Position += new Float3(.1f * time, .05f * MathF.Sin(time * 2), .03f * time);
                if (bone == 5 || bone == 8 || bone == 11) pose.Rotation = RotationVector.Exp(new Float3(.3f * MathF.Sin(time * 3), .2f * time, -.1f * time));
                samples[frame * f.Rows.Length + bone] = pose;
            }
            root[frame] = new RigidPose(new Float3(time, 0, time * .2f), Quaternion.AxisAngle(new Float3(0, 1, 0), time * .5f));
        }
        Assert.Equal(AnimationError.None, SharedClipEncoder.Encode(FamilyId, f.Parents, f.Base, samples, frames, 1, true, RootPolicy.Planar, root,
            [], [], [new PoseMarker(6, f.LeftSole.Offset)], true, CompressionLimits.Default, out bytes, out _));
        Assert.Equal(AnimationError.None, SharedClipReader.Load(bytes, FamilyId, f.Base, out var clip));
        return clip!;
    }

    [Fact]
    public void OneCompressedClipDrivesDifferentShapesWithoutChangingTheClipOrTable()
    {
        var a = new ShapeFixture();
        var b = new ShapeFixture();
        var clip = Encode(a, out var bytes);
        var originalBytes = (byte[])bytes.Clone();
        var originalRows = (RetargetRow[])a.Rows.Clone();
        var decoded = new Pose[17];
        var neutralBefore = new Pose[17];
        var neutralAfter = new Pose[17];
        var framesA = new ShapeFrames();
        var framesB = new ShapeFrames();
        var model = new Affine[17];
        var palette = new Affine[17];
        var normals = new Float3x3[17];
        var map = Enumerable.Range(0, 17).ToArray();
        var binds = new Affine[17];
        for (var i = 0; i < 17; i++)
        {
            Assert.True(RigidMath.TryInverse(a.Models[i], out var inverse));
            binds[i] = PoseMath.Affine(inverse.ToPose());
        }
        for (var tick = 0; tick < 40; tick++)
        {
            b.Values[0] = .8f * MathF.Sin(tick * .1f);
            b.Values[1] = .4f; b.Values[2] = -.5f;
            Assert.Equal(AnimationError.None, b.Evaluate((ulong)tick + 2));
            var sampleTime = tick * .01 + (tick < 8 ? 0 : .4);
            var characters = new[] { a, b };
            var frames = new[] { framesA, framesB };
            for (var character = 0; character < 2; character++)
            {
                var shape = characters[character];
                var state = frames[character];
                if (tick == 8)
                {
                    Assert.Equal(AnimationError.None, clip.Sample(sampleTime - .001, a.Base, decoded));
                    Assert.Equal(AnimationError.None, shape.Retarget(decoded));
                    Assert.Equal(AnimationError.None, ShapeMotionSpace.Normalize(shape.Geometry, shape.Binding, a.Rows, shape.Offsets, shape.Scales, shape.Target, shape.Models, neutralBefore));
                    Assert.Equal(AnimationError.None, clip.Sample(sampleTime + .001, a.Base, decoded));
                    Assert.Equal(AnimationError.None, shape.Retarget(decoded));
                    Assert.Equal(AnimationError.None, ShapeMotionSpace.Normalize(shape.Geometry, shape.Binding, a.Rows, shape.Offsets, shape.Scales, shape.Target, shape.Models, neutralAfter));
                    Assert.Equal(AnimationError.None, ShapeMotionSpace.Differentiate(neutralBefore, neutralAfter, .002f, state.Incoming));
                }
                Assert.Equal(AnimationError.None, clip.Sample(sampleTime, a.Base, decoded));
                Assert.Equal(AnimationError.None, RetargetTable.ApplyGeometry(clip.Family, a.Binding, shape.Geometry.Retarget, a.Parents, decoded,
                    a.Rows, shape.Offsets, shape.CanonicalModels, shape.TargetModels, shape.Target));
                Assert.Equal(AnimationError.None, state.Step(shape, tick, tick < 8 ? 1UL : 2UL, sampleSeconds: sampleTime));
                Hierarchy.Model(a.Parents, state.Displayed, model);
                Assert.Equal(AnimationError.None, ShapeSkinPalette.Build(model, shape.Deformations, map, binds, Affine.Identity, palette, normals));
                foreach (var matrix in palette) Assert.True(matrix.C3.IsFinite);
            }
            for (var i = 0; i < 17; i++)
            {
                Near(framesA.Neutral[i].Position, framesB.Neutral[i].Position, 1e-5f);
                Near(framesA.Neutral[i].Rotation, framesB.Neutral[i].Rotation, 1e-5f);
            }
        }
        Assert.NotEqual(framesA.Displayed[11].Position, framesB.Displayed[11].Position);
        Assert.Equal(originalRows, a.Rows);
        Assert.Equal(originalBytes, bytes);
    }

    [Fact]
    public void WarmShapeSamplingTransitionsContactsAndSkinningAllocateZeroBytes()
    {
        var shape = new ShapeFixture();
        var clip = Encode(shape, out _);
        var frames = new ShapeFrames();
        var parameters = new ShapeParameter[4];
        var nextParameters = new ShapeParameter[4];
        var request = new float[4];
        Assert.Equal(AnimationError.None, ShapeParameters.Reset(shape.Channels, parameters));
        var decoded = new Pose[17];
        var blended = new Pose[17];
        var model = new Affine[17];
        var palette = new Affine[17];
        var normals = new Float3x3[17];
        var afterIk = new Pose[17];
        var map = Enumerable.Range(0, 17).ToArray();
        var binds = Enumerable.Repeat(Affine.Identity, 17).ToArray();
        var channels = shape.Channels;
        var limb = new ShapeLimb(4, 5, 6);
        AnimationError Step(int tick)
        {
            request[0] = .5f * MathF.Sin(tick * .01f);
            request[1] = -.6f * MathF.Sin(tick * .007f);
            var error = ShapeParameters.Step(channels, request, parameters, .01f, nextParameters);
            if (error != AnimationError.None) return error;
            for (var i = 0; i < 4; i++) shape.Values[i] = nextParameters[i].Value;
            error = shape.Evaluate((ulong)tick + 1);
            if (error != AnimationError.None) return error;
            error = clip.Sample(tick * .01, shape.Base, decoded);
            if (error != AnimationError.None) return error;
            frames.Contributions[0] = new ClipContribution(new ClipHandle(0, 1), tick * .01, 1f);
            error = CanonicalBlend.Evaluate(frames.Contributions, frames.Layers, decoded, [], [], shape.Base, blended);
            if (error != AnimationError.None) return error;
            error = shape.Retarget(blended);
            if (error != AnimationError.None) return error;
            error = frames.Step(shape, tick, (ulong)(tick / 50 + 1));
            if (error != AnimationError.None) return error;
            var anchor = new Float3(-.15f, .1f, .05f);
            if (!ShapeContacts.TryAnkleTarget(shape.LeftSole, shape.Deformations, anchor, Quaternion.Identity, out var ankle)) return AnimationError.InvalidFrame;
            error = ShapeContacts.Reach(shape.Rows, frames.Displayed, limb, new ShapeContactTarget(ankle, new Float3(-.15f, .6f, 1f), Quaternion.Identity), shape.Models, afterIk, out _);
            if (error != AnimationError.None) return error;
            Hierarchy.Model(shape.Parents, afterIk, model);
            error = ShapeSkinPalette.Build(model, shape.Deformations, map, binds, Affine.Identity, palette, normals);
            if (error == AnimationError.None) nextParameters.CopyTo(parameters, 0);
            return error;
        }
        for (var tick = 0; tick < 200; tick++) Assert.Equal(AnimationError.None, Step(tick));
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = AnimationError.None;
        for (var tick = 200; tick < 1200; tick++)
        {
            result = Step(tick);
            if (result != AnimationError.None) break;
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(AnimationError.None, result);
        Assert.Equal(0, bytes);
        Assert.True(palette[6].C3.IsFinite);
    }
}
