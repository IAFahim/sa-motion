using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.ReactionControl
{
    [StructLayout(LayoutKind.Sequential)]
    public struct BodyBalanceState
    {
        public float ArmsOutTimer;
        public float BendTimer;
        public float Cut;
        public uint Stepping;
        public float LookTime;
        public float LookRand;
        public float TurnRand;
        public float TurnTime;
        public byte LookVel;
        public Float3 PrevEuler;
        public Float3 FrameX;
        public Float3 FrameY;
        public Float3 FrameZ;
        public uint Rng;
        public BalanceDecision Decision;
        public float HoldTimer;
        public float LookVelProb;

        public static BodyBalanceState Activate(in BodyBalanceParams p, Quaternion torsoRotation, Quaternion pelvisRotation)
        {
            Quaternion baseRotation = p.UsePelvisFrame != 0 ? pelvisRotation : torsoRotation;
            if (!baseRotation.IsValidRotation)
            {
                baseRotation = Quaternion.Identity;
            }
            BodyBalance.OwnFrame(baseRotation, out Float3 x, out Float3 y, out Float3 z);
            BodyBalanceState s = new BodyBalanceState
            {
                ArmsOutTimer = 1f,
                BendTimer = 0f,
                Cut = 0f,
                Stepping = 0u,
                LookTime = 0f,
                LookRand = 0f,
                TurnRand = 0f,
                TurnTime = 0f,
                LookVel = 0,
                PrevEuler = BodyBalance.LeanAngles(x, y, z),
                FrameX = x,
                FrameY = y,
                FrameZ = z,
                Rng = 0x9E3779B9u,
                Decision = BalanceDecision.Recovering,
                HoldTimer = 0f,
                LookVelProb = -1f,
            };
            s.LookRand = BodyBalance.Unit01(ref s.Rng);
            s.TurnRand = BodyBalance.Unit01(ref s.Rng);
            return s;
        }
    }
}
