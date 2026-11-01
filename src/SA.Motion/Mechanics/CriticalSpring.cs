using System;
using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.Motion.Mechanics
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SpringState
    {
        public float Value;
        public float Velocity;
    }

    public static class CriticalSpring
    {
        const float Ln2 = 0.6931472f;

        public static SpringState Step(SpringState state, float target, float halfLife, float deltaTime)
        {
            var value = Scalar.Finite(state.Value);
            var velocity = Scalar.Finite(state.Velocity);
            var goal = Scalar.IsFinite(target) ? target : value;
            if (!Scalar.IsFinite(deltaTime) || deltaTime <= 0f)
                return new SpringState { Value = value, Velocity = velocity };
            if (!Scalar.IsFinite(halfLife) || halfLife <= Scalar.DurationEpsilon)
                return new SpringState { Value = goal, Velocity = 0f };
            if (value == goal && velocity == 0f)
                return new SpringState { Value = goal, Velocity = 0f };
            var rate = 2f * Ln2 / halfLife;
            var offset = value - goal;
            var momentum = velocity + offset * rate;
            var decay = (float)Math.Exp(-rate * deltaTime);
            var next = new SpringState
            {
                Value = decay * (offset + momentum * deltaTime) + goal,
                Velocity = decay * (velocity - momentum * rate * deltaTime)
            };
            return Scalar.IsFinite(next.Value) && Scalar.IsFinite(next.Velocity) ? next : new SpringState { Value = goal, Velocity = 0f };
        }
    }
}
