using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.Humanoid.Hinge
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct HingeEstimate
    {
        public HingeEstimate(Float3 axis, float min, float max, float fit, bool valid)
        {
            Axis = axis;
            Min = min;
            Max = max;
            Fit = fit;
            Valid = valid;
        }

        public Float3 Axis { get; }
        public float Min { get; }
        public float Max { get; }
        public float Fit { get; }
        public bool Valid { get; }
    }
}
