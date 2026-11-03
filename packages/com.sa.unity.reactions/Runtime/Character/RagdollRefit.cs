using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using Math = Unity.Physics.Math;

namespace SA.Reactions
{
    public struct RagdollRefitState : IComponentData
    {
        public ulong AppliedRevision;
        public int CommittedCount;
        public int RefusedCount;
        public FixedString128Bytes Diagnostic;
    }

    public struct RagdollRefitTag : IComponentData
    {
        public BlobAssetReference<Collider> Owned;
    }

    public struct RagdollRefitSnapshot
    {
        public float3 ColliderCenter;
        public float ColliderRadius;
        public float ColliderHeight;
        public float MassKg;
        public float3 AnchorInParent;
    }

    public enum RagdollRefitError : byte
    {
        None = 0,
        InvalidSnapshot = 1,
        UnsupportedShape = 2,
        ScaledBone = 3,
        MidSimulationChange = 4
    }

    public static class RagdollRefit
    {
        public const float MinBoneScale = 0.98f;
        public const float MaxBoneScale = 1.02f;
        public const float MaxInverseInertia = 80f;
        public const float MinRadius = 1e-4f;
        public const float MinSize = 1e-6f;

        public const string MidSimulationMessage = "SA: shape changed mid-simulation outside the refit boundary. Squash shape edits while the ragdoll is live.";
        public const string InvalidSnapshotMessage = "SA: ragdoll refit found an invalid shape snapshot. Kept the previous colliders, anchors and mass.";
        public const string UnsupportedShapeMessage = "SA: ragdoll refit met an unsupported collider. Kept the previous colliders, anchors and mass.";
        public const string ScaledBoneMessage = "SA: ragdoll refit refused a scaled bone. Refit moves colliders, never bones.";

        public static string MessageFor(RagdollRefitError error)
        {
            if (error == RagdollRefitError.MidSimulationChange)
                return MidSimulationMessage;
            if (error == RagdollRefitError.UnsupportedShape)
                return UnsupportedShapeMessage;
            if (error == RagdollRefitError.ScaledBone)
                return ScaledBoneMessage;
            return InvalidSnapshotMessage;
        }

        public static void Refuse(EntityManager manager, Entity rig, RagdollRefitError error)
        {
            if (!manager.HasComponent<RagdollRefitState>(rig))
                manager.AddComponentData(rig, new RagdollRefitState());
            var state = manager.GetComponentData<RagdollRefitState>(rig);
            state.Diagnostic = MessageFor(error);
            state.RefusedCount++;
            manager.SetComponentData(rig, state);
        }

        public static RagdollRefitError SyncRig(EntityManager manager, Entity rig, byte livePhysical, ulong shapeRevision, ulong appliedRevision, NativeArray<RagdollRefitSnapshot> snapshots)
        {
            if (!manager.HasComponent<RagdollRefitState>(rig))
                manager.AddComponentData(rig, new RagdollRefitState());
            var state = manager.GetComponentData<RagdollRefitState>(rig);
            if (state.AppliedRevision == appliedRevision)
            {
                if (livePhysical != 0 && shapeRevision != appliedRevision)
                {
                    state.Diagnostic = MidSimulationMessage;
                    state.RefusedCount++;
                    manager.SetComponentData(rig, state);
                    return RagdollRefitError.MidSimulationChange;
                }
                if (state.Diagnostic.Length > 0)
                {
                    state.Diagnostic = default;
                    manager.SetComponentData(rig, state);
                }
                return RagdollRefitError.None;
            }
            if (livePhysical != 0 && shapeRevision != appliedRevision)
            {
                state.Diagnostic = MidSimulationMessage;
                state.RefusedCount++;
                manager.SetComponentData(rig, state);
                return RagdollRefitError.MidSimulationChange;
            }
            var error = TryCommit(manager, rig, snapshots);
            if (error != RagdollRefitError.None)
            {
                state.Diagnostic = MessageFor(error);
                state.RefusedCount++;
                manager.SetComponentData(rig, state);
                return error;
            }
            state.AppliedRevision = appliedRevision;
            state.CommittedCount++;
            state.Diagnostic = default;
            manager.SetComponentData(rig, state);
            return RagdollRefitError.None;
        }

