using System;
using System.Runtime.InteropServices;
using SA.Animation;
using SA.Motion.Kinematics;
using SA.Motion.Numerics;

namespace SA.Shape
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct ShapeLimb
    {
        public readonly int Upper;
        public readonly int Middle;
        public readonly int End;
        public ShapeLimb(int upper, int middle, int end) { Upper = upper; Middle = middle; End = end; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct ShapeLimbMetrics
    {
        public readonly float UpperLength;
        public readonly float LowerLength;
        public float MaximumReach => UpperLength + LowerLength;
        public ShapeLimbMetrics(float upperLength, float lowerLength) { UpperLength = upperLength; LowerLength = lowerLength; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct ShapeContactTarget
    {
        public readonly Float3 Position;
        public readonly Float3 Pole;
        public readonly Quaternion Rotation;
        public readonly byte OrientEnd;
        public ShapeContactTarget(Float3 position, Float3 pole, Quaternion rotation, bool orientEnd = true)
        { Position = position; Pole = pole; Rotation = rotation; OrientEnd = orientEnd ? (byte)1 : (byte)0; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct ShapeReachResult
    {
        public readonly ShapeLimbMetrics Metrics;
        public readonly Float3 Reached;
        public readonly float Residual;
        public readonly byte Clamped;
        public ShapeReachResult(ShapeLimbMetrics metrics, Float3 reached, float residual, bool clamped)
        { Metrics = metrics; Reached = reached; Residual = residual; Clamped = clamped ? (byte)1 : (byte)0; }
    }

    public static class ShapeContacts
    {
        public static AnimationError Measure(ReadOnlySpan<RetargetRow> rows, ReadOnlySpan<Pose> local, in ShapeLimb limb,
            Span<RigidPose> modelScratch, out ShapeLimbMetrics metrics)
        {
            metrics = default;
            if (!Limb(rows, limb) || rows.Length != local.Length || rows.Length > SampleLocator.MaxBones) return AnimationError.InvalidTable;
            var error = ShapeMath.Model(rows, local, modelScratch);
            if (error != AnimationError.None) return error;
            var upper = (modelScratch[limb.Middle].Position - modelScratch[limb.Upper].Position).Length;
            var lower = (modelScratch[limb.End].Position - modelScratch[limb.Middle].Position).Length;
            if (!Scalar.IsFinite(upper + lower) || upper <= 1e-5f || lower <= 1e-5f) return AnimationError.InvalidTable;
            metrics = new ShapeLimbMetrics(upper, lower);
            return AnimationError.None;
        }

        public static AnimationError Reach(ReadOnlySpan<RetargetRow> rows, ReadOnlySpan<Pose> source, in ShapeLimb limb,
            in ShapeContactTarget target, Span<RigidPose> modelScratch, Span<Pose> output, out ShapeReachResult result)
        {
            result = default;
            if (!target.Position.IsFinite || !target.Pole.IsFinite || target.OrientEnd > 1 || target.OrientEnd != 0 && !RigidMath.UnitRotation(target.Rotation))
                return AnimationError.InvalidFrame;
            if (output.Length < source.Length) return AnimationError.CapacityExceeded;
            if (source.Overlaps(output)) return AnimationError.AliasedBuffers;
            var error = Measure(rows, source, limb, modelScratch, out var metrics);
            if (error != AnimationError.None) return error;
            var upper = modelScratch[limb.Upper];
            var middle = modelScratch[limb.Middle];
            var end = modelScratch[limb.End];
            var solved = TwoBoneIK.Solve(upper.Position, middle.Position, end.Position, target.Position, target.Pole);
            if (!solved.Valid || !solved.Middle.IsFinite || !solved.End.IsFinite || !RigidMath.UnitRotation(solved.UpperDelta) || !RigidMath.UnitRotation(solved.LowerDelta))
                return AnimationError.InvalidFrame;
            source.CopyTo(output);
            var parent = rows[limb.Upper].Parent;
            var upperRotation = Quaternion.Multiply(solved.UpperDelta, upper.Rotation);
            output[limb.Upper].Rotation = Quaternion.Normalize(Quaternion.Multiply(Quaternion.InverseUnit(modelScratch[parent].Rotation), upperRotation));
            error = ShapeMath.Model(rows, output.Slice(0, rows.Length), modelScratch);
            if (error != AnimationError.None) return error;
            parent = rows[limb.Middle].Parent;
            var lowerRotation = Quaternion.Multiply(solved.LowerDelta, middle.Rotation);
            output[limb.Middle].Rotation = Quaternion.Normalize(Quaternion.Multiply(Quaternion.InverseUnit(modelScratch[parent].Rotation), lowerRotation));
            error = ShapeMath.Model(rows, output.Slice(0, rows.Length), modelScratch);
            if (error != AnimationError.None) return error;
            if (target.OrientEnd != 0)
            {
                parent = rows[limb.End].Parent;
                output[limb.End].Rotation = Quaternion.Normalize(Quaternion.Multiply(Quaternion.InverseUnit(modelScratch[parent].Rotation), target.Rotation));
                error = ShapeMath.Model(rows, output.Slice(0, rows.Length), modelScratch);
                if (error != AnimationError.None) return error;
            }
            var reached = modelScratch[limb.End].Position;
            var residual = (target.Position - reached).Length;
            var distance = (target.Position - upper.Position).Length;
            if (!Scalar.IsFinite(residual) || !Scalar.IsFinite(distance)) return AnimationError.InvalidFrame;
            result = new ShapeReachResult(metrics, reached, residual,
                distance > metrics.MaximumReach - 1e-5f || distance < Math.Abs(metrics.UpperLength - metrics.LowerLength) + 1e-5f);
            return AnimationError.None;
        }

        public static bool TryMarker(in RigidPose model, in Affine deformation, Float3 boneOffset, out Float3 point)
        {
            point = default;
            if (!model.IsValid || !RigidMath.UnitRotation(model.Rotation) || !ShapeMath.Finite(deformation) || !boneOffset.IsFinite) return false;
            var candidate = model.Position + Quaternion.Rotate(model.Rotation, ShapeMath.Point(deformation, boneOffset));
            if (!candidate.IsFinite) return false;
            point = candidate;
            return true;
        }

        public static bool TryAnkleTarget(in ShapeMarker sole, ReadOnlySpan<Affine> deformations, Float3 supportAnchor,
            Quaternion footRotation, out Float3 ankle)
        {
            ankle = default;
            if (sole.Bone < 0 || sole.Bone >= deformations.Length || !supportAnchor.IsFinite || !RigidMath.UnitRotation(footRotation)
                || !sole.Offset.IsFinite || !ShapeMath.Finite(deformations[sole.Bone])) return false;
            var candidate = supportAnchor - Quaternion.Rotate(footRotation, ShapeMath.Point(deformations[sole.Bone], sole.Offset));
            if (!candidate.IsFinite) return false;
            ankle = candidate;
            return true;
        }

        static bool Limb(ReadOnlySpan<RetargetRow> rows, in ShapeLimb limb) => limb.Upper > 0 && limb.Middle > limb.Upper && limb.End > limb.Middle
            && ShapeMath.Descendant(rows, limb.Middle, limb.Upper) && ShapeMath.Descendant(rows, limb.End, limb.Middle);
    }
}
