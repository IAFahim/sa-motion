using System.Diagnostics;
using NUnit.Framework;
using SA.Motion.Legs;
using SA.Motion.Unity;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace SA.Reactions.Tests
{
    public class LegIKCostTests
    {
        const int Characters = 1000;
        const int Updates = 120;

        [Test, Explicit("Prints the cost of LegIKSystem for 1000 characters")]
        public void CostPerLegOfTheSolverAndItsGroundRays()
        {
            var world = new World("SA.LegIK.Cost");
            var manager = world.EntityManager;
            var collider = BoxCollider.Create(new BoxGeometry { Center = new float3(0, -0.5f, 0), Size = new float3(400, 1, 400), Orientation = quaternion.identity, BevelRadius = 0 }, CollisionFilter.Default, Material.Default);
            var physics = new PhysicsWorld(1, 0, 0);
            try
            {
                var floor = manager.CreateEntity(typeof(LocalTransform));
                manager.SetComponentData(floor, LocalTransform.FromPosition(float3.zero));
                var bodies = physics.Bodies;
                bodies[0] = new RigidBody { Collider = collider, Entity = floor, WorldFromBody = RigidTransform.identity, Scale = 1f };
                physics.CollisionWorld.BuildBroadphase(ref physics, 1f / 60f, new float3(0, -9.81f, 0));
                manager.CreateSingleton(new PhysicsWorldSingleton { PhysicsWorld = physics });
                for (var c = 0; c < Characters; c++)
                {
                    var rig = Node(manager, Entity.Null, new float3(c % 40 * 2f - 40f, 0, c / 40 * 2f - 20f));
                    manager.AddComponent<CpuEngine>(rig);
                    var upper = Node(manager, rig, new float3(0, 2, 0));
                    var lower = Node(manager, upper, new float3(0, -0.9f, 0.1f));
                    var foot = Node(manager, lower, new float3(0, -0.95f, -0.1f));
                    var boneBuffer = manager.AddBuffer<BoneEntity>(rig);
                    boneBuffer.Add(new BoneEntity { Value = upper });
                    boneBuffer.Add(new BoneEntity { Value = lower });
                    boneBuffer.Add(new BoneEntity { Value = foot });
                    manager.AddComponentData(rig, new LegIK { Params = LegParams.Gentle, Weight = 1f, BodyScale = 1f, GroundMask = uint.MaxValue });
                    manager.AddBuffer<LegIKFoot>(rig).Add(new LegIKFoot
                    {
                        Upper = upper,
                        Lower = lower,
                        Foot = foot,
                        Shape = new FootShape { Heel = Conversions.ToMotion(new float3(0, -0.15f, -0.05f)), Ball = Conversions.ToMotion(new float3(0, -0.15f, 0.12f)) }
                    });
                }
                var system = world.GetOrCreateSystem<LegIKSystem>();
                for (var i = 0; i < 20; i++)
                    Step(world, manager, system, i);
                var watch = Stopwatch.StartNew();
                for (var i = 0; i < Updates; i++)
                    Step(world, manager, system, 20 + i);
                var microseconds = watch.Elapsed.TotalMilliseconds * 1000.0 / Updates;
                UnityEngine.Debug.Log("LegIKSystem: " + microseconds.ToString("F1") + " us per update for " + Characters + " legs (" + (microseconds * 1000.0 / Characters).ToString("F0") + " ns per leg)");
            }
            finally
            {
                manager.CompleteAllTrackedJobs();
                world.Dispose();
                physics.Dispose();
                collider.Dispose();
            }
        }

        static void Step(World world, EntityManager manager, SystemHandle system, int frame)
        {
            world.SetTime(new TimeData(frame / 60.0, 1f / 60f));
            system.Update(world.Unmanaged);
            manager.CompleteAllTrackedJobs();
        }

        static Entity Node(EntityManager manager, Entity parent, float3 position)
        {
            var entity = manager.CreateEntity(typeof(LocalTransform));
            manager.SetComponentData(entity, LocalTransform.FromPosition(position));
            if (parent != Entity.Null)
                manager.AddComponentData(entity, new Parent { Value = parent });
            return entity;
        }
    }
}
