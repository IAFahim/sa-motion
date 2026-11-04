using System;
using NUnit.Framework;
using SA.Motion.Legs;
using SA.Motion.Numerics;
using MotionQuaternion = SA.Motion.Numerics.Quaternion;

namespace SA.Reactions.Tests
{
    public class InPlaceReplayTests
    {
        const int Flat = 0;
        const int Step = 1;
        const int Tilt = 2;
        const float StepStart = 2f;
        const float StepEnd = 4f;
        const float StepHeight = 0.12f;
        const float StepMidMargin = 0.3f;
        const float AnchorMillimetres = 2f;

        static readonly float TiltSlope = (float)Math.Tan(7.72 * Math.PI / 180.0);
        static readonly FootShape Shape = new FootShape
        {
            Heel = new Float3(-0.113f, -0.05f, 0f),
            Ball = new Float3(-0.113f, 0.12f, 0f)
        };

        sealed class ReplayResult
        {
            public readonly int[] Events = new int[2];
            public int PlantedFrames;
            public int TotalFrames;
            public readonly System.Collections.Generic.List<byte> Planted = new System.Collections.Generic.List<byte>();
            public float MaxSteadyGap;
            public int SteadySamples;
            public float MaxStepMidGap;
            public int StepMidSamples;
        }

        static float GroundHeight(int ground, float z)
        {
            if (ground == Step) return (z >= StepStart && z < StepEnd) ? StepHeight : 0f;
            if (ground == Tilt) return z * TiltSlope;
            return 0f;
        }

        static Float3 ReadPosition(float[] track, int f, int leg)
        {
            var b = f * InPlaceReplayData.Stride + leg * 21;
            return new Float3(track[b], track[b + 1], track[b + 2]);
        }

        static Float3 ReadKnee(float[] track, int f, int leg)
        {
            var b = f * InPlaceReplayData.Stride + leg * 21 + 3;
            return new Float3(track[b], track[b + 1], track[b + 2]);
        }

        static Float3 ReadAnkle(float[] track, int f, int leg)
        {
            var b = f * InPlaceReplayData.Stride + leg * 21 + 6;
            return new Float3(track[b], track[b + 1], track[b + 2]);
        }

        static MotionQuaternion ReadUpper(float[] track, int f, int leg)
        {
            var b = f * InPlaceReplayData.Stride + leg * 21 + 9;
            return new MotionQuaternion(track[b], track[b + 1], track[b + 2], track[b + 3]);
        }

        static MotionQuaternion ReadLower(float[] track, int f, int leg)
        {
            var b = f * InPlaceReplayData.Stride + leg * 21 + 13;
            return new MotionQuaternion(track[b], track[b + 1], track[b + 2], track[b + 3]);
        }

        static MotionQuaternion ReadFoot(float[] track, int f, int leg)
        {
            var b = f * InPlaceReplayData.Stride + leg * 21 + 17;
            return new MotionQuaternion(track[b], track[b + 1], track[b + 2], track[b + 3]);
        }

