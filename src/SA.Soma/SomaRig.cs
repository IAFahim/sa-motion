using System;
using SA.Motion.Numerics;

namespace SA.Soma
{
    public static class SomaRig
    {
        public const int ModelJointCount = 30;
        public const int SkeletonJointCount = 77;
        public const int SourceCount = 27;
        public const int Hips = 0;
        public const int Neck1 = 4;
        public const int Neck2 = 5;
        public const int Head = 6;
        public const int LeftEye = 8;
        public const int RightEye = 9;
        public const int LeftShoulder = 10;
        public const int LeftArm = 11;
        public const int LeftForeArm = 12;
        public const int LeftHand = 13;
        public const int LeftThumbEnd = 14;
        public const int LeftMiddleEnd = 15;
        public const int RightShoulder = 16;
        public const int RightArm = 17;
        public const int RightForeArm = 18;
        public const int RightHand = 19;
        public const int RightThumbEnd = 20;
        public const int RightMiddleEnd = 21;
        public const int LeftLeg = 22;
        public const int LeftFoot = 24;
        public const int RightLeg = 26;
        public const int RightFoot = 28;
        public const string SkeletonAsset = "motionbricks/assets/human/soma_base_skel_minimal_no_root.usd";
        public const string SkeletonSha256 = "4ab93d78a0f1c06d3bcbedee5ceebb5d9d5b002fd0a88638e9399ed80b61871d";

        static readonly string[] Names =
        {
            "Hips", "Spine1", "Spine2", "Chest", "Neck1", "Neck2", "Head", "Jaw", "LeftEye", "RightEye",
            "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand", "LeftHandThumbEnd", "LeftHandMiddleEnd",
            "RightShoulder", "RightArm", "RightForeArm", "RightHand", "RightHandThumbEnd", "RightHandMiddleEnd",
            "LeftLeg", "LeftShin", "LeftFoot", "LeftToeBase", "RightLeg", "RightShin", "RightFoot", "RightToeBase"
        };

        static readonly int[] ParentData = { -1, 0, 1, 2, 3, 4, 5, 6, 6, 6, 3, 10, 11, 12, -1, -1, 3, 16, 17, 18, -1, -1, 0, 22, 23, 24, 0, 26, 27, 28 };
        static readonly int[] SkeletonIndexData = { 0, 1, 2, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13, 14, 18, 28, 39, 40, 41, 42, 46, 56, 67, 68, 69, 70, 72, 73, 74, 75 };
        static readonly int[] SourceData = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, -1, -1, 15, 16, 17, 18, -1, -1, 19, 20, 21, 22, 23, 24, 25, 26 };
        static readonly int[] TargetData =
        {
            -2, 0, 1, 2, 3, 5, 6, 7, 8, 9, 10, 11, 12, 13,
            -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
            16, 17, 18, 19,
            -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
            22, 23, 24, 25, 26, 27, 28, 29
        };