        public static RagdollRefitError TryCommit(EntityManager manager, Entity rig, NativeArray<RagdollRefitSnapshot> snapshots)
        {
            if (!manager.HasComponent<Ragdoll>(rig))
                return RagdollRefitError.InvalidSnapshot;
            if (!manager.HasBuffer<RagdollBody>(rig) || !manager.HasBuffer<RagdollRoleSpec>(rig))
                return RagdollRefitError.InvalidSnapshot;
            var bodies = manager.GetBuffer<RagdollBody>(rig);
            var specs = manager.GetBuffer<RagdollRoleSpec>(rig);
            if (bodies.Length == 0 || specs.Length != bodies.Length || snapshots.Length != bodies.Length)
                return RagdollRefitError.InvalidSnapshot;
            var ragdoll = manager.GetComponentData<Ragdoll>(rig);
            if (ragdoll.Spawned != 1)
                return RagdollRefitError.InvalidSnapshot;
            for (var i = 0; i < bodies.Length; i++)
            {
                var snapshotError = ValidateSnapshot(snapshots[i]);
                if (snapshotError != RagdollRefitError.None)
                    return snapshotError;
                if (bodies[i].ParentRole != specs[i].ParentRole)
                    return RagdollRefitError.InvalidSnapshot;
                if (specs[i].ParentRole < -1 || specs[i].ParentRole >= bodies.Length)
                    return RagdollRefitError.InvalidSnapshot;
                var body = bodies[i].Body;
                if (!manager.HasComponent<PhysicsCollider>(body) || !manager.HasComponent<PhysicsMass>(body) || !manager.HasComponent<PhysicsVelocity>(body) || !manager.HasComponent<LocalTransform>(body))
                    return RagdollRefitError.InvalidSnapshot;
                if (!manager.HasComponent<LocalToWorld>(specs[i].Bone))
                    return RagdollRefitError.InvalidSnapshot;
                if (!UnitScale(manager.GetComponentData<LocalToWorld>(specs[i].Bone).Value))
                    return RagdollRefitError.ScaledBone;
            }
            var savedMass = new NativeArray<PhysicsMass>(bodies.Length, Allocator.Temp);
            var savedVelocity = new NativeArray<PhysicsVelocity>(bodies.Length, Allocator.Temp);
            var built = new NativeArray<BlobAssetReference<Collider>>(bodies.Length, Allocator.Temp);
            var joints = new NativeArray<Entity>(bodies.Length, Allocator.Temp);
            var fresh = new NativeArray<PhysicsJoint>(bodies.Length, Allocator.Temp);
            var error = RagdollRefitError.None;
            var committed = false;
            try
            {
                PauseAll(manager, bodies, savedMass, savedVelocity);
                for (var i = 0; i < bodies.Length && error == RagdollRefitError.None; i++)
                {
                    var old = manager.GetComponentData<PhysicsCollider>(bodies[i].Body);
                    error = TryBuildCollider(snapshots[i], old.Value, out var shape);
                    built[i] = shape;
                }
                if (error == RagdollRefitError.None && ragdoll.JointMode != 2)
                    error = MapJoints(manager, bodies, specs, joints);
                if (error == RagdollRefitError.None && ragdoll.JointMode != 2)
                    BuildJoints(manager, bodies, specs, snapshots, ragdoll.JointMode, fresh);
                if (error == RagdollRefitError.None)
                {
                    CommitBodies(manager, rig, bodies, snapshots, built);
                    specs = manager.GetBuffer<RagdollRoleSpec>(rig);
                    CommitJoints(manager, specs, joints, fresh);
                    committed = true;
                }
            }
            finally
            {
                if (!committed)
                    Rollback(manager, rig, savedMass, savedVelocity, built);
                savedMass.Dispose();
                savedVelocity.Dispose();
                built.Dispose();
                joints.Dispose();
                fresh.Dispose();
            }
            return error;
        }

        public static RagdollRefitError ValidateSnapshot(RagdollRefitSnapshot snapshot)
        {
            if (!Finite(snapshot.ColliderCenter) || !Finite(snapshot.AnchorInParent))
                return RagdollRefitError.InvalidSnapshot;
            if (!math.isfinite(snapshot.ColliderRadius) || snapshot.ColliderRadius < MinRadius)
                return RagdollRefitError.InvalidSnapshot;
            if (!math.isfinite(snapshot.ColliderHeight) || snapshot.ColliderHeight < 2f * snapshot.ColliderRadius)
                return RagdollRefitError.InvalidSnapshot;
            if (!math.isfinite(snapshot.MassKg) || snapshot.MassKg <= 0f)
                return RagdollRefitError.InvalidSnapshot;
            return RagdollRefitError.None;
        }

