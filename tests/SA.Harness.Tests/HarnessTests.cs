using System;
using SA.Humanoid;
using SA.Motion.Numerics;
using SA.ReactionControl;
using Xunit;

namespace SA.Harness.Tests;

public class HarnessTests
{
    const float Dt60 = 0.01666667f;
    const float Dt120 = 0.008333333f;
    const float CrumpleSeconds = 5f;
    const float RestPenetrationLimit = 0.02f;
    const float AnchorLimit = 0.02f;
    const float LimitRadians = 0.08726647f;
    const float PelvisHoldLimit = 0.10f;
    const float TimestepAgreeLimit = 0.05f;

    static HarnessWorld Lifted()
    {
        var world = HarnessWorld.Create(70f);
        world.TranslateAll(new Float3(0f, 1f, 0f));
        return world;
    }

    static void March(HarnessWorld world, float dt, float seconds)
    {
        int steps = (int)(seconds / dt + 0.5f);
        for (int i = 0; i < steps; i++)
        {
            world.Step(dt);
        }
    }

    static float MaxPenetration(HarnessWorld world)
    {
        float worst = 0f;
        for (int i = 0; i < HarnessWorld.BodyCount; i++)
        {
            var role = (Role)i;
            RoleBody body = world.Bodies[i];
            Float3 axis = Quaternion.Rotate(body.Rotation, new Float3(0f, 1f, 0f));
            float half = world.BodyHalfLength(role);
            float radius = world.BodyRadius(role);
            for (int s = -1; s <= 1; s++)
            {
                Float3 sample = body.Position + axis * (half * s);
                float penetration = radius - sample.Y;
                if (penetration > worst)
                {
                    worst = penetration;
                }
            }
        }
        return worst;
    }

    static float MaxAnchorSeparation(HarnessWorld world)
    {
        float worst = 0f;
        for (int i = 0; i < HarnessWorld.BodyCount; i++)
        {
            var role = (Role)i;
            if (Roles.IsRoot(role))
            {
                continue;
            }
            float gap = world.JointAnchorSeparation(role);
            if (gap > worst)
            {
                worst = gap;
            }
        }
        return worst;
    }

    static float MaxLimitViolation(HarnessWorld world)
    {
        float worst = 0f;
        for (int i = 0; i < HarnessWorld.BodyCount; i++)
        {
            var role = (Role)i;
            if (Roles.IsRoot(role))
            {
                continue;
            }
            float excess = world.JointLimitViolation(role);
            if (excess > worst)
            {
                worst = excess;
            }
        }
        return worst;
    }

    [Fact]
    public void DropSettlesWithShallowPenetrationAndDecayingEnergy()
    {
        var world = Lifted();
        float previous = world.TotalEnergy();
        float restWorst = 0f;
        int steps = (int)(CrumpleSeconds / Dt60 + 0.5f);
        for (int i = 0; i < steps; i++)
        {
            world.Step(Dt60);
            float next = world.TotalEnergy();
            Assert.True(next <= previous * 1.01f, EnergyMessage(i, previous, next));
            previous = next;
            if (i >= steps - 60)
            {
                float penetration = MaxPenetration(world);
                if (penetration > restWorst)
                {
                    restWorst = penetration;
                }
            }
        }
        Assert.True(restWorst <= RestPenetrationLimit, PenetrationMessage(restWorst));
    }

    [Fact]
    public void JointsStayConnectedDuringPassiveCollapse()
    {
        var world = Lifted();
        float worst = 0f;
        int steps = (int)(CrumpleSeconds / Dt60 + 0.5f);
        for (int i = 0; i < steps; i++)
        {
            world.Step(Dt60);
            float gap = MaxAnchorSeparation(world);
            if (gap > worst)
            {
                worst = gap;
            }
        }
        Assert.True(worst <= AnchorLimit, AnchorMessage(worst));
    }

    [Fact]
    public void JointLimitsHoldAtRest()
    {
        var world = Lifted();
        March(world, Dt60, CrumpleSeconds);
        float worst = MaxLimitViolation(world);
        Assert.True(worst <= LimitRadians, LimitMessage(worst));
    }

    [Fact]
    public void StrongDrivesHoldStandingPose()
    {
        var world = HarnessWorld.Create(70f);
        Float3 start = world.Bodies[(int)Role.Pelvis].Position;
        var drives = new JointDrive[HarnessWorld.BodyCount];
        for (int i = 0; i < HarnessWorld.BodyCount; i++)
        {
            var role = (Role)i;
            Float3 principal = world.BodyPrincipalInertia(role);
            float weak = Scalar.Min(principal.X, Scalar.Min(principal.Y, principal.Z));
            drives[i] = new JointDrive
            {
                Target = world.Bodies[i].Rotation,
                TargetAngularVelocity = Float3.Zero,
                Stiffness = HoldStiffness(role),
                Damping = HoldDamping(world, role),
                MaxTorque = HoldStiffness(role) * 0.5f,
            };
        }
        world.ApplyDrives(drives);
        March(world, Dt60, CrumpleSeconds);
        Float3 end = world.Bodies[(int)Role.Pelvis].Position;
        float drift = (end - start).Length;
        Assert.True(drift <= PelvisHoldLimit, HoldMessage(drift));
        float rest = MaxPenetration(world);
        Assert.True(rest <= RestPenetrationLimit, PenetrationMessage(rest));
    }

