using System;
using SA.Motion.Legs;
using SA.Motion.Unity;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace SA.Reactions
{
    [UpdateInGroup(typeof(AnimationSystemGroup))]
    [UpdateAfter(typeof(RagdollWriteBackSystem))]
    [UpdateBefore(typeof(PoseConstraintSystemGroup))]
    public partial struct LegIKSystem : ISystem
    {
        public const int MaxLegs = 8;
        uint frame;

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physics))
                return;
            frame++;
            state.Dependency = new SolveJob
            {
                World = physics.PhysicsWorld,
                DeltaTime = SystemAPI.Time.DeltaTime,
                Frame = frame,
                Transforms = SystemAPI.GetComponentLookup<LocalTransform>(),
                Parents = SystemAPI.GetComponentLookup<Parent>(true),
                Post = SystemAPI.GetComponentLookup<PostTransformMatrix>(true),
                Fixed = SystemAPI.GetComponentLookup<LocalToWorld>(true),
                Ragdolls = SystemAPI.GetComponentLookup<Ragdoll>(true),
                Clips = SystemAPI.GetBufferLookup<ClipAsset>(true),
                States = SystemAPI.GetBufferLookup<ClipState>(true),
                Transitions = SystemAPI.GetBufferLookup<BoneTransition>()
            }.ScheduleParallel(state.Dependency);
        }

        public static float ClipWeight(ReadOnlySpan<ClipAsset> clips, ReadOnlySpan<ClipState> states)
        {
            var total = 0f;
            var weighted = 0f;
            foreach (var state in states)
            {
                var weight = math.max(0f, state.Weight * state.Fade);
                if (state.Mode != ClipMode.Override || state.Clip.Index >= clips.Length || !(weight > 0f))
                    continue;
                total += weight;
                weighted += weight * math.saturate(clips[state.Clip.Index].FootIK);
            }
            return total > 0f ? weighted / total : 1f;
        }

        [BurstCompile]
        [WithAll(typeof(CpuEngine))]
        partial struct SolveJob : IJobEntity
        {
            [ReadOnly] public PhysicsWorld World;
            public float DeltaTime;
            public uint Frame;
            [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> Transforms;
            [ReadOnly] public ComponentLookup<Parent> Parents;
            [ReadOnly] public ComponentLookup<PostTransformMatrix> Post;
            [ReadOnly] public ComponentLookup<LocalToWorld> Fixed;
            [ReadOnly] public ComponentLookup<Ragdoll> Ragdolls;
            [ReadOnly] public BufferLookup<ClipAsset> Clips;
            [ReadOnly] public BufferLookup<ClipState> States;
            [NativeDisableParallelForRestriction] public BufferLookup<BoneTransition> Transitions;

            void Execute(Entity entity, ref LegIK ik, in DynamicBuffer<BoneEntity> bones, ref DynamicBuffer<LegIKFoot> feet)
            {
                var resumed = ik.LastFrame + 1u != Frame;
                ik.LastFrame = Frame;
                var physical = Ragdolls.HasComponent(entity) && Ragdolls[entity].Spawned == 1 && Ragdolls[entity].BlendLeft <= 0f;
                if (resumed || physical)
                {
                    ik.State = default;
                    for (var i = 0; i < feet.Length; i++)
                    {
                        var reset = feet[i];
                        reset.State = default;
                        reset.Support = Entity.Null;
                        feet[i] = reset;
                    }
                    if (physical)
                        return;
                }
                var count = math.min(feet.Length, MaxLegs);
                if (count == 0)
                    return;
                var root = Matrix(entity);
                var rootScale = math.max(0.01f, math.length(root.c1.xyz));
                var up = math.normalizesafe(root.c1.xyz, math.up());
                var forward = math.normalizesafe(root.c2.xyz, new float3(0f, 0f, 1f));
                var parameters = ik.Params;
                var pelvis = ik.Pelvis != Entity.Null && Transforms.HasComponent(ik.Pelvis);
                if (!pelvis)
                    parameters.PelvisWeight = 0f;
                var bodyScale = math.max(0.01f, ik.BodyScale) * rootScale;
                var reach = (parameters.MaxStepHeight + 0.15f) * bodyScale;
                var weight = ik.Weight * (Clips.HasBuffer(entity) && States.HasBuffer(entity)
                    ? ClipWeight(Clips[entity].AsNativeArray().AsReadOnlySpan(), States[entity].AsNativeArray().AsReadOnlySpan())
                    : 1f);
                Span<LegInput> inputs = stackalloc LegInput[count];
                Span<FootShape> shapes = stackalloc FootShape[count];
                Span<LegState> states = stackalloc LegState[count];
                Span<LegPose> poses = stackalloc LegPose[count];
                Span<Entity> supports = stackalloc Entity[count];
                for (var i = 0; i < count; i++)
                {
                    var foot = feet[i];
                    if (!Transforms.HasComponent(foot.Upper) || !Transforms.HasComponent(foot.Lower) || !Transforms.HasComponent(foot.Foot))
                    {
                        inputs[i] = default;
                        shapes[i] = foot.Shape;
                        states[i] = default;
                        supports[i] = Entity.Null;
                        continue;
                    }
                    var upper = Matrix(foot.Upper);
                    var lower = Matrix(foot.Lower);
                    var ankle = Matrix(foot.Foot);
                    var footScale = math.max(0.01f, math.length(ankle.c0.xyz));
                    var shape = new FootShape
                    {
                        Heel = Conversions.ToMotion(Conversions.ToUnity(foot.Shape.Heel) * footScale),
                        Ball = Conversions.ToMotion(Conversions.ToUnity(foot.Shape.Ball) * footScale)
                    };
                    var footRotation = Rotation(ankle);
                    var contact = ankle.c3.xyz + math.rotate(footRotation, math.lerp(Conversions.ToUnity(shape.Heel), Conversions.ToUnity(shape.Ball), math.saturate(parameters.TurnPivot)));
                    var ray = new RaycastInput
                    {
                        Start = contact + up * reach,
                        End = contact - up * (reach + 0.2f * bodyScale),
                        Filter = new CollisionFilter { BelongsTo = uint.MaxValue, CollidesWith = ik.GroundMask }
                    };
                    var hit = World.CastRay(ray, out var ground);
                    supports[i] = hit ? World.Bodies[ground.RigidBodyIndex].Entity : Entity.Null;
                    if (foot.State.Planted != 0 && foot.Support != Entity.Null && (Transforms.HasComponent(foot.Support) || Fixed.HasComponent(foot.Support)))
                        foot.State.Anchor = Conversions.ToMotion(math.transform(Matrix(foot.Support), foot.SupportAnchor));
                    inputs[i] = new LegInput
                    {
                        Hip = Conversions.ToMotion(upper.c3.xyz),
                        Knee = Conversions.ToMotion(lower.c3.xyz),
                        Ankle = Conversions.ToMotion(ankle.c3.xyz),
                        Upper = Conversions.ToMotion(Rotation(upper)),
                        Lower = Conversions.ToMotion(Rotation(lower)),
                        Foot = Conversions.ToMotion(footRotation),
                        Ground = new GroundHit
                        {
                            Hit = (byte)(hit ? 1 : 0),
                            Point = Conversions.ToMotion(hit ? ground.Position : contact),
                            Normal = Conversions.ToMotion(hit ? ground.SurfaceNormal : up)
                        }
                    };
                    shapes[i] = shape;
                    states[i] = foot.State;
                }
                var shift = Conversions.ToUnity(LegRig.Step(parameters, bodyScale, weight, ref ik.State, states, inputs, shapes,
                    Conversions.ToMotion(root.c3.xyz), Conversions.ToMotion(up), Conversions.ToMotion(forward), DeltaTime, poses));
                if (pelvis && math.lengthsq(shift) > 0f)
                {
                    var local = Transforms[ik.Pelvis];
                    var parent = Parents.HasComponent(ik.Pelvis) ? Matrix(Parents[ik.Pelvis].Value) : float4x4.identity;
                    local.Position += math.mul(math.inverse(parent), new float4(shift, 0f)).xyz;
                    Transforms[ik.Pelvis] = local;
                }
                for (var i = 0; i < count; i++)
                {
                    var foot = feet[i];
                    foot.State = states[i];
                    var pose = poses[i];
                    if (pose.Planted != 0)
                    {
                        if (foot.State.Changed != 0 || supports[i] != foot.Support)
                        {
                            foot.Support = supports[i];
                            foot.SupportAnchor = supports[i] != Entity.Null && (Transforms.HasComponent(supports[i]) || Fixed.HasComponent(supports[i]))
                                ? math.transform(math.inverse(Matrix(supports[i])), Conversions.ToUnity(foot.State.Anchor))
                                : float3.zero;
                        }
                    }
                    else
                        foot.Support = Entity.Null;
                    feet[i] = foot;
                    if (pose.Solved == 0)
                        continue;
                    if (pose.Stretch > 1.0001f)
                    {
                        Stretch(foot.Lower, pose.Stretch);
                        Stretch(foot.Foot, pose.Stretch);
                    }
                    SetRotation(foot.Upper, Conversions.ToUnity(pose.Upper));
                    SetRotation(foot.Lower, Conversions.ToUnity(pose.Lower));
                    SetRotation(foot.Foot, Conversions.ToUnity(pose.Foot));
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
                        item.History.Current.Position = Conversions.ToMotion(local.Position);
                        history[b] = item;
                    }
                }
            }

            void Stretch(Entity bone, float amount)
            {
                var local = Transforms[bone];
                local.Position *= amount;
                Transforms[bone] = local;
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
