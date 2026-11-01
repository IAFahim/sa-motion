using System.Collections.Generic;
using SA.Humanoid;
using SA.Motion.Legs;
using SA.Motion.Unity;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace SA.Reactions.Editor
{
    public static class FootShapes
    {
        public static FootShape Auto(Transform root, Transform foot, Transform toe)
        {
            var up = root.up;
            var floor = root.position;
            Vector3 Flat(Vector3 point) => point - up * Vector3.Dot(point - floor, up);
            var forward = Vector3.ProjectOnPlane(toe != null ? toe.position - foot.position : root.forward, up);
            if (forward.sqrMagnitude < 1e-8f)
                forward = Vector3.ProjectOnPlane(root.forward, up);
            forward.Normalize();
            var ankle = Flat(foot.position);
            var size = Mathf.Max(0.01f, root.lossyScale.y);
            var ball = toe != null ? Flat(toe.position) : ankle + forward * (0.13f * size);
            var length = Mathf.Max(Vector3.Distance(ankle, ball), 0.03f * size);
            var heel = ankle - forward * (0.36f * length);
            return new FootShape
            {
                Heel = Conversions.ToMotion((float3)foot.InverseTransformPoint(heel)),
                Ball = Conversions.ToMotion((float3)foot.InverseTransformPoint(ball))
            };
        }

        public static Transform Toe(Animator animator, Transform foot, bool left)
        {
            if (animator != null && animator.isHuman)
            {
                var toe = animator.GetBoneTransform(left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
                if (toe != null)
                    return toe;
            }
            foreach (Transform child in foot)
                if (child.name.ToLowerInvariant().Contains("toe"))
                    return child;
            return null;
        }

        public static FootShape Resolve(Transform root, Animator animator, Transform foot, bool left, FootShapeOverride shape) =>
            shape.Override
                ? new FootShape { Heel = Conversions.ToMotion((float3)shape.Heel), Ball = Conversions.ToMotion((float3)shape.Ball) }
                : Auto(root, foot, Toe(animator, foot, left));
    }

    class LegIKBaker : Baker<RigAuthoring>
    {
        public override void Bake(RigAuthoring authoring)
        {
            var settings = GetComponent<LegIKAuthoring>();
            if (!authoring.FootIK && settings == null)
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
                Debug.LogError($"SA: {authoring.name}: leg IK needs a humanoid Avatar or recognizable humanoid bone names: {string.Join(", ", errors)}", authoring);
                return;
            }
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            var feet = AddBuffer<LegIKFoot>(entity);
            var legLength = 0f;
            for (var side = 0; side < 2; side++)
            {
                var left = side == 0;
                var upper = bones[(int)(left ? Role.ThighLeft : Role.ThighRight)];
                var lower = bones[(int)(left ? Role.ShinLeft : Role.ShinRight)];
                var foot = bones[(int)(left ? Role.FootLeft : Role.FootRight)];
                DependsOn(upper);
                DependsOn(lower);
                DependsOn(foot);
                legLength += Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, foot.position);
                var shape = settings != null ? (left ? settings.LeftFoot : settings.RightFoot) : default;
                feet.Add(new LegIKFoot
                {
                    Upper = GetEntity(upper, TransformUsageFlags.Dynamic),
                    Lower = GetEntity(lower, TransformUsageFlags.Dynamic),
                    Foot = GetEntity(foot, TransformUsageFlags.Dynamic),
                    Shape = FootShapes.Resolve(authoring.transform, animator, foot, left, shape)
                });
            }
            var scale = Mathf.Max(0.01f, authoring.transform.lossyScale.y);
            AddComponent(entity, new LegIK
            {
                Params = LegParamsSpec.Sanitize(settings != null ? settings.Settings : LegParams.Gentle),
                Weight = settings != null ? settings.Weight : 1f,
                BodyScale = legLength * 0.5f / scale / LegParams.ReferenceLegLength,
                GroundMask = settings != null ? (uint)settings.Ground.value : 1u,
                Pelvis = GetEntity(bones[(int)Role.Pelvis], TransformUsageFlags.Dynamic)
            });
            SetComponentEnabled<LegIK>(entity, settings == null || settings.StartEnabled);
        }
    }
}
