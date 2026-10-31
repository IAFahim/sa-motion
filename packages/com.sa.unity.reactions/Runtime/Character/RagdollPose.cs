using SA;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SA.Reactions
{
    static class RagdollPose
    {
        public static bool Evaluate(EntityManager manager, Entity rig, out NativeArray<float3> position, out NativeArray<quaternion> rotation)
        {
            position = default;
            rotation = default;
            var skeleton = manager.GetComponentData<Rig>(rig).Value;
            if (!skeleton.IsCreated)
                return false;
            var clips = manager.GetBuffer<ClipAsset>(rig).ToNativeArray(Allocator.Temp);
            var masks = manager.GetBuffer<MaskAsset>(rig).ToNativeArray(Allocator.Temp);
            var states = manager.GetBuffer<ClipState>(rig).ToNativeArray(Allocator.Temp);
            ref var blob = ref skeleton.Value;
            position = new NativeArray<float3>(blob.Bones.Length, Allocator.Temp);
            rotation = new NativeArray<quaternion>(blob.Bones.Length, Allocator.Temp);
            for (var b = 0; b < blob.Bones.Length; b++)
            {
                var pose = Evaluation.Bone(ref blob, clips.AsReadOnlySpan(), masks.AsReadOnlySpan(), states.AsReadOnlySpan(), b);
                var parent = blob.Bones[b].Parent;
                rotation[b] = parent < 0 ? pose.Rotation : math.mul(rotation[parent], pose.Rotation);
                position[b] = parent < 0 ? pose.Position : position[parent] + math.rotate(rotation[parent], pose.Position);
            }
            return true;
        }
    }
}
