using System.Collections.Generic;
using SA.Humanoid;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace SA.Reactions.Editor
{
    class RagdollBaker : Baker<RagdollAuthoring>
    {
        public override void Bake(RagdollAuthoring authoring)
        {
            var animator = GetComponent<Animator>();
            if (animator != null && animator.avatar != null)
                DependsOn(animator.avatar);
            var errors = new List<string>();
            var bones = RagdollGeometry.Resolve(authoring.transform, animator, authoring.Overrides, errors);
            if (errors.Count > 0)
            {
                Debug.LogError($"RagdollAuthoring on '{authoring.name}': {string.Join(", ", errors)}");
                return;
            }
            var group = -(1 + (math.abs(authoring.gameObject.GetEntityId().GetHashCode()) % 100000));
            var geometry = RagdollGeometry.Build(authoring.transform, bones, authoring.TotalMass, authoring.GainScale, group, errors);
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Ragdoll
            {
                MaxStrength = authoring.MaxStrength,
                JointMode = (byte)authoring.JointMode,
                RecoverRate = 1f / math.max(0.1f, authoring.RecoverSeconds),
                LimpSeconds = authoring.LimpSeconds,
                LimpImpulse = authoring.LimpImpulse,
                LimpTone = authoring.LimpTone,
                StrengthLossPerImpulse = authoring.StrengthLossPerImpulse
            });
            if (authoring.BalanceController)
                AddComponent(entity, new BalanceControl { Enabled = 1 });
            AddBuffer<RagdollBody>(entity);
            AddBuffer<RagdollHit>(entity);
            var specs = AddBuffer<RagdollRoleSpec>(entity);
            foreach (var item in geometry)
            {
                var shape = item.Shape;
                AddBlobAsset(ref shape, out _);
                specs.Add(new RagdollRoleSpec
                {
                    Bone = GetEntity(bones[item.Role], TransformUsageFlags.Dynamic),
                    Role = item.Role,
                    ParentRole = item.ParentRole,
                    Shape = shape,
                    Mass = item.Mass,
                    AnchorInParent = item.AnchorInParent,
                    JointFrameInParent = item.JointFrameInParent,
                    JointFrameInChild = item.JointFrameInChild,
                    ConeAngle = item.ConeAngle,
                    PlaneRange = new float2(-math.PI * 0.5f, math.PI * 0.5f),
                    TwistRange = item.TwistRange,
                    Frequency = item.Frequency,
                    DampingRatio = 1f,
                    MaxTorque = item.MaxTorque,
                    TrackGain = item.TrackGain
                });
            }
        }
    }
}
