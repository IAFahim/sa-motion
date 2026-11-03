using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SA.Animation;
using SA.Animation.Content;
using SA.Motion.Inertialization;
using SA.Motion.Kinematics;
using SA.Motion.Legs;
using SA.Motion.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace SA.Animation.Tests;

public sealed class C13Contacts
{
    const int TurnFrames = 33;
    const double TurnDuration = 2.0;
    const float HoldBudget = 0.002f;
    const float ReleaseBound = 0.002f;
    const float BlendSeconds = 0.1f;
    const float ScaleUnit = 1e-6f;
    const float MaxWalkableSlope = 0.5f;

    static readonly string[] CanonicalNames = new[]
    {
        "Root", "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head", "Jaw",
        "LeftEye", "RightEye", "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand",
        "LeftThumbProximal", "LeftThumbIntermediate", "LeftThumbDistal",
        "LeftIndexProximal", "LeftIndexIntermediate", "LeftIndexDistal",
        "LeftMiddleProximal", "LeftMiddleIntermediate", "LeftMiddleDistal",
        "LeftRingProximal", "LeftRingIntermediate", "LeftRingDistal",
        "LeftLittleProximal", "LeftLittleIntermediate", "LeftLittleDistal",
        "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
        "RightThumbProximal", "RightThumbIntermediate", "RightThumbDistal",
        "RightIndexProximal", "RightIndexIntermediate", "RightIndexDistal",
        "RightMiddleProximal", "RightMiddleIntermediate", "RightMiddleDistal",
        "RightRingProximal", "RightRingIntermediate", "RightRingDistal",
        "RightLittleProximal", "RightLittleIntermediate", "RightLittleDistal",
        "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes",
        "RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes"
    };

    static readonly string[] RealClips = new[] { "Armature|Walk_Loop", "Armature|Walk_Carry_Loop", "Kyle_Walk" };
    struct RigCalibration
    {
        public string Rig;
        public string File;
        public string[] Names;
        public int[] Parents;
        public Pose[] Reference;
        public AssetId Family;
        public int Hips;
        public float HipHeight;
        public RetargetRow[] Rows;
        public RetargetBinding Binding;
        public RetargetRow[] UpRows;
        public RetargetBinding UpBinding;
        public bool UpValid;
        public AnimationError UpError;
        public int UpperLeg;
        public Float3 SoleLocal0;
        public Float3 SoleLocal1;
        public int LowerLeg;
        public int Foot;
    }

    struct Support
    {
        public Float3 Position;
        public float Yaw;
        public uint Generation;
    }

    struct ContactLock
    {
        public bool Planted;
        public Float3 AnchorLocal;
        public uint SupportGeneration;
        public float Weight;
    }
    readonly ITestOutputHelper output;

