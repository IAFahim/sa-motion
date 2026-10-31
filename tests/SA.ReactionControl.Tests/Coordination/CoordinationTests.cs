using System;
using SA.Humanoid;
using SA.Motion.Numerics;
using SA.ReactionControl.Coordination;
using Xunit;

namespace SA.ReactionControl.Tests.Coordination
{
    public class CoordinationTests
    {
        static float[] Priorities()
        {
            return new float[] { 0f, 10f, 20f, 30f, 40f };
        }

        static ReactionMode ModeAt(int index)
        {
            return (ReactionMode)(byte)index;
        }

        [Fact]
        public void ArbitrationPicksSingleHighestPriorityOwner()
        {
            float[] priorities = Priorities();
            for (int mask = 0; mask < 32; mask++)
            {
                ModeRequest[] requests = new ModeRequest[5];
                for (int i = 0; i < 5; i++)
                    requests[i] = new ModeRequest { Mode = ModeAt(i), Active = (mask & (1 << i)) != 0, Priority = priorities[i] };
                ArbitrationResult result = Coordinator.Arbitrate(requests, ReactionMode.Animated, 5f);
                ReactionMode expected = ReactionMode.Animated;
                float best = float.NegativeInfinity;
                for (int i = 0; i < 5; i++)
                {
                    if ((mask & (1 << i)) != 0 && priorities[i] > best)
                    {
                        best = priorities[i];
                        expected = ModeAt(i);
                    }
                }
                Assert.Equal(expected, result.Owner);
                Assert.True((byte)result.Reason <= (byte)ArbitrationReason.Retained);
            }
        }

        [Fact]
        public void LowerPriorityRequestsNeverOverrideHigher()
        {
            ModeRequest[] falling = new ModeRequest[]
            {
                new ModeRequest { Mode = ReactionMode.Animated, Active = true, Priority = 0f },
                new ModeRequest { Mode = ReactionMode.Balancing, Active = true, Priority = 10f },
                new ModeRequest { Mode = ReactionMode.Recovering, Active = true, Priority = 20f },
                new ModeRequest { Mode = ReactionMode.Bracing, Active = true, Priority = 30f },
                new ModeRequest { Mode = ReactionMode.Falling, Active = true, Priority = 40f },
            };
            Assert.Equal(ReactionMode.Falling, Coordinator.Arbitrate(falling, ReactionMode.Balancing, 5f).Owner);
            ModeRequest[] bracing = new ModeRequest[]
            {
                new ModeRequest { Mode = ReactionMode.Animated, Active = true, Priority = 0f },
                new ModeRequest { Mode = ReactionMode.Balancing, Active = true, Priority = 10f },
                new ModeRequest { Mode = ReactionMode.Recovering, Active = true, Priority = 20f },
                new ModeRequest { Mode = ReactionMode.Bracing, Active = true, Priority = 30f },
            };
            Assert.Equal(ReactionMode.Bracing, Coordinator.Arbitrate(bracing, ReactionMode.Balancing, 5f).Owner);
            ModeRequest[] recovering = new ModeRequest[]
            {
                new ModeRequest { Mode = ReactionMode.Animated, Active = true, Priority = 0f },
                new ModeRequest { Mode = ReactionMode.Balancing, Active = true, Priority = 10f },
                new ModeRequest { Mode = ReactionMode.Recovering, Active = true, Priority = 20f },
            };
            Assert.Equal(ReactionMode.Recovering, Coordinator.Arbitrate(recovering, ReactionMode.Animated, 5f).Owner);
        }

        [Fact]
        public void HysteresisHoldsOwnerInsideMarginAndReleasesOutside()
        {
            ModeRequest[] challenged = new ModeRequest[]
            {
                new ModeRequest { Mode = ReactionMode.Balancing, Active = true, Priority = 10f },
                new ModeRequest { Mode = ReactionMode.Bracing, Active = true, Priority = 12f },
            };
            ArbitrationResult held = Coordinator.Arbitrate(challenged, ReactionMode.Balancing, 5f);
            Assert.Equal(ReactionMode.Balancing, held.Owner);
            Assert.Equal(ArbitrationReason.HysteresisHold, held.Reason);
            ModeRequest[] released = new ModeRequest[]
            {
                new ModeRequest { Mode = ReactionMode.Balancing, Active = true, Priority = 10f },
                new ModeRequest { Mode = ReactionMode.Bracing, Active = true, Priority = 16f },
            };
            ArbitrationResult won = Coordinator.Arbitrate(released, ReactionMode.Balancing, 5f);
            Assert.Equal(ReactionMode.Bracing, won.Owner);
            Assert.Equal(ArbitrationReason.HighestPriority, won.Reason);
            ArbitrationResult noMargin = Coordinator.Arbitrate(challenged, ReactionMode.Balancing, float.NaN);
            Assert.Equal(ReactionMode.Bracing, noMargin.Owner);
            ModeRequest[] idle = new ModeRequest[]
            {
                new ModeRequest { Mode = ReactionMode.Falling, Active = false, Priority = 40f },
                new ModeRequest { Mode = ReactionMode.Balancing, Active = true, Priority = 10f },
            };
            ArbitrationResult idleCurrent = Coordinator.Arbitrate(idle, ReactionMode.Falling, 5f);
            Assert.Equal(ReactionMode.Balancing, idleCurrent.Owner);
            Assert.Equal(ArbitrationReason.HighestPriority, idleCurrent.Reason);
        }

