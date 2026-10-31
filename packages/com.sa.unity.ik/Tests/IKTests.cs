using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace SA.IK.Tests
{
    public class IKTests
    {
        static Entity Node(EntityManager manager, Entity parent, float3 position)
        {
            var node = manager.CreateEntity(typeof(LocalTransform));
            manager.SetComponentData(node, LocalTransform.FromPosition(position));
            if (parent != Entity.Null)
                manager.AddComponentData(node, new Parent { Value = parent });
            return node;
        }

        static float4x4 Matrix(EntityManager manager, Entity node)
        {
            var result = float4x4.identity;
            while (node != Entity.Null)
            {
                result = math.mul(manager.GetComponentData<LocalTransform>(node).ToMatrix(), result);
                node = manager.HasComponent<Parent>(node) ? manager.GetComponentData<Parent>(node).Value : Entity.Null;
            }
            return result;
        }

        [Test]
        public void HeadFollowsMovingTargetUnderRotatedParentAndYieldsToPhysics()
        {
            using var world = new World("SA.LookAt");
            var manager = world.EntityManager;
            var rig = Node(manager, Entity.Null, new float3(2, 0, 3));
            manager.SetComponentData(rig, LocalTransform.FromPositionRotation(new float3(2, 0, 3), quaternion.RotateY(math.PI / 2)));
            manager.AddComponent<CpuEngine>(rig);
            manager.AddComponent<IKEnabled>(rig);
            var head = Node(manager, rig, new float3(0, 1, 0));
            var target = Node(manager, Entity.Null, new float3(2, 1, 5));
            manager.AddBuffer<BoneEntity>(rig).Add(new BoneEntity { Value = head });
            manager.AddBuffer<LookAtConstraint>(rig).Add(new LookAtConstraint
            { Bone = head, Target = target, Forward = new float3(0, 0, 1), Weight = 1, MaxRadians = math.PI });
            var system = world.GetOrCreateSystem<IKSystem>();
            world.SetTime(new TimeData(0.02, 0.02f));
            system.Update(world.Unmanaged);
            manager.CompleteAllTrackedJobs();
            Assert.Less(math.distance(Matrix(manager, head).c2.xyz, new float3(0, 0, 1)), 1e-4f);
            manager.SetComponentData(target, LocalTransform.FromPosition(new float3(0, 1, 3)));
            system.Update(world.Unmanaged);
            manager.CompleteAllTrackedJobs();
            Assert.Less(math.distance(Matrix(manager, head).c2.xyz, new float3(-1, 0, 0)), 1e-4f);
            manager.AddComponent<ExternalPoseControl>(rig);
            var rotation = manager.GetComponentData<LocalTransform>(head).Rotation;
            manager.SetComponentData(target, LocalTransform.FromPosition(new float3(2, 4, 3)));
            system.Update(world.Unmanaged);
            manager.CompleteAllTrackedJobs();
            Assert.AreEqual(rotation, manager.GetComponentData<LocalTransform>(head).Rotation);
        }

        [Test]
        public void PhysicalControllersReceiveLookTargetsWithoutTeleportingBones()
        {
            using var world = new World("SA.IK.Targets");
            var manager = world.EntityManager;
            var rig = Node(manager, Entity.Null, float3.zero);
            manager.AddComponent<IKEnabled>(rig);
            manager.AddComponent<ExternalPoseControl>(rig);
            manager.AddComponentData(rig, new LocalToWorld { Value = float4x4.identity });
            var head = Node(manager, rig, new float3(0, 1, 0));
            var target = Node(manager, Entity.Null, new float3(2, 1, 0));
            manager.AddBuffer<BoneEntity>(rig).Add(new BoneEntity { Value = head });
            manager.AddBuffer<LookAtConstraint>(rig).Add(new LookAtConstraint
            { Bone = head, Target = target, Forward = new float3(0, 0, 1), Weight = 1, MaxRadians = math.PI });
            using var positions = new Unity.Collections.NativeArray<float3>(new[] { new float3(0, 1, 0) }, Unity.Collections.Allocator.Temp);
            using var rotations = new Unity.Collections.NativeArray<quaternion>(new[] { quaternion.identity }, Unity.Collections.Allocator.Temp);
            ModelConstraints.Apply(manager, rig, positions, rotations);
            Assert.Less(math.distance(math.rotate(rotations[0], new float3(0, 0, 1)), new float3(1, 0, 0)), 1e-4f);
            Assert.AreEqual(quaternion.identity, manager.GetComponentData<LocalTransform>(head).Rotation);
        }

        [Test]
        public void ReachTracksAnEntityTargetAndPreservesBoneLengths()
        {
            using var world = new World("SA.Reach");
            var manager = world.EntityManager;
            var rig = Node(manager, Entity.Null, float3.zero);
            manager.AddComponent<CpuEngine>(rig);
            manager.AddComponent<IKEnabled>(rig);
            var upper = Node(manager, rig, new float3(0, 2, 0));
            var middle = Node(manager, upper, new float3(0, -1, 0));
            var end = Node(manager, middle, new float3(0, -1, 0));
            var target = Node(manager, Entity.Null, new float3(0.5f, 0.6f, 0.2f));
            var bones = manager.AddBuffer<BoneEntity>(rig);
            bones.Add(new BoneEntity { Value = upper });
            bones.Add(new BoneEntity { Value = middle });
            bones.Add(new BoneEntity { Value = end });
            manager.AddBuffer<TwoBoneConstraint>(rig).Add(new TwoBoneConstraint
            { Upper = upper, Middle = middle, End = end, Target = target, PolePosition = new float3(0, 1, 1), Weight = 1 });
            var system = world.GetOrCreateSystem<IKSystem>();
            world.SetTime(new TimeData(0.02, 0.02f));
            system.Update(world.Unmanaged);
            manager.CompleteAllTrackedJobs();
            Assert.Less(math.distance(Matrix(manager, end).c3.xyz, new float3(0.5f, 0.6f, 0.2f)), 1e-4f);
            Assert.AreEqual(1f, math.distance(Matrix(manager, upper).c3.xyz, Matrix(manager, middle).c3.xyz), 1e-4f);
            Assert.AreEqual(1f, math.distance(Matrix(manager, middle).c3.xyz, Matrix(manager, end).c3.xyz), 1e-4f);
        }
    }
}