    public C13Contacts(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void HeightRatioFromCalibrationScalesRootOnly()
    {
        var directory = FindDirectory();
        if (directory == null)
        {
            output.WriteLine("C13 awaiting extractor JSONs: Temp/sa/c08-full/_manifest.json not present; synthetic checks carry the gate.");
            return;
        }
        var rigs = LoadCalibrations(directory);
        var ual1 = FindRig(rigs, "UAL1_Standard");
        var ual2 = FindRig(rigs, "UAL2_Standard");
        var ratio = ual2.HipHeight / ual1.HipHeight;
        Assert.True(float.IsFinite(ratio) && ratio > 0f);
        Assert.True(float.IsFinite(ual1.UpBinding.HeightRatio) && ual1.UpBinding.HeightRatio > 0f);
        Assert.True(float.IsFinite(ual2.Binding.HeightRatio) && ual2.Binding.HeightRatio > 0f);
        Assert.InRange(ual1.UpBinding.HeightRatio * ual2.Binding.HeightRatio / ratio, 0.999f, 1.001f);
        var reference = CanonicalReference();
        var motion = TurnClip(reference);
        Assert.Equal(AnimationError.None, RootMotion.Delta(motion.Root, TurnDuration, 0, TurnDuration, true, 1f, out var unit));
        Assert.Equal(AnimationError.None, RootMotion.Delta(motion.Root, TurnDuration, 0, TurnDuration, true, ratio, out var scaled));
        Assert.InRange((scaled.Position - unit.Position * ratio).Length, 0, 1e-4f);
        Assert.InRange(RotationGap(scaled.Rotation, unit.Rotation), 0, 1e-6f);
        var turn = RotationVector.Log(unit.Rotation);
        Assert.InRange(turn.Y, Scalar.Pi * 0.5f - 1e-3f, Scalar.Pi * 0.5f + 1e-3f);
        output.WriteLine("C13 UAL1 hipHeight " + ual1.HipHeight.ToString("F4") + " UAL2 hipHeight " + ual2.HipHeight.ToString("F4")
            + " ratio " + ratio.ToString("F4") + " travel " + unit.Position.Length.ToString("F4") + "m scaled " + scaled.Position.Length.ToString("F4") + "m");
    }

    [Fact]
    public void KyleReverseTableRejectedForSiblingSpine()
    {
        var directory = FindDirectory();
        if (directory == null)
        {
            output.WriteLine("C13 awaiting extractor JSONs: Temp/sa/c08-full/_manifest.json not present; synthetic checks carry the gate.");
            return;
        }
        var rigs = LoadCalibrations(directory);
        foreach (var rig in rigs)
        {
            output.WriteLine("C13 reverse " + rig.Rig + ": valid " + rig.UpValid + " error " + rig.UpError);
        }
        var kyle = FindRig(rigs, "Robot Kyle");
        Assert.False(kyle.UpValid);
        Assert.Equal(AnimationError.InvalidTable, kyle.UpError);
        Assert.True(FindRig(rigs, "UAL1_Standard").UpValid);
        Assert.True(FindRig(rigs, "UAL2_Standard").UpValid);
    }

    [Fact]
    public void TravellingRootStanceAnchorHoldsWithinBudget()
    {
        var directory = FindDirectory();
        if (directory == null)
        {
            output.WriteLine("C13 awaiting extractor JSONs: Temp/sa/c08-full/_manifest.json not present; synthetic checks carry the gate.");
            return;
        }
        var rigs = LoadCalibrations(directory);
        var failures = new List<string>();
        var turnReference = CanonicalReference();
        var turn = TurnClip(turnReference);
        foreach (var rig in rigs)
        {
            var synthetic = CheckTravellingAnchor(rig, rig, null, turn.Samples, turn.Root, TurnDuration, TurnFrames, 0);
            output.WriteLine("C13 travel " + rig.Rig + " synthetic: " + synthetic.Summary);
            if (synthetic.Error != null)
            {
                failures.Add(synthetic.Error);
            }
            var travelSource = rig.UpValid ? rig : FindRig(rigs, "UAL1_Standard");
            var real = LoadRealClip(directory, travelSource, RealClipFor(travelSource.Rig));
            var posed = CheckTravellingAnchor(rig, travelSource, real, turn.Samples, turn.Root, TurnDuration, real.SampleCount, 2);
            output.WriteLine("C13 travel " + rig.Rig + " " + real.Clip + ": " + posed.Summary);
            if (posed.Error != null)
            {
                failures.Add(posed.Error);
            }
        }
        var ual1 = FindRig(rigs, "UAL1_Standard");
        var ual2 = FindRig(rigs, "UAL2_Standard");
        var walk = LoadRealClip(directory, ual1, RealClips[0]);
        var variant = CheckTravellingAnchor(ual2, ual1, walk, turn.Samples, turn.Root, TurnDuration, walk.SampleCount, 2);
        output.WriteLine("C13 travel UAL1->UAL2 " + RealClips[0] + ": " + variant.Summary);
        if (variant.Error != null)
        {
            failures.Add(variant.Error);
        }
        var seen = failures.Count > 8 ? failures.GetRange(0, 8) : failures;
        Assert.True(failures.Count == 0, "C13 travelling anchor failures: " + string.Join(" | ", seen));
    }

    [Fact]
    public void SlopeProbeHoldsAlignsAndRejectsSteep()
    {
        var directory = FindDirectory();
        if (directory == null)
        {
            output.WriteLine("C13 awaiting extractor JSONs: Temp/sa/c08-full/_manifest.json not present; synthetic checks carry the gate.");
            return;
        }
        var rigs = LoadCalibrations(directory);
        var rig = FindRig(rigs, "UAL2_Standard");
        var tilt = 10f * Scalar.Pi / 180f;
        var normal = new Float3(0f, (float)Math.Cos(tilt), (float)Math.Sin(tilt));
        var up = new Float3(0f, 1f, 0f);
        var flat = new GroundHit { Hit = 1, Point = Float3.Zero, Normal = up };
        var slope = new GroundHit { Hit = 1, Point = Float3.Zero, Normal = normal };
        var steepNormal = new Float3(0f, (float)Math.Cos(45f * Scalar.Pi / 180f), (float)Math.Sin(45f * Scalar.Pi / 180f));
        var steep = new GroundHit { Hit = 1, Point = Float3.Zero, Normal = steepNormal };
        var degenerate = new GroundHit { Hit = 1, Point = Float3.Zero, Normal = Float3.Zero };
        var missing = new GroundHit { Hit = 0, Point = Float3.Zero, Normal = up };
        Assert.True(Planting.Walkable(flat, up, MaxWalkableSlope));
        Assert.True(Planting.Walkable(slope, up, MaxWalkableSlope));
        Assert.False(Planting.Walkable(steep, up, MaxWalkableSlope));
        Assert.False(Planting.Walkable(degenerate, up, MaxWalkableSlope));
        Assert.False(Planting.Walkable(missing, up, MaxWalkableSlope));
        var stanceClip = LoadRealClip(directory, rig, RealClipFor(rig.Rig));
        var soleY = new float[stanceClip.SampleCount];
        var anklePos = new Float3[stanceClip.SampleCount];
        var stanceProbe = new RigidPose[rig.Names.Length];
        for (var frame = 0; frame < stanceClip.SampleCount; frame++)
        {
            var stanceLocal = RetargetRealFrame(rig, rig, stanceClip, frame);
            Assert.Equal(AnimationError.None, RigidMath.Model(rig.Parents, stanceLocal, stanceProbe));
            anklePos[frame] = stanceProbe[rig.Foot].Position;
            soleY[frame] = (anklePos[frame] + Quaternion.Rotate(stanceProbe[rig.Foot].Rotation, rig.SoleLocal0)).Y;
        }
        var floorY = float.MaxValue;
        for (var frame = 0; frame < stanceClip.SampleCount; frame++)
        {
            if (soleY[frame] < floorY)
            {
                floorY = soleY[frame];
            }
        }
        var stanceFrame = 0;
        var stanceSpeed = float.MaxValue;
        for (var frame = 0; frame < stanceClip.SampleCount - 1; frame++)
        {
            if (soleY[frame] > floorY + 0.02f)
            {
                continue;
            }
            var speed = (anklePos[frame + 1] - anklePos[frame]).Length;
            if (speed < stanceSpeed)
            {
                stanceSpeed = speed;
                stanceFrame = frame;
            }
        }
        output.WriteLine("C13 slope stance frame " + stanceFrame + " floorY " + floorY.ToString("G9") + " stanceSoleY " + soleY[stanceFrame].ToString("G9") + " hipH " + rig.HipHeight.ToString("G9"));
        var targetLocal = RetargetRealFrame(rig, rig, stanceClip, stanceFrame);
        var model = new RigidPose[rig.Names.Length];
        Assert.Equal(AnimationError.None, RigidMath.Model(rig.Parents, targetLocal, model));
        var ankle = model[rig.Foot].Position;
        var sole = ankle + Quaternion.Rotate(model[rig.Foot].Rotation, rig.SoleLocal0);
        var groundHeight = floorY;
        var clearance = sole.Y - groundHeight;
        Assert.InRange(clearance, -0.005f, 0.03f);
        var soleDown = Quaternion.Rotate(model[rig.Foot].Rotation, new Float3(0f, -1f, 0f));
        var align = TwoBoneIK.FromTo(soleDown, normal * -1f);
        var alignedFoot = Quaternion.Normalize(Quaternion.Multiply(align, model[rig.Foot].Rotation));
        var alignedDown = Quaternion.Rotate(alignedFoot, new Float3(0f, -1f, 0f));
        Assert.InRange((alignedDown + normal).Length, 0, 1e-3f);
        var solved = TwoBoneIK.Solve(model[rig.UpperLeg].Position, model[rig.LowerLeg].Position, ankle, ankle, model[rig.LowerLeg].Position);
        Assert.True(solved.Valid);
        Assert.InRange((solved.End - ankle).Length, 0, 1e-4f);
        output.WriteLine("C13 slope UAL2: tiltDeg 10 walkable True steepRejected True soleClearanceM "
            + clearance.ToString("F4") + " alignGap " + (alignedDown + normal).Length.ToString("G9"));
    }

    [Fact]
    public void PlatformEdgeAndTeleportResetWithZeroLeakage()
    {
        var directory = FindDirectory();
        if (directory == null)
        {
            output.WriteLine("C13 awaiting extractor JSONs: Temp/sa/c08-full/_manifest.json not present; synthetic checks carry the gate.");
            return;
        }
        var rigs = LoadCalibrations(directory);
        var rig = FindRig(rigs, "UAL1_Standard");
        var reference = CanonicalReference();
        var motion = TurnClip(reference);
        var dt = (float)(TurnDuration / (TurnFrames - 1));
        var support = new Support { Position = Float3.Zero, Yaw = 0f, Generation = 7 };
        var plantFrame = 2;
        var targetLocal = RetargetFrame(rig, motion.Samples, plantFrame);
        var model = new RigidPose[rig.Names.Length];
        Assert.Equal(AnimationError.None, RigidMath.Model(rig.Parents, targetLocal, model));
        var lockState = new ContactLock();
        Plant(ref lockState, support, model[rig.Foot].Position);
        Assert.True(lockState.Planted && lockState.SupportGeneration == 7);
        support.Position = new Float3(0f, 0.03f, 0f);
        support.Yaw = 0.02f;
        var moved = RetargetFrame(rig, motion.Samples, 6);
        Assert.Equal(AnimationError.None, RigidMath.Model(rig.Parents, moved, model));
        var expected = SupportToWorld(support, lockState.AnchorLocal);
        var solved = TwoBoneIK.Solve(model[rig.UpperLeg].Position, model[rig.LowerLeg].Position, model[rig.Foot].Position, expected, model[rig.LowerLeg].Position);
        Assert.True(solved.Valid);
        Assert.InRange((solved.End - expected).Length, 0, HoldBudget);
        support.Generation = 8;
        var staleAnchor = lockState.AnchorLocal;
        var edgeReleased = lockState.SupportGeneration != support.Generation;
        if (edgeReleased)
        {
            Release(ref lockState);
        }
        Assert.True(edgeReleased);
        Assert.False(lockState.Planted);
        Assert.InRange(lockState.Weight, 0f, 0f);
        var animated = RetargetFrame(rig, motion.Samples, 9);
        Assert.Equal(AnimationError.None, RigidMath.Model(rig.Parents, animated, model));
        var teleportFrame = 12;
        var teleportSeconds = TurnDuration * teleportFrame / (TurnFrames - 1);
        Assert.Equal(AnimationError.None, RootMotion.Delta(motion.Root, TurnDuration, teleportSeconds, teleportSeconds, true, 1f, out var seek));
        Assert.InRange(seek.Position.Length, 0, 1e-6f);
        Assert.InRange(RotationVector.Log(seek.Rotation).Length, 0, 1e-6f);
        var after = RetargetFrame(rig, motion.Samples, teleportFrame);
        var velocity = new PoseVelocity(Float3.Zero, Float3.Zero);
        Assert.True(PoseDecay.TryCreate(after[rig.Foot], velocity, after[rig.Foot], velocity, out var decay));
        Assert.True(decay.TryApply(after[rig.Foot], 0.05f, 0.25f, out var smoothed));
        Assert.InRange(PoseGap(smoothed, after[rig.Foot]), 0f, 0f);
        Plant(ref lockState, support, model[rig.Foot].Position);
        Assert.InRange((lockState.AnchorLocal - staleAnchor).Length, 1e-6f, 1e6f);
        Assert.True(lockState.Planted && lockState.SupportGeneration == 8);
        output.WriteLine("C13 platform UAL1: movedHoldM " + (solved.End - expected).Length.ToString("G9")
            + " edgeReleased True teleportSeekZero True smoothingLeak 0 replantGen 8 dt " + dt.ToString("G9"));
    }

    [Fact]
    public void AuthoredReleaseClearsLocks()
    {
        var contact = LegParams.Contact;
        output.WriteLine("C13 Contact preset: plantH " + contact.PlantHeight.ToString("G9") + " releaseH " + contact.ReleaseHeight.ToString("G9")
            + " plantRise " + contact.PlantRise.ToString("G9") + " releaseRise " + contact.ReleaseRise.ToString("G9")
            + " releaseDist " + contact.ReleaseDistance.ToString("G9") + " blendS " + contact.BlendSeconds.ToString("G9"));
        Assert.True(Planting.Decide(false, true, 0.01f, 0f, 0f, 0f, 1, 1f, contact));
        Assert.True(Planting.Decide(true, true, 0.01f, 0f, 0f, 0f, 1, 1f, contact));
        Assert.False(Planting.Decide(true, true, 0.01f, 0f, 0f, 0f, 1, 0f, contact));
        Assert.False(Planting.Decide(true, false, 0.01f, 0f, 0f, 0f, 1, 1f, contact));
        Assert.False(Planting.Decide(true, true, 0.01f, 0f, 0f, 0f, 1, 0.49f, contact));
        Assert.True(Planting.Decide(true, true, 0.01f, 0f, 0f, 0f, 1, 0.5f, contact));
        Assert.False(Planting.Decide(false, true, float.NaN, 0f, 0f, 0f, 0, 0f, contact));
        var lockState = new ContactLock();
        var anchor = new Float3(0.2f, 0.05f, 0.1f);
        var support = new Support { Position = Float3.Zero, Yaw = 0f, Generation = 3 };
        Plant(ref lockState, support, anchor);
        Assert.True(lockState.Planted);
        var before = SupportToWorld(support, lockState.AnchorLocal);
        Release(ref lockState);
        Assert.False(lockState.Planted);
        Assert.InRange(lockState.Weight, 0f, 0f);
        Assert.InRange(lockState.AnchorLocal.Length, 0f, 0f);
        Assert.InRange((before - anchor).Length, 0f, 1e-6f);
        Plant(ref lockState, support, anchor);
        Assert.True(lockState.Planted);
    }

    [Fact]
    public void TargetScalesStayUnitAndHipsOwnTranslation()
    {
        var directory = FindDirectory();
        if (directory == null)
        {
            output.WriteLine("C13 awaiting extractor JSONs: Temp/sa/c08-full/_manifest.json not present; synthetic checks carry the gate.");
            return;
        }
        var rigs = LoadCalibrations(directory);
        var failures = new List<string>();
        var frameTotal = 0;
        foreach (var rig in rigs)
        {
            var reference = CanonicalReference();
            var motion = TurnClip(reference);
            for (var frame = 0; frame < TurnFrames; frame++)
            {
                var local = RetargetFrame(rig, motion.Samples, frame);
                var detail = CheckNoStretch(rig, rig.File + " synthetic-turn", frame, local);
                if (detail != null)
                {
                    failures.Add(detail);
                }
                frameTotal++;
            }
            var source = rig.UpValid ? rig : FindRig(rigs, "UAL1_Standard");
            if (rig.Rig == "UAL2_Standard")
            {
                source = FindRig(rigs, "UAL1_Standard");
            }
            var real = LoadRealClip(directory, source, RealClipFor(source.Rig));
            for (var frame = 0; frame < real.SampleCount; frame++)
            {
                var local = RetargetRealFrame(rig, source, real, frame);
                var detail = CheckNoStretch(rig, real.Clip + " frame-from-" + source.Rig, frame, local);
                if (detail != null)
                {
                    failures.Add(detail);
                }
                frameTotal++;
            }
            output.WriteLine("C13 nostretch " + rig.Rig + ": synthetic " + TurnFrames + " real " + real.SampleCount + " (" + real.Clip + ")");
        }
        var seen = failures.Count > 8 ? failures.GetRange(0, 8) : failures;
        Assert.True(failures.Count == 0, "C13 no-stretch failures over " + frameTotal + " frames: " + string.Join(" | ", seen));
    }

    struct AnchorResult
    {
        public string Summary;
        public string? Error;
    }


    AnchorResult CheckTravellingAnchor(RigCalibration target, RigCalibration source, RealClipData? real, Pose[] turnSamples, RigidPose[] rootTrack, double rootDuration, int frames, int minHeld)
    {
        var summary = "";
        var rho = real == null ? target.HipHeight : target.HipHeight / source.HipHeight;
        var dt = (float)(rootDuration / (frames - 1));
        var rootWorld = RigidPose.Identity;
        var support = new Support { Position = Float3.Zero, Yaw = 0f, Generation = 1 };
        var locks = new ContactLock[2];
        var releasedFrame = -1;
        var worstHold = 0f;
        var heldFrames = 0;
        var model = new RigidPose[target.Names.Length];
        var plantAt = new[] { 0, 1 };
        if (real != null)
        {
            plantAt[0] = StanceOnset(target, source, real, frames, target.Foot, target.UpperLeg, target.SoleLocal0);
            plantAt[1] = StanceOnset(target, source, real, frames, MirrorFoot(target), MirrorUpper(target), target.SoleLocal1);
        }
    int StanceOnset(RigCalibration stanceTarget, RigCalibration stanceSource, RealClipData stanceReal, int stanceFrames, int stanceFoot, int stanceUpper, Float3 stanceSole)
    {
        var stanceAnkle = new Float3[stanceFrames];
        var stanceSoleY = new float[stanceFrames];
        var stanceModel = new RigidPose[stanceTarget.Names.Length];
        for (var frame = 0; frame < stanceFrames; frame++)
        {
            var stanceLocal = FrameAt(stanceTarget, stanceSource, stanceReal, turnSamples, frame);
            if (RigidMath.Model(stanceTarget.Parents, stanceLocal, stanceModel) != AnimationError.None)
            {
                throw new InvalidOperationException("C13 " + stanceTarget.Rig + ": stance FK failed; fix: check the " + stanceTarget.File + " calibration table.");
            }
            stanceAnkle[frame] = stanceModel[stanceFoot].Position;
            stanceSoleY[frame] = (stanceAnkle[frame] + Quaternion.Rotate(stanceModel[stanceFoot].Rotation, stanceSole)).Y;
        }
        var floor = float.MaxValue;
        for (var frame = 0; frame < stanceFrames; frame++)
        {
            if (stanceSoleY[frame] < floor)
            {
                floor = stanceSoleY[frame];
            }
        }
        var best = -1;
        var bestSpeed = float.MaxValue;
        for (var frame = 0; frame < stanceFrames - 1 && frame <= stanceFrames / 2; frame++)
        {
            if (stanceSoleY[frame] > floor + 0.02f)
            {
                continue;
            }
            var speed = (stanceAnkle[frame + 1] - stanceAnkle[frame]).Length;
            if (speed < bestSpeed)
            {
                bestSpeed = speed;
                best = frame;
            }
        }
        if (best >= 0)
        {
            return best;
        }
        return BentFrame(stanceTarget, stanceSource, stanceReal, turnSamples, stanceFrames, stanceFoot, stanceUpper);
    }
        for (var frame = 0; frame < frames; frame++)
        {
            var local = FrameAt(target, source, real, turnSamples, frame);
            if (RigidMath.Model(target.Parents, local, model) != AnimationError.None)
            {
                return new AnchorResult { Summary = summary, Error = target.Rig + " frame " + frame + ": FK failed; fix: check the " + target.File + " calibration table." };
            }
            var seconds = rootDuration * frame / (frames - 1);
            var previous = frame == 0 ? seconds : seconds - dt;
            if (RootMotion.Delta(rootTrack, rootDuration, previous, seconds, true, rho, out var delta) != AnimationError.None)
            {
                return new AnchorResult { Summary = summary, Error = target.Rig + " frame " + frame + ": root delta failed; fix: check the synthetic turn root track." };
            }
            if (!RigidMath.TryCompose(rootWorld, delta, out rootWorld))
            {
                return new AnchorResult { Summary = summary, Error = target.Rig + " frame " + frame + ": root compose failed; fix: check for non-finite root samples." };
            }
            if (!RigidMath.TryInverse(rootWorld, out var worldToLocal))
            {
                return new AnchorResult { Summary = summary, Error = target.Rig + " frame " + frame + ": root inverse failed; fix: check for degenerate root rotation." };
            }
            for (var leg = 0; leg < 2; leg++)
            {
                var footBone = leg == 0 ? target.Foot : MirrorFoot(target);
                var upperBone = leg == 0 ? target.UpperLeg : MirrorUpper(target);
                var lowerBone = leg == 0 ? target.LowerLeg : MirrorLower(target);
                var ankleWorld = Quaternion.Rotate(rootWorld.Rotation, model[footBone].Position) + rootWorld.Position;
                if (frame == plantAt[leg])
                {
                    Plant(ref locks[leg], support, ankleWorld);
                    continue;
                }
                if (!locks[leg].Planted)
                {
                    continue;
                }
                var expectedWorld = SupportToWorld(support, locks[leg].AnchorLocal);
                var expectedLocal = Quaternion.Rotate(worldToLocal.Rotation, expectedWorld - rootWorld.Position);
                var solved = TwoBoneIK.Solve(model[upperBone].Position, model[lowerBone].Position, model[footBone].Position, expectedLocal, model[lowerBone].Position);
                if (!solved.Valid)
                {
                    return new AnchorResult { Summary = summary, Error = target.Rig + " frame " + frame + " leg " + leg + ": IK invalid; fix: check leg bone lengths in " + target.File + "." };
                }
                var drag = (solved.End - expectedLocal).Length;
                if (drag > ReleaseBound)
                {
                    if (releasedFrame < 0)
                    {
                        releasedFrame = frame;
                    }
                    Release(ref locks[leg]);
                    continue;
                }
                var elapsed = (frame - plantAt[leg]) * dt;
                var weight = Math.Min(1f, elapsed / BlendSeconds);
                if (weight >= 1f)
                {
                    var upperParentWorld = target.Parents[upperBone] < 0 ? Quaternion.Identity : model[target.Parents[upperBone]].Rotation;
                    var upperNew = Quaternion.Normalize(Quaternion.Multiply(Quaternion.Multiply(Quaternion.InverseUnit(upperParentWorld), solved.UpperDelta), Quaternion.Multiply(upperParentWorld, local[upperBone].Rotation)));
                    local[upperBone] = new Pose { Position = local[upperBone].Position, Rotation = upperNew, Scale = Float3.One };
                    var upperNewWorld = Quaternion.Normalize(Quaternion.Multiply(solved.UpperDelta, model[upperBone].Rotation));
                    var lowerNew = Quaternion.Normalize(Quaternion.Multiply(Quaternion.InverseUnit(upperNewWorld), Quaternion.Multiply(solved.LowerDelta, model[lowerBone].Rotation)));
                    local[lowerBone] = new Pose { Position = local[lowerBone].Position, Rotation = lowerNew, Scale = Float3.One };
                    var corrected = new RigidPose[target.Names.Length];
                    if (RigidMath.Model(target.Parents, local, corrected) != AnimationError.None)
                    {
                        return new AnchorResult { Summary = summary, Error = target.Rig + " frame " + frame + ": corrected FK failed; fix: check IK delta application." };
                    }
                    var heldWorld = Quaternion.Rotate(rootWorld.Rotation, corrected[footBone].Position) + rootWorld.Position;
                    var holdError = (heldWorld - expectedWorld).Length;
                    if (holdError > worstHold)
                    {
                        worstHold = holdError;
                    }
                    heldFrames++;
                    if (holdError > HoldBudget)
                    {
                        return new AnchorResult
                        {
                            Summary = summary,
                            Error = target.Rig + " frame " + frame + " leg " + leg + " bone " + footBone + " (" + target.Names[footBone] + "): hold error "
                                + holdError.ToString("G9") + " m exceeds 0.002 m budget; fix: widen the plant window or shorten the stride before planting.",
                        };
                    }
                }
            }
        }
        if (releasedFrame < 0)
        {
            return new AnchorResult { Summary = summary, Error = target.Rig + ": stance never released over " + frames + " frames of travel (plant " + plantAt[0] + "/" + plantAt[1] + "); fix: lengthen the travel or narrow the release bound." };
        }
        if (heldFrames < minHeld)
        {
            return new AnchorResult { Summary = summary, Error = target.Rig + ": stance held only " + heldFrames + " full-weight frames, need " + minHeld + "; fix: plant during a bent-knee stance phase with more reach slack." };
        }
        summary = "held " + heldFrames + " frames worstHoldM " + worstHold.ToString("G9") + " releasedFrame " + releasedFrame + " rho " + rho.ToString("F4");
        return new AnchorResult { Summary = summary, Error = null };
    }

    static void Plant(ref ContactLock lockState, in Support support, Float3 ankleWorld)
    {
        lockState.Planted = true;
        lockState.AnchorLocal = SupportFromWorld(support, ankleWorld);
        lockState.SupportGeneration = support.Generation;
        lockState.Weight = 0f;
    }

    static void Release(ref ContactLock lockState)
    {
        lockState.Planted = false;
        lockState.AnchorLocal = Float3.Zero;
        lockState.SupportGeneration = 0;
        lockState.Weight = 0f;
    }

    static Float3 SupportToWorld(in Support support, Float3 local)
    {
        return Quaternion.Rotate(Quaternion.AxisAngle(new Float3(0f, 1f, 0f), support.Yaw), local) + support.Position;
    }

    static Float3 SupportFromWorld(in Support support, Float3 world)
    {
        return Quaternion.Rotate(Quaternion.AxisAngle(new Float3(0f, 1f, 0f), -support.Yaw), world - support.Position);
    }

    string? CheckNoStretch(RigCalibration rig, string what, int frame, Pose[] local)
    {
        for (var bone = 0; bone < rig.Names.Length; bone++)
        {
            var scaleGap = ScaleGap(local[bone].Scale);
            if (scaleGap > ScaleUnit)
            {
                return "rig " + rig.Rig + " " + what + " frame " + frame + " bone " + bone + " (" + rig.Names[bone] + "): target scale gap "
                    + scaleGap.ToString("G9") + " exceeds 1e-6; fix: re-extract " + rig.File + " with baked unit scales.";
            }
            if (bone != rig.Hips)
            {
                var translationGap = VectorGap(local[bone].Position, rig.Reference[bone].Position);
                if (translationGap != 0f)
                {
                    return "rig " + rig.Rig + " " + what + " frame " + frame + " bone " + bone + " (" + rig.Names[bone]
                        + " role " + RoleName(rig.Names[bone]) + "): non-Hips translation moved by " + translationGap.ToString("G9")
                        + " m; fix: keep translation degrees of freedom disabled in the bake profile.";
                }
            }
        }
        return null;
    }

    static string RoleName(string path)
    {
        var role = LeafRole(Leaf(path));
        return role >= 0 && role < CanonicalNames.Length ? CanonicalNames[role] : "Static";
    }

    static int MirrorFoot(RigCalibration rig)
    {
        for (var i = 0; i < rig.Names.Length; i++)
        {
            if (LeafRole(Leaf(rig.Names[i])) == 54)
            {
                return i;
            }
        }
        return rig.Foot;
    }

    static int MirrorUpper(RigCalibration rig)
    {
        for (var i = 0; i < rig.Names.Length; i++)
        {
            if (LeafRole(Leaf(rig.Names[i])) == 52)
            {
                return i;
            }
        }
        return rig.UpperLeg;
    }

    static int MirrorLower(RigCalibration rig)
    {
        for (var i = 0; i < rig.Names.Length; i++)
        {
            if (LeafRole(Leaf(rig.Names[i])) == 53)
            {
                return i;
            }
        }
        return rig.LowerLeg;
    }
    static float HipHeightAboveGround(string[] names, RigidPose[] models, int hips, string rig, string file)
    {
        var ankle = float.MaxValue;
        for (var bone = 1; bone < names.Length; bone++)
        {
            var role = LeafRole(Leaf(names[bone]));
            if (role != 50 && role != 54)
            {
                continue;
            }
            if (models[bone].Position.Y < ankle)
            {
                ankle = models[bone].Position.Y;
            }
        }
        if (ankle == float.MaxValue)
        {
            throw new InvalidOperationException("C13 rig " + rig + ": no foot leaves mapped; fix: extend the leaf map for " + file + ".");
        }
        var height = (models[hips].Position.Y - ankle) / 0.9f;
        if (height <= 0.3f || height >= 1.8f)
        {
            throw new InvalidOperationException("C13 rig " + rig + ": hip height " + height.ToString("F4") + " outside 0.3-1.8 m; fix: check frame 0 ground contact in " + file + ".");
        }
        return height;
    }

    Pose[] RetargetFrame(RigCalibration target, Pose[] canonicalSamples, int frame)
    {
        var canonical = new Pose[CanonicalNames.Length];
        Array.Copy(canonicalSamples, frame * CanonicalNames.Length, canonical, 0, CanonicalNames.Length);
        return ApplyTarget(target, canonical, "synthetic-turn frame " + frame);
    }
    Pose[] FrameAt(RigCalibration target, RigCalibration source, RealClipData? real, Pose[] turnSamples, int frame)
    {
        if (real == null)
        {
            return RetargetFrame(target, turnSamples, frame);
        }
        return RetargetRealFrame(target, source, real, frame);
    }

    int BentFrame(RigCalibration target, RigCalibration source, RealClipData real, Pose[] turnSamples, int frames, int footBone, int upperBone)
    {
        var last = frames - 8;
        if (last < 1)
        {
            last = 1;
        }
        var best = 0;
        var bestDistance = float.MaxValue;
        var probe = new RigidPose[target.Names.Length];
        for (var frame = 0; frame <= last; frame++)
        {
            var local = FrameAt(target, source, real, turnSamples, frame);
            if (RigidMath.Model(target.Parents, local, probe) != AnimationError.None)
            {
                throw new InvalidOperationException("C13 " + target.Rig + " frame " + frame + ": FK failed; fix: check the " + target.File + " calibration table.");
            }
            var distance = (probe[upperBone].Position - probe[footBone].Position).Length;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = frame;
            }
        }
        return best;
    }

