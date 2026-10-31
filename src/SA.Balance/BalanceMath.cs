using SA.Humanoid;
using SA.Humanoid.Profile;
using SA.Motion.Numerics;

namespace SA.Balance
{
    static class BalanceMath
    {
        public static float SanitizeNumber(float value, float fallback)
        {
            if (!Scalar.IsFinite(value))
            {
                return fallback;
            }
            return value;
        }

        public static Float3 SanitizeVector(Float3 value, Float3 fallback)
        {
            if (!value.IsFinite)
            {
                return fallback;
            }
            return value;
        }

        public static Quaternion SanitizeOrientation(Quaternion value, Quaternion fallback)
        {
            if (!value.IsValidRotation)
            {
                return SanitizeOrientationFallback(fallback);
            }
            return Quaternion.Normalize(value);
        }

        static Quaternion SanitizeOrientationFallback(Quaternion fallback)
        {
            if (!fallback.IsValidRotation)
            {
                return Quaternion.Identity;
            }
            return Quaternion.Normalize(fallback);
        }

        public static BalanceParams SanitizeParams(BalanceParams p)
        {
            BalanceParams standard = BalanceParams.Standard;
            BalanceParams q = p;
            q.Gravity = PositiveOr(q.Gravity, standard.Gravity);
            q.ComHeightMin = PositiveOr(q.ComHeightMin, standard.ComHeightMin);
            q.ComHeightMax = PositiveOr(q.ComHeightMax, standard.ComHeightMax);
            q.PelvisStiffness = NonNegativeOr(q.PelvisStiffness, standard.PelvisStiffness);
            q.TorsoStiffness = NonNegativeOr(q.TorsoStiffness, standard.TorsoStiffness);
            q.HandStiffness = NonNegativeOr(q.HandStiffness, standard.HandStiffness);
            q.ThighStiffness = NonNegativeOr(q.ThighStiffness, standard.ThighStiffness);
            q.ShinStiffness = NonNegativeOr(q.ShinStiffness, standard.ShinStiffness);
            q.FootStiffness = NonNegativeOr(q.FootStiffness, standard.FootStiffness);
            q.PelvisDamping = NonNegativeOr(q.PelvisDamping, standard.PelvisDamping);
            q.TorsoDamping = NonNegativeOr(q.TorsoDamping, standard.TorsoDamping);
            q.HeadDamping = NonNegativeOr(q.HeadDamping, standard.HeadDamping);
            q.ArmDamping = NonNegativeOr(q.ArmDamping, standard.ArmDamping);
            q.ForearmDamping = NonNegativeOr(q.ForearmDamping, standard.ForearmDamping);
            q.HandDamping = NonNegativeOr(q.HandDamping, standard.HandDamping);
            q.ThighDamping = NonNegativeOr(q.ThighDamping, standard.ThighDamping);
            q.ShinDamping = NonNegativeOr(q.ShinDamping, standard.ShinDamping);
            q.FootDamping = NonNegativeOr(q.FootDamping, standard.FootDamping);
            q.TorqueScale = NonNegativeOr(q.TorqueScale, standard.TorqueScale);
            q.SupportRadius = NonNegativeOr(q.SupportRadius, standard.SupportRadius);
            q.HeightGain = SanitizeNumber(q.HeightGain, standard.HeightGain);
            q.HeightMax = NonNegativeOr(q.HeightMax, standard.HeightMax);
            q.UprightGain = SanitizeNumber(q.UprightGain, standard.UprightGain);
            q.UprightDamp = SanitizeNumber(q.UprightDamp, standard.UprightDamp);
            q.UprightMax = NonNegativeOr(q.UprightMax, standard.UprightMax);
            q.AnkleGain = SanitizeNumber(q.AnkleGain, standard.AnkleGain);
            q.AnkleVelGain = SanitizeNumber(q.AnkleVelGain, standard.AnkleVelGain);
            q.HipGain = SanitizeNumber(q.HipGain, standard.HipGain);
            q.HipVelGain = SanitizeNumber(q.HipVelGain, standard.HipVelGain);
            q.HipRollGain = SanitizeNumber(q.HipRollGain, standard.HipRollGain);
            q.HipRollVelGain = SanitizeNumber(q.HipRollVelGain, standard.HipRollVelGain);
            q.LeanMax = NonNegativeOr(q.LeanMax, standard.LeanMax);
            q.StepMargin = NonNegativeOr(q.StepMargin, standard.StepMargin);
            q.StepHeight = NonNegativeOr(q.StepHeight, standard.StepHeight);
            q.StepTime = PositiveOr(q.StepTime, standard.StepTime);
            q.StepAnkleHeight = PositiveOr(q.StepAnkleHeight, standard.StepAnkleHeight);
            q.StepThighLength = PositiveOr(q.StepThighLength, standard.StepThighLength);
            q.StepShinLength = PositiveOr(q.StepShinLength, standard.StepShinLength);
            q.StepLead = NonNegativeOr(q.StepLead, standard.StepLead);
            q.MaxReach = PositiveOr(q.MaxReach, standard.MaxReach);
            q.FallReach = PositiveOr(q.FallReach, standard.FallReach);
            q.StanceBoost = PositiveOr(q.StanceBoost, standard.StanceBoost);
            q.FallToneStiffness = NonNegativeOr(q.FallToneStiffness, standard.FallToneStiffness);
            q.FallToneDamping = NonNegativeOr(q.FallToneDamping, standard.FallToneDamping);
            q.FallToneTorque = NonNegativeOr(q.FallToneTorque, standard.FallToneTorque);
            if (q.ComHeightMax < q.ComHeightMin)
            {
                q.ComHeightMax = q.ComHeightMin;
            }
            return q;
        }

