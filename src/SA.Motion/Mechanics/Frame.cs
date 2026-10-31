using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.Motion.Mechanics
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Frame
    {
        public Float3 Position;
        public Quaternion Rotation;

        public Frame(Float3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public static Frame Identity => new Frame(Float3.Zero, Quaternion.Identity);
    }

    public enum Axis : byte
    {
        X,
        Y,
        Z
    }
}
