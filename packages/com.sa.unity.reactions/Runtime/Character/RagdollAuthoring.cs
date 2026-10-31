using UnityEngine;

namespace SA.Reactions
{
    [DisallowMultipleComponent]
    public class RagdollAuthoring : MonoBehaviour
    {
        public float TotalMass = 70f;
        public float GainScale = 1f;
        public int JointMode = 1;
        public float LimpTone = 0.05f;
        public float MaxStrength = 1f;
        public float RecoverSeconds = 1.2f;
        public float LimpSeconds = 4f;
        public float LimpImpulse = 260f;
        public float StrengthLossPerImpulse = 0.004f;
        public Transform[] Overrides = new Transform[15];
    }
}
