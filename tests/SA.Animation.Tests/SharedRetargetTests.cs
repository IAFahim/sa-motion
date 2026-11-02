using SA.Motion.Numerics;
using Xunit;

namespace SA.Animation.Tests;

public class SharedRetargetTests
{
    static readonly AssetId Family = new(1, 2, 3, 4);
    static readonly AssetId Target = new(5, 6, 7, 8);
    static Pose At(Float3 p, Quaternion q) => new() { Position = p, Rotation = q, Scale = Float3.One };
    static void Near(Float3 a, Float3 b, float tolerance = 1e-5f) => Assert.InRange((a - b).Length, 0, tolerance);
    static void Near(Quaternion a, Quaternion b) => Assert.InRange(RotationVector.Log(Quaternion.Multiply(a, Quaternion.InverseUnit(b))).Length, 0, 1e-5f);

    [Fact]
    public void CalibratedAPoseRigReturnsItsTPoseAndKeepsItsOwnLengths()
    {
        var parents = new[] { -1, 0, 1, 2 };
        var canonical = new[] { Pose.Identity, At(new Float3(0, 1, 0), Quaternion.Identity), At(new Float3(1, .5f, 0), Quaternion.Identity), At(new Float3(1, 0, 0), Quaternion.Identity) };
        var target = new[] { Pose.Identity, At(new Float3(0, 2, 0), RotationVector.Exp(new Float3(.2f, .1f, -.2f))), At(new Float3(-.5f, .7f, .3f), RotationVector.Exp(new Float3(-.3f, .4f, .2f))), At(new Float3(.8f, .1f, 0), RotationVector.Exp(new Float3(.1f, .1f, .3f))) };
        var map = new[] { 0, 1, 2, 3 };
        var rows = new RetargetRow[4];
        var scratch = new RigidPose[8];
        Assert.Equal(AnimationError.None, RetargetTable.Compile(Family, Target, parents, canonical, parents, target, map, 1, 1, 2f, scratch, rows, out var binding));
        var output = new Pose[4];
        Assert.Equal(AnimationError.None, RetargetTable.Apply(Family, binding, parents, canonical, rows, scratch.AsSpan(0, 4), scratch.AsSpan(4), output));
        for (var i = 0; i < 4; i++) { Near(target[i].Position, output[i].Position); Near(target[i].Rotation, output[i].Rotation); }
        var input = (Pose[])canonical.Clone();
        input[1].Position += new Float3(.2f, -.1f, .3f);
        input[2].Rotation = RotationVector.Exp(new Float3(.4f, -.7f, .3f));
        Assert.Equal(AnimationError.None, RetargetTable.Apply(Family, binding, parents, input, rows, scratch.AsSpan(0, 4), scratch.AsSpan(4), output));
        for (var i = 2; i < 4; i++) Assert.Equal(target[i].Position, output[i].Position);
        Near(target[1].Position + new Float3(.4f, -.2f, .6f), scratch[5].Position);
    }

    [Fact]
    public void GeneralAndCompiledRowsAgreeOnNonIdentityFramesAndMissingNodes()
    {
        var random = new Random(20261103);
        var cp = new[] { -1, 0, 1, 2, 3, 3, 5, 6, 3, 8, 9 };
        var tp = new[] { -1, 0, 1, 2, 3, 3, 5, 6, 3, 8, 9, 10 };
        var map = new[] { 0, 1, -1, 2, 4, 5, -1, 7, 8, -1, 9, 10 };
        for (var trial = 0; trial < 100; trial++)
        {
            Pose[] Poses(int count) => Enumerable.Range(0, count).Select(i => i == 0 ? Pose.Identity : At(PoseFixtures.RandomVector(random, -.5f, .5f), PoseFixtures.RandomRotation(random))).ToArray();
            var c0 = Poses(cp.Length);
            var t0 = Poses(tp.Length);
            var animated = Poses(cp.Length);
            var rows = new RetargetRow[tp.Length];
            var compileScratch = new RigidPose[cp.Length + tp.Length];
            Assert.Equal(AnimationError.None, RetargetTable.Compile(Family, Target, cp, c0, tp, t0, map, 1, 1, 1.7f, compileScratch, rows, out var binding));
            var cModel = new RigidPose[cp.Length];
            var tModel = new RigidPose[tp.Length];
            var result = new Pose[tp.Length];
            var reference = new Pose[tp.Length];
            Assert.Equal(AnimationError.None, RetargetTable.Apply(Family, binding, cp, animated, rows, cModel, tModel, result));
            Assert.Equal(AnimationError.None, RetargetTable.ApplyReference(Family, binding, cp, animated, rows, cModel, tModel, reference));
            for (var i = 0; i < tp.Length; i++)
            {
                Near(result[i].Rotation, reference[i].Rotation);
                Near(result[i].Position, reference[i].Position);
                if (i != 1) Assert.Equal(t0[i].Position, result[i].Position);
                if (map[i] >= 0) Near(Quaternion.Multiply(cModel[map[i]].Rotation, rows[i].Correction), tModel[i].Rotation);
            }
        }
    }

