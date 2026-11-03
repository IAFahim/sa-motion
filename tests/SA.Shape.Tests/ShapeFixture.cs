using SA.Animation;
using SA.Motion.Inertialization;
using SA.Motion.Numerics;
using Xunit;

namespace SA.Shape.Tests;

internal sealed class ShapeFixture
{
    internal static readonly AssetId FamilyId = new(1, 2, 3, 4);
    internal static readonly AssetId TargetId = new(5, 6, 7, 8);
    internal static readonly AssetId DefinitionId = new(9, 10, 11, 12);
    internal readonly int[] Parents = [-1, 0, 1, 2, 1, 4, 5, 1, 7, 8, 2, 10, 11, 2, 13, 14, 3];
    internal readonly Pose[] Base = [Pose.Identity, At(0, 1, 0), At(0, .4f, 0), At(0, .25f, 0), At(-.15f, 0, 0), At(0, -.45f, 0), At(0, -.45f, 0),
        At(.15f, 0, 0), At(0, -.45f, 0), At(0, -.45f, 0), At(-.2f, .3f, 0), At(-.35f, 0, 0), At(-.3f, 0, 0), At(.2f, .3f, 0), At(.35f, 0, 0), At(.3f, 0, 0), At(.1f, .1f, 0)];
    internal readonly RetargetRow[] Rows = new RetargetRow[17];
    internal RetargetBinding Binding;
    internal readonly ShapeChannel[] Channels = [new(1, -1, 1), new(2, -1, 1), new(3, -1, 1), new(4, -1, 1)];
    internal readonly OffsetEffect[] OffsetEffects = [new(0, 5, Float3.Zero, MathF.Log(1.25f)), new(0, 6, Float3.Zero, MathF.Log(1.25f)),
        new(0, 8, Float3.Zero, MathF.Log(1.25f)), new(0, 9, Float3.Zero, MathF.Log(1.25f)), new(2, 11, Float3.Zero, MathF.Log(1.5f)),
        new(2, 12, Float3.Zero, MathF.Log(1.5f)), new(2, 14, Float3.Zero, MathF.Log(1.5f)), new(2, 15, Float3.Zero, MathF.Log(1.5f)), new(3, 3, new Float3(0, .1f, 0))];
    internal readonly SkinEffect[] SkinEffects = [new(1, 3, new Float3(MathF.Log(1.5f), 0, MathF.Log(1.3f))), new(1, 6, new Float3(MathF.Log(1.5f), MathF.Log(1.2f), 0)),
        new(1, 9, new Float3(MathF.Log(1.5f), MathF.Log(1.2f), 0)), new(0, 4, new Float3(0, MathF.Log(1.25f), 0)), new(0, 5, new Float3(0, MathF.Log(1.25f), 0)),
        new(0, 7, new Float3(0, MathF.Log(1.25f), 0)), new(0, 8, new Float3(0, MathF.Log(1.25f), 0)), new(2, 10, new Float3(MathF.Log(1.5f), 0, 0)),
        new(2, 11, new Float3(MathF.Log(1.5f), 0, 0)), new(2, 13, new Float3(MathF.Log(1.5f), 0, 0)), new(2, 14, new Float3(MathF.Log(1.5f), 0, 0))];
    internal readonly ShapeMarker LeftSole = new(6, new Float3(0, -.1f, .05f));
    internal readonly ShapeMarker RightSole = new(9, new Float3(0, -.1f, .05f));
    internal readonly float[] Values = new float[4];
    internal readonly Float3[] Offsets = new Float3[17];
    internal readonly float[] Lengths = new float[17];
    internal readonly float[] Scales = new float[17];
    internal readonly Float3[] SkinScales = new Float3[17];
    internal readonly Affine[] Deformations = new Affine[17];
    internal readonly RigidPose[] Models = new RigidPose[17];
    internal readonly RigidPose[] CanonicalModels = new RigidPose[17];
    internal readonly RigidPose[] TargetModels = new RigidPose[17];
    internal readonly Pose[] Target = new Pose[17];
    internal ShapeGeometry Geometry;
    internal ShapeGrounding Grounding = ShapeGrounding.KeepBase;
    internal ShapeDefinition Definition => new(new ShapeProfile(DefinitionId, TargetId, LeftSole, RightSole, Grounding), Channels, OffsetEffects, SkinEffects, []);
    internal ShapeWorkspace Workspace => new(Offsets, Lengths, Scales, SkinScales, Deformations, Models);

