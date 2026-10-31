using System;
using SA.Humanoid;
using SA.Motion.Numerics;

namespace SA.Harness
{
    static class HarnessTables
    {
        public static float Radius(Role role) => role switch
        {
            Role.Pelvis => 0.11f,
            Role.Torso => 0.13f,
            Role.Head => 0.10f,
            Role.UpperArmLeft => 0.05f,
            Role.UpperArmRight => 0.05f,
            Role.ForearmLeft => 0.04f,
            Role.ForearmRight => 0.04f,
            Role.HandLeft => 0.035f,
            Role.HandRight => 0.035f,
            Role.ThighLeft => 0.075f,
            Role.ThighRight => 0.075f,
            Role.ShinLeft => 0.055f,
            Role.ShinRight => 0.055f,
            Role.FootLeft => 0.05f,
            Role.FootRight => 0.05f,
            _ => 0.05f,
        };

        public static float HalfLength(Role role) => role switch
        {
            Role.Pelvis => 0.05f,
            Role.Torso => 0.14f,
            Role.Head => 0.04f,
            Role.UpperArmLeft => 0.11f,
            Role.UpperArmRight => 0.11f,
            Role.ForearmLeft => 0.10f,
            Role.ForearmRight => 0.10f,
            Role.HandLeft => 0.05f,
            Role.HandRight => 0.05f,
            Role.ThighLeft => 0.15f,
            Role.ThighRight => 0.15f,
            Role.ShinLeft => 0.14f,
            Role.ShinRight => 0.14f,
            Role.FootLeft => 0.08f,
            Role.FootRight => 0.08f,
            _ => 0.05f,
        };

        public static Float3 Center(Role role) => role switch
        {
            Role.Pelvis => new Float3(0f, 1.00f, 0f),
            Role.Torso => new Float3(0f, 1.30f, 0f),
            Role.Head => new Float3(0f, 1.61f, 0f),
            Role.UpperArmLeft => new Float3(-0.23f, 1.31f, 0f),
            Role.UpperArmRight => new Float3(0.23f, 1.31f, 0f),
            Role.ForearmLeft => new Float3(-0.26f, 1.04f, 0f),
            Role.ForearmRight => new Float3(0.26f, 1.04f, 0f),
            Role.HandLeft => new Float3(-0.275f, 0.84f, 0f),
            Role.HandRight => new Float3(0.275f, 0.84f, 0f),
            Role.ThighLeft => new Float3(-0.11f, 0.745f, 0f),
            Role.ThighRight => new Float3(0.11f, 0.745f, 0f),
            Role.ShinLeft => new Float3(-0.11f, 0.315f, 0f),
            Role.ShinRight => new Float3(0.11f, 0.315f, 0f),
            Role.FootLeft => new Float3(-0.11f, 0.05f, 0f),
            Role.FootRight => new Float3(0.11f, 0.05f, 0f),
            _ => Float3.Zero,
        };

        public static Quaternion BaseOrientation(Role role)
        {
            if (role == Role.FootLeft || role == Role.FootRight)
            {
                return Quaternion.AxisAngle(new Float3(1f, 0f, 0f), Scalar.Pi * 0.5f);
            }
            return Quaternion.Identity;
        }

        public static Float3 JointPoint(Role role) => role switch
        {
            Role.Torso => new Float3(0f, 1.10f, 0f),
            Role.Head => new Float3(0f, 1.50f, 0f),
            Role.UpperArmLeft => new Float3(-0.21f, 1.45f, 0f),
            Role.UpperArmRight => new Float3(0.21f, 1.45f, 0f),
            Role.ForearmLeft => new Float3(-0.25f, 1.17f, 0f),
            Role.ForearmRight => new Float3(0.25f, 1.17f, 0f),
            Role.HandLeft => new Float3(-0.27f, 0.92f, 0f),
            Role.HandRight => new Float3(0.27f, 0.92f, 0f),
            Role.ThighLeft => new Float3(-0.11f, 0.96f, 0f),
            Role.ThighRight => new Float3(0.11f, 0.96f, 0f),
            Role.ShinLeft => new Float3(-0.11f, 0.53f, 0f),
            Role.ShinRight => new Float3(0.11f, 0.53f, 0f),
            Role.FootLeft => new Float3(-0.11f, 0.10f, 0f),
            Role.FootRight => new Float3(0.11f, 0.10f, 0f),
            _ => Center(Role.Pelvis),
        };

        public static Float3 PrincipalInertia(float mass, Role role)
        {
            float radius = Radius(role);
            float length = 2f * HalfLength(role);
            float transverse = mass * (3f * radius * radius + length * length) / 12f;
            float axial = 0.5f * mass * radius * radius;
            if (!Scalar.IsFinite(transverse) || transverse <= 0f)
            {
                transverse = 1e-6f;
            }
            if (!Scalar.IsFinite(axial) || axial <= 0f)
            {
                axial = 1e-6f;
            }
            return new Float3(transverse, axial, transverse);
        }
    }
}
