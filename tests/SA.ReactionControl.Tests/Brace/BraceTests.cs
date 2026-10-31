using System;
using SA.Humanoid;
using SA.Motion.Numerics;
using SA.ReactionControl.Brace;
using BraceApi = SA.ReactionControl.Brace.Brace;
using Xunit;

namespace SA.ReactionControl.Tests
{
    public class BraceTests
    {
        const float Dt = 1f / 60f;

        static RoleBody[] Stance()
        {
            var bodies = new RoleBody[Roles.Count];
            bodies[(int)Role.Pelvis] = Body(new Float3(0f, 1f, 0f));
            bodies[(int)Role.Torso] = Body(new Float3(0f, 1.35f, 0f));
            bodies[(int)Role.UpperArmLeft] = Body(new Float3(-0.25f, 1.5f, 0f));
            bodies[(int)Role.UpperArmRight] = Body(new Float3(0.25f, 1.5f, 0f));
            bodies[(int)Role.ForearmLeft] = Body(new Float3(-0.3f, 1.25f, 0f));
            bodies[(int)Role.ForearmRight] = Body(new Float3(0.3f, 1.25f, 0f));
            bodies[(int)Role.HandLeft] = Body(new Float3(-0.3f, 1f, 0f));
            bodies[(int)Role.HandRight] = Body(new Float3(0.3f, 1f, 0f));
            return bodies;
        }

        static RoleBody Body(Float3 position)
        {
            return new RoleBody { Position = position, Rotation = Quaternion.Identity };
        }

        static SupportProbe Wall(Float3 point, Float3 normal)
        {
            return new SupportProbe { Hit = 1, Point = point, Normal = normal, Distance = 0.5f, PointVelocity = Float3.Zero };
        }

        static SupportProbe Miss()
        {
            return new SupportProbe { Hit = 0, Point = Float3.Zero, Normal = new Float3(0f, 0f, 1f), Distance = 10f, PointVelocity = Float3.Zero };
        }

        static SupportProbe Touch(Float3 point)
        {
            return new SupportProbe { Hit = 1, Point = point, Normal = new Float3(0f, 0f, -1f), Distance = 0f, PointVelocity = Float3.Zero };
        }

        static BraceResult Run(BraceParams p, ref BraceState state, RoleBody[] bodies, SupportProbe wall, SupportProbe left, SupportProbe right, JointDrive[] drives, int ticks)
        {
            BraceResult result = default;
            for (int i = 0; i < ticks; i++)
            {
                result = BraceApi.Tick(p, ref state, bodies, wall, left, right, BalanceHint.Supported, Dt, drives);
            }
            return result;
        }

        [Fact]
        public void ApproachingWallReachesHandsOntoPlaneWithinReach()
        {
            var p = BraceParams.Standard;
            var state = new BraceState();
            BraceApi.Activate(ref state);
            var bodies = Stance();
            var drives = new JointDrive[Roles.Count];
            var wall = Wall(new Float3(0f, 1.2f, 0.6f), new Float3(0f, 0f, -1f));
            BraceResult result = Run(p, ref state, bodies, wall, Miss(), Miss(), drives, 30);
            Assert.Equal(BracePhase.Braced, result.Phase);
            Assert.Equal(3, result.ArmMask);
            Float3 shoulderLeft = bodies[(int)Role.UpperArmLeft].Position;
            Float3 shoulderRight = bodies[(int)Role.UpperArmRight].Position;
            Assert.True((result.ReachLeft - shoulderLeft).Length <= p.MaxReach + 1e-4f);
            Assert.True((result.ReachRight - shoulderRight).Length <= p.MaxReach + 1e-4f);
            Assert.True(Scalar.Abs(Float3.Dot(result.ReachLeft - wall.Point, wall.Normal)) < 0.05f);
            Assert.True(Scalar.Abs(Float3.Dot(result.ReachRight - wall.Point, wall.Normal)) < 0.05f);
            Assert.Equal(p.BodyStiffness, drives[(int)Role.UpperArmLeft].Stiffness);
            Assert.Equal(p.BodyStiffness, drives[(int)Role.UpperArmRight].Stiffness);
            Assert.True(result.CrouchIntent > 0f);
        }

        [Fact]
        public void OutOfReachWallIsSafeMissWithNoExtension()
        {
            var p = BraceParams.Standard;
            var state = new BraceState();
            BraceApi.Activate(ref state);
            var bodies = Stance();
            var drives = new JointDrive[Roles.Count];
            var wall = Wall(new Float3(0f, 1.2f, 5f), new Float3(0f, 0f, -1f));
            BraceResult result = Run(p, ref state, bodies, wall, Miss(), Miss(), drives, 30);
            Assert.Equal(0, result.ArmMask);
            Assert.True(result.Phase == BracePhase.Reaching || result.Phase == BracePhase.Idle);
            Assert.Equal(Quaternion.Identity, drives[(int)Role.UpperArmLeft].Target);
            Assert.Equal(Quaternion.Identity, drives[(int)Role.UpperArmRight].Target);
        }

