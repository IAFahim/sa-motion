using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace SA.Reactions
{
    [UpdateInGroup(typeof(AnimationSystemGroup))]
    [UpdateAfter(typeof(CpuPoseSystem))]
    [UpdateBefore(typeof(PoseConstraintSystemGroup))]
    public partial struct RagdollWriteBackSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var manager = state.EntityManager;
            using var rigs = new NativeList<Entity>(Allocator.Temp);
            foreach (var (ragdoll, entity) in SystemAPI.Query<RefRO<Ragdoll>>().WithEntityAccess())
                if (ragdoll.ValueRO.Spawned == 1)
                    rigs.Add(entity);
            foreach (var rig in rigs)
                WriteBack(manager, rig);
        }

        static void WriteBack(EntityManager manager, Entity rig)
        {
            var skeleton = manager.GetComponentData<Rig>(rig).Value;
            if (!skeleton.IsCreated)
                return;
            var bones = manager.GetBuffer<BoneEntity>(rig).ToNativeArray(Allocator.Temp);
            var bodies = manager.GetBuffer<RagdollBody>(rig).ToNativeArray(Allocator.Temp);
            ref var blob = ref skeleton.Value;
            var count = math.min(bones.Length, blob.Bones.Length);
            var bodyOfBone = new NativeArray<Entity>(count, Allocator.Temp);
            for (var role = 0; role < bodies.Length; role++)
                if (bodies[role].BoneIndex >= 0 && bodies[role].BoneIndex < count)
                    bodyOfBone[bodies[role].BoneIndex] = bodies[role].Body;
            var origin = manager.GetComponentData<LocalToWorld>(rig);
            var originRotation = math.normalize(origin.Rotation);
            var worldPosition = new NativeArray<float3>(count, Allocator.Temp);
            var worldRotation = new NativeArray<quaternion>(count, Allocator.Temp);
            var pelvisBone = -1;
            var pelvisBody = Entity.Null;
            for (var role = 0; role < bodies.Length; role++)
                if (bodies[role].BoneIndex >= 0 && bodies[role].BoneIndex < count && bodies[role].ParentRole < 0)
                {
                    pelvisBone = bodies[role].BoneIndex;
                    pelvisBody = bodies[role].Body;
                }
            if (pelvisBone < 0)
                return;
            for (var b = 0; b < count; b++)
            {
                var parent = blob.Bones[b].Parent;
                var parentPosition = parent < 0 ? origin.Position : worldPosition[parent];
                var parentRotation = parent < 0 ? originRotation : worldRotation[parent];
                var local = manager.GetComponentData<LocalTransform>(bones[b].Value);
                worldPosition[b] = parentPosition + math.rotate(parentRotation, local.Position);
                worldRotation[b] = math.mul(parentRotation, local.Rotation);
            }
            var offset = manager.GetComponentData<LocalTransform>(pelvisBody).Position - worldPosition[pelvisBone];
            for (var b = 0; b < count; b++)
            {
                var parent = blob.Bones[b].Parent;
                var parentPosition = parent < 0 ? origin.Position : worldPosition[parent];
                var parentRotation = parent < 0 ? originRotation : worldRotation[parent];
                var local = manager.GetComponentData<LocalTransform>(bones[b].Value);
                if (parent < 0)
                {
                    local.Position += math.rotate(math.inverse(originRotation), offset);
                    manager.SetComponentData(bones[b].Value, local);
                }
                if (bodyOfBone[b] != Entity.Null)
                {
                    var body = manager.GetComponentData<LocalTransform>(bodyOfBone[b]);
                    local.Rotation = math.mul(math.inverse(parentRotation), body.Rotation);
                    manager.SetComponentData(bones[b].Value, local);
                    worldRotation[b] = body.Rotation;
                }
                else
                    worldRotation[b] = math.mul(parentRotation, local.Rotation);
                worldPosition[b] = parentPosition + math.rotate(parentRotation, local.Position);
            }
        }
    }
}