        static ReplayResult Replay(float[] track, float[] rootZ, int frames, float duration, int ground, int loops, LegParams parameters, bool travelling)
        {
            var result = new ReplayResult();
            var dt = duration / frames;
            var travelPerLoop = rootZ[frames - 1] - rootZ[0];
            var rig = new LegRigState();
            var states = new LegState[2];
            var poses = new LegPose[2];
            var shapes = new[] { Shape, Shape };
            var up = new Float3(0f, 1f, 0f);
            var forward = new Float3(0f, 0f, 1f);
            var length = (float)Math.Sqrt(TiltSlope * TiltSlope + 1f);
            var tiltNormal = new Float3(0f, 1f / length, -TiltSlope / length);
            var blendFrames = (int)Math.Ceiling(parameters.BlendSeconds / dt);
            var previous = new byte[2];
            var age = new int[2];
            for (var loop = 0; loop < loops; loop++)
            {
                for (var f = 0; f < frames; f++)
                {
                    var root = loop * travelPerLoop + (rootZ[f] - rootZ[0]);
                    var own = travelling ? rootZ[f] - rootZ[0] : 0f;
                    var shift = new Float3(0f, 0f, root - own);
                    var follow = ground == Tilt ? GroundHeight(Tilt, root) : 0f;
                    var lift = new Float3(0f, follow, 0f) + shift;
                    var floor = new Float3(0f, follow, root);
                    var inputs = new LegInput[2];
                    for (var leg = 0; leg < 2; leg++)
                    {
                        var ankle = ReadAnkle(track, f, leg) + lift;
                        var height = GroundHeight(ground, ankle.Z);
                        inputs[leg] = new LegInput
                        {
                            Hip = ReadPosition(track, f, leg) + lift,
                            Knee = ReadKnee(track, f, leg) + lift,
                            Ankle = ankle,
                            Upper = ReadUpper(track, f, leg),
                            Lower = ReadLower(track, f, leg),
                            Foot = ReadFoot(track, f, leg),
                            Ground = new GroundHit
                            {
                                Hit = 1,
                                Point = new Float3(ankle.X, height, ankle.Z),
                                Normal = ground == Tilt ? tiltNormal : up
                            },
                            GroundVelocity = Float3.Zero
                        };
                    }
                    LegRig.Step(parameters, InPlaceReplayData.BodyScale, 1f, ref rig, states, inputs, shapes, floor, up, forward, dt, poses);
                    for (var leg = 0; leg < 2; leg++)
                    {
                        if (states[leg].Planted == 1) age[leg]++;
                        else age[leg] = 0;
                        if (loop == 0)
                        {
                            previous[leg] = states[leg].Planted;
                            result.Planted.Add(states[leg].Planted);
                            continue;
                        }
                        result.TotalFrames++;
                        if (states[leg].Planted == 1) result.PlantedFrames++;
                        if (states[leg].Planted == 1 && previous[leg] == 0) result.Events[leg]++;
                        previous[leg] = states[leg].Planted;
                        result.Planted.Add(states[leg].Planted);
                        var nearEdge = ground == Step && ((states[leg].Anchor.Z > StepStart - StepMidMargin && states[leg].Anchor.Z < StepStart + StepMidMargin) || (states[leg].Anchor.Z > StepEnd - StepMidMargin && states[leg].Anchor.Z < StepEnd + StepMidMargin));
                        if (states[leg].Planted == 1 && age[leg] > blendFrames && !nearEdge)
                        {
                            var surface = GroundHeight(ground, states[leg].Anchor.Z);
                            var gap = Math.Abs(states[leg].Anchor.Y - surface);
                            if (gap > result.MaxSteadyGap) result.MaxSteadyGap = gap;
                            result.SteadySamples++;
                            if (ground == Step && states[leg].Anchor.Z > StepStart + StepMidMargin && states[leg].Anchor.Z < StepEnd - StepMidMargin)
                            {
                                var stepGap = Math.Abs(states[leg].Anchor.Y - StepHeight);
                                if (stepGap > result.MaxStepMidGap) result.MaxStepMidGap = stepGap;
                                result.StepMidSamples++;
                            }
                        }
                    }
                }
            }
            return result;
        }

        static MotionQuaternion Norm(MotionQuaternion q)
        {
            var length = (float)Math.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
            return length > 1e-12f ? new MotionQuaternion(q.X / length, q.Y / length, q.Z / length, q.W / length) : new MotionQuaternion(0f, 0f, 0f, 1f);
        }

        static float AngleDegrees(MotionQuaternion a, MotionQuaternion b)
        {
            var u = Norm(a);
            var v = Norm(b);
            var dot = Math.Abs(u.X * v.X + u.Y * v.Y + u.Z * v.Z + u.W * v.W);
            if (dot > 1f) dot = 1f;
            return 2f * (float)Math.Acos(dot) * 57.29578f;
        }

