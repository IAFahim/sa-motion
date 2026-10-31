using System;
using SA.Humanoid;
using SA.Motion.Numerics;
using Xunit;

namespace SA.ReactionControl.Tests.Totality;

static class Fuzz
{
    public const int Combos = 20000;

    public static readonly float[] Palette = BuildPalette();

    static float[] BuildPalette()
    {
        var random = new Random(12345);
        var palette = new float[24];
        palette[0] = 0f;
        palette[1] = -0f;
        palette[2] = float.NaN;
        palette[3] = float.PositiveInfinity;
        palette[4] = float.NegativeInfinity;
        palette[5] = float.Epsilon;
        palette[6] = float.MaxValue;
        palette[7] = -float.MaxValue;
        palette[8] = 1e-30f;
        palette[9] = -1e-30f;
        palette[10] = 1e30f;
        palette[11] = -1e30f;
        for (var k = 0; k < 12; k++)
        {
            palette[12 + k] = (float)(random.NextDouble() * 20.0 - 10.0);
        }
        return palette;
    }

    public static float Pick(int i)
    {
        var n = Palette.Length;
        var m = i % n;
        if (m < 0)
        {
            m += n;
        }
        return Palette[m];
    }

    public static Float3 Vec(int a)
    {
        return new Float3(Pick(a), Pick(a + 1), Pick(a + 2));
    }

    public static Quaternion Quat(int a, int tag)
    {
        switch (tag & 7)
        {
            case 0:
                return Quaternion.Identity;
            case 1:
                return default;
            case 2:
                return new Quaternion(0f, 0f, 0f, 0f);
            case 3:
                return Quaternion.AxisAngle(new Float3(0f, 1f, 0f), Pick(a));
            default:
                return new Quaternion(Pick(a), Pick(a + 1), Pick(a + 2), Pick(a + 3));
        }
    }

    public static RoleBody Body(int a, int tag)
    {
        return new RoleBody
        {
            Position = Vec(a),
            Rotation = Quat(a + 3, tag),
            LinearVelocity = Vec(a + 7),
            AngularVelocity = Vec(a + 10),
            InverseMass = Pick(a + 13),
            InverseInertiaWorld = default,
        };
    }

    public static RoleBody[] Bodies(int seed, int count)
    {
        var bodies = new RoleBody[count];
        for (var i = 0; i < count; i++)
        {
            bodies[i] = Body(seed + i * 17, seed + i);
        }
        return bodies;
    }

    public static SupportProbe Probe(int a, int tag)
    {
        return new SupportProbe
        {
            Hit = (byte)((a + tag) & 1),
            Distance = Pick(a),
            Point = Vec(a + 1),
            Normal = Vec(a + 4),
            PointVelocity = Vec(a + 7),
        };
    }

    public static void Drive(JointDrive d)
    {
        Assert.True(float.IsFinite(d.Target.X));
        Assert.True(float.IsFinite(d.Target.Y));
        Assert.True(float.IsFinite(d.Target.Z));
        Assert.True(float.IsFinite(d.Target.W));
        Assert.True(float.IsFinite(d.TargetAngularVelocity.X));
        Assert.True(float.IsFinite(d.TargetAngularVelocity.Y));
        Assert.True(float.IsFinite(d.TargetAngularVelocity.Z));
        Assert.True(float.IsFinite(d.Stiffness) && d.Stiffness >= 0f);
        Assert.True(float.IsFinite(d.Damping) && d.Damping >= 0f);
        Assert.True(float.IsFinite(d.MaxTorque) && d.MaxTorque >= 0f);
    }

    public static void Drives(RoleBody[] bodies, JointDrive[] drives)
    {
        Assert.Equal(bodies.Length, drives.Length);
        for (var i = 0; i < drives.Length; i++)
        {
            Drive(drives[i]);
        }
    }

    public static void Value(float v)
    {
        Assert.True(float.IsFinite(v));
    }

    public static void Value(Float3 v)
    {
        Assert.True(float.IsFinite(v.X));
        Assert.True(float.IsFinite(v.Y));
        Assert.True(float.IsFinite(v.Z));
    }
}
