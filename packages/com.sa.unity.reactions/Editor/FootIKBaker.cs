using System.Collections.Generic;
using SA.Humanoid;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace SA.Reactions.Editor
{
    class FootIKBaker : Baker<RigAuthoring>
    {
        public override void Bake(RigAuthoring authoring)
        {
            if (!authoring.FootIK)
                return;
            var animator = GetComponentInChildren<Animator>();
            if (animator != null)
            {
                DependsOn(animator);
                DependsOn(animator.avatar);
            }
            var errors = new List<string>();
            var bones = RagdollGeometry.Resolve(authoring.transform, animator, null, errors);
            if (errors.Count > 0)
            {
                Debug.LogError($"SA: {authoring.name}: Foot IK needs a humanoid Avatar or recognizable humanoid bone names: {string.Join(", ", errors)}", authoring);
                return;
            }
            var legs = AddBuffer<FootIKLeg>(GetEntity(TransformUsageFlags.Dynamic));
            for (var side = 0; side < 2; side++)
            {
                var upper = bones[(int)(side == 0 ? Role.ThighLeft : Role.ThighRight)];
                var lower = bones[(int)(side == 0 ? Role.ShinLeft : Role.ShinRight)];
                var foot = bones[(int)(side == 0 ? Role.FootLeft : Role.FootRight)];
                DependsOn(upper);
                DependsOn(lower);
                DependsOn(foot);
                var forward = authoring.transform.forward;
                foreach (Transform child in foot)
                    if (child.name.ToLowerInvariant().Contains("toe"))
                    {
                        forward = (child.position - foot.position).normalized;
                        break;
                    }
                legs.Add(new FootIKLeg
                {
                    Upper = GetEntity(upper, TransformUsageFlags.Dynamic),
                    Lower = GetEntity(lower, TransformUsageFlags.Dynamic),
                    Foot = GetEntity(foot, TransformUsageFlags.Dynamic),
                    SoleHeight = Mathf.Clamp((foot.position.y - authoring.transform.position.y) / Mathf.Max(0.01f, authoring.transform.lossyScale.y), 0.015f, 0.15f),
                    Pole = math.rotate(math.inverse((quaternion)authoring.transform.rotation), (float3)forward),
                    FootUp = math.rotate(math.inverse((quaternion)foot.rotation), math.up())
                });
            }
        }
    }
}
