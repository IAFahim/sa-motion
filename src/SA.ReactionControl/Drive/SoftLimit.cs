using System;
using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.ReactionControl.Drive
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SoftLimit
    {
        public float Angle;
        public float Direction;
        public float Stiffness;
        public float Damping;
        public byte VelocityScaled;
        public byte Enabled;

        public static float Accel(SoftLimit limit, float at, float velocity)
        {
            if (limit.Enabled == 0)
                return 0f;
            if (!Scalar.IsFinite(at) || !Scalar.IsFinite(velocity))
                return 0f;
            if (!Scalar.IsFinite(limit.Angle) || !Scalar.IsFinite(limit.Direction) || !Scalar.IsFinite(limit.Stiffness) || !Scalar.IsFinite(limit.Damping))
                return 0f;
            var error = at - limit.Angle;
            if (error * limit.Direction <= 0f)
                return 0f;
            var scale = 1f;
            if (limit.VelocityScaled != 0)
            {
                var approach = velocity * limit.Direction;
                scale = 1f + (approach > 0f ? approach : 0f);
            }
            var shaped = limit.Stiffness > 0f ? limit.Stiffness : 0f;
            var result = -(limit.Stiffness * error + 2f * limit.Damping * (float)Math.Sqrt(shaped) * velocity) * scale;
            return Scalar.IsFinite(result) ? result : 0f;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SoftBounds
    {
        public SoftLimit Twist;
        public SoftLimit Lean1;
        public SoftLimit Lean2;
    }
}