    [Fact]
    public void CyclesDuplicateMappingWrongFamilyAndAliasingAreRejected()
    {
        var parents = new[] { -1, 0, 1 };
        var poses = new[] { Pose.Identity, At(new Float3(0, 1, 0), Quaternion.Identity), At(new Float3(0, 1, 0), Quaternion.Identity) };
        var rows = new RetargetRow[3];
        var scratch = new RigidPose[6];
        Assert.Equal(AnimationError.InvalidTable, RetargetTable.Compile(Family, Target, parents, poses, parents, poses, new[] { 0, 1, 1 }, 1, 1, 1, scratch, rows, out _));
        Assert.Equal(AnimationError.InvalidTable, RetargetTable.Compile(Family, Target, new[] { -1, 2, 1 }, poses, parents, poses, new[] { 0, 1, 2 }, 1, 1, 1, scratch, rows, out _));
        Assert.Equal(AnimationError.None, RetargetTable.Compile(Family, Target, parents, poses, parents, poses, new[] { 0, 1, 2 }, 1, 1, 1, scratch, rows, out var binding));
        var output = new Pose[3];
        Assert.Equal(AnimationError.FamilyMismatch, RetargetTable.Apply(Target, binding, parents, poses, rows, scratch.AsSpan(0, 3), scratch.AsSpan(3), output));
        Assert.Equal(AnimationError.AliasedBuffers, RetargetTable.Apply(Family, binding, parents, poses, rows, scratch.AsSpan(0, 3), scratch.AsSpan(0, 3), output));
        Assert.Equal(AnimationError.AliasedBuffers, RetargetTable.Apply(Family, binding, parents, poses, rows, scratch.AsSpan(0, 3), scratch.AsSpan(3), poses));
        Assert.All(output, x => Assert.Equal(default, x));
    }

    [Fact]
    public void CanonicalDraftIsTopologicalSymmetricAndOneMetreAtHips()
    {
        var parents = new int[56];
        var local = new Pose[56];
        var model = new RigidPose[56];
        Assert.Equal(AnimationError.None, CanonicalHumanoid.WriteReference(parents, local));
        Assert.Equal(AnimationError.None, RigidMath.Model(parents, local, model));
        Assert.Equal(-1, parents[0]);
        Assert.Equal(new Float3(0, 1, 0), model[1].Position);
        for (var i = 1; i < 56; i++) Assert.InRange(parents[i], 0, i - 1);
        Near(new Float3(-.095f, .035f, .16f), model[51].Position);
        Near(new Float3(.095f, .035f, .16f), model[55].Position);
        for (var i = 0; i < 15; i++)
        {
            var l = model[14 + i].Position;
            var r = model[33 + i].Position;
            Near(new Float3(-l.X, l.Y, l.Z), r);
        }
    }

    [Fact]
    public void SegmentWindowsIncludeBothEndpointsAndNegativeLoops()
    {
        foreach (var count in new[] { 1, 2, 16, 17, 18, 32, 33, 34, 1001 })
        {
            var duration = count == 1 ? 0 : 1d;
            for (var sample = 0; sample < count; sample++)
            {
                Assert.Equal(AnimationError.None, SampleLocator.Locate(duration, count, count == 1 ? 0 : sample / (double)(count - 1), false, out var window));
                Assert.InRange(window.First, window.Segment * 16, Math.Min(window.Segment * 16 + 16, count - 1));
                Assert.InRange(window.Second, window.Segment * 16, Math.Min(window.Segment * 16 + 16, count - 1));
            }
        }
        Assert.Equal(AnimationError.None, SampleLocator.Locate(1, 61, -.25, true, out var negative));
        Assert.Equal(-1, negative.Cycle);
        Assert.Equal(45, negative.First);
        Assert.Equal(AnimationError.TimeOutOfRange, SampleLocator.Locate(1, 61, 1e20, true, out _));
        Assert.Equal(AnimationError.InvalidFrame, SampleLocator.Locate(double.NaN, 61, 0, true, out _));
    }

    [Fact]
    public void TurningRootComposesCyclesAndCancelsLargeCommonPrefixes()
    {
        var root = new[] { RigidPose.Identity, new RigidPose(new Float3(1, 0, 1), RotationVector.Exp(new Float3(0, Scalar.Pi / 2, 0))) };
        Assert.Equal(AnimationError.None, RootMotion.Delta(root, 1, 0, 2, true, 1, out var two));
        Near(new Float3(2, 0, 0), two.Position);
        Assert.Equal(AnimationError.None, RootMotion.Delta(root, 1, 0, 4, true, 1, out var four));
        Near(Float3.Zero, four.Position);
        Near(Quaternion.Identity, four.Rotation);
        Assert.Equal(AnimationError.None, RootMotion.Delta(root, 1, -.25, 2.3, true, 2, out var forward));
        Assert.Equal(AnimationError.None, RootMotion.Delta(root, 1, 2.3, -.25, true, 2, out var backward));
        Assert.True(RigidMath.TryCompose(forward, backward, out var cancelled));
        Near(Float3.Zero, cancelled.Position);
        Near(Quaternion.Identity, cancelled.Rotation);
        Assert.Equal(AnimationError.None, RootMotion.Delta(root, 1, 1000000.25, 1000000.5, true, 1, out var large));
        Assert.Equal(AnimationError.None, RootMotion.Delta(root, 1, .25, .5, true, 1, out var small));
        Near(small.Position, large.Position);
        Near(small.Rotation, large.Rotation);
        Assert.True(RigidMath.TryCompose(root[1], root[1], out var alias));
        Assert.True(RigidMath.TryCompose(alias, RigidPose.Identity, out alias));
        Near(two.Position, alias.Position);
    }
}
