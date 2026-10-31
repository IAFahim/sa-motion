using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Extensions;
using Unity.Physics.Systems;

namespace SA.Reactions
{
    [UpdateInGroup(typeof(PhysicsSystemGroup))]
    [UpdateAfter(typeof(PhysicsInitializeGroup))]
    [UpdateBefore(typeof(PhysicsSimulationGroup))]
    public partial struct PoseDriveSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PoseDrive>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ref var singleton = ref SystemAPI.GetSingletonRW<PhysicsWorldSingleton>().ValueRW;
            state.Dependency = new DriveJob { World = singleton.PhysicsWorld, Timestep = SystemAPI.Time.DeltaTime }.Schedule(state.Dependency);
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

            void Execute(Entity entity, in PoseDrive drive, ref PoseDriveReport report)
            {
                report = default;
                var childIndex = World.GetRigidBodyIndex(entity);
                var parentIndex = World.GetRigidBodyIndex(drive.Parent);
                if (childIndex < 0 || parentIndex < 0 || childIndex >= World.NumDynamicBodies || Timestep <= 0f)
                    return;
                var impulse = PoseDriveMath.Impulse(Read(parentIndex), Read(childIndex), drive, Timestep, out var error, out var singular);
                report.Error = error;
                report.Singular = singular;
                report.Applied = true;
                report.ChildImpulse = impulse;
                World.ApplyAngularImpulse(childIndex, impulse);
                if (parentIndex < World.NumDynamicBodies)
                {
                    report.ParentImpulse = -impulse;
                    World.ApplyAngularImpulse(parentIndex, -impulse);
                }
            }
        }
    }
}