        static float MaxLocalGap(float[] travel, float[] inplace, int frames)
        {
            var worst = 0f;
            for (var f = 0; f < frames; f++)
            {
                for (var bone = 0; bone < 6; bone++)
                {
                    var b = f * InPlaceReplayData.LocalStride + bone * 4;
                    var t = new MotionQuaternion(travel[b], travel[b + 1], travel[b + 2], travel[b + 3]);
                    var p = new MotionQuaternion(inplace[b], inplace[b + 1], inplace[b + 2], inplace[b + 3]);
                    var gap = AngleDegrees(t, p);
                    if (gap > worst) worst = gap;
                }
            }
            return worst;
        }

        static float MaxStrippedHipGap(float[] travel, float[] rootZ, float[] inplace, int frames)
        {
            var worst = 0f;
            for (var f = 0; f < frames; f++)
            {
                var shift = rootZ[f] - rootZ[0];
                for (var leg = 0; leg < 2; leg++)
                {
                    var a = ReadPosition(travel, f, leg);
                    var b = ReadPosition(inplace, f, leg);
                    var dx = a.X - b.X;
                    var dy = a.Y - b.Y;
                    var dz = a.Z - shift - b.Z;
                    var gap = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (gap > worst) worst = gap;
                }
            }
            return worst;
        }

        static double Agreement(ReplayResult travel, ReplayResult inplace)
        {
            Assert.AreEqual(travel.Planted.Count, inplace.Planted.Count);
            var same = 0;
            for (var i = 0; i < travel.Planted.Count; i++)
                if (travel.Planted[i] == inplace.Planted[i]) same++;
            return (double)same / travel.Planted.Count;
        }

        [Test]
        public void TravellingAndInPlaceBakesShareLegChainsAndRootTravel()
        {
            Assert.LessOrEqual(MaxLocalGap(InPlaceReplayData.WalkLocalTravel, InPlaceReplayData.WalkLocalInPlace, InPlaceReplayData.WalkFrames), 0.1f);
            Assert.LessOrEqual(MaxLocalGap(InPlaceReplayData.RunLocalTravel, InPlaceReplayData.RunLocalInPlace, InPlaceReplayData.RunFrames), 0.1f);
            Assert.AreEqual(1.872f, InPlaceReplayData.WalkRootZ[InPlaceReplayData.WalkFrames - 1] - InPlaceReplayData.WalkRootZ[0], 0.001f);
            Assert.AreEqual(2.853f, InPlaceReplayData.RunRootZ[InPlaceReplayData.RunFrames - 1] - InPlaceReplayData.RunRootZ[0], 0.001f);
            Assert.LessOrEqual(MaxStrippedHipGap(InPlaceReplayData.WalkTravel, InPlaceReplayData.WalkRootZ, InPlaceReplayData.WalkInPlace, InPlaceReplayData.WalkFrames), 0.06f);
            Assert.LessOrEqual(MaxStrippedHipGap(InPlaceReplayData.RunTravel, InPlaceReplayData.RunRootZ, InPlaceReplayData.RunInPlace, InPlaceReplayData.RunFrames), 0.15f);
        }

        [Test]
        public void WalkGentleInPlaceMatchesTravellingOnFlatStepAndTilt()
        {
            foreach (var ground in new[] { Flat, Step, Tilt })
            {
                var travel = Replay(InPlaceReplayData.WalkTravel, InPlaceReplayData.WalkRootZ, InPlaceReplayData.WalkFrames, InPlaceReplayData.WalkDuration, ground, 4, LegParams.Gentle, true);
                var inplace = Replay(InPlaceReplayData.WalkInPlace, InPlaceReplayData.WalkRootZ, InPlaceReplayData.WalkFrames, InPlaceReplayData.WalkDuration, ground, 4, LegParams.Gentle, false);
                Assert.AreEqual(3, travel.Events[0]);
                Assert.AreEqual(3, travel.Events[1]);
                Assert.AreEqual(travel.Events[0], inplace.Events[0]);
                Assert.AreEqual(travel.Events[1], inplace.Events[1]);
                Assert.GreaterOrEqual(Agreement(travel, inplace), 0.8);
            }
        }

