using System;
using SA.Humanoid;
using SA.Motion.Numerics;
using SA.ReactionControl;

namespace SA.Harness
{
    public static class Controllers
    {
        public static void Passive(HarnessWorld world, float dt, Span<JointDrive> drives)
        {
            for (int i = 0; i < drives.Length; i++)
            {
                drives[i] = JointDrive.Passive;
            }
        }

        public static ControllerStep HoldPose()
        {
            Quaternion[]? targets = null;
            float[]? stiffness = null;
            float[]? damping = null;
            return (HarnessWorld world, float dt, Span<JointDrive> drives) =>
            {
                if (targets is null || stiffness is null || damping is null)
                {
                    targets = new Quaternion[HarnessWorld.BodyCount];
                    stiffness = new float[HarnessWorld.BodyCount];
                    damping = new float[HarnessWorld.BodyCount];
                    for (int i = 0; i < HarnessWorld.BodyCount; i++)
                    {
                        var role = (Role)i;
                        targets[i] = world.Bodies[i].Rotation;
                        stiffness[i] = HoldStiffness(role);
                        damping[i] = HoldDamping(world, role);
                    }
                }
                int count = drives.Length < HarnessWorld.BodyCount ? drives.Length : HarnessWorld.BodyCount;
                for (int i = 0; i < count; i++)
                {
                    drives[i] = new JointDrive
                    {
                        Target = targets[i],
                        TargetAngularVelocity = Float3.Zero,
                        Stiffness = stiffness[i],
                        Damping = damping[i],
                        MaxTorque = stiffness[i] * 0.5f,
                    };
                }
            };
        }

        static float HoldStiffness(Role role) => role switch
        {
            Role.Pelvis => 2000f,
            Role.Torso => 200f,
            Role.Head => 80f,
            Role.UpperArmLeft => 30f,
            Role.UpperArmRight => 30f,
            Role.ForearmLeft => 15f,
            Role.ForearmRight => 15f,
            Role.HandLeft => 10f,
            Role.HandRight => 10f,
            Role.ThighLeft => 1000f,
            Role.ThighRight => 1000f,
            Role.ShinLeft => 900f,
            Role.ShinRight => 900f,
            Role.FootLeft => 700f,
            Role.FootRight => 700f,
            _ => 0f,
        };

        static float HoldDamping(HarnessWorld world, Role role)
        {
            Float3 child = world.BodyPrincipalInertia(role);
            float inverse = 1f / child.X;
            if (!Roles.IsRoot(role))
            {
                Float3 parent = world.BodyPrincipalInertia(Roles.Parent(role));
                inverse += 1f / parent.X;
            }
            float coupled = 1f / inverse;
            return 2f * (float)Math.Sqrt(HoldStiffness(role) * coupled);
        }
    }
}
