using System;
using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.Motion.Inertialization
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct PoseVelocity
    {
        public readonly Float3 Linear;
        public readonly Float3 Angular;

        public PoseVelocity(Float3 linear, Float3 angular) { Linear = linear; Angular = angular; }
        public bool IsFinite => Linear.IsFinite && Angular.IsFinite;

        public static bool TryBetween(in Pose previous, in Pose current, float interval, out PoseVelocity velocity)
        {
            velocity = default;
            if (!PoseDecay.Valid(previous) || !PoseDecay.Valid(current) || !Scalar.IsFinite(interval) || interval <= Scalar.DurationEpsilon)
                return false;
            var linear = (current.Position - previous.Position) / interval;
            var angular = RotationVector.Log(Quaternion.Multiply(Quaternion.Normalize(current.Rotation), Quaternion.InverseUnit(Quaternion.Normalize(previous.Rotation)))) / interval;
            var candidate = new PoseVelocity(linear, angular);
            if (!candidate.IsFinite)
                return false;
            velocity = candidate;
            return true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct PoseDecay
    {
        public readonly Float3 PositionOffset;
        public readonly Float3 PositionVelocity;
        public readonly Float3 RotationOffset;
        public readonly Float3 RotationVelocity;

        public PoseDecay(Float3 positionOffset, Float3 positionVelocity, Float3 rotationOffset, Float3 rotationVelocity)
        {
            PositionOffset = positionOffset;
            PositionVelocity = positionVelocity;
            RotationOffset = rotationOffset;
            RotationVelocity = rotationVelocity;
        }

        public static bool Valid(in Pose pose) => pose.Position.IsFinite && pose.Rotation.IsValidRotation && pose.Scale.IsPositive;

        public static bool TryCreate(in Pose outgoing, in PoseVelocity outgoingVelocity, in Pose destination, in PoseVelocity destinationVelocity, out PoseDecay decay)
        {
            decay = default;
            if (!Valid(outgoing) || !Valid(destination) || !outgoingVelocity.IsFinite || !destinationVelocity.IsFinite)
                return false;
            var error = Quaternion.Normalize(Quaternion.Multiply(Quaternion.Normalize(outgoing.Rotation), Quaternion.InverseUnit(Quaternion.Normalize(destination.Rotation))));
            var rotation = RotationVector.Log(error);
            var angular = outgoingVelocity.Angular - Quaternion.Rotate(error, destinationVelocity.Angular);
            if (!RotationVector.TryLeftJacobianInverse(rotation, angular, out var rotationVelocity))
                return false;
            var position = outgoing.Position - destination.Position;
            var velocity = outgoingVelocity.Linear - destinationVelocity.Linear;
            if (!position.IsFinite || !velocity.IsFinite)
                return false;
            decay = new PoseDecay(position, velocity, rotation, rotationVelocity);
            return true;
        }

        public bool TryApply(Pose destination, float elapsed, float duration, out Pose output)
        {
            output = Pose.Identity;
            if (!Valid(destination) || !Scalar.IsFinite(elapsed) || !Scalar.IsFinite(duration) || elapsed < 0f || duration < 0f)
                return false;
            if (duration <= Scalar.DurationEpsilon || elapsed >= duration)
            {
                output = destination;
                return true;
            }
            if (!PositionOffset.IsFinite || !PositionVelocity.IsFinite || !RotationOffset.IsFinite || !RotationVelocity.IsFinite)
                return false;
            var u = (double)elapsed / duration;
            var inverse = 1.0 - u;
            var b0 = inverse * inverse * inverse * (1.0 + 3.0 * u + 6.0 * u * u);
            var b1 = u * inverse * inverse * inverse * (1.0 + 3.0 * u) * duration;
            var position = destination.Position + PositionOffset * (float)b0 + PositionVelocity * (float)b1;
            var rotation = RotationOffset * (float)b0 + RotationVelocity * (float)b1;
            if (!position.IsFinite || !rotation.IsFinite)
                return false;
            output = new Pose { Position = position, Rotation = Quaternion.Normalize(Quaternion.Multiply(RotationVector.Exp(rotation), Quaternion.Normalize(destination.Rotation))), Scale = destination.Scale };
            return true;
        }
    }
}
