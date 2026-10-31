using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace SA.IK.Editor
{
    class IKBaker : Baker<IKAuthoring>
    {
        public override void Bake(IKAuthoring authoring)
        {
            var rig = GetComponent<RigAuthoring>();
            if (rig == null)
                return;
            var animator = GetComponentInChildren<Animator>();
            if (animator != null)
            {
                DependsOn(animator);
                DependsOn(animator.avatar);
            }
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<IKEnabled>(entity);
            var looks = AddBuffer<LookAtConstraint>(entity);
            if (authoring.HeadTarget != null)
            {
                var head = Find(authoring.transform, animator, HumanBodyBones.Head);
                if (head != null)
                    looks.Add(new LookAtConstraint
                    {
                        Bone = GetEntity(head, TransformUsageFlags.Dynamic), Target = GetEntity(authoring.HeadTarget, TransformUsageFlags.Dynamic),
                        Position = authoring.HeadTarget.position, Weight = authoring.HeadWeight, MaxRadians = math.radians(authoring.HeadAngle),
                        Forward = math.rotate(math.inverse((quaternion)head.rotation), (float3)authoring.transform.forward)
                    });
                else
                    Debug.LogError($"SA: {authoring.name}: no head bone was found for Head Target.", authoring);
            }
            var limbs = AddBuffer<TwoBoneConstraint>(entity);
            foreach (var item in authoring.ReachTargets)
            {
                if (item == null || item.Target == null)
                    continue;
                var arm = item.Limb == Limb.LeftArm || item.Limb == Limb.RightArm;
                var left = item.Limb == Limb.LeftArm || item.Limb == Limb.LeftLeg;
                var upper = Find(authoring.transform, animator, arm ? (left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm) : (left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg));
                var middle = Find(authoring.transform, animator, arm ? (left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm) : (left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg));
                var end = Find(authoring.transform, animator, arm ? (left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand) : (left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot));
                if (upper == null || middle == null || end == null)
                {
                    Debug.LogError($"SA: {authoring.name}: could not find {item.Limb}. Use a Humanoid Avatar or standard humanoid bone names.", authoring);
                    continue;
                }
                limbs.Add(new TwoBoneConstraint
                {
                    Upper = GetEntity(upper, TransformUsageFlags.Dynamic), Middle = GetEntity(middle, TransformUsageFlags.Dynamic), End = GetEntity(end, TransformUsageFlags.Dynamic),
                    Target = GetEntity(item.Target, TransformUsageFlags.Dynamic), Position = item.Target.position,
                    Pole = item.Pole != null ? GetEntity(item.Pole, TransformUsageFlags.Dynamic) : Entity.Null,
                    PolePosition = item.Pole != null ? (float3)item.Pole.position : (float3)(middle.position + authoring.transform.forward), Weight = item.Weight
                });
            }
        }

        static Transform Find(Transform root, Animator animator, HumanBodyBones bone)
        {
            if (animator != null && animator.avatar != null && animator.avatar.isHuman)
                return animator.GetBoneTransform(bone);
            var name = bone.ToString().ToLowerInvariant();
            foreach (var node in root.GetComponentsInChildren<Transform>(true))
            {
                var actual = node.name.ToLowerInvariant().Replace("_", "").Replace(" ", "").Replace("joint", "").TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
                var alias = name.Replace("upperleg", "thigh").Replace("lowerleg", "knee").Replace("lowerarm", "forearm").Replace("hand", "wrist").Replace("foot", "ankle");
                if (actual == name || actual == alias || actual.EndsWith(":" + name) || actual.EndsWith(":" + alias) || (bone == HumanBodyBones.Head && actual.EndsWith("head")))
                    return node;
            }
            return null;
        }
    }
}