        static float PositiveOr(float value, float fallback)
        {
            if (!Scalar.IsFinite(value) || value <= 0f)
            {
                return fallback;
            }
            return value;
        }

        static float NonNegativeOr(float value, float fallback)
        {
            if (!Scalar.IsFinite(value) || value < 0f)
            {
                return fallback;
            }
            return value;
        }

        public static float StiffnessFor(Role role, BalanceParams p)
        {
            switch (role)
            {
                case Role.Pelvis: return p.PelvisStiffness;
                case Role.Torso: return p.TorsoStiffness;
                case Role.Head: return p.HeadStiffness;
                case Role.UpperArmLeft: return p.ArmStiffness;
                case Role.UpperArmRight: return p.ArmStiffness;
                case Role.ForearmLeft: return p.ForearmStiffness;
                case Role.ForearmRight: return p.ForearmStiffness;
                case Role.HandLeft: return p.HandStiffness;
                case Role.HandRight: return p.HandStiffness;
                case Role.ThighLeft: return p.ThighStiffness;
                case Role.ThighRight: return p.ThighStiffness;
                case Role.ShinLeft: return p.ShinStiffness;
                case Role.ShinRight: return p.ShinStiffness;
                case Role.FootLeft: return p.FootStiffness;
                case Role.FootRight: return p.FootStiffness;
                default: return 0f;
            }
        }

        public static float DampingFor(Role role, BalanceParams p)
        {
            switch (role)
            {
                case Role.Pelvis: return p.PelvisDamping;
                case Role.Torso: return p.TorsoDamping;
                case Role.Head: return p.HeadDamping;
                case Role.UpperArmLeft: return p.ArmDamping;
                case Role.UpperArmRight: return p.ArmDamping;
                case Role.ForearmLeft: return p.ForearmDamping;
                case Role.ForearmRight: return p.ForearmDamping;
                case Role.HandLeft: return p.HandDamping;
                case Role.HandRight: return p.HandDamping;
                case Role.ThighLeft: return p.ThighDamping;
                case Role.ThighRight: return p.ThighDamping;
                case Role.ShinLeft: return p.ShinDamping;
                case Role.ShinRight: return p.ShinDamping;
                case Role.FootLeft: return p.FootDamping;
                case Role.FootRight: return p.FootDamping;
                default: return 0f;
            }
        }

        public static void CenterOfMass(System.ReadOnlySpan<BodyState> bodies, out Float3 center, out Float3 velocity)
        {
            Float3 sum = Float3.Zero;
            Float3 vsum = Float3.Zero;
            float total = 0f;
            int count = bodies.Length < Roles.Count ? bodies.Length : Roles.Count;
            for (int i = 0; i < count; i++)
            {
                float mass = HumanProfile.Mass((Role)i, 1f);
                if (!Scalar.IsFinite(mass) || mass <= 0f)
                {
                    continue;
                }
                Float3 position = SanitizeVector(bodies[i].Position, Float3.Zero);
                Float3 linear = SanitizeVector(bodies[i].LinearVelocity, Float3.Zero);
                sum = sum + position * mass;
                vsum = vsum + linear * mass;
                total += mass;
            }
            if (!(total > 1e-9f))
            {
                center = Float3.Zero;
                velocity = Float3.Zero;
                return;
            }
            center = sum / total;
            velocity = vsum / total;
        }

        public static Float3 CapturePoint(Float3 center, Float3 velocity, BalanceParams p)
        {
            float height = center.Y;
            if (height < p.ComHeightMin)
            {
                height = p.ComHeightMin;
            }
            if (height > p.ComHeightMax)
            {
                height = p.ComHeightMax;
            }
            float omega = (float)System.Math.Sqrt(p.Gravity / height);
            if (!(omega > 1e-6f) || !Scalar.IsFinite(omega))
            {
                return new Float3(center.X, 0f, center.Z);
            }
            float x = center.X + velocity.X / omega;
            float z = center.Z + velocity.Z / omega;
            if (!Scalar.IsFinite(x))
            {
                x = center.X;
            }
            if (!Scalar.IsFinite(z))
            {
                z = center.Z;
            }
            return new Float3(x, 0f, z);
        }

