using SA.Motion.Numerics;
using global::Unity.Mathematics;
using MotionQuaternion = SA.Motion.Numerics.Quaternion;

namespace SA.Motion.Unity
{
    public static class Conversions
    {
        public static Float3 ToMotion(float3 value) => new Float3(value.x, value.y, value.z);
        public static float3 ToUnity(Float3 value) => new float3(value.X, value.Y, value.Z);
        public static MotionQuaternion ToMotion(quaternion value) => new MotionQuaternion(value.value.x, value.value.y, value.value.z, value.value.w);
        public static quaternion ToUnity(MotionQuaternion value) => new quaternion(value.X, value.Y, value.Z, value.W);
    }
}
