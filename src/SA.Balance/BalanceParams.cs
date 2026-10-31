using System.Runtime.InteropServices;

namespace SA.Balance
{
    [StructLayout(LayoutKind.Sequential)]
    public struct BalanceParams
    {
        public float Gravity;
        public float ComHeightMin;
        public float ComHeightMax;
        public float PelvisStiffness;
        public float TorsoStiffness;
        public float HeadStiffness;
        public float ArmStiffness;
        public float ForearmStiffness;
        public float HandStiffness;
        public float ThighStiffness;
        public float ShinStiffness;
        public float FootStiffness;
        public float PelvisDamping;
        public float TorsoDamping;
        public float HeadDamping;
        public float ArmDamping;
        public float ForearmDamping;
        public float HandDamping;
        public float ThighDamping;
        public float ShinDamping;
        public float FootDamping;
        public float TorqueScale;
        public float SupportRadius;
        public float HeightGain;
        public float HeightMax;
        public float UprightGain;
        public float UprightDamp;
        public float UprightMax;
        public float AnkleGain;
        public float AnkleVelGain;
        public float HipGain;
        public float HipVelGain;
        public float HipRollGain;
        public float HipRollVelGain;
        public float LeanMax;
        public float StepMargin;
        public float StepHeight;
        public float StepTime;
        public float StepAnkleHeight;
        public float StepThighLength;
        public float StepShinLength;
        public float StepLead;
        public float MaxReach;
        public float FallReach;
        public float StanceBoost;
        public float FallToneStiffness;
        public float FallToneDamping;
        public float FallToneTorque;

        public static BalanceParams Standard
        {
            get
            {
                var p = new BalanceParams();
                p.Gravity = 9.81f;
                p.ComHeightMin = 0.5f;
                p.ComHeightMax = 1.4f;
                p.PelvisStiffness = 2000f;
                p.TorsoStiffness = 200f;
                p.HeadStiffness = 80f;
                p.ArmStiffness = 30f;
                p.ForearmStiffness = 15f;
                p.HandStiffness = 10f;
                p.ThighStiffness = 1000f;
                p.ShinStiffness = 900f;
                p.FootStiffness = 700f;
                p.PelvisDamping = 15.54f;
                p.TorsoDamping = 4.64f;
                p.HeadDamping = 2.11f;
                p.ArmDamping = 1.01f;
                p.ForearmDamping = 0.41f;
                p.HandDamping = 0.13f;
                p.ThighDamping = 9.49f;
                p.ShinDamping = 7.98f;
                p.FootDamping = 2.57f;
                p.TorqueScale = 0.5f;
                p.SupportRadius = 0.2f;
                p.HeightGain = 0f;
                p.HeightMax = 0.2f;
                p.UprightGain = 0.3f;
                p.UprightDamp = 0.15f;
                p.UprightMax = 0.3f;
                p.AnkleGain = 2f;
                p.AnkleVelGain = 0f;
                p.HipGain = 0.3f;
                p.HipVelGain = 0.05f;
                p.HipRollGain = 1f;
                p.HipRollVelGain = 0.2f;
                p.LeanMax = 0.3f;
                p.StepMargin = 0.1f;
                p.StepHeight = 0.1f;
                p.StepTime = 0.22f;
                p.StepAnkleHeight = 0.1f;
                p.StepThighLength = 0.43f;
                p.StepShinLength = 0.43f;
                p.StepLead = 0.5f;
                p.MaxReach = 0.6f;
                p.FallReach = 1.5f;
                p.StanceBoost = 1.5f;
                p.FallToneStiffness = 8f;
                p.FallToneDamping = 0.8f;
                p.FallToneTorque = 20f;
                return p;
            }
        }
    }
}
