using System;
using System.Collections.Generic;
using UnityEngine;

namespace SA.IK
{
    public enum Limb { LeftArm, RightArm, LeftLeg, RightLeg }

    [Serializable]
    public class LimbTarget
    {
        public Limb Limb;
        public Transform Target;
        public Transform Pole;
        [Range(0f, 1f)] public float Weight = 1f;
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(RigAuthoring))]
    public class IKAuthoring : MonoBehaviour
    {
        public Transform HeadTarget;
        [Range(0f, 1f)] public float HeadWeight = 1f;
        [Range(0f, 180f)] public float HeadAngle = 75f;
        public List<LimbTarget> ReachTargets = new List<LimbTarget> { new LimbTarget() };

        void Reset() => GetComponent<RigAuthoring>().Engine = Engine.Cpu;
    }
}
