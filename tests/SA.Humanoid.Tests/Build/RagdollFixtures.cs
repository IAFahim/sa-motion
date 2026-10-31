using SA.Humanoid;
using SA.Humanoid.Build;
using SA.Humanoid.Mapping;
using SA.Motion.Numerics;
using Xunit;

namespace SA.Humanoid.Tests.Build;

static class RagdollFixtures
{
    internal static string[] Names() => new string[]
    {
        "Hips", "Spine", "Neck", "Head",
        "ShoulderL", "UpperArmL", "ForearmL", "HandL",
        "ShoulderR", "UpperArmR", "ForearmR", "HandR",
        "ThighL", "ShinL", "FootL", "ToeL",
        "ThighR", "ShinR", "FootR", "ToeR"
    };

    internal static int[] Parents() => new int[]
    {
        -1, 0, 1, 2,
        1, 4, 5, 6,
        1, 8, 9, 10,
        0, 12, 13, 14,
        0, 16, 17, 18
    };

    internal static Float3[] Worlds() => new Float3[]
    {
        new Float3(0f, 0.95f, 0f), new Float3(0f, 1.20f, 0f), new Float3(0f, 1.55f, 0f), new Float3(0f, 1.65f, 0f),
        new Float3(-0.18f, 1.50f, 0f), new Float3(-0.24f, 1.50f, 0f), new Float3(-0.50f, 1.48f, 0f), new Float3(-0.72f, 1.46f, 0f),
        new Float3(0.18f, 1.50f, 0f), new Float3(0.24f, 1.50f, 0f), new Float3(0.50f, 1.48f, 0f), new Float3(0.72f, 1.46f, 0f),
        new Float3(-0.10f, 0.90f, 0f), new Float3(-0.11f, 0.48f, 0f), new Float3(-0.11f, 0.08f, 0f), new Float3(-0.11f, 0.02f, 0.14f),
        new Float3(0.10f, 0.90f, 0f), new Float3(0.11f, 0.48f, 0f), new Float3(0.11f, 0.08f, 0f), new Float3(0.11f, 0.02f, 0.14f)
    };

    internal static int[] Map() => new int[] { 0, 1, 3, 5, 9, 6, 10, 7, 11, 12, 16, 13, 17, 14, 18 };

    internal static SkeletonBone[] Bones() => FromWorlds(Names(), Parents(), Worlds());

    internal static SkeletonBone[] FromWorlds(string[] names, int[] parents, Float3[] worlds)
    {
        SkeletonBone[] bones = new SkeletonBone[names.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            Float3 local = worlds[i];
            if (parents[i] >= 0)
            {
                local = worlds[i] - worlds[parents[i]];
            }
            bones[i] = new SkeletonBone(names[i], parents[i], new Pose { Position = local, Rotation = Quaternion.Identity, Scale = Float3.One });
        }
        return bones;
    }

