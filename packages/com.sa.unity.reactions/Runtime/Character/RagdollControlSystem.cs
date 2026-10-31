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
                    ragdoll.Mode = RagdollMode.Tracking;
                    ragdoll.Timer = 0f;
                }
            }
            else
                ragdoll.Strength = math.min(ragdoll.MaxStrength, ragdoll.Strength + ragdoll.RecoverRate * dt);
            manager.SetComponentData(rig, ragdoll);
            WriteTargets(manager, rig, ragdoll.Strength, bodies, specs);
        }

        static void ApplyHits(EntityManager manager, Entity rig, ref Ragdoll ragdoll, NativeArray<RagdollBody> bodies)
        {
            var hits = manager.GetBuffer<RagdollHit>(rig);
            for (var i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (hit.Role < 0 || hit.Role >= bodies.Length)
                    continue;
                var body = bodies[hit.Role].Body;
                var velocity = manager.GetComponentData<PhysicsVelocity>(body);
                var mass = manager.GetComponentData<PhysicsMass>(body);
                var transform = manager.GetComponentData<LocalTransform>(body);
                velocity.ApplyImpulse(mass, transform.Position, transform.Rotation, hit.Impulse, hit.Point);
                manager.SetComponentData(body, velocity);
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

        static void WriteTargets(EntityManager manager, Entity rig, float strength, NativeArray<RagdollBody> bodies, NativeArray<RagdollRoleSpec> specs)
        {
            if (!RagdollPose.Evaluate(manager, rig, out _, out var model))
                return;
            for (var i = 0; i < specs.Length; i++)
            {
                var spec = specs[i];
                if (spec.ParentRole < 0)
                    continue;
                var childBone = bodies[spec.Role].BoneIndex;
                var parentBone = bodies[spec.ParentRole].BoneIndex;
                if (childBone < 0 || parentBone < 0)
                    continue;
                var body = bodies[spec.Role].Body;
                var drive = manager.GetComponentData<PoseDrive>(body);
                drive.Target = math.mul(math.inverse(model[parentBone]), model[childBone]);
                drive.Strength = strength;
                drive.MaxTorque = spec.MaxTorque * strength;
                manager.SetComponentData(body, drive);
            }
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