        static readonly float[] RestTranslationData =
        {
            0.000000f, 1.012054f, 0.000000f,
            0.051311f, 0.000593f, 0f,
            0.072061f, 0.000000f, 0f,
            0.072620f, -0.000666f, 0.000000f,
            0.263508f, 0.000000f, 0f,
            0.078721f, 0f, 0f,
            0.066593f, 0.000000f, 0f,
            0.001112f, 0.030213f, 0f,
            0.042952f, 0.082240f, 0.031058f,
            0.042952f, 0.082240f, -0.031058f,
            0.233884f, 0.050382f, 0.016136f,
            0.157430f, 0.000000f, 0f,
            0.289160f, 0.000000f, 0f,
            0.270262f, 0.000000f, 0f,
            0.031806f, 0f, 0.000000f,
            0.023414f, 0.000000f, 0.000000f,
            0.233885f, 0.050382f, -0.016167f,
            -0.157431f, 0f, 0.000000f,
            -0.289151f, 0f, 0.000000f,
            -0.270262f, 0f, 0.000000f,
            -0.031806f, 0.000000f, 0.000000f,
            -0.023415f, 0f, 0.000000f,
            -0.083079f, 0.021259f, 0.100069f,
            0.433003f, 0f, 0f,
            0.424241f, 0.000000f, 0f,
            0.141328f, 0f, 0.000000f,
            -0.083079f, 0.021259f, -0.100015f,
            -0.433004f, 0.000000f, 0f,
            -0.424240f, 0f, 0f,
            -0.141328f, 0.000000f, 0f,
        };
        static readonly float[] BindPositionData =
        {
            0f, 0.998041f, -0.049450f,
            0f, 1.049355f, -0.049558f,
            0f, 1.121399f, -0.051135f,
            0.000000f, 1.193727f, -0.057672f,
            0f, 1.457233f, -0.058604f,
            0f, 1.532469f, -0.035442f,
            0f, 1.596114f, -0.015849f,
            0f, 1.601064f, 0.013978f,
            0.031058f, 1.649188f, 0.060252f,
            -0.031058f, 1.649188f, 0.060252f,
            0.016136f, 1.427788f, -0.008118f,
            0.161180f, 1.401453f, -0.063372f,
            0.342040f, 1.176171f, -0.051094f,
            0.530342f, 1.020415f, 0.064336f,
            0.537138f, 0.950515f, 0.188904f,
            0.614699f, 0.880705f, 0.151966f,
            -0.016167f, 1.427788f, -0.008118f,
            -0.161211f, 1.401451f, -0.063371f,
            -0.342071f, 1.176179f, -0.051092f,
            -0.530374f, 1.020425f, 0.064338f,
            -0.537171f, 0.950522f, 0.188905f,
            -0.614733f, 0.880714f, 0.151968f,
            0.100069f, 0.915260f, -0.027058f,
            0.130298f, 0.483996f, -0.002775f,
            0.156741f, 0.072029f, -0.100569f,
            0.182359f, 0.021553f, 0.028928f,
            -0.100015f, 0.915260f, -0.027058f,
            -0.130245f, 0.483996f, -0.002775f,
            -0.156688f, 0.072029f, -0.100569f,
            -0.182305f, 0.021553f, 0.028928f,
        };
        static readonly float[] BindRotationData =
        {
            0.496573f, 0.503404f, 0.496573f, 0.503404f,
            0.494499f, 0.505441f, 0.494499f, 0.505441f,
            0.479358f, 0.519823f, 0.479358f, 0.519823f,
            0.499115f, 0.500884f, 0.499115f, 0.500884f,
            0.568820f, 0.420052f, 0.568820f, 0.420052f,
            0.568820f, 0.420052f, 0.568820f, 0.420052f,
            0.467083f, 0.530880f, 0.467083f, 0.530880f,
            0.469716f, 0.528552f, 0.469716f, 0.528552f,
            0.533350f, 0.498629f, 0.499144f, 0.466650f,
            0.499144f, 0.466650f, 0.533350f, 0.498629f,
            0.704278f, 0.063204f, -0.188002f, 0.681655f,
            0.475748f, -0.248032f, -0.354606f, 0.765766f,
            0.409415f, -0.346750f, -0.177187f, 0.825075f,
            0.552918f, -0.454232f, -0.093735f, 0.692220f,
            0.666354f, -0.578321f, 0.308491f, 0.355457f,
            0.423901f, -0.565958f, -0.335988f, 0.622183f,
            -0.681655f, -0.188005f, -0.063194f, 0.704277f,
            0.765767f, 0.354595f, -0.248031f, -0.475757f,
            0.825072f, 0.177180f, -0.346749f, -0.409425f,
            0.692217f, 0.093743f, -0.454232f, -0.552921f,
            -0.355451f, 0.308494f, 0.578325f, 0.666353f,
            0.622189f, 0.335978f, -0.565957f, -0.423902f,
            0.535543f, -0.472471f, 0.491797f, -0.498098f,
            -0.461986f, 0.544613f, -0.415008f, 0.563678f,
            0.759832f, -0.140315f, 0.624243f, -0.115276f,
            0.764510f, -0.108779f, 0.629031f, -0.089502f,
            0.498098f, 0.491797f, 0.472471f, 0.535542f,
            0.563679f, 0.415009f, 0.544613f, 0.461986f,
            0.115276f, 0.624243f, 0.140315f, 0.759832f,
            0.089503f, 0.629031f, 0.108780f, 0.764510f,
        };

        public static string Name(int joint) => joint >= 0 && joint < ModelJointCount ? Names[joint] : string.Empty;
        public static int Parent(int joint) => joint >= 0 && joint < ModelJointCount ? ParentData[joint] : -1;
        public static int SkeletonIndex(int joint) => joint >= 0 && joint < ModelJointCount ? SkeletonIndexData[joint] : -1;
        public static int Source(int joint) => joint >= 0 && joint < ModelJointCount ? SourceData[joint] : -1;
        public static int TargetModel(int bone) => bone >= 0 && bone < TargetData.Length ? TargetData[bone] : -1;
        public static bool IsMarker(int joint) => joint == LeftThumbEnd || joint == LeftMiddleEnd || joint == RightThumbEnd || joint == RightMiddleEnd;

        public static int ModelOfSource(int source)
        {
            for (var i = 0; i < ModelJointCount; i++)
                if (SourceData[i] == source) return i;
            return -1;
        }

        public static Float3 RestTranslation(int joint) => Vector(RestTranslationData, joint);
        public static Float3 BindPosition(int joint) => Vector(BindPositionData, joint);

        public static Quaternion BindRotation(int joint) => joint >= 0 && joint < ModelJointCount
            ? Quaternion.Normalize(new Quaternion(BindRotationData[joint * 4], BindRotationData[joint * 4 + 1], BindRotationData[joint * 4 + 2], BindRotationData[joint * 4 + 3]))
            : Quaternion.Identity;

        public static Pose BindLocal(int joint)
        {
            if (joint < 0 || joint >= ModelJointCount) return Pose.Identity;
            var parent = ParentData[joint];
            var rotation = parent < 0 ? BindRotation(joint) : Quaternion.Normalize(Quaternion.Multiply(Quaternion.InverseUnit(BindRotation(parent)), BindRotation(joint)));
            return new Pose { Position = parent < 0 ? BindPosition(joint) : RestTranslation(joint), Rotation = rotation, Scale = Float3.One };
        }

        public static void SourceParents(Span<int> parents)
        {
            if (parents.Length < SourceCount) return;
            parents[0] = -1;
            for (var joint = 0; joint < ModelJointCount; joint++)
            {
                var source = SourceData[joint];
                if (source < 0) continue;
                var parent = ParentData[joint];
                parents[source] = parent < 0 ? 0 : SourceData[parent];
            }
        }

        static Float3 Vector(float[] data, int joint) => joint >= 0 && joint < ModelJointCount
            ? new Float3(data[joint * 3], data[joint * 3 + 1], data[joint * 3 + 2]) : Float3.Zero;
    }
}
