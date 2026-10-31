using SA.Humanoid;

namespace SA.Balance.Arms
{
    public static class ArmAssistParams
    {
        public static BalanceParams Standard
        {
            get
            {
                BalanceParams inner = BalanceParams.Standard;
                inner.FallSpeed = 3.1f;
                return inner;
            }
        }

        public static float ArmPitchGain => 0.12f;

        public static float ArmRollGain => 0.12f;

        public static float ArmMax => 0.9f;

        public static float ForearmShare => 0.65f;

        public static float HandShare => 0.4f;

        public static float ArmStiffnessBoost => 2f;

        public static float TorsoRateGain => 0.05f;

        public static float TorsoRateMax => 0.15f;

        public static float CushionRaise => 1f;

        public static float CushionSpread => 0.35f;

        public static float CushionElbow => 1.2f;

        public static readonly Role[] ArmRoles =
        {
            Role.UpperArmLeft,
            Role.UpperArmRight,
            Role.ForearmLeft,
            Role.ForearmRight,
            Role.HandLeft,
            Role.HandRight,
        };
    }
}
