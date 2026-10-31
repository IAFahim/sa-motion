using System;
using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.Balance
{
    [StructLayout(LayoutKind.Sequential)]
    public struct BodyState
    {
        public Float3 Position;
        public Quaternion Rotation;
        public Float3 LinearVelocity;
        public Float3 AngularVelocity;
        public float InverseMass;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FootContact
    {
        public byte Hit;
        public Float3 Point;
        public Float3 Normal;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BodyDrive
    {
        public Quaternion TargetWorld;
        public float Stiffness;
        public float Damping;
        public float MaxTorque;

        public static BodyDrive Passive => new BodyDrive { TargetWorld = Quaternion.Identity };
    }

    public enum ControllerDecision : byte
    {
        Standing,
        Recovering,
        Stepping,
        Falling
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ControllerReport
    {
        public ControllerDecision Decision;
        public Float3 Com;
        public Float3 CapturePoint;
        public float Margin;
    }

    public interface IBalanceController
    {
        ControllerReport Tick(ReadOnlySpan<BodyState> bodies, ReadOnlySpan<FootContact> feet, ReadOnlySpan<Quaternion> targetWorld, float dt, Span<BodyDrive> drives);

        void Reset();
    }
}
