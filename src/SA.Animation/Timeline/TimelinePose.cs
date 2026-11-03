using System;
using System.Runtime.InteropServices;
using SA.Motion.Inertialization;
using SA.Motion.Numerics;

namespace SA.Animation
{
    [StructLayout(LayoutKind.Sequential)]
    public struct BoneFrameState
    {
        public PoseHistory History;
        public PoseDecay Decay;
        public Pose Destination;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PoseFrameState
    {
        public AnimationFrame LastFrame;
        public int ContributionCount;
        public int LayerCount;
        public int BoneCount;
        public float Elapsed;
        public float Duration;
        public byte Initialized;
        public byte Waiting;
    }

    public static class TimelinePose
    {
        public static AnimationError Evaluate(in AnimationFrame frame, ReadOnlySpan<ClipContribution> contributions, ReadOnlySpan<LayerInput> layers,
            bool dataReady, ReadOnlySpan<Pose> destination, ReadOnlySpan<PoseVelocity> incomingVelocity,
            ref PoseFrameState state, Span<BoneFrameState> bones, Span<BoneFrameState> boneScratch,
            Span<ClipContribution> previousContributions, Span<LayerInput> previousLayers, Span<Pose> committedPose, Span<Pose> poseScratch, out FrameAction action)
        {
            var error = Prepare(frame, contributions, layers, dataReady, destination, incomingVelocity, state, bones, boneScratch,
                previousContributions, previousLayers, committedPose, poseScratch, out var prepared, out action);
            if (error == AnimationError.NeedsData) state = prepared;
            if (error != AnimationError.None || action == FrameAction.Reused) return error;
            return Commit(prepared, contributions, layers, boneScratch, poseScratch, ref state, bones, previousContributions, previousLayers, committedPose);
        }