        public static unsafe RagdollRefitError TryBuildCollider(RagdollRefitSnapshot snapshot, BlobAssetReference<Collider> baked, out BlobAssetReference<Collider> shape)
        {
            shape = default;
            if (!baked.IsCreated)
                return RagdollRefitError.UnsupportedShape;
            var root = (Collider*)baked.GetUnsafePtr();
            var filter = root->GetCollisionFilter();
            var material = new Material
            {
                Friction = root->GetFriction(),
                Restitution = root->GetRestitution(),
                CollisionResponse = root->GetCollisionResponse()
            };
            if (root->Type == ColliderType.Capsule)
            {
                var capsule = (CapsuleCollider*)root;
                var geometry = capsule->Geometry;
                var axis = geometry.Vertex1 - geometry.Vertex0;
                var length = math.length(axis);
                axis = length > MinSize ? axis / length : new float3(0f, 1f, 0f);
                var half = snapshot.ColliderHeight * 0.5f - snapshot.ColliderRadius;
                shape = CapsuleCollider.Create(new CapsuleGeometry
                {
                    Vertex0 = snapshot.ColliderCenter - axis * half,
                    Vertex1 = snapshot.ColliderCenter + axis * half,
                    Radius = snapshot.ColliderRadius
                }, filter, material);
                return RagdollRefitError.None;
            }
            if (root->Type == ColliderType.Sphere)
            {
                shape = SphereCollider.Create(new SphereGeometry
                {
                    Center = snapshot.ColliderCenter,
                    Radius = snapshot.ColliderRadius
                }, filter, material);
                return RagdollRefitError.None;
            }
            if (root->Type == ColliderType.Box)
            {
                var box = (BoxCollider*)root;
                var geometry = box->Geometry;
                var reference = 0.5f * math.min(geometry.Size.x, geometry.Size.z);
                if (!math.isfinite(reference) || reference < MinSize)
                    return RagdollRefitError.UnsupportedShape;
                var scale = snapshot.ColliderRadius / reference;
                if (!math.isfinite(scale) || scale <= 0f)
                    return RagdollRefitError.InvalidSnapshot;
                var size = geometry.Size * scale;
                if (!Finite(size) || size.x < MinSize || size.y < MinSize || size.z < MinSize)
                    return RagdollRefitError.InvalidSnapshot;
                shape = BoxCollider.Create(new BoxGeometry
                {
                    Center = snapshot.ColliderCenter,
                    Orientation = geometry.Orientation,
                    Size = size,
                    BevelRadius = geometry.BevelRadius * scale
                }, filter, material);
                return RagdollRefitError.None;
            }
            return RagdollRefitError.UnsupportedShape;
        }

        static bool Finite(float3 value)
        {
            return math.isfinite(value.x) && math.isfinite(value.y) && math.isfinite(value.z);
        }

        static bool UnitScale(float4x4 value)
        {
            var x = math.length(value.c0.xyz);
            var y = math.length(value.c1.xyz);
            var z = math.length(value.c2.xyz);
            return x >= MinBoneScale && x <= MaxBoneScale && y >= MinBoneScale && y <= MaxBoneScale && z >= MinBoneScale && z <= MaxBoneScale;
        }

        static PhysicsMass MassFor(BlobAssetReference<Collider> shape, float massKg)
        {
            var mass = PhysicsMass.CreateDynamic(shape.Value.MassProperties, massKg);
            mass.InverseInertia = math.min(mass.InverseInertia, new float3(MaxInverseInertia));
            return mass;
        }

        static void PauseAll(EntityManager manager, DynamicBuffer<RagdollBody> bodies, NativeArray<PhysicsMass> mass, NativeArray<PhysicsVelocity> velocity)
        {
            for (var i = 0; i < bodies.Length; i++)
            {
                var body = bodies[i].Body;
                mass[i] = manager.GetComponentData<PhysicsMass>(body);
                velocity[i] = manager.GetComponentData<PhysicsVelocity>(body);
                var frozen = mass[i];
                frozen.InverseMass = 0f;
                frozen.InverseInertia = float3.zero;
                manager.SetComponentData(body, frozen);
                manager.SetComponentData(body, new PhysicsVelocity());
            }
        }

        static void Rollback(EntityManager manager, Entity rig, NativeArray<PhysicsMass> mass, NativeArray<PhysicsVelocity> velocity, NativeArray<BlobAssetReference<Collider>> built)
        {
            var bodies = manager.GetBuffer<RagdollBody>(rig);
            for (var i = 0; i < bodies.Length; i++)
            {
                var body = bodies[i].Body;
                manager.SetComponentData(body, mass[i]);
                manager.SetComponentData(body, velocity[i]);
                if (built[i].IsCreated)
                    built[i].Dispose();
            }
        }

