using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using Collider = Unity.Physics.Collider;
using Math = Unity.Physics.Math;

namespace SA.Reactions.Tests
{
    public class RagdollRefitTests
    {
        sealed class Rig : System.IDisposable
        {
            public World World;
            public Entity Root;
            public Entity Bone0;
            public Entity Bone1;
            public Entity Body0;
            public Entity Body1;
            public Entity Joint;
            public BlobAssetReference<Collider> Baked0;
            public BlobAssetReference<Collider> Baked1;

            public EntityManager Manager => World.EntityManager;

            public void Dispose()
            {
                var manager = World.EntityManager;
                if (manager.HasBuffer<RagdollBody>(Root))
                {
                    var bodies = manager.GetBuffer<RagdollBody>(Root);
                    for (var i = 0; i < bodies.Length; i++)
                    {
                        if (!manager.HasComponent<RagdollRefitTag>(bodies[i].Body))
                            continue;
                        var owned = manager.GetComponentData<RagdollRefitTag>(bodies[i].Body).Owned;
                        if (owned.IsCreated && !owned.Equals(Baked0) && !owned.Equals(Baked1))
                            owned.Dispose();
                    }
                }
                if (Baked0.IsCreated)
                    Baked0.Dispose();
                if (Baked1.IsCreated)
                    Baked1.Dispose();
                World.Dispose();
            }
        }

        static Rig CreateRig(byte jointMode, float3 velocity)
        {
            var rig = new Rig { World = new World("SA.RagdollRefit") };
            var manager = rig.Manager;
            rig.Baked0 = CapsuleCollider.Create(new CapsuleGeometry
            {
                Vertex0 = new float3(0f, 0f, 0f),
                Vertex1 = new float3(0f, 0.4f, 0f),
                Radius = 0.1f
            }, CollisionFilter.Default, Material.Default);
            rig.Baked1 = SphereCollider.Create(new SphereGeometry
            {
                Center = new float3(0f, 0.05f, 0f),
                Radius = 0.1f
            }, CollisionFilter.Default, Material.Default);
            rig.Bone0 = manager.CreateEntity(typeof(LocalToWorld));
            manager.SetComponentData(rig.Bone0, new LocalToWorld { Value = float4x4.identity });
            rig.Bone1 = manager.CreateEntity(typeof(LocalToWorld));
            manager.SetComponentData(rig.Bone1, new LocalToWorld { Value = float4x4.identity });
            rig.Body0 = manager.CreateEntity(typeof(LocalTransform), typeof(PhysicsCollider), typeof(PhysicsVelocity), typeof(PhysicsMass));
            manager.SetComponentData(rig.Body0, LocalTransform.FromPosition(new float3(0f, 1f, 0f)));
            manager.SetComponentData(rig.Body0, new PhysicsCollider { Value = rig.Baked0 });
            manager.SetComponentData(rig.Body0, PhysicsMass.CreateDynamic(rig.Baked0.Value.MassProperties, 5f));
            manager.SetComponentData(rig.Body0, new PhysicsVelocity { Linear = velocity });
            rig.Body1 = manager.CreateEntity(typeof(LocalTransform), typeof(PhysicsCollider), typeof(PhysicsVelocity), typeof(PhysicsMass));
            manager.SetComponentData(rig.Body1, LocalTransform.FromPosition(new float3(0f, 0.6f, 0f)));
            manager.SetComponentData(rig.Body1, new PhysicsCollider { Value = rig.Baked1 });
            manager.SetComponentData(rig.Body1, PhysicsMass.CreateDynamic(rig.Baked1.Value.MassProperties, 4f));
            manager.SetComponentData(rig.Body1, new PhysicsVelocity { Linear = velocity });
            rig.Root = manager.CreateEntity(typeof(Ragdoll));
            manager.SetComponentData(rig.Root, new Ragdoll { Spawned = 1, JointMode = jointMode });
            var bodies = manager.AddBuffer<RagdollBody>(rig.Root);
            bodies.Add(new RagdollBody { Body = rig.Body0, BoneIndex = 0, ParentRole = -1 });
            bodies.Add(new RagdollBody { Body = rig.Body1, BoneIndex = 1, ParentRole = 0 });
            var specs = manager.AddBuffer<RagdollRoleSpec>(rig.Root);
            specs.Add(new RagdollRoleSpec
            {
                Bone = rig.Bone0,
                Role = 0,
                ParentRole = -1,
                Shape = rig.Baked0,
                Mass = 35f,
                JointFrameInChild = quaternion.identity,
                ConeAngle = 0.5f,
                PlaneRange = new float2(-0.5f, 0.5f),
                TwistRange = new float2(-0.4f, 0.4f),
                Frequency = 14f,
                DampingRatio = 1f,
                MaxTorque = 100f,
                TrackGain = 0.06f
            });
            specs.Add(new RagdollRoleSpec
            {
                Bone = rig.Bone1,
                Role = 1,
                ParentRole = 0,
                Shape = rig.Baked1,
                Mass = 10f,
                AnchorInParent = new float3(0f, -0.4f, 0f),
                JointFrameInChild = quaternion.identity,
                ConeAngle = 0.5f,
                PlaneRange = new float2(-0.5f, 0.5f),
                TwistRange = new float2(-0.4f, 0.4f),
                Frequency = 14f,
                DampingRatio = 1f,
                MaxTorque = 100f,
                TrackGain = 0.06f
            });
            PhysicsJoint.CreateRagdoll(
                new BodyFrame(new RigidTransform(quaternion.identity, new float3(0f, -0.4f, 0f))),
                new BodyFrame(new RigidTransform(quaternion.identity, float3.zero)),
                0.5f,
                new Math.FloatRange(-0.5f, 0.5f),
                new Math.FloatRange(-0.4f, 0.4f),
                out var joint,
                out _);
            rig.Joint = manager.CreateEntity(typeof(PhysicsJoint), typeof(PhysicsConstrainedBodyPair), typeof(PhysicsWorldIndex));
            manager.SetComponentData(rig.Joint, joint);
            manager.SetComponentData(rig.Joint, new PhysicsConstrainedBodyPair(rig.Body0, rig.Body1, false));
            return rig;
        }