        [Fact]
        public void EmptyAndHostileRequestsFallBackToAnimated()
        {
            ArbitrationResult empty = Coordinator.Arbitrate(Array.Empty<ModeRequest>(), ReactionMode.Balancing, 5f);
            Assert.Equal(ReactionMode.Animated, empty.Owner);
            Assert.Equal(ArbitrationReason.NoneActive, empty.Reason);
            ModeRequest[] inactive = new ModeRequest[]
            {
                new ModeRequest { Mode = ReactionMode.Falling, Active = false, Priority = 40f },
            };
            Assert.Equal(ReactionMode.Animated, Coordinator.Arbitrate(inactive, ReactionMode.Falling, 5f).Owner);
            ModeRequest[] poisoned = new ModeRequest[]
            {
                new ModeRequest { Mode = ReactionMode.Bracing, Active = true, Priority = float.NaN },
                new ModeRequest { Mode = (ReactionMode)200, Active = true, Priority = 100f },
            };
            Assert.Equal(ReactionMode.Animated, Coordinator.Arbitrate(poisoned, ReactionMode.Balancing, 5f).Owner);
        }

        [Fact]
        public void FadePairSumsToOneAndMovesMonotonically()
        {
            float fadeTime = 0.25f;
            float[] grid = new float[] { -1f, 0f, 0.0625f, 0.125f, 0.1875f, 0.25f, 0.5f };
            float previous = -1f;
            for (int i = 0; i < grid.Length; i++)
            {
                BlendWeights.FadePair(grid[i], fadeTime, out float staying, out float incoming);
                Assert.True(Math.Abs(staying + incoming - 1f) < 1e-6f);
                Assert.True(incoming >= 0f && incoming <= 1f);
                Assert.True(incoming >= previous);
                previous = incoming;
            }
            BlendWeights.FadePair(0.125f, 0f, out _, out float instant);
            Assert.Equal(1f, instant);
            BlendWeights.FadePair(0.125f, float.NaN, out _, out float nanTime);
            Assert.Equal(1f, nanTime);
            BlendWeights.FadePair(float.NaN, fadeTime, out _, out float nanElapsed);
            Assert.Equal(0f, nanElapsed);
            BlendWeights.FadePair(10f, fadeTime, out _, out float finished);
            Assert.Equal(1f, finished);
        }

        [Fact]
        public void BlendAuthorityHonoursEndpoints()
        {
            float fadeTime = 0.25f;
            Assert.True(Math.Abs(BlendWeights.BlendAuthority(0.2f, 0.9f, 0f, fadeTime) - 0.2f) < 1e-6f);
            Assert.True(Math.Abs(BlendWeights.BlendAuthority(0.2f, 0.9f, 0.25f, fadeTime) - 0.9f) < 1e-6f);
            Assert.True(Math.Abs(BlendWeights.BlendAuthority(0.2f, 0.9f, 10f, fadeTime) - 0.9f) < 1e-6f);
            float mid = BlendWeights.BlendAuthority(0.2f, 0.9f, 0.125f, fadeTime);
            Assert.True(mid > 0.2f && mid < 0.9f);
        }

        [Fact]
        public void CalibrateIgnoresPositionAndKeepsUnitTarget()
        {
            Quaternion pose = Quaternion.Normalize(new Quaternion(0.3f, 0.5f, 0.2f, 0.8f));
            JointDrive left = StanceDrive.Calibrate(pose, 200f, 20f, 500f);
            RoleBody shifted = new RoleBody { Position = new Float3(10f, -4f, 2f), Rotation = pose };
            JointDrive right = StanceDrive.Calibrate(shifted.Rotation, 200f, 20f, 500f);
            Assert.Equal(left.Target.X, right.Target.X);
            Assert.Equal(left.Target.Y, right.Target.Y);
            Assert.Equal(left.Target.Z, right.Target.Z);
            Assert.Equal(left.Target.W, right.Target.W);
            float length = (float)Math.Sqrt(Quaternion.Dot(left.Target, left.Target));
            Assert.True(Math.Abs(length - 1f) < 1e-5f);
            Assert.Equal(0f, left.TargetAngularVelocity.X);
            Assert.Equal(0f, left.TargetAngularVelocity.Y);
            Assert.Equal(0f, left.TargetAngularVelocity.Z);
            JointDrive degenerate = StanceDrive.Calibrate(new Quaternion(0f, 0f, 0f, 0f), 200f, 20f, 500f);
            Assert.Equal(0f, degenerate.Target.X);
            Assert.Equal(1f, degenerate.Target.W);
        }

