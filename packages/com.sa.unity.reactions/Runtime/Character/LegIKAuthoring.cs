using System;
using SA.Motion.Legs;
using UnityEngine;

namespace SA.Reactions
{
    [Serializable]
    public struct FootShapeOverride
    {
        [Tooltip("Use the heel and ball below instead of the points found from the skeleton.")]
        public bool Override;
        [Tooltip("The back of the sole, in the foot bone's local space.")]
        public Vector3 Heel;
        [Tooltip("The ball of the foot on the sole, in the foot bone's local space.")]
        public Vector3 Ball;
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(RigAuthoring))]
    public class LegIKAuthoring : MonoBehaviour, IFootIKSettings
    {
        [Tooltip("Start with leg IK enabled. Gameplay or a timeline toggles the LegIK component at runtime.")]
        public bool StartEnabled = true;
        [Range(0f, 1f)]
        [Tooltip("Runtime strength. Multiplied by each clip's Foot IK tick and eased by Weight Half Life.")]
        public float Weight = 1f;
        [Tooltip("A starting point for every setting. Gentle keeps close to the animation and is the safe default.")]
        public LegPreset Preset = LegPreset.Gentle;
        [Tooltip("Use the settings below instead of the preset.")]
        public bool Customize;
        public LegParams Custom = LegParams.Gentle;
        [Tooltip("Layers the feet stand on.")]
        public LayerMask Ground = 1;
        public FootShapeOverride LeftFoot;
        public FootShapeOverride RightFoot;

        public LegParams Settings => Customize ? Custom : LegParams.Preset(Preset);

        void OnDrawGizmosSelected()
        {
            var animator = GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman)
                return;
            Draw(animator.GetBoneTransform(HumanBodyBones.LeftFoot), LeftFoot);
            Draw(animator.GetBoneTransform(HumanBodyBones.RightFoot), RightFoot);
        }

        static void Draw(Transform foot, FootShapeOverride shape)
        {
            if (foot == null || !shape.Override)
                return;
            var heel = foot.TransformPoint(shape.Heel);
            var ball = foot.TransformPoint(shape.Ball);
            Gizmos.color = new Color(1f, 0.55f, 0.1f);
            Gizmos.DrawLine(heel, ball);
            Gizmos.DrawWireSphere(heel, 0.015f);
            Gizmos.DrawWireSphere(ball, 0.015f);
        }
    }
}