        public static Float3 SupportCenter(System.ReadOnlySpan<FootContact> feet, Float3 fallback)
        {
            Float3 sum = Float3.Zero;
            int hits = 0;
            for (int i = 0; i < feet.Length; i++)
            {
                if (!feet[i].Hit)
                {
                    continue;
                }
                Float3 point = SanitizeVector(feet[i].Point, fallback);
                sum = sum + new Float3(point.X, 0f, point.Z);
                hits++;
            }
            if (hits > 0)
            {
                return sum / hits;
            }
            for (int i = 0; i < feet.Length; i++)
            {
                Float3 point = SanitizeVector(feet[i].Point, fallback);
                sum = sum + new Float3(point.X, 0f, point.Z);
            }
            if (feet.Length > 0)
            {
                return sum / feet.Length;
            }
            return new Float3(fallback.X, 0f, fallback.Z);
        }

        public static float Clamp(float value, float minimum, float maximum)
        {
            if (value < minimum)
            {
                return minimum;
            }
            if (value > maximum)
            {
                return maximum;
            }
            return value;
        }

        public static Quaternion OffsetAboutAxis(Quaternion basis, Float3 axis, float angle)
        {
            float length = axis.Length;
            if (!(length > 1e-9f) || !Scalar.IsFinite(angle) || angle == 0f)
            {
                return basis;
            }
            Float3 unit = axis / length;
            Quaternion shift = Quaternion.AxisAngle(unit, angle);
            return Quaternion.Normalize(Quaternion.Multiply(shift, SanitizeOrientation(basis, Quaternion.Identity)));
        }

        public static Quaternion OffsetByVector(Quaternion basis, Float3 vector, float maximum)
        {
            float length = vector.Length;
            if (!(length > 1e-9f))
            {
                return basis;
            }
            float angle = length;
            if (angle > maximum)
            {
                angle = maximum;
                vector = vector * (maximum / length);
            }
            Float3 axis = vector / length;
            if (!axis.IsFinite)
            {
                return basis;
            }
            Quaternion shift = Quaternion.AxisAngle(axis, angle);
            if (!shift.IsValidRotation)
            {
                return basis;
            }
            return Quaternion.Normalize(Quaternion.Multiply(shift, SanitizeOrientation(basis, Quaternion.Identity)));
        }
        public static Float3 HipJoint(BodyState pelvis, int side)
        {
            Float3 center = SanitizeVector(pelvis.Position, Float3.Zero);
            float lateral = side < 0 ? -0.11f : 0.11f;
            return center + new Float3(lateral, -0.04f, 0f);
        }

        public static bool SolveKnee(Float3 hip, Float3 ankle, float thigh, float shin, out Float3 knee)
        {
            knee = hip + new Float3(0f, -thigh, 0f);
            if (!(thigh > 1e-6f) || !(shin > 1e-6f))
            {
                return false;
            }
            Float3 span = ankle - hip;
            float distance = span.Length;
            float reach = thigh + shin - 1e-4f;
            if (!(distance > 1e-9f))
            {
                return false;
            }
            if (distance > reach)
            {
                span = span * (reach / distance);
                distance = reach;
            }
            Float3 direction = span / distance;
            float cosine = (thigh * thigh + distance * distance - shin * shin) / (2f * thigh * distance);
            cosine = Clamp(cosine, -1f, 1f);
            float sine = (float)System.Math.Sqrt(1f - cosine * cosine);
            Float3 hint = new Float3(0f, 0f, 1f);
            Float3 axis = Float3.Cross(direction, hint);
            if (!(axis.LengthSquared > 1e-12f))
            {
                axis = new Float3(1f, 0f, 0f);
            }
            axis = axis / axis.Length;
            Float3 bend = Float3.Cross(axis, direction);
            if (!(bend.LengthSquared > 1e-12f))
            {
                return false;
            }
            bend = bend / bend.Length;
            knee = hip + (direction * cosine + bend * sine) * thigh;
            return knee.IsFinite;
        }

        public static Quaternion MapAxisY(Float3 direction)
        {
            if (!direction.IsFinite || !(direction.LengthSquared > 1e-12f))
            {
                return Quaternion.Identity;
            }
            Float3 unit = direction / direction.Length;
            float cosine = Clamp(unit.Y, -1f, 1f);
            if (cosine > 1f - 1e-6f)
            {
                return Quaternion.Identity;
            }
            if (cosine < -1f + 1e-6f)
            {
                return Quaternion.AxisAngle(new Float3(1f, 0f, 0f), Scalar.Pi);
            }
            Float3 axis = new Float3(unit.Z, 0f, -unit.X);
            float length = axis.Length;
            if (!(length > 1e-9f))
            {
                return Quaternion.Identity;
            }
            float angle = (float)System.Math.Acos(cosine);
            return Quaternion.Normalize(Quaternion.AxisAngle(axis / length, angle));
        }
    }
}
