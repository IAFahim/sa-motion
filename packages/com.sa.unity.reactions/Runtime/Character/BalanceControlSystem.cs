using System;
using System.Collections.Generic;
using SA.Balance;
using SA.Humanoid;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using Float3 = SA.Motion.Numerics.Float3;
using MotionQuaternion = SA.Motion.Numerics.Quaternion;
using PhysicsMaterialPlaceholder = Unity.Physics.Material;

namespace SA.Reactions
{
    [UpdateInGroup(typeof(BeforePhysicsSystemGroup))]
    [UpdateAfter(typeof(RagdollControlSystem))]
    public partial class BalanceControlSystem : SystemBase
    {
        const float ControllerStep = 1f / 60f;
        const float ContactDistance = 0.06f;
        const int ReadyTicks = 240;

        sealed class Runner
        {
            public BalanceParams Parameters = TunedParams.Tuned;
            public BalanceState State;
            public readonly quaternion[] CanonicalOffset = new quaternion[Roles.Count];
            public readonly BodyState[] Bodies = new BodyState[Roles.Count];
            public readonly FootContact[] Feet = new FootContact[2];
            public readonly BodyDrive[] Drives = new BodyDrive[Roles.Count];
            public readonly MotionQuaternion[] Targets = new MotionQuaternion[Roles.Count];
            public float Accumulator = ControllerStep;
            public bool Limp;
            public int Grounded;
        }

        static readonly float[] LastDistance = new float[2];

        readonly Dictionary<Entity, Runner> runners = new Dictionary<Entity, Runner>();

        protected override void OnCreate()
        {
            RequireForUpdate<PhysicsWorldSingleton>();
        }

        static MotionQuaternion Bind(int role) =>
            role == (int)Role.FootLeft || role == (int)Role.FootRight ? MotionQuaternion.AxisAngle(new Float3(1f, 0f, 0f), SA.Motion.Numerics.Scalar.Pi * 0.5f) : MotionQuaternion.Identity;

        protected override void OnUpdate()
        {
            var manager = EntityManager;
            var dt = SystemAPI.Time.DeltaTime;
            var world = SystemAPI.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld;
            using var rigs = new NativeList<Entity>(Allocator.Temp);
            foreach (var (ragdoll, control, entity) in SystemAPI.Query<RefRO<Ragdoll>, RefRO<BalanceControl>>().WithEntityAccess())
                if (ragdoll.ValueRO.Spawned == 1 && control.ValueRO.Enabled != 0)
                    rigs.Add(entity);
            foreach (var rig in rigs)
                Control(manager, world, rig, dt);
        }

        void Control(EntityManager manager, PhysicsWorld world, Entity rig, float dt)
        {
            var ragdoll = manager.GetComponentData<Ragdoll>(rig);
            var bodies = manager.GetBuffer<RagdollBody>(rig).ToNativeArray(Allocator.Temp);
            if (!runners.TryGetValue(rig, out var runner))
            {
                runner = new Runner();
                var origin = manager.GetComponentData<LocalToWorld>(rig);
                for (var role = 0; role < Roles.Count; role++)
                {
                    var rotation = manager.GetComponentData<LocalTransform>(bodies[role].Body).Rotation;
                    var bind = Bind(role);
                    runner.CanonicalOffset[role] = math.mul(math.inverse(rotation), new quaternion(bind.X, bind.Y, bind.Z, bind.W));
                    if (!manager.HasComponent<TorqueDrive>(bodies[role].Body))
                        manager.AddComponentData(bodies[role].Body, new TorqueDrive());
                }
                runners[rig] = runner;
            }
            if (ragdoll.Mode != RagdollMode.Tracking)
            {
                runner.Limp = true;
                runner.Grounded = 0;
                var limpControl = manager.GetComponentData<BalanceControl>(rig);
                limpControl.Ready = 0;
                manager.SetComponentData(rig, limpControl);
                Release(manager, bodies);
                return;
            }
            if (runner.Limp)
            {
                runner.Limp = false;
                runner.State = new BalanceState();
                runner.Accumulator = ControllerStep;
            }
            var feetOnGround = Contact(manager, world, bodies[(int)Role.FootLeft].Body, 0).Hit && Contact(manager, world, bodies[(int)Role.FootRight].Body, 1).Hit;
            runner.Grounded = feetOnGround ? runner.Grounded + 1 : 0;
            var control = manager.GetComponentData<BalanceControl>(rig);
            if (runner.Grounded < ReadyTicks)
            {
                control.Ready = 0;
                manager.SetComponentData(rig, control);
                Release(manager, bodies);
                return;
            }
            if (control.Ready == 0)
            {
                control.Ready = 1;
                manager.SetComponentData(rig, control);
                runner.State = new BalanceState();
                runner.Accumulator = ControllerStep;
            }
            runner.Accumulator += dt;
            if (runner.Accumulator >= ControllerStep)
            {
                runner.Accumulator = math.max(0f, runner.Accumulator - ControllerStep);
                Tick(manager, world, rig, bodies, runner, ref ragdoll);
                manager.SetComponentData(rig, ragdoll);
            }
            Apply(manager, bodies, runner);
        }