        [Test]
        public void WalkGentleAnchorsRestOnFlatStepAndTilt()
        {
            foreach (var ground in new[] { Flat, Step, Tilt })
            {
                AssertAnchors(Replay(InPlaceReplayData.WalkTravel, InPlaceReplayData.WalkRootZ, InPlaceReplayData.WalkFrames, InPlaceReplayData.WalkDuration, ground, 4, LegParams.Gentle, true), ground);
                AssertAnchors(Replay(InPlaceReplayData.WalkInPlace, InPlaceReplayData.WalkRootZ, InPlaceReplayData.WalkFrames, InPlaceReplayData.WalkDuration, ground, 4, LegParams.Gentle, false), ground);
            }
        }

        static void AssertAnchors(ReplayResult result, int ground)
        {
            Assert.Greater(result.SteadySamples, 0);
            Assert.LessOrEqual(result.MaxSteadyGap * 1000f, AnchorMillimetres);
            if (ground == Step)
            {
                Assert.Greater(result.StepMidSamples, 0);
                Assert.LessOrEqual(result.MaxStepMidGap * 1000f, AnchorMillimetres);
            }
        }

        [Test]
        public void RunContactInPlaceMatchesTravelling()
        {
            foreach (var ground in new[] { Flat, Step, Tilt })
            {
                var travel = Replay(InPlaceReplayData.RunTravel, InPlaceReplayData.RunRootZ, InPlaceReplayData.RunFrames, InPlaceReplayData.RunDuration, ground, 8, LegParams.Contact, true);
                var inplace = Replay(InPlaceReplayData.RunInPlace, InPlaceReplayData.RunRootZ, InPlaceReplayData.RunFrames, InPlaceReplayData.RunDuration, ground, 8, LegParams.Contact, false);
                Assert.AreEqual(7, travel.Events[0]);
                Assert.AreEqual(7, travel.Events[1]);
                Assert.AreEqual(7, inplace.Events[0]);
                Assert.AreEqual(7, inplace.Events[1]);
                Assert.GreaterOrEqual(Agreement(travel, inplace), 0.8);
                Assert.That(100.0 * travel.PlantedFrames / travel.TotalFrames, Is.InRange(15.0, 45.0));
                Assert.That(100.0 * inplace.PlantedFrames / inplace.TotalFrames, Is.InRange(15.0, 45.0));
            }
        }

        [Test]
        public void RunGentleSkimsOnBothBakes()
        {
            foreach (var ground in new[] { Flat, Step, Tilt })
            {
                var travel = Replay(InPlaceReplayData.RunTravel, InPlaceReplayData.RunRootZ, InPlaceReplayData.RunFrames, InPlaceReplayData.RunDuration, ground, 8, LegParams.Gentle, true);
                var inplace = Replay(InPlaceReplayData.RunInPlace, InPlaceReplayData.RunRootZ, InPlaceReplayData.RunFrames, InPlaceReplayData.RunDuration, ground, 8, LegParams.Gentle, false);
                Assert.LessOrEqual(100.0 * travel.PlantedFrames / travel.TotalFrames, 10.0);
                Assert.LessOrEqual(100.0 * inplace.PlantedFrames / inplace.TotalFrames, 10.0);
                Assert.GreaterOrEqual(Agreement(travel, inplace), 0.8);
                Assert.AreEqual(7, travel.Events[0]);
                Assert.AreEqual(0, travel.Events[1]);
                Assert.AreEqual(7, inplace.Events[0]);
                Assert.AreEqual(0, inplace.Events[1]);
            }
        }
    }
}
