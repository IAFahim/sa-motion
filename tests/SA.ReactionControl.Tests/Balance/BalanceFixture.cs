using SA.Humanoid;
using SA.Motion.Mechanics;
using SA.Motion.Numerics;
using SA.ReactionControl.Balance;

namespace SA.ReactionControl.Tests.Balance;

static class BalanceFixture
{

    public static RoleBody[] Standing(Float3 velocity)
    {
        return Stance(velocity, 0.02f, 0.02f);
    }

    public static RoleBody[] GuardStance(Float3 velocity)
    {
        return Stance(velocity, 0.03f, 0.01f);
    }
    static RoleBody[] Stance(Float3 velocity, float leftZ, float rightZ)
    {
        var bodies = new RoleBody[Roles.Count];
        Set(bodies, Role.Pelvis, new Float3(0f, 0.95f, 0.005f), velocity);
        Set(bodies, Role.Torso, new Float3(0f, 1.25f, 0.005f), velocity);
        Set(bodies, Role.Head, new Float3(0f, 1.55f, 0f), velocity);
        Set(bodies, Role.UpperArmLeft, new Float3(0.25f, 1.35f, 0f), velocity);
        Set(bodies, Role.UpperArmRight, new Float3(-0.25f, 1.35f, 0f), velocity);
        Set(bodies, Role.ForearmLeft, new Float3(0.28f, 1.1f, 0f), velocity);
        Set(bodies, Role.ForearmRight, new Float3(-0.28f, 1.1f, 0f), velocity);
        Set(bodies, Role.HandLeft, new Float3(0.29f, 0.9f, 0f), velocity);
        Set(bodies, Role.HandRight, new Float3(-0.29f, 0.9f, 0f), velocity);
        Set(bodies, Role.ThighLeft, new Float3(0.11f, 0.6f, leftZ * 0.5f), velocity);
        Set(bodies, Role.ThighRight, new Float3(-0.11f, 0.6f, rightZ * 0.5f), velocity);
        Set(bodies, Role.ShinLeft, new Float3(0.12f, 0.32f, leftZ * 0.75f), velocity);
        Set(bodies, Role.ShinRight, new Float3(-0.12f, 0.32f, rightZ * 0.75f), velocity);
        Set(bodies, Role.FootLeft, new Float3(0.13f, 0.06f, leftZ), velocity);
        Set(bodies, Role.FootRight, new Float3(-0.13f, 0.06f, rightZ), velocity);
        return bodies;
    }

    public static void Probes(out SupportProbe left, out SupportProbe right)
    {
        left = new SupportProbe
        {
            Hit = 1,
            Distance = 0.06f,
            Point = new Float3(0.13f, 0f, 0.02f),
            Normal = new Float3(0f, 1f, 0f),
            PointVelocity = Float3.Zero
        };
        right = new SupportProbe
        {
            Hit = 1,
            Distance = 0.06f,
            Point = new Float3(-0.13f, 0f, 0.02f),
            Normal = new Float3(0f, 1f, 0f),
            PointVelocity = Float3.Zero
        };
    }

    public static RoleBody[] Mirrored(RoleBody[] bodies)
    {
        var mirrored = new RoleBody[bodies.Length];
        for (var i = 0; i < bodies.Length; i++)
            mirrored[i] = bodies[i];
        Swap(mirrored, Role.ThighLeft, Role.ThighRight);
        Swap(mirrored, Role.ShinLeft, Role.ShinRight);
        Swap(mirrored, Role.FootLeft, Role.FootRight);
        Swap(mirrored, Role.UpperArmLeft, Role.UpperArmRight);
        Swap(mirrored, Role.ForearmLeft, Role.ForearmRight);
        Swap(mirrored, Role.HandLeft, Role.HandRight);
        for (var i = 0; i < mirrored.Length; i++)
        {
            var body = mirrored[i];
            body.Position = Mirror(body.Position);
            body.Rotation = MirrorRotation(body.Rotation);
            body.LinearVelocity = Mirror(body.LinearVelocity);
            body.AngularVelocity = Mirror(body.AngularVelocity);
            mirrored[i] = body;
        }
        return mirrored;
    }

    public static SupportProbe Mirrored(SupportProbe probe)
    {
        probe.Point = Mirror(probe.Point);
        probe.Normal = Mirror(probe.Normal);
        probe.PointVelocity = Mirror(probe.PointVelocity);
        return probe;
    }

    public static Float3 Mirror(Float3 value)
    {
        return new Float3(-value.X, value.Y, value.Z);
    }

    public static SA.Motion.Numerics.Quaternion MirrorRotation(SA.Motion.Numerics.Quaternion value)
    {
        return new SA.Motion.Numerics.Quaternion(value.X, -value.Y, -value.Z, value.W);
    }

    public static Role MirrorRole(Role role)
    {
        switch (role)
        {
            case Role.ThighLeft: return Role.ThighRight;
            case Role.ThighRight: return Role.ThighLeft;
            case Role.ShinLeft: return Role.ShinRight;
            case Role.ShinRight: return Role.ShinLeft;
            case Role.FootLeft: return Role.FootRight;
            case Role.FootRight: return Role.FootLeft;
            case Role.UpperArmLeft: return Role.UpperArmRight;
            case Role.UpperArmRight: return Role.UpperArmLeft;
            case Role.ForearmLeft: return Role.ForearmRight;
            case Role.ForearmRight: return Role.ForearmLeft;
            case Role.HandLeft: return Role.HandRight;
            case Role.HandRight: return Role.HandLeft;
            default: return role;
        }
    }

    static void Set(RoleBody[] bodies, Role role, Float3 position, Float3 velocity)
    {
        bodies[(int)role] = new RoleBody
        {
            Position = position,
            Rotation = SA.Motion.Numerics.Quaternion.Identity,
            LinearVelocity = velocity,
            AngularVelocity = Float3.Zero,
            InverseMass = 1f,
            InverseInertiaWorld = default
        };
    }

    static void Swap(RoleBody[] bodies, Role a, Role b)
    {
        var temporary = bodies[(int)a];
        bodies[(int)a] = bodies[(int)b];
        bodies[(int)b] = temporary;
    }
}
