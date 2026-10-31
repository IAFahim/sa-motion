using SA.Motion.Kinematics;
using SA.Motion.Unity;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace SA.Reactions
{
    [InternalBufferCapacity(2)]
    public struct FootIKLeg : IBufferElementData
    {
        public Entity Upper;
        public Entity Lower;
        public Entity Foot;
        public float SoleHeight;
        public float3 Pole;
        public float3 FootUp;
        public float3 GroundNormal;
        public FootLockState Lock;
        public float3 Previous;
        public byte HasPrevious;
        public Entity Support;
        public float3 SupportAnchor;
    }

    [UpdateInGroup(typeof(AnimationSystemGroup))]
    [UpdateAfter(typeof(RagdollWriteBackSystem))]
    [UpdateBefore(typeof(PoseConstraintSystemGroup))]
    public partial struct FootIKSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physics))
                return;
            state.Dependency = new SolveJob
            {
                World = physics.PhysicsWorld,
                DeltaTime = SystemAPI.Time.DeltaTime,
                Transforms = SystemAPI.GetComponentLookup<LocalTransform>(),
                Parents = SystemAPI.GetComponentLookup<Parent>(true),
                Post = SystemAPI.GetComponentLookup<PostTransformMatrix>(true),
                Fixed = SystemAPI.GetComponentLookup<LocalToWorld>(true),
                Ragdolls = SystemAPI.GetComponentLookup<Ragdoll>(true),
                Transitions = SystemAPI.GetBufferLookup<BoneTransition>()
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(CpuEngine))]
        partial struct SolveJob : IJobEntity
        {
            [ReadOnly] public PhysicsWorld World;
            public float DeltaTime;
            [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> Transforms;
            [ReadOnly] public ComponentLookup<Parent> Parents;
            [ReadOnly] public ComponentLookup<PostTransformMatrix> Post;
            [ReadOnly] public ComponentLookup<LocalToWorld> Fixed;
            [ReadOnly] public ComponentLookup<Ragdoll> Ragdolls;
            [NativeDisableParallelForRestriction] public BufferLookup<BoneTransition> Transitions;

            void Execute(Entity entity, in DynamicBuffer<BoneEntity> bones, ref DynamicBuffer<FootIKLeg> legs)
            {
                var physical = Ragdolls.HasComponent(entity) && Ragdolls[entity].Spawned == 1 && Ragdolls[entity].BlendLeft <= 0f;
                var root = Matrix(entity);
                for (var i = 0; i < legs.Length; i++)
                {
                    var leg = legs[i];
                    if (physical || !Transforms.HasComponent(leg.Upper) || !Transforms.HasComponent(leg.Lower) || !Transforms.HasComponent(leg.Foot))
                    {
                        leg.Lock = default;
                        leg.HasPrevious = 0;
                        legs[i] = leg;
                        continue;
                    }
                    var upper = Matrix(leg.Upper);
                    var lower = Matrix(leg.Lower);
                    var foot = Matrix(leg.Foot);
                    var position = foot.c3.xyz;
                    var scale = math.max(0.01f, math.length(root.c1.xyz));
                    var sole = leg.SoleHeight * scale;
                    var input = new RaycastInput
                    {
                        Start = position + math.up() * (0.35f * scale),
                        End = position - math.up() * (0.65f * scale),
                        Filter = new CollisionFilter { BelongsTo = uint.MaxValue, CollidesWith = 1u }
                    };
                    var hit = World.CastRay(input, out var contact) && contact.SurfaceNormal.y > 0.6f;
                    var ground = hit ? contact.Position + math.up() * sole : position;
                    var support = hit ? World.Bodies[contact.RigidBodyIndex].Entity : Entity.Null;
                    if ((leg.Lock.Locked || leg.Lock.Weight > 0f) && leg.Support != Entity.Null)
                        leg.Lock.Position = Conversions.ToMotion(math.transform(Matrix(leg.Support), leg.SupportAnchor));
                    if (support != leg.Support)
                        leg.Lock.Locked = false;
                    var velocity = leg.HasPrevious == 1 && DeltaTime > 0f ? (position.y - leg.Previous.y) / DeltaTime : 0f;
                    leg.Previous = position;
                    leg.HasPrevious = 1;
                    leg.Lock = FootLock.Step(leg.Lock, Conversions.ToMotion(position), Conversions.ToMotion(ground), hit,
                        position.y - ground.y, velocity, 0.045f * scale, 0.13f * scale, 0.45f * scale, DeltaTime, 0.1f);
                    if (leg.Lock.Locked)
                    {
                        leg.Support = support;
                        leg.GroundNormal = contact.SurfaceNormal;
                        if (support != Entity.Null)
                            leg.SupportAnchor = math.transform(math.inverse(Matrix(support)), Conversions.ToUnity(leg.Lock.Position));
                    }
                    if (leg.Lock.Weight > 0f)
                    {
                        var target = Conversions.ToUnity(leg.Lock.Position);
                        var pole = lower.c3.xyz + math.rotate(Rotation(root), leg.Pole) * scale;
                        var solved = TwoBoneIK.Solve(Conversions.ToMotion(upper.c3.xyz), Conversions.ToMotion(lower.c3.xyz),
                            Conversions.ToMotion(position), Conversions.ToMotion(target), Conversions.ToMotion(pole));
                        if (solved.Valid)
                        {
                            var originalFoot = Rotation(foot);
                            SetRotation(leg.Upper, math.nlerp(Rotation(upper), math.mul(Conversions.ToUnity(solved.UpperDelta), Rotation(upper)), leg.Lock.Weight));
                            SetRotation(leg.Lower, math.nlerp(Rotation(lower), math.mul(Conversions.ToUnity(solved.LowerDelta), Rotation(lower)), leg.Lock.Weight));
                            var align = TwoBoneIK.FromTo(Conversions.ToMotion(math.rotate(originalFoot, leg.FootUp)), Conversions.ToMotion(leg.GroundNormal));
                            SetRotation(leg.Foot, math.nlerp(originalFoot, math.mul(Conversions.ToUnity(align), originalFoot), leg.Lock.Weight));
                        }
                    }
                    legs[i] = leg;
                }
                if (Transitions.HasBuffer(entity))
                {
                    var history = Transitions[entity];
                    for (var b = 0; b < math.min(history.Length, bones.Length); b++)
                    {
                        if (!Transforms.HasComponent(bones[b].Value))
                            continue;
                        var local = Transforms[bones[b].Value];
                        var item = history[b];
                        item.History.Current.Rotation = Conversions.ToMotion(local.Rotation);
                        history[b] = item;
                    }
                }
            }

            float4x4 Matrix(Entity entity)
            {
                var matrix = float4x4.identity;
                for (var depth = 0; depth < 256 && entity != Entity.Null; depth++)
                {
                    if (!Transforms.HasComponent(entity))
                        return Fixed.HasComponent(entity) ? math.mul(Fixed[entity].Value, matrix) : matrix;
                    var local = Transforms[entity].ToMatrix();
                    if (Post.HasComponent(entity))
                        local = math.mul(local, Post[entity].Value);
                    matrix = math.mul(local, matrix);
                    entity = Parents.HasComponent(entity) ? Parents[entity].Value : Entity.Null;
                }
                return matrix;
            }

            void SetRotation(Entity bone, quaternion rotation)
            {
                var parent = Parents.HasComponent(bone) ? Rotation(Matrix(Parents[bone].Value)) : quaternion.identity;
                var local = Transforms[bone];
                local.Rotation = math.normalizesafe(math.mul(math.inverse(parent), rotation));
                Transforms[bone] = local;
            }

            static quaternion Rotation(float4x4 matrix) => math.rotation((float3x3)matrix);
        }
    }
}
