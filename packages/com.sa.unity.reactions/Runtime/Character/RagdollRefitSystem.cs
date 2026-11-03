using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics.Systems;
using SA.Shape.Unity;

namespace SA.Reactions
{
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateBefore(typeof(PhysicsSystemGroup))]
    public partial struct RagdollRefitSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var manager = state.EntityManager;
            using var rigs = new NativeList<Entity>(Allocator.Temp);
            foreach (var (ragdoll, entity) in SystemAPI.Query<RefRO<Ragdoll>>().WithEntityAccess())
            {
                if (ragdoll.ValueRO.Spawned != 1)
                    continue;
                if (!manager.HasComponent<ShapePhysical>(entity) || !manager.HasComponent<ShapeState>(entity))
                    continue;
                if (!manager.HasBuffer<ShapePhysicalBody>(entity) || !manager.HasBuffer<ShapePhysicalJoint>(entity))
                    continue;
                if (!manager.HasBuffer<RagdollBody>(entity) || !manager.HasBuffer<RagdollRoleSpec>(entity))
                    continue;
                rigs.Add(entity);
            }
            foreach (var rig in rigs)
                SyncOne(manager, rig);
        }

        static void SyncOne(EntityManager manager, Entity rig)
        {
            var physical = manager.GetComponentData<ShapePhysical>(rig);
            var shapeState = manager.GetComponentData<ShapeState>(rig);
            var applied = manager.HasComponent<RagdollRefitState>(rig) ? manager.GetComponentData<RagdollRefitState>(rig).AppliedRevision : 0ul;
            if (applied == physical.AppliedRevision || (physical.LivePhysical != 0 && shapeState.Revision != physical.AppliedRevision))
            {
                using var settled = new NativeArray<RagdollRefitSnapshot>(0, Allocator.Temp);
                RagdollRefit.SyncRig(manager, rig, physical.LivePhysical, shapeState.Revision, physical.AppliedRevision, settled);
                return;
            }
            var snapshotBodies = manager.GetBuffer<ShapePhysicalBody>(rig);
            var snapshotJoints = manager.GetBuffer<ShapePhysicalJoint>(rig);
            var bodies = manager.GetBuffer<RagdollBody>(rig);
            var specs = manager.GetBuffer<RagdollRoleSpec>(rig);
            if (bodies.Length == 0 || snapshotBodies.Length == 0 || snapshotJoints.Length != snapshotBodies.Length)
            {
                RagdollRefit.Refuse(manager, rig, RagdollRefitError.InvalidSnapshot);
                return;
            }
            var ragdoll = manager.GetComponentData<Ragdoll>(rig);
            var total = 0f;
            for (var i = 0; i < specs.Length; i++)
                total += specs[i].Mass;
            if (!math.isfinite(total) || total <= 0f)
            {
                RagdollRefit.Refuse(manager, rig, RagdollRefitError.InvalidSnapshot);
                return;
            }
            var snapshots = new NativeArray<RagdollRefitSnapshot>(bodies.Length, Allocator.Temp);
            try
            {
                for (var i = 0; i < bodies.Length; i++)
                {
                    var bone = bodies[i].BoneIndex;
                    if (bone < 0 || bone >= snapshotBodies.Length)
                    {
                        RagdollRefit.Refuse(manager, rig, RagdollRefitError.InvalidSnapshot);
                        return;
                    }
                    var anchor = float3.zero;
                    if (ragdoll.JointMode != 2)
                        anchor = snapshotJoints[bone].Anchor;
                    snapshots[i] = new RagdollRefitSnapshot
                    {
                        ColliderCenter = snapshotBodies[bone].ColliderCenter,
                        ColliderRadius = snapshotBodies[bone].ColliderRadius,
                        ColliderHeight = snapshotBodies[bone].ColliderHeight,
                        MassKg = snapshotBodies[bone].Mass * total,
                        AnchorInParent = anchor
                    };
                }
                RagdollRefit.SyncRig(manager, rig, physical.LivePhysical, shapeState.Revision, physical.AppliedRevision, snapshots);
            }
            finally
            {
                snapshots.Dispose();
            }
        }
    }
}
