using System;
using SA.Motion.Numerics;

namespace SA.Animation
{
    public static class CanonicalHumanoid
    {
        public const int BoneCount = 56;
        public const int Hips = 1;

        public static AnimationError WriteReference(Span<int> parents, Span<Pose> local)
        {
            if (parents.Length < BoneCount || local.Length < BoneCount)
                return AnimationError.CapacityExceeded;
            for (var i = 0; i < BoneCount; i++)
            {
                var parent = Parent(i);
                parents[i] = parent;
                local[i] = new Pose { Position = Position(i) - (parent < 0 ? Float3.Zero : Position(parent)), Rotation = Quaternion.Identity, Scale = Float3.One };
            }
            return AnimationError.None;
        }

        static int Parent(int i)
        {
            if (i >= 14 && i <= 28)
                return (i - 14) % 3 == 0 ? 13 : i - 1;
            if (i >= 33 && i <= 47)
                return (i - 33) % 3 == 0 ? 32 : i - 1;
            return i switch
            {
                0 => -1, 1 => 0, 2 => 1, 3 => 2, 4 => 3, 5 => 4, 6 => 5,
                7 => 6, 8 => 6, 9 => 6, 10 => 4, 11 => 10, 12 => 11, 13 => 12,
                29 => 4, 30 => 29, 31 => 30, 32 => 31,
                48 => 1, 49 => 48, 50 => 49, 51 => 50, 52 => 1, 53 => 52, 54 => 53, 55 => 54, _ => -1
            };
        }

        static Float3 Position(int i)
        {
            if ((i >= 14 && i <= 28) || (i >= 33 && i <= 47))
            {
                var left = i < 29;
                var offset = i - (left ? 14 : 33);
                var finger = offset / 3;
                var joint = offset % 3;
                var distance = finger == 0 ? (joint == 0 ? 0.035f : joint == 1 ? 0.060f : 0.080f)
                    : (joint == 0 ? 0.045f : joint == 1 ? 0.075f : 0.095f);
                var z = finger switch { 0 => 0.055f, 1 => 0.025f, 2 => 0f, 3 => -0.025f, _ => -0.045f };
                return new Float3((left ? -1f : 1f) * (0.75f + distance), 1.46f, z);
            }
            return i switch
            {
                1 => new Float3(0f, 1f, 0f), 2 => new Float3(0f, 1.12f, 0f), 3 => new Float3(0f, 1.32f, 0f),
                4 => new Float3(0f, 1.46f, 0f), 5 => new Float3(0f, 1.56f, 0f), 6 => new Float3(0f, 1.68f, 0f),
                7 => new Float3(0f, 1.63f, 0.075f), 8 => new Float3(-0.032f, 1.72f, 0.075f), 9 => new Float3(0.032f, 1.72f, 0.075f),
                10 => new Float3(-0.10f, 1.46f, 0f), 11 => new Float3(-0.20f, 1.46f, 0f), 12 => new Float3(-0.50f, 1.46f, 0f), 13 => new Float3(-0.75f, 1.46f, 0f),
                29 => new Float3(0.10f, 1.46f, 0f), 30 => new Float3(0.20f, 1.46f, 0f), 31 => new Float3(0.50f, 1.46f, 0f), 32 => new Float3(0.75f, 1.46f, 0f),
                48 => new Float3(-0.095f, 0.95f, 0f), 49 => new Float3(-0.095f, 0.53f, 0.015f), 50 => new Float3(-0.095f, 0.10f, 0f), 51 => new Float3(-0.095f, 0.035f, 0.16f),
                52 => new Float3(0.095f, 0.95f, 0f), 53 => new Float3(0.095f, 0.53f, 0.015f), 54 => new Float3(0.095f, 0.10f, 0f), 55 => new Float3(0.095f, 0.035f, 0.16f), _ => Float3.Zero
            };
        }
    }
}
