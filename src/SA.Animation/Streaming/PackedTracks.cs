using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.Animation
{
    public static class PackedRotation
    {
        const double Range = 0.7071067811865475244;

        public static bool Encode(Quaternion rotation, Span<byte> bytes)
        {
            if (bytes.Length < 8 || !rotation.IsValidRotation)
                return false;
            var q = Quaternion.Normalize(rotation);
            var largest = 0;
            for (var i = 1; i < 4; i++)
                if (Math.Abs(Component(q, i)) > Math.Abs(Component(q, largest)))
                    largest = i;
            if (Component(q, largest) < 0)
                q = -q;
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)largest);
            var offset = 2;
            for (var i = 0; i < 4; i++)
                if (i != largest)
                {
                    var scaled = Math.Round(Component(q, i) / Range * 32767.0, MidpointRounding.AwayFromZero);
                    BinaryPrimitives.WriteInt16LittleEndian(bytes.Slice(offset), (short)Math.Max(-32767, Math.Min(32767, scaled)));
                    offset += 2;
                }
            return true;
        }

        public static bool Decode(ReadOnlySpan<byte> bytes, out Quaternion rotation)
        {
            rotation = Quaternion.Identity;
            if (bytes.Length < 8)
                return false;
            var largest = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
            if (largest > 3)
                return false;
            var q = default(Quaternion);
            var square = 0.0;
            var offset = 2;
            for (var i = 0; i < 4; i++)
                if (i != largest)
                {
                    var encoded = BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(offset));
                    if (encoded == short.MinValue)
                        return false;
                    var value = encoded * Range / 32767.0;
                    Set(ref q, i, (float)value);
                    square += value * value;
                    offset += 2;
                }
            Set(ref q, largest, (float)Math.Sqrt(Math.Max(0, 1 - square)));
            rotation = Quaternion.Normalize(q);
            return true;
        }

        static float Component(Quaternion q, int i) => i == 0 ? q.X : i == 1 ? q.Y : i == 2 ? q.Z : q.W;
        static void Set(ref Quaternion q, int i, float value)
        {
            if (i == 0) q.X = value;
            else if (i == 1) q.Y = value;
            else if (i == 2) q.Z = value;
            else q.W = value;
        }
    }

    public static class PackedPosition
    {
        public static bool ValidRange(Float3 min, Float3 max) => min.IsFinite && max.IsFinite && min.X <= max.X && min.Y <= max.Y && min.Z <= max.Z;

        public static bool Encode(Float3 value, Float3 min, Float3 max, Span<byte> bytes)
        {
            if (bytes.Length < 6 || !value.IsFinite || !ValidRange(min, max)
                || value.X < min.X || value.X > max.X || value.Y < min.Y || value.Y > max.Y || value.Z < min.Z || value.Z > max.Z)
                return false;
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, Quantize(value.X, min.X, max.X));
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.Slice(2), Quantize(value.Y, min.Y, max.Y));
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.Slice(4), Quantize(value.Z, min.Z, max.Z));
            return true;
        }

        public static bool Decode(ReadOnlySpan<byte> bytes, Float3 min, Float3 max, out Float3 value)
        {
            value = Float3.Zero;
            if (bytes.Length < 6 || !ValidRange(min, max))
                return false;
            value = new Float3(Expand(BinaryPrimitives.ReadUInt16LittleEndian(bytes), min.X, max.X),
                Expand(BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(2)), min.Y, max.Y),
                Expand(BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(4)), min.Z, max.Z));
            return true;
        }

        static ushort Quantize(float value, float min, float max) => min == max ? (ushort)0 : (ushort)Math.Round(((double)value - min) / ((double)max - min) * 65535.0, MidpointRounding.AwayFromZero);
        static float Expand(ushort value, float min, float max) => (float)(min + ((double)max - min) * (value / 65535.0));
    }

    public enum TrackChannel : byte { Position, Rotation }
    public enum TrackStorage : byte { Default, Constant, Dynamic }
    public enum TrackEncoding : byte { None, Position16, Quaternion8, Position32, Quaternion32 }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct TrackDescription
    {
        public readonly int Bone;
        public readonly TrackChannel Channel;
        public readonly TrackStorage Storage;
        public readonly Quaternion Constant;
        public TrackDescription(int bone, TrackChannel channel, TrackStorage storage, Quaternion constant)
        { Bone = bone; Channel = channel; Storage = storage; Constant = constant; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct TrackBlock
    {
        public readonly int Offset;
        public readonly TrackEncoding Encoding;
        public readonly Float3 Min;
        public readonly Float3 Max;
        public TrackBlock(int offset, TrackEncoding encoding, Float3 min, Float3 max)
        { Offset = offset; Encoding = encoding; Min = min; Max = max; }
    }

    public static class SegmentDecoder
    {
        public static AnimationError Bind(ReadOnlySpan<TrackDescription> tracks, int samples, ReadOnlySpan<byte> bytes, bool packed, Span<TrackBlock> blocks)
        {
            if (samples < 1 || samples > 17 || tracks.Length == 0 || tracks.Length > 2 * SampleLocator.MaxBones || tracks.Length % 2 != 0 || blocks.Length < tracks.Length)
                return AnimationError.InvalidFormat;
            var cursor = 0;
            for (var i = 0; i < tracks.Length; i++)
            {
                if (!ValidDescription(tracks[i], i))
                    return AnimationError.InvalidFormat;
                if (tracks[i].Storage != TrackStorage.Dynamic)
                {
                    blocks[i] = default;
                    continue;
                }
                if (!Wire.Fits(bytes.Length, cursor, 8) || bytes[cursor + 1] != 0 || bytes[cursor + 2] != 0 || bytes[cursor + 3] != 0)
                    return AnimationError.CorruptData;
                var encoding = (TrackEncoding)bytes[cursor];
                var stride = Stride(encoding);
                var rangeBytes = encoding == TrackEncoding.Position16 ? 24 : 0;
                var length = 8 + rangeBytes + stride * samples;
                if (stride == 0 || BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(cursor + 4)) != (uint)length || !Wire.Fits(bytes.Length, cursor, length)
                    || !Allowed(tracks[i].Channel, encoding) || (!packed && encoding != TrackEncoding.Position32 && encoding != TrackEncoding.Quaternion32))
                    return AnimationError.CorruptData;
                var min = Float3.Zero;
                var max = Float3.Zero;
                if (rangeBytes != 0)
                {
                    min = Wire.Vector(bytes.Slice(cursor + 8));
                    max = Wire.Vector(bytes.Slice(cursor + 20));
                    if (!PackedPosition.ValidRange(min, max))
                        return AnimationError.CorruptData;
                }
                var block = new TrackBlock(cursor + 8 + rangeBytes, encoding, min, max);
                for (var sample = 0; sample < samples; sample++)
                    if (!Read(bytes, block, sample, out _))
                        return AnimationError.CorruptData;
                blocks[i] = block;
                cursor += length;
            }
            return cursor == bytes.Length ? AnimationError.None : AnimationError.CorruptData;
        }

        public static AnimationError Decode(ReadOnlySpan<TrackDescription> tracks, ReadOnlySpan<TrackBlock> blocks, ReadOnlySpan<byte> segment,
            int samples, int first, int second, float alpha, ReadOnlySpan<Pose> familyReference, Span<Pose> output)
        {
            if (familyReference.Length == 0 || familyReference.Length > SampleLocator.MaxBones || tracks.Length != 2 * familyReference.Length || blocks.Length < tracks.Length
                || output.Length < familyReference.Length || samples < 1 || samples > 17 || first < 0 || first >= samples || second < 0 || second >= samples
                || !Scalar.IsFinite(alpha) || alpha < 0f || alpha > 1f)
                return AnimationError.InvalidFrame;
            if (familyReference.Overlaps(output))
                return AnimationError.AliasedBuffers;
            for (var bone = 0; bone < familyReference.Length; bone++)
            {
                if (!RigidMath.UnitPose(familyReference[bone]) || !Value(tracks, blocks, segment, bone * 2, first, familyReference[bone], out var p0)
                    || !Value(tracks, blocks, segment, bone * 2, second, familyReference[bone], out var p1)
                    || !Value(tracks, blocks, segment, bone * 2 + 1, first, familyReference[bone], out var q0)
                    || !Value(tracks, blocks, segment, bone * 2 + 1, second, familyReference[bone], out var q1))
                    return AnimationError.CorruptData;
                var position = p0.Vector + (p1.Vector - p0.Vector) * alpha;
                if (!position.IsFinite)
                    return AnimationError.CorruptData;
                output[bone] = new Pose { Position = position, Rotation = PoseMath.Nlerp(q0, q1, alpha), Scale = Float3.One };
            }
            return AnimationError.None;
        }

        public static bool ValidDescription(in TrackDescription track, int index)
        {
            if (track.Bone != index / 2 || (int)track.Channel != index % 2 || track.Storage > TrackStorage.Dynamic)
                return false;
            if (track.Storage != TrackStorage.Constant)
                return track.Constant.X == 0f && track.Constant.Y == 0f && track.Constant.Z == 0f && track.Constant.W == 0f;
            return track.Channel == TrackChannel.Position ? track.Constant.Vector.IsFinite && track.Constant.W == 0f : RigidMath.UnitRotation(track.Constant);
        }

        static bool Value(ReadOnlySpan<TrackDescription> tracks, ReadOnlySpan<TrackBlock> blocks, ReadOnlySpan<byte> bytes, int trackIndex, int sample, in Pose reference, out Quaternion value)
        {
            value = default;
            ref readonly var track = ref tracks[trackIndex];
            if (!ValidDescription(track, trackIndex))
                return false;
            if (track.Storage == TrackStorage.Default)
            {
                value = track.Channel == TrackChannel.Position ? new Quaternion(reference.Position.X, reference.Position.Y, reference.Position.Z, 0f) : reference.Rotation;
                return true;
            }
            if (track.Storage == TrackStorage.Constant)
            {
                value = track.Constant;
                return true;
            }
            return Allowed(track.Channel, blocks[trackIndex].Encoding) && Read(bytes, blocks[trackIndex], sample, out value);
        }

        static bool Read(ReadOnlySpan<byte> bytes, in TrackBlock block, int sample, out Quaternion value)
        {
            value = default;
            var stride = Stride(block.Encoding);
            var offset = (long)block.Offset + (long)sample * stride;
            if (stride == 0 || sample < 0 || offset < 0 || offset > int.MaxValue || !Wire.Fits(bytes.Length, (int)offset, stride))
                return false;
            var data = bytes.Slice((int)offset, stride);
            if (block.Encoding == TrackEncoding.Quaternion8)
                return PackedRotation.Decode(data, out value);
            if (block.Encoding == TrackEncoding.Quaternion32)
            {
                value = Wire.Rotation(data);
                return RigidMath.UnitRotation(value);
            }
            Float3 position;
            if (block.Encoding == TrackEncoding.Position16)
            {
                if (!PackedPosition.Decode(data, block.Min, block.Max, out position))
                    return false;
            }
            else
                position = Wire.Vector(data);
            value = new Quaternion(position.X, position.Y, position.Z, 0f);
            return position.IsFinite;
        }

        static int Stride(TrackEncoding encoding) => encoding == TrackEncoding.Position16 ? 6 : encoding == TrackEncoding.Quaternion8 ? 8 : encoding == TrackEncoding.Position32 ? 12 : encoding == TrackEncoding.Quaternion32 ? 16 : 0;
        static bool Allowed(TrackChannel channel, TrackEncoding encoding) => channel == TrackChannel.Position
            ? encoding == TrackEncoding.Position16 || encoding == TrackEncoding.Position32
            : encoding == TrackEncoding.Quaternion8 || encoding == TrackEncoding.Quaternion32;
    }

    internal static class Wire
    {
        internal static bool Fits(int total, int offset, int length) => offset >= 0 && length >= 0 && offset <= total && length <= total - offset;
        internal static float Single(ReadOnlySpan<byte> bytes) => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes));
        internal static double Double(ReadOnlySpan<byte> bytes) => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(bytes));
        internal static Float3 Vector(ReadOnlySpan<byte> bytes) => new Float3(Single(bytes), Single(bytes.Slice(4)), Single(bytes.Slice(8)));
        internal static Quaternion Rotation(ReadOnlySpan<byte> bytes) => new Quaternion(Single(bytes), Single(bytes.Slice(4)), Single(bytes.Slice(8)), Single(bytes.Slice(12)));
        internal static AssetId Id(ReadOnlySpan<byte> bytes) => new AssetId(BinaryPrimitives.ReadUInt64LittleEndian(bytes), BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(8)), BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(16)), BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(24)));
    }
}