        [Fact]
        public void StanceResolveMatchesSupportTruthTable()
        {
            CoordinationParams parameters = CoordinationParams.Standard;
            for (int want = 0; want < 4; want++)
            {
                for (int contact = 0; contact < 4; contact++)
                {
                    StanceIntent intent = StanceDrive.Resolve(
                        (want & 1) != 0, (want & 2) != 0, (contact & 1) != 0, (contact & 2) != 0,
                        0.8f, 0.5f, 200f, 200f, parameters);
                    Assert.Equal(((want & 1) != 0) && ((contact & 1) != 0), intent.LeftSupport);
                    Assert.Equal(((want & 2) != 0) && ((contact & 2) != 0), intent.RightSupport);
                }
            }
            StanceIntent clamped = StanceDrive.Resolve(true, true, true, true, 0.4f, 0.9f, 5f, 5000f, parameters);
            Assert.Equal(0.4f, clamped.Weight);
            Assert.Equal(0.4f, clamped.GainWeight);
            Assert.Equal(parameters.MinLegStiffness, clamped.StiffnessLeft);
            Assert.Equal(parameters.MaxLegStiffness, clamped.StiffnessRight);
            StanceIntent nanStiffness = StanceDrive.Resolve(true, true, true, true, 0.8f, 0.5f, float.NaN, float.PositiveInfinity, parameters);
            Assert.Equal(parameters.MinLegStiffness, nanStiffness.StiffnessLeft);
            Assert.Equal(parameters.MinLegStiffness, nanStiffness.StiffnessRight);
            Assert.Equal(3f, StanceDrive.ResponseFor(Role.FootLeft, true, parameters));
            Assert.Equal(1f, StanceDrive.ResponseFor(Role.FootLeft, false, parameters));
            Assert.Equal(2f, StanceDrive.ResponseFor(Role.ShinRight, true, parameters));
            Assert.Equal(2f, StanceDrive.ResponseFor(Role.ThighRight, false, parameters));
            Assert.Equal(0f, StanceDrive.ResponseFor(Role.Head, true, parameters));
        }

        [Fact]
        public void AuthorityTableIsUnitBoundedAndFallingIsPassive()
        {
            for (int r = 0; r < Roles.Count; r++)
            {
                Role role = (Role)(byte)r;
                for (int m = 0; m <= (int)ReactionMode.Recovering; m++)
                {
                    float authority = BlendWeights.Authority(role, (ReactionMode)(byte)m);
                    Assert.True(authority >= 0f && authority <= 1f);
                    if ((ReactionMode)(byte)m == ReactionMode.Falling)
                        Assert.Equal(0f, authority);
                }
            }
            Assert.Equal(0f, BlendWeights.Authority((Role)200, ReactionMode.Balancing));
            Assert.Equal(0f, BlendWeights.Authority(Role.Torso, (ReactionMode)200));
        }