        public static AnimationError Prepare(in AnimationFrame frame, ReadOnlySpan<ClipContribution> contributions, ReadOnlySpan<LayerInput> layers,
            bool dataReady, ReadOnlySpan<Pose> destination, ReadOnlySpan<PoseVelocity> incomingVelocity,
            in PoseFrameState state, ReadOnlySpan<BoneFrameState> bones, Span<BoneFrameState> boneScratch,
            ReadOnlySpan<ClipContribution> previousContributions, ReadOnlySpan<LayerInput> previousLayers, ReadOnlySpan<Pose> committedPose,
            Span<Pose> poseScratch, out PoseFrameState prepared, out FrameAction action)
        {
            prepared = state;
            action = FrameAction.Waiting;
            if (!Valid(frame)) return AnimationError.InvalidFrame;
            if (contributions.Length > previousContributions.Length || layers.Length > previousLayers.Length)
                return AnimationError.CapacityExceeded;
            var error = TimelineInput.Validate(contributions, layers);
            if (error != AnimationError.None) return error;
            if (bones.Overlaps(boneScratch) || committedPose.Overlaps(poseScratch) || destination.Overlaps(committedPose) || destination.Overlaps(poseScratch)
                || contributions.Overlaps(previousContributions) || layers.Overlaps(previousLayers))
                return AnimationError.AliasedBuffers;
            var active = state.Initialized != 0 && state.LastFrame.RigEpoch == frame.RigEpoch;
            if (state.ContributionCount < 0 || state.ContributionCount > previousContributions.Length || state.LayerCount < 0 || state.LayerCount > previousLayers.Length
                || (active && (state.BoneCount <= 0 || state.BoneCount > bones.Length || state.BoneCount > committedPose.Length)))
                return AnimationError.InvalidFrame;
            var seek = frame.Mode == FrameMode.Seek || frame.Mode == FrameMode.Teleport;
            if (active && frame.Tick == state.LastFrame.Tick && !seek)
            {
                if (!frame.SameInput(state.LastFrame) || !Same(contributions, layers, previousContributions, previousLayers, state, true))
                    return AnimationError.TickConflict;
                if (dataReady)
                {
                    if (destination.Length != state.BoneCount) return AnimationError.TickConflict;
                    for (var i = 0; i < destination.Length; i++) if (!Equal(destination[i], bones[i].Destination)) return AnimationError.TickConflict;
                }
                action = FrameAction.Reused;
                return AnimationError.None;
            }
            if (active && !seek && (frame.Tick < state.LastFrame.Tick || frame.SimulationSeconds < state.LastFrame.SimulationSeconds))
                return AnimationError.TickConflict;
            if (active && !seek && frame.Continuity == state.LastFrame.Continuity && !Same(contributions, layers, previousContributions, previousLayers, state, false))
                return AnimationError.ContinuityMisuse;
            if (active && (frame.Mode == FrameMode.Pause || frame.DeltaSeconds == 0 && !seek))
            {
                action = FrameAction.Reused;
                return AnimationError.None;
            }
            if (!dataReady)
            {
                prepared.Waiting = 1;
                return AnimationError.NeedsData;
            }
            var count = destination.Length;
            if (count <= 0 || count > SampleLocator.MaxBones || bones.Length < count || boneScratch.Length < count || committedPose.Length < count || poseScratch.Length < count)
                return AnimationError.CapacityExceeded;
            if (active && state.BoneCount != count)
                return AnimationError.InvalidTable;
            for (var i = 0; i < count; i++) if (!RigidMath.UnitPose(destination[i])) return AnimationError.InvalidFrame;
            var reset = !active || seek;
            var gap = active ? frame.SimulationSeconds - state.LastFrame.SimulationSeconds : 0;
            var reseed = state.Waiting != 0 || gap > frame.MaxHistoryGapSeconds;
            var begin = active && !seek && (frame.Continuity != state.LastFrame.Continuity || reseed);
            var duration = begin ? frame.TransitionSeconds : state.Duration;
            if (!Scalar.IsFinite(duration) || duration < 0 || !Scalar.IsFinite(state.Elapsed) || state.Elapsed < 0)
                return AnimationError.InvalidFrame;
            var elapsed = reset || begin ? 0 : (float)Math.Min(duration, (double)state.Elapsed + frame.DeltaSeconds);
            if (begin && duration > Scalar.DurationEpsilon)
            {
                if (incomingVelocity.Length != count) return AnimationError.InvalidFrame;
                for (var i = 0; i < count; i++) if (!incomingVelocity[i].IsFinite) return AnimationError.InvalidFrame;
            }
            for (var i = 0; i < count; i++)
            {
                var bone = reset ? default : bones[i];
                Pose output;
                if (reset)
                {
                    output = destination[i];
                    bone.History.Reset(output);
                    bone.Decay = default;
                }
                else
                {
                    if (begin)
                    {
                        var velocity = default(PoseVelocity);
                        if (!reseed && bone.History.Count > 1 && bone.History.Interval <= frame.MaxHistoryGapSeconds)
                            PoseVelocity.TryBetween(bone.History.Previous, bone.History.Current, bone.History.Interval, out velocity);
                        if (duration > Scalar.DurationEpsilon && !PoseDecay.TryCreate(bone.History.Current, velocity, destination[i], incomingVelocity[i], out bone.Decay))
                            return AnimationError.InvalidFrame;
                        if (duration <= Scalar.DurationEpsilon) bone.Decay = default;
                    }
                    if (!bone.Decay.TryApply(destination[i], elapsed, duration, out output) || !RigidMath.UnitPose(output))
                        return AnimationError.InvalidFrame;
                    if (begin)
                    {
                        if (reseed || duration <= Scalar.DurationEpsilon) bone.History.Reset(output);
                        else bone.History.Current = output;
                    }
                    else
                        bone.History.Capture(output, (float)gap);
                }
                bone.Destination = destination[i];
                boneScratch[i] = bone;
                poseScratch[i] = output;
            }
            prepared = new PoseFrameState { LastFrame = frame, BoneCount = count, ContributionCount = contributions.Length, LayerCount = layers.Length,
                Duration = reset ? 0 : duration, Elapsed = elapsed, Initialized = 1, Waiting = 0 };
            action = reset ? (seek ? FrameAction.Sought : FrameAction.Initialized) : begin ? FrameAction.Transitioned : FrameAction.Advanced;
            return AnimationError.None;
        }

