using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.Motion.Tuning
{
    public enum ParamUnit : byte
    {
        Ratio,
        Meters,
        MetersPerSecond,
        Radians,
        Seconds
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct ParamBounds
    {
        public readonly ParamUnit Unit;
        public readonly float Minimum;
        public readonly float Maximum;

        public ParamBounds(ParamUnit unit, float minimum, float maximum)
        {
            Unit = unit;
            Minimum = minimum;
            Maximum = maximum;
        }

        public bool ScalesWithBody => Unit == ParamUnit.Meters || Unit == ParamUnit.MetersPerSecond;

        public float Clamp(float value, float fallback)
        {
            if (!Scalar.IsFinite(value))
                return fallback;
            if (value < Minimum)
                return Minimum;
            if (value > Maximum)
                return Maximum;
            return value;
        }
    }

    public readonly struct ParamText
    {
        public readonly string Name;
        public readonly string Group;
        public readonly string Description;

        public ParamText(string name, string group, string description)
        {
            Name = name;
            Group = group;
            Description = description;
        }
    }
}
