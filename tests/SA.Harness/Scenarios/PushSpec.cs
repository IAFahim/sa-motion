using System.Runtime.InteropServices;
using SA.Humanoid;
using SA.Motion.Numerics;

namespace SA.Harness
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct PushSpec
    {
        public readonly Role Role;
        public readonly Float3 Direction;
        public readonly float Impulse;
        public readonly float AtSeconds;

        public PushSpec(Role role, Float3 direction, float impulse, float atSeconds)
        {
            Role = role;
            Direction = direction;
            Impulse = impulse;
            AtSeconds = atSeconds;
        }
    }
}
