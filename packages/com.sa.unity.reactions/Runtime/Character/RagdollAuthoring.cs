using UnityEngine;

namespace SA.Reactions
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RigAuthoring))]
    public class RagdollAuthoring : MonoBehaviour
    {
        public float TotalMass = 70f;
        public float GainScale = 1f;
        public int JointMode;
        public bool BalanceController;
        public float LimpTone = 0.05f;
        public float MaxStrength = 1f;
        public float RecoverSeconds = 1.2f;
        public float LimpSeconds = 4f;
        public float BlendSeconds = 0.7f;
        public float LimpImpulse = 260f;
        public float StrengthLossPerImpulse = 0.004f;
        public AnimationClip Flinch;
        [Range(0f, 1f)] public float FlinchWeight = 0.8f;
        [Tooltip("Optional bone overrides. Avatar and bone-name discovery are used for empty entries.")]
        public Transform[] Overrides = new Transform[15];

        void Reset()
        {
            var rig = GetComponent<RigAuthoring>();
            rig.Engine = Engine.Cpu;
            rig.Inertialize = true;
        }
    }
}