    [Fact]
    public void RunsAreBitIdentical()
    {
        var first = Lifted();
        var second = Lifted();
        var drives = new JointDrive[HarnessWorld.BodyCount];
        for (int i = 0; i < HarnessWorld.BodyCount; i++)
        {
            var role = (Role)i;
            drives[i] = new JointDrive
            {
                Target = first.Bodies[i].Rotation,
                TargetAngularVelocity = Float3.Zero,
                Stiffness = HoldStiffness(role),
                Damping = HoldDamping(first, role),
                MaxTorque = HoldStiffness(role) * 0.5f,
            };
        }
        first.ApplyDrives(drives);
        second.ApplyDrives(drives);
        first.ApplyImpulse(Role.Torso, new Float3(4f, 0f, 0f), first.Bodies[(int)Role.Torso].Position);
        second.ApplyImpulse(Role.Torso, new Float3(4f, 0f, 0f), second.Bodies[(int)Role.Torso].Position);
        for (int i = 0; i < 120; i++)
        {
            first.Step(Dt60);
            second.Step(Dt60);
        }
        for (int i = 0; i < HarnessWorld.BodyCount; i++)
        {
            RoleBody a = first.Bodies[i];
            RoleBody b = second.Bodies[i];
            Assert.True(SameBody(a, b), DeterminismMessage(i));
        }
    }

    [Fact]
    public void CoarseAndFineStepsAgreeOnRestPose()
    {
        var coarse = Lifted();
        var fine = Lifted();
        March(coarse, Dt60, CrumpleSeconds);
        March(fine, Dt120, CrumpleSeconds);
        float worst = 0f;
        for (int i = 0; i < HarnessWorld.BodyCount; i++)
        {
            float gap = (coarse.Bodies[i].Position - fine.Bodies[i].Position).Length;
            if (gap > worst)
            {
                worst = gap;
            }
        }
        Assert.True(worst <= TimestepAgreeLimit, TimestepMessage(worst));
    }

    [Fact]
    public void ProbeGroundTracksFootHeight()
    {
        var world = HarnessWorld.Create(70f);
        SupportProbe touching = world.ProbeGround(Role.FootLeft);
        Assert.Equal((byte)1, touching.Hit);
        Assert.True(Scalar.Abs(touching.Distance) <= 0.03f, ProbeMessage(touching.Distance));
        world.TranslateAll(new Float3(0f, 1f, 0f));
        SupportProbe lifted = world.ProbeGround(Role.FootLeft);
        Assert.Equal((byte)0, lifted.Hit);
        Assert.True(lifted.Distance > 0.5f, ProbeMessage(lifted.Distance));
    }

    [Fact]
    public void ApplyImpulseChangesVelocityAndIgnoresBadInput()
    {
        var world = HarnessWorld.Create(70f);
        RoleBody before = world.Bodies[(int)Role.Pelvis];
        var push = new Float3(2f, 0f, 0f);
        world.ApplyImpulse(Role.Pelvis, push, before.Position);
        RoleBody after = world.Bodies[(int)Role.Pelvis];
        Float3 expected = before.LinearVelocity + push * before.InverseMass;
        Assert.Equal(expected.X, after.LinearVelocity.X);
        Assert.Equal(expected.Y, after.LinearVelocity.Y);
        Assert.Equal(expected.Z, after.LinearVelocity.Z);
        Assert.Equal(before.AngularVelocity.X, after.AngularVelocity.X);
        world.ApplyImpulse((Role)77, push, before.Position);
        world.ApplyImpulse(Role.Pelvis, new Float3(float.NaN, 0f, 0f), before.Position);
        world.Step(float.NaN);
        world.Step(-1f);
        var fallback = HarnessWorld.Create(-3f);
        Assert.Equal(HarnessWorld.BodyCount, fallback.Bodies.Length);
    }

    static bool SameBody(RoleBody a, RoleBody b)
    {
        return a.Position.X == b.Position.X && a.Position.Y == b.Position.Y && a.Position.Z == b.Position.Z
            && a.Rotation.X == b.Rotation.X && a.Rotation.Y == b.Rotation.Y
            && a.Rotation.Z == b.Rotation.Z && a.Rotation.W == b.Rotation.W
            && a.LinearVelocity.X == b.LinearVelocity.X && a.LinearVelocity.Y == b.LinearVelocity.Y
            && a.LinearVelocity.Z == b.LinearVelocity.Z
            && a.AngularVelocity.X == b.AngularVelocity.X && a.AngularVelocity.Y == b.AngularVelocity.Y
            && a.AngularVelocity.Z == b.AngularVelocity.Z;
    }

    static string EnergyMessage(int step, float previous, float next)
    {
        return string.Concat("energy rose at step ", step.ToString(), ": ", previous.ToString(), " -> ", next.ToString());
    }

    static string PenetrationMessage(float penetration)
    {
        return string.Concat("penetration ", penetration.ToString(), " exceeds 0.02");
    }

    static string AnchorMessage(float gap)
    {
        return string.Concat("anchor gap ", gap.ToString(), " exceeds 0.02");
    }

    static string LimitMessage(float excess)
    {
        return string.Concat("limit excess ", excess.ToString(), " radians exceeds 5 degrees");
    }

    static string HoldMessage(float drift)
    {
        return string.Concat("pelvis drift ", drift.ToString(), " exceeds 0.10");
    }

    static string DeterminismMessage(int index)
    {
        return string.Concat("body ", index.ToString(), " differs between runs");
    }

    static string TimestepMessage(float gap)
    {
        return string.Concat("rest pose gap ", gap.ToString(), " exceeds 0.05");
    }

    static string ProbeMessage(float distance)
    {
        return string.Concat("probe distance ", distance.ToString(), " out of range");
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
