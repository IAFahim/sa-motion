using SA.Motion.Mechanics;
using SA.Motion.Unity;
using Unity.Mathematics;

namespace SA.Reactions
{
    public struct DriveBody
    {
        public quaternion Orientation;
        public quaternion InertiaOrientation;
        public float3 InverseInertia;
        public float3 AngularVelocity;

        public static DriveBody Static(quaternion orientation) => new DriveBody
        {
            Orientation = orientation,
            InertiaOrientation = quaternion.identity
        };
    }

    public static class PoseDriveMath
    {
        public static float3x3 InverseInertiaWorld(in DriveBody body)
        {
            var rotation = new float3x3(body.InertiaOrientation);
            return math.mul(math.mul(rotation, float3x3.Scale(body.InverseInertia)), math.transpose(rotation));
        }

        public static float3 Error(in DriveBody parent, in DriveBody child, in PoseDrive drive)
        {
            var jointParent = math.mul(parent.Orientation, drive.ParentJoint);
            var jointChild = math.mul(child.Orientation, drive.ChildJoint);
            var relative = math.mul(math.inverse(jointParent), jointChild);
            var local = Conversions.ToUnity(RotationError.Between(Conversions.ToMotion(relative), Conversions.ToMotion(drive.Target)));
            return math.rotate(jointParent, local);
        }

        public static float3 Impulse(in DriveBody parent, in DriveBody child, in PoseDrive drive, float timestep, out float3 error, out bool singular)
        {
            singular = false;
            error = Error(parent, child, drive);
            var acceleration = Conversions.ToUnity(AngularSpring.Acceleration(
                Conversions.ToMotion(error),
                Conversions.ToMotion(child.AngularVelocity - parent.AngularVelocity),
                drive.Frequency, drive.DampingRatio, timestep)) * math.saturate(drive.Strength);
            var response = InverseInertiaWorld(parent) + InverseInertiaWorld(child);
            var determinant = math.determinant(response);
            if (!math.isfinite(determinant) || math.abs(determinant) < 1e-18f)
            {
                singular = math.any(acceleration != 0f);
                return float3.zero;
            }
            var impulse = math.mul(math.inverse(response), acceleration * timestep);
            var cap = math.max(0f, drive.MaxTorque) * timestep;
            var length = math.length(impulse);
            if (!math.isfinite(length))
                return float3.zero;
            return length > cap ? impulse * (cap / length) : impulse;
        }
    }
}