        static NativeArray<RagdollRefitSnapshot> Snapshots()
        {
            var snapshots = new NativeArray<RagdollRefitSnapshot>(2, Allocator.Temp);
            snapshots[0] = new RagdollRefitSnapshot
            {
                ColliderCenter = new float3(0f, 0.2f, 0f),
                ColliderRadius = 0.12f,
                ColliderHeight = 0.5f,
                MassKg = 35f,
                AnchorInParent = float3.zero
            };
            snapshots[1] = new RagdollRefitSnapshot
            {
                ColliderCenter = new float3(0f, 0.05f, 0f),
                ColliderRadius = 0.12f,
                ColliderHeight = 0.24f,
                MassKg = 10f,
                AnchorInParent = new float3(0f, -0.4f, 0f)
            };
            return snapshots;
        }

        static unsafe CapsuleGeometry ReadCapsule(BlobAssetReference<Collider> shape)
        {
            return ((CapsuleCollider*)shape.GetUnsafePtr())->Geometry;
        }

        static unsafe SphereGeometry ReadSphere(BlobAssetReference<Collider> shape)
        {
            return ((SphereCollider*)shape.GetUnsafePtr())->Geometry;
        }

        [Test]
        public unsafe void CommittedSnapshotOverwritesColliderMassAndJointAnchor()
        {
            using var rig = CreateRig(0, new float3(1f, 2f, 3f));
            var manager = rig.Manager;
            using var snapshots = Snapshots();
            var error = RagdollRefit.SyncRig(manager, rig.Root, 0, 7, 7, snapshots);
            Assert.AreEqual(RagdollRefitError.None, error);
            var capsule = ReadCapsule(manager.GetComponentData<PhysicsCollider>(rig.Body0).Value);
            Assert.AreEqual(0.07f, capsule.Vertex0.y, 1e-5f);
            Assert.AreEqual(0.33f, capsule.Vertex1.y, 1e-5f);
            Assert.AreEqual(0f, capsule.Vertex0.x, 1e-6f);
            Assert.AreEqual(0.12f, capsule.Radius, 1e-6f);
            var sphere = ReadSphere(manager.GetComponentData<PhysicsCollider>(rig.Body1).Value);
            Assert.AreEqual(0.05f, sphere.Center.y, 1e-6f);
            Assert.AreEqual(0.12f, sphere.Radius, 1e-6f);
            Assert.AreEqual(1f / 35f, manager.GetComponentData<PhysicsMass>(rig.Body0).InverseMass, 1e-6f);
            Assert.AreEqual(1f / 10f, manager.GetComponentData<PhysicsMass>(rig.Body1).InverseMass, 1e-6f);
            var anchor = manager.GetComponentData<PhysicsJoint>(rig.Joint).BodyAFromJoint.Position;
            Assert.AreEqual(-0.4f, anchor.y, 1e-6f);
            Assert.AreEqual(0f, anchor.x, 1e-6f);
            var filter = manager.GetComponentData<PhysicsCollider>(rig.Body0).Value.Value.GetCollisionFilter();
            Assert.IsTrue(filter.Equals(CollisionFilter.Default));
            Assert.AreEqual(float3.zero, manager.GetComponentData<PhysicsVelocity>(rig.Body0).Linear);
            var state = manager.GetComponentData<RagdollRefitState>(rig.Root);
            Assert.AreEqual(7ul, state.AppliedRevision);
            Assert.AreEqual(1, state.CommittedCount);
            Assert.AreEqual(0, state.Diagnostic.Length);
        }

