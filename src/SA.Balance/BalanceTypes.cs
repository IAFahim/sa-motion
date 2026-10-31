using System;
using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.Balance
{
    public enum Decision : byte
    {
        Standing = 0,
        Recovering = 1,
        Stepping = 2,
        Falling = 3
    }

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
        public bool Hit;
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

        public static BodyDrive Passive
        {
            get
            {
                var drive = new BodyDrive();
                drive.TargetWorld = Quaternion.Identity;
                drive.Stiffness = 0f;
                drive.Damping = 0f;
                drive.MaxTorque = 0f;
                return drive;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct BalanceInput
    {
        public readonly Quaternion[]? TargetWorld;
        public readonly float Timestep;

        public BalanceInput(Quaternion[]? targetWorld, float timestep)
        {
            TargetWorld = targetWorld;
            Timestep = timestep;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BalanceState
    {
        public int Ticks;
        public bool HasStart;
        public Float3 StartPelvis;
        public bool FallingLatched;
        public int SwingFoot;
        public float SwingTime;
        public Float3 SwingFrom;
        public Float3 SwingTo;
        public Float3 LastCapture;
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct BalanceReport
    {
        public readonly Decision Decision;
        public readonly Float3 Com;
        public readonly Float3 CapturePoint;
        public readonly float Margin;
        public readonly int SteppingFoot;

        public BalanceReport(Decision decision, Float3 com, Float3 capturePoint, float margin, int steppingFoot)
        {
            Decision = decision;
            Com = com;
            CapturePoint = capturePoint;
            Margin = margin;
            SteppingFoot = steppingFoot;
        }
    }
}
