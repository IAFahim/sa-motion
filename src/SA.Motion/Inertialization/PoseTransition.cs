using System;
using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.Motion.Inertialization
{
    [StructLayout(LayoutKind.Sequential)]
    public struct VectorOffset
    {
        public Float3 Direction;
        public ScalarDecay Decay;

        public static VectorOffset Create(Float3 current, Float3 previous, Float3 destination, float historyInterval, float seconds)
        {
            var delta = current - destination;
            var magnitude = delta.Length;
            if (!delta.IsFinite || !Scalar.IsFinite(magnitude) || magnitude <= Scalar.MagnitudeEpsilon)
                return default;
            var direction = delta / magnitude;
            var velocity = previous.IsFinite && Scalar.IsFinite(historyInterval) && historyInterval > Scalar.DurationEpsilon
                ? (magnitude - Float3.Dot(previous - destination, direction)) / historyInterval : 0f;
            return new VectorOffset { Direction = direction, Decay = ScalarDecay.Create(magnitude, velocity, seconds) };
        }

        public Float3 Apply(Float3 destination, float elapsed) => destination + Direction * Decay.Evaluate(elapsed).Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RotationOffset
    {
        public Float3 Axis;
        public ScalarDecay Decay;

        public static RotationOffset Create(Quaternion current, Quaternion previous, Quaternion destination, float historyInterval, float seconds)
        {
            if (!current.IsValidRotation || !destination.IsValidRotation)
                return default;
            destination = Quaternion.Normalize(destination);
            var inverse = Quaternion.InverseUnit(destination);
            var delta = Quaternion.Canonical(Quaternion.Multiply(Quaternion.Normalize(current), inverse));
            var length = delta.Vector.Length;
            if (length <= Scalar.MagnitudeEpsilon)
                return default;
            var axis = delta.Vector / length;
            var angle = 2f * (float)Math.Atan2(length, delta.W);
            var velocity = 0f;
            if (previous.IsValidRotation && Scalar.IsFinite(historyInterval) && historyInterval > Scalar.DurationEpsilon)
            {
                var old = Quaternion.Normalize(Quaternion.Multiply(Quaternion.Normalize(previous), inverse));
                var projection = Float3.Dot(old.Vector, axis);
                if (projection * projection + old.W * old.W > Scalar.MagnitudeEpsilon * Scalar.MagnitudeEpsilon)
                {
                    var previousAngle = 2f * (float)Math.Atan2(projection, old.W);
                    previousAngle += Scalar.TwoPi * (float)Math.Floor((angle - previousAngle + Scalar.Pi) / Scalar.TwoPi);
                    velocity = (angle - previousAngle) / historyInterval;
                }
            }
            return new RotationOffset { Axis = axis, Decay = ScalarDecay.Create(angle, velocity, seconds) };
        }

        public Quaternion Apply(Quaternion destination, float elapsed) =>
            Quaternion.Normalize(Quaternion.Multiply(Quaternion.AxisAngle(Axis, Decay.Evaluate(elapsed).Value), Quaternion.Normalize(destination)));
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PoseHistory
    {
        public Pose Current;
        public Pose Previous;
        public float Interval;
        public uint Count;

        public void Reset(Pose output)
        {
            Current = output;
            Previous = output;
            Interval = 0f;
            Count = 1;
        }

        public void Capture(Pose output, float actualDeltaTime)
        {
            if (Count == 0)
                Reset(output);
            else if (Scalar.IsFinite(actualDeltaTime) && actualDeltaTime > 0f)
            {
                Previous = Current;
                Current = output;
                if (!output.Scale.IsPositive || !Previous.Scale.IsPositive)
                    Previous.Scale = output.Scale;
                Interval = actualDeltaTime;
                Count = 2;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PoseTransition
    {
        public VectorOffset Position;
        public RotationOffset Rotation;
        public VectorOffset LogScale;

        public static PoseTransition Create(in PoseHistory history, in Pose destination, float seconds)
        {
            if (history.Count == 0)
                return default;
            var interval = history.Count > 1 ? history.Interval : 0f;
            var result = new PoseTransition
            {
                Position = VectorOffset.Create(history.Current.Position, history.Previous.Position, destination.Position, interval, seconds),
                Rotation = RotationOffset.Create(history.Current.Rotation, history.Previous.Rotation, destination.Rotation, interval, seconds)
            };
            if (history.Current.Scale.IsPositive && destination.Scale.IsPositive)
            {
                var current = Float3.Log(history.Current.Scale);
                var previous = history.Previous.Scale.IsPositive ? Float3.Log(history.Previous.Scale) : current;
                result.LogScale = VectorOffset.Create(current, previous, Float3.Log(destination.Scale), interval, seconds);
            }
            return result;
        }

        public static Pose Apply(ref PoseTransition transition, in Pose destination, float elapsed)
        {
            if (!destination.Scale.IsPositive)
                transition.LogScale = default;
            return new Pose
            {
                Position = transition.Position.Apply(destination.Position, elapsed),
                Rotation = transition.Rotation.Apply(destination.Rotation, elapsed),
                Scale = destination.Scale.IsPositive && transition.LogScale.Decay.Duration > 0f
                    ? Float3.Exp(transition.LogScale.Apply(Float3.Log(destination.Scale), elapsed)) : destination.Scale
            };
        }
    }
}
