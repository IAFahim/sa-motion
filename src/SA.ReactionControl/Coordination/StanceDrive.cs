using System.Runtime.InteropServices;
using SA.Humanoid;
using SA.Motion.Numerics;

namespace SA.ReactionControl.Coordination
{
    [StructLayout(LayoutKind.Sequential)]
    public struct StanceIntent
    {
        public bool LeftSupport;
        public bool RightSupport;
        public float Weight;
        public float GainWeight;
        public float StiffnessLeft;
        public float StiffnessRight;
    }

    public static class StanceDrive
    {
        public static StanceIntent Resolve(
            bool wantLeft,
            bool wantRight,
            bool contactLeft,
            bool contactRight,
            float weight,
            float gainWeight,
            float stiffnessLeft,
            float stiffnessRight,
            CoordinationParams parameters)
        {
            float lo = Scalar.IsFinite(parameters.MinLegStiffness) && parameters.MinLegStiffness > 0f ? parameters.MinLegStiffness : 0f;
            float hi = Scalar.IsFinite(parameters.MaxLegStiffness) && parameters.MaxLegStiffness > lo ? parameters.MaxLegStiffness : lo;
            float w = Scalar.IsFinite(weight) && weight > 0f ? weight > 1f ? 1f : weight : 0f;
            float g = Scalar.IsFinite(gainWeight) && gainWeight > 0f ? gainWeight > w ? w : gainWeight : 0f;
            return new StanceIntent
            {
                LeftSupport = wantLeft && contactLeft,
                RightSupport = wantRight && contactRight,
                Weight = w,
                GainWeight = g,
                StiffnessLeft = ClampStiffness(stiffnessLeft, lo, hi),
                StiffnessRight = ClampStiffness(stiffnessRight, lo, hi),
            };
        }

        public static float ResponseFor(Role role, bool supported, CoordinationParams parameters)
        {
            float stanceAnkle = PositiveOr(parameters.SupportedAnkleResponse, 1f);
            float stanceKnee = PositiveOr(parameters.SupportedKneeResponse, 1f);
            float swingAnkle = PositiveOr(parameters.SwingAnkleResponse, 1f);
            float swingKnee = PositiveOr(parameters.SwingKneeResponse, 1f);
            switch (role)
            {
                case Role.FootLeft:
                case Role.FootRight:
                    return supported ? stanceAnkle : swingAnkle;
                case Role.ShinLeft:
                case Role.ShinRight:
                    return supported ? stanceKnee : swingKnee;
                case Role.ThighLeft:
                case Role.ThighRight:
                    return stanceKnee;
                default:
                    return 0f;
            }
        }

        public static JointDrive Calibrate(Quaternion pose, float stiffness, float damping, float maxTorque)
        {
            return new JointDrive
            {
                Target = Quaternion.Normalize(pose),
                TargetAngularVelocity = Float3.Zero,
                Stiffness = NonNegativeOrZero(stiffness),
                Damping = NonNegativeOrZero(damping),
                MaxTorque = NonNegativeOrZero(maxTorque),
            };
        }

        public static float RelaxAuthority(float relaxation)
        {
            if (!Scalar.IsFinite(relaxation) || relaxation <= 0f)
                return 1f;
            if (relaxation >= 1f)
                return 0f;
            return 1f - relaxation;
        }

        static float ClampStiffness(float value, float lo, float hi)
        {
            if (!Scalar.IsFinite(value) || value <= lo)
                return lo;
            if (value >= hi)
                return hi;
            return value;
        }

        static float PositiveOr(float value, float fallback)
        {
            return Scalar.IsFinite(value) && value > 0f ? value : fallback;
        }

        static float NonNegativeOrZero(float value)
        {
            return Scalar.IsFinite(value) && value > 0f ? value : 0f;
        }
    }
}