        [Fact]
        public void HostileInputsProduceFiniteBoundedOutputs()
        {
            float[] hostile = new float[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 1e30f, -1e30f };
            Quaternion[] poses = new Quaternion[]
            {
                new Quaternion(float.NaN, 0f, 0f, 1f),
                new Quaternion(0f, 0f, 0f, 0f),
                new Quaternion(1e30f, 1e30f, 1e30f, 1e30f),
                new Quaternion(float.PositiveInfinity, 0f, 0f, 0f),
            };
            CoordinationParams garbage = new CoordinationParams
            {
                HysteresisMargin = float.NaN,
                FadeTime = float.NegativeInfinity,
                MinLegStiffness = float.NaN,
                MaxLegStiffness = -10f,
                SupportedAnkleResponse = float.NaN,
                SupportedKneeResponse = float.PositiveInfinity,
                SwingAnkleResponse = -3f,
                SwingKneeResponse = float.NaN,
            };
            for (int i = 0; i < hostile.Length; i++)
            {
                ModeRequest[] requests = new ModeRequest[]
                {
                    new ModeRequest { Mode = ReactionMode.Falling, Active = true, Priority = hostile[i] },
                    new ModeRequest { Mode = ReactionMode.Animated, Active = true, Priority = hostile[(i + 1) % hostile.Length] },
                };
                ArbitrationResult result = Coordinator.Arbitrate(requests, (ReactionMode)200, hostile[i]);
                Assert.True((byte)result.Owner <= (byte)ReactionMode.Recovering);
                Assert.True((byte)result.Reason <= (byte)ArbitrationReason.Retained);
                Assert.True(Scalar.IsFinite(result.Priority));
                BlendWeights.FadePair(hostile[i], hostile[(i + 1) % hostile.Length], out float staying, out float incoming);
                Assert.True(Scalar.IsFinite(staying) && Scalar.IsFinite(incoming));
                Assert.True(staying >= 0f && staying <= 1f && incoming >= 0f && incoming <= 1f);
                float relaxed = StanceDrive.RelaxAuthority(hostile[i]);
                Assert.True(relaxed >= 0f && relaxed <= 1f && Scalar.IsFinite(relaxed));
                StanceIntent intent = StanceDrive.Resolve(true, true, true, true, hostile[i], hostile[(i + 1) % hostile.Length], hostile[i], hostile[(i + 2) % hostile.Length], garbage);
                Assert.True(Scalar.IsFinite(intent.Weight) && intent.Weight >= 0f && intent.Weight <= 1f);
                Assert.True(Scalar.IsFinite(intent.GainWeight) && intent.GainWeight >= 0f && intent.GainWeight <= intent.Weight);
                Assert.True(Scalar.IsFinite(intent.StiffnessLeft) && Scalar.IsFinite(intent.StiffnessRight));
                for (int p = 0; p < poses.Length; p++)
                {
                    JointDrive drive = StanceDrive.Calibrate(poses[p], hostile[i], hostile[(i + 1) % hostile.Length], hostile[(i + 2) % hostile.Length]);
                    Assert.True(Scalar.IsFinite(drive.Target.X) && Scalar.IsFinite(drive.Target.Y) && Scalar.IsFinite(drive.Target.Z) && Scalar.IsFinite(drive.Target.W));
                    float length = (float)Math.Sqrt(Quaternion.Dot(drive.Target, drive.Target));
                    Assert.True(Math.Abs(length - 1f) < 1e-4f);
                    Assert.True(drive.Stiffness >= 0f && Scalar.IsFinite(drive.Stiffness));
                    Assert.True(drive.Damping >= 0f && Scalar.IsFinite(drive.Damping));
                    Assert.True(drive.MaxTorque >= 0f && Scalar.IsFinite(drive.MaxTorque));
                    JointDrive blended = BlendWeights.BlendDrive(drive, StanceDrive.Calibrate(poses[(p + 1) % poses.Length], 100f, 10f, 300f), hostile[i], hostile[(i + 1) % hostile.Length]);
                    Assert.True(Scalar.IsFinite(blended.Target.X) && Scalar.IsFinite(blended.Stiffness) && blended.Stiffness >= 0f);
                }
            }
        }

        [Fact]
        public void RepeatedCallsReturnIdenticalResults()
        {
            ModeRequest[] requests = new ModeRequest[]
            {
                new ModeRequest { Mode = ReactionMode.Balancing, Active = true, Priority = 10f },
                new ModeRequest { Mode = ReactionMode.Bracing, Active = true, Priority = 12f },
            };
            CoordinationParams parameters = CoordinationParams.Standard;
            Quaternion pose = new Quaternion(0.3f, 0.5f, 0.2f, 0.8f);
            for (int run = 0; run < 2; run++)
            {
                ArbitrationResult result = Coordinator.Arbitrate(requests, ReactionMode.Balancing, parameters.HysteresisMargin);
                Assert.Equal(ReactionMode.Balancing, result.Owner);
                Assert.Equal(ArbitrationReason.HysteresisHold, result.Reason);
                BlendWeights.FadePair(0.125f, parameters.FadeTime, out float staying, out float incoming);
                Assert.True(Math.Abs(staying + incoming - 1f) < 1e-6f);
                JointDrive drive = StanceDrive.Calibrate(pose, 200f, 20f, 500f);
                JointDrive again = StanceDrive.Calibrate(pose, 200f, 20f, 500f);
                Assert.Equal(drive.Target.X, again.Target.X);
                Assert.Equal(drive.Stiffness, again.Stiffness);
                StanceIntent intent = StanceDrive.Resolve(true, false, true, true, 0.8f, 0.5f, 200f, 300f, parameters);
                Assert.True(intent.LeftSupport && !intent.RightSupport);
                Assert.Equal(0.5f, intent.GainWeight);
            }
        }
    }
}
