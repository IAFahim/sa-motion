using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.ReactionControl.CatchFall
{
    [StructLayout(LayoutKind.Sequential)]
    public struct CatchFallArmState
    {
        public Float3 ContactVelocity;
        public float StiffnessScale;
        public float ClavLean1;
        public float ClavLean2;
        public float Elbow;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CatchFallState
    {
        public byte Active;
        public byte Touched;
        public Float3 Dir;
        public float Forward;
        public float Gain;
        public float Upright;
        public float LegLeft;
        public float LegRight;
        public float Lean2;
        public CatchFallArmState ArmLeft;
        public CatchFallArmState ArmRight;
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct CatchFallArmProbe
    {
        public readonly SupportProbe Probe;
        public readonly byte Touching;

        public CatchFallArmProbe(SupportProbe probe, byte touching)
        {
            Probe = probe;
            Touching = touching;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct CatchFallProbes
    {
        public readonly CatchFallArmProbe Left;
        public readonly CatchFallArmProbe Right;

        public CatchFallProbes(CatchFallArmProbe left, CatchFallArmProbe right)
        {
            Left = left;
            Right = right;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct CatchFallResult
    {
        public readonly Float3 HandTargetLeft;
        public readonly Float3 HandTargetRight;
        public readonly Float3 HeadTarget;
        public readonly float SpineTwist;
        public readonly Float3 SpinTorque;
        public readonly byte Touched;

        public CatchFallResult(Float3 handTargetLeft, Float3 handTargetRight, Float3 headTarget, float spineTwist, Float3 spinTorque, byte touched)
        {
            HandTargetLeft = handTargetLeft;
            HandTargetRight = handTargetRight;
            HeadTarget = headTarget;
            SpineTwist = spineTwist;
            SpinTorque = spinTorque;
            Touched = touched;
        }
    }
}
