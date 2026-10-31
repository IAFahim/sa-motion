using SA.Motion.Kinematics;
using SA.Motion.Unity;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace SA.IK
{
    [UpdateInGroup(typeof(PoseConstraintSystemGroup))]
    public partial struct IKSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new SolveJob
            {
                Transforms = SystemAPI.GetComponentLookup<LocalTransform>(),
                Parents = SystemAPI.GetComponentLookup<Parent>(true),
                Post = SystemAPI.GetComponentLookup<PostTransformMatrix>(true),
                Fixed = SystemAPI.GetComponentLookup<LocalToWorld>(true),
                Looks = SystemAPI.GetBufferLookup<LookAtConstraint>(true),
                Limbs = SystemAPI.GetBufferLookup<TwoBoneConstraint>(true),
                History = SystemAPI.GetBufferLookup<BoneTransition>()
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(CpuEngine), typeof(IKEnabled))]
        [WithNone(typeof(ExternalPoseControl))]
        partial struct SolveJob : IJobEntity
        {
            [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> Transforms;
            [ReadOnly] public ComponentLookup<Parent> Parents;
            [ReadOnly] public ComponentLookup<PostTransformMatrix> Post;
            [ReadOnly] public ComponentLookup<LocalToWorld> Fixed;
            [ReadOnly] public BufferLookup<LookAtConstraint> Looks;
            [ReadOnly] public BufferLookup<TwoBoneConstraint> Limbs;
            [NativeDisableParallelForRestriction] public BufferLookup<BoneTransition> History;

            void Execute(Entity entity, in DynamicBuffer<BoneEntity> bones)
            {
                if (Looks.HasBuffer(entity))
                    foreach (var item in Looks[entity])
                    {
                        if (!Transforms.HasComponent(item.Bone))
                            continue;
                        var matrix = Matrix(item.Bone);
                        var target = Position(item.Target, item.Position);
                        var rotation = Aim.Solve(Conversions.ToMotion(Rotation(matrix)), Conversions.ToMotion(item.Forward),
                            Conversions.ToMotion(target - matrix.c3.xyz), item.Weight, item.MaxRadians);
                        Set(item.Bone, Conversions.ToUnity(rotation));
                    }
                if (Limbs.HasBuffer(entity))
                    foreach (var item in Limbs[entity])
                    {
                        if (!Transforms.HasComponent(item.Upper) || !Transforms.HasComponent(item.Middle) || !Transforms.HasComponent(item.End))
                            continue;
                        var upper = Matrix(item.Upper);
                        var middle = Matrix(item.Middle);
                        var end = Matrix(item.End);
                        var solved = TwoBoneIK.Solve(Conversions.ToMotion(upper.c3.xyz), Conversions.ToMotion(middle.c3.xyz),
                            Conversions.ToMotion(end.c3.xyz), Conversions.ToMotion(Position(item.Target, item.Position)), Conversions.ToMotion(Position(item.Pole, item.PolePosition)));
                        if (!solved.Valid)
                            continue;
                        var weight = math.saturate(math.select(0f, item.Weight, math.isfinite(item.Weight)));
                        Set(item.Upper, math.nlerp(Rotation(upper), math.mul(Conversions.ToUnity(solved.UpperDelta), Rotation(upper)), weight));
                        Set(item.Middle, math.nlerp(Rotation(middle), math.mul(Conversions.ToUnity(solved.LowerDelta), Rotation(middle)), weight));
                        Set(item.End, Rotation(end));
                    }
                if (History.HasBuffer(entity))
                {
                    var transitions = History[entity];
                    for (var i = 0; i < math.min(transitions.Length, bones.Length); i++)
                        if (Transforms.HasComponent(bones[i].Value))
                        {
                            var value = transitions[i];
                            value.History.Current.Rotation = Conversions.ToMotion(Transforms[bones[i].Value].Rotation);
                            transitions[i] = value;
                        }
                }
            }

            float3 Position(Entity target, float3 fallback) => target != Entity.Null && (Transforms.HasComponent(target) || Fixed.HasComponent(target)) ? Matrix(target).c3.xyz : fallback;
            static quaternion Rotation(float4x4 value) => math.rotation((float3x3)value);

            float4x4 Matrix(Entity entity)
            {
                var result = float4x4.identity;
                for (var depth = 0; depth < 256 && entity != Entity.Null; depth++)
                {
                    if (!Transforms.HasComponent(entity))
                        return Fixed.HasComponent(entity) ? math.mul(Fixed[entity].Value, result) : result;
                    var local = Transforms[entity].ToMatrix();
                    if (Post.HasComponent(entity))
                        local = math.mul(local, Post[entity].Value);
                    result = math.mul(local, result);
                    entity = Parents.HasComponent(entity) ? Parents[entity].Value : Entity.Null;
                }
                return result;
            }

            void Set(Entity bone, quaternion rotation)
            {
                var parent = Parents.HasComponent(bone) ? Rotation(Matrix(Parents[bone].Value)) : quaternion.identity;
                var local = Transforms[bone];
                local.Rotation = math.normalizesafe(math.mul(math.inverse(parent), rotation));
                Transforms[bone] = local;
            }
        }
    }
}
