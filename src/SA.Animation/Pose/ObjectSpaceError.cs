using System;
using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.Animation
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct PoseMarker
    {
        public readonly int Bone;
        public readonly Float3 Offset;
        public PoseMarker(int bone, Float3 offset) { Bone = bone; Offset = offset; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct PoseError
    {
        public readonly float AngleRadians;
        public readonly float PositionMetres;
        public PoseError(float angleRadians, float positionMetres) { AngleRadians = angleRadians; PositionMetres = positionMetres; }
        public PoseError Include(in PoseError other) => new PoseError(Math.Max(AngleRadians, other.AngleRadians), Math.Max(PositionMetres, other.PositionMetres));
    }

    public static class ObjectSpaceError
    {
        public static AnimationError Measure(ReadOnlySpan<int> parents, ReadOnlySpan<Pose> reference, ReadOnlySpan<Pose> decoded, ReadOnlySpan<PoseMarker> markers,
            Span<RigidPose> modelScratch, out PoseError error)
        {
            error = default;
            var count = parents.Length;
            if (count <= 0 || count > SampleLocator.MaxBones || reference.Length != count || decoded.Length != count || modelScratch.Length < 2 * count)
                return AnimationError.InvalidFrame;
            for (var i = 0; i < markers.Length; i++)
                if (markers[i].Bone < 0 || markers[i].Bone >= count || !markers[i].Offset.IsFinite)
                    return AnimationError.InvalidFrame;
            var a = modelScratch.Slice(0, count);
            var b = modelScratch.Slice(count, count);
            if (RigidMath.Model(parents, reference, a) != AnimationError.None || RigidMath.Model(parents, decoded, b) != AnimationError.None)
                return AnimationError.InvalidFrame;
            var angle = 0f;
            var position = 0f;
            for (var i = 0; i < count; i++)
            {
                angle = Math.Max(angle, RotationVector.Log(Quaternion.Multiply(reference[i].Rotation, Quaternion.InverseUnit(decoded[i].Rotation))).Length);
                position = Math.Max(position, (a[i].Position - b[i].Position).Length);
                position = Math.Max(position, Probe(a[i], b[i], new Float3(.05f, 0f, 0f)));
                position = Math.Max(position, Probe(a[i], b[i], new Float3(0f, .05f, 0f)));
                position = Math.Max(position, Probe(a[i], b[i], new Float3(0f, 0f, .05f)));
            }
            for (var i = 0; i < markers.Length; i++)
                position = Math.Max(position, Probe(a[markers[i].Bone], b[markers[i].Bone], markers[i].Offset));
            if (!Scalar.IsFinite(angle) || !Scalar.IsFinite(position))
                return AnimationError.InvalidFrame;
            error = new PoseError(angle, position);
            return AnimationError.None;
        }

        static float Probe(in RigidPose a, in RigidPose b, Float3 offset) =>
            (a.Position - b.Position + (Quaternion.Rotate(a.Rotation, offset) - Quaternion.Rotate(b.Rotation, offset))).Length;
    }
}