        [Fact]
        public void ContactThenReleaseReturnsToIdleWithoutFlicker()
        {
            var p = BraceParams.Standard;
            var state = new BraceState();
            BraceApi.Activate(ref state);
            var bodies = Stance();
            var drives = new JointDrive[Roles.Count];
            var wall = Wall(new Float3(0f, 1.2f, 0.6f), new Float3(0f, 0f, -1f));
            var touchLeft = Touch(bodies[(int)Role.HandLeft].Position);
            var touchRight = Touch(bodies[(int)Role.HandRight].Position);
            BraceResult held = Run(p, ref state, bodies, wall, touchLeft, touchRight, drives, 30);
            Assert.Equal(BracePhase.Braced, held.Phase);
            Assert.Equal((byte)1, held.HasContact);
            int releases = 60;
            var masks = new byte[releases];
            BraceResult result = default;
            var gone = new SupportProbe { Hit = 0 };
            for (int i = 0; i < releases; i++)
            {
                result = BraceApi.Tick(p, ref state, bodies, gone, gone, gone, BalanceHint.Supported, Dt, drives);
                masks[i] = result.ArmMask;
            }
            Assert.Equal(BracePhase.Idle, result.Phase);
            Assert.Equal(0, result.ArmMask);
            int transitions = 0;
            for (int i = 1; i < releases; i++)
            {
                if (masks[i] != masks[i - 1])
                {
                    transitions++;
                }
            }
            Assert.True(transitions <= 1);
            for (int i = 0; i < drives.Length; i++)
            {
                Assert.Equal(JointDrive.Passive, drives[i]);
            }
        }

        [Fact]
        public void MirroredInputMirrorsOutput()
        {
            var p = BraceParams.Standard;
            var bodies = Stance();
            var wall = Wall(new Float3(0.7f, 1.5f, 0.1f), new Float3(0f, 0f, -1f));
            var stateA = new BraceState();
            var drivesA = new JointDrive[Roles.Count];
            BraceResult a = Run(p, ref stateA, bodies, wall, Miss(), Miss(), drivesA, 30);
            var mirrored = MirrorBodies(bodies);
            var mirroredWall = Wall(MirrorPoint(wall.Point), MirrorPoint(wall.Normal));
            var stateB = new BraceState();
            var drivesB = new JointDrive[Roles.Count];
            BraceResult b = Run(p, ref stateB, mirrored, mirroredWall, Miss(), Miss(), drivesB, 30);
            Assert.Equal(a.Phase, b.Phase);
            Assert.Equal(SwapBits(a.ArmMask), b.ArmMask);
            Assert.True((b.ReachLeft - MirrorPoint(a.ReachRight)).Length < 1e-4f);
            Assert.True((b.ReachRight - MirrorPoint(a.ReachLeft)).Length < 1e-4f);
            AssertMirrorDrive(drivesA[(int)Role.UpperArmRight], drivesB[(int)Role.UpperArmLeft]);
            AssertMirrorDrive(drivesA[(int)Role.ForearmRight], drivesB[(int)Role.ForearmLeft]);
            AssertMirrorDrive(drivesA[(int)Role.HandRight], drivesB[(int)Role.HandLeft]);
            AssertMirrorDrive(drivesA[(int)Role.UpperArmLeft], drivesB[(int)Role.UpperArmRight]);
        }

        [Fact]
        public void HostileInputsStayFiniteAndPassive()
        {
            var p = BraceParams.Standard;
            var state = new BraceState();
            var drives = new JointDrive[Roles.Count];
            var wall = Wall(new Float3(0f, 1.2f, 0.6f), new Float3(0f, 0f, -1f));
            var nan = new Float3(float.NaN, float.NaN, float.NaN);
            var bodies = Stance();
            bodies[(int)Role.Torso] = Body(nan);
            BraceResult poisoned = BraceApi.Tick(p, ref state, bodies, wall, Miss(), Miss(), BalanceHint.Supported, Dt, drives);
            Assert.Equal(BracePhase.Idle, poisoned.Phase);
            AssertFiniteAll(drives);
            var huge = new Float3(1e30f, -1e30f, 1e30f);
            var tall = Stance();
            tall[(int)Role.Torso] = Body(huge);
            var far = Wall(huge, huge);
            BraceResult wild = BraceApi.Tick(p, ref state, tall, far, Miss(), Miss(), BalanceHint.Supported, Dt, drives);
            AssertFiniteAll(drives);
            AssertFiniteResult(wild);
            BraceResult zeroDt = BraceApi.Tick(p, ref state, Stance(), wall, Miss(), Miss(), BalanceHint.Supported, 0f, drives);
            AssertFiniteAll(drives);
            Assert.Equal(BracePhase.Idle, zeroDt.Phase);
            var shortBodies = new RoleBody[3];
            var shortDrives = new JointDrive[3];
            BraceResult shortRun = BraceApi.Tick(p, ref state, shortBodies, wall, Miss(), Miss(), BalanceHint.Supported, Dt, shortDrives);
            Assert.Equal(BracePhase.Idle, shortRun.Phase);
        }

