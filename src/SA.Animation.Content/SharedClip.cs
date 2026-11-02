using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.Animation.Content
{
    public enum RootPolicy : uint { None, Planar, FullRigid }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct LoadedSegment
    {
        public readonly int FirstSample;
        public readonly int SampleCount;
        public readonly byte[] Bytes;
        public readonly TrackBlock[] Blocks;
        public LoadedSegment(int firstSample, int sampleCount, byte[] bytes, TrackBlock[] blocks)
        { FirstSample = firstSample; SampleCount = sampleCount; Bytes = bytes; Blocks = blocks; }
    }

    public sealed class SharedClip
    {
        public AssetId Family { get; }
        public AssetId Artifact { get; }
        public double Duration { get; }
        public int SampleCount { get; }
        public int BoneCount => Tracks.Length / 2;
        public bool Loop { get; }
        public RootPolicy RootPolicy { get; }
        public TrackDescription[] Tracks { get; }
        public LoadedSegment[] Segments { get; }
        public RigidPose[] Root { get; }
        public Pose[] AdditiveReference { get; }
        public float[] ContactWeights { get; }

        internal SharedClip(AssetId family, AssetId artifact, double duration, int samples, bool loop, RootPolicy rootPolicy,
            TrackDescription[] tracks, LoadedSegment[] segments, RigidPose[] root, Pose[] additiveReference, float[] contactWeights)
        { Family = family; Artifact = artifact; Duration = duration; SampleCount = samples; Loop = loop; RootPolicy = rootPolicy;
            Tracks = tracks; Segments = segments; Root = root; AdditiveReference = additiveReference; ContactWeights = contactWeights; }

        public AnimationError Sample(double seconds, ReadOnlySpan<Pose> familyReference, Span<Pose> output)
        {
            var error = SampleLocator.Locate(Duration, SampleCount, seconds, Loop, out var frame);
            if (error != AnimationError.None)
                return error;
            if (familyReference.Length != BoneCount || frame.Segment >= Segments.Length)
                return AnimationError.InvalidFrame;
            var segment = Segments[frame.Segment];
            return SegmentDecoder.Decode(Tracks, segment.Blocks, segment.Bytes, segment.SampleCount,
                frame.First - segment.FirstSample, frame.Second - segment.FirstSample, frame.Alpha, familyReference, output);
        }
    }

    public static class SharedClipReader
    {
        public static AnimationError Load(ReadOnlySpan<byte> bytes, AssetId expectedFamily, ReadOnlySpan<Pose> familyReference, out SharedClip? clip)
        {
            clip = null;
            Span<SacaChunk> directory = stackalloc SacaChunk[SacaContainer.MaxChunks];
            var error = SacaFile.Verify(bytes, directory, out var count, out var header);
            if (error != AnimationError.None)
                return error;
            if (header.Family != expectedFamily)
                return AnimationError.FamilyMismatch;
            var chunks = directory.Slice(0, count);
            if (header.Kind != SacaKind.Clip || !Required(chunks, SacaContainer.Meta, 1, 64, out var metaChunk))
                return AnimationError.InvalidFormat;
            var meta = bytes.Slice(metaChunk.Offset, metaChunk.Length);
            var bones = BinaryPrimitives.ReadUInt32LittleEndian(meta);
            var samples = BinaryPrimitives.ReadUInt32LittleEndian(meta.Slice(4));
            var duration = ContentWire.Double(meta.Slice(8));
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(meta.Slice(16));
            var codec = BinaryPrimitives.ReadUInt32LittleEndian(meta.Slice(20));
            var intervals = BinaryPrimitives.ReadUInt32LittleEndian(meta.Slice(24));
            var segmentCount = BinaryPrimitives.ReadUInt32LittleEndian(meta.Slice(28));
            var policy = (RootPolicy)BinaryPrimitives.ReadUInt32LittleEndian(meta.Slice(32));
            var referenceTime = ContentWire.Double(meta.Slice(40));
            if (bones == 0 || bones > SampleLocator.MaxBones || bones != familyReference.Length || samples == 0 || samples > SampleLocator.MaxSamples
                || SampleLocator.Locate(duration, (int)samples, 0, false, out _) != AnimationError.None || flags > 3 || codec > 1 || intervals != 16
                || segmentCount != SampleLocator.SegmentCount((int)samples) || policy > RootPolicy.FullRigid || ContentWire.Single(meta.Slice(36)) != 1f
                || !ContentWire.Zero(meta.Slice(48)) || double.IsNaN(referenceTime) || double.IsInfinity(referenceTime) || referenceTime < 0 || referenceTime > 3600
                || ((flags & 2) == 0 && referenceTime != 0) || !RootIdentity(new RigidPose(familyReference[0].Position, familyReference[0].Rotation)))
                return AnimationError.InvalidFormat;
            for (var i = 0; i < familyReference.Length; i++)
                if (!RigidMath.UnitPose(familyReference[i]))
                    return AnimationError.InvalidFormat;
            if (!Required(chunks, SacaContainer.Tracks, 2 * bones, 32, out var trackChunk)
                || !Required(chunks, SacaContainer.Segments, segmentCount, 32, out var segmentChunk)
                || !Required(chunks, SacaContainer.Data, 0, 0, out var dataChunk)
                || !Required(chunks, SacaContainer.Root, samples, 28, out var rootChunk))
                return AnimationError.InvalidFormat;
            var tracks = new TrackDescription[2 * bones];
            for (var i = 0; i < tracks.Length; i++)
            {
                var record = bytes.Slice(trackChunk.Offset + i * 32, 32);
                var track = new TrackDescription(BinaryPrimitives.ReadUInt16LittleEndian(record), (TrackChannel)record[2], (TrackStorage)record[3], ContentWire.Rotation(record.Slice(4)));
                if (!SegmentDecoder.ValidDescription(track, i) || !ContentWire.Zero(record.Slice(20)) || (i < 2 && track.Storage != TrackStorage.Default))
                    return AnimationError.InvalidFormat;
                tracks[i] = track;
            }
            var root = new RigidPose[samples];
            for (var i = 0; i < root.Length; i++)
            {
                var record = bytes.Slice(rootChunk.Offset + i * 28, 28);
                root[i] = new RigidPose(ContentWire.Vector(record), ContentWire.Rotation(record.Slice(12)));
                if (!ValidRoot(root[i], policy) || (i == 0 && !RootIdentity(root[i])))
                    return AnimationError.InvalidFormat;
            }
            var reference = Array.Empty<Pose>();
            var hasReference = SacaContainer.Find(chunks, SacaContainer.Reference, out var referenceChunk);
            if (hasReference != ((flags & 2) != 0))
                return AnimationError.InvalidFormat;
            if (hasReference)
            {
                if (!Required(chunks, SacaContainer.Reference, bones, 28, out referenceChunk))
                    return AnimationError.InvalidFormat;
                reference = new Pose[bones];
                for (var i = 0; i < reference.Length; i++)
                {
                    var record = bytes.Slice(referenceChunk.Offset + i * 28, 28);
                    reference[i] = new Pose { Position = ContentWire.Vector(record), Rotation = ContentWire.Rotation(record.Slice(12)), Scale = Float3.One };
                    if (!RigidMath.UnitPose(reference[i]) || (i == 0 && !RootIdentity(new RigidPose(reference[i].Position, reference[i].Rotation))))
                        return AnimationError.InvalidFormat;
                }
            }
            var contacts = Array.Empty<float>();
            if (SacaContainer.Find(chunks, SacaContainer.Contacts, out var contactChunk))
            {
                if (contactChunk.Count != samples || contactChunk.Stride != 16)
                    return AnimationError.InvalidFormat;
                contacts = new float[samples * 4];
                for (var i = 0; i < contacts.Length; i++)
                {
                    contacts[i] = ContentWire.Single(bytes.Slice(contactChunk.Offset + i * 4));
                    if (!Scalar.IsFinite(contacts[i]) || contacts[i] < 0f || contacts[i] > 1f)
                        return AnimationError.InvalidFormat;
                }
            }
            var segments = new LoadedSegment[segmentCount];
            var cursor = 0UL;
            for (var i = 0; i < segments.Length; i++)
            {
                var record = bytes.Slice(segmentChunk.Offset + i * 32, 32);
                var first = BinaryPrimitives.ReadUInt32LittleEndian(record);
                var frames = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(4));
                var offset = BinaryPrimitives.ReadUInt64LittleEndian(record.Slice(8));
                var length = BinaryPrimitives.ReadUInt64LittleEndian(record.Slice(16));
                var crc = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(24));
                if (first != i * 16 || frames != Math.Min(17, (int)samples - i * 16) || offset != cursor || offset > (ulong)dataChunk.Length
                    || length > (ulong)dataChunk.Length - offset || BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(28)) != 0)
                    return AnimationError.InvalidFormat;
                var segmentBytes = bytes.Slice(dataChunk.Offset + (int)offset, (int)length);
                if (AnimationCrc32.Of(segmentBytes) != crc)
                    return AnimationError.CorruptData;
                var blocks = new TrackBlock[tracks.Length];
                error = SegmentDecoder.Bind(tracks, (int)frames, segmentBytes, codec == 1, blocks);
                if (error != AnimationError.None)
                    return error;
                segments[i] = new LoadedSegment((int)first, (int)frames, segmentBytes.ToArray(), blocks);
                cursor += length;
            }
            if (cursor != (ulong)dataChunk.Length)
                return AnimationError.InvalidFormat;
            clip = new SharedClip(expectedFamily, header.Digest, duration, (int)samples, (flags & 1) != 0, policy, tracks, segments, root, reference, contacts);
            return AnimationError.None;
        }

        internal static bool RootIdentity(in RigidPose pose) => pose.Position.IsFinite && pose.Position.LengthSquared <= 1e-12f
            && RigidMath.UnitRotation(pose.Rotation) && Math.Abs(pose.Rotation.W) >= .999999f;

        internal static bool ValidRoot(in RigidPose pose, RootPolicy policy) => pose.Position.IsFinite && RigidMath.UnitRotation(pose.Rotation)
            && (policy != RootPolicy.None || RootIdentity(pose))
            && (policy != RootPolicy.Planar || Math.Abs(pose.Position.Y) <= 1e-6f && Math.Abs(pose.Rotation.X) <= 1e-6f && Math.Abs(pose.Rotation.Z) <= 1e-6f);

        internal static bool Required(ReadOnlySpan<SacaChunk> chunks, uint tag, uint count, uint stride, out SacaChunk chunk) =>
            SacaContainer.Find(chunks, tag, out chunk) && chunk.Flags == 1 && chunk.Count == count && chunk.Stride == stride;
    }
}
