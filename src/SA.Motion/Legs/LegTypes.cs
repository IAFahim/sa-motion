using System.Runtime.InteropServices;
using SA.Motion.Inertialization;
using SA.Motion.Mechanics;
using SA.Motion.Numerics;

namespace SA.Motion.Legs
{
    [StructLayout(LayoutKind.Sequential)]
    public struct FootShape
    {
        public Float3 Heel;
        public Float3 Ball;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GroundHit
    {
        public byte Hit;
        public Float3 Point;
        public Float3 Normal;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LegInput
    {
        public Float3 Hip;
        public Float3 Knee;
        public Float3 Ankle;
        public Quaternion Upper;
        public Quaternion Lower;
        public Quaternion Foot;
        public GroundHit Ground;
        public byte ContactAuthored;
        public float Contact;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LegState
    {
        public byte Planted;
        public byte Changed;
        public byte HasPrevious;
        public float PreviousClearance;
        public SpringState Terrain;
        public Float3 Anchor;
        public Float3 AnchorNormal;
        public PoseHistory Output;
        public PoseTransition Blend;
        public float BlendElapsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LegPose
    {
        public Quaternion Upper;
        public Quaternion Lower;
        public Quaternion Foot;
        public Float3 Ankle;
        public Float3 Contact;
        public float Stretch;
        public byte Planted;
        public byte Solved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct LegReach
    {
        public readonly bool Valid;
        public readonly Float3 Knee;
        public readonly Float3 Ankle;
        public readonly Quaternion UpperDelta;
        public readonly Quaternion LowerDelta;
        public readonly float Stretch;

        public LegReach(bool valid, Float3 knee, Float3 ankle, Quaternion upperDelta, Quaternion lowerDelta, float stretch)
        {
            Valid = valid;
            Knee = knee;
            Ankle = ankle;
            UpperDelta = upperDelta;
            LowerDelta = lowerDelta;
            Stretch = stretch;
        }
    }
}
