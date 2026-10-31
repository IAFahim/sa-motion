using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace SA.Reactions.Tests
{
    public class FootIKTests
    {
        static Entity Node(EntityManager manager, Entity parent, float3 position)
        {
            var entity = manager.CreateEntity(typeof(LocalTransform));
            manager.SetComponentData(entity, LocalTransform.FromPosition(position));
            if (parent != Entity.Null)
                manager.AddComponentData(entity, new Parent { Value = parent });
            return entity;
        }

        static float4x4 Matrix(EntityManager manager, Entity node)
        {
            var matrix = float4x4.identity;
            while (node != Entity.Null)
            {
                matrix = math.mul(manager.GetComponentData<LocalTransform>(node).ToMatrix(), matrix);
                node = manager.HasComponent<Parent>(node) ? manager.GetComponentData<Parent>(node).Value : Entity.Null;
            }
            return matrix;
        }

        [Test]
        public void PlantedFootStaysInWorldFollowsSupportAndYieldsToRagdoll()
        {
            using var world = new World("SA.FootIK");
            var manager = world.EntityManager;
            using var collider = BoxCollider.Create(new BoxGeometry
            {
                Center = new float3(0, -0.5f, 0), Size = new float3(10, 1, 10), Orientation = quaternion.identity, BevelRadius = 0
            }, CollisionFilter.Default, Material.Default);
            var physics = new PhysicsWorld(1, 0, 0);
            try
            {
                var floor = Node(manager, Entity.Null, float3.zero);
                var physicsBodies = physics.Bodies;
                physicsBodies[0] = new RigidBody { Collider = collider, Entity = floor, WorldFromBody = RigidTransform.identity, Scale = 1f };
                physics.CollisionWorld.BuildBroadphase(ref physics, 1f / 60f, new float3(0, -9.81f, 0));
                manager.CreateSingleton(new PhysicsWorldSingleton { PhysicsWorld = physics });
                var rig = Node(manager, Entity.Null, float3.zero);
                manager.AddComponent<CpuEngine>(rig);
                var upper = Node(manager, rig, new float3(0, 2, 0));
                var lower = Node(manager, upper, new float3(0, -0.9f, 0.1f));
                var foot = Node(manager, lower, new float3(0, -0.95f, -0.1f));
                var bones = manager.AddBuffer<BoneEntity>(rig);
                bones.Add(new BoneEntity { Value = upper });
                bones.Add(new BoneEntity { Value = lower });
                bones.Add(new BoneEntity { Value = foot });
                manager.AddBuffer<FootIKLeg>(rig).Add(new FootIKLeg
                {
                    Upper = upper, Lower = lower, Foot = foot, SoleHeight = 0.15f,
                    Pole = new float3(0, 0, 1), FootUp = math.up()
                });
                var system = world.GetOrCreateSystem<FootIKSystem>();
                for (var frame = -8; frame < 8; frame++)
                {
                    manager.SetComponentData(rig, LocalTransform.FromPosition(new float3(math.max(0, frame) * 0.015f, 0, 0)));
                    manager.SetComponentData(upper, LocalTransform.FromPosition(new float3(0, 2, 0)));
                    manager.SetComponentData(lower, LocalTransform.FromPosition(new float3(0, -0.9f, 0.1f)));
                    manager.SetComponentData(foot, LocalTransform.FromPosition(new float3(0, -0.95f, -0.1f)));
                    world.SetTime(new TimeData((frame + 9) / 60.0, 1f / 60f));
                    system.Update(world.Unmanaged);
                    manager.CompleteAllTrackedJobs();
                    Assert.IsTrue(manager.GetBuffer<FootIKLeg>(rig)[0].Lock.Locked);
                    Assert.Less(math.distance(new float3(0, 0.15f, 0), Matrix(manager, foot).c3.xyz), 0.002f);
                }
                manager.SetComponentData(floor, LocalTransform.FromPosition(new float3(0.04f, 0, 0)));
                system.Update(world.Unmanaged);
                manager.CompleteAllTrackedJobs();
                Assert.AreEqual(0.04f, Matrix(manager, foot).c3.x, 0.002f);
                manager.SetComponentData(upper, LocalTransform.FromPosition(new float3(0, 2.2f, 0)));
                manager.SetComponentData(lower, LocalTransform.FromPosition(new float3(0, -0.9f, 0.1f)));
                manager.SetComponentData(foot, LocalTransform.FromPosition(new float3(0, -0.95f, -0.1f)));
                system.Update(world.Unmanaged);
                manager.CompleteAllTrackedJobs();
                var released = manager.GetBuffer<FootIKLeg>(rig)[0].Lock;
                Assert.IsFalse(released.Locked);
                Assert.That(released.Weight, Is.InRange(0.83f, 0.84f));
                manager.AddComponentData(rig, new Ragdoll { Spawned = 1 });
                var rotation = manager.GetComponentData<LocalTransform>(upper).Rotation;
                system.Update(world.Unmanaged);
                manager.CompleteAllTrackedJobs();
                Assert.IsFalse(manager.GetBuffer<FootIKLeg>(rig)[0].Lock.Locked);
                Assert.AreEqual(rotation, manager.GetComponentData<LocalTransform>(upper).Rotation);
            }
            finally
            {
                manager.CompleteAllTrackedJobs();
                physics.Dispose();
            }
        }
    }
}