    internal ShapeFixture()
    {
        var map = new int[Parents.Length];
        for (var i = 0; i < map.Length; i++) map[i] = i == 16 ? -1 : i;
        Assert.Equal(AnimationError.None, RetargetTable.Compile(FamilyId, TargetId, Parents, Base, Parents, Base, map, 1, 1, 1f,
            new RigidPose[Parents.Length * 2], Rows, out Binding));
        Assert.Equal(AnimationError.None, Evaluate());
    }

    internal AnimationError Evaluate(ulong revision = 1) => ShapeRig.Evaluate(Definition, Binding, Rows, Values, revision, Workspace, out Geometry);
    internal AnimationError Retarget(Pose[]? canonical = null) => RetargetTable.ApplyGeometry(FamilyId, Binding, Geometry.Retarget, Parents, canonical ?? Base,
        Rows, Offsets, CanonicalModels, TargetModels, Target);
    internal static Pose At(float x, float y, float z) => new() { Position = new Float3(x, y, z), Rotation = Quaternion.Identity, Scale = Float3.One };
    internal static void Near(Float3 actual, Float3 expected, float tolerance = 1e-5f) => Assert.InRange((actual - expected).Length, 0, tolerance);
    internal static void Near(float actual, float expected, float tolerance = 1e-5f) => Assert.InRange(MathF.Abs(actual - expected), 0, tolerance);
    internal static void Near(Quaternion actual, Quaternion expected, float tolerance = 1e-5f)
        => Assert.InRange(RotationVector.Log(Quaternion.Multiply(actual, Quaternion.InverseUnit(expected))).Length, 0, tolerance);
}

internal sealed class ShapeFrames
{
    internal ShapeFrameState State;
    internal readonly BoneFrameState[] Bones;
    internal readonly BoneFrameState[] BoneScratch;
    internal readonly ClipContribution[] Previous = new ClipContribution[1];
    internal readonly LayerInput[] PreviousLayers = new LayerInput[1];
    internal readonly Pose[] Destination;
    internal readonly Pose[] Neutral;
    internal readonly Pose[] Scratch;
    internal readonly Pose[] Displayed;
    internal readonly Pose[] DisplayScratch;
    internal readonly RigidPose[] Models;
    internal readonly PoseVelocity[] Incoming;
    internal readonly ClipContribution[] Contributions = [new(new ClipHandle(0, 1), 0, 1)];
    internal readonly LayerInput[] Layers = [new(0, ClipMode.Override, 0, 1)];
    internal FrameAction Action;
    internal ShapePoseWorkspace Workspace => new(Destination, Neutral, Scratch, Displayed, DisplayScratch, Models);
    internal ShapeFrames(int count = 17)
    {
        Bones = new BoneFrameState[count]; BoneScratch = new BoneFrameState[count]; Destination = new Pose[count]; Neutral = new Pose[count];
        Scratch = new Pose[count]; Displayed = new Pose[count]; DisplayScratch = new Pose[count]; Models = new RigidPose[count]; Incoming = new PoseVelocity[count];
    }
    internal AnimationError Step(ShapeFixture shape, int tick, ulong continuity = 1, uint clip = 0, bool ready = true,
        FrameMode mode = FrameMode.Advance, uint epoch = 1, float dt = .01f, double? sampleSeconds = null)
    {
        Contributions[0] = new ClipContribution(new ClipHandle(clip, 1), sampleSeconds ?? tick * .01, 1f);
        var frame = new AnimationFrame((ulong)tick, epoch, continuity, tick * .01, dt, .2f, mode);
        return ShapeTimelinePose.Evaluate(frame, Contributions, Layers, ready, ready ? shape.Target : [], Incoming, shape.Geometry, shape.Binding,
            shape.Rows, shape.Offsets, shape.Scales, ref State, Bones, BoneScratch, Previous, PreviousLayers, Workspace, out Action);
    }
}