        [Test]
        public void MidSimulationChangeRefusesWithoutTouchingBodies()
        {
            using var rig = CreateRig(0, float3.zero);
            var manager = rig.Manager;
            using var snapshots = Snapshots();
            Assert.AreEqual(RagdollRefitError.None, RagdollRefit.SyncRig(manager, rig.Root, 0, 7, 7, snapshots));
            var before = manager.GetComponentData<PhysicsCollider>(rig.Body0).Value;
            manager.SetComponentData(rig.Body0, new PhysicsVelocity { Linear = new float3(1f, 0f, 0f) });
            var error = RagdollRefit.SyncRig(manager, rig.Root, 1, 9, 7, snapshots);
            Assert.AreEqual(RagdollRefitError.MidSimulationChange, error);
            var after = manager.GetComponentData<PhysicsCollider>(rig.Body0).Value;
            Assert.IsTrue(before.Equals(after));
            Assert.AreEqual(1f, manager.GetComponentData<PhysicsVelocity>(rig.Body0).Linear.x, 1e-6f);
            var state = manager.GetComponentData<RagdollRefitState>(rig.Root);
            Assert.AreEqual(7ul, state.AppliedRevision);
            Assert.AreEqual(1, state.CommittedCount);
            Assert.AreEqual(1, state.RefusedCount);
            Assert.AreEqual(RagdollRefit.MidSimulationMessage, state.Diagnostic.ToString());
        }

        [Test]
        public void InvalidSnapshotRollsBackPauseAndKeepsBodies()
        {
            using var rig = CreateRig(0, new float3(1f, 2f, 3f));
            var manager = rig.Manager;
            var snapshots = Snapshots();
            snapshots[1] = new RagdollRefitSnapshot
            {
                ColliderCenter = new float3(0f, 0.05f, 0f),
                ColliderRadius = 0f,
                ColliderHeight = 0.24f,
                MassKg = 10f,
                AnchorInParent = new float3(0f, -0.4f, 0f)
            };
            var massBefore = manager.GetComponentData<PhysicsMass>(rig.Body1);
            var error = RagdollRefit.SyncRig(manager, rig.Root, 0, 7, 7, snapshots);
            Assert.AreEqual(RagdollRefitError.InvalidSnapshot, error);
            Assert.IsTrue(rig.Baked1.Equals(manager.GetComponentData<PhysicsCollider>(rig.Body1).Value));
            Assert.AreEqual(massBefore.InverseMass, manager.GetComponentData<PhysicsMass>(rig.Body1).InverseMass);
            var velocity = manager.GetComponentData<PhysicsVelocity>(rig.Body1).Linear;
            Assert.AreEqual(1f, velocity.x, 1e-6f);
            Assert.AreEqual(2f, velocity.y, 1e-6f);
            Assert.AreEqual(3f, velocity.z, 1e-6f);
            Assert.IsFalse(manager.HasComponent<RagdollRefitTag>(rig.Body0));
            var state = manager.GetComponentData<RagdollRefitState>(rig.Root);
            Assert.AreEqual(0ul, state.AppliedRevision);
            Assert.AreEqual(0, state.CommittedCount);
            Assert.AreEqual(1, state.RefusedCount);
            Assert.AreEqual(RagdollRefit.InvalidSnapshotMessage, state.Diagnostic.ToString());
            snapshots.Dispose();
        }

