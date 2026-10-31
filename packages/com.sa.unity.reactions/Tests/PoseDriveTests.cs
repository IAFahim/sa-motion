using System;
using System.Collections.Generic;
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
    sealed class Arm : IDisposable
    {
        public World World;
        public Entity Parent;
        public Entity Child;
        public float Step;
        BlobAssetReference<Collider> parentShape;
        BlobAssetReference<Collider> childShape;
        PhysicsSystemGroup physics;
        double time;

        EntityManager Manager => World.EntityManager;

        public static Arm Create(float3 gravity, bool parentDynamic, quaternion parentRotation, quaternion childRotation, float3 capsuleStart, float3 capsuleEnd, PoseDrive drive, float step)
        {
            var arm = new Arm { World = new World("SA.Reactions.Arm"), Step = step };
            var systems = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.All)
                .Where(type => type.Assembly.GetName().Name == "Unity.Physics" || type == typeof(PoseDriveSystem)).ToList();
            DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(arm.World, systems);
            arm.physics = arm.World.GetExistingSystemManaged<PhysicsSystemGroup>();
            var manager = arm.Manager;
            var physicsStep = PhysicsStep.Default;
            physicsStep.Gravity = gravity;
            manager.CreateSingleton(physicsStep);
            arm.parentShape = CapsuleCollider.Create(new CapsuleGeometry { Vertex0 = new float3(-0.1f, 0f, 0f), Vertex1 = new float3(0.1f, 0f, 0f), Radius = 0.05f }, CollisionFilter.Default, Material.Default);
            arm.childShape = CapsuleCollider.Create(new CapsuleGeometry { Vertex0 = capsuleStart, Vertex1 = capsuleEnd, Radius = 0.05f }, CollisionFilter.Default, Material.Default);
            var anchor = new float3(0f, 2f, 0f);
            arm.Parent = Body(manager, anchor, parentRotation, arm.parentShape, parentDynamic ? 4f : 0f);
            arm.Child = Body(manager, anchor, childRotation, arm.childShape, 2f);
            var joint = manager.CreateEntity(typeof(PhysicsJoint), typeof(PhysicsConstrainedBodyPair), typeof(PhysicsWorldIndex));
            manager.SetComponentData(joint, PhysicsJoint.CreateBallAndSocket(float3.zero, float3.zero));
            manager.SetComponentData(joint, new PhysicsConstrainedBodyPair(arm.Parent, arm.Child, false));
            drive.Parent = arm.Parent;
            manager.AddComponentData(arm.Child, drive);
            manager.AddComponentData(arm.Child, new PoseDriveReport());
            return arm;
        }

        static Entity Body(EntityManager manager, float3 position, quaternion rotation, BlobAssetReference<Collider> shape, float mass)
        {
            var dynamic = mass > 0f;
            var entity = dynamic
                ? manager.CreateEntity(typeof(LocalTransform), typeof(PhysicsCollider), typeof(PhysicsWorldIndex), typeof(PhysicsVelocity), typeof(PhysicsMass))
                : manager.CreateEntity(typeof(LocalTransform), typeof(PhysicsCollider), typeof(PhysicsWorldIndex));
            manager.SetComponentData(entity, LocalTransform.FromPositionRotation(position, rotation));
            manager.SetComponentData(entity, new PhysicsCollider { Value = shape });
            if (dynamic)
                manager.SetComponentData(entity, PhysicsMass.CreateDynamic(shape.Value.MassProperties, mass));
            return entity;
        }

        public void Run(int steps)
        {
            for (var i = 0; i < steps; i++)
            {
                time += Step;
                World.SetTime(new TimeData(time, Step));
                physics.Update();
                Manager.CompleteAllTrackedJobs();
            }
        }

        public quaternion ParentRotation => Manager.GetComponentData<LocalTransform>(Parent).Rotation;
        public quaternion ChildRotation => Manager.GetComponentData<LocalTransform>(Child).Rotation;
        public float3 ChildPosition => Manager.GetComponentData<LocalTransform>(Child).Position;
        public float3 ParentPosition => Manager.GetComponentData<LocalTransform>(Parent).Position;
        public PoseDriveReport Report => Manager.GetComponentData<PoseDriveReport>(Child);

        public float RelativeError(quaternion target)
        {
            var relative = math.mul(math.inverse(ParentRotation), ChildRotation);
            return 2f * math.acos(math.min(1f, math.abs(math.dot(relative.value, target.value))));
        }

        public void Dispose()
        {
            World.Dispose();
            parentShape.Dispose();
            childShape.Dispose();
        }
    }

    public class PoseDriveTests
    {
        static readonly float3 Zero = float3.zero;
        static readonly float3 Down = new float3(0f, -9.81f, 0f);
        static readonly quaternion TiltedFrame = quaternion.Euler(0.1f, 0.5f, -0.15f);
        static readonly quaternion HoldTarget = math.mul(quaternion.RotateZ(1.0f), quaternion.RotateY(0.3f));

        static PoseDrive Drive(float strength = 1f, float maxTorque = 1000f) => new PoseDrive
        {
            ParentJoint = quaternion.identity,
            ChildJoint = quaternion.identity,
            Target = HoldTarget,
            Frequency = 10f,
            DampingRatio = 1f,
            MaxTorque = maxTorque,
            Strength = strength
        };

        static Arm Hold(float step, float strength = 1f, float maxTorque = 1000f)
        {
            var start = math.mul(TiltedFrame, math.mul(HoldTarget, quaternion.RotateZ(0.25f)));
            return Arm.Create(Down, false, TiltedFrame, start, Zero, new float3(0.5f, 0f, 0f), Drive(strength, maxTorque), step);
        }

        [Test]
        public void ZeroErrorAndVelocityGiveZeroImpulse()
        {
            var parent = DriveBody.Static(quaternion.identity);
            var child = new DriveBody { Orientation = HoldTarget, InertiaOrientation = quaternion.identity, InverseInertia = new float3(10f, 10f, 10f) };
            var impulse = PoseDriveMath.Impulse(parent, child, Drive(), 1f / 60f, out var error, out var singular);
            Assert.AreEqual(0f, math.length(impulse), 1e-6f);
            Assert.AreEqual(0f, math.length(error), 1e-5f);
            Assert.IsFalse(singular);
        }

        [Test]
        public void TheSignOfTheTargetDoesNotChangeTheImpulse()
        {
            var parent = DriveBody.Static(TiltedFrame);
            var child = new DriveBody { Orientation = quaternion.identity, InertiaOrientation = quaternion.identity, InverseInertia = new float3(10f, 12f, 14f) };
            var drive = Drive();
            var a = PoseDriveMath.Impulse(parent, child, drive, 1f / 60f, out _, out _);
            drive.Target = new quaternion(-drive.Target.value);
            var b = PoseDriveMath.Impulse(parent, child, drive, 1f / 60f, out _, out _);
            Assert.AreEqual(0f, math.length(a - b), 1e-4f);
            Assert.Greater(math.length(a), 0f);
        }

        [Test]
        public void TwoStaticBodiesAreSingularAndProduceNoImpulse()
        {
            var body = DriveBody.Static(quaternion.identity);
            var impulse = PoseDriveMath.Impulse(body, DriveBody.Static(quaternion.RotateX(1f)), Drive(), 1f / 60f, out _, out var singular);
            Assert.AreEqual(0f, math.length(impulse));
            Assert.IsTrue(singular);
        }

        [Test]
        public void HoldsATiltedTargetAgainstGravityWithRotatedFramesAndAnOffsetCenterOfMass()
        {
            using var arm = Hold(1f / 60f);
            arm.Run(240);
            Assert.Less(arm.RelativeError(HoldTarget), 0.1f);
            Assert.Less(math.length(arm.ChildPosition - arm.ParentPosition), 0.02f);
        }

        [Test]
        public void ZeroStrengthLeavesTheArmPassiveSoItFalls()
        {
            using var arm = Arm.Create(Down, false, quaternion.identity, quaternion.identity, Zero, new float3(0.5f, 0f, 0f), Drive(0f), 1f / 60f);
            arm.Run(90);
            Assert.Greater(2f * math.acos(math.min(1f, math.abs(arm.ChildRotation.value.w))), 0.5f);
            Assert.Less(math.length(arm.ChildPosition - arm.ParentPosition), 0.02f);
        }

        [Test]
        public void ADisplacedArmIsPulledBackAndAnExternalKickDisplacesIt()
        {
            using var arm = Hold(1f / 60f);
            arm.Run(240);
            var settled = arm.RelativeError(HoldTarget);
            var manager = arm.World.EntityManager;
            var velocity = manager.GetComponentData<PhysicsVelocity>(arm.Child);
            velocity.Angular += new float3(0f, 0f, 25f);
            manager.SetComponentData(arm.Child, velocity);
            var peak = 0f;
            for (var i = 0; i < 20; i++)
            {
                arm.Run(1);
                peak = math.max(peak, arm.RelativeError(HoldTarget));
            }
            arm.Run(240);
            Assert.Greater(peak, settled + 0.1f);
            Assert.Less(arm.RelativeError(HoldTarget), 0.1f);
        }

        [Test]
        public void TheTorqueCapIsRespectedAndReachedAndStopsTheArmHolding()
        {
            const float step = 1f / 60f;
            const float cap = 0.5f;
            using var arm = Hold(step, 1f, cap);
            var largest = 0f;
            for (var i = 0; i < 120; i++)
            {
                arm.Run(1);
                var length = math.length(arm.Report.ChildImpulse);
                Assert.LessOrEqual(length, cap * step * 1.0001f);
                largest = math.max(largest, length);
            }
            Assert.GreaterOrEqual(largest, cap * step * 0.99f);
            Assert.Greater(arm.RelativeError(HoldTarget), 0.3f);
        }

        [Test]
        public void InternalImpulsesAreEqualAndOppositeWhenBothBodiesAreDynamic()
        {
            var drive = Drive();
            using var arm = Arm.Create(Zero, true, TiltedFrame, math.mul(TiltedFrame, quaternion.RotateZ(0.6f)), new float3(-0.25f, 0f, 0f), new float3(0.25f, 0f, 0f), drive, 1f / 60f);
            for (var i = 0; i < 20; i++)
            {
                arm.Run(1);
                var report = arm.Report;
                Assert.AreEqual(0f, math.length(report.ParentImpulse + report.ChildImpulse), 1e-6f);
                if (i == 0)
                    Assert.Greater(math.length(report.ChildImpulse), 1e-4f);
            }
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 120f)]
        public void TimestepVariantsStayBounded(float step)
        {
            using var arm = Hold(step);
            arm.Run((int)(4f / step));
            Assert.Less(arm.RelativeError(HoldTarget), 0.25f);
            Assert.Less(math.length(arm.ChildPosition - arm.ParentPosition), 0.02f);
        }

        [TestCase(1f, 0f, 0f)]
        [TestCase(0f, 1f, 0f)]
        [TestCase(0f, 0f, 1f)]
        public void ABallJointKeepsAllThreeRotationalDegreesOfFreedom(float x, float y, float z)
        {
            using var arm = Arm.Create(Zero, false, quaternion.identity, quaternion.identity, new float3(-0.25f, 0f, 0f), new float3(0.25f, 0f, 0f), Drive(0f), 1f / 120f);
            var manager = arm.World.EntityManager;
            var mass = manager.GetComponentData<PhysicsMass>(arm.Child);
            var axis = new float3(x, y, z);
            manager.SetComponentData(arm.Child, new PhysicsVelocity { Angular = math.rotate(math.inverse(mass.InertiaOrientation), axis) });
            arm.Run(60);
            var turned = 2f * math.acos(math.min(1f, math.abs(arm.ChildRotation.value.w)));
            Assert.AreEqual(0.5f, turned, 0.03f);
        }

        [Test]
        public void RecordGateMMeasurements()
        {
            var lines = new List<string>();
            foreach (var step in new[] { 1f / 30f, 1f / 60f, 1f / 120f })
            {
                using var arm = Hold(step);
                arm.Run((int)(4f / step));
                lines.Add($"\"hold_{(int)math.round(1f / step)}Hz\": {{ \"error_rad\": {arm.RelativeError(HoldTarget):F4}, \"anchor_separation_m\": {math.length(arm.ChildPosition - arm.ParentPosition):F5} }}");
            }
            using (var passive = Arm.Create(Down, false, quaternion.identity, quaternion.identity, Zero, new float3(0.5f, 0f, 0f), Drive(0f), 1f / 60f))
            {
                passive.Run(90);
                lines.Add($"\"passive_fall_1p5s_rad\": {2f * math.acos(math.min(1f, math.abs(passive.ChildRotation.value.w))):F4}");
            }
            var report = "{\n  \"gravity\": [0, -9.81, 0],\n  \"solver\": \"PhysicsStep.Default (substeps 1, iterations 4)\",\n  \"gains\": { \"frequency_hz\": 10, \"damping_ratio\": 1, \"max_torque\": 1000 },\n  " + string.Join(",\n  ", lines) + "\n}\n";
            System.IO.Directory.CreateDirectory("Temp/sa");
            System.IO.File.WriteAllText("Temp/sa/gate_m.json", report);
            Assert.IsTrue(lines.Count == 4);
        }
    }
}
