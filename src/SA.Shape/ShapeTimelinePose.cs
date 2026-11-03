using System;
using System.Runtime.InteropServices;
using SA.Animation;
using SA.Motion.Inertialization;
using SA.Motion.Numerics;

namespace SA.Shape
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ShapeFrameState
    {
        public PoseFrameState Motion;
        public AssetId Definition;
        public AssetId Target;
        public ulong Revision;
        public ulong GeometryStamp;
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly ref struct ShapePoseWorkspace
    {
        public readonly Span<Pose> Destination;
        public readonly Span<Pose> NeutralCommitted;
        public readonly Span<Pose> NeutralScratch;
        public readonly Span<Pose> Displayed;
        public readonly Span<Pose> DisplayScratch;
        public readonly Span<RigidPose> Models;
        public ShapePoseWorkspace(Span<Pose> destination, Span<Pose> neutralCommitted, Span<Pose> neutralScratch,
            Span<Pose> displayed, Span<Pose> displayScratch, Span<RigidPose> models)
        { Destination = destination; NeutralCommitted = neutralCommitted; NeutralScratch = neutralScratch;
            Displayed = displayed; DisplayScratch = displayScratch; Models = models; }
    }

    public static class ShapeTimelinePose
    {
        public static AnimationError Evaluate(in AnimationFrame frame, ReadOnlySpan<ClipContribution> contributions, ReadOnlySpan<LayerInput> layers,
            bool dataReady, ReadOnlySpan<Pose> targetDestination, ReadOnlySpan<PoseVelocity> incomingNeutralVelocity,
            in ShapeGeometry geometry, in RetargetBinding binding, ReadOnlySpan<RetargetRow> rows, ReadOnlySpan<Float3> offsets, ReadOnlySpan<float> motionScales,
            ref ShapeFrameState state, Span<BoneFrameState> bones, Span<BoneFrameState> boneScratch,
            Span<ClipContribution> previousContributions, Span<LayerInput> previousLayers, ShapePoseWorkspace workspace, out FrameAction action)
        {
            var error = Prepare(frame, contributions, layers, dataReady, targetDestination, incomingNeutralVelocity, geometry, binding, rows, offsets, motionScales,
                state, bones, boneScratch, previousContributions, previousLayers, workspace, out var prepared, out action);
            if (error == AnimationError.NeedsData) state.Motion = prepared.Motion;
            if (error != AnimationError.None || action == FrameAction.Reused) return error;
            return Commit(prepared, contributions, layers, boneScratch, workspace, ref state, bones, previousContributions, previousLayers);
        }

        public static AnimationError Prepare(in AnimationFrame frame, ReadOnlySpan<ClipContribution> contributions, ReadOnlySpan<LayerInput> layers,
            bool dataReady, ReadOnlySpan<Pose> targetDestination, ReadOnlySpan<PoseVelocity> incomingNeutralVelocity,
            in ShapeGeometry geometry, in RetargetBinding binding, ReadOnlySpan<RetargetRow> rows, ReadOnlySpan<Float3> offsets, ReadOnlySpan<float> motionScales,
            in ShapeFrameState state, ReadOnlySpan<BoneFrameState> bones, Span<BoneFrameState> boneScratch,
            ReadOnlySpan<ClipContribution> previousContributions, ReadOnlySpan<LayerInput> previousLayers, ShapePoseWorkspace workspace,
            out ShapeFrameState prepared, out FrameAction action)
        {
            prepared = state;
            action = FrameAction.Waiting;
            var count = rows.Length;
            var error = ShapeMotionSpace.ValidateGeometry(geometry, binding, rows, offsets, motionScales);
            if (error != AnimationError.None) return error;
            if (workspace.Destination.Length < count || workspace.NeutralCommitted.Length < count || workspace.NeutralScratch.Length < count
                || workspace.Displayed.Length < count || workspace.DisplayScratch.Length < count || workspace.Models.Length < count)
                return AnimationError.CapacityExceeded;
            if (Aliased(workspace, targetDestination)) return AnimationError.AliasedBuffers;
            var active = state.Motion.Initialized != 0 && state.Motion.LastFrame.RigEpoch == frame.RigEpoch;
            var seek = frame.Mode == FrameMode.Seek || frame.Mode == FrameMode.Teleport;
            if (active && !seek && (state.Definition != geometry.Definition || state.Target != geometry.Retarget.Target))
                return AnimationError.InvalidTable;
            var stamp = Stamp(geometry, offsets, motionScales);
            if (active && !seek && (frame.Tick == state.Motion.LastFrame.Tick || frame.Mode == FrameMode.Pause || frame.DeltaSeconds == 0f)
                && (state.Revision != geometry.Revision || state.GeometryStamp != stamp))
                return AnimationError.TickConflict;
            if (dataReady)
            {
                error = ShapeMotionSpace.Normalize(geometry, binding, rows, offsets, motionScales, targetDestination, workspace.Models, workspace.Destination);
                if (error != AnimationError.None) return error;
            }
            error = TimelinePose.Prepare(frame, contributions, layers, dataReady, dataReady ? workspace.Destination.Slice(0, count) : ReadOnlySpan<Pose>.Empty,
                incomingNeutralVelocity, state.Motion, bones, boneScratch, previousContributions, previousLayers, workspace.NeutralCommitted,
                workspace.NeutralScratch, out var motion, out action);
            if (error == AnimationError.NeedsData) prepared.Motion = motion;
            if (error != AnimationError.None || action == FrameAction.Reused) return error;
            error = ShapeMotionSpace.Compose(geometry, binding, rows, offsets, motionScales, workspace.NeutralScratch.Slice(0, count), workspace.Models, workspace.DisplayScratch);
            if (error != AnimationError.None) return error;
            prepared = new ShapeFrameState { Motion = motion, Definition = geometry.Definition, Target = geometry.Retarget.Target,
                Revision = geometry.Revision, GeometryStamp = stamp };
            return AnimationError.None;
        }

        public static AnimationError Commit(in ShapeFrameState prepared, ReadOnlySpan<ClipContribution> contributions, ReadOnlySpan<LayerInput> layers,
            ReadOnlySpan<BoneFrameState> boneScratch, ShapePoseWorkspace workspace, ref ShapeFrameState state, Span<BoneFrameState> bones,
            Span<ClipContribution> previousContributions, Span<LayerInput> previousLayers)
        {
            var count = prepared.Motion.BoneCount;
            if (prepared.Definition.IsEmpty || prepared.Target.IsEmpty) return AnimationError.InvalidTable;
            if (count <= 0 || workspace.DisplayScratch.Length < count || workspace.Displayed.Length < count) return AnimationError.CapacityExceeded;
            if (Aliased(workspace, ReadOnlySpan<Pose>.Empty)) return AnimationError.AliasedBuffers;
            for (var i = 0; i < count; i++) if (!RigidMath.UnitPose(workspace.DisplayScratch[i])) return AnimationError.InvalidFrame;
            var motion = state.Motion;
            var error = TimelinePose.Commit(prepared.Motion, contributions, layers, boneScratch, workspace.NeutralScratch, ref motion, bones,
                previousContributions, previousLayers, workspace.NeutralCommitted);
            if (error != AnimationError.None) return error;
            workspace.DisplayScratch.Slice(0, count).CopyTo(workspace.Displayed);
            state = prepared;
            return AnimationError.None;
        }

        static bool Aliased(ShapePoseWorkspace w, ReadOnlySpan<Pose> input) => w.Destination.Overlaps(w.NeutralCommitted) || w.Destination.Overlaps(w.NeutralScratch)
            || w.Destination.Overlaps(w.Displayed) || w.Destination.Overlaps(w.DisplayScratch) || w.NeutralCommitted.Overlaps(w.NeutralScratch)
            || w.NeutralCommitted.Overlaps(w.Displayed) || w.NeutralCommitted.Overlaps(w.DisplayScratch) || w.NeutralScratch.Overlaps(w.Displayed)
            || w.NeutralScratch.Overlaps(w.DisplayScratch) || w.Displayed.Overlaps(w.DisplayScratch) || input.Overlaps(w.Destination)
            || input.Overlaps(w.NeutralCommitted) || input.Overlaps(w.NeutralScratch) || input.Overlaps(w.Displayed) || input.Overlaps(w.DisplayScratch);

        static ulong Stamp(in ShapeGeometry geometry, ReadOnlySpan<Float3> offsets, ReadOnlySpan<float> scales)
        {
            var hash = 14695981039346656037UL;
            hash = Mix(hash, geometry.Retarget.HipReference.X);
            hash = Mix(hash, geometry.Retarget.HipReference.Y);
            hash = Mix(hash, geometry.Retarget.HipReference.Z);
            hash = Mix(hash, geometry.Retarget.HeightRatio);
            for (var i = 0; i < offsets.Length; i++)
            {
                hash = Mix(hash, offsets[i].X); hash = Mix(hash, offsets[i].Y); hash = Mix(hash, offsets[i].Z); hash = Mix(hash, scales[i]);
            }
            return hash;
        }

        static ulong Mix(ulong hash, float value) => unchecked((hash ^ (uint)BitConverter.SingleToInt32Bits(value)) * 1099511628211UL);
    }
}
