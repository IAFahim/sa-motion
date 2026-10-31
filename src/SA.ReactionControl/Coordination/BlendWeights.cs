using SA.Humanoid;
using SA.Motion.Numerics;

namespace SA.ReactionControl.Coordination
{
    public static class BlendWeights
    {
        public static float Authority(Role role, ReactionMode mode)
        {
            if ((byte)role >= Roles.Count || (byte)mode > (byte)ReactionMode.Recovering)
                return 0f;
            switch (mode)
            {
                case ReactionMode.Falling:
                    return 0f;
                case ReactionMode.Animated:
                    return role == Role.Pelvis ? 0f : role == Role.Head ? 0.6f : 1f;
                case ReactionMode.Balancing:
                    return AuthorityBalancing(role);
                case ReactionMode.Bracing:
                    return AuthorityBracing(role);
                default:
                    return AuthorityRecovering(role);
            }
        }

        public static float Fade(float elapsed, float fadeTime)
        {
            if (!Scalar.IsFinite(elapsed) || elapsed <= 0f)
                return 0f;
            if (!Scalar.IsFinite(fadeTime) || fadeTime <= Scalar.DurationEpsilon)
                return 1f;
            float t = elapsed / fadeTime;
            if (t >= 1f)
                return 1f;
            return t;
        }

        public static void FadePair(float elapsed, float fadeTime, out float staying, out float incoming)
        {
            incoming = Fade(elapsed, fadeTime);
            staying = 1f - incoming;
        }

        public static float BlendAuthority(float fromAuthority, float toAuthority, float elapsed, float fadeTime)
        {
            float from = UnitOrZero(fromAuthority);
            float to = UnitOrZero(toAuthority);
            return from + (to - from) * Fade(elapsed, fadeTime);
        }

        public static JointDrive BlendDrive(JointDrive from, JointDrive to, float elapsed, float fadeTime)
        {
            float t = Fade(elapsed, fadeTime);
            Quaternion a = Quaternion.Normalize(from.Target);
            Quaternion b = Quaternion.Normalize(to.Target);
            if (Quaternion.Dot(a, b) < 0f)
                b = -b;
            Quaternion target = Quaternion.Normalize(new Quaternion(
                a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t,
                a.Z + (b.Z - a.Z) * t,
                a.W + (b.W - a.W) * t));
            Float3 velocity = from.TargetAngularVelocity + (to.TargetAngularVelocity - from.TargetAngularVelocity) * t;
            if (!velocity.IsFinite)
                velocity = Float3.Zero;
            return new JointDrive
            {
                Target = target,
                TargetAngularVelocity = velocity,
                Stiffness = LerpGain(from.Stiffness, to.Stiffness, t),
                Damping = LerpGain(from.Damping, to.Damping, t),
                MaxTorque = LerpGain(from.MaxTorque, to.MaxTorque, t),
            };
        }

        static float AuthorityBalancing(Role role)
        {
            switch (role)
            {
                case Role.Pelvis:
                    return 0f;
                case Role.ThighLeft:
                case Role.ThighRight:
                case Role.ShinLeft:
                case Role.ShinRight:
                case Role.FootLeft:
                case Role.FootRight:
                    return 1f;
                case Role.Torso:
                    return 0.8f;
                case Role.Head:
                    return 0.5f;
                default:
                    return 0.3f;
            }
        }

        static float AuthorityBracing(Role role)
        {
            switch (role)
            {
                case Role.Pelvis:
                    return 0f;
                case Role.UpperArmLeft:
                case Role.UpperArmRight:
                case Role.ForearmLeft:
                case Role.ForearmRight:
                case Role.HandLeft:
                case Role.HandRight:
                    return 1f;
                case Role.Torso:
                    return 0.8f;
                case Role.Head:
                    return 0.7f;
                default:
                    return 0.8f;
            }
        }

        static float AuthorityRecovering(Role role)
        {
            switch (role)
            {
                case Role.Pelvis:
                    return 0f;
                case Role.Torso:
                case Role.ThighLeft:
                case Role.ThighRight:
                case Role.ShinLeft:
                case Role.ShinRight:
                case Role.FootLeft:
                case Role.FootRight:
                    return 0.7f;
                case Role.Head:
                    return 0.4f;
                default:
                    return 0.5f;
            }
        }

        static float UnitOrZero(float value)
        {
            if (!Scalar.IsFinite(value) || value <= 0f)
                return 0f;
            if (value >= 1f)
                return 1f;
            return value;
        }

        static float LerpGain(float from, float to, float t)
        {
            float a = Scalar.IsFinite(from) && from > 0f ? from : 0f;
            float b = Scalar.IsFinite(to) && to > 0f ? to : 0f;
            return a + (b - a) * t;
        }
    }
}
