using SA.Motion.Kinematics;
using SA.Motion.Unity;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace SA.IK
{
    public static class ModelConstraints
    {
        public static void Apply(EntityManager manager, Entity rig, NativeArray<float3> positions, NativeArray<quaternion> rotations)
        {
            if (!manager.HasBuffer<BoneEntity>(rig) || !manager.HasComponent<IKEnabled>(rig))
                return;
            var bones = manager.GetBuffer<BoneEntity>(rig, true);
            var origin = manager.GetComponentData<LocalToWorld>(rig).Value;
            var inverse = math.inverse(origin);
            if (manager.HasBuffer<LookAtConstraint>(rig))
                foreach (var item in manager.GetBuffer<LookAtConstraint>(rig, true))
                {
                    var index = Index(bones, item.Bone);
                    if (index < 0 || index >= rotations.Length)
                        continue;
                    var target = math.transform(inverse, Position(manager, item.Target, item.Position));
                    rotations[index] = Conversions.ToUnity(Aim.Solve(Conversions.ToMotion(rotations[index]), Conversions.ToMotion(item.Forward),
                        Conversions.ToMotion(target - positions[index]), item.Weight, item.MaxRadians));
                }
            if (!manager.HasBuffer<TwoBoneConstraint>(rig))
                return;
            foreach (var item in manager.GetBuffer<TwoBoneConstraint>(rig, true))
            {
                var a = Index(bones, item.Upper);
                var b = Index(bones, item.Middle);
                var c = Index(bones, item.End);
                if (a < 0 || b < 0 || c < 0 || a >= positions.Length || b >= positions.Length || c >= positions.Length)
                    continue;
                var target = math.transform(inverse, Position(manager, item.Target, item.Position));
                var pole = math.transform(inverse, Position(manager, item.Pole, item.PolePosition));
                var result = TwoBoneIK.Solve(Conversions.ToMotion(positions[a]), Conversions.ToMotion(positions[b]), Conversions.ToMotion(positions[c]), Conversions.ToMotion(target), Conversions.ToMotion(pole));
                if (!result.Valid)
                    continue;
                var weight = math.saturate(math.select(0f, item.Weight, math.isfinite(item.Weight)));
                rotations[a] = math.nlerp(rotations[a], math.mul(Conversions.ToUnity(result.UpperDelta), rotations[a]), weight);
                rotations[b] = math.nlerp(rotations[b], math.mul(Conversions.ToUnity(result.LowerDelta), rotations[b]), weight);
                positions[b] = math.lerp(positions[b], Conversions.ToUnity(result.Middle), weight);
                positions[c] = math.lerp(positions[c], Conversions.ToUnity(result.End), weight);
            }
        }

        static int Index(DynamicBuffer<BoneEntity> bones, Entity entity)
        {
            for (var i = 0; i < bones.Length; i++)
                if (bones[i].Value == entity)
                    return i;
            return -1;
        }

        static float3 Position(EntityManager manager, Entity target, float3 fallback)
        {
            if (target == Entity.Null || !manager.Exists(target))
                return fallback;
            var matrix = float4x4.identity;
            for (var depth = 0; depth < 256 && target != Entity.Null; depth++)
            {
                if (!manager.HasComponent<LocalTransform>(target))
                    return manager.HasComponent<LocalToWorld>(target) ? math.transform(manager.GetComponentData<LocalToWorld>(target).Value, matrix.c3.xyz) : matrix.c3.xyz;
                var local = manager.GetComponentData<LocalTransform>(target).ToMatrix();
                if (manager.HasComponent<PostTransformMatrix>(target))
                    local = math.mul(local, manager.GetComponentData<PostTransformMatrix>(target).Value);
                matrix = math.mul(local, matrix);
                target = manager.HasComponent<Parent>(target) ? manager.GetComponentData<Parent>(target).Value : Entity.Null;
            }
            return matrix.c3.xyz;
        }
    }
}
