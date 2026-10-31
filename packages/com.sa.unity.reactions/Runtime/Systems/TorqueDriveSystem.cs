using SA.Motion.Mechanics;
using SA.Motion.Numerics;
using SA.Motion.Unity;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Extensions;
using Unity.Physics.Systems;
using Float3 = SA.Motion.Numerics.Float3;

namespace SA.Reactions
{
    [UpdateInGroup(typeof(PhysicsSystemGroup))]
    [UpdateAfter(typeof(PoseDriveSystem))]
    [UpdateBefore(typeof(PhysicsSimulationGroup))]
    public partial struct TorqueDriveSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<TorqueDrive>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ref var singleton = ref SystemAPI.GetSingletonRW<PhysicsWorldSingleton>().ValueRW;
            state.Dependency = new DriveJob { World = singleton.PhysicsWorld, Timestep = SystemAPI.Time.DeltaTime }.Schedule(state.Dependency);
        }

        public static float ClampScale(float3 total, float maximum)
        {
            var length = math.length(total);
            if (!(length > 1e-9f) || !(maximum > 0f) || !math.isfinite(maximum) || length <= maximum)
                return 1f;
            return maximum / length;
        }

        public static float ResponseMax(in DriveBody body)
        {
            var inverse = PoseDriveMath.InverseInertiaWorld(body);
            return math.max(0f, math.max(inverse.c0.x, math.max(inverse.c1.y, inverse.c2.z)));
        }

        [BurstCompile]
        partial struct DriveJob : IJobEntity
        {
            public PhysicsWorld World;
            public float Timestep;

            DriveBody Read(int index)
            {
                var orientation = World.Bodies[index].WorldFromBody.rot;
                if (index >= World.NumDynamicBodies)
                    return DriveBody.Static(orientation);
                var inertiaOrientation = World.MotionDatas[index].WorldFromMotion.rot;
                return new DriveBody
                {
                    Orientation = orientation,
                    InertiaOrientation = inertiaOrientation,
                    InverseInertia = World.MotionVelocities[index].InverseInertia,
                    AngularVelocity = math.rotate(inertiaOrientation, World.MotionVelocities[index].AngularVelocity)
                };
            }

            void Execute(Entity entity, in TorqueDrive drive)
            {
                var childIndex = World.GetRigidBodyIndex(entity);
                if (childIndex < 0 || childIndex >= World.NumDynamicBodies || Timestep <= 0f || !(drive.MaxTorque > 0f))
                    return;
                var child = Read(childIndex);
                var hold = new DriveGains { Stiffness = drive.Stiffness, Damping = 0f, MaxTorque = float.MaxValue };
                var loose = new DriveGains { Stiffness = 0f, Damping = drive.Damping, MaxTorque = float.MaxValue };
                var current = Conversions.ToMotion(child.Orientation);
                var target = Conversions.ToMotion(drive.TargetWorld);
                var parentIndex = drive.Parent == Entity.Null ? -1 : World.GetRigidBodyIndex(drive.Parent);
                var parent = parentIndex >= 0 ? Read(parentIndex) : DriveBody.Static(quaternion.identity);
                var relativeSpin = parentIndex >= 0 ? child.AngularVelocity - parent.AngularVelocity : child.AngularVelocity;
                var push = Conversions.ToUnity(QuaternionDrive.Torque(current, target, Float3.Zero, Float3.Zero, hold));
                var drag = Conversions.ToUnity(QuaternionDrive.Torque(current, current, Conversions.ToMotion(relativeSpin), Float3.Zero, loose));
                var scale = ClampScale(push + drag, drive.MaxTorque);
                var wide = ResponseMax(child) + (parentIndex >= 0 && parentIndex < World.NumDynamicBodies ? ResponseMax(parent) : 0f);
                var pull = drive.Damping * Timestep * wide;
                var factor = pull > 0f && math.isfinite(pull) ? 1f / (1f + pull) : 1f;
                var impulse = push * (scale * Timestep) + drag * (scale * Timestep * factor);
                if (!math.all(math.isfinite(impulse)))
                    return;
                World.ApplyAngularImpulse(childIndex, impulse);
                if (parentIndex >= 0 && parentIndex < World.NumDynamicBodies)
                    World.ApplyAngularImpulse(parentIndex, -impulse);
            }
        }
    }
}
