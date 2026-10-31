using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.Motion.Kinematics
{
    [StructLayout(LayoutKind.Sequential)]
    public struct FootLockState
    {
        public bool Locked;
        public Float3 Position;
        public float Weight;
    }

    public static class FootLock
    {
        public static FootLockState Step(in FootLockState previous, Float3 animated, Float3 ground, bool hit, float height, float verticalSpeed, float plantHeight, float liftHeight, float maxDistance, float deltaTime = 0f, float blendSeconds = 0f)
        {
            if (!animated.IsFinite || !ground.IsFinite || !Scalar.IsFinite(height) || !Scalar.IsFinite(verticalSpeed))
                return default;
            plantHeight = Scalar.Max(0f, Scalar.Finite(plantHeight));
            liftHeight = Scalar.Max(plantHeight, Scalar.Finite(liftHeight));
            maxDistance = Scalar.Max(0.01f, Scalar.Finite(maxDistance));
            var next = default(FootLockState);
            if (hit && previous.Locked && previous.Position.IsFinite && height < liftHeight && (previous.Position - animated).Length <= maxDistance)
                next = previous;
            else if (hit && height <= plantHeight && verticalSpeed <= 0.05f)
                next = new FootLockState { Locked = true, Position = ground };
            var target = next.Locked ? 1f : 0f;
            var step = blendSeconds > Scalar.DurationEpsilon ? Scalar.Max(0f, Scalar.Finite(deltaTime)) / blendSeconds : 1f;
            var weight = Scalar.Max(0f, Scalar.Min(1f, Scalar.Finite(previous.Weight)));
            next.Weight = weight < target ? Scalar.Min(target, weight + step) : Scalar.Max(target, weight - step);
            if (!next.Locked && next.Weight > 0f && previous.Position.IsFinite)
                next.Position = previous.Position;
            return next;
        }
    }
}