    internal static Quaternion TestRotation() => Quaternion.Normalize(Quaternion.Multiply(
        Quaternion.AxisAngle(new Float3(0f, 0f, 1f), 1.5707964f),
        Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.5235988f)));

    internal static SkeletonBone[] Rotated(SkeletonBone[] source, Quaternion rotation, Float3 offset)
    {
        Quaternion unit = Quaternion.Normalize(rotation);
        SkeletonBone[] bones = new SkeletonBone[source.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            Pose rest = source[i].Rest;
            Float3 local = Quaternion.Rotate(unit, rest.Position);
            if (source[i].Parent < 0)
            {
                local = local + offset;
            }
            bones[i] = new SkeletonBone(source[i].Name, source[i].Parent, new Pose { Position = local, Rotation = rest.Rotation, Scale = rest.Scale });
        }
        return bones;
    }

    internal static SkeletonBone[] Mirrored(SkeletonBone[] source)
    {
        SkeletonBone[] bones = new SkeletonBone[source.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            Pose rest = source[i].Rest;
            Float3 local = new Float3(-rest.Position.X, rest.Position.Y, rest.Position.Z);
            bones[i] = new SkeletonBone(source[i].Name, source[i].Parent, new Pose { Position = local, Rotation = rest.Rotation, Scale = rest.Scale });
        }
        return bones;
    }

    internal static void Near(float expected, float actual, float tolerance) => Assert.InRange(actual, expected - tolerance, expected + tolerance);

    internal static void Near3(Float3 expected, Float3 actual, float tolerance)
    {
        Near(expected.X, actual.X, tolerance);
        Near(expected.Y, actual.Y, tolerance);
        Near(expected.Z, actual.Z, tolerance);
    }

    internal static void Near4(Quaternion expected, Quaternion actual, float tolerance)
    {
        Near(expected.X, actual.X, tolerance);
        Near(expected.Y, actual.Y, tolerance);
        Near(expected.Z, actual.Z, tolerance);
        Near(expected.W, actual.W, tolerance);
    }

    internal static Float3 TransformPoint(BodyLayout body, Float3 point) => body.WorldFromBody.Position + Quaternion.Rotate(body.WorldFromBody.Rotation, point);
    internal static float KyleSegmentLength(int role)
    {
        Float3[] worlds = Worlds();
        int[] map = Map();
        int primary = PrimaryRole(role);
        if (primary >= 0)
        {
            return (worlds[map[primary]] - worlds[map[role]]).Length;
        }
        int parent = (int)Roles.Parent((Role)role);
        return (worlds[map[role]] - worlds[map[parent]]).Length;
    }


    internal static int FiniteViolations(RagdollLayout layout)
    {
        int bad = 0;
        for (int i = 0; i < layout.Bodies.Length; i++)
        {
            BodyLayout body = layout.Bodies[i];
            if (!body.WorldFromBody.Position.IsFinite) bad++;
            if (!body.WorldFromBody.Rotation.IsValidRotation) bad++;
            if (!body.CapsuleStart.IsFinite) bad++;
            if (!body.CapsuleEnd.IsFinite) bad++;
            if (!Scalar.IsFinite(body.Radius)) bad++;
            if (!body.BoxHalfExtents.IsFinite) bad++;
            if (!Scalar.IsFinite(body.Mass)) bad++;
            if (!body.CenterOfMass.IsFinite) bad++;
        }
        for (int i = 0; i < layout.Joints.Length; i++)
        {
            JointLayout joint = layout.Joints[i];
            if (!joint.AnchorInParent.IsFinite) bad++;
            if (!joint.AnchorInChild.IsFinite) bad++;
            if (!joint.JointFrameInParent.IsValidRotation) bad++;
            if (!joint.JointFrameInChild.IsValidRotation) bad++;
            if (!joint.RestRelative.IsValidRotation) bad++;
            if (!Scalar.IsFinite(joint.Limit.TwistMin)) bad++;
            if (!Scalar.IsFinite(joint.Limit.TwistMax)) bad++;
            if (!Scalar.IsFinite(joint.Limit.Swing1Max)) bad++;
            if (!Scalar.IsFinite(joint.Limit.Swing2Max)) bad++;
        }
        return bad;
    }

    internal static bool Contains(RagdollLayout layout, DiagnosticCode code, Role role)
    {
        for (int i = 0; i < layout.Diagnostics.Length; i++)
        {
            if (layout.Diagnostics[i].Code == code && layout.Diagnostics[i].Role == role)
            {
                return true;
            }
        }
        return false;
    }

    internal static int PrimaryRole(int role)
    {
        switch (role)
        {
            case 0: return 1;
            case 1: return 2;
            case 3: return 5;
            case 4: return 6;
            case 5: return 7;
            case 6: return 8;
            case 9: return 11;
            case 10: return 12;
            case 11: return 13;
            case 12: return 14;
            default: return -1;
        }
    }

    internal static float PointSegmentDistance(Float3 point, Float3 start, Float3 end)
    {
        Float3 delta = end - start;
        float denom = Float3.Dot(delta, delta);
        if (denom <= 0f)
        {
            return (point - start).Length;
        }
        float t = Float3.Dot(point - start, delta) / denom;
        if (t < 0f)
        {
            t = 0f;
        }
        if (t > 1f)
        {
            t = 1f;
        }
        return (point - (start + delta * t)).Length;
    }
}
