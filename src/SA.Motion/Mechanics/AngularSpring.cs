using System;
using SA.Motion.Numerics;

namespace SA.Motion.Mechanics
{
    public static class AngularSpring
    {
        public static Float3 Acceleration(Float3 error, Float3 relativeAngularVelocity, float frequencyHz, float dampingRatio, float timestep)
        {
            if (!error.IsFinite || !relativeAngularVelocity.IsFinite)
                return Float3.Zero;
            if (!Scalar.IsFinite(frequencyHz) || !Scalar.IsFinite(dampingRatio) || !Scalar.IsFinite(timestep))
                return Float3.Zero;
            if (frequencyHz <= 0f || dampingRatio < 0f || timestep < 0f)
                return Float3.Zero;
            var omega = 2f * Scalar.Pi * frequencyHz;
            var stiffness = omega * omega;
            var damping = 2f * dampingRatio * omega;
            var denominator = 1f + timestep * damping + timestep * timestep * stiffness;
            var result = (error * stiffness - relativeAngularVelocity * (damping + timestep * stiffness)) / denominator;
            return result.IsFinite ? result : Float3.Zero;
        }
    }
}