        public static AnimationError Commit(in PoseFrameState prepared, ReadOnlySpan<ClipContribution> contributions, ReadOnlySpan<LayerInput> layers,
            ReadOnlySpan<BoneFrameState> boneScratch, ReadOnlySpan<Pose> poseScratch, ref PoseFrameState state, Span<BoneFrameState> bones,
            Span<ClipContribution> previousContributions, Span<LayerInput> previousLayers, Span<Pose> committedPose)
        {
            var count = prepared.BoneCount;
            if (prepared.Initialized == 0 || count <= 0 || count > SampleLocator.MaxBones || count > bones.Length || count > boneScratch.Length
                || count > committedPose.Length || count > poseScratch.Length || contributions.Length != prepared.ContributionCount || layers.Length != prepared.LayerCount
                || contributions.Length > previousContributions.Length || layers.Length > previousLayers.Length)
                return AnimationError.CapacityExceeded;
            if (boneScratch.Overlaps(bones) || poseScratch.Overlaps(committedPose) || contributions.Overlaps(previousContributions) || layers.Overlaps(previousLayers))
                return AnimationError.AliasedBuffers;
            if (prepared.Initialized != 1 || prepared.Waiting != 0 || !Valid(prepared.LastFrame) || !Scalar.IsFinite(prepared.Duration) || prepared.Duration < 0f
                || !Scalar.IsFinite(prepared.Elapsed) || prepared.Elapsed < 0f || prepared.Elapsed > prepared.Duration)
                return AnimationError.InvalidFrame;
            var error = TimelineInput.Validate(contributions, layers);
            if (error != AnimationError.None) return error;
            for (var i = 0; i < count; i++)
                if (!RigidMath.UnitPose(poseScratch[i]) || !RigidMath.UnitPose(boneScratch[i].History.Current) || !RigidMath.UnitPose(boneScratch[i].History.Previous)
                    || !RigidMath.UnitPose(boneScratch[i].Destination) || boneScratch[i].History.Count < 1 || boneScratch[i].History.Count > 2
                    || !Scalar.IsFinite(boneScratch[i].History.Interval) || boneScratch[i].History.Interval < 0f
                    || !boneScratch[i].Decay.PositionOffset.IsFinite || !boneScratch[i].Decay.PositionVelocity.IsFinite
                    || !boneScratch[i].Decay.RotationOffset.IsFinite || !boneScratch[i].Decay.RotationVelocity.IsFinite)
                    return AnimationError.InvalidFrame;
            boneScratch.Slice(0, count).CopyTo(bones);
            poseScratch.Slice(0, count).CopyTo(committedPose);
            contributions.CopyTo(previousContributions);
            layers.CopyTo(previousLayers);
            state = prepared;
            return AnimationError.None;
        }

        static bool Same(ReadOnlySpan<ClipContribution> contributions, ReadOnlySpan<LayerInput> layers, ReadOnlySpan<ClipContribution> previousContributions,
            ReadOnlySpan<LayerInput> previousLayers, in PoseFrameState state, bool exact)
        {
            if (contributions.Length != state.ContributionCount || layers.Length != state.LayerCount) return false;
            for (var i = 0; i < contributions.Length; i++)
                if (exact ? !contributions[i].SameInput(previousContributions[i]) : !contributions[i].SameMember(previousContributions[i])) return false;
            for (var i = 0; i < layers.Length; i++)
                if (exact ? !layers[i].SameInput(previousLayers[i]) : !layers[i].SameMember(previousLayers[i])) return false;
            return true;
        }

        static bool Equal(in Pose a, in Pose b) => a.Position.X == b.Position.X && a.Position.Y == b.Position.Y && a.Position.Z == b.Position.Z
            && a.Rotation.X == b.Rotation.X && a.Rotation.Y == b.Rotation.Y && a.Rotation.Z == b.Rotation.Z && a.Rotation.W == b.Rotation.W
            && a.Scale.X == b.Scale.X && a.Scale.Y == b.Scale.Y && a.Scale.Z == b.Scale.Z;

        static bool Valid(in AnimationFrame frame) => frame.Mode <= FrameMode.Pause && !double.IsNaN(frame.SimulationSeconds) && !double.IsInfinity(frame.SimulationSeconds)
            && Scalar.IsFinite(frame.DeltaSeconds) && frame.DeltaSeconds >= 0f && Scalar.IsFinite(frame.TransitionSeconds) && frame.TransitionSeconds >= 0f
            && Scalar.IsFinite(frame.MaxHistoryGapSeconds) && frame.MaxHistoryGapSeconds > 0f;
    }
}