        static RagdollRefitError MapJoints(EntityManager manager, DynamicBuffer<RagdollBody> bodies, DynamicBuffer<RagdollRoleSpec> specs, NativeArray<Entity> joints)
        {
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<PhysicsJoint>(), ComponentType.ReadOnly<PhysicsConstrainedBodyPair>());
            using var candidates = query.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < bodies.Length; i++)
            {
                joints[i] = Entity.Null;
                if (specs[i].ParentRole < 0)
                    continue;
                var parent = bodies[specs[i].ParentRole].Body;
                var child = bodies[i].Body;
                for (var c = 0; c < candidates.Length; c++)
                {
                    var pair = manager.GetComponentData<PhysicsConstrainedBodyPair>(candidates[c]);
                    if (pair.EntityA == parent && pair.EntityB == child)
                    {
                        joints[i] = candidates[c];
                        break;
                    }
                }
                if (joints[i] == Entity.Null)
                    return RagdollRefitError.InvalidSnapshot;
            }
            return RagdollRefitError.None;
        }

        static void BuildJoints(EntityManager manager, DynamicBuffer<RagdollBody> bodies, DynamicBuffer<RagdollRoleSpec> specs, NativeArray<RagdollRefitSnapshot> snapshots, byte jointMode, NativeArray<PhysicsJoint> fresh)
        {
            for (var i = 0; i < bodies.Length; i++)
            {
                if (specs[i].ParentRole < 0)
                    continue;
                if (jointMode == 1)
                {
                    fresh[i] = PhysicsJoint.CreateBallAndSocket(snapshots[i].AnchorInParent, float3.zero);
                    continue;
                }
                var parentTransform = manager.GetComponentData<LocalTransform>(bodies[specs[i].ParentRole].Body);
                var childTransform = manager.GetComponentData<LocalTransform>(bodies[i].Body);
                var inverseParent = math.inverse(parentTransform.Rotation);
                var frameInParent = math.mul(inverseParent, math.mul(childTransform.Rotation, specs[i].JointFrameInChild));
                PhysicsJoint.CreateRagdoll(
                    new BodyFrame(new RigidTransform(frameInParent, snapshots[i].AnchorInParent)),
                    new BodyFrame(new RigidTransform(specs[i].JointFrameInChild, float3.zero)),
                    specs[i].ConeAngle,
                    new Math.FloatRange(specs[i].PlaneRange.x, specs[i].PlaneRange.y),
                    new Math.FloatRange(specs[i].TwistRange.x, specs[i].TwistRange.y),
                    out var joint,
                    out _);
                fresh[i] = joint;
            }
        }

        static void CommitBodies(EntityManager manager, Entity rig, DynamicBuffer<RagdollBody> bodies, NativeArray<RagdollRefitSnapshot> snapshots, NativeArray<BlobAssetReference<Collider>> built)
        {
            var count = bodies.Length;
            var targets = new NativeArray<Entity>(count, Allocator.Temp);
            for (var i = 0; i < count; i++)
                targets[i] = bodies[i].Body;
            for (var i = 0; i < count; i++)
            {
                if (!manager.HasComponent<RagdollRefitTag>(targets[i]))
                    manager.AddComponentData(targets[i], new RagdollRefitTag());
            }
            bodies = manager.GetBuffer<RagdollBody>(rig);
            for (var i = 0; i < bodies.Length; i++)
            {
                var body = bodies[i].Body;
                manager.SetComponentData(body, new PhysicsCollider { Value = built[i] });
                manager.SetComponentData(body, MassFor(built[i], snapshots[i].MassKg));
                var tag = manager.GetComponentData<RagdollRefitTag>(body);
                if (tag.Owned.IsCreated)
                    tag.Owned.Dispose();
                tag.Owned = built[i];
                built[i] = default;
                manager.SetComponentData(body, tag);
            }
            targets.Dispose();
        }

        static void CommitJoints(EntityManager manager, DynamicBuffer<RagdollRoleSpec> specs, NativeArray<Entity> joints, NativeArray<PhysicsJoint> fresh)
        {
            for (var i = 0; i < specs.Length; i++)
            {
                if (specs[i].ParentRole < 0)
                    continue;
                manager.SetComponentData(joints[i], fresh[i]);
            }
        }
    }
}
