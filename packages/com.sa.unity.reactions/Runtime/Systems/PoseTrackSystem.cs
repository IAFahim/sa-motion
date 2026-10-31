using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Extensions;
using Unity.Physics.Systems;

namespace SA.Reactions
{
    [UpdateInGroup(typeof(PhysicsSystemGroup))]
    [UpdateAfter(typeof(PoseDriveSystem))]
    [UpdateBefore(typeof(PhysicsSimulationGroup))]
    public partial struct PoseTrackSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PoseTrack>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ref var singleton = ref SystemAPI.GetSingletonRW<PhysicsWorldSingleton>().ValueRW;
            state.Dependency = new TrackJob { World = singleton.PhysicsWorld, Timestep = SystemAPI.Time.DeltaTime }.Schedule(state.Dependency);
        }

        public static float3 RotationVector(quaternion rotation)
        {
            var value = math.normalizesafe(rotation).value;
            if (value.w < 0f)
                value = -value;
            var vector = value.xyz;
            var length = math.length(vector);
            return length < 1e-6f ? vector * 2f : vector * (2f * math.atan2(length, value.w) / length);
        }

        public static float3 ClampLength(float3 value, float limit)
        {
            var length = math.length(value);
            return length > limit && length > 0f ? value * (limit / length) : value;
        }

        [BurstCompile]
        partial struct TrackJob : IJobEntity
        {
            public PhysicsWorld World;
            public float Timestep;

            void Execute(Entity entity, in PoseTrack track)
            {
                var index = World.GetRigidBodyIndex(entity);
                if (index < 0 || index >= World.NumDynamicBodies || Timestep <= 0f || track.Gain <= 0f)
                    return;
                var body = World.Bodies[index].WorldFromBody;
                var centerOfMass = World.MotionDatas[index].BodyFromMotion.pos;
                var target = new RigidTransform(track.Rotation, track.Position);
                var linear = (math.transform(target, centerOfMass) - math.transform(body, centerOfMass)) / Timestep;
                var angular = RotationVector(math.mul(track.Rotation, math.inverse(body.rot))) / Timestep;
                var gain = math.saturate(track.Gain);
                World.SetLinearVelocity(index, math.lerp(World.GetLinearVelocity(index), ClampLength(linear, track.MaxLinearSpeed), gain));
                World.SetAngularVelocity(index, math.lerp(World.GetAngularVelocity(index), ClampLength(angular, track.MaxAngularSpeed), gain));
            }
        }
    }
}
