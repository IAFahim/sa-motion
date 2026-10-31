using SA.Humanoid;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using Math = Unity.Physics.Math;

namespace SA.Reactions
{
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public partial struct RagdollSpawnSystem : ISystem
    {
        const float MaxInverseInertia = 80f;

        public void OnUpdate(ref SystemState state)
        {
            using var pending = new NativeList<Entity>(Allocator.Temp);
            foreach (var (ragdoll, entity) in SystemAPI.Query<RefRO<Ragdoll>>().WithEntityAccess())
                if (ragdoll.ValueRO.Spawned == 0)
                    pending.Add(entity);
            foreach (var rig in pending)
                Spawn(ref state, rig);
        }

        static void Spawn(ref SystemState state, Entity rig)
        {
            var manager = state.EntityManager;
            var specs = manager.GetBuffer<RagdollRoleSpec>(rig).ToNativeArray(Allocator.Temp);
            for (var i = 0; i < specs.Length; i++)
                if (!manager.HasComponent<LocalToWorld>(specs[i].Bone))
                    return;
            var boneBuffer = manager.GetBuffer<BoneEntity>(rig).ToNativeArray(Allocator.Temp);
            var ragdoll = manager.GetComponentData<Ragdoll>(rig);
            var bodies = new NativeArray<Entity>(Roles.Count, Allocator.Temp);
            var bodyBuffer = manager.GetBuffer<RagdollBody>(rig);
            bodyBuffer.Length = Roles.Count;
            for (var i = 0; i < specs.Length; i++)
            {
                var spec = specs[i];
                var world = manager.GetComponentData<LocalToWorld>(spec.Bone);
                var scale = math.length(world.Value.c0.xyz);
                if (scale < 0.98f || scale > 1.02f)
                {
                    Debug.LogError($"Ragdoll needs a unit-scale rig; bone scale is {scale:F3}. Ragdoll disabled.");
                    ragdoll.Spawned = 2;
                    manager.SetComponentData(rig, ragdoll);
                    return;
                }
            }
            if (!RagdollPose.Evaluate(manager, rig, out var modelPosition, out var modelRotation))
                return;
            var origin = manager.GetComponentData<LocalToWorld>(rig);
            var originRotation = math.normalize(origin.Rotation);
            for (var i = 0; i < specs.Length; i++)
            {
                var spec = specs[i];
                var boneIndex = -1;
                for (var b = 0; b < boneBuffer.Length; b++)
                    if (boneBuffer[b].Value == spec.Bone)
                        boneIndex = b;
                if (boneIndex < 0)
                {
                    Debug.LogError("Ragdoll bone is not part of the SA rig skeleton. Ragdoll disabled.");
                    ragdoll.Spawned = 2;
                    manager.SetComponentData(rig, ragdoll);
                    return;
                }
                var body = manager.CreateEntity(typeof(LocalTransform), typeof(PhysicsCollider), typeof(PhysicsVelocity), typeof(PhysicsMass), typeof(PhysicsDamping), typeof(PhysicsWorldIndex), typeof(RagdollOwner));
                manager.SetComponentData(body, LocalTransform.FromPositionRotation(origin.Position + math.rotate(originRotation, modelPosition[boneIndex]), math.mul(originRotation, modelRotation[boneIndex])));
                manager.SetComponentData(body, new PhysicsCollider { Value = spec.Shape });
                var mass = PhysicsMass.CreateDynamic(spec.Shape.Value.MassProperties, spec.Mass);
                mass.InverseInertia = math.min(mass.InverseInertia, new float3(MaxInverseInertia));
                manager.SetComponentData(body, mass);
                manager.SetComponentData(body, new PhysicsDamping { Linear = 0.02f, Angular = 0.08f });
                manager.SetComponentData(body, new RagdollOwner { Rig = rig, Role = spec.Role });
                manager.AddComponentData(body, new PoseTrack { MaxLinearSpeed = 3f, MaxAngularSpeed = 20f, Rotation = quaternion.identity });
                bodies[spec.Role] = body;
                bodyBuffer = manager.GetBuffer<RagdollBody>(rig);
                bodyBuffer[spec.Role] = new RagdollBody { Body = body, BoneIndex = boneIndex, ParentRole = spec.ParentRole };
            }
            for (var i = 0; i < specs.Length; i++)
            {
                var spec = specs[i];
                if (spec.ParentRole < 0)
                    continue;
                var parent = bodies[spec.ParentRole];
                var child = bodies[spec.Role];
                var parentTransform = manager.GetComponentData<LocalTransform>(parent);
                var childTransform = manager.GetComponentData<LocalTransform>(child);
                var inverseParent = math.inverse(parentTransform.Rotation);
                var anchorInParent = math.rotate(inverseParent, childTransform.Position - parentTransform.Position);
                var frameInParent = math.mul(inverseParent, math.mul(childTransform.Rotation, spec.JointFrameInChild));
                if (ragdoll.JointMode != 2)
                {
                    PhysicsJoint joint;
                    if (ragdoll.JointMode == 1)
                        joint = PhysicsJoint.CreateBallAndSocket(anchorInParent, float3.zero);
                    else
                        PhysicsJoint.CreateRagdoll(
                            new BodyFrame(new RigidTransform(frameInParent, anchorInParent)),
                            new BodyFrame(new RigidTransform(spec.JointFrameInChild, float3.zero)),
                            spec.ConeAngle, new Math.FloatRange(spec.PlaneRange.x, spec.PlaneRange.y), new Math.FloatRange(spec.TwistRange.x, spec.TwistRange.y),
                            out joint, out _);
                    var jointEntity = manager.CreateEntity(typeof(PhysicsJoint), typeof(PhysicsConstrainedBodyPair), typeof(PhysicsWorldIndex));
                    manager.SetComponentData(jointEntity, joint);
                    manager.SetComponentData(jointEntity, new PhysicsConstrainedBodyPair(parent, child, false));
                }
                manager.AddComponentData(child, new PoseDrive
                {
                    Parent = parent,
                    ParentJoint = quaternion.identity,
                    ChildJoint = quaternion.identity,
                    Target = quaternion.identity,
                    Frequency = spec.Frequency,
                    DampingRatio = spec.DampingRatio,
                    MaxTorque = spec.MaxTorque,
                    Strength = 0f
                });
                manager.AddComponentData(child, new PoseDriveReport());
            }
            ragdoll.Spawned = 1;
            ragdoll.Mode = RagdollMode.Tracking;
            ragdoll.Strength = ragdoll.MaxStrength;
            ragdoll.StandHeight = manager.GetComponentData<LocalTransform>(bodies[(int)Role.Pelvis]).Position.y;
            manager.SetComponentData(rig, ragdoll);
        }
    }
}