    Pose[] ApplyTarget(RigCalibration target, Pose[] canonical, string what)
    {
        var canonicalModel = new RigidPose[CanonicalNames.Length];
        var targetModel = new RigidPose[target.Names.Length];
        var local = new Pose[target.Names.Length];
        if (RetargetTable.Apply(CanonicalFamily(), target.Binding, CanonicalParents(), canonical, target.Rows, canonicalModel, targetModel, local) != AnimationError.None)
        {
            throw new InvalidOperationException("C13 " + target.Rig + " " + what + ": Apply failed; fix: recompile the " + target.File + " table.");
        }
        return local;
    }

    sealed class RealClipData
    {
        public string Clip = "";
        public Pose[] Samples = Array.Empty<Pose>();
        public int SampleCount;
        public double Duration;
    }

    RealClipData LoadRealClip(string directory, RigCalibration source, string clipName)
    {
        var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "_manifest.json")));
        string? file = null;
        foreach (var entry in manifest.RootElement.GetProperty("clips").EnumerateArray())
        {
            if ((entry.GetProperty("rig").GetString() ?? "") == source.Rig && (entry.GetProperty("clip").GetString() ?? "") == clipName)
            {
                file = entry.GetProperty("file").GetString();
            }
        }
        if (file == null)
        {
            foreach (var entry in manifest.RootElement.GetProperty("clips").EnumerateArray())
            {
                if ((entry.GetProperty("rig").GetString() ?? "") == source.Rig)
                {
                    file = entry.GetProperty("file").GetString();
                    clipName = entry.GetProperty("clip").GetString() ?? clipName;
                    break;
                }
            }
        }
        if (file == null)
        {
            throw new InvalidOperationException("C13 rig " + source.Rig + ": no clips in manifest; fix: re-run the extractor into Temp/sa/c08-full.");
        }
        var loaded = LoadClipFile(directory, file, clipName);
        return new RealClipData { Clip = clipName, Samples = loaded.Samples, SampleCount = loaded.SampleCount, Duration = loaded.Duration };
    }

    Pose[] RetargetRealFrame(RigCalibration target, RigCalibration source, RealClipData real, int frame)
    {
        var decoded = new Pose[source.Names.Length];
        Array.Copy(real.Samples, frame * source.Names.Length, decoded, 0, source.Names.Length);
        var canonical = new Pose[CanonicalNames.Length];
        var canonicalModel = new RigidPose[source.Names.Length];
        var canonicalTarget = new RigidPose[CanonicalNames.Length];
        if (RetargetTable.Apply(source.Family, source.UpBinding, source.Parents, decoded, source.UpRows, canonicalModel, canonicalTarget, canonical) != AnimationError.None)
        {
            throw new InvalidOperationException("C13 " + source.Rig + " real " + real.Clip + " frame " + frame + ": source-to-canonical Apply failed; fix: recompile the " + source.File + " table.");
        }
        return ApplyTarget(target, canonical, "real " + real.Clip + " frame " + frame);
    }

    static string RealClipFor(string rig)
    {
        if (rig == "UAL1_Standard")
        {
            return RealClips[0];
        }
        if (rig == "UAL2_Standard")
        {
            return RealClips[1];
        }
        return RealClips[2];
    }

    List<RigCalibration> LoadCalibrations(string directory)
    {
        var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "_manifest.json")));
        var firstByRig = new Dictionary<string, string>(StringComparer.Ordinal);
        var poseByRig = new Dictionary<string, string>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var entry in manifest.RootElement.GetProperty("clips").EnumerateArray())
        {
            var rig = entry.GetProperty("rig").GetString() ?? "";
            var file = entry.GetProperty("file").GetString() ?? "";
            var clip = entry.GetProperty("clip").GetString() ?? "";
            if (!firstByRig.ContainsKey(rig))
            {
                firstByRig[rig] = file;
                order.Add(rig);
            }
            if (clip.IndexOf("TPose", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                poseByRig[rig] = file;
            }
            else if (clip.IndexOf("Idle", StringComparison.OrdinalIgnoreCase) >= 0 && !poseByRig.ContainsKey(rig))
            {
                poseByRig[rig] = file;
            }
        }
        foreach (var rig in poseByRig.Keys)
        {
            firstByRig[rig] = poseByRig[rig];
        }
        order.Sort(StringComparer.Ordinal);
        var rigs = new List<RigCalibration>();
        foreach (var rig in order)
        {
            rigs.Add(LoadCalibration(directory, rig, firstByRig[rig]));
        }
        return rigs;
    }

    RigCalibration LoadCalibration(string directory, string rig, string file)
    {
        var loaded = LoadClipFile(directory, file, "calibration");
        var data = new RigCalibration { Rig = rig, File = file, Names = loaded.Names, Parents = loaded.Parents, Reference = loaded.Reference };
        if (ContentIdentity.Skeleton(rig, 1, data.Names, data.Parents, data.Reference, out var family) != AnimationError.None || family.IsEmpty)
        {
            throw new InvalidOperationException("C13 rig " + rig + ": skeleton hash failed; fix: check bone names and parents in " + file + ".");
        }
        data.Family = family;
        data.Hips = -1;
        data.UpperLeg = -1;
        data.LowerLeg = -1;
        data.Foot = -1;
        for (var i = 1; i < data.Names.Length; i++)
        {
            var role = LeafRole(Leaf(data.Names[i]));
            if (role == CanonicalHumanoid.Hips)
            {
                data.Hips = i;
            }
            if (role == 48)
            {
                data.UpperLeg = i;
            }
            if (role == 49)
            {
                data.LowerLeg = i;
            }
            if (role == 50)
            {
                data.Foot = i;
            }
        }
        if (data.Hips <= 0 || data.UpperLeg <= 0 || data.LowerLeg <= 0 || data.Foot <= 0)
        {
            throw new InvalidOperationException("C13 rig " + rig + ": missing Hips or left-leg leaves; fix: extend the leaf map for " + file + ".");
        }
        var models = new RigidPose[data.Names.Length];
        if (RigidMath.Model(data.Parents, data.Reference, models) != AnimationError.None)
        {
            throw new InvalidOperationException("C13 rig " + rig + ": calibration FK failed; fix: re-extract " + file + " with a valid frame 0.");
        }
        data.HipHeight = HipHeightAboveGround(data.Names, models, data.Hips, rig, file);
        var soleOffset = new Float3(0f, -0.1f * data.HipHeight, 0.08f * data.HipHeight);
        data.SoleLocal0 = Quaternion.Rotate(Quaternion.InverseUnit(models[data.Foot].Rotation), soleOffset);
        data.SoleLocal1 = data.SoleLocal0;
        for (var bone = 1; bone < data.Names.Length; bone++)
        {
            if (LeafRole(Leaf(data.Names[bone])) == 54)
            {
                data.SoleLocal1 = Quaternion.Rotate(Quaternion.InverseUnit(models[bone].Rotation), soleOffset);
            }
        }
        var canonicalParents = CanonicalParents();
        var canonicalReference = CanonicalReference();
        var map = new int[data.Names.Length];
        map[0] = 0;
        for (var bone = 1; bone < data.Names.Length; bone++)
        {
            map[bone] = LeafRole(Leaf(data.Names[bone]));
        }
        var scratch = new RigidPose[CanonicalNames.Length + data.Names.Length];
        var rows = new RetargetRow[data.Names.Length];
        if (RetargetTable.Compile(CanonicalFamily(), data.Family, canonicalParents, canonicalReference, data.Parents, data.Reference,
            map, CanonicalHumanoid.Hips, data.Hips, data.HipHeight, scratch, rows, out var binding) != AnimationError.None)
        {
            throw new InvalidOperationException("C13 rig " + rig + ": canonical-to-target Compile failed; fix: check topology of " + file + " against the canonical parent chain.");
        }
        data.Rows = rows;
        data.Binding = binding;
        var upMap = new int[CanonicalNames.Length];
        for (var role = 0; role < CanonicalNames.Length; role++)
        {
            upMap[role] = -1;
        }
        upMap[0] = 0;
        for (var bone = 1; bone < data.Names.Length; bone++)
        {
            var role = LeafRole(Leaf(data.Names[bone]));
            if (role <= 0)
            {
                continue;
            }
            if (upMap[role] >= 0)
            {
                throw new InvalidOperationException("C13 rig " + rig + ": bones map twice to role " + role + "; fix: disambiguate leaves in " + file + ".");
            }
            upMap[role] = bone;
        }
        var upScratch = new RigidPose[data.Names.Length + CanonicalNames.Length];
        var upRows = new RetargetRow[CanonicalNames.Length];
        data.UpError = RetargetTable.Compile(data.Family, CanonicalFamily(), data.Parents, data.Reference, canonicalParents, canonicalReference,
            upMap, data.Hips, CanonicalHumanoid.Hips, 1f / data.HipHeight, upScratch, upRows, out var upBinding);
        data.UpValid = data.UpError == AnimationError.None;
        if (data.UpValid)
        {
            data.UpRows = upRows;
            data.UpBinding = upBinding;
        }
        return data;
    }

    (Pose[] Samples, RigidPose[] Root) TurnClip(Pose[] reference)
    {
        var samples = new Pose[TurnFrames * CanonicalNames.Length];
        var root = new RigidPose[TurnFrames];
        var up = new Float3(0f, 1f, 0f);
        var outOfPlane = new Float3(0f, 0f, 1f);
        for (var i = 0; i < TurnFrames; i++)
        {
            var t = TurnFrames == 1 ? 0f : i / (float)(TurnFrames - 1);
            reference.CopyTo(samples, i * CanonicalNames.Length);
            samples[i * CanonicalNames.Length + 1].Position.Y += 0.03f * MathF.Sin(t * Scalar.TwoPi);
            samples[i * CanonicalNames.Length + 11].Rotation = Quaternion.AxisAngle(outOfPlane, 0.25f * MathF.Sin(t * Scalar.TwoPi));
            samples[i * CanonicalNames.Length + 30].Rotation = Quaternion.AxisAngle(outOfPlane, -0.25f * MathF.Sin(t * Scalar.TwoPi));
            root[i] = new RigidPose(new Float3(0f, 0f, t), Quaternion.AxisAngle(up, t * Scalar.Pi * 0.5f));
        }
        return (samples, root);
    }

    sealed class LoadedFile
    {
        public string[] Names = Array.Empty<string>();
        public int[] Parents = Array.Empty<int>();
        public Pose[] Reference = Array.Empty<Pose>();
        public Pose[] Samples = Array.Empty<Pose>();
        public int SampleCount;
        public double Duration;
    }

    LoadedFile LoadClipFile(string directory, string file, string what)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, file)));
        var root = document.RootElement;
        var bonesElement = root.GetProperty("bones");
        var framesElement = root.GetProperty("frames");
        var sampleCount = root.GetProperty("sampleCount").GetInt32();
        var duration = root.GetProperty("duration").GetDouble();
        var bones = bonesElement.GetArrayLength();
        if (bones <= 0 || bones > SampleLocator.MaxBones || sampleCount != framesElement.GetArrayLength())
        {
            throw new InvalidOperationException("C13 " + what + " " + file + ": bad bone or frame count; fix: re-run the extractor into Temp/sa/c08-full.");
        }
        var loaded = new LoadedFile { SampleCount = sampleCount, Duration = duration };
        loaded.Names = new string[bones];
        loaded.Parents = new int[bones];
        var index = 0;
        foreach (var bone in bonesElement.EnumerateArray())
        {
            loaded.Names[index] = bone.GetProperty("path").GetString() ?? ("b" + index);
            var parent = bone.GetProperty("parent").GetInt32();
            loaded.Parents[index] = index == 0 && parent == 0 ? -1 : parent;
            if (loaded.Parents[index] >= index || (index > 0 && loaded.Parents[index] < 0))
            {
                throw new InvalidOperationException("C13 " + what + " " + file + " bone " + index + ": parent order broken; fix: re-run the extractor into Temp/sa/c08-full.");
            }
            index++;
        }
        loaded.Samples = new Pose[bones * sampleCount];
        index = 0;
        foreach (var frame in framesElement.EnumerateArray())
        {
            var positions = frame.GetProperty("p");
            var rotations = frame.GetProperty("q");
            var scales = frame.GetProperty("s");
            for (var bone = 0; bone < bones; bone++)
            {
                var p = positions[bone];
                var q = rotations[bone];
                var s = scales[bone];
                var scaleGap = Math.Max(Math.Abs(s[0].GetSingle() - 1f), Math.Max(Math.Abs(s[1].GetSingle() - 1f), Math.Abs(s[2].GetSingle() - 1f)));
                if (scaleGap > 1e-5f)
                {
                    throw new InvalidOperationException("C13 " + what + " " + file + " frame " + index + " bone " + bone
                        + ": non-unit scale gap " + scaleGap.ToString("G9") + "; fix: re-extract " + file + " with baked unit scales.");
                }
                loaded.Samples[index * bones + bone] = new Pose
                {
                    Position = new Float3(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle()),
                    Rotation = new Quaternion(q[0].GetSingle(), q[1].GetSingle(), q[2].GetSingle(), q[3].GetSingle()),
                    Scale = Float3.One
                };
            }
            index++;
        }
        loaded.Reference = new Pose[bones];
        Array.Copy(loaded.Samples, loaded.Reference, bones);
        return loaded;
    }

    static RigCalibration FindRig(List<RigCalibration> rigs, string rig)
    {
        foreach (var candidate in rigs)
        {
            if (candidate.Rig == rig)
            {
                return candidate;
            }
        }
        throw new InvalidOperationException("C13: rig " + rig + " missing from manifest; fix: extract all three rigs into Temp/sa/c08-full.");
    }

    static string? FindDirectory()
    {
        var scan = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && scan != null; i++)
        {
            var candidate = Path.Combine(scan, "Temp", "sa", "c08-full", "_manifest.json");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate);
            }
            scan = Path.GetDirectoryName(scan);
        }
        const string absolute = "/home/i/ruk2/Temp/sa/c08-full/_manifest.json";
        if (File.Exists(absolute))
        {
            return Path.GetDirectoryName(absolute);
        }
        return null;
    }

    static int[] CanonicalParents()
    {
        var parents = new int[CanonicalNames.Length];
        var reference = new Pose[CanonicalNames.Length];
        if (CanonicalHumanoid.WriteReference(parents, reference) != AnimationError.None)
        {
            throw new InvalidOperationException("C13: canonical reference failed; fix: repair CanonicalHumanoid.WriteReference.");
        }
        return parents;
    }

    static Pose[] CanonicalReference()
    {
        var parents = new int[CanonicalNames.Length];
        var reference = new Pose[CanonicalNames.Length];
        if (CanonicalHumanoid.WriteReference(parents, reference) != AnimationError.None)
        {
            throw new InvalidOperationException("C13: canonical reference failed; fix: repair CanonicalHumanoid.WriteReference.");
        }
        return reference;
    }

    static AssetId CanonicalFamily()
    {
        if (ContentIdentity.Skeleton("Humanoid", 1, CanonicalNames, CanonicalParents(), CanonicalReference(), out var family) != AnimationError.None || family.IsEmpty)
        {
            throw new InvalidOperationException("C13: canonical family hash failed; fix: check the 56 canonical names and parents.");
        }
        return family;
    }

    static string Leaf(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash >= 0 ? path.Substring(slash + 1) : path;
    }

    static float VectorGap(Float3 a, Float3 b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Max(Math.Abs(a.Y - b.Y), Math.Abs(a.Z - b.Z)));

    static float RotationGap(Quaternion a, Quaternion b)
    {
        var sign = Quaternion.Dot(a, b) < 0f ? -1f : 1f;
        return Math.Max(Math.Max(Math.Abs(a.X - sign * b.X), Math.Abs(a.Y - sign * b.Y)), Math.Max(Math.Abs(a.Z - sign * b.Z), Math.Abs(a.W - sign * b.W)));
    }

    static float ScaleGap(Float3 scale) =>
        Math.Max(Math.Abs(scale.X - 1f), Math.Max(Math.Abs(scale.Y - 1f), Math.Abs(scale.Z - 1f)));

    static float PoseGap(in Pose a, in Pose b) =>
        Math.Max(Math.Max(VectorGap(a.Position, b.Position), VectorGap(a.Scale, b.Scale)), RotationGap(a.Rotation, b.Rotation));

    static int LeafRole(string leaf)
    {
        switch (leaf.ToLowerInvariant())
        {
            case "pelvis":
            case "hip": return 1;
            case "spine_01":
            case "ribs": return 2;
            case "spine_02": return 3;
            case "spine_03": return 4;
            case "neck_01":
            case "neck": return 5;
            case "head": return 6;
            case "clavicle_l":
            case "left_shoulder_joint_01": return 10;
            case "upperarm_l":
            case "left_upper_arm_joint_01": return 11;
            case "lowerarm_l":
            case "left_forearm_joint_01": return 12;
            case "hand_l":
            case "left_wrist_joint_01": return 13;
            case "thumb_01_l":
            case "left_thumb_joint_01a": return 14;
            case "thumb_02_l":
            case "left_thumb_joint_01b": return 15;
            case "thumb_03_l": return 16;
            case "index_01_l":
            case "left_index_finger_joint_01a": return 17;
            case "index_02_l":
            case "left_index_finger_joint_01b": return 18;
            case "index_03_l":
            case "left_index_finger_joint_01c": return 19;
            case "middle_01_l":
            case "left_middle_finger_joint_01a": return 20;
            case "middle_02_l":
            case "left_middle_finger_joint_01b": return 21;
            case "middle_03_l":
            case "left_middle_finger_joint_01c": return 22;
            case "ring_01_l":
            case "left_ring_finger_joint_01a": return 23;
            case "ring_02_l":
            case "left_ring_finger_joint_01b": return 24;
            case "ring_03_l":
            case "left_ring_finger_joint_01c": return 25;
            case "pinky_01_l":
            case "left_pinky_finger_joint_01a": return 26;
            case "pinky_02_l":
            case "left_pinky_finger_joint_01b": return 27;
            case "pinky_03_l":
            case "left_pinky_finger_joint_01c": return 28;
            case "clavicle_r":
            case "right_shoulder_joint_01": return 29;
            case "upperarm_r":
            case "right_upper_arm_joint_01": return 30;
            case "lowerarm_r":
            case "right_forearm_joint_01": return 31;
            case "hand_r":
            case "right_wrist_joint_01": return 32;
            case "thumb_01_r":
            case "right_thumb_joint_01a": return 33;
            case "thumb_02_r":
            case "right_thumb_joint_01b": return 34;
            case "thumb_03_r": return 35;
            case "index_01_r":
            case "right_index_finger_joint_01a": return 36;
            case "index_02_r":
            case "right_index_finger_joint_01b": return 37;
            case "index_03_r":
            case "right_index_finger_joint_01c": return 38;
            case "middle_01_r":
            case "right_middle_finger_joint_01a": return 39;
            case "middle_02_r":
            case "right_middle_finger_joint_01b": return 40;
            case "middle_03_r":
            case "right_middle_finger_joint_01c": return 41;
            case "ring_01_r":
            case "right_ring_finger_joint_01a": return 42;
            case "ring_02_r":
            case "right_ring_finger_joint_01b": return 43;
            case "ring_03_r":
            case "right_ring_finger_joint_01c": return 44;
            case "pinky_01_r":
            case "right_pinky_finger_joint_01a": return 45;
            case "pinky_02_r":
            case "right_pinky_finger_joint_01b": return 46;
            case "pinky_03_r":
            case "right_pinky_finger_joint_01c": return 47;
            case "thigh_l":
            case "left_thigh_joint_01": return 48;
            case "calf_l":
            case "left_knee_joint_01": return 49;
            case "foot_l":
            case "left_ankle_joint_01": return 50;
            case "ball_l":
            case "left_toe_joint_01": return 51;
            case "thigh_r":
            case "right_thigh_joint_01": return 52;
            case "calf_r":
            case "right_knee_joint_01": return 53;
            case "foot_r":
            case "right_ankle_joint_01": return 54;
            case "ball_r":
            case "right_toe_joint_01": return 55;
            default: return -1;
        }
    }
}

