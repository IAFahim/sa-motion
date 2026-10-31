using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using Collider = Unity.Physics.Collider;

namespace SA.Reactions.Tests
{
    public class PoseTrackTests
    {
        static World Create(float3 gravity)
        {
            var world = new World("SA.Reactions.Track");
            var systems = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.All)
                .Where(type => type.Assembly.GetName().Name == "Unity.Physics" || type == typeof(PoseDriveSystem) || type == typeof(PoseTrackSystem)).ToList();
            DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, systems);
            var step = PhysicsStep.Default;
            step.Gravity = gravity;
            world.EntityManager.CreateSingleton(step);
            return world;
        }

        static Entity Body(World world, BlobAssetReference<Collider> shape, float3 position)
        {
            var manager = world.EntityManager;
            var entity = manager.CreateEntity(typeof(LocalTransform), typeof(PhysicsCollider), typeof(PhysicsWorldIndex), typeof(PhysicsVelocity), typeof(PhysicsMass));
            manager.SetComponentData(entity, LocalTransform.FromPosition(position));
            manager.SetComponentData(entity, new PhysicsCollider { Value = shape });
            manager.SetComponentData(entity, PhysicsMass.CreateDynamic(shape.Value.MassProperties, 5f));
            return entity;
        }

        static void Run(World world, int steps, float step)
        {
            var group = world.GetExistingSystemManaged<PhysicsSystemGroup>();
            var time = 0.0;
            for (var i = 0; i < steps; i++)
            {
                time += step;
                world.SetTime(new TimeData(time, step));
                group.Update();
                world.EntityManager.CompleteAllTrackedJobs();
            }
        }

        [Test]
        public void TrackingHoldsABodyAgainstGravityAtItsTarget()
        {
            using var world = Create(new float3(0f, -9.81f, 0f));
            var shape = SphereCollider.Create(new SphereGeometry { Center = float3.zero, Radius = 0.2f }, CollisionFilter.Default, Material.Default);
            try
            {
                var body = Body(world, shape, new float3(0f, 1f, 0f));
                world.EntityManager.AddComponentData(body, new PoseTrack { Position = new float3(0f, 3f, 0f), Rotation = quaternion.identity, Gain = 0.15f, MaxLinearSpeed = 3f, MaxAngularSpeed = 20f });
                Run(world, 180, 1f / 60f);
                var position = world.EntityManager.GetComponentData<LocalTransform>(body).Position;
                Assert.AreEqual(3f, position.y, 0.15f);
            }
            finally
            {
                shape.Dispose();
            }
        }

        [Test]
        public void ZeroGainLeavesTheBodyFalling()
        {
            using var world = Create(new float3(0f, -9.81f, 0f));
            var shape = SphereCollider.Create(new SphereGeometry { Center = float3.zero, Radius = 0.2f }, CollisionFilter.Default, Material.Default);
            try
            {
                var body = Body(world, shape, new float3(0f, 5f, 0f));
                world.EntityManager.AddComponentData(body, new PoseTrack { Position = new float3(0f, 8f, 0f), Rotation = quaternion.identity, Gain = 0f, MaxLinearSpeed = 3f, MaxAngularSpeed = 20f });
                Run(world, 30, 1f / 60f);
                Assert.Less(world.EntityManager.GetComponentData<LocalTransform>(body).Position.y, 4.9f);
            }
            finally
            {
                shape.Dispose();
            }
        }
    }
}