        [Fact]
        public void StandardParamsAndActivateAreSane()
        {
            var p = BraceParams.Standard;
            Assert.True(Scalar.IsFinite(p.BodyStiffness) && p.BodyStiffness > 0f);
            Assert.True(Scalar.IsFinite(p.MaxReach) && p.MaxReach > 0f);
            Assert.True(Scalar.IsFinite(p.BraceDistance) && p.BraceDistance > 0f);
            Assert.True(Scalar.IsFinite(p.ContactHoldTime) && p.ContactHoldTime > 0f);
            var state = new BraceState { ArmMask = 3, Phase = BracePhase.Braced, Lean = 5f };
            BraceApi.Activate(ref state);
            Assert.Equal(new BraceState(), state);
        }

        static void AssertFiniteAll(JointDrive[] drives)
        {
            foreach (JointDrive drive in drives)
            {
                Assert.True(Scalar.IsFinite(drive.Target.X));
                Assert.True(Scalar.IsFinite(drive.Target.Y));
                Assert.True(Scalar.IsFinite(drive.Target.Z));
                Assert.True(Scalar.IsFinite(drive.Target.W));
                Assert.True(Scalar.IsFinite(drive.TargetAngularVelocity.X));
                Assert.True(Scalar.IsFinite(drive.Stiffness));
                Assert.True(Scalar.IsFinite(drive.Damping));
                Assert.True(Scalar.IsFinite(drive.MaxTorque));
            }
        }

        static void AssertFiniteResult(BraceResult result)
        {
            Assert.True(Scalar.IsFinite(result.SmoothedDistance));
            Assert.True(Scalar.IsFinite(result.LeanIntent));
            Assert.True(Scalar.IsFinite(result.CrouchIntent));
            Assert.True(result.ReachLeft.IsFinite);
            Assert.True(result.ReachRight.IsFinite);
        }

        static void AssertMirrorDrive(JointDrive original, JointDrive mirrored)
        {
            Assert.Equal(original.Stiffness, mirrored.Stiffness);
            Assert.Equal(original.Damping, mirrored.Damping);
            Assert.Equal(original.MaxTorque, mirrored.MaxTorque);
            Quaternion expect = Quaternion.Canonical(MirrorQuat(original.Target));
            Quaternion actual = Quaternion.Canonical(mirrored.Target);
            Assert.True(Scalar.Abs(Quaternion.Dot(expect, actual)) > 0.9999f);
        }

        static byte SwapBits(byte mask)
        {
            return (byte)(((mask & 1) << 1) | ((mask & 2) >> 1));
        }

        static Float3 MirrorPoint(Float3 value)
        {
            return new Float3(-value.X, value.Y, value.Z);
        }

        static Quaternion MirrorQuat(Quaternion value)
        {
            return new Quaternion(value.X, -value.Y, -value.Z, value.W);
        }

        static RoleBody MirrorBody(RoleBody body)
        {
            body.Position = MirrorPoint(body.Position);
            body.Rotation = MirrorQuat(body.Rotation);
            body.LinearVelocity = MirrorPoint(body.LinearVelocity);
            body.AngularVelocity = new Float3(body.AngularVelocity.X, -body.AngularVelocity.Y, -body.AngularVelocity.Z);
            return body;
        }

        static RoleBody[] MirrorBodies(RoleBody[] bodies)
        {
            var mirrored = new RoleBody[Roles.Count];
            for (int i = 0; i < Roles.Count; i++)
            {
                mirrored[i] = MirrorBody(bodies[i]);
            }
            Swap(mirrored, Role.UpperArmLeft, Role.UpperArmRight);
            Swap(mirrored, Role.ForearmLeft, Role.ForearmRight);
            Swap(mirrored, Role.HandLeft, Role.HandRight);
            Swap(mirrored, Role.ThighLeft, Role.ThighRight);
            Swap(mirrored, Role.ShinLeft, Role.ShinRight);
            Swap(mirrored, Role.FootLeft, Role.FootRight);
            return mirrored;
        }

        static void Swap(RoleBody[] bodies, Role left, Role right)
        {
            RoleBody keep = bodies[(int)left];
            bodies[(int)left] = bodies[(int)right];
            bodies[(int)right] = keep;
        }
    }
}