        static void Release(EntityManager manager, NativeArray<RagdollBody> bodies)
        {
            for (var role = 0; role < bodies.Length; role++)
                if (manager.HasComponent<TorqueDrive>(bodies[role].Body))
                    manager.SetComponentData(bodies[role].Body, new TorqueDrive());
        }

        void Tick(EntityManager manager, PhysicsWorld world, Entity rig, NativeArray<RagdollBody> bodies, Runner runner, ref Ragdoll ragdoll)
        {
            for (var role = 0; role < Roles.Count; role++)
            {
                var body = bodies[role].Body;
                var transform = manager.GetComponentData<LocalTransform>(body);
                var velocity = manager.GetComponentData<PhysicsVelocity>(body);
                var mass = manager.GetComponentData<PhysicsMass>(body);
                var angular = math.rotate(math.mul(transform.Rotation, mass.InertiaOrientation), velocity.Angular);
                var center = transform.Position + math.rotate(transform.Rotation, mass.CenterOfMass);
                var canonical = math.mul(transform.Rotation, runner.CanonicalOffset[role]);
                runner.Bodies[role] = new BodyState
                {
                    Position = new Float3(center.x, center.y, center.z),
                    Rotation = new MotionQuaternion(canonical.value.x, canonical.value.y, canonical.value.z, canonical.value.w),
                    LinearVelocity = new Float3(velocity.Linear.x, velocity.Linear.y, velocity.Linear.z),
                    AngularVelocity = new Float3(angular.x, angular.y, angular.z),
                    InverseMass = mass.InverseMass
                };
                runner.Drives[role] = BodyDrive.Passive;
            }
            runner.Feet[0] = Contact(manager, world, bodies[(int)Role.FootLeft].Body, 0);
            runner.Feet[1] = Contact(manager, world, bodies[(int)Role.FootRight].Body, 1);
            if (!RagdollPose.Evaluate(manager, rig, out _, out var model))
                return;
            var origin = manager.GetComponentData<LocalToWorld>(rig);
            var originRotation = math.normalize(origin.Rotation);
            for (var role = 0; role < Roles.Count; role++)
            {
                var bone = bodies[role].BoneIndex;
                var target = bone >= 0 ? math.mul(math.mul(originRotation, model[bone]), runner.CanonicalOffset[role]) : quaternion.identity;
                runner.Targets[role] = new MotionQuaternion(target.value.x, target.value.y, target.value.z, target.value.w);
            }
            var input = new BalanceInput(runner.Targets, ControllerStep);
            var report = StandingController.Tick(in runner.Parameters, ref runner.State, runner.Bodies, runner.Feet, in input, runner.Drives);
            manager.SetComponentData(rig, new BalanceControl { Enabled = 1, Ready = 1, Decision = (byte)report.Decision, FootContacts = (byte)((runner.Feet[0].Hit ? 1 : 0) | (runner.Feet[1].Hit ? 2 : 0)), Margin = report.Margin, DistanceLeft = LastDistance[0], DistanceRight = LastDistance[1] });
            if (report.Decision == Decision.Falling)
            {
                ragdoll.Mode = RagdollMode.Limp;
                ragdoll.Timer = 0f;
            }
        }

        static FootContact Contact(EntityManager manager, PhysicsWorld world, Entity foot, int side)
        {
            var transform = manager.GetComponentData<LocalTransform>(foot);
            var index = world.GetRigidBodyIndex(foot);
            var origin = transform.Position;
            var input = new RaycastInput
            {
                Start = origin + new float3(0f, 0.2f, 0f),
                End = origin - new float3(0f, 0.6f, 0f),
                Filter = new CollisionFilter { BelongsTo = 0xFFFFFFFFu, CollidesWith = ~2u, GroupIndex = 0 }
            };
            if (index < 0 || !world.CastRay(input, out var hit))
            {
                LastDistance[side] = index < 0 ? -1f : -2f;
                return new FootContact { Normal = new Float3(0f, 1f, 0f) };
            }
            var bottom = world.Bodies[index].CalculateAabb().Min.y;
            var distance = bottom - hit.Position.y;
            LastDistance[side] = distance;
            return new FootContact
            {
                Hit = distance <= ContactDistance,
                Point = new Float3(origin.x, hit.Position.y, origin.z),
                Normal = new Float3(hit.SurfaceNormal.x, hit.SurfaceNormal.y, hit.SurfaceNormal.z)
            };
        }

        static void Apply(EntityManager manager, NativeArray<RagdollBody> bodies, Runner runner)
        {
            for (var role = 0; role < Roles.Count; role++)
            {
                var drive = runner.Drives[role];
                var target = math.mul(new quaternion(drive.TargetWorld.X, drive.TargetWorld.Y, drive.TargetWorld.Z, drive.TargetWorld.W), math.inverse(runner.CanonicalOffset[role]));
                var parent = bodies[role].ParentRole;
                manager.SetComponentData(bodies[role].Body, new TorqueDrive
                {
                    Parent = parent >= 0 ? bodies[parent].Body : Entity.Null,
                    TargetWorld = math.normalizesafe(target),
                    Stiffness = drive.Stiffness,
                    Damping = drive.Damping,
                    MaxTorque = drive.MaxTorque
                });
            }
        }
    }
}
