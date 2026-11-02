using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.Animation.Content
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct CompressionLimits
    {
        public readonly float AngleRadians;
        public readonly float PositionMetres;
        public CompressionLimits(float angleRadians, float positionMetres) { AngleRadians = angleRadians; PositionMetres = positionMetres; }
        public static CompressionLimits Default => new CompressionLimits(.15f * Scalar.Pi / 180f, .002f);
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct CompressionReport
    {
        public readonly int EncodedBytes;
        public readonly int Segments;
        public readonly int RawFallbackSegments;
        public readonly int DefaultTracks;
        public readonly int ConstantTracks;
        public readonly PoseError Error;
        public CompressionReport(int encodedBytes, int segments, int rawFallbackSegments, int defaultTracks, int constantTracks, PoseError error)
        { EncodedBytes = encodedBytes; Segments = segments; RawFallbackSegments = rawFallbackSegments; DefaultTracks = defaultTracks; ConstantTracks = constantTracks; Error = error; }
    }

    public static class SharedClipEncoder
    {
        public static AnimationError Encode(AssetId family, ReadOnlySpan<int> parents, ReadOnlySpan<Pose> familyReference, ReadOnlySpan<Pose> samples,
            int sampleCount, double duration, bool loop, RootPolicy rootPolicy, ReadOnlySpan<RigidPose> root, ReadOnlySpan<Pose> additiveReference,
            ReadOnlySpan<float> contactWeights, ReadOnlySpan<PoseMarker> markers, bool packed, in CompressionLimits limits, out byte[] bytes, out CompressionReport report)
        {
            bytes = Array.Empty<byte>();
            report = default;
            var bones = parents.Length;
            if (family.IsEmpty || bones <= 0 || bones > SampleLocator.MaxBones || familyReference.Length != bones || samples.Length != (long)bones * sampleCount
                || root.Length != sampleCount || rootPolicy > RootPolicy.FullRigid || (additiveReference.Length != 0 && additiveReference.Length != bones)
                || (contactWeights.Length != 0 && contactWeights.Length != (long)sampleCount * 4) || !Scalar.IsFinite(limits.AngleRadians) || limits.AngleRadians < 0f
                || !Scalar.IsFinite(limits.PositionMetres) || limits.PositionMetres < 0f || SampleLocator.Locate(duration, sampleCount, 0, false, out _) != AnimationError.None)
                return AnimationError.InvalidFrame;
            var segmentCount = SampleLocator.SegmentCount(sampleCount);
            var rawBound = (long)sampleCount * (bones * 28L + 28) + (long)segmentCount * (bones * 64L + 64) + bones * 128L + contactWeights.Length * 4L + 1024;
            if (rawBound > SacaContainer.MaxFileBytes)
                return AnimationError.CapacityExceeded;
            var models = new RigidPose[bones * 2];
            if (parents[0] != -1 || RigidMath.Model(parents, familyReference, models) != AnimationError.None || !SharedClipReader.RootIdentity(new RigidPose(familyReference[0].Position, familyReference[0].Rotation)))
                return AnimationError.InvalidTable;
            for (var frame = 0; frame < sampleCount; frame++)
            {
                if (!SharedClipReader.ValidRoot(root[frame], rootPolicy) || (frame == 0 && !SharedClipReader.RootIdentity(root[frame])))
                    return AnimationError.InvalidFrame;
                for (var bone = 0; bone < bones; bone++)
                {
                    var pose = samples[frame * bones + bone];
                    if (!RigidMath.UnitPose(pose) || (bone == 0 && !SharedClipReader.RootIdentity(new RigidPose(pose.Position, pose.Rotation))))
                        return AnimationError.InvalidFrame;
                }
            }
            for (var i = 0; i < additiveReference.Length; i++)
                if (!RigidMath.UnitPose(additiveReference[i]) || (i == 0 && !SharedClipReader.RootIdentity(new RigidPose(additiveReference[i].Position, additiveReference[i].Rotation))))
                    return AnimationError.InvalidFrame;
            for (var i = 0; i < contactWeights.Length; i++)
                if (!Scalar.IsFinite(contactWeights[i]) || contactWeights[i] < 0f || contactWeights[i] > 1f)
                    return AnimationError.InvalidFrame;
            for (var i = 0; i < markers.Length; i++)
                if (markers[i].Bone < 0 || markers[i].Bone >= bones || !markers[i].Offset.IsFinite)
                    return AnimationError.InvalidFrame;
            var tracks = Describe(samples, familyReference, sampleCount);
            var trackBytes = new byte[tracks.Length * 32];
            var defaults = 0;
            var constants = 0;
            for (var i = 0; i < tracks.Length; i++)
            {
                var data = trackBytes.AsSpan(i * 32, 32);
                BinaryPrimitives.WriteUInt16LittleEndian(data, (ushort)tracks[i].Bone);
                data[2] = (byte)tracks[i].Channel;
                data[3] = (byte)tracks[i].Storage;
                ContentWire.Rotation(data.Slice(4), tracks[i].Constant);
                if (tracks[i].Storage == TrackStorage.Default) defaults++;
                if (tracks[i].Storage == TrackStorage.Constant) constants++;
            }
            var index = new byte[segmentCount * 32];
            using var allData = new MemoryStream();
            var rawPose = new Pose[bones];
            var decodedPose = new Pose[bones];
            var blocks = new TrackBlock[tracks.Length];
            var fallbacks = 0;
            var worst = default(PoseError);
            for (var segment = 0; segment < segmentCount; segment++)
            {
                var first = segment * 16;
                var frames = Math.Min(17, sampleCount - first);
                var payload = Segment(samples, bones, first, frames, tracks, packed);
                var error = Validate(payload, packed, tracks, parents, familyReference, samples, first, frames, markers, limits, models, rawPose, decodedPose, blocks, out var measured);
                if (error != AnimationError.None && packed)
                {
                    fallbacks++;
                    payload = Segment(samples, bones, first, frames, tracks, false);
                    error = Validate(payload, false, tracks, parents, familyReference, samples, first, frames, markers, limits, models, rawPose, decodedPose, blocks, out measured);
                }
                if (error != AnimationError.None)
                    return error;
                worst = worst.Include(measured);
                var entry = index.AsSpan(segment * 32, 32);
                BinaryPrimitives.WriteUInt32LittleEndian(entry, (uint)first);
                BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(4), (uint)frames);
                BinaryPrimitives.WriteUInt64LittleEndian(entry.Slice(8), (ulong)allData.Length);
                BinaryPrimitives.WriteUInt64LittleEndian(entry.Slice(16), (ulong)payload.Length);
                BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(24), AnimationCrc32.Of(payload));
                allData.Write(payload, 0, payload.Length);
            }
            var meta = new byte[64];
            BinaryPrimitives.WriteUInt32LittleEndian(meta, (uint)bones);
            BinaryPrimitives.WriteUInt32LittleEndian(meta.AsSpan(4), (uint)sampleCount);
            ContentWire.Double(meta.AsSpan(8), duration);
            BinaryPrimitives.WriteUInt32LittleEndian(meta.AsSpan(16), (loop ? 1u : 0u) | (additiveReference.Length > 0 ? 2u : 0u));
            BinaryPrimitives.WriteUInt32LittleEndian(meta.AsSpan(20), packed ? 1u : 0u);
            BinaryPrimitives.WriteUInt32LittleEndian(meta.AsSpan(24), 16);
            BinaryPrimitives.WriteUInt32LittleEndian(meta.AsSpan(28), (uint)segmentCount);
            BinaryPrimitives.WriteUInt32LittleEndian(meta.AsSpan(32), (uint)rootPolicy);
            ContentWire.Single(meta.AsSpan(36), 1f);
            var rootBytes = new byte[sampleCount * 28];
            for (var i = 0; i < sampleCount; i++)
            {
                ContentWire.Vector(rootBytes.AsSpan(i * 28), root[i].Position);
                ContentWire.Rotation(rootBytes.AsSpan(i * 28 + 12), Quaternion.Canonical(root[i].Rotation));
            }
            var chunks = new List<ChunkPayload>
            {
                new ChunkPayload(SacaContainer.Meta, meta, 1, 64),
                new ChunkPayload(SacaContainer.Tracks, trackBytes, (uint)tracks.Length, 32),
                new ChunkPayload(SacaContainer.Segments, index, (uint)segmentCount, 32),
                new ChunkPayload(SacaContainer.Data, allData.ToArray()),
                new ChunkPayload(SacaContainer.Root, rootBytes, (uint)sampleCount, 28)
            };
            if (additiveReference.Length != 0)
            {
                var referenceBytes = new byte[bones * 28];
                for (var i = 0; i < bones; i++)
                {
                    ContentWire.Vector(referenceBytes.AsSpan(i * 28), additiveReference[i].Position);
                    ContentWire.Rotation(referenceBytes.AsSpan(i * 28 + 12), Quaternion.Canonical(additiveReference[i].Rotation));
                }
                chunks.Add(new ChunkPayload(SacaContainer.Reference, referenceBytes, (uint)bones, 28));
            }
            if (contactWeights.Length != 0)
            {
                var contactBytes = new byte[contactWeights.Length * 4];
                for (var i = 0; i < contactWeights.Length; i++) ContentWire.Single(contactBytes.AsSpan(i * 4), contactWeights[i]);
                chunks.Add(new ChunkPayload(SacaContainer.Contacts, contactBytes, (uint)sampleCount, 16, false));
            }
            var result = SacaFile.Create(SacaKind.Clip, family, chunks.ToArray(), out bytes);
            if (result == AnimationError.None)
                report = new CompressionReport(bytes.Length, segmentCount, fallbacks, defaults, constants, worst);
            return result;
        }

        static TrackDescription[] Describe(ReadOnlySpan<Pose> samples, ReadOnlySpan<Pose> reference, int frames)
        {
            var tracks = new TrackDescription[reference.Length * 2];
            for (var i = 0; i < tracks.Length; i++)
            {
                var bone = i / 2;
                var channel = (TrackChannel)(i % 2);
                var first = Value(samples[bone], channel);
                var storage = TrackStorage.Constant;
                for (var frame = 1; frame < frames; frame++)
                    if (!Same(first, Value(samples[frame * reference.Length + bone], channel))) { storage = TrackStorage.Dynamic; break; }
                if (storage == TrackStorage.Constant && Same(first, Value(reference[bone], channel))) storage = TrackStorage.Default;
                tracks[i] = new TrackDescription(bone, channel, storage, storage == TrackStorage.Constant ? first : default);
            }
            return tracks;
        }

        static byte[] Segment(ReadOnlySpan<Pose> samples, int bones, int first, int frames, ReadOnlySpan<TrackDescription> tracks, bool packed)
        {
            using var output = new MemoryStream();
            for (var i = 0; i < tracks.Length; i++)
            {
                var track = tracks[i];
                if (track.Storage != TrackStorage.Dynamic) continue;
                var position = track.Channel == TrackChannel.Position;
                var encoding = packed ? (position ? TrackEncoding.Position16 : TrackEncoding.Quaternion8) : (position ? TrackEncoding.Position32 : TrackEncoding.Quaternion32);
                var stride = packed ? (position ? 6 : 8) : (position ? 12 : 16);
                var header = packed && position ? 32 : 8;
                var block = new byte[header + frames * stride];
                block[0] = (byte)encoding;
                BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(4), (uint)block.Length);
                var min = samples[first * bones + track.Bone].Position;
                var max = min;
                if (packed && position)
                {
                    for (var frame = 1; frame < frames; frame++)
                    {
                        var value = samples[(first + frame) * bones + track.Bone].Position;
                        min = new Float3(Math.Min(min.X, value.X), Math.Min(min.Y, value.Y), Math.Min(min.Z, value.Z));
                        max = new Float3(Math.Max(max.X, value.X), Math.Max(max.Y, value.Y), Math.Max(max.Z, value.Z));
                    }
                    ContentWire.Vector(block.AsSpan(8), min);
                    ContentWire.Vector(block.AsSpan(20), max);
                }
                for (var frame = 0; frame < frames; frame++)
                {
                    var pose = samples[(first + frame) * bones + track.Bone];
                    var data = block.AsSpan(header + frame * stride, stride);
                    if (packed && position) PackedPosition.Encode(pose.Position, min, max, data);
                    else if (packed) PackedRotation.Encode(pose.Rotation, data);
                    else if (position) ContentWire.Vector(data, pose.Position);
                    else ContentWire.Rotation(data, Quaternion.Canonical(pose.Rotation));
                }
                output.Write(block, 0, block.Length);
            }
            return output.ToArray();
        }

        static AnimationError Validate(ReadOnlySpan<byte> bytes, bool packed, ReadOnlySpan<TrackDescription> tracks, ReadOnlySpan<int> parents,
            ReadOnlySpan<Pose> familyReference, ReadOnlySpan<Pose> raw, int first, int frames, ReadOnlySpan<PoseMarker> markers,
            in CompressionLimits limits, Span<RigidPose> modelScratch, Span<Pose> reference, Span<Pose> decoded, Span<TrackBlock> blocks, out PoseError worst)
        {
            worst = default;
            var error = SegmentDecoder.Bind(tracks, frames, bytes, packed, blocks);
            if (error != AnimationError.None) return error;
            var steps = frames == 1 ? 1 : (frames - 1) * 4 + 1;
            for (var step = 0; step < steps; step++)
            {
                var lower = Math.Min(step / 4, frames - 1);
                var upper = Math.Min(lower + 1, frames - 1);
                var alpha = (step % 4) * .25f;
                for (var bone = 0; bone < parents.Length; bone++)
                    reference[bone] = PoseMath.Lerp(raw[(first + lower) * parents.Length + bone], raw[(first + upper) * parents.Length + bone], alpha);
                error = SegmentDecoder.Decode(tracks, blocks, bytes, frames, lower, upper, alpha, familyReference, decoded);
                if (error != AnimationError.None) return error;
                error = ObjectSpaceError.Measure(parents, reference, decoded, markers, modelScratch, out var measured);
                if (error != AnimationError.None) return error;
                worst = worst.Include(measured);
            }
            return worst.AngleRadians <= limits.AngleRadians && worst.PositionMetres <= limits.PositionMetres ? AnimationError.None : AnimationError.CorruptData;
        }

        static Quaternion Value(in Pose pose, TrackChannel channel) => channel == TrackChannel.Position
            ? new Quaternion(pose.Position.X, pose.Position.Y, pose.Position.Z, 0f) : Quaternion.Canonical(pose.Rotation);
        static bool Same(Quaternion a, Quaternion b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z && a.W == b.W;
    }
}
