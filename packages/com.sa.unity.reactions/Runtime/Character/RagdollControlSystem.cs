using SA.Humanoid;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Extensions;
using Unity.Physics.Systems;
using Unity.Transforms;

namespace SA.Reactions
{
    [UpdateInGroup(typeof(BeforePhysicsSystemGroup))]
    public partial struct RagdollControlSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var manager = state.EntityManager;
            var dt = SystemAPI.Time.DeltaTime;
            using var rigs = new NativeList<Entity>(Allocator.Temp);
            foreach (var (ragdoll, entity) in SystemAPI.Query<RefRO<Ragdoll>>().WithEntityAccess())
                if (ragdoll.ValueRO.Spawned == 1)
                    rigs.Add(entity);
            foreach (var rig in rigs)
                Control(manager, rig, dt);
        }

        static void Control(EntityManager manager, Entity rig, float dt)
        {
            var ragdoll = manager.GetComponentData<Ragdoll>(rig);
            var bodies = manager.GetBuffer<RagdollBody>(rig).ToNativeArray(Allocator.Temp);
            var specs = manager.GetBuffer<RagdollRoleSpec>(rig).ToNativeArray(Allocator.Temp);
            ApplyHits(manager, rig, ref ragdoll, bodies);
            if (ragdoll.Mode == RagdollMode.Limp)
            {
                ragdoll.Strength = 0f;
                ragdoll.Timer += dt;
                if (ragdoll.Timer >= ragdoll.LimpSeconds)
                {
                    ResetToAnimation(manager, rig, bodies);
                    BeginBlend(manager, rig, ref ragdoll);
                    ragdoll.Mode = RagdollMode.Tracking;
                    ragdoll.Timer = 0f;
                    ragdoll.Strength = ResetStrength;
                }
            }
            else
                ragdoll.Strength = math.min(ragdoll.MaxStrength, ragdoll.Strength + ragdoll.RecoverRate * dt);
            ragdoll.BlendLeft = math.max(0f, ragdoll.BlendLeft - dt);
            manager.SetComponentData(rig, ragdoll);
            var balanced = ragdoll.Mode == RagdollMode.Tracking && manager.HasComponent<BalanceControl>(rig) && manager.GetComponentData<BalanceControl>(rig).Enabled != 0 && manager.GetComponentData<BalanceControl>(rig).Ready != 0;
            var tone = ragdoll.Mode == RagdollMode.Limp ? ragdoll.LimpTone : ragdoll.Strength;
            WriteTargets(manager, rig, balanced ? 0f : ragdoll.Strength, balanced ? 0f : tone, bodies, specs);
        }

        const float LocalShare = 0.4f;
        const float MaxLocalSpeed = 4f;
        const float ResetStrength = 0.8f;

        static void ApplyHits(EntityManager manager, Entity rig, ref Ragdoll ragdoll, NativeArray<RagdollBody> bodies)
        {
            var hits = manager.GetBuffer<RagdollHit>(rig);
            for (var i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (hit.Role < 0 || hit.Role >= bodies.Length)
                    continue;
                HitReactions.Play(manager, rig);
                var totalMass = 0f;
                for (var role = 0; role < bodies.Length; role++)
                    totalMass += 1f / math.max(1e-6f, manager.GetComponentData<PhysicsMass>(bodies[role].Body).InverseMass);
                for (var role = 0; role < bodies.Length; role++)
                {
                    var body = bodies[role].Body;
                    var velocity = manager.GetComponentData<PhysicsVelocity>(body);
                    var mass = manager.GetComponentData<PhysicsMass>(body);
                    velocity.ApplyLinearImpulse(mass, hit.Impulse * ((1f - LocalShare) / mass.InverseMass / totalMass));
                    if (role == hit.Role)
                    {
                        var transform = manager.GetComponentData<LocalTransform>(body);
                        var local = hit.Impulse * LocalShare;
                        var cap = MaxLocalSpeed / mass.InverseMass;
                        var length = math.length(local);
                        if (length > cap)
                            local *= cap / length;
                        velocity.ApplyImpulse(mass, transform.Position, transform.Rotation, local, hit.Point);
                    }
                    manager.SetComponentData(body, velocity);
                }
                var magnitude = math.length(hit.Impulse);
                ragdoll.Strength = math.max(0f, ragdoll.Strength - magnitude * ragdoll.StrengthLossPerImpulse);
                if (magnitude >= ragdoll.LimpImpulse && ragdoll.Mode == RagdollMode.Tracking)
                {
                    ragdoll.Mode = RagdollMode.Limp;
                    ragdoll.Timer = 0f;
                }
            }
            hits.Clear();
        }

        static void WriteTargets(EntityManager manager, Entity rig, float strength, float tone, NativeArray<RagdollBody> bodies, NativeArray<RagdollRoleSpec> specs)
        {
            if (!RagdollPose.Evaluate(manager, rig, out var modelPosition, out var model))
                return;
            var origin = manager.GetComponentData<LocalToWorld>(rig);
            var originRotation = math.normalize(origin.Rotation);
            for (var i = 0; i < specs.Length; i++)
            {
                var spec = specs[i];
                var boneIndex = bodies[spec.Role].BoneIndex;
                if (boneIndex < 0)
                    continue;
                var body = bodies[spec.Role].Body;
                var track = manager.GetComponentData<PoseTrack>(body);
                track.Position = origin.Position + math.rotate(originRotation, modelPosition[boneIndex]);
                track.Rotation = math.mul(originRotation, model[boneIndex]);
                track.Gain = spec.TrackGain * strength;
                manager.SetComponentData(body, track);
                if (spec.ParentRole < 0)
                    continue;
                var parentBone = bodies[spec.ParentRole].BoneIndex;
                if (parentBone < 0)
                    continue;
                var drive = manager.GetComponentData<PoseDrive>(body);
                drive.Target = math.mul(math.inverse(model[parentBone]), model[boneIndex]);
                drive.Strength = tone;
                drive.MaxTorque = spec.MaxTorque * tone;
                manager.SetComponentData(body, drive);
            }
        }

        static void BeginBlend(EntityManager manager, Entity rig, ref Ragdoll ragdoll)
        {
            if (!manager.HasComponent<TransitionIntent>(rig) || ragdoll.BlendSeconds <= 0f)
                return;
            var intent = manager.GetComponentData<TransitionIntent>(rig);
            intent.Pending = true;
            intent.Seconds = ragdoll.BlendSeconds;
            manager.SetComponentData(rig, intent);
            ragdoll.BlendLeft = ragdoll.BlendSeconds;
        }

        static void ResetToAnimation(EntityManager manager, Entity rig, NativeArray<RagdollBody> bodies)
        {
            if (!RagdollPose.Evaluate(manager, rig, out var position, out var rotation))
                return;
            var origin = manager.GetComponentData<LocalToWorld>(rig);
            var originRotation = math.normalize(origin.Rotation);
            for (var role = 0; role < bodies.Length; role++)
            {
                var index = bodies[role].BoneIndex;
                if (index < 0)
                    continue;
                var body = bodies[role].Body;
                manager.SetComponentData(body, LocalTransform.FromPositionRotation(origin.Position + math.rotate(originRotation, position[index]), math.mul(originRotation, rotation[index])));
                manager.SetComponentData(body, new PhysicsVelocity());
            }
        }
    }
}
