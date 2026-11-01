using NUnit.Framework;
using SA.Motion.Legs;
using SA.Motion.Unity;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace SA.Reactions.Tests
{
    public class LegIKTests
    {
        World world;
        EntityManager manager;
        PhysicsWorld physics;
        BlobAssetReference<Unity.Physics.Collider> collider;
        Entity floor;
        Entity rig;
        Entity upper;
        Entity lower;
        Entity foot;
        int frame;

        static readonly float3 UpperRest = new float3(0, 2, 0);
        static readonly float3 LowerRest = new float3(0, -0.9f, 0.1f);
        static readonly float3 FootRest = new float3(0, -0.95f, -0.1f);

        Entity Node(Entity parent, float3 position)
        {
            var entity = manager.CreateEntity(typeof(LocalTransform));
            manager.SetComponentData(entity, LocalTransform.FromPosition(position));
            if (parent != Entity.Null)
                manager.AddComponentData(entity, new Parent { Value = parent });
            return entity;
        }

        float4x4 Matrix(Entity node)
        {
            var matrix = float4x4.identity;
            while (node != Entity.Null)
            {
                matrix = math.mul(manager.GetComponentData<LocalTransform>(node).ToMatrix(), matrix);
                node = manager.HasComponent<Parent>(node) ? manager.GetComponentData<Parent>(node).Value : Entity.Null;
            }
            return matrix;
        }

        [SetUp]
        public void SetUp()
        {
            world = new World("SA.LegIK");
            manager = world.EntityManager;
            collider = BoxCollider.Create(new BoxGeometry { Center = new float3(0, -0.5f, 0), Size = new float3(10, 1, 10), Orientation = quaternion.identity, BevelRadius = 0 }, CollisionFilter.Default, Material.Default);
            physics = new PhysicsWorld(1, 0, 0);
            floor = Node(Entity.Null, float3.zero);
            var bodies = physics.Bodies;
            bodies[0] = new RigidBody { Collider = collider, Entity = floor, WorldFromBody = RigidTransform.identity, Scale = 1f };
            physics.CollisionWorld.BuildBroadphase(ref physics, 1f / 60f, new float3(0, -9.81f, 0));
            manager.CreateSingleton(new PhysicsWorldSingleton { PhysicsWorld = physics });
            rig = Node(Entity.Null, float3.zero);
            manager.AddComponent<CpuEngine>(rig);
            upper = Node(rig, UpperRest);
            lower = Node(upper, LowerRest);
            foot = Node(lower, FootRest);
            var bones = manager.AddBuffer<BoneEntity>(rig);
            bones.Add(new BoneEntity { Value = upper });
            bones.Add(new BoneEntity { Value = lower });
            bones.Add(new BoneEntity { Value = foot });
            manager.AddComponentData(rig, new LegIK { Params = LegParams.Standard, Weight = 1f, BodyScale = 1f, GroundMask = uint.MaxValue });
            manager.AddBuffer<LegIKFoot>(rig).Add(new LegIKFoot
            {
                Upper = upper,
                Lower = lower,
                Foot = foot,
                Shape = new FootShape { Heel = Conversions.ToMotion(new float3(0, -0.15f, -0.05f)), Ball = Conversions.ToMotion(new float3(0, -0.15f, 0.12f)) }
            });
            frame = 0;
        }

        [TearDown]
        public void TearDown()
        {
            manager.CompleteAllTrackedJobs();
            world.Dispose();
            physics.Dispose();
            collider.Dispose();
        }

        void Animate(float3 rigPosition, float lift)
        {
            manager.SetComponentData(rig, LocalTransform.FromPosition(rigPosition));
            manager.SetComponentData(upper, LocalTransform.FromPosition(UpperRest + new float3(0, lift, 0)));
            manager.SetComponentData(lower, LocalTransform.FromPosition(LowerRest));
            manager.SetComponentData(foot, LocalTransform.FromPosition(FootRest));
            frame++;
            world.SetTime(new TimeData(frame / 60.0, 1f / 60f));
            world.GetOrCreateSystem<LegIKSystem>().Update(world.Unmanaged);
            manager.CompleteAllTrackedJobs();
        }

        LegIKFoot Leg => manager.GetBuffer<LegIKFoot>(rig)[0];

        [Test]
        public void PlantedFootStaysInTheWorldWhileTheBodyDriftsAndFollowsItsSupport()
        {
            for (var i = 0; i < 8; i++)
                Animate(float3.zero, 0f);
            for (var i = 1; i <= 8; i++)
            {
                Animate(new float3(0.015f * i, 0, 0), 0f);
                Assert.AreEqual(1, Leg.State.Planted);
                Assert.Less(math.distance(new float3(0, 0.15f, 0), Matrix(foot).c3.xyz), 0.002f);
            }
            manager.SetComponentData(floor, LocalTransform.FromPosition(new float3(0.04f, 0, 0)));
            Animate(new float3(0.12f, 0, 0), 0f);
            Assert.AreEqual(0.04f, Matrix(foot).c3.x, 0.002f);
        }

        [Test]
        public void ReleaseNeverTeleportsAndThenFollowsTheAnimation()
        {
            for (var i = 0; i < 8; i++)
                Animate(float3.zero, 0f);
            var planted = Matrix(foot).c3.xyz;
            Animate(float3.zero, 0.2f);
            Assert.AreEqual(0, Leg.State.Planted);
            var released = Matrix(foot).c3.xyz;
            Assert.That(released.y, Is.InRange(planted.y - 0.002f, 0.35f + 0.002f));
            for (var i = 0; i < 30; i++)
                Animate(float3.zero, 0.2f);
            Assert.Less(math.distance(new float3(0, 0.35f, 0), Matrix(foot).c3.xyz), 0.01f);
        }

        [Test]
        public void ClipsWithoutFootIKFadeTheSolverOut()
        {
            var clips = manager.AddBuffer<ClipAsset>(rig);
            clips.Add(new ClipAsset { FootIK = 0f });
            var unticked = Names.ClipAt(clips.AsNativeArray().AsReadOnlySpan(), 0);
            manager.AddBuffer<ClipState>(rig).Add(new ClipState { Clip = unticked, Weight = 1f, Fade = 1f });
            for (var i = 0; i < 8; i++)
                Animate(float3.zero, 0f);
            for (var i = 1; i <= 60; i++)
                Animate(new float3(0.005f * i, 0, 0), 0f);
            Assert.Less(manager.GetComponentData<LegIK>(rig).State.Weight.Value, 0.01f);
            Assert.Less(math.distance(new float3(0.3f, 0.15f, 0), Matrix(foot).c3.xyz), 0.01f, "with the clip's Foot IK unticked the foot follows the animation");
        }

        [Test]
        public void DisablingOrSpawningTheRagdollLeavesTheAnimatedPose()
        {
            for (var i = 0; i < 8; i++)
                Animate(float3.zero, 0f);
            manager.SetComponentEnabled<LegIK>(rig, false);
            Animate(new float3(0.1f, 0, 0), 0f);
            Assert.AreEqual(quaternion.identity, manager.GetComponentData<LocalTransform>(upper).Rotation);
            manager.SetComponentEnabled<LegIK>(rig, true);
            manager.AddComponentData(rig, new Ragdoll { Spawned = 1 });
            Animate(new float3(0.2f, 0, 0), 0f);
            Assert.AreEqual(0, Leg.State.Planted);
            Assert.AreEqual(quaternion.identity, manager.GetComponentData<LocalTransform>(upper).Rotation);
        }
    }
}