        [Test]
        public void ScaledBoneRefusesInsteadOfScalingBones()
        {
            using var rig = CreateRig(0, new float3(1f, 0f, 0f));
            var manager = rig.Manager;
            manager.SetComponentData(rig.Bone1, new LocalToWorld
            {
                Value = new float4x4(new float4(2f, 0f, 0f, 0f), new float4(0f, 2f, 0f, 0f), new float4(0f, 0f, 2f, 0f), new float4(0f, 0f, 0f, 1f))
            });
            using var snapshots = Snapshots();
            var error = RagdollRefit.SyncRig(manager, rig.Root, 0, 7, 7, snapshots);
            Assert.AreEqual(RagdollRefitError.ScaledBone, error);
            Assert.IsTrue(rig.Baked0.Equals(manager.GetComponentData<PhysicsCollider>(rig.Body0).Value));
            Assert.AreEqual(1f, manager.GetComponentData<PhysicsVelocity>(rig.Body0).Linear.x, 1e-6f);
            var state = manager.GetComponentData<RagdollRefitState>(rig.Root);
            Assert.AreEqual(RagdollRefit.ScaledBoneMessage, state.Diagnostic.ToString());
        }

        [Test]
        public unsafe void SecondCommitReplacesTheOwnedBlob()
        {
            using var rig = CreateRig(0, float3.zero);
            var manager = rig.Manager;
            var snapshots = Snapshots();
            Assert.AreEqual(RagdollRefitError.None, RagdollRefit.SyncRig(manager, rig.Root, 0, 7, 7, snapshots));
            var first = manager.GetComponentData<RagdollRefitTag>(rig.Body0).Owned;
            snapshots[0] = new RagdollRefitSnapshot
            {
                ColliderCenter = new float3(0f, 0.25f, 0f),
                ColliderRadius = 0.15f,
                ColliderHeight = 0.6f,
                MassKg = 35f,
                AnchorInParent = float3.zero
            };
            snapshots[1] = new RagdollRefitSnapshot
            {
                ColliderCenter = new float3(0f, 0.05f, 0f),
                ColliderRadius = 0.12f,
                ColliderHeight = 0.24f,
                MassKg = 10f,
                AnchorInParent = new float3(0f, -0.45f, 0f)
            };
            Assert.AreEqual(RagdollRefitError.None, RagdollRefit.SyncRig(manager, rig.Root, 0, 8, 8, snapshots));
            var second = manager.GetComponentData<RagdollRefitTag>(rig.Body0).Owned;
            Assert.IsTrue(second.IsCreated);
            Assert.IsFalse(second.Equals(first));
            var capsule = ReadCapsule(second);
            Assert.AreEqual(0.15f, capsule.Radius, 1e-6f);
            Assert.AreEqual(0.1f, capsule.Vertex0.y, 1e-5f);
            Assert.AreEqual(0.4f, capsule.Vertex1.y, 1e-5f);
            var anchor = manager.GetComponentData<PhysicsJoint>(rig.Joint).BodyAFromJoint.Position;
            Assert.AreEqual(-0.45f, anchor.y, 1e-6f);
            var state = manager.GetComponentData<RagdollRefitState>(rig.Root);
            Assert.AreEqual(8ul, state.AppliedRevision);
            Assert.AreEqual(2, state.CommittedCount);
            snapshots.Dispose();
        }
    }
}
